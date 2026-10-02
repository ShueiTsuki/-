package hexoracle;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import net.fabricmc.fabric.api.entity.FakePlayer;
import net.minecraft.class_1297;
import net.minecraft.class_2168;
import net.minecraft.class_3218;
import net.minecraft.server.MinecraftServer;

/**
 * 对拍用的测试场景：一切都放在 z = 0 的平面上 —— 移植版的世界就是这个平面（z 不为 0 一律算远处），
 * 移植版对拍程序里的场景世界（port/SceneWorld.cs）照这里一样布置。平坦世界：基岩 y=-64，泥土 -63、-62，草方块 -61，地面在 y=-60。
 *
 *   施法者（假玩家）：脚底 (0.5, -60, 0)，面朝 +x（yRot = -90）；假玩家不在世界的实体列表里，区域查询、射线都碰不到它
 *   猪 "pig"：脚底 (3.5, -60, 0)，面朝 +x，不动、不受伤、不消失
 *   掉落物 "item"：脚底 (-2.5, -60, 0)，3 个紫水晶粉，不受重力、捡不起来、不消失
 *   石头：方块 (2, -60, 0)
 *
 * 实体用原版指令放（不用去查实体类型的中间名），UUID 写死，之后按 UUID 找回来；坐标写 0.0 而不是 0 ——
 * summon 会把整数坐标挪到格子中心（0 变成 0.5）。昼夜、天气、生物刷新、随机刻都关掉。
 *
 * 每个用例开始前清掉场景以外的实体（{@link #reset}）：所有用例都在服务器的同一个刻里跑完，世界不走刻，
 * 原版有的事故会留下实体 —— 比如「位置太远」把施法者手上的东西扔向目标，空手也照样生成一个空的掉落物，
 * 真实游戏里下一刻就清掉了，这里却会越积越多（2026-10-02 找到）。
 */
final class Scene {
    static final UUID PIG = new UUID(0x4845584f52414331L, 0x0000000000000001L);
    static final UUID ITEM = new UUID(0x4845584f52414331L, 0x0000000000000002L);

    final FakePlayer caster;
    private final MinecraftServer server;
    /** 名字 → 实体（不含施法者；施法者的名字是 "self"）。 */
    final Map<String, class_1297> entities = new LinkedHashMap<>();

    private Scene(MinecraftServer server, FakePlayer caster) {
        this.server = server;
        this.caster = caster;
    }

    static Scene build(MinecraftServer server, class_3218 world, FakePlayer caster) {
        for (String cmd : new String[]{
            "gamerule doDaylightCycle false", "time set 6000", "weather clear 1000000",
            "gamerule doMobSpawning false", "gamerule randomTickSpeed 0", "gamerule doWeatherCycle false",
            "kill @e[type=!minecraft:player]",
            "setblock 2 -60 0 minecraft:stone",
            "summon minecraft:pig 3.5 -60.0 0.0 {NoAI:1b,Invulnerable:1b,PersistenceRequired:1b,Silent:1b,Tags:['hexoracle'],Rotation:[-90f,0f],UUID:" + snbt(PIG) + "}",
            "summon minecraft:item -2.5 -60.0 0.0 {Item:{id:'hexcasting:amethyst_dust',Count:3b},PickupDelay:32767,Age:-32768,NoGravity:1b,Tags:['hexoracle'],Rotation:[-90f,0f],UUID:" + snbt(ITEM) + "}",
        }) {
            command(server, cmd);
        }
        Scene scene = new Scene(server, caster);
        scene.entities.put("pig", require(world, PIG, "pig"));
        scene.entities.put("item", require(world, ITEM, "item"));
        scene.resetCaster();
        return scene;
    }

    /** 每个用例开始前：清掉场景以外的实体，施法者回满血、放回原位、面朝 +x。 */
    void reset() {
        command(server, "kill @e[type=!minecraft:player,tag=!hexoracle]");
        resetCaster();
    }

    private void resetCaster() {
        caster.method_5814(0.5, -60.0, 0.0);
        caster.method_36456(-90f);
        caster.method_5847(-90f);
        caster.method_36457(0f);
        caster.method_6033(caster.method_6063());
    }

    class_1297 byName(String name) {
        if ("self".equals(name)) return caster;
        class_1297 e = entities.get(name);
        if (e == null) throw new IllegalArgumentException("场景里没有这个实体：" + name);
        return e;
    }

    /** 场景里的实体给名字，别的返回 null。 */
    String nameOf(class_1297 e) {
        UUID id = e.method_5667();
        if (id.equals(caster.method_5667())) return "self";
        for (Map.Entry<String, class_1297> kv : entities.entrySet()) {
            if (kv.getValue().method_5667().equals(id)) return kv.getKey();
        }
        return null;
    }

    private static void command(MinecraftServer server, String cmd) {
        class_2168 source = server.method_3739().method_9217();
        server.method_3734().method_44252(source, cmd);
    }

    private static class_1297 require(class_3218 world, UUID id, String name) {
        class_1297 e = world.method_14190(id);
        if (e == null) throw new IllegalStateException("场景实体没放出来：" + name);
        return e;
    }

    private static String snbt(UUID id) {
        long hi = id.getMostSignificantBits(), lo = id.getLeastSignificantBits();
        return "[I;" + (int) (hi >> 32) + "," + (int) hi + "," + (int) (lo >> 32) + "," + (int) lo + "]";
    }
}
