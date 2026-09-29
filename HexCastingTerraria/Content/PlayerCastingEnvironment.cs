using HexCastingTerraria.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ModLoader;
using HexCastingTerraria.Client;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Content;

/// <summary>
/// 玩家施法环境：把 VM 的抽象需求接到泰拉玩家身上。
///
/// 对应源项目 api/casting/eval/env/PlayerBasedCastEnv + StaffCastEnv。
///
/// 与源项目的差异（有意为之）：
///   - 媒质来源：与源项目一致，只从背包里的媒质物品扣（见 ExtractMediaEnvironment）
///   - 过载：与源项目一致，先失败过一次大法术（进度「盲目绘制」）才能过载
/// </summary>
public class PlayerCastingEnvironment : CastingEnvironment
{
    private readonly Player _player;

    public PlayerCastingEnvironment(Player player)
    {
        _player = player;
    }

    public Player Player => _player;

    // ── 「手」：原版 getPrimaryStacks = [另一只手, 施法的手] ───────────────
    //
    // 泰拉没有副手。「另一只手」= **快捷栏里施法物品右边那一格**（到第 10 格就绕回第 1 格）——
    // 原版放方块时也是从「法杖右边一格」开始找（书里讲过），这样拿着法杖施法时，
    // 把核心 / 法术书 / 空瓶 / 染料放在它右边就等于拿在另一只手里。
    // 施法物品不在快捷栏（鼠标上拿着）时只有手上这一格。所有「手持物品」类图案都按这个顺序找。

    /// <summary>[另一只手, 施法的手] 对应的背包格子。</summary>
    public int[] PrimarySlots()
    {
        int main = _player.selectedItem;
        return main is >= 0 and < 10 ? new[] { (main + 1) % 10, main } : new[] { main };
    }

    /// <summary>原版 getHeldItemToOperateOn(谓词)：第一个满足谓词的格子，没有 → -1。</summary>
    private int FindHeld(System.Func<Item, bool> ok)
    {
        foreach (int slot in PrimarySlots())
        {
            if (slot >= 0 && slot < _player.inventory.Length && _player.inventory[slot] is { IsAir: false } it && ok(it))
            {
                return slot;
            }
        }
        return -1;
    }

    private Item? HeldAt(System.Func<Item, bool> ok) => FindHeld(ok) is var s and >= 0 ? _player.inventory[s] : null;

    // ── 数据载体（read / write / erase）──────────────────────────────

    public override Core.Casting.Iotas.Iota? ReadHeldIota()
        => (HeldAt(i => i.ModItem is ItemIotaStorage s && s.Read() != null)?.ModItem as ItemIotaStorage)?.Read();

    public override bool HasHeldStorage() => FindHeld(i => i.ModItem is ItemIotaStorage) >= 0;

    public override bool IsHeldWritable()
        => HeldAt(i => i.ModItem is ItemIotaStorage)?.ModItem is ItemIotaStorage { Writeable: true };

    public override bool CanWriteHeld(Core.Casting.Iotas.Iota? datum)
        => FindHeld(i => i.ModItem is ItemIotaStorage s && s.WriteIota(datum, simulate: true)) >= 0;

    /// <summary>手持物品归本人客户端 —— 走 PlayerEffects（服务端自己那份同时改，客户端那份由它转发）。</summary>
    public override bool WriteHeldIota(Core.Casting.Iotas.Iota value)
    {
        int slot = FindHeld(i => i.ModItem is ItemIotaStorage s && s.WriteIota(value, simulate: true));
        return slot >= 0 && PlayerEffects.WriteSlot(_player, slot, value);
    }

    /// <summary>原版 OpErase 的目标：装着咒术的打包法术，或者肯被清除的载体。</summary>
    private int EraseTarget() => FindHeld(i =>
        i.ModItem is ItemPackagedSpell { IsEmpty: false }
        || i.ModItem is ItemIotaStorage s && s.WriteIota(null, simulate: true));

    public override int HeldEraseableCount() => EraseTarget() is var s and >= 0 ? _player.inventory[s].stack : 0;

    public override void EraseHeld()
    {
        int slot = EraseTarget();
        if (slot >= 0) PlayerEffects.EraseSlot(_player, slot);
    }

    // ── 哨卫（sentinel/* 图案）──────────────────────────────────────

    /// <summary>当前哨卫。存在玩家身上，跨施法、跨重登都在。</summary>
    public override SentinelState? Sentinel => HexPlayer.Get(_player).Sentinel;

    public override void SetSentinel(double x, double y, bool great)
    {
        HexPlayer.Get(_player).Sentinel = new SentinelState(x, y, great);
        HexPlayer.Get(_player).SyncFromServer();   // 存档在客户端、画也在客户端
    }

    public override void ClearSentinel()
    {
        HexPlayer.Get(_player).Sentinel = null;
        HexPlayer.Get(_player).SyncFromServer();
    }

    // ── 打包法术与媒质瓶（craft/* 图案）────────────────────────────

    /// <summary>
    /// 手上**空的**打包法术是哪一种。装过东西的不算 ——
    /// `craft/*` 只往空容器里封（原版 `!hexHolder.hasHex()`），否则会把存好的咒术覆盖掉。
    /// </summary>
    public override PackagedSpellKind? HeldEmptyPackagedSpell
        => (HeldAt(i => i.ModItem is ItemPackagedSpell { IsEmpty: true })?.ModItem as ItemPackagedSpell)?.Kind;

    /// <summary>手上第一个空瓶的数量（原版 PHIAL_BASE；恰好 1 个的检查在图案里）。</summary>
    public override int HeldPhialCount()
        => HeldAt(i => i.type == Terraria.ID.ItemID.Bottle)?.stack ?? 0;

    public override bool FillHeldPackagedSpell(
        System.Collections.Generic.IReadOnlyList<Core.Casting.Iotas.Iota> patterns, long media)
    {
        int slot = FindHeld(i => i.ModItem is ItemPackagedSpell { IsEmpty: true });
        return slot >= 0 && PlayerEffects.FillPackaged(_player, slot, patterns, media);
    }

    /// <summary>
    /// 把空瓶换成媒质瓶：存量 = 上限 = 抽到的量
    ///（源项目 `ItemMediaHolder.withMedia(BATTERY, mediamount, mediamount)`）。
    /// </summary>
    public override bool CraftBatteryHeld(long media)
    {
        int slot = FindHeld(i => i.type == Terraria.ID.ItemID.Bottle);
        if (slot < 0 || _player.inventory[slot].stack != 1) return false;
        PlayerEffects.MakeFlask(_player, slot, media);
        return true;
    }

    /// <summary>原版 OpRecharge 的目标：可充能（媒质瓶，或装过咒术的打包法术）且还装得下。</summary>
    private int RechargeTarget() => FindHeld(i => i.ModItem switch
    {
        MediaFlask f => f.Media < f.MaxMedia,
        ItemPackagedSpell p => p.MaxMedia > 0 && p.Media < p.MaxMedia,
        _ => false,
    });

    public override long HeldRechargeSpace()
    {
        int slot = RechargeTarget();
        return slot < 0 ? -1 : _player.inventory[slot].ModItem switch
        {
            MediaFlask f => f.MaxMedia - f.Media,
            ItemPackagedSpell p => p.MaxMedia - p.Media,
            _ => -1,
        };
    }

    public override void ChargeHeld(long media)
    {
        int slot = RechargeTarget();
        if (slot >= 0) PlayerEffects.BatteryDelta(_player, slot, media);
    }

    /// <summary>原版 VariantItem：符纸 / 缀品 / 造物、核心、法术书。</summary>
    public override bool HeldHasVariants() => FindHeld(i => i.ModItem is IHexVariantItem) >= 0;

    public override bool CycleHeldVariant()
    {
        int slot = FindHeld(i => i.ModItem is IHexVariantItem);
        return slot >= 0 && PlayerEffects.CycleVariant(_player, slot);
    }

    // ── 法术配色（colorize）────────────────────────────────────────

    /// <summary>
    /// 手上（两个位置之一）的颜料（泰拉侧 = 基础染料）。原版只看手上，不翻背包。
    /// 特殊染料（火焰/渐变/彩虹）没有单一颜色，不算。
    /// </summary>
    public override int FindPigmentItem()
        => HeldAt(i => Client.HexPigment.ColorOf(i.type) is not null)?.type ?? 0;

    /// <summary>
    /// 消耗一份同种染料并把配色记到玩家身上。
    /// 原版 withdrawItem 的顺序：背包从后往前（跳过手上那格），最后才是手上。
    /// </summary>
    public override void ApplyPigment(int itemType)
    {
        int slot = -1;
        for (int i = 49; i >= 0 && slot < 0; i--)
        {
            if (i != _player.selectedItem && _player.inventory[i] is { IsAir: false } item && item.type == itemType) slot = i;
        }
        if (slot < 0 && _player.HeldItem.type == itemType) slot = _player.selectedItem;
        if (slot < 0) return;
        PlayerEffects.ConsumeSlot(_player, slot, 1);

        HexPlayer.Get(_player).PigmentDyeType = itemType;

        if (_player.whoAmI == Main.myPlayer)
        {
            Client.HexPigment.Refresh(itemType);
            Client.HexCanvasState.SetMessage("法术配色已更换");
        }

        HexPlayer.Get(_player).SyncFromServer();
    }

    /// <summary>
    /// 世界访问：由 <see cref="TerrariaCastingWorld"/> 提供。
    ///
    /// 世界图案（`get_caster` / `entity_pos/*` / `get_entity_*`）通过它取值。
    /// 之前这里是 null，世界图案一律报 MishapNoWorld —— 现在补上了。
    /// </summary>
    public override ICastingWorld World => _world ??= new HexSpaceWorld(new TerrariaCastingWorld(_player));

    private ICastingWorld? _world;

    /// <summary>
    /// 媒质扣除。移植自源项目 PlayerBasedCastEnv.extractMediaFromInventory：
    ///
    ///   ① 背包里的媒质来源，按原版优先级（媒质瓶 → 粉 → 碎片 → 充能紫水晶 → 淬灵碎片，同级存量大的先扣）；
    ///      堆叠物品整件扣，多付的浪费（原版同）；媒质瓶按量扣
    ///   ② 仍不足且能过载 → 用生命抵
    ///
    /// 付不起时与源项目相同：物品照扣，能过载就按全部缺口扣血（可以致死），从不退款。
    /// 返回**还未付清**的量（&lt;=0 表示够）。
    /// </summary>
    protected override long ExtractMediaEnvironment(long cost, bool simulate)
    {
        if (cost <= 0)
        {
            return 0;
        }

        var hexPlayer = HexPlayer.Get(_player);

        // 创造模式（无限媒质）：直接视为够用（源项目 StaffCastEnv 在创造模式下同样不扣）
        if (hexPlayer.InfiniteMedia || HexClientConfig.Instance.InfiniteMedia)
        {
            return 0;
        }

        var plan = MediaPaymentPlanner.Plan(cost, hexPlayer.CollectMediaSources());
        long shortfall = plan.Shortfall;

        if (simulate)
        {
            if (shortfall <= 0)
            {
                return 0;
            }
            // 试算（原版 simulate 分支）：能过载时按生命折算，换到的媒质 = min(当前生命, 需要扣的生命) × 汇率
            if (!CanOvercast()) { return shortfall; }
            var (_, gainable, _) = Overcast.Plan(shortfall, _player.statLife, _player.statLifeMax2);
            return shortfall - gainable;
        }

        // ---- 真正扣除 ----
        // 物品**照扣不误**（付不起也扣光），再用生命抵剩下的，**从不退款**。
        // 联机：背包归本人客户端，走 PlayerEffects（服务端直接改会被忽略 / 刷媒质）。
        foreach (var w in plan.Withdrawals)
        {
            if (w.Items > 0) { PlayerEffects.ConsumeSlot(_player, w.Slot, w.Items); }
            if (w.BatteryMedia > 0) { PlayerEffects.BatteryDelta(_player, w.Slot, -w.BatteryMedia); }
        }

        if (shortfall <= 0 || !CanOvercast())
        {
            return System.Math.Max(0, shortfall);
        }

        // ---- 剩余缺口走过载（扣血）：源项目 trulyHurt(缺口 / 汇率)，超过剩余生命就死 ----
        var (damage, gained, lethal) = Overcast.Plan(shortfall, _player.statLife, _player.statLifeMax2);
        int lifeBefore = _player.statLife;
        int lifeAfter = lifeBefore - damage;
        hexPlayer.NoteOvercast(damage);
        if (!HexClientConfig.Instance.NoOvercastDamage)
        {
            // 死亡信息「%s的意识消散为了能量」
            PlayerEffects.TrueDamage(_player, damage, _player.name + "的意识消散为了能量");
            if (lethal) { return shortfall - gained; }
        }
        else if (_player.whoAmI == Main.myPlayer)
        {
            CombatText.NewText(_player.getRect(), HexColors.Overcast, damage);
        }

        // 原版 ENLIGHTEN：这一下用掉 ≥80% 最大生命，且活了下来、只剩不到半颗心
        if (lifeAfter > 0)
        {
            hexPlayer.Overcasted = true;
        }
        if (Overcast.IsEnlightening(damage, _player.statLifeMax2, lifeAfter))
        {
            hexPlayer.GrantEnlightenment();
        }
        PlayerEffects.SyncProgress(_player);
        return System.Math.Max(0, shortfall - gained);
    }

    /// <summary>是否已启蒙（可施放大法术）。对应源项目的 enlightenment 成就。</summary>
    /// <summary>是否已启蒙。配置里的 AlwaysEnlightened 是调试开关 —— 用来测 14 条大法术。</summary>
    public override bool IsEnlightened()
        => HexPlayer.Get(_player).Enlightened || HexClientConfig.Instance.AlwaysEnlightened;

    /// <summary>原版 canOvercast：要先失败过一次大法术（进度「盲目绘制」）才解锁过载。</summary>
    public override bool CanOvercast() => HexPlayer.Get(_player).FailedGreatSpell;

    public override void OnFailedGreatSpell()
    {
        var hp = HexPlayer.Get(_player);
        if (hp.FailedGreatSpell) { return; }
        hp.FailedGreatSpell = true;
        PlayerEffects.SyncProgress(_player);
        PrintMessage("盲目绘制 —— 法术没能起效，但你隐约察觉到：媒质不够时，也许可以拿生命去换。");
    }

    /// <summary>原版 dropHeldItems：把手上的物品丢出去（未启蒙强行施放大法术的代价）。</summary>
    /// <summary>原版 dropHeldItems：朝视线方向（前方一格）甩出去。</summary>
    public override void DropHeldItems()
        => PlayerEffects.YeetHeld(_player, PrimarySlots(), _player.Center + HexPlayer.Get(_player).Look * 16f);

    // ── mishap 惩罚（原版 PlayerBasedMishapEnv）──────────────────────────

    public override void YeetHeldItemsTowards(double x, double y)
        => PlayerEffects.YeetHeld(_player, PrimarySlots(), HexSpaceWorld.ToWorldPixels(x, y));   // Core 给的是法术坐标（+Y 朝上）

    /// <summary>原版 damage(p)：trulyHurt(当前生命 × p)。</summary>
    public override void MishapDamage(double healthProportion)
        => PlayerEffects.TrueDamage(_player, (int)System.Math.Ceiling(_player.statLife * healthProportion), _player.name + "的咒术反噬了自己");

    public override void MishapDrown() => PlayerEffects.Drown(_player);

    /// <summary>原版失明 → 泰拉「黑暗」（Blackout，视野近乎全黑）。MC 刻 × 3 = 泰拉帧。</summary>
    public override void MishapBlind(int mcTicks)
        => TerrariaCastingWorld.AddPlayerBuff(_player, Terraria.ID.BuffID.Blackout, mcTicks * 3);

    public override void MishapDropInventory() => PlayerEffects.DropInventory(_player);

    public override int MaxOpCount() => HexClientConfig.Instance.MaxOpCount;

    /// <summary>
    /// 每条图案执行后：把 mishap 的错误消息发到聊天框。
    ///
    /// 对应源项目 PlayerBasedCastEnv.postExecution ——
    /// 它遍历本次结果的副作用，遇到 DoMishap 就 sendMishapMsgToPlayer，
    /// 消息带图案名前缀（errorMessageWithName）。
    /// 这就是玩家在聊天框看到的「施法提示」。
    /// </summary>
    public override void PostExecution(CastResult result)
    {
        base.PostExecution(result);

        // 调试开关：每次求值后把媒质补满（连续测法术时不用一直补）
        if (HexClientConfig.Instance.RefillMediaAfterCast)
        {
            PlayerEffects.RefillFlasks(_player);
        }

        for (int i = 0; i < result.SideEffects.Count; i++)
        {
            if (result.SideEffects[i] is DoMishapSideEffect doMishap)
            {
                var msg = doMishap.Mishap.ErrorMessageWithName(this, doMishap.ErrorCtx);
                if (!string.IsNullOrEmpty(msg))
                {
                    PrintMessage(msg!);
                }
            }
        }
    }

    /// <summary>把消息发到玩家聊天框。对应源项目 printMessage → sendSystemMessage。</summary>
    public override void PrintMessage(string message)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            return;   // 服务端由 tModLoader 转发，不在本地打印
        }
        Main.NewText(message, HexColors.Overcast);
    }
}