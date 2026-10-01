using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.HexDebug.Core.Splicing;

/// <summary>
/// 剪接台方块实体里与游戏无关的那部分（上游 blocks/splicing/SplicingTableBlockEntity.kt 的
/// runAction / drawPattern / selectIndex / clampView / getClientView / listStackChanged / clipboardStackChanged）。
/// 游戏侧提供槽位里的物品（<see cref="ISplicingHolder"/>）与媒质。
/// </summary>
public sealed class SplicingTableState
{
    public Selection? Selection { get; set; }

    public int ViewStartIndex { get; set; }

    /// <summary>上游注释「TODO: save?」：撤销栈不存档。</summary>
    public UndoStack UndoStack { get; } = new();

    public SplicingTableData Data(bool hasPlayer, ISplicingHolder? list, ISplicingHolder? clipboard, Func<IReadOnlyList<Iota>, bool>? isTransferSafe = null)
        => new(hasPlayer, UndoStack, Selection, ViewStartIndex, list, clipboard, isTransferSafe);

    /// <summary>第一次操作前先把当前状态记成撤销栈的底。</summary>
    private void SetupUndoStack(SplicingTableData data)
    {
        if (UndoStack.Size == 0)
        {
            data.PushUndoState(hasList: true, list: data.List ?? new List<Iota>(), hasClipboard: true, clipboard: data.Clipboard,
                hasSelection: true, selection: Selection);
        }
    }

    /// <summary>上游 runAction：媒质不够的「耗媒质」操作不做。返回要不要扣一次媒质。</summary>
    public bool RunAction(SplicingTableAction action, SplicingTableData data, long media, long mediaCost)
    {
        if (SplicingActions.ConsumesMedia(action) && media < mediaCost) return false;
        ClampView(data.List, data);
        SetupUndoStack(data);
        if (!SplicingActions.Run(action, data)) return false;
        return PostRunAction(data);
    }

    /// <summary>
    /// 上游 drawPattern：在剪接台的小画布上画一个图案，替换掉选区（或插在光标处）。成功是「转义」色，失败是「出错」色。
    /// 第二个返回值：要不要扣一次媒质。
    /// </summary>
    public (ResolvedPatternType Type, bool ConsumeMedia) DrawPattern(HexPattern pattern, SplicingTableData data, long media, long mediaCost)
    {
        if (media < mediaCost) return (ResolvedPatternType.Errored, false);
        if (!data.HasPlayer || data.List is null || data.ListWriter is null || data.Selection is null) return (ResolvedPatternType.Errored, false);
        ClampView(data.List, data);
        SetupUndoStack(data);
        if (data.Selection is null) return (ResolvedPatternType.Errored, PostRunAction(data));

        var sel = data.Selection;
        var list = data.List;
        sel.RemoveFrom(list);
        list.Insert(sel.Start, new PatternIota(pattern));
        ResolvedPatternType result;
        if (data.WriteList(list))
        {
            data.ShouldConsumeMedia = true;
            data.Selection = Selection.Edge(sel.Start + 1);
            if (data.Selection is Selection.EdgeSel e) data.MakeEdgeVisible(e);
            data.PushUndoState(hasList: true, list: list, hasSelection: true, selection: data.Selection);
            result = ResolvedPatternType.Escaped;
        }
        else
        {
            result = ResolvedPatternType.Errored;
        }
        return (result, PostRunAction(data));
    }

    private bool PostRunAction(SplicingTableData data)
    {
        Selection = data.Selection;
        ViewStartIndex = data.ViewStartIndex;
        ClampView(data.List, data);
        return data.ShouldConsumeMedia;
    }

    // ==================== 点格子 / 点缝选择 ====================

    /// <summary>上游 selectIndex：点格子（isIota）或点缝；按住潜行是从现有选区拉到这里；再点一次已单独选中的就取消。</summary>
    public void SelectIndex(IReadOnlyList<Iota>? list, int index, bool shift, bool isIota)
    {
        if (list is null) return;
        if (isIota) SelectIota(list, index, shift);
        else SelectEdge(list, index, shift);
    }

    private void SelectIota(IReadOnlyList<Iota> list, int index, bool shift)
    {
        if (!IsInRange(list, index)) return;
        var sel = Selection;
        if (sel is not null && sel.Size == 1 && sel.From == index)
        {
            Selection = null;
        }
        else if (shift && sel is not null)
        {
            Selection = sel is Selection.EdgeSel && index < sel.From ? Selection.Of(sel.From - 1, index) : Selection.Of(sel.From, index);
        }
        else
        {
            Selection = Selection.WithSize(index, 1);
        }
    }

    private void SelectEdge(IReadOnlyList<Iota> list, int index, bool shift)
    {
        if (!IsEdgeInRange(list, index)) return;
        var sel = Selection;
        if (sel is not null && sel.Start == index && sel.End is null)
        {
            Selection = null;
        }
        else if (shift && sel is not null)
        {
            if (sel is Selection.EdgeSel && index < sel.From) Selection = Selection.Of(sel.From - 1, index);
            else if (index > sel.From) Selection = Selection.Of(sel.From, index - 1);
            else Selection = Selection.Of(sel.From, index);
        }
        else
        {
            Selection = Selection.Edge(index);
        }
    }

    private static bool IsInRange(IReadOnlyList<Iota>? list, int index) => list is not null && index >= 0 && index < list.Count;

    /// <summary>缝右边或左边那格在范围里；空列表允许选最左边那条缝。</summary>
    private static bool IsEdgeInRange(IReadOnlyList<Iota> list, int index)
        => IsInRange(list, index) || IsInRange(list, index - 1) || (index == 0 && list.Count == 0);

    // ==================== 视野 ====================

    /// <summary>上游 clampView：选区超出列表就清掉；视野起点夹在 0 到「末尾 - 8」之间。没有列表时全部归零。</summary>
    public void ClampView(IReadOnlyList<Iota>? list, SplicingTableData? data = null)
    {
        if (list is not null)
        {
            int maxStart = Math.Max(0, list.Count - 1 - SplicingTableData.ViewEndIndexOffset);
            switch (Selection)
            {
                case Selection.RangeSel r when !IsInRange(list, r.EndValue):
                    Selection = null;
                    break;
                case Selection.EdgeSel e when !IsEdgeInRange(list, e.Index):
                    Selection = null;
                    break;
            }
            ViewStartIndex = Math.Clamp(ViewStartIndex, 0, maxStart);
        }
        else
        {
            Selection = null;
            ViewStartIndex = 0;
        }
        if (data is not null)
        {
            data.Selection = Selection;
            data.ViewStartIndex = ViewStartIndex;
        }
    }

    /// <summary>上游 listStackChanged：列表物品拿走了就清撤销栈、选区、视野；换了别的就重新夹一下。</summary>
    public void ListStackChanged(bool empty, IReadOnlyList<Iota>? list)
    {
        if (empty)
        {
            UndoStack.Clear();
            Selection = null;
            ViewStartIndex = 0;
        }
        else
        {
            ClampView(list);
        }
    }

    /// <summary>上游 clipboardStackChanged：剪贴板物品拿走了，撤销栈里所有剪贴板的改动都去掉（空了的项整项删）。</summary>
    public void ClipboardStackChanged(bool empty)
    {
        if (!empty) return;
        var kept = UndoStack.Stack
            .Select(e => e with { HasClipboard = false, Clipboard = null })
            .Where(e => e.IsNotEmpty)
            .ToList();
        UndoStack.Stack.Clear();
        UndoStack.Stack.AddRange(kept);
        UndoStack.Index = Math.Min(UndoStack.Index, UndoStack.Stack.Count - 1);
    }

    /// <summary>上游 getClientView：每个 iota 带上括号深度（反思先减、内省后加）。</summary>
    public SplicingClientView ClientView(SplicingTableData data, bool enlightened, bool hasHex)
    {
        List<SplicingIotaView>? views = null;
        if (data.List is { } list)
        {
            views = new List<SplicingIotaView>(list.Count);
            int depth = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var sig = (list[i] as PatternIota)?.Pattern.AnglesSignature();
                if (sig == "eee") depth--;
                views.Add(new SplicingIotaView(list[i], i, depth));
                if (sig == "qqq") depth++;
            }
        }
        return new SplicingClientView
        {
            List = views,
            Clipboard = data.Clipboard,
            IsListWritable = data.ListWriter is not null,
            IsClipboardWritable = data.ClipboardWriter is not null,
            IsEnlightened = enlightened,
            HasHex = hasHex,
            UndoSize = UndoStack.Size,
            UndoIndex = UndoStack.Index,
        };
    }
}
