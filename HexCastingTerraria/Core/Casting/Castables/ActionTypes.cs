namespace HexCastingTerraria.Core.Casting.Castables;

/// <summary>
/// 一条图案的**参数类型契约**：它消耗什么类型、产出什么类型。
///
/// ## 为什么需要它
///
/// `Argc` 只说明"要吃几个参数"，不说明"吃什么"。于是存在一整类问题：
/// **能编译、能过全部现有测试、只在游戏里炸**。
/// 真实案例（已在游戏里发生过）：
///
/// ```
/// get_caster → entity_pos/eye → get_entity_look → raycast → break_block
/// ```
/// `entity_pos/eye` 把【实体】换成了【坐标】，后面 `get_entity_look` 要实体却拿到坐标 ——
/// 它报错但**栈不变**，于是 `raycast` 也报错、栈还是不变，
/// 最后 `break_block` 拿到残留的眼位向量，**挖掉了玩家自己脚下那一格**。
/// 深度检查完全看不出问题（每步的 Argc 都够）。
///
/// 有了类型契约，检查器在第二步就能报出来。
///
/// ## 约定
///
/// · <see cref="Consumes"/> 的顺序与 `Execute(args, env)` 的 `args` **一致**：
///   第 0 个是最深（最早入栈）的那个，最后一个是最靠近栈顶的。
/// · 类型标签用 `IotaTypes` 里的常量；<see cref="IotaTypes.Any"/> 表示"不挑"。
/// · 没标注的图案是 <see cref="Unknown"/> —— 检查器只对**两端都确定**的组合报错，
///   绝不因为"有人忘了标注"而误报（那会让断言变成噪音，最后被关掉）。
/// </summary>
public readonly struct ActionTypes
{
    /// <summary>消耗的类型标签，顺序与 `Execute` 的 args 一致。</summary>
    public readonly string[] Consumes;

    /// <summary>
    /// 产出的类型标签，**按压栈顺序**；空数组表示这条图案不往栈上压东西。
    ///
    /// 为什么是数组而不是单个值：`duplicate` 压 2 个、`deconstruct_vec` 压 2 个、
    /// `for_each` 压 1 个…… 只建模"最多压 1 个"会在这些图案上把栈深算错，
    /// 而栈深一错，后面所有类型判定都会跟着错位。
    /// </summary>
    public readonly string[] Produces;

    /// <summary>是否有可用的类型信息。false 时检查器跳过这条图案。</summary>
    public readonly bool IsKnown;

    private ActionTypes(string[] consumes, string[] produces, bool known)
    {
        Consumes = consumes;
        Produces = produces;
        IsKnown = known;
    }

    /// <summary>未标注。</summary>
    public static readonly ActionTypes Unknown = new(System.Array.Empty<string>(), System.Array.Empty<string>(), false);

    /// <summary>
    /// 声明契约（压 0 或 1 个）。<paramref name="produces"/> 传 null 表示不产出。
    /// <paramref name="consumes"/> 顺序 = `Execute` 的 args 顺序（深的在前）。
    /// </summary>
    public static ActionTypes Of(string? produces, params string[] consumes)
        => new(consumes, produces is null ? System.Array.Empty<string>() : new[] { produces }, true);

    /// <summary>声明契约（压多个，按压栈顺序）。</summary>
    public static ActionTypes OfMany(string[] produces, params string[] consumes)
        => new(consumes, produces, true);
}

/// <summary>iota 的类型标签。字符串常量而不是 enum：便于日志与断言里直接打印。</summary>
public static class IotaTypes
{
    /// <summary>不挑类型。</summary>
    public const string Any = "any";

    public const string Entity = "entity";
    public const string Vec = "vec";
    public const string Num = "num";
    public const string Bool = "bool";
    public const string List = "list";
    public const string Pattern = "pattern";
    public const string Null = "null";
}
