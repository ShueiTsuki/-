using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `compare_entity`：两个实体**是不是同类**。
/// 移植自源项目 `OpEntityEquality`。
///
/// 注意比的是**种类（type）**，不是「是不是同一个实体」——
/// 源项目 `entityA.type == entityB.type`。
/// 想判「是不是同一个」应该用 `equals`。
/// </summary>
public sealed class OpEntityEquality : ConstMediaAction
{
    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var a = env.ResolveEntity(args[0]);
        var b = env.ResolveEntity(args[1]);

        // 同类判定要读泰拉的实体类型字段（NPC.netID 等），所以交给世界侧
        return new Iota[] { BooleanIota.Of(env.RequireWorld().IsSameEntityType(a, b)) };
    }
}

/// <summary>
/// `compare_block/lenient` 与 `/strict`：两个位置的方块是不是同一种。
/// 移植自源项目 `OpBlockEquality`。
///
/// 区别：
///   - lenient：只比**方块种类**（源项目 `blockA.is(blockB.block)`）
///   - strict：比**完整状态**（含朝向、帧、油漆等）
/// </summary>
public sealed class OpBlockEquality : ConstMediaAction
{
    private readonly bool _exact;

    public OpBlockEquality(bool exact) => _exact = exact;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x1, y1, z1) = CastingEnvironment.RequireVec3(args[0], "第一个位置");
        var (x2, y2, z2) = CastingEnvironment.RequireVec3(args[1], "第二个位置");

        env.AssertVecInRange(x1, y1, z1);
        env.AssertVecInRange(x2, y2, z2);

        return new Iota[] { BooleanIota.Of(env.RequireWorld().CompareBlocks(x1, y1, x2, y2, _exact)) };
    }
}

/// <summary>
/// `compare_item/lenient` 与 `/strict`：两个物品实体是不是同一种物品。
/// 移植自源项目 `OpItemEquality`。
///
/// 注意：**与源项目的差异**：源项目要求参数是「能持有物品的实体」
/// （MC 的物品展示框、盔甲架之类）。
/// 泰拉侧没有等价的通用实体，所以改为接受**掉在地上的物品**（`EntityKind.Item`）——
/// 它们同样是「持有物品的实体」，语义最接近。
/// </summary>
public sealed class OpItemEquality : ConstMediaAction
{
    private readonly bool _exact;

    public OpItemEquality(bool exact) => _exact = exact;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var a = env.ResolveEntity(args[0]);
        var b = env.ResolveEntity(args[1]);

        if (a.Target != EntityIota.EntityKind.Item || b.Target != EntityIota.EntityKind.Item)
        {
            throw new MishapInvalidIota(
                a.Target != EntityIota.EntityKind.Item ? args[0] : args[1],
                "物品（掉在地上的东西）");
        }

        return new Iota[] { BooleanIota.Of(env.RequireWorld().CompareItems(a, b, _exact)) };
    }
}

/// <summary>比较类图案的注册。</summary>
public static class CompareActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:compare_entity", new OpEntityEquality());
        PatternRegistry.RegisterAction("hexcasting:compare_block/lenient", new OpBlockEquality(exact: false));
        PatternRegistry.RegisterAction("hexcasting:compare_block/strict", new OpBlockEquality(exact: true));
        PatternRegistry.RegisterAction("hexcasting:compare_item/lenient", new OpItemEquality(exact: false));
        PatternRegistry.RegisterAction("hexcasting:compare_item/strict", new OpItemEquality(exact: true));

        return PatternRegistry.RegisteredActionCount - before;
    }
}

/// <summary>
/// **原理上不适用于泰拉**的图案。
///
/// 这个清单是**明确的结论**，不是「还没做」——列在这里是为了让
/// 「188 条里到底差多少」这个问题有准确的答案，而不是永远差几条说不清。
/// </summary>
public static class NotApplicablePatterns
{
    /// <summary>
    /// （已清空）Z 轴单位向量曾因「二维世界没有第三轴」列在这里。
    /// 现在向量与原版一样是三维的，`const/vec/pz` / `nz` 已实现（世界是 z = 0 的平面）。
    /// </summary>
    public static readonly string[] ZAxisVectors = System.Array.Empty<string>();

    /// <summary>
    /// Pehkui 联动（MC 的实体缩放模组）。泰拉没有等价物，也没有对应的需求。
    /// </summary>
    public static readonly string[] PehkuiInterop =
    {
        "hexcasting:interop/pehkui/get",
        "hexcasting:interop/pehkui/set",
    };

    /// <summary>全部不适用的图案数。</summary>
    public static int Count => ZAxisVectors.Length + PehkuiInterop.Length;
}
