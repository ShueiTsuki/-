using System;
using System.Collections.Generic;
using System.Globalization;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 数字 -> 数字之精思的笔画（上游 misc/NumEvaluatorBrute.java，逐行照搬），以及数字的最短文本写法。
///
/// 算法：先处理小数（有小数部分就 ×2 并记一个 d，最多 100 次），再按位拆整数：
/// 含 1010 记 e（-10）、含 0101 记 q（-5）、含 1 记 w（-1）、否则记 a（÷2）；最后接上前缀 aqaa / dedd，整体倒序。
/// 负数起笔东北，正数起笔东南（由调用方决定）。
/// </summary>
public static class NumEvaluator
{
    /// <summary>上游 DoubleIota.TOLERANCE / 10。</summary>
    private const double Tolerance = 0.0001 / 10;
    private const int MaxDecimalReach = 100;

    public static string AnglesFromNum(double target)
    {
        double tolerance = Tolerance;
        if (double.IsNaN(target)) target = 0;
        bool neg = target < 0;
        target = Math.Min(Math.Abs(target), long.MaxValue);
        var seq = new List<string>();

        // 1. 小数部分
        for (int i = 0; i < MaxDecimalReach; i++)
        {
            if (target % 1 < tolerance) break;
            target *= 2;
            tolerance *= 2;
            seq.Add("d");
        }

        // 2. 按位拆（Java Math.round = floor(x + 0.5)，不是 .NET 默认的银行家舍入）
        long n = (long)Math.Floor(target + 0.5);
        while (n != 0)
        {
            if ((n & 10) == 10) { seq.Add("e"); n ^= 10; }
            else if ((n & 5) == 5) { seq.Add("q"); n ^= 5; }
            else if ((n & 1) == 1) { seq.Add("w"); n ^= 1; }
            else { seq.Add("a"); n = (long)((ulong)n >> 1); }
        }
        seq.Add(neg ? "dedd" : "aqaa");
        seq.Reverse();
        return string.Concat(seq);
    }

    /// <summary>
    /// 上游 INbt2Str.displayMinimal：<c>"%.4f"</c> 再去掉末尾的 0 和小数点。
    /// Java 的 %.4f 是对二进制精确值做「四舍五入（HALF_UP）」—— 用 decimal 按远离零舍入来对齐；超出 decimal 范围的退回 F4。
    /// </summary>
    public static string DisplayMinimal(double raw)
    {
        string mid;
        if (double.IsNaN(raw)) mid = "NaN";
        else if (double.IsInfinity(raw)) mid = raw > 0 ? "Infinity" : "-Infinity";
        else if (Math.Abs(raw) < 7.9e27)
            mid = Math.Round((decimal)raw, 4, MidpointRounding.AwayFromZero).ToString("F4", CultureInfo.InvariantCulture);
        else
            mid = raw.ToString("F4", CultureInfo.InvariantCulture);
        // Java 对负数（含 -0.0 和舍成 0 的负数）照样印负号：-0.0000；decimal 会把负零的符号吃掉
        if ((raw < 0 || (raw == 0 && double.IsNegative(raw))) && !mid.StartsWith("-", StringComparison.Ordinal)) mid = "-" + mid;

        int ptr = mid.Length;
        while (ptr > 0)
        {
            char c = mid[ptr - 1];
            if (c == '0') ptr--;
            else
            {
                if (c == '.') ptr--;
                break;
            }
        }
        return mid.Substring(0, ptr);
    }
}
