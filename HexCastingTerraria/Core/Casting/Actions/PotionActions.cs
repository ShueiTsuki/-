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
/// 药水效果的种类。对应源项目注册的 10 个 MC `MobEffects`。
///
/// 做成 Core 侧枚举而不是直接用泰拉的 `BuffID`：
/// **`Core/` 不得引用 Terraria**（这条约束已经踩过四次），
/// 实际映射由世界侧负责。
/// </summary>
public enum PotionEffectKind
{
    Weakness = 0,
    Levitation = 1,
    Wither = 2,
    Poison = 3,
    Slowness = 4,
    Regeneration = 5,
    NightVision = 6,
    Absorption = 7,
    Haste = 8,
    Strength = 9,
}

/// <summary>
/// `potion/*`：给一个活物施加药水效果。
/// 移植自源项目 spells/OpPotionEffect.kt。
///
/// 参数：目标实体、**持续时间（秒）**、[效力]（仅部分效果可加浓）。
///
/// 消耗公式（逐字照抄）：
/// ```
/// cost = baseCost × duration × (potency³ 或 potency²)
/// ```
/// 「立方」的那几个是增益类效果（再生/夜视/吸收/急速/力量）——
/// 加浓它们的代价增长得更快，原版是这么平衡的。
/// </summary>
public sealed class OpPotionEffect : SpellAction
{
    private readonly PotionEffectKind _effect;
    private readonly long _baseCost;
    private readonly bool _allowPotency;
    private readonly bool _potencyCubic;

    public OpPotionEffect(PotionEffectKind effect, long baseCost, bool allowPotency, bool potencyCubic)
    {
        _effect = effect;
        _baseCost = baseCost;
        _allowPotency = allowPotency;
        _potencyCubic = potencyCubic;
    }

    /// <summary>可加浓的效果吃 3 个参数，否则 2 个。</summary>
    public override int Argc => _allowPotency ? 3 : 2;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);

        // 源项目用 getLivingEntityButNotArmorStand —— 泰拉的对应物是玩家或 NPC
        if (target.Target is not (EntityIota.EntityKind.Player or EntityIota.EntityKind.Npc))
        {
            throw new MishapInvalidIota(args[0], InvalidValue.EntityLiving);
        }

        // 源项目 getPositiveDouble：含 0（0 秒 = 不上 buff、也不花钱）
        double duration = CastingEnvironment.RequirePositiveDouble(args[1]);

        double potency = 1.0;
        if (_allowPotency)
        {
            // 源项目 getDoubleBetween(1.0, 127.0)
            potency = CastingEnvironment.RequireDoubleBetween(args[2], 1.0, 127.0);
        }

        // 消耗 = 基础 × 持续 × 效力²（或 ³）
        double potencyFactor = _potencyCubic ? potency * potency * potency : potency * potency;
        long cost = (long)(_baseCost * duration * potencyFactor);

        var world = env.RequireWorld();
        var (tx, ty) = world.FeetPosition(target);

        return new SpellResult
        {
            Effect = new PotionSpell(target, _effect, duration, potency),
            Cost = cost,
            Particles = new[] { ParticleSpray.Cloud(tx, ty, spread: 1.0f, count: 20) },
        };
    }

    private sealed class PotionSpell : IRenderedSpell
    {
        private readonly EntityIota _target;
        private readonly PotionEffectKind _effect;
        private readonly double _duration;
        private readonly double _potency;

        public PotionSpell(EntityIota target, PotionEffectKind effect, double duration, double potency)
        {
            _target = target;
            _effect = effect;
            _duration = duration;
            _potency = potency;
        }

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            // 注意：源项目这条判定很关键：**持续不足 1 tick 就不施加**。
            // MC 里 1 秒 = 20 tick，所以阈值是 1/20 秒。
            // 泰拉 1 秒 = 60 tick —— 这里沿用源项目的**秒数**语义，
            // 换算成泰拉 tick 时用 60。
            if (_duration > 1.0 / 60.0)
            {
                int ticks = (int)(_duration * 60.0);
                env.World?.ApplyPotion(_target, _effect, ticks, (int)_potency);
            }

            return null;
        }
    }
}

/// <summary>药水图案的注册。10 条，参数逐个抄自源项目 `HexActions.java`。</summary>
public static class PotionActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        long dust = MediaConstants.DustUnit;

        // 负面效果：平方增长，基础消耗低
        R("hexcasting:potion/weakness", PotionEffectKind.Weakness, dust / 10, true, false);
        R("hexcasting:potion/levitation", PotionEffectKind.Levitation, dust / 5, false, false);
        R("hexcasting:potion/wither", PotionEffectKind.Wither, dust, true, false);
        R("hexcasting:potion/poison", PotionEffectKind.Poison, dust / 3, true, false);
        R("hexcasting:potion/slowness", PotionEffectKind.Slowness, dust / 3, true, false);

        // 增益效果：**立方**增长 —— 加浓它们的代价涨得更快
        R("hexcasting:potion/regeneration", PotionEffectKind.Regeneration, dust, true, true);
        R("hexcasting:potion/night_vision", PotionEffectKind.NightVision, dust / 5, false, true);
        R("hexcasting:potion/absorption", PotionEffectKind.Absorption, dust, true, true);
        R("hexcasting:potion/haste", PotionEffectKind.Haste, dust / 3, true, true);
        R("hexcasting:potion/strength", PotionEffectKind.Strength, dust / 3, true, true);

        return PatternRegistry.RegisteredActionCount - before;
    }

    private static void R(string id, PotionEffectKind effect, long baseCost, bool allowPotency, bool cubic)
        => PatternRegistry.RegisterAction(id, new OpPotionEffect(effect, baseCost, allowPotency, cubic));
}
