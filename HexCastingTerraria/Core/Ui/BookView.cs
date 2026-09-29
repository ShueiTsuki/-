using System.Collections.Generic;

namespace HexCastingTerraria.Core.Ui;

// ── 书本的导航状态机 ──────────────────────────────────────────────────
//
// 照 Patchouli 的三层界面（VazkiiMods/Patchouli，CC BY-NC-SA 3.0，见 CREDITS.md）：
//   GuiBookLanding（落地页：引言 + 分类图标格）
//   GuiBookCategory（左：分类名 + 描述 + 子分类；右起：条目列表，分页）
//   GuiBookEntry（双页跨页：第 2s 页在左、2s+1 页在右）
// 「返回」走历史栈（和 Patchouli 一样）：从链接跳进来的条目，返回回到原来那一页。
//
// 这里没有一行绘制代码 —— 翻页越界、返回层级这类毛病在离线测试里用断言钉死。
// 所有会改状态的入口都返回 bool（是否真的动了），调用方据此决定放不放翻页音效。

public enum BookViewKind
{
    Landing = 0,
    Category = 1,
    Entry = 2,
}

public sealed class BookView
{
    /// <summary>分类页第一跨页右页能放的条目数（Patchouli ENTRIES_IN_FIRST_PAGE）。</summary>
    public const int EntriesInFirstPage = 11;

    /// <summary>之后每页能放的条目数（Patchouli ENTRIES_PER_PAGE），一个跨页两页。</summary>
    public const int EntriesPerPage = 13;

    private readonly Stack<(BookViewKind Kind, string Cat, string Entry, int Spread)> _history = new();

    public BookView(BookDocument doc)
    {
        Document = doc ?? new BookDocument();
    }

    public BookDocument Document { get; }
    public BookViewKind Kind { get; private set; } = BookViewKind.Landing;
    public string CategoryId { get; private set; } = string.Empty;
    public string EntryId { get; private set; } = string.Empty;

    /// <summary>当前跨页序号（从 0 开始）。</summary>
    public int Spread { get; private set; }

    public BookCategory? CurrentCategory => Document.FindCategory(CategoryId);
    public BookEntry? CurrentEntry => string.IsNullOrEmpty(EntryId) ? null : Document.FindEntry(EntryId);

    /// <summary>落地页上的分类（顶层、按 sortnum）。</summary>
    public List<BookCategory> TopCategories()
    {
        var list = Document.Categories.FindAll(c => string.IsNullOrEmpty(c.ParentId));
        list.Sort((a, b) => a.SortNum.CompareTo(b.SortNum));
        return list;
    }

    public List<BookCategory> Subcategories(BookCategory parent)
    {
        var list = Document.Categories.FindAll(c => c.ParentId == parent.Id);
        list.Sort((a, b) => a.SortNum.CompareTo(b.SortNum));
        return list;
    }

    public int SpreadCount
    {
        get
        {
            switch (Kind)
            {
                case BookViewKind.Category:
                    int n = CurrentCategory?.Entries.Count ?? 0;
                    int rest = System.Math.Max(0, n - EntriesInFirstPage);
                    return 1 + ((rest + (2 * EntriesPerPage) - 1) / (2 * EntriesPerPage));
                case BookViewKind.Entry:
                    int pages = CurrentEntry?.Pages.Count ?? 0;
                    return System.Math.Max(1, (pages + 1) / 2);
                default:
                    return 1;
            }
        }
    }

    /// <summary>打开书：回到落地页，清空历史。</summary>
    public void Open()
    {
        _history.Clear();
        Kind = BookViewKind.Landing;
        CategoryId = string.Empty;
        EntryId = string.Empty;
        Spread = 0;
    }

    public bool OpenCategory(string categoryId)
    {
        if (Document.FindCategory(categoryId) is null) { return false; }
        Push();
        Kind = BookViewKind.Category;
        CategoryId = categoryId;
        EntryId = string.Empty;
        Spread = 0;
        return true;
    }

    /// <summary>打开条目；<paramref name="anchor"/> 非空时翻到那一页所在的跨页。</summary>
    public bool OpenEntry(string entryId, string? anchor = null)
    {
        var e = Document.FindEntry(entryId);
        if (e is null) { return false; }
        Push();
        Kind = BookViewKind.Entry;
        CategoryId = e.CategoryId;
        EntryId = e.Id;
        Spread = 0;
        if (!string.IsNullOrEmpty(anchor))
        {
            int idx = e.Pages.FindIndex(p => p.Anchor == anchor);
            if (idx >= 0) { Spread = idx / 2; }
        }
        return true;
    }

    /// <summary>
    /// 跟随正文里的链接 <c>$(l:目标)</c>：目标形如 <c>patterns/readwrite#hexcasting:write</c>。
    /// 外部网址（http…）返回 false，由调用方决定怎么处理。
    /// </summary>
    public bool FollowLink(string target)
    {
        if (string.IsNullOrEmpty(target) || target.StartsWith("http", System.StringComparison.Ordinal)) { return false; }
        int hash = target.IndexOf('#');
        string id = hash >= 0 ? target.Substring(0, hash) : target;
        string? anchor = hash >= 0 ? target.Substring(hash + 1) : null;
        if (Document.FindEntry(id) is not null) { return OpenEntry(id, anchor); }
        return OpenCategory(id);
    }

    /// <summary>返回上一个界面。已在落地页且没有历史时返回 false（交给调用方关书）。</summary>
    public bool Back()
    {
        if (_history.Count > 0)
        {
            var (kind, cat, entry, spread) = _history.Pop();
            Kind = kind;
            CategoryId = cat;
            EntryId = entry;
            Spread = spread;
            return true;
        }
        if (Kind == BookViewKind.Landing) { return false; }
        Kind = BookViewKind.Landing;
        CategoryId = string.Empty;
        EntryId = string.Empty;
        Spread = 0;
        return true;
    }

    public bool NextSpread()
    {
        if (Spread + 1 >= SpreadCount) { return false; }
        Spread++;
        return true;
    }

    public bool PrevSpread()
    {
        if (Spread <= 0) { return false; }
        Spread--;
        return true;
    }

    private void Push() => _history.Push((Kind, CategoryId, EntryId, Spread));
}
