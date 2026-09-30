using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Castables;

/// <summary>
/// 一条图案的行为。
/// 移植自 at.petrak.hexcasting.api.casting.castables.Action。
///
/// 注意：Action 实例只应在服务端使用；客户端只需要图案的展示信息。
/// </summary>
public interface IAction
{
    /// <summary>
    /// 执行并返回新状态与副作用。
    /// 调用时保证 image.ParenCount == 0。
    /// 实现者需自行递增 op 计数。
    /// </summary>
    OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation);

    /// <summary>
    /// 括号内行为。默认只是把该图案加入括号列表，不消耗 op、不产生效果。
    /// </summary>
    ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
        => new ParenthesizedOperationResult(
            image.WithNewParenthesized(thisIota, escaped: false),
            System.Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Escaped);

    /// <summary>
    /// 参数类型契约（消耗什么类型、产出什么类型）。见 <see cref="ActionTypes"/>。
    ///
    /// 默认 <see cref="ActionTypes.Unknown"/> —— 没标注的图案会被类型检查**跳过**，
    /// 不会因为"有人忘了标注"而报假错。标注是增量补的，覆盖多少就查多少。
    ///
    /// 它存在的原因只有一个：有一类问题能编译、能过全部现有测试、只在游戏里炸
    /// （把实体的位置当实体传给下一个图案，一路报错但栈不变，最后作用在错误的目标上）。
    /// </summary>
    ActionTypes Types => ActionTypes.Unknown;
}

/// <summary>
/// 固定媒质消耗、固定参数个数的图案基类。
/// 移植自 at.petrak.hexcasting.api.casting.castables.ConstMediaAction。
/// </summary>
public abstract class ConstMediaAction : IAction
{
    /// <summary>需要的参数个数。</summary>
    public abstract int Argc { get; }
    /// <summary>
    /// 参数类型契约。见 <see cref="ActionTypes"/>。
    ///
    /// 注意：必须在这里声明成 virtual，而不能只靠 <see cref="IAction.Types"/> 的默认实现：
    /// **默认接口成员不会被派生类的同名成员重新绑定** ——
    /// 派生类没有在基类列表里再写一次 `IAction`，接口映射就沿用基类那份，
    /// 于是在子类里写 `public ActionTypes Types => ...` 看上去编译通过、实际永远走默认值
    /// （这个坑让类型检查静默失效过一轮，靠"直接 new 具体类型 vs 以接口引用调用"的对比才定位到）。
    ///
    /// 默认 Unknown = 未标注，类型检查会跳过它，不报假错。
    /// </summary>
    public virtual ActionTypes Types => ActionTypes.Unknown;

    /// <summary>媒质消耗。</summary>
    public virtual long MediaCost => 0;

    /// <summary>真正做事的函数：吃 args，吐新的栈内容。</summary>
    public abstract IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env);

    /// <summary>可覆写以返回自定义 op 消耗。</summary>
    public virtual (IReadOnlyList<Iota> Stack, long OpCount) ExecuteWithOpCount(
        IReadOnlyList<Iota> args, CastingEnvironment env)
        => (Execute(args, env), 1);

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);

        if (Argc > stack.Count)
        {
            // 返回而不是抛出：参数不足是**画图的正常失误**，不是程序异常。
            // 抛出会让 tModLoader 每个 mishap 打一条带堆栈的日志。
            return OperationResult.Fail(new MishapNotEnoughArgs(Argc, stack.Count), image);
        }

        // 取末尾 Argc 个作为参数并弹出
        var args = new List<Iota>(Argc);
        for (int i = stack.Count - Argc; i < stack.Count; i++)
        {
            args.Add(stack[i]);
        }
        stack.RemoveRange(stack.Count - Argc, Argc);

        // 媒质预检必须放在 Execute 之前：Execute 可能已经有可观察的副作用，
        // 原版同样是先探媒质（simulate）再真正执行。
        if (env.ExtractMedia(MediaCost, simulate: true) > 0)
        {
            return OperationResult.Fail(new MishapNotEnoughMedia(MediaCost), image);
        }

        IReadOnlyList<Iota> produced;
        long opCount;
        try
        {
            var result = ExecuteWithOpCount(args, env);
            produced = result.Stack;
            opCount = result.OpCount;
        }
        catch (Mishap m)
        {
            // 兼容尚未改造为 Error 返回的行为：在这里收口，仍然不向日志泄漏。
            return OperationResult.Fail(m, image);
        }

        stack.AddRange(produced);

        var sideEffects = new List<OperatorSideEffect> { new ConsumeMediaSideEffect(MediaCost) };

        var image2 = image.WithStack(stack).WithUsedOps(opCount);
        return new OperationResult(image2, sideEffects, continuation, EvalSound.NormalExecute);
    }
}
