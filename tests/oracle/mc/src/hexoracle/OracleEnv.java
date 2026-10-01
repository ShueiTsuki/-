package hexoracle;

import at.petrak.hexcasting.api.casting.eval.env.StaffCastEnv;
import at.petrak.hexcasting.api.casting.eval.sideeffects.OperatorSideEffect;
import com.google.gson.JsonArray;
import net.minecraft.class_1268;
import net.minecraft.class_2561;
import net.minecraft.class_3222;

/**
 * 原版的法杖施法环境，只接管三处，其余一律照原版：
 * - 事故：记下事故类名和它给玩家的那句话（原版 sendMishapMsgToPlayer 发给玩家）；
 * - 消息：揭示之类的图案打印的内容（原版 printMessage 发给玩家）；
 * - 媒质：只记账、不真扣（原版从背包扣），所以每步花了多少媒质能精确对上；
 * - 启蒙：由用例指定（原版看玩家有没有「获得启迪」进度）。
 *
 * 名字里的 class_xxxx 是 Fabric 的中间名：class_3222 = ServerPlayer，class_1268 = InteractionHand，class_2561 = Component。
 */
public final class OracleEnv extends StaffCastEnv {
    private final boolean enlightened;
    final JsonArray mishaps = new JsonArray();
    final JsonArray messages = new JsonArray();
    long mediaSpent;

    public OracleEnv(class_3222 caster, class_1268 hand, boolean enlightened) {
        super(caster, hand);
        this.enlightened = enlightened;
    }

    @Override
    public long extractMediaEnvironment(long cost, boolean simulate) {
        if (!simulate) mediaSpent += cost;
        return 0;
    }

    @Override
    public boolean isEnlightened() {
        return enlightened;
    }

    @Override
    protected void sendMishapMsgToPlayer(OperatorSideEffect.DoMishap mishap) {
        mishaps.add(mishap.getMishap().getClass().getSimpleName());
        class_2561 msg = mishap.getMishap().errorMessageWithName(this, mishap.getErrorCtx());
        if (msg != null) messages.add("mishap: " + msg.getString());
    }

    @Override
    public void printMessage(class_2561 message) {
        messages.add(message.getString());
    }
}
