using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace Addons;

/// <summary>
/// HexDebug 外部调试器（DAP）：按编辑器的顺序发请求（initialize → attach → setBreakpoints → configurationDone → …），
/// 看响应和事件。期望值按上游 adapter/DebugAdapter.kt 与 DAP 规范推。游戏侧（端口、联机转发）离线测不到。
/// </summary>
static class DapTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("DAP：" + name, ok, detail);

    sealed class Env : DebugEnvironment
    {
        public override bool Resume(CastingEnvironment env, CastingImage image, ResolvedPatternType resolutionType) => false;
        public override void Restart(int threadId) { }
        public override void Terminate() { }
        public override bool IsCasterInRange => true;
        public override string Name => "测试杖";
    }

    sealed class Host : IDebugAdapterHost
    {
        public readonly Dictionary<int, HexDebugger> Map = new();
        public readonly List<JsonObject> Sent = new();
        public readonly List<string> Statuses = new();
        public int BreakpointChanges;
        public DebugAdapter Adapter = null!;

        public SharedDebugState Shared { get; } = new();
        public IReadOnlyDictionary<int, HexDebugger> Debuggers => Map;
        public string PlayerKey => "Tester";

        // 和游戏侧 HexDebugSessions.Handle 一样：走一步、发事件、结束了就移除线程
        public void Step(int threadId, RequestStepType? type, bool sendContinued)
        {
            var dbg = Map[threadId];
            var r = dbg.ExecuteUntilStopped(type);
            Adapter.OnStep(dbg, r, true, sendContinued);
            if (r.IsDone) Remove(threadId);
        }

        public void Remove(int threadId)
        {
            Map.Remove(threadId);
            Adapter.ThreadExited(threadId, Map.Count == 0);
        }

        public void Restart(IReadOnlyList<int> threadIds) { }
        public void Terminate(IReadOnlyList<int> threadIds) { foreach (var t in threadIds) Remove(t); }
        public void Send(string json) => Sent.Add((JsonNode.Parse(json) as JsonObject)!);
        public void Status(string key) => Statuses.Add(key);
        public void BreakpointsChanged() => BreakpointChanges++;

        public HexDebugger StartThread(int threadId, IReadOnlyList<Iota> hex)
        {
            var dbg = new HexDebugger(Shared, new Env(), threadId);
            Map[threadId] = dbg;
            Adapter.ThreadStarted(threadId);
            var r = dbg.StartExecuting(new TestEnv(), hex, null)!;
            Adapter.OnStep(dbg, r, false, true);
            return dbg;
        }
    }

    static int _seq = 100;

    /// <summary>发一个请求，返回它的响应和这期间发出的事件。</summary>
    static (JsonObject Response, List<JsonObject> Events) Req(Host h, string command, JsonObject? args = null)
    {
        int seq = ++_seq;
        var msg = new JsonObject { ["seq"] = seq, ["type"] = "request", ["command"] = command };
        if (args is not null) msg["arguments"] = args;
        int before = h.Sent.Count;
        h.Adapter.Handle(msg.ToJsonString());
        var got = h.Sent.Skip(before).ToList();
        var resp = got.First(m => (string?)m["type"] == "response");
        return (resp, got.Where(m => (string?)m["type"] == "event").ToList());
    }

    static List<JsonObject> EventsDuring(Host h, Action act)
    {
        int before = h.Sent.Count;
        act();
        return h.Sent.Skip(before).Where(m => (string?)m["type"] == "event").ToList();
    }

    static string? Ev(JsonObject e) => (string?)e["event"];

    static PatternIota P(string id) => new(PatternRegistry.FindById(id)!.Prototype);

    static PatternIota Num(double n)
    {
        HexPattern.TryFromAnglesUnchecked(SpecialPatterns.EncodeNumber(n)!, HexDir.SouthEast, out var p, out _);
        return new PatternIota(p!);
    }

    public static void Run()
    {
        Console.WriteLine("=== 附属 HexDebug：外部调试器（DAP）===");
        DebugAdapter.ForgetKnownPlayers();
        var h = new Host();
        h.Adapter = new DebugAdapter(h);

        var (init, _) = Req(h, "initialize", new JsonObject { ["linesStartAt1"] = true, ["columnsStartAt1"] = true });
        var caps = init["body"] as JsonObject;
        Check("initialize：成功，带上游的能力（事故断点过滤器、三种断点模式）",
            (bool?)init["success"] == true && (string?)init["command"] == "initialize"
            && (string?)caps?["exceptionBreakpointFilters"]?[0]?["filter"] == "UNCAUGHT_MISHAPS"
            && (caps?["breakpointModes"] as JsonArray)?.Count == 3 && (bool?)caps?["supportsConfigurationDoneRequest"] == true,
            init.ToJsonString());
        Check("响应的 request_seq 对得上请求", (int?)init["request_seq"] == _seq);

        var (attach, attachEvents) = Req(h, "attach", new JsonObject { ["stopOnEntry"] = true, ["indentWidth"] = 2 });
        Check("attach：成功，发 initialized，提示「已连接」，启动参数生效",
            (bool?)attach["success"] == true && attachEvents.Any(e => Ev(e) == "initialized") && h.Statuses.Contains("Connected")
            && h.Adapter.IsConnected && h.Shared.LaunchArgs.IndentWidth == 2);

        // 开始调试一段：1 2 加法
        HexDebugger dbg = null!;
        var startEvents = EventsDuring(h, () => dbg = h.StartThread(0, new Iota[] { Num(1), Num(2), P("hexcasting:add") }));
        var loaded = startEvents.FirstOrDefault(e => Ev(e) == "loadedSource");
        int sourceRef = (int?)loaded?["body"]?["source"]?["sourceReference"] ?? -1;
        Check("开始调试：线程 started、新源码 loadedSource、在入口停（stopped: entry）",
            startEvents.Any(e => Ev(e) == "thread" && (string?)e["body"]!["reason"] == "started")
            && (string?)loaded?["body"]?["reason"] == "new" && (string?)loaded?["body"]?["source"]?["name"] == "source" + sourceRef + ".hexpattern"
            && startEvents.Any(e => Ev(e) == "stopped" && (string?)e["body"]!["reason"] == "entry"),
            string.Join(" ", startEvents.Select(e => e.ToJsonString())));

        var bpArgs = new JsonObject
        {
            ["source"] = new JsonObject { ["sourceReference"] = sourceRef },
            ["breakpoints"] = new JsonArray(new JsonObject { ["line"] = 3 }, new JsonObject { ["line"] = 9 }),
        };
        var (early, _) = Req(h, "setBreakpoints", (JsonObject)bpArgs.DeepClone());
        Check("configurationDone 之前（这次开服还没连过）发来的断点一律作废", (bool?)early["body"]?["breakpoints"]?[0]?["verified"] == false
            && (string?)early["body"]?["breakpoints"]?[0]?["message"] == "Invalid");

        var (done, doneEvents) = Req(h, "configurationDone");
        Check("configurationDone：有线程就对每个线程发 stopped", (bool?)done["success"] == true
            && doneEvents.Any(e => Ev(e) == "stopped" && (int?)e["body"]!["threadId"] == 0));

        var (bps, _) = Req(h, "setBreakpoints", (JsonObject)bpArgs.DeepClone());
        var list = bps["body"]?["breakpoints"] as JsonArray;
        Check("setBreakpoints：第 3 行（1 起）有效，第 9 行超出范围", (bool?)list?[0]?["verified"] == true && (int?)list?[0]?["line"] == 3
            && (bool?)list?[1]?["verified"] == false && (string?)list?[1]?["message"] == "Line number out of range"
            && h.Shared.BreakpointAt(sourceRef, 2) == SourceBreakpointMode.Evaluated && h.BreakpointChanges > 0,
            bps.ToJsonString());

        var (threads, _) = Req(h, "threads");
        Check("threads：线程号和名字", (int?)threads["body"]?["threads"]?[0]?["id"] == 0
            && (string?)threads["body"]?["threads"]?[0]?["name"] == "Thread 0 (测试杖)");

        var (trace, _) = Req(h, "stackTrace", new JsonObject { ["threadId"] = 0 });
        var frame0 = trace["body"]?["stackFrames"]?[0] as JsonObject;
        Check("stackTrace：一帧，在源码第 1 行", (int?)trace["body"]?["totalFrames"] == 1 && (int?)frame0?["line"] == 1
            && (int?)frame0?["source"]?["sourceReference"] == sourceRef && (string?)frame0?["name"] == "[1] FrameEvaluate",
            trace.ToJsonString());

        var (scopes, _) = Req(h, "scopes", new JsonObject { ["frameId"] = (int)frame0!["id"]! });
        var names = (scopes["body"]?["scopes"] as JsonArray)?.Select(s => (string?)s?["name"]).ToList();
        Check("scopes：Data、State、Frame", names is not null && names.SequenceEqual(new[] { "Data", "State", "Frame" }), scopes.ToJsonString());

        var (src, _) = Req(h, "source", new JsonObject { ["source"] = new JsonObject { ["sourceReference"] = sourceRef } });
        var content = (string?)src["body"]?["content"] ?? "";
        Check("source：一行一个 iota", content.Split('\n').Length == 3 && content.Split('\n')[2] == IotaText.Source(P("hexcasting:add")), content);

        var (cont, contEvents) = Req(h, "continue", new JsonObject { ["threadId"] = 0 });
        Check("continue：跑到第 3 行的断点停下（stopped: breakpoint），自己不发 continued",
            (bool?)cont["success"] == true && (bool?)cont["body"]?["allThreadsContinued"] == false
            && contEvents.Any(e => Ev(e) == "stopped" && (string?)e["body"]!["reason"] == "breakpoint")
            && !contEvents.Any(e => Ev(e) == "continued") && dbg.CurrentPosition()?.LineIndex == 2,
            string.Join(" ", contEvents.Select(e => e.ToJsonString())));

        // 看栈：Data → Stack → 两个数，栈顶在前
        var (sc2, _) = Req(h, "scopes", new JsonObject { ["frameId"] = (int)((JsonObject)Req(h, "stackTrace", new JsonObject { ["threadId"] = 0 }).Response["body"]!["stackFrames"]![0]!)["id"]! });
        int dataRef = (int)sc2["body"]!["scopes"]![0]!["variablesReference"]!;
        var (dataVars, _) = Req(h, "variables", new JsonObject { ["variablesReference"] = dataRef });
        var stackVar = dataVars["body"]?["variables"]?[0] as JsonObject;
        var (stackItems, _) = Req(h, "variables", new JsonObject { ["variablesReference"] = (int)stackVar!["variablesReference"]! });
        var items = (stackItems["body"]?["variables"] as JsonArray)?.Select(v => (string?)v?["value"]).ToList();
        Check("variables：栈展开是 2.00、1.00（栈顶在前），叶子不能再展开",
            (string?)stackVar["name"] == "Stack" && items is not null && items.SequenceEqual(new[] { "2.00", "1.00" })
            && (int?)stackItems["body"]!["variables"]![0]!["variablesReference"] == 0
            && (string?)dataVars["body"]?["variables"]?[1]?["value"] == "Null",
            stackItems.ToJsonString());

        var outEvents = EventsDuring(h, () => h.Adapter.Output(dbg, DisplayTags.Of(new DoubleIota(3)), OutputCategory.Stdout, true));
        Check("输出：去掉颜色标记、末尾换行、带上源码位置", (string?)outEvents.Single()["body"]!["output"] == "3.00\n"
            && (string?)outEvents.Single()["body"]!["category"] == "stdout", outEvents.Single().ToJsonString());

        var (next, nextEvents) = Req(h, "next", new JsonObject { ["threadId"] = 0 });
        Check("next 跑完最后一个：线程 exited，然后整个 exited", (bool?)next["success"] == true
            && nextEvents.Any(e => Ev(e) == "thread" && (string?)e["body"]!["reason"] == "exited")
            && nextEvents.Any(e => Ev(e) == "exited") && h.Map.Count == 0,
            string.Join(" ", nextEvents.Select(e => e.ToJsonString())));

        // 第二个线程：引用编号带线程号，叶子保持 0
        var dbg1 = h.StartThread(1, new Iota[] { Num(4), Num(5) });
        h.Step(1, RequestStepType.Over, true);
        var trace1 = Req(h, "stackTrace", new JsonObject { ["threadId"] = 1 }).Response;
        int fid = (int)trace1["body"]!["stackFrames"]![0]!["id"]!;
        var sc1 = Req(h, "scopes", new JsonObject { ["frameId"] = fid }).Response;
        int dref = (int)sc1["body"]!["scopes"]![0]!["variablesReference"]!;
        var v1 = Req(h, "variables", new JsonObject { ["variablesReference"] = dref }).Response;
        int stackRef = (int)v1["body"]!["variables"]![0]!["variablesReference"]!;
        var leaf = Req(h, "variables", new JsonObject { ["variablesReference"] = stackRef }).Response;
        Check("线程 1：帧号、变量号的高位是线程号；不能展开的叶子是 0",
            (fid >> 28) == 1 && (dref >> 28) == 1 && (string?)leaf["body"]!["variables"]![0]!["value"] == "4.00"
            && (int?)leaf["body"]!["variables"]![0]!["variablesReference"] == 0, leaf.ToJsonString());

        var byPath = Req(h, "setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "D:/somewhere/" + dbg1.CurrentPosition()!.Source.Name },
            ["breakpoints"] = new JsonArray(new JsonObject { ["line"] = 2, ["mode"] = "ESCAPED" }),
        }).Response;
        Check("只带文件名（没有 sourceReference）也认得源码；断点模式照发来的", (bool?)byPath["body"]?["breakpoints"]?[0]?["verified"] == true
            && h.Shared.BreakpointAt(dbg1.CurrentPosition()!.Source.Reference, 1) == SourceBreakpointMode.Escaped, byPath.ToJsonString());

        var (exc, _) = Req(h, "setExceptionBreakpoints", new JsonObject { ["filters"] = new JsonArray() });
        Check("setExceptionBreakpoints：去掉「未捕获的事故」就不再停", (bool?)exc["success"] == true && !h.Shared.StopOnUncaughtMishaps);

        var (eval, _) = Req(h, "evaluate", new JsonObject { ["expression"] = "1" });
        Check("不支持的请求回失败的响应（编辑器不会一直等）", (bool?)eval["success"] == false && (string?)eval["command"] == "evaluate");

        var (disc, _) = Req(h, "disconnect");
        Check("disconnect：断开，断点和「未捕获的事故」回到默认", (bool?)disc["success"] == true && !h.Adapter.IsConnected
            && h.Shared.Breakpoints.Count == 0 && h.Shared.StopOnUncaughtMishaps);
        var quiet = EventsDuring(h, () => h.Step(1, null, true));
        Check("断开以后不再发事件", quiet.Count == 0);

        h.Adapter.Handle("{ not json");
        Check("坏 JSON 不崩、不回东西", true);

        Framing();
    }

    static void Framing()
    {
        string a = "{\"seq\":1,\"type\":\"request\",\"command\":\"threads\"}";
        string b = "{\"text\":\"中文和 Ω\"}";
        var bytes = DapFraming.Frame(a).Concat(DapFraming.Frame(b)).ToArray();
        var f = new DapFraming();
        var got = new List<string>();
        // 一个字节一个字节喂（跨过头、跨过多字节字符）
        foreach (byte x in bytes) got.AddRange(f.Feed(new[] { x }));
        Check("分帧：零碎到达也能拼回两条，中文不乱", got.Count == 2 && got[0] == a && got[1] == b, string.Join(" | ", got));
        Check("分帧：头部是 Content-Length（按字节数）", Encoding.ASCII.GetString(DapFraming.Frame(b)).StartsWith("Content-Length: " + Encoding.UTF8.GetByteCount(b)));
        var g = new DapFraming();
        var two = g.Feed(bytes);
        Check("分帧：一次到两条", two.Count == 2);
    }
}
