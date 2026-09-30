using System;

namespace HexCastingTerraria.Core.Casting;

// 注意：本命名空间下存在 HexCastingTerraria.Core.Casting.Math，
// 因此所有 BCL 数学函数必须写 System.Math（踩过多次的坑）。

/// <summary>
/// 数值安全工具。
///
/// 对应源项目 api/utils/HexUtils.kt 里的 fixNAN 等函数。
///
/// 注意：为什么必须做 NaN 清洗（见 TERRARIA_2D_ADAPTATION.md 高危点③）：
/// 泰拉的 NPC **没有持久朝向**（只有 int direction ±1），
/// 如果拿 velocity 当视线方向，静止实体的速度是零向量 →
/// Normalize 得到 NaN → 之后所有比较、范围检查、isTruthy 全部静默失败
/// （不抛异常、不打日志，极难定位）。
/// </summary>
public static class HexMathUtil
{
    /// <summary>把 NaN / 无穷大替换为 0。对应源项目 HexUtils.fixNAN。</summary>
    public static double FixNaN(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;

    public static float FixNaN(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

    /// <summary>
    /// 安全归一化：长度过小或结果非法时返回 <paramref name="fallback"/>。
    /// 所有归一化都必须走这里，绝不要直接除长度。
    /// </summary>
    public static (double X, double Y) SafeNormalize(double x, double y, double fallbackX = 1.0, double fallbackY = 0.0)
    {
        double lenSq = x * x + y * y;
        if (lenSq < 1e-12 || double.IsNaN(lenSq) || double.IsInfinity(lenSq))
        {
            return (fallbackX, fallbackY);
        }

        double len = System.Math.Sqrt(lenSq);
        double nx = x / len;
        double ny = y / len;

        if (double.IsNaN(nx) || double.IsNaN(ny) || double.IsInfinity(nx) || double.IsInfinity(ny))
        {
            return (fallbackX, fallbackY);
        }

        return (nx, ny);
    }

    /// <summary>两点距离。</summary>
    public static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>向量长度。</summary>
    public static double Length(double x, double y) => System.Math.Sqrt(x * x + y * y);

    /// <summary>向量长度平方。</summary>
    public static double LengthSquared(double x, double y) => x * x + y * y;
}
