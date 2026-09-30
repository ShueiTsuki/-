using System.Collections;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 函数式（持久化）列表。移植自 at.petrak.hexcasting.api.casting.SpellList。
///
/// 注意：高危点（spec 7.3 第①条）：**必须保持持久化、不可变**。
/// 用 List&lt;Iota&gt; 代替会导致 for_each / eval_breakable 迭代丢失或重复，
/// 且极难定位。这里忠实实现 LPair + LList 两个不可变节点。
/// </summary>
public abstract class SpellList : IEnumerable<Iota>
{
    public abstract bool NonEmpty { get; }

    /// <summary>列表头（首元素）。</summary>
    public abstract Iota Car { get; }

    /// <summary>去掉头之后的剩余列表。</summary>
    public abstract SpellList Cdr { get; }

    public static SpellList Empty => new LList(0, System.Array.Empty<Iota>());

    /// <summary>持久化 cons 节点。</summary>
    public sealed class LPair : SpellList
    {
        private readonly Iota _car;
        private readonly SpellList _cdr;

        public LPair(Iota car, SpellList cdr)
        {
            _car = car;
            _cdr = cdr;
        }

        public override bool NonEmpty => true;
        public override Iota Car => _car;
        public override SpellList Cdr => _cdr;
    }

    /// <summary>基于只读数组的尾部节点（叶子）。</summary>
    public sealed class LList : SpellList
    {
        private readonly int _idx;
        private readonly IReadOnlyList<Iota> _list;

        public LList(int idx, IReadOnlyList<Iota> list)
        {
            _idx = idx;
            _list = list;
        }

        public LList(IReadOnlyList<Iota> list) : this(0, list) { }

        public override bool NonEmpty => _idx < _list.Count;
        public override Iota Car => _list[_idx];
        public override SpellList Cdr => new LList(_idx + 1, _list);
    }

    /// <summary>元素数量（O(n)）。</summary>
    public int Size()
    {
        int size = 0;
        var ptr = this;
        while (ptr.NonEmpty)
        {
            ptr = ptr.Cdr;
            size++;
        }
        return size;
    }

    public List<Iota> ToList()
    {
        var result = new List<Iota>();
        var ptr = this;
        while (ptr.NonEmpty)
        {
            result.Add(ptr.Car);
            ptr = ptr.Cdr;
        }
        return result;
    }

    public IEnumerator<Iota> GetEnumerator()
    {
        var ptr = this;
        while (ptr.NonEmpty)
        {
            yield return ptr.Car;
            ptr = ptr.Cdr;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
