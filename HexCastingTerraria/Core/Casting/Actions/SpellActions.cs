using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `add_motion`：给目标实体施加一次推力。
/// 移植自源项目 spells/OpAddMotion.kt。
///
/// 消耗按**速度平方**计价（`|motion|² × 粉尘`），所以推得越猛越贵。
/// </summary>
public sealed class OpAddMotion : SpellAction
{
    public override int Argc => 2;

    public override ActionTypes Types => ActionTypes.Of(null, IotaTypes.Entity, IotaTypes.Vec);

    /// <summary>
    /// 单次推动的速度上限。源项目为 bug #387 加的硬上限 ——
    /// 没有它可以用一次极猛的推动把实体甩出世界，或者直接把游戏搞崩。
    /// </summary>
    public const double MaxMotion = 8192.0;

    public override SpellResult ExecuteWithUserdata(
        IReadOnlyList<Iota> args, CastingEnvironment env, CastUserData userData)
    {
        var target = env.ResolveEntity(args[0]);
        var (mx, my, mz) = CastingEnvironment.RequireVec3(args[1], "推力");

        // 计价用的是**原始** motion 的长度平方（三维，源项目 motion.lengthSqr()），不是被截断后的。
        // z 分量在二维世界里推不动任何东西，但原版照样按它收费。
        double motionForCost = mx * mx + my * my + mz * mz;

        // 源项目 bug #387 的防刷机制：同一目标重复推动，每次多收 1 粉尘。
        // 没有它，可以用许多次极小的推动把速度堆起来，而每次消耗都趋近于零。
        if (CastingImage.CheckAndMarkGivenMotion(userData, target))
        {
            motionForCost += 1.0;
        }

        // 超过上限就按方向截断到上限长度
        double lenSq = mx * mx + my * my;
        double outX = mx, outY = my;
        if (lenSq > MaxMotion * MaxMotion)
        {
            double len = System.Math.Sqrt(lenSq);
            outX = mx / len * MaxMotion;
            outY = my / len * MaxMotion;
        }

        var world = env.RequireWorld();
        var (tx, ty) = world.FeetPosition(target);

        return new SpellResult
        {
            Effect = new PushSpell(target, outX, outY),
            Cost = (long)(motionForCost * MediaConstants.DustUnit),
            // 粒子从目标身上喷出，方向沿推力方向
            Particles = new[]
            {
                ParticleSpray.Burst(tx, ty, spread: 0.1f, count: 20),
            },
        };
    }

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        // 源项目：直接调 execute 会抛 IllegalStateException。
        // 我们这里给个空数据袋走同一条路径，行为一致但不会炸。
        => ExecuteWithUserdata(args, env, new CastUserData());

    /// <summary>真正的世界改动：推动实体。</summary>
    private sealed class PushSpell : IRenderedSpell
    {
        private readonly EntityIota _target;
        private readonly double _mx;
        private readonly double _my;

        public PushSpell(EntityIota target, double mx, double my)
        {
            _target = target;
            _mx = mx;
            _my = my;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            // 延迟施放期间实体可能已经死了 —— 静默跳过，不报错。
            // 这不是失误：媒质已经扣了，此时报 mishap 反而会让玩家困惑。
            env.World?.ApplyMotion(_target, _mx, _my);
            return null;
        }
    }
}

/// <summary>
/// `blink`：让目标沿**自身视线**瞬移一段距离。
/// 移植自源项目 spells/OpBlink.kt。
///
/// 与 `add_motion` 不同，这里方向不是参数而是**目标的视线** ——
/// 泰拉侧由 `LookResolver` 提供（玩家=冻结的鼠标方向，NPC=速度/目标/缓存）。
/// </summary>
public sealed class OpBlink : SpellAction
{
    public override int Argc => 2;

    public override ActionTypes Types => ActionTypes.Of(null, IotaTypes.Entity, IotaTypes.Num);

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);
        var delta = CastingEnvironment.RequireDouble(args[1], "距离");

        var world = env.RequireWorld();
        if (world.IsTeleportImmune(target))
        {
            throw new MishapImmuneEntity(target);
        }

        // 源项目：dvec = getEntityLookDirSpecial(target).scale(delta)
        var (lx, ly) = world.Look(target);
        double dvecX = lx * delta;
        double dvecY = ly * delta;

        var (fx, fy) = world.FeetPosition(target);

        // 起点与终点都必须仍在施法范围内 ——
        // 只查起点的话，站在范围边缘可以把自己传送到半张地图外
        env.AssertVecInRange(fx, fy);
        env.AssertVecInRange(fx + dvecX, fy + dvecY);

        if (!env.World!.IsVecInWorld(fx + dvecX, fy + dvecY))
        {
            throw new MishapBadLocation(fx + dvecX, fy + dvecY, "太靠近世界边界");
        }

        return new SpellResult
        {
            Effect = new BlinkSpell(target, dvecX, dvecY, fx, fy),
            // 源项目：(SHARD_UNIT × |delta| × 0.5).roundToLong()
            Cost = (long)System.Math.Round(MediaConstants.ShardUnit * System.Math.Abs(delta) * 0.5),
            Particles = new[]
            {
                // 出发点一团云、落点一圈爆开 —— 让玩家看清从哪到哪
                ParticleSpray.Cloud(fx, fy, spread: 2.0f, count: 50),
                ParticleSpray.Burst(fx + dvecX, fy + dvecY, spread: 2.0f, count: 100),
            },
        };
    }

    private sealed class BlinkSpell : IRenderedSpell
    {
        private readonly EntityIota _target;
        private readonly double _dx;
        private readonly double _dy;

        public BlinkSpell(EntityIota target, double dx, double dy, double fx, double fy)
        {
            _target = target;
            _dx = dx;
            _dy = dy;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            env.World?.TeleportBy(_target, _dx, _dy);
            return null;
        }
    }
}

/// <summary>
/// `teleport/great`：把一个实体瞬移一段**位移向量**（而不是沿自身视线）。
/// 移植自源项目 spells/great/OpTeleport.kt。
///
/// 与 `blink` 的三个区别：
///   ① 方向是**参数**而不是目标的视线 —— 可以朝任意方向传，包括队友
///   ② 消耗极高：**10 晶体 = 1,000,000 媒质**（blink 只要几千）
///   ③ **有代价**：按距离概率把施法者自己的物品震落在地
///      （源项目 `doesGreaterTeleportSplatItems`，默认开启）
///
/// 这是「大法术」之一，需要启蒙才能使用（见 PatternRegistry.EnlightenmentRequired）。
/// </summary>
public sealed class OpTeleport : SpellAction
{
    public override int Argc => 2;

    /// <summary>媒质消耗：10 晶体。源项目 `10 * MediaConstants.CRYSTAL_UNIT`。</summary>
    public const long Cost = 10 * MediaConstants.CrystalUnit;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var teleportee = env.ResolveEntity(args[0]);
        var (dx, dy) = CastingEnvironment.RequireVec(args[1], "位移");

        var world = env.RequireWorld();
        if (world.IsTeleportImmune(teleportee))
        {
            throw new MishapImmuneEntity(teleportee);
        }
        var (fx, fy) = world.FeetPosition(teleportee);

        double targetX = fx + dx;
        double targetY = fy + dy;

        // 目标点必须在世界内
        if (!world.IsVecInWorld(targetX, targetY))
        {
            throw new MishapBadLocation(targetX, targetY, "在世界之外");
        }

        // 目标点的**正下方一格**也必须在世界内 ——
        // 否则会把实体送到世界底部之外，它会一直往下掉、
        // 再也回不来（源项目同样检查这一条，理由写在 "too_close_to_out" 里）
        if (!world.IsVecInWorld(targetX, targetY - 1.0))
        {
            throw new MishapBadLocation(targetX, targetY, "太靠近世界边界");
        }

        double distance = System.Math.Sqrt(dx * dx + dy * dy);

        return new SpellResult
        {
            Effect = new TeleportSpell(teleportee, dx, dy, distance),
            Cost = Cost,
            Particles = new[]
            {
                ParticleSpray.Cloud(fx, fy, spread: 2.0f, count: 50),
                ParticleSpray.Burst(targetX, targetY, spread: 2.0f, count: 100),
            },
        };
    }

    private sealed class TeleportSpell : IRenderedSpell
    {
        private readonly EntityIota _target;
        private readonly double _dx;
        private readonly double _dy;
        private readonly double _distance;

        public TeleportSpell(EntityIota target, double dx, double dy, double distance)
        {
            _target = target;
            _dx = dx;
            _dy = dy;
            _distance = distance;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            var world = env.World;
            if (world == null)
            {
                return null;
            }

            world.TeleportBy(_target, _dx, _dy);

            // 代价：按距离震落施法者自己的物品。
            // 只在启用时执行 —— 这是世界规则，由服务端配置决定。
            if (GreatTeleportRules.DropsItems())
            {
                world.ScatterInventory(_target, _distance);
            }

            return null;
        }
    }
}

/// <summary>法术类图案的注册。</summary>
public static class SpellActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:add_motion", new OpAddMotion());
        PatternRegistry.RegisterAction("hexcasting:blink", new OpBlink());
        PatternRegistry.RegisterAction("hexcasting:teleport/great", new OpTeleport());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
