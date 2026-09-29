using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `read_into_parens`：从**手持的数据载体**读出一个 iota 并放进括号列表。
/// 移植自源项目 escaping/OpReadIntoParens.kt。
///
/// 只在括号内有效 —— 不在括号里时报 MishapNeedsParens，
/// 因为没有括号就没有「放进哪里」这回事。
///
/// 读进来的 iota 标记为 **escaped = true**：
/// 源项目注释写明「插入的括号不应调整括号计数」，
/// 所以这里也必须转义，否则括号计数会被读进来的值带偏。
/// </summary>
public sealed class OpReadIntoParens : IAction
{
    public OperationResult Operate(CastingEnvironment env, CastingImage image, SpellContinuation continuation)
        => OperationResult.Fail(new MishapNeedsParens(), image);

    public ParenthesizedOperationResult OperateInParens(
        CastingEnvironment env, CastingImage image, SpellContinuation continuation, Iota thisIota)
    {
        // 源项目：readIota ?: emptyIota ?: mishap —— 拿着**空**载体时插入空值，不报错（这里曾经直接报错）
        if (!env.HasHeldStorage())
        {
            throw new MishapBadHeldItem();
        }
        var datum = env.ReadHeldIota() ?? NullIota.Instance;

        var image2 = image.WithUsedOp().WithNewParenthesized(datum, escaped: true);

        return new ParenthesizedOperationResult(
            image2,
            System.Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Evaluated);
    }
}

/// <summary>
/// `write_iota`（源项目 `OpWriteIota`）：把栈顶的 iota 写进手持的数据载体。
///
/// 与 <see cref="OpReadIntoParens"/> 配对 —— 有读就得有写，
/// 否则玩家只能读到预置的内容，无法把自己的计算结果存下来复用。
/// </summary>
public sealed class OpWriteIota : IAction
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

        if (!env.WriteHeldIota(value))
        {
            return OperationResult.Fail(new MishapBadHeldItem(), image);
        }

        var image2 = image.WithStack(stack).WithUsedOp();
        return new OperationResult(image2, System.Array.Empty<OperatorSideEffect>(),
            continuation, EvalSound.NormalExecute);
    }
}

/// <summary>数据载体类图案的注册。</summary>
public static class StorageActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        PatternRegistry.RegisterAction("hexcasting:read_into_parens", new OpReadIntoParens());

        return PatternRegistry.RegisteredActionCount - before;
    }
}
