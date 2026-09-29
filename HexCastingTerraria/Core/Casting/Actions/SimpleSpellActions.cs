using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

// 源项目 `spells/` 里「一个格子/一个点」就能说完的那几个法术。
// 它们共性很强：1~3 个参数 → 一次世界改动，消耗固定。
// 放在一个文件里是为了能一眼看出彼此的**消耗差异**（这才是玩家真正关心的）。

/// <summary>
/// `beep`：在指定位置敲一个音符。
/// 移植自源项目 `OpBeep`。
///
/// 参数：`(位置, 乐器, 音高)`；音高 0~24（源项目的魔数），乐器是 0 起的编号。
/// 消耗 `DUST_UNIT / 10`（1 千）—— 便宜到可以拿来做音乐。
///
/// ⚠️ **与源项目的差异**：MC 有 16 种音符盒乐器，泰拉没有音符盒。
/// 我们用手头真实存在的音效凑了 <see cref="InstrumentCount"/> 种
/// （竖琴 + 6 个吉他和弦 + 7 件鼓组）。所以「乐器」编号的合法范围比原版小，
/// 画同样图案时听到的音色不会和原版一一对应 —— 这是无法消除的差异，
/// 只能保证「同一个编号永远是同一种音色」。
///
/// 音高映射：源项目的 25 档正好是两个八度，泰拉的 `Pitch` 范围
/// 也是 -1.0（低一个八度）~ 1.0（高一个八度），所以直接线性映射，不损失音域。
/// </summary>
public sealed class OpBeep : SpellAction
{
    /// <summary>
    /// 泰拉侧实际能提供的乐器数。
    ///
    /// 源项目是 `NoteBlockInstrument.values().size`（16）。这里少一些，
    /// 因为泰拉没有对应的音效 —— 宁可少而真，不要多而假。
    /// </summary>
    public const int InstrumentCount = 14;

    /// <summary>音高上限（含）。源项目 `getPositiveIntUnderInclusive(2, 24)`。</summary>
    public const int MaxNote = 24;

    public override int Argc => 3;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        int instrument = CastingEnvironment.RequireIndex(args[1]);
        if (instrument < 0 || instrument >= InstrumentCount)
        {
            throw new MishapInvalidIota(args[1], $"0 ~ {InstrumentCount - 1} 之间的乐器编号");
        }

        int note = CastingEnvironment.RequireIndex(args[2]);
        if (note < 0 || note > MaxNote)
        {
            throw new MishapInvalidIota(args[2], $"0 ~ {MaxNote} 之间的音高");
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.Beep(x, y, instrument, note)),
            MediaConstants.DustUnit / 10,
            new[] { ParticleSpray.Cloud(x, y, spread: 1.0f, count: 6) });
    }

    /// <summary>音频类法术不播施法音效（源项目 `hasCastingSound = false`），否则会盖住音符本身。</summary>
    public override bool HasCastingSound(CastingEnvironment env) => false;
}

/// <summary>
/// `create_lava`：造出一格岩浆。
/// 移植自源项目 `OpCreateFluid` 的岩浆实例。
///
/// 消耗 `CRYSTAL_UNIT`（10 万）—— 是 `create_water`（1 万）的 **10 倍**，
/// 因为岩浆在泰拉里同样是要命的东西，这个价差是刻意的平衡，不要顺手改成一样。
/// </summary>
public sealed class OpCreateLava : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.CreateLavaAt(x, y)),
            MediaConstants.CrystalUnit,
            new[] { ParticleSpray.Burst(x, y, spread: 1.0f, count: 20) });
    }
}

/// <summary>
/// `edify`：把一棵树苗催成一棵树。
/// 移植自源项目 `OpEdifySapling`。
///
/// 消耗 `CRYSTAL_UNIT`（10 万）—— 比 `bonemeal`（1.125 万）贵近 9 倍。
/// 两者的区别不是数值而是**条件**：
///   - `bonemeal` 是「把骨粉撒在这个位置」，能长什么由游戏自己判断
///   - `edify` 是「**这一格必须是树苗**，把它催成树」，条件不满足直接报 mishap
///
/// 源项目的 `edify` 长的是模组自己的「阿卡夏树」，泰拉侧没有对应树种，
/// 所以改成催熟**任何**树苗 —— 语义从「召唤特定树」变成「加速已有树」。
/// 这是移植里少数几个「功能等价但产物不同」的点，已在计划里登记。
/// </summary>
public sealed class OpEdify : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();
        if (!world.IsSaplingAt(x, y))
        {
            throw new MishapBadBlock(x, y, "这里没有树苗");
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.GrowTreeAt(x, y)),
            MediaConstants.CrystalUnit,
            new[] { ParticleSpray.Burst(x, y - 2.0, spread: 0.1f, count: 100) });
    }
}

/// <summary>
/// `place_block`：把背包里的**可放置物品**放到指定格子上。
/// 移植自源项目 `OpPlaceBlock`。
///
/// 消耗 `DUST_UNIT / 8`（1250）—— 比 `conjure_block` 便宜得多，
/// 因为放的是**你自己的方块**，媒质只用来「代替手」。
///
/// 源项目要求那格 `canBeReplaced`；泰拉侧的对应判定是
/// <see cref="ICastingWorld.IsReplaceable"/>（空气/草/水这类）。
/// </summary>
public sealed class OpPlaceBlock : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();
        // 源项目：先找快捷栏物品（找不到 → MishapLackingHotbarItem），再看目标格
        //（这里曾经不检查：没东西可放时照扣媒质、什么也不放）
        if (!world.HasPlaceableInHotbar())
        {
            throw new MishapLackingHotbarItem("可放置的方块");
        }
        if (!world.IsReplaceable(x, y))
        {
            throw new MishapBadBlock(x, y, "这一格不是可替换的（要空气、草或水）");
        }

        // 放下去之后还要消耗背包里的物品 —— 由世界侧在**施放阶段**做，
        // 这样媒质不足时不会先把玩家的方块吃掉。
        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.PlaceBlockAt(x, y)),
            MediaConstants.DustUnit / 8,
            new[] { ParticleSpray.Cloud(x, y, spread: 1.0f, count: 8) });
    }
}

/// <summary>
/// `recharge`：从地上的媒质物品里抽媒质，装进**手上的可充能物品**（媒质瓶 / 打包法术）。
/// 移植自源项目 OpRecharge：只抽够填满的量（堆叠物品整件扣，多出的浪费），消耗 SHARD_UNIT。
///
/// ⚠️ 这里曾经把媒质抽进「玩家媒质池」—— 原版没有那个池子，已按原版改回。
/// </summary>
public sealed class OpRecharge : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var world = env.RequireWorld();

        if (entity.Target != EntityIota.EntityKind.Item)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        // 源项目：先找手上的可充能物品（还有空间的），找不到 → MishapBadOffhandItem("rechargable")
        long space = env.HeldRechargeSpace();
        if (space <= 0)
        {
            throw new MishapBadHeldItem();
        }

        if (world.ItemEntityMedia(entity, forBattery: false) <= 0)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w =>
            {
                long room = env.HeldRechargeSpace();
                if (room <= 0) return;
                long got = w.DrainItemEntity(entity, room, forBattery: false);
                if (got > 0) env.ChargeHeld(System.Math.Min(got, room));
            }),
            MediaConstants.ShardUnit,
            new[] { ParticleSpray.Burst(world.FeetPosition(entity).X, world.FeetPosition(entity).Y, spread: 0.5f, count: 20) });
    }
}

/// <summary>本组图案的注册。</summary>
public static class SimpleSpellActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:beep", new OpBeep());
        PatternRegistry.RegisterAction("hexcasting:create_lava", new OpCreateLava());
        PatternRegistry.RegisterAction("hexcasting:edify", new OpEdify());
        PatternRegistry.RegisterAction("hexcasting:place_block", new OpPlaceBlock());
        PatternRegistry.RegisterAction("hexcasting:recharge", new OpRecharge());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
