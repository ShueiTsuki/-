using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// 认知危害 iota（上游 casting/iotas/CognitohazardIota.kt）：被调试器登记到就结束调试；平时求值什么都不做。
/// </summary>
public sealed class CognitohazardIota : Iota
{
    public const string KindTag = "hexdebug:cognitohazard";

    public override IotaKind Kind => IotaKind.Addon;

    public override string TypeName => "cognitohazard";

    public override bool IsExecutable => true;

    public override bool IsTruthy() => true;

    /// <summary>上游 toleratesOther = typesMatch：所有认知危害都相等。</summary>
    public override bool ValueEquals(Iota other) => other is CognitohazardIota;

    public override object? Serialize() => IotaSerializer.Envelope(KindTag, "");

    public static Iota? Read(object? payload) => new CognitohazardIota();

    /// <summary>上游 hexdebug.tooltip.cognitohazard_iota。</summary>
    protected override string DescribeValue() => "认知危害";

    /// <summary>上游 DISPLAY：黑色。</summary>
    public override DisplayText DisplayRich() => DisplayText.Literal("认知危害", McColors.Black);

    public override CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => new(this, continuation, null, Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Evaluated, EvalSound.Nothing);
}

/// <summary>认知危害之精思：压一个认知危害。</summary>
public sealed class OpCognitohazard : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env) => new Iota[] { new CognitohazardIota() };
}

/// <summary>调试杖之精思（上游 OpIsDebugging）：这次施法是不是正在被调试。</summary>
public sealed class OpIsDebugging : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { env.DebugObserver is DebugEnvironment ? BooleanIota.True : BooleanIota.False };
}

/// <summary>在前方 / 后方添加断点（上游 OpBreakpoint）：压一个断点帧，不调试时没有效果。</summary>
public sealed class OpBreakpoint : IAction
{
    private readonly bool _stopBefore;

    public OpBreakpoint(bool stopBefore) => _stopBefore = stopBefore;

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => new(image.WithUsedOp(), Array.Empty<OperatorSideEffect>(), continuation.PushFrame(new FrameBreakpoint(_stopBefore)), EvalSound.NormalExecute);
}
