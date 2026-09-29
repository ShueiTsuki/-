using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

// ── 壁挂装饰与阿卡夏装饰的物品形态 ──────────────────────────────────
//
// 全部复用 <see cref="HexDecoBlockItem"/> 的行为（贴图复用方块那张）。
// 这里每个类只声明「我是哪个方块 + 我稀有度多少 + 我怎么合成」。

/// <summary>卷轴纸（物品）。</summary>
public sealed class ScrollPaperItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<ScrollPaper>();

    public override void AddRecipes()
    {
        CreateRecipe(4)
            .AddIngredient(ItemID.Silk, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>古卷轴纸（物品）。</summary>
public sealed class AncientScrollPaperItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<AncientScrollPaper>();

    public override void AddRecipes()
    {
        CreateRecipe(4)
            .AddIngredient<ScrollPaperItem>(4)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>卷轴纸灯笼（物品）。</summary>
public sealed class ScrollPaperLanternItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<ScrollPaperLantern>();

    public override void AddRecipes()
    {
        CreateRecipe(2)
            .AddIngredient<ScrollPaperItem>(2)
            .AddIngredient(ItemID.Torch, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>古卷轴纸灯笼（物品）。</summary>
public sealed class AncientScrollPaperLanternItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<AncientScrollPaperLantern>();

    public override void AddRecipes()
    {
        CreateRecipe(2)
            .AddIngredient<AncientScrollPaperItem>(2)
            .AddIngredient(ItemID.Torch, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>紫晶壁灯（物品）。</summary>
public sealed class AmethystSconceItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<AmethystSconce>();

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.Amethyst, 2)
            .AddIngredient(ItemID.Torch, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>阿卡夏书架（物品）。</summary>
public sealed class AkashicBookshelfItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<AkashicBookshelf>();

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:410-417，shaped：
        //     LPL        L = 任意启迪原木 ×4
        //     CCC        P = 任意启迪木板 ×2
        //     LPL        C = 书 ×3
        //
        // 阶段：月后 —— 源项目这条挂 enlightenment 门槛（启蒙大战法术那条线）。
        CreateRecipe()
            .AddRecipeGroup(HexRecipeGroups.EdifiedLogs, 4)
            .AddIngredient<EdifiedPlanksItem>(2)
            .AddIngredient(ItemID.Book, 3)
            .AddTile(TileID.LunarCraftingStation)
            .Register();
    }
}

/// <summary>阿卡夏系带（物品）。</summary>
public sealed class AkashicLigatureItem : HexDecoBlockItem
{
    public override int TileType => ModContent.TileType<AkashicLigature>();

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:419-428，shaped，产出 ×4：
        //     LPL        L = 任意启迪原木 ×4
        //     123        P = 任意启迪木板 ×2
        //     LPL        1 = 紫水晶粉 ×1，2 = 紫水晶碎片 ×1，3 = 充能紫水晶 ×1
        //
        // 阶段：月后（源挂 enlightenment 门槛）。
        CreateRecipe(4)
            .AddRecipeGroup(HexRecipeGroups.EdifiedLogs, 4)
            .AddIngredient<EdifiedPlanksItem>(2)
            .AddIngredient<AmethystDust>(1)
            .AddIngredient<AmethystShard>(1)
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.LunarCraftingStation)
            .Register();
    }
}
