namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 媒质容器。移植自 at.petrak.hexcasting.api.addldata.ADMediaHolder 与
/// api.item.MediaHolderItem 的合并语义（源项目因要适配 Forge/Fabric 两套能力系统
/// 才拆成两层，C# 侧合并为一个接口）。
///
/// 约定：
///   - <see cref="Withdraw"/> 传入负数表示「尽量全取」；返回实际取到的量，不足不报错。
///   - <see cref="Insert"/> 传入负数表示「尽量全存」；返回实际存入的量。
///   - 是否够用由调用方判断并抛 <c>MishapNotEnoughMedia</c>（与源项目一致）。
/// </summary>
public interface IMediaStorage
{
    /// <summary>当前媒质。</summary>
    long Media { get; }

    /// <summary>容量上限。</summary>
    long MaxMedia { get; }

    /// <summary>能否被外部充能。</summary>
    bool CanRecharge { get; }

    /// <summary>能否向外提供媒质。</summary>
    bool CanProvide { get; }

    /// <summary>储量比例 0~1。</summary>
    float Fullness => MaxMedia == 0 ? 0f : (float)((double)Media / MaxMedia);

    /// <summary>取出媒质。cost &lt; 0 表示尽量全取。返回实际取出量。</summary>
    long Withdraw(long cost);

    /// <summary>存入媒质。amount &lt; 0 表示尽量全存。返回实际存入量。</summary>
    long Insert(long amount);

    /// <summary>试算能否取出指定量（不改变状态）。</summary>
    bool CanWithdraw(long cost) => Media >= cost;

    /// <summary>便捷方法：够则扣并返回 true；不够返回 false 且不改变状态。</summary>
    bool TrySpend(long cost)
    {
        if (cost <= 0)
        {
            return true;
        }
        if (!CanWithdraw(cost))
        {
            return false;
        }
        Withdraw(cost);
        return true;
    }
}
