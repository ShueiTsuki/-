using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `equals` / `not_equals`：比较两个 iota 是否相等。
/// 移植自源项目 `OpEquality`。
///
/// **用容差比较**（`Iota.tolerates`）而不是精确相等 ——
/// 数值经过几轮运算后几乎不可能位级相等，
/// 精确比较会让「等于」这个图案在浮点场景下几乎永远为假。
/// </summary>
public sealed class OpEquality : ConstMediaAction
{
    private readonly bool _invert;

    public OpEquality(bool invert) => _invert = invert;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { BooleanIota.Of(Iota.Tolerates(args[0], args[1]) != _invert) };
}

/// <summary>
/// `type_equals` / `type_not_equals`：只比较**类型**，不比较值。
/// 移植自源项目 `OpTypeEquality`。
/// </summary>
public sealed class OpTypeEquality : ConstMediaAction
{
    private readonly bool _invert;

    public OpTypeEquality(bool invert) => _invert = invert;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { BooleanIota.Of((args[0].Kind == args[1].Kind) != _invert) };
}

/// <summary>
/// `bool_coerce`：把任意 iota 转成它的真假值。
/// 移植自源项目 `OpCoerceToBool`。就是 `isTruthy`。
/// </summary>
public sealed class OpCoerceToBool : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { BooleanIota.Of(args[0].IsTruthy()) };
}

/// <summary>
/// `coerce_axial`：把数值/向量规整到「轴向单位向量」。
/// 移植自源项目 `OpCoerceToAxial`。
///
/// 数值 → 取符号（-1 / 0 / 1）
/// 向量 → **零向量原样返回**；否则取最接近的轴方向的单位向量
///
/// 「零向量原样返回」是有意的：零向量没有方向，
/// 强行归到某个轴上会让「这个向量是零」这个事实消失。
/// </summary>
public sealed class OpCoerceToAxial : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        switch (args[0])
        {
            case DoubleIota d:
                return new Iota[] { new DoubleIota(System.Math.Sign(d.Value)) };

            case VectorIota v:
            {
                if (System.Math.Abs(v.X) < 1e-9 && System.Math.Abs(v.Y) < 1e-9)
                {
                    return new Iota[] { v };   // 零向量原样返回
                }

                // 取绝对值较大的那个分量定轴（2D 下就是上下左右四向）
                return System.Math.Abs(v.X) >= System.Math.Abs(v.Y)
                    ? new Iota[] { new VectorIota(System.Math.Sign(v.X), 0) }
                    : new Iota[] { new VectorIota(0, System.Math.Sign(v.Y)) };
            }

            default:
                throw new MishapInvalidIota(args[0], "数值或向量");
        }
    }
}

/// <summary>
/// `last_n_list`：把栈顶的「个数」弹出，再把下面 n 项打包成一个列表。
/// 移植自源项目 `OpLastNToList`。
///
/// 栈效果：`[..., a, b, c, n]` → `[..., list(a, b, c)]`（n = 3）
///
/// ⚠️ 个数上限是 `stack.size - 1` —— 也就是**不能把整个栈都打包**，
/// 至少要留一项位置给结果列表本身。
/// </summary>
public sealed class OpLastNToList : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(1, 0), image);
        }

        // 栈顶是个数（必须是整数）
        int n;
        try
        {
            n = CastingEnvironment.RequireIndex(stack[stack.Count - 1]);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        stack.RemoveAt(stack.Count - 1);

        // 源项目 getPositiveIntUnderInclusive(0, stack.size - 1)
        if (n < 0 || n > stack.Count)
        {
            return OperationResult.Fail(
                new MishapInvalidIota(stack.Count > 0 ? stack[^1] : NullIota.Instance,
                    $"0 到 {stack.Count} 之间的整数"), image);
        }

        var output = new List<Iota>(n);
        for (int i = stack.Count - n; i < stack.Count; i++)
        {
            output.Add(stack[i]);
        }
        stack.RemoveRange(stack.Count - n, n);

        stack.Add(new ListIota(output));

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// `swizzle`：按 Lehmer 码重排栈顶的若干项。
/// 移植自源项目 `OpAlwinfyHasAscendedToABeingOfPureMath`。
///
/// 参数是栈顶的一个整数（Lehmer 码），它决定下面 k 项的排列方式，
/// 其中 k 由码的大小决定（取阶乘不超过码的最大个数）。
///
/// 这是原版里唯一能做「任意置换」的图案 —— 写排序类法术要靠它。
/// </summary>
public sealed class OpSwizzle : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(1, 0), image);
        }

        long code;
        try
        {
            code = CastingEnvironment.RequireIndexLong(stack[stack.Count - 1]);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        if (code < 0)
        {
            return OperationResult.Fail(new MishapInvalidIota(stack[^1], "非负整数"), image);
        }

        stack.RemoveAt(stack.Count - 1);

        // 取阶乘序列：1!, 2!, ... 直到超过 code
        var strides = new List<long>();
        long fact = 1;
        for (int i = 2; fact <= code && i < 40; i++)
        {
            strides.Add(fact);
            // 防溢出
            if (fact > long.MaxValue / i) break;
            fact *= i;
        }
        if (strides.Count == 0)
        {
            strides.Add(1);   // code = 0 时也要有一个「不进位」的位
        }

        if (strides.Count > stack.Count)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(strides.Count + 1, stack.Count + 1), image);
        }

        // 取栈顶 strides.Count 项做置换
        int start = stack.Count - strides.Count;
        var pool = new List<Iota>();
        for (int i = start; i < stack.Count; i++)
        {
            pool.Add(stack[i]);
        }
        stack.RemoveRange(start, strides.Count);

        // Lehmer 码解码：从高位到低位依次取出第 index 个元素
        long radix = code;
        var permuted = new List<Iota>(strides.Count);
        for (int i = strides.Count - 1; i >= 0; i--)
        {
            long divisor = strides[i];
            int index = (int)(radix / divisor);
            radix %= divisor;

            if (index < 0 || index >= pool.Count) index = pool.Count - 1;   // 越界时取末尾，别让法术崩掉
            permuted.Add(pool[index]);
            pool.RemoveAt(index);
        }

        stack.AddRange(permuted);

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>`equals` 系列与小工具图案的注册。</summary>
public static class LogicActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:equals", new OpEquality(invert: false));
        PatternRegistry.RegisterAction("hexcasting:not_equals", new OpEquality(invert: true));
        PatternRegistry.RegisterAction("hexcasting:type_equals", new OpTypeEquality(invert: false));
        PatternRegistry.RegisterAction("hexcasting:type_not_equals", new OpTypeEquality(invert: true));

        PatternRegistry.RegisterAction("hexcasting:bool_coerce", new OpCoerceToBool());
        PatternRegistry.RegisterAction("hexcasting:coerce_axial", new OpCoerceToAxial());
        PatternRegistry.RegisterAction("hexcasting:last_n_list", new OpLastNToList());
        PatternRegistry.RegisterAction("hexcasting:swizzle", new OpSwizzle());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
