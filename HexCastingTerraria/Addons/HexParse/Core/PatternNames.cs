using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 图案名 -> 图案（上游 hooks/PatternMapper.java）。
///
/// - 元符号：<c>\</c>（考察）、<c>del</c> / <c>undo</c>（撤销）、<c>( {</c>（内省）、<c>) }</c>（反思）
/// - 普通图案：长 id（hexcasting:add）和短名（add）都能用
/// - 大法术：本世界的画法（每个世界笔顺不同），另放一张表，解析时还要查解锁表
/// - 短名冲突（两个附属的图案路径相同）：玩家手动指定的优先，其次本体（hexcasting 命名空间），再其次先登记的
///
/// 图案来源 = 本体 188 条 + 开着的附属的图案（关着的附属不在表里，和上游没装那个附属一样）。
/// </summary>
public sealed class PatternNames
{
    public const string HexNamespace = "hexcasting";

    public Dictionary<string, HexPattern> Meta { get; } = new();
    public Dictionary<string, HexPattern> Normal { get; } = new();
    public Dictionary<string, HexPattern> Great { get; } = new();

    /// <summary>短名 -> 用这个短名的所有长 id。</summary>
    public Dictionary<string, SortedSet<string>> AllPointed { get; } = new();

    /// <summary>短名当前指向的长 id。</summary>
    public Dictionary<string, string> ActiveShortName { get; } = new();

    /// <summary>有冲突的短名（指向不止一个长 id）。</summary>
    public SortedSet<string> ShortNameWithConflicts { get; } = new();

    private readonly System.Func<string, string?> _manual;

    private PatternNames(System.Func<string, string?> manual)
    {
        _manual = manual;
        Meta["\\"] = Pat("qqqaw", HexDir.West);
        var undo = Pat("eeedw", HexDir.East);
        Meta["del"] = undo;
        Meta["undo"] = undo;
        var open = Pat("qqq", HexDir.West);
        var close = Pat("eee", HexDir.East);
        Meta["("] = open;
        Meta["{"] = open;
        Meta[")"] = close;
        Meta["}"] = close;
    }

    /// <summary>
    /// 从当前登记表建表（上游 PatternMapper.init）。
    /// <paramref name="patternInThisWorld"/> 给出大法术在本世界的画法；<paramref name="manual"/> 是 /hexParse conflict set 存的指定。
    /// </summary>
    public static PatternNames Build(System.Func<PatternDef, HexPattern> patternInThisWorld, System.Func<string, string?> manual)
    {
        var names = new PatternNames(manual);
        foreach (var def in PatternRegistry.All.Concat(PatternRegistry.EnabledAddonPatterns()))
        {
            if (PatternRegistry.IsPerWorld(def)) names.Set(names.Great, def.Id, patternInThisWorld(def));
            else names.Set(names.Normal, def.Id, def.Prototype);
        }
        return names;
    }

    public static string ShortOf(string longId) => longId.Contains(':') ? longId.Substring(longId.IndexOf(':') + 1) : longId;

    public static string NamespaceOf(string longId) => longId.Contains(':') ? longId.Substring(0, longId.IndexOf(':')) : "";

    /// <summary>短名 -> 长 id（上游 getActiveLongName：不认识就原样返回）。</summary>
    public string ActiveLongName(string shortName) => ActiveShortName.TryGetValue(shortName, out var id) ? id : shortName;

    private void Set(Dictionary<string, HexPattern> map, string longId, HexPattern pattern)
    {
        map[longId] = pattern;
        if (RecordNewShortName(longId)) map[ShortOf(longId)] = pattern;
    }

    /// <summary>上游 ShortNameTracker.recordNewShortName：返回这个 id 是否该占用短名。</summary>
    private bool RecordNewShortName(string longId)
    {
        string shortName = ShortOf(longId);
        if (!AllPointed.TryGetValue(shortName, out var set)) AllPointed[shortName] = set = new SortedSet<string>();
        set.Add(longId);
        if (set.Count > 1) ShortNameWithConflicts.Add(shortName);

        ActiveShortName.TryGetValue(shortName, out var existing);
        string? selected = _manual(shortName);
        bool manualSelected = longId == selected;
        bool manualOld = !string.IsNullOrEmpty(selected);
        bool imBoss = manualSelected || (!manualOld && NamespaceOf(longId) == HexNamespace);
        if (existing == null || imBoss)
        {
            ActiveShortName[shortName] = longId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 上游 redirectShortName：把短名改指向另一个长 id。只允许有冲突的短名、而且目标确实用这个短名。
    /// 成功后调用方负责把指定存进世界（<see cref="IHexParseHost.ManualShortName"/> 的来源）。
    /// </summary>
    public void RedirectShortName(string shortName, string newLongId)
    {
        if (!AllPointed.TryGetValue(shortName, out var valid) || !ShortNameWithConflicts.Contains(shortName))
            throw new HexParseException($"设置短名称 \"{shortName}\" 时出错：无效名称");
        if (!valid.Contains(newLongId))
            throw new HexParseException($"设置短名称 \"{shortName}\" 时出错：无效图案ID：{newLongId}");
        bool found = false;
        foreach (var map in new[] { Normal, Great })
        {
            map.Remove(shortName);
            if (found) continue;
            if (map.TryGetValue(newLongId, out var p))
            {
                found = true;
                map[shortName] = p;
            }
        }
        if (!found) throw new HexParseException($"设置短名称 \"{shortName}\" 时出错：excuse me WTF?");
        ActiveShortName[shortName] = newLongId;
    }

    private static HexPattern Pat(string angles, HexDir dir)
    {
        HexPattern.TryFromAnglesUnchecked(angles, dir, out var p, out _);
        return p!;
    }
}
