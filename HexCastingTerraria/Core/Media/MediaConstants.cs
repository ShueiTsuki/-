namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 媒质单位换算。移植自 at.petrak.hexcasting.api.misc.MediaConstants（数值逐项对齐）。
///
/// 原设定：1 紫水晶粉 = 10000 媒质。
/// Terraria 侧的资源对应关系在 <see cref="MediaConstants.ResourceMapping"/> 里说明。
/// </summary>
public static class MediaConstants
{
    /// <summary>一粒粉尘所含媒质。</summary>
    public const long DustUnit = 10000;

    /// <summary>碎晶：5 粉。</summary>
    public const long ShardUnit = 5 * DustUnit;

    /// <summary>晶体：10 粉。</summary>
    public const long CrystalUnit = 10 * DustUnit;

    /// <summary>淬灵碎晶：3 晶体。</summary>
    public const long QuenchedShardUnit = 3 * CrystalUnit;

    /// <summary>淬灵方块：4 淬灵碎晶。</summary>
    public const long QuenchedBlockUnit = 4 * QuenchedShardUnit;

    /// <summary>
    /// 泰拉瑞亚资源对应方案（决策点 D6）。
    ///
    /// MC 原版有而 Terraria 没有的东西：
    ///   - 紫水晶簇 / 紫水晶粉  → Terraria 有「紫晶 Amethyst」（宝石），语义最接近
    ///   - 充能紫水晶          → 泰拉无对应，用「紫晶 + 坠落之星」合成，或直接以紫晶替代
    ///   - 淬灵合金(Quenched Allay) → 泰拉无对应，属原作后期材料，长期再设计
    ///
    /// 暂定对应（实现时逐条可调）：
    ///   1 紫水晶粉   ↔ 1 紫晶(Amethyst) 的 1/10 概念价值
    ///   1 充能紫水晶 ↔ 1 紫晶(Amethyst)
    ///   1 淬灵碎晶   ↔ 3 紫晶
    /// 具体物品实现见 Content/Items。
    /// </summary>
    public static class ResourceMapping
    {
        public const long PerAmethystItem = CrystalUnit;
    }


    /// <summary>把媒质量格式化成人类可读文本（用于 HUD 与物品提示）。</summary>
    public static string Format(long media)
    {
        if (media >= QuenchedBlockUnit)
        {
            return $"{media / (double)QuenchedBlockUnit:0.##} 淬灵块";
        }
        if (media >= QuenchedShardUnit)
        {
            return $"{media / (double)QuenchedShardUnit:0.##} 淬灵碎晶";
        }
        if (media >= CrystalUnit)
        {
            return $"{media / (double)CrystalUnit:0.##} 晶体";
        }
        if (media >= DustUnit)
        {
            return $"{media / (double)DustUnit:0.##} 粉";
        }
        return media.ToString();
    }
}
