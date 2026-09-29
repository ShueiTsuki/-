namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 一个简单的媒质池实现。玩家自身的媒质储量、物品内的媒质都复用它。
/// </summary>
public sealed class MediaPool : IMediaStorage
{
    private long _media;

    public MediaPool(long maxMedia, long initial = 0, bool canRecharge = true, bool canProvide = true)
    {
        MaxMedia = maxMedia;
        _media = initial < 0 ? 0 : (initial > maxMedia ? maxMedia : initial);
        CanRecharge = canRecharge;
        CanProvide = canProvide;
    }

    public long Media => _media;

    public long MaxMedia { get; }

    public bool CanRecharge { get; }

    public bool CanProvide { get; }

    public long Withdraw(long cost)
    {
        if (cost < 0)
        {
            cost = _media;
        }
        if (cost <= 0)
        {
            return 0;
        }
        long taken = System.Math.Min(cost, _media);
        _media -= taken;
        return taken;
    }

    public long Insert(long amount)
    {
        long space = MaxMedia - _media;
        if (space <= 0)
        {
            return 0;
        }
        if (amount < 0)
        {
            amount = space;
        }
        if (amount <= 0)
        {
            return 0;
        }
        long inserted = System.Math.Min(amount, space);
        _media += inserted;
        return inserted;
    }

    /// <summary>直接设置（读档、调试用）。会夹到 [0, MaxMedia]。</summary>
    public void SetMedia(long value)
    {
        if (value < 0)
        {
            value = 0;
        }
        else if (value > MaxMedia)
        {
            value = MaxMedia;
        }
        _media = value;
    }

    public void Clear() => _media = 0;
}
