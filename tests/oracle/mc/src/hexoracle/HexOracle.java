package hexoracle;

import at.petrak.hexcasting.api.HexAPI;
import at.petrak.hexcasting.api.casting.ActionRegistryEntry;
import at.petrak.hexcasting.api.casting.castables.SpellAction;
import at.petrak.hexcasting.api.casting.eval.ExecutionClientView;
import at.petrak.hexcasting.api.casting.eval.vm.CastingImage;
import at.petrak.hexcasting.api.casting.eval.vm.CastingVM;
import at.petrak.hexcasting.api.casting.iota.Iota;
import at.petrak.hexcasting.api.casting.iota.IotaType;
import at.petrak.hexcasting.api.casting.math.HexPattern;
import at.petrak.hexcasting.api.mod.HexTags;
import at.petrak.hexcasting.api.utils.HexUtils;
import at.petrak.hexcasting.common.casting.PatternRegistryManifest;
import at.petrak.hexcasting.xplat.IXplatAbstractions;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import java.io.PrintWriter;
import java.io.StringWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.entity.FakePlayer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.minecraft.class_1268;
import net.minecraft.class_2378;
import net.minecraft.class_2487;
import net.minecraft.class_3218;
import net.minecraft.class_5321;
import net.minecraft.server.MinecraftServer;

/**
 * 原版咒法学的「标准答案机」：没有界面的 MC 服务器起来以后，按启动参数做一件事，写出结果，然后关服。
 *   -Dhexoracle.mode=dump  导出原版运行时的图案注册表（每个图案的 id、起始方向、笔顺、是不是本世界图案、实现类、是不是法术）
 *   -Dhexoracle.mode=run   读 -Dhexoracle.in 的用例，逐个用原版自己的施法虚拟机执行，结果写到 -Dhexoracle.out
 *
 * 每个用例：假玩家回满血、放回原位，用例给的初始栈；程序里的每个 iota 当成一次法杖施法单独执行 ——
 * 和玩家用法杖一笔一笔画一样（原版 StaffCastEnv.handleNewPatternOnServer：每笔新建一个环境，镜像接着用，栈清空就丢掉镜像）。
 * 每一步记下：结果类型、栈、事故、消息、媒质、操作数、括号层数、转义状态、渡鸦之思。
 *
 * class_xxxx 是 Fabric 中间名：class_3218 = ServerLevel，class_2487 = CompoundTag，class_2378 = Registry，class_5321 = ResourceKey。
 */
public final class HexOracle implements ModInitializer {
    private static final Gson GSON = new GsonBuilder().disableHtmlEscaping().serializeNulls().create();

    @Override
    public void onInitialize() {
        String mode = System.getProperty("hexoracle.mode");
        if (mode == null) return;
        ServerLifecycleEvents.SERVER_STARTED.register(server -> {
            JsonObject out = new JsonObject();
            out.addProperty("mode", mode);
            try {
                class_3218 world = server.method_30002();
                if (mode.equals("dump")) out.add("actions", dump(world));
                else if (mode.equals("run")) out.add("results", run(world, Path.of(System.getProperty("hexoracle.in"))));
                else throw new IllegalArgumentException("不认识的模式：" + mode);
                out.addProperty("ok", true);
            } catch (Throwable t) {
                out.addProperty("ok", false);
                out.addProperty("error", stackTrace(t));
            }
            try {
                Path target = Path.of(System.getProperty("hexoracle.out"));
                Path tmp = target.resolveSibling(target.getFileName() + ".tmp");
                Files.writeString(tmp, GSON.toJson(out), StandardCharsets.UTF_8);
                Files.move(tmp, target, StandardCopyOption.REPLACE_EXISTING);
            } catch (Exception e) {
                e.printStackTrace();
            }
            server.method_3747(false);
        });
    }

    private static JsonArray dump(class_3218 world) {
        class_2378<ActionRegistryEntry> registry = IXplatAbstractions.INSTANCE.getActionRegistry();
        JsonArray a = new JsonArray();
        for (Map.Entry<class_5321<ActionRegistryEntry>, ActionRegistryEntry> e : registry.method_29722()) {
            class_5321<ActionRegistryEntry> key = e.getKey();
            boolean perWorld = HexUtils.isOfTag(registry, key, HexTags.Actions.PER_WORLD_PATTERN);
            HexPattern p = perWorld ? PatternRegistryManifest.getCanonicalStrokesPerWorld(key, world) : e.getValue().prototype();
            JsonObject o = new JsonObject();
            o.addProperty("id", key.method_29177().toString());
            o.addProperty("dir", p.getStartDir().name());
            o.addProperty("angles", p.anglesSignature());
            o.addProperty("perWorld", perWorld);
            o.addProperty("class", e.getValue().action().getClass().getName());
            o.addProperty("spell", e.getValue().action() instanceof SpellAction);
            if (perWorld) {
                o.addProperty("canonicalDir", e.getValue().prototype().getStartDir().name());
                o.addProperty("canonicalAngles", e.getValue().prototype().anglesSignature());
            }
            a.add(o);
        }
        return a;
    }

    private static JsonArray run(class_3218 world, Path in) throws Exception {
        JsonObject doc = JsonParser.parseString(Files.readString(in, StandardCharsets.UTF_8)).getAsJsonObject();
        JsonArray results = new JsonArray();
        // 整轮只用一个假玩家：Fabric 按名片缓存假玩家、从不释放，每个用例新建一个，几千个以后内存就被吃光了
        FakePlayer player = FakePlayer.get(world, new GameProfile(UUID.nameUUIDFromBytes("hexoracle".getBytes(StandardCharsets.UTF_8)), "oracle"));
        for (JsonElement c : doc.getAsJsonArray("cases")) {
            // 进度写进服务器日志：万一原版自己在哪个用例上崩了（比如内存溢出），能从日志最后一行看出是哪个
            System.out.println("[hexoracle] case " + c.getAsJsonObject().get("id").getAsString());
            results.add(runCase(world, player, c.getAsJsonObject()));
        }
        return results;
    }

    private static JsonObject runCase(class_3218 world, FakePlayer player, JsonObject c) {
        String id = c.get("id").getAsString();
        JsonObject r = new JsonObject();
        r.addProperty("id", id);
        JsonArray steps = new JsonArray();
        r.add("steps", steps);
        try {
            player.method_5814(0.5, -60.0, 0.5);
            player.method_6033(player.method_6063());
            boolean enlightened = !c.has("enlightened") || c.get("enlightened").getAsBoolean();

            List<Iota> stack = new ArrayList<>();
            for (JsonElement x : c.getAsJsonArray("stack")) stack.add(IotaJson.parse(x, player));
            CastingImage image = new CastingImage(stack, 0, List.of(), false, 0L, new class_2487());

            for (JsonElement p : c.getAsJsonArray("program")) {
                Iota iota = IotaJson.parse(p, player);
                OracleEnv env = new OracleEnv(player, class_1268.field_5808, enlightened);
                CastingVM vm = new CastingVM(image, env);
                ExecutionClientView view = vm.queueExecuteAndWrapIota(iota, world);
                image = vm.getImage();

                JsonObject s = new JsonObject();
                s.addProperty("res", view.getResolutionType().name());
                s.add("stack", IotaJson.writeAll(image.getStack(), player));
                s.add("mishaps", env.mishaps);
                s.add("msgs", env.messages);
                s.addProperty("media", env.mediaSpent);
                s.addProperty("ops", image.getOpsConsumed());
                s.addProperty("parens", image.getParenCount());
                s.addProperty("escape", image.getEscapeNext());
                s.addProperty("clear", view.isStackClear());
                class_2487 userData = image.getUserData();
                if (userData.method_10545(HexAPI.RAVENMIND_USERDATA)) {
                    s.add("raven", IotaJson.write(IotaType.deserialize(userData.method_10562(HexAPI.RAVENMIND_USERDATA), world), player));
                }
                steps.add(s);
                if (view.isStackClear()) image = new CastingImage();
            }
        } catch (Throwable t) {
            r.addProperty("exception", stackTrace(t));
        }
        return r;
    }

    private static String stackTrace(Throwable t) {
        StringWriter w = new StringWriter();
        t.printStackTrace(new PrintWriter(w));
        return w.toString();
    }
}
