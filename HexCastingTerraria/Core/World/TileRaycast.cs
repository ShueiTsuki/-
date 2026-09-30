using System;

namespace HexCastingTerraria.Core.World;

/// <summary>
/// 二维图格网格上的射线求交（Amanatides &amp; Woo 的 DDA 算法）。
///
/// 为什么自己写而不是直接用 Terraria 的 `Utils.PlotTileLine`：
/// ① `PlotTileLine` 是「沿一条**有宽度的**线扫过所有图格」，语义是「挖矿激光」，
///    不是数学射线 —— 它会在拐角处多命中图格，返回的也不是「第一个命中」。
/// ② 射线求交是**差一错误的重灾区**（边界落在整数格线上时归谁、起点格要不要算），
///    放在 Core 里用纯几何实现，才能用离线用例把边界情况钉死。
///
/// 与源项目的对应：源项目用 MC 的 `level.clip(ClipContext)`（体素射线），
/// 这里用等价的二维 DDA。
/// </summary>
public static class TileRaycast
{
    /// <summary>一次命中。</summary>
    public readonly struct Hit
    {
        /// <summary>命中图格的坐标。</summary>
        public required int TileX { get; init; }

        public required int TileY { get; init; }

        /// <summary>
        /// 命中面的法线（指向射线来的一侧），分量只可能是 -1/0/1。
        /// 对应源项目 `blockHitResult.direction.step()`（`raycast/axis` 用它）。
        ///
        /// 起点就在实心格内时法线为 (0,0) —— 此时没有「进入面」可言。
        /// </summary>
        public required int NormalX { get; init; }

        public required int NormalY { get; init; }
    }

    /// <summary>
    /// 从 (originX, originY) 沿方向 (dirX, dirY) 投射，最远 <paramref name="maxDist"/> 格，
    /// 返回**第一个**实心图格。
    ///
    /// 坐标为**图格单位**（不是像素）。任何输入都是安全的：
    /// 方向退化为零向量时返回 null（无命中），与 MC 的 `Vec3.normalize()`
    /// 在长度过小时返回 ZERO 的行为一致 —— 那种情况下源项目的射线长度为零，必然 miss。
    /// </summary>
    /// <param name="isSolid">查询某图格是否实心。</param>
    public static Hit? Cast(Func<int, int, bool> isSolid, double originX, double originY,
                            double dirX, double dirY, double maxDist)
    {
        if (isSolid == null) throw new ArgumentNullException(nameof(isSolid));
        if (maxDist <= 0) return null;

        // 方向归一化。退化方向 → 无命中。
        // 注意：这里**不能**用 HexMathUtil.SafeNormalize（它兜底返回 (1,0)）：
        // 那会把「零向量」悄悄变成「向右打 32 格」，可能命中一个玩家根本没指着的方块。
        // 源项目此处等价于 miss，保持一致。
        double len = System.Math.Sqrt(dirX * dirX + dirY * dirY);
        if (double.IsNaN(len) || double.IsInfinity(len) || len < 1e-9)
        {
            return null;
        }

        dirX /= len;
        dirY /= len;

        if (double.IsNaN(originX) || double.IsNaN(originY)) return null;

        int tileX = (int)System.Math.Floor(originX);
        int tileY = (int)System.Math.Floor(originY);

        // 起点就在实心格内：立刻命中，无进入面
        if (isSolid(tileX, tileY))
        {
            return new Hit { TileX = tileX, TileY = tileY, NormalX = 0, NormalY = 0 };
        }

        int stepX = dirX > 0 ? 1 : (dirX < 0 ? -1 : 0);
        int stepY = dirY > 0 ? 1 : (dirY < 0 ? -1 : 0);

        // 走到下一条竖直/水平格线所需的参数距离
        double tMaxX = stepX == 0
            ? double.PositiveInfinity
            : (stepX > 0 ? (tileX + 1 - originX) : (originX - tileX)) / System.Math.Abs(dirX);

        double tMaxY = stepY == 0
            ? double.PositiveInfinity
            : (stepY > 0 ? (tileY + 1 - originY) : (originY - tileY)) / System.Math.Abs(dirY);

        // 每跨一整格所需的参数距离
        double tDeltaX = stepX == 0 ? double.PositiveInfinity : 1.0 / System.Math.Abs(dirX);
        double tDeltaY = stepY == 0 ? double.PositiveInfinity : 1.0 / System.Math.Abs(dirY);

        // 保险步数：每步至少跨一格，正常不会超过 ceil(maxDist) 步。
        // 轴向射线（step 为 0）时另一个轴的 tMax 是无穷，靠这个上限兜住。
        int guard = (int)System.Math.Ceiling(maxDist) + 4;
        double t = 0;

        while (guard-- > 0)
        {
            int normalX, normalY;

            if (tMaxX < tMaxY)
            {
                t = tMaxX;
                tileX += stepX;
                tMaxX += tDeltaX;
                normalX = -stepX;
                normalY = 0;
            }
            else
            {
                t = tMaxY;
                tileY += stepY;
                tMaxY += tDeltaY;
                normalX = 0;
                normalY = -stepY;
            }

            // 超出射程就不再检查 —— 注意先前进再判距离，
            // 否则「恰好落在 maxDist 上的那一格」会被漏掉。
            if (t > maxDist) break;

            if (isSolid(tileX, tileY))
            {
                return new Hit { TileX = tileX, TileY = tileY, NormalX = normalX, NormalY = normalY };
            }
        }

        return null;
    }
}
