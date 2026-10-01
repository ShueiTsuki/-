using System;

namespace HexCastingTerraria.Core.World;

/// <summary>
/// 淬灵晶块的掉落（源项目 datagen/HexLootTables.java 的 quenchedPool），纯逻辑，离线可测。
///
/// 原版：精准采集掉方块本身（移植版是「共振」前缀的镐子，见 Content/Prefixes/Resonant.cs）；
/// 否则掉淬灵晶碎片：先均匀 2~4 片，再按时运等级的概率多掉 1 片（bonusLevelFlatChance 0.25 / 0.5 / 0.75 / 1.0，
/// 等级超出表就按最后一档）。时运由镐力换算，同晶簇（<see cref="AmethystLoot.FortuneFromPickaxePower"/>）。
/// 这里曾经固定 2~4 片、没有多掉的那一片。
/// </summary>
public static class QuenchedLoot
{
    public const int MinShards = 2;
    public const int MaxShards = 4;

    private static readonly double[] BonusChances = { 0.25, 0.5, 0.75, 1.0 };

    /// <summary>这一档时运多掉 1 片的概率。</summary>
    public static double BonusChance(int fortuneLevel)
        => BonusChances[Math.Clamp(fortuneLevel, 0, BonusChances.Length - 1)];

    /// <param name="nextInt">返回 [0, n) 均匀整数。</param>
    /// <param name="chance">概率判定：给定概率返回是否命中。</param>
    public static int RollShards(int fortuneLevel, Func<int, int> nextInt, Func<double, bool> chance)
    {
        if (nextInt == null) throw new ArgumentNullException(nameof(nextInt));
        if (chance == null) throw new ArgumentNullException(nameof(chance));
        int shards = MinShards + nextInt(MaxShards - MinShards + 1);
        if (chance(BonusChance(fortuneLevel))) shards++;
        return shards;
    }
}
