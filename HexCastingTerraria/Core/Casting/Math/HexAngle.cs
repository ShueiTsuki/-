namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 六种转向角。顺序与源项目一致，序号参与模运算，不可重排。
/// 源：at.petrak.hexcasting.api.casting.math.HexAngle
/// </summary>
public enum HexAngle
{
    Forward = 0,
    Right = 1,
    RightBack = 2,
    Back = 3,
    LeftBack = 4,
    Left = 5,
}

public static class HexAngleExtensions
{
    public const int Count = 6;

    /// <summary>角度叠加即模 6 相加。</summary>
    public static HexAngle RotatedBy(this HexAngle self, HexAngle other)
        => (HexAngle)(((int)self + (int)other) % Count);

    /// <summary>解析角度签名字符；非法字符返回 null。</summary>
    public static HexAngle? FromChar(char c) => c switch
    {
        'w' => HexAngle.Forward,
        'e' => HexAngle.Right,
        'd' => HexAngle.RightBack,
        's' => HexAngle.Back,
        'a' => HexAngle.LeftBack,
        'q' => HexAngle.Left,
        _ => null,
    };

    /// <summary>角度签名用的单字符表示。</summary>
    public static char ToChar(this HexAngle self) => self switch
    {
        HexAngle.Forward => 'w',
        HexAngle.Right => 'e',
        HexAngle.RightBack => 'd',
        HexAngle.Back => 's',
        HexAngle.LeftBack => 'a',
        HexAngle.Left => 'q',
        _ => '?',
    };
}
