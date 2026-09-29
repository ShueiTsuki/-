using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `get_caster`：把施法者自身压上栈。
/// 移植自源项目 selectors/OpGetCaster.kt。
///
/// ⚠️ 关键分支：**没有实体施法者时吐 NullIota，而不是报错**。
/// 源项目里 `env.castingEntity` 可为 null（法术环、法术书等无人施法场景），
/// 此时 `get_caster` 合法地返回 null。把这里写成 mishap 会让那类法术直接失效。
/// </summary>
public sealed class OpGetCaster : ConstMediaAction
{
    public override int Argc => 0;

    public override ActionTypes Types => ActionTypes.Of(IotaTypes.Entity);

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var caster = env.RequireWorld().Caster;

        if (caster == null)
        {
            // 源项目：return null.asActionResult —— 吐 NullIota，不报错
            return new Iota[] { NullIota.Instance };
        }

        // 存活 + 范围校验统一走 ResolveEntity
        return new Iota[] { env.ResolveEntity(caster) };
    }
}

/// <summary>
/// `entity_pos/eye` 与 `entity_pos/foot`：取实体位置。
/// 移植自源项目 queryentity/OpEntityPos.kt。
///
/// 原版一个类带 `feet` 布尔切换两个图案，这里保持一致
/// （两个图案的**行为完全同构**，拆成两个类会重复代码）。
/// </summary>
public sealed class OpEntityPos : ConstMediaAction
{
    private readonly bool _feet;

    public OpEntityPos(bool feet) => _feet = feet;

    public override int Argc => 1;

    public override ActionTypes Types => ActionTypes.Of(IotaTypes.Vec, IotaTypes.Entity);

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var world = env.RequireWorld();

        var (x, y) = _feet ? world.FeetPosition(entity) : world.EyePosition(entity);
        return new Iota[] { new VectorIota(x, y) };
    }
}

/// <summary>
/// `get_entity_velocity`：取实体速度。
/// 移植自源项目 queryentity/OpEntityVelocity.kt。
///
/// 注意：静止实体返回**零向量**是合法的（源项目同），
/// 不要在这里做「零向量兜底」—— 那会让「这怪没动」这个事实消失。
/// 归一化类图案自己负责处理零向量。
/// </summary>
public sealed class OpEntityVelocity : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var (x, y) = env.RequireWorld().Velocity(entity);
        return new Iota[] { new VectorIota(x, y) };
    }
}

/// <summary>
/// `get_entity_look`：取实体视线方向（单位向量）。
/// 移植自源项目 queryentity/OpEntityLook.kt。
///
/// 泰拉侧「视线」由 `LookResolver` 的四层策略提供，
/// **保证不是 NaN、不是零向量**（详见 Core/World/LookResolver.cs）。
/// </summary>
public sealed class OpEntityLook : ConstMediaAction
{
    public override int Argc => 1;

    public override ActionTypes Types => ActionTypes.Of(IotaTypes.Vec, IotaTypes.Entity);

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var (x, y) = env.RequireWorld().Look(entity);
        return new Iota[] { new VectorIota(x, y) };
    }
}

/// <summary>
/// `get_entity_height`：取实体高度。
/// 移植自源项目 queryentity/OpEntityHeight.kt。
///
/// 2D 侧视下「实体高度」仍有意义（判定箱高度），单位同样是**图格**。
/// </summary>
public sealed class OpEntityHeight : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        double height = env.RequireWorld().EntityHeight(entity);
        return new Iota[] { new DoubleIota(height) };
    }
}

/// <summary>
/// 只读类世界图案的注册。
///
/// 之所以先做这一批：它们**只读**，不改变世界状态，失败最多报个 mishap，
/// 不会像 `blink` / `teleport` 那样把玩家送进墙里，也不需要联机同步。
/// 做完这层，「鼠标指向的位置」就能真正用起来。
/// </summary>
public static class WorldActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:get_caster", new OpGetCaster());

        PatternRegistry.RegisterAction("hexcasting:entity_pos/eye", new OpEntityPos(feet: false));
        PatternRegistry.RegisterAction("hexcasting:entity_pos/foot", new OpEntityPos(feet: true));

        PatternRegistry.RegisterAction("hexcasting:get_entity_velocity", new OpEntityVelocity());
        PatternRegistry.RegisterAction("hexcasting:get_entity_look", new OpEntityLook());
        PatternRegistry.RegisterAction("hexcasting:get_entity_height", new OpEntityHeight());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
