using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 是谁拿着镐在挖这一格。掉落要看「用没用对工具」、时运（镐力换算）、共振前缀的方块都靠它：
/// 紫水晶簇（<see cref="AmethystGrowth"/>）、淬灵晶块（<see cref="QuenchedAllayGlobal"/>）。
///
/// 泰拉的挖掉方块钩子不告诉你是谁挖的：单机 / 本地客户端看本人瞄着的格子；
/// 服务端不知道别人瞄着哪儿，就找附近（12 格内）正在挥镐的玩家。爆炸、法术之类没人挖 → null。
/// </summary>
internal static class TileMiner
{
    public static Player? Find(int i, int j)
    {
        var center = new Vector2(i * 16 + 8, j * 16 + 8);
        if (Main.netMode != NetmodeID.Server)
        {
            var p = Main.LocalPlayer;
            return p is { active: true, dead: false } && p.itemAnimation > 0 && IsPickaxe(p.HeldItem)
                   && Player.tileTargetX == i && Player.tileTargetY == j ? p : null;
        }
        Player? best = null;
        float bestD = 16f * 12f;
        foreach (var p in Main.ActivePlayers)
        {
            if (p.dead || p.itemAnimation <= 0 || !IsPickaxe(p.HeldItem)) continue;
            float d = Vector2.Distance(p.Center, center);
            if (d < bestD) { best = p; bestD = d; }
        }
        return best;
    }

    public static bool IsPickaxe(Item item) => !item.IsAir && item.pick > 0;
}
