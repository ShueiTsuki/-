namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 媒质单位换算。移植自 at.petrak.hexcasting.api.misc.MediaConstants（数值逐项对齐）。
///
/// 原设定：1 紫水晶粉 = 10000 媒质。
///
/// 媒质物品从哪来照原版：粉、碎片、充能紫水晶都只能挖紫水晶簇得到（另有粉块拆回粉、淬灵碎片拆分），
/// 没有手搓的配方 —— 泰拉的紫晶（宝石）不是媒质。移植版只多一条：水晶球旁 10 粉换 1 个充能紫水晶
///（2026-10-01 用户定：咒法师自己捏不出充能紫水晶，要靠肉后的水晶球）。见 Content/Items/MediaMaterials.cs。
/// </summary>
public static class MediaConstants
{
    /// <summary>一粒粉尘所含媒质。</summary>
    public const long DustUnit = 10000;

    /// <summary>碎晶：5 粉。</summary>
    public const long ShardUnit = 5 * DustUnit;

    /// <summary>晶体：10 粉。</summary>
    public const long CrystalUnit = 10 * DustUnit;

    /// <summary>淬灵晶碎片：3 个充能紫水晶。</summary>
    public const long QuenchedShardUnit = 3 * CrystalUnit;

    /// <summary>淬灵晶块：4 片淬灵晶碎片。</summary>
    public const long QuenchedBlockUnit = 4 * QuenchedShardUnit;


    /// <summary>把媒质量格式化成人类可读文本（用于 HUD 与物品提示）。</summary>
    public static string Format(long media)
    {
        if (media >= QuenchedBlockUnit)
        {
            return $"{media / (double)QuenchedBlockUnit:0.##} 淬灵晶块";
        }
        if (media >= QuenchedShardUnit)
        {
            return $"{media / (double)QuenchedShardUnit:0.##} 淬灵晶碎片";
        }
        if (media >= CrystalUnit)
        {
            return $"{media / (double)CrystalUnit:0.##} 充能紫水晶";
        }
        if (media >= DustUnit)
        {
            return $"{media / (double)DustUnit:0.##} 紫水晶粉";
        }
        return media.ToString();
    }
}
