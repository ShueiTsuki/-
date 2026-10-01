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
        // 源项目：readIota ?: emptyIota ?: mishap —— 原版没有哪个物品定义了 emptyIota，空载体同样报错
        var datum = env.ReadHeldIota() ?? throw new MishapBadHeldItem(MishapBadHeldItem.Need.Read, actual: env.HeldStorageItem());

        var image2 = image.WithUsedOp().WithNewParenthesized(datum, escaped: true);

        return new ParenthesizedOperationResult(
            image2,
            System.Array.Empty<OperatorSideEffect>(),
            continuation,
            EvalSound.NormalExecute,
            ResolvedPatternType.Evaluated);
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
