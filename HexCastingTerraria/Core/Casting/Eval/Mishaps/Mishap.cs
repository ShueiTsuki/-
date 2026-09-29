using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

/// <summary>
/// mishap 的上下文：出错的图案与（可能的）图案名。
/// 移植自 at.petrak.hexcasting.api.casting.mishaps.Mishap.Context。
/// </summary>
public sealed class MishapContext
{
    public HexPattern? Pattern { get; }
    public string? Name { get; }

    public MishapContext(HexPattern? pattern, string? name)
    {
        Pattern = pattern;
        Name = name;
    }
}

/// <summary>
/// 咒法学里的「错误」。命名沿用原作的 mishap。
/// 移植自 at.petrak.hexcasting.api.casting.mishaps.Mishap。
///
/// ⚠️ 注意：mishap 是**异常**，但它的作用是「反噬」——
/// 由 DoMishap 副作用调用 <see cref="Execute"/> 来修改栈并产生游戏效果。
/// </summary>
public abstract class Mishap : Exception
{
    protected Mishap() { }

    protected Mishap(string message) : base(message) { }

    /// <summary>
    /// 执行实际效果（不只是特效用），可以修改栈。
    /// 对应源项目的 execute。
    /// </summary>
    public abstract void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack);

    /// <summary>解析状态，默认 Errored。少数 mishap 会覆盖（如 ItemTooFarAway 相关）。</summary>
    public virtual ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Errored;

    /// <summary>执行并返回修改后的栈。</summary>
    public List<Iota> ExecuteReturnStack(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        Execute(env, errorCtx, stack);
        return stack;
    }

    /// <summary>错误消息（用于聊天栏显示）。返回 null 表示不显示。</summary>
    protected abstract string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx);

    public string? ErrorMessageWithName(CastingEnvironment env, MishapContext errorCtx)
    {
        var msg = ErrorMessage(env, errorCtx);
        if (msg == null)
        {
            return null;
        }
        return errorCtx.Name != null ? $"「{errorCtx.Name}」：{msg}" : msg;
    }
}
