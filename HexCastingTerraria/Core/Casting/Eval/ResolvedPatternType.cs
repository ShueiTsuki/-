namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 一条图案被求值后的解析状态。
/// 移植自 at.petrak.hexcasting.api.casting.eval.ResolvedPatternType。
/// </summary>
public enum ResolvedPatternType
{
    Unresolved = 0,
    Evaluated = 1,
    Escaped = 2,
    Undone = 3,
    Errored = 4,
    Invalid = 5,
}

public static class ResolvedPatternTypeExtensions
{
    /// <summary>是否表示「成功」执行。源项目里 success 字段。</summary>
    public static bool IsSuccess(this ResolvedPatternType t)
        => t is ResolvedPatternType.Evaluated or ResolvedPatternType.Escaped or ResolvedPatternType.Undone;
}
