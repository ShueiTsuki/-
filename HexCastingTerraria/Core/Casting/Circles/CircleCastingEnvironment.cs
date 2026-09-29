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
}

/// <summary>
/// 法术环的施法环境。移植自源项目 `CircleCastEnv`。
///
/// 与玩家环境的**三处本质区别**（逐条核对过 `CircleCastEnv.java`）：
///
/// | | 玩家环境 | 法术环 |
/// |---|---|---|
/// | 媒质来源 | 玩家媒质池 + 背包物品 | **原动力的媒质池**（负数 = 无限） |
/// | 范围判定 | 以玩家为中心 32 格半径 | **环自身的包围盒** |
/// | 施法者 | 玩家实体 | **null**（无人施法） |
///
/// 最后一行正好用上 `get_caster` 早先特意保留的分支：
/// 无实体施法者时返回 `NullIota` 而**不是**报错。
/// </summary>
public sealed class CircleCastingEnvironment : CastingEnvironment
{
    private readonly ICastingWorld _world;
    private readonly CircleState _state;
    private readonly System.Func<long, bool, long> _extractMedia;

    /// <param name="world">世界访问（泰拉侧由 TerrariaCastingWorld 提供）。</param>
    /// <param name="state">环的状态。</param>
    /// <param name="extractMedia">媒质支取：(消耗, 是否试算) -> 还未付清的量。</param>
    public CircleCastingEnvironment(ICastingWorld world, CircleState state,
                                    System.Func<long, bool, long> extractMedia)
    {
        _world = world;
        _state = state;
        _extractMedia = extractMedia;
    }

    /// <summary>环的状态。三个 `circle/*` 图案通过它取值。</summary>
    public override CircleState Circle => _state;

    public override ICastingWorld World => _world;

    protected override long ExtractMediaEnvironment(long cost, bool simulate)
        => _extractMedia(cost, simulate);

    /// <summary>环里没有实体施法者 —— 这正是 `get_caster` 会吐 NullIota 的场景。</summary>
    public override bool IsEnlightened() => true;

    public override void PrintMessage(string message)
    {
        // 环没有施法者，没有地方「发消息给玩家」。
        // 源项目是显示在原动力的上方（postDisplay），泰拉侧先走注入的消息出口。
        CircleMessages.Post(message);
    }
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
            return new Iota[] { new VectorIota(x, y) };
        }
    }

    /// <summary>
    /// `circle/bounds/min` 与 `circle/bounds/max`：环的包围盒角点。
    ///
    /// ⚠️ 源项目在这里做了 **±0.5 的方块中心修正**：
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
