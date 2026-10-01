using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core.Splicing;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

namespace Addons;

/// <summary>
/// HexDebug 剪接台的离线用例（上游 splicing/ 的选区、撤销、各个按钮操作，blocks/splicing 的选择与视野）。
/// 期望值按上游源码逐条推出来。
/// </summary>
static class SplicingTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("剪接台：" + name, ok, detail);

    sealed class Holder : ISplicingHolder
    {
        public Iota? Value;
        public bool CanWrite = true;

        public Iota? Read() => Value;

        public bool Writable => CanWrite;

        public bool Write(Iota? value)
        {
            Value = value;
            return true;
        }
    }

    static Holder Nums(params int[] xs) => new() { Value = new ListIota(xs.Select(x => (Iota)new DoubleIota(x)).ToList()) };

    static string Show(Iota? i) => i switch
    {
        null => "null",
        ListIota l => "[" + string.Join(",", l.Items.Select(Show)) + "]",
        DoubleIota d => d.Value.ToString(),
        PatternIota p => "p:" + p.Pattern.AnglesSignature(),
        _ => i.ToString(),
    };

    /// <summary>做一次操作（媒质充足、有玩家），返回列表、剪贴板、选区。</summary>
    static (string List, string Clip, string? Sel, bool Consumed) Do(SplicingTableState st, Holder list, Holder clip, SplicingTableAction a)
    {
        var data = st.Data(true, list, clip);
        bool consumed = st.RunAction(a, data, 1000, 1);
        return (Show(list.Value), Show(clip.Value), st.Selection?.ToString(), consumed);
    }

    public static void Run()
    {
        Console.WriteLine("=== 附属 HexDebug：剪接台 ===");

        // ---- 选区 ----
        var r = Selection.Range(3, 1)!;
        Check("区间可以反着拖：start / end / size", r.Start == 1 && r.End == 3 && r.Size == 3 && r.LastIndex == 3);
        var e = Selection.Edge(2)!;
        Check("缝往右扩一格 = 选中右边那格，往左扩 = 左边那格", e.ExpandBy(1) == Selection.Range(2, 2) && e.ExpandBy(-1) == Selection.Range(1, 1)
            && e.LastIndex == 1 && e.Size == 0);
        Check("负的下标没有选区", Selection.Range(-1, 2) is null && Selection.Edge(-1) is null);
        Check("存档写法：-1 是没有，to = -1 是缝", Selection.FromRawIndices(-1, 3) is null && Selection.FromRawIndices(2, -1) == Selection.Edge(2)
            && Selection.FromRawIndices(1, 4) == Selection.Range(1, 4));

        // ---- 撤销栈 ----
        var u = new UndoStack { MaxSize = 3 };
        for (int i = 0; i < 4; i++) u.Push(new UndoStack.Entry(true, new Iota[] { new DoubleIota(i) }, false, null, false, null));
        Check("撤销栈超过上限丢最早的", u.Size == 3 && u.Index == 2 && Show(u.Stack[0].List![0]) == "1");
        u.Undo();
        u.Push(new UndoStack.Entry(true, Array.Empty<Iota>(), false, null, false, null));
        Check("撤销后再改：丢掉后面的", u.Size == 3 && u.Index == 2);
        Check("撤销到底就停", u.Undo() is not null && u.Undo() is not null && u.Undo() is null && u.Index == 0);

        // ---- 点格子 / 点缝 ----
        var st = new SplicingTableState();
        var five = Nums(0, 1, 2, 3, 4).Value is ListIota fl ? fl.Items : throw new Exception();
        st.SelectIndex(five, 2, false, true);
        Check("点格子：单选这一格", st.Selection == Selection.Range(2, 2));
        st.SelectIndex(five, 2, false, true);
        Check("再点一次：取消", st.Selection is null);
        st.SelectIndex(five, 1, false, true);
        st.SelectIndex(five, 4, true, true);
        Check("潜行点：从原来的起点拉到这里", st.Selection == Selection.Range(1, 4));
        st.SelectIndex(five, 3, false, false);
        Check("点缝：光标", st.Selection == Selection.Edge(3));
        st.SelectIndex(five, 5, true, false);
        Check("从缝潜行点右边的缝：选中中间的格子", st.Selection == Selection.Range(3, 4), st.Selection?.ToString());
        st.SelectIndex(five, 3, false, false);
        st.SelectIndex(five, 1, true, false);
        Check("从缝潜行点左边的缝", st.Selection == Selection.Of(2, 1), st.Selection?.ToString());
        st.SelectIndex(five, 9, false, true);
        Check("点到列表外面不变", st.Selection == Selection.Of(2, 1));
        var empty = new List<Iota>();
        st.SelectIndex(empty, 0, false, false);
        Check("空列表可以选最左边的缝", st.Selection == Selection.Edge(0));

        // ---- 操作 ----
        var s = new SplicingTableState();
        var L = Nums(0, 1, 2, 3, 4);
        var C = new Holder();
        var res = Do(s, L, C, SplicingTableAction.SelectAll);
        Check("全选（不耗媒质）", res.Sel == "[0..4]" && !res.Consumed);
        s.Selection = Selection.Range(1, 2);
        res = Do(s, L, C, SplicingTableAction.Delete);
        Check("删除：选区变成删掉处的缝，耗媒质", res.List == "[0,3,4]" && res.Sel == "|1" && res.Consumed, res.ToString());
        res = Do(s, L, C, SplicingTableAction.Undo);
        // 撤销的底是第一次操作（全选）之前记下的：那时还没有选区；点选格子不进撤销栈（上游同样）
        Check("撤销：回到删除前（选区回到撤销底记下的「没有」）", res.List == "[0,1,2,3,4]" && res.Sel is null, res.ToString());
        res = Do(s, L, C, SplicingTableAction.Redo);
        Check("重做", res.List == "[0,3,4]" && res.Sel == "|1", res.ToString());

        L = Nums(0, 1, 2, 3, 4);
        s = new SplicingTableState { Selection = Selection.Range(2, 3) };
        res = Do(s, L, C, SplicingTableAction.NudgeLeft);
        Check("左移：左边那个挪到选区后面", res.List == "[0,2,3,1,4]" && res.Sel == "[1..2]", res.ToString());
        L = Nums(0, 1, 2, 3, 4);
        s = new SplicingTableState { Selection = Selection.Range(1, 2) };
        res = Do(s, L, C, SplicingTableAction.NudgeRight);
        Check("右移：右边那个挪到选区前面", res.List == "[0,3,1,2,4]" && res.Sel == "[2..3]", res.ToString());
        L = Nums(0, 1, 2);
        s = new SplicingTableState { Selection = Selection.Range(1, 1) };
        res = Do(s, L, C, SplicingTableAction.Duplicate);
        Check("重复：复制一份接在后面并选中它", res.List == "[0,1,1,2]" && res.Sel == "[2..2]", res.ToString());
        L = Nums(0, 1, 2, 3);
        s = new SplicingTableState { Selection = Selection.Edge(2) };
        res = Do(s, L, C, SplicingTableAction.Backspace);
        Check("退格：删掉光标左边一个", res.List == "[0,2,3]" && res.Sel == "|1", res.ToString());

        L = Nums(0, 1, 2, 3, 4);
        C = new Holder();
        s = new SplicingTableState { Selection = Selection.Range(1, 2) };
        res = Do(s, L, C, SplicingTableAction.Copy);
        Check("复制几个：剪贴板是列表，原列表不变", res.Clip == "[1,2]" && res.List == "[0,1,2,3,4]");
        s.Selection = Selection.Range(3, 3);
        res = Do(s, L, C, SplicingTableAction.Copy);
        Check("复制一个：剪贴板就是那一个", res.Clip == "3");
        s.Selection = Selection.Range(0, 1);
        res = Do(s, L, C, SplicingTableAction.Cut);
        Check("剪切", res.List == "[2,3,4]" && res.Clip == "[0,1]" && res.Sel == "|0", res.ToString());
        s.Selection = Selection.Edge(1);
        res = Do(s, L, C, SplicingTableAction.PasteVerbatim);
        Check("粘贴（逐项）：剪贴板整个作为一项放进去", res.List == "[2,[0,1],3,4]" && res.Sel == "|2", res.ToString());
        L = Nums(2, 3, 4);
        s = new SplicingTableState { Selection = Selection.Edge(1) };
        res = Do(s, L, C, SplicingTableAction.PasteSplat);
        Check("粘贴（扁平化）：剪贴板列表展开放进去", res.List == "[2,0,1,3,4]" && res.Sel == "|3", res.ToString());

        // 光标与视野
        L = Nums(Enumerable.Range(0, 20).ToArray());
        s = new SplicingTableState();
        res = Do(s, L, C, SplicingTableAction.CursorLeft);
        Check("没有选区时光标出现在视野中间", res.Sel == "|4", res.Sel);
        Do(s, L, C, SplicingTableAction.ViewRight);
        Check("视野右移一格", s.ViewStartIndex == 1);
        Do(s, L, C, SplicingTableAction.ViewRightFull);
        Check("视野移到末尾：最后一格在第 9 个位置", s.ViewStartIndex == 11);
        Do(s, L, C, SplicingTableAction.ViewLeftPage);
        Check("视野往左翻一页（9 格）", s.ViewStartIndex == 2);
        Do(s, L, C, SplicingTableAction.ViewLeftPage);
        Check("翻过头夹回 0", s.ViewStartIndex == 0);
        s.Selection = Selection.Edge(15);
        Do(s, L, C, SplicingTableAction.CursorRight);
        Check("光标走出视野时视野跟过去", s.Selection == Selection.Edge(16) && s.ViewStartIndex == 7, s.ViewStartIndex.ToString());

        // 只读 / 媒质 / 按钮亮不亮
        var ro = Nums(0, 1, 2);
        ro.CanWrite = false;
        s = new SplicingTableState { Selection = Selection.Range(0, 0) };
        res = Do(s, ro, C, SplicingTableAction.Delete);
        Check("列表只读：删除不做", res.List == "[0,1,2]" && !res.Consumed);
        L = Nums(0, 1, 2);
        s = new SplicingTableState { Selection = Selection.Range(0, 0) };
        bool consumed = s.RunAction(SplicingTableAction.Delete, s.Data(true, L, C), media: 0, mediaCost: 1);
        Check("媒质不够：耗媒质的操作不做", !consumed && Show(L.Value) == "[0,1,2]");
        s.RunAction(SplicingTableAction.SelectAll, s.Data(true, L, C), media: 0, mediaCost: 1);
        Check("媒质不够：只挪选区 / 视野的照做", s.Selection == Selection.Range(0, 2));
        Check("没有玩家时什么都不做（上游 player!!）", !new SplicingTableState().RunAction(SplicingTableAction.SelectAll,
            new SplicingTableState().Data(false, L, C), 1000, 1));

        var view = s.ClientView(s.Data(true, L, new Holder()), false, false);
        Check("按钮：没剪贴板不能粘贴、能删除、刚开始不能撤销",
            !SplicingActions.Test(SplicingTableAction.PasteSplat, view, Selection.Edge(0), 0)
            && SplicingActions.Test(SplicingTableAction.Delete, view, Selection.Range(0, 0), 0)
            && !SplicingActions.Test(SplicingTableAction.Undo, view, null, 0)
            && !SplicingActions.Test(SplicingTableAction.Delete, view, Selection.Edge(0), 0));
        var roView = s.ClientView(s.Data(true, ro, null), false, false);
        Check("按钮：列表只读时编辑类都灭、没有剪贴板槽不能复制", !SplicingActions.Test(SplicingTableAction.Backspace, roView, Selection.Edge(1), 0)
            && !SplicingActions.Test(SplicingTableAction.Copy, roView, Selection.Range(0, 1), 0)
            && SplicingActions.Test(SplicingTableAction.SelectAll, roView, null, 0));

        // 画图案进列表
        L = Nums(0, 1, 2);
        s = new SplicingTableState { Selection = Selection.Edge(1) };
        HexPattern.TryFromAnglesUnchecked("qaq", HexDir.East, out var qaq, out _);
        var (type, consume) = s.DrawPattern(qaq!, s.Data(true, L, C), 1000, 1);
        Check("画图案：插在光标处，光标跟到它后面，是转义色", Show(L.Value) == "[0,p:qaq,1,2]" && s.Selection == Selection.Edge(2)
            && type == ResolvedPatternType.Escaped && consume);
        s.Selection = Selection.Range(0, 1);
        s.DrawPattern(qaq!, s.Data(true, L, C), 1000, 1);
        Check("画图案：替换掉选中的", Show(L.Value) == "[p:qaq,1,2]" && s.Selection == Selection.Edge(1));
        s.Selection = null;
        Check("没有选区不能画（出错色）", s.DrawPattern(qaq!, s.Data(true, L, C), 1000, 1).Type == ResolvedPatternType.Errored);

        // 客户端视图：括号深度
        HexPattern.TryFromAnglesUnchecked("qqq", HexDir.West, out var open, out _);
        HexPattern.TryFromAnglesUnchecked("eee", HexDir.East, out var close, out _);
        var bracket = new Holder { Value = new ListIota(new Iota[] { new PatternIota(open!), new DoubleIota(1), new PatternIota(close!), new DoubleIota(2) }) };
        var bv = new SplicingTableState().ClientView(new SplicingTableState().Data(true, bracket, null), true, false);
        Check("括号深度：内省之后加一层，反思本身回到外层", bv.List!.Select(x => x.Depth).SequenceEqual(new[] { 0, 1, 0, 0 }) && bv.IsEnlightened);

        // 拿走物品
        s = new SplicingTableState { Selection = Selection.Range(0, 1), ViewStartIndex = 3 };
        s.UndoStack.Push(new UndoStack.Entry(false, null, true, new DoubleIota(1), false, null));
        s.UndoStack.Push(new UndoStack.Entry(true, Array.Empty<Iota>(), true, new DoubleIota(2), false, null));
        s.ClipboardStackChanged(empty: true);
        Check("拿走剪贴板物品：撤销栈里只改剪贴板的项删掉，其余去掉剪贴板部分", s.UndoStack.Size == 1 && !s.UndoStack.Stack[0].HasClipboard && s.UndoStack.Index == 0);
        s.ListStackChanged(empty: true, null);
        Check("拿走列表物品：撤销栈、选区、视野全清", s.UndoStack.Size == 0 && s.Selection is null && s.ViewStartIndex == 0);
    }
}
