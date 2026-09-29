using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.World;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// 三个射线图案的共同部分。
/// 移植自源项目 `Action.raycastEnd` 与三个 Op*Raycast 的公共逻辑。
/// </summary>
internal static class RaycastCommon
{
    /// <summary>
    /// 射线最大长度。源项目 `Action.RAYCAST_DISTANCE = 32.0`（单位：格）。
    /// ⚠️ 单位是**图格**不是像素 —— 泰拉 1 图格 = 16 像素，
    /// 直接抄成 32 像素会变成只能打 2 格。
    /// </summary>
    public const double DistanceTiles = HexUnits.RaycastDistanceTiles;

    /// <summary>
    /// 每个射线图案的媒质消耗。
    /// 源项目 `MediaConstants.DUST_UNIT / 100` = 10000 / 100 = **100**。
    /// 很便宜是刻意的：射线是基础查询，玩家会高频使用。
    /// </summary>
    public const long MediaCost = MediaConstants.DustUnit / 100;

    /// <summary>取两个向量参数并校验起点在范围内。任一失败即抛 mishap。</summary>
    public static void ReadArgs(IReadOnlyList<Iota> args, CastingEnvironment env,
                                out double ox, out double oy, out double lx, out double ly)
    {
        (ox, oy) = CastingEnvironment.RequireVec(args[0], "射线起点");
        (lx, ly) = CastingEnvironment.RequireVec(args[1], "射线方向");
        env.AssertVecInRange(ox, oy);
    }

    /// <summary>
    /// 归一化方向。退化方向返回 false —— **不能**用 `SafeNormalize` 兜底成 (1,0)，
    /// 那会把「玩家给了一个零向量」悄悄变成「向右打 32 格」，可能命中他根本没指的方块。
    /// 源项目此处等价于 miss。
    /// </summary>
    public static bool TryNormalize(double x, double y, out double nx, out double ny)
    {
        nx = ny = 0;
        double len = System.Math.Sqrt(x * x + y * y);
        if (double.IsNaN(len) || double.IsInfinity(len) || len < 1e-9) return false;
        nx = x / len;
        ny = y / len;
        return true;
    }

    public static IReadOnlyList<Iota> Null() => new Iota[] { NullIota.Instance };
}

/// <summary>
/// `raycast`：从起点沿方向打射线，返回命中的**图格中心**坐标；没打中返回 NullIota。
/// 移植自源项目 raycast/OpBlockRaycast.kt。
///
/// ⚠️ 返回的是**图格中心**而不是命中点 —— 源项目特意这么做的，注释写着：
/// 命中点在方块**外表面**，拿它去 `break_block` 会挖到隔壁那一格。
/// 所以宁可丢掉「精确命中点」也要返回图格中心。
/// </summary>
public sealed class OpBlockRaycast : ConstMediaAction
{
    public override int Argc => 2;

    public override ActionTypes Types => ActionTypes.Of(IotaTypes.Vec, IotaTypes.Vec, IotaTypes.Vec);

    public override long MediaCost => RaycastCommon.MediaCost;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var world = env.RequireWorld();
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly);

        var hit = TileRaycast.Cast(world.IsTileSolid, ox, oy, lx, ly, RaycastCommon.DistanceTiles);
        if (hit == null) return RaycastCommon.Null();

        var h = hit.Value;

        // 命中点也要在范围内 —— 源项目同样检查（否则可以站在边缘探测远处地形）
        if (!world.IsVecInRange(h.TileX + 0.5, h.TileY + 0.5)) return RaycastCommon.Null();

        return new Iota[] { new VectorIota(h.TileX + 0.5, h.TileY + 0.5) };
    }
}

/// <summary>
/// `raycast/axis`：返回命中面的**法线**；没打中返回 NullIota。
/// 移植自源项目 raycast/OpBlockAxisRaycast.kt。
///
/// 用法：法线就是「从命中面往外走一格」的方向，
/// 拿命中点 + 法线即可得到「贴着墙面的那一格」，是放置方块类法术的基础。
/// </summary>
public sealed class OpBlockAxisRaycast : ConstMediaAction
{
    public override int Argc => 2;

    public override long MediaCost => RaycastCommon.MediaCost;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var world = env.RequireWorld();
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly);

        var hit = TileRaycast.Cast(world.IsTileSolid, ox, oy, lx, ly, RaycastCommon.DistanceTiles);
        if (hit == null) return RaycastCommon.Null();

        var h = hit.Value;
        if (!world.IsVecInRange(h.TileX + 0.5, h.TileY + 0.5)) return RaycastCommon.Null();

        // 源项目：blockHitResult.direction.step() —— 面法线
        return new Iota[] { new VectorIota(h.NormalX, h.NormalY) };
    }
}

/// <summary>
/// `raycast/entity`：返回射线命中的**实体**；没打中或命中者超范围返回 NullIota。
/// 移植自源项目 raycast/OpEntityRaycast.kt。
/// </summary>
public sealed class OpEntityRaycast : ConstMediaAction
{
    public override int Argc => 2;

    public override long MediaCost => RaycastCommon.MediaCost;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var world = env.RequireWorld();
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly);

        if (!RaycastCommon.TryNormalize(lx, ly, out double nx, out double ny))
        {
            return RaycastCommon.Null();
        }

        double endX = ox + nx * RaycastCommon.DistanceTiles;
        double endY = oy + ny * RaycastCommon.DistanceTiles;

        // 候选集：射线的包围盒。Core 层不知道世界里有谁，由世界侧枚举。
        var boxes = world.EntitiesInArea(
            System.Math.Min(ox, endX), System.Math.Min(oy, endY),
            System.Math.Max(ox, endX), System.Math.Max(oy, endY));

        var hit = SegmentSweep.Cast(boxes, ox, oy, lx, ly, RaycastCommon.DistanceTiles);
        if (hit == null) return RaycastCommon.Null();

        // 命中实体也必须仍在范围内（源项目：env.isEntityInRange）
        if (!world.IsInRange(hit.Value.Entity)) return RaycastCommon.Null();

        return new Iota[] { hit.Value.Entity };
    }
}

/// <summary>射线类图案的注册。对应 HexActions.java 的 RAYCAST 段。</summary>
public static class RaycastActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:raycast", new OpBlockRaycast());
        PatternRegistry.RegisterAction("hexcasting:raycast/axis", new OpBlockAxisRaycast());
        PatternRegistry.RegisterAction("hexcasting:raycast/entity", new OpEntityRaycast());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
