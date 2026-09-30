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

    /// <summary>上游 execute：什么都不做，结果 Escaped，没有声音。</summary>
    public override CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => new(this, continuation, null, System.Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Escaped, EvalSound.Nothing);

    private static HexPattern MakeCommentPattern()
    {
        HexPattern.TryFromAnglesUnchecked("adadaqadadaaww", HexDir.SouthEast, out var p, out _);
        return p!;
    }
}
