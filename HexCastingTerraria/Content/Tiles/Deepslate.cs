using HexCastingTerraria.Content.Items;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 深板岩。原版石板 / 板岩块的原料是 MC 的深板岩，泰拉没有；移植版自己做一个（2026-10-01 群友提议、用户定）：
/// 石块 ×6 + 紫水晶粉 → 1 个深板岩；1 个深板岩 → 2 块石板（<see cref="HexSlateItem"/>）；
/// 板岩块照原版 8 深板岩 + 1 粉 → 8。
///
/// 方块属性照搬泰拉地下的花岗岩（用户的主意），换个颜色：贴图直接用游戏自带的花岗岩贴图，画的时候把蓝色压下去，
/// 变成接近中性的深灰。不存贴图文件 —— 泰拉原版贴图不进公开仓库。
/// </summary>
public sealed class Deepslate : ModTile
{
    /// <summary>
    /// 花岗岩偏蓝紫（泰拉给它的地图颜色是 50, 46, 104），蓝色乘一半左右就接近中性的深灰（MC 深板岩那种颜色）。
    /// 染色只能变暗，所以只动蓝色。
    /// </summary>
    internal static readonly Color Tint = new(255, 250, 125);

    public override string Texture => $"Terraria/Images/Tiles_{TileID.Granite}";

    public override void SetStaticDefaults()
    {
        // 照搬花岗岩的方块属性
        Main.tileSolid[Type] = Main.tileSolid[TileID.Granite];
        Main.tileBlockLight[Type] = Main.tileBlockLight[TileID.Granite];
        Main.tileMergeDirt[Type] = Main.tileMergeDirt[TileID.Granite];
        Main.tileStone[Type] = Main.tileStone[TileID.Granite];
        Main.tileBrick[Type] = Main.tileBrick[TileID.Granite];
        for (int t = 0; t < TileID.Count; t++)
        {
            Main.tileMerge[Type][t] = Main.tileMerge[TileID.Granite][t];
        }

        MinPick = 0;
        DustType = DustID.Granite;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(56, 54, 58));
    }

    public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, ref TileDrawInfo drawData)
    {
        drawData.colorTint = Tint;
        drawData.finalColor = drawData.finalColor.MultiplyRGB(Tint);
    }
}

/// <summary>深板岩（物品形态）。图标借用泰拉的花岗岩块，画的时候同样压掉蓝色。</summary>
public sealed class DeepslateItem : ModItem
{
    public override string Texture => $"Terraria/Images/Item_{ItemID.GraniteBlock}";

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<Deepslate>());
        Item.width = 16;
        Item.height = 16;
        Item.value = Item.sellPrice(copper: 20);
    }

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor,
        Color itemColor, Vector2 origin, float scale)
    {
        spriteBatch.Draw(TextureAssets.Item[Type].Value, position, frame, drawColor.MultiplyRGBA(Deepslate.Tint), 0f, origin, scale, SpriteEffects.None, 0f);
        return false;
    }

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI)
    {
        var texture = TextureAssets.Item[Type].Value;
        var position = Item.position - Main.screenPosition + new Vector2(Item.width / 2f, Item.height - texture.Height * 0.5f + 2f);
        spriteBatch.Draw(texture, position, null, alphaColor.MultiplyRGBA(Deepslate.Tint), rotation, texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);
        return false;
    }

    public override void AddRecipes()
    {
        // 群友的方案（用户定）：石块 ×6 + 紫水晶粉 ×1 → 1 个深板岩
        CreateRecipe()
            .AddIngredient(ItemID.StoneBlock, 6)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
