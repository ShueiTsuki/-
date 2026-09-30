using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 促动石（物品形态）的公共部分。对应源项目 `hexcasting:impetus/*`。
///
/// 与原版一致的获取方式：**只有空白促动石能合成**；三种会启动的促动石
/// 要把空白促动石放下，再对站在上面的城镇 NPC 施「脑叶切除」（原版对村民）：
///   工具匠 → 哥布林工匠、制箭师 → 军火商、牧师 → 护士（见 HexCastingTerraria.Load 的配方表）。
/// 这里曾经让配方直接做出「右键促动石」，跳过了整条脑叶切除线。
/// </summary>
public abstract class HexImpetusItemBase : ModItem
{
    protected abstract int TileType { get; }

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 999;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = TileType;
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }
}

/// <summary>工具匠促动石（原版 impetus/rightclick）。类名沿用旧的，已有存档里的物品不会丢。</summary>
public sealed class HexImpetusItem : HexImpetusItemBase
{
    protected override int TileType => ModContent.TileType<HexImpetus>();
}

/// <summary>制箭师促动石（原版 impetus/look）。</summary>
public sealed class HexImpetusLookItem : HexImpetusItemBase
{
    protected override int TileType => ModContent.TileType<HexImpetusLook>();
}

/// <summary>牧师促动石（原版 impetus/redstone）。</summary>
public sealed class HexImpetusRedstoneItem : HexImpetusItemBase
{
    protected override int TileType => ModContent.TileType<HexImpetusRedstone>();
}

/// <summary>空白促动石（原版 impetus/empty）：唯一能合成的一种。</summary>
public sealed class HexImpetusEmptyItem : HexImpetusItemBase
{
    protected override int TileType => ModContent.TileType<HexImpetusEmpty>();

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:390-398，shaped，产物是 IMPETUS_EMPTY：
        //     PSS        P = 紫珀块 ×2      （末地建材）
        //     BAB        S = 板岩块 ×4
        //     SSP        B = 铁栏杆 ×2
        //                A = 充能紫水晶 ×1
        // **阶段：肉后 + 启蒙**（源项目这条挂 enlightenment 门槛）。
        // 紫珀块（末地）用水晶碎块（肉后神圣地）代替；铁栏杆 → 铁栅栏。
        CreateRecipe()
            .AddIngredient(ItemID.CrystalShard, 2)
            .AddIngredient<SlateBlockItem>(4)
            .AddIngredient(ItemID.IronFence, 2)
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.MythrilAnvil)
            .AddCondition(HexConditions.Enlightened)
            .Register();
    }
}
