using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 紫水晶种植盆。**移植版新增，原版没有**（2026-10-01 用户要的）。
///
/// 原版里紫水晶只能靠母岩（`budding_amethyst`）再生，而母岩要拿紫水晶块对村民施脑叶切除才做得出来 ——
/// 是靠后的内容，也是「心智」的产物。用户定：泰拉侧再给一条再生的路，用肉后的魂来做，
/// 因为魂比紫水晶更接近思维与意识，契合咒法学的设定。
///
/// 行为：
///   - 实心 1×1，能站在上面
///   - 只有**正上方**那一格会长晶簇，阶段、每次随机刻的概率都和母岩的某一面相同
///     （<see cref="AmethystLoot.RollPlanterGrowth"/>，合计 1/20），生长本身与母岩共用 <see cref="AmethystGrowth.GrowAt"/>
///   - 收获：用镐挖成熟晶簇，掉落和晶洞完全一样；盆留着，上方从空位重新长。芽阶段挖掉什么都不掉
///   - 挖掉盆本身掉盆（母岩不掉），上方的晶簇随即碎掉，按「没用对工具」掉落（和母岩没了一样）
/// </summary>
public sealed class AmethystPlanter : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;

        // 同 GeodeCore：自动选帧的普通实心方块，贴图是 288×270 图集、每一帧都是同一个盆
        Main.tileFrameImportant[Type] = false;
        Main.tileMergeDirt[Type] = false;

        MinPick = 0;
        MineResist = 1f;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(110, 84, 140), CreateMapEntryName());
    }

    /// <summary>不许锤成斜坡 / 半砖：盆口要平，晶簇才贴得住。</summary>
    public override bool Slope(int i, int j) => false;

    /// <summary>随机刻：按母岩一面的概率，让正上方那一格长一级。</summary>
    public override void RandomUpdate(int i, int j)
    {
        if (!AmethystGrowth.InstantGrowth && !AmethystLoot.RollPlanterGrowth(Main.rand.Next))
        {
            return;
        }

        AmethystGrowth.GrowAt(i, j - 1);
    }
}

/// <summary>
/// 紫水晶种植盆的物品形态。
///
/// 配方（移植版自定，原版没有）：紫水晶粉块 + 充能紫水晶 + 光明之魂 ×5 + 暗影之魂 ×5，秘银砧 / 山铜砧。
///   - 紫水晶粉块：原版做母岩时被脑叶切除的那块紫水晶块（移植版的脑叶切除配方也是粉块 → 母岩）
///   - 光明之魂 + 暗影之魂：代替被切除的村民心智（用户定：肉后的魂，比紫水晶更接近思维与意识）
///   - 充能紫水晶：脑叶切除要花媒质，用一颗充能紫水晶代表
/// 阶段：肉后（两种魂都只在肉后掉），不要求启蒙。
/// </summary>
public sealed class AmethystPlanterItem : ModItem
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
        Item.createTile = ModContent.TileType<AmethystPlanter>();
        Item.rare = ItemRarityID.LightRed;
        Item.value = Item.sellPrice(silver: 30);
    }

    /// <summary>物品图标单独一张（方块贴图是 288×270 图集，见 HexDecoBlockItem.Texture 的说明）。</summary>
    public override string Texture => "HexCastingTerraria/Content/Items/Blocks/AmethystPlanter";

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<AmethystDustBlockItem>(1)
            .AddIngredient<ChargedAmethyst>(1)
            .AddIngredient(ItemID.SoulofLight, 5)
            .AddIngredient(ItemID.SoulofNight, 5)
            .AddTile(TileID.MythrilAnvil)       // 秘银砧 / 山铜砧（肉后）
            .Register();
    }
}
