using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 建材方块物品的公共实现（配合 <see cref="HexDecoBlock"/>）。
///
/// 两个要点：
///   ① **贴图复用方块的**：`Texture` 指向方块那张 16x16。建材的物品图标与
///      放置后的样子本来就该一致，多存一份 PNG 只会让两边慢慢画得不一样。
///   ② 放置完全交给原版的 `createTile` 流程 —— 连通性、合并、支撑判定
///      这些泰拉自己算得比我们准。
/// </summary>
public abstract class HexDecoBlockItem : ModItem
{
    /// <summary>对应的方块类型。</summary>
    public abstract int TileType { get; }

    /// <summary>物品稀有度。</summary>
    protected virtual int ItemRarity => ItemRarityID.White;

    /// <summary>
    /// 物品图标单独一张（`Content/Items/Blocks/方块名.png`，原版贴图 ×2）。
    /// 不能复用方块贴图：自动选帧的方块贴图是 288×270 的整张图集，拿来当图标会被整张缩进背包格子。
    /// </summary>
    public override string Texture => (Mod as HexCastingTerraria) is null
        ? base.Texture
        : $"HexCastingTerraria/Content/Items/Blocks/{GetType().Name.Replace("Item", string.Empty)}";

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 999;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = TileType;
        Item.rare = ItemRarity;
        Item.value = Item.sellPrice(copper: 20);
    }
}
