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

/// <summary>世界效果类图案的公共部分。</summary>
internal static class WorldSpell
{
    public static SpellResult Make(IRenderedSpell effect, long cost, IReadOnlyList<ParticleSpray>? particles = null)
        => new()
        {
            Effect = effect,
            Cost = cost,
            Particles = particles ?? System.Array.Empty<ParticleSpray>(),
        };

    /// <summary>只做一件事的法术（多数世界效果都是这样）。</summary>
    public sealed class Simple : IRenderedSpell
    {
        private readonly System.Action<ICastingWorld> _act;

        public Simple(System.Action<ICastingWorld> act) => _act = act;

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            if (env.World is { } w) _act(w);
            return null;
        }
    }
}

/// <summary>
/// `summon_rain` / `dispel_rain`：改变天气。
/// 移植自源项目 `OpWeather`。
///
/// 消耗：下雨 `CRYSTAL_UNIT`（10 万），放晴 `SHARD_UNIT`（5 万）—— 下雨更贵。
/// 持续时间是**随机**的（源项目：下雨 30~90 分钟、放晴 60~180 分钟）。
/// </summary>
public sealed class OpWeather : SpellAction
{
    private readonly bool _rain;

    public OpWeather(bool rain) => _rain = rain;

    public override int Argc => 0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目取随机时长（分钟）
        int minMinutes = _rain ? 30 : 60;
        int maxMinutes = _rain ? 90 : 180;

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.SetRain(_rain, minMinutes, maxMinutes)),
            _rain ? MediaConstants.CrystalUnit : MediaConstants.ShardUnit);
    }
}

/// <summary>
/// `ignite`：点燃一个实体**或**一个位置。
/// 移植自源项目 `OpIgnite`。
///
/// 参数是实体就烧它；是向量就烧那个位置。
///
/// 注意：**泰拉没有 MC 那样的「火焰方块」**，所以位置分支用
/// 「烧这一格附近的实体 + 撒火粒子」近似，而不是真的生成会蔓延的火。
/// </summary>
public sealed class OpIgnite : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        switch (args[0])
        {
            case EntityIota:
            {
                var entity = env.ResolveEntity(args[0]);
                return WorldSpell.Make(
                    new WorldSpell.Simple(w => w.IgniteEntity(entity)),
                    MediaConstants.DustUnit);
            }

            case VectorIota:
            {
                var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
                env.AssertVecInRange(x, y, z);
                return WorldSpell.Make(
                    new WorldSpell.Simple(w => w.IgniteAt(x, y)),
                    MediaConstants.DustUnit);
            }

            default:
                throw new MishapInvalidIota(args[0], InvalidValue.EntityOrVector);
        }
    }
}

/// <summary>
/// `extinguish`：扑灭一片区域的火焰。
/// 移植自源项目 `OpExtinguish`。
///
/// 消耗 `DUST × 6`（6 万），用**泛洪**扑灭，上限 1024 格
/// —— 源项目注释里自嘲「这是第几层借来的代码了」，
/// 因为它和 `destroy_water` 用的是同一套泛洪。
/// </summary>
public sealed class OpExtinguish : SpellAction
{
    /// <summary>最多扑灭多少格。源项目 `MAX_DESTROY_COUNT = 1024`。</summary>
    public const int MaxCount = 1024;

    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        env.AssertVecInRange(x, y, z);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.ExtinguishAt(x, y, MaxCount)),
            MediaConstants.DustUnit * 6);
    }
}

/// <summary>
/// `destroy_water`：抽干一片水域。
/// 移植自源项目 `OpDestroyFluid`。消耗 `2 × CRYSTAL_UNIT`（20 万），泛洪上限 1024 格。
/// </summary>
public sealed class OpDestroyWater : SpellAction
{
    public const int MaxCount = 1024;

    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        env.AssertVecInRange(x, y, z);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.DestroyWaterAt(x, y, MaxCount)),
            2 * MediaConstants.CrystalUnit,
            new[] { ParticleSpray.Burst(x, y, spread: 3.0f, count: 30) });
    }
}

/// <summary>
/// `create_water`：造出一格水。
/// 移植自源项目 `OpCreateFluid`。消耗 `DUST_UNIT`（1 万）。
/// </summary>
public sealed class OpCreateWater : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        env.AssertVecInRange(x, y, z);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.CreateWaterAt(x, y)),
            MediaConstants.DustUnit);
    }
}

/// <summary>
/// `lightning`：召下一道闪电。
/// 移植自源项目 `OpLightning`。消耗 `3 × SHARD_UNIT`（15 万）。
///
/// 泰拉有现成的天气闪电（`Main` 的闪电系统），直接复用它，
/// 再补一次范围伤害 —— 源项目的闪电本身就会伤害并点燃。
/// </summary>
public sealed class OpLightning : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        env.AssertVecInRange(x, y, z);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.SpawnLightning(x, y)),
            3 * MediaConstants.ShardUnit,
            new[] { ParticleSpray.Burst(x, y - 2.0, spread: 0.5f, count: 30) });
    }
}

/// <summary>
/// `bonemeal`：催熟。
/// 移植自源项目 `OpTheOnlyReasonAnyoneDownloadedPsi`
/// （那个类名是致敬 Psi 模组的同名法术，作者显然对「大家只为这一个功能装 Psi」很有意见）。
///
/// 消耗 `DUST × 1.125` = 11250。
/// </summary>
public sealed class OpBonemeal : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        env.AssertVecInRange(x, y, z);

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.ApplyBonemeal(x, y)),
            (long)(MediaConstants.DustUnit * 1.125));
    }
}

/// <summary>世界效果类图案的注册。</summary>
public static class WorldEffectActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:summon_rain", new OpWeather(rain: true));
        PatternRegistry.RegisterAction("hexcasting:dispel_rain", new OpWeather(rain: false));

        PatternRegistry.RegisterAction("hexcasting:ignite", new OpIgnite());
        PatternRegistry.RegisterAction("hexcasting:extinguish", new OpExtinguish());

        PatternRegistry.RegisterAction("hexcasting:create_water", new OpCreateWater());
        PatternRegistry.RegisterAction("hexcasting:destroy_water", new OpDestroyWater());

        PatternRegistry.RegisterAction("hexcasting:lightning", new OpLightning());
        PatternRegistry.RegisterAction("hexcasting:bonemeal", new OpBonemeal());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
