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
        // 注意：这份清单**逐条抄自源项目** `datagen/tag/HexActionTagProvider.java` 里
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

    /// <summary>
    /// 「每个世界笔顺不同」的图案（源项目标签 hexcasting:per_world_pattern，与上面 14 个大法术是同一批）。
    /// 它们**不进普通查找表**：标准笔顺在原版里根本不被识别，只认本世界的笔顺（由世界种子生成，
    /// 形状与标准图案相同、笔顺不同，见 <see cref="EulerPathFinder"/>）。玩家要从古卷里学本世界的画法。
    /// 没有世界时（离线测试、主菜单）本世界表就是标准笔顺。
    /// </summary>
    public static readonly IReadOnlySet<string> PerWorldIds = new HashSet<string>
    {
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

    public static bool IsPerWorld(PatternDef def) => PerWorldIds.Contains(def.Id);

    /// <summary>本世界的笔顺：签名 → 图案定义。</summary>
    private static readonly Dictionary<string, PatternDef> PerWorldLookup = new();

    /// <summary>本世界的笔顺：图案 id → 本世界的画法（起始方向 + 角度）。</summary>
    private static readonly Dictionary<string, HexPattern> PerWorldById = new();

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

            AllList.Add(def);
            if (IsPerWorld(def))
            {
                result.Loaded++;
                continue;   // 每个世界的图案不进普通查找表（源项目 processRegistry 同样跳过它们）
            }

            if (Lookup.ContainsKey(def.MatchKey))
            {
                result.DuplicateSignatures.Add($"{def.MatchKey} 被 {def.Id} 覆盖（原：{Lookup[def.MatchKey].Id}）");
            }

            Lookup[def.MatchKey] = def;
            result.Loaded++;
        }

        LoadResult = result;
        _initialized = true;
        RebuildAddonLookup();   // 本体表重建后，附属与本体撞车的判断要重算
        ResetPerWorldToCanonical();
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
        foreach (var defs in AddonDeclared.Values)
        {
            foreach (var def in defs)
            {
                if (def.Id == id) { return def; }
            }
        }
        return null;
    }

    public static PatternDef? Match(HexPattern pattern) => Match(pattern.MatchKey());

    // ── 附属的图案 ─────────────────────────────────────────────────
    //
    // 附属图案不进 AllList（本体「188 条」的统计、收口用例、书都不受影响），单独登记：
    //   - 声明（DeclareAddonPatterns）：不管附属开没开都声明 —— 每个世界的大法术笔顺要避开**全部**附属图案，
    //     否则附属关着时建的世界可能生成一条和附属图案撞车的笔顺（ADDONS.md「开关的实际效果」）。
    //   - 启用（SetAddonEnabled）：只有开着的附属参与识别；关着 = 画出来是无效图案，和原版没装这个附属一样。
    // 查表顺序同源项目：普通图案（本体在前、附属在后）→ 本世界的大法术 → 特殊图案（调用方处理）。

    private static readonly Dictionary<string, List<PatternDef>> AddonDeclared = new();
    private static readonly HashSet<string> AddonEnabled = new();
    private static readonly Dictionary<string, PatternDef> AddonLookup = new();

    /// <summary>声明一个附属的全部图案（重复声明会覆盖）。签名必须能画出来，否则抛异常 —— 这是编译期数据，错了就该立刻暴露。</summary>
    public static void DeclareAddonPatterns(string addonId, IEnumerable<PatternData> patterns)
    {
        var defs = new List<PatternDef>();
        foreach (var data in patterns)
        {
            if (!HexPattern.TryFromAnglesUnchecked(data.Angles, data.StartDir, out var proto, out var err) || proto is null)
            {
                throw new System.ArgumentException($"附属 {addonId} 的图案 {data.Id} 画不出来：{err}");
            }
            defs.Add(new PatternDef { Id = data.Id, Angles = data.Angles, StartDir = data.StartDir, Op = data.Op, Prototype = proto });
        }
        AddonDeclared[addonId] = defs;
        RebuildAddonLookup();
    }

    /// <summary>打开 / 关掉一个附属的图案识别。</summary>
    public static void SetAddonEnabled(string addonId, bool enabled)
    {
        if (enabled) AddonEnabled.Add(addonId); else AddonEnabled.Remove(addonId);
        RebuildAddonLookup();
    }

    /// <summary>清掉全部附属登记（模组卸载 / 离线测试用）。</summary>
    public static void ClearAddons()
    {
        AddonDeclared.Clear();
        AddonEnabled.Clear();
        AddonLookup.Clear();
    }

    /// <summary>某个附属声明的图案（没声明返回空表）。</summary>
    public static IReadOnlyList<PatternDef> AddonPatterns(string addonId)
        => AddonDeclared.TryGetValue(addonId, out var defs) ? defs : System.Array.Empty<PatternDef>();

    public static bool IsAddonEnabled(string addonId) => AddonEnabled.Contains(addonId);

    /// <summary>附属图案和本体 / 别的附属撞了签名的记录（启用时发现），供加载日志排查。</summary>
    public static IReadOnlyList<string> AddonConflicts => _addonConflicts;
    private static readonly List<string> _addonConflicts = new();

    private static void RebuildAddonLookup()
    {
        AddonLookup.Clear();
        _addonConflicts.Clear();
        foreach (var (addonId, defs) in AddonDeclared)
        {
            if (!AddonEnabled.Contains(addonId)) continue;
            foreach (var def in defs)
            {
                string key = def.MatchKey;
                if (Lookup.TryGetValue(key, out var baseDef) || AddonLookup.TryGetValue(key, out baseDef))
                {
                    _addonConflicts.Add($"{def.Id} 与 {baseDef.Id} 签名相同（{key}），保留 {baseDef.Id}");
                    continue;
                }
                AddonLookup[key] = def;
            }
        }
    }

    // ── 每个世界的笔顺 ─────────────────────────────────────────────

    /// <summary>没有世界时：本世界表 = 标准笔顺。</summary>
    public static void ResetPerWorldToCanonical()
    {
        EnsureLoaded();
        var table = new Dictionary<string, HexPattern>();
        foreach (var def in AllList)
        {
            if (IsPerWorld(def)) table[def.Id] = def.Prototype;
        }
        SetPerWorld(table);
    }

    /// <summary>设置本世界的笔顺（id → 画法）。世界存档 / 联机同步时调用。</summary>
    public static void SetPerWorld(IReadOnlyDictionary<string, HexPattern> table)
    {
        PerWorldLookup.Clear();
        PerWorldById.Clear();
        foreach (var (id, pattern) in table)
        {
            var def = FindById(id);
            if (def is null || !IsPerWorld(def)) continue;
            PerWorldLookup[pattern.MatchKey()] = def;
            PerWorldById[id] = pattern;
        }
    }

    /// <summary>
    /// 源项目 ScrungledPatternsSave.createFromScratch(seed)：每个大法术用 EulerPathFinder 从标准图案 + 世界种子生成本世界的笔顺。
    /// 另加一条保护：生成的签名不能与普通图案、数字、掩码撞车（源项目注释写着「没有撞车保护，别那么做」），
    /// 撞了就换一条路径 —— 否则那个大法术在这个世界里会永远画不出来。
    /// </summary>
    public static Dictionary<string, HexPattern> GeneratePerWorld(long seed)
    {
        EnsureLoaded();
        var table = new Dictionary<string, HexPattern>();
        var taken = new HashSet<string>();
        // 全部附属图案（开没开都算）也当作已占用，见上面「附属的图案」
        foreach (var defs in AddonDeclared.Values)
        {
            foreach (var def in defs) taken.Add(def.MatchKey);
        }
        foreach (var def in AllList)
        {
            if (!IsPerWorld(def)) continue;
            var pat = EulerPathFinder.FindAltDrawing(def.Prototype, seed, p =>
            {
                string sig = p.AnglesSignature();
                return !Lookup.ContainsKey(sig) && !taken.Contains(sig)
                    && !SpecialPatterns.TryNumber(sig, out _) && !SpecialPatterns.TryMask(p, out _);
            });
            taken.Add(pat.AnglesSignature());
            table[def.Id] = pat;
        }
        return table;
    }

    /// <summary>这个大法术在本世界的画法（不是大法术则是标准画法）。</summary>
    public static HexPattern PatternInThisWorld(PatternDef def)
        => IsPerWorld(def) && PerWorldById.TryGetValue(def.Id, out var p) ? p : def.Prototype;

    /// <summary>本世界的全部大法术画法（存档用）。</summary>
    public static IReadOnlyDictionary<string, HexPattern> PerWorldTable => PerWorldById;

    /// <summary>
    /// 按角度签名匹配。
    /// 源项目匹配表只以角度序列为键（PatternRegistryManifest.java:92-119），
    /// 起始方向仅用于书中展示，因此这里不接受起始方向参数。
    /// </summary>
    public static PatternDef? Match(string angles)
    {
        EnsureLoaded();
        // 源项目 matchPattern：先普通图案，再本世界的大法术（特殊图案在更后面，由调用方处理）
        if (Lookup.TryGetValue(angles, out var def)) return def;
        if (AddonLookup.TryGetValue(angles, out var addonDef)) return addonDef;
        return PerWorldLookup.TryGetValue(angles, out var pw) ? pw : null;
    }

    /// <summary>判断匹配类型，对应源项目 matchPattern 的返回语义。</summary>
    public static (PatternMatchKind Kind, PatternDef? Def) MatchPattern(HexPattern pattern)
    {
        var def = Match(pattern);
        return def is null ? (PatternMatchKind.Nothing, null)
            : (IsPerWorld(def) ? PatternMatchKind.PerWorld : PatternMatchKind.Normal, def);
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
