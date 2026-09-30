using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 启迪木的家具。原版 HexBlocks 里有 8 种：楼梯、台阶、栅栏、栅栏门、门、活板门、按钮、压力板。
///
/// 规则：**泰拉有对应物的做成真的启迪木家具，没有的不做**（2026-09-30 用户定）：
///   - 门、栅栏（泰拉的栅栏是墙）、按钮（= 开关）、压力板 → 本文件下方的物品 + Content/Tiles/EdifiedFurnitureTiles.cs
///   - 楼梯 / 台阶 → 泰拉的楼梯就是平台、台阶就是锤出来的半砖：启迪木板本身能锤成半砖，平台用启迪木板合成原版木平台
///     （平台是 27 帧的斜坡图集，原版素材里没有能照着生成的东西）
///   - 栅栏门、活板门 → 泰拉没有栅栏门；活板门的开合写死在原版代码里、没有模组接口 —— 不做
///
/// 另外保留的「用启迪木板合成原版工作台 / 椅 / 桌 / 书架」是泰拉的惯例（每种木头都能做家具），原版没有。
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

        // 平台（对应源项目的 edified_stairs —— 泰拉的楼梯就是平台；台阶 = 锤出来的半砖，不需要单独的物品）
        Recipe.Create(ItemID.WoodPlatform, 2)
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

/// <summary>启迪木门。原版配方：启迪木板 ×6 → 3 扇（HexplatRecipes 的 door 配方）。</summary>
public sealed class EdifiedDoorItem : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 1;

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<EdifiedDoorClosed>());
        Item.width = 14;
        Item.height = 28;
        Item.value = 200;
    }

    public override void AddRecipes()
        => CreateRecipe(3).AddIngredient<EdifiedPlanksItem>(6).AddTile(TileID.WorkBenches).Register();
}

/// <summary>启迪木栅栏（墙）。原版 4 木板 + 2 木棍 → 3 个栅栏方块；泰拉栅栏是一格一格铺的墙，按泰拉木栅栏 1 → 4。</summary>
public sealed class EdifiedFenceItem : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 400;

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableWall(ModContent.WallType<EdifiedFence>());
        Item.width = 24;
        Item.height = 24;
    }

    public override void AddRecipes()
    {
        CreateRecipe(4).AddIngredient<EdifiedPlanksItem>().AddTile(TileID.WorkBenches).Register();
        // 泰拉的墙都能拆回材料（4 墙 → 1 块）
        Recipe.Create(ModContent.ItemType<EdifiedPlanksItem>()).AddIngredient(Type, 4).AddTile(TileID.WorkBenches).Register();
    }
}

/// <summary>启迪木按钮。原版配方：启迪木板 ×1 → 1。</summary>
public sealed class EdifiedButtonItem : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 1;

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<EdifiedButton>());
        Item.width = 16;
        Item.height = 16;
        Item.mech = true;   // 拿着时显示电线
    }

    public override void AddRecipes()
        => CreateRecipe().AddIngredient<EdifiedPlanksItem>().AddTile(TileID.WorkBenches).Register();
}

/// <summary>启迪木压力板。原版配方：启迪木板 ×2 → 1。</summary>
public sealed class EdifiedPressurePlateItem : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 1;

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<EdifiedPressurePlate>());
        Item.width = 16;
        Item.height = 16;
        Item.mech = true;
    }

    public override void AddRecipes()
        => CreateRecipe().AddIngredient<EdifiedPlanksItem>(2).AddTile(TileID.WorkBenches).Register();
}
