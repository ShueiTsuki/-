namespace HexCastingTerraria.Core.Casting.Circles;

/// <summary>
/// 法术环控制流的**方向**。
///
/// 注意：**不要复用 <see cref="Math.HexDir"/>**：那是六方向的（画图案用的六边形方向），
/// 而泰拉是方格世界，控制流只能走上下左右四个方向。
/// 源项目用的是 MC 的 `Direction`（六向，因为有 Y 轴），
/// 移植到 2D 后正好退化成四向 —— 这也是 2D 反而更简单的地方。
/// </summary>
public enum CircleDir
{
    /// <summary>上（-Y）。</summary>
    Up = 0,

    /// <summary>下（+Y）。</summary>
    Down = 1,

    /// <summary>左（-X）。</summary>
    Left = 2,

    /// <summary>右（+X）。</summary>
    Right = 3,
}

/// <summary>环部件的种类。对应源项目的 8 个方块。</summary>
public enum CircleComponentKind
{
    /// <summary>无部件（空位）。</summary>
    None = 0,

    /// <summary>原动力：环的「CPU」，持媒质池与执行状态，不含图案。</summary>
    Impetus = 1,

    /// <summary>石板：环的「指令」，存一个图案；空石板直通。</summary>
    Slate = 2,

    /// <summary>空导线：只改流向，不做任何判断。</summary>
    DirectrixEmpty = 3,

    /// <summary>布尔导线：按栈顶布尔值分流。</summary>
    DirectrixBool = 4,

    /// <summary>红石导线：按红石信号分流。</summary>
    DirectrixRedstone = 5,

    /// <summary>
    /// 空白促动石（原版 BlockEmptyImpetus，「其实不是促动石」）：媒质只从箭头方向流出，
    /// 不能从箭头反方向进。本身是脑叶切除做成三种促动石的原料。
    /// </summary>
    EmptyImpetus = 6,
}

/// <summary>环方向工具。</summary>
public static class CircleDirs
{
    /// <summary>四个方向，顺序固定（用于确定性遍历）。</summary>
    public static readonly CircleDir[] All =
    {
        CircleDir.Up, CircleDir.Down, CircleDir.Left, CircleDir.Right,
    };

    /// <summary>该方向的位移。对应源项目 `Direction.step()`。</summary>
    public static (int Dx, int Dy) Step(this CircleDir dir) => dir switch
    {
        CircleDir.Up => (0, -1),
        CircleDir.Down => (0, 1),
        CircleDir.Left => (-1, 0),
        _ => (1, 0),
    };

    /// <summary>反方向。源项目 `Direction.getOpposite()`。</summary>
    public static CircleDir Opposite(this CircleDir dir) => dir switch
    {
        CircleDir.Up => CircleDir.Down,
        CircleDir.Down => CircleDir.Up,
        CircleDir.Left => CircleDir.Right,
        _ => CircleDir.Left,
    };

    /// <summary>沿该方向走一步。</summary>
    public static (int X, int Y) Offset(this CircleDir dir, int x, int y)
    {
        var (dx, dy) = dir.Step();
        return (x + dx, y + dy);
    }

    /// <summary>
    /// 方向 → 单位向量（对应源项目 `Direction.step()` 得到的向量）。
    /// 用于 `circle/impetus_dir` 图案的返回值。
    /// </summary>
    public static (double X, double Y) ToVector(this CircleDir dir)
    {
        var (dx, dy) = dir.Step();
        return (dx, dy);
    }
}
