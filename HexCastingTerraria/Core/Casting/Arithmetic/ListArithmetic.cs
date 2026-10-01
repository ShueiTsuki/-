using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Arithmetic;

/// <summary>
/// 列表算术。对应源项目 ListArithmetic.kt。
///
/// 已实现：index / slice / append / unappend / add / abs / reverse /
///         index_of / remove_from / replace / construct(cons) / deconstruct(uncons)
///         —— 与源项目 ListArithmetic.kt 的 OPS 清单**完全对齐**。
///
/// 注意：`add` 与 `abs` 与数值/向量算术**同名**——
/// 分派靠操作数类型，因此这里只在「参数都是列表」时生效。
/// </summary>
public sealed class ListArithmetic : IArithmetic
{
    public string Name => "list_ops";

    private static bool AllList(IReadOnlyList<Iota> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is not ListIota) return false;
        }
        return args.Count > 0;
    }

    private static ListIota L(Iota i) => (ListIota)i;

    /// <summary>
    /// 源项目 Iota.tolerates：两个双精度在 TOLERANCE 内视为相等，其余按自身相等性。
    /// 用于 index_of 的查找。
    /// </summary>
    private static bool Tolerates(Iota a, Iota b) => Iota.Tolerates(a, b);

    /// <summary>
    /// 源项目 nextInt：要求是「整数值的双精度」，容差 TOLERANCE，否则报 mishap。
    /// 注意与 Floor 的区别——3.7 不是合法索引，原版会报错而不是截断。
    /// </summary>
    private static int NextInt(Iota iota) => CastingEnvironment.RequireIndex(iota);

    /// <summary>
    /// 源项目 nextPositiveIntUnder：必须是 [0, max) 内的整数。
    /// 注意：上游这里报的说法是 int.positive.less.equal（「小于等于 max」），照抄。
    /// </summary>
    private static int NextPositiveIntUnder(Iota iota, int max)
    {
        try
        {
            return CastingEnvironment.RequirePositiveIntUnder(iota, max);
        }
        catch (MishapInvalidIota)
        {
            throw new MishapInvalidIota(iota, InvalidValue.IntPositiveLessEqual(max));
        }
    }

    /// <summary>源项目 nextPositiveIntUnderInclusive：必须是 [0, max] 内的整数（闭区间）。</summary>
    private static int NextPositiveIntUnderInclusive(Iota iota, int max) => CastingEnvironment.RequirePositiveIntUnderInclusive(iota, max);

    private static ListIota RequireList(Iota iota)
    {
        if (iota is ListIota l) return l;
        throw new MishapInvalidIota(iota, InvalidValue.List);
    }

    public int Arity(string op) => op switch
    {
        "index" or "append" or "construct" or "add" => 2,
        "index_of" or "remove_from" => 2,
        "slice" or "replace" => 3,
        "reverse" or "deconstruct" or "abs" or "unappend" => 1,
        _ => -1,
    };

    public IReadOnlyList<Iota>? Apply(string op, IReadOnlyList<Iota> args)
    {
        // ---- 一元 ----
        if (op is "reverse" or "deconstruct" or "abs" or "unappend")
        {
            if (args.Count < 1 || args[0] is not ListIota list0) return null;

            switch (op)
            {
                case "abs":
                    // 列表长度 → number
                    return new Iota[] { new DoubleIota(list0.Count) };

                case "reverse":
                {
                    var items = new List<Iota>(list0.Items);
                    items.Reverse();
                    return new Iota[] { new ListIota(items) };
                }

                case "unappend":
                {
                    // 源项目：吐 [去掉末元素的列表, 末元素]；空列表时末元素是 NullIota（**不报错**）
                    var items = new List<Iota>(list0.Items);
                    Iota last = items.Count > 0 ? items[items.Count - 1] : NullIota.Instance;
                    if (items.Count > 0) items.RemoveAt(items.Count - 1);
                    return new Iota[] { new ListIota(items), last };
                }

                case "deconstruct":
                {
                    // 源项目 OperatorUnCons：吐 [剩余列表, 首元素] —— **首元素在栈顶**；
                    // 空列表**不报错**，吐 [原列表, 空]。（这里曾经顺序反了、空列表还报错）
                    if (list0.Count == 0)
                    {
                        return new Iota[] { list0, NullIota.Instance };
                    }
                    var rest = new List<Iota>();
                    for (int i = 1; i < list0.Count; i++) rest.Add(list0.Items[i]);
                    return new Iota[] { new ListIota(rest), list0.Items[0] };
                }
            }
        }

        // ---- 二元 ----
        if (args.Count < 2) return null;

        // add：两个列表拼接
        if (op == "add")
        {
            if (args[0] is not ListIota la || args[1] is not ListIota lb) return null;
            var merged = new List<Iota>(la.Items);
            merged.AddRange(lb.Items);
            return new Iota[] { new ListIota(merged) };
        }

        // 其余二元运算要求第一个参数是列表
        if (args[0] is not ListIota list) return null;

        switch (op)
        {
            case "index":
            {
                if (args[1] is not DoubleIota idx) return null;
                // 源项目 OperatorIndex：roundToInt（四舍五入），越界返回 Null —— **不报错**
                //（这里曾经取底并在越界时报错）
                double r = System.Math.Floor(idx.Value + 0.5);
                if (r < 0 || r >= list.Count)
                {
                    return new Iota[] { NullIota.Instance };
                }
                return new Iota[] { list.Items[(int)r] };
            }

            case "append":
            {
                // 列表 + 任意值 → 末尾追加
                var items = new List<Iota>(list.Items) { args[1] };
                return new Iota[] { new ListIota(items) };
            }

            case "construct":
            {
                // cons：列表 + 任意值 → 头部插入
                var items = new List<Iota> { args[1] };
                items.AddRange(list.Items);
                return new Iota[] { new ListIota(items) };
            }

            case "index_of":
            {
                // 找不到返回 -1（源项目 indexOfFirst 的语义），**不报错**
                var target = args[1];
                int found = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (Tolerates(target, list.Items[i])) { found = i; break; }
                }
                return new Iota[] { new DoubleIota(found) };
            }

            case "remove_from":
            {
                // 索引越界 → 原样返回列表（源项目同，不报错）
                int i = NextInt(args[1]);
                var items = new List<Iota>(list.Items);
                if (i >= 0 && i < items.Count) items.RemoveAt(i);
                return new Iota[] { new ListIota(items) };
            }

            case "slice":
            {
                // 两个端点都是闭区间 [0, size]，允许 size（取到末尾）
                int i0 = NextPositiveIntUnderInclusive(args[1], list.Count);
                int i1 = NextPositiveIntUnderInclusive(args[2], list.Count);
                if (i0 == i1) return new Iota[] { new ListIota(new List<Iota>()) };
                int lo = System.Math.Min(i0, i1), hi = System.Math.Max(i0, i1);
                var items = new List<Iota>();
                for (int i = lo; i < hi; i++) items.Add(list.Items[i]);
                return new Iota[] { new ListIota(items) };
            }

            case "replace":
            {
                // 索引必须在 [0, size)，越界报错
                int i = NextPositiveIntUnder(args[1], list.Count);
                var items = new List<Iota>(list.Items) { [i] = args[2] };
                return new Iota[] { new ListIota(items) };
            }
        }

        return null;
    }
}
