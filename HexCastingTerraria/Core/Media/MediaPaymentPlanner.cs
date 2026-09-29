using System;
using System.Collections.Generic;

namespace HexCastingTerraria.Core.Media;

/// <summary>背包里一堆可提供媒质的物品。</summary>
public readonly struct MediaStack
{
    /// <summary>背包槽位下标。</summary>
    public required int Slot { get; init; }

    /// <summary>单件蕴含的媒质量。</summary>
    public required long UnitValue { get; init; }

    /// <summary>该槽位的堆叠数量。</summary>
    public required int Count { get; init; }
}

/// <summary>一次媒质支付方案。</summary>
public sealed class MediaPaymentPlan
{
    /// <summary>从玩家自身媒质池扣除的量。</summary>
    public required long FromPool { get; init; }

    /// <summary>要从背包扣除的物品（槽位, 件数）。</summary>
    public required IReadOnlyList<(int Slot, int Count)> FromItems { get; init; }

    /// <summary>
    /// 物品**多付**的部分，应当退回媒质池。
    ///
    /// 为什么会有多付：泰拉的堆叠物品没有「单件独立数据」，
    /// 没法像源项目那样把一件粉尘扣到只剩 5,000。
    /// 所以只能整件消耗 —— 多出来的量退回池中，玩家不亏。
    /// </summary>
    public required long Change { get; init; }

    /// <summary>仍未付清的缺口，由过载（扣血）承担。</summary>
    public required long Shortfall { get; init; }

    /// <summary>池 + 背包物品的媒质总量。用于快速判断「够不够」。</summary>
    public required long TotalAvailable { get; init; }

    /// <summary>是否完全不用过载。</summary>
    public bool CoveredWithoutOvercast => Shortfall <= 0;
}

/// <summary>
/// 媒质支付规划。移植自源项目 `MediaHolderEnv` 的取值逻辑
/// （那里是为 `ItemMediaHolder` 逐个 `withdrawMedia`）。
///
/// 放在 Core 且写成纯函数，是因为这里有真实的边界情况值得离线钉住：
/// 整件消耗造成的多付、缺口如何划分、以及**顺序必须是池 → 物品 → 过载**。
/// </summary>
public static class MediaPaymentPlanner
{
    /// <summary>
    /// 规划一次支付。
    ///
    /// 取值顺序：
    ///   ① 自身媒质池
    ///   ② 背包物品，**按面额升序** —— 优先用小的，
    ///      否则为了 1 万媒质就得拆掉一个 30 万的淬灵晶
    ///   ③ 剩余缺口（由调用方决定是否过载）
    /// </summary>
    /// <param name="cost">需要的媒质总量。</param>
    /// <param name="poolMedia">玩家自身池中的媒质。</param>
    /// <param name="items">背包中的媒质材料。</param>
    public static MediaPaymentPlan Plan(long cost, long poolMedia, IReadOnlyList<MediaStack> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        cost = System.Math.Max(0, cost);
        poolMedia = System.Math.Max(0, poolMedia);

        long totalAvailable = poolMedia;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Count > 0 && items[i].UnitValue > 0)
            {
                totalAvailable += items[i].UnitValue * items[i].Count;
            }
        }

        if (cost == 0)
        {
            return new MediaPaymentPlan
            {
                FromPool = 0,
                FromItems = Array.Empty<(int, int)>(),
                Change = 0,
                Shortfall = 0,
                TotalAvailable = totalAvailable,
            };
        }

        long remaining = cost;

        // ① 自身池
        long fromPool = System.Math.Min(remaining, poolMedia);
        remaining -= fromPool;

        // ② 背包物品：面额升序
        var candidates = new List<MediaStack>();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Count > 0 && it.UnitValue > 0)
            {
                candidates.Add(it);
            }
        }
        candidates.Sort((a, b) =>
        {
            int c = a.UnitValue.CompareTo(b.UnitValue);
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);   // 同面额按槽位，保证确定性
        });

        var taken = new List<(int Slot, int Count)>();
        for (int i = 0; i < candidates.Count && remaining > 0; i++)
        {
            var it = candidates[i];

            // 需要几件？向上取整
            long need = (remaining + it.UnitValue - 1) / it.UnitValue;
            int take = (int)System.Math.Min(need, it.Count);
            if (take <= 0) continue;

            taken.Add((it.Slot, take));
            remaining -= (long)take * it.UnitValue;
        }

        long change = remaining < 0 ? -remaining : 0;
        long shortfall = remaining > 0 ? remaining : 0;

        return new MediaPaymentPlan
        {
            FromPool = fromPool,
            FromItems = taken,
            Change = change,
            Shortfall = shortfall,
            TotalAvailable = totalAvailable,
        };
    }
}
