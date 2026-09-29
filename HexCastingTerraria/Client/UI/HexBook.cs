using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 咒法学之书。
///
/// ## 它解决什么问题
///
/// 咒法学有 188 条图案，而「怎么画」这件事**只能靠图形传达** ——
/// 用文字描述「从西北起，角度序列 wqaqwd」对人是不可读的。
/// 所以原版的核心是一本**画着所有图案形状**的书（Patchouli 写的）。
///
/// 泰拉没有 Patchouli，所以这里自己做一个：
/// 左边是可滚动的图案网格（每格画出图案本身），右边是选中图案的详情。
///
/// ## 为什么不做搜索框
///
/// 原版的书也没有搜索 —— 因为它按**分类目录**组织，而图案的记忆方式本来就是
/// 「我见过这个形状」。这里沿用同一思路：按 id 的第一段分组（常量/数学/栈/世界/法术/环…），
/// 顶部一排分类标签点一下就切过去。搜索框需要文本输入，而泰拉的文本输入
/// 会与我们的画布输入压制打架，收益也不大。
///
/// ## 数据从哪来
///
/// 全部来自 <see cref="PatternRegistry"/>（188 条）+ 已注册的行为
/// （用来显示「参数个数 / 基础消耗 / 是否已实现」）。
/// **不额外维护一份描述表** —— 那种表一定会和代码漂移，
/// 而漂移的表现是「书里写的和实际的不一样」，比没有书更糟。
/// </summary>
public sealed partial class HexBook
{
    /// <summary>每页的格子尺寸（像素）。</summary>
    private const float CellW = 92f;
    private const float CellH = 92f;

    private const float PanelPad = 16f;

    public bool IsOpen { get; private set; }

    /// <summary>当前分类（null = 全部）。</summary>
    private string? _category;

    /// <summary>滚动偏移（像素）。</summary>
    private float _scroll;

    /// <summary>上一次滚动的目标，用来平滑。这里直接赋值，不做缓动。</summary>
    private int _selectedIndex;

    /// <summary>
    /// 是否处于「条目详情页」。
    ///
    /// 原版那本书是**章 → 条目 → 页**：点进一个图案会翻到一整页，
    /// 页上有大图与说明，再点一次才回去。我们之前把详情做成了右侧小栏，
    /// 信息够用但不像书 —— 这里补上页面视图，交互对得上原版。
    /// </summary>
    private bool _pageOpen;

    /// <summary>缓存的条目列表（只建一次）。</summary>
    private static List<Entry>? _entries;

    /// <summary>分类标签。</summary>
    private static List<string>? _categories;

    /// <summary>一条图案的记录。</summary>
    public sealed class Entry
    {
        public required PatternDef Def { get; init; }

        /// <summary>分类（id 的第一段）。</summary>
        public required string Category { get; init; }

        /// <summary>短名（id 去掉 `hexcasting:` 前缀）。</summary>
        public required string ShortId { get; init; }

        /// <summary>
        /// 原版官方中文名（「意识之精思」）。查不到为 null，此时界面回落显示 ShortId。
        ///
        /// 玩家记的是名字不是 id —— 书上写 `open_paren` 没人看得懂，
        /// 写「内省」才对得上原版攻略和视频。
        /// </summary>
        public string? Name { get; init; }

        /// <summary>是否已实现行为。</summary>
        public bool Implemented { get; init; }

        /// <summary>参数个数；-1 表示不适用（例如元求值类）。</summary>
        public int Argc { get; init; } = -1;

        /// <summary>基础媒质消耗；-1 表示由法术自己决定。</summary>
        public long MediaCost { get; init; } = -1;

        /// <summary>「不适用于泰拉」的原因（null = 适用）。</summary>
        public string? NotApplicableReason { get; init; }
    }

    // ── 数据 ────────────────────────────────────────────────────────

    /// <summary>
    /// 建立条目表。**只做一次** —— 注册表在 `Mod.Load` 之后就不再变，
    /// 每帧重建会让打开书的那一帧卡一下，而且没必要。
    /// </summary>
    private static void EnsureEntries()
    {
        if (_entries != null) return;

        var notApplicable = new HashSet<string>(
            Core.Casting.Actions.NotApplicablePatterns.ZAxisVectors
                .Concat(Core.Casting.Actions.NotApplicablePatterns.PehkuiInterop));

        var list = new List<Entry>();
        foreach (var def in PatternRegistry.All)
        {
            string shortId = def.Id.StartsWith("hexcasting:", StringComparison.Ordinal)
                ? def.Id["hexcasting:".Length..]
                : def.Id;

            int slash = shortId.IndexOf('/');
            string category = slash < 0 ? "基础" : shortId[..slash];

            bool implemented = PatternRegistry.HasAction(def);
            int argc = -1;
            long cost = -1;

            if (PatternRegistry.TryGetAction(def, out var action) && action != null)
            {
                switch (action)
                {
                    case ConstMediaAction c:
                        argc = c.Argc;
                        cost = c.MediaCost;
                        break;

                    case SpellAction s:
                        argc = s.Argc;
                        break;
                }
            }

            list.Add(new Entry
            {
                Def = def,
                Category = category,
                ShortId = shortId,
                Name = GeneratedPatternNames.NameOf(def.Id),
                Implemented = implemented,
                Argc = argc,
                MediaCost = cost,
                NotApplicableReason = notApplicable.Contains(def.Id) ? "泰拉没有这个维度或对应系统" : null,
            });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.ShortId, b.ShortId));
        _entries = list;

        var cats = new List<string> { "全部" };
        cats.AddRange(list.Select(e => e.Category).Distinct().OrderBy(x => x, StringComparer.Ordinal));
        _categories = cats;
    }

    /// <summary>当前分类下可见的条目。</summary>
    private List<Entry> Visible()
    {
        EnsureEntries();
        return _category == null || _category == "全部"
            ? _entries!
            : _entries!.Where(e => e.Category == _category).ToList();
    }

    // ── 开关 ────────────────────────────────────────────────────────

    public void Open()
    {
        EnsureEntries();
        IsOpen = true;
        _scroll = 0f;
        _selectedIndex = 0;
        _pageOpen = false;
    }

    public void Close() => IsOpen = false;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    // ── 输入 ────────────────────────────────────────────────────────

    /// <summary>
    /// 每帧处理输入。返回 true 表示这一帧的鼠标/滚轮已经被书吃掉了。
    ///
    /// ⚠️ 必须**吃掉**滚轮与左键：不然滚动书页会同时切换快捷栏、
    /// 点选图案会同时挖掉脚下的方块。
    /// </summary>
    public bool HandleInput(float w, float h, Vector2 mouse)
    {
        if (!IsOpen) return false;

        var visible = Visible();
        var (gridX, gridY, gridW, gridH) = GridRect(w, h);

        // 滚轮
        int wheel = Microsoft.Xna.Framework.Input.Mouse.GetState().ScrollWheelValue;
        int delta = wheel - _lastWheel;
        _lastWheel = wheel;

        if (delta != 0)
        {
            // 滚动范围也走 BookLayout：以前这里用 `count * CellW / gridW` 近似行数，
            // 而列数其实是 floor(gridW / CellW)，两者在边界上会差一行（滚不到底或多滚一屏）。
            var gridRect = new Core.Ui.RectF(gridX, gridY, gridW, gridH);
            float maxScroll = Core.Ui.BookLayout.MaxScroll(visible.Count, gridRect);

            _scroll = System.Math.Clamp(_scroll - delta * 0.2f, 0f, maxScroll);
        }

        // 左键：选中
        bool leftDown = Main.mouseLeft && !_leftWasDown;
        _leftWasDown = Main.mouseLeft;

        // 记录「本帧是否有一次新的点击」。
        // ⚠️ 必须在输入阶段算好，不能留给绘制阶段去比较 `Main.mouseLeft && !_leftWasDown`：
        // 输入跑在 PostUpdateInput、绘制跑在 DrawInterface，
        // 绘制时 `_leftWasDown` 已经被本帧更新过，那段判断永远是 false ——
        // 表现是「分类标签点不动」，而且**不报错**。
        ClickedThisFrame = leftDown;

        if (leftDown && mouse.X >= gridX && mouse.X <= gridX + gridW
                     && mouse.Y >= gridY && mouse.Y <= gridY + gridH)
        {
            // 命中判定与绘制（DrawGrid → BookLayout.Cell）**共用同一份算法**。
            // 以前这两处各写了一遍 col/row，只要改了一处忘了另一处，
            // 表现就是「点不中格子」，而且不会有任何报错。
            int index = Core.Ui.BookLayout.IndexAt(
                new Core.Ui.RectF(gridX, gridY, gridW, gridH), mouse.X, mouse.Y, visible.Count, _scroll);

            if (index >= 0)
            {
                _selectedIndex = index;
                _pageOpen = true;      // 点条目 = 翻到它的页面（对齐原版）
            }
        }

        // 点面板外也关：泰拉玩家的直觉是「点外面就退出」。
        // 书是模态界面，不给这条出路的话只能靠右键/Esc，容易让人以为卡住了。
        if (leftDown)
        {
            var (px, py, pw, ph) = PanelRect(w, h);
            bool inside = mouse.X >= px && mouse.X <= px + pw && mouse.Y >= py && mouse.Y <= py + ph;
            if (!inside)
            {
                Close();
                return true;
            }
        }

        // 右键 / Esc：关闭
        if (Main.mouseRight && !_rightWasDown)
        {
            _rightWasDown = Main.mouseRight;

            // 右键的语义与原版一致：先退回目录，再关书
            if (_pageOpen) _pageOpen = false;
            else Close();

            return true;
        }

        _rightWasDown = Main.mouseRight;

        if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            if (_pageOpen) _pageOpen = false;
            else Close();
        }

        // 书开着的时候，凡是落在书上的点击都不该传给游戏
        return true;
    }

    /// <summary>本帧是否发生了一次新的左键按下（供绘制阶段判断标签点击用）。</summary>
    public bool ClickedThisFrame { get; private set; }

    private int _lastWheel;
    private bool _leftWasDown;
    private bool _rightWasDown;

    // ── 布局 ────────────────────────────────────────────────────────

    /// <summary>面板矩形（屏幕坐标）。</summary>
    private static (float X, float Y, float W, float H) PanelRect(float w, float h)
    {
        float pw = System.Math.Min(900f, w - 80f);
        float ph = System.Math.Min(560f, h - 80f);
        return ((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
    }

    /// <summary>网格区域（不含右侧详情栏）。</summary>
    private static (float X, float Y, float W, float H) GridRect(float w, float h)
    {
        var (px, py, pw, ph) = PanelRect(w, h);

        // 顶部：标题 + 分类标签（约 84px）；右侧：详情栏（260px）
        float x = px + PanelPad;
        float y = py + 84f;
        return (x, y, pw - 260f - PanelPad * 2f, ph - 84f - PanelPad);
    }

    // ── 绘制 ────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, float w, float h, Vector2 mouse,
                     Action<Vector2, Vector2, float, Color> line,
                     Action<Vector2, float, Color> dot)
    {
        if (!IsOpen) return;

        var visible = Visible();

        // 条目详情页：整块面板变成一页书
        if (_pageOpen && visible.Count > 0)
        {
            DrawPage(sb, w, h, visible, line, dot);
            return;
        }

        var (px, py, pw, ph) = PanelRect(w, h);
        var (gx, gy, gw, gh) = GridRect(w, h);

        // 半透明底：既压住游戏画面，又保留一点环境感
        DrawRect(sb, new Rectangle((int)px, (int)py, (int)pw, (int)ph), new Color(18, 14, 30, 235));
        DrawBorder(sb, new Rectangle((int)px, (int)py, (int)pw, (int)ph), new Color(150, 120, 220));

        Terraria.Utils.DrawBorderString(sb, "咒法学之书",
            new Vector2(px + PanelPad, py + 12f), new Color(226, 210, 255), 1.0f);

        Terraria.Utils.DrawBorderString(sb, $"{visible.Count} / {PatternRegistry.Count} 条图案   滚轮翻页 · 左键选中 · 右键关闭",
            new Vector2(px + PanelPad, py + 40f), new Color(170, 160, 200), 0.7f);

        DrawCategories(sb, px + PanelPad, py + 62f, mouse);

        // 网格
        DrawGrid(sb, gx, gy, gw, gh, visible, mouse, line, dot);

        // 详情
        DrawDetails(sb, px + pw - 260f, gy, 244f, gh, visible);
    }

    private void DrawCategories(SpriteBatch sb, float x, float y, Vector2 mouse)
    {
        EnsureEntries();

        float cx = x;
        foreach (var cat in _categories!)
        {
            float width = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(cat).X * 0.7f + 16f;
            var rect = new Rectangle((int)cx, (int)y, (int)width, 20);
            bool active = (_category ?? "全部") == cat;
            bool hover = rect.Contains((int)mouse.X, (int)mouse.Y);

            DrawRect(sb, rect, active
                ? new Color(96, 74, 150)
                : hover ? new Color(60, 48, 96) : new Color(38, 30, 60));

            Terraria.Utils.DrawBorderString(sb, cat,
                new Vector2(cx + 8f, y + 2f),
                active ? new Color(240, 232, 255) : new Color(180, 172, 205), 0.7f);

            // 点击切换
            if (hover && ClickedThisFrame)
            {
                _category = cat == "全部" ? null : cat;
                _scroll = 0f;
                _selectedIndex = 0;
            }

            cx += width + 6f;
        }
    }

    private void DrawGrid(SpriteBatch sb, float x, float y, float w, float h,
                          List<Entry> visible, Vector2 mouse,
                          Action<Vector2, Vector2, float, Color> line,
                          Action<Vector2, float, Color> dot)
    {
        // 格子位置一律走 BookLayout.Cell —— 与命中判定（BookLayout.IndexAt）同一份算法。
        // 以前这里是第二份手写的 col/row/cx/cy，和输入阶段那份只要有一点不一致，
        // 表现就是「点不中/点到隔壁」，而且不报错。
        var grid = new Core.Ui.RectF(x, y, w, h);

        for (int i = 0; i < visible.Count; i++)
        {
            var cell = Core.Ui.BookLayout.Cell(grid, i, _scroll);

            // 视口裁剪：滚动出去的格子直接跳过（188 个全画会白白吃掉帧率）
            if (!Core.Ui.BookLayout.CellVisible(grid, cell)) continue;

            float cx = cell.X;
            float cy = cell.Y;

            var rect = new Rectangle((int)cx + 2, (int)cy + 2, (int)Core.Ui.BookLayout.CellW - 6,
                                     (int)Core.Ui.BookLayout.CellH - 6);
            bool selected = i == _selectedIndex;
            bool hover = rect.Contains((int)mouse.X, (int)mouse.Y);

            DrawRect(sb, rect, selected
                ? new Color(70, 56, 112)
                : hover ? new Color(48, 38, 76) : new Color(30, 24, 46));

            if (selected)
            {
                DrawBorder(sb, rect, new Color(200, 170, 255));
            }

            var entry = visible[i];

            // 图案本体
            var center = new Vector2(cx + CellW * 0.5f - 2f, cy + CellH * 0.5f - 10f);
            var color = entry.NotApplicableReason != null
                ? new Color(110, 100, 120)                 // 不适用：灰掉
                : entry.Implemented
                    ? new Color(206, 186, 255)
                    : new Color(150, 130, 140);            // 未实现：暗一点

            PatternRenderer.DrawStaticPreview(line, dot, entry.Def.Prototype, center, 26f, color);

            // 名字优先用官方中文名，没有才退回 id。
            // 太长就截断 —— 泰拉的字符串绘制没有自动省略。
            string shown = entry.Name ?? entry.ShortId;
            string label = shown.Length > 14 ? shown[..13] + "…" : shown;
            Terraria.Utils.DrawBorderString(sb, label,
                new Vector2(cx + 6f, cy + CellH - 22f),
                new Color(190, 182, 215), 0.6f);
        }
    }

    private void DrawDetails(SpriteBatch sb, float x, float y, float w, float h, List<Entry> visible)
    {
        DrawRect(sb, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(26, 20, 40));
        DrawBorder(sb, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(80, 64, 120));

        if (visible.Count == 0) return;
        if (_selectedIndex < 0 || _selectedIndex >= visible.Count) _selectedIndex = 0;

        var e = visible[_selectedIndex];
        float ty = y + 10f;

        void Line(string text, Color color, float scale = 0.72f)
        {
            Terraria.Utils.DrawBorderString(sb, text, new Vector2(x + 12f, ty), color, scale);
            ty += 20f * scale + 4f;
        }

        Line(e.Name ?? e.ShortId, new Color(236, 224, 255), 0.9f);

        // 中文名之下再挂一行英文 id：查 wiki / 抄别人的图时要用 id 对号。
        if (e.Name != null)
        {
            Line(e.ShortId, new Color(150, 145, 175), 0.62f);
        }

        Line($"起始方向：{e.Def.StartDir}", new Color(190, 182, 215));
        Line($"角度序列：{e.Def.Angles}", new Color(190, 182, 215));

        if (e.Argc >= 0)
        {
            Line($"参数个数：{e.Argc}", new Color(190, 182, 215));
        }

        if (e.MediaCost > 0)
        {
            Line($"基础媒质：{Core.Media.MediaConstants.Format(e.MediaCost)}", new Color(196, 168, 255));
        }
        else if (e.MediaCost == 0)
        {
            Line("基础媒质：0（只读查询）", new Color(150, 200, 160));
        }
        else
        {
            Line("媒质消耗：由法术自己决定", new Color(190, 182, 215));
        }

        if (e.Def.RequiresEnlightenment)
        {
            Line("需要启蒙", new Color(255, 210, 120));
        }

        ty += 6f;

        if (e.NotApplicableReason != null)
        {
            Line("不适用于泰拉", new Color(255, 150, 150));
            Line(e.NotApplicableReason, new Color(210, 170, 170), 0.66f);
        }
        else if (e.Implemented)
        {
            Line("已实现", new Color(150, 220, 160));
        }
        else
        {
            Line("尚未实现（开发中）", new Color(230, 180, 140));
        }

        // 放大的图案：详情栏里画大一点，方便看清走线
        var center = new Vector2(x + w * 0.5f, y + h - 86f);
        PatternRenderer.DrawStaticPreview(
            (a, b, thickness, c) => HexPixel.DrawLine(sb, a, b, thickness, c),
            (p, r, c) => HexPixel.DrawDot(sb, p, r, c),
            e.Def.Prototype, center, 62f,
            new Color(226, 210, 255));
    }

    // ── 绘制小工具（原版没有现成的矩形填充，自己写）────────────────

    private static void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
    {
        sb.Draw(HexPixel.Value, rect, color);
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color)
    {
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    /// <summary>
    /// 条目详情页：**整页**显示一个图案。
    ///
    /// 为什么要有这一页（而不是只留右侧小栏）：原版那本书就是「一条图案一整页」，
    /// 图案画得很大、下面才是说明。信息量一样，但**看得清**是重点 ——
    /// 图案是要照着画的东西，画小了根本认不出走线。
    /// </summary>
    private void DrawPage(SpriteBatch sb, float w, float h, List<Entry> visible,
                          Action<Vector2, Vector2, float, Color> line,
                          Action<Vector2, float, Color> dot)
    {
        if (_selectedIndex < 0 || _selectedIndex >= visible.Count) _selectedIndex = 0;
        var e = visible[_selectedIndex];

        var (px, py, pw, ph) = PanelRect(w, h);

        DrawRect(sb, new Rectangle((int)px, (int)py, (int)pw, (int)ph), new Color(18, 14, 30, 240));
        DrawBorder(sb, new Rectangle((int)px, (int)py, (int)pw, (int)ph), new Color(150, 120, 220));

        Terraria.Utils.DrawBorderString(sb, e.Name ?? e.ShortId,
            new Vector2(px + PanelPad, py + 12f), new Color(236, 224, 255), 1.0f);

        if (e.Name != null)
        {
            Terraria.Utils.DrawBorderString(sb, e.ShortId,
                new Vector2(px + PanelPad, py + 40f), new Color(150, 145, 175), 0.68f);
        }

        Terraria.Utils.DrawBorderString(sb, "左键/右键返回目录 · Esc 关闭",
            new Vector2(px + pw - 240f, py + 16f), new Color(150, 145, 175), 0.65f);

        // 大图案：占据上半部分
        var center = new Vector2(px + pw * 0.5f, py + ph * 0.34f);
        float size = System.Math.Min(pw, ph) * 0.26f;
        PatternRenderer.DrawStaticPreview(line, dot, e.Def.Prototype, center, size,
            e.NotApplicableReason != null ? new Color(140, 130, 150) : new Color(226, 210, 255));

        // 说明：逐行列在下方
        float ty = py + ph * 0.62f;
        float tx = px + PanelPad * 2f;

        void Row(string label, string value, Color color)
        {
            Terraria.Utils.DrawBorderString(sb, label, new Vector2(tx, ty), new Color(160, 152, 190), 0.75f);
            Terraria.Utils.DrawBorderString(sb, value, new Vector2(tx + 150f, ty), color, 0.78f);
            ty += 24f;
        }

        Row("起始方向", e.Def.StartDir.ToString(), new Color(210, 200, 240));
        Row("角度序列", e.Def.Angles, new Color(210, 200, 240));
        Row("分类", e.Category, new Color(210, 200, 240));

        if (e.Argc >= 0) Row("参数个数", e.Argc.ToString(), new Color(210, 200, 240));

        if (e.MediaCost > 0)
            Row("基础媒质", Core.Media.MediaConstants.Format(e.MediaCost), new Color(196, 168, 255));
        else if (e.MediaCost == 0)
            Row("基础媒质", "0（只读查询）", new Color(150, 200, 160));
        else
            Row("媒质消耗", "由法术自己决定", new Color(190, 182, 215));

        if (e.Def.RequiresEnlightenment)
            Row("门槛", "需要启蒙（大法术）", new Color(255, 210, 120));

        // 坐标类常数最容易踩坑：泰拉的 +Y 是**向下**（MC 是向上），
        // 所以 py 是「下」、ny 才是「上」。这一句直接写在页面上，
        // 免得玩家照着 MC 的教程画完发现法术往下钻。
        if (e.ShortId.StartsWith("const/vec/", StringComparison.Ordinal))
        {
            Row("注意", "泰拉坐标 +Y 向下：py = 下，ny = 上", new Color(255, 200, 140));
        }

        if (e.NotApplicableReason != null)
        {
            Row("状态", "不适用于泰拉", new Color(255, 150, 150));
            Row("原因", e.NotApplicableReason, new Color(210, 170, 170));
        }
        else if (e.Implemented)
        {
            Row("状态", "已实现", new Color(150, 220, 160));
        }
        else
        {
            Row("状态", "尚未实现（开发中）", new Color(230, 180, 140));
        }
    }
}
