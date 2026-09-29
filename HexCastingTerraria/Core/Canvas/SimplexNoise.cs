namespace HexCastingTerraria.Core.Canvas;

/// <summary>
/// Minecraft 的 `SimplexNoise` + `SingleThreadedRandomSource`，逐行移植。
///
/// 咒法学的电光线型（RenderLib.makeZappy）用的是 `SimplexNoise(SingleThreadedRandomSource(9001))`。
/// 之前这里换成了自制的 value-noise：分布、频率都不同，还多除了一次 2，
/// 于是只好把抖动幅度从原版的 2.5 调到 0.55 来「凑观感」—— 线型和原版不是一回事。
/// 现在种子、置换表、梯度表都与原版一致，参数也就能原样照抄。
/// </summary>
public sealed class SimplexNoise
{
    private static readonly int[][] Gradient =
    {
        new[] { 1, 1, 0 }, new[] { -1, 1, 0 }, new[] { 1, -1, 0 }, new[] { -1, -1, 0 },
        new[] { 1, 0, 1 }, new[] { -1, 0, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, -1 },
        new[] { 0, 1, 1 }, new[] { 0, -1, 1 }, new[] { 0, 1, -1 }, new[] { 0, -1, -1 },
        new[] { 1, 1, 0 }, new[] { 0, -1, 1 }, new[] { -1, 1, 0 }, new[] { 0, -1, -1 },
    };

    private const double F3 = 0.3333333333333333;
    private const double G3 = 0.16666666666666666;

    private readonly int[] _p = new int[512];

    public SimplexNoise(long seed)
    {
        var random = new JavaLcg(seed);
        // 原版构造函数先取三个偏移量（3D getValue 不用它们，但它们消耗了随机数，影响置换表）
        random.NextDouble();
        random.NextDouble();
        random.NextDouble();
        for (int i = 0; i < 256; i++) _p[i] = i;
        for (int i = 0; i < 256; i++)
        {
            int j = random.NextInt(256 - i);
            (_p[i], _p[j + i]) = (_p[j + i], _p[i]);
        }
    }

    /// <summary>`RenderLib.NOISE`：种子 9001。</summary>
    public static SimplexNoise Hex { get; } = new(9001L);

    private int P(int i) => _p[i & 0xFF];

    private static double Dot(int[] g, double x, double y, double z) => g[0] * x + g[1] * y + g[2] * z;

    private static double Corner(int gi, double x, double y, double z, double max)
    {
        double d = max - x * x - y * y - z * z;
        if (d < 0.0) return 0.0;
        d *= d;
        return d * d * Dot(Gradient[gi], x, y, z);
    }

    public double GetValue(double x, double y, double z)
    {
        double s = (x + y + z) * F3;
        int i = Floor(x + s);
        int j = Floor(y + s);
        int k = Floor(z + s);
        double t = (i + j + k) * G3;
        double x0 = x - (i - t);
        double y0 = y - (j - t);
        double z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
        else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
        else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }

        double x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
        double x2 = x0 - i2 + F3, y2 = y0 - j2 + F3, z2 = z0 - k2 + F3;
        double x3 = x0 - 1.0 + 0.5, y3 = y0 - 1.0 + 0.5, z3 = z0 - 1.0 + 0.5;

        int ii = i & 255, jj = j & 255, kk = k & 255;
        int gi0 = P(ii + P(jj + P(kk))) % 12;
        int gi1 = P(ii + i1 + P(jj + j1 + P(kk + k1))) % 12;
        int gi2 = P(ii + i2 + P(jj + j2 + P(kk + k2))) % 12;
        int gi3 = P(ii + 1 + P(jj + 1 + P(kk + 1))) % 12;

        double n0 = Corner(gi0, x0, y0, z0, 0.6);
        double n1 = Corner(gi1, x1, y1, z1, 0.6);
        double n2 = Corner(gi2, x2, y2, z2, 0.6);
        double n3 = Corner(gi3, x3, y3, z3, 0.6);
        return 32.0 * (n0 + n1 + n2 + n3);
    }

    private static int Floor(double v)
    {
        int i = (int)v;
        return v < i ? i - 1 : i;
    }

    /// <summary>MC 的 `SingleThreadedRandomSource`（java.util.Random 的 48 位 LCG）。</summary>
    private sealed class JavaLcg
    {
        private const long Multiplier = 0x5DEECE66DL;
        private const long Mask = (1L << 48) - 1;
        private long _seed;

        public JavaLcg(long seed) => _seed = (seed ^ Multiplier) & Mask;

        private int Next(int bits)
        {
            _seed = (_seed * Multiplier + 0xBL) & Mask;
            return (int)(_seed >> (48 - bits));
        }

        public int NextInt(int bound)
        {
            if ((bound & (bound - 1)) == 0) return (int)((bound * (long)Next(31)) >> 31);
            int r, m;
            do
            {
                r = Next(31);
                m = r % bound;
            } while (r - m + (bound - 1) < 0);
            return m;
        }

        public double NextDouble()
        {
            long l = ((long)Next(26) << 27) + Next(27);
            return l * (double)1.110223E-16F;
        }
    }
}
