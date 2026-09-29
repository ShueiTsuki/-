using System.Collections.Generic;
using System.Linq;

namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 给一个图案找另一种画法：**形状（边集）完全相同，笔顺不同**。
/// 移植自源项目 EulerPathFinder.kt —— 大法术的「每个世界的笔顺」就是用它从标准图案 + 世界种子生成的。
///
/// 算法（Hierholzer）：把图案转成无向图，从奇度点（或任意点）出发随机走欧拉路径。
/// 随机源不同于 Java 的 Random，所以同一个种子得到的笔顺与 MC 版不同 —— 这无关紧要：
/// 要的是「每个世界固定、世界之间不同」。为了确定性，集合的遍历顺序一律排序（Java 版依赖 HashMap 的顺序）。
/// </summary>
public static class EulerPathFinder
{
    /// <summary>源项目 findAltDrawing：最多尝试 100 次，<paramref name="rule"/> 不满足就换一条；都不行返回原图案。</summary>
    public static HexPattern FindAltDrawing(HexPattern original, long seed, System.Func<HexPattern, bool>? rule = null)
    {
        var rand = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        for (int i = 0; i < 100; i++)
        {
            var path = WalkPath(original, rand);
            if (path != null && (rule == null || rule(path)))
            {
                return path;
            }
        }
        return original;
    }

    private static HexPattern? WalkPath(HexPattern original, System.Random rand)
    {
        var graph = ToGraph(original);
        var oddNodes = graph.Where(kv => kv.Value.Count % 2 == 1).Select(kv => kv.Key).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        var allNodes = graph.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        HexCoord current = oddNodes.Count switch
        {
            0 => allNodes[rand.Next(allNodes.Count)],
            2 => oddNodes[rand.Next(2)],
            _ => throw new System.InvalidOperationException("图案不是可一笔画的图"),
        };

        var stack = new Stack<HexCoord>();
        var output = new List<HexCoord>();
        do
        {
            var exits = graph[current];
            if (exits.Count == 0)
            {
                output.Add(current);
                current = stack.Pop();
            }
            else
            {
                stack.Push(current);
                var sorted = exits.OrderBy(d => (int)d).ToList();
                var burnDir = sorted[rand.Next(sorted.Count)];
                exits.Remove(burnDir);
                var next = current + burnDir;
                if (graph.TryGetValue(next, out var back)) back.Remove(burnDir.RotatedBy(HexAngle.Back));
                current = next;
            }
        } while ((graph.TryGetValue(current, out var left) && left.Count > 0) || stack.Count > 0);
        output.Add(current);

        // 相邻节点 → 方向 → 相邻方向之间的转角
        var dirs = new List<HexDir>();
        for (int i = 0; i + 1 < output.Count; i++)
        {
            var d = DeltaDir(output[i], output[i + 1]);
            if (d == null) return null;
            dirs.Add(d.Value);
        }
        if (dirs.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i + 1 < dirs.Count; i++)
        {
            sb.Append(dirs[i + 1].AngleFrom(dirs[i]).ToChar());
        }
        return HexPattern.TryFromAnglesUnchecked(sb.ToString(), dirs[0], out var p, out _) ? p : null;
    }

    private static HexDir? DeltaDir(HexCoord a, HexCoord b)
    {
        for (int i = 0; i < HexDirExtensions.Count; i++)
        {
            var d = (HexDir)i;
            if ((a + d).Equals(b)) return d;
        }
        return null;
    }

    private static Dictionary<HexCoord, HashSet<HexDir>> ToGraph(HexPattern pat)
    {
        var graph = new Dictionary<HexCoord, HashSet<HexDir>>();
        HashSet<HexDir> At(HexCoord c)
        {
            if (!graph.TryGetValue(c, out var set)) graph[c] = set = new HashSet<HexDir>();
            return set;
        }
        var compass = pat.StartDir;
        var cursor = HexCoord.Origin;
        foreach (var a in pat.Angles)
        {
            At(cursor).Add(compass);
            At(cursor + compass).Add(compass.RotatedBy(HexAngle.Back));
            cursor += compass;
            compass = compass.RotatedBy(a);
        }
        At(cursor).Add(compass);
        At(cursor + compass).Add(compass.RotatedBy(HexAngle.Back));
        return graph;
    }

    /// <summary>图案的无向边集（归一化后），用来判断两种画法是不是同一个形状。</summary>
    public static HashSet<(HexCoord, HexCoord)> EdgeSet(HexPattern pat)
    {
        var edges = new List<(HexCoord, HexCoord)>();
        var compass = pat.StartDir;
        var cursor = HexCoord.Origin;
        void Add(HexCoord a, HexCoord b) => edges.Add(a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a));
        foreach (var a in pat.Angles)
        {
            Add(cursor, cursor + compass);
            cursor += compass;
            compass = compass.RotatedBy(a);
        }
        Add(cursor, cursor + compass);
        // 平移到最小坐标为原点，比较时与画在哪里无关
        int minX = edges.Min(e => System.Math.Min(e.Item1.X, e.Item2.X));
        int minY = edges.Min(e => System.Math.Min(e.Item1.Y, e.Item2.Y));
        var o = new HexCoord(minX, minY);
        return edges.Select(e => (e.Item1 - o, e.Item2 - o)).ToHashSet();
    }
}
