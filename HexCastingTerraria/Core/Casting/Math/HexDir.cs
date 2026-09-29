namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 六边形网格的六个方向。顺序与源项目一致（顺时针，从东北开始），
/// 序号参与模运算，不可重排。
/// 源：at.petrak.hexcasting.api.casting.math.HexDir
/// </summary>
public enum HexDir
{
    NorthEast = 0,
    East = 1,
    SouthEast = 2,
    SouthWest = 3,
    West = 4,
    NorthWest = 5,
}

public static class HexDirExtensions
{
    public const int Count = 6;

    /// <summary>方向按角度旋转，等价于模 6 加。</summary>
    public static HexDir RotatedBy(this HexDir self, HexAngle angle)
        => (HexDir)(((int)self + (int)angle) % Count);

    /// <summary>从 other 转到 self 需要的角度。</summary>
    public static HexAngle AngleFrom(this HexDir self, HexDir other)
        => (HexAngle)((((int)self - (int)other) % Count + Count) % Count);

    /// <summary>该方向在轴坐标下的位移增量。</summary>
    public static HexCoord AsDelta(this HexDir self) => self switch
    {
        HexDir.NorthEast => new HexCoord(1, -1),
        HexDir.East => new HexCoord(1, 0),
        HexDir.SouthEast => new HexCoord(0, 1),
        HexDir.SouthWest => new HexCoord(-1, 1),
        HexDir.West => new HexCoord(-1, 0),
        HexDir.NorthWest => new HexCoord(0, -1),
        _ => new HexCoord(0, 0),
    };

    /// <summary>源项目里解析失败会回退到 WEST，此处保持一致。</summary>
    public static HexDir FromString(string key)
        => System.Enum.TryParse<HexDir>(key, ignoreCase: true, out var v) ? v : HexDir.West;
}
