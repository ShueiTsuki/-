namespace HexCastingTerraria.Core.Ui;

/// <summary>浮点矩形。Core 不许引用 XNA（也就不能用 `Rectangle`/`Vector2`），所以自带一个。</summary>
public readonly struct RectF
{
    public readonly float X;
    public readonly float Y;
    public readonly float W;
    public readonly float H;

    public RectF(float x, float y, float w, float h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    public float Right => X + W;
    public float Bottom => Y + H;

    public bool Contains(float px, float py) => px >= X && px <= Right && py >= Y && py <= Bottom;

    /// <summary>与另一个矩形是否有重叠面积（相切不算）。</summary>
    public bool Overlaps(RectF o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public override string ToString() => $"({X:0.#}, {Y:0.#}, {W:0.#}×{H:0.#})";
}

/// <summary>
/// 咒法学之书的**布局数学**。
///
/// ## 为什么要把这段从 `HexBook` 里抽出来
///
/// 抽出来之前，同一套「第几个格子在哪、点在哪个格子上」的算法在 `HexBook` 里
/// **写了两遍**：一遍给命中判定（输入阶段），一遍给绘制（绘制阶段）。
/// 两份只要有一点不一致，表现就是「点不中」「点到了隔壁」——
/// 而且**不会有任何报错**，只能靠人一个一个点着试。
///
/// 抽到这里之后它成了纯数学（不碰 XNA），于是可以离线断言那条最关键的性质：
/// **`Cell(i)` 的中心点必须被 `IndexAt` 判回 `i`** ——
/// 画在哪、点在哪，由构造保证一致，而不是靠"两段代码长得像"。
///
/// 布局本身保持与抽取前**逐字一致**（同样的常量、同样的取整方式），
/// 唯一修正的是滚动范围：原来用 `count * CellW / gridW` 近似行数，
/// 列数其实是 `floor(gridW / CellW)`，两者在边界上会差一行。现在按列数算。
/// </summary>
public static class BookLayout
{
    /// <summary>单个条目的格子尺寸。</summary>
    public const float CellW = 92f;

    public const float CellH = 92f;

    /// <summary>面板内边距。</summary>
    public const float PanelPad = 16f;

    /// <summary>右侧详情栏宽度。</summary>
    public const float DetailWidth = 260f;

    /// <summary>面板顶部（标题 + 分类标签）占的高度。</summary>
    public const float HeaderHeight = 84f;

    public const float MaxPanelW = 900f;

    public const float MaxPanelH = 560f;

    /// <summary>面板与屏幕边缘的最小留白。</summary>
    public const float PanelMargin = 80f;

    /// <summary>面板矩形（屏幕坐标，居中）。</summary>
    public static RectF Panel(float viewW, float viewH)
    {
        float pw = System.Math.Min(MaxPanelW, viewW - PanelMargin);
        float ph = System.Math.Min(MaxPanelH, viewH - PanelMargin);
        return new RectF((viewW - pw) * 0.5f, (viewH - ph) * 0.5f, pw, ph);
    }

    /// <summary>条目网格区域（去掉顶部标题栏与右侧详情栏）。</summary>
    public static RectF Grid(RectF panel)
        => new(panel.X + PanelPad,
               panel.Y + HeaderHeight,
               panel.W - DetailWidth - PanelPad * 2f,
               panel.H - HeaderHeight - PanelPad);

    /// <summary>网格列数。至少 1 —— 面板再窄也不能出现 0 列（会除零/丢内容）。</summary>
    public static int Columns(float gridW) => System.Math.Max(1, (int)(gridW / CellW));

    /// <summary>放下 <paramref name="count"/> 个条目需要多少行。</summary>
    public static int Rows(int count, float gridW)
    {
        if (count <= 0) return 0;
        int cols = Columns(gridW);
        return (count + cols - 1) / cols;
    }

    /// <summary>内容总高度（用于滚动范围）。</summary>
    public static float ContentHeight(int count, float gridW) => Rows(count, gridW) * CellH;

    /// <summary>最大滚动量。内容装得下就是 0。</summary>
    public static float MaxScroll(int count, RectF grid)
        => System.Math.Max(0f, ContentHeight(count, grid.W) - grid.H);

    /// <summary>第 <paramref name="index"/> 个条目的格子矩形（已含滚动偏移）。</summary>
    public static RectF Cell(RectF grid, int index, float scroll)
    {
        int cols = Columns(grid.W);
        int col = index % cols;
        int row = index / cols;
        return new RectF(grid.X + col * CellW, grid.Y + row * CellH - scroll, CellW, CellH);
    }

    /// <summary>
    /// 鼠标落在第几个条目上；-1 表示没落在任何有效条目上。
    ///
    /// ⚠️ 它与 <see cref="Cell"/> 必须互为逆运算 —— 这是本文件存在的全部理由，
    /// 由 `drawtest` 的对拍断言保证。
    /// </summary>
    public static int IndexAt(RectF grid, float mouseX, float mouseY, int count, float scroll)
    {
        if (mouseX < grid.X || mouseX > grid.Right) return -1;
        if (mouseY < grid.Y || mouseY > grid.Bottom) return -1;

        int cols = Columns(grid.W);
        int col = (int)((mouseX - grid.X) / CellW);
        int row = (int)((mouseY - grid.Y + scroll) / CellH);
        int index = row * cols + col;

        if (col < 0 || col >= cols) return -1;
        if (index < 0 || index >= count) return -1;
        return index;
    }

    /// <summary>
    /// 该格子是否有**可见面积**（用于跳过屏幕外的绘制）。
    ///
    /// ⚠️ 必须用严格不等号。第一版写的是 `!(cell.Bottom &lt; grid.Y || cell.Y &gt; grid.Bottom)`，
    /// 于是"底边正好贴住网格顶部"（可见高度为 0）的格子被算作可见 ——
    /// 它会被画一次、却点不中（点那个位置属于下一行的格子）。
    /// 这类"画了但点不到"的边界不一致，是靠 `drawtest` 的
    /// 「CellVisible 说可见的格子必须真的点得到」这条对拍断言抓出来的。
    /// </summary>
    public static bool CellVisible(RectF grid, RectF cell)
        => cell.Bottom > grid.Y && cell.Y < grid.Bottom;

    /// <summary>分类标签条的位置（在标题下方、网格上方）。</summary>
    public static RectF CategoryStrip(RectF panel)
        => new(panel.X + PanelPad, panel.Y + 62f, panel.W - PanelPad * 2f, 20f);

    /// <summary>右侧详情栏。</summary>
    public static RectF Detail(RectF panel)
        => new(panel.Right - DetailWidth - PanelPad,
               panel.Y + HeaderHeight,
               DetailWidth,
               panel.H - HeaderHeight - PanelPad);
}
