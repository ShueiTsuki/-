using Terraria;

namespace HexCastingTerraria.Content.Items;

/// <summary>本模组的配方条件。</summary>
public static class HexConditions
{
    /// <summary>
    /// 已启蒙（原版进度 enlightenment「获得启迪」：一次过载用掉 ≥80% 生命、只剩不到半颗心）。
    /// 原版里法术环（原动力 / 导线）与阿卡夏图书馆的门槛就是它；泰拉侧再叠加「肉后」的合成站。
    /// </summary>
    public static readonly Condition Enlightened = new(
        "Mods.HexCastingTerraria.Conditions.Enlightened",
        () => Main.LocalPlayer is { active: true } p && (HexPlayer.Get(p).Enlightened || Config.HexClientConfig.Instance.AlwaysEnlightened));
}
