using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.World;

/// <summary>
/// 一个实体的判定箱，坐标单位：**图格**。
/// 由游戏侧填好后交给 <see cref="SegmentSweep"/>，于是扫掠逻辑可以离线测试。
/// </summary>
public readonly struct EntityBox
{
    public required EntityIota Entity { get; init; }

    public required double MinX { get; init; }
    public required double MinY { get; init; }
    public required double MaxX { get; init; }
    public required double MaxY { get; init; }
}

/// <summary>
/// 线段 vs 一组轴对齐判定箱的扫掠，返回**沿射线最近**的命中。
///
/// 移植自源项目 `OpEntityRaycast.getEntityHitResult`（MC 的 `AABB.clip` 循环）。
///
/// 源项目那段代码有几个绕的地方，这里按它的实际效果复刻：
///   - 起点的距离初值是「无穷远」，命中一个更近的就更新
///   - 起点已落在箱内时，该项距离记为 0（最近），直接锁定
///   - 取距离最小者，所以**射线会穿过多个实体时只会命中最近的那个**
/// </summary>
public static class SegmentSweep
{
    /// <summary>命中结果。</summary>
    public readonly struct Hit
    {
        public required EntityIota Entity { get; init; }

        /// <summary>命中点到射线起点的距离（图格）。起点在箱内时为 0。</summary>
        public required double Distance { get; init; }
    }

    /// <summary>
    /// 从 (originX, originY) 沿 (dirX, dirY) 扫掠，最远 <paramref name="maxDist"/> 格。
    /// 返回最近的命中；无命中返回 null。
    /// </summary>
    public static Hit? Cast(IReadOnlyList<EntityBox> boxes,
                            double originX, double originY,
                            double dirX, double dirY, double maxDist)
    {
        if (boxes == null) throw new ArgumentNullException(nameof(boxes));
        if (maxDist <= 0) return null;

        double len = System.Math.Sqrt(dirX * dirX + dirY * dirY);
        if (double.IsNaN(len) || double.IsInfinity(len) || len < 1e-9) return null;
        dirX /= len;
        dirY /= len;

        double endX = originX + dirX * maxDist;
        double endY = originY + dirY * maxDist;

        EntityIota? best = null;
        double bestDist = double.PositiveInfinity;

        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];

            // 起点已在箱内 —— 源项目里这一支把距离记为 0，除「更早的 0 距离命中」外必胜
            if (originX >= box.MinX && originX <= box.MaxX
                && originY >= box.MinY && originY <= box.MaxY)
            {
                if (bestDist > 0.0)
                {
                    best = box.Entity;
                    bestDist = 0.0;
                }
                continue;
            }

            // 线段与 AABB 求交（slab 法），拿进入参数 t ∈ [0,1]
            if (!TrySegmentBox(originX, originY, endX, endY, box, out double t))
            {
                continue;
            }

            double dist = t * maxDist;
            if (dist < bestDist || bestDist == 0.0)
            {
                // 与源项目一致：距离为 0 的命中一旦锁定就不被更长距离顶掉
                if (bestDist == 0.0) continue;
                best = box.Entity;
                bestDist = dist;
            }
        }

        return best == null
            ? null
            : new Hit { Entity = best, Distance = bestDist };
    }

    /// <summary>
    /// 线段 vs 轴对齐矩形：slab 法。
    /// 返回进入参数 t（0..1），无交返回 false。
    /// </summary>
    private static bool TrySegmentBox(double x0, double y0, double x1, double y1,
                                      in EntityBox box, out double t)
    {
        t = 0;

        double dx = x1 - x0;
        double dy = y1 - y0;

        double tMin = 0.0;
        double tMax = 1.0;

        // X 轴 slab
        if (!ClipAxis(x0, dx, box.MinX, box.MaxX, ref tMin, ref tMax)) return false;
        // Y 轴 slab
        if (!ClipAxis(y0, dy, box.MinY, box.MaxY, ref tMin, ref tMax)) return false;

        t = tMin;
        return true;
    }

    /// <summary>在单一轴上收紧 [tMin, tMax] 区间。</summary>
    private static bool ClipAxis(double origin, double delta, double min, double max,
                                 ref double tMin, ref double tMax)
    {
        // 该轴无位移：起点必须已落在区间内，否则整条线段都不可能相交
        if (System.Math.Abs(delta) < 1e-12)
        {
            return origin >= min && origin <= max;
        }

        double t1 = (min - origin) / delta;
        double t2 = (max - origin) / delta;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;

        return tMin <= tMax;
    }
}
