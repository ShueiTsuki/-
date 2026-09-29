using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;

namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 图案执行后的通用结果接口。
/// 移植自 at.petrak.hexcasting.api.casting.eval.IOperationResult。
/// </summary>
public interface IOperationResult
{
    /// <summary>
    /// 非 null 表示本次操作**失败于 mishap**。
    ///
    /// 为什么用字段而不是抛异常：mishap 是**正常控制流**（画错图案是预期行为），
    /// 而 tModLoader 会把捕获到的每个异常连堆栈写日志 —— 高频 mishap 会把日志刷爆。
    /// 这里改为把 mishap 作为「结果的一部分」返回，调用方判定即可。
    /// </summary>
    Mishap? Error { get; }

    CastingImage NewImage { get; }
    IReadOnlyList<OperatorSideEffect> SideEffects { get; }
    SpellContinuation NewContinuation { get; }
    EvalSound Sound { get; }
}

/// <summary>普通（非括号内）执行结果。</summary>
public sealed class OperationResult : IOperationResult
{
    public Mishap? Error { get; init; }

    public CastingImage NewImage { get; }
    public IReadOnlyList<OperatorSideEffect> SideEffects { get; }
    public SpellContinuation NewContinuation { get; }
    public EvalSound Sound { get; }

    /// <summary>
    /// 构造一个「失败于 mishap」的结果：不改动 image、不产生副作用、不再继续。
    /// 调用方看到 <see cref="Error"/> 非 null 就必须立刻中止本次求值。
    /// </summary>
    public static OperationResult Fail(Mishap mishap, CastingImage image)
        => new OperationResult(
            image,
            System.Array.Empty<OperatorSideEffect>(),
            SpellContinuation.Done.Instance,
            EvalSound.Nothing)
        { Error = mishap };

    public OperationResult(
        CastingImage newImage,
        IReadOnlyList<OperatorSideEffect> sideEffects,
        SpellContinuation newContinuation,
        EvalSound sound)
    {
        NewImage = newImage;
        SideEffects = sideEffects;
        NewContinuation = newContinuation;
        Sound = sound;
    }
}

/// <summary>括号内执行结果，额外带解析状态。</summary>
public sealed class ParenthesizedOperationResult : IOperationResult
{
    public Mishap? Error { get; init; }

    public CastingImage NewImage { get; }
    public IReadOnlyList<OperatorSideEffect> SideEffects { get; }
    public SpellContinuation NewContinuation { get; }
    public EvalSound Sound { get; }
    public ResolvedPatternType ResolutionType { get; }

    public ParenthesizedOperationResult(
        CastingImage newImage,
        IReadOnlyList<OperatorSideEffect> sideEffects,
        SpellContinuation newContinuation,
        EvalSound sound,
        ResolvedPatternType resolutionType)
    {
        NewImage = newImage;
        SideEffects = sideEffects;
        NewContinuation = newContinuation;
        Sound = sound;
        ResolutionType = resolutionType;
    }
}
