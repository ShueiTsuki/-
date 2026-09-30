using System.Collections.Generic;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// 一条「脑叶切除」配方。移植自源项目 `common/recipe/BrainsweepRecipe.java`。
///
/// 一条配方说的是：**在某种方块上，对某种生物下手，能得到什么**。
/// 原版把它做成了数据包配方（可以加模组配方），泰拉侧做成一张表 ——
/// 配方本身不变，只是注入方式不同（泰拉没有数据包）。
/// </summary>
/// <param name="SourceTile">脚下必须是什么方块（泰拉的图格 ID）。</param>
/// <param name="TargetSpecies">
/// 目标生物的「种类编号」。`-1` = 任何可切除的生物。
/// 泰拉侧用 NPC.netID；城镇 NPC 用负数编码（见 <see cref="BrainsweepRules.TownNpcSpeciesBase"/>）。
/// </param>
/// <param name="ResultTile">产出的方块（-1 = 不换方块）。</param>
/// <param name="ResultItem">产出的物品（-1 = 不掉物品）。</param>
/// <param name="MediaCost">媒质消耗。源项目写在配方里，不是写死在图案里。</param>
public readonly record struct BrainsweepRecipe(
    int SourceTile,
    int TargetSpecies,
    int ResultTile,
    int ResultItem,
    long MediaCost);

/// <summary>
/// 脑叶切除配方表。
///
/// ## 为什么配方在 Core 而不是 Content
///
/// 「哪条配方匹配」是**纯逻辑**（三个整数比较），把它放进 Core 才能离线测试；
/// 具体是哪些图格 ID 是**游戏数据**，由 <c>Mod.Load()</c> 注入。
/// 这样配方的**匹配与代价**能被 400+ 条离线用例覆盖，而内容变动不需要改测试。
///
/// ## 与源项目的内容差异（必须说清楚）
///
/// 原版 8 条配方全部实现：
///
/// | 原版 | 泰拉侧 |
/// |---|---|
/// | 紫水晶块 + 村民 → 母岩 | 紫水晶粉块 + 任意城镇 NPC → 母岩 |
/// | 空导向石 + 石匠 → 石匠导向石 | 空导向石 + 爆破专家 |
/// | 空导向石 + 牧羊人 → 牧羊人导向石 | 空导向石 + 染料商 |
/// | 阿卡夏系带 + 图书管理员 → 记录方块 | 紫水晶粉块 + 巫师（没有「系带」这个前置方块） |
/// | 紫水晶块 + 悦灵 → 淬灵悦灵 | 紫水晶粉块 + 妖精 → 淬灵晶碎片（泰拉的妖精 = 悦灵） |
/// | 空白促动石 + 工具匠 → 工具匠促动石 | 空白促动石 + 哥布林工匠 |
/// | 空白促动石 + 制箭师 → 制箭师促动石 | 空白促动石 + 军火商 |
/// | 空白促动石 + 牧师 → 牧师促动石 | 空白促动石 + 护士 |
/// </summary>
public static class BrainsweepRules
{
    /// <summary>
    /// 城镇 NPC 的「种类编号」基址。泰拉的 `npc.netID` 是正数，
    /// 城镇 NPC 用 <c>TownNpcSpeciesBase + npcType</c> 编码，避免与普通怪物撞号。
    /// </summary>
    public const int TownNpcSpeciesBase = -1_000_000;

    /// <summary>「任何种类都行」。</summary>
    public const int AnySpecies = -1;

    private static readonly List<BrainsweepRecipe> Table = new();

    /// <summary>当前配方表（只读）。</summary>
    public static IReadOnlyList<BrainsweepRecipe> Recipes => Table;

    /// <summary>由 <c>Mod.Load()</c> 注入具体配方。</summary>
    public static void Configure(IEnumerable<BrainsweepRecipe> recipes)
    {
        Table.Clear();
        Table.AddRange(recipes);
    }

    /// <summary>把泰拉的城镇 NPC 类型编号编码成「种类编号」。</summary>
    public static int TownNpcSpecies(int npcType) => TownNpcSpeciesBase - npcType;

    /// <summary>
    /// 找一条匹配的配方。
    ///
    /// 匹配规则**有序**：先找精确指定种类的，再找 `AnySpecies` 的。
    /// 顺序不能反 —— 否则「任意城镇 NPC → 母岩」会把「巫师 → 记录方块」吃掉，
    /// 表现成「巫师上去也只出母岩」，而且不报错。
    /// </summary>
    public static bool TryFind(int tileType, int species, out BrainsweepRecipe recipe)
    {
        foreach (var r in Table)
        {
            if (r.SourceTile != tileType) continue;
            if (r.TargetSpecies == species)
            {
                recipe = r;
                return true;
            }
        }

        foreach (var r in Table)
        {
            if (r.SourceTile != tileType) continue;
            if (r.TargetSpecies == AnySpecies)
            {
                recipe = r;
                return true;
            }
        }

        recipe = default;
        return false;
    }
}
