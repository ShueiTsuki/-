using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

// 源项目 `spells/sentinel/` 五个图案。
//
// 哨卫是什么：一个**你自己**的坐标书签。放下之后：
//   - 跨施法、跨重登都在（存在玩家身上）
//   - `sentinel/get_pos` 随时问它在哪
//   - `sentinel/wayfind` 问「从某点到它，往哪走」
//   - **大哨卫**（`sentinel/create/great`）还能把施法范围延伸过去 ——
//     这是它真正的用处：站在家里，对远处的哨卫周围施法。
//
// 为什么「大」要贵一倍：范围延伸等于把 32 格的施法半径复制一份到 16 格外的另一个点，
// 是实打实的权力扩张。

/// <summary>
/// `sentinel/create` 与 `sentinel/create/great`：在指定位置放置哨卫。
/// 移植自源项目 `OpCreateSentinel`。
///
/// 消耗：普通 `DUST_UNIT × 1`，大哨卫 `DUST_UNIT × 2`。
/// </summary>
public sealed class OpCreateSentinel : SpellAction
{
    private readonly bool _great;

    public OpCreateSentinel(bool great) => _great = great;

    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目：env.castingEntity !is ServerPlayer → MishapBadCaster（法术环里不能用哨卫）
        SentinelGuard.RequirePlayerCaster(env);
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        return WorldSpell.Make(
            new WorldSpell.Simple(_ => env.SetSentinel(x, y, _great)),
            MediaConstants.DustUnit * (_great ? 2 : 1),
            new[] { ParticleSpray.Burst(x, y, spread: 2.0f, count: 40) });
    }
}

/// <summary>
/// `sentinel/destroy`：移除哨卫。
/// 移植自源项目 `OpDestroySentinel`。
///
/// 消耗 `DUST_UNIT / 10`（1 千）—— 很便宜，因为「放错了想撤掉」不该被收费惩罚。
/// 没放哨卫时也**不报错**（源项目同），只是没有粒子。
/// </summary>
public sealed class OpDestroySentinel : SpellAction
{
    public override int Argc => 0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目：env.castingEntity !is ServerPlayer → MishapBadCaster（法术环里不能用哨卫）
        SentinelGuard.RequirePlayerCaster(env);
        var particles = env.Sentinel is { } s
            ? new[] { ParticleSpray.Cloud(s.X, s.Y, spread: 2.0f, count: 30) }
            : System.Array.Empty<ParticleSpray>();

        return WorldSpell.Make(
            new WorldSpell.Simple(_ => env.ClearSentinel()),
            MediaConstants.DustUnit / 10,
            particles);
    }
}

/// <summary>
/// `sentinel/get_pos`：把哨卫坐标压栈；没放哨卫时压 `null`。
/// 移植自源项目 `OpGetSentinelPos`。消耗 `DUST_UNIT / 10`。
/// </summary>
public sealed class OpGetSentinelPos : ConstMediaAction
{
    public override int Argc => 0;

    public override long MediaCost => MediaConstants.DustUnit / 10;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目：env.castingEntity !is ServerPlayer → MishapBadCaster
        SentinelGuard.RequirePlayerCaster(env);
        return new Iota[]
        {
            env.Sentinel is { } s
                ? new VectorIota(s.X, s.Y)
                : NullIota.Instance,
        };
    }
}

/// <summary>
/// `sentinel/wayfind`：从给定点指向哨卫的**单位向量**。
/// 移植自源项目 `OpGetSentinelWayfind`。消耗 `DUST_UNIT / 10`。
///
/// 两个边界情形都照抄源码：
///   - 没放哨卫 -> 压 `null`（不是报错）
///   - 给的点和哨卫重合 -> 压零向量（MC 的 `Vec3.normalize()` 对零向量返回零向量）
/// </summary>
public sealed class OpGetSentinelWayfind : ConstMediaAction
{
    public override int Argc => 1;

    public override long MediaCost => MediaConstants.DustUnit / 10;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目：env.castingEntity !is ServerPlayer → MishapBadCaster（法术环里不能用哨卫）
        SentinelGuard.RequirePlayerCaster(env);
        var (x, y) = CastingEnvironment.RequireVec(args[0], "起点");

        if (env.Sentinel is not { } s)
        {
            return new Iota[] { NullIota.Instance };
        }

        double dx = s.X - x;
        double dy = s.Y - y;
        double lenSq = dx * dx + dy * dy;

        // 与 MC 的 Vec3.normalize() 同款阈值：太短就返回零向量，不产生 NaN
        if (lenSq < 1e-8)
        {
            return new Iota[] { VectorIota.Zero };
        }

        double len = System.Math.Sqrt(lenSq);
        return new Iota[] { new VectorIota(dx / len, dy / len) };
    }
}

/// <summary>哨卫图案的注册。</summary>
public static class SentinelActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:sentinel/create", new OpCreateSentinel(false));
        PatternRegistry.RegisterAction("hexcasting:sentinel/create/great", new OpCreateSentinel(true));
        PatternRegistry.RegisterAction("hexcasting:sentinel/destroy", new OpDestroySentinel());
        PatternRegistry.RegisterAction("hexcasting:sentinel/get_pos", new OpGetSentinelPos());
        PatternRegistry.RegisterAction("hexcasting:sentinel/wayfind", new OpGetSentinelWayfind());

        return PatternRegistry.RegisteredActionCount - before;
    }
}

internal static class SentinelGuard
{
    /// <summary>哨卫挂在玩家身上：没有玩家施法者（法术环）就是 MishapBadCaster。</summary>
    public static void RequirePlayerCaster(CastingEnvironment env)
    {
        if (env.World?.Caster is not { Target: EntityIota.EntityKind.Player })
        {
            throw new MishapBadCaster();
        }
    }
}
