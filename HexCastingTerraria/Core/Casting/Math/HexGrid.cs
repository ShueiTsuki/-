using System;
using System.Collections.Generic;

namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 六边形网格的像素换算与遍历。
/// 移植自 at.petrak.hexcasting.api.utils.HexUtils（coordToPx / pxToCoord）
/// 与 HexCoord.kt 的 rangeAround。
///
/// 几何约定（flat-top 六边形，尖角朝左右）：
///   相邻格点中心距离 = size
///   x = √3·q + (√3/2)·r
///   y = 1.5·r
///
/// ⚠️ 本文件**必须保持不依赖 XNA / tModLoader**（因此用 <see cref="Vec2f"/> 而不是 `Vector2`）。
/// 原因：坐标换算是画布、射线、瞄准预览共用的底座，错一点就会「预览指着一个地方、
/// 真正施法打到另一个地方」，而且两边都不报错。它必须留在离线测试工程里，
/// 而且测的必须是**这份真代码**，不是手抄的副本。
///
/// 注意：本文件所在命名空间以 .Math 结尾，所以调用 BCL 数学函数必须写 System.Math，
/// 否则会被解析成命名空间 HexCastingTerraria.Core.Casting.Math。
/// </summary>
public static class HexGrid
{
    public const float Sqrt3 = 1.7320508f;

    /// <summary>格点坐标 → 像素坐标。</summary>
    public static Vec2f CoordToPx(HexCoord coord, float size, Vec2f offset)
    {
        float x = Sqrt3 * coord.X + Sqrt3 / 2f * coord.Y;
        float y = 1.5f * coord.Y;
        return new Vec2f(x * size + offset.X, y * size + offset.Y);
    }

    /// <summary>
    /// 像素坐标 → 最近的格点坐标。
    /// 源实现用「取整后看哪个轴残差更大」的方式做立方坐标取整，这里逐行对齐。
    /// </summary>
    public static HexCoord PxToCoord(Vec2f px, float size, Vec2f offset)
    {
        float offsettedX = px.X - offset.X;
        float offsettedY = px.Y - offset.Y;
        float qf = (Sqrt3 / 3f * offsettedX - 0.33333f * offsettedY) / size;
        float rf = (0.66666f * offsettedY) / size;

        int q = (int)System.MathF.Round(qf);
        int r = (int)System.MathF.Round(rf);
        qf -= q;
        rf -= r;

        return System.Math.Abs(q) >= System.Math.Abs(r)
            ? new HexCoord(q + (int)System.MathF.Round(qf + 0.5f * rf), r)
            : new HexCoord(q, r + (int)System.MathF.Round(rf + 0.5f * qf));
    }

    /// <summary>
    /// 画布格点间距。对应源项目 GuiSpellcasting.hexSize()：
    ///   baseScale = √(width × height / 512)
    ///   实际 = baseScale / zoom
    /// 其中 512 是原作给的「约 512 格面积」预算。
    /// </summary>
    public static float HexSize(float width, float height, float zoom = 1f)
    {
        if (zoom <= 0f)
        {
            zoom = 1f;
        }
        float baseScale = System.MathF.Sqrt(width * height / 512f);
        return baseScale / zoom;
    }

    /// <summary>以某点为中心、半径 radius 内的全部格点（含中心）。对应 rangeAround。</summary>
    public static IEnumerable<HexCoord> RangeAround(HexCoord center, int radius)
    {
        for (int dq = -radius; dq <= radius; dq++)
        {
            int rMin = System.Math.Max(-radius, -dq - radius);
            int rMax = System.Math.Min(radius, -dq + radius);
            for (int dr = rMin; dr <= rMax; dr++)
            {
                yield return new HexCoord(center.X + dq, center.Y + dr);
            }
        }
    }

    /// <summary>两格点间的六边形距离。</summary>
    public static int Distance(HexCoord a, HexCoord b)
    {
        int dq = a.X - b.X;
        int dr = a.Y - b.Y;
        int ds = -(dq + dr);
        return (System.Math.Abs(dq) + System.Math.Abs(dr) + System.Math.Abs(ds)) / 2;
    }

    /// <summary>图案线段在像素空间中的端点序列（用于绘制）。</summary>
    public static List<Vec2f> PatternLinePoints(HexPattern pattern, HexCoord origin, float size, Vec2f offset)
    {
        var positions = pattern.Positions(origin);
        var outList = new List<Vec2f>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            outList.Add(CoordToPx(positions[i], size, offset));
        }
        return outList;
    }
}
