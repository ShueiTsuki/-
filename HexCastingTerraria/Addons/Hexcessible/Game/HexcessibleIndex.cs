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
            _index = new PatternEntries(defs, book, PatternRegistry.IsPerWorld);
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

    public static void Reset()
    {
        _index = null;
        _book = null;
        _progress = null;
    }
}
