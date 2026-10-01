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
/// </summary>
public sealed class TileEntityRepair : ModSystem
{
    public override void PostWorldLoad()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        // 先按方块类型查好表，扫全图时每格只查一次数组
        var owner = new ModTileEntity?[TileLoader.TileCount];
        for (int type = TileID.Count; type < TileLoader.TileCount; type++)
        {
            owner[type] = TileLoader.GetTile(type) switch
            {
                HexSlate => ModContent.GetInstance<HexSlateEntity>(),
                HexDirectrixBase => ModContent.GetInstance<HexDirectrixEntity>(),
                HexImpetusBase => ModContent.GetInstance<HexImpetusEntity>(),
                WallScrollTile => ModContent.GetInstance<WallScrollEntity>(),
                _ => null,
            };
        }

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
}
