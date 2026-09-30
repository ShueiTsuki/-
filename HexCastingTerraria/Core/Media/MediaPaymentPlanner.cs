using System;
using System.Collections.Generic;

namespace HexCastingTerraria.Core.Media;

/// <summary>
/// 扣费优先级。逐字照抄源项目 ADMediaHolder：数值越大越先扣。
/// </summary>
public static class MediaPriority
{
    public const int QuenchedAllay = 800;
    public const int QuenchedShard = 900;
    public const int ChargedAmethyst = 1000;
    public const int AmethystShard = 2000;
    public const int AmethystDust = 3000;
    public const int Battery = 4000;
}

/// <summary>
/// 背包里一个可提供媒质的物品槽（源项目 ADMediaHolder）。两种：
///   - 堆叠物品（粉、碎片、充能紫水晶……）：<see cref="UnitValue"/> × <see cref="Count"/>，**按整件扣**；
///   - 媒质瓶（battery）：<see cref="UnitValue"/> = 0，按 <see cref="Stored"/> **按量扣**。
/// </summary>
public readonly struct MediaSource
{
    public required int Slot { get; init; }

    public required int Priority { get; init; }

    /// <summary>堆叠物品的单件媒质；媒质瓶为 0。</summary>
    public long UnitValue { get; init; }

    /// <summary>堆叠数量（媒质瓶忽略）。</summary>
    public int Count { get; init; }

    /// <summary>媒质瓶当前存量（堆叠物品忽略）。</summary>
    public long Stored { get; init; }

    public bool IsBattery => UnitValue <= 0;

    /// <summary>源项目 withdrawMedia(-1, simulate=true)：这一槽总共能出多少。</summary>
    public long Total => IsBattery ? Math.Max(0, Stored) : UnitValue * Math.Max(0, Count);
}

/// <summary>从某一槽扣多少：堆叠物品扣 <see cref="Items"/> 件，媒质瓶扣 <see cref="BatteryMedia"/>。</summary>
public readonly record struct MediaWithdrawal(int Slot, int Items, long BatteryMedia);

/// <summary>一次媒质支付方案。</summary>
public sealed class MediaPaymentPlan
{
    public required IReadOnlyList<MediaWithdrawal> Withdrawals { get; init; }

    /// <summary>
    /// 整件扣多出来的部分。源项目里**直接浪费**（stack.shrink 整件，多出的媒质不找零）。
    /// </summary>
    public required long Wasted { get; init; }

    /// <summary>仍未付清的缺口（由调用方决定要不要过载）。</summary>
    public required long Shortfall { get; init; }

    /// <summary>所有来源的媒质总量。</summary>
    public required long TotalAvailable { get; init; }
}

/// <summary>
/// 媒质支付规划。移植自源项目 PlayerBasedCastEnv.extractMediaFromInventory + MediaHelper：
///
///   1. scanPlayerForMediaStuff：按 compareMediaItem 排序 —— **优先级高的先扣**
///     （媒质瓶 4000 → 紫水晶粉 3000 → 碎片 2000 → 充能紫水晶 1000 → 淬灵碎片 900），
///      同优先级时**总量大的先扣**；
///   2. 逐个 withdrawMedia：堆叠物品 itemsUsed = min(ceil(剩余 / 单件), 数量)，整件扣、多付浪费；
///      媒质瓶扣 min(剩余, 存量)；
///   3. 还不够的部分交给过载。
///
/// ⚠️ 这里曾经有一个「玩家媒质池」最先扣、整件多付的部分找零回池 —— 原版没有这个池子，已删除。
/// </summary>
public static class MediaPaymentPlanner
{
    public static MediaPaymentPlan Plan(long cost, IReadOnlyList<MediaSource> sources)
    {
        if (sources == null) throw new ArgumentNullException(nameof(sources));
        cost = Math.Max(0, cost);

        var usable = new List<MediaSource>();
        long total = 0;
        foreach (var s in sources)
        {
            if (s.Total <= 0) continue;
            usable.Add(s);
            total = s.Total > long.MaxValue - total ? long.MaxValue : total + s.Total;   // 媒质立方是 long.MaxValue
        }

        if (cost == 0)
        {
            return new MediaPaymentPlan
            {
                Withdrawals = Array.Empty<MediaWithdrawal>(),
                Wasted = 0,
                Shortfall = 0,
                TotalAvailable = total,
            };
        }

        // 源项目：sortWith(compareMediaItem) 再 reverse → 优先级高的在前；同优先级总量大的在前。
        // 槽位作为最后的决胜，保证结果确定。
        usable.Sort((a, b) =>
        {
            int c = b.Priority.CompareTo(a.Priority);
            if (c != 0) return c;
            c = b.Total.CompareTo(a.Total);
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);
        });

        long remaining = cost;
        var taken = new List<MediaWithdrawal>();
        foreach (var s in usable)
        {
            if (remaining <= 0) break;
            if (s.IsBattery)
            {
                long take = Math.Min(remaining, s.Stored);
                taken.Add(new MediaWithdrawal(s.Slot, 0, take));
                remaining -= take;
            }
            else
            {
                long need = (remaining + s.UnitValue - 1) / s.UnitValue;
                int items = (int)Math.Min(need, s.Count);
                taken.Add(new MediaWithdrawal(s.Slot, items, 0));
                remaining -= items * s.UnitValue;
            }
        }

        return new MediaPaymentPlan
        {
            Withdrawals = taken,
            Wasted = remaining < 0 ? -remaining : 0,
            Shortfall = remaining > 0 ? remaining : 0,
            TotalAvailable = total,
        };
    }
}
