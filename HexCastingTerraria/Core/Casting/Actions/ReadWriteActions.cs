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

// 本文件 = 源项目 `common/casting/actions/rw/` 与 `actions/local/` 两组图案。
//
// 一句话题眼：**iota 要存在某个地方，才能被读回来**。
// 源项目那边有四种「地方」，我们一一对应：
//
//   | 源项目          | 泰拉侧                        | 图案                        |
//   |-----------------|-------------------------------|-----------------------------|
//   | 副手物品        | 手持物品                      | read / write / readable / writable |
//   | 实体（展示框等）| 掉落物、物品框里的载体、壁挂卷轴（只读） | read/entity / write/entity / …     |
//   | userData        | 同左（本次施法内有效）        | read/local / write/local    |
//   | ——              | ——                            | erase（清空手持载体）        |

/// <summary>
/// 「只做一件事」的法术，但作用对象是**施法环境**（手持物品）而不是世界。
///
/// 与 <c>WorldSpell</c> 分开是有必要的：`write` / `erase` 改的是手里的载体，
/// 和世界状态无关；硬塞进 WorldSpell 会让「世界」这个概念被稀释。
/// </summary>
internal static class EnvSpell
{
    public static SpellResult Make(IRenderedSpell effect, long cost, IReadOnlyList<ParticleSpray>? particles = null)
        => new()
        {
            Effect = effect,
            Cost = cost,
            Particles = particles ?? System.Array.Empty<ParticleSpray>(),
        };

    public sealed class Simple : IRenderedSpell
    {
        private readonly System.Action<CastingEnvironment> _act;

        public Simple(System.Action<CastingEnvironment> act) => _act = act;

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            _act(env);
            return null;
        }
    }
}

/// <summary>
/// `read`：把**手持**数据载体里的 iota 压栈。
/// 移植自源项目 `OpRead`：`readIota ?: emptyIota ?: mishap` —— 原版没有哪个物品有「空值」，
/// 所以拿着**空**的核心 / 卷轴 read 也是 mishap（「需要一个可以读出 iota 的地方」）。
/// </summary>
public sealed class OpReadHeld : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { env.ReadHeldIota() ?? throw new MishapBadHeldItem(MishapBadHeldItem.Need.Read, actual: env.HeldStorageItem()) };
}

/// <summary>
/// `readable`：手上有没有**读得出东西**的载体（压入布尔）。
/// 移植自源项目 `OpReadable`：载体是空的 → false。
/// </summary>
public sealed class OpReadableHeld : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { BooleanIota.Of(env.ReadHeldIota() != null) };
}

/// <summary>
/// `write`：把栈顶的 iota 写进**手持**数据载体。
/// 移植自源项目 `OpWrite`（那边是个法术，因为「写进去该有点动静」）。
///
/// 消耗为 0，但仍然是法术 —— 源项目注释写明了理由：
/// *「让它悄无声息地什么都没发生，实在太虎头蛇尾了」*。
/// 所以这里有粒子、有音效，只是不扣媒质。
/// </summary>
public sealed class OpWriteHeld : SpellAction
{
    public override int Argc => 1;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var value = args[0];
        // 原版：先找肯收它的载体；没有 → 有载体就报「只读」（连同那件载体），连载体都没有就报「需要可写入的地方」
        if (!env.CanWriteHeld(value))
        {
            throw env.HeldStorageItem() is { } holder
                ? new MishapBadHeldItem(MishapBadHeldItem.Need.ReadOnly, value, holder)
                : new MishapBadHeldItem(MishapBadHeldItem.Need.Write);
        }
        // 源项目 OpWrite：不能把别的玩家写进物品（真名保护，联机防恶意）
        MishapOthersName.ThrowIfTrueName(value, env.World?.Caster, allowSelf: true);
        return EnvSpell.Make(
            new EnvSpell.Simple(castEnv => castEnv.WriteHeldIota(value)),
            cost: 0);
    }
}

/// <summary>
/// `writable`：手持载体可不可写（压入布尔）。
/// 移植自源项目 `OpWritable`。
/// </summary>
public sealed class OpWritableHeld : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { BooleanIota.Of(env.IsHeldWritable()) };
}

/// <summary>
/// `erase`：清空手持的打包法术里的咒术，或数据载体里存的 iota。
/// 移植自源项目 `OpErase`：目标 = 第一个「装着咒术」或「writeIota(null) 行得通」的物品 ——
/// 所以**空的核心也能清**（不报错，照样扣费），封了的核心也能清（顺带解封），念珠清不了。
///
/// 消耗按**手持物品的堆叠数**算（源项目 `DUST_UNIT * handStack.getCount()`）。
///
/// 注意：这里只能**查**，不能清：之前在 Execute 里就调了清除，媒质不够、法术根本没放出来，东西也已经被清空了。
/// </summary>
public sealed class OpEraseHeld : SpellAction
{
    public override int Argc => 0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        int count = env.HeldEraseableCount();
        if (count <= 0)
        {
            throw new MishapBadHeldItem(MishapBadHeldItem.Need.Eraseable);
        }

        return EnvSpell.Make(
            new EnvSpell.Simple(castEnv => castEnv.EraseHeld()),
            MediaConstants.DustUnit * count);
    }
}

/// <summary>
/// `read/entity`：读**某个实体**身上载体里的 iota。
/// 移植自源项目 `OpTheCoolerRead`（名字就叫「更酷的 read」，作者的幽默感）。
/// </summary>
public sealed class OpReadEntity : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var world = env.RequireWorld();
        // 原版：readIota ?: emptyIota ?: mishap —— 空载体同样报错
        var datum = world.IsEntityIotaHolder(entity) ? world.ReadEntityIota(entity) : null;
        return new Iota[] { datum ?? throw MishapBadEntity.Of(entity, Wanted.IotaRead) };
    }
}

/// <summary>`readable/entity`：某个实体身上有没有**读得出东西**的载体（压入布尔）。移植自 `OpTheCoolerReadable`。</summary>
public sealed class OpReadableEntity : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var world = env.RequireWorld();
        return new Iota[] { BooleanIota.Of(world.IsEntityIotaHolder(entity) && world.ReadEntityIota(entity) != null) };
    }
}

/// <summary>
/// `write/entity`：把 iota 写进某个实体身上的载体。
/// 移植自源项目 `OpTheCoolerWrite`。
/// </summary>
public sealed class OpWriteEntity : SpellAction
{
    public override int Argc => 2;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        var value = args[1];

        var world = env.RequireWorld();
        // 原版 writeIota(datum, simulate: true)：卷轴只收图案、念珠只收一次 —— 光看 writeable() 不够
        if (!world.IsEntityIotaHolder(entity) || !world.CanWriteEntityIota(entity, value))
        {
            throw MishapBadEntity.Of(entity, Wanted.IotaWrite);
        }

        // 源项目 OpTheCoolerWrite：getTrueNameFromDatum(datum, null) —— 连自己的名字也不能写进实体
        MishapOthersName.ThrowIfTrueName(value, world.Caster, allowSelf: false);

        return EnvSpell.Make(
            new EnvSpell.Simple(castEnv => castEnv.RequireWorld().WriteEntityIota(entity, value)),
            cost: 0,
            new[] { ParticleSpray.Burst(world.FeetPosition(entity).X, world.FeetPosition(entity).Y, spread: 0.25f, count: 40) });
    }
}

/// <summary>`writable/entity`：某个实体身上的载体可不可写（压入布尔）。移植自 `OpTheCoolerWritable`。</summary>
public sealed class OpWritableEntity : ConstMediaAction
{
    public override int Argc => 1;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var entity = env.ResolveEntity(args[0]);
        return new Iota[] { BooleanIota.Of(env.RequireWorld().IsEntityIotaWritable(entity)) };
    }
}

/// <summary>
/// `read/local`：把本次施法的 Ravenmind 压栈。
/// 移植自源项目 `OpPeekLocal`。
///
/// 没写过时压 `null` —— 源项目就是这么干的（`NullIota()`），
/// 不是报错。这与 `read/local` 常被用来做「有没有存过东西」的判定有关。
/// </summary>
public sealed class OpPeekLocal : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack)
        {
            image.UserData.Ravenmind ?? NullIota.Instance,
        };

        // 注意：**不修改** userData，只读
        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// `write/local`：把栈顶 iota 存进本次施法的 Ravenmind；栈顶是 `null` 则**清除**。
/// 移植自源项目 `OpPushLocal`。
///
/// 注意：写的是 `image.userData`，不是玩家存档 ——
/// 所以它的生命周期是「一次施法」，而在**法术环**里是「环走完所有石板」。
/// </summary>
public sealed class OpPushLocal : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(1, 0), image);
        }

        var value = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);

        var userData = image.UserData.Clone();
        userData.Ravenmind = value is NullIota ? null : value;

        var image2 = image.WithStack(stack).WithUserData(userData).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>读/写类图案的注册。</summary>
public static class ReadWriteActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // 手持载体
        PatternRegistry.RegisterAction("hexcasting:read", new OpReadHeld());
        PatternRegistry.RegisterAction("hexcasting:readable", new OpReadableHeld());
        PatternRegistry.RegisterAction("hexcasting:write", new OpWriteHeld());
        PatternRegistry.RegisterAction("hexcasting:writable", new OpWritableHeld());
        PatternRegistry.RegisterAction("hexcasting:erase", new OpEraseHeld());

        // 实体载体
        PatternRegistry.RegisterAction("hexcasting:read/entity", new OpReadEntity());
        PatternRegistry.RegisterAction("hexcasting:readable/entity", new OpReadableEntity());
        PatternRegistry.RegisterAction("hexcasting:write/entity", new OpWriteEntity());
        PatternRegistry.RegisterAction("hexcasting:writable/entity", new OpWritableEntity());

        // 本次施法的局部存储
        PatternRegistry.RegisterAction("hexcasting:read/local", new OpPeekLocal());
        PatternRegistry.RegisterAction("hexcasting:write/local", new OpPushLocal());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
