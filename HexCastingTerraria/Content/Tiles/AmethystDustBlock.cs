using HexCastingTerraria.Content.Items;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 紫水晶粉块。对应源项目 `hexcasting:amethyst_dust_block`（装饰方块）。
///
/// 配方比例是 **4 ↔ 1**，不是 MC 常见的 9 ↔ 1 ——
/// 逐条抄自源项目的 `amethyst_dust_packing` / `amethyst_dust_unpacking`。
/// </summary>
public sealed class AmethystDustBlock : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Dig;
        AddMapEntry(new Color(150, 108, 205));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 微微发光，当装饰建材时观感更好
        r = 0.10f;
        g = 0.06f;
        b = 0.16f;
    }
}

/// <summary>紫水晶粉块的物品形态。</summary>
public sealed class AmethystDustBlockItem : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 9999;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = ModContent.TileType<AmethystDustBlock>();
        Item.value = Item.sellPrice(copper: 10);
    }

    /// <summary>复用方块贴图（理由见 HexSlateItem.Texture）。</summary>
    public override string Texture => "HexCastingTerraria/Content/Tiles/AmethystDustBlock";

    public override void AddRecipes()
    {
        // 打包：2×2 粉尘 -> 1 方块（源项目是 shaped 的 "XX"/"XX"）
        CreateRecipe()
            .AddIngredient<AmethystDust>(4)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 解包：1 方块 -> 4 粉尘（不亏不赚）
        CreateRecipe(4)
            .AddIngredient<AmethystDustBlockItem>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
