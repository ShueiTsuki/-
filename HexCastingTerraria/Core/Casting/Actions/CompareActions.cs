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
        // 原版 args.getEntity(0) / getEntity(1) 漏传了参数个数：类型不对时事故下标就是 0 / 1（见 EntityAt）
        var a = EntityAt(env, args[0], 0);
        var b = EntityAt(env, args[1], 1);

        // 同类判定要读泰拉的实体类型字段（NPC.netID 等），所以交给世界侧
        return new Iota[] { BooleanIota.Of(env.RequireWorld().IsSameEntityType(a, b)) };
    }

    /// <summary>
    /// 原版比较实体 / 比较物品取实体时漏传了参数个数（args.getEntity(idx) 而不是 getEntity(idx, argc)），
    /// 类型不对时事故下标就是 idx 本身，被换成垃圾的格子和出错的那个正好反过来；照搬（和原版对拍时发现，2026-10-02）。
    /// </summary>
    internal static EntityIota EntityAt(CastingEnvironment env, Iota iota, int reverseIdx)
    {
        try
        {
            return env.ResolveEntity(iota);
        }
        catch (MishapInvalidIota m) when (m.ReverseIdx is null)
        {
            throw m.At(reverseIdx);
        }
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
        // 照原版的顺序：取第一个位置、查范围，再取第二个、查范围。
        // 原版这里取参数时漏传了参数个数（args.getBlockPos(0) 而不是 getBlockPos(0, argc)），类型不对时事故的下标
        // 就是 0 / 1 本身，被换成垃圾的格子和出错的那个正好反过来；照搬（和原版对拍时发现，2026-10-02）。
        var (x1, y1, z1) = Vec3At(args[0], 0);
        env.AssertVecInRange(x1, y1, z1);
        var (x2, y2, z2) = Vec3At(args[1], 1);
        env.AssertVecInRange(x2, y2, z2);

        return new Iota[] { BooleanIota.Of(env.RequireWorld().CompareBlocks(x1, y1, x2, y2, _exact)) };
    }

    private static (double X, double Y, double Z) Vec3At(Iota iota, int reverseIdx)
    {
        try
        {
            return CastingEnvironment.RequireVec3(iota);
        }
        catch (MishapInvalidIota m)
        {
            throw m.At(reverseIdx);
        }
    }
}

/// <summary>
/// `compare_item/lenient` 与 `/strict`：两个实体拿着的物品是不是同一种。
/// 移植自源项目 `OpItemEquality` + `HexItemHolderHandlers`：掉落物、物品框、玩家（见 ICastingWorld.HasHeldItem），
/// 别的实体或拿着的是空的 → 事故「一个持有物品的实体」。
/// </summary>
public sealed class OpItemEquality : ConstMediaAction
{
    private readonly bool _exact;

    public OpItemEquality(bool exact) => _exact = exact;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var world = env.RequireWorld();
        // 上游的顺序：先取第一个的物品（取不到就事故），再取第二个的。
        // 下标照搬原版：取实体漏传了参数个数、「不是持有物品的实体」写的是 0 / 1 —— 都和出错的那个反过来（见 OpEntityEquality.EntityAt）
        var a = OpEntityEquality.EntityAt(env, args[0], 0);
        if (!world.HasHeldItem(a)) throw new MishapInvalidIota(args[0], InvalidValue.EntityItemHolder) { ReverseIdx = 0 };
        var b = OpEntityEquality.EntityAt(env, args[1], 1);
        if (!world.HasHeldItem(b)) throw new MishapInvalidIota(args[1], InvalidValue.EntityItemHolder) { ReverseIdx = 1 };

        return new Iota[] { BooleanIota.Of(world.CompareItems(a, b, _exact)) };
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
