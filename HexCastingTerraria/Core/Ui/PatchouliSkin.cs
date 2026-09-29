namespace HexCastingTerraria.Core.Ui;

// ── 帕秋莉版皮肤 ──────────────────────────────────────────────────────
//
// 观感与几何照 Patchouli 的 `client/book/gui/GuiBook.java` 一族的做法
//（VazkiiMods/Patchouli，CC BY-NC-SA 3.0，见 CREDITS.md），
// 切片坐标是用 `_tools/zoom_atlas.ps1` 放大叠网格**读**出来的。
//
// ## 为什么这个文件在 Core/ 而不是 Client/
//
// 它只调用 <see cref="IBookCanvas"/>（不引用 XNA / tModLoader），所以能放 Core。
// 这一点很关键：**同一份皮肤代码同时驱动游戏内渲染和离屏出图**。
// 如果皮肤写在 Client/，离屏工程（drawtest）编译不了它，就只能另写一份"预览版"，
// 两份必然长歪 —— 而"出图看着好、进游戏变样"正是要避免的事。
// 真正需要 XNA 的只有 `IBookCanvas` 的实现（包 SpriteBatch），那个留在 Client/。
//
// ## 尺度
//
// 图集切片的**原始**尺寸就是原版在 GUI scale 1 下的逻辑尺寸。
// 但直接用 1 倍会让字显得过大（行高/页面高 12.5%，而原版是 7.7%，差 1.6 倍）——
// 因为游戏里字体是固定的 FontAssets.MouseText，改不了字号，杠杆只能是书的大小。
// 所以默认 Scale = 2：这也**正好是忠实的** —— Patchouli 在 MC 里就是在 GUI scale 2 下
// 画这张图集的，物理上就是 2 倍最近邻放大。
public sealed class PatchouliSkin : BookSkin
{
    // ── 图集切片（zoom_atlas.ps1 读出来的绝对坐标）────────────────────
    private static readonly RectF SliceBook = new(4, 4, 272, 181);
    private static readonly RectF SliceBookmark = new(0, 188, 135, 18);
    private static readonly RectF SliceButtons = new(135, 195, 40, 10);

    // 纸面与中缝（图集坐标）
    private const float PaperX0 = 22f;
    private const float PaperY0 = 18f;
    private const float PaperX1 = 262f;
    private const float PaperY1 = 170f;
    private const float SpineX = 145f;

    // ── 调色板（第 3 节那 10 条硬规则里的低饱和一套）──────────────────
    private static readonly Color32 Ink = Color32.FromHex("#2E2A24");
    private static readonly Color32 Ink2 = Color32.FromHex("#5E5647");
    private static readonly Color32 Accent = Color32.FromHex("#6E5A9E");
    private static readonly Color32 AccentLt = Color32.FromHex("#8A73BE");
    private static readonly Color32 Rule = Color32.FromHex("#C0A87E");
    private static readonly Color32 Card = Color32.FromHex("#EFE4C8");
    private static readonly Color32 ArrowOn = Color32.FromHex("#8A7350");
    private static readonly Color32 ArrowOff = Color32.FromHex("#C0A87E");
    private static readonly Color32 Tint = new(255, 255, 255);

    public override string Name => "Patchouli";

    /// <summary>书体缩放。2 = 还原原版 GUI scale 2 的显示尺度（见文件头说明）。</summary>
    public float Scale { get; set; } = 2f;

    public override BookMetrics Measure(BookView view, RectF viewport)
    {
        float s = Scale < 1f ? 1f : Scale;
        var m = new BookMetrics();

        float bw = SliceBook.W * s;
        float bh = SliceBook.H * s;

        // 居中放；放不下就按比例缩到视口内（保持整数缩放会更好看，但先保证不越界）
        float ox = viewport.X + ((viewport.W - bw) / 2f);
        float oy = viewport.Y + ((viewport.H - bh) / 2f);
        ox = System.MathF.Round(ox);
        oy = System.MathF.Round(oy);

        m.Book = new RectF(ox, oy, bw, bh);
        m.LeftPage = Map(PaperX0, PaperY0, SpineX - 8f - PaperX0, PaperY1 - PaperY0, ox, oy, s);
        m.RightPage = Map(SpineX + 8f, PaperY0, PaperX1 - 6f - (SpineX + 8f), PaperY1 - PaperY0, ox, oy, s);
        m.Header = new RectF(m.LeftPage.X, m.LeftPage.Y, m.LeftPage.W, 0f);

        m.TextLineWidth = (int)(m.LeftPage.W - (6f * s));
        m.TextLinesPerPage = 1;   // 由 Draw 按页面高度重算（这里只放个非零值）
        // 页码点 + 翻页箭头（右页底角，与已定稿的出图一致）
        float ay = Map(PaperY1 - 16f, oy, s);
        m.PrevArrow = new RectF(Map(PaperX1 - 30f, ox, s), ay, 7f * s, 7f * s);
        m.NextArrow = new RectF(Map(PaperX1 - 20f, ox, s), ay, 7f * s, 7f * s);
        m.BackButton = new RectF(m.Book.X + (4f * s), m.Book.Y + (4f * s), 12f * s, 12f * s);

        int spreads = view.SpreadCount;
        for (int i = 0; i < spreads; i++)
        {
            m.PageDots.Add(new RectF(
                m.LeftPage.X + (4f * s) + (i * 9f * s),
                m.LeftPage.Bottom - (16f * s),
                5f * s, 5f * s));
        }

        // 目录页的右页 = 所选分类的**条目列表**。
        // 原版那本是「目录 → 条目列表 → 页」三层，我上一版漏了中间这层：
        // 点分类直接跳进条目页，而 DrawCategories 又从不填 EntryRows ——
        // 于是右页什么也点不动。**命中区必须在 Measure 里算**，
        // 这样绘制与命中用的是同一份矩形（不会"画在这儿、点在那儿"）。
        if (view.Kind == BookViewKind.Categories && view.CurrentCategory is { } selCat)
        {
            float ry = oy + ((PaperY0 + 34f) * s);
            for (int i = 0; i < selCat.Entries.Count; i++)
            {
                var row = new RectF(m.RightPage.X, ry + (i * 22f * s), m.RightPage.W, 22f * s);
                if (row.Bottom > m.RightPage.Bottom) { break; }   // 放不下的不参与命中
                m.EntryRows.Add(row);
            }
        }

        return m;
    }

    public override void Draw(IBookCanvas canvas, BookView view, BookMetrics m)
    {
        float s = Scale < 1f ? 1f : Scale;
        float ox = m.Book.X;
        float oy = m.Book.Y;

        // 书体 + 书签 + 底角按钮：整块贴原图集（tint 全白 = 不改色）
        canvas.DrawImage("book", SliceBook, m.Book, Tint);
        canvas.DrawImage("bookmark", SliceBookmark,
            new RectF(ox, Map(188f, oy, s), 135f * s, 18f * s), Tint);
        canvas.DrawImage("buttons", SliceButtons,
            new RectF(Map(135f, ox, s), Map(195f, oy, s), 40f * s, 10f * s), Tint);

        switch (view.Kind)
        {
            case BookViewKind.Cover:
                DrawCover(canvas, view, m, s);
                break;
            case BookViewKind.Categories:
                DrawCategories(canvas, view, m, s);
                break;
            default:
                DrawEntry(canvas, view, m, s);
                break;
        }
    }

    // ── 封面 ────────────────────────────────────────────────────────
    private void DrawCover(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        string title = string.IsNullOrEmpty(view.Document.DisplayTitle)
            ? "The Hex Book"
            : view.Document.DisplayTitle;

        float cxx = m.Book.X + (m.Book.W / 2f);
        float yy = m.Book.Y + (m.Book.H * 0.42f);
        canvas.DrawText(title, cxx, yy, Ink, BookTextAlign.Center, bold: true, scale: (int)System.MathF.Max(1f, s));
        canvas.HLine(cxx - (60f * s), yy + (26f * s), 120f * s, Rule);
    }

    // ── 分类页：左页列表 + 右页预览 ──────────────────────────────────
    private void DrawCategories(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        var doc = view.Document;
        int lh = LineHeightOf(canvas);

        float rowH = 21f * s;
        float y0 = Map(PaperY0 + 2f, m.Book.Y, s);
        int sel = -1;
        for (int i = 0; i < doc.Categories.Count; i++)
        {
            if (doc.Categories[i].Id == view.CurrentCategoryId) { sel = i; }
        }

        // ⚠️ 列表必须**裁剪**在纸面内。
        // 上一版忘了加（只在 VanillaSkin 加了），而旧的图案浏览器有 **19 个分类** ——
        // 一页放不下 7 个以上，第 8 个开始就画到书外面去了，看起来就是"字错位"。
        canvas.PushClip(m.LeftPage);

        for (int i = 0; i < doc.Categories.Count; i++)
        {
            var cat = doc.Categories[i];
            float ry = y0 + (i * rowH);
            float ty = ry + ((rowH - lh) / 2f);
            bool on = i == sel;

            if (on)
            {
                var band = new RectF(m.LeftPage.X + (4f * s), ty - (4f * s), m.LeftPage.W + (4f * s), lh + (8f * s));
                canvas.FillRect(band, Accent.WithAlpha(46));
                canvas.FillRect(new RectF(band.X, band.Y, 3f * s, band.H), AccentLt);
            }

            canvas.DrawText(Label(cat.DisplayName, cat.NameKey), m.LeftPage.X + (12f * s), ty, on ? Ink : Ink2);
            canvas.DrawText(cat.Entries.Count.ToString(),
                m.Book.X + (Map(SpineX - 26f, 0f, s)), ty, Ink2, BookTextAlign.Right);
        }

        canvas.PopClip();

        // 右页：所选分类的**条目列表**（原版三层里的中间那层）
        if (sel >= 0)
        {
            var cat = doc.Categories[sel];
            float ry = Map(PaperY0 + 4f, m.Book.Y, s);
            canvas.DrawText(Label(cat.DisplayName, cat.NameKey), m.RightPage.X, ry, Ink, BookTextAlign.Left, bold: true);
            canvas.HLine(m.RightPage.X, ry + lh + (2f * s), m.RightPage.W, Rule);

            float ly = ry + lh + (8f * s);
            // 条目多于一页放得下时要裁 —— 和左边分类列表同一个道理
            canvas.PushClip(new RectF(m.RightPage.X, ly, m.RightPage.W, m.RightPage.Bottom - ly));

            for (int i = 0; i < cat.Entries.Count && i < m.EntryRows.Count; i++)
            {
                var row = m.EntryRows[i];
                bool on = cat.Entries[i].Id == view.CurrentEntryId;

                if (on)
                {
                    canvas.FillRect(row, Accent.WithAlpha(46));
                    canvas.FillRect(new RectF(row.X, row.Y, 2f * s, row.H), AccentLt);
                }

                canvas.DrawText(Label(cat.Entries[i].DisplayName, cat.Entries[i].NameKey),
                    row.X + (6f * s), row.Y + (1f * s), on ? Ink : Ink2);
            }

            canvas.PopClip();
        }
    }

    // ── 条目双页：左页标题 + 正文，右页继续 ──────────────────────────
    private void DrawEntry(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        var entry = view.CurrentEntry;
        if (entry is null) { return; }

        int lh = LineHeightOf(canvas);
        float ty = Map(PaperY0 + 2f, m.Book.Y, s);

        // 标题也走排版：太长就折行，不许横穿中缝。
        // ⚠️ 行宽一律用 Measure 里算好的 m.TextLineWidth，**不要**在这里另取 m.LeftPage.W ——
        //    上一版就是这么写的，两个值差 12px，出图后正文正好越过中缝。
        //    同一件事有两个值，迟早对不上。
        var titlePg = BookTextLayout.Layout(Label(entry.DisplayName, entry.NameKey),
            m.TextLineWidth, 2, t => canvas.MeasureText(t));
        foreach (var ln in titlePg[0].Lines)
        {
            canvas.DrawText(ln.PlainText(), m.LeftPage.X, ty, Ink, BookTextAlign.Left, bold: true);
            ty += lh;
        }

        ty += 2f * s;
        canvas.HLine(m.LeftPage.X, ty, m.LeftPage.W, Rule);
        ty += 6f * s;

        // 每页能放几行 —— 算出来，不写死
        int maxLines = (int)((m.LeftPage.Bottom - ty) / lh);
        if (maxLines < 1) { maxLines = 1; }

        int drawn = 0;
        for (int p = view.LeftPageIndex; p < entry.Pages.Count && drawn < maxLines; p++)
        {
            var page = entry.Pages[p];
            string text = page.Text;
            if (string.IsNullOrEmpty(text)) { continue; }

            var laid = BookTextLayout.Layout(text, m.TextLineWidth, maxLines - drawn,
                t => canvas.MeasureText(t));
            foreach (var ln in laid[0].Lines)
            {
                canvas.DrawText(ln.PlainText(), m.LeftPage.X, ty, Ink2);
                ty += lh;
                drawn++;
            }
        }

        // 右页：配方框（有配方时）
        var cur = view.CurrentEntry;
        if (cur is not null && view.RightPageIndex >= 0 && view.RightPageIndex < cur.Pages.Count)
        {
            var rp = cur.Pages[view.RightPageIndex];
            if (rp.Kind == BookPageKind.Crafting)
            {
                var box = new RectF(m.RightPage.X, m.RightPage.Y + (10f * s), m.RightPage.W, 52f * s);
                canvas.FillRect(box, Card.WithAlpha(150));
                canvas.StrokeRect(box, Rule, 1);
                canvas.FillRect(new RectF(box.X + (8f * s), box.Y + (12f * s), 16f * s, 16f * s), Accent);
                canvas.DrawText("crafting", box.X + (32f * s), box.Y + (8f * s), Ink);
                if (!string.IsNullOrEmpty(rp.RecipeItem))
                {
                    canvas.DrawText(rp.RecipeItem, box.X + (32f * s), box.Y + (30f * s), Ink2);
                }
            }
        }

        // 页脚：页码 + 翻页箭头
        string foot = $"{view.SpreadNumber} / {System.Math.Max(1, view.SpreadCount)}";
        canvas.DrawText(foot, m.LeftPage.X, m.LeftPage.Bottom - (16f * s), Ink2);
        canvas.FillRect(m.PrevArrow, view.SpreadNumber > 1 ? ArrowOn : ArrowOff);
        canvas.FillRect(m.NextArrow, view.SpreadNumber < view.SpreadCount ? ArrowOn : ArrowOff);
    }

    // ── 工具 ────────────────────────────────────────────────────────

    /// <summary>图集坐标 → 屏幕坐标（只处理单轴，调用处要传对应轴的偏移）。</summary>
    private static float Map(float atlasValue, float origin, float s) => origin + (atlasValue * s);

    private static RectF Map(float x, float y, float w, float h, float ox, float oy, float s)
        => new(ox + (x * s), oy + (y * s), w * s, h * s);

    /// <summary>
    /// 行高。字模的行高只有真实画布知道（<see cref="IBookCanvas.MeasureText"/> 只能量宽），
    /// 所以这里定一个约定值 —— 与 `_tools/gen_font_atlas.ps1` 烘出来的 lineH 一致（19@14px）。
    /// 换字体时改这一处。
    /// </summary>
    private const int LineHeightBase = 19;

    private static int LineHeightOf(IBookCanvas canvas) => LineHeightBase;

    /// <summary>显示名优先；没有就退回本地化键（总比画空白强）。</summary>
    private static string Label(string display, string key)
        => !string.IsNullOrEmpty(display) ? display : key;
}
