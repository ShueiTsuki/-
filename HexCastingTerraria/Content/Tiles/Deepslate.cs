using System.IO;
using HexCastingTerraria.Content.Items;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 深板岩。原版石板 / 板岩块的原料是 MC 的深板岩，泰拉没有；移植版自己做一个（2026-10-01 群友提议、用户定）：
/// 石块 ×6 + 紫水晶粉 → 1 个深板岩；1 个深板岩 → 2 块石板（<see cref="HexSlateItem"/>）；
/// 板岩块照原版 8 深板岩 + 1 粉 → 8。
///
/// 方块属性照搬泰拉地下的花岗岩（用户的主意），换个颜色：游戏启动后把自带的花岗岩贴图读出来，
/// 去色调亮成深灰（见 <see cref="DeepslateArt"/>）。不存贴图文件 —— 泰拉原版贴图不进公开仓库。
/// </summary>
public sealed class Deepslate : ModTile
{
    /// <summary>贴图路径先指向游戏自带的花岗岩（服务端 / 上色之前用它），客户端加载完换成上过色的。</summary>
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
        AddMapEntry(new Color(62, 62, 67));
    }
}

/// <summary>深板岩（物品形态）。图标借用泰拉的天然花岗岩，同样上色。</summary>
public sealed class DeepslateItem : ModItem
{
    public override string Texture => $"Terraria/Images/Item_{ItemID.Granite}";   // 天然花岗岩（Granite = 3086；GraniteBlock 是光面花岗岩块）

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<Deepslate>());
        Item.width = 16;
        Item.height = 16;
        Item.value = Item.sellPrice(copper: 20);
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

/// <summary>
/// 客户端：内容加载完以后，把游戏自带的花岗岩贴图（方块图集和物品图标）读出来，去色调亮成中等深灰，
/// 换成深板岩自己的贴图。只在内存里做，不存任何文件。
///
/// 为什么不在画的时候乘一个颜色：花岗岩是很艳的蓝紫色，乘色只能压暗，压掉蓝色后几乎是黑的、还带青紫杂点；
/// 用户看过离线预览后选了「先去色、再调亮」这一版（2026-10-01）。
/// </summary>
public sealed class DeepslateArt : ModSystem
{
    private static Asset<Texture2D>? _tile;
    private static Asset<Texture2D>? _item;

    public override void PostSetupContent()
    {
        if (Main.dedServ) return;
        Main.QueueMainThreadAction(() =>
        {
            // 上色只是外观：万一失败，记日志、照用原版花岗岩的样子，不能让整个模组加载失败
            try
            {
                _tile = Recolor($"Images/Tiles_{TileID.Granite}", "DeepslateTile");
                _item = Recolor($"Images/Item_{ItemID.Granite}", "DeepslateItem");
                TextureAssets.Tile[ModContent.TileType<Deepslate>()] = _tile;
                TextureAssets.Item[ModContent.ItemType<DeepslateItem>()] = _item;
            }
            catch (System.Exception e)
            {
                Mod.Logger.Error("[HexCasting] 深板岩上色失败，照用花岗岩的样子：" + e);
            }
        });
    }

    /// <summary>上过色的两张是自己建的（不归资源库管），卸载时自己释放。</summary>
    public override void Unload()
    {
        var tile = _tile;
        var item = _item;
        _tile = null;
        _item = null;
        if (tile is null && item is null) return;
        Main.QueueMainThreadAction(() =>
        {
            tile?.Dispose();
            item?.Dispose();
        });
    }

    /// <summary>
    /// 每个像素取 RGB 里最大的那个当亮度，乘 0.6 再加 18，三个通道都用它（蓝色多 5，带一点冷色）。
    /// 泰拉贴图读出来是预乘过透明度的，先还原再算，存成 PNG 后读回来时泰拉会再预乘一次。
    /// </summary>
    internal static Color DeepslateColor(Color c)
    {
        if (c.A == 0) return Color.Transparent;
        float k = 255f / c.A;
        float v = System.Math.Max(c.R, System.Math.Max(c.G, c.B)) * k * 0.6f + 18f;
        byte gray = (byte)System.Math.Min(255f, v);
        byte blue = (byte)System.Math.Min(255f, v + 5f);
        return new Color(gray, gray, blue, c.A);
    }

    private static Asset<Texture2D> Recolor(string vanillaPath, string name)
    {
        var src = Main.Assets.Request<Texture2D>(vanillaPath, AssetRequestMode.ImmediateLoad).Value;
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        for (int i = 0; i < data.Length; i++) data[i] = DeepslateColor(data[i]);

        using var png = new MemoryStream();
        using (var tex = new Texture2D(Main.graphics.GraphicsDevice, src.Width, src.Height))
        {
            tex.SetData(data);
            tex.SaveAsPng(png, src.Width, src.Height);
        }
        png.Position = 0;
        return Main.Assets.CreateUntracked<Texture2D>(png, name + ".png", AssetRequestMode.ImmediateLoad);
    }
}
