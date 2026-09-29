using System.Collections.Generic;
using System.Text;

namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 一条咒术图案：起始方向 + 一串转向角。
///
/// 移植自 at.petrak.hexcasting.api.casting.math.HexPattern (Kotlin)，
/// 语义严格对齐，包括两条绘制禁令：
///   1. 不能重走已经画过的边（含反向）
///   2. 不能立刻上溯（下一个角度不能是 BACK）
/// </summary>
public sealed class HexPattern
{
    public HexDir StartDir { get; private set; }
    private readonly List<HexAngle> _angles = new();

    public HexPattern(HexDir startDir)
    {
        StartDir = startDir;
    }

    public IReadOnlyList<HexAngle> Angles => _angles;

    public int Length => _angles.Count;

    /// <summary>角度签名，例如 "aqaa"。</summary>
    public string AnglesSignature()
    {
        var sb = new StringBuilder(_angles.Count);
        for (int i = 0; i < _angles.Count; i++)
        {
            sb.Append(_angles[i].ToChar());
        }
        return sb.ToString();
    }

    /// <summary>
    /// 沿某个方向延长一笔。成功返回 true；违反绘制禁令返回 false 且不修改自身。
    /// 对应源项目的 tryAppendDir。
    /// </summary>
    public bool TryAppendDir(HexDir newDir)
    {
        // 记录已经画过的有向边：起点->方向
        var linesSeen = new HashSet<(HexCoord, HexDir)>();

        var compass = StartDir;
        var cursor = HexCoord.Origin;
        foreach (var a in _angles)
        {
            linesSeen.Add((cursor, compass));
            // 从 A 到 B 的线，也占用了 B 到 A 的方向
            linesSeen.Add((cursor + compass, compass.RotatedBy(HexAngle.Back)));
            cursor += compass;
            compass = compass.RotatedBy(a);
        }
        cursor += compass;

        if (linesSeen.Contains((cursor, newDir)))
        {
            return false;
        }

        var nextAngle = newDir.AngleFrom(compass);
        if (nextAngle == HexAngle.Back)
        {
            return false;
        }

        _angles.Add(nextAngle);
        return true;
    }

    /// <summary>
    /// 移除最后一个角度。用于绘制时的「回溯」（反方向拖拽）。
    /// 对应源项目直接操作 angles 列表的 removeLast()。
    /// </summary>
    public bool RemoveLastAngle()
    {
        if (_angles.Count == 0)
        {
            return false;
        }
        _angles.RemoveAt(_angles.Count - 1);
        return true;
    }

    /// <summary>图案经过的全部格点，含起点与终点。</summary>
    public List<HexCoord> Positions(HexCoord? start = null)
    {
        var origin = start ?? HexCoord.Origin;
        var outList = new List<HexCoord>(_angles.Count + 2) { origin };

        var compass = StartDir;
        var cursor = origin;
        foreach (var a in _angles)
        {
            cursor += compass;
            outList.Add(cursor);
            compass = compass.RotatedBy(a);
        }
        outList.Add(cursor + compass);
        return outList;
    }

    /// <summary>图案覆盖的方向序列，长度比角度数多 1。</summary>
    public List<HexDir> Directions()
    {
        var outList = new List<HexDir>(_angles.Count + 1) { StartDir };
        var compass = StartDir;
        foreach (var a in _angles)
        {
            compass = compass.RotatedBy(a);
            outList.Add(compass);
        }
        return outList;
    }

    /// <summary>落笔时的朝向。</summary>
    public HexDir FinalDir()
    {
        var compass = StartDir;
        foreach (var a in _angles)
        {
            compass = compass.RotatedBy(a);
        }
        return compass;
    }

    /// <summary>只比较角度序列，不比起始方向（对应源项目 sigsEqual）。</summary>
    public bool SigsEqual(HexPattern that)
    {
        if (_angles.Count != that._angles.Count)
        {
            return false;
        }
        for (int i = 0; i < _angles.Count; i++)
        {
            if (_angles[i] != that._angles[i])
            {
                return false;
            }
        }
        return true;
    }

    public override string ToString() => $"HexPattern[{StartDir}, {AnglesSignature()}]";

    /// <summary>
    /// 由角度签名构造图案，带完整的重叠校验。
    /// 对应源项目的 fromAngles；失败时返回 false 并给出错误位置。
    /// </summary>
    public static bool TryFromAngles(string signature, HexDir startDir, out HexPattern? pattern, out ParseError? error)
    {
        pattern = null;
        error = null;

        var result = new HexPattern(startDir);
        var cursor = HexCoord.Origin;
        var compass = startDir;

        var linesSeen = new HashSet<(HexCoord, HexDir)>
        {
            (cursor, compass),
            (cursor + compass, compass.RotatedBy(HexAngle.Back)),
        };

        for (int idx = 0; idx < signature.Length; idx++)
        {
            char c = signature[idx];
            var angle = HexAngleExtensions.FromChar(c);
            if (angle == null)
            {
                error = new ParseError(idx, c, "不是合法的角度字符（只允许 w/e/d/s/a/q）");
                return false;
            }

            cursor += compass;
            compass = compass.RotatedBy(angle.Value);

            if (!linesSeen.Add((cursor, compass)) ||
                !linesSeen.Add((cursor + compass, compass.RotatedBy(HexAngle.Back))))
            {
                error = new ParseError(idx, c, "该笔会回折到自身已有笔画上，图案非法");
                return false;
            }

            result._angles.Add(angle.Value);
        }

        pattern = result;
        return true;
    }

    /// <summary>
    /// 不做重叠校验的构造，对应源项目的 fromAnglesUnchecked。
    /// 用于加载已知合法的内置图案数据。
    /// </summary>
    public static bool TryFromAnglesUnchecked(string signature, HexDir startDir, out HexPattern? pattern, out ParseError? error)
    {
        pattern = null;
        error = null;

        var result = new HexPattern(startDir);
        for (int idx = 0; idx < signature.Length; idx++)
        {
            char c = signature[idx];
            var angle = HexAngleExtensions.FromChar(c);
            if (angle == null)
            {
                error = new ParseError(idx, c, "不是合法的角度字符（只允许 w/e/d/s/a/q）");
                return false;
            }
            result._angles.Add(angle.Value);
        }

        pattern = result;
        return true;
    }

    /// <summary>
    /// 图案匹配键。
    ///
    /// 重要：**只用角度签名，不含起始方向**。
    /// 依据源项目 PatternRegistryManifest.java:92-119 —— 匹配表是以
    /// `pat.getAngles()`（角度序列）为键的哈希表；
    /// 而 ActionRegistryEntry.java:9-14 的注释写明：
    /// "The start dir acts as the canonical start direction for display in the book"
    /// 即起始方向只是书里展示用的规范方向，**不参与匹配**。
    ///
    /// 若把起始方向并进键里，就会出现「形状画对了但起手方向不同 → 判定未识别」，
    /// 比原版更严格，属于实现错误。
    /// </summary>
    public string MatchKey() => AnglesSignature();
}
