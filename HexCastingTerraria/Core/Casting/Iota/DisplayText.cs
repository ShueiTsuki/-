using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>
/// 上游 Minecraft Component（带颜色、可嵌套的文字）的最小替身。iota 的显示（上游 IotaType.display）都返回它。
///
/// 一个节点要么是一段文字，要么是一个内嵌的小图案（上游装着 Inline 时，图案 iota 就显示成这样的小图），
/// 要么只是一组子节点。颜色可以继承：自己没设颜色就用外层的，和 Component 的 Style 一样 ——
/// 所以列表的「[ , ]」跟着列表的颜色走，里面的数字、图案保持自己的颜色。
///
/// 游戏里怎么画（聊天、物品说明、HUD）见 Client/UI/RichText.cs；转成聊天标记见 <see cref="DisplayTags"/>。
/// </summary>
public sealed class DisplayText
{
    private readonly List<DisplayText> _children = new();

    private DisplayText(string? text, HexPattern? pattern, uint? color)
    {
        Text = text;
        Pattern = pattern;
        Color = color;
    }

    /// <summary>这个节点自己的文字（没有就是 null）。</summary>
    public string? Text { get; }

    /// <summary>这个节点是一个内嵌图案（没有就是 null）。</summary>
    public HexPattern? Pattern { get; }

    /// <summary>0xRRGGBB；null = 用外层的颜色。</summary>
    public uint? Color { get; private set; }

    public IReadOnlyList<DisplayText> Children => _children;

    public static DisplayText Literal(string text, uint? color = null) => new(text, null, color);

    public static DisplayText Glyph(HexPattern pattern, uint? color = null) => new(null, pattern, color);

    /// <summary>上游 Component.empty()。</summary>
    public static DisplayText Empty() => new(null, null, null);

    public DisplayText Append(DisplayText child)
    {
        _children.Add(child);
        return this;
    }

    public DisplayText Append(string text) => Append(Literal(text));

    /// <summary>上游 withStyle(颜色)：换掉自己的颜色（子节点自己有颜色的不受影响）。</summary>
    public DisplayText WithColor(uint color)
    {
        Color = color;
        return this;
    }

    /// <summary>上游 getString：纯文字。图案写成 HexPattern[方向, 签名]（上游 Inline 图案的文字就是 pattern.toString()）。</summary>
    public string Plain()
    {
        var sb = new StringBuilder();
        foreach (var s in Spans(McColors.White))
        {
            sb.Append(s.Text ?? s.Pattern!.ToString());
        }
        return sb.ToString();
    }

    /// <summary>压平成一段一段（文字或图案）+ 实际颜色，按显示顺序。</summary>
    public List<DisplaySpan> Spans(uint baseColor)
    {
        var o = new List<DisplaySpan>();
        Collect(o, baseColor);
        return o;
    }

    private void Collect(List<DisplaySpan> o, uint inherited)
    {
        uint c = Color ?? inherited;
        if (Text is { Length: > 0 }) o.Add(new DisplaySpan(Text, null, c));
        if (Pattern is not null) o.Add(new DisplaySpan(null, Pattern, c));
        foreach (var child in _children) child.Collect(o, c);
    }

    public override string ToString() => Plain();
}

/// <summary>压平后的一段：文字（<see cref="Text"/>）或图案（<see cref="Pattern"/>），带实际颜色 0xRRGGBB。</summary>
public readonly record struct DisplaySpan(string? Text, HexPattern? Pattern, uint Color);

/// <summary>Minecraft 的 16 种格式色（ChatFormatting），上游 iota 显示用的就是这些。</summary>
public static class McColors
{
    public const uint Black = 0x000000;
    public const uint DarkBlue = 0x0000AA;
    public const uint DarkGreen = 0x00AA00;
    public const uint DarkAqua = 0x00AAAA;
    public const uint DarkRed = 0xAA0000;
    public const uint DarkPurple = 0xAA00AA;
    public const uint Gold = 0xFFAA00;
    public const uint Gray = 0xAAAAAA;
    public const uint DarkGray = 0x555555;
    public const uint Blue = 0x5555FF;
    public const uint Green = 0x55FF55;
    public const uint Aqua = 0x55FFFF;
    public const uint Red = 0xFF5555;
    public const uint LightPurple = 0xFF55FF;
    public const uint Yellow = 0xFFFF55;
    public const uint White = 0xFFFFFF;
}

/// <summary>
/// 显示时的挂钩：附属在这里改列表 / 图案的显示（上游 HexParse 用 mixin 给嵌套列表和括号上色、去掉注释旁边的逗号）。
/// 调用顺序和上游 mixin 一样：列表显示开始时 <see cref="BeforeList"/>，里面的元素显示完、整个列表拼好后 <see cref="AfterList"/>。
/// </summary>
public interface IIotaDisplayDecorator
{
    void BeforeList(ListIota list);

    void AfterList(ListIota list, DisplayText shown);

    void AfterPattern(PatternIota pattern, DisplayText shown);

    /// <summary>列表里相邻的 a、b 之间本来要加逗号：返回 true 就不加。</summary>
    bool DropComma(Iota a, Iota b);
}

/// <summary>iota 显示的全局设置与挂钩。</summary>
public static class IotaDisplay
{
    /// <summary>附属登记的显示挂钩（加载时加、卸载时删）。</summary>
    public static readonly List<IIotaDisplayDecorator> Decorators = new();

    /// <summary>原版客户端设置 alwaysShowListCommas（默认关）：列表里图案之间也加逗号。</summary>
    public static bool AlwaysShowListCommas { get; set; }

    /// <summary>实体的显示名（Core 不认识泰拉的实体，由游戏侧设置）。返回 null = 原版「未知实体」。</summary>
    public static Func<EntityIota, string?>? EntityName { get; set; }

    /// <summary>上游 Shift 键是否按着（HexParse 的注释按住 Shift 时不显示）。由游戏侧设置。</summary>
    public static Func<bool>? ShiftDown { get; set; }

    /// <summary>上游 Util.getMillis 的替身（HexParse 大法术占位的滚动效果）。测试里可以换掉。</summary>
    public static Func<long> Millis { get; set; } = () => Environment.TickCount64;

    internal static bool DropsComma(Iota a, Iota b)
    {
        foreach (var d in Decorators)
        {
            if (d.DropComma(a, b)) return true;
        }
        return false;
    }

    /// <summary>上游 String.format("%.2f", d)：两位小数，四舍五入，不受系统区域影响。</summary>
    public static string Fixed2(double d) => d.ToString("F2", CultureInfo.InvariantCulture);
}

/// <summary>
/// 把 <see cref="DisplayText"/> 写成泰拉的聊天标记，让聊天栏、物品说明这些「字符串 + 标记」的地方也能画颜色和小图案。
///
/// 泰拉自带的 [c/颜色:文字] 不行：文字里不能有「]」（列表正好满是方括号），也没有转义。所以用自己的两个标记，
/// 内容里的特殊字符用 % 编码：
///   [hext/RRGGBB:文字]        一段有颜色的文字
///   [hexp/RRGGBB:方向/签名]    一个内嵌图案（方向是 HexDir 的数字）
/// 标记由 Client/UI/RichText.cs 注册给泰拉；服务端和测试只用这里的纯字符串函数。
/// </summary>
public static class DisplayTags
{
    public const string TextTag = "hext";
    public const string GlyphTag = "hexp";

    private const char Backslash = (char)92;

    public static string Of(Iota iota) => Of(iota.DisplayRich());

    public static string Of(DisplayText text, uint baseColor = McColors.White)
    {
        var sb = new StringBuilder();
        foreach (var s in text.Spans(baseColor))
        {
            string col = s.Color.ToString("X6", CultureInfo.InvariantCulture);
            if (s.Pattern is { } p)
            {
                sb.Append('[').Append(GlyphTag).Append('/').Append(col).Append(':')
                  .Append(((int)p.StartDir).ToString(CultureInfo.InvariantCulture)).Append('/').Append(p.AnglesSignature()).Append(']');
            }
            else
            {
                sb.Append('[').Append(TextTag).Append('/').Append(col).Append(':').Append(Escape(s.Text!)).Append(']');
            }
        }
        return sb.ToString();
    }

    /// <summary>% 编码会破坏标记的字符：% [ ] 反斜杠 换行。</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '%': sb.Append("%25"); break;
                case '[': sb.Append("%5B"); break;
                case ']': sb.Append("%5D"); break;
                case Backslash: sb.Append("%5C"); break;
                case '\n': sb.Append("%0A"); break;
                case '\r': break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        if (s.IndexOf('%') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length
                && int.TryParse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 2;
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>读回图案标记的内容（「方向/签名」）。</summary>
    public static HexPattern? ParseGlyph(string payload)
    {
        int slash = payload.IndexOf('/');
        if (slash <= 0 || !int.TryParse(payload.AsSpan(0, slash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dir)
            || dir < 0 || dir > 5) return null;
        return HexPattern.TryFromAnglesUnchecked(payload.Substring(slash + 1), (HexDir)dir, out var p, out _) ? p : null;
    }

    /// <summary>去掉本类的标记，留下纯文字（图案写成 HexPattern[…]）。给日志、外部调试器这些不认标记的地方用。</summary>
    public static string Strip(string tagged)
    {
        if (tagged.IndexOf('[') < 0) return tagged;
        var sb = new StringBuilder(tagged.Length);
        int i = 0;
        while (i < tagged.Length)
        {
            if (tagged[i] == '[' && TryReadTag(tagged, i, out string tag, out string payload, out int end))
            {
                if (tag == TextTag) sb.Append(Unescape(payload));
                else sb.Append(ParseGlyph(payload)?.ToString() ?? payload);
                i = end;
                continue;
            }
            sb.Append(tagged[i]);
            i++;
        }
        return sb.ToString();
    }

    private static bool TryReadTag(string s, int start, out string tag, out string payload, out int end)
    {
        tag = payload = "";
        end = start;
        foreach (string name in new[] { TextTag, GlyphTag })
        {
            if (string.CompareOrdinal(s, start + 1, name, 0, name.Length) != 0 || start + 1 + name.Length >= s.Length
                || s[start + 1 + name.Length] != '/') continue;
            int colon = s.IndexOf(':', start + 2 + name.Length);
            int close = colon < 0 ? -1 : s.IndexOf(']', colon);
            if (close < 0) return false;
            tag = name;
            payload = s.Substring(colon + 1, close - colon - 1);
            end = close + 1;
            return true;
        }
        return false;
    }
}
