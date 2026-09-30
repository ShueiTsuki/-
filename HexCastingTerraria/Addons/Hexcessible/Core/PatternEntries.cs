using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 按签名查图案的说明（上游 entries/PatternEntries.java 的 getFromSig + Entry.toString，
/// 与 entries/BookEntries.java 的参数行：书里图案页的 input -> output）。
/// </summary>
public static class PatternEntries
{
    /// <summary>一条图案：id、显示名、起笔方向与签名、书里每一页图案页的参数行。</summary>
    public sealed record Entry(string Id, string Name, HexDir Dir, string Angles, IReadOnlyList<string> Args)
    {
        /// <summary>上游 Entry.toSignature：<c>&lt;EAST,qaq&gt;</c>。</summary>
        public string Signature => "<" + JavaDirName(Dir) + "," + Angles.ToLowerInvariant() + ">";

        /// <summary>上游 Entry.toString（tooltipRenderSigs 默认开：签名 + 空格 + 名字）。</summary>
        public override string ToString() => Signature + " " + Name;
    }

    private static Dictionary<string, List<string>>? _args;
    private static BookDocument? _argsFrom;

    /// <summary>
    /// 上游 getFromSig（去掉智能签名，那一块单独移植）。大法术（每个世界画法不同）不在这里认：
    /// 上游只认玩家手持远古卷轴学过的那些（PerWorldLearnMixin，单独一项功能），没学过就当不认识，免得泄露画法。
    /// </summary>
    public static Entry? FromSig(IReadOnlyList<HexAngle> sig, BookDocument? book)
    {
        var angles = new string(sig.Select(KeyboardPlacement.LetterOf).ToArray());
        var def = PatternRegistry.Match(angles);
        if (def is null || PatternRegistry.IsPerWorld(def)) return null;
        return new Entry(def.Id, def.DisplayName(), def.StartDir, def.Angles, ArgsOf(def.Id, book));
    }

    /// <summary>上游 BookEntries.Entry.getArgs：<c>(in + " -> " + out).strip()</c>，一个图案可以有好几页。</summary>
    public static IReadOnlyList<string> ArgsOf(string id, BookDocument? book)
    {
        if (book is null) return System.Array.Empty<string>();
        if (_args is null || !ReferenceEquals(_argsFrom, book))
        {
            _args = Index(book);
            _argsFrom = book;
        }
        return _args.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)System.Array.Empty<string>();
    }

    public static Dictionary<string, List<string>> Index(BookDocument book)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var entry in book.EntryById.Values)
        {
            foreach (var page in entry.Pages)
            {
                if (page.Kind != BookPageKind.Pattern || page.PatternId.Length == 0) continue;
                if (!map.TryGetValue(page.PatternId, out var list)) map[page.PatternId] = list = new List<string>();
                list.Add((page.Input + " -> " + page.Output).Trim());
            }
        }
        return map;
    }

    public static void Invalidate()
    {
        _args = null;
        _argsFrom = null;
    }

    /// <summary>上游 HexDir 的 Java 枚举名（签名里显示的就是它）。</summary>
    public static string JavaDirName(HexDir d) => d switch
    {
        HexDir.NorthEast => "NORTH_EAST",
        HexDir.East => "EAST",
        HexDir.SouthEast => "SOUTH_EAST",
        HexDir.SouthWest => "SOUTH_WEST",
        HexDir.West => "WEST",
        _ => "NORTH_WEST",
    };
}
