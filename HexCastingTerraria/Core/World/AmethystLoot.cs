using System;

namespace HexCastingTerraria.Core.World;

/// <summary>晶簇的生长阶段。对应 MC 的四个方块。</summary>
public enum AmethystStage
{
    /// <summary>空位（可以长出新芽）。</summary>
    None = 0,

    /// <summary>小芽。对应 `small_amethyst_bud`。</summary>
    SmallBud = 1,

    /// <summary>中芽。对应 `medium_amethyst_bud`。</summary>
    MediumBud = 2,

    /// <summary>大芽。对应 `large_amethyst_bud`。</summary>
    LargeBud = 3,

    /// <summary>成熟晶簇。对应 `amethyst_cluster`。</summary>
    Cluster = 4,
}

/// <summary>
/// 紫水晶晶洞的生长与掉落规则。
///
/// 移植自 MC 的 `BuddingAmethystBlock.randomTick` 与
/// Hex Casting 注入的掉落表 `hexcasting:inject/amethyst_cluster`。
///
/// **为什么写成纯函数**：掉落表是移植最容易静默走样的地方 ——
/// 「工具合格与否」的落差极大（粉 1~4 vs 0~2、充能晶体 25%~100% vs 12.5%），
/// 而两种分支在游戏里长得一模一样，只有数字不同。
/// 做成纯函数才能用确定性随机源把所有分支钉死。
/// </summary>
public static class AmethystLoot
{
    /// <summary>
    /// 随机刻触发概率的分母。对应 MC 的 `random.nextInt(5) == 0`（即 1/5）。
    /// </summary>
    public const int TickChanceDenominator = 5;

    /// <summary>
    /// 紫水晶碎片的掉落倍率修正。对应源项目 `shardDelta = -0.5`
    /// （`AmethystReducerFunc`：`count * (1 + delta)` = **减半**）。
    /// </summary>
    public const double ShardMultiplier = 0.5;

    /// <summary>
    /// 时运等级对应的充能紫水晶掉率。
    /// 逐条抄自 `inject/amethyst_cluster.json` 的 `table_bonus` 数组。
    /// 索引 = 时运等级（0~4），超出按 4 取。
    /// </summary>
    private static readonly double[] ChargedChances = { 0.25, 0.35, 0.50, 0.75, 1.00 };

    /// <summary>工具**不合格**时充能紫水晶的固定掉率。源项目同为 `0.125`。</summary>
    public const double ChargedChanceWithoutProperTool = 0.125;

    /// <summary>一次收获的结果。</summary>
    public readonly record struct LootResult(int Dust, int ChargedCrystal, int Shards);

    /// <summary>
    /// 本阶段是否掉落东西。
    ///
    /// ⚠️ 只有**成熟晶簇**掉落。三个芽阶段**什么都不掉**
    /// （源项目的掉落注入只挂在 `minecraft:blocks/amethyst_cluster` 上）。
    /// 这是「必须等它长熟」这一玩法的全部意义所在 ——
    /// 提前敲掉就白等一轮生长。
    /// </summary>
    public static bool DropsLoot(AmethystStage stage) => stage == AmethystStage.Cluster;

    /// <summary>下一个生长阶段。已成熟则不再变化。</summary>
    public static AmethystStage NextStage(AmethystStage current) => current switch
    {
        AmethystStage.None => AmethystStage.SmallBud,
        AmethystStage.SmallBud => AmethystStage.MediumBud,
        AmethystStage.MediumBud => AmethystStage.LargeBud,
        AmethystStage.LargeBud => AmethystStage.Cluster,
        _ => AmethystStage.Cluster,
    };

    /// <summary>
    /// 一次随机刻是否生长。对应 MC 的 `random.nextInt(5) == 0`。
    /// </summary>
    /// <param name="nextInt">返回 [0, n) 均匀整数。传 <c>Main.rand.Next</c> 即可。</param>
    public static bool RollGrowth(Func<int, int> nextInt)
    {
        if (nextInt == null) throw new ArgumentNullException(nameof(nextInt));
        return nextInt(TickChanceDenominator) == 0;
    }

    /// <summary>
    /// MC 的 `ore_drops` 附魔加成公式（`ApplyBonusCount.ORE_DROPS`）：
    /// <code>
    ///   i = rand.nextInt(fortune + 2) - 1
    ///   if (i &lt; 0) i = 0
    ///   newCount = originalCount * (i + 1)
    /// </code>
    /// 时运 0 时原样返回。
    /// </summary>
    public static int ApplyOreDrops(int originalCount, int fortuneLevel, Func<int, int> nextInt)
    {
        if (nextInt == null) throw new ArgumentNullException(nameof(nextInt));
        if (fortuneLevel <= 0) return originalCount;

        int i = nextInt(fortuneLevel + 2) - 1;
        if (i < 0) i = 0;
        return originalCount * (i + 1);
    }

    /// <summary>时运等级对应的充能紫水晶掉率。</summary>
    public static double ChargedChance(int fortuneLevel)
    {
        if (fortuneLevel < 0) fortuneLevel = 0;
        if (fortuneLevel >= ChargedChances.Length) fortuneLevel = ChargedChances.Length - 1;
        return ChargedChances[fortuneLevel];
    }

    /// <summary>
    /// 掷一次成熟晶簇的掉落。四个池逐条对应 `inject/amethyst_cluster.json`。
    /// </summary>
    /// <param name="properTool">
    /// 工具是否「合格」（源项目的 `cluster_max_harvestables` 标签）。
    /// 泰拉侧映射为**任意镐类工具**；空手或非镐为不合格。
    /// </param>
    /// <param name="fortuneLevel">时运等级 0~4（泰拉侧由镐力分级映射）。</param>
    /// <param name="nextInt">返回 [0, n) 均匀整数。</param>
    /// <param name="chance">概率判定：给定概率返回是否命中。</param>
    /// <param name="vanillaShardBase">
    /// MC 原生紫水晶碎片的基础掉落数（时运加成前）。泰拉侧对应「紫水晶碎片」物品。
    /// </param>
    public static LootResult RollCluster(
        bool properTool,
        int fortuneLevel,
        Func<int, int> nextInt,
        Func<double, bool> chance,
        int vanillaShardBase = 1)
    {
        if (nextInt == null) throw new ArgumentNullException(nameof(nextInt));
        if (chance == null) throw new ArgumentNullException(nameof(chance));

        int dust;
        int charged = 0;

        if (properTool)
        {
            // 池①：粉，均匀 1~4（含两端），再叠 ore_drops 时运
            int baseDust = 1 + nextInt(4);
            dust = ApplyOreDrops(baseDust, fortuneLevel, nextInt);

            // 池③：充能紫水晶，时运查表
            if (chance(ChargedChance(fortuneLevel))) charged = 1;
        }
        else
        {
            // 池②：粉，均匀 0~2（含两端），**无时运加成**
            dust = nextInt(3);

            // 池④：充能紫水晶，固定 12.5%
            if (chance(ChargedChanceWithoutProperTool)) charged = 1;
        }

        // MC 原生碎片，经源项目 AmethystReducerFunc 减半
        int shards = (int)(ApplyOreDrops(vanillaShardBase, fortuneLevel, nextInt) * ShardMultiplier);

        return new LootResult(dust, charged, shards);
    }

    /// <summary>
    /// 泰拉镐力 → 时运等级的映射。
    ///
    /// 泰拉**没有时运附魔**，所以用镐力分级来保留
    /// 「工具越好、充能紫水晶越多」这一进度感。
    /// 分档按泰拉自身的进度节点切，而不是照搬 MC 的附魔等级。
    /// </summary>
    public static int FortuneFromPickaxePower(int pickaxePower)
    {
        if (pickaxePower >= 200) return 4;   // 幽灵镐 / 日耀及以上
        if (pickaxePower >= 180) return 3;   // 精金 / 钛金
        if (pickaxePower >= 150) return 2;   // 秘银 / 山铜
        if (pickaxePower >= 100) return 1;   // 炎狱 / 钴 / 钯金
        return 0;                            // 铜 / 铁 / 银 / 金 / 铂金
    }
}
