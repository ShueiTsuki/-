using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.Hexcessible.Core;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;
using Terraria;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// 游戏侧的图案索引：本体 + 已开的附属图案 + 书（含附属书页），书条目锁不锁看当前玩家的解锁进度。
/// 上游在每次打开施法界面时 invalidateCaches，这里画布关闭时清查询缓存。
/// </summary>
public static class HexcessibleIndex
{
    private static PatternEntries? _index;
    private static BookDocument? _book;
    private static BookProgress? _progress;
    private static uint _progressTick = uint.MaxValue;

    public static PatternEntries Get()
    {
        var book = HexBook.Document;
        if (_index is null || !ReferenceEquals(_book, book))
        {
            var seen = new HashSet<string>();
            var defs = PatternRegistry.All.Concat(PatternRegistry.EnabledAddonPatterns()).Where(d => seen.Add(d.Id)).ToList();
            // 大法术：学会本世界画法（上游 PerWorldLearnMixin）那项还没做，先一律当没学会
            _index = new PatternEntries(defs, book, PatternRegistry.IsPerWorld)
            {
                AliasOf = HexcessibleStore.AliasOf,
                Smart = new SmartSigs(SmartText()),
            };
            _book = book;
        }
        return _index;
    }

    /// <summary>上游 Entry.locked()：图案所在书条目没解锁。进度一帧只算一次。</summary>
    public static bool IsLocked(PatternEntries.Entry e)
    {
        if (_progress is null || _progressTick != Main.GameUpdateCount)
        {
            _progress = HexBook.CurrentProgress();
            _progressTick = Main.GameUpdateCount;
        }
        var p = _progress;
        return Get().IsLocked(e.Id, adv => BookUnlocks.IsUnlocked(adv, p));
    }

    public static void InvalidateCaches() => _index?.InvalidateCaches();

    /// <summary>
    /// 智能签名的文字。「数字之精思：%s」「簿记员之策略：%s」和其它图案名一样用官方中文名（移植版的图案名只有中文）；
    /// 簿记员的说明是 Hexcessible 自己的文字，跟随游戏语言。
    /// </summary>
    private static SmartSigText SmartText()
    {
        static string T(string key) => Terraria.Localization.Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible.SmartSig." + key);
        return new SmartSigText("数字之精思：{0}", "簿记员之策略：{0}",
            T("Prefix"), T("Drop"), T("Drop1"), T("Keep"), T("Keep1"), T("Join"), T("Suffix"));
    }

    public static void Reset()
    {
        _index = null;
        _book = null;
        _progress = null;
    }
}
