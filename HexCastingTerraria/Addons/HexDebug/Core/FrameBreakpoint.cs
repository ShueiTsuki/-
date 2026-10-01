using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// 断点帧（上游 casting/eval/FrameBreakpoint.kt）：不调试时什么都不做；调试器看到它就停。
/// <see cref="IsFatal"/> 是调试器自己在「捕获到事故」时压的：再往下走就结束。
/// </summary>
public sealed class FrameBreakpoint : IContinuationFrame
{
    public FrameBreakpoint(bool stopBefore, bool isFatal = false)
    {
        StopBefore = stopBefore;
        IsFatal = isFatal;
    }

    public bool StopBefore { get; }

    public bool IsFatal { get; }

    public static FrameBreakpoint Fatal() => new(stopBefore: true, isFatal: true);

    public (bool Stop, List<Iota> Stack) BreakDownwards(List<Iota> stack) => (false, stack);

    public CastResult Evaluate(SpellContinuation continuation, CastingVM harness)
        => new(NullIota.Instance, continuation, null, Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Evaluated, EvalSound.Nothing);

    public int Size() => 0;
}
