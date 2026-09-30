using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// <c>.hexpattern</c> 格式 -> HexParse 代码（上游 parsers/hexpattern/DotHexPatternMapper.kt + TriePrefixMap.kt）。
/// 上游作者自己写着「实验性质、只支持一部分」—— 这里照搬它做到的那部分：
/// 一行一个图案名（按**当前语言的显示名**反查 id）、<c>数字之精思：5</c> / <c>簿记员之策略：-v</c> 这类前缀、
/// <c>&lt;…&gt;</c> 里的列表 / 向量 / 「方向 签名」常量、<c>( ) [ ] { }</c> 原样保留；认不出的去掉空格原样交给解析器。
///
/// 当前语言：本模组的界面是中文，所以用官方中文名（本体 + 开着的附属）。上游还能从服务端拿英文名表（syncDisplayToClient），泰拉侧不做。
/// </summary>
public static class DotHexPattern
{
    private static readonly HashSet<string> KeepSelfKeys = new() { "(", ")", "[", "]", "{", "}" };

    /// <summary>上游 RawPatternMap：这几个图案不写 id，写 HexParse 的元符号。</summary>
    private static readonly Dictionary<string, string> RawPatternMap = new()
    {
        ["hexcasting:open_paren"] = "{",
        ["hexcasting:close_paren"] = "}",
        ["hexcasting:escape"] = "\\",
        ["hexcasting:undo"] = "undo",
    };

    /// <summary>上游 RawSpecialHandlerMap 的前缀（官方中文「数字之精思：%s」「簿记员之策略：%s」去掉 %s）。</summary>
    private static readonly Dictionary<string, string> SpecialPrefixes = new()
    {
        ["数字之精思："] = "num_",
        ["簿记员之策略："] = "mask_",
    };

    private static readonly Regex EmbeddedVec = new(@"\G\(\s*([0-9.-e]+)\s*,\s*([0-9.-e]+)\s*,\s*([0-9.-e]+)\s*\)");
    private static readonly Regex EmbeddedSig = new(@"\G(((NORTH|SOUTH)_)?(WEST|EAST))?\s+(?<sig>[wedsaq]+)");
    // .NET 的 Regex.Split 会把捕获组也放进结果里 —— 所以这里全是非捕获组（上游 Kotlin 的 split 没有这个行为）
    private static readonly Regex LineSep = new(@"(?:(?<=[\[\]])?\s*,\s*(?=[\[\]])?)|(?<=[\[\]])|(?=[\[\]])");
    private static readonly Regex CommentBlock = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex CommentLine = new("//[^\r\n\u0085\u2028\u2029]*");

    /// <summary>上游 processCode(code, firstCall = true)：先去掉注释，再逐行转换。</summary>
    public static string ProcessCode(string code) => Process(CommentLine.Replace(CommentBlock.Replace(code, ""), ""));

    private static string Process(string code)
    {
        var names = NameMap();
        var sb = new StringBuilder();
        foreach (var line in code.Split('\n').Select(l => l.Trim()))
        {
            sb.Append(' ').Append(ProcessOne(line, names));
        }
        return sb.ToString();
    }

    private static string ProcessOne(string p, Dictionary<string, string> names)
    {
        if (p.StartsWith("<", System.StringComparison.Ordinal) && p.EndsWith(">", System.StringComparison.Ordinal) && p.Length >= 2)
        {
            string unwrapped = p.Substring(1, p.Length - 2);
            if (unwrapped.StartsWith("[", System.StringComparison.Ordinal) || unwrapped.EndsWith("]", System.StringComparison.Ordinal))
            {
                return Process(string.Join("\n", LineSep.Split(unwrapped)));
            }
            var vec = EmbeddedVec.Match(unwrapped, 0);
            if (vec.Success) return $"vec_{vec.Groups[1].Value}_{vec.Groups[2].Value}_{vec.Groups[3].Value}";
            var sig = EmbeddedSig.Match(unwrapped, 0);
            if (sig.Success) return "_" + sig.Groups["sig"].Value;
            return unwrapped;
        }
        if (KeepSelfKeys.Contains(p)) return p;

        // 上游 TriePrefixMap.get：最短的匹配前缀 + 剩下的部分（去掉两边空白）
        foreach (var (prefix, value) in SpecialPrefixes.OrderBy(kv => kv.Key.Length))
        {
            if (p.StartsWith(prefix, System.StringComparison.Ordinal)) return value + p.Substring(prefix.Length).Trim();
        }

        return names.TryGetValue(p, out var key) ? key : p.Replace(" ", "");
    }

    /// <summary>上游 doCollect：显示名 -> 长 id（元符号那几个 -> 元符号）。</summary>
    private static Dictionary<string, string> NameMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var def in PatternRegistry.All.Concat(PatternRegistry.EnabledAddonPatterns()))
        {
            string display = def.DisplayName();
            if (display != def.ShortId()) map[display] = RawPatternMap.TryGetValue(def.Id, out var raw) ? raw : def.Id;
        }
        return map;
    }
}
