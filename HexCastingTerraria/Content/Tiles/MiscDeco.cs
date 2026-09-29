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
/// 阿卡夏书架。对应源项目 `hexcasting:akashic_bookshelf`。
///
/// 源项目里它是「能存阿卡夏记录的书架」（挨着记录方块会一起参与存储）。
/// 泰拉侧的记录方块已经有完整的键值存储，书架目前只承担**装饰**功能 ——
/// 这一点在计划里记为「与源项目的功能差异」，不是遗漏。
/// </summary>
public sealed class AkashicBookshelf : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileMergeDirt[Type] = false;

        MinPick = 0;
        DustType = DustID.WoodFurniture;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(96, 74, 108));
    }
}

/// <summary>
/// 阿卡夏系带。对应源项目 `hexcasting:akashic_connector`。
///
/// 它是**脑叶切除的原料**：`阿卡夏系带 + 巫师 → 阿卡夏记录方块`。
/// 泰拉侧因为「粉块」更贴近我们的晶洞体系，配方改用了粉块当原料，
/// 但这个方块本身作为装饰保留 —— 同时也是「将来要接成配方原料」的占位。
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
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(150, 120, 196));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        r = 0.16f;
        g = 0.12f;
        b = 0.24f;
    }
}
