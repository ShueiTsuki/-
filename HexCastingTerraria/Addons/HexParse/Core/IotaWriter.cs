using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// iota -> 代码（上游 ParserMain.ParseIotaNbt + nbt2str/* + misc/StringProcessors.java + parsers/meta/MetaHolder.java）。
///
/// - 列表：根列表不加括号，里层 <c>[…]</c>，元素用逗号连接
/// - 图案：<c>( ) \</c> 三个特殊写法；认得的写图案名（本体用短名，附属默认也用短名）；数字之精思写 <c>num_x</c>、
///   簿记员写 <c>mask_-v</c>；都不是写 <c>_角度串</c>；「只要签名」时一律 <c>_角度串</c>
/// - 注释：代码注释写回 <c>/*…*/</c>，缩进写回换行 + 空格，大法术占位写回 id，其余 <c>comment_…</c>
/// - 数字 / 向量去掉多余的 0；实体写 self 或 entity_…；true / false / null / garbage
/// - 没有写法的 iota：按配置写 nbt_…（能解析回来）/ UNKNOWN(…) / UNKNOWN
/// - 开头按配置附 <c>// Author:</c> 与 <c>// Requires:</c>（用到了哪些附属）
/// </summary>
public sealed class IotaWriter
{
    /// <summary>上游 StringProcessors.READ_DEFAULT：去掉括号 / 行首行尾旁边多余的逗号。</summary>
    public static readonly Func<string, string> ReadDefault =
        s => Regex.Replace(s, @"(?<=\[|]|\(|\)|^|\n|\s),|,(?=\[|]|\(|\)|$|\n)", "");

    /// <summary>上游 StringProcessors.READ_HEXBOT_VARIANT：改成 Discord 上 HexBug 机器人的 /patterns hex 写法。</summary>
    public static readonly Func<string, string> ReadHexbug = s =>
    {
        s = s.Replace("\\", "consideration");
        s = s.Replace('(', '{').Replace(')', '}');
        s = Regex.Replace(s, "mask_", "mask ");
        s = Regex.Replace(s, "num_", "number ");
        s = Regex.Replace(s, "comment_.*?(?=,|$)", "");
        s = Regex.Replace(s, @"\n\s*", ",");
        s = Regex.Replace(s, "[a-z_]+:", "");
        s = Regex.Replace(s, ",(?=,)", "");
        return s;
    };

    private static readonly Regex IndentComment = new(@"^\n\s*$");
    private static readonly Dictionary<string, string> SpecialPatternText = new() { ["qqq"] = "(", ["eee"] = ")", ["qqqaw"] = "\\" };

    private readonly IHexParseHost _host;
    private readonly HexParseSettings _settings;
    private readonly HashSet<string> _namespaces = new();
    private bool _forceSignatures;

    public IotaWriter(IHexParseHost host, HexParseSettings settings)
    {
        _host = host;
        _settings = settings;
    }

    /// <summary>
    /// 把一个 iota 写成代码。<paramref name="forceSignatures"/> = <c>read_signatures</c>（图案一律写角度串）；
    /// <paramref name="post"/> = 写完之后的整体处理（<see cref="ReadDefault"/> / <see cref="ReadHexbug"/>）。
    /// </summary>
    public string Write(Iota root, bool forceSignatures, Func<string, string> post)
    {
        _forceSignatures = forceSignatures;
        _namespaces.Clear();
        string res = post(WriteNode(root, isRoot: true));
        if (_settings.AttachCodeMeta != 0) res = DumpMeta() + res;
        return res;
    }

    private string WriteNode(Iota node, bool isRoot)
    {
        try
        {
            if (node is ListIota list)
            {
                var sb = new StringBuilder();
                if (!isRoot) sb.Append('[');
                bool first = true;
                foreach (var sub in list.Items)
                {
                    if (first) first = false;
                    else sb.Append(',');
                    sb.Append(WriteNode(sub, isRoot: false));
                }
                if (!isRoot) sb.Append(']');
                return sb.ToString();
            }
            string? text = node switch
            {
                PatternIota p => WritePattern(p.Pattern),
                CommentIota c => WriteComment(c.Comment),
                DoubleIota d => NumEvaluator.DisplayMinimal(d.Value),
                VectorIota v => WriteVec(v),
                EntityIota e => _host.Self is { } self && self.ValueEquals(e) ? "self" : _host.EntityToCode(e),
                BooleanIota b => b.Value ? "true" : "false",
                NullIota => "null",
                GarbageIota => "garbage",
                _ => null,
            };
            if (node is not PatternIota) _namespaces.Add(NamespaceOfKind(node));
            if (text != null) return text;
            return _settings.ShowUnknownNbt switch
            {
                HexParseSettings.UnknownMode.KeepNbt => FallbackBinary.Encode(node),
                HexParseSettings.UnknownMode.ShowNbt => $"UNKNOWN({node})",
                _ => "UNKNOWN",
            };
        }
        catch (Exception e)
        {
            _host.Message($"在解析{node}时出错：{e.Message}", HexParseMessageKind.Error);
            return "ERROR";
        }
    }

    private string WritePattern(HexPattern pattern)
    {
        string sig = pattern.AnglesSignature();
        if (_forceSignatures) return "_" + sig;
        if (SpecialPatternText.TryGetValue(sig, out var special)) return special;

        var (kind, def) = PatternRegistry.MatchPattern(pattern);
        if (kind != PatternMatchKind.Nothing && def != null)
        {
            string ns = PatternNames.NamespaceOf(def.Id);
            _namespaces.Add(ns);
            return ns == PatternNames.HexNamespace || _settings.AlwaysShortName ? PatternNames.ShortOf(def.Id) : def.Id;
        }
        // 特殊处理器（上游 SPECIAL_HANDLER_MAP）：数字、簿记员
        if (SpecialPatterns.TryNumber(sig, out double number))
        {
            _namespaces.Add(PatternNames.HexNamespace);
            return "num_" + NumEvaluator.DisplayMinimal(number);
        }
        if (SpecialPatterns.TryMask(pattern, out bool[] mask))
        {
            _namespaces.Add(PatternNames.HexNamespace);
            return "mask_" + new string(mask.Select(m => m ? '-' : 'v').ToArray());
        }
        return "_" + sig;
    }

    /// <summary>上游 CommentParser。</summary>
    private static string WriteComment(string content)
    {
        if (content.StartsWith("\"", StringComparison.Ordinal)) return "/*" + content.Substring(1, content.Length - 2) + "*/";
        if (IndentComment.IsMatch(content)) return content;
        if (CommentIota.IsGreatPlaceholder(content))
            return content.Substring(CommentIota.GreatPlaceholderPrefix.Length,
                content.Length - CommentIota.GreatPlaceholderPrefix.Length - CommentIota.GreatPlaceholderPostfix.Length);
        return "comment_" + content;
    }

    /// <summary>上游 VecParser：去掉末尾的 0 分量。</summary>
    private static string WriteVec(VectorIota v)
    {
        var axes = new List<double> { v.X, v.Y, v.Z };
        while (axes.Count > 0 && axes[^1] == 0) axes.RemoveAt(axes.Count - 1);
        return string.Join("_", new[] { "vec" }.Concat(axes.Select(NumEvaluator.DisplayMinimal)));
    }

    /// <summary>iota 种类的命名空间（上游 IMetaCollector：iota 类型 id 的命名空间）；本体的种类算 hexcasting。</summary>
    private static string NamespaceOfKind(Iota iota)
    {
        if (iota.Kind is IotaKind.Addon or IotaKind.Unknown
            && iota.Serialize() is Dictionary<string, object?> env
            && env.TryGetValue(IotaSerializer.KindKey, out var k) && k is string kind && kind.Contains(':'))
        {
            return PatternNames.NamespaceOf(kind);
        }
        return PatternNames.HexNamespace;
    }

    /// <summary>上游 MetaHolder.dump。</summary>
    private string DumpMeta()
    {
        _namespaces.Remove(PatternNames.HexNamespace);
        _namespaces.Remove("");
        var sb = new StringBuilder();
        if ((_settings.AttachCodeMeta & 1) > 0) sb.Append($"// Author: {_host.AuthorName}\n");
        if (_namespaces.Count > 0 && (_settings.AttachCodeMeta & 2) > 0)
            sb.Append($"// Requires: {string.Join(", ", _namespaces.OrderBy(x => x, StringComparer.Ordinal))}\n");
        return sb.ToString();
    }

    /// <summary>
    /// 上游 CommandLehmerHelper.CalcLehmer：给一个排列（如 <c>0 1 2 3 4</c> 打乱后的顺序），算出 Lehmer 码 ——
    /// 「重排之策略（swizzle）」要的那个数。最多 20 个数。
    /// </summary>
    public static long Lehmer(IReadOnlyList<int> orders)
    {
        if (orders.Count > 20) throw new HexParseException("代码过长（20）");
        long res = 0, frac = 1;
        for (int offset = 1; offset < orders.Count; offset++)
        {
            frac *= offset;
            int pos = orders.Count - 1 - offset;
            int cur = orders[pos], cnt = 0;
            for (int j = pos; j < orders.Count; j++) if (orders[j] < cur) cnt++;
            res += frac * cnt;
        }
        return res;
    }
}
