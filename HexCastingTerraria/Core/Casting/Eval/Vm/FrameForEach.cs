using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// Thoth（for_each / eval_breakable）求值帧。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.FrameForEach。
///
/// 注意：高危点（spec 7.3 第①条）：
/// 本帧会**把同一份 code 反复重新执行**，且 data/acc 会被多帧共享。
/// 因此：
///   - `Data` / `Code` 必须是持久化 SpellList（不可变），不能是可变 List
///   - `Acc`（累加器）必须**追加时复制**，绝不能就地 Add
/// 否则会出现迭代丢失/重复，且只在用到 for_each 时才暴露，极难定位。
///
/// 语义：
///   1. 第一次进入时记录 baseStack（Thoth 入口的栈）
///   2. 每个 datum：把它压到 baseStack 之上，然后执行一遍 code
///   3. code 执行完后栈的**最终状态**被追加到累加器，并恢复 baseStack
///   4. data 用尽后，把「累加器构成的列表」压回 baseStack 之上
/// </summary>
public sealed class FrameForEach : IContinuationFrame
{
    /// <summary>剩余的待迭代数据。</summary>
    public SpellList Data { get; }

    /// <summary>每个 datum 要执行的代码块（**同一份会被反复执行**）。</summary>
    public SpellList Code { get; }

    /// <summary>Thoth 入口处的栈快照；null 表示尚未记录（第一次进入）。</summary>
    public IReadOnlyList<Iota>? BaseStack { get; }

    /// <summary>已收集的各次迭代最终栈状态（追加时复制，保持不可变）。</summary>
    public IReadOnlyList<Iota> Acc { get; }

    public FrameForEach(SpellList data, SpellList code, IReadOnlyList<Iota>? baseStack, IReadOnlyList<Iota> acc)
    {
        Data = data;
        Code = code;
        BaseStack = baseStack;
        Acc = acc;
    }

    /// <summary>
    /// 遇到 OpHalt 时：把当前栈并入累加器，返回「入口栈 + 累加器列表」。
    /// 返回 true 表示 halt 到此为止。
    /// </summary>
    public (bool Stop, List<Iota> Stack) BreakDownwards(List<Iota> stack)
    {
        var newStack = BaseStack != null ? new List<Iota>(BaseStack) : new List<Iota>();

        // 追加时复制（不可变语义）
        var merged = new List<Iota>(Acc);
        merged.AddRange(stack);
        newStack.Add(new ListIota(merged));

        return (true, newStack);
    }

    public CastResult Evaluate(SpellContinuation continuation, CastingVM harness)
    {
        // ---- 第 1 步：取「本次迭代的基准栈」与「累加器」----
        IReadOnlyList<Iota> stack;
        IReadOnlyList<Iota> newAcc;

        if (BaseStack == null)
        {
            // 第一次进入：基准栈 = 当前栈；累加器不变
            stack = new List<Iota>(harness.Image.Stack);
            newAcc = Acc;
        }
        else
        {
            // 后续迭代：把上一次迭代结束时的栈并入累加器，并回到入口栈
            var merged = new List<Iota>(Acc);
            merged.AddRange(harness.Image.Stack);

            stack = BaseStack;
            newAcc = merged;
        }

        // ---- 第 2 步：还有数据就再跑一轮 code，否则收尾 ----
        Iota stackTop;
        CastingImage newImage;
        SpellContinuation newCont;

        if (Data.NonEmpty)
        {
            // 注意压栈顺序：先压「下一次迭代的 ForEach 帧」，再压「要执行的 code」
            // 这样 code 先跑完，才会回到下一次迭代
            var cont2 = continuation
                .PushFrame(new FrameForEach(Data.Cdr, Code, stack, newAcc))
                .PushFrame(new FrameEvaluate(Code, isMetacasting: true));

            stackTop = Data.Car;
            newImage = harness.Image.WithUsedOp();
            newCont = cont2;
        }
        else
        {
            // 数据用尽：把累加器作为列表压回
            stackTop = new ListIota(newAcc);
            newImage = harness.Image;
            newCont = continuation;
        }

        // ---- 第 3 步：把 stackTop 放到基准栈之上 ----
        var tStack = new List<Iota>(stack) { stackTop };

        return new CastResult(
            new ListIota(Code.ToList()),
            newCont,
            // 重置转义状态，避免它泄漏到其他迭代或泄漏出 Thoth
            newImage.WithResetEscape().WithStack(tStack),
            System.Array.Empty<OperatorSideEffect>(),
            ResolvedPatternType.Evaluated,
            EvalSound.Thoth);
    }

    public int Size() => Data.Size() + Code.Size() + Acc.Count + (BaseStack?.Count ?? 0);
}
