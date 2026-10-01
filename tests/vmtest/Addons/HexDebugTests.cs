using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace Addons;

/// <summary>
/// HexDebug 附属的离线用例：调试器在本体 VM 上一步步跑真实的咒术。期望值按上游 HexDebugger.kt 的步进规则推出来。
/// </summary>
static class HexDebugTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("HexDebug：" + name, ok, detail);

    sealed class TestDebugEnv : DebugEnvironment
    {
        public readonly List<(string Text, OutputCategory Cat)> Out = new();

        public TestDebugEnv() => Output = (t, c, _) => Out.Add((t, c));

        public override bool Resume(CastingEnvironment env, CastingImage image, ResolvedPatternType resolutionType) => false;

        public override void Restart(int threadId) { }

        public override void Terminate() { }

        public override bool IsCasterInRange => true;

        public override string Name => "测试";
    }

    static PatternIota P(string id) => new(PatternRegistry.FindById(id)!.Prototype);

    static PatternIota Num(double n)
    {
        HexPattern.TryFromAnglesUnchecked(SpecialPatterns.EncodeNumber(n)!, HexDir.SouthEast, out var p, out _);
        return new PatternIota(p!);
    }

    static string Stack(CastingImage img) => "[" + string.Join(", ", img.Stack.Select(IotaText.Display)) + "]";

    static (HexDebugger Dbg, TestDebugEnv DebugEnv, SharedDebugState Shared) New()
    {
        var shared = new SharedDebugState();
        var de = new TestDebugEnv();
        return (new HexDebugger(shared, de, 0), de, shared);
    }

    public static void Run()
    {
        Console.WriteLine("=== 附属 HexDebug：调试器 ===");
        PatternRegistry.DeclareAddonPatterns("hexdebug", HexDebugPatterns.All);
        PatternRegistry.SetAddonEnabled("hexdebug", true);
        PatternRegistry.RegisterAction("hexdebug:breakpoint/before", new OpBreakpoint(true));
        PatternRegistry.RegisterAction("hexdebug:breakpoint/after", new OpBreakpoint(false));
        PatternRegistry.RegisterAction("hexdebug:const/debugging", new OpIsDebugging());
        PatternRegistry.RegisterAction("hexdebug:const/cognitohazard", new OpCognitohazard());
        try
        {
            Stepping();
            Hermes();
            Breakpoints();
            Mishaps();
            Misc();
        }
        finally
        {
            PatternRegistry.SetAddonEnabled("hexdebug", false);
        }
    }

    static void Stepping()
    {
        var (dbg, _, _) = New();
        var hex = new Iota[] { Num(1), Num(2), P("hexcasting:add") };
        var start = dbg.StartExecuting(new TestEnv(), hex, null);
        Check("开始调试：默认在入口停（stopOnEntry），什么都还没跑", start?.Reason == StopReason.Started && dbg.State == DebuggerState.Paused
            && dbg.Image.Stack.Count == 0 && dbg.CurrentPosition()?.LineIndex == 0);
        Check("下一个要跑的是第 0 行", dbg.GetNextIotaToEvaluate()?.Index == 0);

        var r1 = dbg.ExecuteUntilStopped(RequestStepType.In);
        Check("单步调试：跑一个 iota 停在下一行", r1.Reason == StopReason.Step && Stack(dbg.Image) == "[1]" && dbg.CurrentPosition()?.LineIndex == 1,
            Stack(dbg.Image));
        dbg.ExecuteUntilStopped(RequestStepType.Over);
        Check("逐过程：再跑一个", Stack(dbg.Image) == "[1, 2]" && dbg.CurrentPosition()?.LineIndex == 2);
        var r3 = dbg.ExecuteUntilStopped(RequestStepType.Over);
        Check("跑完最后一个就结束（玩家调试不会接着跑）", r3.IsDone && dbg.State == DebuggerState.Terminated && Stack(dbg.Image) == "[3]", Stack(dbg.Image));

        var (dbg2, _, _) = New();
        dbg2.StartExecuting(new TestEnv(), hex, null);
        var rc = dbg2.ExecuteUntilStopped();
        Check("继续：没有断点就一口气跑完", rc.IsDone && Stack(dbg2.Image) == "[3]");
    }

    static void Hermes()
    {
        var (dbg, _, shared) = New();
        var five = Num(5);
        var hex = new Iota[] { P("hexcasting:open_paren"), five, P("hexcasting:close_paren"), P("hexcasting:eval") };
        dbg.StartExecuting(new TestEnv(), hex, null);
        var src = dbg.MetadataOf(hex[0])!.Source;

        dbg.ExecuteUntilStopped(RequestStepType.Over);
        Check("逐过程跨过整段括号（转义的几步不停），停在赫尔墨斯之策略", dbg.CurrentPosition()?.LineIndex == 3 && dbg.Image.Stack.Count == 1,
            dbg.CurrentPosition()?.LineIndex + " " + Stack(dbg.Image));
        var lines = dbg.SourceLines(src);
        Check("源码视图：括号写成 { }，括号里的缩进一层", lines.Count == 4 && lines[0] == "{" && lines[2] == "}"
            && lines[1] == "    " + IotaText.Source(five), string.Join(" | ", lines));

        var rin = dbg.ExecuteUntilStopped(RequestStepType.In);
        Check("单步调试进入赫尔墨斯：停在列表里第一个 iota（就是源码第 1 行那个）", rin.Type == DebugStepType.In
            && ReferenceEquals(dbg.CurrentPosition()?.Source, src) && dbg.CurrentPosition()?.LineIndex == 1, rin.Type?.ToString());
        var frames = dbg.StackFrames();
        Check("调用栈：当前帧在最前，下面还有调用它的帧", frames.Count >= 2 && frames[0].Name == "FrameEvaluate" && !frames[0].IsVirtual,
            string.Join(",", frames.Select(f => f.Name)));

        var rout = dbg.ExecuteUntilStopped(RequestStepType.Out);
        Check("单步跳出：跑完列表回到外面（这里外面已经没东西了，结束）", rout.IsDone && Stack(dbg.Image) == "[5]", Stack(dbg.Image));
        _ = shared;
    }

    static void Breakpoints()
    {
        var (dbg, _, _) = New();
        var hex = new Iota[] { Num(1), P("hexdebug:breakpoint/before"), Num(2), Num(3) };
        dbg.StartExecuting(new TestEnv(), hex, null);
        var r = dbg.ExecuteUntilStopped();
        Check("在前方添加断点：继续时停在断点处", r.Reason == StopReason.Breakpoint && Stack(dbg.Image) == "[1]" && dbg.State == DebuggerState.Paused,
            r.Reason + " " + Stack(dbg.Image));
        var r2 = dbg.ExecuteUntilStopped();
        Check("再继续就跑完", r2.IsDone && Stack(dbg.Image) == "[1, 2, 3]");

        var (dbg2, _, shared2) = New();
        var hex2 = new Iota[] { Num(1), Num(2), Num(3), Num(4) };
        dbg2.StartExecuting(new TestEnv(), hex2, null);
        var src = dbg2.MetadataOf(hex2[0])!.Source;
        Check("面板里点行设断点", shared2.ToggleBreakpoint(src.Reference, 2) && shared2.BreakpointAt(src.Reference, 2) == SourceBreakpointMode.Evaluated);
        var r3 = dbg2.ExecuteUntilStopped();
        Check("行断点：继续时停在第 2 行之前", r3.Reason == StopReason.Breakpoint && dbg2.CurrentPosition()?.LineIndex == 2 && Stack(dbg2.Image) == "[1, 2]");
        Check("再点一次去掉断点", !shared2.ToggleBreakpoint(src.Reference, 2) && shared2.BreakpointAt(src.Reference, 2) is null);

        // 正常施法里断点图案没有任何效果
        var vm = CastingVM.Empty(new TestEnv());
        var outcome = vm.QueueExecute(vm.Image, hex);
        Check("不调试时断点图案什么都不做", Stack(outcome.Image) == "[1, 2, 3]" && outcome.ResolutionType.IsSuccess());
    }

    static void Mishaps()
    {
        var (dbg, de, _) = New();
        var hex = new Iota[] { Num(1), P("hexcasting:add"), Num(9) };
        dbg.StartExecuting(new TestEnv(), hex, null);
        var r = dbg.ExecuteUntilStopped();
        Check("出事故时停下（默认捕获未处理的事故），栈是事故之前的样子", r.Reason == StopReason.Exception && dbg.State == DebuggerState.CaughtMishap
            && Stack(dbg.Image) == "[1]", r.Reason + " " + Stack(dbg.Image));
        var r2 = dbg.ExecuteUntilStopped();
        Check("事故后再继续就结束，后面的不跑", r2.IsDone && dbg.State == DebuggerState.Terminated);
        _ = de;

        var (dbg2, _, shared2) = New();
        shared2.StopOnUncaughtMishaps = false;
        dbg2.StartExecuting(new TestEnv(), hex, null);
        var r3 = dbg2.ExecuteUntilStopped();
        Check("关掉捕获：事故直接结束", r3.IsDone);
    }

    static void Misc()
    {
        // 调试杖之精思
        var (dbg, _, _) = New();
        dbg.StartExecuting(new TestEnv(), new Iota[] { P("hexdebug:const/debugging") }, null);
        dbg.ExecuteUntilStopped();
        var vm = CastingVM.Empty(new TestEnv());
        var plain = vm.QueueExecute(vm.Image, new Iota[] { P("hexdebug:const/debugging") });
        Check("调试杖之精思：调试时 true，平时 false", Stack(dbg.Image) == "[true]" && Stack(plain.Image) == "[false]",
            Stack(dbg.Image) + " / " + Stack(plain.Image));

        // 认知危害：登记进来就结束，什么都不跑
        var (dbg2, _, _) = New();
        dbg2.StartExecuting(new TestEnv(), new Iota[] { Num(1), new CognitohazardIota() }, null);
        var r = dbg2.ExecuteUntilStopped();
        Check("认知危害：调试直接结束，一个都不跑", r.IsDone && dbg2.Image.Stack.Count == 0);
        var plain2 = vm.QueueExecute(new CastingImage(), new Iota[] { Num(1), new CognitohazardIota() });
        Check("认知危害：平时求值什么都不做", Stack(plain2.Image) == "[1]" && plain2.ResolutionType.IsSuccess());
        Check("认知危害之精思：压一个认知危害", vm.QueueExecute(new CastingImage(), new Iota[] { P("hexdebug:const/cognitohazard") }).Image.Stack.Single() is CognitohazardIota);

        // 运行杖：在调试器当前的栈上跑，潜行使用回到画之前
        var (dbg3, _, _) = New();
        dbg3.StartExecuting(new TestEnv(), new Iota[] { Num(1), Num(2) }, null);
        dbg3.ExecuteUntilStopped(RequestStepType.In);
        var ev = dbg3.Evaluate(new SpellList.LList(0, new Iota[] { Num(7) }));
        Check("运行杖：画的图案在调试器的栈上跑，原来的位置不变", ev?.StartedEvaluating == true && Stack(dbg3.Image) == "[1, 7]"
            && dbg3.CurrentPosition()?.LineIndex == 1, Stack(dbg3.Image));
        var ev2 = dbg3.Evaluate(new SpellList.LList(0, new Iota[] { Num(8) }));
        Check("运行杖：只有第一次算「开始」", ev2?.StartedEvaluating == false && Stack(dbg3.Image) == "[1, 7, 8]");
        Check("运行杖潜行：回到画第一个图案之前", dbg3.ResetEvaluator() && Stack(dbg3.Image) == "[1]" && !dbg3.ResetEvaluator());

        // 输出：揭示打到调试输出，事故打成错误
        var (dbg4, de4, _) = New();
        dbg4.StartExecuting(new PrintingEnv(), new Iota[] { Num(4), P("hexcasting:print"), P("hexcasting:add") }, null);
        dbg4.ExecuteUntilStopped();
        Check("调试输出：揭示是普通输出", de4.Out.Any(o => o.Cat == OutputCategory.Stdout), string.Join(" | ", de4.Out));

        // 显示文字
        Check("源码写法：不认识的图案写成 <方向 签名>", IotaText.Source(new PatternIota(MakePattern("qwqwqwqwqwqwqwqw"))) == "<EAST qwqwqwqwqwqwqwqw>");
        Check("源码写法：列表与数字", IotaText.Source(new ListIota(new Iota[] { new DoubleIota(1) })).StartsWith("<[", StringComparison.Ordinal));
    }

    sealed class PrintingEnv : CastingEnvironment
    {
        protected override long ExtractMediaEnvironment(long cost, bool simulate) => 0;

        public override void PrintMessage(string message)
        {
            DebugObserver?.OnPrint(message);
        }
    }

    static HexPattern MakePattern(string sig)
    {
        HexPattern.TryFromAnglesUnchecked(sig, HexDir.East, out var p, out _);
        return p!;
    }
}
