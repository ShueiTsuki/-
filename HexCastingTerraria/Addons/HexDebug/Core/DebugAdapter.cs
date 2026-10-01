using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// 调试适配器要游戏做的事（上游 DebugAdapter 里直接调用的 ServerPlayer / DebugEnvironment / 网络消息）。
/// 游戏侧每个玩家一个实现；离线测试用假的。
/// </summary>
public interface IDebugAdapterHost
{
    SharedDebugState Shared { get; }

    /// <summary>这个玩家现有的调试线程（线程号 → 调试器）。</summary>
    IReadOnlyDictionary<int, HexDebugger> Debuggers { get; }

    /// <summary>上游 knownPlayers 用的身份（同一个人重连能认出来）。</summary>
    string PlayerKey { get; }

    /// <summary>让一个线程走一步（null = 继续），走完由游戏调 <see cref="DebugAdapter.OnStep"/>。</summary>
    void Step(int threadId, RequestStepType? type, bool sendContinued);

    void Restart(IReadOnlyList<int> threadIds);

    void Terminate(IReadOnlyList<int> threadIds);

    /// <summary>发一条 DAP 消息（JSON）给玩家电脑上的编辑器。</summary>
    void Send(string json);

    /// <summary>上游 displayClientMessage(..., true)：屏幕上的提示。</summary>
    void Status(string key);

    /// <summary>断点变了：刷新游戏内调试面板。</summary>
    void BreakpointsChanged();
}

/// <summary>
/// 外部调试器（Debug Adapter Protocol，VSCode 等）的服务端（上游 adapter/DebugAdapter.kt）。
/// 编辑器连到玩家自己电脑上的端口，客户端把消息原样转到服务端，这里处理请求、回响应、发事件；每个玩家一个。
/// 上游用 lsp4j 生成 JSON；这里直接按 DAP 的字段名写。调试本身还是 <see cref="HexDebugger"/>，游戏内调试面板照常可用。
/// </summary>
public sealed class DebugAdapter
{
    private const int ThreadIdShift = 28;   // 上游 32 - THREAD_BITS（4）
    private const int ReferenceMask = (1 << ThreadIdShift) - 1;

    /// <summary>上游 knownPlayers：服务器开着以来连过的人。没连过的人发来的断点编号是上次开服的，一律作废。</summary>
    private static readonly HashSet<string> KnownPlayers = new();

    private readonly IDebugAdapterHost _host;
    private int _seq = 1;
    private HexDebugger? _lastDebugger;

    public DebugAdapter(IDebugAdapterHost host) => _host = host;

    public bool IsConnected { get; private set; }

    public static void ForgetKnownPlayers() => KnownPlayers.Clear();

    private SharedDebugState Shared => _host.Shared;

    private static int Pack(int threadId, int reference) => (threadId << ThreadIdShift) | (reference & ReferenceMask);

    private static (int ThreadId, int Reference) Unpack(int id) => ((int)((uint)id >> ThreadIdShift), id & ReferenceMask);

    private HexDebugger? Debugger(int threadId) => _host.Debuggers.TryGetValue(threadId, out var d) ? d : null;

    private HexDebugger? InRangeDebugger(int threadId) => Debugger(threadId) is { DebugEnv.IsCasterInRange: true } d ? d : null;

    // ==================== 收请求 ====================

    /// <summary>编辑器发来的一条消息（只处理 request；JSON 坏了就当没收到，和 lsp4j 记一条错误一样）。</summary>
    public void Handle(string json)
    {
        JsonObject? msg;
        try { msg = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return; }
        if (msg is null || Str(msg["type"]) != "request") return;
        string command = Str(msg["command"]) ?? "";
        var args = msg["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            var body = Dispatch(command, args, out bool handled, out string? error);
            if (!handled) Respond(msg, command, false, null, $"Unsupported request: {command}");
            else if (error is not null) Respond(msg, command, false, null, error);
            else Respond(msg, command, true, body, null);
        }
        catch (Exception e)
        {
            // 上游 exceptionHandler：内部错误也要回一条失败的响应，编辑器才不会一直等
            Respond(msg, command, false, null, e.Message);
        }
        if (command is "attach" or "launch" && IsConnected) AfterAttach();
    }

    private JsonNode? Dispatch(string command, JsonObject a, out bool handled, out string? error)
    {
        handled = true;
        error = null;
        switch (command)
        {
            case "initialize": return Initialize(a);
            case "attach":
            case "launch":
                Attach(a);
                return null;
            case "setBreakpoints": return SetBreakpoints(a);
            case "setExceptionBreakpoints": return SetExceptionBreakpoints(a);
            case "configurationDone":
                KnownPlayers.Add(_host.PlayerKey);
                if (_host.Debuggers.Count > 0) SendStopped(null, StopReason.Step);
                return null;
            case "next": StepRequest(a, RequestStepType.Over); return null;
            case "stepIn": StepRequest(a, RequestStepType.In); return null;
            case "stepOut": StepRequest(a, RequestStepType.Out); return null;
            case "continue": return Continue(a);
            case "pause":
                InRangeDebugger(Int(a["threadId"]) ?? -1)?.Pause();
                return null;
            case "restart": Restart(); return null;
            case "terminateThreads":
                TerminateThreads((a["threadIds"] as JsonArray)?.Select(Int).OfType<int>() ?? Enumerable.Empty<int>());
                return null;
            case "terminate":
                TerminateThreads(_host.Debuggers.Keys.ToList());
                return null;
            case "disconnect": Disconnect(); return null;
            case "threads": return Threads();
            case "stackTrace": return StackTrace(a);
            case "scopes": return Scopes(a);
            case "variables": return Variables(a);
            case "source": return Source(a);
            case "loadedSources":
                return new JsonObject { ["sources"] = new JsonArray(Shared.Sources.Select(s => (JsonNode)SourceJson(s)).ToArray()) };
            default:
                handled = false;
                return null;
        }
    }

    // ==================== 初始化 / 连接 ====================

    private JsonObject Initialize(JsonObject a)
    {
        Shared.LinesStartAt1 = Bool(a["linesStartAt1"]) ?? true;
        Shared.ColumnsStartAt1 = Bool(a["columnsStartAt1"]) ?? true;
        var modes = new JsonArray();
        foreach (var m in new[] { SourceBreakpointMode.Evaluated, SourceBreakpointMode.Escaped, SourceBreakpointMode.All })
        {
            var (label, description) = DapNames.ModeText(m);
            modes.Add(new JsonObject
            {
                ["mode"] = DapNames.ModeName(m),
                ["label"] = label,
                ["description"] = description,
                ["appliesTo"] = new JsonArray("source"),
            });
        }
        return new JsonObject
        {
            ["supportsConfigurationDoneRequest"] = true,
            ["supportsLoadedSourcesRequest"] = true,
            ["supportsTerminateRequest"] = true,
            ["supportsTerminateThreadsRequest"] = true,
            ["supportsSingleThreadExecutionRequests"] = true,
            ["supportsRestartRequest"] = true,
            ["exceptionBreakpointFilters"] = new JsonArray(new JsonObject
            {
                ["filter"] = DapNames.UncaughtMishaps,
                ["label"] = "Uncaught Mishaps",
                ["default"] = true,
            }),
            ["breakpointModes"] = modes,
        };
    }

    /// <summary>上游 attach：记下启动参数（上游 LaunchArgs 的五项，默认值照搬）。</summary>
    private void Attach(JsonObject a)
    {
        IsConnected = true;
        Shared.LaunchArgs = new LaunchArgs
        {
            StopOnEntry = Bool(a["stopOnEntry"]) ?? true,
            StopOnExit = Bool(a["stopOnExit"]) ?? false,
            SkipNonEvalFrames = Bool(a["skipNonEvalFrames"]) ?? true,
            IndentWidth = Int(a["indentWidth"]) ?? 4,
            ShowTailCallFrames = Bool(a["showTailCallFrames"]) ?? true,
        };
    }

    /// <summary>上游 attach 里的 initialized 事件和「调试客户端已连接！」。</summary>
    private void AfterAttach()
    {
        Event("initialized", null);
        _host.Status("Connected");
    }

    private void Disconnect()
    {
        IsConnected = false;
        Shared.OnDisconnect();
        if (_host.Debuggers.Count == 0) _lastDebugger = null;
        _host.BreakpointsChanged();
    }

    // ==================== 断点 ====================

    private JsonObject SetBreakpoints(JsonObject a)
    {
        var src = a["source"] as JsonObject;
        int reference = Int(src?["sourceReference"]) ?? ParseSourceName(Str(src?["name"]) ?? Str(src?["path"])) ?? 0;
        var requested = new List<(int, string?)>();
        if (a["breakpoints"] is JsonArray bps)
        {
            foreach (var bp in bps.OfType<JsonObject>()) requested.Add((Int(bp["line"]) ?? 0, Str(bp["mode"])));
        }
        else if (a["lines"] is JsonArray lines)
        {
            foreach (var l in lines) requested.Add((Int(l) ?? 0, null));
        }

        JsonArray result;
        if (KnownPlayers.Contains(_host.PlayerKey))
        {
            result = new JsonArray(Shared.SetBreakpoints(reference, requested).Select(r =>
            {
                var o = new JsonObject { ["verified"] = r.Verified };
                if (r.Message is not null) o["message"] = r.Message;
                if (r.Reason is not null) o["reason"] = r.Reason;
                if (r.Source is not null) o["source"] = SourceJson(r.Source);
                if (r.Line is { } line) o["line"] = line;
                return (JsonNode)o;
            }).ToArray());
            _host.BreakpointsChanged();
        }
        else
        {
            // 上游 invalidateBreakpoints
            result = new JsonArray(requested.Select(_ => (JsonNode)new JsonObject
            {
                ["verified"] = false,
                ["message"] = "Invalid",
                ["reason"] = "failed",
            }).ToArray());
        }
        return new JsonObject { ["breakpoints"] = result };
    }

    /// <summary>编辑器没带 sourceReference、只带了名字时（「source3.hexpattern」）从名字里认编号。</summary>
    private static int? ParseSourceName(string? name)
    {
        if (name is null) return null;
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf((char)92));
        string file = name.Substring(slash + 1);
        if (!file.StartsWith("source", StringComparison.Ordinal) || !file.EndsWith(".hexpattern", StringComparison.Ordinal)) return null;
        return int.TryParse(file.AsSpan(6, file.Length - 6 - ".hexpattern".Length), out int n) ? n : null;
    }

    private JsonObject SetExceptionBreakpoints(JsonObject a)
    {
        var filters = (a["filters"] as JsonArray)?.Select(Str).ToList() ?? new List<string?>();
        Shared.StopOnUncaughtMishaps = filters.Contains(DapNames.UncaughtMishaps);
        return new JsonObject
        {
            ["breakpoints"] = new JsonArray(filters.Select(f => (JsonNode)new JsonObject { ["verified"] = f == DapNames.UncaughtMishaps }).ToArray()),
        };
    }

    // ==================== 步进 ====================

    private void StepRequest(JsonObject a, RequestStepType type)
    {
        int threadId = Int(a["threadId"]) ?? -1;
        if (InRangeDebugger(threadId) is not null) _host.Step(threadId, type, sendContinued: true);
        // 上游的折中：VSCode 不支持单线程执行，所以只有明确说了 singleThread = false 才让别的线程也跑
        if (Bool(a["singleThread"]) == false) ResumeAllExcept(threadId, sendContinued: true);
    }

    private JsonObject Continue(JsonObject a)
    {
        int threadId = Int(a["threadId"]) ?? -1;
        if (InRangeDebugger(threadId) is not null) _host.Step(threadId, null, sendContinued: false);
        bool all = Bool(a["singleThread"]) == false;
        if (all) ResumeAllExcept(threadId, sendContinued: false);
        return new JsonObject { ["allThreadsContinued"] = all };
    }

    private void ResumeAllExcept(int threadId, bool sendContinued)
    {
        foreach (int other in _host.Debuggers.Keys.ToList())
        {
            if (other != threadId && InRangeDebugger(other) is not null) _host.Step(other, null, sendContinued);
        }
    }

    /// <summary>上游 restart：有一个线程的施法者不在范围内就什么都不做；否则全部从头开始。</summary>
    private void Restart()
    {
        if (_host.Debuggers.Values.Any(d => !d.DebugEnv.IsCasterInRange)) return;
        _host.Restart(_host.Debuggers.Keys.ToList());
    }

    private void TerminateThreads(IEnumerable<int> threadIds)
    {
        var list = threadIds.Where(t => InRangeDebugger(t) is not null).ToList();
        if (list.Count > 0) _host.Terminate(list);
    }

    // ==================== 运行时数据 ====================

    private JsonObject Threads() => new()
    {
        ["threads"] = new JsonArray(_host.Debuggers.OrderBy(kv => kv.Key).Select(kv => (JsonNode)new JsonObject
        {
            ["id"] = kv.Key,
            ["name"] = $"Thread {kv.Key} ({kv.Value.DebugEnv.Name})",
        }).ToArray()),
    };

    private JsonObject StackTrace(JsonObject a)
    {
        int threadId = Int(a["threadId"]) ?? -1;
        var frames = Debugger(threadId)?.GetStackFrames() ?? new List<DapFrame>();
        var page = Paginate(frames, Int(a["startFrame"]), Int(a["levels"]));
        return new JsonObject
        {
            ["stackFrames"] = new JsonArray(page.Select(f =>
            {
                var o = new JsonObject { ["id"] = Pack(threadId, f.Id), ["name"] = f.Name, ["line"] = 0, ["column"] = 0 };
                SetPosition(o, f.Meta);
                if (f.Subtle) o["presentationHint"] = "subtle";
                return (JsonNode)o;
            }).ToArray()),
            ["totalFrames"] = frames.Count,
        };
    }

    private JsonObject Scopes(JsonObject a)
    {
        var (threadId, frameId) = Unpack(Int(a["frameId"]) ?? 0);
        var scopes = Debugger(threadId)?.GetScopes(frameId) ?? new List<DapScope>();
        return new JsonObject
        {
            ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)new JsonObject
            {
                ["name"] = s.Name,
                ["variablesReference"] = Pack(threadId, s.VariablesReference),
                ["expensive"] = false,
            }).ToArray()),
        };
    }

    private JsonObject Variables(JsonObject a)
    {
        var (threadId, reference) = Unpack(Int(a["variablesReference"]) ?? 0);
        var vars = Debugger(threadId)?.GetVariables(reference) ?? Array.Empty<DapVariable>();
        var page = Paginate(vars, Int(a["start"]), Int(a["count"]));
        return new JsonObject
        {
            ["variables"] = new JsonArray(page.Select(v =>
            {
                // 上游连 0 也打包，线程号大于 0 时「不能展开」就变成了「能展开」；这里 0 保持 0
                var o = new JsonObject
                {
                    ["name"] = v.Name,
                    ["value"] = DisplayTags.Strip(v.Value),
                    ["variablesReference"] = v.VariablesReference > 0 ? Pack(threadId, v.VariablesReference) : 0,
                };
                if (v.Type is not null) o["type"] = v.Type;
                if (v.IndexedVariables is { } n) o["indexedVariables"] = n;
                return (JsonNode)o;
            }).ToArray()),
        };
    }

    private JsonObject Source(JsonObject a)
    {
        var src = a["source"] as JsonObject;
        var debugger = (Int(src?["adapterData"]) is { } t ? Debugger(t) : null) ?? _lastDebugger ?? _host.Debuggers.Values.FirstOrDefault();
        int reference = Int(src?["sourceReference"]) ?? Int(a["sourceReference"]) ?? 0;
        string content = debugger?.GetSourceContents(reference)
                         ?? (Shared.FindSource(reference) is { } s ? string.Join("\n", s.Iotas.Select(i => IotaText.Source(i))) : "");
        return new JsonObject { ["content"] = content };
    }

    private static List<T> Paginate<T>(IReadOnlyList<T> list, int? start, int? count)
    {
        IEnumerable<T> r = list;
        if (start is > 0) r = r.Skip(start.Value);
        if (count is > 0) r = r.Take(count.Value);
        return r.ToList();
    }

    // ==================== 游戏通知（发事件）====================

    public void ThreadStarted(int threadId)
    {
        if (IsConnected) Event("thread", new JsonObject { ["reason"] = "started", ["threadId"] = threadId });
    }

    /// <summary>上游 removeThreadInner + postRemoveThreads：线程退出；一个都不剩时发 exited。</summary>
    public void ThreadExited(int threadId, bool noneLeft)
    {
        if (IsConnected)
        {
            Event("thread", new JsonObject { ["reason"] = "exited", ["threadId"] = threadId });
            if (noneLeft) Event("exited", new JsonObject { ["exitCode"] = 0 });
        }
        if (noneLeft && !IsConnected) _lastDebugger = null;
    }

    /// <summary>上游 handleDebuggerStep 里发事件的部分：新源码、停下或接着跑。</summary>
    public void OnStep(HexDebugger dbg, DebugStepResult result, bool wasPaused, bool sendContinued)
    {
        var loaded = dbg.TakeLoadedSources();
        _lastDebugger = dbg;
        if (!IsConnected || result.IsDone) return;
        foreach (var (src, isNew) in loaded)
        {
            Event("loadedSource", new JsonObject { ["reason"] = isNew ? "new" : "changed", ["source"] = SourceJson(src) });
        }
        if (result.Reason is { } reason)
        {
            SendStopped(dbg.ThreadId, reason);
        }
        else if (wasPaused && sendContinued)
        {
            Event("continued", new JsonObject { ["threadId"] = dbg.ThreadId, ["allThreadsContinued"] = false });
        }
    }

    /// <summary>
    /// 上游 print：调试输出。上游发的是消息原文；DAP 的输出是接着上一条写的，这里每条末尾补一个换行，免得连成一行。
    /// </summary>
    public void Output(HexDebugger dbg, string text, OutputCategory category, bool withSource)
    {
        if (!IsConnected) return;
        var o = new JsonObject
        {
            ["category"] = category == OutputCategory.Stderr ? "stderr" : "stdout",
            ["output"] = DisplayTags.Strip(text) + "\n",
        };
        if (withSource) SetPosition(o, dbg.LastEvaluatedMetadata);
        Event("output", o);
    }

    private void SendStopped(int? threadId, StopReason reason)
    {
        if (threadId is null)
        {
            foreach (int t in _host.Debuggers.Keys.ToList()) SendStopped(t, reason);
            return;
        }
        string? r = reason switch
        {
            StopReason.Step => "step",
            StopReason.Pause => "pause",
            StopReason.Breakpoint => "breakpoint",
            StopReason.Exception => "exception",
            StopReason.Started => "entry",
            _ => null,
        };
        if (r is not null) Event("stopped", new JsonObject { ["reason"] = r, ["threadId"] = threadId.Value });
    }

    // ==================== JSON ====================

    private JsonObject SourceJson(DebugSource s) => new()
    {
        ["name"] = s.Name,
        ["path"] = s.Name,
        ["sourceReference"] = s.Reference,
        ["adapterData"] = s.ThreadId,
    };

    private void SetPosition(JsonObject o, IotaMetadata? meta)
    {
        if (meta is null) return;
        o["source"] = SourceJson(meta.Source);
        o["line"] = Shared.IndexToLine(meta.LineIndex);
        if (meta.ColumnIndex is { } c) o["column"] = Shared.IndexToColumn(c);
    }

    private void Respond(JsonObject request, string command, bool success, JsonNode? body, string? message)
    {
        var o = new JsonObject
        {
            ["seq"] = _seq++,
            ["type"] = "response",
            ["request_seq"] = Int(request["seq"]) ?? 0,
            ["success"] = success,
            ["command"] = command,
        };
        if (message is not null) o["message"] = message;
        if (body is not null) o["body"] = body;
        _host.Send(o.ToJsonString());
    }

    private void Event(string name, JsonObject? body)
    {
        var o = new JsonObject { ["seq"] = _seq++, ["type"] = "event", ["event"] = name };
        if (body is not null) o["body"] = body;
        _host.Send(o.ToJsonString());
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<int>(out int i)) return i;
        if (v.TryGetValue<double>(out double d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue) return (int)d;
        return null;
    }

    private static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out bool b) ? b : null;
}

/// <summary>
/// DAP 的分帧（上游 lsp4j StreamMessageProducer / Consumer）：「Content-Length: N」+ 空行 + N 字节 UTF-8 的 JSON。
/// </summary>
public sealed class DapFraming
{
    private readonly List<byte> _buffer = new();

    public static byte[] Frame(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
        var all = new byte[header.Length + body.Length];
        header.CopyTo(all, 0);
        body.CopyTo(all, header.Length);
        return all;
    }

    /// <summary>喂进收到的字节，吐出凑齐了的消息。头里没有 Content-Length 的消息丢掉。</summary>
    public List<string> Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) _buffer.Add(b);
        var messages = new List<string>();
        while (true)
        {
            int end = HeaderEnd();
            if (end < 0) break;
            string header = Encoding.ASCII.GetString(_buffer.GetRange(0, end).ToArray());
            int? length = null;
            foreach (var line in header.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(t.AsSpan("Content-Length:".Length).Trim(), out int n) && n >= 0) length = n;
            }
            int bodyStart = end + 4;
            if (length is null)
            {
                _buffer.RemoveRange(0, bodyStart);
                continue;
            }
            if (_buffer.Count < bodyStart + length.Value) break;
            messages.Add(Encoding.UTF8.GetString(_buffer.GetRange(bodyStart, length.Value).ToArray()));
            _buffer.RemoveRange(0, bodyStart + length.Value);
        }
        return messages;
    }

    private int HeaderEnd()
    {
        for (int i = 0; i + 3 < _buffer.Count; i++)
        {
            if (_buffer[i] == 13 && _buffer[i + 1] == 10 && _buffer[i + 2] == 13 && _buffer[i + 3] == 10) return i;
        }
        return -1;
    }
}
