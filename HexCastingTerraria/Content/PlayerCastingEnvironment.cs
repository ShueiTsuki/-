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
///   - 媒质来源：源项目扫背包里的紫水晶粉等容器；泰拉侧先用玩家媒质池（HexPlayer），
///     之后再接「从背包媒质物品扣取」
///   - 过载：源项目需要成就才能过载；泰拉侧由配置项/生命值决定
/// </summary>
public class PlayerCastingEnvironment : CastingEnvironment
{
    private readonly Player _player;

    public PlayerCastingEnvironment(Player player)
    {
        _player = player;
    }

    public Player Player => _player;

    /// <summary>
    /// 读取手持的数据载体里的 iota。
    ///
    /// 对应源项目 `env.getHeldItemToOperateOn` —— 那边查的是**副手**。
    /// 泰拉没有 MC 那种「主手/副手」双持语义，所以查手持物品。
    /// </summary>
    public override Core.Casting.Iotas.Iota? ReadHeldIota()
    {
        var held = _player.HeldItem;
        if (held.IsAir || held.ModItem is not ItemIotaStorage storage) return null;
        return storage.Read();
    }

    /// <summary>把 iota 写进手持的数据载体。只读载体（如卷轴）会拒绝。</summary>
    public override bool WriteHeldIota(Core.Casting.Iotas.Iota value)
    {
        var held = _player.HeldItem;
        if (held.IsAir || held.ModItem is not ItemIotaStorage storage) return false;
        return storage.TryStore(value);
    }

    /// <summary>手上是否拿着数据载体（不管里面有没有东西）。`readable` / `writable` 靠它区分「没拿」与「空着」。</summary>
    public override bool HasHeldStorage()
        => !_player.HeldItem.IsAir && _player.HeldItem.ModItem is ItemIotaStorage;

    /// <summary>手持载体可不可写。只读载体（卷轴）返回 false。</summary>
    public override bool IsHeldWritable()
        => _player.HeldItem.ModItem is ItemIotaStorage { ReadOnlyStorage: false };

    /// <summary>清空手持载体。返回原来是否有东西。</summary>
    public override bool ClearHeldIota()
        => _player.HeldItem.ModItem is ItemIotaStorage storage && storage.Clear();

    // ── 哨卫（sentinel/* 图案）──────────────────────────────────────

    /// <summary>当前哨卫。存在玩家身上，跨施法、跨重登都在。</summary>
    public override SentinelState? Sentinel => HexPlayer.Get(_player).Sentinel;

    public override void SetSentinel(double x, double y, bool great)
        => HexPlayer.Get(_player).Sentinel = new SentinelState(x, y, great);

    public override void ClearSentinel()
        => HexPlayer.Get(_player).Sentinel = null;

    // ── 打包法术与媒质瓶（craft/* 图案）────────────────────────────

    /// <summary>
    /// 手持的**空的**打包法术物品是哪一种。装过东西的不算 ——
    /// `craft/*` 只往空容器里封，否则会把别人存好的咒术覆盖掉。
    /// </summary>
    public override PackagedSpellKind? HeldEmptyPackagedSpell
        => _player.HeldItem.ModItem is ItemPackagedSpell { IsEmpty: true } packed
            ? packed.Kind
            : null;

    /// <summary>
    /// 手持的是不是「空瓶」。对应源项目 `PHIAL_BASE` 标签。
    ///
    /// ⚠️ 源项目额外要求**恰好 1 个**（`handStack.count != 1` 报错）。
    /// 泰拉的 `ItemID.Bottle` 可堆叠，所以这条限制要在这里补上 ——
    /// 否则一次施法会「用一个瓶子做出 N 个媒质瓶」。
    /// </summary>
    public override bool IsHeldPhialBase()
    {
        var held = _player.HeldItem;
        return !held.IsAir && held.type == Terraria.ID.ItemID.Bottle && held.stack == 1;
    }

    public override bool FillHeldPackagedSpell(
        System.Collections.Generic.IReadOnlyList<Core.Casting.Iotas.Iota> patterns, long media)
        => _player.HeldItem.ModItem is ItemPackagedSpell packed && packed.Fill(patterns, media);

    /// <summary>
    /// 把空瓶换成媒质瓶。
    ///
    /// 装出来的瓶子里存的**就是抽到的那个数**（煤质瓶带 `StoredMedia`），
    /// 所以「地上放 3 个充能紫水晶再 craft/battery」得到的是 30 万的瓶子 ——
    /// 与源项目 `ItemMediaHolder.withMedia(BATTERY, mediamount, mediamount)` 同一语义。
    /// </summary>
    public override bool CraftBatteryHeld(long media)
    {
        if (!IsHeldPhialBase()) return false;

        var held = _player.HeldItem;
        held.SetDefaults(ModContent.ItemType<MediaFlask>());

        if (held.ModItem is MediaFlask flask)
        {
            flask.SetStoredMedia(media);
        }

        return true;
    }

    public override bool HeldHasVariants() => _player.HeldItem.ModItem is ItemPackagedSpell;

    public override bool CycleHeldVariant()
    {
        if (_player.HeldItem.ModItem is not ItemPackagedSpell packed) return false;
        packed.CycleVariant();
        return true;
    }

    // ── 法术配色（colorize）────────────────────────────────────────

    /// <summary>
    /// 找一份可用作颜料的染料。
    ///
    /// 只认**基础染料**（有单一颜色的那些）：特殊染料（火焰/渐变/彩虹）会被跳过，
    /// 因为给它们硬套一个色值，玩家会看到「拿了彩虹染料却变紫」这种莫名其妙的result。
    /// 优先找手持的，再找背包里的 —— 与源项目「先在副手找」的优先级一致。
    /// </summary>
    public override int FindPigmentItem()
    {
        var held = _player.HeldItem;
        if (!held.IsAir && Client.HexPigment.ColorOf(held.type) is not null)
        {
            return held.type;
        }

        for (int i = 0; i < _player.inventory.Length; i++)
        {
            var item = _player.inventory[i];
            if (item is null || item.IsAir) continue;
            if (Client.HexPigment.ColorOf(item.type) is not null) return item.type;
        }

        return 0;
    }

    /// <summary>消耗一份染料并把配色记到玩家身上。</summary>
    public override void ApplyPigment(int itemType)
    {
        for (int i = 0; i < _player.inventory.Length; i++)
        {
            var item = _player.inventory[i];
            if (item is null || item.IsAir || item.type != itemType) continue;

            item.stack--;
            if (item.stack <= 0)
            {
                item.TurnToAir();
            }

            break;
        }

        HexPlayer.Get(_player).PigmentDyeType = itemType;

        if (_player.whoAmI == Main.myPlayer)
        {
            Client.HexPigment.Refresh(itemType);
            Client.HexCanvasState.SetMessage("法术配色已更换");
        }

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendData(Terraria.ID.MessageID.SyncPlayer, -1, -1, null, _player.whoAmI);
        }
    }

    /// <summary>
    /// 世界访问：由 <see cref="TerrariaCastingWorld"/> 提供。
    ///
    /// 世界图案（`get_caster` / `entity_pos/*` / `get_entity_*`）通过它取值。
    /// 之前这里是 null，世界图案一律报 MishapNoWorld —— 现在补上了。
    /// </summary>
    public override ICastingWorld World => _world ??= new TerrariaCastingWorld(_player);

    private ICastingWorld? _world;

    /// <summary>
    /// 媒质扣除。**顺序必须是 池 → 背包物品 → 过载**：
    ///
    ///   ① 玩家自身媒质池
    ///   ② 背包里的媒质材料（紫水晶粉 / 碎片 / 充能紫水晶 / 淬灵晶碎片）
    ///      —— 这是源项目的核心经济：媒质来自**背包物品**，不是凭空来的
    ///   ③ 仍不足则过载（用生命抵）
    ///
    /// 整件消耗造成的多付会**退回池中**（泰拉堆叠物品没有单件独立数据，
    /// 做不到源项目那样把一件粉尘扣到只剩 5,000）。
    /// 付不起时与源项目相同：物品照扣，能过载就按全部缺口扣血（可以致死），从不退款。
    ///
    /// 返回**还未付清**的量（<=0 表示够）。
    /// </summary>
    protected override long ExtractMediaEnvironment(long cost, bool simulate)
    {
        if (cost <= 0)
        {
            return 0;
        }

        var hexPlayer = HexPlayer.Get(_player);

        // 创造模式（无限媒质）：直接视为够用
        if (hexPlayer.InfiniteMedia || HexClientConfig.Instance.InfiniteMedia)
        {
            return 0;
        }

        var plan = MediaPaymentPlanner.Plan(cost, hexPlayer.Media, CollectMediaItems());
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
        // 源项目 extractMediaFromInventory：物品**照扣不误**（付不起也扣光），再用生命抵剩下的，**从不退款**。
        //（这里曾经「付不起就把已扣的退回去」—— 于是 MishapNotEnoughMedia 的「抽干」惩罚、
        //   以及原版「过载超过剩余生命会死」都不会发生）
        // 联机：背包与媒质池都归本人客户端，走 PlayerEffects（服务端直接改会被忽略 / 刷媒质）。
        if (plan.FromPool > 0)
        {
            hexPlayer.MediaStorage.Withdraw(plan.FromPool);
        }
        for (int i = 0; i < plan.FromItems.Count; i++)
        {
            var (slot, count) = plan.FromItems[i];
            PlayerEffects.ConsumeSlot(_player, slot, count);
        }
        if (plan.Change > 0)
        {
            hexPlayer.MediaStorage.Insert(plan.Change);   // 整件消耗的多付退回池中
        }
        PlayerEffects.SyncMedia(_player);

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

    /// <summary>
    /// 收集背包里所有媒质材料。
    ///
    /// 只认 MediaMaterial **类型**，不按名字判定 ——
    /// 名字判定会被其它模组的同名物品骗到。
    /// </summary>
    private System.Collections.Generic.List<MediaStack> CollectMediaItems()
    {
        var list = new System.Collections.Generic.List<MediaStack>();

        for (int i = 0; i < _player.inventory.Length; i++)
        {
            var item = _player.inventory[i];
            if (item.IsAir || item.stack <= 0) continue;
            if (item.ModItem is not MediaMaterial material) continue;

            list.Add(new MediaStack
            {
                Slot = i,
                UnitValue = material.MediaValue,
                Count = item.stack,
            });
        }

        return list;
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
        => PlayerEffects.YeetHeld(_player, _player.Center + HexPlayer.Get(_player).Look * 16f);

    // ── mishap 惩罚（原版 PlayerBasedMishapEnv）──────────────────────────

    public override void YeetHeldItemsTowards(double x, double y)
        => PlayerEffects.YeetHeld(_player, new Microsoft.Xna.Framework.Vector2((float)(x * 16.0), (float)(y * 16.0)));

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
            var hexPlayer = HexPlayer.Get(_player);
            hexPlayer.MediaStorage.Insert(hexPlayer.MaxMedia);
            PlayerEffects.SyncMedia(_player);
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