using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Dev;

/// <summary>法术里的一步：一条具名图案，或一个数字字面量（数字图案 aqaa… 由 <see cref="SpecialPatterns.EncodeNumber"/> 生成）。</summary>
public readonly record struct HexStep(string? PatternId, double Number)
{
    public static HexStep P(string shortId) => new("hexcasting:" + shortId, 0);
    public static HexStep N(double value) => new(null, value);

    public bool IsNumber => PatternId is null;

    /// <summary>面板上显示的名字（图案中文名 / 「数字 3」）。</summary>
    public string Label
    {
        get
        {
            if (IsNumber) { return "数字 " + Number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
            var def = PatternRegistry.FindById(PatternId!);
            return def is null ? PatternId! : def.DisplayName();
        }
    }

    /// <summary>
    /// 玩家画的这条算不算「这一步」：具名图案看识别结果（起始方向随意，和识别一致），
    /// 数字看数值（同一个数有很多种画法，都算）。
    /// </summary>
    public bool Matches(HexPattern drawn)
    {
        if (IsNumber)
        {
            return SpecialPatterns.TryNumber(drawn.AnglesSignature(), out var v) && System.Math.Abs(v - Number) < 1e-9;
        }
        return PatternRegistry.Match(drawn)?.Id == PatternId;
    }

    /// <summary>这一步要画的图案；id 不存在 / 数字编不出来时返回 null。</summary>
    public HexPattern? ToPattern()
    {
        if (!IsNumber)
        {
            // 大法术取**本世界**的笔顺（每个世界不同，见 PatternRegistry.PerWorldIds）
            var def = PatternRegistry.FindById(PatternId!);
            return def is null ? null : PatternRegistry.PatternInThisWorld(def);
        }
        string? sig = SpecialPatterns.EncodeNumber(Number);
        if (sig is null) { return null; }
        // 数字图案匹配只看签名（起始方向随意）；取原版书里数字页的 SOUTH_EAST
        return HexPattern.TryFromAngles(sig, HexDir.SouthEast, out var p, out _) ? p : null;
    }
}

/// <summary>一条完整的示例法术。</summary>
public sealed class SampleHex
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required HexStep[] Steps { get; init; }

    /// <summary>含大法术（要启蒙；没启蒙时强行施放会触发「盲目绘制」）。</summary>
    public bool Great { get; init; }
}

/// <summary>
/// 开发者面板里的「法术示例」：不知道该放什么的时候，从这里挑一条，照着画或者一键施放。
///
/// 每条都在 tests/vmtest 里用假世界真跑过一遍（结果必须是「求值成功」且栈清空），
/// 所以这里的步骤顺序是对的 —— 改动时跑一下测试。
///
/// 约定：「视线命中」= 从眼睛沿视线（泰拉里是鼠标方向）打射线，命中的方块。
/// </summary>
public static class SampleHexes
{
    private static HexStep P(string id) => HexStep.P(id);
    private static HexStep N(double v) => HexStep.N(v);

    /// <summary>眼睛位置、视线方向：两条射线图案的共同前缀。</summary>
    private static readonly HexStep[] EyeAndLook =
    {
        P("get_caster"), P("entity_pos/eye"), P("get_caster"), P("get_entity_look"),
    };

    /// <summary>视线命中的方块坐标。</summary>
    private static HexStep[] LookedBlock() => Concat(EyeAndLook, P("raycast"));

    /// <summary>视线命中方块**贴着的那一格**（命中格 + 命中面法线）—— 放东西用。</summary>
    private static HexStep[] LookedFace() => Concat(EyeAndLook,
        P("2dup"), P("raycast"), P("rotate"), P("rotate"), P("raycast/axis"), P("add"));

    private static HexStep[] Concat(HexStep[] head, params HexStep[] tail)
    {
        var list = new List<HexStep>(head);
        list.AddRange(tail);
        return list.ToArray();
    }

    public static readonly IReadOnlyList<SampleHex> All = new[]
    {
        new SampleHex
        {
            Name = "向前冲", Description = "朝鼠标方向推自己一把（驱动）。",
            Steps = new[] { P("get_caster"), P("get_caster"), P("get_entity_look"), N(2), P("mul"), P("add_motion") },
        },
        new SampleHex
        {
            Name = "闪现", Description = "朝鼠标方向瞬移 4 格。",
            Steps = new[] { P("get_caster"), N(4), P("blink") },
        },
        new SampleHex
        {
            Name = "挖掉指着的方块", Description = "破坏视线命中的方块（射线 + 破坏方块）。",
            Steps = Concat(LookedBlock(), P("break_block")),
        },
        new SampleHex
        {
            Name = "构筑方块", Description = "在指着的方块表面凭空造一块会消失的魔法方块。",
            Steps = Concat(LookedFace(), P("conjure_block")),
        },
        new SampleHex
        {
            Name = "构筑光源", Description = "在指着的方块表面放一个魔法光源。",
            Steps = Concat(LookedFace(), P("conjure_light")),
        },
        new SampleHex
        {
            Name = "放水", Description = "在指着的方块表面造一格水。",
            Steps = Concat(LookedFace(), P("create_water")),
        },
        new SampleHex
        {
            Name = "催生", Description = "让指着的树苗 / 草药 / 草长大。",
            Steps = Concat(LookedBlock(), P("bonemeal")),
        },
        new SampleHex
        {
            Name = "爆炸", Description = "在指着的方块处引爆，威力 3（离远点）。",
            Steps = Concat(LookedBlock(), N(3), P("explode")),
        },
        new SampleHex
        {
            Name = "火球", Description = "同上，带火。",
            Steps = Concat(LookedBlock(), N(3), P("explode/fire")),
        },
        new SampleHex
        {
            Name = "召唤哨卫", Description = "在指着的方块处放一个哨卫（坐标书签，之后可以寻路过去）。",
            Steps = Concat(LookedBlock(), P("sentinel/create")),
        },
        new SampleHex
        {
            Name = "漂浮", Description = "给自己 10 秒漂浮。",
            Steps = new[] { P("get_caster"), N(10), P("potion/levitation") },
        },
        new SampleHex
        {
            Name = "换法术颜色", Description = "内化染色剂：把染色剂拿在手上或放在快捷栏中手持物品右边一格，施放后火花和哨卫换成它的颜色，染色剂被消耗。",
            Steps = new[] { P("colorize") },
        },
        new SampleHex
        {
            Name = "召雷", Description = "大法术：往指着的地方劈一道闪电。", Great = true,
            Steps = Concat(LookedBlock(), P("lightning")),
        },
        new SampleHex
        {
            Name = "翱翔", Description = "大法术：获得一段飞行，落地结束。", Great = true,
            Steps = new[] { P("get_caster"), P("flight") },
        },
        new SampleHex
        {
            Name = "再生", Description = "大法术：再生 10 秒，效力 1。", Great = true,
            Steps = new[] { P("get_caster"), N(10), N(1), P("potion/regeneration") },
        },
        new SampleHex
        {
            Name = "卓越传送", Description = "大法术：朝鼠标方向传送 30 格。", Great = true,
            Steps = new[] { P("get_caster"), P("get_caster"), P("get_entity_look"), N(30), P("mul"), P("teleport/great") },
        },
        new SampleHex
        {
            Name = "召雨", Description = "大法术：让天下雨。", Great = true,
            Steps = new[] { P("summon_rain") },
        },
    };
}
