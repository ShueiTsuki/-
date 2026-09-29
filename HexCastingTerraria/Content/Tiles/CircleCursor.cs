using System.Collections.Generic;
using Terraria;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 法术环的「执行游标」：当前正在执行哪一格。
///
/// ## 为什么需要一个共享状态
///
/// 泰拉的方块绘制发生在 `ModTile.SpecialDraw` 里，那里拿不到「环执行到哪了」——
/// 环的状态在 TileEntity 上，而绘制回调只给你坐标。
/// 所以用一个极小的全局表把两者连起来：环推进时登记，绘制时查询。
///
/// ## 为什么带 TTL 而不是「开始登记、结束清除」
///
/// 环可能因为挖掉一块石板、玩家死亡、服务端重载而**再也不调用 Stop**。
/// 那种情况下如果只靠显式清除，高亮会永久留在那一格上 ——
/// 而且玩家完全不知道该怎么消掉它。TTL 让它自己过期，是**自我修复**的。
///
/// 联机：服务端推进环，客户端画方块。所以游标要广播（见 `HexNetSync.BroadcastCircleCursor`），
/// 客户端收到后就往这张表里写，走的是同一套查询路径。
/// </summary>
internal static class CircleCursor
{
    /// <summary>登记后保留多少 tick。比最慢的走格速度（10 tick）再宽一点。</summary>
    public const int TtlTicks = 14;

    private static readonly Dictionary<(int X, int Y), int> Remaining = new();

    /// <summary>登记一格（或刷新它的存活时间）。</summary>
    public static void Mark(int x, int y)
    {
        Clear();
        Remaining[(x, y)] = TtlTicks;
    }

    /// <summary>这一格现在是不是「正在执行」。</summary>
    public static bool IsActive(int x, int y)
        => Remaining.TryGetValue((x, y), out int left) && left > 0;

    /// <summary>清空（环停止时调用）。</summary>
    public static void Clear() => Remaining.Clear();

    /// <summary>每 tick 递减。由客户端系统调用。</summary>
    public static void Tick()
    {
        if (Remaining.Count == 0) return;

        var expired = new List<(int X, int Y)>();
        var keys = new List<(int X, int Y)>(Remaining.Keys);
        foreach (var key in keys)
        {
            int left = Remaining[key] - 1;
            if (left <= 0)
            {
                expired.Add(key);
            }
            else
            {
                Remaining[key] = left;
            }
        }

        foreach (var key in expired)
        {
            Remaining.Remove(key);
        }
    }

    /// <summary>服务端推进时：本地登记 + 广播给附近玩家。</summary>
    public static void MarkAndBroadcast(int x, int y)
    {
        Mark(x, y);

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Net.HexNetSync.BroadcastCircleCursor(x, y);
        }
    }

    /// <summary>客户端收到广播。</summary>
    public static void Receive(int x, int y) => Mark(x, y);
}
