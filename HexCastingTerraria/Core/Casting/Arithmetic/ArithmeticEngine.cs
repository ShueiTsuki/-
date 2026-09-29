using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Arithmetic;

/// <summary>
/// 一种算术实现（对应源项目 Arithmetic 接口）。
///
/// 每种算术只处理它认识的**操作数类型组合**；不认识就返回 null，
/// 由引擎继续问下一种算术。这正是源项目 ArithmeticEngine 的分派方式。
/// </summary>
public interface IArithmetic
{
    string Name { get; }

    /// <summary>该运算符在这套算术里的参数个数；返回 -1 表示这套算术不支持该运算符。</summary>
    int Arity(string op);

    /// <summary>执行；返回 null 表示类型不匹配、交给下一种算术。</summary>
    IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args);
}

/// <summary>
/// 算术引擎：按运算符 + 操作数类型分派到具体的 Arithmetic。
/// 对应源项目 api/casting/arithmetic/engine/ArithmeticEngine。
/// </summary>
public static class ArithmeticEngine
{
    private static readonly List<IArithmetic> Registered = new();

    public static void Register(IArithmetic arithmetic) => Registered.Add(arithmetic);

    public static void Clear() => Registered.Clear();

    public static int Count => Registered.Count;

    /// <summary>某个运算符需要几个参数（取第一个支持它的算术的答案）。</summary>
    public static int ArityOf(string op)
    {
        foreach (var a in Registered)
        {
            int n = a.Arity(op);
            if (n > 0) return n;
        }
        return -1;
    }

    /// <summary>按顺序询问每种算术，第一个能处理的生效。</summary>
    public static IReadOnlyList<Iota> Apply(string op, IReadOnlyList<Iota> args)
    {
        foreach (var a in Registered)
        {
            var r = a.Apply(op, args);
            if (r != null) return r;
        }

        throw new MishapInvalidOperatorArgs(op, DescribeArgs(args));
    }

    private static string DescribeArgs(IReadOnlyList<Iota> args)
    {
        var parts = new string[args.Count];
        for (int i = 0; i < args.Count; i++) parts[i] = args[i].TypeName;
        return string.Join(", ", parts);
    }
}

/// <summary>
/// 双精度算术。逐行对齐源项目 DoubleArithmetic.kt（17 个运算符）。
/// </summary>
public sealed class DoubleArithmetic : IArithmetic
{
    public string Name => "double_math";

    /// <summary>所有参数都是 number 才归本算术处理。</summary>
    private static bool AllDouble(IReadOnlyList<Iota> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is not DoubleIota) return false;
        }
        return args.Count > 0;
    }

    private static double D(Iota i) => ((DoubleIota)i).Value;

    public int Arity(string op) => op switch
    {
        "add" or "sub" or "mul" or "div" or "pow" or "arctan2" or "logarithm" or "modulo" => 2,
        "abs" or "floor" or "ceil" or "sin" or "cos" or "tan" or "arcsin" or "arccos" or "arctan" => 1,
        _ => -1,
    };

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        if (!AllDouble(args)) return null;

        switch (op)
        {
            case "add": return One(D(args[0]) + D(args[1]));
            case "sub": return One(D(args[0]) - D(args[1]));

            // mul 对数字是普通乘法（对向量才是点积，见 Vec2Arithmetic）
            case "mul": return One(D(args[0]) * D(args[1]));

            case "div":
                if (D(args[1]) == 0.0) throw new MishapDivideByZero(D(args[0]), D(args[1]), "divisor");
                return One(D(args[0]) / D(args[1]));

            case "abs": return One(System.Math.Abs(D(args[0])));

            case "pow":
            {
                double a = D(args[0]), b = D(args[1]);
                // 源项目：负数开分数次幂视为除以零错误（如 sqrt(-1)）
                if (a < 0 && !Tolerates(System.Math.Floor(b), b))
                {
                    throw new MishapDivideByZero(a, b, "exponent");
                }
                return One(System.Math.Pow(a, b));
            }

            case "floor": return One(System.Math.Floor(D(args[0])));
            case "ceil": return One(System.Math.Ceiling(D(args[0])));
            case "sin": return One(System.Math.Sin(D(args[0])));
            case "cos": return One(System.Math.Cos(D(args[0])));

            case "tan":
                if (System.Math.Cos(D(args[0])) == 0.0) throw new MishapDivideByZero(D(args[0]), 0.0, "tangent");
                return One(System.Math.Tan(D(args[0])));

            // 源项目把入参夹到 [-1,1] 再求反三角，避免定义域外返回 NaN
            case "arcsin": return One(System.Math.Asin(ClampTo(D(args[0]), -1.0, 1.0, 0.0)));
            case "arccos": return One(System.Math.Acos(ClampTo(D(args[0]), -1.0, 1.0, 0.0)));
            case "arctan": return One(System.Math.Atan(D(args[0])));

            // 注意参数顺序：源项目是 atan2(a, b)
            case "arctan2": return One(System.Math.Atan2(D(args[0]), D(args[1])));

            case "logarithm": return One(OperatorLog(D(args[0]), D(args[1])));

            case "modulo":
                if (D(args[1]) == 0.0) throw new MishapDivideByZero(D(args[0]), D(args[1]), "divisor");
                return One(D(args[0]) % D(args[1]));

            default: return null;
        }
    }

    private static IReadOnlyList<Iota> One(double v) => new Iota[] { new DoubleIota(v) };

    /// <summary>数值容差比较。源项目用 DoubleIota.tolerates（0.0001）。</summary>
    private static bool Tolerates(double a, double b)
        => System.Math.Abs(a - b) < DoubleIota.Tolerance;

    private static double ClampTo(double v, double lo, double hi, double fallback)
        => double.IsNaN(v) ? fallback : System.Math.Clamp(v, lo, hi);

    /// <summary>
    /// 对数。源项目用 OperatorLog（支持任意底数），此处按其语义实现：
    /// log_base(value) = ln(value) / ln(base)。
    /// </summary>
    private static double OperatorLog(double value, double logBase)
    {
        if (value <= 0 || logBase <= 0 || logBase == 1)
        {
            // 定义域外：源项目会抛 MishapDivideByZero 系错误，这里走同样路径
            throw new MishapDivideByZero(value, logBase, "logarithm");
        }
        return System.Math.Log(value) / System.Math.Log(logBase);
    }
}

/// <summary>
/// 布尔与比较算术。逐行对齐源项目 BoolArithmetic.kt。
///
/// 注意：**比较运算符（greater/less/greater_eq/less_eq）属于这里**，不属于 DoubleArithmetic。
/// 它们的操作数是 number，返回 boolean。
/// </summary>
public sealed class BoolArithmetic : IArithmetic
{
    public string Name => "bool_math";

    private static bool AllBool(IReadOnlyList<Iota> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is not BooleanIota) return false;
        }
        return args.Count > 0;
    }

    private static bool AllDouble(IReadOnlyList<Iota> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is not DoubleIota) return false;
        }
        return args.Count > 0;
    }

    public int Arity(string op) => op switch
    {
        "and" or "or" or "xor" or "greater" or "less" or "greater_eq" or "less_eq" => 2,
        "not" => 1,
        _ => -1,
    };

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        // 纯布尔运算
        if (AllBool(args))
        {
            bool a = ((BooleanIota)args[0]).Value;
            switch (op)
            {
                case "and": return One(a && ((BooleanIota)args[1]).Value);
                case "or": return One(a || ((BooleanIota)args[1]).Value);
                case "xor": return One(a ^ ((BooleanIota)args[1]).Value);
                case "not": return One(!a);
            }
        }

        // 比较运算：吃 number，吐 boolean
        if (AllDouble(args))
        {
            double x = ((DoubleIota)args[0]).Value;
            double y = ((DoubleIota)args[1]).Value;
            switch (op)
            {
                case "greater": return One(x > y);
                case "less": return One(x < y);
                // 源项目：容差内也算「大于等于」/「小于等于」
                case "greater_eq": return One(Tolerates(x, y) || x >= y);
                case "less_eq": return One(Tolerates(x, y) || x <= y);
            }
        }

        return null;
    }

    private static IReadOnlyList<Iota> One(bool v) => new Iota[] { BooleanIota.Of(v) };

    private static bool Tolerates(double a, double b)
        => System.Math.Abs(a - b) < DoubleIota.Tolerance;
}

/// <summary>
/// 二维向量算术。
///
/// ⚠️ 3D → 2D 适配要点（见 TERRARIA_2D_ADAPTATION.md）：
///   - 源项目的 mul 对向量是**点积**，结果仍是标量 → 2D 同样
///   - 源项目的 div 对向量是**叉积**：3D 得向量，**2D 只得标量**
///     （u.x*v.y - u.y*v.x）。这里接受这个破坏性变更，返回 number。
///     若硬造一个向量返回，语义会变成"旋转 90°"，几何法术会静默算错。
///   - abs 对向量是**长度**，返回 number
///   - pow（投影）、floor/ceil（分量取整）暂未实现，等核对 Vec3Arithmetic 后再补
/// </summary>
public sealed class Vec2Arithmetic : IArithmetic
{
    public string Name => "vec2_math";

    private static bool AllVector(IReadOnlyList<Iota> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is not VectorIota) return false;
        }
        return args.Count > 0;
    }

    private static (double X, double Y) V(Iota i)
    {
        var v = (VectorIota)i;
        return (v.X, v.Y);
    }

    public int Arity(string op) => op switch
    {
        "add" or "sub" or "mul" or "div" or "pow" => 2,
        "abs" or "floor" or "ceil" => 1,
        _ => -1,
    };

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        if (!AllVector(args)) return null;

        var (ax, ay) = V(args[0]);

        switch (op)
        {
            case "abs":
                return new Iota[] { new DoubleIota(HexMathUtil.Length(ax, ay)) };

            // 分量取整。源项目 Vec3Arithmetic.java:71/73 —— 各分量各自 Math.floor / Math.ceil。
            // ⚠️ 必须放在下面的双参分支**之前**：它们只吃一个参数。
            case "floor":
                return Vec(System.Math.Floor(ax), System.Math.Floor(ay));
            case "ceil":
                return Vec(System.Math.Ceiling(ax), System.Math.Ceiling(ay));
        }

        if (args.Count < 2) return null;
        var (bx, by) = V(args[1]);

        switch (op)
        {
            case "add": return Vec(ax + bx, ay + by);
            case "sub": return Vec(ax - bx, ay - by);

            // 点积：向量 · 向量 → 标量
            case "mul": return new Iota[] { new DoubleIota(ax * bx + ay * by) };

            // 叉积的 2D 形式 → 标量（破坏性变更，见类注释）
            case "div": return new Iota[] { new DoubleIota(ax * by - ay * bx) };

            // u 在 v 上的**投影**（源项目 Vec3Arithmetic.java:69：v.normalize().scale(u.dot(v.normalize()))）。
            //
            // 零向量的处理必须与原版一致：MC 的 `Vec3.normalize()` 在长度 < 1e-4 时返回 ZERO，
            // 于是点积为 0、再缩放仍是 ZERO —— **返回零向量，而不是报错**。
            // 若改成报错，玩家写 `pow(任意, 零向量)` 会得到一个与原版不同的失败，
            // 而这类差异在移植里最难被发现：两边都不崩，只是结果不同。
            case "pow":
            {
                double len = HexMathUtil.Length(bx, by);
                if (len < 1e-4)
                {
                    return Vec(0.0, 0.0);
                }
                double nx = bx / len;
                double ny = by / len;
                double dot = ax * nx + ay * ny;
                return Vec(nx * dot, ny * dot);
            }

            default: return null;
        }
    }

    private static IReadOnlyList<Iota> Vec(double x, double y) => new Iota[] { new VectorIota(x, y) };
}
