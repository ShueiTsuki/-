using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using XnaColor = Microsoft.Xna.Framework.Color;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 已画完的一条图案：图案本体、起始格点、匹配到的定义（null 表示未命中）。
/// </summary>
public sealed class ResolvedPattern
{
    public required HexPattern Pattern { get; init; }
    public required HexCoord Origin { get; init; }
    public PatternDef? Matched { get; init; }

    public bool IsValid => Matched != null;
}

/// <summary>
/// 绘制状态机。与源项目 GuiSpellcasting.PatternDrawState 一一对应。
///
/// 注意：源项目把 current 定义为「图案当前的终点格」，
/// drawMove 的锚点取自它，而不是「下一格」。
/// </summary>
public enum DrawState
{
    /// <summary>等待落笔。</summary>
    BetweenPatterns,

    /// <summary>已落笔但还没有画出第一段线。</summary>
    JustStarted,

    /// <summary>正在绘制。</summary>
    Drawing,
}

/// <summary>
/// 咒术绘制画布。
///
/// 逐行对齐 at.petrak.hexcasting.client.gui.GuiSpellcasting：
///   - 全屏、无背景板
///   - 网格原点在屏幕中心
///   - 格点间距 = √(w×h/512) / zoom
///   - 鼠标周围半径 3 格显示引导点，亮度按距离插值
///   - 吸附：anchor.distanceToSqr(mouse) >= hexSize²×2×threshold（平方比平方）
///   - 反方向拖拽触发回溯（删掉最后一笔）
///   - 已用格点不再提示、也不能在其上落笔
/// </summary>
public sealed class HexCanvas
{
    /// <summary>引导点显示半径（源项目为 3）。</summary>
    public const int GuideDotRadius = 3;

    /// <summary>
    /// 吸附阈值系数。源项目 `gridSnapThreshold`，取值 clamp 到 0.5~1.0，
    /// 而它的**默认值是 0.5**（`HexConfig.DEFAULT_GRID_SNAP_THRESHOLD`）。
    ///
    /// ⚠️ 这里曾经默认 1.0，是个实打实的 bug：每记一步要求的拖拽距离变成
    ///     hexSize² × 2 × 1.0 → √2·hexSize ≈ 1.41 个格距，
    /// 而格点间距**就是** hexSize（见 HexGrid 的几何约定）。于是图案永远落后鼠标 41%，
    /// 玩家拖到图案终点松开时，最后一段永远记不上 —— 表现为「画的 pi 被识别成 qdwd」。
    /// 0.5 时每步恰好 1 个格距，鼠标走到哪个格点图案就画到哪个格点。
    /// </summary>
    public float SnapThreshold { get; set; } = 0.5f;

    /// <summary>网格缩放（对应源项目 GRID_ZOOM 属性）。</summary>
    public float Zoom { get; set; } = 1.0f;

    public bool IsOpen { get; private set; }

    private readonly List<ResolvedPattern> _patterns = new();
    private readonly HashSet<HexCoord> _usedSpots = new();

    private DrawState _state = DrawState.BetweenPatterns;

    /// <summary>图案起点（JUST_STARTED 时也是起点）。</summary>
    private HexCoord _start;

    /// <summary>图案当前的终点格。drawMove 的锚点。对应源项目的 current。</summary>
    private HexCoord _current;

    private HexPattern? _wipPattern;

    public IReadOnlyList<ResolvedPattern> Patterns => _patterns;

    public ResolvedPattern? LastPattern => _patterns.Count > 0 ? _patterns[^1] : null;

    public DrawState State => _state;

    public HexPattern? WipPattern => _wipPattern;

    /// <summary>动画时钟（帧），驱动 zappy 抖动流动。</summary>
    public double Tick { get; set; }

    /// <summary>当前锚点格（调试显示用）。</summary>
    public HexCoord AnchorCoord => _state == DrawState.Drawing ? _current : _start;

    // ---- 画布配色（取自源项目 GuiSpellcasting.render）----
    // 正在绘制的图案：0xff_64c8ff → 0xff_fecbe6
    private static readonly XnaColor WipTail = new XnaColor(0x64, 0xC8, 0xFF, 0xFF);
    private static readonly XnaColor WipHead = new XnaColor(0xFE, 0xCB, 0xE6, 0xFF);
    // 已画且命中：青色渐变
    private static readonly XnaColor ValidTail = new XnaColor(0x64, 0xC8, 0xFF, 0xE0);
    private static readonly XnaColor ValidHead = new XnaColor(0xB8, 0xF0, 0xFF, 0xE0);
    // 已画但未命中：暗红
    private static readonly XnaColor InvalidTail = new XnaColor(0xC8, 0x3C, 0x3C, 0xC8);
    private static readonly XnaColor InvalidHead = new XnaColor(0xFF, 0x8C, 0x8C, 0xC8);

    public void Open() => IsOpen = true;

    public void Close(bool clearPatterns = true)
    {
        IsOpen = false;
        _state = DrawState.BetweenPatterns;
        _wipPattern = null;
        if (clearPatterns)
        {
            Reset();
        }
    }

    public void Reset()
    {
        _patterns.Clear();
        _usedSpots.Clear();
        _state = DrawState.BetweenPatterns;
        _wipPattern = null;
    }

    public float HexSize(float viewWidth, float viewHeight) => HexGrid.HexSize(viewWidth, viewHeight, Zoom);

    public Vector2 CoordsOffset(float viewWidth, float viewHeight) => new Vector2(viewWidth * 0.5f, viewHeight * 0.5f);

    public Vector2 CoordToPx(HexCoord c, float viewWidth, float viewHeight)
        => HexGrid.CoordToPx(c, HexSize(viewWidth, viewHeight), CoordsOffset(viewWidth, viewHeight).ToCore()).ToXna();

    public HexCoord PxToCoord(Vector2 px, float viewWidth, float viewHeight)
        => HexGrid.PxToCoord(px.ToCore(), HexSize(viewWidth, viewHeight), CoordsOffset(viewWidth, viewHeight).ToCore());

    /// <summary>吸附距离（平方值）。源项目：hexSize² × 2 × clamp(threshold, 0.5, 1.0)。</summary>
    private float SnapDistanceSq(float viewWidth, float viewHeight)
    {
        float size = HexSize(viewWidth, viewHeight);
        return size * size * 2f * Math.Clamp(SnapThreshold, 0.5f, 1.0f);
    }

    /// <summary>落笔。对应源项目 drawStart。</summary>
    public bool DrawStart(Vector2 mouse, float viewWidth, float viewHeight)
    {
        if (!IsOpen || _state != DrawState.BetweenPatterns)
        {
            return false;
        }

        var coord = PxToCoord(mouse, viewWidth, viewHeight);

        // 源项目：已用格点不能起笔
        if (_usedSpots.Contains(coord))
        {
            return false;
        }

        _start = coord;
        _current = coord;
        _state = DrawState.JustStarted;
        _wipPattern = null;
        return true;
    }

    /// <summary>
    /// 拖拽/移动。对应源项目 drawMove。
    /// 返回 true 表示本帧图案发生了变化。
    /// </summary>
    public bool DrawMove(Vector2 mouse, float viewWidth, float viewHeight)
    {
        if (!IsOpen || _state == DrawState.BetweenPatterns)
        {
            return false;
        }

        var anchorCoord = _state == DrawState.JustStarted ? _start : _current;
        var anchorPx = CoordToPx(anchorCoord, viewWidth, viewHeight);
        var delta = new Vector2(mouse.X - anchorPx.X, mouse.Y - anchorPx.Y);

        // 关键：源项目比较的是 distanceToSqr 与 snapDist，两者都是平方量。
        // 之前多开了一次方，导致阈值相当于放大到 2 倍，几乎吸不动。
        if (delta.LengthSquared() < SnapDistanceSq(viewWidth, viewHeight))
        {
            return false;
        }

        float angle = MathF.Atan2(delta.Y, delta.X);
        float turns = angle / (MathF.PI * 2f) * 6f;
        int snapped = ((int)MathF.Round(turns) + 1) % 6;
        if (snapped < 0)
        {
            snapped += 6;
        }
        var newDir = (HexDir)snapped;

        var idealNextLoc = anchorCoord + newDir;

        // 目标格已被占用 → 本帧无操作
        if (_usedSpots.Contains(idealNextLoc))
        {
            return false;
        }

        if (_state == DrawState.JustStarted)
        {
            _wipPattern = new HexPattern(newDir);
            _state = DrawState.Drawing;
            _current = idealNextLoc;
            return true;
        }

        // Drawing：先处理「反方向拖拽 = 回溯」
        var wip = _wipPattern!;
        var lastDir = wip.FinalDir();

        if (newDir == lastDir.RotatedBy(HexAngle.Back))
        {
            // 已经是单笔图案 → 退回起点
            if (wip.Length == 0)
            {
                _state = DrawState.JustStarted;
                _current = _current + newDir;
                _start = _current;
            }
            else
            {
                _current = _current + newDir;
                wip.RemoveLastAngle();
            }
            return true;
        }

        if (wip.TryAppendDir(newDir))
        {
            _current = idealNextLoc;
            return true;
        }

        return false;
    }

    /// <summary>结束当前图案。对应源项目 drawEnd。</summary>
    public ResolvedPattern? DrawEnd()
    {
        if (!IsOpen)
        {
            return null;
        }

        if (_state == DrawState.JustStarted)
        {
            // 这一轮什么都没画出来
            _state = DrawState.BetweenPatterns;
            _wipPattern = null;
            return null;
        }

        if (_state != DrawState.Drawing || _wipPattern == null)
        {
            _state = DrawState.BetweenPatterns;
            return null;
        }

        var wip = _wipPattern;
        var matched = PatternRegistry.Match(wip);

        var resolved = new ResolvedPattern
        {
            Pattern = wip,
            Origin = _start,
            Matched = matched,
        };

        foreach (var p in wip.Positions())
        {
            _usedSpots.Add(_start + p);
        }

        _patterns.Add(resolved);
        _state = DrawState.BetweenPatterns;
        _wipPattern = null;
        return resolved;
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, float viewWidth, float viewHeight, Vector2 mouse)
    {
        if (!IsOpen)
        {
            return;
        }

        sb.Draw(pixel, new Rectangle(0, 0, (int)viewWidth, (int)viewHeight), new XnaColor(0, 0, 0, 0));
    }

    /// <summary>
    /// 绘制画布内容。透明背景，只画引导点、已画图案与正在绘制的图案。
    /// </summary>
    public void DrawContent(SpriteBatch sb, Texture2D pixel, float viewWidth, float viewHeight, Vector2 mouse)
    {
        if (!IsOpen)
        {
            return;
        }

        float size = HexSize(viewWidth, viewHeight);
        var mouseCoord = PxToCoord(mouse, viewWidth, viewHeight);

        // ---- 鼠标附近的引导点 ----
        foreach (var dotCoord in HexGrid.RangeAround(mouseCoord, GuideDotRadius))
        {
            if (_usedSpots.Contains(dotCoord))
            {
                continue;
            }

            var dotPx = CoordToPx(dotCoord, viewWidth, viewHeight);
            float delta = Vector2.Distance(dotPx, mouse);

            // 源项目：clamp(1 - (delta - hexSize) / (radius * hexSize), 0, 1)
            // 即格点离鼠标越近越亮；减去一个 hexSize 是为了让鼠标正下方留出一块「全亮小岛」
            float scaledDist = Math.Clamp(1f - (delta - size) / (GuideDotRadius * size), 0f, 1f);

            // 源项目 RegLib.drawSpot(radius, r, g, b, a)：
            //     radius = scaledDist * 2
            //     r = lerp(scaledDist, 0.4, 0.5)
            //     g = lerp(scaledDist, 0.8, 1.0)
            //     b = lerp(scaledDist, 0.7, 0.9)
            //     a = scaledDist
            // 是一颗**青白色**的点，而且 alpha 跟着 scaledDist 从中心 1.0 渐隐到边缘 0 ——
            // 所以越远的点越淡，不会「凭空出现/消失」。
            //
            // 之前用的是粉紫 (255,204,255) + alpha 下限 0.45，色相不对且外围反而偏亮，
            // 结果在亮地形上几乎看不出格点在哪，玩家没法瞄。
            float radius = MathHelper.Lerp(1.3f, 2.6f, scaledDist);
            int cr = (int)(MathHelper.Lerp(scaledDist, 0.40f, 0.50f) * 255f);
            int cg = (int)(MathHelper.Lerp(scaledDist, 0.80f, 1.00f) * 255f);
            int cb = (int)(MathHelper.Lerp(scaledDist, 0.70f, 0.90f) * 255f);
            DrawDot(sb, pixel, dotPx, radius,
                new XnaColor(cr, cg, cb, (int)(Math.Clamp(scaledDist, 0f, 1f) * 255f)));

            // 靠近鼠标的点叠一层白色高光，做出发光观感
            if (scaledDist > 0.5f)
            {
                float glow = (scaledDist - 0.5f) / 0.5f;
                DrawDot(sb, pixel, dotPx, radius * 0.5f,
                    new XnaColor(255, 255, 255, (int)(200 * glow)));
            }
        }

        // ---- 已完成的图案（用与原作一致的 zappy 电光渲染）----
        for (int idx = 0; idx < _patterns.Count; idx++)
        {
            var rp = _patterns[idx];
            var pts = HexGrid.PatternLinePoints(rp.Pattern, rp.Origin, size,
                CoordsOffset(viewWidth, viewHeight).ToCore()).ToXna();

            var tail = rp.IsValid ? ValidTail : InvalidTail;
            var head = rp.IsValid ? ValidHead : InvalidHead;

            // seed 用图案序号，保证不同图案的抖动互不相同且稳定
            PatternRenderer.DrawPattern(
                (a, b, w, c) => DrawSegment(sb, pixel, a, b, w, c),
                (p, r, c) => DrawDot(sb, pixel, p, r, c),
                pts, tail, head, Tick, idx);
        }

        // ---- 正在绘制的图案 ----
        // 原作在这一步会把**鼠标当前位置**作为最后一个点追加进去（GuiSpellcasting.kt:446
        // `points.add(mousePos)`），所以画的时候线条是跟着鼠标走的，而不是只画到已吸附的格点。
        if (_state == DrawState.Drawing && _wipPattern != null)
        {
            var pts = HexGrid.PatternLinePoints(_wipPattern, _start, size,
                CoordsOffset(viewWidth, viewHeight).ToCore()).ToXna();
            pts.Add(mouse);   // ← 跟随鼠标的关键一行

            var dup = PatternRenderer.FindDupIndices(_wipPattern.Positions());
            PatternRenderer.DrawPattern(
                (a, b, w, c) => DrawSegment(sb, pixel, a, b, w, c),
                (p, r, c) => DrawDot(sb, pixel, p, r, c),
                pts, WipTail, WipHead, Tick, _patterns.Count,
                dup.Count > 0 ? dup : null);

            // 当前锚点高亮
            var anchorPx = CoordToPx(_current, viewWidth, viewHeight);
            DrawDot(sb, pixel, anchorPx, 3.2f, new XnaColor(255, 255, 255, 240));
        }
        else if (_state == DrawState.JustStarted)
        {
            // 刚落笔还没画第一笔：从起点画一条短线到鼠标，给出「将要画到哪」的即时反馈
            var startPx = CoordToPx(_start, viewWidth, viewHeight);
            DrawSegment(sb, pixel, startPx, mouse, 2.5f, WipTail);
            DrawDot(sb, pixel, startPx, 3.2f, new XnaColor(255, 255, 255, 240));
        }
    }

    /// <summary>画一段带粗细的线段（供 PatternRenderer 回调）。</summary>
    private static void DrawSegment(SpriteBatch sb, Texture2D pixel, Vector2 a, Vector2 b, float width, XnaColor color)
    {
        var delta = b - a;
        float len = delta.Length();
        if (len < 0.01f)
        {
            return;
        }
        sb.Draw(pixel, a, null, color, MathF.Atan2(delta.Y, delta.X),
            new Vector2(0f, pixel.Height * 0.5f), new Vector2(len, width), SpriteEffects.None, 0f);
    }

    /// <summary>画一个空心圆环，用作吸附目标指示。</summary>
    private static void DrawRingOutline(SpriteBatch sb, Texture2D pixel, Vector2 center, float radius, XnaColor color, float thickness)
    {
        const int segments = 20;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathHelper.TwoPi * i / segments;
            float a1 = MathHelper.TwoPi * (i + 1) / segments;
            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            var delta = p1 - p0;
            float len = delta.Length();
            if (len < 0.01f)
            {
                continue;
            }
            sb.Draw(pixel, p0, null, color, MathF.Atan2(delta.Y, delta.X),
                new Vector2(0f, pixel.Height * 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// 画一个六边形点。
    ///
    /// 源项目的 drawSpot 用 6 段近似圆（注释原话："yes they are gonna be little hexagons fite me"），
    /// 不是真圆也不是方块。FNA 的 SpriteBatch 没有图元绘制接口，
    /// 这里用 6 条以中心为轴、互相错开 60° 的窄条拼出实心六边形。
    /// </summary>
    private static void DrawDot(SpriteBatch sb, Texture2D pixel, Vector2 center, float radius, XnaColor color)
    {
        if (radius <= 0.05f)
        {
            return;
        }

        const int sides = 6;
        // 每条窄条长度取内切直径，绕中心旋转后刚好填满六边形
        float barLength = radius * 1.732f; // √3
        float barWidth = radius * 0.58f;   // 拼合后外接半径≈radius

        for (int i = 0; i < sides; i++)
        {
            float rot = MathHelper.Pi * i / sides;
            sb.Draw(
                pixel,
                center,
                null,
                color,
                rot,
                new Vector2(pixel.Width * 0.5f, pixel.Height * 0.5f),
                new Vector2(barLength, barWidth),
                SpriteEffects.None,
                0f);
        }
    }

    /// <summary>画折线（用旋转的 1px 贴图逐段绘制）。</summary>
    private static void DrawPolyline(SpriteBatch sb, Texture2D pixel, IReadOnlyList<Vector2> pts, XnaColor color)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var delta = new Vector2(b.X - a.X, b.Y - a.Y);
            float length = delta.Length();
            if (length < 0.01f)
            {
                continue;
            }
            sb.Draw(pixel, a, null, color, MathF.Atan2(delta.Y, delta.X),
                new Vector2(0f, pixel.Height * 0.5f), new Vector2(length, 3f), SpriteEffects.None, 0f);
        }
    }
}
