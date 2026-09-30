using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 图案索引（上游 entries/PatternEntries.java + entries/BookEntries.java）：每个图案的名字、签名、书里的图案页
/// （参数行与说明），按签名查、按名字模糊搜。纯逻辑：图案表与书由调用方给，所以离线可测。
/// </summary>
public sealed class PatternEntries
{
    /// <summary>书里的一页图案页（上游 BookEntries.Entry）。</summary>
    public sealed record Impl(string Id, string EntryId, string Desc, string In, string Out, int Page)
    {
        /// <summary>上游 getArgs：<c>(in + " -> " + out).strip()</c>。</summary>
        public string Args => (In + " -> " + Out).Trim();

        /// <summary>上游 getDesc：去掉 Patchouli 的 <c>$(...)</c> 与 <c>/$</c> 标记，<c>_</c> 前面是空白的换成空格。</summary>
        public string CleanDesc => Underscore.Replace(Markup.Replace(Desc, ""), " ");

        private static readonly Regex Markup = new(@"\$\([^)]*\)|/\$", RegexOptions.Compiled);
        private static readonly Regex Underscore = new(@"[\s^]_", RegexOptions.Compiled);
    }

    /// <summary>
    /// 一条图案（上游 PatternEntries.Entry）。<see cref="Sigs"/> 为 null = 每个世界画法不同、还没学会（上游 sig() 返回 null）。
    /// 智能签名（数字等）一条可以是好几个图案，所以签名是一组。
    /// </summary>
    public sealed record Entry(string Id, string RawName, HexDir Dir, IReadOnlyList<IReadOnlyList<HexAngle>>? Sigs,
        IReadOnlyList<IReadOnlyList<HexAngle>> RawSigs, IReadOnlyList<Impl> Impls, int Z = 0)
    {
        /// <summary>显示名（别名功能接上后在这里换成别名）。</summary>
        public string Name => RawName;

        /// <summary>
        /// 上游 toSignature：每条签名一段 <c>&lt;EAST,qaq&gt;</c>。用的是记录里的原始签名（大法术就是注册时的标准画法），
        /// 不是 sig()，所以大法术也照样显示标准画法（不是本世界的）。
        /// </summary>
        public string Signature => string.Concat(RawSigs.Select(s =>
            "<" + JavaDirName(Dir) + "," + new string(s.Select(KeyboardPlacement.LetterOf).ToArray()) + ">"));

        /// <summary>上游 toString（tooltipRenderSigs 默认开：签名 + 空格 + 名字）。</summary>
        public override string ToString() => Signature + " " + Name;

        /// <summary>上游 is(sig)：只有一条签名且角度完全相同。</summary>
        public bool Is(IReadOnlyList<HexAngle> sig) => Sigs is { Count: 1 } s && s[0].SequenceEqual(sig);
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _bySig = new();
    private readonly Dictionary<string, string> _advancementOf = new();
    private readonly Dictionary<string, List<Entry>> _searchCache = new();

    /// <summary>
    /// 上游 reindex。<paramref name="perWorldSig"/>：大法术（每个世界画法不同）学会了就给出本世界的画法，没学会给 null。
    /// </summary>
    public PatternEntries(IEnumerable<PatternDef> patterns, BookDocument? book, Func<PatternDef, bool> isPerWorld,
        Func<PatternDef, HexPattern?>? perWorldSig = null)
    {
        var impls = new Dictionary<string, List<Impl>>();
        if (book is not null)
        {
            foreach (var entry in book.EntryById.Values)
            {
                int page = 0;
                foreach (var p in entry.Pages)
                {
                    if (p.Kind != BookPageKind.Pattern || p.PatternId.Length == 0) continue;
                    // 上游：同一个图案出现在好几个条目里时，锁不锁看第一个
                    if (!_advancementOf.ContainsKey(p.PatternId)) _advancementOf[p.PatternId] = entry.Advancement;
                    if (!impls.TryGetValue(p.PatternId, out var list)) impls[p.PatternId] = list = new List<Impl>();
                    list.Add(new Impl(p.PatternId, entry.Id, p.Text, p.Input, p.Output, page++));
                }
            }
        }

        foreach (var def in patterns)
        {
            IReadOnlyList<IReadOnlyList<HexAngle>>? sigs;
            var raw = new[] { (IReadOnlyList<HexAngle>)def.Prototype.Angles.ToList() };
            HexDir dir = def.StartDir;
            if (isPerWorld(def))
            {
                var learned = perWorldSig?.Invoke(def);
                sigs = learned is null ? null : new[] { (IReadOnlyList<HexAngle>)learned.Angles.ToList() };
            }
            else
            {
                sigs = raw;
            }
            var e = new Entry(def.Id, def.DisplayName(), dir, sigs, raw,
                impls.TryGetValue(def.Id, out var li) ? li : (IReadOnlyList<Impl>)Array.Empty<Impl>());
            _entries.Add(e);
            if (sigs is { Count: 1 }) _bySig.TryAdd(Key(sigs[0]), e);
        }
    }

    public IReadOnlyList<Entry> All => _entries;

    /// <summary>上游 getFromSig（智能签名单独一块，调用方先查）：第一个签名完全相同的图案。</summary>
    public Entry? FromSig(IReadOnlyList<HexAngle> sig) => _bySig.TryGetValue(Key(sig), out var e) ? e : null;

    /// <summary>上游 BookEntries.isLocked：图案所在的（第一个）书条目还没解锁。书里没有这个图案 = 不锁。</summary>
    public bool IsLocked(string id, Func<string, bool> isAdvancementUnlocked)
        => _advancementOf.TryGetValue(id, out var adv) && !isAdvancementUnlocked(adv);

    /// <summary>
    /// 上游 get(query)：空查询返回全部；否则 分数 = z × 10000 + 名字命中 × 3 + id 命中（id 里的 : _ / 当空格），
    /// 去掉 0 分，按分数从高到低（同分保持原顺序）。<paramref name="extra"/> 是智能签名给出的候选。
    /// </summary>
    public IReadOnlyList<Entry> Search(string query, IEnumerable<Entry>? extra = null)
    {
        if (string.IsNullOrEmpty(query)) return _entries;
        if (extra is null && _searchCache.TryGetValue(query, out var cached)) return cached;

        var pool = extra is null ? _entries : _entries.Concat(extra);
        var result = pool
            .Select(e => (Entry: e, Score: e.Z * 10_000 + FluffySearch.Score(query, e.Name) * 3
                                              + FluffySearch.Score(query, IdWords.Replace(e.Id, " "))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Entry)
            .ToList();
        if (extra is null) _searchCache[query] = result;
        return result;
    }

    /// <summary>上游 invalidateCaches。</summary>
    public void InvalidateCaches() => _searchCache.Clear();

    private static readonly Regex IdWords = new("[:_/]", RegexOptions.Compiled);

    private static string Key(IReadOnlyList<HexAngle> sig) => new(sig.Select(KeyboardPlacement.LetterOf).ToArray());

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
