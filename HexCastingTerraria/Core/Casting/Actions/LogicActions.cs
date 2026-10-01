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
                if (v.X == 0 && v.Y == 0 && v.Z == 0)
                {
                    return new Iota[] { v };   // 源项目：vec == Vec3.ZERO 原样返回
                }

                // 源项目 Direction.getNearest(x, y, z)：按 DOWN, UP, NORTH(−z), SOUTH(+z), WEST(−x), EAST(+x)
                // 的顺序取点积最大的方向，**平局取先出现的**（所以 (1,1,0) 得到的是「上」）。
                // 注意：这里曾经只看 x/y、平局取 x 轴。
                (double X, double Y, double Z)[] dirs = { (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1), (-1, 0, 0), (1, 0, 0) };
                var best = (X: 0.0, Y: 0.0, Z: -1.0);   // NORTH（MC 的初值）
                double bestDot = float.Epsilon;          // Float.MIN_VALUE
                foreach (var d in dirs)
                {
                    double dot = (float)v.X * d.X + (float)v.Y * d.Y + (float)v.Z * d.Z;
                    if (dot > bestDot) { bestDot = dot; best = d; }
                }
                return new Iota[] { new VectorIota(best.X, best.Y, best.Z) };
            }

            default:
                throw new MishapInvalidIota(args[0], InvalidValue.NumVec);
        }
    }
}

/// <summary>
/// `last_n_list`：把栈顶的「个数」弹出，再把下面 n 项打包成一个列表。
/// 移植自源项目 `OpLastNToList`。
///
/// 栈效果：`[..., a, b, c, n]` → `[..., list(a, b, c)]`（n = 3）
///
/// 注意：个数上限是 `stack.size - 1` —— 也就是**不能把整个栈都打包**，
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

        // 源项目 getPositiveIntUnderInclusive(0, stack.size - 1)：栈顶是个数，最多把它下面的都打包。
        // 出错的是**个数那一项**（栈顶）—— 这里曾经把它下面那一项当成出错参数，惩罚会把错的那项换成垃圾值
        int n;
        try
        {
            n = CastingEnvironment.RequirePositiveIntUnderInclusive(stack[stack.Count - 1], stack.Count - 1);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        stack.RemoveAt(stack.Count - 1);

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

        // 源项目 getPositiveLong：不是整数和是负数报的是同一句
        long code;
        try
        {
            code = CastingEnvironment.RequirePositiveLong(stack[stack.Count - 1]);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        stack.RemoveAt(stack.Count - 1);

        // 源项目 FactorialIter：0!, 1!, 2!, 3!… = 1, 1, 2, 6, 24…，取所有 ≤ code 的。
        // 注意：这里曾经从 1! 开始（漏了 0! 那个 1），所有置换码都错一位 ——
        //    code=1 在原版是「交换栈顶两项」，这里什么都不做；code=0 原版不碰栈，这里却要求栈上至少一项。
        var strides = new List<long>();
        long acc = 1, n = 1;
        while (acc <= code)
        {
            strides.Add(acc);
            if (acc > long.MaxValue / n) break;   // 防溢出
            acc *= n;
            n++;
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
