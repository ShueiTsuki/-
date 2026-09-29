namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 过载（用生命换媒质）与「启蒙」的判定，照原版 PlayerBasedCastEnv.extractMediaFromInventory
/// 与 HexAdvancements.ENLIGHTEN。
///
/// 原版：mediaToHealthRate = 2 × 充能紫水晶 / 20，MC 满血 20 点 → **满血 = 2 个充能紫水晶的媒质**。
/// 泰拉的生命上限是 100~500 且会成长，所以按**生命比例**换算：不论上限多少，满血都等于 2 个充能紫水晶。
/// （旧实现写死「1 点生命 = 1 个充能紫水晶」，100 血就是 100 个，比原版宽松 50 倍。）
/// </summary>
public static class Overcast
{
    /// <summary>满血能换到的媒质（原版 20 × mediaToHealthRate）。</summary>
    public const double FullHealthMedia = 2.0 * MediaConstants.CrystalUnit;

    public static double MediaPerHealth(int maxLife) => FullHealthMedia / System.Math.Max(1, maxLife);

    /// <summary>
    /// 缺口 <paramref name="shortfall"/> 需要扣多少生命、实际能换到多少媒质。
    /// 原版至少扣 0.5 点（MC 满血 20 的 2.5%）；扣到的生命不超过当前生命。
    /// <c>Lethal</c> = 这一下会把施法者耗死（原版允许 —— 过载本来就是拿命换）。
    /// </summary>
    public static (int Damage, long MediaGained, bool Lethal) Plan(long shortfall, int life, int maxLife)
    {
        double rate = MediaPerHealth(maxLife);
        double hp = System.Math.Max(shortfall / rate, maxLife * 0.025);
        int damage = (int)System.Math.Ceiling(hp - 1e-9);
        int taken = System.Math.Min(damage, System.Math.Max(0, life));
        long gained = (long)System.Math.Ceiling(taken * rate - 1e-6);
        return (damage, gained, damage >= life);
    }

    /// <summary>
    /// 原版 ENLIGHTEN：一次过载用掉 ≥ 80% 最大生命，且剩下的生命在 (0, 半颗心]（MC 的 1 点 = 满血的 1/20）。
    /// 耗死了不算 —— 必须活着撑过这一下。
    /// </summary>
    public static bool IsEnlightening(int healthUsed, int maxLife, int lifeLeft)
        => healthUsed >= 0.8 * maxLife && lifeLeft > 0 && lifeLeft <= System.Math.Max(1.0, maxLife / 20.0);
}
