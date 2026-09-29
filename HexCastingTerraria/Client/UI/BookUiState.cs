using System.Collections.Generic;
using HexCastingTerraria.Config;
using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 书本 UI 的内容（<see cref="UIState"/>）—— **原版皮肤**，用泰拉自己的控件搭。
///
/// ## 为什么是元素树而不是自绘
///
/// 见 `BOOK_UI_DESIGN.md` 第 15 节。要点：`UIPanel` 画的就是原版那套面板
/// （颜色在 `UICommon` 里：`DefaultUIBlue = Color(73,94,171)`、
/// `MainPanelBackground = Color(33,43,79)*0.8f`、`DefaultUIBorder = Color.Black`），
/// 命中测试、事件冒泡、UI 缩放、输入封锁框架全包。
///
/// 我上一版自绘 + 猜颜色（`#3E4C6B`）等于把原版已经写好的东西重做了一遍，然后每个接线都出错。
///
/// ## 布局是绝对的，不是流式的
///
/// 泰拉的 `UIElement` 用 `Left/Top/Width/Height`（`StyleDimension`）绝对定位。
/// 所以这里手算行位置 —— 但**只算位置**，尺寸与文本交给 `UIText` 自己量。
/// 内容是 <see cref="BookDocument"/>（188 个图案按分类分组），与帕秋莉皮肤同一份来源。
/// </summary>
public sealed class BookUiState : UIState
{
    private const float PanelW = 640f;
    private const float PanelH = 420f;
    private const float Pad = 14f;
    private const float HeaderH = 34f;
    private const float ListW = 220f;
    private const float RowH = 24f;

    private BookDocument _doc = null!;
    private string _selectedCategoryId = string.Empty;
    private bool _dirty = true;

    private UIPanel _panel = null!;
    private UIPanel _listPanel = null!;
    private UIPanel _contentPanel = null!;
    private UIScrollbar _scrollbar = null!;
    private UIPanel _entryPanel = null!;
    private UIScrollbar _entryScroll = null!;
    private UIText _detail = null!;
    private string _selectedEntryId = string.Empty;
    private UIText _header = null!;
    private UIText _hint = null!;

    /// <summary>帕秋莉皮肤时用的那个自绘元素；原版皮肤时为 null。</summary>
    private PatchouliBookElement? _patchouli;

    /// <summary>当前搭的是哪一套（用来发现配置变了要重建）。</summary>
    private bool _builtVanilla;

    /// <summary>分类行（行 + 文本 + id）。排布用绝对 Top，滚动靠整体偏移。</summary>
    private readonly List<(UIPanel Row, UIText Label, string Id)> _categoryRows = new();

    /// <summary>条目行，同上。</summary>
    private readonly List<(UIPanel Row, BookEntry Entry)> _entryRows = new();

    private UIText _catTitle = null!;
    private UIText _catDesc = null!;

    public override void OnInitialize()
    {
        _doc = HexBook.ThemedDocument;
        if (_doc.Categories.Count > 0) { _selectedCategoryId = _doc.Categories[0].Id; }

        BuildForCurrentSkin();
    }

    /// <summary>
    /// 按配置搭哪一套：**关 = 帕秋莉皮革书；开 = 泰拉原版元素树**。
    /// 两套共用同一份 <see cref="BookDocument"/>（188 个图案），切换不会丢内容。
    /// </summary>
    private void BuildForCurrentSkin()
    {
        RemoveAllChildren();
        _categoryRows.Clear();
        _patchouli = null;
        _builtVanilla = HexClientConfig.Instance.VanillaBookSkin;

        if (_builtVanilla) { BuildVanillaTree(); }
        else { BuildPatchouli(); }
    }

    /// <summary>帕秋莉皮肤：整本书就是一个自绘元素，套在元素树里。</summary>
    private void BuildPatchouli()
    {
        _patchouli = new PatchouliBookElement(_doc);
        // 用 Set() 而不是初始化器直接赋字段 —— 面板那条路就是用 Set()，实测能居中
        _patchouli.Width.Set(PanelW, 0f);
        _patchouli.Height.Set(PanelH, 0f);
        _patchouli.HAlign = 0.5f;
        _patchouli.VAlign = 0.5f;
        // 右键关闭 —— 与旧界面一致，也是玩家对"书"这类全屏界面的默认预期
        _patchouli.OnRightClick += (_, _) => RequestClose();
        Append(_patchouli);
    }

    private void BuildVanillaTree()
    {
        _panel = new UIPanel();
        _panel.Width.Set(PanelW, 0f);
        _panel.Height.Set(PanelH, 0f);
        _panel.HAlign = 0.5f;
        _panel.VAlign = 0.5f;
        Append(_panel);

        _header = new UIText("咒法学之书", 1.1f);
        _header.Left.Set(Pad, 0f);
        _header.Top.Set(8f, 0f);
        _panel.Append(_header);

        _hint = new UIText("左键选分类 · Esc 关闭", 0.75f);
        _hint.Left.Set(Pad, 0f);
        _hint.Top.Set(PanelH - 26f, 0f);
        _panel.Append(_hint);

        // 左侧：分类列表（可滚动）
        _listPanel = new UIPanel();
        _listPanel.Left.Set(Pad, 0f);
        _listPanel.Top.Set(HeaderH + 6f, 0f);
        _listPanel.Width.Set(ListW, 0f);
        _listPanel.Height.Set(PanelH - HeaderH - 40f, 0f);
        // ⚠️ 仍然要裁。
        // 实测：旧的图案浏览器有 **19 个分类**，19 × 24px = 456px，而这块只有 346px。
        // `UIPanel` 默认不裁子元素 —— 上一轮在自绘画布上正是栽在这里（"字错位"），
        // 换成元素树后是**同一个问题换了地方**。
        // 现在接了 UIList + UIScrollbar，能滚了；但**裁**仍然是底线。
        _listPanel.OverflowHidden = true;
        _panel.Append(_listPanel);


        // UIList + UIScrollbar：滚动不用自己写，框架的现成控件

        _scrollbar = new UIScrollbar();
        _scrollbar.SetView(100f, 1000f);      // 官方 ExampleMod 的惯用值：决定滑块长度比例
        _scrollbar.Height.Set(PanelH - HeaderH - 40f, 0f);
        _scrollbar.Width.Set(20f, 0f);
        _scrollbar.HAlign = 1f;
        _listPanel.Append(_scrollbar);
        // 滚动由 ApplyScroll 统一偏移（不用 UIList）

        // 右侧：所选分类的内容
        _contentPanel = new UIPanel();
        _contentPanel.Left.Set(Pad + ListW + 8f, 0f);
        _contentPanel.Top.Set(HeaderH + 6f, 0f);
        _contentPanel.Width.Set(PanelW - ListW - (Pad * 3f), 0f);
        _contentPanel.Height.Set(PanelH - HeaderH - 40f, 0f);
        _panel.Append(_contentPanel);

        _catTitle = new UIText(string.Empty, 1.0f);
        _catTitle.Left.Set(10f, 0f);
        _catTitle.Top.Set(8f, 0f);
        _contentPanel.Append(_catTitle);

        _catDesc = new UIText(string.Empty, 0.75f);
        _catDesc.Left.Set(10f, 0f);
        _catDesc.Top.Set(34f, 0f);
        _catDesc.Width.Set(PanelW - ListW - (Pad * 3f) - 20f, 0f);
        _contentPanel.Append(_catDesc);

        float contentW = PanelW - ListW - (Pad * 3f);
        float entryTop = 54f;
        float entryH = PanelH - HeaderH - 40f - entryTop - 76f;   // 底下留 76px 给详情

        // 条目列表：每个条目 = 图案缩略图 + 名字，放进 UIList 里自动滚动
        _entryPanel = new UIPanel();
        _entryPanel.Left.Set(8f, 0f);
        _entryPanel.Top.Set(entryTop, 0f);
        _entryPanel.Width.Set(contentW - 30f, 0f);
        _entryPanel.Height.Set(entryH, 0f);
        _entryPanel.OverflowHidden = true;   // 与分类列表同理：行数会超过可用高度
        _contentPanel.Append(_entryPanel);

        _entryScroll = new UIScrollbar();
        _entryScroll.SetView(100f, 1000f);
        _entryScroll.Left.Set(contentW - 24f, 0f);
        _entryScroll.Top.Set(entryTop, 0f);
        _entryScroll.Height.Set(entryH, 0f);
        _entryScroll.Width.Set(20f, 0f);
        _contentPanel.Append(_entryScroll);
        // 滚动由 ApplyEntryScroll 统一偏移（不用 UIList）

        // 详情：起笔方向 / 角度序列 / 参数个数 / 媒质消耗（旧界面那几行）
        _detail = new UIText(string.Empty, 0.75f);
        _detail.Left.Set(10f, 0f);
        _detail.Top.Set(entryTop + entryH + 6f, 0f);
        _detail.Width.Set(contentW - 20f, 0f);
        _contentPanel.Append(_detail);

        BuildCategoryRows();
    }

    private void BuildCategoryRows()
    {
        foreach (var (row, _, _) in _categoryRows) { row.Remove(); }
        _categoryRows.Clear();
        // ⚠️ 这里**不用 UIList**。
        // 上一版用 UIList + UIScrollbar，实机截图里两个列表**都是空的** ——
        // UIList 的排布依赖它自己的 Recalculate 流程（谁触发、何时触发、与 UIState 的
        // Recalculate 是什么关系），我拿不准；而拿不准的东西不该放在"能不能看到内容"
        // 这条主链路上。
        //
        // 改成**绝对 Top + 整体偏移**：每行自己设 Top，滚动时统一减一个偏移量。
        // 逻辑直白、结果确定；命中测试仍由框架负责（元素 Top 变了 ContainsPoint 跟着变）。
        for (int i = 0; i < _doc.Categories.Count; i++)
        {
            var cat = _doc.Categories[i];
            var row = new UIPanel { Height = StyleDimension.FromPixels(RowH) };
            row.Width.Set(ListW - 30f, 0f);
            row.Left.Set(2f, 0f);
            row.Top.Set(4f + (i * RowH), 0f);

            var label = new UIText(string.IsNullOrEmpty(cat.DisplayName) ? cat.NameKey : cat.DisplayName, 0.85f);
            label.Left.Set(6f, 0f);
            label.VAlign = 0.5f;
            row.Append(label);

            string id = cat.Id;
            // 点击由框架命中测试 + 事件冒泡负责 —— 不用自己算鼠标在哪一行
            row.OnLeftClick += (_, _) => SelectCategory(id);

            _listPanel.Append(row);
            _categoryRows.Add((row, label, id));
        }

        RefreshContent();
    }

    /// <summary>
    /// 把滚动条位置应用到行的 Top 上。
    /// `UIScrollbar.ViewPosition` 是 0~1 的归一化位置（拖滑块或点轨道都会变），
    /// 乘上「内容比视口高出来的部分」就是像素偏移。
    /// </summary>
    private void ApplyScroll()
    {
        float viewH = PanelH - HeaderH - 40f;
        // 几何全部走 Core/Ui/ListLayout —— 那份数学**离线有断言**（drawtest），
        // 而"滚动偏移算错""列表没到底"正是几何问题。
        // 这里只负责把算出来的数字喂给 Top.Set。
        float raw = (float)_scrollbar.ViewPosition * ListLayout.MaxScroll(
            _categoryRows.Count, RowH, viewH, 4f, 4f);
        float offset = ListLayout.ClampOffset(
            raw, _categoryRows.Count, RowH, viewH, 4f, 4f);

        for (int i = 0; i < _categoryRows.Count; i++)
        {
            _categoryRows[i].Row.Top.Set(
                ListLayout.RowTop(i, RowH, offset, 4f), 0f);
        }

        Recalculate();
    }

    /// <summary>关书。Esc 与右键都走这里，只有一处真相。</summary>
    private static void RequestClose()
    {
        HexCanvasState.Book.Close();
        BookUiSystem.Hide();
    }

    private void SelectCategory(string id)
    {
        _selectedCategoryId = id;
        RefreshContent();
    }

    private void RefreshContent()
    {
        var cat = _doc.FindCategory(_selectedCategoryId);
        if (cat is null) { return; }

        _catTitle.SetText(string.IsNullOrEmpty(cat.DisplayName) ? cat.NameKey : cat.DisplayName);

        // 分类名太长就截断 —— 泰拉的字符串绘制没有自动省略
        string first = cat.Entries.Count > 0
            ? (string.IsNullOrEmpty(cat.Entries[0].DisplayName) ? cat.Entries[0].NameKey : cat.Entries[0].DisplayName)
            : "（空）";
        _catDesc.SetText($"{cat.Entries.Count} 个条目 · 例如：{first}");

        BuildEntryRows(cat);

        // 选中行加亮：UIPanel 自己不做选中态，这里用文本颜色表达
        foreach (var (_, label, id) in _categoryRows)
        {
            label.TextColor = id == _selectedCategoryId ? Color.White : new Color(160, 180, 220);
        }
    }

    /// <summary>
    /// 一个分类下的条目列表：每行 = 图案缩略图 + 中文名。
    ///
    /// 缩略图用 <see cref="PatternIconElement"/>（它内部复用 <c>PatternRenderer</c>，
    /// 与旧界面画的是同一份几何）。放进 <see cref="UIList"/> 就自动能滚、
    /// 自动获得命中测试 —— 这两样上一轮我都是自己写的，也都写错了。
    /// </summary>
    private void BuildEntryRows(BookCategory cat)
    {
        foreach (var (row, _) in _entryRows) { row.Remove(); }
        _entryRows.Clear();

        _detail.SetText(string.Empty);
        if (cat.Entries.Count > 0) { ShowDetail(cat.Entries[0]); }
        _entryScroll.ViewPosition = 0f;

        // 同样不用 UIList：绝对 Top + 整体偏移（理由见 BuildCategoryRows）
        for (int i = 0; i < cat.Entries.Count; i++)
        {
            var entry = cat.Entries[i];
            var row = new UIPanel { Height = StyleDimension.FromPixels(RowH + 6f) };
            row.Width.Set(PanelW - ListW - (Pad * 3f) - 34f, 0f);
            row.Left.Set(4f, 0f);
            row.Top.Set(i * (RowH + 6f), 0f);

            if (entry.Pattern is not null)
            {
                var icon = new PatternIconElement(entry.Pattern, 22f);
                icon.Left.Set(4f, 0f);
                icon.VAlign = 0.5f;
                row.Append(icon);
            }

            var label = new UIText(
                string.IsNullOrEmpty(entry.DisplayName) ? entry.NameKey : entry.DisplayName, 0.85f);
            label.Left.Set(34f, 0f);
            label.VAlign = 0.5f;
            row.Append(label);

            var captured = entry;
            row.OnLeftClick += (_, _) => ShowDetail(captured);

            _entryPanel.Append(row);
            _entryRows.Add((row, entry));
        }
    }

    /// <summary>条目列表的滚动 —— 与分类列表同一套做法。</summary>
    private void ApplyEntryScroll()
    {
        float viewH = PanelH - HeaderH - 40f - 54f - 76f;
        float rowH = RowH + 6f;
        float raw = (float)_entryScroll.ViewPosition * ListLayout.MaxScroll(
            _entryRows.Count, rowH, viewH);
        float offset = ListLayout.ClampOffset(
            raw, _entryRows.Count, rowH, viewH);

        for (int i = 0; i < _entryRows.Count; i++)
        {
            _entryRows[i].Row.Top.Set(ListLayout.RowTop(i, rowH, offset), 0f);
        }
    }

    /// <summary>选中一个条目 → 右下角显示它的信息（旧界面右侧那几行）。</summary>
    private void ShowDetail(BookEntry entry)
    {
        _selectedEntryId = entry.Id;

        // 正文里就是旧界面右侧的那几行（id / 参数个数 / 媒质消耗 / 是否已实现），
        // 由 HexBook.ThemedDocument 生成时写好的
        string body = entry.Pages.Count > 0 ? entry.Pages[0].Text : string.Empty;
        _detail.SetText(body.Replace("\n", "   "));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        // 配置在游戏里改了 → 立刻换皮肤（两套共用同一份内容，切了不丢东西）
        if (_builtVanilla != HexClientConfig.Instance.VanillaBookSkin)
        {
            BuildForCurrentSkin();
            Recalculate();
        }

        if (_dirty) { _dirty = false; Recalculate(); }

        if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
        {
            RequestClose();
        }

        // 滚动：把滚动条位置应用到行的 Top 上（不用 UIList，见 BuildCategoryRows 说明）
        // ⚠️ 只在原版皮肤下调用 —— 帕秋莉路径里 _scrollbar/_entryScroll 是 null，
        // 无条件调用会在每次 Update 抛空引用，把整个 UI 更新链打断。
        if (_builtVanilla)
        {
            ApplyScroll();
            ApplyEntryScroll();
        }

        // 内容为空说明当初建文档时注册表还没好 —— 重建一次
        if (_doc.Categories.Count == 0)
        {
            _doc = HexBook.ThemedDocument;
            if (_doc.Categories.Count > 0)
            {
                _selectedCategoryId = _doc.Categories[0].Id;
                BuildForCurrentSkin();
                Recalculate();
            }
        }
    }

    /// <summary>
    /// 输入封锁。**加在面板上，不是 UIState 上** —— 官方文档专门提醒过：
    /// UIState 铺满整屏，写在那里会把整个世界都当成界面区。
    /// </summary>
    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);   // 必须调，否则子元素不画

        // 两套皮肤都要拦：帕秋莉那套整个书体是一个元素，没有 _panel
        var rect = _builtVanilla && _panel is not null
            ? new Rectangle((int)_panel.Left.Pixels, (int)_panel.Top.Pixels, 0, 0)
            : Rectangle.Empty;

        bool inside = _builtVanilla
            ? (_panel is not null && _panel.ContainsPoint(Main.MouseScreen))
            : (_patchouli is not null && _patchouli.ContainsPoint(Main.MouseScreen));

        _ = rect;

        if (inside)
        {
            Main.LocalPlayer.mouseInterface = true;                          // 点击别穿透去用武器
            PlayerInput.LockVanillaMouseScroll("HexCastingTerraria/Book");   // 滚轮别换快捷栏
        }
    }
}
