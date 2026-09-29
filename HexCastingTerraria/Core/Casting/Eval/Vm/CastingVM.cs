using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// 施法虚拟机。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.CastingVM。
///
/// 主循环：反复弹出续延栈顶的帧执行，直到栈空或提前退出（分辨率失败）。
/// </summary>
public sealed class CastingVM
{
    /// <summary>防御性迭代上限：正常情况下由 opsConsumed 触发 MishapEvalTooMuch 终止；
    /// 此上限只用于防止续延实现出错导致死循环。</summary>
    private const int SafetyIterationLimit = 1_000_000;

    public CastingImage Image { get; private set; }
    public CastingEnvironment Env { get; }

    public CastingVM(CastingImage image, CastingEnvironment env)
    {
        Image = image;
        Env = env;
    }

    public static CastingVM Empty(CastingEnvironment env) => new CastingVM(new CastingImage(), env);

    /// <summary>供副作用（如 mishap 改栈）直接替换 image。</summary>
    public void SetImage(CastingImage image) => Image = image;

    /// <summary>
    /// 依次执行一组 iota，返回最终状态。
    /// 移植自源项目 queueExecuteAndWrapIotas（去掉了客户端描述部分）。
    /// </summary>
    public CastOutcome QueueExecute(CastingImage image, IReadOnlyList<Iota> iotas)
    {
        Image = image;

        // 初始化续延栈：一个针对全部 iota 的顶层求值帧
        var continuation = SpellContinuation.Done.Instance
            .PushFrame(new FrameEvaluate(new SpellList.LList(0, iotas), false));

        var lastResolutionType = ResolvedPatternType.Unresolved;
        bool earlyExit = false;
        int iterations = 0;

        // 本次求值产生的粒子。
        // ⚠️ 必须在这里收集：`ParticlesSideEffect.PerformEffect` 是空的
        //（Core 不能引用 Terraria），真正的生成在表现层 ——
        // 只有把「该在哪喷」带出去，客户端才有东西可画。
        // 之前没人带，所以**所有法术粒子都不显示**，而且不报错。
        var particles = new List<ParticleSpray>();

        // 本次求值要播的音效。
        // ⚠️ 和粒子同样的坑：EvalSound 一直**被算出来但没人播** ——
        // 法杖的 UseSound 又是 null（注释还写着「施法音效由 EvalSound 负责」），
        // 结果整个模组的施法**一声不响**。这里把它带出来交给表现层。
        EvalSound sound = EvalSound.Nothing;

        while (continuation is SpellContinuation.NotDone notDone && !earlyExit)
        {
            if (++iterations > SafetyIterationLimit)
            {
                throw new InvalidOperationException("施法续延未在安全迭代上限内结束，疑似续延实现有误");
            }

            var frame = notDone.Frame;
            var result = frame.Evaluate(notDone.Next, this);

            // 两道保险（对应源项目 CastingVM.kt:55-72）
            // 1. 栈过大到无法序列化 → StackSize mishap
            // 2. 求值步数超上限 → EvalTooMuch mishap
            if (result.NewData != null && IsStackTooLarge(result.NewData.Stack))
            {
                result = new CastResult(
                    result.Cast,
                    result.Continuation,
                    null,
                    new OperatorSideEffect[] { new DoMishapSideEffect(new MishapStackSize(), new MishapContext(null, null)) },
                    ResolvedPatternType.Errored,
                    EvalSound.Mishap);
            }
            else if (result.NewData != null && result.NewData.OpsConsumed > Env.MaxOpCount())
            {
                result = new CastResult(
                    result.Cast,
                    result.Continuation,
                    null,
                    new OperatorSideEffect[] { new DoMishapSideEffect(new MishapEvalTooMuch(), new MishapContext(null, null)) },
                    ResolvedPatternType.Errored,
                    EvalSound.Mishap);
            }

            // 采纳状态更新（NewData 为 null 时**不**更新，这是关键语义）
            if (result.NewData != null)
            {
                Image = result.NewData;
            }

            Env.PostExecution(result);
            continuation = result.Continuation;
            lastResolutionType = result.ResolutionType;

            if (result.Sound != EvalSound.Nothing)
            {
                sound = result.Sound;
            }

            // 先把粒子捞出来（顺序无关，但要保证 mishap 抛异常时也已经收过）
            for (int i = 0; i < result.SideEffects.Count; i++)
            {
                if (result.SideEffects[i] is ParticlesSideEffect ps)
                {
                    particles.Add(ps.Spray);
                }
            }

            try
            {
                PerformSideEffects(result.SideEffects);
            }
            catch (Exception e)
            {
                PerformSideEffects(new OperatorSideEffect[]
                {
                    new DoMishapSideEffect(new MishapInternalException(e), new MishapContext(null, null)),
                });
            }

            earlyExit = earlyExit || !lastResolutionType.IsSuccess();
        }

        if (continuation is SpellContinuation.NotDone)
        {
            lastResolutionType = lastResolutionType.IsSuccess()
                ? ResolvedPatternType.Evaluated
                : ResolvedPatternType.Errored;
        }

        Env.PostCast(Image);

        return new CastOutcome(Image, lastResolutionType, particles, sound);
    }

    /// <summary>
    /// 执行单个 iota。
    /// 移植自源项目 executeInner（CastingVM.kt:108-162）。
    ///
    /// ⚠️ 三个分支的顺序不可调换（spec 7.3 第③条）：
    /// escapeNext → inParens → 普通执行。
    /// </summary>
    public CastResult ExecuteInner(Iota iota, SpellContinuation continuation)
    {
        try
        {
            // ① 单个 iota 的转义（Consideration）：此行为不可被 iota 覆盖，故放在这里
            if (Image.EscapeNext)
            {
                if (Image.ParenCount > 0)
                {
                    // 括号内 → 记入括号列表并标记 escaped
                    var img = Image.WithEscapeNext(false).WithNewParenthesized(iota, escaped: true);
                    return new CastResult(iota, continuation, img,
                        Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Escaped, EvalSound.NormalExecute);
                }
                else
                {
                    // 不在括号内 → 直接压栈
                    var stack = new List<Iota>(Image.Stack) { iota };
                    var img = Image.WithEscapeNext(false).WithStack(stack);
                    return new CastResult(iota, continuation, img,
                        Array.Empty<OperatorSideEffect>(), ResolvedPatternType.Escaped, EvalSound.NormalExecute);
                }
            }

            // ② 括号内执行
            if (Image.ParenCount > 0)
            {
                return iota.ExecuteInParens(this, continuation);
            }

            // ③ 普通执行
            return iota.Execute(this, continuation);
        }
        catch (Mishap mishap)
        {
            // mishap 转为副作用；是否清空括号取决于当前帧是否元求值
            bool wipeParens = continuation is SpellContinuation.NotDone cnd
                              && cnd.Frame is FrameEvaluate fe && fe.IsMetacasting;

            return new CastResult(
                iota,
                continuation,
                wipeParens ? Image.WithResetEscape() : null,
                new OperatorSideEffect[]
                {
                    new DoMishapSideEffect(mishap, new MishapContext(null, null)),
                },
                mishap.ResolutionType(Env),
                EvalSound.Mishap);
        }
        catch (Exception e)
        {
            // 内部异常：包装成 mishap
            return new CastResult(
                iota,
                continuation,
                null,
                new OperatorSideEffect[]
                {
                    new DoMishapSideEffect(new MishapInternalException(e), new MishapContext(null, null)),
                },
                ResolvedPatternType.Errored,
                EvalSound.Mishap);
        }
    }

    /// <summary>依次执行副作用。</summary>
    public void PerformSideEffects(IReadOnlyList<OperatorSideEffect> sideEffects)
    {
        for (int i = 0; i < sideEffects.Count; i++)
        {
            sideEffects[i].PerformEffect(this);
        }
    }

    /// <summary>
    /// 栈是否大到无法序列化。
    /// 源项目用 IotaType.isTooLargeToSerialize（深度 256 / 总量 1024）。
    /// </summary>
    private static bool IsStackTooLarge(IReadOnlyList<Iota> stack)
    {
        int total = 0;
        for (int i = 0; i < stack.Count; i++)
        {
            total += EstimateSize(stack[i], 0);
            if (total > Iota.MaxSerializationTotal)
            {
                return true;
            }
        }
        return false;
    }

    private static int EstimateSize(Iota iota, int depth)
    {
        if (depth > Iota.MaxSerializationDepth)
        {
            return Iota.MaxSerializationTotal + 1;
        }

        if (iota is ListIota list)
        {
            int sum = 1;
            foreach (var sub in list.Items)
            {
                sum += EstimateSize(sub, depth + 1);
                if (sum > Iota.MaxSerializationTotal)
                {
                    return sum;
                }
            }
            return sum;
        }

        return 1;
    }
}

/// <summary>一次施法的最终结果。</summary>
public sealed class CastOutcome
{
    public CastingImage Image { get; }
    public ResolvedPatternType ResolutionType { get; }

    /// <summary>
    /// 本次求值产生的粒子（世界**像素**坐标）。
    ///
    /// 为什么放在结果里而不是当场生成：`Core/` 不许引用 Terraria（否则
    /// 全部离线用例编译不过），所以 Core 只负责「说该喷在哪」，
    /// 真正 `Dust.NewDust` 由表现层做。
    /// </summary>
    public IReadOnlyList<ParticleSpray> Particles { get; }

    /// <summary>
    /// 本次求值要播的音效（最后一条非 Nothing 的）。
    /// 与粒子同理：Core 只负责「该响哪种声音」，实际播放交给表现层。
    /// </summary>
    public EvalSound Sound { get; }

    public CastOutcome(CastingImage image, ResolvedPatternType resolutionType,
                       IReadOnlyList<ParticleSpray>? particles = null,
                       EvalSound sound = EvalSound.Nothing)
    {
        Image = image;
        ResolutionType = resolutionType;
        Particles = particles ?? System.Array.Empty<ParticleSpray>();
        Sound = sound;
    }
}
