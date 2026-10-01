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
/// `open_n_parens`：一次开 n 层括号，n 从栈顶取。
/// 移植自源项目 `OpOpenNParens`。
///
/// 注意：它**不覆写括号内行为** —— 源项目注释写明了原因：
/// *「在括号内没法合理判断该再开几层，所以干脆不覆写，当作普通图案处理」*。
/// 这个「故意不写」很容易被后来者当成遗漏而补上。
/// </summary>
public sealed class OpOpenNParens : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);
        // 原版是 newStack.getPositiveInt(newStack.lastIndex)，没先查栈空、也没传参数个数：
        //   栈空时报的是「要 0 个参数、有 0 个」，不压垃圾；类型不对时下标就是 lastIndex，被换成垃圾的是整个栈最底下那格。
        // 两处都照搬（和原版对拍时发现，2026-10-02）。
        if (stack.Count == 0)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(0, 0), image);
        }

        // 源项目 getPositiveInt
        int layers;
        try
        {
            layers = CastingEnvironment.RequirePositiveInt(stack[stack.Count - 1]);
        }
        catch (MishapInvalidIota m)
        {
            return OperationResult.Fail(m.At(stack.Count - 1), image);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        stack.RemoveAt(stack.Count - 1);

        var image2 = image.WithStack(stack).WithParenCount(layers).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// `close_all_parens`：一次关掉所有括号，并把「层数」和「内容列表」压栈。
/// 移植自源项目 `OpCloseAllParens`。
///
/// 栈效果：`[..., a, b]` → `[..., a, b, 层数, list(a, b)]`
/// （先压层数、再压列表，所以列表在栈顶）
///
/// 不在括号内时报 `MishapNeedsParens`。
/// </summary>
public sealed class OpCloseAllParens : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => OperationResult.Fail(new MishapNeedsParens(), image);

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
    {
        var stack = new List<Iota>(image.Stack);

        stack.Add(new DoubleIota(image.ParenCount));

        var contents = new List<Iota>(image.Parenthesized.Count);
        foreach (var p in image.Parenthesized)
        {
            contents.Add(p.Iota);
        }
        stack.Add(new ListIota(contents));

        var image2 = image.WithStack(stack)
            .WithParenCount(0)
            .WithClearedParenthesized()
            .WithUsedOp();

        return new ParenthesizedOperationResult(
            image2,
            System.Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Evaluated);
    }
}

/// <summary>
/// `runtime_escape`：把「下一个 iota 要被转义」的标记打开。
/// 移植自源项目 `OpRuntimeEscape`。
/// 与 `escape` 图案的区别：那个是编译期写死的，这个是运行期由栈决定的。
/// </summary>
public sealed class OpRuntimeEscape : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var image2 = image.WithEscapeNext(true).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>
/// `duplicate_n`：把栈顶项复制 n 份，一份一份压回栈上（原版返回 List(count) { args[0] }，常量图案的返回值逐个入栈；
/// 这里曾经包成一个列表压上去，和原版对拍时发现，2026-10-02 改回）。
/// 移植自源项目 `OpDuplicateN`。
///
/// n 超过 1024 时**不报错而是截断**（源项目 `MAX_SERIALIZATION_TOTAL`）——
/// 源码注释写明理由：在这里抛异常的话错误会指向本图案（而不是用户的操作），
/// 所以宁可截断，让后续的「iota 太多」检查去报错。
/// </summary>
public sealed class OpDuplicateN : ConstMediaAction
{
    /// <summary>源项目 `HexIotaTypes.MAX_SERIALIZATION_TOTAL = 1024`。</summary>
    public const int MaxCount = 1024;

    public override int Argc => 2;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        // 源项目 getPositiveInt：负数是 MishapInvalidIota（这里曾经当成 0 处理）
        int count = CastingEnvironment.RequirePositiveInt(args[1]);

        // 截断而不是报错 —— 理由见类型注释
        if (count > MaxCount) count = MaxCount;

        var list = new List<Iota>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(args[0]);
        }

        return list;
    }
}

/// <summary>
/// `random`：一个 [0, 1) 的随机数。
/// 移植自源项目 `OpRandom`。
/// </summary>
public sealed class OpRandom : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        => new Iota[] { new DoubleIota(env.RequireWorld().NextDouble()) };
}

/// <summary>
/// `get_media`：查询施法环境**还有多少媒质**，单位是粉尘。
/// 移植自源项目 `OpGetMedia`。
///
/// 实现方式很巧妙：拿一个「几乎无限大」的量去试算，
/// 返回的未付清部分就是缺口，`Long.MAX_VALUE` 减去它就是可用量。
/// 这样不需要环境额外暴露「查询余额」的接口。
/// </summary>
public sealed class OpGetMedia : ConstMediaAction
{
    public override int Argc => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        long unpaid = env.ExtractMedia(long.MaxValue, simulate: true);
        long available = long.MaxValue - unpaid;

        return new Iota[] { new DoubleIota(available / (double)MediaConstants.DustUnit) };
    }
}

/// <summary>
/// `fisherman` 与 `fisherman/copy`：把栈里第 depth 项「钓」到栈顶。
/// 移植自源项目 `OpFisherman` / `OpFishermanButItCopies`。
///
/// 语义（两个图案共用，只差「钓上来之后原位置还留不留」）：
///   - `depth ≥ 0`：从栈顶往下数第 depth 项 → 移到/复制到栈顶
///   - `depth &lt; 0`：栈顶项 → 塞到「从栈顶往下数第 |depth| 项」的**下面**
///
/// 注意：两个版本的动作在负数分支上**不一样**，照抄源码时很容易写成一个：
/// 移动版先把栈顶**摘掉**再插回去（所以插入位置少 1），
/// 复制版栈顶不动（插入位置按原索引算）。这里分别照抄，没有合并。
/// </summary>
public sealed class OpFisherman : IAction
{
    private readonly bool _copy;

    public OpFisherman(bool copy) => _copy = copy;

    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
    {
        var stack = new List<Iota>(image.Stack);

        if (stack.Count < 2)
        {
            return OperationResult.Fail(new MishapNotEnoughArgs(2, stack.Count), image);
        }

        // 源项目 getIntBetween(-maxIdx, maxIdx)：弹出 depth 之后还剩 count-1 项，能取的最大深度就是 count-2
        int maxIdx = stack.Count - 2;
        int depth;
        try
        {
            depth = CastingEnvironment.RequireIntBetween(stack[stack.Count - 1], -maxIdx, maxIdx);
        }
        catch (MishapInvalidIota m) when (_copy)
        {
            // 复制版在原版是 stack.getIntBetween(stack.lastIndex, …)，没传参数个数：事故下标就是 lastIndex，
            // 被换成垃圾的是整个栈最底下那格（移动版明确写了下标 0，是栈顶）。照搬（和原版对拍时发现，2026-10-02）。
            return OperationResult.Fail(m.At(stack.Count - 1), image);
        }
        catch (Mishap m)
        {
            return OperationResult.Fail(m, image);
        }

        stack.RemoveAt(stack.Count - 1);

        if (depth >= 0)
        {
            int index = stack.Count - 1 - depth;
            var fish = stack[index];
            if (!_copy)
            {
                stack.RemoveAt(index);
            }
            stack.Add(fish);
        }
        else if (_copy)
        {
            // 复制版：栈顶不动，插入位置 = size-1+depth
            stack.Insert(stack.Count - 1 + depth, stack[stack.Count - 1]);
        }
        else
        {
            // 移动版：先摘掉栈顶，再插到 size+depth 处
            var lure = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            stack.Insert(stack.Count + depth, lure);
        }

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>栈/括号工具类图案的注册。</summary>
public static class StackUtilActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:open_n_parens", new OpOpenNParens());
        PatternRegistry.RegisterAction("hexcasting:close_all_parens", new OpCloseAllParens());
        PatternRegistry.RegisterAction("hexcasting:runtime_escape", new OpRuntimeEscape());

        PatternRegistry.RegisterAction("hexcasting:duplicate_n", new OpDuplicateN());

        PatternRegistry.RegisterAction("hexcasting:random", new OpRandom());
        PatternRegistry.RegisterAction("hexcasting:get_media", new OpGetMedia());

        PatternRegistry.RegisterAction("hexcasting:fisherman", new OpFisherman(copy: false));
        PatternRegistry.RegisterAction("hexcasting:fisherman/copy", new OpFisherman(copy: true));

        return PatternRegistry.RegisteredActionCount - before;
    }
}
