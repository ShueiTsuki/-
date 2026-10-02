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
    /// 注意：单位是**图格**不是像素 —— 泰拉 1 图格 = 16 像素，
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
    /// <summary>
    /// 取起点与方向并校验起点在范围内。
    /// <paramref name="distance"/>：射线在 xy 平面上的投影长度 —— 源项目射线沿三维方向走 32 格
    ///（raycastEnd = origin + look.normalize() × 32），世界沿 z 无限延伸，碰撞只看 xy 投影，
    /// 所以方向带 z 分量时投影会变短；纯 z 方向投影为 0，打不中任何东西。
    /// </summary>
    public static void ReadArgs(IReadOnlyList<Iota> args, CastingEnvironment env,
                                out double ox, out double oy, out double lx, out double ly, out double distance)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0]);
        var (dx, dy, dz) = CastingEnvironment.RequireVec3(args[1]);
        env.AssertVecInRange(x, y, z);
        (ox, oy, lx, ly) = (x, y, dx, dy);
        double len3 = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double lenXy = System.Math.Sqrt(dx * dx + dy * dy);
        // MC 的 normalize：长度 < 1e-4 视为零向量（射线长度为 0，必然落空）
        distance = len3 < 1e-4 || lenXy < 1e-9 ? 0.0 : DistanceTiles * lenXy / len3;
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
/// 注意：返回的是**图格中心**而不是命中点 —— 源项目特意这么做的，注释写着：
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
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly, out double dist);
        if (dist <= 0) return RaycastCommon.Null();

        var hit = TileRaycast.Cast(world.IsTileSolid, ox, oy, lx, ly, dist);
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
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly, out double dist);
        if (dist <= 0) return RaycastCommon.Null();

        var hit = TileRaycast.Cast(world.IsTileSolid, ox, oy, lx, ly, dist);
        if (hit == null) return RaycastCommon.Null();

        var h = hit.Value;
        if (!world.IsVecInRange(h.TileX + 0.5, h.TileY + 0.5)) return RaycastCommon.Null();

        // 起点就在实心格里（TileRaycast 给的法线是零）：MC VoxelShape.clip 这时给的面是
        // Direction.getNearest(射线方向).getOpposite() —— 和射线方向最接近的轴的反方向（含 z）。
        // 这里曾经返回零向量，和原版对拍时发现（2026-10-02）。
        if (h.NormalX == 0 && h.NormalY == 0)
        {
            var (dx, dy, dz) = CastingEnvironment.RequireVec3(args[1]);
            var (nx, ny, nz) = OpCoerceToAxial.NearestDirection(dx, dy, dz);
            return new Iota[] { new VectorIota(Negate(nx), Negate(ny), Negate(nz)) };
        }

        // 源项目：blockHitResult.direction.step() —— 面法线
        return new Iota[] { new VectorIota(h.NormalX, h.NormalY) };
    }

    /// <summary>取反但不出 -0（原版的法线是整数方向，没有 -0；显示向量时 -0 会写成「-0」）。</summary>
    private static double Negate(double v) => v == 0 ? 0 : -v;
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
        RaycastCommon.ReadArgs(args, env, out double ox, out double oy, out double lx, out double ly, out double dist);
        if (dist <= 0) return RaycastCommon.Null();

        if (!RaycastCommon.TryNormalize(lx, ly, out double nx, out double ny))
        {
            return RaycastCommon.Null();
        }

        double endX = ox + nx * dist;
        double endY = oy + ny * dist;

        // 候选集：射线的包围盒。Core 层不知道世界里有谁，由世界侧枚举。
        var boxes = world.EntitiesInArea(
            System.Math.Min(ox, endX), System.Math.Min(oy, endY),
            System.Math.Max(ox, endX), System.Math.Max(oy, endY));

        var hit = SegmentSweep.Cast(boxes, ox, oy, lx, ly, dist);
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
