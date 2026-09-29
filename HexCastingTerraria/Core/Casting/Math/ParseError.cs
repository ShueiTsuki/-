namespace HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// 图案解析失败的位置与原因。
/// 从 HexPattern 里提出来做顶层类型，便于调用方无需限定名就能引用。
/// </summary>
public readonly struct ParseError
{
    public readonly int Index;
    public readonly char Character;
    public readonly string Reason;

    public ParseError(int index, char character, string reason)
    {
        Index = index;
        Character = character;
        Reason = reason;
    }

    public override string ToString() => $"索引 {Index} 处的字符 '{Character}'：{Reason}";
}
