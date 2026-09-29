using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// 开括号：进入列表构建模式。
/// 移植自 common/casting/actions/escaping/OpOpenParen.kt。
///
/// 两种情形（源项目同）：
///   - 括号外执行：parenCount + 1，消耗 1 op
///   - 括号内执行（本来就在建列表）：把本图案也记进列表，并再 +1（嵌套列表）
/// </summary>
public sealed class OpOpenParen : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => new OperationResult(
            image.WithUsedOp().WithParenCount(image.ParenCount + 1),
            Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute);

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
    {
        var image2 = image.WithNewParenthesized(thisIota, escaped: false)
                          .WithParenCount(image.ParenCount + 1);

        return new ParenthesizedOperationResult(
            image2, Array.Empty<OperatorSideEffect>(), continuation,
            EvalSound.NormalExecute, ResolvedPatternType.Escaped);
    }
}

/// <summary>
/// 闭括号：结束列表构建。
/// 移植自 common/casting/actions/escaping/OpCloseParen.kt。
///
/// 关键：**括号外执行会抛 MishapNeedsParens**（"你画了闭括号但没开"）。
/// 括号内执行时 parenCount - 1；减到 0 就把累积的列表压栈并清空 parens，
/// 否则（形如 "(()"）把本图案也记进列表并保持 ESCAPED。
/// </summary>
public sealed class OpCloseParen : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => throw new MishapNeedsParens();

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
    {
        int newParenCount = image.ParenCount - 1;

        if (newParenCount == 0)
        {
            // 把累积的全部 iota 作为一个列表压栈
            var items = new List<Iota>(image.Parenthesized.Count);
            for (int i = 0; i < image.Parenthesized.Count; i++)
            {
                items.Add(image.Parenthesized[i].Iota);
            }

            var stack = new List<Iota>(image.Stack) { new ListIota(items) };

            var image2 = image.WithUsedOp()
                              .WithStack(stack)
                              .WithParenCount(0)
                              .WithClearedParenthesized();

            return new ParenthesizedOperationResult(
                image2, Array.Empty<OperatorSideEffect>(), continuation,
                EvalSound.NormalExecute, ResolvedPatternType.Evaluated);
        }

        // 形如 "(()"：本图案也要进列表
        var image3 = image.WithNewParenthesized(thisIota, escaped: false)
                          .WithParenCount(newParenCount);

        return new ParenthesizedOperationResult(
            image3, Array.Empty<OperatorSideEffect>(), continuation,
            EvalSound.NormalExecute, ResolvedPatternType.Escaped);
    }
}

/// <summary>
/// Consideration（转义）：让**下一个** iota 不被执行，而是作为值入栈。
/// 移植自 common/casting/actions/escaping/OpEscape.kt。
///
/// 这是"快照"能力的实现基础 —— 把图案本身当数据用，而不是执行它。
/// </summary>
public sealed class OpEscape : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => new OperationResult(
            image.WithUsedOp().WithEscapeNext(true),
            Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute);

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
        => new ParenthesizedOperationResult(
            image.WithUsedOp().WithEscapeNext(true),
            Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Evaluated);
}

/// <summary>空列表：压入一个 0 元素的列表。源：OpEmptyList。</summary>
public sealed class OpEmptyList : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { new ListIota(Array.Empty<Iota>()) };
}

/// <summary>单元素列表：把栈顶包成一个 1 元素列表。源：OpSingleton。</summary>
public sealed class OpSingleton : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { new ListIota(new[] { args[0] }) };
}

/// <summary>
/// 展开列表：把列表的各个元素摊到栈上（逆操作于构建列表）。源：OpSplat。
/// </summary>
public sealed class OpSplat : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        if (args[0] is not ListIota list)
        {
            throw new MishapInvalidIota(args[0], "list");
        }

        var outList = new List<Iota>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            outList.Add(list.Items[i]);
        }
        return outList;
    }
}

/// <summary>括号与控制流图案的注册。</summary>
public static class ListActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // 括号与转义（对应 HexActions.java 的 escaping 段）
        PatternRegistry.RegisterAction("hexcasting:open_paren", new OpOpenParen());
        PatternRegistry.RegisterAction("hexcasting:close_paren", new OpCloseParen());
        PatternRegistry.RegisterAction("hexcasting:escape", new OpEscape());

        // 列表构造
        PatternRegistry.RegisterAction("hexcasting:empty_list", new OpEmptyList());
        PatternRegistry.RegisterAction("hexcasting:singleton", new OpSingleton());
        PatternRegistry.RegisterAction("hexcasting:splat", new OpSplat());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
