using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client.UI;

/// <summary>已画完的一条图案 + 注册表匹配结果（null = 未命中）+ 求值结果（决定颜色）。</summary>
public sealed class ResolvedPattern
{
    internal ResolvedPattern(DrawnPattern drawn)
    {
        Drawn = drawn;
        Matched = PatternRegistry.Match(drawn.Pattern);
    }

    internal DrawnPattern Drawn { get; }
    public HexPattern Pattern => Drawn.Pattern;
    public HexCoord Origin => Drawn.Origin;
    public PatternDef? Matched { get; }
    public bool IsValid => Matched != null;
    public ResolvedPatternType Type => Drawn.Type;
}

/// <summary>附属画在画布上的一条图案（颜色 ARGB：尾 / 头渐变，同原版 RenderLib 的两色）。</summary>
public readonly record struct CanvasOverlay(HexPattern Pattern, HexCoord Origin, uint Tail, uint Head);

/// <summary>画布状态（兼容旧调用方的名字）。</summary>
public enum DrawState
{
    BetweenPatterns = DrawPhase.BetweenPatterns,
    JustStarted = DrawPhase.JustStarted,
    Drawing = DrawPhase.Drawing,
}

/// <summary>
/// 咒术绘制画布：逻辑全部在 <see cref="PatternDrawer"/>（Core，离线可测），
/// 线型全部在 <see cref="PatternGeometry"/>（Core，逐行移植 RenderLib），这里只负责接线与提交绘制。
///
/// 对齐 GuiSpellcasting.render 的绘制顺序：引导点 → 已画图案 → 正在画的图案（最后一点是鼠标）。
/// 坐标一律是**屏幕像素**（界面缩放无关）：网格、鼠标、绘制用同一套，
/// 否则界面缩放 ≠ 100% 时笔迹会偏离光标。
/// </summary>
public sealed class HexCanvas
{
    /// <summary>引导点显示半径（原版 3）。</summary>
    public const int GuideDotRadius = 3;

    private readonly PatternDrawer _drawer = new();
    private readonly List<ResolvedPattern> _resolved = new();
    private readonly List<ColoredVertex> _verts = new(8192);

    /// <summary>吸附阈值（原版 gridSnapThreshold，默认 0.5）。</summary>
    public float SnapThreshold
    {
        get => _drawer.SnapThreshold;
        set => _drawer.SnapThreshold = value;
    }

    /// <summary>网格缩放（原版 GRID_ZOOM 属性）。</summary>
    public float Zoom { get; set; } = 1.0f;

    /// <summary>笔迹粗细倍率（设置项 StrokeScale）。线宽、节点、引导点一起缩放。</summary>
    public float StrokeScale { get; set; } = 0.5f;

    /// <summary>电光抖动强度倍率（设置项 WobbleScale）。1.0 = 原版 variance 2.5。</summary>
    public float WobbleScale { get; set; } = 0.5f;

    public bool IsOpen { get; private set; }

    public IReadOnlyList<ResolvedPattern> Patterns => _resolved;
    public ResolvedPattern? LastPattern => _resolved.Count > 0 ? _resolved[^1] : null;
    public DrawState State => (DrawState)_drawer.Phase;
    public HexPattern? WipPattern => _drawer.Wip;
    public HexCoord AnchorCoord => _drawer.Phase == DrawPhase.Drawing ? _drawer.Current : _drawer.Start;

    /// <summary>
    /// 动画时钟，单位 = MC 游戏刻（每秒 20）。原版电光流动速度按 ClientTickCounter 计，
    /// 以前这里每帧 +1（每秒 60），电光流动快了 3 倍。
    /// </summary>
    public double Tick { get; set; }

    public void Open() => IsOpen = true;

    public void Close(bool clearPatterns = true)
    {
        IsOpen = false;
        _drawer.Cancel();
        if (clearPatterns) Reset();
    }

    public void Reset()
    {
        _drawer.Reset();
        _resolved.Clear();
    }

    /// <summary>放弃正在画的一笔（不关画布）。</summary>
    public void CancelStroke() => _drawer.Cancel();

    public float HexSize(float viewWidth, float viewHeight) => HexGrid.HexSize(viewWidth, viewHeight, Zoom);

    private static Vec2f Offset(float w, float h) => new(w * 0.5f, h * 0.5f);

    public Vector2 CoordToPx(HexCoord c, float w, float h) => HexGrid.CoordToPx(c, HexSize(w, h), Offset(w, h)).ToXna();

    public HexCoord PxToCoord(Vector2 px, float w, float h) => HexGrid.PxToCoord(px.ToCore(), HexSize(w, h), Offset(w, h));

    /// <summary>原版 drawStart。返回是否真的落笔。</summary>
    public bool DrawStart(Vector2 mouse, float w, float h)
        => IsOpen && _drawer.Begin(Clamp(mouse, w, h).ToCore(), HexSize(w, h), Offset(w, h));

    /// <summary>
    /// 原版 drawMove，但沿上一帧到这一帧的鼠标路径逐步采样（见 <see cref="PatternDrawer.MoveAlong"/>）。
    /// 返回本帧的吸附事件，调用方据此播放音效。
    /// </summary>
    public List<MoveResult> DrawMove(Vector2 from, Vector2 to, float w, float h)
    {
        if (!IsOpen) return new List<MoveResult>();
        return _drawer.MoveAlong(Clamp(from, w, h).ToCore(), Clamp(to, w, h).ToCore(), HexSize(w, h), Offset(w, h));
    }

    /// <summary>原版 drawEnd。</summary>
    public ResolvedPattern? DrawEnd()
    {
        if (!IsOpen) return null;
        var drawn = _drawer.End();
        if (drawn is null) return null;
        var rp = new ResolvedPattern(drawn);
        _resolved.Add(rp);
        return rp;
    }

    /// <summary>这个格点被已画的图案占了吗。</summary>
    public bool IsUsed(HexCoord c) => _drawer.IsUsed(c);

    /// <summary>当前这一笔的起点（JUSTSTARTED 时就是按下的那个格点；附属的自动补全从这里开始）。</summary>
    public HexCoord DrawStartCoord => _drawer.Start;

    /// <summary>放弃正在开始 / 正在画的这一笔（附属开始打字时，上游 CastRef.stopDrawing）。</summary>
    public void CancelDrawing() => _drawer.Cancel();

    /// <summary>把一整条图案直接放在某个格点上（附属的键盘绘制 / 自动补全）。放完照常送去求值，见 HexClientSystem.Submit。</summary>
    public ResolvedPattern PlacePattern(HexPattern pattern, HexCoord origin)
    {
        var rp = new ResolvedPattern(_drawer.Place(pattern, origin));
        _resolved.Add(rp);
        return rp;
    }

    /// <summary>
    /// 这一帧额外画的图案（附属的预览 / 虚影）：扩展每帧清空再填，和已画图案同一套线型、同一次提交。
    /// </summary>
    public List<CanvasOverlay> Overlays { get; } = new();

    /// <summary>附属（Hexcessible showAllDots）：引导点画到很远（半径 50 格），远处的点保持一半大小与亮度。每帧重设。</summary>
    public bool ShowAllDots { get; set; }

    /// <summary>附属（Hexcessible dimmed）：画布底下铺一层 MC 界面的暗色背景。每帧重设。</summary>
    public bool Dimmed { get; set; }

    /// <summary>求值结果回来了：给最后一条图案上色（原版 recvServerUpdate）。</summary>
    public void ApplyResolution(ResolvedPatternType type)
        => _drawer.ApplyResolution(type, p => PatternRegistry.Match(p)?.Id is "open_paren" or "read_into_parens");

    private static Vector2 Clamp(Vector2 m, float w, float h) => new(Math.Clamp(m.X, 0, w), Math.Clamp(m.Y, 0, h));

    /// <summary>
    /// 画布内容。必须在一个以**屏幕像素**为坐标的界面层里调用（InterfaceScaleType.None）。
    /// <paramref name="showStrokeOrder"/> = 原版按住 Ctrl 显示笔顺渐变。
    /// </summary>
    public void DrawContent(float w, float h, Vector2 mouseXna, bool showStrokeOrder, Matrix spriteBatchTransform)
    {
        if (!IsOpen) return;

        float size = HexSize(w, h);
        var offset = Offset(w, h);
        var mouse = mouseXna.ToCore();
        // 原版在 MC 的 GUI 坐标里画，线宽 5 / 点半径 2 都是 GUI 单位。
        // 原版格距 = √(宽×高/512) 个 GUI 单位；1080p 自动界面缩放 4 时约 16 个 GUI 单位。
        // 按「一个格距 = 16 GUI 单位」换算，保持线宽与格距的比例不随分辨率变。
        // 再乘玩家设置的粗细倍率（默认 0.5：按 1.0 画玩家反馈偏粗）。
        float unit = size / 16f * StrokeScale;
        _verts.Clear();

        // ---- 引导点 ----
        var mouseCoord = HexGrid.PxToCoord(mouse, size, offset);
        // 原版半径 3；Hexcessible showAllDots 只把遍历范围放到 50（淡出仍按 3 算），并把最暗压在 0.5
        float minScale = ShowAllDots ? 0.5f : 0f;
        foreach (var dot in HexGrid.RangeAround(mouseCoord, ShowAllDots ? 50 : GuideDotRadius))
        {
            if (_drawer.IsUsed(dot)) continue;
            var px = HexGrid.CoordToPx(dot, size, offset);
            float delta = (px - mouse).Length;
            // 贴着光标 = 1，半径边缘 = 0，这样点不会突然出现/消失；减去一个格距让光标附近留一小块全亮区
            float s = Math.Clamp(1f - (delta - size) / (GuideDotRadius * size), minScale, 1f);
            PatternGeometry.Spot(_verts, px, s * 2f * unit, PatternGeometry.Argb(
                (int)(s * 255), (int)(MathHelper.Lerp(0.4f, 0.5f, s) * 255),
                (int)(MathHelper.Lerp(0.8f, 1.0f, s) * 255), (int)(MathHelper.Lerp(0.7f, 0.9f, s) * 255)));
        }

        // ---- 已画完的图案 ----
        for (int idx = 0; idx < _resolved.Count; idx++)
        {
            var rp = _resolved[idx];
            var (color, fade, success) = PatternGeometry.ResolvedColors(rp.Type);
            var positions = rp.Pattern.Positions();
            var pts = HexGrid.PatternLinePoints(rp.Pattern, rp.Origin, size, offset);
            PatternGeometry.PatternFromPoints(_verts, pts, PatternGeometry.FindDupIndices(positions), true,
                color | 0xC8000000u, fade | 0xC8000000u, success ? 0.2f : 0.9f,
                PatternGeometry.DefaultReadabilityOffset, 1f, idx, Tick, unit, showStrokeOrder,
                PatternGeometry.Variance * WobbleScale);
        }

        // ---- 附属的预览 / 虚影 ----
        foreach (var o in Overlays)
        {
            var pts = HexGrid.PatternLinePoints(o.Pattern, o.Origin, size, offset);
            PatternGeometry.PatternFromPoints(_verts, pts, PatternGeometry.FindDupIndices(o.Pattern.Positions()), false,
                o.Tail, o.Head, 0.1f, PatternGeometry.DefaultReadabilityOffset, 1f, _resolved.Count, Tick, unit, false,
                PatternGeometry.Variance * WobbleScale);
        }

        // ---- 正在画的图案：已吸附的格点 + 鼠标当前位置 ----
        if (_drawer.Phase != DrawPhase.BetweenPatterns)
        {
            var pts = new List<Vec2f>();
            ISet<int>? dup = null;
            if (_drawer.Phase == DrawPhase.JustStarted)
            {
                pts.Add(HexGrid.CoordToPx(_drawer.Start, size, offset));
            }
            else
            {
                var positions = _drawer.Wip!.Positions();
                dup = PatternGeometry.FindDupIndices(positions);
                pts.AddRange(HexGrid.PatternLinePoints(_drawer.Wip!, _drawer.Start, size, offset));
            }
            pts.Add(mouse);
            // 以图案条数为种子：收笔变成已画图案时电光不会跳
            PatternGeometry.PatternFromPoints(_verts, pts, dup, false, PatternGeometry.WipTail, PatternGeometry.WipHead,
                0.1f, PatternGeometry.DefaultReadabilityOffset, 1f, _resolved.Count, Tick, unit, showStrokeOrder,
                PatternGeometry.Variance * WobbleScale);
        }

        PrimitiveBatch.Flush(_verts, spriteBatchTransform);
    }
}
