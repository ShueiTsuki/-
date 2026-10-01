using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 注释 iota（上游 hooks/CommentIota.java + CommentIotaType.java）。
///
/// - 存的是一段文字；执行时什么都不做（Escaped，不压栈、不出声）；真假值为假
/// - 三种写法共用这一种 iota：
///   <c>comment_xxx</c>（普通注释）、<c>"…"</c> 开头（代码里的 // /* */ 注释，带引号保存）、
///   <c>\n + N 个空格</c>（换行缩进 tab_N）、<c>&lt;id?&gt;</c>（没解锁的大法术占位）
/// - 种类标签 <see cref="Kind"/>；HexParse 关着时它由 UnknownIota 原样保管
/// </summary>
public sealed class CommentIota : Iota
{
    public const string KindTag = "hexparse:comment";

    /// <summary>上游 CommentIotaType.COMMENT_PATTERN：「压缩注释之纯化」也会顺手删掉这个图案。</summary>
    public static readonly HexPattern CommentPattern = MakeCommentPattern();

    public const string GreatPlaceholderPrefix = "<";
    public const string GreatPlaceholderPostfix = "?>";

    public CommentIota(string comment) => Comment = comment;

    public string Comment { get; }

    public override IotaKind Kind => IotaKind.Addon;

    public override string TypeName => "comment";

    public override bool ValueEquals(Iota other) => other is CommentIota c && c.Comment == Comment;

    public override object? Serialize() => IotaSerializer.Envelope(KindTag, Comment);

    /// <summary>读取器：给 IotaSerializer.RegisterKind 用。载荷必须是字符串。</summary>
    public static Iota? Read(object? payload) => payload is string s ? new CommentIota(s) : null;

    public static bool IsGreatPlaceholder(string s)
        => s.StartsWith(GreatPlaceholderPrefix, System.StringComparison.Ordinal) && s.EndsWith(GreatPlaceholderPostfix, System.StringComparison.Ordinal);

    public static string MakeGreatPlaceholder(string id) => GreatPlaceholderPrefix + id + GreatPlaceholderPostfix;

    /// <summary>换行缩进：一个换行 + N 个空格（上游 IotaFactory.makeTab）。</summary>
    public static CommentIota Tab(int n) => new("\n" + new string(' ', System.Math.Max(0, n)));

    /// <summary>上游 display：代码注释去掉两边的引号；其余原样（上游是深绿色，大法术占位另有滚动效果）。</summary>
    protected override string DescribeValue()
    {
        if (!IsGreatPlaceholder(Comment) && Comment.Length > 1 && Comment[0] == '"')
            return Comment.Substring(1, Comment.Length - 2);
        return Comment;
    }

    /// <summary>
    /// 上游 CommentIotaType.display：注释深绿（代码注释去掉两边引号，按住 Shift 时不显示）；
    /// 大法术占位用图案的金色，每隔一阵从左到右滚过一遍随机的大法术名（浅紫），提示「这里有个没解锁的大法术」。
    /// </summary>
    public override DisplayText DisplayRich()
    {
        string raw = Comment;
        if (!IsGreatPlaceholder(raw))
        {
            if (IotaDisplay.ShiftDown?.Invoke() == true) return DisplayText.Empty();
            return DisplayText.Literal(DescribeValue(), McColors.DarkGreen);
        }
        int len = raw.Length;
        int loopSize = (int)System.Math.Floor(len * System.Math.PI * 2);
        long ticker = IotaDisplay.Millis() / 20;
        int looper = (int)(ticker % loopSize);
        if (looper >= len * 2) return DisplayText.Literal(raw, McColors.Gold);
        ticker -= looper;
        string filled = MakeGreatPlaceholder(RandomGreatKey(ticker, len - GreatPlaceholderPrefix.Length - GreatPlaceholderPostfix.Length));
        if (looper <= len)
        {
            return DisplayText.Empty().Append(DisplayText.Literal(filled.Substring(0, looper), McColors.LightPurple))
                .Append(DisplayText.Literal(raw.Substring(looper), McColors.Gold));
        }
        looper -= len;
        return DisplayText.Empty().Append(DisplayText.Literal(raw.Substring(0, looper), McColors.Gold))
            .Append(DisplayText.Literal(filled.Substring(looper), McColors.LightPurple));
    }

    private static string? _greatKeys;

    /// <summary>上游 pickRandomGreatPatternKey：所有大法术 id 的路径各放三份、打乱、用「---」连起来，从 from 处截 size 个字（循环）。</summary>
    private static string RandomGreatKey(long from, int size)
    {
        if (_greatKeys is null)
        {
            var keys = new System.Collections.Generic.List<string>();
            foreach (var def in global::HexCastingTerraria.Core.Registry.PatternRegistry.All)
            {
                if (!global::HexCastingTerraria.Core.Registry.PatternRegistry.IsPerWorld(def)) continue;
                string path = def.Id.Substring(def.Id.IndexOf(':') + 1);
                for (int i = 0; i < 3; i++) keys.Add(path);
            }
            var rng = new System.Random();
            for (int i = keys.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (keys[i], keys[j]) = (keys[j], keys[i]);
            }
            _greatKeys = string.Concat(keys.ConvertAll(k => k + "---"));
            if (_greatKeys.Length == 0) _greatKeys = "---";
        }
        var all = _greatKeys;
        size = System.Math.Max(0, size);
        var sb = new System.Text.StringBuilder(size);
        int start = (int)(from % all.Length);
        for (int i = 0; i < size; i++) sb.Append(all[(start + i) % all.Length]);
        return sb.ToString();
    }

    /// <summary>上游 execute：什么都不做，结果 Escaped，没有声音。</summary>
    public override CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => new(this, continuation, null, System.Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Escaped, EvalSound.Nothing);

    private static HexPattern MakeCommentPattern()
    {
        HexPattern.TryFromAnglesUnchecked("adadaqadadaaww", HexDir.SouthEast, out var p, out _);
        return p!;
    }
}
