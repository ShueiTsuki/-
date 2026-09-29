using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.SideEffects;

/// <summary>
/// 施法完成后发生的副作用。
/// 移植自 at.petrak.hexcasting.api.casting.eval.sideeffects.OperatorSideEffect。
/// </summary>
public abstract class OperatorSideEffect
{
    /// <summary>执行该副作用。</summary>
    public abstract void PerformEffect(CastingVM harness);
}

/// <summary>扣除媒质。真正的扣除发生在副作用阶段（试算阶段不扣）。</summary>
public sealed class ConsumeMediaSideEffect : OperatorSideEffect
{
    public long Amount { get; }

    public ConsumeMediaSideEffect(long amount) => Amount = amount;

    public override void PerformEffect(CastingVM harness)
    {
        harness.Env.ExtractMedia(Amount, simulate: false);
    }
}

/// <summary>触发一次 mishap：改栈 + 播放效果（效果部分待接世界）。</summary>
public sealed class DoMishapSideEffect : OperatorSideEffect
{
    public Mishap Mishap { get; }
    public MishapContext ErrorCtx { get; }

    public DoMishapSideEffect(Mishap mishap, MishapContext errorCtx)
    {
        Mishap = mishap;
        ErrorCtx = errorCtx;
    }

    public override void PerformEffect(CastingVM harness)
    {
        var stack = new List<Iota>(harness.Image.Stack);
        var newStack = Mishap.ExecuteReturnStack(harness.Env, ErrorCtx, stack);
        harness.SetImage(harness.Image.WithStack(newStack));
    }
}

/// <summary>
/// 由某个法术执行世界效果（移动、传送、爆炸等）。
/// 对应源项目的 AttemptSpell。
/// </summary>
public sealed class AttemptSpellSideEffect : OperatorSideEffect
{
    public IRenderedSpell Spell { get; }
    public bool AwardStat { get; }

    public AttemptSpellSideEffect(IRenderedSpell spell, bool awardStat = true)
    {
        Spell = spell;
        AwardStat = awardStat;
    }

    public override void PerformEffect(CastingVM harness)
    {
        var updated = Spell.Cast(harness.Env, harness.Image);
        if (updated != null)
        {
            harness.SetImage(updated);
        }
    }
}

/// <summary>
/// 会作用于世界的法术。对应源项目 RenderedSpell。
/// 返回 null 表示不修改 VM 状态。
/// </summary>
public interface IRenderedSpell
{
    CastingImage? Cast(CastingEnvironment env, CastingImage image);
}
