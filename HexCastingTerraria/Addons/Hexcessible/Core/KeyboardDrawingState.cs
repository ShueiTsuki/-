using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 键盘绘制的状态（上游 drawstate/KeyboardDrawing.java 去掉渲染的部分）：当前签名、光标处的起点与起笔方向、
/// 实际能放下的位置、排队中的下一条。画布相关的判断（格点是否被占 / 在不在屏幕里）由调用方传进来，所以离线可测。
/// </summary>
public sealed class KeyboardDrawingState
{
    private readonly List<HexAngle> _sig;

    /// <summary>上游 KeyboardDrawing(castref, sig)：从画布原点 (0,0)、朝东开始，鼠标一动就跟到鼠标。</summary>
    public KeyboardDrawingState(IEnumerable<HexAngle> sig, Func<HexCoord, bool> isUsed)
    {
        _sig = new List<HexAngle>(sig);
        Origin = new HexCoord(0, 0);
        Recalculate(isUsed);
    }

    /// <summary>上游 KeyboardDrawing(castref, start, sigs, dir)：一次排好几条（自动补全选中一组图案时）。</summary>
    public KeyboardDrawingState(HexCoord start, IReadOnlyList<IReadOnlyList<HexAngle>> sigs, HexDir dir, Func<HexCoord, bool> isUsed)
    {
        if (sigs.Count == 0) throw new ArgumentException("至少要有一条签名", nameof(sigs));
        _sig = new List<HexAngle>(sigs[0]);
        Origin = start;
        if (sigs.Count > 1)
        {
            var rest = new List<IReadOnlyList<HexAngle>>();
            for (int i = 1; i < sigs.Count; i++) rest.Add(sigs[i]);
            Next = new KeyboardDrawingState(start, rest, dir, isUsed);
        }
        OriginDir = dir;
        Recalculate(isUsed);
    }

    public IReadOnlyList<HexAngle> Sig => _sig;

    /// <summary>光标处（想放的位置）。</summary>
    public HexCoord Origin { get; private set; }

    public HexDir OriginDir { get; private set; } = HexDir.East;

    /// <summary>实际能放下的起点与起笔方向；null = 附近放不下。</summary>
    public HexCoord? Start { get; private set; }

    public HexDir? StartDir { get; private set; }

    /// <summary>最后一个格点与最后一笔的方向（下一笔从这里接）。</summary>
    public HexCoord? End { get; private set; }

    public HexDir? EndDir { get; private set; }

    /// <summary>这一条画完后接着画的（上游 nextDrawing）。</summary>
    public KeyboardDrawingState? Next { get; }

    /// <summary>上游 queuedCount：后面还排着几条。</summary>
    public int QueuedCount => Next is null ? 0 : 1 + Next.QueuedCount;

    /// <summary>上游 recalculateNewAll：从光标处按 BFS 找最近的空位。</summary>
    public void Recalculate(Func<HexCoord, bool> isUsed)
    {
        if (_sig.Count == 0)
        {
            Start = Origin;
            StartDir = OriginDir;
            End = Origin;
            EndDir = OriginDir;
            return;
        }

        var found = KeyboardPlacement.FindClosestAvailable(Origin, KeyboardPlacement.Pattern(OriginDir, _sig), isUsed);
        if (found is null)
        {
            Start = null;
            StartDir = null;
            End = null;
            EndDir = null;
            return;
        }
        Start = found.Value.Coord;
        StartDir = found.Value.StartDir;

        var pat = KeyboardPlacement.Pattern(found.Value.StartDir, _sig);
        End = KeyboardPlacement.FinalPos(found.Value.Coord, pat);
        EndDir = pat.FinalDir();
    }

    /// <summary>上游 canGo：再转这个角度，笔画不会和自己重叠。</summary>
    public bool CanGo(HexAngle angle)
        => StartDir is { } dir && KeyboardPlacement.CanAppend(dir, _sig, angle);

    /// <summary>上游 onCharType：s 撤销一笔，q w e a d（大小写都行）画一笔。返回签名有没有变。</summary>
    public bool Type(char c, Func<HexCoord, bool> isUsed)
    {
        if (char.ToLowerInvariant(c) == 's') return Undo(isUsed);
        if (!KeyboardPlacement.IsDrawLetter(c)) return false;
        var angle = KeyboardPlacement.AngleOf(c)!.Value;
        if (!CanGo(angle)) return false;
        _sig.Add(angle);
        Recalculate(isUsed);
        return true;
    }

    /// <summary>上游 removeCharFromSig（退格 / s）。</summary>
    public bool Undo(Func<HexCoord, bool> isUsed)
    {
        if (_sig.Count == 0) return false;
        _sig.RemoveAt(_sig.Count - 1);
        Recalculate(isUsed);
        return true;
    }

    /// <summary>上游 moveOrigin：h j k l / 方向键挪一格；挪出屏幕就不挪。</summary>
    public void MoveOrigin(int dq, int dr, Func<HexCoord, bool> isVisible, Func<HexCoord, bool> isUsed)
    {
        var next = Origin + new HexCoord(dq, dr);
        if (isVisible(next)) Origin = next;
        Recalculate(isUsed);
    }

    /// <summary>上游 onMouseMove：光标处就是新的原点。</summary>
    public void SetOrigin(HexCoord origin, Func<HexCoord, bool> isUsed)
    {
        Origin = origin;
        Recalculate(isUsed);
    }

    /// <summary>上游 rotate：r 顺时针、Shift+r 逆时针、滚轮反着转。</summary>
    public void Rotate(int delta, Func<HexCoord, bool> isUsed)
    {
        OriginDir = (HexDir)((((int)OriginDir + delta) % 6 + 6) % 6);
        Recalculate(isUsed);
    }

    /// <summary>上游 submit：放得下就给出要放的图案与起点，放不下是 null。</summary>
    public (HexPattern Pattern, HexCoord Start)? Submit(Func<HexCoord, bool> isUsed)
    {
        Recalculate(isUsed);
        if (Start is not { } start || StartDir is not { } dir) return null;
        return (KeyboardPlacement.Pattern(dir, _sig), start);
    }

    /// <summary>上游 renderNextPointTooltips 的取点：下一笔能去的格点与对应字母。</summary>
    public IEnumerable<(char Letter, HexCoord Pos)> NextPoints(Func<HexCoord, bool> isUsed)
    {
        if (End is not { } end || EndDir is not { } endDir) yield break;
        foreach (HexAngle angle in Enum.GetValues(typeof(HexAngle)))
        {
            var pos = end + endDir.RotatedBy(angle);
            if (isUsed(pos) || !CanGo(angle)) continue;
            yield return (KeyboardPlacement.LetterOf(angle), pos);
        }
    }
}
