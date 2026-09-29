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

        throw new MishapInvalidOperatorArgs(op, DescribeArgs(args), args.Count);
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

            // 源项目 asDoubleBetween(-1, 1)：定义域外是 MishapInvalidIota，**不是**夹取
            //（这里曾写「源项目把入参夹到 [-1,1]」并静默夹取 —— 与原版不符）
            case "arcsin": return One(System.Math.Asin(Between(args[0], -1.0, 1.0)));
            case "arccos": return One(System.Math.Acos(Between(args[0], -1.0, 1.0)));
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

    private static double Between(Iota iota, double lo, double hi)
    {
        double v = D(iota);
        if (v >= lo && v <= hi) return v;
        throw new MishapInvalidIota(iota, $"{lo} 到 {hi} 之间的数");
    }

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
        "not" or "abs" => 1,
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
                // 源项目 BoolArithmetic.ABS：真 → 1，假 → 0
                case "abs": return new Iota[] { new DoubleIota(a ? 1.0 : 0.0) };
            }
        }

        // 比较运算：吃 number，吐 boolean。只有两个参数时才是比较 ——
        // 单个数字（如数字的「非」）要交给位运算；这里曾经不看个数直接读 args[1]，数组越界成了内部错误
        if (args.Count == 2 && AllDouble(args))
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

    /// <summary>
    /// 向量与数字混用（至少一个向量、其余是数字）：**逐分量**套数字算术，数字广播到每个分量。
    /// 源项目 OperatorVec3Delegating：add / sub / mul / div / pow / mod 都走这条
    ///（向量 × 数字 = 缩放 —— 「视线 × 2」这种最常用的写法全靠它）。
    /// ⚠️ 这里曾经只认「全是向量」，向量 × 数字直接报「参数类型不对」。
    /// </summary>
    private static IReadOnlyList<Iota>? ApplyMixed(string op, IReadOnlyList<Iota> args)
    {
        if (args.Count != 2 || op is not ("add" or "sub" or "mul" or "div" or "pow" or "modulo")) return null;
        bool anyVec = false;
        foreach (var a in args)
        {
            if (a is VectorIota) { anyVec = true; }
            else if (a is not DoubleIota) { return null; }
        }
        if (!anyVec) return null;

        static (double X, double Y) Spread(Iota i) => i is VectorIota v ? (v.X, v.Y) : (((DoubleIota)i).Value, ((DoubleIota)i).Value);
        var (ax, ay) = Spread(args[0]);
        var (bx, by) = Spread(args[1]);
        var scalar = new DoubleArithmetic();
        double C(double a, double b) => ((DoubleIota)scalar.Apply(op, new Iota[] { new DoubleIota(a), new DoubleIota(b) })![0]).Value;
        return Vec(C(ax, bx), C(ay, by));
    }

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        if (!AllVector(args)) return ApplyMixed(op, args);

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

            // 取余：两边都是向量也逐分量（源项目 MOD 用的就是 make2Fallback）
            case "modulo": return ApplyMixed(op, args);

            default: return null;
        }
    }

    private static IReadOnlyList<Iota> Vec(double x, double y) => new Iota[] { new VectorIota(x, y) };
}

/// <summary>
/// 数字的位运算。逐行对齐源项目 BitwiseSetArithmetic.kt：
/// 与 / 或 / 异或 / 非 作用在**数字**上时，先 roundToLong（四舍五入）再按位运算。
/// ⚠️ 这一类曾经整个缺失 —— 数字「与」直接报参数类型不对。
/// </summary>
public sealed class BitwiseSetArithmetic : IArithmetic
{
    public string Name => "bitwise_set_ops";

    public int Arity(string op) => op switch
    {
        "and" or "or" or "xor" => 2,
        "not" => 1,
        _ => -1,
    };

    /// <summary>Kotlin Double.roundToLong = Math.round：floor(x + 0.5)。</summary>
    private static long L(Iota i) => (long)System.Math.Floor(((DoubleIota)i).Value + 0.5);

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        if (args.Count == 0) return null;
        foreach (var a in args)
        {
            if (a is not DoubleIota) return null;
        }
        long x = L(args[0]);
        if (op == "not") return new Iota[] { new DoubleIota(~x) };
        if (args.Count < 2) return null;
        long y = L(args[1]);
        return op switch
        {
            "and" => new Iota[] { new DoubleIota(x & y) },
            "or" => new Iota[] { new DoubleIota(x | y) },
            "xor" => new Iota[] { new DoubleIota(x ^ y) },
            _ => null,
        };
    }
}

/// <summary>
/// 列表的集合运算。逐行对齐源项目 ListSetArithmetic.kt（相等用 Iota.tolerates）：
///   与 = 交集（保留左表顺序）；或 = 左表 + 右表里左表没有的；异或 = 对称差。
/// 「唯一之纯化」在原版也属于这一类，移植版单独做成了 OpUnique。
/// ⚠️ 这一类曾经整个缺失。
/// </summary>
public sealed class ListSetArithmetic : IArithmetic
{
    public string Name => "list_set_ops";

    public int Arity(string op) => op is "and" or "or" or "xor" ? 2 : -1;

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        if (args.Count != 2 || args[0] is not ListIota l0 || args[1] is not ListIota l1) return null;
        static bool In(IReadOnlyList<Iota> list, Iota x)
        {
            foreach (var y in list)
            {
                if (Iota.Tolerates(x, y)) return true;
            }
            return false;
        }
        var a = l0.Items;
        var b = l1.Items;
        var result = new List<Iota>();
        switch (op)
        {
            case "and":
                foreach (var x in a) { if (In(b, x)) result.Add(x); }
                break;
            case "or":
                result.AddRange(a);
                foreach (var x in b) { if (!In(a, x)) result.Add(x); }
                break;
            case "xor":
                foreach (var x in a) { if (!In(b, x)) result.Add(x); }
                foreach (var x in b) { if (!In(a, x)) result.Add(x); }
                break;
            default:
                return null;
        }
        return new Iota[] { new ListIota(result) };
    }
}
