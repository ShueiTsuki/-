using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

/// <summary>栈上的参数不够。栈不变。</summary>
public sealed class MishapNotEnoughArgs : Mishap
{
    public int Expected { get; }
    public int Got { get; }

    public MishapNotEnoughArgs(int expected, int got)
        : base($"not_enough_args: expected {expected}, got {got}")
    {
        Expected = expected;
        Got = got;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变（源项目同）
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"需要 {Expected} 个参数，但栈上只有 {Got} 个";
}

/// <summary>媒质不足且无法过载。栈不变。</summary>
public sealed class MishapNotEnoughMedia : Mishap
{
    public long Cost { get; }

    public MishapNotEnoughMedia(long cost) : base($"not_enough_media: {cost}")
    {
        Cost = cost;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "媒质不足，且无法过载";
}

/// <summary>画了一个不在注册表里的图案。解析状态为 INVALID。</summary>
public sealed class MishapInvalidPattern : Mishap
{
    public HexPattern? Pattern { get; }

    public MishapInvalidPattern(HexPattern? pattern) : base("invalid_pattern")
    {
        Pattern = pattern;
    }

    public override ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Invalid;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    /// <summary>
    /// ⚠️ 这条文案要**告诉玩家画的是什么、该往哪改**。
    ///
    /// 只写「这不是一个有效的图案」，玩家能做的只有反复猜。
    /// 所以这里报三件事：识别出的角度串（能对着书逐笔比对）、
    /// 最接近的图案名、差几笔。
    ///
    /// 文案来自 <see cref="PatternSuggestion.Describe"/> —— 画布 HUD 用的是同一个函数，
    /// 免得同一件事在两处说法不一致。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
    {
        if (Pattern == null)
        {
            return "这不是一个有效的图案";
        }

        string signature = Pattern.AnglesSignature();
        string head = $"这不是一个有效的图案（识别到 [{Pattern.StartDir} {signature}]）";

        string? hint = PatternSuggestion.Describe(signature);
        return hint == null ? head : $"{head}\n{hint}";
    }
}

/// <summary>未转义的裸 iota 被直接执行。栈不变（源项目此处是 TODO）。</summary>
public sealed class MishapUnescapedValue : Mishap
{
    public Iota Perpetrator { get; }

    public MishapUnescapedValue(Iota perpetrator) : base("unescaped_value")
    {
        Perpetrator = perpetrator;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目此处为 TODO（不修改栈）
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"值 {Perpetrator.TypeName} 未被转义";
}

/// <summary>栈过大（序列化上限）。清空栈并放入一个垃圾 iota。</summary>
public sealed class MishapStackSize : Mishap
{
    public MishapStackSize() : base("stack_size") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        stack.Clear();
        stack.Add(GarbageIota.Instance);
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "栈过大，已被清空";
}

/// <summary>求值步数超出上限（对应 EvalTooMuch）。</summary>
public sealed class MishapEvalTooMuch : Mishap
{
    public MishapEvalTooMuch() : base("eval_too_much") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "算力耗尽（求值步数超出上限）";
}

/// <summary>内部异常被包装成 mishap。</summary>
public sealed class MishapInternalException : Mishap
{
    public Exception Exception { get; }

    public MishapInternalException(Exception exception) : base("internal_exception")
    {
        Exception = exception;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"内部错误：{Exception.Message}";
}

/// <summary>需要「启蒙」才能使用该图案（大法术）。</summary>
public sealed class MishapUnenlightened : Mishap
{
    public MishapUnenlightened() : base("unenlightened") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "你尚未启蒙，无法施放此法术";
}

/// <summary>
/// 除零（也用于负数开分数次幂、tan(π/2)、log 定义域外等情形）。
/// 源项目里这类 mishap 会让施法者受到伤害；伤害效果等接世界层时再补。
/// </summary>
public sealed class MishapDivideByZero : Mishap
{
    public double A { get; }
    public double B { get; }
    public string Role { get; }

    public MishapDivideByZero(double a, double b, string role = "divisor") : base("divide_by_zero")
    {
        A = a;
        B = b;
        Role = role;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变。（源项目此处让施法者掉血，等接世界层时补）
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"除以零（{Role}：{B:0.####}）";
}

/// <summary>运算符的操作数类型不受支持。</summary>
public sealed class MishapInvalidOperatorArgs : Mishap
{
    public string Op { get; }
    public string ArgTypes { get; }

    public MishapInvalidOperatorArgs(string op, string argTypes) : base("invalid_operator_args")
    {
        Op = op;
        ArgTypes = argTypes;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"运算符「{Op}」不支持这些操作数类型：{ArgTypes}";
}

/// <summary>
/// 某个槽位收到了类型不对的 iota（例如 construct_vec 收到 bool）。
/// 源项目：MishapInvalidIota。
/// </summary>
public sealed class MishapInvalidIota : Mishap
{
    public Iota Perpetrator { get; }
    public string Expected { get; }

    public MishapInvalidIota(Iota perpetrator, string expected) : base("invalid_iota")
    {
        Perpetrator = perpetrator;
        Expected = expected;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"此处需要 {Expected}，但收到 {Perpetrator.TypeName}";
}

/// <summary>
/// 图案已注册，但**行为尚未实现**（开发中状态）。
///
/// 必须与 MishapInvalidPattern 区分开：
///   - 无效图案 = 这个签名根本不在 188 条注册表里（玩家画错了）
///   - 未实现   = 签名是对的、能找到图案，只是我们还没写它的行为
/// 两者混在一起会让玩家以为是自己画错了。
/// </summary>
public sealed class MishapNotImplemented : Mishap
{
    public string PatternId { get; }

    public MishapNotImplemented(string patternId) : base("not_implemented")
    {
        PatternId = patternId;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"图案 {PatternId} 已识别，但其行为尚未实现（开发中）";
}

/// <summary>
/// 画了闭括号但当前没在构建列表。
/// 对应源项目 MishapNeedsParens（OpCloseParen 在括号外执行时抛出）。
/// </summary>
public sealed class MishapNeedsParens : Mishap
{
    public MishapNeedsParens() : base("needs_parens") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "没有正在构建的列表，闭括号无处可关";
}

/// <summary>
/// 当前施法环境**不支持世界访问**。
///
/// 这是移植侧特有的 mishap，原版没有：原版所有施法都发生在世界里，
/// 而我们的 <see cref="CastingEnvironment"/> 可以是纯 VM 环境（离线测试）。
/// 世界图案在这种环境下必须**明确报错**，而不是静默返回零向量 ——
/// 后者会一路变成 NaN 静默故障。
/// </summary>
public sealed class MishapNoWorld : Mishap
{
    public MishapNoWorld() : base("no_world") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "当前施法环境不支持世界访问（世界类图案无法在此使用）";
}

/// <summary>
/// 目标实体超出施法范围。
/// 移植自源项目 MishapEntityTooFarAway。
///
/// 原作效果是「把手中物品朝目标实体扔出去」（yeetHeldItemsTowards）——
/// 那依赖 MC 的物品/NBT 体系。泰拉侧暂为空实现，
/// 等 P2-3（iota 存储与手持物品体系）落地后再补等效表现。
/// </summary>
public sealed class MishapEntityTooFarAway : Mishap
{
    private readonly EntityIota _entity;

    public MishapEntityTooFarAway(EntityIota entity) : base("entity_too_far")
    {
        _entity = entity;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 原作：env.mishapEnvironment.yeetHeldItemsTowards(entity.position())
        // 泰拉侧暂无手持 iota 物品体系，留空。
    }

    /// <summary>实体描述（EntityIota.DescribeValue 是 protected，这里自己拼）。</summary>
    public string EntityText => $"{_entity.Target}#{_entity.Index}";

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"目标实体 {EntityText} 超出施法范围";
}

/// <summary>
/// 目标**坐标**超出施法范围。
/// 移植自源项目 MishapLocationTooFarAway（`assertVecInRange` 抛出的那个）。
///
/// 与 <see cref="MishapEntityTooFarAway"/> 的区别：
/// 前者校验「一个点」（射线起点、法术落点），后者校验「一个实体」。
/// 源项目里两者是分开的 mishap，消息与表现都不同，不要合并。
/// </summary>
public sealed class MishapLocationTooFarAway : Mishap
{
    private readonly double _x;
    private readonly double _y;

    public MishapLocationTooFarAway(double x, double y) : base("location_too_far")
    {
        _x = x;
        _y = y;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"坐标 ({_x:0.##}, {_y:0.##}) 超出施法范围";
}

/// <summary>
/// 目标位置不可用（世界外、维度不允许传送等）。
/// 移植自源项目 MishapBadLocation。
///
/// 与 <see cref="MishapLocationTooFarAway"/> 的区别：
/// 后者是「超出施法范围」（可能只是站远了），
/// 这个是「这个位置根本不能去」（世界外 / 会掉出地图），语义完全不同，不要合并。
/// </summary>
public sealed class MishapBadLocation : Mishap
{
    private readonly double _x;
    private readonly double _y;
    private readonly string _reason;

    public MishapBadLocation(double x, double y, string reason) : base("bad_location")
    {
        _x = x;
        _y = y;
        _reason = reason;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"({_x:0.##}, {_y:0.##}) 这个位置不能去：{_reason}";
}

/// <summary>
/// 手上没有可用的数据载体。
/// 移植自源项目 `MishapBadOffhandItem`（那边查的是副手，泰拉侧查手持）。
///
/// 触发场景：用了 `read_into_parens`，但手上没拿着聚念核心之类的可读物品。
/// 消息必须说清楚「该拿什么」，否则玩家只会觉得这个图案坏了。
/// </summary>
public sealed class MishapBadHeldItem : Mishap
{
    public MishapBadHeldItem() : base("bad_held_item") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "手上没有可读取的物品（需要拿一个能存 iota 的东西，例如聚念核心）";
}

/// <summary>
/// 目标实体身上没有需要的那种数据载体。
/// 移植自源项目 `MishapBadEntity`（`read/entity` / `write/entity` 用）。
///
/// 消息里必须写清「这个实体不是载体」而不是「实体无效」——
/// 玩家指着一只史莱姆说「读它」，需要知道的是「史莱姆不存东西」。
/// </summary>
public sealed class MishapBadEntity : Mishap
{
    private readonly EntityIota _entity;

    public MishapBadEntity(EntityIota entity, string expected) : base("bad_entity")
    {
        _entity = entity;
        Expected = expected;
    }

    public string Expected { get; }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"实体 {_entity.Target}#{_entity.Index} 不是{Expected}";
}

/// <summary>
/// 目标掉落物不满足要求（不是媒质物品之类）。
/// 移植自源项目 `MishapBadItem`（`recharge` 与打包法术用）。
/// </summary>
public sealed class MishapBadItem : Mishap
{
    private readonly EntityIota _entity;

    public MishapBadItem(EntityIota entity, string expected) : base("bad_item")
    {
        _entity = entity;
        Expected = expected;
    }

    public string Expected { get; }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"掉落物 {_entity.Index} 不是{Expected}";
}

/// <summary>
/// 脑叶切除失败：这个生物配不上这个方块。
/// 移植自源项目 `MishapBadBrainsweep`。
///
/// 消息里必须同时给出「哪只」与「哪种方块」—— 因为失败的原因永远是**组合不对**，
/// 只说「不能切除」的话玩家会去换生物、换方块，试很多次才发现是配对的问题。
/// </summary>
public sealed class MishapBadBrainsweep : Mishap
{
    private readonly EntityIota _entity;
    private readonly double _x;
    private readonly double _y;

    public MishapBadBrainsweep(EntityIota entity, double x, double y) : base("bad_brainsweep")
    {
        _entity = entity;
        _x = x;
        _y = y;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"没法把 {_entity.Target}#{_entity.Index} 切在 ({_x:0.##}, {_y:0.##}) 这块方块上";
}

/// <summary>
/// 这只生物已经被切除过了。移植自源项目 `MishapAlreadyBrainswept`。
///
/// 必须与 <see cref="MishapBadBrainsweep"/> 分开：一个是「配不上」，
/// 一个是「已经切过了」—— 后者说明玩家配方是对的，只是找错了对象。
/// </summary>
public sealed class MishapAlreadyBrainswept : Mishap
{
    private readonly EntityIota _entity;

    public MishapAlreadyBrainswept(EntityIota entity) : base("already_brainswept")
    {
        _entity = entity;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{_entity.Target}#{_entity.Index} 已经被切除过了";
}

/// <summary>
/// 目标位置不是阿卡夏记录方块。
/// 移植自源项目 `MishapNoAkashicRecord`。
///
/// 消息里带上坐标很重要 —— 玩家常常是「记错了位置」而不是「不知道要用记录方块」，
/// 报出坐标能让 TA 一眼看出自己指到哪去了。
/// </summary>
public sealed class MishapNoAkashicRecord : Mishap
{
    private readonly double _x;
    private readonly double _y;

    public MishapNoAkashicRecord(double x, double y) : base("no_akashic_record")
    {
        _x = x;
        _y = y;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"({_x:0.##}, {_y:0.##}) 这里没有阿卡夏记录方块";
}

/// <summary>
/// 不在法术环里却用了 `circle/*` 图案。
/// 移植自源项目 `MishapNoSpellCircle`。
///
/// 这三个图案只有在法术环中执行时才有意义 ——
/// 用玩家法杖画它们应当明确报错，而不是返回一个「零坐标」之类的假值。
/// </summary>
public sealed class MishapNoSpellCircle : Mishap
{
    public MishapNoSpellCircle() : base("no_spell_circle") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "这个图案只能在法术环里使用";
}

/// <summary>
/// 目标方块不满足条件（不可替换 / 不可挖等）。
/// 移植自源项目 `MishapBadBlock`。
///
/// 消息里带坐标与原因：玩家指着一个位置施法失败时，
/// 最需要知道的就是「为什么这一格不行」。
/// </summary>
public sealed class MishapBadBlock : Mishap
{
    private readonly double _x;
    private readonly double _y;
    private readonly string _reason;

    public MishapBadBlock(double x, double y, string reason) : base("bad_block")
    {
        _x = x;
        _y = y;
        _reason = reason;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 栈不变
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"({_x:0.##}, {_y:0.##}) 这一格不行：{_reason}";
}