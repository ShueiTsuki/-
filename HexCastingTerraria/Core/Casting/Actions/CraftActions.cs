using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

// 源项目 `spells/OpMakePackagedSpell.kt` / `OpMakeBattery.kt` / `OpCycleVariant.kt`。
//
// 这一组是「把咒术装进物品」的图案：
//   你需要**手上拿着一个空的容器**，地上**放着一份媒质**，
//   图案把地上的媒质抽出来灌进容器里 —— 于是得到一个能反复施放的法术道具。
//
// 为什么要有这一组：法杖每次施法都要现场画图案。
// 打包法术让你把画好的图案**存成物品**，之后右键就能直接放 ——
// 这是原版从「手动施法」走向「工程化」的关键一步。

/// <summary>
/// `craft/cypher` / `craft/trinket` / `craft/artifact`：
/// 把一串图案与地上媒质物品里的媒质一起封进手持的空容器。
/// 移植自源项目 `OpMakePackagedSpell`。
///
/// 消耗：符纸 `CRYSTAL_UNIT`、饰品 `5 × CRYSTAL_UNIT`、法器 `10 × CRYSTAL_UNIT`。
/// 差价对应**容量与可重复性**，不是随手定的。
/// </summary>
public sealed class OpMakePackagedSpell : SpellAction
{
    private readonly PackagedSpellKind _kind;

    public OpMakePackagedSpell(PackagedSpellKind kind) => _kind = kind;

    public override int Argc => 2;

    /// <summary>各档的媒质消耗。数值取自源项目 `HexActions` 的注册处。</summary>
    public static long CostFor(PackagedSpellKind kind) => kind switch
    {
        PackagedSpellKind.Cypher => MediaConstants.CrystalUnit,
        PackagedSpellKind.Trinket => 5 * MediaConstants.CrystalUnit,
        _ => 10 * MediaConstants.CrystalUnit,
    };

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        if (entity.Target != EntityIota.EntityKind.Item)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        if (args[1] is not ListIota list)
        {
            throw new MishapInvalidIota(args[1], "图案列表");
        }

        // 列表里必须全是图案：源项目写入时也只接受图案
        // （`ItemPackagedHex.writeHex` 存的就是图案列表）。
        for (int i = 0; i < list.Count; i++)
        {
            if (list.Items[i] is not PatternIota)
            {
                throw new MishapInvalidIota(list.Items[i], "图案");
            }
        }

        if (env.HeldEmptyPackagedSpell != _kind)
        {
            throw new MishapBadHeldItem();
        }

        var world = env.RequireWorld();
        if (world.ExtractMediaFromItem(entity, simulate: true) <= 0)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        var patterns = list.Items;
        // 源项目 OpMakePackagedSpell：getTrueNameFromArgs(patterns, caster)
        foreach (var p in patterns)
        {
            MishapOthersName.ThrowIfTrueName(p, env.World?.Caster, allowSelf: true);
        }
        return WorldSpell.Make(
            new WorldSpell.Simple(w =>
            {
                long media = w.ExtractMediaFromItem(entity, simulate: false);
                if (media > 0)
                {
                    env.FillHeldPackagedSpell(patterns, media);
                }
            }),
            CostFor(_kind),
            new[] { ParticleSpray.Burst(world.FeetPosition(entity).X, world.FeetPosition(entity).Y, spread: 0.5f, count: 20) });
    }
}

/// <summary>
/// `craft/battery`：把地上媒质物品里的媒质装进手持的**空瓶**，做成媒质瓶。
/// 移植自源项目 `OpMakeBattery`。
///
/// 消耗 `CRYSTAL_UNIT`（10 万）—— 与符纸同价。
/// 源项目要求瓶子**恰好 1 个**（`handStack.count != 1` 直接报错），
/// 因为一次只做一个；泰拉的 `ItemID.Bottle` 可堆叠，所以这里也照抄这条限制。
/// </summary>
public sealed class OpMakeBattery : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        if (entity.Target != EntityIota.EntityKind.Item)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        if (!env.IsHeldPhialBase())
        {
            throw new MishapBadHeldItem();
        }

        var world = env.RequireWorld();
        if (world.ExtractMediaFromItem(entity, simulate: true) <= 0)
        {
            throw new MishapBadItem(entity, "装着媒质的掉落物");
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w =>
            {
                long media = w.ExtractMediaFromItem(entity, simulate: false);
                if (media > 0)
                {
                    env.CraftBatteryHeld(media);
                }
            }),
            MediaConstants.CrystalUnit,
            new[] { ParticleSpray.Burst(world.FeetPosition(entity).X, world.FeetPosition(entity).Y, spread: 0.5f, count: 20) });
    }
}

/// <summary>
/// `cycle_variant`：把手持物品的「变体」推进一格。
/// 移植自源项目 `OpCycleVariant`。
///
/// 消耗 `DUST_UNIT / 10`（1 千）。
/// 原版里只有打包法术那三件东西有变体（决定贴图/模型的变化），
/// 所以这个图案的实际用途是「把符纸换成另一种花纹」。
///
/// ⚠️ 泰拉侧目前的变体**只影响 tooltip 上显示的编号**，贴图还没做 ——
/// 这属于「美术最后统一处理」的欠账，机制本身是完整的
/// （`variant = (variant + 1) % numVariants`，与源项目逐字一致）。
/// </summary>
public sealed class OpCycleVariant : SpellAction
{
    public override int Argc => 0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 求值阶段只**检查**：真正推进变体要等媒质扣完（否则媒质不够时变体已经转了）
        if (!env.HeldHasVariants())
        {
            throw new MishapBadHeldItem();
        }

        return EnvSpell.Make(
            new EnvSpell.Simple(castEnv => castEnv.CycleHeldVariant()),
            MediaConstants.DustUnit / 10);
    }
}

/// <summary>打包法术与媒质瓶图案的注册。</summary>
public static class CraftActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:craft/cypher",
            new OpMakePackagedSpell(PackagedSpellKind.Cypher));
        PatternRegistry.RegisterAction("hexcasting:craft/trinket",
            new OpMakePackagedSpell(PackagedSpellKind.Trinket));
        PatternRegistry.RegisterAction("hexcasting:craft/artifact",
            new OpMakePackagedSpell(PackagedSpellKind.Artifact));
        PatternRegistry.RegisterAction("hexcasting:craft/battery", new OpMakeBattery());

        PatternRegistry.RegisterAction("hexcasting:cycle_variant", new OpCycleVariant());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
