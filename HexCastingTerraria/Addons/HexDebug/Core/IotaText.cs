using System.Linq;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// iota 转成文字（上游 utils/Extensions.kt 的 displayWithPatternName / toHexpatternSource / getI18nOrNull / simpleString）。
/// </summary>
public static class IotaText
{
    /// <summary>上游 displayWithPatternName：图案用名字，列表逐项展开，其余用 iota 自己的显示。</summary>
    public static string Display(Iota iota) => iota switch
    {
        PatternIota p => PatternName(p.Pattern) ?? p.Display(),
        ListIota l => "[" + string.Join(", ", l.Items.Select(Display)) + "]",
        _ => iota.Display(),
    };

    /// <summary>
    /// 上游 toHexpatternSource：源码视图里一行的写法。内省 / 反思写成 { 和 }，认识的图案写名字，
    /// 不认识的写成 &lt;方向 签名&gt;，列表写成 [a, b]，其余 iota 包在尖括号里。
    /// </summary>
    public static string Source(Iota iota, bool wrapEmbedded = true)
    {
        string text;
        switch (iota)
        {
            case PatternIota p:
                var sig = p.Pattern.AnglesSignature();
                if (sig == "qqq") return "{";
                if (sig == "eee") return "}";
                var name = PatternName(p.Pattern);
                if (name is not null) return name;
                text = SimpleString(p.Pattern);
                break;
            case ListIota l:
                text = "[" + string.Join(", ", l.Items.Select(i => i is PatternIota ip
                    ? PatternName(ip.Pattern) ?? SimpleString(ip.Pattern)
                    : Source(i, wrapEmbedded: false))) + "]";
                break;
            case GarbageIota:
                text = "Garbage";
                break;
            default:
                text = iota.Display();
                break;
        }
        return wrapEmbedded ? "<" + text + ">" : text;
    }

    /// <summary>上游 getI18nOrNull：普通图案、本世界大法术、特殊图案（数字 / 簿记员）的名字；都不是返回 null。</summary>
    public static string? PatternName(HexPattern pattern)
    {
        var def = PatternRegistry.Match(pattern);
        if (def is not null) return def.DisplayName();
        if (SpecialPatterns.TryNumber(pattern.AnglesSignature(), out var n)) return "数字之精思：" + new DoubleIota(n).Display();
        if (SpecialPatterns.TryMask(pattern, out var mask)) return "簿记员之策略：" + new string(mask.Select(k => k ? '-' : 'v').ToArray());
        return null;
    }

    /// <summary>上游 simpleString：「起笔方向 签名」，如 EAST、NORTH_WEST aqwed。</summary>
    public static string SimpleString(HexPattern p)
    {
        var dir = p.StartDir switch
        {
            HexDir.NorthEast => "NORTH_EAST",
            HexDir.East => "EAST",
            HexDir.SouthEast => "SOUTH_EAST",
            HexDir.SouthWest => "SOUTH_WEST",
            HexDir.West => "WEST",
            _ => "NORTH_WEST",
        };
        var sig = p.AnglesSignature();
        return sig.Length == 0 ? dir : dir + " " + sig;
    }
}
