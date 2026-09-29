using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 建材族的**配方组**。
///
/// ## 为什么需要它们
///
/// 源项目的建材有两套配方（`HexplatRecipes.java` 605-651）：
///   - `stoneSet`：在**工作台**上按固定比例互转（如「瓦 ×4 ← 砖 ×4」）
///   - `stoneCutterFromTag`：在**切石机**上把**任意同族方块** 1:1 切成任意造型
///
/// 后者的输入是 MC 的 **tag**（`HexTags.Items.SLATE_BLOCKS` 等），不是某一个物品。
/// 泰拉没有切石机，也没有 tag —— 等价物是两者：
///   - 切石机 → **重型工作台**（`TileID.HeavyWorkBench`）
///   - tag → **RecipeGroup**（原版的 `RecipeGroupID.Wood` 就是这么做的）
///
/// 于是「板岩砖 → 板岩柱 1:1」这种只有切石机能做的转换，在泰拉也能做。
/// 少了这层，玩家只能在几个固定比例之间来回换，转造型要经过基底块、还会损耗。
///
/// ## 显示名为什么是中文常量
///
/// `RecipeGroup.Register(key, Func&lt;string&gt; getName, ...)` 这个重载的 `getName`
/// 返回的是**最终显示文本**（另一个重载收的才是本地化 *键*）。这里直接给中文，
/// 免得再引入一层键名约定；键与常量的对应关系由 <c>HexRecipeGroups.*</c> 保证。
/// </summary>
public sealed class HexRecipeGroups : ModSystem
{
    /// <summary>任意板岩：基底块 / 砖 / 小砖 / 瓦 / 柱。</summary>
    public const string SlateBlocks = "HexCastingTerraria:SlateBlocks";

    /// <summary>任意紫晶方块：基底块（紫水晶粉块）/ 砖 / 小砖 / 瓦 / 柱。</summary>
    public const string AmethystBlocks = "HexCastingTerraria:AmethystBlocks";

    /// <summary>任意淬灵方块：基底块 / 砖 / 小砖 / 瓦（源项目这一族没有柱）。</summary>
    public const string QuenchedAllayBlocks = "HexCastingTerraria:QuenchedAllayBlocks";

    /// <summary>任意启迪原木（含 4 种彩色变体）。源项目对应 `HexTags.Items.EDIFIED_LOGS`。</summary>
    public const string EdifiedLogs = "HexCastingTerraria:EdifiedLogs";

    public override void AddRecipeGroups()
    {
        RecipeGroup.Register(SlateBlocks, () => "任意板岩", new[]
        {
            ModContent.ItemType<SlateBlockItem>(),
            ModContent.ItemType<SlateBricksItem>(),
            ModContent.ItemType<SlateBricksSmallItem>(),
            ModContent.ItemType<SlateTilesItem>(),
            ModContent.ItemType<SlatePillarItem>(),
        });

        RecipeGroup.Register(AmethystBlocks, () => "任意紫晶方块", new[]
        {
            ModContent.ItemType<AmethystDustBlockItem>(),
            ModContent.ItemType<AmethystBricksItem>(),
            ModContent.ItemType<AmethystBricksSmallItem>(),
            ModContent.ItemType<AmethystTilesItem>(),
            ModContent.ItemType<AmethystPillarItem>(),
        });

        RecipeGroup.Register(QuenchedAllayBlocks, () => "任意淬灵方块", new[]
        {
            ModContent.ItemType<QuenchedAllayItem>(),
            ModContent.ItemType<QuenchedAllayBricksItem>(),
            ModContent.ItemType<QuenchedAllayBricksSmallItem>(),
            ModContent.ItemType<QuenchedAllayTilesItem>(),
        });

        RecipeGroup.Register(EdifiedLogs, () => "任意启迪原木", new[]
        {
            ModContent.ItemType<EdifiedLogItem>(),
            ModContent.ItemType<EdifiedLogAmethystItem>(),
            ModContent.ItemType<EdifiedLogAventurineItem>(),
            ModContent.ItemType<EdifiedLogCitrineItem>(),
            ModContent.ItemType<EdifiedLogPurpleItem>(),
        });
    }
}
