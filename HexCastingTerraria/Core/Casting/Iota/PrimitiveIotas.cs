using System;
using System.Collections.Generic;
using System.Globalization;

namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>空值 iota。源：NullIota。单例。</summary>
public sealed class NullIota : Iota
{
    public static readonly NullIota Instance = new();

    private NullIota() { }

    public override IotaKind Kind => IotaKind.Null;

    public override string TypeName => "null";

    public override bool ValueEquals(Iota other) => other is NullIota;

    public override object? Serialize() => IotaSerializer.Envelope(IotaSerializer.KindNull, null);

    protected override string DescribeValue() => "null";
}

/// <summary>布尔 iota。源：BooleanIota。</summary>
public sealed class BooleanIota : Iota
{
    public static readonly BooleanIota True = new(true);
    public static readonly BooleanIota False = new(false);

    public bool Value { get; }

    public BooleanIota(bool value) => Value = value;

    public static BooleanIota Of(bool v) => v ? True : False;

    public override IotaKind Kind => IotaKind.Boolean;

    public override string TypeName => "boolean";

    public override bool ValueEquals(Iota other) => other is BooleanIota b && b.Value == Value;

    /// <summary>真假值：就是这个布尔本身（源项目 `getBool()`）。</summary>
    public override bool IsTruthy() => Value;

    public override object? Serialize() => IotaSerializer.Envelope(IotaSerializer.KindBool, Value);

    public override bool AsBool() => Value;

    protected override string DescribeValue() => Value ? "true" : "false";
}

/// <summary>
/// 双精度浮点 iota。源：DoubleIota。
/// 源项目比较用 TOLERANCE = 0.0001，不是精确相等——移植时必须保留这个容差，
/// 否则图案里的数值比较会出现"看着相等却不相等"。
/// </summary>
public sealed class DoubleIota : Iota
{
    /// <summary>比较容差（源：DoubleIota.TOLERANCE）。</summary>
    public const double Tolerance = 0.0001;

    public double Value { get; }

    public DoubleIota(double value) => Value = HexMathUtil.FixNaN(value);

    public override IotaKind Kind => IotaKind.Double;

    public override string TypeName => "number";

    public override bool ValueEquals(Iota other) => other is DoubleIota d && d.Value == Value;

    public override object? Serialize() => IotaSerializer.Envelope(IotaSerializer.KindDouble, Value);

    /// <summary>
    /// 真假值：**非零为真**（源项目 `getDouble() != 0.0`）。
    ///
    /// 不覆写的话会落到基类的 false —— 于是「非零数」在布尔语境里全变成假，
    /// 而这类错误**不会报错**，只会让条件分支永远走同一边。
    /// </summary>
    public override bool IsTruthy() => Value != 0.0;

    protected override string DescribeValue()
        => Value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// 三维向量 iota。源：Vec3Iota（double 三元组）。
///
/// 泰拉世界是二维的，但**向量保持三维**，与原版一致：±Z 常量、叉积（得向量）、三分量拆装都能用。
/// 世界是 z = 0 的平面（方块沿 z 无限延伸）：世界给出的位置 / 视线 / 速度 z 恒为 0；
/// 作为位置使用时方块查询忽略 z，但施法范围按三维距离算（见 CastingEnvironment.AssertVecInRange）。
/// ⚠️ 这里曾经砍成二维：±Z 常量删了、叉积改成返回数字、拆装只有两个分量。
/// </summary>
public sealed class VectorIota : Iota
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public VectorIota(double x, double y, double z = 0.0)
    {
        // NaN 清洗（源项目 Vec3Iota 同样对每个分量 fixNAN）
        X = HexMathUtil.FixNaN(x);
        Y = HexMathUtil.FixNaN(y);
        Z = HexMathUtil.FixNaN(z);
    }

    public static readonly VectorIota Zero = new(0.0, 0.0, 0.0);
    public static readonly VectorIota UnitX = new(1.0, 0.0);
    public static readonly VectorIota UnitY = new(0.0, 1.0);
    public static readonly VectorIota UnitZ = new(0.0, 0.0, 1.0);
    public static readonly VectorIota NegUnitX = new(-1.0, 0.0);
    public static readonly VectorIota NegUnitY = new(0.0, -1.0);
    public static readonly VectorIota NegUnitZ = new(0.0, 0.0, -1.0);

    public override IotaKind Kind => IotaKind.Vector;

    public override string TypeName => "vector";

    public override bool ValueEquals(Iota other)
        => other is VectorIota v && v.X == X && v.Y == Y && v.Z == Z;

    /// <summary>真假值：**非零向量为真**（源项目 `!(x==0 && y==0 && z==0)`）。</summary>
    public override bool IsTruthy() => X != 0.0 || Y != 0.0 || Z != 0.0;

    public override object? Serialize()
        => IotaSerializer.Envelope(IotaSerializer.KindVec, new List<object?> { X, Y, Z });

    protected override string DescribeValue()
        => string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
}

/// <summary>
/// 实体引用 iota。源：EntityIota。
///
/// 关键设计：**不持有实体对象引用**，只记录「哪一类 + 哪个索引」，
/// 因为 iota 要被序列化进存档与网络包，持有引用既不能序列化也会在实体卸载后悬空。
/// </summary>
public sealed class EntityIota : Iota
{
    public enum EntityKind
    {
        Player = 0,
        Npc = 1,
        Projectile = 2,

        /// <summary>
        /// 掉在地上的物品。
        /// `zone_entity/item` 要用它 —— 源项目那边是 MC 的 `ItemEntity`。
        /// </summary>
        Item = 3,
    }

    public EntityKind Target { get; }

    /// <summary>对应 whoAmI 索引。源项目用 UUID，泰拉侧索引更自然，但需在解析时校验存活。</summary>
    public int Index { get; }

    public EntityIota(EntityKind target, int index)
    {
        Target = target;
        Index = index;
    }

    public override IotaKind Kind => IotaKind.Entity;

    public override string TypeName => "entity";

    public override bool ValueEquals(Iota other)
        => other is EntityIota e && e.Target == Target && e.Index == Index;

    /// <summary>真假值：实体引用**恒为真**（源项目 `return true`）。</summary>
    public override bool IsTruthy() => true;

    public override object? Serialize()
        => IotaSerializer.Envelope(IotaSerializer.KindEntity,
            new List<object?> { (double)Target, (double)Index });

    protected override string DescribeValue() => $"{Target}#{Index}";
}

/// <summary>
/// 垃圾 iota。源：GarbageIota。
/// 表示"这个值已被消费/不可用"，出现在错误恢复与某些图案的占位语义中。
/// </summary>
public sealed class GarbageIota : Iota
{
    public static readonly GarbageIota Instance = new();

    private GarbageIota() { }

    public override IotaKind Kind => IotaKind.Garbage;

    public override string TypeName => "garbage";

    public override bool ValueEquals(Iota other) => other is GarbageIota;

    public override object? Serialize() => IotaSerializer.Envelope(IotaSerializer.KindGarbage, null);

    protected override string DescribeValue() => "garbage";
}
