using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Ui;

// ── 书本内容的数据模型 ────────────────────────────────────────────────
//
// 移植自 Patchouli 的 `client/book/` 与 `common/book/Book.java`
//（VazkiiMods/Patchouli，CC BY-NC-SA 3.0，见 CREDITS.md）。字段与层级**刻意保持一一对应**，
// 这样对着 Java 读代码时不用在脑子里做映射：
//
//   common/book/Book.java            -> BookDocument
//   client/book/BookCategory.java    -> BookCategory
//   client/book/BookEntry.java       -> BookEntry
//   client/book/page/…               -> BookPage（+ BookPageKind）
//
// **这里只有数据，没有任何绘制或布局**（Core/ 不许引用 XNA/tModLoader，由 check_arch 断言① 盯着）。
// 几何在 BookLayout，渲染在 Client/UI，这样两边都能各自演进、也都能离线测。
//
// 为什么不用 record / 可空标注：项目要求 **0 警告**。全部引用类型给出默认值，
// 就不需要 `= null!` 之类的噪声，也不会因为 `Nullable` 设置变化而破线。

/// <summary>
/// 页面类型。对应 Patchouli 注册在 <c>patchouli:…</c> 名下的一组页面类；
/// 这里只保留泰拉侧**真会用到**的那几种（MC 特有的多方块预览 / 实体预览 / 进度任务已剔除）。
/// </summary>
public enum BookPageKind
{
    /// <summary>纯正文（<c>patchouli:text</c>）。</summary>
    Text = 0,

    /// <summary>配方展示（<c>patchouli:crafting</c>）。</summary>
    Crafting = 1,

    /// <summary>把图案画在页面上（源项目的 <c>ManualPatternComponent</c>）。</summary>
    Pattern = 2,

    /// <summary>中央一件物品 + 说明（<c>patchouli:spotlight</c>）。</summary>
    Spotlight = 3,

    /// <summary>模板里的标题组件（<c>patchouli:header</c>）。</summary>
    Header = 4,

    /// <summary>模板里的分隔线（<c>patchouli:separator</c>）。</summary>
    Separator = 5,
}

/// <summary>
/// 书里的一页。
///
/// 一个页面对象可能同时携带多种内容（Patchouli 里就是一页上叠组件），
/// 所以这里是**字段并集**而不是子类继承 —— 加载 hjson 时不用做多态派发，
/// 画的时候按 <see cref="Kind"/> 决定用哪几个字段。
/// </summary>
public sealed class BookPage
{
    public BookPageKind Kind { get; set; } = BookPageKind.Text;

    /// <summary>本页的小标题（可空；留空就不画标题）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>正文。支持 Patchouli 的排版标记，见 <c>Core/Ui/BookText.cs</c>。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>配方展示用：产出物品的内部名（如 <c>AmethystDust</c>）。</summary>
    public string RecipeItem { get; set; } = string.Empty;

    /// <summary>图案展示用：图案 id（如 <c>hexcasting:edify</c>）。</summary>
    public string PatternId { get; set; } = string.Empty;

    /// <summary>
    /// 本页引用的模板 id（源项目里大量条目用 <c>"template": "hexcasting:pattern"</c> 拼版，
    /// 而不是逐页写组件）。空 = 不是模板页。
    /// </summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>中央物品展示用：物品内部名。</summary>
    public string IconItem { get; set; } = string.Empty;

    /// <summary>模板参数（对应 Patchouli 模板里的 <c>#key#</c> 变量替换）。</summary>
    public System.Collections.Generic.Dictionary<string, string> Variables { get; }
        = new System.Collections.Generic.Dictionary<string, string>();
}

/// <summary>
/// 一个条目。对应 Patchouli 的 <c>client/book/BookEntry.java</c>。
///
/// 源项目里有 82 个条目（`patchouli_books/thehexbook/en_us/entries/`），
/// 分成 7 个分类。
/// </summary>
public sealed class BookEntry
{
    /// <summary>内部 id，形如 <c>hexcasting:jeweler_hammer</c>。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>所属分类的 id。</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>本地化键（如 <c>hexcasting.entry.jeweler_hammer</c>）。</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>
    /// 已解析好的显示名。**由调用方（Client）从本地化填进来** ——
    /// Core/ 不能引用 tModLoader，拿不到 <c>Language.GetTextValue</c>，
    /// 所以皮肤只认这个字段，认不到就退回 <see cref="NameKey"/> 原文。
    /// 这样皮肤既不用关心本地化，也不会画出空白。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>列表与页眉上画的那个物品（物品内部名）。</summary>
    public string IconItem { get; set; } = string.Empty;

    /// <summary>排序号；同分类内按它排。</summary>
    public int SortNum { get; set; }

    /// <summary>
    /// 解锁条件（源项目是 advancement 名）。留空 = 一开始就能看。
    /// 泰拉侧暂时只解析不强制，等进度流程接上再启用。
    /// </summary>
    public string Advancement { get; set; } = string.Empty;

    /// <summary>条目正文，按顺序排好。</summary>
    public System.Collections.Generic.List<BookPage> Pages { get; }
        = new System.Collections.Generic.List<BookPage>();

    /// <summary>
    /// 若这条内容对应一个**图案**，这里放它的定义（画缩略图要用）。
    /// 非图案条目为 null。
    ///
    /// 为什么要带在模型上：缩略图必须由 <c>PatternRenderer</c> 画，而它要的是
    /// <see cref="HexCastingTerraria.Core.Registry.PatternDef"/>。
    /// 放到这里之后，UI 层不用再按 id 反查一遍（反查要处理 id 前缀、找不到等分支，
    /// 都是能出错的地方）。<c>PatternDef</c> 本身在 Core 里、与 XNA 无关，
    /// 所以这不违反分层约束。
    /// </summary>
    public PatternDef? Pattern { get; set; }
}

/// <summary>
/// 一个分类。对应 <c>client/book/BookCategory.java</c>。
///
/// 注意：源项目的分类**没有自己的美术**，落地页画的是 <see cref="IconItem"/> 指的那个物品 ——
/// 所以落地页就是「3 列物品图标格」，不需要为 7 个分类各画一张图。
/// </summary>
public sealed class BookCategory
{
    public string Id { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    /// <summary>已解析好的显示名（同 <see cref="BookEntry.DisplayName"/> 的说明）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>落地页格子上画的物品（物品内部名）。</summary>
    public string IconItem { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>已解析好的分类描述（同 <see cref="BookEntry.DisplayName"/> 的说明）。</summary>
    public string DisplayDescription { get; set; } = string.Empty;

    public int SortNum { get; set; }

    public System.Collections.Generic.List<BookEntry> Entries { get; }
        = new System.Collections.Generic.List<BookEntry>();
}

/// <summary>
/// 一整本书。对应 <c>common/book/Book.java</c>。
///
/// 内容全部来自 hjson（**不写死在 C# 里**）：82 个条目写死是灾难，
/// 而且这样才和原版的作者工作流一致、本地化也天然干净。
/// </summary>
public sealed class BookDocument
{
    public string Id { get; set; } = string.Empty;

    public string TitleKey { get; set; } = string.Empty;

    /// <summary>已解析好的书名（同 <see cref="BookEntry.DisplayName"/> 的说明）。</summary>
    public string DisplayTitle { get; set; } = string.Empty;

    public System.Collections.Generic.List<BookCategory> Categories { get; }
        = new System.Collections.Generic.List<BookCategory>();

    /// <summary>
    /// 按 id 找条目。加载完内容后调一次 <see cref="RebuildIndex"/> 建索引 ——
    /// 翻书时要按 id 跳转（书签、链接），不能每次线性扫。
    /// </summary>
    public System.Collections.Generic.Dictionary<string, BookEntry> EntryById { get; }
        = new System.Collections.Generic.Dictionary<string, BookEntry>();

    public BookCategory? FindCategory(string id)
    {
        foreach (var c in Categories)
        {
            if (c.Id == id) { return c; }
        }

        return null;
    }

    public BookEntry? FindEntry(string id)
        => EntryById.TryGetValue(id, out var e) ? e : null;

    /// <summary>重建 id 索引，并按 <c>SortNum</c> 给分类与条目排序（稳定排序，同号保持加载顺序）。</summary>
    public void RebuildIndex()
    {
        Categories.Sort((a, b) => a.SortNum.CompareTo(b.SortNum));

        EntryById.Clear();

        foreach (var c in Categories)
        {
            c.Entries.Sort((a, b) => a.SortNum.CompareTo(b.SortNum));

            foreach (var e in c.Entries)
            {
                EntryById[e.Id] = e;
            }
        }
    }
}
