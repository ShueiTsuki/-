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

/// <summary>
/// `get_entity/*`：取**某个坐标上**的实体（最近的一个）。
/// 移植自源项目 selectors/OpGetEntityAt.kt。
///
/// 与 `zone_entity/*` 的区别：
///   - `zone_entity` 吃**半径**，返回**列表**
///   - `get_entity` 吃**坐标**，只返回**最近的一个**（或空）
///
/// 源项目用的判定盒是 `pos ± 0.5`（一个以该坐标为中心、边长 1 的立方体），
/// 而不是球体 —— 也就是「这一格上的实体」。
/// </summary>
public sealed class OpGetEntityAt : ConstMediaAction
{
    private readonly ZoneEntityFilter? _filter;   // null = 不筛选，任何实体

    public OpGetEntityAt(ZoneEntityFilter? filter) => _filter = filter;

    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "坐标");
        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();

        // 判定盒 pos±0.5：用半径 0.5 的圆近似。
        // 圆会漏掉方形四角，但实体中心几乎总落在格心附近，实际没有差别。
        var found = _filter == null
            ? world.QueryNearestEntity(x, y)
            : FirstOrNull(world.QueryEntities(_filter.Value, negate: false, x, y, 0.5));

        // 没有实体时返回 `NullIota`（源项目 `getOrNull(0).asActionResult`）
        return new Iota[] { found ?? (Iota)NullIota.Instance };
    }

    private static EntityIota? FirstOrNull(IReadOnlyList<EntityIota> list)
        => list.Count > 0 ? list[0] : null;
}

/// <summary>
/// `explode` 与 `explode/fire`：在指定位置制造爆炸。
/// 移植自源项目 spells/OpExplode.kt。
///
/// ⚠️ 源项目里有一处**看起来多余但必须保留**的处理：
/// 如果爆炸点恰好落在某个实体的**眼睛位置**，就把爆炸点向上挪 0.000001 ——
/// 源码注释写明理由是「防止爆炸正好在实体眼位时不造成伤害」的坑。
/// 少了这一下，贴脸爆炸会莫名其妙打不到人。
/// </summary>
public sealed class OpExplode : SpellAction
{
    private readonly bool _fire;

    public OpExplode(bool fire) => _fire = fire;

    public override int Argc => 2;

    /// <summary>强度上限。源项目用 `getPositiveDoubleUnderInclusive(1, 10.0)`。</summary>
    public const double MaxStrength = 10.0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "爆炸位置");

        // 源项目 getPositiveDoubleUnderInclusive(1, 10.0)：[0, 10] 闭区间（曾经把 0 和 10 都拒掉了）
        double strength = CastingEnvironment.RequirePositiveDoubleUnderInclusive(args[1], MaxStrength, $"0 到 {MaxStrength} 之间的数");

        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();

        // 眼位修正：见类型注释。先查这个点上有没有实体「贴脸」。
        if (world.HasEntityEyeExactlyAt(x, y))
        {
            y += 0.000001;
        }

        double clamped = System.Math.Clamp(strength, 0.0, MaxStrength);

        // 消耗 = 粉尘 × (3×强度 + (带火 ? 1.0 : 0.125))
        double costFactor = 3.0 * clamped + (_fire ? 1.0 : 0.125);
        long cost = (long)(MediaConstants.DustUnit * costFactor);

        return new SpellResult
        {
            Effect = new ExplodeSpell(x, y, clamped, _fire),
            Cost = cost,
            Particles = new[] { ParticleSpray.Burst(x, y, (float)clamped, 50) },
        };
    }

    private sealed class ExplodeSpell : IRenderedSpell
    {
        private readonly double _x;
        private readonly double _y;
        private readonly double _strength;
        private readonly bool _fire;

        public ExplodeSpell(double x, double y, double strength, bool fire)
        {
            _x = x;
            _y = y;
            _strength = strength;
            _fire = fire;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            env.World?.Explode(_x, _y, _strength, _fire);
            return null;
        }
    }
}

/// <summary>`get_entity/*` 与 `explode/*` 的注册。</summary>
public static class EntitySelectActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // get_entity：不筛选，任何实体
        PatternRegistry.RegisterAction("hexcasting:get_entity", new OpGetEntityAt(null));
        PatternRegistry.RegisterAction("hexcasting:get_entity/animal",
            new OpGetEntityAt(ZoneEntityFilter.Animal));
        PatternRegistry.RegisterAction("hexcasting:get_entity/monster",
            new OpGetEntityAt(ZoneEntityFilter.Monster));
        PatternRegistry.RegisterAction("hexcasting:get_entity/item",
            new OpGetEntityAt(ZoneEntityFilter.Item));
        PatternRegistry.RegisterAction("hexcasting:get_entity/player",
            new OpGetEntityAt(ZoneEntityFilter.Player));
        PatternRegistry.RegisterAction("hexcasting:get_entity/living",
            new OpGetEntityAt(ZoneEntityFilter.Living));

        PatternRegistry.RegisterAction("hexcasting:explode", new OpExplode(fire: false));
        PatternRegistry.RegisterAction("hexcasting:explode/fire", new OpExplode(fire: true));

        return PatternRegistry.RegisteredActionCount - before;
    }
}
