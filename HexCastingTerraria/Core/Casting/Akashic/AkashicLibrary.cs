using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Akashic;

/// <summary>
/// 阿卡夏图书馆的查找规则（源项目 blocks/akashic/AkashicFloodfiller.java + BlockAkashicRecord.java），纯逻辑，离线可测。
///
/// 原版的结构：
///   - **记录**只是入口，自己什么都不存，也不传导；
///   - **书架**每格存一条「图案 → iota」，能传导；
///   - **桥接块**（原版 akashic_connector / BlockAkashicLigature）什么都不存，只传导，用来把书架连得更远。
/// 从记录出发，经相邻的书架 / 桥接块往外泛洪，离记录不超过 128 格（直线距离的平方比较）。
/// MC 是 6 个方向，泰拉 2D 只剩上下左右。
/// </summary>
public enum AkashicBlock : byte
{
    None = 0,
    Record,
    Bookshelf,
    Ligature,
}

/// <summary>图书馆对世界的访问（泰拉侧读图格与书架实体，测试里是字典）。</summary>
public interface IAkashicLibraryView
{
    /// <summary>这一格是哪种阿卡夏方块。</summary>
    AkashicBlock BlockAt(int x, int y);

    /// <summary>书架上存的键；空书架、不是书架都返回 null。</summary>
    HexPattern? KeyAt(int x, int y);
}

public static class AkashicLibrary
{
    /// <summary>原版 floodFillFor 的 maxRange 默认 128。</summary>
    public const int MaxRange = 128;

    /// <summary>原版 addNewDatum 找空书架时的 skipChance：每找到一个空书架有 90% 跳过，所以写进哪个书架基本是随机的。</summary>
    public const float WriteSkipChance = 0.9f;

    /// <summary>
    /// 原版 AkashicFloodfiller.floodFillFor：从 <paramref name="sx"/>,<paramref name="sy"/> 出发广度优先，
    /// 每个邻格只看一次；是目标就按 skipChance 掷一次（<c>nextFloat() &gt; skipChance</c> 才要它，否则记下跳过），
    /// 能传导的（书架 / 桥接块）就继续往外扩。走完都跳过了，就返回第一个跳过的。
    /// </summary>
    /// <param name="nextFloat">[0, 1) 的随机数；skipChance 为 0 时不会调用。</param>
    public static (int X, int Y)? FloodFillFor(int sx, int sy, IAkashicLibraryView view,
        Func<int, int, bool> isTarget, float skipChance, Func<float>? nextFloat, int maxRange = MaxRange)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (isTarget == null) throw new ArgumentNullException(nameof(isTarget));

        var seen = new HashSet<(int, int)>();
        var todo = new Queue<(int X, int Y)>();
        todo.Enqueue((sx, sy));
        (int X, int Y)? firstSkipped = null;
        long maxSq = (long)maxRange * maxRange;

        while (todo.Count > 0)
        {
            var here = todo.Dequeue();
            foreach (var (dx, dy) in Directions)
            {
                int nx = here.X + dx, ny = here.Y + dy;
                long ddx = nx - sx, ddy = ny - sy;
                if (ddx * ddx + ddy * ddy > maxSq) continue;
                if (!seen.Add((nx, ny))) continue;

                if (isTarget(nx, ny))
                {
                    if (skipChance <= 0f || nextFloat!() > skipChance) return (nx, ny);
                    firstSkipped ??= (nx, ny);
                }
                if (CanFloodThrough(view.BlockAt(nx, ny)))
                {
                    todo.Enqueue((nx, ny));
                }
            }
        }
        return firstSkipped;
    }

    /// <summary>
    /// 原版 Direction.values() 去掉南北两个（2D 没有 z 轴）后的顺序：下、上、西、东。
    /// 泰拉的 y 朝下，所以 MC 的「下」是 y + 1。
    /// </summary>
    private static readonly (int Dx, int Dy)[] Directions = { (0, 1), (0, -1), (-1, 0), (1, 0) };

    /// <summary>原版只有实现了 AkashicFloodfiller 的方块能传导：书架和桥接块。记录不传导。</summary>
    public static bool CanFloodThrough(AkashicBlock block) => block is AkashicBlock.Bookshelf or AkashicBlock.Ligature;

    /// <summary>原版 lookupPattern 的查找部分：网络里存着这个键的书架（按角度序列比，不比起笔方向）。</summary>
    public static (int X, int Y)? FindKey(int sx, int sy, IAkashicLibraryView view, HexPattern key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        return FloodFillFor(sx, sy, view,
            (x, y) => view.BlockAt(x, y) == AkashicBlock.Bookshelf && view.KeyAt(x, y) is { } k && k.SigsEqual(key),
            0f, null);
    }

    /// <summary>
    /// 原版 addNewDatum 的选址：网络里**已经有这个键就不写**（原版注释「Will never clobber anything」，返回 null）；
    /// 否则找一个空书架（90% 跳过，基本随机）；一个空书架都没有也返回 null。都是静默的，不报错。
    /// </summary>
    public static (int X, int Y)? FindWriteSlot(int sx, int sy, IAkashicLibraryView view, HexPattern key, Func<float> nextFloat)
    {
        if (nextFloat == null) throw new ArgumentNullException(nameof(nextFloat));
        if (FindKey(sx, sy, view, key) != null) return null;
        return FloodFillFor(sx, sy, view,
            (x, y) => view.BlockAt(x, y) == AkashicBlock.Bookshelf && view.KeyAt(x, y) == null,
            WriteSkipChance, nextFloat);
    }
}
