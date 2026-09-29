using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;

namespace HexCastingTerraria.Content;

/// <summary>
/// 作用在「玩家自己的东西」上的效果：生命、背包、手持物品、氧气。
///
/// 为什么要单独一层：泰拉联机时这些由**玩家本人的客户端**做主。服务端改了，本人客户端会忽略
///（反编译 MessageBuffer.GetData：case 5 背包同步、case 16 生命同步，都有
/// `num == Main.myPlayer &amp;&amp; !Main.ServerSideCharacter → 忽略`）。
/// 而施法在服务端跑 —— 以前服务端直接 `statLife -= …`、`item.TurnToAir()`，
/// 联机时要么不生效（过载不扣血），要么刷物品（服务端以为丢了，客户端手里还在）。
///
/// 所以：单人 / 客户端直接做；服务端把同一件事发给那个玩家的客户端去做（<see cref="Net.HexMessage.OwnerEffect"/>）。
/// </summary>
public static class PlayerEffects
{
    private enum Kind : byte
    {
        TrueDamage = 0,
        YeetHeld = 1,
        Drown = 2,
        DropInventory = 3,
        ConsumeSlot = 4,
        Progress = 5,
    }

    // ── 对外 ─────────────────────────────────────────────────────────

    /// <summary>无视护甲扣血（原版 Mishap.trulyHurt）；扣到 0 就死，死亡信息用 <paramref name="deathText"/>。</summary>
    public static void TrueDamage(Player p, int amount, string deathText)
    {
        if (amount <= 0) { return; }
        if (Route(p, Kind.TrueDamage, w => { w.Write(amount); w.Write(deathText); })) { return; }
        ApplyTrueDamage(p, amount, deathText);
    }

    /// <summary>把手持物品朝某点（世界像素）甩出去（原版 yeetHeldItemsTowards，速度 0.5 格/刻）。</summary>
    public static void YeetHeld(Player p, Vector2 targetPx)
    {
        if (Route(p, Kind.YeetHeld, w => { w.Write(targetPx.X); w.Write(targetPx.Y); })) { return; }
        ApplyYeetHeld(p, targetPx);
    }

    /// <summary>原版 drown()：氧气清零；本来就缺氧（不到 2/3）再扣 2/20 生命。</summary>
    public static void Drown(Player p)
    {
        if (Route(p, Kind.Drown, _ => { })) { return; }
        ApplyDrown(p);
    }

    /// <summary>原版 MishapNoSpellCircle：背包、盔甲、饰品全部掉出来（收藏的也掉；原版只放过绑定诅咒的盔甲）。</summary>
    public static void DropInventory(Player p)
    {
        if (Route(p, Kind.DropInventory, _ => { })) { return; }
        ApplyDropInventory(p);
    }

    /// <summary>从某个背包格子扣掉几个（放置方块用掉的那块）。</summary>
    public static void ConsumeSlot(Player p, int slot, int count)
    {
        if (Route(p, Kind.ConsumeSlot, w => { w.Write((short)slot); w.Write(count); })) { return; }
        ApplyConsumeSlot(p, slot, count);
    }

    /// <summary>
    /// 把服务端这边的咒法学进度标记（启蒙 / 盲目绘制 / 睁开双眼）同步给玩家客户端 ——
    /// 存档在客户端，服务端设了不同步的话，下线就没了。只会**置真**，不会清掉客户端已有的标记。
    /// </summary>
    public static void SyncProgress(Player p)
    {
        if (Main.netMode != NetmodeID.Server) { return; }
        var hp = HexPlayer.Get(p);
        Route(p, Kind.Progress, w => { w.Write(hp.Enlightened); w.Write(hp.FailedGreatSpell); w.Write(hp.Overcasted); });
    }

    // ── 联机转发 ─────────────────────────────────────────────────────

    /// <summary>服务端：发给那个玩家的客户端去做，返回 true。其它情况返回 false（就地执行）。</summary>
    private static bool Route(Player p, Kind kind, System.Action<System.IO.BinaryWriter> write)
    {
        if (Main.netMode != NetmodeID.Server) { return false; }
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet is null) { return true; }
        packet.Write((byte)Net.HexMessage.OwnerEffect);
        packet.Write((byte)kind);
        write(packet);
        packet.Send(p.whoAmI);
        return true;
    }

    /// <summary>客户端收到 <see cref="Net.HexMessage.OwnerEffect"/>：对自己执行。</summary>
    public static void Handle(System.IO.BinaryReader r)
    {
        var kind = (Kind)r.ReadByte();
        var p = Main.LocalPlayer;
        switch (kind)
        {
            case Kind.TrueDamage:
            {
                int amount = r.ReadInt32();
                string text = r.ReadString();
                if (p is { active: true, dead: false }) { ApplyTrueDamage(p, amount, text); }
                break;
            }
            case Kind.YeetHeld:
            {
                var target = new Vector2(r.ReadSingle(), r.ReadSingle());
                if (p is { active: true, dead: false }) { ApplyYeetHeld(p, target); }
                break;
            }
            case Kind.Drown:
                if (p is { active: true, dead: false }) { ApplyDrown(p); }
                break;
            case Kind.DropInventory:
                if (p is { active: true }) { ApplyDropInventory(p); }
                break;
            case Kind.ConsumeSlot:
            {
                int slot = r.ReadInt16();
                int count = r.ReadInt32();
                if (p is { active: true }) { ApplyConsumeSlot(p, slot, count); }
                break;
            }
            case Kind.Progress:
            {
                bool enlightened = r.ReadBoolean(), failed = r.ReadBoolean(), overcasted = r.ReadBoolean();
                if (p is not { active: true }) { break; }
                var hp = HexPlayer.Get(p);
                if (failed) { hp.FailedGreatSpell = true; }
                if (overcasted) { hp.Overcasted = true; }
                if (enlightened) { hp.GrantEnlightenment(); }   // 本地调用才会在聊天框出「获得启迪」
                break;
            }
        }
    }

    // ── 本地执行（单人 / 本人客户端）─────────────────────────────────

    private static void ApplyTrueDamage(Player p, int amount, string deathText)
    {
        if (p.creativeGodMode) { return; }
        p.statLife -= amount;
        CombatText.NewText(p.getRect(), Client.HexColors.Overcast, amount);
        if (p.statLife <= 0)
        {
            p.statLife = 0;
            p.KillMe(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral(deathText)), amount, 0);
            return;
        }
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, p.whoAmI);
        }
    }

    /// <summary>MC 的 0.5 格/刻 → 泰拉像素/帧（1 格/刻 = 16/3 像素/帧，与 TerrariaCastingWorld.ApplyMotion 同一换算）。</summary>
    private const float YeetSpeed = 0.5f * 16f / 3f;

    private static void ApplyYeetHeld(Player p, Vector2 targetPx)
    {
        ref Item held = ref p.inventory[p.selectedItem];
        if (held is null || held.IsAir) { return; }
        var dir = targetPx - p.Center;
        dir = dir.LengthSquared() < 1e-4f ? new Vector2(p.direction, 0f) : Vector2.Normalize(dir);
        var vel = dir * YeetSpeed + new Vector2(Main.rand.NextFloat(-0.05f, 0.05f), Main.rand.NextFloat(-0.05f, 0.05f)) * 16f / 3f;
        // 原版 setPickUpDelay(40)：丢出去的东西本人 2 秒内捡不回来 —— 1.4.5 用 GrabDelayForLocalPlayer 表达
        Item.RequestNewItem(p.GetSource_Misc("HexMishap"), p.Center, held.Clone(), NewItemOwnership.GrabDelayForLocalPlayer, vel);
        held.TurnToAir();
    }

    private static void ApplyDrown(Player p)
    {
        if (p.breath < p.breathMax * 2 / 3)
        {
            // 原版：空气不足 200/300 时再受 2 点溺水伤害（20 点满血的 1/10）
            ApplyTrueDamage(p, System.Math.Max(1, p.statLifeMax2 / 10), p.name + "溺死了");
        }
        p.breath = 0;
        p.breathCD = 0;
    }

    private static void ApplyDropInventory(Player p)
    {
        var src = p.GetSource_Misc("HexMishap");
        // 0..49 背包 + 50..57 钱币弹药；58 是鼠标上的物品
        for (int i = 0; i < 59; i++)
        {
            ref Item it = ref p.inventory[i];
            if (it is null || it.IsAir) { continue; }
            Item.RequestNewItem(src, p.Center, it.Clone(), NewItemOwnership.GrabDelayForLocalPlayer,
                new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-4f, -1f)));
            it.TurnToAir();
        }
        for (int i = 0; i < p.armor.Length; i++)
        {
            ref Item it = ref p.armor[i];
            if (it is null || it.IsAir) { continue; }
            Item.RequestNewItem(src, p.Center, it.Clone(), NewItemOwnership.GrabDelayForLocalPlayer,
                new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-4f, -1f)));
            it.TurnToAir();
        }
        Main.mouseItem = new Item();
    }

    private static void ApplyConsumeSlot(Player p, int slot, int count)
    {
        if (slot < 0 || slot >= p.inventory.Length) { return; }
        ref Item it = ref p.inventory[slot];
        if (it is null || it.IsAir) { return; }
        it.stack -= count;
        if (it.stack <= 0) { it.TurnToAir(); }
    }
}
