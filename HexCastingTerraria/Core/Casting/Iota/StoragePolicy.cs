namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>数据载体的存储策略。</summary>
public enum StorageKind
{
    /// <summary>什么 iota 都能存（聚念核心 / 念珠）。</summary>
    Any = 0,

    /// <summary>只能存图案（卷轴）。对应源项目 `ItemScroll.canWrite`。</summary>
    PatternOnly = 1,

    /// <summary>只能存数值（算盘）。对应源项目 `ItemAbacus`：只接受 DoubleIota。</summary>
    NumberOnly = 2,

    /// <summary>只能存图案**列表**（法术书的一页）。对应源项目 `ItemSpellbook`。</summary>
    PatternListOnly = 3,
}

/// <summary>
/// 数据载体的存储策略判定。
///
/// 放在 Core 而不是物品类里，是为了能被离线测试覆盖 ——
/// 「卷轴只能存图案」这条约束如果不成立，
/// 墙上挂的卷轴会突然变成「存了一个数字的卷轴」，而且不报错。
/// </summary>
public static class StoragePolicy
{
    /// <summary>是不是「全是图案」的列表（法术书的一页只接受这个）。</summary>
    private static bool IsPatternList(Iota value)
    {
        if (value is not ListIota list) return false;

        for (int i = 0; i < list.Count; i++)
        {
            if (list.Items[i] is not PatternIota) return false;
        }

        return true;
    }

    public static bool CanStore(StorageKind kind, Iota value) => kind switch
    {
        StorageKind.PatternOnly => value is PatternIota,
        StorageKind.NumberOnly => value is DoubleIota,
        StorageKind.PatternListOnly => IsPatternList(value),
        _ => true,
    };
}
