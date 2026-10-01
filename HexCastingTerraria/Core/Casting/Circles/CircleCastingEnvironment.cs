using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Circles;

/// <summary>
/// 法术环的执行状态（供 `circle/*` 三个图案读取）。
/// 对应源项目 `CircleExecutionState` 里被图案用到的那几个字段。
/// </summary>
public sealed class CircleState
{
    /// <summary>原动力坐标。</summary>
    public required double ImpetusX { get; init; }
    public required double ImpetusY { get; init; }

    /// <summary>媒质流出的方向。</summary>
    public required CircleDir ImpetusDir { get; init; }

    /// <summary>环的包围盒（图格）。</summary>
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int MaxX { get; init; }
    public required int MaxY { get; init; }

    /// <summary>
    /// 坐标系 Y 朝上（原版的约定；泰拉侧由 HexSpaceWorld 换算过来）。
    /// 此时 <see cref="ImpetusDir"/> 的上下要翻过来才是法术里的方向（CircleDir 按泰拉图格定义，Up = y−1）。
    /// </summary>
    public bool YUp { get; init; }
}

/// <summary>
/// 法术环的施法环境。移植自源项目 `CircleCastEnv`。
///
/// 与玩家环境的区别（逐条核对过 `CircleCastEnv.java`）：
///
/// | | 玩家环境 | 法术环 |
/// |---|---|---|
/// | 媒质来源 | 背包里的媒质物品 | **原动力的媒质池**（负数 = 无限） |
/// | 范围判定 | 以玩家为中心 32 格半径 | 环的包围盒；**再加上施法者身边和他的大哨卫**（世界侧实现） |
/// | 施法者 | 玩家实体 | **启动它的玩家**（工具匠 / 制箭师）或牧师促动石绑定的玩家；可以没有 |
/// | 启蒙 | 看玩家 | 看施法者；**没人绑定的环算启蒙** |
/// | 手持物品 | 玩家的两只手 | 施法者的两只手（原版 getPrimaryStacksForPlayer(OFF_HAND, caster)） |
/// | 消息 / mishap | 发给玩家 | **显示在原动力上**（原版 postPrint / postMishap，探知透镜里看） |
///
/// 没有施法者时 `get_caster` 吐 `NullIota` 而**不是**报错。
/// </summary>
public sealed class CircleCastingEnvironment : CastingEnvironment
{
    private readonly ICastingWorld _world;
    private readonly CircleState _state;
    private readonly System.Func<long, bool, long> _extractMedia;
    private readonly CastingEnvironment? _caster;
    private readonly System.Action<string, bool> _display;
    private readonly System.Action<int>? _setPigment;

    /// <param name="world">世界访问（泰拉侧由 TerrariaCastingWorld 提供，施法者 / 范围都在里面）。</param>
    /// <param name="state">环的状态。</param>
    /// <param name="extractMedia">媒质支取：(消耗, 是否试算) -> 还未付清的量。</param>
    /// <param name="caster">施法者的环境（手持物品、启蒙、哨卫、配色都转给它）；null = 没有施法者。</param>
    /// <param name="display">原动力上的显示：(文字, 是不是 mishap)。</param>
    /// <param name="setPigment">给原动力换颜料（参数 = 颜料物品类型）。</param>
    public CircleCastingEnvironment(ICastingWorld world, CircleState state,
                                    System.Func<long, bool, long> extractMedia,
                                    CastingEnvironment? caster = null,
                                    System.Action<string, bool>? display = null,
                                    System.Action<int>? setPigment = null)
    {
        _setPigment = setPigment;
        _world = world;
        _state = state;
        _extractMedia = extractMedia;
        _caster = caster;
        _display = display ?? ((msg, _) => CircleMessages.Post(msg));
    }

    /// <summary>环的状态。三个 `circle/*` 图案通过它取值。</summary>
    public override CircleState Circle => _state;

    public override ICastingWorld World => _world;

    protected override long ExtractMediaEnvironment(long cost, bool simulate)
        => _extractMedia(cost, simulate);

    /// <summary>原版：`if (getCastingEntity() == null) return true; return super.isEnlightened();`</summary>
    public override bool IsEnlightened() => _caster?.IsEnlightened() ?? true;

    /// <summary>原版 printMessage → impetus.postPrint。</summary>
    public override void PrintMessage(string message) => _display(message, false);

    /// <summary>原版 castingEntity.sendSystemMessage：环的 castingEntity 是施法者，没有就谁也不发。</summary>
    public override void MessageCaster(string message) => _caster?.MessageCaster(message);

    /// <summary>原版 postExecution：本次结果里的 mishap 显示到原动力上（postMishap）。</summary>
    public override void PostExecution(CastResult result)
    {
        base.PostExecution(result);
        foreach (var effect in result.SideEffects)
        {
            if (effect is DoMishapSideEffect doMishap
                && doMishap.Mishap.ErrorMessageWithName(this, doMishap.ErrorCtx) is { Length: > 0 } msg)
            {
                _display(msg, true);
            }
        }
    }

    // ── 施法者的「手」与身上的东西（没有施法者就全是默认值：原版 getPrimaryStacks 返回空）──

    public override Iota? ReadHeldIota() => _caster?.ReadHeldIota();
    public override bool HasHeldStorage() => _caster?.HasHeldStorage() ?? false;
    public override ItemStackInfo? HeldStorageItem() => _caster?.HeldStorageItem();
    public override bool IsHeldWritable() => _caster?.IsHeldWritable() ?? false;
    public override bool CanWriteHeld(Iota? datum) => _caster?.CanWriteHeld(datum) ?? false;
    public override bool WriteHeldIota(Iota value) => _caster?.WriteHeldIota(value) ?? false;
    public override int HeldEraseableCount() => _caster?.HeldEraseableCount() ?? 0;
    public override void EraseHeld() => _caster?.EraseHeld();
    public override PackagedSpellKind? HeldEmptyPackagedSpell => _caster?.HeldEmptyPackagedSpell;
    public override int HeldPhialCount() => _caster?.HeldPhialCount() ?? 0;
    public override ItemStackInfo? HeldPhialItem() => _caster?.HeldPhialItem();
    public override bool FillHeldPackagedSpell(IReadOnlyList<Iota> patterns, long media)
        => _caster?.FillHeldPackagedSpell(patterns, media) ?? false;
    public override bool CraftBatteryHeld(long media) => _caster?.CraftBatteryHeld(media) ?? false;
    public override long HeldRechargeSpace() => _caster?.HeldRechargeSpace() ?? -1;
    public override void ChargeHeld(long media) => _caster?.ChargeHeld(media);
    public override bool HeldHasVariants() => _caster?.HeldHasVariants() ?? false;
    public override bool CycleHeldVariant() => _caster?.CycleHeldVariant() ?? false;
    public override int FindPigmentItem() => _caster?.FindPigmentItem() ?? 0;

    /// <summary>
    /// 原版 OpColorize 在环里：颜料从施法者身上扣（withdrawItem），染的却是**原动力**（CircleCastEnv.setPigment）。
    /// </summary>
    public override void ApplyPigment(int itemType)
    {
        if (_caster is not null && _caster.WithdrawPigment(itemType)) { _setPigment?.Invoke(itemType); }
    }

    /// <summary>哨卫挂在施法者身上（原版 setSentinel(castingEntity)）；没有施法者时图案先报 MishapBadCaster。</summary>
    public override SentinelState? Sentinel => _caster?.Sentinel;
    public override void SetSentinel(double x, double y, bool great) => _caster?.SetSentinel(x, y, great);
    public override void ClearSentinel() => _caster?.ClearSentinel();
}

/// <summary>`circle/*` 三个图案的注册与实现。</summary>
public static class CircleActions
{
    private static CircleState RequireCircle(CastingEnvironment env)
        => env.Circle ?? throw new MishapNoSpellCircle();

    /// <summary>`circle/impetus_pos`：原动力的位置。</summary>
    public sealed class OpImpetusPos : Castables.ConstMediaAction
    {
        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var state = RequireCircle(env);
            // 源项目用 BlockPos.asActionResult = 方块中心
            return new Iota[] { new VectorIota(state.ImpetusX + 0.5, state.ImpetusY + 0.5) };
        }
    }

    /// <summary>`circle/impetus_dir`：媒质流出的方向（单位向量）。</summary>
    public sealed class OpImpetusDir : Castables.ConstMediaAction
    {
        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var state = RequireCircle(env);
            var (x, y) = state.ImpetusDir.ToVector();
            return new Iota[] { new VectorIota(x, state.YUp ? -y : y) };
        }
    }

    /// <summary>
    /// `circle/bounds/min` 与 `circle/bounds/max`：环的包围盒角点。
    ///
    /// 注意：源项目在这里做了 **±0.5 的方块中心修正**：
    /// AABB 的角是方块**边角**，减/加 0.5 才是最外方块的**中心**。
    /// 不做这个修正的话，返回值会落在方块的角上 —— 拿去当坐标用会偏半格。
    /// </summary>
    public sealed class OpCircleBounds : Castables.ConstMediaAction
    {
        private readonly bool _max;

        public OpCircleBounds(bool max) => _max = max;

        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var state = RequireCircle(env);

            return _max
                ? new Iota[] { new VectorIota(state.MaxX + 0.5, state.MaxY + 0.5) }
                : new Iota[] { new VectorIota(state.MinX + 0.5, state.MinY + 0.5) };
        }
    }

    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:circle/impetus_pos", new OpImpetusPos());
        PatternRegistry.RegisterAction("hexcasting:circle/impetus_dir", new OpImpetusDir());
        PatternRegistry.RegisterAction("hexcasting:circle/bounds/min", new OpCircleBounds(max: false));
        PatternRegistry.RegisterAction("hexcasting:circle/bounds/max", new OpCircleBounds(max: true));

        return PatternRegistry.RegisteredActionCount - before;
    }
}
