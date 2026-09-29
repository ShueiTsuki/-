using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client;

/// <summary>
/// 咒法学的表现层配色。
///
/// 为什么单独放这里：这些是 `Microsoft.Xna.Framework.Color`，
/// 属于**表现层**。原先 `OvercastColor` 混在 `Core/Media/MediaConstants.cs` 里，
/// 导致整个 Media 目录无法进离线测试工程（只要碰一下常量就把 XNA 拖进来）。
/// 拆开之后 `MediaConstants` 是纯数值，可离线测试。
///
/// 色值取自源项目 `ItemMediaHolder.HEX_COLOR` 同一色系。
/// </summary>
public static class HexColors
{
    /// <summary>过载施法的提示色。用于过载掉血的战斗文字与「启蒙」提示。</summary>
    public static readonly Color Overcast = new(179, 142, 243);

    /// <summary>法术图案的主色（画布描线）。</summary>
    public static readonly Color PatternMain = new(120, 200, 255);

    /// <summary>媒质相关文本色。</summary>
    public static readonly Color Media = new(196, 168, 255);
}
