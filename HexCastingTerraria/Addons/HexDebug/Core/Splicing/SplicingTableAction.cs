using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexDebug.Core.Splicing;

/// <summary>剪接台的按钮操作（上游 splicing/SplicingTableAction.kt，顺序与名字照搬）。</summary>
public enum SplicingTableAction
{
    ViewLeft,
    ViewLeftPage,
    ViewLeftFull,
    ViewRight,
    ViewRightPage,
    ViewRightFull,
    CursorLeft,
    CursorRight,
    ExpandSelectionLeft,
    ExpandSelectionRight,
    MoveSelectionLeft,
    MoveSelectionRight,
    SelectNone,
    SelectAll,
    Undo,
    Redo,
    NudgeLeft,
    NudgeRight,
    Duplicate,
    Delete,
    Backspace,
    Cut,
    Copy,
    PasteVerbatim,
    PasteSplat,
}

/// <summary>
/// 每个操作要的数据（上游 SplicingTableDataConverter 的几种）、客户端判断按钮亮不亮（test）、服务端再验一遍（validate）并执行（run）。
/// </summary>
public static class SplicingActions
{
    private const int Buttons = SplicingTableData.IotaButtons;

    private enum Need
    {
        ReadList,
        ReadWriteList,
        ReadWriteListFromClipboard,
        ReadWriteListRange,
        ReadWriteListRangeToClipboard,
        ReadListRangeToClipboard,
    }

    private static Need NeedOf(SplicingTableAction a) => a switch
    {
        SplicingTableAction.NudgeLeft or SplicingTableAction.NudgeRight or SplicingTableAction.Duplicate or SplicingTableAction.Delete => Need.ReadWriteListRange,
        SplicingTableAction.Backspace => Need.ReadWriteList,
        SplicingTableAction.Cut => Need.ReadWriteListRangeToClipboard,
        SplicingTableAction.Copy => Need.ReadListRangeToClipboard,
        SplicingTableAction.PasteVerbatim or SplicingTableAction.PasteSplat => Need.ReadWriteListFromClipboard,
        _ => Need.ReadList,
    };

    /// <summary>上游 consumesMedia：只挪视野 / 选区的不耗媒质。</summary>
    public static bool ConsumesMedia(SplicingTableAction a) => a >= SplicingTableAction.Undo;

    // ==================== 客户端：按钮亮不亮 ====================

    private static bool ClientNeed(Need n, SplicingClientView v, Selection? sel) => n switch
    {
        Need.ReadList => v.IsListReadable,
        Need.ReadWriteList => sel is not null && v.IsListReadable && v.IsListWritable,
        Need.ReadWriteListFromClipboard => sel is not null && v.IsListReadable && v.IsListWritable && v.IsClipboardReadable,
        Need.ReadWriteListRange => sel is Selection.RangeSel && v.IsListReadable && v.IsListWritable,
        Need.ReadWriteListRangeToClipboard => sel is Selection.RangeSel && v.IsListReadable && v.IsListWritable && v.IsClipboardWritable,
        _ => sel is Selection.RangeSel && v.IsListReadable && v.IsClipboardWritable,
    };

    public static bool Test(SplicingTableAction a, SplicingClientView v, Selection? sel, int viewStart)
    {
        if (!ClientNeed(NeedOf(a), v, sel)) return false;
        return a switch
        {
            SplicingTableAction.ViewLeft or SplicingTableAction.ViewLeftPage or SplicingTableAction.ViewLeftFull => viewStart > 0,
            SplicingTableAction.ViewRight or SplicingTableAction.ViewRightPage or SplicingTableAction.ViewRightFull
                => viewStart + SplicingTableData.ViewEndIndexOffset < v.LastIndex,
            SplicingTableAction.CursorLeft => sel is not Selection.EdgeSel e || e.Index > 0,
            SplicingTableAction.CursorRight => sel is not Selection.EdgeSel e2 || e2.Index <= v.LastIndex,
            SplicingTableAction.ExpandSelectionLeft or SplicingTableAction.MoveSelectionLeft => sel is not null && sel.Start > 0,
            SplicingTableAction.ExpandSelectionRight or SplicingTableAction.MoveSelectionRight => sel is not null && sel.LastIndex < v.LastIndex,
            SplicingTableAction.SelectNone => sel is not null,
            SplicingTableAction.SelectAll => sel != Selection.Range(0, v.LastIndex) && v.ListSize > 0,
            SplicingTableAction.Undo => v.UndoSize > 1 && v.UndoIndex > 0,
            SplicingTableAction.Redo => v.UndoSize > 1 && v.UndoIndex < v.UndoSize - 1,
            SplicingTableAction.NudgeLeft => sel is not null && sel.Start > 0,
            SplicingTableAction.NudgeRight => sel is Selection.RangeSel r && v.List is not null && r.EndValue < v.LastIndex,
            SplicingTableAction.Backspace => sel is not Selection.EdgeSel e3 || e3.Index > 0,
            _ => true,
        };
    }

    // ==================== 服务端：验证并执行 ====================

    private static bool ServerNeed(Need n, SplicingTableData d) => d.HasPlayer && d.List is not null && n switch
    {
        Need.ReadList => true,
        Need.ReadWriteList => d.Selection is not null && d.ListWriter is not null,
        Need.ReadWriteListFromClipboard => d.Selection is not null && d.ListWriter is not null && d.Clipboard is not null,
        Need.ReadWriteListRange => d.Selection is Selection.RangeSel && d.ListWriter is not null,
        Need.ReadWriteListRangeToClipboard => d.Selection is Selection.RangeSel && d.ListWriter is not null && d.ClipboardWriter is not null,
        _ => d.Selection is Selection.RangeSel && d.ClipboardWriter is not null,
    };

    private static bool Validate(SplicingTableAction a, SplicingTableData d)
    {
        var list = d.List!;
        var sel = d.Selection;
        return a switch
        {
            SplicingTableAction.ViewLeft or SplicingTableAction.ViewLeftPage or SplicingTableAction.ViewLeftFull => d.ViewStartIndex > 0,
            SplicingTableAction.ViewRight or SplicingTableAction.ViewRightPage or SplicingTableAction.ViewRightFull => d.ViewEndIndex < list.Count - 1,
            SplicingTableAction.CursorLeft => sel is not Selection.EdgeSel e || e.Index > 0,
            SplicingTableAction.CursorRight => sel is not Selection.EdgeSel e2 || e2.Index <= list.Count - 1,
            SplicingTableAction.ExpandSelectionLeft or SplicingTableAction.MoveSelectionLeft => sel is not null && sel.Start > 0,
            SplicingTableAction.ExpandSelectionRight or SplicingTableAction.MoveSelectionRight => sel is not null && sel.LastIndex < list.Count - 1,
            SplicingTableAction.SelectNone => sel is not null,
            SplicingTableAction.SelectAll => sel != Selection.Range(0, list.Count - 1) && list.Count > 0,
            SplicingTableAction.Undo => d.UndoStack.Size > 1 && d.UndoStack.Index > 0,
            SplicingTableAction.Redo => d.UndoStack.Size > 1 && d.UndoStack.Index < d.UndoStack.Size - 1,
            SplicingTableAction.NudgeLeft => sel!.Start > 0,
            SplicingTableAction.NudgeRight => ((Selection.RangeSel)sel!).EndValue < list.Count - 1,
            SplicingTableAction.Backspace => sel is not Selection.EdgeSel e3 || e3.Index > 0,
            _ => true,
        };
    }

    /// <summary>执行；数据不够或验证不过返回 false（什么都不做）。</summary>
    public static bool Run(SplicingTableAction a, SplicingTableData d)
    {
        if (!ServerNeed(NeedOf(a), d) || !Validate(a, d)) return false;
        var list = d.List!;
        switch (a)
        {
            case SplicingTableAction.ViewLeft: d.ViewStartIndex -= 1; break;
            case SplicingTableAction.ViewLeftPage: d.ViewStartIndex -= Buttons; break;
            case SplicingTableAction.ViewLeftFull: d.ViewStartIndex = 0; break;
            case SplicingTableAction.ViewRight: d.ViewEndIndex += 1; break;
            case SplicingTableAction.ViewRightPage: d.ViewEndIndex += Buttons; break;
            case SplicingTableAction.ViewRightFull: d.ViewEndIndex = list.Count - 1; break;

            case SplicingTableAction.CursorLeft:
            {
                int newIndex = d.Selection switch
                {
                    Selection.RangeSel r => r.Start,
                    Selection.EdgeSel e => e.Index - 1,
                    _ => Math.Min(d.ViewStartIndex + Buttons / 2, list.Count),
                };
                d.Selection = Selection.Edge(newIndex);
                if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                break;
            }
            case SplicingTableAction.CursorRight:
            {
                int newIndex = d.Selection switch
                {
                    Selection.RangeSel r => r.EndValue + 1,
                    Selection.EdgeSel e => e.Index + 1,
                    _ => Math.Min(d.ViewStartIndex + Buttons / 2 + 1, list.Count),
                };
                d.Selection = Selection.Edge(newIndex);
                if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                break;
            }
            case SplicingTableAction.ExpandSelectionLeft:
                d.Selection = d.Selection!.ExpandBy(-1);
                if (d.Selection is { } s1) d.MakeToVisible(s1);
                break;
            case SplicingTableAction.ExpandSelectionRight:
                d.Selection = d.Selection!.ExpandBy(1);
                if (d.Selection is { } s2) d.MakeToVisible(s2);
                break;
            case SplicingTableAction.MoveSelectionLeft:
                d.Selection = d.Selection!.MoveBy(-1);
                if (d.Selection is { } s3) d.MakeStartVisible(s3);
                break;
            case SplicingTableAction.MoveSelectionRight:
                d.Selection = d.Selection!.MoveBy(1);
                if (d.Selection is { } s4) d.MakeEndVisible(s4);
                break;
            case SplicingTableAction.SelectNone: d.Selection = null; break;
            case SplicingTableAction.SelectAll: d.Selection = Selection.Range(0, list.Count - 1); break;
            case SplicingTableAction.Undo: d.UndoStack.Undo()?.ApplyTo(d); break;
            case SplicingTableAction.Redo: d.UndoStack.Redo()?.ApplyTo(d); break;

            case SplicingTableAction.NudgeLeft:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                var moved = list[sel.Start - 1];
                list.RemoveAt(sel.Start - 1);
                list.Insert(sel.EndValue, moved);
                if (d.WriteList(list))
                {
                    d.Selection = sel.MoveBy(-1);
                    if (d.Selection is { } ns) d.MakeStartVisible(ns);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.NudgeRight:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                var moved = list[sel.EndValue + 1];
                list.RemoveAt(sel.EndValue + 1);
                list.Insert(sel.Start, moved);
                if (d.WriteList(list))
                {
                    d.Selection = sel.MoveBy(1);
                    if (d.Selection is { } ns) d.MakeEndVisible(ns);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.Duplicate:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                list.InsertRange(sel.EndValue + 1, sel.SubList(list));
                if (d.WriteList(list))
                {
                    d.Selection = Selection.WithSize(sel.EndValue + 1, sel.Size);
                    if (d.Selection is { } ns) d.MakeEndVisible(ns);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.Delete:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                sel.RemoveFrom(list);
                if (d.WriteList(list))
                {
                    d.Selection = Selection.Edge(sel.Start);
                    if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.Backspace:
            {
                var sel = d.Selection is Selection.EdgeSel e ? e.ExpandBy(-1)! : d.Selection!;
                sel.RemoveFrom(list);
                if (d.WriteList(list))
                {
                    d.Selection = Selection.Edge(sel.Start);
                    if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.Cut:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                var sub = sel.SubList(list);
                Iota iota = sub.Count == 1 ? sub[0] : new ListIota(sub);
                sel.RemoveFrom(list);
                if (d.IsClipboardTransferSafe(iota) && d.WriteClipboard(iota))
                {
                    if (d.WriteList(list))
                    {
                        d.Selection = Selection.Edge(sel.Start);
                        if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                        d.PushUndoState(hasList: true, list: list, hasClipboard: true, clipboard: iota, hasSelection: true, selection: d.Selection);
                    }
                    else
                    {
                        d.PushUndoState(hasClipboard: true, clipboard: iota);
                    }
                }
                break;
            }
            case SplicingTableAction.Copy:
            {
                var sel = (Selection.RangeSel)d.Selection!;
                var sub = sel.SubList(list);
                Iota iota = sub.Count == 1 ? sub[0] : new ListIota(sub);
                if (d.IsClipboardTransferSafe(iota) && d.WriteClipboard(iota))
                {
                    d.PushUndoState(hasClipboard: true, clipboard: iota);
                }
                break;
            }
            case SplicingTableAction.PasteVerbatim:
            {
                var sel = d.Selection!;
                var clip = d.Clipboard!;
                sel.RemoveFrom(list);
                list.Insert(sel.Start, clip);
                if (d.IsClipboardTransferSafe(clip) && d.WriteList(list))
                {
                    d.Selection = Selection.Edge(sel.Start + 1);
                    if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
            case SplicingTableAction.PasteSplat:
            {
                var sel = d.Selection!;
                var clip = d.Clipboard!;
                var values = clip is ListIota li ? li.Items.ToList() : new List<Iota> { clip };
                sel.RemoveFrom(list);
                list.InsertRange(sel.Start, values);
                if (d.IsClipboardTransferSafe(clip) && d.WriteList(list))
                {
                    d.Selection = Selection.Edge(sel.Start + values.Count);
                    if (d.Selection is Selection.EdgeSel ne) d.MakeEdgeVisible(ne);
                    d.PushUndoState(hasList: true, list: list, hasSelection: true, selection: d.Selection);
                }
                break;
            }
        }
        return true;
    }
}
