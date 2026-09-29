using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

/// <summary>栈上的参数不够。惩罚：把缺的那几个补成垃圾值（源项目同）。</summary>
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
        // 源项目：repeat(expected - got) { stack.add(GarbageIota()) }（这里曾写「栈不变（源项目同）」）
        for (int i = Got; i < Expected; i++) stack.Add(GarbageIota.Instance);
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
        // 源项目：env.extractMedia(cost, false) —— 付不起也要把能付的都抽走
        env.ExtractMedia(Cost, simulate: false);
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
        // 源项目：stack.add(GarbageIota())
        stack.Add(GarbageIota.Instance);
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
        // 源项目：env.mishapEnvironment.drown()
        env.MishapDrown();
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
        // 源项目：NO-OP
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"内部错误：{Exception.Message}";
}

/// <summary>
/// 需要「启蒙」才能使用该图案（大法术）。对应原版 MishapUnenlightened.kt：
/// 丢下手上的东西、提示「法术没起效」、触发 FAIL_GREAT_SPELL（→ 解锁过载），图案判为无效。
/// </summary>
public sealed class MishapUnenlightened : Mishap
{
    public MishapUnenlightened() : base("unenlightened") { }

    public override ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Invalid;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        env.DropHeldItems();
        env.OnFailedGreatSpell();
    }

    /// <summary>原版文本 hexcasting.message.cant_great_spell（官方中文）。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "奇怪，法术没起效……也许我还不够熟练？";
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
        // 源项目：stack.add(GarbageIota()); env.mishapEnvironment.damage(0.5f) —— 扣掉当前生命的一半
        stack.Add(GarbageIota.Instance);
        env.MishapDamage(0.5);
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"除以零（{Role}：{B:0.####}）";
}

/// <summary>运算符的操作数类型不受支持。</summary>
public sealed class MishapInvalidOperatorArgs : Mishap
{
    public string Op { get; }
    public string ArgTypes { get; }

    /// <summary>参与运算的参数个数（惩罚时从栈顶换掉这么多个）。</summary>
    public int ArgCount { get; }

    public MishapInvalidOperatorArgs(string op, string argTypes, int argCount = 0) : base("invalid_operator_args")
    {
        Op = op;
        ArgTypes = argTypes;
        ArgCount = argCount;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：把参与运算的每个参数都换成垃圾值（栈顶往下 perpetrators.size 个）
        for (int i = 0; i < ArgCount && i < stack.Count; i++)
        {
            stack[stack.Count - 1 - i] = GarbageIota.Instance;
        }
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

    /// <summary>出错参数离栈顶多远（0 = 栈顶）。不给时按引用在栈上找（参数就是栈上那个对象）。</summary>
    public int? ReverseIdx { get; init; }

    private static int LocateFromTop(List<Iota> stack, Iota target)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(stack[i], target)) return stack.Count - 1 - i;
        }
        return -1;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：stack[size - 1 - reverseIdx] = GarbageIota() —— 把出错的那个参数换成垃圾值
        int idx = ReverseIdx ?? LocateFromTop(stack, Perpetrator);
        if (idx >= 0 && idx < stack.Count)
        {
            stack[stack.Count - 1 - idx] = GarbageIota.Instance;
        }
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
        // 移植版特有（原版没有「未实现」）：不改栈
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
        // 源项目：把出错的图案压回栈（if (errorCtx.pattern != null) stack.add(PatternIota(pattern))）
        if (errorCtx.Pattern != null) stack.Add(new PatternIota(errorCtx.Pattern));
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
        // 移植版特有（离线环境没有世界）：不改栈
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
        // 源项目：yeetHeldItemsTowards(entity.position())
        if (env.World is { } w) { var (x, y) = w.FeetPosition(_entity); env.YeetHeldItemsTowards(x, y); }
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
        // 源项目（1.20 里是 MishapBadLocation "too_far"）：yeetHeldItemsTowards(location)
        env.YeetHeldItemsTowards(_x, _y);
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
        // 源项目：yeetHeldItemsTowards(location)
        env.YeetHeldItemsTowards(_x, _y);
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"({_x:0.##}, {_y:0.##}) 这个位置不能去：{_reason}";
}

/// <summary>
/// 「另一只手」里没有需要的东西。移植自源项目 `MishapBadOffhandItem`。
///
/// 泰拉没有副手：「另一只手」= 快捷栏里施法物品右边那一格（见 PlayerCastingEnvironment.PrimarySlots），
/// 其次才是手上拿着的。消息照原版的几种说法（bad_item.offhand + 各 key 的中文）。
/// </summary>
public sealed class MishapBadHeldItem : Mishap
{
    /// <summary>原版 MishapBadOffhandItem 的 wanted key。</summary>
    public enum Need { Storage, Read, Write, ReadOnly, Eraseable, Colorizer, Variant, Bottle, OnlyOne, Rechargeable }

    private readonly Need? _need;
    private readonly Iota? _datum;
    private readonly string? _desc;

    public MishapBadHeldItem(Need need, Iota? datum = null) : base("bad_held_item")
    {
        _need = need;
        _datum = datum;
    }

    /// <summary>直接给出「需要什么」（原版 craft/* 用物品名，例如「一张空的符纸」）。</summary>
    public MishapBadHeldItem(string wanted) : base("bad_held_item") => _desc = wanted;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目 MishapBadOffhandItem：dropHeldItems()
        env.DropHeldItems();
    }

    private string Wanted => _desc ?? _need switch
    {
        Need.Read => "一个可以读出iota的地方",
        Need.Write => "一个可以写入iota的地方",
        Need.ReadOnly => $"一个能够接受{_datum}的地方",
        Need.Eraseable => "一个可清除的物品",
        Need.Colorizer => "一个染色剂",
        Need.Variant => "一个有变种的物品",
        Need.Bottle => "一个玻璃瓶",
        Need.OnlyOne => "仅一个物品",
        Need.Rechargeable => "一个可重新充能的物品",
        _ => "一个可以存储iota的地方",
    };

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"需要在另一只手里持有{Wanted}（泰拉：快捷栏里施法物品右边那一格，或者手上）";
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
        // 源项目：yeetHeldItemsTowards(entity.position())
        if (env.World is { } w) { var (x, y) = w.FeetPosition(_entity); env.YeetHeldItemsTowards(x, y); }
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
        // 源项目：那个掉落物往上弹一下
        env.World?.MishapLaunchItem(_entity);
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
        // 源项目：trulyHurt(mob, 1f)
        env.World?.MishapHurtEntity(_entity, kill: false);
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
        // 源项目：mob.hurt(..., mob.health) —— 直接杀死
        env.World?.MishapHurtEntity(_entity, kill: true);
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
        // 源项目：removeXp(100)。泰拉没有经验值 → 环境默认不做
        env.MishapRemoveXp(100);
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
        // 源项目：把施法者整个背包（含盔甲，绑定诅咒除外）掉出来
        env.MishapDropInventory();
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
        // 源项目：world.explode(null, pos + 0.5, 0.25f, ExplosionInteraction.NONE) —— 小爆炸、不破坏方块
        env.World?.MishapExplosion(System.Math.Floor(_x) + 0.5, System.Math.Floor(_y) + 0.5);
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"({_x:0.##}, {_y:0.##}) 这一格不行：{_reason}";
}

/// <summary>
/// 目标免疫这个法术（原版 MishapImmuneEntity：tag cannot_teleport 之类）。
/// 泰拉侧：Boss 与 Boss 的身体部件不能被闪现 / 传送。
/// 惩罚：手持物品甩向那个实体。
/// </summary>
public sealed class MishapImmuneEntity : Mishap
{
    private readonly EntityIota _entity;

    public MishapImmuneEntity(EntityIota entity) : base("immune_entity")
    {
        _entity = entity;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        if (env.World is { } w) { var (x, y) = w.FeetPosition(_entity); env.YeetHeldItemsTowards(x, y); }
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{_entity.Target}#{_entity.Index} 不受这个法术影响";
}

/// <summary>
/// 试图把**别的玩家**的实体引用写进物品 / 阿卡夏 / 打包法术（原版 MishapOthersName —— 保护「真名」）。
/// 惩罚：失明，写的是自己 5 秒，别人 60 秒。
/// </summary>
public sealed class MishapOthersName : Mishap
{
    public bool IsSelf { get; }

    public MishapOthersName(bool isSelf) : base("others_name")
    {
        IsSelf = isSelf;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
        => env.MishapBlind((IsSelf ? 5 : 60) * 20);

    /// <summary>
    /// 源项目 getTrueNameFromDatum：在 datum（含嵌套列表）里找**玩家**实体引用。
    /// allowSelf=true 时施法者自己不算（写物品 / 阿卡夏 / 打包法术）；
    /// false 时连自己也不行（编年史家之策略写实体）。找到就抛。
    /// </summary>
    public static void ThrowIfTrueName(Iota datum, EntityIota? caster, bool allowSelf)
    {
        var queue = new Queue<Iota>();
        queue.Enqueue(datum);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (d is EntityIota { Target: EntityIota.EntityKind.Player } e)
            {
                bool self = caster is { Target: EntityIota.EntityKind.Player } c && c.Index == e.Index;
                if (!(allowSelf && self))
                {
                    throw new MishapOthersName(self);
                }
            }
            if (d is ListIota list)
            {
                foreach (var sub in list.Items) queue.Enqueue(sub);
            }
        }
    }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => IsSelf ? "不能这样写下自己的真名" : "不能写下别人的真名";
}

/// <summary>快捷栏里没有需要的物品（原版 MishapLackingHotbarItem，放置方块时）。惩罚：丢下手持物品。</summary>
public sealed class MishapLackingHotbarItem : Mishap
{
    private readonly string _what;

    public MishapLackingHotbarItem(string what) : base("lacking_hotbar_item")
    {
        _what = what;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
        => env.DropHeldItems();

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"快捷栏里没有{_what}";
}

/// <summary>这个图案需要玩家施法者（原版 MishapBadCaster，比如法术环里用哨卫图案）。NO-OP。</summary>
public sealed class MishapBadCaster : Mishap
{
    public MishapBadCaster() : base("bad_caster") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack) { }

    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "这个图案需要由玩家施放";
}
