using System;
using System.Collections.Generic;

namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 一种颜料（原版 <c>PigmentItem</c> 的 <c>ColorProvider</c>）。颜色一律 0xRRGGBB。
/// </summary>
public sealed class PigmentDef
{
    public PigmentDef(string id, int[] colors, float period, bool perOwner = false)
    {
        Id = id;
        Colors = colors;
        Period = period;
        PerOwner = perOwner;
    }

    /// <summary>原版物品名去掉前缀：<c>dye_red</c>、<c>pride_gay</c>、<c>default</c>、<c>ancient</c>、<c>uuid</c>。</summary>
    public string Id { get; }

    /// <summary>渐变用的颜色表；只有一个颜色就是纯色。</summary>
    public int[] Colors { get; }

    /// <summary>渐变一圈要多少 MC 刻（原版 <c>time / 400</c>、<c>time / 600</c>）；0 = 纯色。</summary>
    public float Period { get; }

    /// <summary>灵魂闪光：颜色由施放「内化颜料」的人决定（原版 ItemUUIDPigment 用施法者 UUID）。</summary>
    public bool PerOwner { get; }
}

/// <summary>
/// 全部颜料与取色公式。逐字移植原版：
///   - 颜色表：ItemDyePigment（MC <c>DyeColor.getTextColor()</c>）、ItemPridePigment.Type、
///     ItemAmethystPigment、ItemAmethystAndCopperPigment、ItemUUIDPigment
///   - 渐变：ADPigment.morphBetweenColors（三次缓入缓出）
///   - 取色：ColorProvider.getColor（亮度低于 0.05 时叠一层暗色色轮，纯黑也看得见）
/// </summary>
public static class Pigments
{
    public const string DefaultId = "default";

    /// <summary>原版 DyeColor 顺序（与合成书页的顺序一致）及其 textColor。</summary>
    public static readonly (string Name, int Rgb)[] DyeColors =
    {
        ("white", 0xFFFFFF), ("orange", 0xFF681F), ("magenta", 0xFF00FF), ("light_blue", 0x9AC0CD),
        ("yellow", 0xFFFF00), ("lime", 0xBFFF00), ("pink", 0xFF69B4), ("gray", 0x808080),
        ("light_gray", 0xD3D3D3), ("cyan", 0x00FFFF), ("purple", 0xA020F0), ("blue", 0x0000FF),
        ("brown", 0x8B4513), ("green", 0x00FF00), ("red", 0xFF0000), ("black", 0x000000),
    };

    /// <summary>原版 ItemPridePigment.Type（顺序同枚举）。</summary>
    public static readonly (string Name, int[] Colors)[] PrideColors =
    {
        ("agender", new[] { 0x16a10c, 0xffffff, 0x7a8081, 0x302f30 }),
        ("aroace", new[] { 0x7210bc, 0xebf367, 0xffffff, 0x82dceb, 0x2f4dd8 }),
        ("aromantic", new[] { 0x16a10c, 0x82eb8b, 0xffffff, 0x7a8081, 0x302f30 }),
        ("asexual", new[] { 0x333233, 0x9a9fa1, 0xffffff, 0x7210bc }),
        ("bisexual", new[] { 0xdb45ff, 0x9c2bd0, 0x6894d4 }),
        ("demiboy", new[] { 0x9a9fa1, 0xa9ffff, 0xffffff }),
        ("demigirl", new[] { 0x9a9fa1, 0xfcb1ff, 0xffffff }),
        ("gay", new[] { 0xd82f3a, 0xe0883f, 0xebf367, 0x2db418, 0x2f4dd8 }),
        ("genderfluid", new[] { 0xfbacf9, 0xffffff, 0x9c2bd0, 0x333233, 0x2f4dd8 }),
        ("genderqueer", new[] { 0xca78ef, 0xffffff, 0x2db418 }),
        ("intersex", new[] { 0xebf367, 0x7210bc }),
        ("lesbian", new[] { 0xd82f3a, 0xefb87d, 0xffffff, 0xfbacf9, 0xa30262 }),
        ("nonbinary", new[] { 0xebf367, 0xffffff, 0x7210bc, 0x333233 }),
        ("pansexual", new[] { 0xe278ef, 0xebf367, 0x6ac2e4 }),
        ("plural", new[] { 0x30c69f, 0x347ddf, 0x6b3fbe, 0x000000 }),
        ("transgender", new[] { 0xeb92ea, 0xffffff, 0x6ac2e4 }),
    };

    private static readonly List<PigmentDef> _all = Build();
    private static readonly Dictionary<string, PigmentDef> _byId = Index(_all);

    public static IReadOnlyList<PigmentDef> All => _all;

    public static PigmentDef? Find(string id) => _byId.TryGetValue(id, out var d) ? d : null;

    private static List<PigmentDef> Build()
    {
        var list = new List<PigmentDef>();
        foreach (var (name, rgb) in DyeColors) { list.Add(new PigmentDef("dye_" + name, new[] { rgb }, 0)); }
        foreach (var (name, colors) in PrideColors) { list.Add(new PigmentDef("pride_" + name, colors, 400)); }
        list.Add(new PigmentDef(DefaultId, new[] { 0xab65eb }, 0));
        list.Add(new PigmentDef("ancient", new[] { 0x54398a, 0xcfa0f3, 0xfecbe6, 0xcfa0f3, 0xe77c56 }, 600));
        list.Add(new PigmentDef("uuid", Array.Empty<int>(), 400, perOwner: true));
        return list;
    }

    private static Dictionary<string, PigmentDef> Index(List<PigmentDef> all)
    {
        var d = new Dictionary<string, PigmentDef>();
        foreach (var p in all) { d[p.Id] = p; }
        return d;
    }

    /// <summary>原版 ColorProvider.MINIMUM_LUMINANCE_COLOR_WHEEL（去掉 alpha）。</summary>
    private static readonly int[] MinLuminanceWheel = { 0x200000, 0x202000, 0x002000, 0x002020, 0x000020, 0x200020 };

    /// <summary>
    /// 原版 <c>ColorProvider.getColor(time, position)</c>。
    /// <paramref name="mcTime"/> = MC 刻（泰拉 60 帧 = MC 20 刻，调用方除以 3）；
    /// <paramref name="posDot"/> = 原版的 <c>gradientDir · position</c>，gradientDir 恒为 (0.1, 0.1, 0.1)。
    /// 未知 id 按原版 FrozenPigment 读不出时的做法退回默认颜料。
    /// </summary>
    public static int Color(string id, Guid owner, float mcTime, float posDot)
    {
        var def = Find(id) ?? Find(DefaultId)!;
        int raw = RawColor(def, owner, mcTime, posDot);
        int r = (raw >> 16) & 0xFF, g = (raw >> 8) & 0xFF, b = raw & 0xFF;
        double luminance = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 0xFF;
        if (luminance < 0.05)
        {
            int mod = Morph(MinLuminanceWheel, mcTime / 20 / 20, posDot);
            r += (mod >> 16) & 0xFF;
            g += (mod >> 8) & 0xFF;
            b += mod & 0xFF;
        }
        return (r << 16) | (g << 8) | b;
    }

    private static int RawColor(PigmentDef def, Guid owner, float mcTime, float posDot)
    {
        var colors = def.PerOwner ? OwnerColors(owner) : def.Colors;
        if (def.Period <= 0 || colors.Length == 1) { return colors[0]; }
        return Morph(colors, mcTime / def.Period, posDot);
    }

    /// <summary>原版 ADPigment.morphBetweenColors（颜色 0xRRGGBB，alpha 恒为不透明所以不插值）。</summary>
    public static int Morph(int[] colors, float time, float posDot)
    {
        float x = time + posDot;
        float fIdx = (x - MathF.Floor(x)) * colors.Length;   // Mth.positiveModulo(…, 1)
        int baseIdx = (int)MathF.Floor(fIdx);
        float tRaw = fIdx - baseIdx;
        float t = tRaw < 0.5f ? 4 * tRaw * tRaw * tRaw : (float)(1 - (Math.Pow((-2 * tRaw) + 2, 3) / 2));
        int start = colors[baseIdx % colors.Length];
        int end = colors[(baseIdx + 1) % colors.Length];
        int Lerp(int shift) => (int)(((start >> shift) & 0xFF) + (t * (((end >> shift) & 0xFF) - ((start >> shift) & 0xFF))));
        return (Lerp(16) << 16) | (Lerp(8) << 8) | Lerp(0);
    }

    /// <summary>
    /// 灵魂闪光的两种颜色（原版 ItemUUIDPigment：不是贡献者时用 UUID 播种的 java.util.Random 随机出两个 HSV 色）。
    /// 泰拉没有玩家 UUID，用每个角色存档里的一个 Guid 代替（见 HexPlayer.Uuid）。
    /// </summary>
    public static int[] OwnerColors(Guid owner)
    {
        var bytes = owner.ToByteArray();
        long msb = 0, lsb = 0;
        for (int i = 0; i < 8; i++) { msb = (msb << 8) | bytes[i]; }
        for (int i = 8; i < 16; i++) { lsb = (lsb << 8) | bytes[i]; }
        var rand = new JavaRandom(lsb ^ msb);
        float hue1 = rand.NextFloat();
        float saturation1 = rand.NextFloat(0.4f, 0.8f);
        float brightness1 = rand.NextFloat(0.7f, 1.0f);
        float hue2 = rand.NextFloat();
        float saturation2 = rand.NextFloat(0.7f, 1.0f);
        float brightness2 = rand.NextFloat(0.2f, 0.7f);
        return new[] { HsvToRgb(hue1, saturation1, brightness1), HsvToRgb(hue2, saturation2, brightness2) };
    }

    /// <summary>MC 的 Mth.hsvToRgb。</summary>
    public static int HsvToRgb(float hue, float saturation, float value)
    {
        int i = (int)(hue * 6.0f) % 6;
        float f = (hue * 6.0f) - i;
        float p = value * (1.0f - saturation);
        float q = value * (1.0f - (f * saturation));
        float t = value * (1.0f - ((1.0f - f) * saturation));
        (float r, float g, float b) = i switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            5 => (value, p, q),
            _ => throw new ArgumentOutOfRangeException(nameof(hue)),
        };
        int C(float c) => Math.Clamp((int)(c * 255.0f), 0, 255);
        return (C(r) << 16) | (C(g) << 8) | C(b);
    }

    /// <summary>java.util.Random（线性同余，48 位）——只移植灵魂闪光用到的部分。</summary>
    internal sealed class JavaRandom
    {
        private long _seed;

        public JavaRandom(long seed) => _seed = (seed ^ 0x5DEECE66DL) & ((1L << 48) - 1);

        private int Next(int bits)
        {
            _seed = ((_seed * 0x5DEECE66DL) + 0xBL) & ((1L << 48) - 1);
            return (int)((ulong)_seed >> (48 - bits));
        }

        public float NextFloat() => Next(24) / (float)(1 << 24);

        /// <summary>Java 17 RandomSupport.boundedNextFloat。</summary>
        public float NextFloat(float origin, float bound)
        {
            float r = NextFloat();
            r = (r * (bound - origin)) + origin;
            if (r >= bound) { r = MathF.BitDecrement(bound); }
            return r;
        }
    }
}
