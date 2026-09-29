using System.Collections.Generic;
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
/// `conjure_block` 与 `conjure_light`：**凭空**造出一块方块 / 一盏光。
/// 移植自源项目 `OpConjureBlock`。
///
/// 与 `place_block` 的关键区别：**不需要背包里有东西**。
/// 代价是消耗高（1 粉尘 = 10000 媒质）而且造出来的方块是**临时的**，过一会儿会消失。
/// </summary>
public sealed class OpConjureBlock : SpellAction
{
    private readonly bool _light;

    public OpConjureBlock(bool light) => _light = light;

    public override int Argc => 1;

    public override ActionTypes Types => ActionTypes.Of(null, IotaTypes.Vec);

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();

        // 目标必须是空气（源项目要求 canBeReplaced）
        if (!world.IsReplaceable(x, y))
        {
            throw new MishapBadBlock(x, y, "这里已经有东西了");
        }

        return new SpellResult
        {
            Effect = new ConjureSpell(x, y, _light),
            Cost = MediaConstants.DustUnit,
            Particles = new[] { ParticleSpray.Cloud(x, y, spread: 1.0f, count: 15) },
        };
    }

    private sealed class ConjureSpell : IRenderedSpell
    {
        private readonly double _x;
        private readonly double _y;
        private readonly bool _light;

        public ConjureSpell(double x, double y, bool light)
        {
            _x = x;
            _y = y;
            _light = light;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            env.World?.ConjureBlock(_x, _y, _light);
            return null;
        }
    }
}

/// <summary>
/// `break_block`：挖掉指定位置的方块。
/// 移植自源项目 `OpBreakBlock`。
///
/// **消耗分两档**：普通方块 `DUST/8`，而「廉价方块」（草、花、树叶这类）只要 `DUST/100`。
/// 源项目用方块标签区分 —— 不然挖一片草会贵得离谱。
/// </summary>
public sealed class OpBreakBlock : SpellAction
{
    public override int Argc => 1;

    public override ActionTypes Types => ActionTypes.Of(null, IotaTypes.Vec);

    /// <summary>普通方块的消耗：`DUST / 8`。</summary>
    public const long NormalCost = MediaConstants.DustUnit / 8;

    /// <summary>廉价方块的消耗：`DUST / 100`。</summary>
    public const long CheapCost = MediaConstants.DustUnit / 100;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "位置");
        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();

        // 先问清楚「这一格到底能不能挖」**再收费**。
        //
        // 不问的话，法术会走到「成功」然后被世界侧悄悄拒绝：玩家扣了媒质、
        // 方块没动、也没有任何提示 —— 属于最难自查的一类问题
        // （现实案例：对着木板放 break_block，提示成功，木板纹丝不动）。
        if (!world.CanBreakBlockAt(x, y, out string refusal))
        {
            throw new MishapBadBlock(x, y, refusal);
        }

        bool cheap = world.IsCheapToBreak(x, y);

        return new SpellResult
        {
            Effect = new BreakSpell(x, y),
            Cost = cheap ? CheapCost : NormalCost,
            Particles = new[] { ParticleSpray.Burst(x, y, spread: 1.0f, count: 15) },
        };
    }

    private sealed class BreakSpell : IRenderedSpell
    {
        private readonly double _x;
        private readonly double _y;

        public BreakSpell(double x, double y)
        {
            _x = x;
            _y = y;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            env.World?.BreakBlockAt(_x, _y);
            return null;
        }
    }
}

/// <summary>召唤与挖掘图案的注册。</summary>
public static class BlockActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:conjure_block", new OpConjureBlock(light: false));
        PatternRegistry.RegisterAction("hexcasting:conjure_light", new OpConjureBlock(light: true));
        PatternRegistry.RegisterAction("hexcasting:break_block", new OpBreakBlock());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
