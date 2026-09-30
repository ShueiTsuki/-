using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 学会的大法术画法（上游 config.knownWorldPatterns，一行一条「世界 图案id 签名」，按世界分开）。
/// 上游 PatternEntries.populatePerWorldCache / setPerWorldSig 的读写规则。
/// </summary>
public static class KnownWorldPatterns
{
    private static readonly Regex NonWord = new("[^a-zA-Z0-9]", RegexOptions.Compiled);

    /// <summary>上游 Utils.getWorldContext 的清洗：字母数字以外都换成 _。</summary>
    public static string Sanitize(string s) => NonWord.Replace(s, "_");

    /// <summary>这个世界学会的：图案 id → 签名。格式不对（不是三段）的行跳过。</summary>
    public static Dictionary<string, string> ForWorld(IEnumerable<string> lines, string worldContext)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            var parts = line.Split(' ');
            if (parts.Length != 3 || parts[0] != worldContext) continue;
            map[parts[1]] = parts[2];
        }
        return map;
    }

    /// <summary>上游 setPerWorldSig：去掉同一世界同一图案的旧行，再加上新的一行。</summary>
    public static List<string> Learn(IEnumerable<string> lines, string worldContext, string id, string sig)
    {
        var list = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split(' ');
            if (parts.Length == 3 && parts[0] == worldContext && parts[1] == id) continue;
            list.Add(line);
        }
        list.Add(worldContext + " " + id + " " + sig);
        return list;
    }
}
