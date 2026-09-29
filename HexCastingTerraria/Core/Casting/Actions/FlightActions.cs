using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

// 源项目 `spells/OpFlight.kt` + `spells/great/OpAltiora.kt` + `queryentity/OpCanEntityHexFly.kt`。
//
// 四个图案其实说的是同一件事的三个侧面：
//   `flight`       —— Altiora：向上弹一下，落地前一直能飞（不限时不限距）
//   `flight/range` —— 以**施法点**为心画一个圈，出圈就掉
//   `flight/time`  —— 限时飞行
//   `flight/can_fly` —— 问「这个玩家现在有没有咒法飞行」
//
// 原版把它们放在两个文件里（`great/` 那个是启蒙后的大法术），这里合并成一个文件，
// 因为**危险度计算是共用的** —— 拆开会导致两处各写一遍，然后慢慢漂移。

/// <summary>
/// 飞行的「危险度」计算。移植自源项目 `OpFlight.getDanger`。
///
/// 返回值 0 = 一切正常，1 = 该结束飞行了。中间值是给粒子表现用的
/// （越接近 1，粒子越偏红黑 —— 那是「你快掉下去了」的预警）。
///
/// 放在 Core 里当纯函数，是因为这段逻辑**只在边缘情况下才出错**：
/// 圈画得刚好、时间剩几秒 —— 那种时候没人会去游戏里一点点试。
/// </summary>
public static class FlightDanger
{
    /// <summary>距边缘多少格以内开始预警。源项目 `DIST_DANGER_THRESHOLD = 4.0`。</summary>
    public const double DistThreshold = 4.0;

    /// <summary>剩余多少 tick 以内开始预警。源项目 `TIME_DANGER_THRESHOLD = 7 * 20`（7 秒）。</summary>
    public const int TimeThresholdTicks = 7 * 20;

    /// <summary>
    /// <paramref name="radius"/> 或 <paramref name="timeLeftTicks"/> 为负表示**那一项不限**。
    /// </summary>
    public static double Compute(double distanceFromOrigin, double radius, int timeLeftTicks)
    {
        double radiusDanger = 0.0;
        if (radius >= 0.0)
        {
            double fromEdge = radius - distanceFromOrigin;
            if (fromEdge >= DistThreshold)
            {
                radiusDanger = 0.0;
            }
            else if (distanceFromOrigin > radius)
            {
                radiusDanger = 1.0;
            }
            else
            {
                radiusDanger = 1.0 - (fromEdge / DistThreshold);
            }
        }

        double timeDanger = 0.0;
        if (timeLeftTicks >= 0)
        {
            timeDanger = timeLeftTicks >= TimeThresholdTicks
                ? 0.0
                : (TimeThresholdTicks - timeLeftTicks) / (double)TimeThresholdTicks;
        }

        return System.Math.Max(radiusDanger, timeDanger);
    }
}

/// <summary>
/// `flight`（Altiora）：把目标玩家向上弹起，并给一段咒法飞行。
/// 移植自源项目 `OpAltiora`（大法术，需要启蒙）。
///
/// 消耗 `CRYSTAL_UNIT`（10 万）—— 大法术里最便宜的一个，因为它的效果是**临时的**
/// 且落地就结束；相比 `flight/range` 那种「按格收费」的玩法，这个是一次性推进器。
///
/// 源项目的实现有三个细节，都照抄了：
///   ① 先 `push(0, 1.5, 0)` 再给能力 —— 顺序反了会在原地起飞，手感完全不同
///   ② 20 tick 的宽限期：刚起飞时脚还贴着地，没有宽限会被立刻判定为「已落地」
///   ③ 目标**已经有飞行能力时不覆盖**（源项目注释：别把别人的飞行搞没了）
/// </summary>
public sealed class OpAltiora : SpellAction
{
    /// <summary>宽限期。源项目 `GRACE_PERIOD = 20`（1 秒）。</summary>
    public const int GracePeriodTicks = 20;

    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);
        if (target.Target != EntityIota.EntityKind.Player)
        {
            throw new MishapInvalidIota(args[0], "玩家");
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w =>
            {
                if (w.HasHexFlight(target)) return;   // 见细节 ③

                w.LaunchUp(target);
                w.GrantFlight(target, ticks: -1, originX: 0, originY: 0, radius: -1,
                    graceTicks: GracePeriodTicks);
            }),
            MediaConstants.CrystalUnit,
            new[]
            {
                ParticleSpray.Burst(env.RequireWorld().FeetPosition(target).X,
                    env.RequireWorld().FeetPosition(target).Y, spread: 0.5f, count: 30),
            });
    }
}

/// <summary>
/// `flight/range`（`Type.LimitRange`）与 `flight/time`（`Type.LimitTime`）。
/// 移植自源项目 `OpFlight`。
///
/// 计价：**每格半径 / 每秒 2 粉尘单位**（`2 × DUST_UNIT`）。
/// `range` 版有下限「至少 1 格的钱」（源项目 `cost = max(cost, costUnit)`），
/// `time` 版没有下限 —— 这个不对称是源码里写死的，不是笔误。
/// </summary>
public sealed class OpFlight : SpellAction
{
    /// <summary>每格半径 / 每秒的媒质消耗。源项目 `costUnit = 2 * DUST_UNIT`。</summary>
    public const long CostPerUnit = 2 * MediaConstants.DustUnit;

    private readonly bool _limitRange;

    public OpFlight(bool limitRange) => _limitRange = limitRange;

    public override int Argc => 2;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);
        if (target.Target != EntityIota.EntityKind.Player)
        {
            throw new MishapInvalidIota(args[0], "玩家");
        }

        double amount = CastingEnvironment.RequireDouble(args[1], "半径 / 秒数");
        if (amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new MishapInvalidIota(args[1], "正数");
        }

        long cost = (long)System.Math.Round(amount * CostPerUnit);
        if (_limitRange)
        {
            cost = System.Math.Max(cost, CostPerUnit);
        }

        var world = env.RequireWorld();
        var (fx, fy) = world.FeetPosition(target);

        return WorldSpell.Make(
            new WorldSpell.Simple(w =>
            {
                if (w.HasHexFlight(target)) return;

                if (_limitRange)
                {
                    w.GrantFlight(target, ticks: -1, originX: fx, originY: fy, radius: amount, graceTicks: 0);
                }
                else
                {
                    // MC 是 20 tick/秒
                    int ticks = (int)System.Math.Round(amount * 20.0);
                    w.GrantFlight(target, ticks: ticks, originX: fx, originY: fy, radius: -1, graceTicks: 0);
                }
            }),
            cost,
            new[] { ParticleSpray.Cloud(fx, fy, spread: 0.0f, count: 10) });
    }
}

/// <summary>
/// `flight/can_fly`：目标玩家现在有没有咒法飞行（压布尔）。
/// 移植自源项目 `OpCanEntityHexFly`。消耗 0（只读查询）。
/// </summary>
public sealed class OpCanEntityHexFly : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);
        return new Iota[] { BooleanIota.Of(env.RequireWorld().HasHexFlight(target)) };
    }
}

/// <summary>飞行类图案的注册。</summary>
public static class FlightActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:flight", new OpAltiora());
        PatternRegistry.RegisterAction("hexcasting:flight/range", new OpFlight(limitRange: true));
        PatternRegistry.RegisterAction("hexcasting:flight/time", new OpFlight(limitRange: false));
        PatternRegistry.RegisterAction("hexcasting:flight/can_fly", new OpCanEntityHexFly());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
