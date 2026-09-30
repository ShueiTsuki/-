namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>上游 Utils.fluffySearch：按顺序逐字命中（不必连续），连续命中加分，开头就对上再加分；有字没命中 = 0。</summary>
public static class FluffySearch
{
    public static int Score(string? query, string? candidate)
    {
        if (query is null || candidate is null || query.Length == 0) return 0;

        var q = query.ToLowerInvariant();
        var c = candidate.ToLowerInvariant();
        int score = 0, consecutive = 0, qi = 0;

        for (int ci = 0; ci < c.Length && qi < q.Length; ci++)
        {
            if (q[qi] == c[ci])
            {
                score += 10 + consecutive * 5;
                consecutive++;
                qi++;
            }
            else
            {
                consecutive = 0;
            }
        }

        if (candidate.Length > 0 && candidate.ToLowerInvariant().StartsWith(q, System.StringComparison.Ordinal)) score += 15;
        return qi == q.Length ? score : 0;
    }
}
