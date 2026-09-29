using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// 常数图案：不取参数，往栈上压一个固定值。
/// 对应源项目 Action.makeConstantOp(x)。
/// </summary>
public sealed class OpConstant : ConstMediaAction
{
    private readonly Iota _value;

    public OpConstant(Iota value) => _value = value;

    public override int Argc => 0;

    /// <summary>
    /// `const/*` 有几十条（数字 / 向量 / 布尔 / null ...），它们共用这一个类，
    /// 产出类型只能按**值本身**推。不标的话 `pi → print` 这种最基础的法术
    /// 在类型检查里会因为"产出了什么不知道"而把栈深算错。
    /// </summary>
    public override ActionTypes Types => ActionTypes.Of(TypeTagOf(_value));

    private static string TypeTagOf(Iota v) => v switch
    {
        VectorIota => IotaTypes.Vec,
        DoubleIota => IotaTypes.Num,
        BooleanIota => IotaTypes.Bool,
        NullIota => IotaTypes.Null,
        ListIota => IotaTypes.List,
        PatternIota => IotaTypes.Pattern,
        EntityIota => IotaTypes.Entity,
        _ => IotaTypes.Any,
    };

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new[] { _value };
}

/// <summary>
/// 栈重排图案。
/// 移植自 at.petrak.hexcasting.common.casting.actions.stack.OpTwiddling。
///
/// 语义：取末尾 argumentCount 个元素作为 args，按 lookup 里的下标重排后压回。
/// 例：swap = OpTwiddling(2, [1,0])；duplicate = OpTwiddling(1, [0,0])。
/// </summary>
public sealed class OpTwiddling : ConstMediaAction
{
    private readonly int[] _lookup;

    public OpTwiddling(int argumentCount, int[] lookup)
    {
        Argc = argumentCount;
        _lookup = lookup;
    }
    /// <summary>
    /// 栈重排类：弹 <c>argumentCount</c> 个、按 lookup 压回 <c>lookup.Length</c> 个。
    /// 例：duplicate = (1, [0,0]) → 弹 1 压 2。
    /// 不标的话栈深会少算，后面的类型判定会整体错位。
    /// </summary>
    public override ActionTypes Types
    {
        get
        {
            var produced = new string[_lookup.Length];
            for (int i = 0; i < produced.Length; i++) { produced[i] = IotaTypes.Any; }
            var consumed = new string[Argc];
            for (int i = 0; i < consumed.Length; i++) { consumed[i] = IotaTypes.Any; }
            return ActionTypes.OfMany(produced, consumed);
        }
    }

    public override int Argc { get; }

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var result = new Iota[_lookup.Length];
        for (int i = 0; i < _lookup.Length; i++)
        {
            result[i] = args[_lookup[i]];
        }
        return result;
    }
}

/// <summary>
/// 压入当前栈长度。
/// 移植自 at.petrak.hexcasting.common.casting.actions.stack.OpStackSize。
/// 注意压入的是**加入之前**的栈长度。
/// </summary>
public sealed class OpStackSize : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        // 先取原栈长度（不含本次压入的值），与源项目一致
        int preexistingCount = image.Stack.Count;

        var stack = new List<Iota>(image.Stack)
        {
            new DoubleIota(preexistingCount),
        };

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, Array.Empty<OperatorSideEffect>(), continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// 已实现的图案行为注册表。
/// 对应源项目 common/lib/hex/HexActions.java（Id → Action 的映射）。
///
/// 当前只注册了「纯栈操作 + 常数」这一批——它们不碰世界，
/// 最适合先验证 VM 求值链路是否正确。其余图案按批增量补齐。
/// </summary>
public static class HexActions
{
    /// <summary>本次注册了多少条图案行为。</summary>
    public static int RegisteredCount { get; private set; }

    public static void RegisterAll()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // ===== 常数 =====
        Const("hexcasting:const/null", NullIota.Instance);
        Const("hexcasting:const/true", BooleanIota.True);
        Const("hexcasting:const/false", BooleanIota.False);

        // 2D 适配：只保留 x/y 轴单位向量；pz/nz（±Z 轴）在二维世界无意义，不予实现
        Const("hexcasting:const/vec/0", VectorIota.Zero);
        Const("hexcasting:const/vec/px", VectorIota.UnitX);
        Const("hexcasting:const/vec/py", VectorIota.UnitY);
        Const("hexcasting:const/vec/nx", VectorIota.NegUnitX);
        Const("hexcasting:const/vec/ny", VectorIota.NegUnitY);

        // 注意：本命名空间下有 HexCastingTerraria.Core.Casting.Math，
        // 直接用 Math 会被解析成那个命名空间，必须写 System.Math。
        Const("hexcasting:const/double/pi", new DoubleIota(System.Math.PI));
        Const("hexcasting:const/double/tau", new DoubleIota(System.Math.Tau));
        Const("hexcasting:const/double/e", new DoubleIota(System.Math.E));
        Const("hexcasting:const/double/phi", new DoubleIota((1.0 + System.Math.Sqrt(5.0)) / 2.0));

        // ===== 栈操作（对应 HexActions.java:119-134）=====
        PatternRegistry.RegisterAction("hexcasting:swap", new OpTwiddling(2, new[] { 1, 0 }));
        PatternRegistry.RegisterAction("hexcasting:rotate", new OpTwiddling(3, new[] { 1, 2, 0 }));
        PatternRegistry.RegisterAction("hexcasting:rotate_reverse", new OpTwiddling(3, new[] { 2, 0, 1 }));
        PatternRegistry.RegisterAction("hexcasting:duplicate", new OpTwiddling(1, new[] { 0, 0 }));
        PatternRegistry.RegisterAction("hexcasting:over", new OpTwiddling(2, new[] { 0, 1, 0 }));
        PatternRegistry.RegisterAction("hexcasting:tuck", new OpTwiddling(2, new[] { 1, 0, 1 }));
        PatternRegistry.RegisterAction("hexcasting:2dup", new OpTwiddling(2, new[] { 0, 1, 0, 1 }));

        PatternRegistry.RegisterAction("hexcasting:stack_len", new OpStackSize());

        // ===== 数学与逻辑（算术引擎按操作数类型分派）=====
        MathActions.Register();

        // ===== 括号、转义与列表 =====
        ListActions.Register();

        // ===== 求值类（if / for_each / halt / thanos / eval / eval·cc / undo）=====
        EvalActions.Register();

        // ===== 只读类世界图案（get_caster / entity_pos/* / get_entity_*）=====
        // 先接「只读」这一批：它们不改变世界状态，不需要联机同步，风险最低。
        WorldActions.Register();

        // ===== 射线类（raycast / raycast·axis / raycast·entity）=====
        // 同样只读，但会返回「世界里的一个位置」，是粒子标记与后续写入类图案的基础。
        RaycastActions.Register();

        // ===== 法术类（会改变世界状态：add_motion / blink）=====
        SpellActions.Register();

        // ===== 数据载体类（read_into_parens）=====
        StorageActions.Register();

        // ===== 阿卡夏记录（以图案为键的存储）=====
        AkashicActions.Register();

        // ===== 法术环（circle/impetus_pos / impetus_dir / bounds）=====
        Circles.CircleActions.Register();

        // ===== 区域查询（zone_entity 11 条）=====
        ZoneActions.Register();

        // ===== 药水效果（potion 10 条）=====
        PotionActions.Register();

        // ===== 坐标取实体 + 爆炸（get_entity 6 条 / explode 2 条）=====
        EntitySelectActions.Register();

        // ===== 相等比较与小工具（equals / type_equals / bool_coerce / coerce_axial / last_n_list / swizzle）=====
        LogicActions.Register();

        // ===== 方块操作（conjure_block / conjure_light / break_block）=====
        BlockActions.Register();

        // ===== 世界效果（天气/火/水/雷电/催熟 8 条）=====
        WorldEffectActions.Register();

        // ===== 栈/括号工具（open_n_parens / close_all_parens / runtime_escape / duplicate_n / unique / random / get_media）=====
        StackUtilActions.Register();

        // ===== 比较类（compare_entity / compare_block / compare_item）=====
        CompareActions.Register();

        // ===== 读/写数据载体（read / read·entity / read·local / write / writable / erase 共 11 条）=====
        ReadWriteActions.Register();

        // ===== 单点法术（beep / create_lava / edify / place_block / recharge）=====
        SimpleSpellActions.Register();

        // ===== 哨卫（create / create·great / destroy / get_pos / wayfind）=====
        SentinelActions.Register();

        // ===== 咒法飞行（flight / range / time / can_fly）=====
        FlightActions.Register();

        // ===== 打包法术与媒质瓶（craft/cypher·trinket·artifact·battery / cycle_variant）=====
        CraftActions.Register();

        // ===== 脑叶切除（brainsweep）=====
        PatternRegistry.RegisterAction("hexcasting:brainsweep", new OpBrainsweep());

        // ===== 法术配色（colorize）=====
        PatternRegistry.RegisterAction("hexcasting:colorize", new OpColorize());

        RegisteredCount = PatternRegistry.RegisteredActionCount - before;
    }

    private static void Const(string patternId, Iota value)
        => PatternRegistry.RegisterAction(patternId, new OpConstant(value));
}
