using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Eval.Vm;

namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// 一对「括号内的 iota + 是否由 Consideration 转义而来」。
/// escaped 供 OpUndo 判断撤销该 iota 时是否需要调整括号计数
/// （被转义的括号不影响计数）。
/// </summary>
public readonly struct ParenthesizedIota
{
    public Iota Iota { get; }
    public bool Escaped { get; }

    public ParenthesizedIota(Iota iota, bool escaped)
    {
        Iota = iota;
        Escaped = escaped;
    }
}

/// <summary>
/// 施法 VM 的完整状态。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.CastingImage（Kotlin data class）。
///
/// 注意：不可变语义（spec 7.3 第③条）：
/// 所有「修改」都返回**新实例**，绝不就地改。主循环靠 CastResult.NewData 是否为 null
/// 决定是否采纳状态；就地修改会让括号状态与 mishap 语义错乱。
/// </summary>
public sealed class CastingImage
{
    public IReadOnlyList<Iota> Stack { get; }

    /// <summary>当前打开的括号层数（&gt;0 表示正在构建列表）。</summary>
    public int ParenCount { get; }

    /// <summary>括号内已累积的 iota。</summary>
    public IReadOnlyList<ParenthesizedIota> Parenthesized { get; }

    /// <summary>下一个 iota 是否要被转义（Consideration）。</summary>
    public bool EscapeNext { get; }

    /// <summary>已消耗的求值步数。用于 EvalTooMuch 判定。</summary>
    public long OpsConsumed { get; }

    /// <summary>
    /// 本次施法的临时数据袋（不序列化、不进存档）。
    /// 见 <see cref="CastUserData"/> 的说明。
    /// </summary>
    public CastUserData UserData { get; }

    public CastingImage(
        IReadOnlyList<Iota>? stack = null,
        int parenCount = 0,
        IReadOnlyList<ParenthesizedIota>? parenthesized = null,
        bool escapeNext = false,
        long opsConsumed = 0,
        CastUserData? userData = null)
    {
        Stack = stack ?? new List<Iota>();
        ParenCount = parenCount;
        Parenthesized = parenthesized ?? new List<ParenthesizedIota>();
        EscapeNext = escapeNext;
        OpsConsumed = opsConsumed;
        UserData = userData ?? new CastUserData();
    }

    public CastingImage WithStack(IReadOnlyList<Iota> stack)
        => new CastingImage(stack, ParenCount, Parenthesized, EscapeNext, OpsConsumed, UserData);

    /// <summary>返回消耗了 count 步的新实例。</summary>
    public CastingImage WithUsedOps(long count)
        => new CastingImage(Stack, ParenCount, Parenthesized, EscapeNext, OpsConsumed + count, UserData);

    public CastingImage WithUsedOp() => WithUsedOps(1);

    /// <summary>把 opsConsumed 直接覆盖为 count（对应 withOverriddenUsedOps）。</summary>
    public CastingImage WithOverriddenUsedOps(long count)
        => new CastingImage(Stack, ParenCount, Parenthesized, EscapeNext, count, UserData);

    /// <summary>清除转义/括号相关字段。</summary>
    public CastingImage WithResetEscape()
        => new CastingImage(Stack, 0, new List<ParenthesizedIota>(), false, OpsConsumed, UserData);

    /// <summary>把一个 iota 追加到括号列表（返回新实例）。</summary>
    public CastingImage WithNewParenthesized(Iota iota, bool escaped = false)
    {
        var list = new List<ParenthesizedIota>(Parenthesized) { new ParenthesizedIota(iota, escaped) };
        return new CastingImage(Stack, ParenCount, list, EscapeNext, OpsConsumed, UserData);
    }

    /// <summary>
    /// 整体替换括号列表（返回新实例）。
    /// `undo` 要「弹出最后一项」，只靠 WithNewParenthesized（只能追加）做不到，
    /// 所以补一个整体替换的重载。
    /// </summary>
    public CastingImage WithParenthesized(IReadOnlyList<ParenthesizedIota> list)
        => new CastingImage(Stack, ParenCount, new List<ParenthesizedIota>(list), EscapeNext, OpsConsumed, UserData);

    /// <summary>清空括号列表（返回新实例）。</summary>
    public CastingImage WithClearedParenthesized()
        => new CastingImage(Stack, ParenCount, new List<ParenthesizedIota>(), EscapeNext, OpsConsumed, UserData);

    /// <summary>修改括号计数（返回新实例）。</summary>
    public CastingImage WithParenCount(int count)
        => new CastingImage(Stack, count, Parenthesized, EscapeNext, OpsConsumed, UserData);

    /// <summary>修改 escapeNext（返回新实例）。</summary>
    public CastingImage WithEscapeNext(bool value)
        => new CastingImage(Stack, ParenCount, Parenthesized, value, OpsConsumed, UserData);

    /// <summary>替换临时数据袋（返回新实例）。</summary>
    public CastingImage WithUserData(CastUserData userData)
        => new CastingImage(Stack, ParenCount, Parenthesized, EscapeNext, OpsConsumed, userData);

    /// <summary>栈是否为空且无未完成括号 —— 供客户端判断是否可关闭界面。</summary>
    public bool IsStackClear()
        => Stack.Count == 0 && ParenCount == 0 && !EscapeNext;

    /// <summary>
    /// 把某个实体标记为「本次施法已推动过」，并返回**此前是否已标记**。
    /// 移植自源项目 `CastingImage.checkAndMarkGivenMotion`。
    ///
    /// 语义：第一次调用返回 false 并打上标记；之后再调用返回 true。
    /// `add_motion` 用它给重复推动加价（源项目 bug #387）——
    /// 否则可以用许多次极小的推动把速度堆起来，而每次的媒质消耗都趋近于零。
    /// </summary>
    public static bool CheckAndMarkGivenMotion(CastUserData userData, EntityIota entity)
    {
        // 源项目用 entity.stringUUID；泰拉侧用「种类 + 索引」作为实体身份
        string key = $"{entity.Target}#{entity.Index}";

        if (userData.Has(CastUserData.MarkedMovedGroup, key))
        {
            return true;
        }

        userData.Add(CastUserData.MarkedMovedGroup, key);
        return false;
    }
}
