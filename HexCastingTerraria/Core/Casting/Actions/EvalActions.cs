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
/// if：三目运算。吃 (条件:bool, 真值, 假值)，吐选中的那个。
/// 移植自 common/casting/actions/math/logic/OpBoolIf.kt（argc = 3）。
/// </summary>
public sealed class OpBoolIf : ConstMediaAction
{
    public override int Argc => 3;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        if (args[0] is not BooleanIota cond)
        {
            throw new MishapInvalidIota(args[0], InvalidValue.Boolean);
        }
        return new[] { cond.Value ? args[1] : args[2] };
    }
}

/// <summary>
/// for_each（Thoth）：对数据列表逐项执行代码列表。
/// 移植自 common/casting/actions/eval/OpForEach.kt。
///
/// 栈顺序：**倒数第二项是代码(instrs)，栈顶是数据(datums)**。
/// 它不自己迭代，而是压入一个 <see cref="FrameForEach"/> 交给 VM 主循环 ——
/// 这正是我之前实现的高危点①帧的用武之地。
/// </summary>
public sealed class OpForEach : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);

        if (stack.Count < 2)
        {
            throw new MishapNotEnoughArgs(2, stack.Count);
        }

        var instrs = AsList(stack[stack.Count - 2]);
        var datums = AsList(stack[stack.Count - 1]);
        stack.RemoveAt(stack.Count - 1);
        stack.RemoveAt(stack.Count - 1);

        var frame = new FrameForEach(datums, instrs, null, Array.Empty<Iota>());
        var image2 = image.WithUsedOp().WithStack(stack);

        return new OperationResult(
            image2,
            Array.Empty<OperatorSideEffect>(),
            continuation.PushFrame(frame),
            EvalSound.Thoth);
    }

    private static SpellList AsList(Iota iota)
    {
        if (iota is not ListIota list)
        {
            throw new MishapInvalidIota(iota, InvalidValue.List);
        }
        return new SpellList.LList(0, list.Items);
    }
}

/// <summary>
/// halt：跳到最近的元求值末尾（丢弃 Evaluate 帧直到遇到边界帧）。
/// 移植自 common/casting/actions/eval/OpHalt.kt。
///
/// 若走到底都没碰到边界（说明整体被清空），则把栈清空以强制结束施法。
/// </summary>
public sealed class OpHalt : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var newStack = new List<Iota>(image.Stack);
        bool done = false;
        var newCont = continuation;

        while (!done && newCont is SpellContinuation.NotDone notDone)
        {
            var (stop, stack) = notDone.Frame.BreakDownwards(newStack);
            done = stop;
            newStack = stack;
            newCont = notDone.Next;
        }

        if (!done)
        {
            // 没有遇到任何边界帧：清空栈以退出
            newStack = new List<Iota>();
        }

        var image2 = image.WithUsedOp().WithStack(newStack);
        return new OperationResult(image2, Array.Empty<OperatorSideEffect>(), newCont, EvalSound.Spell);
    }
}

/// <summary>
/// thanos：把「剩余算力」压栈，供法术自查预算。
/// 移植自 common/casting/actions/eval/OpThanos.kt。
/// </summary>
public sealed class OpThanos : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        long opsLeft = env.MaxOpCount() - image.OpsConsumed;
        var stack = new List<Iota>(image.Stack) { new DoubleIota(opsLeft) };
        var image2 = image.WithUsedOp().WithStack(stack);
        return new OperationResult(image2, Array.Empty<OperatorSideEffect>(), continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// 元求值：把栈顶的值当作「一串图案」来执行。
/// 移植自源项目 OpEval.kt。
///
/// 栈顶是列表 → 求值列表里的每一条；
/// 栈顶是可执行 iota（图案 / 跳转目标）→ 求值这一条；
/// 其它 → mishap（源项目 evaluatable() 的分支）。
/// </summary>
public sealed class OpEval : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(1, 0), image);
        }

        var iota = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        return Exec(env, image, continuation, stack, iota);
    }

    /// <summary>
    /// 求值主体。`eval/cc` 直接复用它（源项目 OpEvalBreakable 就是调 OpEval.exec）。
    /// </summary>
    public static OperationResult Exec(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation,
        List<Iota> newStack, Iota iota)
    {
        env.DebugObserver?.OnEval();

        SpellList instrs;
        bool single;

        if (iota is ListIota li)
        {
            instrs = new SpellList.LList(0, li.Items);
            single = false;
        }
        else if (iota.IsExecutable)
        {
            instrs = new SpellList.LList(0, new Iota[] { iota });
            single = true;
        }
        else
        {
            return OperationResult.Fail(
                new MishapInvalidIota(iota, InvalidValue.Evaluatable), image);
        }

        // 源项目注释：「求值单条图案时不要制造断点」——否则 halt 的语义会变。
        // 另外若已经处在一个 FrameFinishEval 里，也不再叠加断点。
        bool alreadyAtBreak = continuation is SpellContinuation.NotDone notDone
                              && notDone.Frame is FrameFinishEval;
        var newCont = single || alreadyAtBreak
            ? continuation
            : continuation.PushFrame(FrameFinishEval.Instance);

        var frame = new FrameEvaluate(instrs, isMetacasting: true);
        var image2 = image.WithUsedOp().WithStack(newStack);

        return new OperationResult(
            image2,
            System.Array.Empty<OperatorSideEffect>(),
            newCont.PushFrame(frame),
            EvalSound.Hermes);
    }
}

/// <summary>
/// `eval/cc`：先把「当前续延」压上栈，再执行 eval。
/// 被求值的代码于是能把续延取出来，这是闭包与非局部跳转的基础。
/// 移植自 OpEvalBreakable.kt。
/// </summary>
public sealed class OpEvalBreakable : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(1, 0), image);
        }

        var iota = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);

        // 捕获的是**当前**续延（还没压入任何帧的那个）
        stack.Add(new ContinuationIota(continuation));
        return OpEval.Exec(env, image, continuation, stack, iota);
    }
}

/// <summary>
/// `undo`：在括号内撤销上一笔；不在括号内则报 MishapNeedsParens。
/// 移植自 OpUndo.kt。
///
/// 两个照抄源项目的细节（写错会让括号计数错乱）：
///   ① 括号列表**已空**时 parenCount 直接归零，而不是减一
///      —— 因为可能一开始就是「开 n 个括号」的状态，减一会漏掉；
///   ② 撤销「未被转义的开括号」要减计数、撤销「未被转义的闭括号」要加计数；
///      被转义过的括号不影响计数（插入的括号不该改计数）。
/// </summary>
public sealed class OpUndo : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => OperationResult.Fail(new MishapNeedsParens(), image);

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
    {
        var parens = new List<ParenthesizedIota>(image.Parenthesized);
        ParenthesizedIota? last = parens.Count > 0 ? parens[parens.Count - 1] : null;
        if (parens.Count > 0) parens.RemoveAt(parens.Count - 1);

        int parenCount = image.ParenCount;
        if (last == null)
        {
            parenCount = 0;
        }
        else if (last.Value.Iota is PatternIota pi && !last.Value.Escaped)
        {
            string sig = pi.Pattern.AnglesSignature();
            if (sig == ParenSignatures.Open) parenCount--;
            else if (sig == ParenSignatures.Close) parenCount++;
        }

        var image2 = image.WithUsedOp().WithParenthesized(parens).WithParenCount(parenCount);
        return new ParenthesizedOperationResult(
            image2,
            System.Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Undone);
    }
}

/// <summary>
/// 开 / 闭括号图案的角度签名，供 <see cref="OpUndo"/> 判断该往哪个方向修正括号计数。
/// 从注册表按 Id 取，避免把签名硬编码成第二份真相。
/// </summary>
internal static class ParenSignatures
{
    private static string? _open;
    private static string? _close;

    private static void Ensure()
    {
        if (_open != null) return;
        PatternRegistry.EnsureLoaded();
        foreach (var def in PatternRegistry.All)
        {
            if (def.Id == "hexcasting:open_paren") _open = def.Angles;
            else if (def.Id == "hexcasting:close_paren") _close = def.Angles;
        }
        _open ??= "";
        _close ??= "";
    }

    public static string Open { get { Ensure(); return _open!; } }
    public static string Close { get { Ensure(); return _close!; } }
}

/// <summary>求值类图案的注册（对应 HexActions.java 的 eval 段）。</summary>
public static class EvalActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:if", new OpBoolIf());
        PatternRegistry.RegisterAction("hexcasting:for_each", new OpForEach());
        PatternRegistry.RegisterAction("hexcasting:halt", new OpHalt());
        PatternRegistry.RegisterAction("hexcasting:thanatos", new OpThanos());

        // 元求值三件套：
        //   eval    求值栈顶的值
        //   eval/cc 先压入当前续延再求值（闭包的基础）
        //   undo    括号内撤销上一笔
        // 注意 eval/cc 在注册表里的 Id 是 "hexcasting:eval/cc"（源项目常量名是 EVAL$CC）。
        PatternRegistry.RegisterAction("hexcasting:eval", new OpEval());
        PatternRegistry.RegisterAction("hexcasting:eval/cc", new OpEvalBreakable());
        PatternRegistry.RegisterAction("hexcasting:undo", new OpUndo());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
