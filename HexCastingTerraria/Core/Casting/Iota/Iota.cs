using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;

namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>
/// Iota 类型标签。对应源项目 IotaType 单例的集合（注册于 HexIotaTypes.java）。
///
/// 共 9 种：Null / Boolean / Double / Vector / Entity / List / Pattern / Garbage / Continuation。
/// 注意源项目里这个集合只有 9 个，但 ListIota 与 ContinuationIota 参与求值的语义最复杂。
/// </summary>
public enum IotaKind
{
    Null = 0,
    Boolean = 1,
    Double = 2,
    Vector = 3,
    Entity = 4,
    List = 5,
    Pattern = 6,
    Garbage = 7,
    Continuation = 8,

    /// <summary>附属登记的 iota 种类（HexParse 注释、HexDebug 认知危害…），见 IotaSerializer.RegisterKind。</summary>
    Addon = 9,

    /// <summary>不认识的种类（多半来自关掉的附属），原样保管，见 UnknownIota。</summary>
    Unknown = 10,
}

/// <summary>
/// 所有 iota 的抽象基类。
/// 移植自 at.petrak.hexcasting.api.casting.Iota（Java）。
///
/// 设计要点（对照源项目）：
///   - iota 是**值**，不可变；任何"修改"都返回新实例
///   - 序列化为纯数据，用于存档与网络传输；反序列化时**不持有任何宿主引用**
///   - 深度与总量有上限（MAX_SERIALIZATION_DEPTH=256 / MAX_SERIALIZATION_TOTAL=1024），
///     防止恶意/循环数据导致栈溢出
/// </summary>
public abstract class Iota
{
    /// <summary>序列化深度上限（源：HexIotaTypes.MAX_SERIALIZATION_DEPTH）。</summary>
    public const int MaxSerializationDepth = 256;

    /// <summary>序列化元素总量上限（源：HexIotaTypes.MAX_SERIALIZATION_TOTAL）。</summary>
    public const int MaxSerializationTotal = 1024;

    public abstract IotaKind Kind { get; }

    /// <summary>是否可作为法术执行（源项目中只有 PatternIota 与 ContinuationIota 为 true）。</summary>
    public virtual bool IsExecutable => false;

    /// <summary>类型名，用于 HUD 与错误提示。</summary>
    public abstract string TypeName { get; }

    /// <summary>值相等（用于图案比较、列表成员判断等）。</summary>
    public abstract bool ValueEquals(Iota other);

    /// <summary>
    /// 序列化为纯数据。返回的必须是可安全存入存档/网络的最小结构，
    /// **不得包含任何 Terraria 宿主对象引用**。
    /// </summary>
    public abstract object? Serialize();

    public override string ToString() => $"{TypeName}({DescribeValue()})";

    protected abstract string DescribeValue();

    /// <summary>给玩家看的值（原版 Iota.display()，用在事故消息里）。</summary>
    public string Display() => DescribeValue();

    // ---- 求值相关（移植自源项目 Iota.java 的 execute / executeInParens）----

    /// <summary>
    /// 直接执行本 iota（即它位于元求值列表中且未被转义时）。
    /// 默认返回 MishapUnescapedValue —— 表示「裸值不能直接执行」。
    /// </summary>
    public virtual CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => new CastResult(
            this,
            continuation,
            null,
            new OperatorSideEffect[]
            {
                new DoMishapSideEffect(
                    new MishapUnescapedValue(this),
                    new MishapContext(null, null)),
            },
            ResolvedPatternType.Invalid,
            EvalSound.Mishap);

    /// <summary>
    /// 在括号内执行本 iota。
    /// 默认行为是把它加入正在构建的括号列表（而不是报错）。
    /// </summary>
    public virtual CastResult ExecuteInParens(CastingVM vm, SpellContinuation continuation)
        => new CastResult(
            this,
            continuation,
            vm.Image.WithNewParenthesized(this, escaped: false),
            System.Array.Empty<OperatorSideEffect>(),
            ResolvedPatternType.Escaped,
            EvalSound.NormalExecute);

    /// <summary>
    /// 容差比较。移植自源项目 `Iota.tolerates`。
    ///
    /// **数值用容差**（`DoubleIota.Tolerance`）而不是精确相等 ——
    /// 浮点经过几轮运算后几乎不可能位级相等，
    /// 精确比较会让 `equals` 这个图案在实际使用中几乎永远为假。
    /// 其它类型退回 `ValueEquals`。
    /// </summary>
    public static bool Tolerates(Iota a, Iota b)
    {
        switch (a, b)
        {
            // 源项目 DoubleIota.tolerates：|a-b| < TOLERANCE（严格小于）
            case (DoubleIota da, DoubleIota db):
                return System.Math.Abs(da.Value - db.Value) < DoubleIota.Tolerance;
            // 源项目 Vec3Iota：距离² < TOLERANCE²（曾经是精确相等）
            case (VectorIota va, VectorIota vb):
            {
                double dx = va.X - vb.X, dy = va.Y - vb.Y, dz = va.Z - vb.Z;
                return dx * dx + dy * dy + dz * dz < DoubleIota.Tolerance * DoubleIota.Tolerance;
            }
            // 源项目 ListIota：长度相同且逐项 tolerates（递归；曾经逐项精确相等）
            case (ListIota la, ListIota lb):
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                {
                    if (!Tolerates(la.Items[i], lb.Items[i])) return false;
                }
                return true;
            }
            // 源项目 PatternIota：只比角度串，**不比起始方向**（曾经连起始方向一起比）
            case (PatternIota pa, PatternIota pb):
                return pa.AnglesSignature == pb.AnglesSignature;
        }
        return a.ValueEquals(b);
    }

    /// <summary>真假值判定（供布尔类图案使用）。</summary>
    public virtual bool IsTruthy() => false;

    // ---- 便捷类型转换（对应源项目的 asXxx 系列，失败返回 null 由调用方抛 Mishap）----

    public virtual bool AsBool() => throw new InvalidCastException($"{TypeName} 不是布尔");
}
