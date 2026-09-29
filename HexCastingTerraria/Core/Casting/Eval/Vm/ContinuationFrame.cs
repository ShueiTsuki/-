using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// 求值过程中的一个「帧」。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.ContinuationFrame。
///
/// 求值方式：反复从续延栈顶弹出最内层帧并执行；帧可以再压入新的帧
/// （例如遇到 Hermes／Thoth）。栈空即结束。
/// </summary>
public interface IContinuationFrame
{
    /// <summary>推进一步。FrameEvaluate 表示消费一条图案；FrameForEach 表示入队下一轮迭代。</summary>
    CastResult Evaluate(SpellContinuation continuation, CastingVM harness);

    /// <summary>
    /// OpHalt 要求「跳到最近的元求值末尾」：
    /// 持续丢弃帧直到遇到 FrameFinishEval 或 FrameForEach。
    /// 返回「是否在此停止」以及新的栈状态。
    /// </summary>
    (bool Stop, List<Iota> Stack) BreakDownwards(List<Iota> stack);

    /// <summary>帧内包含的 iota 数量，用于判断是否可序列化。</summary>
    int Size();
}

/// <summary>
/// 一串待依次求值的图案。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.FrameEvaluate。
/// </summary>
public sealed class FrameEvaluate : IContinuationFrame
{
    /// <summary>**剩余**待求值的图案列表。</summary>
    public SpellList List { get; }

    /// <summary>是否由元求值（Hermes）压入。</summary>
    public bool IsMetacasting { get; }

    public FrameEvaluate(SpellList list, bool isMetacasting)
    {
        List = list;
        IsMetacasting = isMetacasting;
    }

    public (bool Stop, List<Iota> Stack) BreakDownwards(List<Iota> stack)
        => (false, stack);

    public CastResult Evaluate(SpellContinuation continuation, CastingVM harness)
    {
        if (List.NonEmpty)
        {
            // 尾调用优化：若后面还有图案，先把「剩余的图案」压入续延
            var newCont = List.Cdr.NonEmpty
                ? continuation.PushFrame(new FrameEvaluate(List.Cdr, IsMetacasting))
                : continuation;

            var update = harness.ExecuteInner(List.Car, newCont);

            // 元求值下，把音效改成 HERMES（mishap 除外）
            return IsMetacasting && update.Sound != EvalSound.Mishap
                ? update.With(sound: EvalSound.Hermes)
                : update;
        }

        // 空列表（例如空 Hermes）：直接成功返回
        return new CastResult(
            new ListIota(System.Array.Empty<Iota>()),
            continuation,
            null,
            System.Array.Empty<OperatorSideEffect>(),
            ResolvedPatternType.Evaluated,
            EvalSound.Hermes);
    }

    public int Size() => List.Size();
}

/// <summary>
/// 元求值结束的边界标记，供 OpHalt 判断何时停止丢弃帧。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.FrameFinishEval。
/// </summary>
public sealed class FrameFinishEval : IContinuationFrame
{
    public static readonly FrameFinishEval Instance = new();

    private FrameFinishEval() { }

    public (bool Stop, List<Iota> Stack) BreakDownwards(List<Iota> stack)
        => (true, stack);

    public CastResult Evaluate(SpellContinuation continuation, CastingVM harness)
        => new CastResult(
            NullIota.Instance,
            continuation,
            null,
            System.Array.Empty<OperatorSideEffect>(),
            ResolvedPatternType.Evaluated,
            EvalSound.Nothing);

    public int Size() => 0;
}
