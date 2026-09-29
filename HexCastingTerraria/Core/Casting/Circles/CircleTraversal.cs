using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Circles;

/// <summary>
/// 法术环对世界的访问。
///
/// 与 <see cref="HexCastingTerraria.Core.Casting.Eval.ICastingWorld"/> 分开：
/// 那个是「施法者视角的世界」（有范围、有实体），
/// 这个是「环视角的世界」（按图格读部件）。混在一起会让两边都难写。
///
/// 之所以做成接口：**闭包校验与走图是纯逻辑**，抽象出来后就能离线测试 ——
/// 这正是最容易写错、又最难在游戏里观察的部分。
/// </summary>
public interface ICircleWorld
{
    /// <summary>读某图格的环部件。没有部件返回 null。</summary>
    CircleComponent? GetComponent(int x, int y);

    /// <summary>读石板上存的图案。空石板返回 null。</summary>
    HexPattern? GetSlatePattern(int x, int y);

    /// <summary>
    /// 该格是否被红石信号激活。供红石导线与红石原动力使用。
    /// 非红石部件可以不实现（返回 false）。
    /// </summary>
    bool IsPowered(int x, int y);
}

/// <summary>闭包校验的结果。</summary>
public readonly struct CircleClosure
{
    /// <summary>是否闭合（泛洪能回到原动力自身）。</summary>
    public required bool IsClosed { get; init; }

    /// <summary>泛洪走到的所有部件坐标。</summary>
    public required IReadOnlyCollection<(int X, int Y)> Reached { get; init; }

    /// <summary>包围盒。环的**施法范围**就是它（不是玩家的 32 格半径）。</summary>
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int MaxX { get; init; }
    public required int MaxY { get; init; }

    /// <summary>失败原因（IsClosed 为 false 时有意义）。</summary>
    public required CircleClosureError Error { get; init; }

    /// <summary>出错位置（用于提示玩家「哪里断了」）。</summary>
    public int ErrorX { get; init; }
    public int ErrorY { get; init; }
}

/// <summary>闭包校验的失败原因。</summary>
public enum CircleClosureError
{
    None = 0,

    /// <summary>闭合成功。</summary>
    Ok = 0,

    /// <summary>泛洪无法回到原动力 —— 环没接上。</summary>
    NoClosure = 1,

    /// <summary>
    /// 环太长，超过配置上限。
    /// **这个上限必须有**：泰拉的图格是直接数组访问，没有区块加载的天然阻力，
    /// 不设上限一个跨半张地图的环会真的走完几百万格（源项目同样有这个限制）。
    /// </summary>
    TooLong = 2,

    /// <summary>原动力旁边没有任何部件。</summary>
    NoExits = 3,
}

/// <summary>
/// 法术环的闭包校验与走图。移植自源项目 `CircleExecutionState`。
///
/// **纯逻辑，不依赖泰拉** —— 世界访问通过 <see cref="ICircleWorld"/> 注入。
/// </summary>
public static class CircleTraversal
{
    /// <summary>环长度上限。对应源项目 `maxSpellCircleLength` 配置项。</summary>
    public const int DefaultMaxLength = 512;

    /// <summary>
    /// 闭包校验。移植自源项目 `CircleExecutionState.createNew`。
    ///
    /// 算法：从原动力沿起始方向出发做 DFS 泛洪，
    /// 沿途只走「是环部件」且「允许从该方向进入」的格子；
    /// 最后看**泛洪能不能回到原动力自己** —— 能回到就是闭合。
    /// </summary>
    /// <param name="world">世界访问。</param>
    /// <param name="impetusX">原动力坐标。</param>
    /// <param name="impetusY">原动力坐标。</param>
    /// <param name="startDir">起始方向（媒质从原动力的哪一面流出）。</param>
    /// <param name="maxLength">长度上限。</param>
    public static CircleClosure Validate(ICircleWorld world, int impetusX, int impetusY,
                                         CircleDir startDir, int maxLength = DefaultMaxLength)
    {
        var reached = new HashSet<(int X, int Y)>();
        var stack = new Stack<(CircleDir EnterDir, int X, int Y)>();

        var (sx, sy) = startDir.Offset(impetusX, impetusY);
        stack.Push((startDir, sx, sy));

        int minX = impetusX, maxX = impetusX, minY = impetusY, maxY = impetusY;
        bool anyComponent = false;

        while (stack.Count > 0)
        {
            var (enterDir, x, y) = stack.Pop();

            var comp = world.GetComponent(x, y);
            if (comp == null)
            {
                // 不是环部件：泛洪到此为止（这一支不算错误，只是走不过去）
                continue;
            }

            // 进入限制：用部件自己声明的允许进入集合
            // （原动力、石板、导线的规则各不相同，见 CircleComponent 的说明）
            if (!comp.Value.AllowedEntries.Has(enterDir))
            {
                continue;
            }

            if (!reached.Add((x, y)))
            {
                continue;   // 已经走过
            }

            anyComponent = true;

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;

            // 长度上限：**必须有**，见 CircleClosureError.TooLong 的说明
            if (reached.Count >= maxLength)
            {
                return new CircleClosure
                {
                    IsClosed = false,
                    Reached = reached,
                    MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
                    Error = CircleClosureError.TooLong,
                    ErrorX = x, ErrorY = y,
                };
            }

            // 原动力本身是环的终点，不再往外扩展
            if (comp.Value.Kind == CircleComponentKind.Impetus)
            {
                continue;
            }

            // 只展开该部件的**可能出口**（对应源项目 possibleExitDirections）。
            // 展开全部四向会探索到源项目根本不会走的路径，闭合判定会偏松。
            foreach (var dir in PossibleExitDirections(comp.Value))
            {
                var (nx, ny) = dir.Offset(x, y);
                stack.Push((dir, nx, ny));
            }
        }

        if (!anyComponent)
        {
            return new CircleClosure
            {
                IsClosed = false,
                Reached = reached,
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
                Error = CircleClosureError.NoExits,
                ErrorX = impetusX, ErrorY = impetusY,
            };
        }

        // 闭合 = 泛洪回到了原动力自己
        bool closed = reached.Contains((impetusX, impetusY));

        return new CircleClosure
        {
            IsClosed = closed,
            Reached = reached,
            MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
            Error = closed ? CircleClosureError.Ok : CircleClosureError.NoClosure,
            ErrorX = impetusX, ErrorY = impetusY,
        };
    }

    /// <summary>
    /// 某个部件在给定来向下，**能往哪些方向出去**。
    ///
    /// ⚠️ 关键：出口集合要**减去来路的反方向** ——
    /// 源项目 `exitDirsSet.remove(enterDir.getOpposite())`。
    /// 不减的话控制流会在两块之间原地打转，
    /// 而且因为「恰好 1 个出口」的检查，它会一直「合法」地循环下去，**不报错**。
    ///
    /// ⚠️ 另一处**不要「修正」**的地方：源项目只移除 `normal` 本身，
    /// `normal.getOpposite()` 那一行是被**注释掉**的
    /// （`// allDirs.remove(normal.getOpposite());`）——
    /// 也就是说**故意允许**沿 normal 的反方向穿过去。
    /// 加上那个移除会让控制流无法沿方块表面通过，环会莫名断掉。
    /// </summary>
    public static List<CircleDir> PossibleExitDirections(CircleComponent comp)
    {
        var result = new List<CircleDir>(4);
        foreach (var dir in comp.ExitMask.Enumerate())
        {
            result.Add(dir);
        }
        return result;
    }

    /// <summary>
    /// 实际走一步时可用的出口 = <see cref="PossibleExitDirections"/> 再减去来路的反方向。
    /// </summary>
    public static List<CircleDir> ExitDirections(CircleComponent comp, CircleDir enterDir)
    {
        var possible = PossibleExitDirections(comp);
        var forbidden = enterDir.Opposite();

        var result = new List<CircleDir>(possible.Count);
        foreach (var dir in possible)
        {
            // 不能原路返回（源项目 acceptControlFlow 里的 remove(enterDir.getOpposite())）
            if (dir == forbidden) continue;
            result.Add(dir);
        }

        return result;
    }

    /// <summary>
    /// 走环的速度：隔多少 tick 走一格。
    /// 移植自源项目 `getTickSpeed`：`max(2, 10 - (reachedSlate - 1) / 3)`。
    ///
    /// 也就是**环走得越深越快**：起步 10 tick/格，每 3 格减 1，最低 2 tick。
    /// </summary>
    public static int TickSpeed(int reachedCount)
        => System.Math.Max(2, 10 - (reachedCount - 1) / 3);
}
