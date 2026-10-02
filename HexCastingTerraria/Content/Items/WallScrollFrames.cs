using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 卷轴挂板的基类。对应源项目里「把卷轴挂到墙上」那一步所需的载体。
///
/// ## 为什么需要它（与源项目的差异）
///
/// 原版是**一步**：拿卷轴对着墙面右键，卷轴本身就变成墙上的悬挂实体。
/// 泰拉侧做成了**两步**（挂板 + 往挂板上挂卷轴），原因是泰拉没有
/// 「放置实体时携带数据」的官方钩子：
///   - `ModTile.PlaceInWorld` 在 1.4.5 已经被移除
///   - `HookPostPlaceMyPlayer` 拿不到客户端手上的物品内容，联机时更是只有服务端在跑
///
/// 换成两步之后，写数据走的是**石板那条已经被验证过的路径**
/// （客户端写 TileEntity + 上报服务端），风险低得多。
/// 代价是多一个合成物品 —— 但挂板本身也是合理的装饰品。
///
/// 三种尺寸对应源项目的 `blockSize` 1/2/3。
/// </summary>
public abstract class WallScrollFrameItem : ModItem
{
    /// <summary>对应的挂板方块类型。</summary>
    public abstract int TileType { get; }

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 99;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = TileType;
        Item.rare = ItemRarityID.White;
        Item.value = Item.sellPrice(silver: 2);
    }
}

/// <summary>小挂板（1x1，原版 blockSize 1）。配小卷轴。</summary>
public sealed class WallScrollFrameSmall : WallScrollFrameItem
{
    public override int TileType => ModContent.TileType<WallScrollSmall>();

    public override void AddRecipes()
    {
        CreateRecipe(2)
            .AddIngredient(ItemID.Wood, 6)
            .AddIngredient(ItemID.Silk, 2)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>中挂板（2x2，原版 blockSize 2）。配中卷轴。</summary>
public sealed class WallScrollFrameMedium : WallScrollFrameItem
{
    public override int TileType => ModContent.TileType<WallScrollMedium>();

    public override void AddRecipes()
    {
        CreateRecipe(2)
            .AddIngredient(ItemID.Wood, 12)
            .AddIngredient(ItemID.Silk, 4)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>大挂板（3x3，原版 blockSize 3）。配大卷轴。</summary>
public sealed class WallScrollFrameLarge : WallScrollFrameItem
{
    public override int TileType => ModContent.TileType<WallScrollLarge>();

    public override void AddRecipes()
    {
        CreateRecipe(2)
            .AddIngredient(ItemID.Wood, 20)
            .AddIngredient(ItemID.Silk, 8)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
