using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 潜艇三明治。原版 HexItems.SUBMARINE_SANDWICH：食物（饥饿值 14、饱和度 1.2 —— 比牛排还顶饱），
/// 配方 " SA" " C " " B "：木棍 + 紫水晶碎片 + 熟牛肉 + 面包（源 HexplatRecipes 165-174，注释「Why am I like this」）。
///
/// 泰拉：MC 的饥饿值对应「吃饱了」buff。14 点是 MC 里数一数二的，给最高一档「酒足饭饱」10 分钟。
/// 熟牛肉 → 烤松鼠（泰拉在烹饪锅里做的熟肉），面包 → 干草（泰拉没有面包），木棍 → 木材。
/// </summary>
public sealed class SubSandwich : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 5;

    public override void SetDefaults()
    {
        Item.DefaultToFood(24, 24, BuffID.WellFed3, 10 * 60 * 60);
        Item.value = Item.sellPrice(silver: 50);
        Item.rare = ItemRarityID.Blue;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.Wood)
            .AddIngredient<AmethystShard>()
            .AddIngredient(ItemID.GrilledSquirrel)
            .AddIngredient(ItemID.Hay)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
