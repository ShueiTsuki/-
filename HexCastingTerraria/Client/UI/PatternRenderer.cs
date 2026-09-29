using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 图案渲染：忠实移植源项目的「zappy（电光抖动）」线型。
///
/// 源：at.petrak.hexcasting.client.render.RenderLib.kt 的
///     makeZappy / drawPatternFromPoints / drawLineSeq / drawSpot / findDupIndices
///
/// 关键参数（与源一致，可逐个对照）：
///   hops = 10            每段线细分的点数
///   variance = 2.5       抖动幅度系数（画布上的 WIP 图案用这个值）
///   speed = 0.1          抖动随时间流动的速度
///   flowIrregular = 0.2  行进的随机性
///   readabilityOffset = 0.2   转角处向内收缩，避免歧义
///   lastSegmentLen = 0.8 最后一段只画 80%，让末端不闭合
///   外层线宽 5 / 内层线宽 2，内层颜色经 screen() 提亮 → 发光观感
/// </summary>
public static class PatternRenderer
{
    // ---- 源项目常量 ----
    private const int Hops = 10;

    /// <summary>
    /// 抖动幅度系数。
    ///
    /// 源项目画布上的 WIP 图案用 2.5（对应 WOBBLY 预设），但那是配合
    /// MC 的 SimplexNoise 标定的。本实现用的是等价 value-noise，
    /// 在同一段内产生的位移更大，照搬 2.5 会变成蛇形波浪而不是电光抖动。
    /// 这里下调到 0.55，使观感回到「细微电光」。
    /// </summary>
    private const float WobbleVariance = 0.55f;

    /// <summary>抖动流动速度。0 表示完全静止。</summary>
    private const float WobbleSpeed = 0.1f;
    private const float FlowIrregular = 0.2f;
    /// <summary>
    /// 转角处的内收比例。
    ///
    /// 源项目用 0.2：在**自交**处把线缩短 20%，制造一个缺口以便看清"这里交叉了"。
    /// 但泰拉侧观感上会被当成「拐弯点断线/变斜」，因此这里取 0 —— 不做内收，
    /// 由顶点圆盘补片保证拐角平滑。若日后想要原作那种自交缺口，改回 0.2 即可。
    /// </summary>
    private const float ReadabilityOffset = 0f;

    /// <summary>
    /// 最后一段的绘制比例。
    ///
    /// 源项目用 0.8（故意留 20% 缺口以便阅读），但移植到泰拉后表现为
    /// 「最后一笔没有连到端点」，观感像断线。这里取 1.0，让图案闭合到终点。
    /// </summary>
    private const float LastSegmentLenProportion = 1.0f;

    private const float OuterWidth = 5f;
    private const float InnerWidth = 2f;
    private const float StartDotRadius = 2f;

    // ---- 噪声 ----

    /// <summary>
    /// 源项目用 SimplexNoise(9001) 且把结果除 2（注释指出 perlin 输出基本落在 -0.5~0.5）。
    /// 这里用等价的确定性 value-noise 实现：同输入必得同输出，且分布同样归一化到约 [-0.5, 0.5]。
    /// </summary>
    private static double Noise(double x, double y, double z)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        int zi = (int)Math.Floor(z);
        double xf = x - xi;
        double yf = y - yi;
        double zf = z - zi;

        // 平滑插值权重
        double u = xf * xf * (3.0 - 2.0 * xf);
        double v = yf * yf * (3.0 - 2.0 * yf);
        double w = zf * zf * (3.0 - 2.0 * zf);

        double Lerp(double a, double b, double t) => a + (b - a) * t;

        double c000 = Hash(xi, yi, zi);
        double c100 = Hash(xi + 1, yi, zi);
        double c010 = Hash(xi, yi + 1, zi);
        double c110 = Hash(xi + 1, yi + 1, zi);
        double c001 = Hash(xi, yi, zi + 1);
        double c101 = Hash(xi + 1, yi, zi + 1);
        double c011 = Hash(xi, yi + 1, zi + 1);
        double c111 = Hash(xi + 1, yi + 1, zi + 1);

        double x00 = Lerp(c000, c100, u);
        double x10 = Lerp(c010, c110, u);
        double x01 = Lerp(c001, c101, u);
        double x11 = Lerp(c011, c111, u);

        double y0 = Lerp(x00, x10, v);
        double y1 = Lerp(x01, x11, v);

        return Lerp(y0, y1, w) - 0.5; // 归一到 [-0.5, 0.5]
    }

    private static double Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h = (h ^ (uint)z) * 16777619u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    private static double GetNoise(double x, double y, double z) => Noise(x * 0.6, y * 0.6, z * 0.6) / 2.0;

    // ---- 主流程 ----

    /// <summary>
    /// 逐行对应源项目 makeZappy。
    /// </summary>
    public static List<Vector2> MakeZappy(
        IReadOnlyList<Vector2> barePoints,
        HashSet<int>? dupIndices,
        double tick,
        double seed)
    {
        var result = new List<Vector2>();
        if (barePoints.Count == 0)
        {
            return result;
        }

        double zSeed = tick * WobbleSpeed;

        List<Vector2> Zappify(List<Vector2> points, bool truncateLast)
        {
            var zappyPts = new List<Vector2>(points.Count * Hops) { points[0] };

            for (int i = 0; i + 1 < points.Count; i++)
            {
                var src = points[i];
                var target = points[i + 1];
                var delta = target - src;
                float segLen = delta.Length();
                float hopDist = segLen / Hops;
                float maxVariance = hopDist * WobbleVariance;

                int maxJ = truncateLast && i == points.Count - 2
                    ? Math.Max(1, (int)Math.Round(LastSegmentLenProportion * Hops))
                    : Hops;

                for (int j = 1; j <= maxJ; j++)
                {
                    double progress = j / (double)(Hops + 1);
                    var pos = src + delta * (float)progress;

                    double minorPerturb = GetNoise(i, j, Math.Sin(zSeed)) * FlowIrregular;
                    float theta = (float)(3.0 * GetNoise(i + progress + minorPerturb - zSeed, 1337.0, seed) * Math.Tau);

                    // scaleVariance：段内两端抖动小、中间最大
                    double p = progress;
                    double scaleVariance = Math.Min(1.0, 8.0 * (0.5 - Math.Abs(0.5 - p)));

                    float r = (float)(GetNoise(i + progress - zSeed, 69420.0, seed) * maxVariance * scaleVariance);
                    var randomHop = new Vector2(r * (float)Math.Cos(theta), r * (float)Math.Sin(theta));

                    zappyPts.Add(pos + randomHop);

                    // 只有走完全部 hops 才补上目标点；若中途截断（maxJ < Hops），
                    // 则最后额外补一个落在目标上的点，避免线尾悬空。
                    if (j == Hops)
                    {
                        zappyPts.Add(target);
                    }
                }

                if (maxJ < Hops)
                {
                    // 截断段：把线拉到目标点，保证与端点相连
                    zappyPts.Add(target);
                }
            }

            return zappyPts;
        }

        if (dupIndices == null)
        {
            return Zappify(new List<Vector2>(barePoints), true);
        }

        // 有重复点时：按 daisy-chain 分段处理（转角处按 readabilityOffset 内收）
        var daisyChain = new List<Vector2>();
        for (int i = 0; i + 1 < barePoints.Count; i++)
        {
            var head = barePoints[i];
            var tail = barePoints[i + 1];
            var tangent = (tail - head) * ReadabilityOffset;

            if (i != 0 && dupIndices.Contains(i))
            {
                daisyChain.Add(head + tangent);
            }
            else
            {
                daisyChain.Add(head);
            }

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

    /// <summary>找重复经过的格点索引（源：findDupIndices）。</summary>
    public static HashSet<int> FindDupIndices<T>(IReadOnlyList<T> pts) where T : notnull
    {
        var seen = new Dictionary<T, int>();
        var found = new HashSet<int>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (seen.TryGetValue(pts[i], out int ix))
            {
                found.Add(i);
                found.Add(ix);
            }
            else
            {
                seen[pts[i]] = i;
            }
        }
        return found;
    }

    /// <summary>
    /// 把一条图案按「以中心为原点的规整折线」渲染出来，用于 HUD 预览。
    /// 返回该图案在局部空间中的折线点（已居中、已缩放）。
    /// </summary>
    public static List<Vector2> GetCenteredPatternLines(
        HexPattern pattern,
        float targetSize,
        out float scale)
    {
        // 源项目 getCenteredPattern 的思路：先用单位缩放求包围盒，再据此定比例
        var raw = new List<Vector2>();
        var positions = pattern.Positions();
        foreach (var p in positions)
        {
            raw.Add(new Vector2(
                HexGrid.Sqrt3 * p.X + HexGrid.Sqrt3 / 2f * p.Y,
                1.5f * p.Y));
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in raw)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        float extent = Math.Max(maxX - minX, maxY - minY);
        scale = extent < 0.001f ? 1f : targetSize / extent;

        var result = new List<Vector2>(raw.Count);
        foreach (var p in raw)
        {
            result.Add((p - center) * scale);
        }
        return result;
    }

    /// <summary>
    /// 在屏幕上画一条图案的静态度预览（不带抖动），用于「该画什么」的提示。
    /// </summary>
    public static void DrawStaticPreview(
        Action<Vector2, Vector2, float, Color> drawSegment,
        Action<Vector2, float, Color> drawDot,
        HexPattern pattern,
        Vector2 center,
        float targetSize,
        Color color)
    {
        var pts = GetCenteredPatternLines(pattern, targetSize, out _);
        if (pts.Count < 2)
        {
            return;
        }

        // 先画一条暗底加粗线做描边，再画亮色主线，提升可读性
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            drawSegment(center + pts[i], center + pts[i + 1], 4.5f, new Color(0, 0, 0, 160));
        }
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            drawSegment(center + pts[i], center + pts[i + 1], 2.5f, color);
        }

        // 起点 + 起始方向箭头。
        //
        // ⚠️ 这里以前只在起点画一个白色小圆点，对**对称图形**完全不够用：
        // 菱形的 4 个角长得一模一样，而从哪个角落笔，记录下来的角度串是**循环移位**的
        // （从下角起笔 = qaq，从右上角起笔 = aqa，注册表里只有前者）。
        // 玩家看着一个说不出该从哪下笔的图形，只能靠运气。
        // 现在起点是红点、并且沿第一段画一根箭头 —— 与原版书里「红点标起点」的画法一致。
        var startPx = center + pts[0];
        drawDot(startPx, 4.0f, new Color(232, 78, 78));

        var dir = pts[1] - pts[0];
        if (dir.LengthSquared() > 0.001f)
        {
            dir.Normalize();
            var tip = startPx + dir * 12f;
            var wing = new Vector2(-dir.Y, dir.X);
            var arrowColor = new Color(255, 214, 130);

            drawSegment(startPx, tip, 2.4f, arrowColor);
            drawSegment(tip, tip - dir * 5.5f + wing * 4.2f, 2.4f, arrowColor);
            drawSegment(tip, tip - dir * 5.5f - wing * 4.2f, 2.4f, arrowColor);
        }
    }

    /// <summary>
    /// 取出**已实现行为**的图案（当前 68 条），按「角度数升序、同长按 Id」排序。
    ///
    /// 为什么只列这些：剩下 120 条画对了也只会提示「尚未实现」，
    /// 拿来当临摹目标只会让人以为是自己画错了 —— 这正是「盲画 16 个图案全不中」的成因。
    /// </summary>
    public static List<PatternDef> GetImplementedPatterns()
    {
        PatternRegistry.EnsureLoaded();
        var list = new List<PatternDef>();
        foreach (var def in PatternRegistry.All)
        {
            if (PatternRegistry.HasAction(def)) list.Add(def);
        }
        list.Sort((a, b) =>
        {
            int c = a.Angles.Length.CompareTo(b.Angles.Length);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        return list;
    }

    /// <summary>
    /// 从已实现行为的图案里挑出最短的若干条，作为「最容易画对的测试目标」。
    /// </summary>
    public static List<PatternDef> GetSimplestPatterns(int count)
    {
        var list = GetImplementedPatterns();
        if (list.Count > count)
        {
            list.RemoveRange(count, list.Count - count);
        }
        return list;
    }

    /// <summary>
    /// 找与给定签名最接近的已注册图案（按字符距离），用于「未识别」时给提示。
    ///
    /// 注意：匹配本身**不比较起始方向**（见 HexPattern.MatchKey），
    /// 所以这里也只比较角度签名；不要因为方向不同而加惩罚距离。
    /// </summary>
    public static (PatternDef Def, int Distance)? FindClosest(string angles)
    {
        PatternRegistry.EnsureLoaded();

        PatternDef? best = null;
        int bestDist = int.MaxValue;

        foreach (var def in PatternRegistry.All)
        {
            int d = Levenshtein(angles, def.Angles);
            if (d < bestDist)
            {
                bestDist = d;
                best = def;
            }
        }

        return best == null ? null : (best, bestDist);
    }

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (int j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }

        return prev[m];
    }

    /// <summary>源项目的 screen()：把颜色往白色方向提亮一半。</summary>
    private static Color Screen(Color c) => new Color(
        (c.R + 255) / 2,
        (c.G + 255) / 2,
        (c.B + 255) / 2,
        c.A);

    /// <summary>
    /// 画一条 zigzag 折线。对应源项目 drawPatternFromPoints 的两次 drawLineSeq。
    /// 外层宽、内层窄且更亮 → 发光观感。
    /// </summary>
    public static void DrawPattern(
        Action<Vector2, Vector2, float, Color> drawSegment,
        Action<Vector2, float, Color> drawDot,
        IReadOnlyList<Vector2> barePoints,
        Color tailColor,
        Color headColor,
        double tick,
        double seed,
        HashSet<int>? dupIndices = null)
    {
        if (barePoints.Count < 2)
        {
            return;
        }

        // 未显式传入时自行计算重复点（对应源项目 findDupIndices）
        dupIndices ??= FindDupIndices(barePoints);
        var zappyPts = MakeZappy(barePoints, dupIndices.Count > 0 ? dupIndices : null, tick, seed);

        // 外层：粗、原色
        DrawLineSeq(drawSegment, zappyPts, OuterWidth, tailColor, headColor);

        // 内层：细、提亮 → 发光
        DrawLineSeq(drawSegment, zappyPts, InnerWidth, Screen(tailColor), Screen(headColor));

        // ---- 顶点补片：填掉拐角缺口 ----
        // 源项目在 drawLineSeq 里用三角形扇精确填充拐角（joinAngles / joinSteps 那一段），
        // 我们若只画旋转矩形，拐角处是「方头相接」：
        //   钝角 → 外侧留楔形缺口；锐角 → 内侧叠出不齐的边
        // 表现为「拐弯的那个点接线会变斜」。
        // 这里用顶点圆盘近似：半径取外层线宽的一半，足以盖住任意夹角的缺口。
        int n2 = barePoints.Count;
        for (int i = 0; i < n2; i++)
        {
            float t = n2 <= 1 ? 0f : i / (float)(n2 - 1);
            var vertexColor = Color.Lerp(tailColor, headColor, Math.Clamp(t, 0f, 1f));

            // 用外层线宽的一半做圆盘，把拐角缺口完全覆盖
            drawDot(barePoints[i], OuterWidth * 0.5f, vertexColor);
        }

        // ---- 末端亮点 ----
        // 源项目 dodge() = 分量 × 0.9
        var headDim = new Color(
            (int)(headColor.R * 0.9f), (int)(headColor.G * 0.9f), (int)(headColor.B * 0.9f), headColor.A);
        drawDot(barePoints[n2 - 1], InnerWidth * 0.6f, headDim);
    }

    /// <summary>
    /// 对应源项目 drawLineSeq：沿折线画带颜色渐变的粗线。
    ///
    /// 注意：这里必须**整段一次画**，不能把每段切成两半分别上色。
    /// 之前切成两半（p[i]→mid 用 c1、mid→p[i+1] 用 c2）会在每个拐角产生两段重叠，
    /// 视觉上表现为「连到弯曲点时那个点变斜」。踩过的坑。
    /// </summary>
    private static void DrawLineSeq(
        Action<Vector2, Vector2, float, Color> drawSegment,
        IReadOnlyList<Vector2> points,
        float width,
        Color tail,
        Color head)
    {
        if (points.Count < 2)
        {
            return;
        }

        int n = points.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            // 用段中点在整个折线中的进度决定颜色，得到平滑渐变
            float t = (i + 0.5f) / (n - 1);
            var color = Color.Lerp(tail, head, Math.Clamp(t, 0f, 1f));
            drawSegment(points[i], points[i + 1], width, color);
        }
    }
}
