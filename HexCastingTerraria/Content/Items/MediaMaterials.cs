using HexCastingTerraria.Client;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 媒质材料物品的基类（源项目 CCMediaHolder.Static）。
///
/// 它们既是**合成材料**，也是**媒质来源**：施法时直接从背包里按整件扣
///（优先级：粉 3000 &gt; 碎片 2000 &gt; 充能紫水晶 1000 &gt; 淬灵碎片 900，见 <see cref="MediaPriority"/>）。
/// 原版不能「右键使用」—— 这里曾经能右键把媒质倒进一个「玩家媒质池」，原版没有那个池子，已删除。
///
/// 数值严格对齐 <see cref="MediaConstants"/>，与源项目 `HexItems` 一致。
/// </summary>
public abstract class MediaMaterial : ModItem
{
    /// <summary>单件蕴含的媒质量。</summary>
    public abstract long MediaValue { get; }

    /// <summary>扣费优先级（源项目 ADMediaHolder.*_PRIORITY）。</summary>
    public abstract int Priority { get; }

    /// <summary>是否使用源项目的「可堆叠上限」（粉/碎晶 64，晶体/淬灵 16）。</summary>
    public virtual int StackSize => 64;

    public override void SetStaticDefaults()
    {
        // 这几个物品在泰拉里属于「材料」，且带有紫色调 —— 用 LightPurple/Purple 档
    }

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = StackSize;
        Item.value = Item.sellPrice(copper: (int)System.Math.Max(1, MediaValue / 1_000));
        Item.rare = RarityFor(MediaValue);
    }

    /// <summary>稀有度按媒质值分档，和「越珍贵的材料越显眼」一致。</summary>
    protected static int RarityFor(long media)
    {
        if (media >= MediaConstants.QuenchedShardUnit) return ItemRarityID.Pink;
        if (media >= MediaConstants.CrystalUnit) return ItemRarityID.LightPurple;
        if (media >= MediaConstants.ShardUnit) return ItemRarityID.Blue;
        return ItemRarityID.White;
    }
}

/// <summary>
/// 紫水晶粉：媒质的基本单位（10,000）。
/// 对应源项目 `hexcasting:amethyst_dust`。
///
/// 泰拉侧由**紫晶（Amethyst 宝石）**研磨而来 —— 泰拉地下天然产出紫晶，
/// 语义上就是原版「晶洞里的紫水晶」的对应物（见 <see cref="MediaConstants.ResourceMapping"/>）。
/// </summary>
public sealed class AmethystDust : MediaMaterial
{
    public override int Priority => MediaPriority.AmethystDust;

    public override long MediaValue => MediaConstants.DustUnit;

    public override void AddRecipes()
    {
        // 1 紫晶 → 10 粉（= CrystalUnit，与 ResourceMapping.PerAmethystItem 自洽）
        CreateRecipe(10)
            .AddIngredient(ItemID.Amethyst, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 紫水晶碎片：5 粉（50,000）。
/// 对应源项目 `amethyst_shard`（MC 原生物品，Hex 只把它当媒质容器用）。
///
/// 泰拉没有对应物，由粉压制而成 —— 与原版「碎片是更原始形态」的方向相反，
/// 但数值关系一致；等 M-2/M-3 的晶洞做出来后会改成从晶簇掉落。
/// </summary>
public sealed class AmethystShard : MediaMaterial
{
    public override int Priority => MediaPriority.AmethystShard;

    public override long MediaValue => MediaConstants.ShardUnit;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Green;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<AmethystDust>(5)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 反向解包：碎片拆回粉（不亏不赚，方便小额使用）
        CreateRecipe(5)
            .AddIngredient<AmethystShard>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 充能紫水晶：10 粉（100,000）。
/// 对应源项目 `charged_amethyst`。
///
/// 原版只能从晶洞里的**紫水晶簇**小概率掉落（工具合格 + 时运时 25%~100%）。
/// 在世界生成（M-2/M-3）做出来之前，先给一条合成路径，
/// 否则模组在没有晶洞的世界里无法推进。M-3 完成后会**保留**这条配方作为保底。
/// </summary>
public sealed class ChargedAmethyst : MediaMaterial
{
    public override int Priority => MediaPriority.ChargedAmethyst;

    public override long MediaValue => MediaConstants.CrystalUnit;

    public override int StackSize => 16;

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<AmethystDust>(10)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 直接用紫晶合成：与 ResourceMapping.PerAmethystItem = CrystalUnit 自洽
        CreateRecipe()
            .AddIngredient(ItemID.Amethyst, 1)
            .AddIngredient(ItemID.FallenStar, 1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 淬灵晶碎片：30 粉（300,000）。
/// 对应源项目 `quenched_allay_shard`。
///
/// ## 获取途径（**不是**「暂无对应物」）
///
/// 原版没有合成配方 —— 它是「脑叶切除一只**悦灵**（allay）」的产物。
/// 泰拉侧的悦灵对应物是**神圣地的妖精**（`NPCID.FairyCritterPink`），
/// 这条路径**已经实现**：见 `HexCastingTerraria.ConfigureBrainsweepRecipes` 里的
/// 「紫水晶粉块 + 妖精 → 淬灵晶碎片」。
///
/// 之前这段注释写着"泰拉侧暂无对应物"，而那条脑叶切除配方一直都在代码里 ——
/// 典型「注释比代码旧」：读注释的人会以为功能没做，然后去找别的路。
///
/// ## 为什么还留一条合成配方
///
/// 原版靠「启蒙」把这条途径锁在后期，而泰拉的启蒙是用图案门槛近似的，
/// 玩家可能长期到不了；完全没有兜底的话整个淬灵系（碎片 / 淬灵晶法杖 /
/// 剖念法杖）会变成死内容。所以保留一条**材料合成**兜底，
/// 材料用**妖精尘**——同样出自神圣地的精灵，来源与原版一致。
///
/// ⚠️ 这里原来还有第二条配方：`1 碎片 + 夜魂×5 + 水晶碎块×10 → 1 碎片`，
/// 是**净亏**（消耗一个碎片只换回一个）。已删除。
/// </summary>
public sealed class QuenchedAllayShard : MediaMaterial
{
    public override int Priority => MediaPriority.QuenchedShard;

    public override long MediaValue => MediaConstants.QuenchedShardUnit;

    public override int StackSize => 16;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Pink;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<ChargedAmethyst>(3)
            .AddIngredient(ItemID.PixieDust, 5)      // 妖精尘：神圣地精灵掉落
            .AddTile(TileID.MythrilAnvil)
            .Register();
    }
}
