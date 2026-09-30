using System.ComponentModel;
using HexCastingTerraria.Addons.Hexcessible.Core;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 的配置项，挂在客户端「附属兼容」页的 Hexcessible 开关下面（上游 HexcessibleConfig，默认值照搬）。
/// 客户端配置，随时改、立即生效；改完 <see cref="Apply"/> 写进 <see cref="HexcessibleSettings.Current"/>。
/// </summary>
public sealed class HexcessibleOptions
{
    [DefaultValue(true)]
    public bool KeyboardAllow { get; set; } = true;

    [DefaultValue(HexcessibleSettings.TooltipMode.Descriptive)]
    public HexcessibleSettings.TooltipMode KeyboardTooltip { get; set; } = HexcessibleSettings.TooltipMode.Descriptive;

    [DefaultValue(true)]
    public bool KeyHint { get; set; } = true;

    [DefaultValue(true)]
    public bool Ghost { get; set; } = true;

    [DefaultValue(true)]
    public bool ShortcutHints { get; set; } = true;

    [DefaultValue(false)]
    public bool UppercaseSig { get; set; }

    public void Apply()
    {
        HexcessibleSettings.Current = new HexcessibleSettings
        {
            KeyboardAllow = KeyboardAllow,
            KeyboardTooltip = KeyboardTooltip,
            KeyHint = KeyHint,
            Ghost = Ghost,
            ShortcutHints = ShortcutHints,
            UppercaseSig = UppercaseSig,
        };
    }

    public override bool Equals(object? obj) => obj is HexcessibleOptions o
        && o.KeyboardAllow == KeyboardAllow && o.KeyboardTooltip == KeyboardTooltip && o.KeyHint == KeyHint
        && o.Ghost == Ghost && o.ShortcutHints == ShortcutHints && o.UppercaseSig == UppercaseSig;

    public override int GetHashCode() => System.HashCode.Combine(KeyboardAllow, KeyboardTooltip, KeyHint, Ghost, ShortcutHints, UppercaseSig);
}
