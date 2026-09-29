using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Canvas;

/// <summary>一个带颜色的顶点。颜色为 0xAARRGGBB（非预乘 alpha）。三个一组构成三角形。</summary>
public readonly struct ColoredVertex
{
    public readonly float X;
    public readonly float Y;
    public readonly uint Argb;

    public ColoredVertex(Vec2f p, uint argb)
    {
        X = p.X;
        Y = p.Y;
        Argb = argb;
    }
}

/// <summary>
/// 咒法学的图案线型，逐行移植 `client/render/RenderLib.kt`：
/// makeZappy / drawLineSeq（含拐角扇形与圆头）/ drawSpot / drawPatternFromPoints / findDupIndices。
///
/// 输出三角形列表，由客户端一次性提交给显卡。放在 Core 是为了离线可测。
///
/// 所有「线宽 / 点半径」参数都以原版 GUI 单位给出，再乘 <c>unit</c>（每 GUI 单位多少屏幕像素）。
/// 原版在 MC 的 GUI 坐标系里画（1080p 自动界面缩放 = 4），线宽 5 在屏幕上是 20 像素，
/// 约为格距的 0.3；以前这里直接按 5 像素画，线细了 4 倍，点也小了 4 倍。
/// </summary>
public static class PatternGeometry
{
    public const int Hops = 10;
    public const float Variance = 2.5f;
    public const float Speed = 0.1f;
    public const float DefaultReadabilityOffset = 0.2f;
    public const float DefaultLastSegmentLenProportion = 0.8f;
    private const float CapTheta = 180f / 10f;
    private const double Tau = System.Math.PI * 2.0;

    private static double GetNoise(double x, double y, double z)
        => SimplexNoise.Hex.GetValue(x * 0.6, y * 0.6, z * 0.6) / 2.0;

    // ── 颜色 ─────────────────────────────────────────────────────────

    public static int A(uint c) => (int)(c >> 24);
    public static int R(uint c) => (int)((c >> 16) & 0xFF);
    public static int G(uint c) => (int)((c >> 8) & 0xFF);
    public static int B(uint c) => (int)(c & 0xFF);
    public static uint Argb(int a, int r, int g, int b)
        => ((uint)(a & 0xFF) << 24) | ((uint)(r & 0xFF) << 16) | ((uint)(g & 0xFF) << 8) | (uint)(b & 0xFF);

    /// <summary>原版 screenCol：各通道 (n + 255) / 2，往白色提亮一半。</summary>
    public static uint ScreenCol(uint c) => Argb(A(c), (R(c) + 255) / 2, (G(c) + 255) / 2, (B(c) + 255) / 2);

    private static float Lerp(float t, float a, float b) => a + (b - a) * t;

    // ── makeZappy ────────────────────────────────────────────────────

    /// <summary>原版 findDupIndices：出现不止一次的点的所有下标。</summary>
    public static HashSet<int> FindDupIndices<T>(IReadOnlyList<T> pts) where T : notnull
    {
        var dedup = new Dictionary<T, int>();
        var found = new HashSet<int>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (dedup.TryGetValue(pts[i], out int ix))
            {
                found.Add(i);
                found.Add(ix);
            }
            else
            {
                dedup[pts[i]] = i;
            }
        }
        return found;
    }

    /// <summary>原版 makeZappy：把折线细分并加上随时间流动的电光抖动。</summary>
    public static List<Vec2f> MakeZappy(
        IReadOnlyList<Vec2f> barePoints, ISet<int>? dupIndices, int hops, float variance, float speed,
        float flowIrregular, float readabilityOffset, float lastSegmentLenProportion, double seed, double time)
    {
        var result = new List<Vec2f>();
        if (barePoints.Count == 0) return result;

        List<Vec2f> Zappify(List<Vec2f> points, bool truncateLast)
        {
            double zSeed = time * speed;
            var zappyPts = new List<Vec2f>(points.Count * hops) { points[0] };
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var src = points[i];
                var target = points[i + 1];
                var delta = target - src;
                float hopDist = delta.Length / hops;
                float maxVariance = hopDist * variance;

                int maxJ = truncateLast && i == points.Count - 2
                    ? (int)System.Math.Round(lastSegmentLenProportion * hops, System.MidpointRounding.AwayFromZero)
                    : hops;

                for (int j = 1; j <= maxJ; j++)
                {
                    double progress = j / (double)(hops + 1);
                    var pos = src + delta * (float)progress;
                    double minorPerturb = GetNoise(i, j, System.Math.Sin(zSeed)) * flowIrregular;
                    float theta = (float)(3 * GetNoise(i + progress + minorPerturb - zSeed, 1337.0, seed) * Tau);
                    double scaleVariance = System.Math.Min(1.0, 8 * (0.5 - System.Math.Abs(0.5 - progress)));
                    float r = (float)(GetNoise(i + progress - zSeed, 69420.0, seed) * maxVariance * scaleVariance);
                    zappyPts.Add(pos + new Vec2f(r * System.MathF.Cos(theta), r * System.MathF.Sin(theta)));

                    // 只有走满 hops 才补上终点；被截断的最后一段故意停在半路（原版行为）
                    if (j == hops) zappyPts.Add(target);
                }
            }
            return zappyPts;
        }

        if (dupIndices is null) return Zappify(new List<Vec2f>(barePoints), true);

        var daisyChain = new List<Vec2f>();
        for (int i = 0; i + 1 < barePoints.Count; i++)
        {
            var head = barePoints[i];
            var tail = barePoints[i + 1];
            var tangent = (tail - head) * readabilityOffset;
            daisyChain.Add(i != 0 && dupIndices.Contains(i) ? head + tangent : head);

            if (i == barePoints.Count - 2)
            {
                daisyChain.Add(tail);
                result.AddRange(Zappify(daisyChain, true));
            }
            else if (dupIndices.Contains(i + 1))
            {
                daisyChain.Add(tail - tangent);
                result.AddRange(Zappify(daisyChain, false));
                daisyChain.Clear();
            }
        }
        return result;
    }

    // ── drawLineSeq ──────────────────────────────────────────────────

    private static Vec2f Rotate(Vec2f v, float theta)
    {
        float c = System.MathF.Cos(theta), s = System.MathF.Sin(theta);
        return new Vec2f(v.X * c - v.Y * s, v.Y * c + v.X * s);
    }

    private static void Tri(List<ColoredVertex> o, Vec2f a, uint ca, Vec2f b, uint cb, Vec2f c, uint cc)
    {
        o.Add(new ColoredVertex(a, ca));
        o.Add(new ColoredVertex(b, cb));
        o.Add(new ColoredVertex(c, cc));
    }

    /// <summary>
    /// 原版 drawLineSeq：沿折线画一条带颜色渐变的粗线，拐角用扇形补齐，两端画半圆头。
    /// <paramref name="showStrokeOrder"/> 为 false 时整条线只用 tail 色（原版默认；按住 Ctrl 才显示笔顺渐变）。
    /// </summary>
    public static void LineSeq(List<ColoredVertex> o, IReadOnlyList<Vec2f> points, float width,
        uint tail, uint head, bool showStrokeOrder)
    {
        if (points.Count <= 1) return;

        float r1 = R(tail), g1 = G(tail), b1 = B(tail), a1 = A(tail);
        uint headSource = showStrokeOrder ? head : tail;
        float r2 = R(headSource), g2 = G(headSource), b2 = B(headSource), a2 = A(headSource);

        int n = points.Count;
        var joinAngles = new float[n];
        var joinOffsets = new float[n];
        for (int i = 2; i < n; i++)
        {
            var p0 = points[i - 2];
            var p1 = points[i - 1];
            var p2 = points[i];
            var prev = p1 - p0;
            var next = p2 - p1;
            float angle = System.MathF.Atan2(prev.X * next.Y - prev.Y * next.X, prev.X * next.X + prev.Y * next.Y);
            joinAngles[i - 1] = angle;
            float clamp = System.Math.Min(prev.Length, next.Length) / (width * 0.5f);
            joinOffsets[i - 1] = System.Math.Clamp(System.MathF.Sin(angle) / (1 + System.MathF.Cos(angle)), -clamp, clamp);
        }

        uint Color(float time) => Argb((int)Lerp(time, a1, a2), (int)Lerp(time, r1, r2),
                                       (int)Lerp(time, g1, g2), (int)Lerp(time, b1, b2));

        for (int i = 0; i < n - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            var tangent = (p2 - p1).Normalized() * (width * 0.5f);
            var normal = new Vec2f(-tangent.Y, tangent.X);
            var negNormal = normal * -1f;

            uint color1 = Color(i / (float)n);
            uint color2 = Color((i + 1f) / n);
            float jlow = joinOffsets[i];
            float jhigh = joinOffsets[i + 1];

            var p1Down = p1 + tangent * System.Math.Max(0f, jlow) + normal;
            var p1Up = p1 + tangent * System.Math.Max(0f, -jlow) + negNormal;
            var p2Down = p2 - tangent * System.Math.Max(0f, jhigh) + normal;
            var p2Up = p2 - tangent * System.Math.Max(0f, -jhigh) + negNormal;

            Tri(o, p1Down, color1, p1, color1, p1Up, color1);
            Tri(o, p1Down, color1, p1Up, color1, p2Up, color2);
            Tri(o, p1Down, color1, p2Up, color2, p2, color2);
            Tri(o, p1Down, color1, p2, color2, p2Down, color2);

            if (i > 0)
            {
                // 与上一段之间的扇形接头
                float sangle = joinAngles[i];
                float angle = System.Math.Abs(sangle);
                var rnormal = negNormal;
                int joinSteps = (int)System.MathF.Ceiling(angle * 180 / (CapTheta * System.MathF.PI));
                if (joinSteps < 1) continue;

                if (sangle < 0)
                {
                    var prevVert = new Vec2f(p1.X - rnormal.X, p1.Y - rnormal.Y);
                    for (int j = 1; j <= joinSteps; j++)
                    {
                        var fan = Rotate(rnormal, -sangle * (j / (float)joinSteps));
                        var fanShift = new Vec2f(p1.X - fan.X, p1.Y - fan.Y);
                        Tri(o, p1, color1, prevVert, color1, fanShift, color1);
                        prevVert = fanShift;
                    }
                }
                else
                {
                    var startFan = Rotate(normal, -sangle);
                    var prevVert = new Vec2f(p1.X - startFan.X, p1.Y - startFan.Y);
                    for (int j = joinSteps - 1; j >= 0; j--)
                    {
                        var fan = Rotate(normal, -sangle * (j / (float)joinSteps));
                        var fanShift = new Vec2f(p1.X - fan.X, p1.Y - fan.Y);
                        Tri(o, p1, color1, prevVert, color1, fanShift, color1);
                        prevVert = fanShift;
                    }
                }
            }
        }

        void Cap(uint color, Vec2f point, Vec2f prev)
        {
            var tangent = (point - prev).Normalized() * (0.5f * width);
            var normal = new Vec2f(-tangent.Y, tangent.X);
            int steps = (int)System.MathF.Ceiling(180f / CapTheta);
            Vec2f? last = null;
            for (int j = steps; j >= 0; j--)
            {
                var fan = Rotate(normal, -System.MathF.PI * (j / (float)steps));
                var v = new Vec2f(point.X + fan.X, point.Y + fan.Y);
                if (last is { } l) Tri(o, point, color, l, color, v, color);
                last = v;
            }
        }
        Cap(Argb((int)a1, (int)r1, (int)g1, (int)b1), points[0], points[1]);
        Cap(Argb((int)a2, (int)r2, (int)g2, (int)b2), points[n - 1], points[n - 2]);
    }

    /// <summary>原版 drawSpot：六边形小点（原注释："yes they are gonna be little hexagons fite me"）。</summary>
    public static void Spot(List<ColoredVertex> o, Vec2f point, float radius, uint color)
    {
        if (radius <= 0f || A(color) == 0) return;
        Vec2f? last = null;
        for (int i = 0; i <= 6; i++)
        {
            float theta = i / 6f * (float)Tau;
            var v = new Vec2f(System.MathF.Cos(theta) * radius + point.X, System.MathF.Sin(theta) * radius + point.Y);
            if (last is { } l) Tri(o, point, color, l, color, v, color);
            last = v;
        }
    }

    /// <summary>
    /// 原版 drawPatternFromPoints。
    /// <paramref name="unit"/> = 每个原版 GUI 单位对应的屏幕像素（线宽 5 / 2、节点半径 2 都乘它）。
    /// </summary>
    public static void PatternFromPoints(List<ColoredVertex> o, IReadOnlyList<Vec2f> points, ISet<int>? dupIndices,
        bool drawLast, uint tail, uint head, float flowIrregular, float readabilityOffset,
        float lastSegmentLenProportion, double seed, double time, float unit, bool showStrokeOrder,
        float variance = Variance)
    {
        if (points.Count == 0) return;
        var zappy = MakeZappy(points, dupIndices, Hops, variance, Speed, flowIrregular, readabilityOffset,
            lastSegmentLenProportion, seed, time);
        LineSeq(o, zappy, 5f * unit, tail, head, showStrokeOrder);
        LineSeq(o, zappy, 2f * unit, ScreenCol(tail), ScreenCol(head), showStrokeOrder);

        // 节点：原版 dodge() = 通道 × 0.9，用 head 色
        uint node = Argb(A(head), (int)(R(head) * 0.9f), (int)(G(head) * 0.9f), (int)(B(head) * 0.9f));
        int count = drawLast ? points.Count : points.Count - 1;
        for (int i = 0; i < count; i++) Spot(o, points[i], 2f * unit, node);
    }

    /// <summary>原版 ResolvedPatternType 的 (color, fadeColor, success)。</summary>
    public static (uint Color, uint Fade, bool Success) ResolvedColors(Casting.Eval.ResolvedPatternType t) => t switch
    {
        Casting.Eval.ResolvedPatternType.Evaluated => (0x7385de, 0xfecbe6, true),
        Casting.Eval.ResolvedPatternType.Escaped => (0xddcc73, 0xfffae5, true),
        Casting.Eval.ResolvedPatternType.Undone => (0xb26b6b, 0xcca88e, true),
        Casting.Eval.ResolvedPatternType.Errored => (0xde6262, 0xffc7a0, false),
        Casting.Eval.ResolvedPatternType.Invalid => (0xb26b6b, 0xcca88e, false),
        _ => (0x7f7f7f, 0xcccccc, false),
    };

    /// <summary>正在画的图案：0xff64c8ff → 0xfffecbe6（GuiSpellcasting.render）。</summary>
    public const uint WipTail = 0xff64c8ff;
    public const uint WipHead = 0xfffecbe6;
}
