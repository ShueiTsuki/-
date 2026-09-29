namespace HexCastingTerraria.Core.Ui;

// ── 书的绘制契约 ──────────────────────────────────────────────────────
//
// 这一层存在的唯一理由是：**让渲染能在没有游戏的情况下跑起来**。
//
// 如果两套皮肤直接调 `Main.spriteBatch` / `Utils.DrawBorderString`，那么
// 「先渲成 PNG、我自己看图改到满意再交给用户」这条路就断了 —— 而用户对这件事
// 唯一的硬要求是「禁止土气」，那是个**只能靠看图判断**的要求。
//
// 所以：皮肤只依赖下面这个最小接口。真实渲染传 SpriteBatch 的实现，
// 离屏出图（drawtest / patdraw）传一个把图元记下来再写成 PNG 的实现。
// 两套皮肤因此都能被离线驱动，Core/ 也仍然零 XNA 依赖（断言①）。

/// <summary>
/// 与渲染框架无关的颜色（Core/ 不能用 XNA 的 Color）。
///
/// 用字节而不是浮点：泰拉的像素美术本来就是 8 位色，浮点只会引入
/// 「看起来一样但比不相等」这种麻烦。
/// </summary>
public readonly struct Color32
{
    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    /// <summary>按 <paramref name="alpha"/>（0~255）调透明度，返回新色。</summary>
    public Color32 WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>与另一种颜色按 <paramref name="t"/>（0~1）线性混合。</summary>
    public Color32 Lerp(Color32 other, float t)
    {
        if (t <= 0f) { return this; }
        if (t >= 1f) { return other; }

        return new Color32(
            (byte)(R + ((other.R - R) * t)),
            (byte)(G + ((other.G - G) * t)),
            (byte)(B + ((other.B - B) * t)),
            (byte)(A + ((other.A - A) * t)));
    }

    /// <summary>从 <c>#RRGGBB</c> 或 <c>#RRGGBBAA</c> 建色。解析失败返回品红 —— 显眼，容易发现。</summary>
    public static Color32 FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) { return Magenta; }
        var s = hex.StartsWith("#", System.StringComparison.Ordinal) ? hex.Substring(1) : hex;
        if (s.Length != 6 && s.Length != 8) { return Magenta; }

        try
        {
            byte r = System.Convert.ToByte(s.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(s.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(s.Substring(4, 2), 16);
            byte a = s.Length == 8 ? System.Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
            return new Color32(r, g, b, a);
        }
        catch (System.Exception)
        {
            return Magenta;
        }
    }

    public static readonly Color32 Magenta = new(255, 0, 255);
}

/// <summary>
/// 文本的水平对齐。
///
/// 单独定义而不是用 XNA 的 <c>SpriteFont</c> 对齐枚举 —— 契约层不引第三方类型。
/// </summary>
public enum BookTextAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>
/// 皮肤能用的**全部**绘制能力。只有这么多，多一个都不给 ——
/// 能力越少，离屏实现越容易做到像素级一致（也就越能相信"我看的图就是游戏里的样子"）。
/// </summary>
public interface IBookCanvas
{
    /// <summary>填充一个矩形。</summary>
    void FillRect(RectF rect, Color32 color);

    /// <summary>画一个描边矩形（<paramref name="thickness"/> 为边宽，向内收）。</summary>
    void StrokeRect(RectF rect, Color32 color, int thickness = 1);

    /// <summary>画一条水平线（两端同色；要做渐变请用 <see cref="FillRect"/> 叠加）。</summary>
    void HLine(float x, float y, float width, Color32 color, int thickness = 1);

    /// <summary>
    /// 画文本。返回实际占用的宽度。
    /// <paramref name="scale"/> 是整数倍缩放（泰拉的像素字非整数缩放会糊）。
    /// </summary>
    float DrawText(string text, float x, float y, Color32 color,
                   BookTextAlign align = BookTextAlign.Left,
                   bool bold = false, bool italic = false, int scale = 1);

    /// <summary>量一段文本的宽度（供排版引擎当 MeasureWidth 用）。</summary>
    int MeasureText(string text, bool bold = false, bool italic = false, int scale = 1);

    /// <summary>
    /// 贴一张图（如 Patchouli 的 512×256 图集切片）。
    /// <paramref name="src"/> 用**像素**坐标；<paramref name="dst"/> 是屏幕坐标。
    /// </summary>
    void DrawImage(string asset, RectF src, RectF dst, Color32 tint);

    /// <summary>推入一个裁剪区（子元素不许画到外面）。必须与 <see cref="PopClip"/> 配对。</summary>
    void PushClip(RectF rect);

    /// <summary>弹出最近一个裁剪区。</summary>
    void PopClip();
}

/// <summary>点击落在了哪里。由皮肤在命中测试里给出。</summary>
public enum BookHitKind
{
    None = 0,

    /// <summary>分类网格里的某一格（<see cref="BookHit.Index"/> 是分类序号）。</summary>
    CategoryCell = 1,

    /// <summary>条目列表里的一行（<see cref="BookHit.Index"/> 是条目序号）。</summary>
    EntryRow = 2,

    /// <summary>上一页 / 下一页箭头。</summary>
    PrevPage = 3,
    NextPage = 4,

    /// <summary>返回上一级。</summary>
    Back = 5,

    /// <summary>某个页码点（<see cref="BookHit.Index"/> 是跨页序号）。</summary>
    PageDot = 6,
}

/// <summary>命中测试结果。</summary>
public readonly struct BookHit
{
    public BookHit(BookHitKind kind, int index = -1)
    {
        Kind = kind;
        Index = index;
    }

    public BookHitKind Kind { get; }

    /// <summary>含义随 <see cref="Kind"/> 变（分类序号 / 条目序号 / 跨页序号）。</summary>
    public int Index { get; }

    public static readonly BookHit None = new(BookHitKind.None);
}

/// <summary>
/// 一次布局的全部几何。皮肤 <c>Measure</c> 的产物，绘制与命中测试都只读它。
///
/// 把几何与绘制分开是为了让两者能用同一套数字 —— 否则「画在这儿、点在那儿」
/// 这种偏差只能靠手点才发现。几何本身是纯数据，所以能被离线断言检查
/// （热区不重叠、不越界）。
/// </summary>
public sealed class BookMetrics
{
    /// <summary>整本书占的屏幕区域。</summary>
    public RectF Book { get; set; }

    /// <summary>左页（单页皮肤时 = 内容区）。</summary>
    public RectF LeftPage { get; set; }

    /// <summary>右页；单页皮肤时宽为 0。</summary>
    public RectF RightPage { get; set; }

    /// <summary>标题区。</summary>
    public RectF Header { get; set; }

    /// <summary>分类网格里每一格的位置（顺序与 <see cref="BookDocument.Categories"/> 一致）。</summary>
    public System.Collections.Generic.List<RectF> CategoryCells { get; }
        = new System.Collections.Generic.List<RectF>();

    /// <summary>条目列表里每一行的位置（顺序与分类内条目顺序一致）。</summary>
    public System.Collections.Generic.List<RectF> EntryRows { get; }
        = new System.Collections.Generic.List<RectF>();

    /// <summary>页码点的位置（顺序 = 跨页序号）。</summary>
    public System.Collections.Generic.List<RectF> PageDots { get; }
        = new System.Collections.Generic.List<RectF>();

    public RectF PrevArrow { get; set; }

    public RectF NextArrow { get; set; }

    public RectF BackButton { get; set; }

    /// <summary>正文可用的行宽（给排版引擎的 <c>lineWidth</c>）。</summary>
    public int TextLineWidth { get; set; }

    /// <summary>正文每页可放几行（给排版引擎的 <c>linesPerPage</c>）。</summary>
    public int TextLinesPerPage { get; set; }
}

/// <summary>
/// 皮肤里的固定文案。
///
/// **不要在皮肤里硬编码中文**：Core/ 不能调 tModLoader 的本地化，而且离屏字模
/// （`_tools/gen_font_atlas.ps1`）现在只有 ASCII —— 硬编码中文的结果是
/// 游戏里好、出图时**整块文字凭空消失**（`Advance` 对未知字返回 0），而且不报错。
/// 所以文案由调用方填（Client 从本地化填中文），这里给英文兜底。
/// </summary>
public sealed class BookChrome
{
    public string CoverTitle { get; set; } = "The Hex Book";

    public string ContentsTitle { get; set; } = "Contents";

    public string Back { get; set; } = "Esc";

    public string EntriesSuffix { get; set; } = "entries";

    public string Crafting { get; set; } = "crafting";
}

/// <summary>
/// 一套书的外观。两套实现：帕秋莉版（照 Patchouli 的 512×256 图集）与
/// 原版版（泰拉自带 UI 观感）。
///
/// ⚠️ <see cref="Measure"/> **必须各自实现**，不能共用：
/// MC 的 GUI 是在 GUI scale 2~3 下画的，泰拉 UI 默认 1x，两边像素密度差一个量级。
/// 「只换贴图」的结果是帕秋莉版又大又糊。
/// </summary>
public abstract class BookSkin
{
    /// <summary>固定文案。调用方（Client）负责填成对应语言。</summary>
    public BookChrome Chrome { get; set; } = new BookChrome();

    /// <summary>皮肤名（配置项里显示、出图时当文件名）。</summary>
    public abstract string Name { get; }

    /// <summary>按视口算一遍几何。同一份内容在两套皮肤下的几何**允许完全不同**。</summary>
    public abstract BookMetrics Measure(BookView view, RectF viewport);

    /// <summary>画一帧。只读 <paramref name="m"/> 提供的几何，不自己再算一套。</summary>
    public abstract void Draw(IBookCanvas canvas, BookView view, BookMetrics m);

    /// <summary>
    /// 命中测试。默认实现按 <see cref="BookMetrics"/> 里的矩形逐个判 ——
    /// 两套皮肤都用得上，所以放在基类；需要特殊形状（如圆形页码点）时再覆写。
    /// </summary>
    public virtual BookHit HitTest(BookView view, BookMetrics m, float x, float y)
    {
        if (Contains(m.BackButton, x, y)) { return new BookHit(BookHitKind.Back); }
        if (Contains(m.PrevArrow, x, y)) { return new BookHit(BookHitKind.PrevPage); }
        if (Contains(m.NextArrow, x, y)) { return new BookHit(BookHitKind.NextPage); }

        for (int i = 0; i < m.CategoryCells.Count; i++)
        {
            if (Contains(m.CategoryCells[i], x, y)) { return new BookHit(BookHitKind.CategoryCell, i); }
        }

        for (int i = 0; i < m.EntryRows.Count; i++)
        {
            if (Contains(m.EntryRows[i], x, y)) { return new BookHit(BookHitKind.EntryRow, i); }
        }

        for (int i = 0; i < m.PageDots.Count; i++)
        {
            if (Contains(m.PageDots[i], x, y)) { return new BookHit(BookHitKind.PageDot, i); }
        }

        return BookHit.None;
    }

    // RectF 的字段是 X/Y/W/H（见 BookLayout.cs），且它自带 Contains —— 直接复用，
    // 不另写一份判据，否则「画的位置」和「点的位置」就有两个真源了。
    protected static bool Contains(RectF r, float x, float y)
        => r.W > 0f && r.H > 0f && r.Contains(x, y);
}
