namespace HexCastingTerraria.Core.Ui;

// ── 泰拉原版观感的皮肤 ────────────────────────────────────────────────
//
// 与 <see cref="PatchouliSkin"/> 是**两套不同的布局哲学**，这是有意的：
//   帕秋莉版 = 一本摊开的书（双页、皮革封面、装订中缝）
//   原版版   = 泰拉自己的 UI 观感：矩形面板 + 左侧列表 + 右侧内容区 + 标题栏
//              （参照物是 Recipe Browser / Boss Checklist 那一类界面）
//
// 两者共用同一份内容（<see cref="BookDocument"/>）、同一个状态机（<see cref="BookView"/>）、
// 同一份契约（<see cref="BookSkin"/> / <see cref="BookMetrics"/> / <see cref="IBookCanvas"/>），
// **只有几何与配色不同** —— 所以不会出现"两套内容各写一遍然后慢慢对不上"。
//
// 配色取泰拉面板的深蓝灰 + 金色点缀：深蓝面板是泰拉 UI 的底色，
// 金色是它惯用的强调（选中、标题、图标描边），不用高饱和色块（硬规则第 3 条）。
public sealed class VanillaSkin : BookSkin
{
    // 泰拉面板的观感要点（上一版全都没做，做出来是个"通用深蓝面板"）：
    //   ① 圆角——泰拉面板是约 3~4px 的切角，不是直角
    //   ② 竖向渐变——上亮下暗，不是平涂
    //   ③ 物品格——泰拉最有辨识度的元素就是那种圆角格子 + 内高光
    //   ④ 选中态 = 金边 + 提亮，而不是一条线
    //   ⑤ 文字带 1px 深色投影（泰拉所有 UI 文字都这样）
    private static readonly Color32 PanelTop = Color32.FromHex("#3A4A66");
    private static readonly Color32 PanelBot = Color32.FromHex("#1E2636");
    private static readonly Color32 SlotTop = Color32.FromHex("#2C3850");
    private static readonly Color32 SlotBot = Color32.FromHex("#161C28");
    private static readonly Color32 SlotEdge = Color32.FromHex("#5A6E90");
    private static readonly Color32 SlotEdgeLit = Color32.FromHex("#8FA6C8");
    private static readonly Color32 SelFill = Color32.FromHex("#4A5F86");
    private static readonly Color32 Gold = Color32.FromHex("#D8B45C");
    private static readonly Color32 GoldDim = Color32.FromHex("#8A7434");
    private static readonly Color32 TextMain = Color32.FromHex("#F0F2F6");
    private static readonly Color32 TextDim = Color32.FromHex("#A8B6CA");
    private static readonly Color32 Shadow = new(0, 0, 0, 170);
    private static readonly Color32 ArrowOn = Color32.FromHex("#D8B45C");
    private static readonly Color32 ArrowOff = Color32.FromHex("#4A5A74");

    // 真贴图是**纯白**的，颜色全靠 tint —— 这几个就是泰拉面板的实际染色值。
    private static readonly Color32 White = new(255, 255, 255);
    private static readonly Color32 TintDim = new(150, 150, 150);
    private static readonly Color32 PanelTint = Color32.FromHex("#3E4C6B");
    private static readonly Color32 BorderTint = Color32.FromHex("#7C93BF");
    private static readonly Color32 TitleTint = Color32.FromHex("#4E6288");
    private static readonly Color32 BorderLit = Color32.FromHex("#9FB4DA");
    private static readonly Color32 BorderDim = Color32.FromHex("#2A3448");

    private const float Pad = 8f;
    private const float TitleH = 26f;
    private const float ListW = 196f;
    private const float FooterH = 22f;
    private const float RowH = 26f;      // 物品格留出行距，24px 格 + 2px 缝
    private const float SlotSize = 24f;

    public override string Name => "Vanilla";

    /// <summary>
    /// 窗口缩放。**默认 1** —— 泰拉 UI 本身就是 1:1 的。
    /// （帕秋莉版默认 2，是因为它的图集是按 MC 的 GUI scale 2 画的 —— 两套的默认尺度本来就不该一样。）
    /// </summary>
    public float Scale { get; set; } = 1f;

    public override BookMetrics Measure(BookView view, RectF viewport)
    {
        float s = Scale < 1f ? 1f : Scale;
        var m = new BookMetrics();

        float w = System.MathF.Min(viewport.W - (40f * s), 560f * s);
        float h = System.MathF.Min(viewport.H - (40f * s), 360f * s);
        float x = System.MathF.Round(viewport.X + ((viewport.W - w) / 2f));
        float y = System.MathF.Round(viewport.Y + ((viewport.H - h) / 2f));

        m.Book = new RectF(x, y, w, h);
        m.Header = new RectF(x, y, w, TitleH * s);

        float listX = x + (Pad * s);
        float listY = y + (TitleH * s) + (Pad * s);
        float listH = h - (TitleH * s) - (FooterH * s) - (Pad * 2f * s);

        m.LeftPage = new RectF(listX, listY, ListW * s, listH);
        m.RightPage = new RectF(listX + (ListW * s) + (Pad * s), listY,
                                w - (ListW * s) - (Pad * 3f * s), listH);

        m.TextLineWidth = (int)(m.RightPage.W - (Pad * s));
        m.TextLinesPerPage = System.Math.Max(1, (int)(m.RightPage.H / LineHeight));

        float fy = y + h - (FooterH * s) + (4f * s);
        m.BackButton = new RectF(x + (Pad * s), fy, 52f * s, 14f * s);
        m.PrevArrow = new RectF(x + w - (44f * s), fy, 14f * s, 14f * s);
        m.NextArrow = new RectF(x + w - (24f * s), fy, 14f * s, 14f * s);

        int rows = view.Kind == BookViewKind.Categories
            ? view.Document.Categories.Count
            : (view.CurrentCategory?.Entries.Count ?? 0);

        for (int i = 0; i < rows; i++)
        {
            m.EntryRows.Add(new RectF(listX, listY + (i * RowH * s), ListW * s, RowH * s));
        }

        int spreads = view.SpreadCount;
        for (int i = 0; i < spreads; i++)
        {
            m.PageDots.Add(new RectF(x + (w / 2f) - ((spreads * 10f * s) / 2f) + (i * 10f * s),
                                     fy + (4f * s), 6f * s, 6f * s));
        }

        return m;
    }

    public override void Draw(IBookCanvas canvas, BookView view, BookMetrics m)
    {
        float s = Scale < 1f ? 1f : Scale;

        // 面板 = **纯白 9 宫格底 + 纯白 9 宫格描边，两层叠起来由代码染色**。
        // 这是从 `_tools/vanilla_ui_sheet.png` 第 1、2 张看出来的泰拉做法 ——
        // 我上一版是手画渐变矩形，从根上就不是同一套做法。
        Panel(canvas, m.Book, PanelTint, BorderTint, s);

        // 标题栏：同两张贴图，换更亮的染色
        Panel(canvas, m.Header, TitleTint, BorderLit, s);

        string title = view.Kind switch
        {
            BookViewKind.Categories => Chrome.ContentsTitle,
            BookViewKind.Entry => titleOf(view),
            _ => Chrome.CoverTitle,
        };
        ShadowText(canvas, title, m.Header.X + (m.Header.W / 2f), m.Header.Y + (5f * s),
                   TextMain, BookTextAlign.Center);

        if (view.Kind == BookViewKind.Categories)
        {
            DrawCategoryList(canvas, view, m, s);
        }
        else
        {
            DrawEntryList(canvas, view, m, s);
        }

        DrawFooter(canvas, view, m, s);
    }

    // ── 泰拉观感的原语（用真贴图）──────────────────────────────────

    /// <summary>泰拉面板：底 + 边两层 9 宫格。贴图是纯白的，颜色靠 tint。</summary>
    private static void Panel(IBookCanvas canvas, RectF r, Color32 tint, Color32 borderTint, float s)
    {
        NinePatch.Draw(canvas, "panel_bg", new RectF(0, 0, 28, 28), r, 8f * s, tint);
        NinePatch.Draw(canvas, "panel_border", new RectF(0, 0, 28, 28), r, 8f * s, borderTint);
    }

    /// <summary>凹陷的内区（`UI_InnerPanelBackground`，本身就是深色，白 tint 即可）。</summary>
    private static void InnerPanel(IBookCanvas canvas, RectF r, float s)
    {
        NinePatch.Draw(canvas, "panel_inner", new RectF(0, 0, 24, 27), r, 6f * s, White);
        NinePatch.Draw(canvas, "panel_border", new RectF(0, 0, 28, 28), r, 8f * s, BorderDim);
    }

    /// <summary>
    /// 物品格：`Slot_Back`（渐变的底）+ `Slot_Front`（蓝细描边），选中时再叠
    /// `Slot_Selection`（**金色描边** —— 这是泰拉原版的选中态，见对照图第 8 张）。
    /// </summary>
    private static void Slot(IBookCanvas canvas, RectF r, float s, bool selected)
    {
        NinePatch.Draw(canvas, "slot_back", new RectF(0, 0, 70, 70), r, 12f * s, White);
        NinePatch.Draw(canvas, "slot_front", new RectF(0, 0, 70, 70), r, 12f * s, White);
        if (selected)
        {
            NinePatch.Draw(canvas, "slot_selection", new RectF(0, 0, 70, 70), r, 12f * s, White);
        }
    }

    private static void Button(IBookCanvas canvas, RectF r, float s, bool lit)
    {
        NinePatch.Draw(canvas, "button_backing", new RectF(0, 0, 22, 22), r, 6f * s,
                       lit ? White : TintDim);
    }

    private void DrawCategoryList(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        InnerPanel(canvas, m.RightPage, s);

        var cats = view.Document.Categories;
        // 列表必须**裁剪**在左栏内：行数 × 行高很容易超过可用高度，
        // 上一版没裁，第 7 行"Patterns"直接画到窗口外面去了。
        // （滚动条等 Client 接线时再接；这里先保证不越界。）
        canvas.PushClip(m.LeftPage);
        for (int i = 0; i < cats.Count && i < m.EntryRows.Count; i++)
        {
            DrawSlotRow(canvas, m.EntryRows[i], s,
                        cats[i].Id == view.CurrentCategoryId,
                        Label(cats[i].DisplayName, cats[i].NameKey), null);
        }
        canvas.PopClip();

        var cat = view.CurrentCategory;
        if (cat is null) { return; }

        float ty = m.RightPage.Y + (Pad * s);
        ShadowText(canvas, Label(cat.DisplayName, cat.NameKey), m.RightPage.X + (Pad * s), ty, Gold);
        ty += LineHeight + (6f * s);

        string desc = Label(cat.DisplayDescription, cat.DescriptionKey);
        if (!string.IsNullOrEmpty(desc))
        {
            var pages = BookTextLayout.Layout(desc, m.TextLineWidth, 8, t => canvas.MeasureText(t));
            foreach (var ln in pages[0].Lines)
            {
                ShadowText(canvas, ln.PlainText(), m.RightPage.X + (Pad * s), ty, TextDim);
                ty += LineHeight;
            }
        }

        ShadowText(canvas, $"{cat.Entries.Count} {Chrome.EntriesSuffix}",
                   m.RightPage.X + (Pad * s), m.RightPage.Bottom - (Pad * s) - LineHeight, TextDim);
    }

    private void DrawEntryList(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        InnerPanel(canvas, m.RightPage, s);

        var cat = view.CurrentCategory;
        if (cat is not null)
        {
            canvas.PushClip(m.LeftPage);   // 同 DrawCategoryList：行数会超出可用高度，必须裁
            for (int i = 0; i < cat.Entries.Count && i < m.EntryRows.Count; i++)
            {
                DrawSlotRow(canvas, m.EntryRows[i], s,
                            cat.Entries[i].Id == view.CurrentEntryId,
                            Label(cat.Entries[i].DisplayName, cat.Entries[i].NameKey), null);
            }
            canvas.PopClip();
        }

        var entry = view.CurrentEntry;
        if (entry is null) { return; }

        float ty = m.RightPage.Y + (Pad * s);
        ShadowText(canvas, Label(entry.DisplayName, entry.NameKey),
                   m.RightPage.X + (Pad * s), ty, Gold);
        canvas.HLine(m.RightPage.X + (Pad * s), ty + LineHeight + (3f * s),
                     m.RightPage.W - (Pad * 2f * s), GoldDim, 1);
        ty += LineHeight + (9f * s);

        int maxLines = System.Math.Max(1, (int)((m.RightPage.Bottom - ty - (Pad * s)) / LineHeight));
        int drawn = 0;
        for (int p = view.LeftPageIndex; p < entry.Pages.Count && drawn < maxLines; p++)
        {
            var page = entry.Pages[p];
            if (string.IsNullOrEmpty(page.Text)) { continue; }

            var laid = BookTextLayout.Layout(page.Text, m.TextLineWidth, maxLines - drawn,
                t => canvas.MeasureText(t));
            foreach (var ln in laid[0].Lines)
            {
                ShadowText(canvas, ln.PlainText(), m.RightPage.X + (Pad * s), ty, TextMain);
                ty += LineHeight;
                drawn++;
            }
        }
    }

    private void DrawFooter(IBookCanvas canvas, BookView view, BookMetrics m, float s)
    {
        var bar = new RectF(m.Book.X + (3f * s), m.Book.Bottom - (FooterH * s),
                            m.Book.W - (6f * s), (FooterH * s) - (3f * s));
        Panel(canvas, bar, TitleTint, BorderDim, s);

        ShadowText(canvas, Chrome.Back, m.BackButton.X, m.BackButton.Y, TextDim);
        ShadowText(canvas, $"{view.SpreadNumber} / {System.Math.Max(1, view.SpreadCount)}",
                   m.Book.X + (m.Book.W / 2f), m.BackButton.Y, TextDim, BookTextAlign.Center);

        DrawArrow(canvas, m.PrevArrow, s, view.SpreadNumber > 1);
        DrawArrow(canvas, m.NextArrow, s, view.SpreadNumber < view.SpreadCount);
    }

    // ── 泰拉观感的原语 ──────────────────────────────────────────────

    /// <summary>竖向渐变填充（用水平细条模拟；<paramref name="s"/> 越大条越粗）。</summary>
    private static void FillPanel(IBookCanvas canvas, RectF r, Color32 top, Color32 bot, float s)
    {
        int bands = System.Math.Max(1, (int)(r.H / (2f * s)));
        float bh = r.H / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = bands == 1 ? 0f : i / (float)(bands - 1);
            canvas.FillRect(new RectF(r.X, r.Y + (i * bh), r.W, System.MathF.Ceiling(bh)),
                            top.Lerp(bot, t));
        }
    }

    /// <summary>圆角：把四个角各切掉 <paramref name="s"/> 像素（泰拉的面板圆角很小）。</summary>
    private static void ChamferCorners(IBookCanvas canvas, RectF r, Color32 c, float s)
    {
        float k = s;
        canvas.FillRect(new RectF(r.X, r.Y, k, k), c);
        canvas.FillRect(new RectF(r.Right - k, r.Y, k, k), c);
        canvas.FillRect(new RectF(r.X, r.Bottom - k, k, k), c);
        canvas.FillRect(new RectF(r.Right - k, r.Bottom - k, k, k), c);
    }

    /// <summary>描边 + 切角。先画直角边，再把四角用底色盖掉一半，形成 2 级切角。</summary>
    private static void ChamferEdge(IBookCanvas canvas, RectF r, Color32 edge, float s)
    {
        canvas.StrokeRect(r, edge, System.Math.Max(1, (int)s));
    }

    /// <summary>外发光/投影：一圈透明度递减的描边。</summary>
    private static void FillGlow(IBookCanvas canvas, RectF r, Color32 c, float s)
    {
        canvas.StrokeRect(r, c.WithAlpha((byte)(c.A / 2)), System.Math.Max(1, (int)s));
    }

    /// <summary>泰拉风格的文字：右下 1px 深色投影 + 正文。</summary>
    private static void ShadowText(IBookCanvas canvas, string text, float x, float y, Color32 color,
                                   BookTextAlign align = BookTextAlign.Left)
    {
        canvas.DrawText(text, x + 1, y + 1, Shadow, align);
        canvas.DrawText(text, x, y, color, align);
    }

    /// <summary>
    /// 物品格样式的行 —— 泰拉最有辨识度的元素。
    /// 圆角凹槽 + 内高光 + 边框；选中时换成金色描边 + 提亮的填充。
    /// </summary>
    private static void DrawSlotRow(IBookCanvas canvas, RectF row, float s, bool selected, string label, string? icon)
    {
        var slot = new RectF(row.X + (2f * s), row.Y + (1f * s), SlotSize * s, SlotSize * s);
        Slot(canvas, slot, s, selected);

        // 图标占位（真图标以后由 Client 贴物品图集；这里是 8x8 的方块）
        canvas.FillRect(new RectF(slot.X + (8f * s), slot.Y + (8f * s), 8f * s, 8f * s),
                        selected ? Gold : GoldDim);

        if (icon is not null)
        {
            ShadowText(canvas, icon, slot.Right + (6f * s), slot.Y, selected ? TextMain : TextDim);
        }
        else
        {
            ShadowText(canvas, label, slot.Right + (6f * s), row.Y + (3f * s),
                       selected ? TextMain : TextDim);
        }
    }

    private static void DrawArrow(IBookCanvas canvas, RectF r, float s, bool on)
    {
        var c = on ? ArrowOn : ArrowOff;
        canvas.FillRect(r, on ? GoldDim : SlotBot);
        // 一个小三角（用三层递窄的横条近似）
        float cx = r.X + (r.W / 2f);
        for (int i = 0; i < 3; i++)
        {
            canvas.HLine(r.X + (4f * s) + (i * s), r.Y + ((4 + (i * 2)) * s),
                         r.W - ((8f + (i * 2f)) * s), c, System.Math.Max(1, (int)s));
        }
    }

    /// <summary>
    /// 行高。与字模（`_tools/gen_font_atlas.ps1`）烘出来的 lineH 一致；换字体改这一处。
    /// </summary>
    private const int LineHeight = 19;

    private static string titleOf(BookView view)
    {
        var e = view.CurrentEntry;
        return e is null ? "Hex Casting" : Label(e.DisplayName, e.NameKey);
    }

    private static string Label(string display, string key)
        => !string.IsNullOrEmpty(display) ? display : key;
}
