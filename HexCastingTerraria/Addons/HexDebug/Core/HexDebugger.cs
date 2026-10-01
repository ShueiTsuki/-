using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>调用栈里的一帧（上游 DAP StackFrame）：帧名、指向的源码位置；虚拟帧是尾调用省掉的 FrameFinishEval。</summary>
public sealed record DebugFrameView(string Name, IotaMetadata? Meta, bool IsVirtual);

/// <summary>
/// 一个调试线程（上游 debugger/HexDebugger.kt，逐行移植）：自己持有续延与 VM 状态，一次走一步或走到该停的地方。
/// 上游的「变量」「源码」通过 DAP 交给编辑器；这里把同样的信息作为视图交给游戏内调试面板（见 <see cref="StackFrames"/> 等）。
/// </summary>
public sealed class HexDebugger
{
    private readonly SharedDebugState _shared;

    private CastingEnvironment? _env;
    private RequestStepType? _lastRequestStepType;

    private readonly Dictionary<Iota, IotaMetadata> _iotaMetadata = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpellContinuation, Func<(Iota Iota, IotaMetadata? Meta)?>> _frameInvocationMetadata = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpellContinuation, List<DebugFrameView>> _virtualFrames = new(ReferenceEqualityComparer.Instance);

    private List<SpellContinuation.NotDone> _callStack = new();
    private EvaluatorResetData? _evaluatorResetData;
    private ResolvedPatternType _lastResolutionType = ResolvedPatternType.Unresolved;
    private CastingImage _image = new();
    private SpellContinuation _nextContinuation = SpellContinuation.Done.Instance;

    public HexDebugger(SharedDebugState shared, DebugEnvironment debugEnv, int threadId)
    {
        _shared = shared;
        DebugEnv = debugEnv;
        ThreadId = threadId;
    }

    public DebugEnvironment DebugEnv { get; }

    public int ThreadId { get; }

    public Guid SessionId => DebugEnv.SessionId;

    public DebuggerState State { get; private set; } = DebuggerState.Running;

    public IotaMetadata? LastEvaluatedMetadata { get; private set; }

    public CastingImage Image => _image;

    public ResolvedPatternType LastResolutionType => _lastResolutionType;

    /// <summary>这一步产生的粒子与音效（游戏侧取走后清空；本体 QueueExecute 也是这样把它们带出去的）。</summary>
    public List<ParticleSpray> PendingParticles { get; } = new();

    public EvalSound PendingSound { get; set; } = EvalSound.Nothing;

    /// <summary>运行杖画在画布上的图案（上游 evaluatorUIPatterns；游戏侧存放）。</summary>
    public object? EvaluatorUi { get; set; }

    private CastingEnvironment? Env
    {
        get => _env;
        set
        {
            _env = value;
            if (value is not null) value.DebugObserver = DebugEnv;
        }
    }

    private SpellContinuation NextContinuation
    {
        get => _nextContinuation;
        set
        {
            _nextContinuation = value;
            _callStack = GetCallStack(value);
        }
    }

    public bool IsDone => _nextContinuation is SpellContinuation.Done;

    private IContinuationFrame? NextFrame => (_nextContinuation as SpellContinuation.NotDone)?.Frame;

    private CastingVM? GetVM() => _env is null ? null : new CastingVM(_image, _env);

    // ==================== 源码登记 ====================

    private DebugSource? RegisterNewSource(IContinuationFrame frame) => GetIotas(frame) is { } iotas ? RegisterNewSource(iotas) : null;

    private DebugSource? RegisterNewSource(IEnumerable<Iota> iotas)
    {
        var unregistered = iotas.Where(i => !_iotaMetadata.ContainsKey(i)).ToList();
        if (unregistered.Count == 0) return null;

        var source = _shared.AddSource(ThreadId, unregistered);
        for (int i = 0; i < unregistered.Count; i++)
        {
            // 认知危害：一登记进来调试就结束（上游 CognitohazardIota 的唯一作用）
            if (unregistered[i] is CognitohazardIota) State = DebuggerState.Terminated;
            _iotaMetadata[unregistered[i]] = new IotaMetadata(source, i);
        }
        return source;
    }

    private static IEnumerable<Iota>? GetIotas(IContinuationFrame frame) => frame switch
    {
        FrameEvaluate fe => fe.List,
        FrameForEach ff => ff.Code,
        _ => null,
    };

    public IotaMetadata? MetadataOf(Iota iota) => _iotaMetadata.TryGetValue(iota, out var m) ? m : null;

    private (Iota Iota, IotaMetadata? Meta)? GetFirstIotaMetadata(SpellContinuation.NotDone continuation)
    {
        // FrameEvaluate：显示下一个要求值的 iota；其余帧：有调用者就显示调用者，否则显示它里面的第一个 iota
        if (continuation.Frame is FrameEvaluate fe && GetFirstIotaMetadata(fe) is { } first) return first;
        if (_frameInvocationMetadata.TryGetValue(continuation, out var inv) && inv() is { } invoked) return invoked;
        return GetFirstIotaMetadata(continuation.Frame);
    }

    private (Iota Iota, IotaMetadata? Meta)? GetFirstIotaMetadata(IContinuationFrame frame)
    {
        var iotas = GetIotas(frame);
        var car = iotas?.FirstOrDefault();
        return car is null ? null : (car, MetadataOf(car));
    }

    /// <summary>当前续延在最后（上游 getCallStack：从栈底到当前）。</summary>
    private static List<SpellContinuation.NotDone> GetCallStack(SpellContinuation current)
    {
        var list = new List<SpellContinuation.NotDone>();
        for (var c = current as SpellContinuation.NotDone; c is not null; c = c.Next as SpellContinuation.NotDone) list.Add(c);
        list.Reverse();
        return list;
    }

    /// <summary>上游 getNextIotaToEvaluate：下一个要求值的 iota 的文字与行号（0 起；不知道是 -1）。</summary>
    public (string Text, int Index)? GetNextIotaToEvaluate()
    {
        if (_nextContinuation is not SpellContinuation.NotDone c) return null;
        if (GetFirstIotaMetadata(c) is not { } first) return null;
        return (IotaText.Display(first.Iota), first.Meta?.LineIndex ?? -1);
    }

    /// <summary>上游 getStackFrames：调用栈，当前帧在最前；虚拟帧（尾调用省掉的）跟在它所在的那一帧后面。</summary>
    public List<DebugFrameView> StackFrames()
    {
        var list = new List<DebugFrameView>();
        foreach (var c in _callStack)
        {
            list.Add(new DebugFrameView(c.Frame.GetType().Name, GetFirstIotaMetadata(c)?.Meta, false));
            if (_virtualFrames.TryGetValue(c, out var v)) list.AddRange(v);
        }
        list.Reverse();
        return list;
    }

    /// <summary>上游 getSourceContents：一段源码逐行的文字（带缩进）。</summary>
    public List<string> SourceLines(DebugSource source)
        => source.Iotas.Select(i => (MetadataOf(i)?.Indent(_shared.LaunchArgs.IndentWidth) ?? "") + IotaText.Source(i)).ToList();

    /// <summary>当前停的位置：下一个要求值的 iota 所在的源码与行。</summary>
    public IotaMetadata? CurrentPosition()
        => _nextContinuation is SpellContinuation.NotDone c ? GetFirstIotaMetadata(c)?.Meta : null;

    // ==================== 断点 ====================

    private bool IsAtBreakpoint()
    {
        Iota? nextIota;
        switch (NextFrame)
        {
            case FrameEvaluate fe:
                nextIota = fe.List.FirstOrDefault();
                break;
            case FrameBreakpoint:
                return true;
            default:
                nextIota = null;
                break;
        }
        if (nextIota is null) return false;

        var meta = MetadataOf(nextIota);
        if (meta is null || _shared.BreakpointAt(meta.Source.Reference, meta.LineIndex) is not { } mode) return false;

        bool escapeNext = _image.EscapeNext || _image.ParenCount > 0;
        return mode switch
        {
            SourceBreakpointMode.Evaluated => !escapeNext,
            SourceBreakpointMode.Escaped => escapeNext,
            _ => true,
        };
    }

    // ==================== 运行杖 ====================

    /// <summary>上游 evaluate：运行杖画的图案在调试器当前的栈上跑（第一次画时记下复原点）。</summary>
    public DebugStepResult? Evaluate(SpellList list)
    {
        var vm = GetVM();
        if (vm is null) return null;
        vm.Env.DebugObserver = DebugEnv;

        if (State == DebuggerState.CaughtMishap)
        {
            // 上游：手动补一次事故音效
            PendingSound = EvalSound.Mishap;
            return new DebugStepResult(StopReason.Exception);
        }

        bool startedEvaluating = _evaluatorResetData is null;
        if (startedEvaluating) _evaluatorResetData = new EvaluatorResetData(_nextContinuation, _image, _lastResolutionType, State);

        NextContinuation = _nextContinuation.PushFrame(new FrameEvaluate(list, false));
        var result = ExecuteNextDebugStep(vm, doStaffMishaps: true) with { StartedEvaluating = startedEvaluating };
        PostStep(result);
        return result;
    }

    /// <summary>上游 resetEvaluator：运行杖潜行使用 —— 回到它画第一个图案之前。返回之前有没有改过。</summary>
    public bool ResetEvaluator()
    {
        bool had = _evaluatorResetData is not null;
        if (_evaluatorResetData is { } d)
        {
            NextContinuation = d.Continuation;
            _image = d.Image;
            _lastResolutionType = d.LastResolutionType;
            State = d.State;
        }
        _evaluatorResetData = null;
        EvaluatorUi = null;
        PostStep(new DebugStepResult(StopReason.Step));
        return had;
    }

    // ==================== 开始 / 暂停 / 运行 ====================

    /// <summary>上游 startExecuting：从头开始调试一串 iota。</summary>
    public DebugStepResult? StartExecuting(CastingEnvironment env, IReadOnlyList<Iota> iotas, CastingImage? image)
    {
        if (!State.CanPause()) return null;

        bool isStarting = _env is null;
        bool isPausing = State == DebuggerState.Pausing;

        State = DebuggerState.Paused;
        Env = env;
        if (image is not null) _image = image;

        SpellContinuation newContinuation = SpellContinuation.Done.Instance;
        if (_shared.LaunchArgs.StopOnExit)
        {
            var lastIota = iotas.Count > 0 ? iotas[^1] : null;
            int? column = lastIota is null ? null : IotaText.Source(lastIota).Length;
            newContinuation = newContinuation.PushFrame(new FrameBreakpoint(stopBefore: true));
            _frameInvocationMetadata[newContinuation] = () => lastIota is null ? null : (lastIota, MetadataOf(lastIota)?.WithColumn(column));
        }
        NextContinuation = newContinuation.PushFrame(new FrameEvaluate(new SpellList.LList(0, iotas), false));

        RegisterNewSource(iotas);

        StopReason? stopReason =
            isStarting && _shared.LaunchArgs.StopOnEntry ? StopReason.Started
            : IsAtBreakpoint() ? StopReason.Breakpoint
            : isPausing ? StopReason.Pause
            : _lastRequestStepType is not null ? StopReason.Step
            : null;

        return stopReason is { } r ? new DebugStepResult(r) : ExecuteUntilStopped();
    }

    public void Pause()
    {
        if (State == DebuggerState.Running) State = DebuggerState.Pausing;
    }

    /// <summary>上游 executeUntilStopped：继续（null）、逐过程、单步跳出、单步调试。</summary>
    public DebugStepResult ExecuteUntilStopped(RequestStepType? stepType = null)
    {
        var vm = GetVM();
        if (vm is null) return new DebugStepResult(null, Skipped: true);
        var result = ExecuteUntilStopped(vm, stepType);
        PostStep(result);
        return result;
    }

    private DebugStepResult ExecuteUntilStopped(CastingVM vm, RequestStepType? stepType)
    {
        _lastRequestStepType = stepType;
        if (stepType == RequestStepType.In) return ExecuteNextDebugStep(vm);

        bool? isEscaping = null;
        int stepDepth = 0;
        bool shouldStop = false;
        bool hitBreakpoint = false;

        while (true)
        {
            var result = ExecuteNextDebugStep(vm, exactlyOnce: true);

            // reason 为 null 或「立刻停」就返回
            if (result.Reason is not { } reason || reason.StopImmediately()) return result;

            if (IsAtBreakpoint()) hitBreakpoint = true;
            if (hitBreakpoint && ShouldStopAtFrame(_nextContinuation)) return result with { Reason = StopReason.Breakpoint };

            // 「继续」只在断点停
            if (stepType is null) continue;

            if (result.Type == DebugStepType.Jump) shouldStop = true;

            stepDepth += result.Type switch
            {
                DebugStepType.In => 1,
                DebugStepType.Out => -1,
                _ => 0,
            };

            isEscaping ??= result.Type == DebugStepType.Escape;

            shouldStop = shouldStop || (isEscaping.Value
                ? result.Type != DebugStepType.Escape
                : stepType switch
                {
                    RequestStepType.Over => stepDepth <= 0,
                    RequestStepType.Out => stepDepth < 0,
                    _ => throw new InvalidOperationException(),
                });

            if (shouldStop && ShouldStopAtFrame(_nextContinuation)) return result;
        }
    }

    /// <summary>上游 executeNextDebugStep：本体 CastingVM.QueueExecute 的循环体，改成能一步一停。</summary>
    private DebugStepResult ExecuteNextDebugStep(CastingVM vm, bool exactlyOnce = false, bool doStaffMishaps = false)
    {
        var stepResult = new DebugStepResult(StopReason.Step);

        if (State == DebuggerState.Running) return stepResult.Resumed().AsSkipped();

        var continuation = _nextContinuation;
        if (continuation is not SpellContinuation.NotDone) return stepResult.Done().AsSkipped();

        bool earlyExit = false;
        while (continuation is SpellContinuation.NotDone notDone && !earlyExit)
        {
            DebugEnv.LastDebugStepType = null;
            DebugEnv.LastEvaluatedWasEval = false;

            var frame = notDone.Frame;
            if ((frame is FrameBreakpoint fb && fb.IsFatal) || State == DebuggerState.Terminated)
            {
                continuation = SpellContinuation.Done.Instance;
                _lastResolutionType = ResolvedPatternType.Errored;
                break;
            }

            var castResult = frame.Evaluate(notDone.Next, vm);
            if (castResult.NewData is { } nd && CastingVM.IsStackTooLarge(nd.Stack))
            {
                castResult = new CastResult(castResult.Cast, castResult.Continuation, null,
                    new OperatorSideEffect[] { new DoMishapSideEffect(new MishapStackSize(), new MishapContext(null, null)) },
                    ResolvedPatternType.Errored, EvalSound.Mishap);
            }
            else if (castResult.NewData is { } nd2 && nd2.OpsConsumed > vm.Env.MaxOpCount())
            {
                castResult = new CastResult(castResult.Cast, castResult.Continuation, null,
                    new OperatorSideEffect[] { new DoMishapSideEffect(new MishapEvalTooMuch(), new MishapContext(null, null)) },
                    ResolvedPatternType.Errored, EvalSound.Mishap);
            }

            var newImage = castResult.NewData;

            // 出事了：要么压一个致命断点停在这里（并记下出事前的栈，好让人看到事故前的样子），要么直接结束
            SpellContinuation newContinuation;
            CastingImage? preMishapImage = null;
            if (castResult.ResolutionType.IsSuccess() || doStaffMishaps)
            {
                newContinuation = castResult.Continuation;
            }
            else if (_shared.StopOnUncaughtMishaps)
            {
                State = DebuggerState.CaughtMishap;
                stepResult = stepResult with { Reason = StopReason.Exception };
                newContinuation = castResult.Continuation.PushFrame(FrameBreakpoint.Fatal());
                preMishapImage = vm.Image;
            }
            else
            {
                newContinuation = SpellContinuation.Done.Instance;
            }

            // 打印到输出时用（必须在 PostExecution 之前，事故消息就是那里发的）
            LastEvaluatedMetadata = MetadataOf(castResult.Cast);

            if (newImage is not null)
            {
                HandleIndent(castResult, vm.Image, newImage);
                vm.SetImage(newImage);
            }
            vm.Env.PostExecution(castResult);

            var stepType = GetStepType(castResult, notDone, newContinuation);
            if (newContinuation is SpellContinuation.NotDone newNotDone)
            {
                SetIotaOverrides(castResult, notDone, newNotDone, stepType);
                RegisterNewSource(newNotDone.Frame);

                // OpEval 做了尾调用、没压 FrameFinishEval 时，补一个虚拟帧让调用栈看得出是从哪儿进来的
                if (_shared.LaunchArgs.ShowTailCallFrames && DebugEnv.LastEvaluatedWasEval)
                {
                    var invokeMeta = MetadataOf(castResult.Cast);
                    var nextInvokeMeta = _frameInvocationMetadata.TryGetValue(newNotDone.Next, out var f) ? f()?.Meta : null;
                    if (invokeMeta is not null && !SameMeta(invokeMeta, nextInvokeMeta))
                    {
                        if (!_virtualFrames.TryGetValue(notDone.Next, out var list)) _virtualFrames[notDone.Next] = list = new();
                        list.Add(new DebugFrameView("FrameFinishEval", invokeMeta, true));
                    }
                }
            }
            if (stepType is not null) stepResult = stepResult with { Type = stepType };

            continuation = newContinuation;
            _lastResolutionType = castResult.ResolutionType;

            if (castResult.Sound != EvalSound.Nothing) PendingSound = castResult.Sound;
            foreach (var se in castResult.SideEffects)
            {
                if (se is ParticlesSideEffect ps) PendingParticles.Add(ps.Spray);
            }

            try
            {
                vm.PerformSideEffects(castResult.SideEffects);
            }
            catch (Exception e)
            {
                vm.PerformSideEffects(new OperatorSideEffect[]
                {
                    new DoMishapSideEffect(new MishapInternalException(e), new MishapContext(null, null)),
                });
            }
            earlyExit = earlyExit || !castResult.ResolutionType.IsSuccess();

            // 必须在 PerformSideEffects 之后：事故就是在那里改栈的
            if (preMishapImage is not null) vm.SetImage(preMishapImage);

            if (exactlyOnce || ShouldStopAtFrame(continuation)) break;
        }

        // 调用栈顶上不显示虚拟帧
        if (_virtualFrames.TryGetValue(continuation, out var top)) top.Clear();

        NextContinuation = continuation;
        _image = vm.Image;

        if (continuation is SpellContinuation.Done)
        {
            if (State.CanResume() && DebugEnv.Resume(vm.Env, _image, _lastResolutionType))
            {
                State = DebuggerState.Running;
                return stepResult.Resumed();
            }
            State = DebuggerState.Terminated;
            return stepResult.Done();
        }
        return stepResult;
    }

    private static bool SameMeta(IotaMetadata a, IotaMetadata? b)
        => b is not null && ReferenceEquals(a.Source, b.Source) && a.LineIndex == b.LineIndex && a.ColumnIndex == b.ColumnIndex;

    private void PostStep(DebugStepResult result)
    {
        if (!result.Skipped && _env is not null) DebugEnv.PostStep(_env, _image, result.Reason);
    }

    private bool ShouldStopAtFrame(SpellContinuation continuation)
        => continuation is not SpellContinuation.NotDone nd || ShouldStopAtFrame(nd.Frame);

    /// <summary>上游：「内部」帧（托特的循环帧、FinishEval 等）默认跳过不停；断点帧看 stopBefore。</summary>
    private bool ShouldStopAtFrame(IContinuationFrame frame)
    {
        if (!_shared.LaunchArgs.SkipNonEvalFrames) return true;
        return frame switch
        {
            FrameEvaluate => true,
            FrameBreakpoint b => b.StopBefore,
            _ => false,
        };
    }

    /// <summary>上游 handleIndent：转义时记下这个 iota 的括号层数（源码视图的缩进）。</summary>
    private void HandleIndent(CastResult castResult, CastingImage oldImage, CastingImage newImage)
    {
        if (castResult.ResolutionType == ResolvedPatternType.Escaped)
        {
            int parenCount = Math.Min(oldImage.ParenCount, newImage.ParenCount);
            MetadataOf(castResult.Cast)?.TrySetParenCount(parenCount);
        }
        else if (castResult.ResolutionType == ResolvedPatternType.Evaluated
                 && newImage.ParenCount == 0 && newImage.Parenthesized.Count == 0 && oldImage.Parenthesized.Count > 0)
        {
            // 列表闭合：括号里的 iota 都定下缩进了
            foreach (var p in oldImage.Parenthesized)
            {
                if (MetadataOf(p.Iota) is { } m) m.NeedsReload = false;
            }
        }
    }

    private DebugStepType? GetStepType(CastResult castResult, SpellContinuation.NotDone continuation, SpellContinuation newContinuation)
    {
        bool isEscaped = castResult.ResolutionType switch
        {
            ResolvedPatternType.Escaped => true,
            ResolvedPatternType.Evaluated => (castResult.NewData?.ParenCount ?? 0) > 0,
            _ => false,
        };
        if (isEscaped) return DebugStepType.Escape;

        if (castResult.Cast is ContinuationIota) return DebugStepType.Jump;

        if (newContinuation is not SpellContinuation.NotDone newNotDone) return null;

        if (ReferenceEquals(newContinuation, continuation.Next))
        {
            // 托特的内层循环结束时不算「跳出」
            return newNotDone.Frame is FrameForEach ? null : DebugStepType.Out;
        }

        if (!ReferenceEquals(continuation.Next, newNotDone.Next) || continuation.Frame.GetType() != newNotDone.Frame.GetType())
        {
            // 托特的内层循环开始时不算「进入」
            return continuation.Frame is FrameForEach ? null : DebugStepType.In;
        }

        return DebugEnv.LastDebugStepType;
    }

    private void SetIotaOverrides(CastResult castResult, SpellContinuation.NotDone continuation, SpellContinuation.NotDone newContinuation, DebugStepType? stepType)
    {
        var nextContinuation = newContinuation.Next;
        var frame = continuation.Frame;
        var newFrame = newContinuation.Frame;
        var nextFrame = (nextContinuation as SpellContinuation.NotDone)?.Frame;

        if (stepType == DebugStepType.In)
        {
            TrySetIotaOverride(newContinuation, castResult);
            if (nextFrame is not FrameEvaluate) TrySetIotaOverride(nextContinuation, castResult);
        }
        else if (frame is FrameForEach ff && newFrame is FrameEvaluate fe && nextFrame is FrameForEach nf
                 && ReferenceEquals(ff.Code, fe.List) && ReferenceEquals(ff.Code, nf.Code))
        {
            // 托特每一轮之间沿用调用位置
            if (_frameInvocationMetadata.TryGetValue(continuation, out var inv)) _frameInvocationMetadata[nextContinuation] = inv;
            else _frameInvocationMetadata.Remove(nextContinuation);
        }
    }

    private void TrySetIotaOverride(SpellContinuation continuation, CastResult castResult)
    {
        if (continuation is SpellContinuation.NotDone && !_frameInvocationMetadata.ContainsKey(continuation))
        {
            var cast = castResult.Cast;
            _frameInvocationMetadata[continuation] = () => (cast, MetadataOf(cast));
        }
    }

    private sealed record EvaluatorResetData(SpellContinuation Continuation, CastingImage Image, ResolvedPatternType LastResolutionType, DebuggerState State);
}
