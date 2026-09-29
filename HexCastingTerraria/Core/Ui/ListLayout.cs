namespace HexCastingTerraria.Core.Ui;

/// <summary>
/// 竖列表的几何：行位置、内容总高、滚动范围、可见行区间。
///
/// ## 为什么要把这点数学抽出来
///
/// 换成 tModLoader 的 `UIElement` 元素树之后，**离线验证闭环断了** ——
/// `UIElement` 需要 tModLoader，drawtest 编译不了，所以那部分只能进游戏才知道对错。
/// 我因此连着出了三个「编译通过、实机是空的/不动」的 bug。
///
/// 但列表的几何**本身是纯数学**，不依赖任何框架：第 i 行画在哪、内容多高、
/// 能滚多远、哪几行落在视口里。把它抽到 Core/ 就重新可测了 ——
/// 而这几条恰恰是「列表溢出」「滚动偏移错了」的根源。
///
/// 于是分工变成：
///   · 几何（这份）—— 离线断言，确定
///   · 元素树怎么摆 —— 把这里算出的数字喂给 `Top.Set(...)`，机械动作
/// 框架出问题只会让"摆"不对，不会让"算"不对；两者分开就查得动。
/// </summary>
public static class ListLayout
{
    /// <summary>内容总高（含上下留白）。</summary>
    public static float ContentHeight(int count, float rowH, float topPad = 0f, float bottomPad = 0f)
    {
        if (count <= 0) { return topPad + bottomPad; }
        return topPad + (count * rowH) + bottomPad;
    }

    /// <summary>最多能滚多远。内容装得下就是 0（不出现"能滚但没东西可滚"）。</summary>
    public static float MaxScroll(int count, float rowH, float viewH, float topPad = 0f, float bottomPad = 0f)
    {
        float max = ContentHeight(count, rowH, topPad, bottomPad) - viewH;
        return max < 0f ? 0f : max;
    }

    /// <summary>把滚动位置夹到合法范围。滚过头会让最后一行悬在视口外，看起来像"列表没到底"。</summary>
    public static float ClampOffset(float offset, int count, float rowH, float viewH,
                                    float topPad = 0f, float bottomPad = 0f)
    {
        if (offset < 0f) { return 0f; }
        float max = MaxScroll(count, rowH, viewH, topPad, bottomPad);
        return offset > max ? max : offset;
    }

    /// <summary>第 <paramref name="index"/> 行相对视口顶部的 Y。调用方把它喂给 <c>Top.Set</c>。</summary>
    public static float RowTop(int index, float rowH, float offset, float topPad = 0f)
        => topPad + (index * rowH) - offset;

    /// <summary>
    /// 落在视口内的行区间（半开区间 <c>[First, Last)</c>）。
    ///
    /// 用于「只画看得见的行」—— 行数多起来之后这是必要的，
    /// 也是判断"某一行为什么没画出来"的第一手依据。
    /// </summary>
    public static (int First, int Last) VisibleRange(int count, float rowH, float viewH, float offset)
    {
        if (count <= 0 || rowH <= 0f) { return (0, 0); }

        int first = (int)System.Math.Floor(offset / rowH);
        if (first < 0) { first = 0; }
        if (first > count) { first = count; }

        int last = (int)System.Math.Ceiling((offset + viewH) / rowH);
        if (last > count) { last = count; }
        if (last < first) { last = first; }

        return (first, last);
    }

    /// <summary>某一行的顶部是否落在视口内（用于断言"可见的行真的画得到"）。</summary>
    public static bool RowVisible(int index, int count, float rowH, float viewH, float offset, float topPad = 0f)
    {
        if (index < 0 || index >= count) { return false; }
        float top = RowTop(index, rowH, offset, topPad);
        return top + rowH > 0f && top < viewH;
    }
}
