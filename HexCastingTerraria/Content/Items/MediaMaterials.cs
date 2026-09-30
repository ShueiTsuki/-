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
/// 淬灵晶碎片：30 粉（300,000）。对应源项目 `quenched_allay_shard`。
///
/// 获取途径与原版一致，**没有合成配方**：
/// 对紫水晶粉块旁的**小精灵**（悦灵的对应物，神圣地的肉后敌怪）施「脑叶切除」（大法术，要启蒙）
/// → 方块变成**淬灵块** → 敲掉掉 2~4 片碎片（见 Tiles/QuenchedAllayDrops）。
///
/// 这里曾经：① 脑叶切除配方写成「城镇 NPC 编码的粉妖精」，永远匹配不上；
/// ② 所以另加了一条原版没有的兜底合成（充能紫水晶 ×3 + 妖精尘 ×5 —— 妖精尘是肉后神圣地的敌怪「妖精」掉的，
///    和小动物妖精不是一回事）。①修好后②删除。
///
/// 原版的三条**分解**配方补上：碎片 + 1 份低级媒质 → 等价数量的低级媒质（多给 1 份）。
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
        // 源 HexplatRecipes decompose_quenched_shard/{dust,shard,charged}：shapeless，碎片 + 1 份 → (碎片 / 单位) + 1 份
        Decompose<AmethystDust>(MediaConstants.DustUnit);
        Decompose<AmethystShard>(MediaConstants.ShardUnit);
        Decompose<ChargedAmethyst>(MediaConstants.CrystalUnit);
    }

    private void Decompose<T>(long unit) where T : ModItem
    {
        Recipe.Create(ModContent.ItemType<T>(), (int)(MediaConstants.QuenchedShardUnit / unit) + 1)
            .AddIngredient(Type, 1)
            .AddIngredient<T>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
