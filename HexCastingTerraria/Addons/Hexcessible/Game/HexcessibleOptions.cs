using System.ComponentModel;
using HexCastingTerraria.Addons.Hexcessible.Core;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 的配置项，挂在客户端「附属兼容」页的 Hexcessible 开关下面（上游 HexcessibleConfig，默认值照搬）。
/// 客户端配置，随时改、立即生效；改完 <see cref="Apply"/> 写进 <see cref="HexcessibleSettings.Current"/>。
/// </summary>
public sealed class HexcessibleOptions
{
    [DefaultValue(HexcessibleSettings.KeyDocsMode.Idling)]
    public HexcessibleSettings.KeyDocsMode KeyDocs { get; set; } = HexcessibleSettings.KeyDocsMode.Idling;

    [DefaultValue(HexcessibleSettings.TooltipMode.Descriptive)]
    public HexcessibleSettings.TooltipMode IdleTooltip { get; set; } = HexcessibleSettings.TooltipMode.Descriptive;

    [DefaultValue(HexcessibleSettings.TooltipMode.Descriptive)]
    public HexcessibleSettings.TooltipMode MouseDrawTooltip { get; set; } = HexcessibleSettings.TooltipMode.Descriptive;

    [DefaultValue(true)]
    public bool KeyboardAllow { get; set; } = true;

    [DefaultValue(HexcessibleSettings.TooltipMode.Descriptive)]
    public HexcessibleSettings.TooltipMode KeyboardTooltip { get; set; } = HexcessibleSettings.TooltipMode.Descriptive;

    [DefaultValue(true)]
    public bool KeyHint { get; set; } = true;

    [DefaultValue(true)]
    public bool Ghost { get; set; } = true;

    [DefaultValue(true)]
    public bool AutoCompleteAllow { get; set; } = true;

    [DefaultValue(HexcessibleSettings.TooltipMode.Descriptive)]
    public HexcessibleSettings.TooltipMode AutoCompleteTooltip { get; set; } = HexcessibleSettings.TooltipMode.Descriptive;

    [DefaultValue(7)]
    [Range(3, 20)]
    [Slider]
    public int AutoCompleteCount { get; set; } = 7;

    [DefaultValue(true)]
    public bool ShortcutHints { get; set; } = true;

    [DefaultValue(false)]
    public bool UppercaseSig { get; set; }

    public void Apply()
    {
        HexcessibleSettings.Current = new HexcessibleSettings
        {
            KeyDocs = KeyDocs,
            IdleTooltip = IdleTooltip,
            MouseDrawTooltip = MouseDrawTooltip,
            KeyboardAllow = KeyboardAllow,
            KeyboardTooltip = KeyboardTooltip,
            KeyHint = KeyHint,
            AutoCompleteAllow = AutoCompleteAllow,
            AutoCompleteTooltip = AutoCompleteTooltip,
            AutoCompleteCount = System.Math.Clamp(AutoCompleteCount, 3, 20),
            Ghost = Ghost,
            ShortcutHints = ShortcutHints,
            UppercaseSig = UppercaseSig,
        };
    }

    public override bool Equals(object? obj) => obj is HexcessibleOptions o
        && o.KeyboardAllow == KeyboardAllow && o.KeyboardTooltip == KeyboardTooltip && o.KeyHint == KeyHint
        && o.Ghost == Ghost && o.ShortcutHints == ShortcutHints && o.UppercaseSig == UppercaseSig
        && o.AutoCompleteAllow == AutoCompleteAllow && o.AutoCompleteTooltip == AutoCompleteTooltip && o.AutoCompleteCount == AutoCompleteCount
        && o.IdleTooltip == IdleTooltip && o.MouseDrawTooltip == MouseDrawTooltip && o.KeyDocs == KeyDocs;

    public override int GetHashCode() => System.HashCode.Combine(
        System.HashCode.Combine(KeyboardAllow, KeyboardTooltip, KeyHint, Ghost, ShortcutHints, UppercaseSig),
        AutoCompleteAllow, AutoCompleteTooltip, AutoCompleteCount, IdleTooltip, MouseDrawTooltip, KeyDocs);
}
