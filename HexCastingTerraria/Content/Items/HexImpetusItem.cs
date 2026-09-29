using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 原动力（物品形态）。对应源项目 `hexcasting:impetus/*`。
///
/// 放下后成为法术环的「CPU」：持媒质池，右击启动环。
/// **它不含任何图案** —— 图案在石板上。
///
/// ⚠️ 与原版的差异：源项目有 4 种触发方式
/// （empty 手动 / rightclick / looking 注视 / redstone 红石），
/// 泰拉侧先做**手动右击**这一种，其余等红石触发那一步再补。
/// </summary>
public sealed class HexImpetusItem : ModItem
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
        Item.createTile = ModContent.TileType<HexImpetus>();
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }

    /// <summary>复用方块贴图（理由见 HexSlateItem.Texture）。</summary>
    public override string Texture => "HexCastingTerraria/Content/Tiles/HexImpetus";

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:390-398，shaped：
        //     PSS        P = 紫珀块 ×2      （末地建材）
        //     BAB        S = 板岩块 ×4
        //     SSP        B = 铁栏杆 ×2
        //                A = 充能紫水晶 ×1
        //
        // **阶段：肉后 + 启蒙。** 源项目这条配方挂的是 `enlightenment` 门槛 ——
        // 启蒙 = 一次过载消耗掉 ≥80% 生命、且只剩不到半颗心的施法（HexAdvancements.ENLIGHTEN），
        // 本模组已按原版实现（Core/Media/Overcast.cs）。所以配方条件就是「已启蒙」，
        // 合成站用秘银砧（肉后），紫珀块（末地）用水晶碎块（肉后神圣地）代替。
        // （曾经定在月后：理由是「泰拉没有过载机制」—— 那时启蒙确实拿不到，现在可以了。）
        //
        // 铁栏杆 → ItemID.IronFence：同为铁制的细栏，视觉与用途都最近。
        CreateRecipe()
            .AddIngredient(ItemID.CrystalShard, 2)      // 紫珀块（末地）→ 水晶碎块（肉后神圣地，同法术书）
            .AddIngredient<SlateBlockItem>(4)           // 板岩块 ×4（源用的是建材块，不是石板）
            .AddIngredient(ItemID.IronFence, 2)         // 铁栏杆 ×2
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.MythrilAnvil)       // 秘银砧 / 山铜砧（肉后）
            .AddCondition(HexConditions.Enlightened)
            .Register();
    }
}
