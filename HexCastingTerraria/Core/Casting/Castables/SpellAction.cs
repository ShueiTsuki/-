using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Castables;

/// <summary>
/// 会**作用于世界**的图案的行为基类。
/// 移植自源项目 `SpellAction`。
///
/// 与 <see cref="ConstMediaAction"/> 的关键区别：**施法是延迟的**。
/// 这里只负责算出「效果」与「媒质消耗」；真正的世界改动
/// （推动实体、传送…）由 <see cref="AttemptSpellSideEffect"/> 在
/// **媒质确认扣除之后**才执行。
///
/// 为什么必须分两段：如果先推动再扣媒质，媒质不足时实体的速度已经改了、
/// 却不会回滚 —— 玩家能白嫖位移。源项目为此专门拆出 AttemptSpell。
/// </summary>
public abstract class SpellAction : IAction
{
    /// <summary>需要的参数个数。</summary>
    public abstract int Argc { get; }
    /// <summary>
    /// 参数类型契约。见 <see cref="ActionTypes"/>。
    ///
    /// 注意：与 <see cref="ConstMediaAction.Types"/> 同样的理由：必须声明成 virtual，
    /// 只靠接口的默认实现不会被派生类重新绑定。
    ///
    /// 另外注意：SpellAction 的净栈效果是 **-Argc**（弹出 Argc 个、什么都不压回），
    /// 而不是 `1 - Argc` —— 类型检查器按前者算，它曾经按后者算过，是一处"手抄模型"。
    /// </summary>
    public virtual ActionTypes Types => ActionTypes.Unknown;

    /// <summary>是否播放施法音效。为 false 时求值音效是 Mute。</summary>
    public virtual bool HasCastingSound(CastingEnvironment env) => true;

    /// <summary>是否计入施法统计。泰拉侧暂未接统计系统，保留接口以对齐行为。</summary>
    public virtual bool AwardsCastingStat(CastingEnvironment env) => true;

    /// <summary>算出本次法术的效果、消耗与粒子。</summary>
    public abstract SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env);

    /// <summary>
    /// 需要「跨图案临时数据」的法术覆写它（例如 `add_motion` 的防刷计数）。
    /// 默认忽略数据袋，直接走 <see cref="Execute"/>。
    /// </summary>
    public virtual SpellResult ExecuteWithUserdata(
        IReadOnlyList<Iota> args, CastingEnvironment env, CastUserData userData)
        => Execute(args, env);

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);

        if (Argc > stack.Count)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(Argc, stack.Count), image);
        }

        var args = new ArgReads(Argc);
        for (int i = stack.Count - Argc; i < stack.Count; i++)
        {
            args.Add(stack[i]);
        }
        stack.RemoveRange(stack.Count - Argc, Argc);

        // 复制一份数据袋再交给法术改（源项目 image.userData.copy()）。
        // 直接改原袋会回溯污染上一个 image，mishap 回滚时数据已经变了。
        var userData = image.UserData.Clone();

        SpellResult result;
        try
        {
            result = ExecuteWithUserdata(args, env, userData);
        }
        catch (Mishap m)
        {
            // 参数类型不对：指向最后读过的那个参数（见 ArgReads；法术这条入口曾经漏了，2026-10-02 和原版对拍时发现）
            if (m is MishapInvalidIota { ReverseIdx: null } bad && args.LastReadIndexOf(bad.Perpetrator) is var i and >= 0)
            {
                m = bad.At(Argc - 1 - i);
            }
            // 与 ConstMediaAction 一致：mishap 走结果通道，不抛到日志
            return OperationResult.Fail(m, image);
        }

        // 媒质预检必须在收集效果之前 —— 不足就整个中止，不留半个效果
        if (env.ExtractMedia(result.Cost, simulate: true) > 0)
        {
            return OperationResult.Fail(new MishapNotEnoughMedia(result.Cost), image);
        }

        var sideEffects = new List<OperatorSideEffect>();

        if (result.Cost > 0)
        {
            sideEffects.Add(new ConsumeMediaSideEffect(result.Cost));
        }

        // 顺序要紧：先扣媒质，再施放。反过来就能白嫖。
        sideEffects.Add(new AttemptSpellSideEffect(result.Effect, result.AwardsCastingStat ?? AwardsCastingStat(env)));

        for (int i = 0; i < result.Particles.Count; i++)
        {
            sideEffects.Add(new ParticlesSideEffect(result.Particles[i]));
        }

        var image2 = image.WithStack(stack).WithUsedOps(result.OpCount).WithUserData(userData);
        var sound = HasCastingSound(env) ? EvalSound.Spell : EvalSound.Mute;

        return new OperationResult(image2, sideEffects, continuation, sound);
    }
}

/// <summary>
/// 一次法术的求值结果。移植自源项目 `SpellAction.Result`。
/// </summary>
public sealed class SpellResult
{
    /// <summary>要施放的效果。</summary>
    public required IRenderedSpell Effect { get; init; }

    /// <summary>媒质消耗。</summary>
    public required long Cost { get; init; }

    /// <summary>要播放的粒子。</summary>
    public IReadOnlyList<ParticleSpray> Particles { get; init; } = System.Array.Empty<ParticleSpray>();

    /// <summary>消耗的求值步数。</summary>
    public long OpCount { get; init; } = 1;

    /// <summary>覆盖「是否计入统计」；null 表示用图案自己的默认值。</summary>
    public bool? AwardsCastingStat { get; init; }
}
