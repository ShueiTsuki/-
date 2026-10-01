using HexCastingTerraria.Client.UI;

namespace HexCastingTerraria.Client;

/// <summary>
/// 画布与 HUD 的客户端共享状态。
///
/// 刻意做成静态类而不是放在 ModPlayer 上：
/// ModPlayer 在客户端与服务端都会加载，而画布、鼠标输入、HUD 全是纯客户端概念。
/// </summary>
public static class HexCanvasState
{
    /// <summary>绘制画布实例（唯一）。</summary>
    public static HexCanvas Canvas { get; } = new HexCanvas();

    /// <summary>咒法学之书。与画布共用「同一时间只开一个」的约束。</summary>
    public static UI.HexBook Book { get; } = new UI.HexBook();

    /// <summary>开发者面板（F7）。</summary>
    public static UI.DevPanel Dev { get; } = new UI.DevPanel();

    /// <summary>最近一次操作反馈，用于 HUD 提示。</summary>
    public static string? LastMessage { get; set; }

    /// <summary>提示信息的剩余显示帧数。</summary>
    public static int MessageTimer { get; set; }

    /// <summary>
    /// 本系统是否已经把 Main.blockInput 置为 true。
    ///
    /// 必须记录状态：如果只在打开时设置、关闭时不恢复，
    /// 玩家退出画布后会完全无法移动（踩过的坑）。
    /// </summary>
    public static bool BlockedInput { get; set; }

    /// <summary>画布刚打开后的输入保护帧数。</summary>
    ///
    /// 必须存在的原因：法杖的 UseItem 在游戏逻辑层把画布设为打开，
    /// 而本系统的 PostUpdateInput 在同一帧稍后才执行。若不保护，
    /// 它会看到「右键刚按下」并立刻把画布关掉 —— 于是每帧开关一次，
    /// 表现为界面闪烁且左键永远无法稳定绘制。
    /// </summary>
    public static int CloseGuardFrames { get; private set; }

    /// <summary>打开画布并启用输入保护。</summary>
    public static void OpenCanvas(int guardFrames = 6)
    {
        Canvas.Open();
        CloseGuardFrames = guardFrames;
        OpenedWithSlot = Terraria.Main.LocalPlayer?.selectedItem ?? -1;
    }

    /// <summary>
    /// 打开画布时手上是快捷栏第几格。之后切到别的格子（数字键、滚轮、自动选工具…）就关画布 ——
    /// 原版施法界面开着时根本切不了物品；以前这里不管，切走以后画布还开着、人却动不了（玩家反馈）。
    /// </summary>
    public static int OpenedWithSlot { get; private set; } = -1;

    /// <summary>
    /// 关闭画布。
    ///
    /// 关键语义（对齐原作）：**关闭画布不清空已画的图案**。
    /// 原作里已解析的图案存在服务端施法状态里，关闭界面再打开是连续的；
    /// 只有「潜行 + 右键」才 clearCastingData 清空重开。
    /// 之前默认清空导致「重新打开就断了」，与用户预期不符。
    /// </summary>
    public static void CloseCanvas()
    {
        Canvas.Close(clearPatterns: false);
        Canvas.Overlays.Clear();
        foreach (var ext in CanvasExtensions.All) ext.OnClose();
        PatternSink = null;
        HudStackOverride = null;
        TypingAllowed = true;
        Closed?.Invoke();
    }

    /// <summary>
    /// 从画布里打开了书（Hexcessible 按 N 查书）：记下当时手上是第几格，书关了就回到画布
    /// （上游 KeyDocsScreenMixin.returnToStaff：关书时换回原来的施法界面）。null = 没有要回去的画布。
    /// </summary>
    public static int? ReturnToCanvasSlot { get; set; }

    /// <summary>
    /// 画好的图案交给谁（附属：HexDebug 运行杖把图案送进调试会话）。返回 true = 它接手了，本体不求值。null = 本体照常求值。
    /// 画布关闭时清空。
    /// </summary>
    public static System.Func<UI.ResolvedPattern, bool>? PatternSink { get; set; }

    /// <summary>附属接管画布时，HUD 上的「VM 栈」显示这些行（栈顶在前）；null = 显示本体的栈。画布关闭时清空。</summary>
    public static System.Collections.Generic.IReadOnlyList<string>? HudStackOverride { get; set; }

    /// <summary>附属的画布扩展能不能接键盘输入（HexDebug 剪接台的画布里不能：上游 Hexcessible 在那里 disallowTyping）。画布关闭时恢复。</summary>
    public static bool TypingAllowed { get; set; } = true;

    /// <summary>画布关闭时通知（附属在这里换回自己的图案快照等）。</summary>
    public static event System.Action? Closed;

    /// <summary>每帧递减保护计数。</summary>
    public static void TickGuard()
    {
        if (CloseGuardFrames > 0)
        {
            CloseGuardFrames--;
        }
    }

    public static void SetMessage(string? message, int frames = 180)
    {
        LastMessage = message;
        MessageTimer = message == null ? 0 : frames;
    }

    /// <summary>每帧递减提示计时。</summary>
    public static void TickMessage()
    {
        if (MessageTimer > 0)
        {
            MessageTimer--;
            if (MessageTimer == 0)
            {
                LastMessage = null;
            }
        }
    }
}
