using System.Collections.Generic;
using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Ui;

/// <summary>书上可点的东西。</summary>
public enum BookActionKind
{
    OpenCategory,
    OpenEntry,
    Link,
    Back,
    PrevSpread,
    NextSpread,
}

public readonly struct BookHit
{
    public BookHit(RectF rect, BookActionKind kind, string arg = "")
    {
        Rect = rect;
        Kind = kind;
        Arg = arg;
    }

    public RectF Rect { get; }
    public BookActionKind Kind { get; }
    public string Arg { get; }
}

/// <summary>一帧的产出：可点区域 + 悬停提示。</summary>
public sealed class BookFrame
{
    public List<BookHit> Hits { get; } = new();
    public string Tooltip { get; set; } = string.Empty;
    public RectF Book { get; set; }

    /// <summary>画到页面外（被截掉）的文字行数。离线测试用它断言「没有字溢出页面」。</summary>
    public int OverflowLines { get; set; }

    public BookHit? HitAt(float x, float y)
    {
        // 后画的在上层：倒序找
        for (int i = Hits.Count - 1; i >= 0; i--)
        {
            if (Hits[i].Rect.Contains(x, y)) { return Hits[i]; }
        }
        return null;
    }
}

/// <summary>
/// 咒法学之书的渲染器：逐项照搬 Patchouli 的界面（VazkiiMods/Patchouli，CC BY-NC-SA 3.0）
/// 与咒法学自己的书页组件（FallingColors/HexMod，MIT），见 CREDITS.md。
///
/// 所有坐标常量都是 Patchouli 源码里的原值（GUI 单位，书体 272×180），
/// 乘以 <see cref="Unit"/>（每 GUI 单位多少像素）画到屏幕上。注释里写了每个数出自哪个类。
/// </summary>
public sealed class PatchouliRenderer
{
    // GuiBook
    public const int FullWidth = 272;
    public const int FullHeight = 180;
    private const int PageWidth = 116;
    private const int PageHeight = 156;
    private const int TopPadding = 18;
    private const int LeftPageX = 15;
    private const int RightPageX = 141;
    private const int TextLineHeight = 9;

    // 颜色：Patchouli 默认（Book.java）+ 咒法学 book.json 的 nameplate_color
    private const int HeaderColor = 0x333333;
    private const int TextColor = 0x000000;
    private const int NameplateColor = 0x00072b;
    private const int LinkColor = 0x0000EE;
    private const int LinkHoverColor = 0x8800EE;

    // 界面文字（Patchouli 的 zh_cn.json）
    private const string CategoriesTitle = "类别";
    private const string ChaptersTitle = "章节";

    /// <summary>
    /// 文字相对行高的放大系数。泰拉的 MouseText 字体行距里留白比 MC 字体多（字身约占行距 65%，MC 约 78%），
    /// 按行高 1:1 缩放会让字比原版小一号；行距仍保持 9 单位，不会挤行。
    /// </summary>
    private const float GlyphBoost = 1.12f;

    private readonly IBookData _data;
    private IBookCanvas _c = null!;
    private BookFrame _frame = null!;
    private float _ox, _oy, _mx, _my;
    private BookView _view = null!;

    public PatchouliRenderer(IBookData data)
    {
        _data = data;
    }

    /// <summary>每 GUI 单位的像素数（整数最近邻放大，像素才不会糊）。</summary>
    public float Unit { get; private set; } = 3f;

    /// <summary>
    /// 按视口选整数倍率：书占视口约 70%。1080p 下是 4 —— 与 MC 在 1080p 自动界面缩放（4）下的书一样大。
    /// </summary>
    public static float ChooseUnit(float viewportW, float viewportH, float sizeFactor = 1f)
    {
        // 书占视口高度约 88%（1080p、界面缩放 100% 时是 5 倍 = 1360×900）。
        // 默认取整数倍（像素最规整）；玩家在设置里调了大小时允许半格步进。
        float target = System.MathF.Min(viewportW * 0.9f / FullWidth, viewportH * 0.88f / FullHeight) * sizeFactor;
        float u = System.MathF.Abs(sizeFactor - 1f) < 0.001f ? System.MathF.Floor(target) : System.MathF.Floor(target * 2f) / 2f;
        // 调大了也不能超出屏幕（留 2% 边）
        float fit = System.MathF.Floor(System.MathF.Min(viewportW * 0.98f / FullWidth, viewportH * 0.98f / FullHeight) * 2f) / 2f;
        return System.Math.Clamp(System.MathF.Min(u, fit), 1f, 10f);
    }

    /// <summary>书本大小倍率（设置项）。1 = 默认。</summary>
    public float SizeFactor { get; set; } = 1f;

    private const string LockedText = "锁定";

    /// <summary>条目是否解锁。</summary>
    public bool IsUnlocked(BookEntry e) => _data.IsUnlocked(e.Advancement);

    /// <summary>分类是否解锁：有任何一个条目（或子分类）解锁即可（Patchouli BookCategory.isLocked）。</summary>
    public bool IsUnlocked(BookView view, BookCategory c)
        => c.Entries.Exists(IsUnlocked) || view.Subcategories(c).Exists(s => IsUnlocked(view, s));

    public BookFrame Render(IBookCanvas canvas, BookView view, float viewportW, float viewportH, float mouseX, float mouseY)
    {
        _c = canvas;
        _frame = new BookFrame();
        Unit = ChooseUnit(viewportW, viewportH, SizeFactor);
        _view = view;
        _ox = System.MathF.Round((viewportW - (FullWidth * Unit)) / 2f);
        _oy = System.MathF.Round((viewportH - (FullHeight * Unit)) / 2f);
        _mx = mouseX;
        _my = mouseY;
        _frame.Book = R(0, 0, FullWidth, FullHeight);

        Tex(BookTextures.Book, 0, 0, FullWidth, FullHeight, 0, 0);

        switch (view.Kind)
        {
            case BookViewKind.Landing: DrawLanding(view); break;
            case BookViewKind.Category: DrawCategory(view); break;
            default: DrawEntry(view); break;
        }

        // GuiBook.init：返回按钮（书底中央）+ 左右翻页箭头（书底两角）
        if (view.Kind != BookViewKind.Landing)
        {
            Button(FullWidth / 2 - 9, FullHeight - 5, 308, 0, 18, 9, BookActionKind.Back, "返回");
        }
        if (view.Spread > 0)
        {
            Button(-4, FullHeight - 6, 272, 10, 18, 10, BookActionKind.PrevSpread, "上一页");
        }
        if (view.Spread + 1 < view.SpreadCount)
        {
            Button(FullWidth - 14, FullHeight - 6, 272, 0, 18, 10, BookActionKind.NextSpread, "下一页");
        }
        return _frame;
    }

    // ── 三个界面 ────────────────────────────────────────────────────

    /// <summary>GuiBookLanding：左页名牌 + 引言，右页「类别」+ 图标格。</summary>
    private void DrawLanding(BookView view)
    {
        // drawHeader：名牌贴图 (0,180,140,31) 画在 (-8,12)，书名在 (13,16)
        Tex(BookTextures.Book, -8, 12, 140, 31, 0, 180);
        Text(view.Document.DisplayTitle, 13, 16, NameplateColor);

        TextBlock(view.Document.LandingText, LeftPageX, TopPadding + 25, TopPadding + PageHeight);

        var cats = view.TopCategories();
        CenteredText(CategoriesTitle, RightPageX + (PageWidth / 2), TopPadding, HeaderColor);
        Separator(RightPageX, TopPadding + 12);
        for (int i = 0; i < cats.Count; i++)
        {
            CategoryButton(cats[i], RightPageX + 10 + ((i % 4) * 24), TopPadding + 25 + ((i / 4) * 24));
        }
        Separator(RightPageX, TopPadding + 12 + 25 + (24 * (((cats.Count - 1) / 4) + 1)));
    }

    /// <summary>GuiBookCategory / GuiBookEntryList：左页分类介绍，右页起条目列表（分页）。</summary>
    private void DrawCategory(BookView view)
    {
        var cat = view.CurrentCategory;
        if (cat is null) { return; }
        var entries = cat.Entries;

        if (view.Spread == 0)
        {
            CenteredText(cat.DisplayName, LeftPageX + (PageWidth / 2), TopPadding, HeaderColor);
            CenteredText(ChaptersTitle, RightPageX + (PageWidth / 2), TopPadding, HeaderColor);
            Separator(LeftPageX, TopPadding + 12);
            Separator(RightPageX, TopPadding + 12);

            var subs = view.Subcategories(cat);
            int subTop = TopPadding + PageHeight - ((subs.Count / 4) * 20) - 38;
            TextBlock(cat.DisplayDescription, LeftPageX, TopPadding + 22, subs.Count > 0 ? subTop - 4 : TopPadding + PageHeight);
            for (int i = 0; i < subs.Count; i++)
            {
                CategoryButton(subs[i], LeftPageX + 10 + ((i % 4) * 24), subTop + ((i / 4) * 20));
            }

            for (int i = 0; i < BookView.EntriesInFirstPage && i < entries.Count; i++)
            {
                EntryButton(entries[i], RightPageX, TopPadding + 20 + (i * 11));
            }
        }
        else
        {
            int start = BookView.EntriesInFirstPage + ((view.Spread - 1) * BookView.EntriesPerPage * 2);
            for (int i = 0; i < BookView.EntriesPerPage * 2 && start + i < entries.Count; i++)
            {
                int x = i < BookView.EntriesPerPage ? LeftPageX : RightPageX;
                EntryButton(entries[start + i], x, TopPadding + ((i % BookView.EntriesPerPage) * 11));
            }
            // 最后一个跨页右页空着时，Patchouli 画一张装饰页
            if (entries.Count - start <= BookView.EntriesPerPage) { Filler(RightPageX, TopPadding); }
        }
    }

    /// <summary>GuiBookEntry：第 2s 页在左、2s+1 页在右。</summary>
    private void DrawEntry(BookView view)
    {
        var entry = view.CurrentEntry;
        if (entry is null) { return; }
        int left = view.Spread * 2;
        DrawPage(entry, left, LeftPageX, TopPadding);
        if (left + 1 < entry.Pages.Count)
        {
            DrawPage(entry, left + 1, RightPageX, TopPadding);
        }
        else
        {
            Filler(RightPageX, TopPadding);
        }
    }

    // ── 页面类型 ────────────────────────────────────────────────────

    private void DrawPage(BookEntry entry, int pageNum, int px, int py)
    {
        var page = entry.Pages[pageNum];
        int bottom = py + PageHeight;
        switch (page.Kind)
        {
            case BookPageKind.Empty:
                Filler(px, py);
                if (page.Title.Length > 0) { CenteredText(page.Title, px + (PageWidth / 2), py, HeaderColor); }
                break;

            case BookPageKind.Pattern:
                DrawPatternPage(page, px, py, bottom);
                break;

            case BookPageKind.Crafting:
                DrawCraftingPage(page, px, py, bottom);
                break;

            case BookPageKind.Spotlight:
            {
                // PageSpotlight：标题 y=0，物品框 (66×26，贴图 0,102) 在 y=10，物品在 y=15，正文 y=40
                string title = page.Title.Length > 0 ? page.Title : _data.ItemName(page.IconItem);
                CenteredText(title, px + (PageWidth / 2), py, HeaderColor);
                Tex(BookTextures.Crafting, px + (PageWidth / 2) - 33, py + 10, 66, 26, 0, 128 - 26, texW: 128, texH: 256);
                Item(page.IconItem, px + (PageWidth / 2) - 8, py + 15);
                TextBlock(page.Text, px, py + 40, bottom);
                break;
            }

            case BookPageKind.Brainsweep:
                // 咒法学的剖念页：泰拉侧没有 MC 的方块/生物预览，只保留标题与正文
                CenteredText("剖念", px + (PageWidth / 2), py, HeaderColor);
                Separator(px, py + 12);
                TextBlock(page.Text, px, py + 22, bottom);
                break;

            default:
            {
                // PageText.getTextHeight：条目第一页 22（上面画条目名 + 分隔线）；有标题 12；否则 -4
                int textY;
                if (pageNum == 0)
                {
                    CenteredText(entry.DisplayName, px + (PageWidth / 2), py, HeaderColor);
                    Separator(px, py + 12);
                    textY = 22;
                }
                else if (page.Title.Length > 0)
                {
                    CenteredText(page.Title, px + (PageWidth / 2), py, HeaderColor);
                    textY = 12;
                }
                else
                {
                    textY = -4;
                }
                TextBlock(page.Text, px, py + textY, bottom);
                break;
            }
        }
    }

    /// <summary>
    /// 咒法学的 pattern / manual_pattern 模板：标题 + 分隔线，图案画在 y∈[16,80] 的格子里，
    /// 有签名时 y=80 画「输入 → 输出」、y=89 起正文；否则 y=80 起正文。
    /// </summary>
    private void DrawPatternPage(BookPage page, int px, int py, int bottom)
    {
        CenteredText(page.Title, px + (PageWidth / 2), py, HeaderColor);
        Separator(px, py + 12);

        var patterns = new List<HexPattern>();
        foreach (var pp in page.Patterns)
        {
            if (HexPattern.TryFromAngles(pp.Signature, ParseDir(pp.StartDir), out var hp, out _) && hp is not null)
            {
                patterns.Add(hp);
            }
        }
        bool strokeOrder = true;
        if (patterns.Count == 0 && page.PatternId.Length > 0)
        {
            var def = PatternRegistry.FindById(page.PatternId);
            if (def is not null)
            {
                patterns.Add(def.Prototype);
                // 源项目 LookupPatternComponent：每个世界笔顺不同的大法术**不显示笔顺**（静态画法、没有起笔点），
                // 书只告诉你形状，本世界的画法要从古卷里学
                strokeOrder = !PatternRegistry.IsPerWorld(def);
            }
        }
        DrawPatternGrid(patterns, px, py, strokeOrder);

        bool sig = page.Input.Length > 0 || page.Output.Length > 0;
        if (sig)
        {
            // 模板原文："$(n)#input#/$ → $(n)#output#/$"
            TextBlock($"$(n){page.Input}/$ → $(n){page.Output}/$", px, py + 80, py + 89);
        }
        TextBlock(page.Text, px, py + (sig ? 89 : 80), bottom);
    }

    /// <summary>
    /// PageCrafting：配方框（crafting.png 0,0,100,62）在 (recipeX-2, recipeY-2)，recipeX = PAGE_WIDTH/2-49，recipeY = 4；
    /// 格子 19 单位一格，产物在 (79,22)，合成站在 (79,41)；标题在 recipeY-10；正文在 4+78-23。
    /// 泰拉的配方不分摆放形状，材料按顺序填进 3×3。
    /// </summary>
    private void DrawCraftingPage(BookPage page, int px, int py, int bottom)
    {
        var items = new List<string>();
        if (page.RecipeItem.Length > 0) { items.Add(page.RecipeItem); }
        items.AddRange(page.RecipeItems);

        int recipeX = px + (PageWidth / 2) - 49;
        int recipeY = py + 4;
        int drawn = 0;
        foreach (var item in items)
        {
            if (drawn >= 2) { break; }   // Patchouli 一页最多两个配方
            var recipe = _data.FindRecipe(item);
            string title = page.Title.Length > 0 && drawn == 0 ? page.Title : _data.ItemName(item);
            CenteredText(title, px + (PageWidth / 2), recipeY - 10, HeaderColor);
            Tex(BookTextures.Crafting, recipeX - 2, recipeY - 2, 100, 62, 0, 0, texW: 128, texH: 256);
            if (recipe is not null)
            {
                for (int i = 0; i < recipe.Ingredients.Count && i < 9; i++)
                {
                    var (ing, count) = recipe.Ingredients[i];
                    Item(ing, recipeX + ((i % 3) * 19) + 3, recipeY + ((i / 3) * 19) + 3, count);
                }
                Item(recipe.Result, recipeX + 79, recipeY + 22, recipe.ResultCount);
                if (recipe.Station.Length > 0) { Item(recipe.Station, recipeX + 79, recipeY + 41); }
            }
            else
            {
                Item(item, recipeX + 79, recipeY + 22);
            }
            drawn++;
            recipeY += 78;
        }
        // Patchouli 取 4 + 78n - 23（= 59），正好压在配方框最下面那条格线上（框画到 2+62=64）；
        // 泰拉字体字身更高，压得更明显，所以正文从框下沿再往下 3 单位开始。
        int textY = drawn == 0 ? py : py + 4 + (78 * (drawn - 1)) + 60 + 3;
        TextBlock(page.Text, px, textY, bottom);
    }

    // ── 组件 ────────────────────────────────────────────────────────

    /// <summary>GuiButtonCategory：20×20，图标在 (2,2)；上面盖一层同位置的书页贴图，未悬停时半透明 → 图标显得褪色。</summary>
    private void CategoryButton(BookCategory cat, int x, int y)
    {
        var rect = R(x, y, 20, 20);
        bool hover = rect.Contains(_mx, _my);
        bool locked = !IsUnlocked(_view, cat);
        if (locked)
        {
            Tex(BookTextures.Book, x + 2, y + 2, 16, 16, 250, 180, alpha: 0.7f);
        }
        else
        {
            _c.DrawItem(cat.IconItem, R(x + 2, y + 2, 16, 16), 1f);
        }
        if (!hover)
        {
            Tex(BookTextures.Book, x, y, 20, 20, x, y, alpha: 0.5f);
        }
        else
        {
            _frame.Tooltip = locked ? LockedText : cat.DisplayName;
        }
        if (!locked) { _frame.Hits.Add(new BookHit(rect, BookActionKind.OpenCategory, cat.Id)); }
    }

    /// <summary>GuiButtonEntry：116×10，半尺寸图标在 (1,1)，名字在 x+12；悬停时铺一条浅灰底。</summary>
    private void EntryButton(BookEntry entry, int x, int y)
    {
        var rect = R(x, y, PageWidth, 10);
        bool locked = !IsUnlocked(entry);
        if (rect.Contains(_mx, _my) && !locked)
        {
            _c.FillRect(rect, new Color32(0, 0, 0, 0x22));
        }
        if (locked)
        {
            Tex(BookTextures.Book, x + 1f, y + 1f, 16, 16, 250, 180, alpha: 0.7f, drawW: 8, drawH: 8);
        }
        else if (entry.IconItem.Length > 0)
        {
            _c.DrawItem(entry.IconItem, R(x + 1f, y + 1f, 8, 8), 1f);
        }
        int color = entry.EntryColor >= 0 ? entry.EntryColor : TextColor;
        float scale = TextScale();
        string name = locked ? LockedText : FitText(entry.DisplayName, (PageWidth - 12) * Unit, scale);
        _c.DrawText(name, X(x + 12), Y(y) + TextNudge(scale), Color32.Rgb(color, locked ? (byte)0x77 : (byte)0xFF), scale, bold: false);
        if (!locked) { _frame.Hits.Add(new BookHit(rect, BookActionKind.OpenEntry, entry.Id)); }
    }

    /// <summary>GuiBook.drawSeparator：贴图 (140,180,110,3)，页内居中，80% 不透明。</summary>
    private void Separator(int pageX, int y)
        => Tex(BookTextures.Book, pageX + (PageWidth / 2) - 55, y, 110, 3, 140, 180, alpha: 0.8f);

    /// <summary>GuiBook.drawPageFiller：128×128 的装饰，页内居中。</summary>
    private void Filler(int pageX, int pageY)
        => Tex(BookTextures.Filler, pageX + (PageWidth / 2) - 64, pageY + (PageHeight / 2) - 74, 128, 128, 0, 0, texW: 128, texH: 128);

    /// <summary>GuiButtonBook：悬停时取贴图右边那一格（u + 宽）。</summary>
    private void Button(int x, int y, int u, int v, int w, int h, BookActionKind kind, string tooltip)
    {
        var rect = R(x, y, w, h);
        bool hover = rect.Contains(_mx, _my);
        Tex(BookTextures.Book, x, y, w, h, u + (hover ? w : 0), v);
        if (hover) { _frame.Tooltip = tooltip; }
        _frame.Hits.Add(new BookHit(rect, kind));
    }

    private void Item(string key, int x, int y, int count = 1)
    {
        if (string.IsNullOrEmpty(key)) { return; }
        var rect = R(x, y, 16, 16);
        _c.DrawItem(key, rect, 1f);
        if (count > 1)
        {
            float s = TextScale() * 0.8f;
            string n = count.ToString();
            float w = _c.MeasureText(n, s, false);
            _c.DrawText(n, rect.Right - w, rect.Bottom - (_c.LineHeight * s * 0.8f), new Color32(255, 255, 255), s, bold: false);
        }
        if (rect.Contains(_mx, _my)) { _frame.Tooltip = _data.ItemName(key); }
    }

    // ── 图案 ────────────────────────────────────────────────────────

    /// <summary>
    /// AbstractPatternComponent：x∈[0,116]、y∈[16,80] 按 √n 分格，每格居中适配（边距 2、格距上限 16），
    /// 笔画 fromStroke(4)：浅色外描边 4、深色内线 1.6；起点蓝点、顶点灰点；READABLE 的静态电光（抖动 0.5、转角内收 0.2、末段 0.8）。
    /// </summary>
    private void DrawPatternGrid(List<HexPattern> patterns, int px, int py, bool strokeOrder = true)
    {
        if (patterns.Count == 0) { return; }
        int cols = (int)System.Math.Ceiling(System.Math.Sqrt(patterns.Count));
        int rows = (int)System.Math.Ceiling(patterns.Count / (double)cols);
        float cellW = 116f / cols;
        float cellH = 64f / rows;
        for (int p = 0; p < patterns.Count; p++)
        {
            float cx = px + (cellW * (p % cols));
            float cy = py + 16 + (cellH * (p / cols));
            DrawBookPattern(patterns[p], cx, cy, cellW, cellH, strokeOrder);
        }
    }

    private void DrawBookPattern(HexPattern pattern, float x, float y, float w, float h, bool strokeOrder = true)
    {
        var raw = HexGrid.PatternLinePoints(pattern, HexCoord.Origin, 1f, Vec2f.Zero);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var v in raw)
        {
            minX = System.Math.Min(minX, v.X); maxX = System.Math.Max(maxX, v.X);
            minY = System.Math.Min(minY, v.Y); maxY = System.Math.Max(maxY, v.Y);
        }
        float bw = System.Math.Max(maxX - minX, 0.001f), bh = System.Math.Max(maxY - minY, 0.001f);
        float size = System.Math.Min(16f, System.Math.Min((w - 4f) / bw, (h - 4f) / bh));
        float offX = x + (w / 2f) - (((minX + maxX) / 2f) * size);
        float offY = y + (h / 2f) - (((minY + maxY) / 2f) * size);

        var pts = new List<Vec2f>(raw.Count);
        foreach (var v in raw) { pts.Add(new Vec2f(X(offX + (v.X * size)), Y(offY + (v.Y * size)))); }

        // 源项目 ZappySettings：READABLE（带笔顺偏移）/ STATIC（readabilityOffset 0、末段比例 1）
        var zappy = PatternGeometry.MakeZappy(pts, PatternGeometry.FindDupIndices(pattern.Positions()), 10, 0.5f, 0f, 0.2f,
            strokeOrder ? PatternGeometry.DefaultReadabilityOffset : 0f,
            strokeOrder ? PatternGeometry.DefaultLastSegmentLenProportion : 1f, 0, 0);
        Polyline(zappy, 4f * Unit, Color32.Rgb(0xd2c8c8));
        Polyline(zappy, 1.6f * Unit, Color32.Rgb(0x554d54));
        for (int i = 1; i < pts.Count; i++)
        {
            _c.FillCircle(pts[i].X, pts[i].Y, 0.64f * Unit * 1.5f, Color32.Rgb(0xd2c8c8, 0x80));
        }
        if (strokeOrder)
        {
            _c.FillCircle(pts[0].X, pts[0].Y, 1.28f * Unit * 1.5f, Color32.Rgb(0x5b7bd7));
        }
    }

    private void Polyline(List<Vec2f> pts, float width, Color32 color)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            _c.DrawLine(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, width, color);
            _c.FillCircle(pts[i + 1].X, pts[i + 1].Y, width / 2f, color);   // 圆角接头
        }
        if (pts.Count > 0) { _c.FillCircle(pts[0].X, pts[0].Y, width / 2f, color); }
    }

    private static HexDir ParseDir(string s) => s switch
    {
        "NORTH_EAST" => HexDir.NorthEast,
        "EAST" => HexDir.East,
        "SOUTH_EAST" => HexDir.SouthEast,
        "SOUTH_WEST" => HexDir.SouthWest,
        "WEST" => HexDir.West,
        "NORTH_WEST" => HexDir.NorthWest,
        _ => HexDir.East,
    };

    // ── 文字 ────────────────────────────────────────────────────────

    /// <summary>缩放到 Patchouli 的 9 单位行高（再乘 <see cref="GlyphBoost"/>）。</summary>
    private float TextScale() => TextLineHeight * Unit * GlyphBoost / System.Math.Max(1f, _c.LineHeight);

    /// <summary>放大后字身变高了，往上提一点让它仍然落在 9 单位的行里。</summary>
    private float TextNudge(float scale) => -((_c.LineHeight * scale) - (TextLineHeight * Unit)) / 2f;

    private void Text(string text, int x, int y, int rgb)
    {
        float s = TextScale();
        _c.DrawText(text, X(x), Y(y) + TextNudge(s), Color32.Rgb(rgb), s, bold: false);
    }

    private void CenteredText(string text, int cx, int y, int rgb)
    {
        float s = TextScale();
        text = FitText(text, PageWidth * Unit, s);
        float w = _c.MeasureText(text, s, false);
        _c.DrawText(text, X(cx) - (w / 2f), Y(y) + TextNudge(s), Color32.Rgb(rgb), s, bold: false);
    }

    /// <summary>放不下就截断加省略号（标题不能横穿中缝）。</summary>
    private string FitText(string text, float maxW, float scale)
    {
        if (_c.MeasureText(text, scale, false) <= maxW) { return text; }
        for (int n = text.Length - 1; n > 0; n--)
        {
            string t = text.Substring(0, n) + "…";
            if (_c.MeasureText(t, scale, false) <= maxW) { return t; }
        }
        return text;
    }

    /// <summary>
    /// BookTextRenderer：页宽 116 自动换行，行高 9。泰拉字体比 MC 字体宽，
    /// 所以放不下时逐级缩小字号（最小 80%），仍放不下的行不画并记入 <see cref="BookFrame.OverflowLines"/>。
    /// </summary>
    private void TextBlock(string markup, int x, int y, int bottom)
    {
        if (string.IsNullOrEmpty(markup)) { return; }
        var segs = BookTextLayout.Parse(markup);
        float baseScale = TextScale();
        float avail = (bottom - y) * Unit;
        List<BookTextLine> lines = new();
        float scale = baseScale, lineH = TextLineHeight * Unit;
        for (float f = 1f; f >= 0.79f; f -= 0.05f)
        {
            scale = baseScale * f;
            lineH = TextLineHeight * Unit * f;
            float sc = scale;
            lines = BookTextLayout.Wrap(segs, (int)(PageWidth * Unit), t => (int)System.MathF.Ceiling(_c.MeasureText(t, sc, false)));
            if (lines.Count * lineH <= avail + 0.5f) { break; }
        }

        float ly = Y(y);
        float nudge = -((_c.LineHeight * scale) - lineH) / 2f;
        foreach (var line in lines)
        {
            if (ly + lineH > Y(bottom) + 0.5f)
            {
                _frame.OverflowLines++;
                continue;
            }
            float lx = X(x);
            foreach (var seg in line.Segments)
            {
                if (seg.Text.Length == 0) { continue; }
                bool isLink = seg.LinkTarget.Length > 0;
                float w = _c.MeasureText(seg.Text, scale, seg.Bold);
                var rect = new RectF(lx, ly, w, lineH);
                bool hover = isLink && rect.Contains(_mx, _my);
                int rgb = isLink ? (hover ? LinkHoverColor : LinkColor) : (seg.Rgb >= 0 ? seg.Rgb : TextColor);
                _c.DrawText(seg.Text, lx, ly + nudge, Color32.Rgb(rgb), scale, seg.Bold);
                if (seg.Underline || isLink)
                {
                    _c.FillRect(new RectF(lx, ly + lineH - System.MathF.Max(1f, Unit * 0.5f), w, System.MathF.Max(1f, Unit * 0.5f)),
                        Color32.Rgb(rgb, isLink ? (byte)0x90 : (byte)0xFF));
                }
                if (isLink && LinkUnlocked(seg.LinkTarget))
                {
                    _frame.Hits.Add(new BookHit(rect, BookActionKind.Link, seg.LinkTarget));
                    if (hover && seg.LinkTarget.StartsWith("http", System.StringComparison.Ordinal))
                    {
                        _frame.Tooltip = seg.LinkTarget;
                    }
                }
                lx += w;
            }
            ly += lineH;
        }
    }

    private bool LinkUnlocked(string target)
    {
        if (target.StartsWith("http", System.StringComparison.Ordinal)) { return true; }
        var e = _view.Document.FindEntry(target.Split('#')[0]);
        return e is null || IsUnlocked(e);
    }

    // ── 坐标 ────────────────────────────────────────────────────────

    private float X(float gx) => _ox + (gx * Unit);
    private float Y(float gy) => _oy + (gy * Unit);
    private RectF R(float gx, float gy, float gw, float gh) => new(X(gx), Y(gy), gw * Unit, gh * Unit);

    private void Tex(string tex, float gx, float gy, int w, int h, int u, int v, float alpha = 1f, int texW = 512, int texH = 256,
        float drawW = -1, float drawH = -1)
    {
        _ = texW;
        _ = texH;
        _c.DrawImage(tex, new RectF(u, v, w, h), R(gx, gy, drawW < 0 ? w : drawW, drawH < 0 ? h : drawH),
            new Color32(255, 255, 255, (byte)(alpha * 255)));
    }
}
