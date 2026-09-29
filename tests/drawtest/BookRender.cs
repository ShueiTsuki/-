// 离屏画布 + 自带 PNG 编码器 —— 书本皮肤的「不开游戏也能出图」基础设施。
//
// ## 为什么不用 System.Drawing
//
// `System.Drawing.Common` 从 .NET 7 起不再随框架分发，是 Windows-only 且需要 NuGet 包，
// 离线环境 restore 不一定成功。这里改用 .NET 自带的 `System.IO.Compression.ZLibStream`
// 加自己写的 PNG 分块与 CRC32 —— 约 200 行、零依赖、任何平台都能跑。
//
// （别被本项目的 PowerShell 工具误导：那边能用 `System.Drawing` 是因为
//   Windows PowerShell 5.1 自带 .NET Framework，与 net10.0 的工程不是一回事。）
//
// ## 字体
//
// 泰拉的像素字体在离屏环境里没有。所以 `DrawText` 现在画的是**按字符宽度撑开的灰块** ——
// 用来核**版面**：行宽、行数、分页、对齐、留白、热区。这些恰恰是「土不土」的大头。
// 等版面定稿，再把泰拉位图字体烘成「位图 + 宽度表」替换进来，那是纯替换、不影响布局。

using HexCastingTerraria.Core.Ui;

/// <summary>把 <see cref="IBookCanvas"/> 画进一块 RGBA 缓冲，最后写成标准 PNG。</summary>
public sealed class OffscreenCanvas : IBookCanvas
{
    private readonly byte[] _px;
    private readonly List<RectF> _clips = new();

    public OffscreenCanvas(int width, int height, Color32 background)
    {
        Width = width;
        Height = height;
        _px = new byte[width * height * 4];
        FillRect(new RectF(0, 0, width, height), background);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>记录所有绘制调用次数 —— 断言「皮肤真的画了东西」比断言像素更稳。</summary>
    public int PrimitiveCount { get; private set; }

    private RectF Clip => _clips.Count > 0 ? _clips[_clips.Count - 1] : new RectF(0, 0, Width, Height);

    public void PushClip(RectF rect)
    {
        var cur = Clip;
        float x = MathF.Max(cur.X, rect.X);
        float y = MathF.Max(cur.Y, rect.Y);
        float r = MathF.Min(cur.Right, rect.Right);
        float b = MathF.Min(cur.Bottom, rect.Bottom);
        _clips.Add(new RectF(x, y, MathF.Max(0f, r - x), MathF.Max(0f, b - y)));
    }

    public void PopClip()
    {
        if (_clips.Count > 0) { _clips.RemoveAt(_clips.Count - 1); }
    }

    public void FillRect(RectF rect, Color32 color)
    {
        PrimitiveCount++;
        var clip = Clip;
        int x0 = (int)MathF.Floor(MathF.Max(rect.X, clip.X));
        int y0 = (int)MathF.Floor(MathF.Max(rect.Y, clip.Y));
        int x1 = (int)MathF.Ceiling(MathF.Min(rect.Right, clip.Right));
        int y1 = (int)MathF.Ceiling(MathF.Min(rect.Bottom, clip.Bottom));

        for (int y = y0; y < y1; y++)
        {
            if (y < 0 || y >= Height) { continue; }
            for (int x = x0; x < x1; x++)
            {
                if (x < 0 || x >= Width) { continue; }
                Blend(x, y, color);
            }
        }
    }

    public void StrokeRect(RectF rect, Color32 color, int thickness = 1)
    {
        if (thickness < 1) { thickness = 1; }
        FillRect(new RectF(rect.X, rect.Y, rect.W, thickness), color);
        FillRect(new RectF(rect.X, rect.Bottom - thickness, rect.W, thickness), color);
        FillRect(new RectF(rect.X, rect.Y, thickness, rect.H), color);
        FillRect(new RectF(rect.Right - thickness, rect.Y, thickness, rect.H), color);
    }

    public void HLine(float x, float y, float width, Color32 color, int thickness = 1)
        => FillRect(new RectF(x, y, width, thickness), color);

    /// <summary>
    /// 已烘好的字模图集。为 null 时退回**占位方块** —— 但那条路算出来的折行位置是错的
    /// （等宽方块 ≠ 真字形），所以出图时一定要确认它是加载上了的。
    /// </summary>
    public BookFont? Font { get; set; }

    /// <summary>
    /// 画文本。有字模就用真字形（查宽度表贴图），没有才退回占位方块。
    /// <paramref name="scale"/> 是整数倍缩放。
    /// </summary>
    public float DrawText(string text, float x, float y, Color32 color,
                          BookTextAlign align = BookTextAlign.Left,
                          bool bold = false, bool italic = false, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) { return 0f; }
        if (scale < 1) { scale = 1; }

        float w = MeasureText(text, bold, italic, scale);
        float penX = align switch
        {
            BookTextAlign.Center => x - (w / 2f),
            BookTextAlign.Right => x - w,
            _ => x,
        };

        PrimitiveCount++;

        if (Font is null)
        {
            // 退化路径：等宽占位方块。只为「字模没加载上」时还能出图，不代表真实版面。
            int cell = 5 * scale;
            int gap = 1 * scale;
            int h = 7 * scale;
            int i = 0;
            foreach (var ch in text)
            {
                if (ch != ' ')
                {
                    FillRect(new RectF(penX + (i * (cell + gap)), y, cell, h), color);
                }
                i++;
            }
            return w;
        }

        foreach (var ch in text)
        {
            int adv = Font.Advance(ch);
            if (ch != ' ' && Font.TryGlyph(ch, out var g))
            {
                Blit(Font.Pixels, Font.AtlasW, Font.AtlasH,
                     new RectF(g.x, g.y, g.w, g.h),
                     new RectF(penX, y, g.w * scale, g.h * scale),
                     color);
            }
            penX += adv * scale;
        }

        return w;
    }

    public int MeasureText(string text, bool bold = false, bool italic = false, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) { return 0; }
        if (scale < 1) { scale = 1; }

        if (Font is null)
        {
            int cell = 5 * scale;
            int gap = 1 * scale;
            return (text.Length * (cell + gap)) - gap;
        }

        int w = 0;
        foreach (var ch in text) { w += Font.Advance(ch); }
        return w * scale;
    }

    /// <summary>
    /// 已加载的**命名图集**（`_tools/ui_atlas.raw` + `.txt`）。
    ///
    /// 语义：<c>DrawImage(name, src, dst, tint)</c> 里的 <paramref name="src"/> 是
    /// **该贴图内部**的坐标，画布负责叠加它在图集里的偏移。
    /// 这样皮肤可以正大光明地写 `DrawImage("slot_back", new RectF(0,0,70,70), ...)`，
    /// 而不用知道它在图集里的位置 —— 位置是打包脚本的事。
    /// </summary>
    public UiAtlas? Atlas { get; set; }

    /// <summary>
    /// 贴图集的某个区域。<paramref name="src"/> 是**该贴图内部**的像素矩形，
    /// <paramref name="dst"/> 是屏幕矩形；最近邻缩放（像素美术不能双线性）。
    /// </summary>
    public void DrawImage(string asset, RectF src, RectF dst, Color32 tint)
    {
        PrimitiveCount++;

        if (Atlas is null || !Atlas.TryRegion(asset, out var region))
        {
            FillRect(dst, tint.A < 255 ? tint : new Color32(120, 100, 140));
            return;
        }

        var abs = new RectF(region.X + src.X, region.Y + src.Y, src.W, src.H);
        Blit(Atlas.Pixels, Atlas.Width, Atlas.Height, abs, dst, tint);
    }

    private void Blit(byte[] px4, int pw, int ph, RectF src, RectF dst, Color32 tint)
    {
        int dw = (int)MathF.Round(dst.W);
        int dh = (int)MathF.Round(dst.H);
        if (dw <= 0 || dh <= 0 || src.W <= 0f || src.H <= 0f) { return; }

        for (int y = 0; y < dh; y++)
        {
            int py = (int)(dst.Y) + y;
            if (py < 0 || py >= Height) { continue; }

            int sy = (int)(src.Y + ((y * src.H) / dh));
            if (sy < 0 || sy >= ph) { continue; }

            for (int x = 0; x < dw; x++)
            {
                int px = (int)(dst.X) + x;
                if (px < 0 || px >= Width) { continue; }

                int sx = (int)(src.X + ((x * src.W) / dw));
                if (sx < 0 || sx >= pw) { continue; }

                int si = ((sy * pw) + sx) * 4;
                var c = new Color32(
                    (byte)(px4[si] * tint.R / 255),
                    (byte)(px4[si + 1] * tint.G / 255),
                    (byte)(px4[si + 2] * tint.B / 255),
                    (byte)(px4[si + 3] * tint.A / 255));

                if (c.A == 0) { continue; }
                // 图集自身带 alpha（书角与字形都是抠出来的），所以这里必须混合而不是覆盖
                Blend(px, py, c);
            }
        }
    }

    private void Blend(int x, int y, Color32 c)
    {
        int i = ((y * Width) + x) * 4;
        if (c.A == 0) { return; }
        if (c.A == 255)
        {
            _px[i] = c.R; _px[i + 1] = c.G; _px[i + 2] = c.B; _px[i + 3] = 255;
            return;
        }

        // 与底色做普通 alpha 混合（不是 premultiplied：PNG 存的是直通 alpha）
        float a = c.A / 255f;
        _px[i] = (byte)((_px[i] * (1 - a)) + (c.R * a));
        _px[i + 1] = (byte)((_px[i + 1] * (1 - a)) + (c.G * a));
        _px[i + 2] = (byte)((_px[i + 2] * (1 - a)) + (c.B * a));
        _px[i + 3] = 255;
    }

    public void SavePng(string path) => PngWriter.Write(path, _px, Width, Height);
}

/// <summary>
/// Patchouli 图集的裸 RGBA 数据。
///
/// 由 `_tools/prep_book_atlas.ps1` 从原 PNG 解出来 —— 离屏环境（net10.0 控制台）
/// 没有 PNG 解码器，而 Windows PowerShell 5.1 自带 .NET Framework，解码放在那边做。
/// 运行时（游戏内）不走这条路：tModLoader 自己会加载 PNG。
/// </summary>
public sealed class BookAtlas
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public byte[] Pixels { get; private set; } = Array.Empty<byte>();

    public static BookAtlas Load(string rawPath, int width, int height)
    {
        var px = File.ReadAllBytes(rawPath);
        int expect = width * height * 4;
        if (px.Length != expect)
        {
            throw new InvalidDataException($"{rawPath} 有 {px.Length} 字节，应为 {expect}（{width}x{height}x4）");
        }

        return new BookAtlas { Width = width, Height = height, Pixels = px };
    }
}

/// <summary>
/// 烘好的字模：裸 RGBA 图集 + 纯文本宽度表。
///
/// 由 `_tools/gen_font_atlas.ps1` 生成（PowerShell 的 System.Drawing 有字体栅格化能力，
/// net10.0 的离屏工程没有）。**注意这套字形不是泰拉原版字体**，
/// 它只用来定版面与配色；字号字距的最后一轮微调仍要对着游戏里的 FontAssets.MouseText 做。
/// </summary>
public sealed class BookFont
{
    public int AtlasW { get; private set; }

    public int AtlasH { get; private set; }

    public int LineHeight { get; private set; }

    public byte[] Pixels { get; private set; } = Array.Empty<byte>();

    private readonly Dictionary<int, int[]> _glyphs = new();   // code -> [x,y,w,h,adv]

    public static BookFont Load(string txtPath, string rawPath)
    {
        var f = new BookFont();
        foreach (var raw in File.ReadAllLines(txtPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) { continue; }

            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "atlas") { f.AtlasW = int.Parse(p[1]); f.AtlasH = int.Parse(p[2]); continue; }
            if (p[0] == "lineH") { f.LineHeight = int.Parse(p[1]); continue; }
            if (p.Length < 6) { continue; }

            f._glyphs[int.Parse(p[0])] = new[]
            {
                int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]),
            };
        }

        f.Pixels = File.ReadAllBytes(rawPath);
        int expect = f.AtlasW * f.AtlasH * 4;
        if (f.Pixels.Length != expect)
        {
            throw new InvalidDataException($"{rawPath} 有 {f.Pixels.Length} 字节，应为 {expect}");
        }

        return f;
    }

    /// <summary>某个字符画完要往右挪多少。表里没有的字（如中文）返回 0 —— 调用方要能看出缺字。</summary>
    public int Advance(char c) => _glyphs.TryGetValue(c, out var g) ? g[4] : 0;

    public bool TryGlyph(char c, out (int x, int y, int w, int h) rect)
    {
        if (_glyphs.TryGetValue(c, out var g))
        {
            rect = (g[0], g[1], g[2], g[3]);
            return true;
        }

        rect = (0, 0, 0, 0);
        return false;
    }

    /// <summary>缺字统计 —— 中文进图集之前，这个数字会很大，是预期的。</summary>
    public int CountMissing(string text)
    {
        int n = 0;
        foreach (var c in text)
        {
            if (c != ' ' && !_glyphs.ContainsKey(c)) { n++; }
        }
        return n;
    }
}

/// <summary>
/// 命名图集：一整张裸 RGBA + 一张「名字 → 矩形」表。
///
/// 由 `_tools/pack_ui_atlas.ps1` 生成 —— 它把 Patchouli 书图集与**从游戏里导出来的**
/// 原版泰拉 UI 贴图（`_tools/vanilla_ui/`）拼成一张，解码在 PowerShell 侧做完
/// （离屏工程没有 PNG 解码器）。
/// </summary>
public sealed class UiAtlas
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public byte[] Pixels { get; private set; } = Array.Empty<byte>();

    private readonly Dictionary<string, RectF> _regions = new();

    public static UiAtlas Load(string txtPath, string rawPath)
    {
        var a = new UiAtlas();

        foreach (var raw in File.ReadAllLines(txtPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) { continue; }

            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "atlas") { a.Width = int.Parse(p[1]); a.Height = int.Parse(p[2]); continue; }
            if (p.Length < 5) { continue; }

            a._regions[p[0]] = new RectF(
                int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]));
        }

        a.Pixels = File.ReadAllBytes(rawPath);
        int expect = a.Width * a.Height * 4;
        if (a.Pixels.Length != expect)
        {
            throw new InvalidDataException($"{rawPath} 有 {a.Pixels.Length} 字节，应为 {expect}");
        }

        return a;
    }

    public bool TryRegion(string name, out RectF region) => _regions.TryGetValue(name, out region);

    public int Count => _regions.Count;
}

/// <summary>最小 PNG 写出器：8 位 RGBA、无过滤、单个 IDAT。</summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static void Write(string path, byte[] rgba, int width, int height)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }

        using var fs = File.Create(path);
        fs.Write(Signature, 0, Signature.Length);

        // IHDR
        var ihdr = new byte[13];
        BeInt(ihdr, 0, width);
        BeInt(ihdr, 4, height);
        ihdr[8] = 8;    // 位深
        ihdr[9] = 6;    // 颜色类型 6 = RGBA
        ihdr[10] = 0;   // 压缩方法
        ihdr[11] = 0;   // 过滤方法
        ihdr[12] = 0;   // 非隔行
        Chunk(fs, "IHDR", ihdr);

        // IDAT：每行前面加一个过滤字节 0
        var raw = new byte[height * ((width * 4) + 1)];
        for (int y = 0; y < height; y++)
        {
            int o = y * ((width * 4) + 1);
            raw[o] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, o + 1, width * 4);
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                z.Write(raw, 0, raw.Length);
            }
            compressed = ms.ToArray();
        }
        Chunk(fs, "IDAT", compressed);
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        var len = new byte[4];
        BeInt(len, 0, data.Length);
        s.Write(len, 0, 4);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);

        uint crc = Crc32(t, data);
        var c = new byte[4];
        BeInt(c, 0, unchecked((int)crc));
        s.Write(c, 0, 4);
    }

    private static void BeInt(byte[] buf, int at, int v)
    {
        buf[at] = (byte)(v >> 24);
        buf[at + 1] = (byte)(v >> 16);
        buf[at + 2] = (byte)(v >> 8);
        buf[at + 3] = (byte)v;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        c = crc(c, a);
        c = crc(c, b);
        return c ^ 0xFFFFFFFFu;

        static uint crc(uint c, byte[] d)
        {
            foreach (var by in d)
            {
                c ^= by;
                for (int i = 0; i < 8; i++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
            }
            return c;
        }
    }
}
