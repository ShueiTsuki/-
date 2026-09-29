using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 图案的静态预览（书、石板、卷轴、阿卡夏记录）与图案检索辅助。
/// 画布上的电光线型在 <see cref="Core.Canvas.PatternGeometry"/>（逐行移植 RenderLib）。
/// </summary>
public static class PatternRenderer
{
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
}
