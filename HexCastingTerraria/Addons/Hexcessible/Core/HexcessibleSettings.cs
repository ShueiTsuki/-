namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// Hexcessible 的配置项（上游 HexcessibleConfig.java，默认值照搬）。纯数据：游戏侧从客户端附属配置抄进 <see cref="Current"/>。
/// 随功能一起加：现在是键盘绘制用到的几项。
/// </summary>
public sealed class HexcessibleSettings
{
    /// <summary>上游 OptionalTooltip：提示框不显示 / 只显示签名 / 连名字和参数一起显示。</summary>
    public enum TooltipMode
    {
        Hidden,
        Simple,
        Descriptive,
    }

    public static HexcessibleSettings Current { get; set; } = new();

    /// <summary>keyboardDraw.allow：允许用键盘画图。</summary>
    public bool KeyboardAllow { get; set; } = true;

    /// <summary>keyboardDraw.tooltip。</summary>
    public TooltipMode KeyboardTooltip { get; set; } = TooltipMode.Descriptive;

    /// <summary>keyboardDraw.keyHint：在下一笔能去的格点旁边标出对应字母。</summary>
    public bool KeyHint { get; set; } = true;

    /// <summary>keyboardDraw.ghost：在光标处画虚影（即使那里放不下）。</summary>
    public bool Ghost { get; set; } = true;

    /// <summary>shortcutHints：左下角列出当前能按的快捷键。</summary>
    public bool ShortcutHints { get; set; } = true;

    /// <summary>uppercaseSig：签名用大写显示（QAQ）。</summary>
    public bool UppercaseSig { get; set; }
}
