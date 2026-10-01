using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Config;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// 玩家用调试杖的调试来历（上游 core api SimplePlayerBasedDebugEnv）：跑完不接着跑；重启 = 用同一串 iota 重新开始。
/// </summary>
internal sealed class PlayerDebugEnv : DebugEnvironment
{
    private readonly Player _player;

    public PlayerDebugEnv(Player player, CastingEnvironment env, IReadOnlyList<Iota> iotas, string name)
    {
        _player = player;
        Env = env;
        Iotas = iotas;
        Name = name;
    }

    public CastingEnvironment Env { get; }

    public IReadOnlyList<Iota> Iotas { get; }

    public override string Name { get; }

    public override bool Resume(CastingEnvironment env, CastingImage image, ResolvedPatternType resolutionType) => false;

    public override void Restart(int threadId) => HexDebugSessions.Start(_player, this, threadId);

    public override void Terminate() { }

    public override bool IsCasterInRange => _player is { active: true, dead: false };
}

/// <summary>
/// 服务端（单机就是本地）的调试会话（上游 adapter/DebugAdapter.kt 里管线程的部分）：每个玩家一份，最多若干个线程。
/// 调试杖每用一次按步进模式走一步；运行杖在暂停的线程上画图案；调试面板点行设断点。
/// 外部调试器（VSCode）的请求由 Core 的 <see cref="DebugAdapter"/> 处理，它通过 <see cref="AdapterHost"/> 回到这里。
/// </summary>
internal static class HexDebugSessions
{
    private sealed class Session
    {
        public readonly SharedDebugState Shared = new();
        public readonly Dictionary<int, HexDebugger> Debuggers = new();
        public readonly HashSet<int> EvaluatorModified = new();
        public readonly Dictionary<int, StopReason?> LastReason = new();
        public DebugAdapter? Adapter;
    }

    /// <summary>调试适配器要游戏做的事（一个玩家一个）。</summary>
    private sealed class AdapterHost : IDebugAdapterHost
    {
        private readonly int _who;

        public AdapterHost(int who) => _who = who;

        private Player Player => Main.player[_who];

        public SharedDebugState Shared => Get(_who).Shared;

        public IReadOnlyDictionary<int, HexDebugger> Debuggers => Get(_who).Debuggers;

        public string PlayerKey => Player.name;

        public void Step(int threadId, RequestStepType? type, bool sendContinued)
        {
            if (Debugger(_who, threadId) is { } dbg) Handle(Player, dbg, dbg.ExecuteUntilStopped(type), wasPaused: true, sendContinued);
        }

        public void Restart(IReadOnlyList<int> threadIds)
        {
            foreach (int t in threadIds) RestartThread(Player, t);
        }

        public void Terminate(IReadOnlyList<int> threadIds)
        {
            foreach (int t in threadIds) RemoveThread(_who, t, terminate: true);
        }

        public void Send(string json) => HexDebugNet.SendDap(_who, json);

        public void Status(string key) => HexDebugSessions.Status(Player, HexDebugText.Get(key));

        public void BreakpointsChanged()
        {
            foreach (var dbg in Get(_who).Debuggers.Values) SendView(_who, dbg);
        }
    }

    private static DebugAdapter AdapterOf(int who)
    {
        var s = Get(who);
        return s.Adapter ??= new DebugAdapter(new AdapterHost(who));
    }

    /// <summary>玩家电脑上的编辑器发来的一条 DAP 消息（客户端原样转过来的）。</summary>
    public static void ReceiveDap(Player player, string json) => AdapterOf(player.whoAmI).Handle(json);

    private static readonly Dictionary<int, Session> Sessions = new();

    private static Session Get(int who)
    {
        if (!Sessions.TryGetValue(who, out var s)) Sessions[who] = s = new Session();
        return s;
    }

    public static HexDebugger? Debugger(int who, int threadId)
        => Sessions.TryGetValue(who, out var s) && s.Debuggers.TryGetValue(threadId, out var d) ? d : null;

    /// <summary>上游 maxDebugThreads(caster)：启蒙了才有配置里那么多，没启蒙只有 1 个。</summary>
    public static int MaxThreads(Player p) => HexPlayer.Get(p).Enlightened ? HexAddonsConfig.Instance.HexDebugOptions.MaxDebugThreads : 1;

    // ==================== 调试杖 ====================

    /// <summary>
    /// 上游 DebuggerItem.useOn：对着促动石用 = 开始调试这个法术环（这个线程没在用、环也没在跑时）；
    /// 否则当成平常的使用（上游 useOn 返回 PASS 后 MC 再调 use）。
    /// </summary>
    public static void UseDebuggerOn(Player player, int slot, int x, int y)
    {
        if (slot >= 0 && slot < player.inventory.Length && player.inventory[slot].ModItem is DebuggerItemBase item
            && Debugger(player.whoAmI, item.ThreadId) is null
            && CircleDebugging.ImpetusAt(player, x, y) is { IsRunning: false } impetus
            && HexPlayer.Get(player).PackagedCooldown <= 0)
        {
            if (CircleDebugging.Start(player, impetus, item.ThreadId))
            {
                HexPlayer.Get(player).StartPackagedCooldown(item.CooldownTicks);
                return;
            }
        }
        UseDebugger(player, slot);
    }

    /// <summary>上游 DebuggerItem.use：没在调试就开始；在调试就按步进模式走一步（正在跑时按「可暂停」的模式就暂停）。</summary>
    public static void UseDebugger(Player player, int slot)
    {
        if (slot < 0 || slot >= player.inventory.Length || player.inventory[slot].ModItem is not DebuggerItemBase item) return;
        int threadId = item.ThreadId;
        var hexPlayer = HexPlayer.Get(player);
        if (hexPlayer.PackagedCooldown > 0) return;

        if (Debugger(player.whoAmI, threadId) is { } dbg)
        {
            if (!dbg.DebugEnv.IsCasterInRange)
            {
                Status(player, HexDebugText.Get("OutOfRange"));
                return;
            }
            var mode = item.Mode;
            if (dbg.State.CanPause() && mode.CanPause())
            {
                dbg.Pause();
            }
            else
            {
                switch (mode)
                {
                    case StepMode.Continue: Step(player, dbg, null); break;
                    case StepMode.Over: Step(player, dbg, RequestStepType.Over); break;
                    case StepMode.In: Step(player, dbg, RequestStepType.In); break;
                    case StepMode.Out: Step(player, dbg, RequestStepType.Out); break;
                    case StepMode.Restart: RestartThread(player, threadId); break;
                    case StepMode.Stop: RemoveThread(player.whoAmI, threadId, terminate: true); break;
                }
            }
        }
        else
        {
            // 开始调试：调试杖里封着的咒术，没有就读另一只手里的列表（上游 otherHand 的 datum holder）
            var env = new PackagedSpellEnvironment(player, item, slot);
            IReadOnlyList<Iota>? iotas = !item.IsEmpty ? item.Program : env.ReadHeldIota() is ListIota li ? li.Items : null;
            if (iotas is null) return;

            var debugEnv = new PlayerDebugEnv(player, env, iotas.ToList(), item.Item.Name);
            if (!Start(player, debugEnv, threadId))
            {
                Status(player, HexDebugText.Get("IllegalThread"));
                return;
            }
        }
        hexPlayer.StartPackagedCooldown(item.CooldownTicks);
    }

    /// <summary>调试杖：建线程并开始调试它封着的咒术。线程号超出范围或已被占用时返回 false。</summary>
    public static bool Start(Player player, PlayerDebugEnv debugEnv, int threadId)
    {
        if (!CreateThread(player, threadId, () => debugEnv, out _)) return false;
        if (!StartDebuggingIotas(player, debugEnv, debugEnv.Env, debugEnv.Iotas, null))
        {
            RemoveThread(player.whoAmI, threadId, terminate: false);
            return false;
        }
        return true;
    }

    /// <summary>上游 createDebugThread：线程号超出范围（没启蒙只有 1 个）或已被占用时返回 false。</summary>
    public static bool CreateThread(Player player, int threadId, System.Func<DebugEnvironment> makeEnv, out DebugEnvironment debugEnv)
    {
        debugEnv = null!;
        var s = Get(player.whoAmI);
        if (threadId < 0 || threadId >= MaxThreads(player) || s.Debuggers.ContainsKey(threadId)) return false;
        debugEnv = makeEnv();
        var dbg = new HexDebugger(s.Shared, debugEnv, threadId);
        debugEnv.Output = (text, cat, withSource) =>
        {
            HexDebugNet.ToClient(player.whoAmI, HexDebugNet.Msg.Output, w =>
            {
                w.Write((byte)threadId);
                w.Write(text);
                w.Write((byte)cat);
            });
            s.Adapter?.Output(dbg, text, cat, withSource);
        };
        s.Debuggers[threadId] = dbg;
        s.EvaluatorModified.Remove(threadId);
        s.Adapter?.ThreadStarted(threadId);
        SendView(player.whoAmI, dbg);
        return true;
    }

    /// <summary>上游 startDebuggingIotas：在这个线程上开始（或接着，法术环的下一块石板）调试一串 iota。</summary>
    public static bool StartDebuggingIotas(Player player, DebugEnvironment debugEnv, CastingEnvironment env, IReadOnlyList<Iota> iotas, CastingImage? image)
    {
        if (!Sessions.TryGetValue(player.whoAmI, out var s)) return false;
        var dbg = s.Debuggers.Values.FirstOrDefault(d => ReferenceEquals(d.DebugEnv, debugEnv));
        if (dbg is null) return false;
        var result = dbg.StartExecuting(env, iotas, image);
        if (result is null) return false;
        Handle(player, dbg, result, wasPaused: false);
        return true;
    }

    private static void Step(Player player, HexDebugger dbg, RequestStepType? type) => Handle(player, dbg, dbg.ExecuteUntilStopped(type));

    private static void RestartThread(Player player, int threadId)
    {
        if (Debugger(player.whoAmI, threadId) is not { } dbg) return;
        RemoveThread(player.whoAmI, threadId, terminate: false);
        dbg.DebugEnv.Restart(threadId);
    }

    /// <summary>
    /// 上游 handleDebuggerStep：结束了就移除线程；停下了就打印下一个 iota；把面板要的东西发给本人；连着编辑器时发事件。
    /// <paramref name="wasPaused"/> = 这一步之前是停着的（刚开始调试时不是）；<paramref name="sendContinued"/> = 接着跑时要不要告诉编辑器（「继续」请求自己会说）。
    /// </summary>
    private static void Handle(Player player, HexDebugger dbg, DebugStepResult result, bool wasPaused = true, bool sendContinued = true)
    {
        FlushEffects(player, dbg);
        Get(player.whoAmI).Adapter?.OnStep(dbg, result, wasPaused, sendContinued);
        if (result.IsDone)
        {
            RemoveThread(player.whoAmI, dbg.ThreadId, terminate: true);
            return;
        }
        Get(player.whoAmI).LastReason[dbg.ThreadId] = result.Reason;
        if (result.Reason is not null && dbg.GetNextIotaToEvaluate() is { } next)
        {
            Status(player, HexDebugText.Get("DebuggerStopped", next.Index, next.Text));
        }
        SendView(player.whoAmI, dbg);
    }

    /// <summary>这一步的粒子与音效（本体 QueueExecute 也是把它们带出来交给表现层）。</summary>
    private static void FlushEffects(Player player, HexDebugger dbg)
    {
        if (dbg.PendingParticles.Count > 0)
        {
            SpellVisuals.Broadcast(dbg.PendingParticles.ToList(), player);
            dbg.PendingParticles.Clear();
        }
        if (dbg.PendingSound != EvalSound.Nothing)
        {
            SpellSounds.EmitEval(dbg.PendingSound, player.Center);
            dbg.PendingSound = EvalSound.Nothing;
        }
    }

    public static void RemoveThread(int who, int threadId, bool terminate)
    {
        if (!Sessions.TryGetValue(who, out var s) || !s.Debuggers.Remove(threadId, out var dbg)) return;
        s.EvaluatorModified.Remove(threadId);
        s.LastReason.Remove(threadId);
        if (terminate) dbg.DebugEnv.Terminate();
        s.Adapter?.ThreadExited(threadId, s.Debuggers.Count == 0);
        HexDebugNet.ToClient(who, HexDebugNet.Msg.RemoveThread, w => w.Write((byte)threadId));
    }

    /// <summary>
    /// 上游 onDeath：死了就结束这个玩家的全部调试（编辑器的连接、断点留着）；
    /// 上游 onRemove：下线时连会话一起丢掉（<paramref name="forget"/>）。
    /// </summary>
    public static void TerminateAll(int who, bool forget)
    {
        if (!Sessions.TryGetValue(who, out var s)) return;
        foreach (var id in s.Debuggers.Keys.ToList()) RemoveThread(who, id, terminate: true);
        if (forget) Sessions.Remove(who);
    }

    /// <summary>退出世界（单机就是关服）：全部丢掉；上游的 knownPlayers 也是开服期间才记得。</summary>
    public static void Clear()
    {
        Sessions.Clear();
        DebugAdapter.ForgetKnownPlayers();
    }

    // ==================== 运行杖 ====================

    /// <summary>上游 EvaluatorItem.use：要有正在调试、已暂停的线程；潜行使用先复原（回到画第一个图案之前）。</summary>
    public static void OpenEvaluator(Player player, int threadId, bool reset)
    {
        var dbg = Debugger(player.whoAmI, threadId);
        if (dbg is null)
        {
            Status(player, HexDebugText.Get("NoSession", threadId));
            return;
        }
        if (!dbg.DebugEnv.IsCasterInRange) return;
        if (dbg.State != DebuggerState.Paused)
        {
            Status(player, HexDebugText.Get("NotPaused", threadId));
            return;
        }
        if (reset)
        {
            dbg.ResetEvaluator();
            Get(player.whoAmI).EvaluatorModified.Remove(threadId);
        }
        HexDebugNet.ToClient(player.whoAmI, HexDebugNet.Msg.EvalOpened, w =>
        {
            w.Write((byte)threadId);
            w.Write(reset);
            HexDebugNet.WriteLines(w, StackLines(dbg.Image));
        });
        SendView(player.whoAmI, dbg);
    }

    /// <summary>上游 EvaluatorItem.handleNewPatternOnServer：运行杖画的图案在调试器当前的栈上跑。</summary>
    public static void Evaluate(Player player, int threadId, HexPattern pattern)
    {
        var dbg = Debugger(player.whoAmI, threadId);
        if (dbg is null || !dbg.DebugEnv.IsCasterInRange) return;
        var result = dbg.Evaluate(new SpellList.LList(0, new Iota[] { new PatternIota(pattern) }));
        if (result is null) return;
        if (result.StartedEvaluating) Get(player.whoAmI).EvaluatorModified.Add(threadId);
        FlushEffects(player, dbg);

        bool stackClear = dbg.IsDone;
        HexDebugNet.ToClient(player.whoAmI, HexDebugNet.Msg.EvalResult, w =>
        {
            w.Write((byte)threadId);
            w.Write((byte)dbg.LastResolutionType);
            w.Write(stackClear);
            HexDebugNet.WriteLines(w, StackLines(dbg.Image));
        });
        Handle(player, dbg, result);
    }

    // ==================== 断点（上游由编辑器设；这里在调试面板里点行） ====================

    public static void ToggleBreakpoint(Player player, int sourceRef, int line)
    {
        if (!Sessions.TryGetValue(player.whoAmI, out var s) || s.Shared.FindSource(sourceRef) is not { } src) return;
        if (line < 0 || line >= src.Iotas.Count) return;
        s.Shared.ToggleBreakpoint(sourceRef, line);
        foreach (var dbg in s.Debuggers.Values) SendView(player.whoAmI, dbg);
    }

    // ==================== 发给面板 ====================

    private static void Status(Player player, string text)
        => HexDebugNet.ToClient(player.whoAmI, HexDebugNet.Msg.Status, w => w.Write(text));

    private static List<string> StackLines(CastingImage image)
        => image.Stack.Reverse().Select(DisplayTags.Of).ToList();

    private static void SendView(int who, HexDebugger dbg)
    {
        var s = Get(who);
        var pos = dbg.CurrentPosition();
        var src = pos?.Source ?? s.Shared.Sources.LastOrDefault(x => x.ThreadId == dbg.ThreadId);
        var view = new HexDebugView
        {
            ThreadId = dbg.ThreadId,
            Name = dbg.DebugEnv.Name,
            State = dbg.State,
            Reason = s.LastReason.TryGetValue(dbg.ThreadId, out var reason) ? reason : null,
            SourceRef = src?.Reference ?? 0,
            SourceLines = src is null ? new List<string>() : dbg.SourceLines(src),
            CurrentLine = pos?.LineIndex ?? -1,
            BreakpointLines = src is not null && s.Shared.Breakpoints.TryGetValue(src.Reference, out var bps) ? bps.Keys.OrderBy(x => x).ToList() : new List<int>(),
            Stack = StackLines(dbg.Image),
            Ravenmind = DisplayTags.Of(dbg.Image.UserData.Ravenmind ?? NullIota.Instance),
            OpsConsumed = dbg.Image.OpsConsumed,
            EscapeNext = dbg.Image.EscapeNext,
            ParenCount = dbg.Image.ParenCount,
            Frames = dbg.StackFrames().Select(f => (f.Name, f.Meta is null ? "" : "#" + f.Meta.Source.Reference + ":" + f.Meta.LineIndex, f.IsVirtual)).ToList(),
            EvaluatorModified = s.EvaluatorModified.Contains(dbg.ThreadId),
        };
        HexDebugNet.ToClient(who, HexDebugNet.Msg.View, view.Write);
    }
}
