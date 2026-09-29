using System.Collections.Generic;

namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// **特殊图案**（special patterns）：不在 188 条注册表里，但能被识别成动作的图案。
/// 移植自源项目 `SpecialHandler` 的两个实现（`SpecialHandlerNumberLiteral` / `SpecialHandlerMask`）。
///
/// ## 为什么这一类东西必须单独实现
///
/// 注册表是「一个签名 → 一个动作」的静态表，而这两个是**参数化的**：
/// 同一个前缀可以生成无穷多条图案，每条的结果都不一样（数字 1、2、3…；各种掩码）。
/// 所以它们走的是另一条路：**先匹配前缀，再从图案本身算出参数**。
///
/// > ⚠️ 漏掉这一类的后果很隐蔽：注册表 188 条全实现了、用例全通过，
/// > 但玩家**画不出任何数字** —— 于是闪现距离、药水时长、飞行秒数这些
/// > 「要一个数当参数」的法术全部无法使用，而且不会报错（只是「图案无效」）。
/// > 这个漏是在对照原版图案工具时才发现的。
/// </summary>
public static class SpecialPatterns
{
    // ── 数字字面量 ─────────────────────────────────────────────────

    /// <summary>正数前缀。源项目 `sig.startsWith("aqaa")`。</summary>
    public const string NumberPrefixPositive = "aqaa";

    /// <summary>负数前缀。源项目 `sig.startsWith("dedd")`。</summary>
    public const string NumberPrefixNegative = "dedd";

    /// <summary>
    /// 把一条图案解释成数字。不是数字图案就返回 false。
    ///
    /// 编码规则（逐字照抄源项目 `Factory.tryMatch`）：
    ///   `w` → +1　`q` → +5　`e` → +10　`a` → ×2　`d` → ÷2　`s` → 空操作（作者注：「行吧，有趣的家伙」）
    ///
    /// 注意是**按顺序依次施加**，不是把各位加起来：
    ///   `aqaaeaw` = ((10 × 2) + 1) = 21，不是 10+1=11。
    /// </summary>
    public static bool TryNumber(string anglesSignature, out double value)
    {
        value = 0;

        bool negative;
        string body;

        if (anglesSignature.StartsWith(NumberPrefixPositive, System.StringComparison.Ordinal))
        {
            negative = false;
            body = anglesSignature.Substring(NumberPrefixPositive.Length);
        }
        else if (anglesSignature.StartsWith(NumberPrefixNegative, System.StringComparison.Ordinal))
        {
            negative = true;
            body = anglesSignature.Substring(NumberPrefixNegative.Length);
        }
        else
        {
            return false;
        }

        double accumulator = 0;

        foreach (char ch in body)
        {
            switch (ch)
            {
                case 'w': accumulator += 1; break;
                case 'q': accumulator += 5; break;
                case 'e': accumulator += 10; break;
                case 'a': accumulator *= 2; break;
                case 'd': accumulator /= 2; break;
                case 's': break;                       // 空操作（源项目里就是故意留着的一档）
                default: return false;                 // 非法字符 -> 不是数字图案
            }
        }

        value = negative ? -accumulator : accumulator;
        return true;
    }

    /// <summary>把数字编码回图案签名（调试/文档用）。只支持能精确表示的整数与半整数。</summary>
    public static string? EncodeNumber(double value)
    {
        bool negative = value < 0;
        double remaining = System.Math.Abs(value);

        var sb = new System.Text.StringBuilder(negative ? NumberPrefixNegative : NumberPrefixPositive);

        // 贪心：能减 10 就减 10，能减 5 就减 5，剩 1，最后用 d 表示 .5
        while (remaining >= 10) { sb.Append('e'); remaining -= 10; }
        while (remaining >= 5) { sb.Append('q'); remaining -= 5; }
        while (remaining >= 1) { sb.Append('w'); remaining -= 1; }

        if (remaining == 0.5) sb.Append('d');

        // 还剩小数（例如 0.25）就用不了这套编码
        return remaining is 0 or 0.5 ? sb.ToString() : null;
    }

    // ── 掩码 ───────────────────────────────────────────────────────

    /// <summary>
    /// 把一条图案解释成**掩码**：从栈上取 N 个值，只保留打勾的那些。
    /// 移植自源项目 `SpecialHandlerMask`。
    ///
    /// 图案的读法（以起点方向为「正前方」基准）：
    ///   - 一段**正前方**的直走 → 掩码追加一个「保留」
    ///   - 一段「右转再接左转」的下凹（`v` 形）→ 掩码追加一个「丢弃」，并跳过这两段
    ///   - 其它任何形状 → 不是掩码
    ///
    /// 起始方向**不参与匹配**（源项目只按角度签名算），与普通图案一致。
    /// </summary>
    public static bool TryMask(HexPattern pattern, out bool[] mask)
    {
        mask = System.Array.Empty<bool>();

        var directions = Directions(pattern);
        if (directions.Count == 0)
        {
            // 一个格点都没有 = 空掩码。源项目里这也会命中（argc=0，什么都不做）。
            mask = System.Array.Empty<bool>();
            return true;
        }

        // 源项目：若第一个角度是 LEFT_BACK，则「正前方」要在此基础上再左转一次。
        // 这条是为了让「起手先回头」的画法也能对齐到同一套掩码语义。
        var flatDir = pattern.StartDir;
        if (pattern.Angles.Count > 0 && pattern.Angles[0] == HexAngle.LeftBack)
        {
            flatDir = directions[0].RotatedBy(HexAngle.Left);
        }

        var result = new List<bool>();
        int i = 0;

        while (i < directions.Count)
        {
            var angle = directions[i].AngleFrom(flatDir);

            if (angle == HexAngle.Forward)
            {
                result.Add(true);
                i++;
                continue;
            }

            // 剩下的不足两段 -> 拼不出一个下凹
            if (i >= directions.Count - 1) return false;

            var angle2 = directions[i + 1].AngleFrom(flatDir);
            if (angle == HexAngle.Right && angle2 == HexAngle.Left)
            {
                result.Add(false);
                i += 2;
                continue;
            }

            return false;
        }

        mask = result.ToArray();
        return true;
    }

    /// <summary>图案依次走过的方向序列（每走一段一个方向）。</summary>
    private static List<HexDir> Directions(HexPattern pattern)
    {
        var list = new List<HexDir>(pattern.Angles.Count);
        var current = pattern.StartDir;

        foreach (var angle in pattern.Angles)
        {
            current = current.RotatedBy(angle);
            list.Add(current);
        }

        return list;
    }
}
