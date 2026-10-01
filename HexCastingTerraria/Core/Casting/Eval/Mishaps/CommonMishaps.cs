using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

// 每个 mishap 的消息都照上游 hexcasting.mishap.<键> 的官方中文（zh_cn.json），键写在各自的 ErrorMessage 上。
// 上游把 iota / 实体 / 向量当成 Component 塞进翻译参数；这里用 DisplayTags 的聊天标记代替（颜色、内嵌小图案都在）。
// 「另一只手」照泰拉的实际操作写成「快捷栏中手持物品右边一格」（2026-10-01 用户要求）。

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

    /// <summary>上游 no_args「本应接受大于等于%s个参数，而实际为空栈」/ not_enough_args「本应接受大于等于%s个参数，而实际栈高度为%s」。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => Got == 0
            ? $"本应接受大于等于{Expected}个参数，而实际为空栈"
            : $"本应接受大于等于{Expected}个参数，而实际栈高度为{Got}";
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

    /// <summary>上游用的是 hexcasting.message.cant_overcast。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "这个咒术需求的媒质量比我有的还多……我应该再算几遍。";
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
    /// 上游 invalid_pattern「图案%s不对应任何操作」（%s = 内嵌的小图案），没有图案时 invalid_pattern_generic。
    /// 注意：这里曾经自己加了角度串和「最接近的图案、差几笔」—— 那段提示画布 HUD 上一直有（PatternSuggestion.Describe），
    /// 事故消息照原版只说这一句。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => Pattern == null
            ? "该图案不对应任何操作"
            : $"图案{DisplayTags.Of(new PatternIota(Pattern))}不对应任何操作";
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

    /// <summary>上游 unescaped「本应运行一个图案，而实际运行了%s」。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"本应运行一个图案，而实际运行了{DisplayTags.Of(Perpetrator)}";
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

    /// <summary>上游 stack_size。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "超出了栈的大小上限";
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

    /// <summary>上游 eval_too_much。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "运行了过多图案";
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

    /// <summary>
    /// 上游 unknown「抛出异常（%s）。这是模组中的漏洞。」，%s 是 Java 的 Throwable.toString()（类名: 消息）。
    /// 上游还把调用栈挂在悬停提示上；聊天栏没有悬停，调用栈在日志里。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"抛出异常（{Exception.GetType().FullName}: {Exception.Message}）。这是模组中的漏洞。";
}

/// <summary>
/// 需要「启蒙」才能使用该图案（大法术）。对应原版 MishapUnenlightened.kt：
/// 丢下手上的东西、提示「法术没起效」、触发 FAIL_GREAT_SPELL（→ 解锁过载），图案判为无效。
/// </summary>
public sealed class MishapUnenlightened : Mishap
{
    /// <summary>原版文本 hexcasting.message.cant_great_spell（官方中文）。</summary>
    public const string CantGreatSpell = "奇怪，法术没起效……也许我还不够熟练？";

    public MishapUnenlightened() : base("unenlightened") { }

    public override ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Invalid;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        env.DropHeldItems();
        // 上游：castingEntity?.sendSystemMessage(cant_great_spell) —— 直接发给施法者，不带图案名前缀
        env.MessageCaster(CantGreatSpell);
        env.OnFailedGreatSpell();
    }

    /// <summary>上游 errorMessage 返回 null：这个事故没有事故消息（提示在 Execute 里单独发给施法者）。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx) => null;
}

/// <summary>
/// 除零（也用于负数开分数次幂、tan(π/2)、log 定义域外等情形）。
/// 惩罚：压一个垃圾、扣掉当前生命的一半（源项目同）。
/// </summary>
public sealed class MishapDivideByZero : Mishap
{
    /// <summary>上游 suffix：divide / project / exponent / logarithm（决定用哪句消息）。</summary>
    public string Suffix { get; }

    /// <summary>两个操作数的显示（已经是聊天标记，或「零」「零向量」这类说法）。</summary>
    public string Operand1 { get; }
    public string Operand2 { get; }

    public MishapDivideByZero(string operand1, string operand2, string suffix = "divide") : base("divide_by_zero")
    {
        Operand1 = operand1;
        Operand2 = operand2;
        Suffix = suffix;
    }

    /// <summary>上游 MishapDivideByZero.of(Double, Double, suffix)。</summary>
    public static MishapDivideByZero Of(double operand1, double operand2, string suffix = "divide")
        => Of(new DoubleIota(operand1), new DoubleIota(operand2), suffix);

    /// <summary>上游 MishapDivideByZero.of(Iota, Iota, suffix)：指数的第二个操作数用 powerOf，其余用 translate。</summary>
    public static MishapDivideByZero Of(Iota operand1, Iota operand2, string suffix = "divide")
        => new(Translate(operand1), suffix == "exponent" ? PowerOf(operand2) : Translate(operand2), suffix);

    /// <summary>上游 MishapDivideByZero.tan：「试图用 x 的余弦除 x 的正弦」。</summary>
    public static MishapDivideByZero Tan(double angle)
    {
        string a = Translate(new DoubleIota(angle));
        return new MishapDivideByZero($"{a}的正弦", $"{a}的余弦");
    }

    /// <summary>上游 translate：0 → divide_by_zero.zero「零」，零向量 → zero.vec「零向量」，其余 iota.display()。</summary>
    private static string Translate(Iota datum) => datum switch
    {
        DoubleIota { Value: 0.0 } => "零",
        VectorIota { X: 0.0, Y: 0.0, Z: 0.0 } => "零向量",
        _ => DisplayTags.Of(datum),
    };

    /// <summary>上游 powerOf(Iota)：0 → zero.power「零次幂」，其余 iota.display()。</summary>
    private static string PowerOf(Iota datum) => datum is DoubleIota { Value: 0.0 } ? "零次幂" : DisplayTags.Of(datum);

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：stack.add(GarbageIota()); env.mishapEnvironment.damage(0.5f) —— 扣掉当前生命的一半
        stack.Add(GarbageIota.Instance);
        env.MishapDamage(0.5);
    }

    /// <summary>上游 divide_by_zero.&lt;suffix&gt;。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx) => Suffix switch
    {
        "project" => $"试图将{Operand1}投影至{Operand2}",
        "exponent" => $"试图计算{Operand1}的{Operand2}",
        "logarithm" => $"试图计算{Operand1}以{Operand2}为底的对数",
        _ => $"试图用{Operand2}除{Operand1}",
    };
}

/// <summary>
/// 运算符的操作数类型不受支持。源项目 MishapInvalidOperatorArgs(perpetrators)：
/// perpetrators = 参与运算的参数，**栈里深的在前、栈顶在后**（ArithmeticEngine 弹栈后 reverse）。
/// </summary>
public sealed class MishapInvalidOperatorArgs : Mishap
{
    public string Op { get; }

    /// <summary>参与运算的参数（深 → 栈顶）。</summary>
    public IReadOnlyList<Iota> Perpetrators { get; }

    public MishapInvalidOperatorArgs(string op, IReadOnlyList<Iota> perpetrators) : base("invalid_operator_args")
    {
        Op = op;
        Perpetrators = perpetrators;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：把参与运算的每个参数都换成垃圾值（栈顶往下 perpetrators.size 个）
        for (int i = 0; i < Perpetrators.Count && i < stack.Count; i++)
        {
            stack[stack.Count - 1 - i] = GarbageIota.Instance;
        }
    }

    /// <summary>
    /// 上游 invalid_operator_args.one「在栈下标为%d处获取到意外iota：%s」，many「在栈下标为%2$d到%3$d处获取到%1$s个意外iota：%4$s」
    /// （下标 0 到 n-1，列出的是参数的**值**，按深 → 栈顶）。之前只列类型名，而且没说顺序，读起来像是「这个图案要这几个参数」。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
    {
        var values = new string[Perpetrators.Count];
        for (int i = 0; i < values.Length; i++) { values[i] = DisplayTags.Of(Perpetrators[i]); }
        return Perpetrators.Count == 1
            ? $"在栈下标为0处获取到意外iota：{values[0]}"
            : $"在栈下标为0到{Perpetrators.Count - 1}处获取到{Perpetrators.Count}个意外iota：{string.Join(", ", values)}";
    }
}

/// <summary>
/// 某个槽位收到了类型不对的 iota（例如 construct_vec 收到 bool）。
/// 源项目：MishapInvalidIota。
/// </summary>
public sealed class MishapInvalidIota : Mishap
{
    public Iota Perpetrator { get; }

    /// <summary>「本应接受什么」：上游 hexcasting.mishap.invalid_value.* 的说法，从 <see cref="InvalidValue"/> 取。</summary>
    public string Expected { get; }

    public MishapInvalidIota(Iota perpetrator, string expected) : base("invalid_iota")
    {
        Perpetrator = perpetrator;
        Expected = expected;
    }

    /// <summary>出错参数离栈顶多远（0 = 栈顶）。不给时按引用在栈上找（参数就是栈上那个对象）。</summary>
    public int? ReverseIdx { get; init; }

    /// <summary>同一个事故，指明出错的是栈顶往下第几个（原版 reverseIdx）。</summary>
    public MishapInvalidIota At(int reverseIdx) => new(Perpetrator, Expected) { ReverseIdx = reverseIdx };

    /// <summary>按引用在栈上找到的位置（离栈顶多远）；-1 = 不在栈上；null = 还没找过。</summary>
    private int? _located;

    private static int LocateFromTop(IReadOnlyList<Iota> stack, Iota target)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(stack[i], target)) return stack.Count - 1 - i;
        }
        return -1;
    }

    /// <summary>
    /// 上游在抛出时就给定 reverseIdx；这里按引用在栈上找。必须在出消息之前找 ——
    /// 上游（和这里）都是先 postExecution 发消息、再执行副作用，以前只在 Execute 里找，消息里的下标永远是 0。
    /// </summary>
    public override void LocateIn(IReadOnlyList<Iota> stack)
    {
        if (ReverseIdx == null) _located = LocateFromTop(stack, Perpetrator);
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：stack[size - 1 - reverseIdx] = GarbageIota() —— 把出错的那个参数换成垃圾值
        int idx = ReverseIdx ?? _located ?? LocateFromTop(stack, Perpetrator);
        _located = idx;
        if (idx >= 0 && idx < stack.Count)
        {
            stack[stack.Count - 1 - idx] = GarbageIota.Instance;
        }
    }

    /// <summary>上游 invalid_value「本应在栈下标为%2$s处接受%1$s，而实际接受了%3$s：%4$s」。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
    {
        int idx = ReverseIdx ?? System.Math.Max(_located ?? 0, 0);
        return $"本应在栈下标为{idx}处接受{Expected}，而实际接受了{KindDesc(Perpetrator)}：{DisplayTags.Of(Perpetrator)}";
    }

    /// <summary>
    /// 上游 hexcasting.iota.&lt;种类&gt;.desc 官方中文。附属的种类上游没有 desc（MC 会直接露出翻译键），
    /// 这里用 class.unknown 的说法，不露出内部类型名。
    /// </summary>
    private static string KindDesc(Iota iota) => iota.Kind switch
    {
        IotaKind.Null => "一个空值",
        IotaKind.Double => "一个数",
        IotaKind.Boolean => "一个布尔值",
        IotaKind.Entity => "一个实体",
        IotaKind.List => "一个列表",
        IotaKind.Pattern => "一个图案",
        IotaKind.Garbage => "垃圾",
        IotaKind.Vector => "一个向量",
        IotaKind.Continuation => "一个跳转iota",
        _ => InvalidValue.ClassUnknown,
    };
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

    /// <summary>移植版特有。图案名由前缀给出（上游 errorMessageWithName），这里不再露出图案 id。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "该图案尚未实现";
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

    /// <summary>上游 needs_parens。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "在绘制反思前未先绘制内省";
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

    /// <summary>移植版特有（只在没有世界的离线环境出现）。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "当前施法环境不支持世界访问";
}

/// <summary>
/// 目标实体超出施法范围。
/// 移植自源项目 MishapEntityTooFarAway。惩罚：手持物品甩向那个实体。
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

    /// <summary>上游 entity_too_far「%s超出影响范围」，%s = entity.displayName（不上色）。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{EntityNameText(_entity)}超出影响范围";
}

/// <summary>
/// 目标**坐标**超出施法范围。
/// 移植自源项目 MishapLocationTooFarAway（`assertVecInRange` 抛出的那个；1.20 里是 MishapBadLocation "too_far"）。
///
/// 与 <see cref="MishapEntityTooFarAway"/> 的区别：
/// 前者校验「一个点」（射线起点、法术落点），后者校验「一个实体」。
/// 源项目里两者是分开的 mishap，消息与表现都不同，不要合并。
/// </summary>
public sealed class MishapLocationTooFarAway : Mishap
{
    private readonly double _x;
    private readonly double _y;
    private readonly double _z;

    public MishapLocationTooFarAway(double x, double y, double z = 0.0) : base("location_too_far")
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：yeetHeldItemsTowards(location)
        env.YeetHeldItemsTowards(_x, _y);
    }

    /// <summary>上游 location_too_far「%s超出影响范围」，%s = Vec3Iota.display(位置)。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{VecText(_x, _y, _z)}超出影响范围";
}

/// <summary>
/// 目标位置不可用（世界外、太靠近世界边界、不许改动等）。
/// 移植自源项目 MishapBadLocation(location, type)。
///
/// 与 <see cref="MishapLocationTooFarAway"/> 的区别：
/// 后者是「超出施法范围」（可能只是站远了），
/// 这个是「这个位置根本不能去」（世界外 / 会掉出地图），语义完全不同，不要合并。
/// </summary>
public sealed class MishapBadLocation : Mishap
{
    /// <summary>上游 type：location_&lt;type&gt; 的后缀。</summary>
    public const string TooFar = "too_far";
    public const string OutOfWorld = "out_of_world";
    public const string TooCloseToOut = "too_close_to_out";
    public const string Forbidden = "forbidden";
    public const string BadDimension = "bad_dimension";

    private readonly double _x;
    private readonly double _y;
    private readonly double _z;

    public string Type { get; }

    public MishapBadLocation(double x, double y, string type = TooFar, double z = 0.0) : base("bad_location")
    {
        _x = x;
        _y = y;
        _z = z;
        Type = type;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：yeetHeldItemsTowards(location)
        env.YeetHeldItemsTowards(_x, _y);
    }

    /// <summary>上游 location_&lt;type&gt;，%s = Vec3Iota.display(位置)。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
    {
        string where = VecText(_x, _y, _z);
        return Type switch
        {
            OutOfWorld => $"{where}不在此世界内",
            TooCloseToOut => $"{where}离世界边界太近了",
            Forbidden => $"{where}并未对你开放",
            BadDimension => "无法在此维度内执行此操作",
            _ => $"{where}超出影响范围",
        };
    }
}

/// <summary>
/// 「另一只手」里没有需要的东西。移植自源项目 `MishapBadOffhandItem`。
///
/// 泰拉没有副手：「另一只手」= 快捷栏里施法物品右边那一格（见 PlayerCastingEnvironment.PrimarySlots），
/// 其次才是手上拿着的。「需要什么」照原版各 key 的中文（<see cref="Wanted"/>）。
/// 上游手上有东西但不合用时，消息后半句报出那件东西（「而实际持有1个[核心]」），没有时报「而实际无对应物品」。
/// </summary>
public sealed class MishapBadHeldItem : Mishap
{
    /// <summary>原版 MishapBadOffhandItem 的 wanted key。</summary>
    public enum Need { Storage, Read, Write, ReadOnly, Eraseable, Colorizer, Variant, Bottle, OnlyOne, Rechargeable }

    private readonly string _wanted;
    private readonly ItemStackInfo? _actual;

    /// <param name="need">要什么（bad_item.*）。</param>
    /// <param name="datum">ReadOnly 时要写的那个 iota（「一个能够接受%s的地方」）。</param>
    /// <param name="actual">手上那件不合用的东西；null = 手上没有对应的物品。</param>
    public MishapBadHeldItem(Need need, Iota? datum = null, ItemStackInfo? actual = null)
        : this(WantedOf(need, datum), actual) { }

    /// <summary>直接给出「需要什么」（原版 craft/* 用物品名，例如「杂件」）。</summary>
    public MishapBadHeldItem(string wanted, ItemStackInfo? actual = null) : base("bad_held_item")
    {
        _wanted = wanted;
        _actual = actual;
    }

    private static string WantedOf(Need need, Iota? datum) => need switch
    {
        Need.Read => Wanted.IotaRead,
        Need.Write => Wanted.IotaWrite,
        Need.ReadOnly => Wanted.IotaReadonly(datum is null ? "" : DisplayTags.Of(datum)),
        Need.Eraseable => Wanted.Eraseable,
        Need.Colorizer => Wanted.Colorizer,
        Need.Variant => Wanted.Variant,
        Need.Bottle => Wanted.Bottle,
        Need.OnlyOne => Wanted.OnlyOne,
        Need.Rechargeable => Wanted.Rechargeable,
        _ => Wanted.IotaHolder,
    };

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目 MishapBadOffhandItem：dropHeldItems()
        env.DropHeldItems();
    }

    /// <summary>
    /// 上游 bad_item.offhand「需要在另一只手里持有%s，而实际持有%d个%s」/ no_item.offhand「需要在另一只手里持有%s，而实际无对应物品」，
    /// 「在另一只手里持有」写成「在快捷栏中手持物品右边一格放有」。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => _actual is { Count: > 0 } a
            ? $"需要在快捷栏中手持物品右边一格放有{_wanted}，而实际放有{a.Count}个{ItemNameText(a.Name)}"
            : $"需要在快捷栏中手持物品右边一格放有{_wanted}，而实际无对应物品";
}

/// <summary>
/// 目标实体身上没有需要的那种数据载体。
/// 移植自源项目 `MishapBadEntity`（`read/entity` / `write/entity` 用）。
/// 掉落物要用 <see cref="Of"/>：上游 MishapBadEntity.of 遇到掉落物换成 MishapBadItem（消息报那堆物品，惩罚是把它弹起来）。
/// </summary>
public sealed class MishapBadEntity : Mishap
{
    private readonly EntityIota _entity;

    public MishapBadEntity(EntityIota entity, string wanted) : base("bad_entity")
    {
        _entity = entity;
        Expected = wanted;
    }

    /// <summary>「需要什么」（上游 bad_item.*，见 Wanted）。</summary>
    public string Expected { get; }

    /// <summary>上游 MishapBadEntity.of(entity, stub)：掉落物 → MishapBadItem，其他实体 → MishapBadEntity。</summary>
    public static Mishap Of(EntityIota entity, string wanted)
        => entity.Target == EntityIota.EntityKind.Item ? new MishapBadItem(entity, wanted) : new MishapBadEntity(entity, wanted);

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：yeetHeldItemsTowards(entity.position())
        if (env.World is { } w) { var (x, y) = w.FeetPosition(_entity); env.YeetHeldItemsTowards(x, y); }
    }

    /// <summary>上游 bad_entity「需要%s，而实际接受了%s」，实体名青色。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"需要{Expected}，而实际接受了{EntityNameAqua(_entity)}";
}

/// <summary>
/// 目标掉落物不满足要求（不是媒质物品之类）。
/// 移植自源项目 `MishapBadItem`（`recharge` 与打包法术用）。
/// </summary>
public sealed class MishapBadItem : Mishap
{
    private readonly EntityIota _entity;

    public MishapBadItem(EntityIota entity, string wanted) : base("bad_item")
    {
        _entity = entity;
        Expected = wanted;
    }

    /// <summary>「需要什么」（上游 bad_item.*，见 Wanted）。</summary>
    public string Expected { get; }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：那个掉落物往上弹一下
        env.World?.MishapLaunchItem(_entity);
    }

    /// <summary>
    /// 上游 bad_item「需要%s，而实际持有%d个%s」（那堆物品的数量与名字），物品是空的时 no_item「需要%s，而实际无对应物品」。
    /// 世界给不出这堆物品（离线测试）时按 1 个、用实体名。
    /// </summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
    {
        var stack = env.World?.ItemStackOf(_entity) ?? new ItemStackInfo(EntityNameText(_entity), 1);
        return stack.Count <= 0
            ? $"需要{Expected}，而实际无对应物品"
            : $"需要{Expected}，而实际持有{stack.Count}个{ItemNameText(stack.Name)}";
    }
}

/// <summary>
/// 脑叶切除失败：这个生物配不上这个方块（或这个生物根本不能切）。
/// 移植自源项目 `MishapBadBrainsweep`。
/// </summary>
public sealed class MishapBadBrainsweep : Mishap
{
    private readonly EntityIota _entity;
    private readonly double _x;
    private readonly double _y;
    private readonly double _z;

    public MishapBadBrainsweep(EntityIota entity, double x, double y, double z = 0.0) : base("bad_brainsweep")
    {
        _entity = entity;
        _x = x;
        _y = y;
        _z = z;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：trulyHurt(mob, 1f)
        env.World?.MishapHurtEntity(_entity, kill: false);
    }

    /// <summary>上游 bad_brainsweep「%s排斥此生物的意识」，%s = 那一格方块的名字（blockAtPos）。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{BlockNameText(env, _x, _y, _z)}排斥此生物的意识";
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

    /// <summary>上游 already_brainswept。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "此意识已被使用";
}

/// <summary>
/// 目标位置不是阿卡夏记录方块。
/// 移植自源项目 `MishapNoAkashicRecord`。
/// </summary>
public sealed class MishapNoAkashicRecord : Mishap
{
    private readonly double _x;
    private readonly double _y;
    private readonly double _z;

    public MishapNoAkashicRecord(double x, double y, double z = 0.0) : base("no_akashic_record")
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：removeXp(100)。泰拉没有经验值 → 环境默认不做
        env.MishapRemoveXp(100);
    }

    /// <summary>上游 no_akashic_record「%s处无阿卡夏记录」，%s = pos.toShortString()。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"{BlockPosText(_x, _y, _z)}处无阿卡夏记录";
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

    /// <summary>上游 no_spell_circle。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "需在法术环上执行";
}

/// <summary>
/// 目标方块不满足条件（不可替换 / 不是树苗等）。
/// 移植自源项目 `MishapBadBlock`。
/// </summary>
public sealed class MishapBadBlock : Mishap
{
    private readonly double _x;
    private readonly double _y;
    private readonly double _z;

    /// <param name="expected">要什么样的方块（上游 bad_block.*，见 <see cref="Wanted"/>）。</param>
    public MishapBadBlock(double x, double y, string expected, double z = 0.0) : base("bad_block")
    {
        _x = x;
        _y = y;
        _z = z;
        Expected = expected;
    }

    public string Expected { get; }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
    {
        // 源项目：world.explode(null, pos + 0.5, 0.25f, ExplosionInteraction.NONE) —— 小爆炸、不破坏方块
        env.World?.MishapExplosion(System.Math.Floor(_x) + 0.5, System.Math.Floor(_y) + 0.5);
    }

    /// <summary>上游 bad_block「本应在%2$s处接受%1$s，而实际接受了%3$s」：位置 pos.toShortString()，最后是那一格方块的名字。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"本应在{BlockPosText(_x, _y, _z)}处接受{Expected}，而实际接受了{BlockNameText(env, _x, _y, _z)}";
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

    /// <summary>上游 immune_entity「无法影响到%s」，实体名青色。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"无法影响到{EntityNameAqua(_entity)}";
}

/// <summary>
/// 试图把**玩家**的实体引用写进物品 / 阿卡夏 / 打包法术 / 实体（原版 MishapOthersName —— 保护「真名」）。
/// 惩罚：失明，写的是施法者自己 5 秒，别人 60 秒。
/// </summary>
public sealed class MishapOthersName : Mishap
{
    /// <summary>名字被写进去的那个玩家（上游 confidant）。</summary>
    public EntityIota Confidant { get; }

    public MishapOthersName(EntityIota confidant) : base("others_name")
    {
        Confidant = confidant;
    }

    /// <summary>上游 `confidant == env.castingEntity`：在执行 / 出消息时按环境判断，而不是看抛出时传了谁当施法者。</summary>
    private bool IsSelf(CastingEnvironment env)
        => env.World?.Caster is { Target: EntityIota.EntityKind.Player } c && c.Index == Confidant.Index;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
        => env.MishapBlind((IsSelf(env) ? 5 : 60) * 20);

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
                    throw new MishapOthersName(e);
                }
            }
            if (d is ListIota list)
            {
                foreach (var sub in list.Items) queue.Enqueue(sub);
            }
        }
    }

    /// <summary>上游 others_name「试图侵犯%s的灵魂的隐私」（%s = 玩家名）/ others_name.self。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => IsSelf(env) ? "试图随意泄露我自己真名的秘密" : $"试图侵犯{EntityNameText(Confidant)}的灵魂的隐私";
}

/// <summary>快捷栏里没有需要的物品（原版 MishapLackingHotbarItem，放置方块时）。惩罚：丢下手持物品。</summary>
public sealed class MishapLackingHotbarItem : Mishap
{
    private readonly string _wanted;

    /// <param name="wanted">要什么（上游 bad_item.*，见 <see cref="Wanted"/>）。</param>
    public MishapLackingHotbarItem(string wanted) : base("lacking_hotbar_item")
    {
        _wanted = wanted;
    }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack)
        => env.DropHeldItems();

    /// <summary>上游 bad_item.hotbar「需要在快捷栏里放有%s」。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => $"需要在快捷栏里放有{_wanted}";
}

/// <summary>
/// 原版 MishapDisallowedSpell：这个图案被禁用了（服务器配置禁用，或附属规定「只能用法杖施放」时在别的地方施放）。
/// 结果 Invalid、黑色火花、不做别的事。本体目前只有附属在用（HexParse 的解码 / 编码之策略）。
/// </summary>
public sealed class MishapDisallowedSpell : Mishap
{
    private readonly string? _actionName;

    /// <param name="actionName">被禁的图案名；null = 上游的「_generic」写法。</param>
    public MishapDisallowedSpell(string? actionName = null) : base("disallowed") => _actionName = actionName;

    public override ResolvedPatternType ResolutionType(CastingEnvironment env) => ResolvedPatternType.Invalid;

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack) { }

    /// <summary>上游 disallowed「%s已被服务器管理员禁用」/ disallowed_generic。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => _actionName == null ? "该图案已被服务器管理员禁用" : $"{_actionName}已被服务器管理员禁用";
}

/// <summary>这个图案需要玩家施法者（原版 MishapBadCaster，比如法术环里用哨卫图案）。NO-OP。</summary>
public sealed class MishapBadCaster : Mishap
{
    public MishapBadCaster() : base("bad_caster") { }

    public override void Execute(CastingEnvironment env, MishapContext errorCtx, List<Iota> stack) { }

    /// <summary>上游 bad_caster。</summary>
    protected override string? ErrorMessage(CastingEnvironment env, MishapContext errorCtx)
        => "试图运行的图案需要强大的意识才能承受";
}
