using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// HexDebug 的 22 个图案（上游 registry/HexDebugActions.kt，形状照抄）。开关关着也要登记形状（本世界大法术笔顺不许和它们撞）；
/// 剪接台那 16 个随剪接台一起实现。
/// </summary>
public static class HexDebugPatterns
{
    public static readonly PatternData[] All =
    {
        new("hexdebug:const/cognitohazard", "wdeaqqdqeedqadqeedqaeadeaqqeadeaqqdqdeaqqeaeedqaw", HexDir.NorthWest, "CognitohazardConst"),
        new("hexdebug:const/debugging", "qqqqqewaa", HexDir.East, "OpIsDebugging"),
        new("hexdebug:breakpoint/before", "awqdeew", HexDir.SouthWest, "OpBreakpoint"),
        new("hexdebug:breakpoint/after", "wqqaewd", HexDir.East, "OpBreakpoint"),
        new("hexdebug:craft/debugger", "aaewwwwwaqwawqwadawqwwwawwwqwwwaw", HexDir.SouthWest, "OpMakePackagedSpell"),
        new("hexdebug:craft/quenched_debugger", "ddwwwwwwedwewdweqewdwwwewwwdwwwew", HexDir.SouthEast, "OpMakePackagedSpell"),

        new("hexdebug:splicing/selection/read", "wqaeaqweeeedq", HexDir.NorthWest, "OpReadSelection"),
        new("hexdebug:splicing/selection/write", "wedqdewqqqqae", HexDir.SouthWest, "OpWriteSelection"),
        new("hexdebug:splicing/view_index/read", "wqaeaqwdwaqaw", HexDir.NorthWest, "OpReadViewIndex"),
        new("hexdebug:splicing/view_index/write", "wedqdewawdedw", HexDir.SouthWest, "OpWriteViewIndex"),
        new("hexdebug:splicing/list/spellbook_index/read", "wqaeaqwedqddq", HexDir.NorthWest, "OpReadSpellbookIndex"),
        new("hexdebug:splicing/list/spellbook_index/write", "wedqdewqaeaae", HexDir.SouthWest, "OpWriteSpellbookIndex"),
        new("hexdebug:splicing/list/spellbook_index/readable", "wqaeaqwedqddqw", HexDir.NorthWest, "OpReadableSpellbookIndex"),
        new("hexdebug:splicing/clipboard/read", "wqaeaqweeeedw", HexDir.NorthWest, "OpReadClipboard"),
        new("hexdebug:splicing/clipboard/write", "wedqdewqqqqaw", HexDir.SouthWest, "OpWriteClipboard"),
        new("hexdebug:splicing/clipboard/readable", "wqaeaqweeeedww", HexDir.NorthWest, "OpReadableClipboard"),
        new("hexdebug:splicing/clipboard/writable", "wedqdewqqqqaww", HexDir.SouthWest, "OpWritableClipboard"),
        new("hexdebug:splicing/clipboard/spellbook_index/read", "wqaeaqwdeaaea", HexDir.NorthWest, "OpReadSpellbookIndex"),
        new("hexdebug:splicing/clipboard/spellbook_index/write", "wedqdewaqddqd", HexDir.SouthWest, "OpWriteSpellbookIndex"),
        new("hexdebug:splicing/clipboard/spellbook_index/readable", "wqaeaqwdeaaeae", HexDir.NorthWest, "OpReadableSpellbookIndex"),
        new("hexdebug:splicing/enlightened/hex/read", "wqaeaqwqqqwqwqqwq", HexDir.NorthWest, "OpReadEnlightenedHex"),
        new("hexdebug:splicing/enlightened/hex/write", "wqaeaqwqqqwqwqqwwqqeaeqqeqqeaeqqw", HexDir.NorthWest, "OpWriteEnlightenedHex"),
    };

    /// <summary>官方中文名（上游 assets/hexdebug/lang/zh_cn.flatten.json5）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["hexdebug:const/debugging"] = "调试杖之精思",
        ["hexdebug:const/cognitohazard"] = "认知危害之精思",
        ["hexdebug:breakpoint/before"] = "在前方添加断点",
        ["hexdebug:breakpoint/after"] = "在后方添加断点",
        ["hexdebug:craft/debugger"] = "制作调试杖",
        ["hexdebug:craft/quenched_debugger"] = "制作淬灵调试杖",
        ["hexdebug:splicing/view_index/read"] = "齿孔胶片之纯化",
        ["hexdebug:splicing/view_index/write"] = "齿孔胶片之策略",
        ["hexdebug:splicing/selection/read"] = "剪接器之分解",
        ["hexdebug:splicing/selection/write"] = "剪接器之策略",
        ["hexdebug:splicing/list/spellbook_index/read"] = "放映员之纯化",
        ["hexdebug:splicing/list/spellbook_index/write"] = "放映员之策略",
        ["hexdebug:splicing/list/spellbook_index/readable"] = "快门之纯化",
        ["hexdebug:splicing/clipboard/read"] = "合成师之纯化",
        ["hexdebug:splicing/clipboard/write"] = "合成师之策略",
        ["hexdebug:splicing/clipboard/readable"] = "制片人之纯化",
        ["hexdebug:splicing/clipboard/writable"] = "导演之纯化",
        ["hexdebug:splicing/clipboard/spellbook_index/read"] = "放映员之纯化，第二型",
        ["hexdebug:splicing/clipboard/spellbook_index/write"] = "放映员之策略，第二型",
        ["hexdebug:splicing/clipboard/spellbook_index/readable"] = "快门之纯化，第二型",
        ["hexdebug:splicing/enlightened/hex/read"] = "制念之纯化",
        ["hexdebug:splicing/enlightened/hex/write"] = "融注制念台",
    };
}
