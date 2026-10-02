using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 补上缺的图格实体。
///
/// tML 1.4.4 的 <c>ModTileEntity.Hook_AfterPlacement</c> 默认什么都不放（直接返回 -1，1.4.5-dev 时代不是这样），
/// 而石板、促动石、导向石、挂轴框的放置钩子之前接的就是它 —— 在 1.4.4.9 上放下去的这些方块都没有图格实体：
/// 法术环走不动，图案存不进去。放置钩子已经改成 <c>Generic_HookPostPlaceMyPlayer</c>；
/// 已经放在世界里的，进世界时（单机 / 服务端）补一遍。补出来的是空的：原来就没有图格实体，也就没有可找回的内容。
///
/// 挂轴框的尺寸也在这里换：2026-10-02 照原版从 2/3/4 格改成 1/2/3 格（<see cref="WallScrollTile.ObjectSize"/>），
/// 旧世界里的挂板还按旧尺寸铺着，帧号超出新的图集。进世界时先把它们缩成新尺寸：左上角不动（图格实体和挂着的图案都在那里），
/// 多出来的一行一列拆掉、不掉东西。
/// </summary>
public sealed class TileEntityRepair : ModSystem
{
    public override void PostWorldLoad()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        // 先按方块类型查好表，扫全图时每格只查一次数组
        var owner = new ModTileEntity?[TileLoader.TileCount];
        var scrollSize = new int[TileLoader.TileCount];
        for (int type = TileID.Count; type < TileLoader.TileCount; type++)
        {
            var modTile = TileLoader.GetTile(type);
            owner[type] = modTile switch
            {
                HexSlate => ModContent.GetInstance<HexSlateEntity>(),
                HexDirectrixBase => ModContent.GetInstance<HexDirectrixEntity>(),
                HexImpetusBase => ModContent.GetInstance<HexImpetusEntity>(),
                WallScrollTile => ModContent.GetInstance<WallScrollEntity>(),
                _ => null,
            };
            if (modTile is WallScrollTile scroll) scrollSize[type] = scroll.ObjectSize;
        }

        int resized = ShrinkOldWallScrolls(scrollSize);
        if (resized > 0) Mod.Logger.Info($"[HexCasting] 旧尺寸的挂轴框换成原版尺寸：{resized} 块");

        int repaired = 0;
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];
                if (!tile.HasTile || owner[tile.TileType] is not { } entity) continue;
                // 多格方块（挂轴框）的图格实体在左上角
                var topLeft = TileObjectData.TopLeft(x, y);
                if (topLeft.X != x || topLeft.Y != y) continue;
                if (TileEntity.ByPosition.ContainsKey(new Point16(x, y))) continue;
                if (entity.Place(x, y) >= 0) repaired++;
            }
        }

        if (repaired > 0) Mod.Logger.Info($"[HexCasting] 补上缺的图格实体：{repaired} 个（1.4.4 上放下去时没放出来的）");
    }

    /// <summary>
    /// 旧挂板比新尺寸 n 大一格（n + 1 见方），帧号是格子在旧挂板里的位置 0..n；新挂板的帧号只到 n − 1。
    /// 所以「横竖帧号都是 n」的那一格就是一块旧挂板的右下角，往左上退 n 格是它的左上角。
    /// </summary>
    internal static int ShrinkOldWallScrolls(int[] scrollSize)
    {
        var olds = new System.Collections.Generic.List<(int X, int Y, ushort Type, int N)>();
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];
                if (!tile.HasTile) continue;
                int n = scrollSize[tile.TileType];
                if (n == 0 || tile.TileFrameX / 18 != n || tile.TileFrameY / 18 != n) continue;
                if (x - n >= 0 && y - n >= 0) olds.Add((x - n, y - n, tile.TileType, n));
            }
        }

        foreach (var (x0, y0, type, n) in olds)
        {
            for (int dx = 0; dx <= n; dx++)
            {
                for (int dy = 0; dy <= n; dy++)
                {
                    var t = Main.tile[x0 + dx, y0 + dy];
                    if (!t.HasTile || t.TileType != type) continue;
                    if (dx == n || dy == n)
                    {
                        t.HasTile = false;
                        continue;
                    }
                    t.TileFrameX = (short)(dx * 18);
                    t.TileFrameY = (short)(dy * 18);
                }
            }
        }
        return olds.Count;
    }
}
