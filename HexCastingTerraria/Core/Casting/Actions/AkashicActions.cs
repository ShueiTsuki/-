using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `akashic/read`：从某个坐标的阿卡夏记录方块上，按**图案**查一个 iota。
/// 移植自源项目 akashic/OpAkashicRead.kt。
///
/// 参数是 (坐标, 图案)。查不到时吐 **NullIota**（缺失是正常情况，不是错误）；
/// 该位置根本不是记录方块才报 MishapNoAkashicRecord。
/// </summary>
public sealed class OpAkashicRead : ConstMediaAction
{
    public override int Argc => 2;

    /// <summary>源项目 `MediaConstants.DUST_UNIT`（1 粉）。</summary>
    public override long MediaCost => MediaConstants.DustUnit;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "坐标");

        if (args[1] is not PatternIota key)
        {
            throw new MishapInvalidIota(args[1], "图案");
        }

        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();
        if (!world.IsAkashicRecord(x, y))
        {
            throw new MishapNoAkashicRecord(x, y);
        }

        var datum = world.LookupAkashic(x, y, key.Pattern);
        return new Iota[] { datum ?? NullIota.Instance };
    }
}

/// <summary>
/// `akashic/write`：把栈顶的 iota 写进某个坐标的阿卡夏记录方块，键是一个图案。
/// 移植自源项目 akashic/OpAkashicWrite.kt。
///
/// 这是 `SpellAction` 而不是 `ConstMediaAction`：它会**改变世界**，
/// 必须走「先扣媒质、再施放」的延迟路径（否则媒质不够时世界已经被改了）。
/// </summary>
public sealed class OpAkashicWrite : SpellAction
{
    public override int Argc => 3;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "坐标");

        if (args[1] is not PatternIota key)
        {
            throw new MishapInvalidIota(args[1], "图案");
        }

        var datum = args[2];

        env.AssertVecInRange(x, y);

        // 源项目 OpAkashicWrite：不能把别的玩家写进阿卡夏记录
        MishapOthersName.ThrowIfTrueName(datum, env.World?.Caster, allowSelf: true);

        var world = env.RequireWorld();
        if (!world.IsAkashicRecord(x, y))
        {
            throw new MishapNoAkashicRecord(x, y);
        }

        return new SpellResult
        {
            Effect = new WriteSpell(x, y, key.Pattern, datum),
            Cost = MediaConstants.DustUnit,
            // 源项目此处没有粒子（那段被注释掉了），保持一致
            Particles = Array.Empty<ParticleSpray>(),
        };
    }

    private sealed class WriteSpell : IRenderedSpell
    {
        private readonly double _x;
        private readonly double _y;
        private readonly HexPattern _key;
        private readonly Iota _datum;

        public WriteSpell(double x, double y, HexPattern key, Iota datum)
        {
            _x = x;
            _y = y;
            _key = key;
            _datum = datum;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            // 延迟施放期间方块可能已经被挖掉了 —— 静默跳过。
            // 此时媒质已经扣了，再报 mishap 只会让玩家困惑「钱花了但没写上」。
            env.World?.WriteAkashic(_x, _y, _key, _datum);
            return null;
        }
    }
}

/// <summary>阿卡夏系统的图案注册。</summary>
public static class AkashicActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:akashic/read", new OpAkashicRead());
        PatternRegistry.RegisterAction("hexcasting:akashic/write", new OpAkashicWrite());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
