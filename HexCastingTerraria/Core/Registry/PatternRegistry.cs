using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Registry;

/// <summary>
/// 图案匹配结果，对应源项目的 PatternShapeMatch 联合类型。
/// </summary>
public enum PatternMatchKind
{
    /// <summary>没有任何图案匹配。</summary>
    Nothing,

    /// <summary>命中普通图案。</summary>
    Normal,

    /// <summary>命中当前世界专属图案（per-world，随机化图案）。</summary>
    PerWorld,

    /// <summary>命中特殊处理器（数字字面量等动态图案）。</summary>
    Special,
}

/// <summary>一条已注册的图案定义。</summary>
public sealed class PatternDef
{
    public required string Id { get; init; }
    public required string Angles { get; init; }
    public required HexDir StartDir { get; init; }
    public required string Op { get; init; }
    public required HexPattern Prototype { get; init; }

    /// <summary>
    /// 是否需要「启蒙」才能使用。
    /// 对应源项目的动作标签 requires_enlightenment（用于大法术）。
    /// </summary>
    public bool RequiresEnlightenment { get; init; }

    /// <summary>匹配键：只看角度签名（起始方向不参与匹配）。</summary>
    public string MatchKey => Prototype.MatchKey();

    public override string ToString() => $"{Id} [{StartDir} {Angles}] -> {Op}";
}

/// <summary>加载与匹配结果统计。</summary>
public sealed class PatternRegistryLoadResult
{
    public int Total;
    public int Loaded;
    public readonly List<string> StrictValidationFailures = new();
    public readonly List<string> DuplicateSignatures = new();
}

/// <summary>
/// 图案注册表（源：PatternRegistryManifest + HexActions 的 ACTIONS 表）。
///
/// 源项目用 signature -&gt; action 的哈希表做 O(1) 匹配；
/// 这里沿用同样的策略：键为「起始方向 + 角度签名」。
///
/// 注意：源项目在 processRegistry 里发现重复签名时只打 warning 并覆盖。
/// 我们保留同样行为，但把重复项记录下来供排查。
/// </summary>
public static class PatternRegistry
{
    /// <summary>
    /// 需要「启蒙」才能使用的图案 Id。
    /// 对应源项目的数据驱动标签 HexTags.Actions.REQUIRES_ENLIGHTENMENT
    /// （那边由 datagen 生成标签文件，我们这里直接列出来，更直观也更好查）。
    ///
    /// 这些都是「大法术」（great spells）：效果强、媒质消耗极高、
    /// 且在设定上是只有启蒙者才敢尝试的东西。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> EnlightenmentRequired = new()
    {
        // ⚠️ 这份清单**逐条抄自源项目** `datagen/tag/HexActionTagProvider.java` 里
        // 打进 `REQUIRES_ENLIGHTENMENT` 标签的那 14 个图案，不是「看起来像大法术的」推测。
        //
        // 之前这里只有 teleport/great 一条 —— 那意味着 lightning、summon_rain、
        // 以及 5 个药水图案在泰拉侧**没启蒙也能用**。这类漏洞不会报错、也不会崩，
        // 只会让「启蒙」这个进度目标形同虚设，属于最难自己发现的一类错误。
        "hexcasting:lightning",
        "hexcasting:flight",
        "hexcasting:create_lava",
        "hexcasting:teleport/great",
        "hexcasting:sentinel/create/great",
        "hexcasting:dispel_rain",
        "hexcasting:summon_rain",
        "hexcasting:brainsweep",
        "hexcasting:craft/battery",
        "hexcasting:potion/regeneration",
        "hexcasting:potion/night_vision",
        "hexcasting:potion/absorption",
        "hexcasting:potion/haste",
        "hexcasting:potion/strength",
    };

    private static readonly Dictionary<string, PatternDef> Lookup = new();
    private static readonly List<PatternDef> AllList = new();
    private static bool _initialized;

    public static IReadOnlyList<PatternDef> All => AllList;

    public static int Count => AllList.Count;

    public static PatternRegistryLoadResult LoadResult { get; private set; } = new PatternRegistryLoadResult();

    /// <summary>
    /// 从生成的数据表装载全部图案。
    /// strictValidation=true 时会用带重叠校验的构造器，用来体检数据本身是否自洽。
    /// </summary>
    public static PatternRegistryLoadResult Load(bool strictValidation = false)
    {
        Lookup.Clear();
        AllList.Clear();
        var result = new PatternRegistryLoadResult();

        foreach (var data in GeneratedPatternData.All)
        {
            result.Total++;

            HexPattern? proto;
            ParseError? err;

            bool ok = strictValidation
                ? HexPattern.TryFromAngles(data.Angles, data.StartDir, out proto, out err)
                : HexPattern.TryFromAnglesUnchecked(data.Angles, data.StartDir, out proto, out err);

            if (!ok || proto is null)
            {
                result.StrictValidationFailures.Add($"{data.Id}: {err}");
                continue;
            }

            var def = new PatternDef
            {
                Id = data.Id,
                Angles = data.Angles,
                StartDir = data.StartDir,
                Op = data.Op,
                Prototype = proto,
                RequiresEnlightenment = EnlightenmentRequired.Contains(data.Id),
            };

            if (Lookup.ContainsKey(def.MatchKey))
            {
                result.DuplicateSignatures.Add($"{def.MatchKey} 被 {def.Id} 覆盖（原：{Lookup[def.MatchKey].Id}）");
            }

            Lookup[def.MatchKey] = def;
            AllList.Add(def);
            result.Loaded++;
        }

        LoadResult = result;
        _initialized = true;
        return result;
    }

    public static void EnsureLoaded()
    {
        if (!_initialized)
        {
            Load();
        }
    }

    /// <summary>
    /// 「角度签名 → 用它的图案 id 列表」。
    ///
    /// 给「你画的这条最接近哪个图案」用（见 <see cref="Math.PatternSuggestion"/>）：
    /// 同一个签名可能对应多条图案（只有起始方向不同），所以值是列表。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SignaturesById()
    {
        EnsureLoaded();

        var map = new Dictionary<string, IReadOnlyList<string>>();
        var temp = new Dictionary<string, List<string>>();

        foreach (var def in AllList)
        {
            if (!temp.TryGetValue(def.Angles, out var list))
            {
                list = new List<string>();
                temp[def.Angles] = list;
            }
            list.Add(def.Id);
        }

        foreach (var kv in temp) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>按 id（如 <c>hexcasting:blink</c>）查图案；查不到返回 null。</summary>
    public static PatternDef? FindById(string id)
    {
        EnsureLoaded();
        foreach (var def in AllList)
        {
            if (def.Id == id) { return def; }
        }
        return null;
    }

    public static PatternDef? Match(HexPattern pattern)
    {
        EnsureLoaded();
        return Lookup.TryGetValue(pattern.MatchKey(), out var def) ? def : null;
    }

    /// <summary>
    /// 按角度签名匹配。
    /// 源项目匹配表只以角度序列为键（PatternRegistryManifest.java:92-119），
    /// 起始方向仅用于书中展示，因此这里不接受起始方向参数。
    /// </summary>
    public static PatternDef? Match(string angles)
    {
        EnsureLoaded();
        return Lookup.TryGetValue(angles, out var def) ? def : null;
    }

    /// <summary>判断匹配类型，对应源项目 matchPattern 的返回语义。</summary>
    public static (PatternMatchKind Kind, PatternDef? Def) MatchPattern(HexPattern pattern)
    {
        var def = Match(pattern);
        return def is null ? (PatternMatchKind.Nothing, null) : (PatternMatchKind.Normal, def);
    }

    // ==================== 图案行为（Action）注册 ====================

    private static readonly Dictionary<string, IAction> Actions = new();

    /// <summary>已实现行为的图案数量。</summary>
    public static int RegisteredActionCount => Actions.Count;

    /// <summary>
    /// 为某个图案注册行为。key 用图案 Id（如 "hexcasting:add"）。
    /// 对应源项目 HexActions 里把 Action 塞进注册表的做法。
    /// </summary>
    public static void RegisterAction(string patternId, IAction action)
    {
        Actions[patternId] = action;
    }

    /// <summary>该图案是否已实现行为。</summary>
    public static bool HasAction(PatternDef def) => Actions.ContainsKey(def.Id);

    /// <summary>取某个图案的行为。</summary>
    public static bool TryGetAction(PatternDef def, out IAction? action)
    {
        if (Actions.TryGetValue(def.Id, out var found))
        {
            action = found;
            return true;
        }
        action = null;
        return false;
    }

    /// <summary>清空已注册的行为（模组卸载时调用）。</summary>
    public static void ClearActions() => Actions.Clear();
}
