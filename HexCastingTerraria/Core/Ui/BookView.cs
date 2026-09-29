namespace HexCastingTerraria.Core.Ui;

// ── 书本的视图状态机 ──────────────────────────────────────────────────
//
// 移植自 Patchouli 的 `client/book/gui/`（GuiBook / GuiBookLanding / GuiBookEntry /
// GuiBookEntryList，VazkiiMods/Patchouli，CC BY-NC-SA 3.0，见 CREDITS.md）。
//
// **这里没有一行绘制代码**，只有「现在看的是哪一层、哪一页」。两个理由：
//   1. Core/ 不许引用 XNA（断言①），所以状态机能离线测；
//   2. 「翻页越界」「返回层级错乱」这类毛病在真机上要靠手点才能发现，
//      放在这里就能用断言钉死。
//
// 所有会改变状态的入口都返回 **bool（是否真的动了）**，而不是 void：
// 调用方要决定「真的翻页了才放音效」，断言要判「到边界就不许再动」，
// 都需要这个信息。返回 void 再让调用方自己比对前后状态，等于把同一件事做两遍。

/// <summary>书当前停在哪一层。</summary>
public enum BookViewKind
{
    /// <summary>封面。</summary>
    Cover = 0,

    /// <summary>分类网格（落地页）。</summary>
    Categories = 1,

    /// <summary>条目内容（双页）。</summary>
    Entry = 2,
}

/// <summary>
/// 书的导航状态。
///
/// 生命周期：<c>Open()</c> 从封面进分类页；选分类进条目；<c>Back()</c> 逐级退。
/// 退到封面之后再调 <c>Back()</c> 返回 false —— 由调用方决定「关书」，
/// 状态机不替它做这个决定（UI 里"退到封面就自动关"和"要再按一次才关"都是合理设计）。
/// </summary>
public sealed class BookView
{
    public BookView(BookDocument doc)
    {
        Document = doc ?? new BookDocument();
        Reset();
    }

    public BookDocument Document { get; }

    public BookViewKind Kind { get; private set; } = BookViewKind.Cover;

    /// <summary>当前分类（<see cref="BookViewKind.Categories"/> 与 <see cref="BookViewKind.Entry"/> 时有效）。</summary>
    public string CurrentCategoryId { get; private set; } = string.Empty;

    /// <summary>当前条目 id（仅 <see cref="BookViewKind.Entry"/> 时有效）。</summary>
    public string CurrentEntryId { get; private set; } = string.Empty;

    /// <summary>
    /// 当前是条目的第几页（**内容页**，不是"第几个跨页"）。
    /// 双页渲染时左页 = 偶数页，右页 = 下一页；单页渲染时就是这一页。
    /// </summary>
    public int PageIndex { get; private set; }

    /// <summary>当前条目的总页数（没有条目时是 0）。</summary>
    public int PageCount
    {
        get
        {
            var e = CurrentEntry;
            return e is null ? 0 : e.Pages.Count;
        }
    }

    public BookCategory? CurrentCategory => Document.FindCategory(CurrentCategoryId);

    public BookEntry? CurrentEntry
        => string.IsNullOrEmpty(CurrentEntryId) ? null : Document.FindEntry(CurrentEntryId);

    /// <summary>回到封面的干净状态。切书 / 重开时调。</summary>
    public void Reset()
    {
        Kind = BookViewKind.Cover;
        CurrentCategoryId = string.Empty;
        CurrentEntryId = string.Empty;
        PageIndex = 0;
    }

    // ── 逐层进入 ────────────────────────────────────────────────────

    /// <summary>封面 → 分类页。已在分类页或更深处时返回 false（不重复动作）。</summary>
    public bool Open()
    {
        if (Kind != BookViewKind.Cover) { return false; }
        Kind = BookViewKind.Categories;
        return true;
    }

    /// <summary>
    /// 进入某个分类。分类不存在时**不动**并返回 false ——
    /// 静默跳到一个空分类比什么都不做更难查。
    /// </summary>
    public bool EnterCategory(string categoryId)
    {
        if (Document.FindCategory(categoryId) is null) { return false; }

        Kind = BookViewKind.Categories;
        CurrentCategoryId = categoryId;
        CurrentEntryId = string.Empty;
        PageIndex = 0;
        return true;
    }

    /// <summary>
    /// 打开某条目。条目不存在时返回 false。
    /// 会自动把 <see cref="CurrentCategoryId"/> 同步成该条目所属分类 ——
    /// 这样从书签/链接直接跳条目时，<c>Back()</c> 退回去仍然是合理的位置。
    /// </summary>
    public bool OpenEntry(string entryId)
    {
        var e = Document.FindEntry(entryId);
        if (e is null) { return false; }

        Kind = BookViewKind.Entry;
        CurrentCategoryId = e.CategoryId;
        CurrentEntryId = e.Id;
        PageIndex = 0;
        return true;
    }

    /// <summary>逐级返回：条目 → 分类页 → 封面。已经在封面时返回 false（交给调用方决定是否关书）。</summary>
    public bool Back()
    {
        switch (Kind)
        {
            case BookViewKind.Entry:
                Kind = BookViewKind.Categories;
                CurrentEntryId = string.Empty;
                PageIndex = 0;
                return true;

            case BookViewKind.Categories:
                Kind = BookViewKind.Cover;
                CurrentCategoryId = string.Empty;
                return true;

            default:
                return false;
        }
    }

    // ── 翻页 ────────────────────────────────────────────────────────

    /// <summary>下一页。已在最后一页时**不动**并返回 false（不循环）。</summary>
    public bool NextPage()
    {
        if (Kind != BookViewKind.Entry) { return false; }
        if (PageIndex + 1 >= PageCount) { return false; }

        PageIndex++;
        return true;
    }

    /// <summary>上一页。已在第一页时不动并返回 false。</summary>
    public bool PrevPage()
    {
        if (Kind != BookViewKind.Entry) { return false; }
        if (PageIndex <= 0) { return false; }

        PageIndex--;
        return true;
    }

    /// <summary>
    /// 翻到下一页；已经是最后一页时**顺延到下一个条目**（翻书时最自然的连续感）。
    /// 返回 false 表示已经到头了。
    /// </summary>
    public bool NextPageOrEntry()
    {
        if (Kind != BookViewKind.Entry) { return false; }
        if (NextPage()) { return true; }
        return NextEntry();
    }

    /// <summary>翻到上一页；已经在第一页时退到上一个条目。</summary>
    public bool PrevPageOrEntry()
    {
        if (Kind != BookViewKind.Entry) { return false; }
        if (PrevPage()) { return true; }
        return PrevEntry();
    }

    // ── 同分类内换条目 ──────────────────────────────────────────────

    /// <summary>下一个条目（同分类内，按排序）。到最后一个不动。</summary>
    public bool NextEntry()
    {
        if (Kind != BookViewKind.Entry) { return false; }

        var cat = CurrentCategory;
        if (cat is null) { return false; }

        int idx = IndexOfEntry(cat, CurrentEntryId);
        if (idx < 0 || idx + 1 >= cat.Entries.Count) { return false; }

        CurrentEntryId = cat.Entries[idx + 1].Id;
        PageIndex = 0;
        return true;
    }

    /// <summary>上一个条目。到第一个不动。</summary>
    public bool PrevEntry()
    {
        if (Kind != BookViewKind.Entry) { return false; }

        var cat = CurrentCategory;
        if (cat is null) { return false; }

        int idx = IndexOfEntry(cat, CurrentEntryId);
        if (idx <= 0) { return false; }

        CurrentEntryId = cat.Entries[idx - 1].Id;
        PageIndex = 0;
        return true;
    }

    // ── 给渲染用的派生信息（纯计算，不含几何）────────────────────────

    /// <summary>
    /// 双页渲染时，左页应该显示哪一页。偶数页在左、奇数页在右；
    /// 于是跨页序号 = <c>PageIndex / 2</c>（整除），总跨页数 = <c>ceil(PageCount / 2)</c>。
    /// </summary>
    public int LeftPageIndex => PageIndex - (PageIndex % 2);

    /// <summary>双页渲染时右页显示哪一页；越界返回 -1（右页留白）。</summary>
    public int RightPageIndex
    {
        get
        {
            int r = LeftPageIndex + 1;
            return r < PageCount ? r : -1;
        }
    }

    /// <summary>总跨页数（双页渲染的页数）。</summary>
    public int SpreadCount => PageCount == 0 ? 0 : ((PageCount + 1) / 2);

    /// <summary>当前跨页序号（从 1 开始，给"第 2 / 5 页"这种显示用）。</summary>
    public int SpreadNumber => PageCount == 0 ? 0 : (PageIndex / 2) + 1;

    /// <summary>
    /// 跳到某个跨页（点页码点、或键盘跳页用）。越界返回 false。
    /// </summary>
    public bool GoToSpread(int spread)
    {
        if (Kind != BookViewKind.Entry) { return false; }
        if (spread < 0 || spread >= SpreadCount) { return false; }

        PageIndex = spread * 2;
        return true;
    }

    private static int IndexOfEntry(BookCategory cat, string entryId)
    {
        for (int i = 0; i < cat.Entries.Count; i++)
        {
            if (cat.Entries[i].Id == entryId) { return i; }
        }

        return -1;
    }
}
