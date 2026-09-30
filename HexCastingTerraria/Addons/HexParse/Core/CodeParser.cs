using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 代码 -> iota 列表（上游 parsers/ParserMain.java 的 ParseCode + str2nbt/* 全部符号解析器 + macro/MacroProcessor.java）。
///
/// 符号按上游的顺序逐个试（第一个认领的解析）：
/// 元符号 -> 常量（self / true / false / null / garbage）-> 普通图案 -> 大法术 -> tab_N -> comment_ -> c"…" ->
/// 数字 -> vec_ -> mask_ -> num_ -> entity_ -> 内置别名（hermes、iris、1.19 旧名…）-> nbt_ -> _角度串。
/// <c>[</c> <c>]</c> 构造嵌套列表；括号不配对时照样尽量把能用的部分保住（和上游一样发消息、补全）。
/// </summary>
public sealed class CodeParser
{
    /// <summary>上游 HexIotaTypes.MAX_SERIALIZATION_TOTAL：宏展开后的符号总数上限。</summary>
    public const int MaxTokens = 1024;

    private static readonly Regex NumRegex = new(@"^[0-9.\-]+(e[0-9.\-]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex MaskRegex = new(@"^mask_[-v]+$", RegexOptions.CultureInvariant);
    private static readonly Regex RawPatternRegex = new(@"^_[wedsaq]*$", RegexOptions.CultureInvariant);

    /// <summary>上游 ToDialect 的内置别名（去掉 hexcasting: 前缀后查）。</summary>
    public static readonly IReadOnlyDictionary<string, string> BuiltinDialects = BuildDialects();

    private readonly IHexParseHost _host;
    private readonly HexParseSettings _settings;
    private readonly PatternNames _names;
    private readonly List<TokenParser> _parsers;

    /// <summary>这次解析累计的媒质（上游 CostTracker）。由游戏侧在解析完后从施法者身上扣。</summary>
    public long TotalCost { get; private set; }

    public CodeParser(IHexParseHost host, HexParseSettings settings, PatternNames names)
    {
        _host = host;
        _settings = settings;
        _names = names;
        _parsers = BuildParsers();
    }

    // ── 入口 ───────────────────────────────────────────────────────

    /// <summary>上游 ParseCode(String)：先分词，分词出错也用已经切好的部分继续。</summary>
    public ListIota ParseCode(string code)
    {
        List<string> tokens;
        var recovered = new List<string>();
        try
        {
            tokens = CodeCutter.Split(code, _settings, out recovered);
        }
        catch (Exception e)
        {
            // 上游 tryRecoverSplittedCode：出错前已经切好的符号照样用
            _host.Message($"在解析时出错：{e.Message}", HexParseMessageKind.Error);
            tokens = recovered;
        }
        return ParseTokens(tokens);
    }

    /// <summary>上游 ParseCode(List)：已经切好的符号（剪贴板由客户端预切好发上来）。</summary>
    public ListIota ParseTokens(IEnumerable<string> tokens)
    {
        TotalCost = 0;
        var stack = new Stack<List<Iota>>();
        stack.Push(new List<Iota>());
        try
        {
            var it = new MacroProcessor(tokens.GetEnumerator(), this, new HashSet<string>(), 0);
            while (it.HasNext)
            {
                string frag = it.Next();
                switch (frag)
                {
                    case "[":
                        stack.Push(new List<Iota>());
                        break;
                    case "]":
                        if (stack.Count <= 1) throw new HexParseException("右括号过多");
                        var inner = new ListIota(stack.Pop());
                        stack.Peek().Add(inner);
                        break;
                    default:
                        try
                        {
                            var parsed = ParseSingleNode(frag, out bool ignored);
                            if (parsed == null)
                            {
                                if (!ignored) _host.Message($"未知符号：{frag}", HexParseMessageKind.Warning);
                            }
                            else stack.Peek().Add(parsed);
                        }
                        catch (Exception e)
                        {
                            _host.Message($"在解析{frag}时出错：{e.Message}", HexParseMessageKind.Error);
                        }
                        break;
                }
            }
            if (stack.Count > 1) throw new HexParseException("左括号过多");
        }
        catch (Exception e)
        {
            _host.Message($"在解析时出错：{e.Message}", HexParseMessageKind.Error);
            // 尽量把数据补全
            while (stack.Count > 1)
            {
                var sub = new ListIota(stack.Pop());
                stack.Peek().Add(sub);
            }
            return new ListIota(stack.Count == 0 ? new List<Iota>() : stack.Pop());
        }
        return new ListIota(stack.Pop());
    }

    /// <summary>
    /// 上游 ParseSingleNode：返回 null = 没有解析器认领（或被配置忽略，此时 <paramref name="ignored"/> 为真）。
    /// </summary>
    public Iota? ParseSingleNode(string frag, out bool ignored)
    {
        ignored = false;
        foreach (var p in _parsers)
        {
            if (!p.Match(frag)) continue;
            if (p.Ignored()) { ignored = true; return null; }
            var res = p.Parse(frag);
            TotalCost += p.Cost();
            return res;
        }
        return null;
    }

    /// <summary>
    /// 客户端预检（上游 preMatchClipboardClient）：只留下「能认出来」的符号，认不出的报未知符号。
    /// 剪贴板的代码在客户端切好、筛过再发给服务端。
    /// </summary>
    public List<string> PreMatch(string code, System.Func<string, bool> isMacroOrDialect)
    {
        var res = new List<string>();
        List<string> frags;
        try
        {
            frags = CodeCutter.Split(code, _settings, out _);
        }
        catch (Exception e)
        {
            _host.Message($"在解析时出错：{e.Message}", HexParseMessageKind.Error);
            return res;
        }
        foreach (var frag in frags)
        {
            bool matched = frag is "[" or "]" || isMacroOrDialect(frag);
            if (!matched)
            {
                foreach (var p in _parsers)
                {
                    if (p.Match(frag) && !p.Ignored()) { matched = true; break; }
                }
            }
            if (matched) res.Add(frag);
            else _host.Message($"未知符号：{frag}", HexParseMessageKind.Warning);
        }
        return res;
    }

    // ── 符号解析器（顺序 = 上游 ParserMain.init）──────────────────────

    private sealed class TokenParser
    {
        public required System.Func<string, bool> Match { get; init; }
        public required System.Func<string, Iota?> Parse { get; init; }
        public System.Func<bool> Ignored { get; init; } = () => false;
        public System.Func<long> Cost { get; init; } = () => 0;
    }

    private List<TokenParser> BuildParsers()
    {
        long baseCost() => _settings.ParserBaseCost;
        bool commentsOff() => _settings.CommentParsing == HexParseSettings.CommentMode.Disabled;
        bool indentsOff() => _settings.IndentParsing == HexParseSettings.CommentMode.Disabled;

        return new List<TokenParser>
        {
            // META：\ del undo ( ) { }
            new() { Match = n => _names.Meta.ContainsKey(n), Parse = n => new PatternIota(_names.Meta[n]), Cost = baseCost },
            // 常量（不分大小写）
            new() { Match = n => MiscConst(n.ToLowerInvariant()) != null, Parse = n => MiscConst(n.ToLowerInvariant())!(), Cost = baseCost },
            // 普通图案
            new() { Match = n => _names.Normal.ContainsKey(n), Parse = n => new PatternIota(_names.Normal[n]), Cost = baseCost },
            // 大法术：没解锁的变成 <id?> 占位注释
            new() { Match = n => _names.Great.ContainsKey(n), Parse = ParseGreat, Cost = baseCost },
            // tab_N / indent…
            new() { Match = n => n.StartsWith("tab", StringComparison.Ordinal) || n.StartsWith("indent", StringComparison.Ordinal),
                    Parse = n => MakeTab(IndentOf(n)), Ignored = indentsOff },
            // comment_xxx
            new() { Match = n => n.StartsWith("comment_", StringComparison.Ordinal), Parse = n => new CommentIota(n.Substring(8)), Ignored = commentsOff },
            // c"…"（代码里的 // /* */ 注释，由分词器在「注释 = ALL」时生成）
            new() { Match = n => n.StartsWith("c\"", StringComparison.Ordinal), Parse = n => new CommentIota(StringEscaper.Unescape(n.Substring(1))), Ignored = commentsOff },
            // 数字
            new() { Match = n => NumRegex.IsMatch(n), Parse = ParseNumber, Cost = baseCost },
            // vec_x_y_z
            new() { Match = n => n.StartsWith("vec", StringComparison.Ordinal), Parse = ParseVec, Cost = baseCost },
            // mask_-v
            new() { Match = n => MaskRegex.IsMatch(n), Parse = ParseMask, Cost = baseCost },
            // num_x -> 数字之精思的图案
            new() { Match = n => n.StartsWith("num", StringComparison.Ordinal), Parse = ParseNumPattern, Cost = baseCost },
            // entity_…（泰拉偏差：见 IHexParseHost.ResolveEntity）
            new() { Match = n => n.StartsWith("entity", StringComparison.Ordinal), Parse = n => _host.ResolveEntity(n) ?? NullIota.Instance, Cost = baseCost },
            // 内置别名
            new() { Match = n => BuiltinDialects.ContainsKey(CutHexHeader(n)), Parse = n => ParseSingleNode(BuiltinDialects[CutHexHeader(n)], out _), Cost = baseCost },
            // nbt_…（只在「未知 iota = 编码保存」时认）
            new() { Match = n => _settings.ShowUnknownNbt == HexParseSettings.UnknownMode.KeepNbt && n.StartsWith(FallbackBinary.Prefix, StringComparison.Ordinal),
                    Parse = FallbackBinary.Decode, Cost = baseCost },
            // _角度串
            new() { Match = n => RawPatternRegex.IsMatch(n), Parse = n => new PatternIota(Pat(n.Substring(1), HexDir.East)), Cost = baseCost },
        };
    }

    private System.Func<Iota>? MiscConst(string key) => key switch
    {
        "self" or "myself" => () => (Iota?)_host.Self ?? NullIota.Instance,
        "false" => () => BooleanIota.Of(false),
        "true" => () => BooleanIota.Of(true),
        "null" => () => NullIota.Instance,
        "garbage" => () => GarbageIota.Instance,
        _ => null,
    };

    private Iota ParseGreat(string node)
    {
        bool unlocked = _settings.ParseGreatSpells switch
        {
            HexParseSettings.GreatMode.All => true,
            HexParseSettings.GreatMode.Disabled => false,
            _ => _host.IsGreatUnlocked(_names.ActiveLongName(node)),
        };
        return unlocked ? new PatternIota(_names.Great[node]) : new CommentIota(CommentIota.MakeGreatPlaceholder(node));
    }

    private static int IndentOf(string node)
    {
        // 上游 Integer.parseInt(node.substring(4))，失败 = 0（indent_3 这种写法也会落到 0，上游如此）
        if (node.Length < 4) return 0;
        return int.TryParse(node.Substring(4), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    private static Iota MakeTab(int n)
    {
        // 上游 " ".repeat(负数) 抛异常 -> 外层发「解析出错」
        if (n < 0) throw new HexParseException("count is negative: " + n);
        return CommentIota.Tab(n);
    }

    private static Iota ParseNumber(string node)
        => new DoubleIota(double.TryParse(node, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN);

    private static Iota ParseVec(string node)
    {
        var frags = node.Split('_');
        var axes = new double[3];
        for (int i = 1; i <= 3; i++)
        {
            if (i >= frags.Length) continue;
            if (double.TryParse(frags[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) axes[i - 1] = d;
        }
        return new VectorIota(axes[0], axes[1], axes[2]);
    }

    private static Iota ParseMask(string node)
    {
        var seq = new StringBuilder();
        bool line = true;
        var start = HexDir.East;
        if (node[5] == 'v')
        {
            line = false;
            seq.Append('a');
            start = HexDir.SouthEast;
        }
        foreach (char c in node.Substring(6))
        {
            if (c == '-')
            {
                seq.Append(line ? 'w' : 'e');
                line = true;
            }
            else
            {
                seq.Append(line ? "ea" : "da");
                line = false;
            }
        }
        return new PatternIota(Pat(seq.ToString(), start));
    }

    private static Iota ParseNumPattern(string node)
    {
        var parts = node.Split('_');
        double num = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        return new PatternIota(Pat(NumEvaluator.AnglesFromNum(num), num < 0 ? HexDir.NorthEast : HexDir.SouthEast));
    }

    private static string CutHexHeader(string node)
        => node.StartsWith(PatternNames.HexNamespace, StringComparison.Ordinal) ? node.Substring(PatternNames.HexNamespace.Length + 1) : node;

    internal static HexPattern Pat(string angles, HexDir dir)
    {
        if (!HexPattern.TryFromAnglesUnchecked(angles, dir, out var p, out _) || p == null)
        {
            // 上游 IotaFactory.makePattern 的报错
            char bad = System.Linq.Enumerable.FirstOrDefault(angles, c => "wedsaq".IndexOf(c) < 0);
            throw new HexParseException($"在序列\"{angles}\"中包含非法字符'{bad}'");
        }
        return p;
    }

    private static Dictionary<string, string> BuildDialects()
    {
        var d = new Dictionary<string, string>
        {
            ["pop"] = "mask_v",
            ["open_paren"] = "(",
            ["close_paren"] = ")",
            ["escape"] = "\\",
            ["hermes"] = "eval",
            ["iris"] = "eval/cc",
            ["thoth"] = "for_each",
            // 1.19 的旧注册名
            ["list_size"] = "abs",
            ["concat"] = "add",
            ["to_set"] = "unique",
            ["teleport"] = "teleport/great",
            ["list_remove"] = "remove_from",
            ["modify_in_place"] = "replace",
        };
        foreach (var oldLong in new[] { "mul_dot", "div_cross", "abs_len", "pow_proj", "and_bit", "or_bit", "xor_bit", "not_bit", "reverse_list" })
        {
            d[oldLong] = oldLong.Split('_')[0];
        }
        return d;
    }

    // ── 宏展开（上游 macro/MacroProcessor.java）──────────────────────

    /// <summary>
    /// 逐个吐出符号，遇到宏（#名）就把宏的内容切开递归展开；遇到玩家定义的别名就换成对应的一个符号。
    /// 宏里的 tab_N 叠加外层当时的缩进（配置 AddIndentInsideMacro）；同一个宏嵌套使用会报错；展开后总数上限 1024。
    /// </summary>
    private sealed class MacroProcessor
    {
        private readonly IEnumerator<string> _source;
        private readonly CodeParser _parser;
        private readonly HashSet<string> _usedMacros;
        private readonly bool _noExtraIndent;
        private MacroProcessor? _inner;
        private string? _innerMacroName;
        private string? _cachedNext;
        private Exception? _cachedError;
        private int _count;
        private readonly int _tabBase;
        private int _tabLastMet;

        public MacroProcessor(IEnumerator<string> source, CodeParser parser, HashSet<string> used, int extraTab)
        {
            _source = source;
            _parser = parser;
            _usedMacros = used;
            _tabBase = _tabLastMet = extraTab;
            _noExtraIndent = !parser._settings.AddIndentInsideMacro || parser._settings.IndentParsing == HexParseSettings.CommentMode.Disabled;
            _cachedNext = ApplyForIndent(CalcCache());
        }

        public bool HasNext => _cachedNext != null;

        public string Next()
        {
            if (_cachedError != null) throw _cachedError;
            _count++;
            if (_count >= MaxTokens) throw new HexParseException("超出了栈的大小上限");
            string res = _cachedNext!;
            _cachedNext = ApplyForIndent(CalcCache());
            return res;
        }

        private static bool IsMacro(string key) => key.StartsWith("#", StringComparison.Ordinal);

        private string? CalcCache()
        {
            while (true)
            {
                if (_inner != null && _inner.HasNext) return _inner.Next();
                if (_inner != null)
                {
                    _usedMacros.Remove(_innerMacroName!);
                    _inner = null;
                }
                if (!_source.MoveNext()) return null;
                string raw = _source.Current;
                bool isMacro = IsMacro(raw);
                if (isMacro && _usedMacros.Contains(raw))
                {
                    _cachedError = new HexParseException($"宏{raw}已使用");
                    return "ERROR";
                }
                string? mapped = _parser._host.GetMacro(raw);
                if (mapped == null) return raw;
                if (!isMacro) return mapped;
                _usedMacros.Add(raw);
                try
                {
                    var tokens = CodeCutter.Split(mapped, _parser._settings, out _);
                    _inner = new MacroProcessor(tokens.GetEnumerator(), _parser, _usedMacros, _tabLastMet);
                }
                catch (Exception e)
                {
                    _cachedError = e;
                    return "ERROR";
                }
                _innerMacroName = raw;
                // 上游这里递归调用 calcCache()；循环等价
            }
        }

        private string? ApplyForIndent(string? original)
        {
            if (_noExtraIndent) return original;
            if (original == null || !(original.StartsWith("tab", StringComparison.Ordinal) || original.StartsWith("indent", StringComparison.Ordinal))) return original;
            _tabLastMet = IndentOf(original) + _tabBase;
            return "tab_" + _tabLastMet;
        }
    }
}
