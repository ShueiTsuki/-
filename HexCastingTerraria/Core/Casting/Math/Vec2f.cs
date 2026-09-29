namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 最小二维浮点向量：只有 `Core` 真正用到的那几个操作。
///
/// ## 为什么要自己定一个，而不是直接用 XNA 的 `Vector2`
///
/// `Core/` 有一条硬约束：**不得引用 XNA / tModLoader**（这样整个 Core 才能在无头环境下跑测试）。
/// 而 `HexGrid` 之前直接用了 `Microsoft.Xna.Framework.Vector2`，后果是它被排除在离线测试工程之外 ——
/// **坐标换算（CoordToPx / PxToCoord / PatternLinePoints）从来没有被真正测过**：
/// 验证工程里测的是手抄的一份公式副本，真代码里改错一个符号，测试照样全绿。
/// 这是最典型的一类"假安心"，比没有测试更危险。
///
/// 有了这个类型，`HexGrid` 就能回到测试网里，测的就是真代码。
///
/// ## 设计约束
///
/// 只实现 `Core` 实际用到的成员，**不加没用的东西** ——
/// 每多一个成员就多一处要维护、要测的表面。需要新操作时再加，并同时补测试。
/// </summary>
public readonly struct Vec2f : System.IEquatable<Vec2f>
{
    public readonly float X;
    public readonly float Y;

    public Vec2f(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2f Zero = new(0f, 0f);

    public static Vec2f operator +(Vec2f a, Vec2f b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2f operator -(Vec2f a, Vec2f b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2f operator *(Vec2f a, float k) => new(a.X * k, a.Y * k);

    public static Vec2f operator /(Vec2f a, float k) => new(a.X / k, a.Y / k);

    public float LengthSquared => X * X + Y * Y;

    public float Length => System.MathF.Sqrt(LengthSquared);

    /// <summary>两点距离的平方。比较距离时优先用它，省一次开方。</summary>
    public float DistanceSquaredTo(Vec2f other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 单位化。**零向量原样返回零向量**，不兜底成 (1,0) ——
    /// 兜底会把「方向未知」悄悄变成「向右」，调用方再也发现不了自己拿到了退化输入。
    /// 需要兜底的调用方自己判断 <see cref="LengthSquared"/>。
    /// </summary>
    public Vec2f Normalized()
    {
        float len = Length;
        return len < 1e-9f ? Zero : new Vec2f(X / len, Y / len);
    }

    public bool Equals(Vec2f other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is Vec2f v && Equals(v);

    public override int GetHashCode() => System.HashCode.Combine(X, Y);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
