using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 贴在墙上的装饰/光源方块的公共实现。
///
/// 对应源项目里那一批「挂在墙上」的装饰：
///   `scroll_paper` / `ancient_scroll_paper` / `scroll_paper_lantern` /
///   `ancient_scroll_paper_lantern` / `amethyst_sconce`
///
/// 泰拉侧的对应物就是**壁挂装饰**（原版火把、壁灯那一类）：
/// 用 `AnchorWall` 贴在墙上、1x1、可发光。这一类比建材简单得多，
/// 因为它不参与方块连通与合并。
/// </summary>
public abstract class WallDeco : ModTile
{
    /// <summary>小地图颜色。</summary>
    protected abstract Color MapColor { get; }

    /// <summary>是否发光。灯笼与壁灯为真。</summary>
    protected virtual bool EmitsLight => false;

    /// <summary>发光颜色（仅 <see cref="EmitsLight"/> 为真时有意义）。</summary>
    protected virtual (float R, float G, float B) LightColor => (0f, 0f, 0f);

    /// <summary>挖掘所需镐力。</summary>
    protected virtual int RequiredPick => 0;

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = EmitsLight;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;

        DustType = DustID.WoodFurniture;
        HitSound = SoundID.Grass;
        MinPick = RequiredPick;
        AddMapEntry(MapColor);

        // 壁挂：只需要背后有墙。把底部锚点清掉 —— 不清的话它会要求脚下有实心块，
        // 那就变成「放在地上的装饰」而不是「挂在墙上的」了。
        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorWall = true;
        TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
        TileObjectData.addTile(Type);
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        if (!EmitsLight) return;

        var (lr, lg, lb) = LightColor;
        r = lr;
        g = lg;
        b = lb;
    }

    public override void NumDust(int i, int j, bool fail, ref int num) => num = fail ? 1 : 3;
}

/// <summary>卷轴纸。对应源项目 `hexcasting:scroll_paper`。纯装饰。</summary>
public sealed class ScrollPaper : WallDeco
{
    protected override Color MapColor => new(226, 214, 186);
}

/// <summary>古卷轴纸。对应源项目 `hexcasting:ancient_scroll_paper`。</summary>
public sealed class AncientScrollPaper : WallDeco
{
    protected override Color MapColor => new(206, 196, 158);
}

/// <summary>卷轴纸灯笼。对应源项目 `hexcasting:scroll_paper_lantern`，会发光。</summary>
public sealed class ScrollPaperLantern : WallDeco
{
    protected override Color MapColor => new(240, 226, 186);
    protected override bool EmitsLight => true;
    protected override (float, float, float) LightColor => (0.62f, 0.56f, 0.40f);
}

/// <summary>古卷轴纸灯笼。对应源项目 `hexcasting:ancient_scroll_paper_lantern`。</summary>
public sealed class AncientScrollPaperLantern : WallDeco
{
    protected override Color MapColor => new(220, 208, 160);
    protected override bool EmitsLight => true;
    protected override (float, float, float) LightColor => (0.56f, 0.52f, 0.34f);
}

/// <summary>紫晶壁灯。对应源项目 `hexcasting:amethyst_sconce`。紫色光源。</summary>
public sealed class AmethystSconce : WallDeco
{
    protected override Color MapColor => new(168, 132, 214);
    protected override bool EmitsLight => true;
    protected override (float, float, float) LightColor => (0.58f, 0.40f, 0.82f);
    protected override int RequiredPick => 0;
}

/// <summary>
/// 阿卡夏桥接块。对应源项目 `hexcasting:akashic_connector`。
///
/// 图书馆里只传导、不存东西的方块：把书架连得更远（见 <see cref="Core.Casting.Akashic.AkashicLibrary"/>）。
/// 也是脑叶切除的原料：放在世界里的桥接块 + 巫师（原版 5 级图书管理员）→ 阿卡夏记录。
/// 原版亮度 4。阿卡夏书架在 AkashicBookshelf.cs。
/// </summary>
public sealed class AkashicLigature : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = true;
        Main.tileMergeDirt[Type] = false;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Dig;
        AddMapEntry(new Color(150, 120, 196));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        r = 0.16f;
        g = 0.12f;
        b = 0.24f;
    }
}
