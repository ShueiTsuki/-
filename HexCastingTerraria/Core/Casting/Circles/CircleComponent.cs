using System;
using System.Collections.Generic;

namespace HexCastingTerraria.Core.Casting.Circles;

/// <summary>方向集合的位掩码。</summary>
[Flags]
public enum CircleDirMask
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,

    /// <summary>四个方向全选。</summary>
    All = Up | Down | Left | Right,
}

/// <summary>方向掩码工具。</summary>
public static class CircleDirMaskUtil
{
    public static CircleDirMask Of(CircleDir dir) => dir switch
    {
        CircleDir.Up => CircleDirMask.Up,
        CircleDir.Down => CircleDirMask.Down,
        CircleDir.Left => CircleDirMask.Left,
        _ => CircleDirMask.Right,
    };

    public static CircleDirMask Without(this CircleDirMask mask, CircleDir dir)
        => mask & ~Of(dir);

    public static bool Has(this CircleDirMask mask, CircleDir dir)
        => (mask & Of(dir)) != 0;

    public static IEnumerable<CircleDir> Enumerate(this CircleDirMask mask)
    {
        foreach (var dir in CircleDirs.All)
        {
            if (mask.Has(dir)) yield return dir;
        }
    }

    /// <summary>掩码里有几个方向。</summary>
    public static int CountBits(this CircleDirMask mask)
    {
        int n = 0;
        foreach (var _ in mask.Enumerate()) n++;
        return n;
    }
}

/// <summary>
/// 某个坐标上的环部件信息。
///
/// ## 为什么用「掩码」而不是「一个朝向」
///
/// 最初的设计只有 `Normal` + `ForbiddenEntry` 两个字段，但读了三根导线之后发现不够：
///
/// | 部件 | 能进入的方向 | 能出去的方向 |
/// |---|---|---|
/// | 石板 / 原动力 | 全部减 1 个 | 全部减 1 个 |
/// | **导线（3 种）** | **全部减 2 个**（轴的两端都不能进） | **只有轴的两端** |
///
/// 导线只能沿**一个轴**传导，从垂直于轴的方向进入 ——
/// 用单一朝向表达不了，所以改成两个掩码。
/// </summary>
public readonly struct CircleComponent
{
    public required CircleComponentKind Kind { get; init; }

    /// <summary>允许**进入**的方向集合。</summary>
    public required CircleDirMask AllowedEntries { get; init; }

    /// <summary>
    /// **可能**出去的方向集合。
    /// 实际走一步时还要减去来路的反方向（见 `CircleTraversal.ExitDirections`）。
    /// </summary>
    public required CircleDirMask ExitMask { get; init; }

    /// <summary>
    /// 部件的朝向。对导线而言它是「传导轴的一端」——
    /// 布尔导线真值出 `Facing.Opposite()`、假值出 `Facing`；红石导线通电出 `Facing`。
    /// </summary>
    public required CircleDir Facing { get; init; }

    // ── 三种构造方式，对应源项目的三类方块 ──────────────────────────

    /// <summary>
    /// 普通部件（石板）：不能往 `normal` 出去、不能从 `normal` 的反方向进来。
    /// 注意**允许**沿 `normal` 的反方向穿过（源项目那行是注释掉的）。
    /// </summary>
    public static CircleComponent Ordinary(CircleComponentKind kind, CircleDir normal) => new()
    {
        Kind = kind,
        AllowedEntries = CircleDirMask.All.Without(normal.Opposite()),
        ExitMask = CircleDirMask.All.Without(normal),
        Facing = normal,
    };

    /// <summary>
    /// 原动力：禁止进入 = **起始方向的反方向**，与 normal 无关。
    /// 把两者混为一谈会让「两块大的假环」被判成闭合（这个缺陷是写用例时跑出来的）。
    /// </summary>
    public static CircleComponent Impetus(CircleDir startDir) => new()
    {
        Kind = CircleComponentKind.Impetus,
        AllowedEntries = CircleDirMask.All.Without(startDir.Opposite()),
        // 原动力是环的终点：它的 acceptControlFlow 返回 Stop，不再往外传导
        ExitMask = CircleDirMask.None,
        Facing = startDir,
    };

    /// <summary>
    /// 空白促动石（原版 BlockEmptyImpetus）：进入规则同促动石（不能从出口的反方向进），
    /// 但它**继续传导**，唯一出口 = 箭头方向（acceptControlFlow → Continue(facing)）。
    /// </summary>
    public static CircleComponent EmptyImpetus(CircleDir facing) => new()
    {
        Kind = CircleComponentKind.EmptyImpetus,
        AllowedEntries = CircleDirMask.All.Without(facing.Opposite()),
        ExitMask = CircleDirMaskUtil.Of(facing),
        Facing = facing,
    };

    /// <summary>
    /// 导线：只能沿**一个轴**传导。轴的两端都能出去、也都不能进入；
    /// 只能从垂直于轴的方向进入。
    /// 对应源项目三根导线的 `possibleExitDirections` 与 `canEnterFromDirection`。
    /// </summary>
    public static CircleComponent Directrix(CircleComponentKind kind, CircleDir facing) => new()
    {
        Kind = kind,
        AllowedEntries = CircleDirMask.All.Without(facing).Without(facing.Opposite()),
        ExitMask = CircleDirMaskUtil.Of(facing) | CircleDirMaskUtil.Of(facing.Opposite()),
        Facing = facing,
    };
}
