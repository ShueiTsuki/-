using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 启迪木的「家具套装」：用启迪木板去合成**原版家具**。
///
/// ## 为什么不自己再做 8 个家具方块
///
/// 源项目的启迪木有一整套家具：楼梯、台阶、栅栏、栅栏门、门、活板门、按钮、压力板。
/// 那是 MC 的必需项 —— MC 里每种木材都得有自己的家具方块。
///
/// 泰拉的家具体系不一样：
///   - **楼梯/台阶** = 平台与半砖，是方块的**属性**而不是独立方块
///   - **栅栏 / 栅栏门 / 按钮 / 压力板** 是原版一整套共用机制（还会接线），复刻等于重写原版
///   - **门 / 活板门** 是多格 + 开合状态的复杂方块
///
/// 所以做法是：**用启迪木板合成原版家具**。玩家拿到的东西用途与 MC 那套完全一致
/// （能坐、能挡、能开门、能接线），而且立刻融入原版机制。
///
/// 这是有意的取舍：少 8 个自定义方块，换来不与原版打架。已登记在 `TODO_PLAN.md` 的差异表。
///
/// ⚠️ 注意这里用的是 `Recipe.Create(物品ID)` —— 产出**原版物品**的配方必须这么写。
/// `ModItem.AddRecipes` 里的 `CreateRecipe()` 只能产出**它自己**，写在那里会静默失败。
/// </summary>
public sealed class EdifiedFurnitureRecipes : ModSystem
{
    public override void AddRecipes()
    {
        int planks = ModContent.ItemType<EdifiedPlanksItem>();

        // 工作台：家具套装的入口
        Recipe.Create(ItemID.WorkBench)
            .AddIngredient(planks, 10)
            .Register();

        // 椅 / 桌
        Recipe.Create(ItemID.WoodenChair)
            .AddIngredient(planks, 4)
            .AddTile(TileID.WorkBenches)
            .Register();

        Recipe.Create(ItemID.WoodenTable)
            .AddIngredient(planks, 8)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 门（对应源项目的 edified_door）
        Recipe.Create(ItemID.WoodenDoor)
            .AddIngredient(planks, 6)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 平台（对应源项目的 edified_stairs / edified_slab —— 泰拉里这两者都是平台/半砖）
        Recipe.Create(ItemID.WoodPlatform, 2)
            .AddIngredient(planks, 1)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 栅栏（对应 edified_fence / edified_fence_gate）
        Recipe.Create(ItemID.WoodenFence, 4)
            .AddIngredient(planks, 1)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 书架（对应 edified 的收纳类家具）
        Recipe.Create(ItemID.Bookcase)
            .AddIngredient(planks, 20)
            .AddIngredient(ItemID.Book, 10)
            .AddTile(TileID.Sawmill)
            .Register();
    }
}
