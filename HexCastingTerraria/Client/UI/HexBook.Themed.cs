using System.Collections.Generic;
using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 书本的**新渲染路径**：用 <see cref="BookView"/> + <see cref="BookSkin"/> 画。
///
/// ## 为什么做成 partial 而不是重写 HexBook
///
/// 旧的图案浏览器（分类标签 + 网格 + 详情页）里有**新模型还没有的信息**：
/// 图案缩略图、官方中文名、起笔方向、角度序列、媒质消耗、大图。直接删掉会丢东西。
/// 所以旧的整套原样留着（一行没动），新的走这个 partial —— 两边都能用，随时可回退。
///
/// ## 数据从哪来
///
/// 把旧的 <c>_entries</c>（188 个图案）按 <c>Category</c> 分组，装进
/// <see cref="BookDocument"/>。这样"两套皮肤"面对的是同一份真实内容，
/// 而不是空壳 —— 之前的离屏预览用的是我合成的假数据。
///
/// ## 与旧路径的关系
///
/// 旧的 <c>Draw</c> / <c>HandleInput</c> 仍然可用；<c>EnsureThemedDocument</c> 只在
/// 第一次画时建一次文档。两者共用同一份 <c>_entries</c> 与同一个 <c>IsOpen</c>。
/// </summary>
public sealed partial class HexBook
{
    /// <summary>
    /// 是否用新渲染路径。默认开 —— 用户的原话是"游戏里一直没变过"，
    /// 所以新界面必须**默认可见**，而不是藏在开关后面。
    /// 旧路径一行没删，改回 false 即可回退。
    /// </summary>
    public static bool UseThemedUi { get; set; }

    /// <summary>用哪套皮肤。等开发者设置接好之后由配置驱动。</summary>
    public enum SkinKind
    {
        /// <summary>帕秋莉版（原版咒法学那本手册的观感）。</summary>
        Patchouli = 0,

        /// <summary>泰拉原版观感。</summary>
        Vanilla = 1,
    }

    private static BookDocument? _themedDoc;
    private static BookView? _themedView;
    private static BookSkin? _themedSkin;
    private static SpriteBatchBookCanvas? _themedCanvas;
    private static SkinKind _themedSkinKind = SkinKind.Patchouli;

    /// <summary>当前皮肤。改它之后下一次绘制生效（并重建皮肤对象）。</summary>
    public static SkinKind ThemedSkin
    {
        get => _themedSkinKind;
        set
        {
            if (_themedSkinKind == value) { return; }
            _themedSkinKind = value;
            _themedSkin = null;
        }
    }

    /// <summary>切换皮肤。给「开发者设置」和临时按键用。</summary>
    public static void CycleThemedSkin()
        => ThemedSkin = _themedSkinKind == SkinKind.Patchouli ? SkinKind.Vanilla : SkinKind.Patchouli;

    /// <summary>
    /// 供新骨架（<c>BookUiSystem</c> / <c>BookUiState</c>）取内容。
    ///
    /// 内容仍然只有一份来源（旧的 188 个图案 → <see cref="BookDocument"/>），
    /// 两条渲染路径共用 —— 不会出现"两套内容各写一遍然后对不上"。
    /// </summary>
    public static BookDocument ThemedDocument
    {
        get
        {
            // ⚠️ 为空就重建。
            // `BookUiState.OnInitialize` 是在 `ModSystem.Load()` 里被调用的（我调 Activate 时），
            // 那时**图案注册表还没填好** —— 于是这里会建出一本**空书并永久缓存**。
            // 症状非常隐蔽：书皮与几何不依赖内容所以照画，分类列表和标题全空。
            if (_themedDoc is null || _themedDoc.Categories.Count == 0)
            {
                _themedDoc = null;
                return EnsureThemedDocument();
            }
            return _themedDoc;
        }
    }

    /// <summary>
    /// 把 188 个图案装成一本 <see cref="BookDocument"/>。
    ///
    /// 分类用旧的 <c>_categories</c>（去掉「全部」—— 新界面本身就是全量列表，
    /// 再放一个"全部"分类只会让分类列表里多一个语义重复的项）。
    /// </summary>
    private static BookDocument EnsureThemedDocument()
    {
        if (_themedDoc is not null) { return _themedDoc; }

        EnsureEntries();

        var doc = new BookDocument { Id = "patterns", TitleKey = "hexcasting.book" };
        doc.DisplayTitle = "咒法学之书";

        var byCat = new Dictionary<string, BookCategory>();

        if (_entries is not null)
        {
            foreach (var e in _entries)
            {
                var catId = string.IsNullOrEmpty(e.Category) ? "misc" : e.Category;
                if (!byCat.TryGetValue(catId, out var cat))
                {
                    cat = new BookCategory
                    {
                        Id = catId,
                        NameKey = catId,
                        DisplayName = catId,
                        DescriptionKey = string.Empty,
                        SortNum = byCat.Count,
                    };
                    byCat[catId] = cat;
                    doc.Categories.Add(cat);
                }

                var entry = new BookEntry
                {
                    Id = e.ShortId,
                    CategoryId = catId,
                    NameKey = e.ShortId,
                    // 官方中文名优先；查不到回落短 id（与旧界面一致）
                    DisplayName = string.IsNullOrEmpty(e.Name) ? e.ShortId : e.Name!,
                    SortNum = cat.Entries.Count,
                    Pattern = e.Def,      // 画缩略图要用
                };

                // 每个图案 = 一页：正文放旧界面右侧详情栏里的那几行信息。
                // 这样"新界面里能看到的东西"不比旧界面少。
                var body = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(e.Name)) { body.Append(e.Name).Append('\n'); }
                body.Append("id: ").Append(e.ShortId).Append('\n');
                if (e.Argc >= 0) { body.Append("参数个数: ").Append(e.Argc).Append('\n'); }
                if (e.MediaCost >= 0) { body.Append("媒质消耗: ").Append(e.MediaCost); }
                else { body.Append("媒质消耗: 由法术自己决定"); }
                body.Append('\n').Append(e.Implemented ? "已实现" : "未实现");

                entry.Pages.Add(new BookPage { Kind = BookPageKind.Text, Text = body.ToString() });
                cat.Entries.Add(entry);
            }
        }

        doc.RebuildIndex();
        _themedDoc = doc;
        return doc;
    }

    private static BookSkin EnsureThemedSkin()
    {
        _themedSkin ??= _themedSkinKind == SkinKind.Patchouli
            ? new PatchouliSkin { Chrome = ThemedChrome }
            : new VanillaSkin { Chrome = ThemedChrome };
        return _themedSkin;
    }

    /// <summary>
    /// 皮肤里的固定文案。
    ///
    /// **中文在游戏里没问题**（泰拉的 MouseText 有全套汉字），但离屏预览的字模只有 ASCII
    /// （`_tools/gen_font_atlas.ps1` 现在只烘了 32..126），所以离屏那边得传英文。
    /// 这里按「有没有自定义字模」分不出，就统一用中文 —— 游戏优先。
    /// </summary>
    private static BookChrome ThemedChrome => new()
    {
        CoverTitle = "咒法学之书",
        ContentsTitle = "目录",
        Back = "Esc 返回",
        EntriesSuffix = "个",
        Crafting = "合成",
    };

    /// <summary>按新皮肤画一帧。旧的 <c>Draw</c> 原样保留，调用方二选一。</summary>
    public void DrawThemed(SpriteBatch sb, float w, float h, Vector2 mouse)
    {
        _ = sb;
        _ = mouse;

        // ⚠️ **必须有这一行。** 旧的 Draw 第一行就是 `if (!IsOpen) return;`，
        // 而 DrawThemed 上一版漏了 —— 它在那趟绘制里无条件执行，于是
        // **书没打开也会画出来**（玩家看到的"自己开了"就是这个）。
        // 这是把绘制逻辑从旧方法搬过来时最容易漏的一行：守卫在方法头上，而不是在调用处。
        if (!IsOpen) { return; }

        var view = _themedView ??= new BookView(EnsureThemedDocument());
        var skin = EnsureThemedSkin();
        _themedCanvas ??= new SpriteBatchBookCanvas();

        var viewport = new RectF(0, 0, w, h);
        var metrics = skin.Measure(view, viewport);
        skin.Draw(_themedCanvas, view, metrics);
    }

    /// <summary>
    /// 新的输入处理。返回值语义与旧的 <c>HandleInput</c> 一致：true = 已消费。
    ///
    /// 命中测试走 <see cref="BookSkin.HitTest"/> —— 所以**点击位置与画出来的位置
    /// 用的是同一份 <c>BookMetrics</c>**，不会出现"画在这儿、点在那儿"。
    /// </summary>
    public bool HandleThemedInput(float w, float h, Vector2 mouse, bool leftClick, bool rightClick)
    {
        var view = _themedView ??= new BookView(EnsureThemedDocument());
        var skin = EnsureThemedSkin();

        var viewport = new RectF(0, 0, w, h);
        var metrics = skin.Measure(view, viewport);

        // Esc：逐级返回，已经在最外层就关书。
        // 上一版完全没处理键盘，玩家按 Esc 一点反应都没有 ——
        // 而"按 Esc 返回"是这类界面的默认预期，必须支持。
        if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            if (!view.Back()) { Close(); }
            return true;
        }

        if (rightClick)
        {
            // 右键：逐级返回；**已经在最外层就关书**。
            // 上一版这里 `return false` 想让调用方去关 —— 但调用方根本没接这个返回值，
            // 结果书关不掉。状态机不替调用方做决定是对的，但"关书"这个动作必须有人做，
            // 而路径最清晰的地方就是这里。
            if (!view.Back()) { Close(); }
            return true;
        }

        if (!leftClick) { return true; }   // 书开着就吃掉输入，别透到世界里

        var hit = skin.HitTest(view, metrics, mouse.X, mouse.Y);
        switch (hit.Kind)
        {
            case BookHitKind.CategoryCell:
                if (hit.Index >= 0 && hit.Index < view.Document.Categories.Count)
                {
                    view.EnterCategory(view.Document.Categories[hit.Index].Id);
                }
                return true;

            case BookHitKind.EntryRow:
                var cat = view.CurrentCategory;
                if (cat is not null && hit.Index >= 0 && hit.Index < cat.Entries.Count)
                {
                    view.OpenEntry(cat.Entries[hit.Index].Id);
                }
                return true;

            case BookHitKind.Back:
                view.Back();
                return true;

            case BookHitKind.NextPage:
                view.NextPageOrEntry();
                return true;

            case BookHitKind.PrevPage:
                view.PrevPageOrEntry();
                return true;

            case BookHitKind.PageDot:
                view.GoToSpread(hit.Index);
                return true;

            default:
                // 点在封面/空白处：在封面上就翻开，否则回到上一级
                if (view.Kind == BookViewKind.Cover) { view.Open(); }
                return true;
        }
    }

    /// <summary>供离屏/调试查看当前视图状态。</summary>
    public BookView? ThemedView => _themedView;
}
