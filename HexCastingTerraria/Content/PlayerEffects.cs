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
///
/// 背包物品类的效果**服务端自己那份也照改**：同一次施法里后面的图案要看到改过的值
///（先 write 再 read、连着扣两次媒质）。客户端改完调 Item.NetStateChanged()，泰拉会把这一格同步回服务端，两边一致。
/// 生命 / 氧气类只在本人客户端做（服务端改了也会被客户端的同步盖掉）。
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
        // 5 = 旧的进度同步，已并入 HexPlayer 的状态同步（HexMessage.PlayerState）
        BatteryDelta = 6,
        MakeFlask = 7,
        RefillFlasks = 8,
        WriteSlot = 9,
        EraseSlot = 10,
        FillPackaged = 11,
        CycleVariant = 12,
        Scatter = 13,
    }

    // ── 对外 ─────────────────────────────────────────────────────────

    /// <summary>无视护甲扣血（原版 Mishap.trulyHurt）；扣到 0 就死，死亡信息用 <paramref name="deathText"/>。</summary>
    public static void TrueDamage(Player p, int amount, string deathText)
    {
        if (amount <= 0) { return; }
        if (ToOwner(p, Kind.TrueDamage, w => { w.Write(amount); w.Write(deathText); })) { return; }
        ApplyTrueDamage(p, amount, deathText);
    }

    /// <summary>
    /// 把「两只手」里的东西朝某点（世界像素）甩出去（原版 yeetHeldItemsTowards：两只手都甩，法杖也不例外，速度 0.5 格/刻）。
    /// <paramref name="slots"/> = PlayerCastingEnvironment.PrimarySlots（另一只手 + 施法的手）。
    /// </summary>
    public static void YeetHeld(Player p, int[] slots, Vector2 targetPx)
    {
        if (ToOwner(p, Kind.YeetHeld, w =>
            {
                w.Write(targetPx.X);
                w.Write(targetPx.Y);
                w.Write((byte)slots.Length);
                foreach (int s in slots) w.Write((short)s);
            }))
        {
            foreach (int s in slots) { Slot(p, s)?.TurnToAir(); }   // 服务端那份：只清掉，掉落物由客户端生成
            return;
        }
        ApplyYeetHeld(p, slots, targetPx);
    }

    /// <summary>原版 drown()：氧气清零；本来就缺氧（不到 2/3）再扣 2/20 生命。</summary>
    public static void Drown(Player p)
    {
        if (ToOwner(p, Kind.Drown, _ => { })) { return; }
        ApplyDrown(p);
    }

    /// <summary>原版 MishapNoSpellCircle：背包、盔甲、饰品全部掉出来（收藏的也掉；原版只放过绑定诅咒的盔甲）。</summary>
    public static void DropInventory(Player p)
    {
        if (ToOwner(p, Kind.DropInventory, _ => { }))
        {
            for (int i = 0; i < 59; i++) { p.inventory[i]?.TurnToAir(); }
            foreach (var it in p.armor) { it?.TurnToAir(); }
            return;
        }
        ApplyDropInventory(p);
    }

    /// <summary>
    /// 大传送的代价（原版 OpTeleport）：每格按概率把**整堆**丢到地上。
    /// 护甲 ×0.25，快捷栏 ×0.5，其余 ×1；**永不掉主手**（原版：主手是饰品的话会被复制）。
    /// 掷骰在本人客户端做 —— 服务端那份不动，等客户端丢完同步回来。
    /// </summary>
    public static void Scatter(Player p, double baseChance)
    {
        if (baseChance <= 0) { return; }
        if (ToOwner(p, Kind.Scatter, w => w.Write(baseChance))) { return; }
        ApplyScatter(p, baseChance);
    }

    /// <summary>从某个背包格子扣掉几个（放置方块用掉的那块）。</summary>
    public static void ConsumeSlot(Player p, int slot, int count)
    {
        ApplyConsumeSlot(p, slot, count);
        ToOwner(p, Kind.ConsumeSlot, w => { w.Write((short)slot); w.Write(count); });
    }

    /// <summary>
    /// 服务端改了咒法学进度（启蒙 / 盲目绘制 / 睁开双眼）、哨卫、配色后，同步给客户端 ——
    /// 存档在客户端，不同步的话下线就没了。见 <see cref="HexPlayer.SyncFromServer"/>。
    /// </summary>
    public static void SyncProgress(Player p) => HexPlayer.Get(p).SyncFromServer();

    /// <summary>
    /// 媒质瓶（或打包法术）的存量增减：施法扣费（负）、重新充能（正）。越界截断。
    /// </summary>
    public static void BatteryDelta(Player p, int slot, long delta)
    {
        ApplyBatteryDelta(p, slot, delta);
        ToOwner(p, Kind.BatteryDelta, w => { w.Write((short)slot); w.Write(delta); });
    }

    /// <summary>把某格的空瓶换成一个存量 = 上限 = <paramref name="media"/> 的媒质瓶（craft/battery）。</summary>
    public static void MakeFlask(Player p, int slot, long media)
    {
        ApplyMakeFlask(p, slot, media);
        ToOwner(p, Kind.MakeFlask, w => { w.Write((short)slot); w.Write(media); });
    }

    /// <summary>开发者：背包里所有媒质瓶补满。</summary>
    public static void RefillFlasks(Player p)
    {
        ApplyRefillFlasks(p);
        ToOwner(p, Kind.RefillFlasks, _ => { });
    }

    /// <summary>往某格的数据载体里写一个 iota（write）。返回是否写进去了（只读 / 类型不符 / 封印 → false）。</summary>
    public static bool WriteSlot(Player p, int slot, Core.Casting.Iotas.Iota value)
    {
        if (!ApplyWriteSlot(p, slot, value)) { return false; }
        ToOwner(p, Kind.WriteSlot, w => { w.Write((short)slot); Net.IotaWire.Write(w, value); });
        return true;
    }

    /// <summary>
    /// `erase`（原版 OpErase.Spell.cast）：打包法术清掉咒术（clearHex），数据载体写入 null（核心顺带解封）。
    /// </summary>
    public static void EraseSlot(Player p, int slot)
    {
        if (!ApplyEraseSlot(p, slot)) { return; }
        ToOwner(p, Kind.EraseSlot, w => w.Write((short)slot));
    }

    /// <summary>把图案与媒质封进某格的空打包法术（craft/cypher 等）。</summary>
    public static bool FillPackaged(Player p, int slot, System.Collections.Generic.IReadOnlyList<Core.Casting.Iotas.Iota> patterns, long media)
    {
        if (!ApplyFillPackaged(p, slot, patterns, media)) { return false; }
        ToOwner(p, Kind.FillPackaged, w =>
        {
            w.Write((short)slot);
            w.Write((ushort)patterns.Count);
            foreach (var iota in patterns) { Net.IotaWire.Write(w, iota); }
            w.Write(media);
        });
        return true;
    }

    /// <summary>某格物品的外观变体 +1（cycle_variant；原版 VariantItem）。</summary>
    public static bool CycleVariant(Player p, int slot)
    {
        if (!ApplyCycleVariant(p, slot)) { return false; }
        ToOwner(p, Kind.CycleVariant, w => w.Write((short)slot));
        return true;
    }

    // ── 联机转发 ─────────────────────────────────────────────────────

    /// <summary>服务端：发给那个玩家的客户端去做，返回 true。其它情况返回 false。</summary>
    private static bool ToOwner(Player p, Kind kind, System.Action<System.IO.BinaryWriter> write)
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
                var slots = new int[r.ReadByte()];
                for (int i = 0; i < slots.Length; i++) { slots[i] = r.ReadInt16(); }
                if (p is { active: true, dead: false }) { ApplyYeetHeld(p, slots, target); }
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
            case Kind.BatteryDelta:
            {
                int slot = r.ReadInt16();
                long delta = r.ReadInt64();
                if (p is { active: true }) { ApplyBatteryDelta(p, slot, delta); }
                break;
            }
            case Kind.MakeFlask:
            {
                int slot = r.ReadInt16();
                long media = r.ReadInt64();
                if (p is { active: true }) { ApplyMakeFlask(p, slot, media); }
                break;
            }
            case Kind.RefillFlasks:
                if (p is { active: true }) { ApplyRefillFlasks(p); }
                break;
            case Kind.WriteSlot:
            {
                int slot = r.ReadInt16();
                var iota = Net.IotaWire.Read(r);
                if (p is { active: true }) { ApplyWriteSlot(p, slot, iota); }
                break;
            }
            case Kind.EraseSlot:
            {
                int slot = r.ReadInt16();
                if (p is { active: true }) { ApplyEraseSlot(p, slot); }
                break;
            }
            case Kind.FillPackaged:
            {
                int slot = r.ReadInt16();
                int n = r.ReadUInt16();
                var list = new System.Collections.Generic.List<Core.Casting.Iotas.Iota>(n);
                for (int i = 0; i < n; i++) { list.Add(Net.IotaWire.Read(r)); }
                long media = r.ReadInt64();
                if (p is { active: true }) { ApplyFillPackaged(p, slot, list, media); }
                break;
            }
            case Kind.Scatter:
            {
                double chance = r.ReadDouble();
                if (p is { active: true, dead: false }) { ApplyScatter(p, chance); }
                break;
            }
            case Kind.CycleVariant:
            {
                int slot = r.ReadInt16();
                if (p is { active: true }) { ApplyCycleVariant(p, slot); }
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

    private static void ApplyYeetHeld(Player p, int[] slots, Vector2 targetPx)
    {
        var dir = targetPx - p.Center;
        dir = dir.LengthSquared() < 1e-4f ? new Vector2(p.direction, 0f) : Vector2.Normalize(dir);
        foreach (int s in slots)
        {
            if (Slot(p, s) is not { } held) { continue; }
            var vel = dir * YeetSpeed + new Vector2(Main.rand.NextFloat(-0.05f, 0.05f), Main.rand.NextFloat(-0.05f, 0.05f)) * 16f / 3f;
            // 原版 setPickUpDelay(40)：丢出去的东西本人 2 秒内捡不回来 —— 1.4.5 用 GrabDelayForLocalPlayer 表达
            Item.RequestNewItem(p.GetSource_Misc("HexMishap"), p.Center, held.Clone(), NewItemOwnership.GrabDelayForLocalPlayer, vel);
            held.TurnToAir();
        }
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

    /// <summary>
    /// 改了物品内部数据（不是类型 / 数量）后必须调：泰拉只比较类型 / 数量 / 前缀 / 这个版本号，
    /// 不调的话客户端改了也不会把这一格同步给服务端。
    /// </summary>
    private static void Touched(Item it) => it.NetStateChanged();

    private static Item? Slot(Player p, int slot)
        => slot >= 0 && slot < p.inventory.Length && p.inventory[slot] is { IsAir: false } it ? it : null;

    private static void ApplyScatter(Player p, double baseChance)
    {
        var src = p.GetSource_Misc("hexcasting:greater_teleport");
        void Roll(ref Item it, double chance)
        {
            if (it is null || it.IsAir || Main.rand.NextDouble() >= chance) { return; }
            Item.RequestNewItem(src, p.Center, it.Clone(), NewItemOwnership.GrabDelayForLocalPlayer, Vector2.Zero);
            it.TurnToAir();
        }
        // 护甲：头 / 胸 / 腿（原版 4 格护甲；时装、饰品不算）
        for (int i = 0; i < 3 && i < p.armor.Length; i++) { Roll(ref p.armor[i], baseChance * 0.25); }
        // 背包 + 钱币 + 弹药（0..57；58 是鼠标上拿着的）
        for (int i = 0; i < 58; i++)
        {
            if (i == p.selectedItem) { continue; }
            Roll(ref p.inventory[i], i < 10 ? baseChance * 0.5 : baseChance);
        }
    }

    private static void ApplyBatteryDelta(Player p, int slot, long delta)
    {
        if (Slot(p, slot) is not { } it) { return; }
        switch (it.ModItem)
        {
            case Items.MediaFlask f:
                if (delta >= 0) { f.Insert(delta); } else { f.Withdraw(-delta); }
                break;
            case Items.ItemPackagedSpell pk:
                if (delta >= 0) { pk.Refund(delta); } else { pk.Spend(-delta); }
                break;
            default:
                return;
        }
        Touched(it);
    }

    private static bool ApplyWriteSlot(Player p, int slot, Core.Casting.Iotas.Iota value)
    {
        if (Slot(p, slot) is not { ModItem: Items.ItemIotaStorage s } it || !s.WriteIota(value, simulate: false)) { return false; }
        Touched(it);
        return true;
    }

    private static bool ApplyEraseSlot(Player p, int slot)
    {
        if (Slot(p, slot) is not { } it) { return false; }
        bool any = false;
        if (it.ModItem is Items.ItemPackagedSpell { IsEmpty: false } pk) { pk.ClearHex(); any = true; }
        if (it.ModItem is Items.ItemIotaStorage s && s.WriteIota(null, simulate: false)) { any = true; }
        if (any) { Touched(it); }
        return any;
    }

    private static bool ApplyFillPackaged(Player p, int slot, System.Collections.Generic.IReadOnlyList<Core.Casting.Iotas.Iota> patterns, long media)
    {
        if (Slot(p, slot) is not { ModItem: Items.ItemPackagedSpell pk } it || !pk.Fill(patterns, media)) { return false; }
        Touched(it);
        return true;
    }

    private static bool ApplyCycleVariant(Player p, int slot)
    {
        if (Slot(p, slot) is not { ModItem: Items.IHexVariantItem v } it) { return false; }
        v.SetVariant((v.Variant + 1) % v.NumVariants);
        Touched(it);
        return true;
    }

    private static void ApplyMakeFlask(Player p, int slot, long media)
    {
        if (slot < 0 || slot >= p.inventory.Length) { return; }
        ref Item it = ref p.inventory[slot];
        if (it is null || it.IsAir || it.type != ItemID.Bottle) { return; }
        it.SetDefaults(Terraria.ModLoader.ModContent.ItemType<Items.MediaFlask>());
        (it.ModItem as Items.MediaFlask)?.SetMedia(media, media);
        Touched(it);
    }

    private static void ApplyRefillFlasks(Player p)
    {
        for (int i = 0; i < 58 && i < p.inventory.Length; i++)
        {
            if (p.inventory[i]?.ModItem is Items.MediaFlask f) { f.SetMedia(f.MaxMedia, f.MaxMedia); Touched(p.inventory[i]); }
        }
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
