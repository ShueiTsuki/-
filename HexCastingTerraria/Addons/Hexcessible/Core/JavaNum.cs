using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 上游 Java 的几个数字细节（智能签名的结果取决于它们）：Float.parseFloat、Float.toString、Math.round(float)、(int) 强转。
/// </summary>
public static class JavaNum
{
    private static readonly Regex DecimalLiteral = new(@"^[+-]?(\d+\.?\d*([eE][+-]?\d+)?|\.\d+([eE][+-]?\d+)?)[fFdD]?$", RegexOptions.Compiled);

    /// <summary>
    /// Float.parseFloat：去掉首尾空白（Java trim：码位不大于空格的字符），认 NaN / Infinity、十进制与指数、结尾的 f F d D。
    /// 十六进制浮点（0x1p3）不认 —— 查询框里没人会这么打。
    /// </summary>
    public static bool TryParseFloat(string s, out float value)
    {
        value = 0f;
        int a = 0, b = s.Length;
        while (a < b && s[a] <= ' ') a++;
        while (b > a && s[b - 1] <= ' ') b--;
        s = s[a..b];
        if (s.Length == 0) return false;

        string body = s.TrimStart('+', '-');
        bool neg = s.StartsWith('-');
        if (s.Length - body.Length > 1) return false;
        if (body == "NaN") { value = float.NaN; return true; }
        if (body == "Infinity") { value = neg ? float.NegativeInfinity : float.PositiveInfinity; return true; }
        if (!DecimalLiteral.IsMatch(s)) return false;
        if ("fFdD".IndexOf(s[^1]) >= 0) s = s[..^1];
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Math.round(float)：floor(x + 0.5)，NaN 得 0，超出 int 范围就取边界。</summary>
    public static int Round(float x)
    {
        if (float.IsNaN(x)) return 0;
        return SaturateToInt(Math.Floor((double)x + 0.5));
    }

    /// <summary>(int) 强转：向零取整，NaN 得 0，超出范围取边界。</summary>
    public static int ToInt(double x)
    {
        if (double.IsNaN(x)) return 0;
        return SaturateToInt(Math.Truncate(x));
    }

    private static int SaturateToInt(double x)
        => x >= int.MaxValue ? int.MaxValue : x <= int.MinValue ? int.MinValue : (int)x;

    /// <summary>
    /// Float.toString：绝对值在 [0.001, 10^7) 用小数写法（至少一位小数，如 5.0），否则用 1.0E7 这样的写法；
    /// 数字取能还原出同一个 float 的最短写法。
    /// </summary>
    public static string FloatToString(float f)
    {
        if (float.IsNaN(f)) return "NaN";
        if (float.IsPositiveInfinity(f)) return "Infinity";
        if (float.IsNegativeInfinity(f)) return "-Infinity";
        if (f == 0f) return BitConverter.SingleToInt32Bits(f) < 0 ? "-0.0" : "0.0";

        string r = f.ToString("R", CultureInfo.InvariantCulture);
        bool neg = r.StartsWith('-');
        if (neg) r = r[1..];
        int exp = 0;
        int e = r.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            exp = int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture);
            r = r[..e];
        }
        int dot = r.IndexOf('.');
        string digits = dot >= 0 ? r.Remove(dot, 1) : r;
        int point = (dot >= 0 ? dot : r.Length) + exp;   // 数值 = 0.digits × 10^point
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..];
        point -= lead;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";

        var sb = new StringBuilder();
        if (neg) sb.Append('-');
        float abs = Math.Abs(f);
        if (abs >= 1e-3f && abs < 1e7f)
        {
            if (point <= 0)
            {
                sb.Append("0.").Append('0', -point).Append(digits);
            }
            else
            {
                sb.Append(digits.Length > point ? digits[..point] : digits.PadRight(point, '0'));
                sb.Append('.');
                sb.Append(digits.Length > point ? digits[point..] : "0");
            }
        }
        else
        {
            sb.Append(digits[0]).Append('.').Append(digits.Length > 1 ? digits[1..] : "0");
            sb.Append('E').Append((point - 1).ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>DecimalFormat("#.##")：最多两位小数、去掉末尾的 0（只用在条目 id 里）。</summary>
    public static string TwoDecimals(float f)
    {
        if (float.IsNaN(f)) return "NaN";
        if (float.IsInfinity(f)) return f > 0 ? "\u221E" : "-\u221E";
        return Math.Round((double)f, 2, MidpointRounding.ToEven).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
