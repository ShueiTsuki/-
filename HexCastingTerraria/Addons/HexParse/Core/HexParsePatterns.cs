using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// HexParse 的 8 个图案（上游 actions/HexParsePatterns.java）：形状、官方中文名，以及不碰游戏世界的那几个行为。
/// 要碰游戏的（剪贴板、读手上的核心、解锁大法术）在 Game/HexParseActions.cs。
/// 形状由 check_patterns_vs_original.py 对拍上游。
/// </summary>
public static class HexParsePatterns
{
    public static readonly PatternData[] All =
    {
        new("hexparse:code2focus", "aqqqqqeawqwqwqwqwqwweeeeed", HexDir.East, "ActionCode2Focus"),
        new("hexparse:focus2code", "aqqqqqwwewewewewewdqeeeeed", HexDir.East, "ActionFocus2Code"),
        new("hexparse:remove_comments", "dadadedadadwqaeaqeww", HexDir.NorthEast, "ActionRemoveComments"),
        new("hexparse:learn_patterns", "aqqqqqeawqwqwqwqwqwwqqeqqeqqeqqeqqeqqdqeeeeed", HexDir.East, "ActionLearnGreatPatterns"),
        new("hexparse:create_linebreak", "dadadedadaddwwwa", HexDir.NorthEast, "ActionCreateLineBreak"),
        new("hexparse:donate", "qqdqqdqqqqqdqqdqqew", HexDir.East, "ActionDonate"),
        new("hexparse:compile", "aqqqqqeawqwqwqwqwqwdeweweqeweweqewewe", HexDir.East, "ActionCompile"),
        new("hexparse:switch_comment", "adadaqadadaawwqde", HexDir.SouthEast, "ActionCommentSwitcher"),
    };

    /// <summary>官方中文名（jar 里的 assets/hexparse/lang/zh_cn.json）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["hexparse:focus2code"] = "编码之策略",
        ["hexparse:code2focus"] = "解码之策略",
        ["hexparse:learn_patterns"] = "内化卓越法术",
        ["hexparse:remove_comments"] = "压缩注释之纯化",
        ["hexparse:create_linebreak"] = "换行之纯化",
        ["hexparse:compile"] = "编译术",
        ["hexparse:switch_comment"] = "注释转换",
        ["hexparse:donate"] = "捐赠",
    };
}

/// <summary>压缩注释之纯化（上游 ActionRemoveComments）：递归删掉列表里所有注释 iota（以及注释那个图案）。</summary>
public sealed class ActionRemoveComments : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        if (args[0] is not ListIota list) throw new MishapInvalidIota(args[0], "列表");
        return new Iota[] { Filter(list) };
    }

    private static ListIota Filter(ListIota target)
    {
        var res = new List<Iota>();
        foreach (var sub in target.Items)
        {
            if (sub is ListIota inner) { res.Add(Filter(inner)); continue; }
            if (sub is CommentIota) continue;
            if (sub is PatternIota p && p.Pattern.AnglesSignature() == CommentIota.CommentPattern.AnglesSignature()) continue;
            res.Add(sub);
        }
        return new ListIota(res);
    }
}

/// <summary>换行之纯化（上游 ActionCreateLineBreak）：压一个「换行 + N 个空格」的注释 iota。</summary>
public sealed class ActionCreateLineBreak : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        int n = CastingEnvironment.RequireIndex(args[0]);
        // 上游 " ".repeat(负数) 直接抛异常 -> 内部错误事故
        if (n < 0) throw new MishapInternalException(new System.ArgumentException("count is negative: " + n));
        return new Iota[] { CommentIota.Tab(n) };
    }
}

/// <summary>捐赠（上游 ActionDonate）：花掉 |x| 个紫水晶粉的媒质，什么都不做。</summary>
public sealed class ActionDonate : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        double dusts = System.Math.Abs(CastingEnvironment.RequireDouble(args[0], "数"));
        return new SpellResult { Effect = Nothing.Instance, Cost = (long)(dusts * MediaConstants.DustUnit) };
    }

    private sealed class Nothing : IRenderedSpell
    {
        public static readonly Nothing Instance = new();

        public CastingImage? Cast(CastingEnvironment env, CastingImage image) => null;
    }
}

/// <summary>
/// 编译术 / 注释转换：上游没装 MoreIotas 时注册的就是这个空动作（CommentIotaType.NULL_ACTION）——
/// 不动栈、不计步数、没有声音。泰拉侧没有 MoreIotas（字符串 iota），所以一直是它。
/// </summary>
public sealed class ActionNull : IAction
{
    public static readonly ActionNull Instance = new();

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => new(image, System.Array.Empty<OperatorSideEffect>(), continuation, EvalSound.Nothing);
}
