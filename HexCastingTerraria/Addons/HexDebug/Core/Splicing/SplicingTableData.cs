using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core.Splicing;

/// <summary>剪接台槽位里的物品能不能读写 iota（核心、法术书……）。由游戏侧实现。</summary>
public interface ISplicingHolder
{
    Iota? Read();

    bool Writable { get; }

    bool Write(Iota? value);
}

/// <summary>剪接台客户端看到的一个 iota：下标、括号深度（上游 SplicingTableIotaClientView）。</summary>
public sealed record SplicingIotaView(Iota Iota, int Index, int Depth);

/// <summary>剪接台给客户端的样子（上游 SplicingTableClientView）。</summary>
public sealed class SplicingClientView
{
    public List<SplicingIotaView>? List { get; init; }
    public Iota? Clipboard { get; init; }
    public bool IsListWritable { get; init; }
    public bool IsClipboardWritable { get; init; }
    public bool IsEnlightened { get; init; }
    public bool HasHex { get; init; }
    public int UndoSize { get; init; }
    public int UndoIndex { get; init; } = -1;

    public bool IsListReadable => List is not null;
    public bool IsClipboardReadable => Clipboard is not null;
    public int LastIndex => (List?.Count ?? 0) - 1;
    public int ListSize => List?.Count ?? 0;

    public bool IsInRange(int index) => List is not null && index >= 0 && index < List.Count;

    public static SplicingClientView Empty() => new();
}

/// <summary>
/// 服务端执行剪接操作时用的数据（上游 splicing/SplicingTableData.kt）：读出来的列表（可改的副本）、剪贴板、选区、视野。
/// 写回去时太大（无法存档）就不写；写成功就记一次耗媒质。
/// </summary>
public sealed class SplicingTableData
{
    public const int IotaButtons = 9;
    public const int ViewEndIndexOffset = IotaButtons - 1;

    private readonly Func<IReadOnlyList<Iota>, bool> _isTransferSafe;

    public SplicingTableData(bool hasPlayer, UndoStack undoStack, Selection? selection, int viewStartIndex,
        ISplicingHolder? listHolder, ISplicingHolder? clipboardHolder, Func<IReadOnlyList<Iota>, bool>? isTransferSafe = null)
    {
        HasPlayer = hasPlayer;
        UndoStack = undoStack;
        Selection = selection;
        ViewStartIndex = viewStartIndex;
        List = listHolder?.Read() is ListIota l ? l.Items.ToList() : null;
        ListWriter = listHolder is { Writable: true } ? listHolder : null;
        Clipboard = clipboardHolder?.Read();
        ClipboardWriter = clipboardHolder is { Writable: true } ? clipboardHolder : null;
        _isTransferSafe = isTransferSafe ?? (_ => true);
    }

    public bool HasPlayer { get; }
    public UndoStack UndoStack { get; }
    public Selection? Selection { get; set; }
    public int ViewStartIndex { get; set; }
    public List<Iota>? List { get; }
    public ISplicingHolder? ListWriter { get; }
    public Iota? Clipboard { get; }
    public ISplicingHolder? ClipboardWriter { get; }
    public bool ShouldConsumeMedia { get; set; }

    public int ViewEndIndex
    {
        get => ViewStartIndex + ViewEndIndexOffset;
        set => ViewStartIndex = value - ViewEndIndexOffset;
    }

    public void PushUndoState(bool hasList = false, IReadOnlyList<Iota>? list = null, bool hasClipboard = false, Iota? clipboard = null,
        bool hasSelection = false, Selection? selection = null)
        => UndoStack.Push(new UndoStack.Entry(hasList, hasList ? list!.ToList() : null, hasClipboard, clipboard, hasSelection, selection));

    public void MakeStartVisible(Selection s)
    {
        if (s is Selection.EdgeSel e) MakeEdgeVisible(e);
        else MakeIotaVisible(s.Start);
    }

    public void MakeEndVisible(Selection s)
    {
        if (s is Selection.EdgeSel e) MakeEdgeVisible(e);
        else MakeIotaVisible(s.End!.Value);
    }

    public void MakeToVisible(Selection s)
    {
        if (s is Selection.EdgeSel e) MakeEdgeVisible(e);
        else MakeIotaVisible(s.To!.Value);
    }

    private void MakeIotaVisible(int index)
    {
        if (index < ViewStartIndex) ViewStartIndex = index;
        else if (index > ViewEndIndex) ViewEndIndex = index;
    }

    /// <summary>缝在左边可见 = 右边那格可见；在右边可见 = 左边那格可见。</summary>
    public void MakeEdgeVisible(Selection.EdgeSel s)
    {
        if (s.Index < ViewStartIndex) ViewStartIndex = s.Index;
        else if (s.Index - 1 > ViewEndIndex) ViewEndIndex = s.Index - 1;
    }

    public bool WriteList(List<Iota> value) => WriteIota(ListWriter, new ListIota(value));

    public bool WriteClipboard(Iota? value) => WriteIota(ClipboardWriter, value);

    public bool WriteIota(ISplicingHolder? holder, Iota? value)
    {
        if (holder is null || (value is not null && CastingVM.IsStackTooLarge(new[] { value }))) return false;
        bool ok = holder.Write(value);
        if (ok) ShouldConsumeMedia = true;
        return ok;
    }

    /// <summary>上游：列表里有别人的真名就不许搬到剪贴板 / 从剪贴板搬进来。</summary>
    public bool IsClipboardTransferSafe(Iota value) => _isTransferSafe(new[] { value });
}
