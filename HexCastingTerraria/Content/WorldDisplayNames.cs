using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content;

/// <summary>
/// 事故消息里要报的世界上的名字：那一格方块叫什么（上游 Mishap.blockAtPos）、地上那堆物品是什么有几个（上游 MishapBadItem）。
///
/// 泰拉的方块没有「方块名」这一说，玩家能看到的名字是两处：
///   ① 地图图例名（鼠标悬停地图时显示的那个，Lang.GetMapObjectName）—— 最贴近「这一格是什么」，但只有客户端加载了地图数据；
///   ② 放置它的物品名（TileLoader.GetItemDropFromTypeAndStyle）—— 服务端也能查。
/// 所以客户端（单人）先用 ①，查不到或在服务端时用 ②；都没有返回 null，事故消息退回坐标。
/// 注意：服务端没有初始化 MapHelper（Main.Initialize 里 !dedServ 才调），那里绝不能碰 ①。
/// </summary>
internal static class WorldDisplayNames
{
    /// <summary>MC 的空气方块叫「空气」（block.minecraft.air）；泰拉空着的格子同样说法。</summary>
    private const string Air = "空气";

    /// <summary>图格 (tx, ty) 的显示名。</summary>
    public static string? BlockName(int tx, int ty)
    {
        if (!WorldGen.InWorld(tx, ty)) return null;
        Tile tile = Main.tile[tx, ty];
        if (!tile.HasTile && tile.LiquidAmount == 0) return Air;

        if (!Main.dedServ && MapHelper.tileLookup is not null)
        {
            try
            {
                string name = Lang.GetMapObjectName(MapHelper.CreateMapTile(tx, ty, 255).Type);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (System.Exception)
            {
                // 地图数据异常时退到下面的物品名，事故消息不该因为取名字而失败
            }
        }

        if (!tile.HasTile) return null;
        int item = TileLoader.GetItemDropFromTypeAndStyle(tile.TileType, TileObjectData.GetTileStyle(tile));
        return item > 0 ? Lang.GetItemNameValue(item) : null;
    }

    /// <summary>掉落物 iota 指着的那一堆物品（名字 + 数量）。不是掉落物或已经没了 → null。</summary>
    public static ItemStackInfo? ItemStack(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Item) return null;
        int i = entity.Index;
        if (i < 0 || i >= Main.maxItems || Main.item[i] is not { active: true } it) return null;
        return new ItemStackInfo(it.Name, it.IsAir ? 0 : it.stack);
    }
}
