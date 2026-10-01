using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core.Splicing;

/// <summary>
/// 剪接台的选区（上游 splicing/Selection.kt）：要么是闭区间 [from, to]（可以反着拖），要么是两个格子之间的一条缝（光标）。
/// 相等只看 from 与 to。
/// </summary>
public abstract class Selection : IEquatable<Selection>
{
    private Selection(int from, int? to)
    {
        From = from;
        To = to;
    }

    public int From { get; }

    public int? To { get; }

    public abstract int Start { get; }

    public abstract int? End { get; }

    /// <summary>区间：末尾下标；缝：左边那一格的下标。</summary>
    public abstract int LastIndex { get; }

    public int Size => End is { } e ? e - Start + 1 : 0;

    protected bool IsValid => Start >= 0 && (End is not { } e || e >= Start);

    public abstract bool Contains(int value);

    /// <summary>正数往右扩，负数往左扩。</summary>
    public abstract Selection? ExpandBy(int extra);

    public abstract Selection? MoveBy(int delta);

    public List<T> SubList<T>(IReadOnlyList<T> list) => list.Skip(Start).Take((End is { } e ? e + 1 : Start) - Start).ToList();

    /// <summary>上游 mutableSubList(list).clear()。</summary>
    public void RemoveFrom<T>(List<T> list) => list.RemoveRange(Start, (End is { } e ? e + 1 : Start) - Start);

    public bool Equals(Selection? other) => other is not null && From == other.From && To == other.To;

    public override bool Equals(object? obj) => obj is Selection s && Equals(s);

    public override int GetHashCode() => HashCode.Combine(From, To);

    public static bool operator ==(Selection? a, Selection? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Selection? a, Selection? b) => !(a == b);

    public sealed class RangeSel : Selection
    {
        private RangeSel(int from, int to) : base(from, to) { }

        public override int Start => Math.Min(From, To!.Value);

        public override int? End => Math.Max(From, To!.Value);

        public int EndValue => End!.Value;

        public int ToValue => To!.Value;

        public override int LastIndex => EndValue;

        public override bool Contains(int value) => value >= Start && value <= EndValue;

        public override Selection? ExpandBy(int extra) => Of(From, ToValue + extra);

        public override Selection? MoveBy(int delta) => Of(From + delta, ToValue + delta);

        public static RangeSel? Of(int from, int to)
        {
            var r = new RangeSel(from, to);
            return r.IsValid ? r : null;
        }
    }

    /// <summary>缝：Index 是缝右边那一格的下标。</summary>
    public sealed class EdgeSel : Selection
    {
        private EdgeSel(int index) : base(index, null) { }

        public int Index => From;

        public override int Start => Index;

        public override int? End => null;

        public override int LastIndex => Index - 1;

        public override bool Contains(int value) => false;

        public override Selection? ExpandBy(int extra) => extra switch
        {
            > 0 => Range(Index, Index + extra - 1),
            < 0 => Range(Index - 1, Index + extra),
            _ => this,
        };

        public override Selection? MoveBy(int delta) => Of(Index + delta);

        public static EdgeSel? Of(int index)
        {
            var e = new EdgeSel(index);
            return e.IsValid ? e : null;
        }
    }

    public static RangeSel? WithSize(int from, int size) => Range(from, from + size - 1);

    public static Selection? Of(int from, int? to) => to is { } t ? Range(from, t) : Edge(from);

    public static RangeSel? Range(int from, int to) => RangeSel.Of(from, to);

    public static EdgeSel? Edge(int index) => EdgeSel.Of(index);

    /// <summary>存档里的写法：from &lt; 0 = 没有选区；to &lt; 0 = 缝。</summary>
    public static Selection? FromRawIndices(int from, int to) => from < 0 ? null : to < 0 ? Edge(from) : Range(from, to);

    public override string ToString() => To is { } t ? $"[{From}..{t}]" : $"|{From}";
}

/// <summary>撤销栈（上游 splicing/UndoStack.kt）：每一项记下列表 / 剪贴板 / 选区里哪些变了。</summary>
public sealed class UndoStack
{
    public readonly record struct Entry(
        bool HasList, IReadOnlyList<Iota>? List,
        bool HasClipboard, Iota? Clipboard,
        bool HasSelection, Selection? Selection)
    {
        public bool IsNotEmpty => HasList || HasClipboard || HasSelection;

        public void ApplyTo(SplicingTableData data)
        {
            if (HasList) data.WriteList(List!.ToList());
            if (HasClipboard) data.WriteClipboard(Clipboard);
            if (HasSelection) data.Selection = Selection;
        }
    }

    public List<Entry> Stack { get; } = new();

    public int Index { get; set; } = -1;

    /// <summary>上游 maxUndoStackSize（0 = 不限）。</summary>
    public int MaxSize { get; set; } = 64;

    public int Size => Stack.Count;

    public Entry? Undo() => MoveTo(Index - 1);

    public Entry? Redo() => MoveTo(Index + 1);

    private Entry? MoveTo(int newIndex)
    {
        if (newIndex < 0 || newIndex >= Stack.Count) return null;
        Index = newIndex;
        return Stack[newIndex];
    }

    public void Push(Entry entry)
    {
        // 撤销过再改：丢掉当前位置之后的
        if (Index < Stack.Count - 1) Stack.RemoveRange(Index + 1, Stack.Count - Index - 1);
        Stack.Add(entry);
        // 超过上限：丢掉最早的
        if (MaxSize > 0 && Stack.Count > MaxSize) Stack.RemoveRange(0, Stack.Count - MaxSize);
        Index = Stack.Count - 1;
    }

    public void Clear()
    {
        Stack.Clear();
        Index = -1;
    }
}
