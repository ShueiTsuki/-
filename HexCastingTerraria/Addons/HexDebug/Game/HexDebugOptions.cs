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

    /// <summary>上游 maxUndoStackSize（默认 64，0 = 不限）。</summary>
    [DefaultValue(64)]
    [Range(0, 1024)]
    public int MaxUndoStackSize { get; set; } = 64;

    /// <summary>上游 splicingTableMediaCost：每次耗媒质的操作花多少（默认紫水晶粉的十分之一 = 1000）。</summary>
    [DefaultValue(1000)]
    [Range(0, 1000000)]
    public int SplicingTableMediaCost { get; set; } = 1000;

    /// <summary>上游 splicingTableMaxMedia：剪接台的媒质上限（默认一个充能紫水晶 = 100000）。</summary>
    [DefaultValue(100000)]
    [Range(1, 100000000)]
    public int SplicingTableMaxMedia { get; set; } = 100000;

    /// <summary>上游 splicingTableCastingCooldown：制念台施法按钮的冷却（上游 5 刻；泰拉每秒 60 帧，×3 = 15 帧）。</summary>
    [DefaultValue(15)]
    [Range(0, 600)]
    public int SplicingTableCastingCooldown { get; set; } = 15;

    /// <summary>上游 splicingTableAmbit：制念台施法范围的半径（格，默认 4）。</summary>
    [DefaultValue(4.0f)]
    [Range(0f, 64f)]
    public float SplicingTableAmbit { get; set; } = 4.0f;

    public override bool Equals(object? obj) => obj is HexDebugOptions o && o.MaxDebugThreads == MaxDebugThreads
        && o.MaxUndoStackSize == MaxUndoStackSize && o.SplicingTableMediaCost == SplicingTableMediaCost
        && o.SplicingTableMaxMedia == SplicingTableMaxMedia && o.SplicingTableCastingCooldown == SplicingTableCastingCooldown
        && o.SplicingTableAmbit == SplicingTableAmbit;

    public override int GetHashCode() => System.HashCode.Combine(MaxDebugThreads, MaxUndoStackSize, SplicingTableMediaCost,
        SplicingTableMaxMedia, SplicingTableCastingCooldown, SplicingTableAmbit);
}
