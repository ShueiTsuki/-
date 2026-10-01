using System.Collections.Generic;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// 客户端这边的调试状态：每个线程最新的样子（调试面板画它）、输出记录、运行杖的画布。
/// 上游客户端只记「哪些线程在调试」「运行杖改过没有」（给物品换图标），其余在编辑器里看。
/// </summary>
internal static class HexDebugClient
{
    public const int MaxOutput = 100;

    public static readonly SortedDictionary<int, HexDebugView> Threads = new();

    public static readonly List<(int Thread, string Text, OutputCategory Category)> Output = new();

    /// <summary>运行杖在用的线程（画布开着、图案送进这个线程）；-1 = 没在用运行杖。</summary>
    public static int EvaluatorThread { get; private set; } = -1;

    /// <summary>每个线程运行杖画布上的图案（上游 evaluatorUIPatterns），与法杖的图案分开保存。</summary>
    private static readonly Dictionary<int, HexCanvas.Snapshot> EvaluatorCanvas = new();
    private static HexCanvas.Snapshot? _staffCanvas;

    public static bool IsDebugging(int threadId) => Threads.ContainsKey(threadId);

    public static void SetView(HexDebugView view) => Threads[view.ThreadId] = view;

    public static void RemoveThread(int threadId)
    {
        Threads.Remove(threadId);
        EvaluatorCanvas.Remove(threadId);
        if (EvaluatorThread == threadId && HexCanvasState.Canvas.IsOpen) HexCanvasState.CloseCanvas();
    }

    public static void AddOutput(int threadId, string text, OutputCategory category)
    {
        Output.Add((threadId, text, category));
        if (Output.Count > MaxOutput) Output.RemoveRange(0, Output.Count - MaxOutput);
    }

    /// <summary>上游 displayClientMessage(…, actionBar = true)：画布 HUD 的那一行消息。</summary>
    public static void ShowStatus(string text) => HexCanvasState.SetMessage(text);

    public static void Clear()
    {
        Threads.Clear();
        Output.Clear();
        EvaluatorCanvas.Clear();
        EvaluatorThread = -1;
        _staffCanvas = null;
    }

    // ==================== 运行杖 ====================

    /// <summary>
    /// 上游 MsgOpenSpellGuiS2C：打开施法界面，显示调试器的栈与这个线程上次画的图案。
    /// 画布换成运行杖的图案（法杖的那份先存起来，关画布时换回）。
    /// </summary>
    public static void OpenEvaluator(int threadId, bool reset, IReadOnlyList<string> stack)
    {
        if (reset) EvaluatorCanvas.Remove(threadId);
        var canvas = HexCanvasState.Canvas;
        if (canvas.IsOpen) HexCanvasState.CloseCanvas();

        _staffCanvas = canvas.TakeSnapshot();
        canvas.Reset();
        if (EvaluatorCanvas.TryGetValue(threadId, out var mine)) canvas.RestoreSnapshot(mine);

        Content.Items.HexStaff.OpenCanvas();
        EvaluatorThread = threadId;
        HexCanvasState.HudStackOverride = stack;
        HexCanvasState.PatternSink = rp =>
        {
            HexDebugNet.ToServer(HexDebugNet.Msg.EvalPattern, w =>
            {
                w.Write((byte)threadId);
                Content.Net.IotaWire.Write(w, new PatternIota(rp.Pattern));
            });
            return true;
        };
        HexCanvasState.Closed -= OnCanvasClosed;
        HexCanvasState.Closed += OnCanvasClosed;
    }

    /// <summary>上游 MsgNewSpellPatternS2C：给刚画的图案上色、更新栈；调试已经跑完（栈清了）就关画布。</summary>
    public static void EvaluatorResult(int threadId, ResolvedPatternType type, bool stackClear, IReadOnlyList<string> stack)
    {
        if (EvaluatorThread != threadId || !HexCanvasState.Canvas.IsOpen) return;
        HexCanvasState.Canvas.ApplyResolution(type);
        HexCanvasState.HudStackOverride = stack;
        if (stackClear)
        {
            HexCanvasState.Canvas.Reset();
            EvaluatorCanvas.Remove(threadId);
            HexCanvasState.CloseCanvas();
        }
    }

    private static void OnCanvasClosed()
    {
        HexCanvasState.Closed -= OnCanvasClosed;
        var canvas = HexCanvasState.Canvas;
        if (EvaluatorThread >= 0 && Threads.ContainsKey(EvaluatorThread))
        {
            EvaluatorCanvas[EvaluatorThread] = canvas.TakeSnapshot();
        }
        EvaluatorThread = -1;
        canvas.Reset();
        if (_staffCanvas is { } staff) canvas.RestoreSnapshot(staff);
        _staffCanvas = null;
    }

    /// <summary>本人手上的调试杖 / 运行杖对着的线程（面板默认显示它）；没拿着返回 -1。</summary>
    public static int HeldThread()
    {
        var item = Main.LocalPlayer?.HeldItem?.ModItem;
        return item switch
        {
            DebuggerItemBase d => d.ThreadId,
            EvaluatorItemBase e => e.ThreadId,
            _ => EvaluatorThread,
        };
    }
}
