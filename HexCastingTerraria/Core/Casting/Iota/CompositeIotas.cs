using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>
/// 列表 iota。源：ListIota。
///
/// 语义要点：列表是**值**，所有"修改"返回新实例。
/// 源项目里列表承载 SpellList（惰性链表），C# 侧先实现为不可变 IReadOnlyList，
/// 等接入求值器的 for_each / eval_breakable 时再换成持久化链表
/// （见 CASTING_ENGINE_SPEC.md 第 7.3 节「最危险的 3 个点」第①条）。
/// </summary>
public sealed class ListIota : Iota
{
    private readonly List<Iota> _items;

    public ListIota(IEnumerable<Iota> items)
    {
        _items = new List<Iota>(items);
    }

    public ListIota(params Iota[] items)
    {
        _items = new List<Iota>(items);
    }

    public IReadOnlyList<Iota> Items => _items;

    public int Count => _items.Count;

    /// <summary>
    /// 真假值：**列表非空为真**（源项目 `getList().getNonEmpty()`）。
    ///
    /// ⚠️ 不覆写会落到基类的 `false` —— 于是**非空列表在布尔语境里也是假**，
    /// 条件分支静默走错边，而且不报错。这个缺陷是写 `bool_coerce` 用例时跑出来的。
    /// </summary>
    public override bool IsTruthy() => Count > 0;

    public override IotaKind Kind => IotaKind.List;

    public override string TypeName => "list";

    public override bool ValueEquals(Iota other)
    {
        if (other is not ListIota l || l.Count != Count)
        {
            return false;
        }
        for (int i = 0; i < Count; i++)
        {
            if (!_items[i].ValueEquals(l._items[i]))
            {
                return false;
            }
        }
        return true;
    }

    public override object? Serialize()
    {
        var outList = new List<object?>(Count);
        foreach (var item in _items)
        {
            outList.Add(item.Serialize());
        }
        return IotaSerializer.Envelope(IotaSerializer.KindList, outList);
    }

    /// <summary>返回追加了新元素的新列表（不修改自身）。</summary>
    public ListIota Appended(Iota item)
    {
        var copy = new List<Iota>(_items) { item };
        return new ListIota(copy);
    }

    protected override string DescribeValue() => $"{Count} 项";
}

/// <summary>
/// 图案 iota。源：PatternIota。
///
/// 它把一条 HexPattern 当作**可执行的值**（IsExecutable = true）——
/// 例如画一个图案字面量把它压栈，再用 eval 去执行它。
/// 执行时查图案注册表找到行为并调用（对应源项目 lookupAndOperate）。
/// </summary>
public sealed class PatternIota : Iota
{
    /// <summary>本 iota 携带的图案。</summary>
    public Math.HexPattern Pattern { get; }

    public PatternIota(Math.HexPattern pattern)
    {
        Pattern = pattern;
    }

    public PatternIota(string anglesSignature, Math.HexDir startDir)
    {
        // 图案字面量来自玩家绘制，理论上合法；用非严格构造避免异常
        if (!Math.HexPattern.TryFromAnglesUnchecked(anglesSignature, startDir, out var pat, out var err) || pat == null)
        {
            throw new System.ArgumentException($"非法的图案签名：{err}");
        }
        Pattern = pat;
    }

    public string AnglesSignature => Pattern.AnglesSignature();

    public Math.HexDir StartDir => Pattern.StartDir;

    public override IotaKind Kind => IotaKind.Pattern;

    public override string TypeName => "pattern";

    public override bool IsExecutable => true;

    public override bool ValueEquals(Iota other)
        => other is PatternIota p
           && p.AnglesSignature == AnglesSignature
           && p.StartDir == StartDir;

    public override object? Serialize()
        => IotaSerializer.Envelope(IotaSerializer.KindPattern,
            new List<object?> { AnglesSignature, (double)StartDir });

    protected override string DescribeValue() => $"{StartDir} {AnglesSignature}";

    public override CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => LookupAndOperate(vm, continuation, inParens: false);

    public override CastResult ExecuteInParens(CastingVM vm, SpellContinuation continuation)
        => LookupAndOperate(vm, continuation, inParens: true);

    /// <summary>
    /// 查表并执行。移植自源项目 PatternIota.lookupAndOperate。
    ///
    /// 三种情况：
    ///   - 命中已注册图案 → 调用其行为
    ///   - 未命中且**不在**括号内 → 抛 MishapInvalidPattern
    ///   - 未命中但在括号内 → 照常加入括号列表（不报错）
    /// </summary>
    private CastResult LookupAndOperate(CastingVM vm, SpellContinuation continuation, bool inParens)
    {
        try
        {
            var def = PatternRegistry.Match(Pattern);

            // ★ 特殊图案：数字字面量与掩码。
            // 它们**不在 188 条注册表里**，而是「前缀 + 图案本身算参数」的另一类。
            // 漏掉它们的后果很隐蔽：注册表全实现、用例全过，但玩家**画不出任何数字**,
            // 于是所有「要一个数当参数」的法术都用不了（而且只报「图案无效」）。
            if (def == null)
            {
                if (TrySpecialPattern(vm, continuation, inParens, out var specialResult))
                {
                    return specialResult;
                }
            }

            vm.Env.PrecheckAction(def);

            IAction? action = null;
            bool hasAction = def != null && PatternRegistry.TryGetAction(def, out action);
            if (def == null || !hasAction || action == null)
            {
                if (inParens)
                {
                    // 无效图案在括号内仍然入列（源项目同）
                    return new CastResult(
                        this,
                        continuation,
                        vm.Image.WithNewParenthesized(this, escaped: false),
                        System.Array.Empty<OperatorSideEffect>(),
                        ResolvedPatternType.Escaped,
                        EvalSound.NormalExecute);
                }

                // 注意：这里**故意不抛异常**。
                // tModLoader 会把捕获到的每个异常连堆栈写进日志，而「画到未实现的图案」
                // 在开发期极其常见 —— 抛异常会把日志刷爆。
                // 直接构造 mishap 结果，效果相同且不产生日志噪音。
                // 区分「根本不存在」与「存在但行为未实现」——
                // 混在一起会让玩家以为是自己画错了。
                var mishap = def == null
                    ? (Mishap)new MishapInvalidPattern(Pattern)
                    : new MishapNotImplemented(def.Id);
                return BuildMishapResult(vm, continuation, mishap);
            }

            if (def.RequiresEnlightenment && !vm.Env.IsEnlightened())
            {
                return BuildMishapResult(vm, continuation, new MishapUnenlightened());
            }

            if (inParens)
            {
                var pres = action.OperateInParens(vm.Env, vm.Image, continuation, this);
                return new CastResult(
                    this,
                    pres.NewContinuation,
                    pres.NewImage,
                    pres.SideEffects,
                    pres.ResolutionType,
                    pres.Sound);
            }

            var result = action.Operate(vm.Env, vm.Image, continuation);

            // P0-3 之后的主路径：行为用 Error 字段报告 mishap，而不是抛异常。
            if (result.Error != null)
            {
                return MishapResult(result.Error);
            }

            return new CastResult(
                this,
                result.NewContinuation,
                result.NewImage,
                result.SideEffects,
                ResolvedPatternType.Evaluated,
                result.Sound);
        }
        catch (Mishap mishap)
        {
            // 兜底：尚未改造为 Error 返回的行为仍可能抛 Mishap。
            return MishapResult(mishap);
        }

        CastResult MishapResult(Mishap mishap)
        {
            // 元求值中途 mishap 需要清空括号（否则括号会一直开着）
            bool wipeParens = continuation is SpellContinuation.NotDone cnd
                              && cnd.Frame is FrameEvaluate fe && fe.IsMetacasting;

            return new CastResult(
                this,
                continuation,
                wipeParens ? vm.Image.WithResetEscape() : null,
                new OperatorSideEffect[]
                {
                    new DoMishapSideEffect(mishap, MishapCtx()),
                },
                mishap.ResolutionType(vm.Env),
                EvalSound.Mishap);
        }
    }

    /// <summary>
    /// 源项目 PatternIota.execute：Mishap.Context(pattern, castedName) —— 聊天提示前缀「图案名：」。
    /// 名字曾经一律传 null。
    /// </summary>
    private MishapContext MishapCtx()
    {
        var def = PatternRegistry.Match(Pattern);
        return new MishapContext(Pattern, def is null ? null : PatternDisplay.DisplayName(def));
    }

    /// <summary>
    /// 构造一个 mishap 结果而不抛异常。
    /// mishap 是**正常控制流**（画错图案是预期行为），不该走异常，
    /// 否则会被 tModLoader 的异常日志记录机制刷屏。
    /// </summary>
    private CastResult BuildMishapResult(
        CastingVM vm, SpellContinuation continuation, Mishap mishap)
    {
        bool wipeParens = continuation is SpellContinuation.NotDone cnd
                          && cnd.Frame is FrameEvaluate fe && fe.IsMetacasting;

        return new CastResult(
            this,
            continuation,
            wipeParens ? vm.Image.WithResetEscape() : null,
            new OperatorSideEffect[]
            {
                new DoMishapSideEffect(mishap, MishapCtx()),
            },
            mishap.ResolutionType(vm.Env),
            EvalSound.Mishap);
    }
    /// <summary>
    /// 尝试把图案当作**特殊图案**执行（数字字面量 / 掩码）。
    /// 返回 false 表示「不是特殊图案」，交给正常的「无效图案」分支处理。
    ///
    /// 顺序与源项目一致：先试数字，再试掩码（掩码的形状要求更严，放后面当兜底更自然）。
    /// </summary>
    private bool TrySpecialPattern(CastingVM vm, SpellContinuation continuation, bool inParens,
                                   out CastResult result)
    {
        result = default!;

        string signature = Pattern.AnglesSignature();

        if (Math.SpecialPatterns.TryNumber(signature, out double number))
        {
            // 数字的取值为 0 消耗（对应源项目 InnerAction 的 ConstMediaAction(argc = 0)）
            var stack = new List<Iota>(vm.Image.Stack) { new DoubleIota(number) };
            var image = vm.Image.WithStack(stack).WithUsedOp();

            result = new CastResult(
                this, continuation, image,
                System.Array.Empty<OperatorSideEffect>(),
                ResolvedPatternType.Evaluated, EvalSound.NormalExecute);
            return true;
        }

        if (Math.SpecialPatterns.TryMask(Pattern, out bool[] mask))
        {
            // 掩码从栈上取 mask.Length 个值，只把打了勾的留下（源项目 InnerAction）
            var stack = new List<Iota>(vm.Image.Stack);

            if (stack.Count < mask.Length)
            {
                result = BuildMishapResult(vm, continuation,
                    new MishapNotEnoughArgs(mask.Length, stack.Count));
                return true;
            }

            var kept = new List<Iota>(mask.Length);
            int start = stack.Count - mask.Length;

            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i]) kept.Add(stack[start + i]);
            }

            stack.RemoveRange(start, mask.Length);
            stack.AddRange(kept);

            var image = vm.Image.WithStack(stack).WithUsedOp();
            result = new CastResult(
                this, continuation, image,
                System.Array.Empty<OperatorSideEffect>(),
                ResolvedPatternType.Evaluated, EvalSound.NormalExecute);
            return true;
        }

        return false;
    }
}

/// <summary>
/// 续延 iota：本质是**一份执行状态**（帧栈快照）。
/// 移植自源项目 ContinuationIota.java。
///
/// 关键语义：**执行它就是跳转** —— 用捕获的续延替换当前续延
/// （源项目 tooltip 字面就是 "jump_iota"）。闭包与转义都建立在这一点上。
///
/// 历史说明：早期版本把它简化成「种类标签 + 被捕获的 iota 列表」，
/// 那种表示**无法表达跳转语义**，因此 `eval/cc`（需要捕获当前续延）和
/// `undo` 都无法实现。现已改为持有真正的 <see cref="SpellContinuation"/>。
/// </summary>
public sealed class ContinuationIota : Iota
{
    public ContinuationIota(SpellContinuation continuation) => Continuation = continuation;

    /// <summary>被捕获的续延（帧栈快照）。</summary>
    public SpellContinuation Continuation { get; }

    public override IotaKind Kind => IotaKind.Continuation;

    public override string TypeName => "continuation";

    public override bool IsExecutable => true;

    /// <summary>源项目：续延恒为真值。</summary>
    public override bool IsTruthy() => true;

    /// <summary>
    /// 源项目的 equals 是 Java 默认的**引用相等**（SpellContinuation 未覆写 equals），
    /// 这里保持一致：只有同一个续延对象才算相等。
    /// </summary>
    public override bool ValueEquals(Iota other)
        => other is ContinuationIota c && ReferenceEquals(c.Continuation, Continuation);

    /// <summary>执行 = 跳到捕获的续延（源项目 ContinuationIota.execute）。</summary>
    public override CastResult Execute(CastingVM vm, SpellContinuation continuation)
        => new CastResult(
            this,
            Continuation,
            vm.Image,
            System.Array.Empty<OperatorSideEffect>(),
            ResolvedPatternType.Evaluated,
            EvalSound.Hermes);

    public override object? Serialize()
        // 源项目把续延序列化成 NBT（SpellContinuation.serializeToNBT）。
        // 未实现前必须显式报错，不能静默产出错误数据 ——
        // 续延里含帧栈，写成占位符会让存档/联网时产生「看起来正常但一跳就崩」的数据。
        => throw new System.NotSupportedException(
            "续延 iota 的序列化尚未实现（需要 SpellContinuation 的帧栈序列化，见 TODO_PLAN.md）");

    /// <summary>
    /// 源项目 size()：把整条链的帧大小累加后取 min(size, 1)，即只可能是 0 或 1。
    /// 用于判断该 iota 是否算「占用栈空间」。
    /// </summary>
    public int ChainSize()
    {
        int size = 0;
        var cont = Continuation;
        while (cont is SpellContinuation.NotDone nd)
        {
            size += 1;
            size += nd.Frame.Size();
            cont = nd.Next;
        }
        return System.Math.Min(size, 1);
    }

    protected override string DescribeValue() => "跳转目标";
}
