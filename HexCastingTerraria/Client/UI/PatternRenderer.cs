using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 图案检索辅助（临摹目标、最接近的图案）。
/// 画图案：画布上的在 <see cref="Core.Canvas.PatternGeometry"/>（逐行移植 RenderLib），
/// 静态的（卷轴、石板、提示框……）在 <see cref="Core.Canvas.StaticPatternArt"/> / <see cref="PatternArt"/>。
/// </summary>
public static class PatternRenderer
{
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
