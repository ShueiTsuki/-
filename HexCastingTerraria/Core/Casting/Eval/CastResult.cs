using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 对施法 VM 做一次操作的结果。
/// 移植自 at.petrak.hexcasting.api.casting.eval.CastResult。
///
/// 注意设计要点（spec 7.3 第③条）：
/// <see cref="NewData"/> 为 **null** 表示「不采纳本次状态更新」，
/// 主循环依靠这个语义决定是否用结果替换当前 image。
/// 若改成「就地修改 CastingImage」，括号状态会错乱。
/// </summary>
public sealed class CastResult
{
    /// <summary>产生此结果的那个被执行的 iota。</summary>
    public Iota Cast { get; }

    /// <summary>接下来要继续执行的续延。</summary>
    public SpellContinuation Continuation { get; }

    /// <summary>新的 VM 状态；null 表示本次不采纳（例如 mishap 时）。</summary>
    public CastingImage? NewData { get; }

    public IReadOnlyList<OperatorSideEffect> SideEffects { get; }

    public ResolvedPatternType ResolutionType { get; }

    public EvalSound Sound { get; }

    public CastResult(
        Iota cast,
        SpellContinuation continuation,
        CastingImage? newData,
        IReadOnlyList<OperatorSideEffect> sideEffects,
        ResolvedPatternType resolutionType,
        EvalSound sound)
    {
        Cast = cast;
        Continuation = continuation;
        NewData = newData;
        SideEffects = sideEffects;
        ResolutionType = resolutionType;
        Sound = sound;
    }

    /// <summary>返回一份副本，替换指定字段（对应 Kotlin 的 copy）。</summary>
    public CastResult With(
        Iota? cast = null,
        SpellContinuation? continuation = null,
        CastingImage? newData = null,
        bool keepNewData = true,
        IReadOnlyList<OperatorSideEffect>? sideEffects = null,
        ResolvedPatternType? resolutionType = null,
        EvalSound? sound = null)
    {
        return new CastResult(
            cast ?? Cast,
            continuation ?? Continuation,
            keepNewData ? (newData ?? NewData) : null,
            sideEffects ?? SideEffects,
            resolutionType ?? ResolutionType,
            sound ?? Sound);
    }
}
