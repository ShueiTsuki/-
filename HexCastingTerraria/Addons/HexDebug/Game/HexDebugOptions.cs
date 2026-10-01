using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// HexDebug 的服务端配置项，挂在「附属兼容」页的 HexDebug 开关下面（上游 HexDebugServerConfig，默认值照搬）。
/// 剪接台的几项随剪接台一起加。
/// </summary>
public sealed class HexDebugOptions
{
    /// <summary>上游 maxDebugThreads（1..16，默认 4）：启蒙后能同时调试几段；没启蒙只有 1 段。</summary>
    [DefaultValue(4)]
    [Range(1, 16)]
    [Slider]
    public int MaxDebugThreads { get; set; } = 4;

    public override bool Equals(object? obj) => obj is HexDebugOptions o && o.MaxDebugThreads == MaxDebugThreads;

    public override int GetHashCode() => MaxDebugThreads;
}
