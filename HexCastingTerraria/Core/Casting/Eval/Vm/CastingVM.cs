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
        // 注意：必须在这里收集：`ParticlesSideEffect.PerformEffect` 是空的
        //（Core 不能引用 Terraria），真正的生成在表现层 ——
        // 只有把「该在哪喷」带出去，客户端才有东西可画。
        // 之前没人带，所以**所有法术粒子都不显示**，而且不报错。
        var particles = new List<ParticleSpray>();

        // 本次求值要播的音效。
        // 注意：和粒子同样的坑：EvalSound 一直**被算出来但没人播** ——
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
    /// 注意：三个分支的顺序不可调换（spec 7.3 第③条）：
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
                    // 源项目 PatternIota.execute：Mishap.Context(this.pattern, castedName) ——
                    // 聊天提示带「图案名：」前缀；MishapNeedsParens 靠它把图案压回栈。（曾经一律传 null）
                    new DoMishapSideEffect(mishap, ContextFor(iota)),
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

    private static MishapContext ContextFor(Iota iota)
    {
        if (iota is PatternIota p)
        {
            var def = Registry.PatternRegistry.Match(p.Pattern);
            return new MishapContext(p.Pattern, def is null ? null : Registry.PatternDisplay.DisplayName(def));
        }
        return new MishapContext(null, null);
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
    /// 源项目 IotaType.isTooLargeToSerialize(stack)：从 1 开始累加每个 iota 的 size()，
    /// 总数 **≥** 1024 算太大；任一 iota 的 depth() **≥** 256 也算太大。
    /// size：普通 iota 1，列表 = 1 + 子项 size 之和；depth：普通 iota 1，列表 = 1 + 子项最大 depth。
    ///（这里曾用「总数 > 1024」「嵌套层数 > 256」，与原版各差一两个）
    /// </summary>
    public static bool IsStackTooLarge(IReadOnlyList<Iota> stack)
    {
        int total = 1;
        for (int i = 0; i < stack.Count; i++)
        {
            var (size, depth) = Measure(stack[i]);
            if (depth >= Iota.MaxSerializationDepth) return true;
            total += size;
            if (total >= Iota.MaxSerializationTotal) return true;
        }
        return false;
    }

    private static (int Size, int Depth) Measure(Iota iota)
    {
        if (iota is not ListIota list) return (1, 1);
        int size = 1, maxChild = 0;
        foreach (var sub in list.Items)
        {
            var (s, d) = Measure(sub);
            size += s;
            if (d > maxChild) maxChild = d;
            // 早停：已经确定太大就不必把一棵巨树走完
            if (size >= Iota.MaxSerializationTotal || maxChild >= Iota.MaxSerializationDepth) break;
        }
        return (size, maxChild + 1);
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
