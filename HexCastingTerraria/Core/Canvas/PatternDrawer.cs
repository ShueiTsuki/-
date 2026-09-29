using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Canvas;

/// <summary>画布上已经画完的一条图案。<see cref="Type"/> 在求值结果回来之前是 Unresolved（灰色）。</summary>
public sealed class DrawnPattern
{
    public DrawnPattern(HexPattern pattern, HexCoord origin)
    {
        Pattern = pattern;
        Origin = origin;
    }

    public HexPattern Pattern { get; }
    public HexCoord Origin { get; }
    public ResolvedPatternType Type { get; set; } = ResolvedPatternType.Unresolved;
}

public enum DrawPhase
{
    /// <summary>等待落笔。</summary>
    BetweenPatterns,
    /// <summary>已落笔，还没画出第一段。</summary>
    JustStarted,
    /// <summary>正在画。</summary>
    Drawing,
}

/// <summary>一次 <see cref="PatternDrawer.Move"/> 的结果；原版在 Started/Added/Backtracked 时播放 ADD_TO_PATTERN。</summary>
public enum MoveResult
{
    None,
    /// <summary>JustStarted → Drawing：画出了第一段。</summary>
    Started,
    Added,
    Backtracked,
}

/// <summary>
/// 画布的绘制状态机。逐行移植 `GuiSpellcasting.kt` 的 drawStart / drawMove / drawEnd。
///
/// 放在 Core（不依赖 XNA）是为了让离线测试直接驱动**这份真代码**。
/// 以前 drawtest 里测的是「照抄 HexCanvas.DrawMove 的状态机」的一份副本 ——
/// 真代码改坏了，测试照样全绿。
///
/// 与原版唯一的**有意差异**是 <see cref="MoveAlong"/>：
/// MC 每个操作系统鼠标事件都会调用一次 drawMove（一帧内可能好几次），
/// 所以鼠标走过的**路径**都被看到；泰拉每帧只给一个鼠标位置。
/// 快速画一个拐角时，一帧内鼠标已经越过拐点，直接拿「锚点→当前鼠标」的方向去吸附，
/// 就会斜着切过拐角 —— 这正是「手感差」的主要来源。
/// 所以这里把上一帧到这一帧的鼠标位移按 ≤ ¼ 格距切成小步，逐步喂给原版的 drawMove，
/// 相当于把丢掉的鼠标事件补回来。
/// </summary>
public sealed class PatternDrawer
{
    private readonly List<DrawnPattern> _patterns = new();
    private readonly HashSet<HexCoord> _usedSpots = new();

    private HexCoord _start;
    private HexCoord _current;
    private HexPattern? _wip;

    /// <summary>
    /// 吸附阈值，原版 `gridSnapThreshold`（原版默认 0.5、范围 [0.5, 1]）。
    /// 提交一笔所需的拖拽距离 = size·√(2·阈值)，而相邻格点相距 √3·size，所以：
    /// 0.5 → 格距的 58%；1.0 → 82%；1.15 → 88%。
    /// 玩家反馈原版 0.5 太容易「碰到点」画错，所以默认 1.0、上限放宽到 1.15。
    /// 上限不能再高：drawtest 实测 1.2 起带手抖的画法开始出错（锚点落后太多，拐角被切），
    /// 1.5 时连「正好停在终点上松手」都会因浮点误差少一笔。
    /// </summary>
    public float SnapThreshold { get; set; } = 1.0f;

    public const float MinSnapThreshold = 0.5f;
    public const float MaxSnapThreshold = 1.15f;

    public DrawPhase Phase { get; private set; } = DrawPhase.BetweenPatterns;
    public IReadOnlyList<DrawnPattern> Patterns => _patterns;
    public IReadOnlyCollection<HexCoord> UsedSpots => _usedSpots;
    public HexPattern? Wip => _wip;
    public HexCoord Start => _start;
    public HexCoord Current => _current;

    public bool IsUsed(HexCoord c) => _usedSpots.Contains(c);

    /// <summary>清空全部已画图案（对应原版关闭施法界面后清栈重开）。</summary>
    public void Reset()
    {
        _patterns.Clear();
        _usedSpots.Clear();
        Cancel();
    }

    /// <summary>放弃正在画的这一笔（对应原版 onClose 在绘制中的分支）。</summary>
    public void Cancel()
    {
        Phase = DrawPhase.BetweenPatterns;
        _wip = null;
    }

    /// <summary>drawStart：落笔。已用格点不能起笔。返回是否真的落笔（原版只在此时播 START_PATTERN）。</summary>
    public bool Begin(Vec2f mouse, float hexSize, Vec2f offset)
    {
        if (Phase != DrawPhase.BetweenPatterns) return false;
        var coord = HexGrid.PxToCoord(mouse, hexSize, offset);
        if (_usedSpots.Contains(coord)) return false;

        _start = coord;
        _current = coord;
        _wip = null;
        Phase = DrawPhase.JustStarted;
        return true;
    }

    /// <summary>drawMove：一次鼠标事件。</summary>
    public MoveResult Move(Vec2f mouse, float hexSize, Vec2f offset)
    {
        if (Phase == DrawPhase.BetweenPatterns) return MoveResult.None;

        var anchorCoord = Phase == DrawPhase.JustStarted ? _start : _current;
        var anchor = HexGrid.CoordToPx(anchorCoord, hexSize, offset);
        float snapDist = hexSize * hexSize * 2f * System.Math.Clamp(SnapThreshold, MinSnapThreshold, MaxSnapThreshold);
        if (anchor.DistanceSquaredTo(mouse) < snapDist) return MoveResult.None;

        var delta = mouse - anchor;
        // 原版：angle / 2π 取 mod 6 再 ×6 四舍五入 +1。atan2 的 -π..π 经 mod 后等价于这里的 ((round + 1) mod 6)
        float turns = System.MathF.Atan2(delta.Y, delta.X) / (System.MathF.PI * 2f) * 6f;
        int snapped = ((int)System.MathF.Round(turns) + 1) % 6;
        if (snapped < 0) snapped += 6;
        var newDir = (HexDir)snapped;

        // 玩家可能瞄不准，所以新锚点取「理想位置」
        var idealNextLoc = anchorCoord + newDir;
        if (_usedSpots.Contains(idealNextLoc)) return MoveResult.None;

        if (Phase == DrawPhase.JustStarted)
        {
            _wip = new HexPattern(newDir);
            _current = idealNextLoc;
            Phase = DrawPhase.Drawing;
            return MoveResult.Started;
        }

        var wip = _wip!;
        if (newDir == wip.FinalDir().RotatedBy(HexAngle.Back))
        {
            // 正好反向：回退一笔
            if (wip.Length == 0)
            {
                _start = _current + newDir;
                _current = _start;
                _wip = null;
                Phase = DrawPhase.JustStarted;
            }
            else
            {
                _current = _current + newDir;
                wip.RemoveLastAngle();
            }
            return MoveResult.Backtracked;
        }

        if (wip.TryAppendDir(newDir))
        {
            _current = idealNextLoc;
            return MoveResult.Added;
        }
        return MoveResult.None;
    }

    /// <summary>
    /// 把一帧内的鼠标位移 <paramref name="from"/> → <paramref name="to"/> 拆成小步逐个 <see cref="Move"/>。
    /// 返回本帧产生的事件（Started/Added/Backtracked），调用方据此播放音效。
    /// </summary>
    public List<MoveResult> MoveAlong(Vec2f from, Vec2f to, float hexSize, Vec2f offset)
    {
        var events = new List<MoveResult>();
        if (Phase == DrawPhase.BetweenPatterns) return events;

        float step = System.Math.Max(1f, hexSize * 0.25f);
        int n = System.Math.Max(1, (int)System.MathF.Ceiling((to - from).Length / step));
        for (int i = 1; i <= n; i++)
        {
            var p = from + (to - from) * (i / (float)n);
            // 同一点上可能连续吸附多步（鼠标停在两格外）；原版每个事件只走一步，
            // 但事件足够密，所以这里在每个采样点上走到不能再走为止。上限防御死循环。
            for (int guard = 0; guard < 64; guard++)
            {
                var r = Move(p, hexSize, offset);
                if (r == MoveResult.None) break;
                events.Add(r);
            }
        }
        return events;
    }

    /// <summary>drawEnd：收笔。JustStarted 时什么都不产生。</summary>
    public DrawnPattern? End()
    {
        if (Phase != DrawPhase.Drawing || _wip is null)
        {
            Cancel();
            return null;
        }

        var drawn = new DrawnPattern(_wip, _start);
        _patterns.Add(drawn);
        foreach (var p in _wip.Positions(_start)) _usedSpots.Add(p);
        Cancel();
        return drawn;
    }

    /// <summary>
    /// 求值结果回来了（单机即时，联机等服务端回包）。对应 recvServerUpdate：
    /// 把最后一条图案设成该结果；若是 Undone，则把它之前最近一条「可撤销」的图案标成 Undone，
    /// 最后一条（撤销图案本身）用 Evaluated 的颜色。
    /// </summary>
    public void ApplyResolution(ResolvedPatternType type, System.Func<HexPattern, bool>? isParenOpener = null)
    {
        if (_patterns.Count == 0) return;
        var last = _patterns[^1];
        if (type != ResolvedPatternType.Undone)
        {
            last.Type = type;
            return;
        }

        for (int i = _patterns.Count - 2; i >= 0; i--)
        {
            var p = _patterns[i];
            bool undoable = p.Type == ResolvedPatternType.Escaped
                || (p.Type == ResolvedPatternType.Evaluated && isParenOpener != null && isParenOpener(p.Pattern));
            if (undoable)
            {
                p.Type = ResolvedPatternType.Undone;
                break;
            }
        }
        last.Type = ResolvedPatternType.Evaluated;
    }
}
