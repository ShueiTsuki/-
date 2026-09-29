using System.Collections.Generic;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 「你画的这条最接近哪个图案」。
///
/// ## 为什么需要它
///
/// 画布只认**注册表里的 188 种形状**，而玩家随手画的曲线几乎一定不在里面。
/// 原本的反馈只有一句「这不是一个有效的图案」—— 玩家既不知道差在哪，
/// 也不知道下一步该怎么改，只能一遍遍猜。
///
/// 这里给出**最接近的那条**与「差几笔」，把死路变成可操作的提示。
///
/// ## 为什么用编辑距离而不是几何相似度
///
/// 匹配本身就是**按角度序列的字符串**做的（<see cref="HexPattern.MatchKey"/>），
/// 所以「哪条最接近」在同一个度量下回答才自洽 ——
/// 换成几何距离会出现「几何上很像、但按匹配规则差 3 笔」的误导性建议。
///
/// 起始方向不参与（与匹配规则一致），所以这里只比角度串。
/// </summary>
public static class PatternSuggestion
{
    /// <summary>一条建议。</summary>
    /// <param name="Signature">最接近的签名。</param>
    /// <param name="Distance">编辑距离（差几笔）。</param>
    /// <param name="PatternIds">用这个签名的图案 id（同签名可能有多条：起始方向不同）。</param>
    public readonly record struct Suggestion(string Signature, int Distance, IReadOnlyList<string> PatternIds);

    /// <summary>
    /// 找最接近的签名。
    ///
    /// <paramref name="maxDistance"/> 之外不给建议 ——
    /// 「你画的这条最像 const/null（差 9 笔）」比不给建议更没用，纯噪音。
    /// </summary>
    public static Suggestion? Nearest(
        string drawnSignature,
        IReadOnlyDictionary<string, IReadOnlyList<string>> knownBySignature,
        int maxDistance = 3)
    {
        string? best = null;
        int bestDistance = int.MaxValue;

        foreach (var signature in knownBySignature.Keys)
        {
            // 长度差已经超过上限的不必算（编辑距离至少是长度差）
            if (System.Math.Abs(signature.Length - drawnSignature.Length) > maxDistance) continue;

            int d = Distance(drawnSignature, signature);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = signature;
            }
        }

        if (best == null || bestDistance > maxDistance) return null;

        return new Suggestion(best, bestDistance, knownBySignature[best]);
    }

    /// <summary>标准 Levenshtein 距离（只用插入/删除/替换）。</summary>
    public static int Distance(string a, string b)    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = System.Math.Min(
                    System.Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // ── 给玩家看的文案 ────────────────────────────────────────────────
    //
    // 画布 HUD 和施法失败的聊天提示都要说同一句话，所以文案只在这里生成一份。
    // 分散在两处写，迟早会出现「HUD 说差 1 笔、聊天说差 2 笔」这种事。

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? _known;

    /// <summary>注册表的签名索引。构建一次就缓存 —— 每条未识别图案都重建会很浪费。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Known()
        => _known ??= PatternRegistry.SignaturesById();

    /// <summary>
    /// 「最接近「弧之精思」（差 1 笔）」。
    ///
    /// 差得太远（超过 <paramref name="maxDistance"/> 笔）返回 null ——
    /// 「最像 const/null，差 9 笔」这种建议只会误导。
    /// </summary>
    public static string? Describe(string drawnSignature, int maxDistance = 3)
    {
        if (Nearest(drawnSignature, Known(), maxDistance) is not { } s) return null;

        string name = NameOf(s.PatternIds);
        return s.Distance == 0
            ? $"与「{name}」签名相同"
            : $"最接近「{name}」（差 {s.Distance} 笔）";
    }

    /// <summary>取签名对应图案的中文名；查不到就用去掉命名空间的 id。</summary>
    private static string NameOf(IReadOnlyList<string> patternIds)
    {
        if (patternIds.Count == 0) return "?";

        string id = patternIds[0];
        return GeneratedPatternNames.NameOf(id)
            ?? (id.StartsWith("hexcasting:", System.StringComparison.Ordinal) ? id["hexcasting:".Length..] : id);
    }
}
