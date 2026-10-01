namespace HexCastingTerraria.Core.Registry;

/// <summary>
/// 图案的**显示名**。界面一律走这里，不要各自拼 `Id.Replace("hexcasting:", "")`。
///
/// 为什么要有这层：图案的正式称呼是中文名（「意识之精思」「内省」），
/// 而 id（`get_caster`、`open_paren`）是给查 wiki / 对日志用的。
/// 两边都要能拿到，且**查不到中文名时必须不崩** —— 附属或自造图案随时可能出现。
/// </summary>
public static class PatternDisplay
{
    /// <summary>
    /// 去掉 `hexcasting:` 前缀的 id。就是原版语言文件里的键名形式。
    /// </summary>
    public static string ShortId(this PatternDef def)
        => def.Id.StartsWith("hexcasting:", System.StringComparison.Ordinal)
            ? def.Id["hexcasting:".Length..]
            : def.Id;

    /// <summary>
    /// 官方中文名；没有就退回 ShortId。
    ///
    /// 只用于**给玩家看**的地方。日志、诊断、匹配提示里要 id 的地方请直接用 <see cref="ShortId"/>，
    /// 否则出错时对不上源代码。
    /// </summary>
    public static string DisplayName(this PatternDef def)
        => GeneratedPatternNames.NameOf(def.Id) ?? (AddonNames.TryGetValue(def.Id, out var n) ? n : def.ShortId());

    /// <summary>附属图案的官方中文名（附属打开时登记，id 带附属命名空间）。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> AddonNames = new();

    public static void RegisterAddonName(string id, string name) => AddonNames[id] = name;

    public static void UnregisterAddonName(string id) => AddonNames.Remove(id);

    /// <summary>
    /// 一个图案叫什么（上游 Inline 图案的悬停名 InlinePatternData.getPatternName、HexDebug 的 getI18nOrNull）：
    /// 普通图案和本世界的大法术用名字；数字和簿记员用特殊名（数字按上游 DOUBLE_FORMATTER「####.####」）；都不是返回 null。
    /// </summary>
    public static string? NameOf(Casting.Math.HexPattern pattern)
    {
        var def = PatternRegistry.Match(pattern);
        if (def is not null) return def.DisplayName();
        if (Casting.Math.SpecialPatterns.TryNumber(pattern.AnglesSignature(), out var n))
            return "数字之精思：" + n.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        if (Casting.Math.SpecialPatterns.TryMask(pattern, out var mask))
            return "簿记员之策略：" + new string(System.Array.ConvertAll(mask, k => k ? '-' : 'v'));
        return null;
    }
}
