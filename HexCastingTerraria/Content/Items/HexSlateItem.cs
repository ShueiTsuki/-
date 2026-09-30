using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 石板（物品形态）。对应源项目 `hexcasting:slate`。
///
/// 放下后成为法术环的一块「指令」：
///   - 用手上**存着图案**的物品右键石板 → 把图案写进去
///   - **空手右键** → 顺时针转 90° 改朝向
///
/// 朝向必须垂直于流向，否则会挡住控制流导致环走不通。
/// </summary>
public sealed class HexSlateItem : ModItem
{
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
        Item.createTile = ModContent.TileType<HexSlate>();
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(silver: 10);
    }


    // 贴图：Content/Items/HexSlateItem.png（原版 item/slate_blank ×2，由 _tools/gen_textures.py 生成）。
    // 注意 tModLoader 找不到类名同名 PNG 会禁用整个模组，而且专用服务器不报（贴图只在客户端加载）。

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:238-243，shaped：
        //     " A "
        //     "SSS"     A = 紫水晶粉 ×1，S = 深板岩 ×3  → 石板 ×6
        //
        // 泰拉没有深板岩，用**石块**（StoneBlock）代替 —— 同属「地下的基础石头」，
        // 而且泰拉的石块比 MC 的深板岩更易得，正好抵掉「没有深层/表层之分」这点差异。
        // 比例（3 石 + 1 粉 → 6 石板）与原版逐项一致，**没有**额外加价：
        // 石板在源项目里就是廉价消耗品（法术环要摆一堆），做贵了整个体系都推不动。
        CreateRecipe(6)
            .AddIngredient(ItemID.StoneBlock, 3)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
