using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 键盘绘制的纯逻辑（上游 accessor/CastRef.java 的 findClosestAvailable / fits / isValidPatternAddition + Utils.java 的角度字母表）。
/// 画布相关的部分（格点是否被占、是否在屏幕里）由调用方传进来，所以离线可测。
/// </summary>
public static class KeyboardPlacement
{
    /// <summary>上游 Utils.angle(char)：q 左、w 直、e 右、a 左后、s 回头（只用来撤销）、d 右后；大小写都认。</summary>
    public static HexAngle? AngleOf(char c) => char.ToLowerInvariant(c) switch
    {
        'q' => HexAngle.Left,
        'w' => HexAngle.Forward,
        'e' => HexAngle.Right,
        'a' => HexAngle.LeftBack,
        's' => HexAngle.Back,
        'd' => HexAngle.RightBack,
        _ => null,
    };

    /// <summary>上游 Utils.angle(HexAngle)。</summary>
    public static char LetterOf(HexAngle a) => a switch
    {
        HexAngle.Left => 'q',
        HexAngle.Forward => 'w',
        HexAngle.Right => 'e',
        HexAngle.LeftBack => 'a',
        HexAngle.Back => 's',
        _ => 'd',
    };

    /// <summary>上游 KeyboardDrawing.validSig：能画的五个字母（s 是撤销）。</summary>
    public static bool IsDrawLetter(char c) => "qweadQWEAD".IndexOf(c) >= 0;

    public static HexPattern Pattern(HexDir startDir, IEnumerable<HexAngle> angles)
    {
        HexPattern.TryFromAnglesUnchecked(new string(angles.Select(LetterOf).ToArray()), startDir, out var p, out _);
        return p!;
    }

    /// <summary>上游 isValidPatternAddition：在末尾再转这个角度，笔画会不会和自己重叠（不看画布上别的图案）。</summary>
    public static bool CanAppend(HexDir startDir, IReadOnlyList<HexAngle> sig, HexAngle next)
    {
        var p = Pattern(startDir, sig);
        return p.TryAppendDir(p.FinalDir().RotatedBy(next));
    }

    /// <summary>上游 Utils.finalPos：图案最后一个格点。</summary>
    public static HexCoord FinalPos(HexCoord start, HexPattern pattern) => pattern.Positions(start)[^1];

    /// <summary>
    /// 上游 findClosestAvailable：从 start 开始按广度优先一圈圈往外找，每个格点先按起笔方向、再按其余五个方向轮着试，
    /// 找到第一个整条图案都落在空格点上的位置。找了 512 个格点还没有就放弃（null）。
    /// </summary>
    public static (HexCoord Coord, HexDir StartDir)? FindClosestAvailable(HexCoord start, HexPattern pattern, System.Func<HexCoord, bool> isUsed)
    {
        var queue = new Queue<HexCoord>();
        var visited = new HashSet<HexCoord> { start };
        queue.Enqueue(start);
        var dirs = DirsFrom(pattern.StartDir);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dir in dirs)
            {
                if (Fits(current, pattern, dir, isUsed)) return (current, dir);
            }
            foreach (var dir in dirs)
            {
                var next = current + dir;
                if (visited.Add(next)) queue.Enqueue(next);
            }
            if (visited.Count > 512) return null;
        }
        return null;
    }

    /// <summary>上游 fits：从 origin 按 startDir 起笔，每个格点都没被占。</summary>
    public static bool Fits(HexCoord origin, HexPattern pattern, HexDir startDir, System.Func<HexCoord, bool> isUsed)
    {
        if (isUsed(origin)) return false;
        var current = origin;
        var dir = startDir;
        current += dir;
        if (isUsed(current)) return false;
        foreach (var angle in pattern.Angles)
        {
            dir = dir.RotatedBy(angle);
            current += dir;
            if (isUsed(current)) return false;
        }
        return true;
    }

    /// <summary>上游 Utils.hexDirs(start)：从 start 起顺时针的六个方向。</summary>
    public static HexDir[] DirsFrom(HexDir start)
        => Enumerable.Range(0, 6).Select(i => (HexDir)(((int)start + i) % 6)).ToArray();
}
