namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 六边形网格上的轴坐标（axial coordinate）。
/// 源：at.petrak.hexcasting.api.casting.math.HexCoord
/// </summary>
public readonly struct HexCoord : System.IEquatable<HexCoord>
{
    public readonly int X;
    public readonly int Y;

    public HexCoord(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static HexCoord Origin => new HexCoord(0, 0);

    public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.X + b.X, a.Y + b.Y);

    public static HexCoord operator +(HexCoord a, HexDir dir) => a + dir.AsDelta();

    public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.X - b.X, a.Y - b.Y);

    public bool Equals(HexCoord other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is HexCoord c && Equals(c);

    public override int GetHashCode() => System.HashCode.Combine(X, Y);

    public override string ToString() => $"({X}, {Y})";
}
