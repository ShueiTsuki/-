using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Arithmetic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// 运算符图案（add / sub / mul / div / abs / pow / and / or / greater ...）。
/// 移植自源项目 castables/OperationAction。
///
/// 它不自己实现运算，而是**按操作数类型**交给算术引擎分派：
///   两个 number → DoubleArithmetic
///   含 vector → Vec3Arithmetic
///   两个 bool   → BoolArithmetic
///   比较运算符   → BoolArithmetic（吃 number，吐 boolean）
/// </summary>
public sealed class OperationAction : IAction
{
    public string Op { get; }

    public OperationAction(string op) => Op = op;

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        int arity = ArithmeticEngine.ArityOf(Op);
        if (arity <= 0)
        {
            return OperationResult.Fail(new MishapInvalidOperatorArgs(Op, "（无算术实现支持该运算符）"), image);
        }

        var stack = new List<Iota>(image.Stack);
        if (arity > stack.Count)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(arity, stack.Count), image);
        }

        // 取末尾 arity 个作为参数并弹出
        var args = new List<Iota>(arity);
        for (int i = stack.Count - arity; i < stack.Count; i++)
        {
            args.Add(stack[i]);
        }
        stack.RemoveRange(stack.Count - arity, arity);

        var result = ArithmeticEngine.Apply(Op, args);
        stack.AddRange(result);

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, Array.Empty<OperatorSideEffect>(), continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// 向量之提整 / 向量之拆解（源项目 Vec3Arithmetic 的 PACK / UNPACK）：三个数字 ↔ 一个向量。
/// ⚠️ 这里曾经是两个分量（「泰拉只有二维」）—— 原版是三个，向量现在也是三维的。
/// </summary>
public sealed class OpConstructVec : ConstMediaAction
{
    public override int Argc => 3;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        double C(int i) => args[i] is DoubleIota d ? d.Value : throw new MishapInvalidIota(args[i], "数字");
        return new Iota[] { new VectorIota(C(0), C(1), C(2)) };
    }
}

public sealed class OpDeconstructVec : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        if (args[0] is not VectorIota v)
        {
            throw new MishapInvalidIota(args[0], "向量");
        }
        return new Iota[] { new DoubleIota(v.X), new DoubleIota(v.Y), new DoubleIota(v.Z) };
    }
}

/// <summary>
/// print 图案：把栈顶 iota 的显示文本发到聊天框，并消耗掉它。
/// 移植自源项目 common/casting/actions/spells/OpPrint.kt。
/// </summary>
public sealed class OpPrint : ConstMediaAction
{
    public override int Argc => 1;

    public override ActionTypes Types => ActionTypes.Of(null, IotaTypes.Any);

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        env.PrintMessage(args[0].ToString());
        return Array.Empty<Iota>();
    }
}

/// <summary>数学与逻辑图案的注册（对应 HexActions.java 的 Math / Logic 段）。</summary>
public static class MathActions
{
    /// <summary>运算符图案：图案 Id 后缀即运算符名。</summary>
    private static readonly string[] OperatorPatterns =
    {
        // 数学（DoubleArithmetic 17 个）
        "add", "sub", "mul", "div", "abs", "pow", "floor", "ceil",
        "sin", "cos", "tan", "arcsin", "arccos", "arctan", "arctan2", "logarithm", "modulo",
        // 逻辑与比较（BoolArithmetic）
        "and", "or", "not", "xor", "greater", "less", "greater_eq", "less_eq",
        // 列表运算（ListArithmetic，与源项目 ListArithmetic.kt 的 OPS 清单对齐）
        // 注意：add / abs 已在上面注册过一次——分派按操作数类型走，列表和数字共用同一个图案，
        // 这里**不能**重复注册（同 Id 二次注册会覆盖/报错）。
        "index", "slice", "append", "unappend", "reverse",
        "index_of", "remove_from", "replace", "construct", "deconstruct",
    };

    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // 注册算术实现（顺序即分派优先级）
        ArithmeticEngine.Register(new DoubleArithmetic());
        ArithmeticEngine.Register(new Vec3Arithmetic());
        ArithmeticEngine.Register(new BoolArithmetic());
        ArithmeticEngine.Register(new ListArithmetic());
        ArithmeticEngine.Register(new ListSetArithmetic());
        ArithmeticEngine.Register(new BitwiseSetArithmetic());

        foreach (var op in OperatorPatterns)
        {
            PatternRegistry.RegisterAction("hexcasting:" + op, new OperationAction(op));
        }

        // 二维向量拆装（2 分量版本）
        PatternRegistry.RegisterAction("hexcasting:construct_vec", new OpConstructVec());
        PatternRegistry.RegisterAction("hexcasting:deconstruct_vec", new OpDeconstructVec());

        // print：把值发到聊天框
        PatternRegistry.RegisterAction("hexcasting:print", new OpPrint());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
