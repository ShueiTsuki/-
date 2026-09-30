using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content;

/// <summary>
/// 为每个 NPC 维护一个持久「视线方向」。
///
/// 为什么需要：泰拉瑞亚的 NPC **没有持久朝向**（只有 <c>direction</c> ±1，仅水平），
/// 而原作大量图案依赖 `entity.getLookAngle()`。这里用
/// <see cref="LookResolver"/> 的四层策略补出一个稳定朝向，
/// 并存回 <see cref="Look"/>，让静止的 NPC 保持上次朝的方向 ——
/// 等价于 MC 的 yaw。
///
/// 成本：每帧一次长度平方比较，至多一次归一化。可忽略。
/// </summary>
public sealed class HexGlobalNPC : GlobalNPC
{
    /// <summary>
    /// **必须为 true**。tModLoader 的 GlobalNPC 默认是「全 NPC 共享一个实例」，
    /// 那种模式下带实例字段会直接抛异常并**禁用整个模组**：
    ///   `HexGlobalNPC instance fields but InstancePerEntity returns false`
    /// 我们要为每个 NPC 存一份视线缓存，所以必须声明每实体一份。
    /// </summary>
    public override bool InstancePerEntity => true;

    /// <summary>
    /// 这只 NPC 是否已经被「脑叶切除」过（失去意识）。
    ///
    /// 原版（IXplatAbstractions.isBrainswept）：生物**还活着**，但 AI 停止（MixinMob.serverAiStep）、
    /// 不出声（playAmbientSound）、不能交互（BrainsweepingEvents）；再切一次才会死（MishapAlreadyBrainswept）。
    /// 同一只只能切一次 —— 否则玩家可以对着同一只村民反复刷母岩。
    /// </summary>
    public bool Brainswept { get; set; }

    /// <summary>标记失去意识并同步（服务端 / 单人）。</summary>
    public static void MakeBrainswept(NPC npc)
    {
        npc.GetGlobalNPC<HexGlobalNPC>().Brainswept = true;
        npc.netUpdate = true;
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendData(Terraria.ID.MessageID.SyncNPC, -1, -1, null, npc.whoAmI);
        }
    }

    /// <summary>原版 MixinMob.serverAiStep：失去意识的生物不再思考 —— 停下来原地飘着（重力照旧）。</summary>
    public override bool PreAI(NPC npc)
    {
        if (!Brainswept) return true;
        npc.velocity.X *= 0.9f;
        if (npc.noGravity) npc.velocity.Y *= 0.9f;
        else npc.velocity.Y = System.Math.Min(npc.velocity.Y + 0.3f, 10f);
        return false;
    }

    /// <summary>失去意识就不会攻击（泰拉的接触伤害不走 AI，要单独关）。</summary>
    public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) => !Brainswept;

    public override bool CanHitNPC(NPC npc, NPC target) => !Brainswept;

    /// <summary>原版 BrainsweepingEvents.interactWithBrainswept：不能交互（城镇 NPC 不能对话、不能交易）。</summary>
    public override bool? CanChat(NPC npc) => Brainswept ? false : null;

    public override void SendExtraAI(NPC npc, Terraria.ModLoader.IO.BitWriter bitWriter, System.IO.BinaryWriter binaryWriter)
        => bitWriter.WriteBit(Brainswept);

    public override void ReceiveExtraAI(NPC npc, Terraria.ModLoader.IO.BitReader bitReader, System.IO.BinaryReader binaryReader)
        => Brainswept = bitReader.ReadBit();

    /// <summary>城镇 NPC 会随世界存档：失去意识的状态也要存（原版是生物的持久数据）。</summary>
    public override void SaveData(NPC npc, Terraria.ModLoader.IO.TagCompound tag)
    {
        if (Brainswept) tag["brainswept"] = true;
    }

    public override void LoadData(NPC npc, Terraria.ModLoader.IO.TagCompound tag)
        => Brainswept = tag.ContainsKey("brainswept");

    /// <summary>
    /// 上一次解析出的视线（单位向量）。
    /// 初值取正方向，与 <see cref="LookResolver"/> 的最终兜底一致。
    /// </summary>
    public Vector2 Look { get; private set; } = new(1f, 0f);

    public override void PostAI(NPC npc)
    {
        // NPC 的 target 指向玩家数组下标；-1 表示没有目标
        bool hasTarget = npc.HasValidTarget;
        Vector2 targetPos = default;
        if (hasTarget)
        {
            var target = Main.player[npc.target];
            if (target is { active: true })
            {
                targetPos = target.Center;
            }
            else
            {
                hasTarget = false;
            }
        }

        var (x, y) = LookResolver.Resolve(new LookInput
        {
            // NPC 没有鼠标瞄点，第 1 层不适用
            HasAim = false,
            SelfX = npc.Center.X,
            SelfY = npc.Center.Y,
            VelX = npc.velocity.X,
            VelY = npc.velocity.Y,
            HasTarget = hasTarget,
            TargetX = targetPos.X,
            TargetY = targetPos.Y,
            PrevX = Look.X,
            PrevY = Look.Y,
            Direction = npc.direction,
        });

        Look = new Vector2(x, y);
    }
}
