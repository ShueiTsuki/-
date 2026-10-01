using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>上游 debugger/Enums.kt DebuggerState。</summary>
public enum DebuggerState
{
    Running,
    Pausing,
    Paused,
    CaughtMishap,
    Terminated,
}

/// <summary>上游 core api StopReason（stopImmediately 见 <see cref="DebugEnums.StopImmediately"/>）。</summary>
public enum StopReason
{
    Step,
    Pause,
    Breakpoint,
    Exception,
    Started,
    Terminated,
}

/// <summary>上游 core api DebugStepType。</summary>
public enum DebugStepType
{
    In,
    Out,
    Jump,
    Escape,
}

/// <summary>上游 SourceBreakpointMode：这一行的断点什么时候停。</summary>
public enum SourceBreakpointMode
{
    /// <summary>这个 iota 要被求值时停（默认）。</summary>
    Evaluated,

    /// <summary>这个 iota 要被转义时停。</summary>
    Escaped,

    /// <summary>总是停。</summary>
    All,
}

/// <summary>上游 RequestStepType：逐过程 / 单步跳出 / 单步调试。</summary>
public enum RequestStepType
{
    Over,
    Out,
    In,
}

/// <summary>上游 DebuggerItem.StepMode（调试杖的步进模式，潜行 + 滚轮切换）。</summary>
public enum StepMode
{
    Continue,
    Over,
    In,
    Out,
    Restart,
    Stop,
}

public static class DebugEnums
{
    public static bool CanPause(this DebuggerState s) => s is DebuggerState.Running or DebuggerState.Pausing;

    public static bool CanResume(this DebuggerState s) => s is DebuggerState.Paused or DebuggerState.CaughtMishap;

    public static bool StopImmediately(this StopReason r) => r != StopReason.Step;

    /// <summary>上游 StepMode.canPause：继续 / 逐过程 / 单步调试 / 单步跳出在调试中按下时会暂停正在跑的线程。</summary>
    public static bool CanPause(this StepMode m) => m is StepMode.Continue or StepMode.Over or StepMode.In or StepMode.Out;
}

/// <summary>上游 adapter/LaunchArgs.kt（默认值照搬；上游由编辑器在启动调试时给）。</summary>
public sealed class LaunchArgs
{
    public bool StopOnEntry { get; init; } = true;
    public bool StopOnExit { get; init; }
    public bool SkipNonEvalFrames { get; init; } = true;
    public int IndentWidth { get; init; } = 4;
    public bool ShowTailCallFrames { get; init; } = true;
}

/// <summary>
/// 一段「源码」：调试时登记的一串 iota（上游 SourceAllocator 里的一项，在编辑器里显示成一个文件，一个 iota 一行）。
/// </summary>
public sealed class DebugSource
{
    public DebugSource(int reference, int threadId, IReadOnlyList<Iota> iotas)
    {
        Reference = reference;
        ThreadId = threadId;
        Iotas = iotas;
    }

    public int Reference { get; }

    public int ThreadId { get; }

    /// <summary>上游 Source.name / path：「source编号.hexpattern」。</summary>
    public string Name => $"source{Reference}.hexpattern";

    /// <summary>上游 Source.name：「线程 N · 第 M 段」由界面拼，这里只给编号。</summary>
    public IReadOnlyList<Iota> Iotas { get; }
}

/// <summary>上游 IotaMetadata：这个 iota 在哪一段源码的第几行（0 起），以及缩进（括号层数，第一次求值 / 转义时定下）。</summary>
public sealed class IotaMetadata
{
    public IotaMetadata(DebugSource source, int lineIndex, int? columnIndex = null)
    {
        Source = source;
        LineIndex = lineIndex;
        ColumnIndex = columnIndex;
    }

    public DebugSource Source { get; }

    public int LineIndex { get; }

    public int? ColumnIndex { get; }

    public bool NeedsReload { get; set; }

    public int? ParenCount { get; private set; }

    public void TrySetParenCount(int parenCount)
    {
        if (ParenCount is null)
        {
            ParenCount = parenCount;
            NeedsReload = true;
        }
    }

    public string Indent(int width) => new(' ', width * (ParenCount ?? 0));

    public IotaMetadata WithColumn(int? column) => new(Source, LineIndex, column) { NeedsReload = NeedsReload };
}

/// <summary>上游 DebugStepResult。</summary>
public sealed record DebugStepResult(
    StopReason? Reason,
    DebugStepType? Type = null,
    bool StartedEvaluating = false,
    bool Skipped = false)
{
    public bool IsDone => Reason == StopReason.Terminated;

    public DebugStepResult Done() => this with { Reason = StopReason.Terminated };

    public DebugStepResult Resumed() => this with { Reason = null };

    public DebugStepResult AsSkipped() => this with { Skipped = true };
}

/// <summary>
/// 上游 SharedDebugState：一个调试会话（一个玩家）里所有线程共享的：启动参数、断点、源码表。
/// 上游的断点由编辑器设；移植版在游戏内调试面板里点行设。
/// </summary>
public sealed class SharedDebugState
{
    public LaunchArgs LaunchArgs { get; set; } = new();

    /// <summary>源码编号 → 行（0 起）→ 断点模式。</summary>
    public Dictionary<int, Dictionary<int, SourceBreakpointMode>> Breakpoints { get; } = new();

    /// <summary>上游 exceptionBreakpoints；默认开着「未捕获的事故」（上游 isDefault = true，编辑器启动时会勾上）。</summary>
    public bool StopOnUncaughtMishaps { get; set; } = true;

    private readonly List<DebugSource> _sources = new();
    private int _nextReference = 1;

    public IReadOnlyList<DebugSource> Sources => _sources;

    /// <summary>
    /// 上游 SourceAllocator.add：同一串 iota（同一批对象）再登记时沿用原来的编号 ——
    /// 重新开始调试同一段咒术时，编辑器 / 面板里设在这段源码上的断点还在。
    /// </summary>
    public DebugSource AddSource(int threadId, IReadOnlyList<Iota> iotas)
    {
        foreach (var old in _sources)
        {
            if (old.Iotas.Count == iotas.Count && System.Linq.Enumerable.SequenceEqual(old.Iotas, iotas, ReferenceEqualityComparer.Instance))
                return old;
        }
        var s = new DebugSource(_nextReference++, threadId, iotas);
        _sources.Add(s);
        return s;
    }

    // ── 外部调试器（DAP）──────────────────────────────────────────

    /// <summary>上游 initArgs.linesStartAt1 / columnsStartAt1（编辑器在 initialize 里说，默认都是 1 起）。</summary>
    public bool LinesStartAt1 { get; set; } = true;

    public bool ColumnsStartAt1 { get; set; } = true;

    public int IndexToLine(int index) => index + (LinesStartAt1 ? 1 : 0);

    public int LineToIndex(int line) => line - (LinesStartAt1 ? 1 : 0);

    public int IndexToColumn(int index) => index + (ColumnsStartAt1 ? 1 : 0);

    /// <summary>
    /// 上游 setBreakpoints：编辑器每次发一整个源码的断点，先清掉这个源码原来的，再逐条验证。
    /// 源码不认识 → 未验证（pending）；行号超出 → 未验证（failed）；模式不认识按「被求值时停」。
    /// </summary>
    public List<BreakpointResult> SetBreakpoints(int sourceReference, IEnumerable<(int Line, string? Mode)> requested)
    {
        if (!Breakpoints.TryGetValue(sourceReference, out var lines)) Breakpoints[sourceReference] = lines = new();
        lines.Clear();
        var source = FindSource(sourceReference);
        var results = new List<BreakpointResult>();
        foreach (var (line, mode) in requested)
        {
            if (source is null)
            {
                results.Add(new BreakpointResult(false, "Unknown source", "pending", null, null));
            }
            else if (line > IndexToLine(source.Iotas.Count - 1) || line < IndexToLine(0))
            {
                results.Add(new BreakpointResult(false, "Line number out of range", "failed", null, null));
            }
            else
            {
                lines[LineToIndex(line)] = DapNames.ParseMode(mode) ?? SourceBreakpointMode.Evaluated;
                results.Add(new BreakpointResult(true, null, null, source, line));
            }
        }
        return results;
    }

    /// <summary>
    /// 上游 onDisconnect：启动参数、行号约定、断点回到默认（源码不清，编辑器重连还要用）。
    /// 上游把「未捕获的事故」也清掉（没有编辑器就不停）；移植版有游戏内面板，没编辑器时也停，所以回到默认的「停」。
    /// </summary>
    public void OnDisconnect()
    {
        LaunchArgs = new LaunchArgs();
        LinesStartAt1 = true;
        ColumnsStartAt1 = true;
        Breakpoints.Clear();
        StopOnUncaughtMishaps = true;
    }

    public DebugSource? FindSource(int reference) => _sources.Find(s => s.Reference == reference);

    /// <summary>某一行的断点：没有 → 加上（默认「被求值时停」），有 → 去掉。返回切换后的状态。</summary>
    public bool ToggleBreakpoint(int sourceReference, int line)
    {
        if (!Breakpoints.TryGetValue(sourceReference, out var lines)) Breakpoints[sourceReference] = lines = new();
        if (lines.Remove(line)) return false;
        lines[line] = SourceBreakpointMode.Evaluated;
        return true;
    }

    public SourceBreakpointMode? BreakpointAt(int sourceReference, int line)
        => Breakpoints.TryGetValue(sourceReference, out var lines) && lines.TryGetValue(line, out var m) ? m : null;
}
