using System;
using System.Text;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>注释字符串的转义 / 反转义（上游 misc/StringEscaper.kt，逐行照搬）。</summary>
public static class StringEscaper
{
    public static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");

    public static string Unescape(string s)
    {
        if (s.Length == 0) throw new ArgumentException("illegal escape pattern, empty string");
        var sb = new StringBuilder();
        int partStart = 0;
        int index = 0;
        // Length - 1：保证 index + 1 总是合法
        while (index < s.Length - 1)
        {
            if (s[index] == '\\')
            {
                sb.Append(s, partStart, index - partStart);
                sb.Append(s[index + 1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    _ => throw new ArgumentException("invalid escape sequence"),
                });
                partStart = index + 2;
                index++;   // 跳过被转义的字符
            }
            index++;
        }
        sb.Append(s, partStart, s.Length - partStart);
        if (s[^1] == '\\' && (s.Length < 2 || s[^2] != '\\'))
        {
            throw new ArgumentException("illegal escape pattern, trailing backslash");
        }
        return sb.ToString();
    }
}
