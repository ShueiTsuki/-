using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 卷轴。移植自源项目 `common/items/storage/ItemScroll.java`。
///
/// 它们的共同点：
///   - 存**一个图案**（`canWrite` 只接受 `PatternIota`，空也可以）——
///     所以墙上挂的卷轴不会突然变成「存了一个数字的卷轴」
///   - 可以被 `read_into_parens` 读出，于是能当作「随身携带的一条咒术」
///
/// 三种尺寸的差别在源项目里是**壁挂展示的宽度**（`blockSize` 1/2/3），
/// 而不是存储容量 —— 这一点很容易误以为是「容量大小」。
///
/// ⚠️ 待补：壁挂展示（源项目的 `EntityWallScroll`）。
/// 泰拉侧需要可放置的多格方块或 TileEntity，属独立的一块工作。
/// 在那之前这三个物品是**纯数据载体**，功能完整但墙上挂不了。
/// </summary>
public abstract class ItemScroll : ItemIotaStorage
{
    /// <summary>壁挂展示宽度（图格）。对应源项目的 `blockSize`。</summary>
    public abstract int BlockSize { get; }

    /// <summary>卷轴**可以**写（原版 `writeable` 返回 true），但只能写图案。</summary>
    public override StorageKind StorageKind => StorageKind.PatternOnly;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.width = 20;
        Item.height = 20;
    }

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
    {
        base.ModifyTooltips(tooltips);
        tooltips.Add(new TooltipLine(Mod, "HexScrollHint", $"只能存图案（壁挂宽度 {BlockSize} 格）"));
        if (Read() is Core.Casting.Iotas.PatternIota)
        {
            // 占几行空白，下面在这块地方画出图案（源项目的卷轴提示框里就是一张带笔顺的图）
            tooltips.Add(new TooltipLine(Mod, "HexScrollPattern", "　\n　\n　\n　"));
        }
    }

    /// <summary>提示框里画出卷轴上的图案：红点是起笔处，箭头是第一笔的方向（大法术的笔顺就靠这个学）。</summary>
    public override void PostDrawTooltipLine(DrawableTooltipLine line)
    {
        if (line.Name != "HexScrollPattern" || Read() is not Core.Casting.Iotas.PatternIota p) return;
        var sb = Main.spriteBatch;
        var center = new Microsoft.Xna.Framework.Vector2(line.X + 60, line.Y + 44);
        Client.UI.PatternRenderer.DrawStaticPreview(
            (a, b, w, c) => Client.HexPixel.DrawLine(sb, a, b, w, c),
            (pt, r, c) => Client.HexPixel.DrawDot(sb, pt, r, c),
            p.Pattern, center, 34f, new Microsoft.Xna.Framework.Color(200, 170, 255));
    }
}

/// <summary>小型卷轴。对应源项目 `scroll_small`（blockSize 1）。</summary>
public sealed class ScrollSmall : ItemScroll
{
    public override int BlockSize => 1;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 20);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:215-220，shaped " A"/"P "：纸 ×1 + 紫水晶粉 ×1。
        // 泰拉没有纸，用**丝绸**代替（同样是「片状可合成材料」，而且泰拉的书也用丝）。
        CreateRecipe()
            .AddIngredient(ItemID.Silk, 1)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>中型卷轴。对应源项目 `scroll_medium`（blockSize 2）。</summary>
public sealed class ScrollMedium : ItemScroll
{
    public override int BlockSize => 2;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(silver: 60);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:222-228，shaped "  A"/"PP "/"PP "：纸 ×4 + 粉 ×1。
        //
        // ⚠️ 三种卷轴在源项目里是**并列**的（各自独立配方、只差纸的用量），
        // 不是「小卷轴 ×2 → 中卷轴」这种**链式**。之前写成了链式，
        // 结果中/大卷轴的**材料成本比原版高一倍**（还要多搭一个小卷轴），
        // 而且和「尺寸只影响壁挂宽度、不影响容量」这个设定自相矛盾 ——
        // 既然容量一样，凭什么大的要用两个小的换。
        CreateRecipe()
            .AddIngredient(ItemID.Silk, 4)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>大型卷轴。对应源项目 `scroll`（blockSize 3）。</summary>
public sealed class ScrollLarge : ItemScroll
{
    public override int BlockSize => 3;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:230-236，shaped "PPA"/"PPP"/"PPP"：纸 ×8 + 粉 ×1。
        CreateRecipe()
            .AddIngredient(ItemID.Silk, 8)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
