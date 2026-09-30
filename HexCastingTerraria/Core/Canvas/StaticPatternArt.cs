using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Canvas;

/// <summary>
/// 图案的「形状与摆放」设置。移植自源项目 client/render/PatternSettings（PositionSettings + StrokeSettings + ZappySettings）。
/// 所有长度都是「单位方格」里的比例（原版叫 pose units：一块方块 / 一张卷轴 = 1）。
/// 只移植了原版实际用到的「居中方块」摆法（PositionSettings.paddedSquare：1×1、两轴 CENTER_FIT）。
/// </summary>
public sealed record PatternStyle(
    string Name,
    double Padding, double BaseScale, double MinSize,
    double InnerWidth, double OuterWidth, double StartDotRadius, double GridDotRadius,
    int Hops, float Variance, float Speed, float FlowIrregular, float ReadabilityOffset, float LastSegmentProp)
{
    /// <summary>原版 StrokeSettings.fromStroke：内线 = 2/5，起笔点 = 0.8 × 内线，格点 = 0.4 × 内线。</summary>
    private static PatternStyle Square(string name, double padding, double stroke,
        int hops, float variance, float speed, float flow, float readable, float lastSeg)
        => new(name, padding, 0.25, 0,
            stroke * 2.0 / 5.0, stroke, 0.8 * stroke * 2.0 / 5.0, 0.4 * stroke * 2.0 / 5.0,
            hops, variance, speed, flow, readable, lastSeg);

    // 原版 ZappySettings：STATIC(10, 0.5, 0, 0.2, 0, 1)、READABLE(10, 0.5, 0, 0.2, 0.2, 0.8)、WOBBLY(10, 2.5, 0.1, 0.2, 0, 1)

    /// <summary>原版 WorldlyPatternRenderHelpers.SCROLL_SETTINGS / WORLDLY_SETTINGS：挂轴、石板、阿卡夏。</summary>
    public static readonly PatternStyle Worldly = Square("worldly", 2.0 / 16, 0.8 / 16, 10, 0.5f, 0f, 0.2f, 0f, 1f);

    /// <summary>原版 READABLE_SCROLL_SETTINGS：卷轴提示框（拐角往里收、最后一段缩短，笔顺看得出来）。</summary>
    public static readonly PatternStyle Readable = Square("scroll_readable", 2.0 / 16, 0.8 / 16, 10, 0.5f, 0f, 0.2f, 0.2f, 0.8f);

    /// <summary>原版 WORLDLY_SETTINGS_WOBBLY：法术环走到的石板（抖动的电光）。</summary>
    public static readonly PatternStyle Wobbly = Square("wobbly_world", 2.0 / 16, 0.8 / 16, 10, 2.5f, 0.1f, 0.2f, 0f, 1f);

    public double StrokeGuess => System.Math.Max(OuterWidth, InnerWidth);
}

/// <summary>图案各部分的颜色（0xAARRGGBB；alpha 为 0 的部分不画）。移植自源项目 PatternColors。</summary>
public readonly record struct PatternPalette(uint InnerStart, uint InnerEnd, uint OuterStart, uint OuterEnd, uint StartDot, uint GridDots)
{
    public const uint StartingDot = 0xff_5b7bd7;
    public const uint GridDotColor = 0x80_d2c8c8;

    /// <summary>原版 DEFAULT_PATTERN_COLOR：内线深 0xff554d54、外线浅 0xffd2c8c8。</summary>
    public static readonly PatternPalette Default = Solid(0xff_554d54, 0xff_d2c8c8);

    /// <summary>原版 DIMMED_COLOR → DEFAULT_GRADIENT_COLOR（按住 Ctrl 看笔顺）。</summary>
    public static readonly PatternPalette DefaultGradient = Default with { InnerEnd = 0xff_b4aaaa, OuterEnd = 0xff_d2c8c8 };

    /// <summary>原版 SLATE_WOBBLY_PURPLE_COLOR：glowyStroke(0xffcfa0f3)，内线 = screenCol(色)。</summary>
    public static readonly PatternPalette SlatePurple = Solid(PatternGeometry.ScreenCol(0xff_cfa0f3), 0xff_cfa0f3);

    public static PatternPalette Solid(uint inner, uint outer) => new(inner, inner, outer, outer, 0, 0);

    public PatternPalette WithDots(bool startingDot, bool gridDots)
        => this with { StartDot = startingDot ? StartingDot : 0, GridDots = gridDots ? GridDotColor : 0 };
}

/// <summary>
/// 静态图案渲染（卷轴、石板、挂轴、阿卡夏、提示框）。移植自源项目 PatternRenderer.renderPattern + HexPatternPoints。
///
/// 做法与原版一致：图案按 1 格 = 1 的格点坐标取折线 → makeZappy（静态：速度 0）→ 按 PatternStyle 缩放并在方格里居中
/// → 先画外线、再画内线 → 起笔点 / 格点。输出三角形（由 PrimitiveBatch 交给显卡）。
/// 画布上正在画的图案走的是 GuiSpellcasting 那一套（PatternGeometry.PatternFromPoints），不在这里。
/// </summary>
public static class StaticPatternArt
{
    /// <summary>算好的摆放：把格点坐标映射到单位方格里（原版 HexPatternPoints）。</summary>
    public readonly struct Layout
    {
        public readonly double MinX, MinY, FinalScale, OffsetX, OffsetY;

        public Layout(double minX, double minY, double finalScale, double offsetX, double offsetY)
        {
            MinX = minX; MinY = minY; FinalScale = finalScale; OffsetX = offsetX; OffsetY = offsetY;
        }

        public Vec2f Map(Vec2f p) => new(
            (float)(((p.X - MinX) * FinalScale) + OffsetX),
            (float)(((p.Y - MinY) * FinalScale) + OffsetY));
    }

    /// <summary>原版 HexPatternLike.getNonZappyPoints：pattern.toLines(1, 0)。</summary>
    public static List<Vec2f> BarePoints(HexPattern pattern)
        => HexGrid.PatternLinePoints(pattern, HexCoord.Origin, 1f, new Vec2f(0f, 0f));

    /// <summary>原版 HexPatternPoints 的构造：用**静态**电光的包围盒定比例（这样动起来也不会忽大忽小）。</summary>
    public static Layout Place(IReadOnlyList<Vec2f> staticZappy, PatternStyle st)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in staticZappy)
        {
            minX = System.Math.Min(minX, p.X); maxX = System.Math.Max(maxX, p.X);
            minY = System.Math.Min(minY, p.Y); maxY = System.Math.Max(maxY, p.Y);
        }
        double rangeX = maxX - minX, rangeY = maxY - minY;

        // 相邻两点相隔 baseScale / 1.5
        double baseScale = st.BaseScale / 1.5;
        double baseW = rangeX * baseScale, baseH = rangeY * baseScale;

        double scale = System.Math.Max(1.0, System.Math.Max(
            (st.MinSize - st.StrokeGuess) / baseW, (st.MinSize - st.StrokeGuess) / baseH));
        // 两轴都是 CENTER_FIT：放不下就缩小
        scale = System.Math.Min(scale, (1.0 - 2 * st.Padding - st.StrokeGuess) / baseH);
        scale = System.Math.Min(scale, (1.0 - 2 * st.Padding - st.StrokeGuess) / baseW);

        double finalScale = baseScale * scale;
        double inherentW = (baseW * scale) + 2 * st.Padding + st.OuterWidth;
        double inherentH = (baseH * scale) + 2 * st.Padding + st.OuterWidth;
        double widthDiff = System.Math.Max(1.0 - inherentW, 0), heightDiff = System.Math.Max(1.0 - inherentH, 0);
        double offsetX = ((inherentW - baseW * scale) / 2) + (widthDiff / 2);
        double offsetY = ((inherentH - baseH * scale) / 2) + (heightDiff / 2);
        return new Layout(minX, minY, finalScale, offsetX, offsetY);
    }

    /// <summary>
    /// 把一个图案画进左上角在 <paramref name="topLeft"/>、边长 <paramref name="size"/> 像素的方格里。
    /// <paramref name="time"/> 只影响会动的样式（Wobbly）；<paramref name="strokeOrder"/> = 原版按住 Ctrl 显示笔顺渐变。
    /// </summary>
    public static void Render(List<ColoredVertex> o, HexPattern pattern, PatternStyle st, PatternPalette pal,
        double seed, double time, Vec2f topLeft, float size, bool strokeOrder = false)
    {
        var bare = BarePoints(pattern);
        if (bare.Count < 2) return;
        var dups = PatternGeometry.FindDupIndices(pattern.Positions());
        var staticZappy = PatternGeometry.MakeZappy(bare, dups, st.Hops, st.Variance, 0f, st.FlowIrregular,
            st.ReadabilityOffset, st.LastSegmentProp, seed, 0);
        var layout = Place(staticZappy, st);
        var zappy = st.Speed == 0f
            ? staticZappy
            : PatternGeometry.MakeZappy(bare, dups, st.Hops, st.Variance, st.Speed, st.FlowIrregular,
                st.ReadabilityOffset, st.LastSegmentProp, seed, time);

        Vec2f Px(Vec2f p) => topLeft + layout.Map(p) * size;
        var pts = new List<Vec2f>(zappy.Count);
        foreach (var p in zappy) pts.Add(Px(p));

        if (PatternGeometry.A(pal.OuterStart) != 0 && PatternGeometry.A(pal.OuterEnd) != 0)
        {
            PatternGeometry.LineSeq(o, pts, (float)(st.OuterWidth * size), pal.OuterStart, pal.OuterEnd, strokeOrder);
        }
        if (PatternGeometry.A(pal.InnerStart) != 0 && PatternGeometry.A(pal.InnerEnd) != 0)
        {
            PatternGeometry.LineSeq(o, pts, (float)(st.InnerWidth * size), pal.InnerStart, pal.InnerEnd, strokeOrder);
        }
        if (PatternGeometry.A(pal.StartDot) != 0)
        {
            PatternGeometry.Spot(o, Px(bare[0]), (float)(st.StartDotRadius * size), pal.StartDot);
        }
        if (PatternGeometry.A(pal.GridDots) != 0)
        {
            for (int i = 1; i < bare.Count; i++)
            {
                PatternGeometry.Spot(o, Px(bare[i]), (float)(st.GridDotRadius * size), pal.GridDots);
            }
        }
    }
}
