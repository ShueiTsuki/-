using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.Hexcessible.Core;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;

namespace Addons;

/// <summary>
/// Hexcessible 附属的离线用例（键盘绘制的纯逻辑）。期望值按上游源码（addons_src/hexcessible 的 KeyboardDrawing / CastRef / Utils）推出来。
/// </summary>
static class HexcessibleTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("Hexcessible：" + name, ok, detail);

    static readonly Func<HexCoord, bool> Free = _ => false;

    static bool Eq(HexCoord? a, HexCoord? b) => Nullable.Equals(a, b);

    static List<HexAngle> Sig(string s) => s.Select(c => KeyboardPlacement.AngleOf(c)!.Value).ToList();

    static PatternDef Def(string id, string angles) => new()
    {
        Id = id,
        Angles = angles,
        StartDir = HexDir.East,
        Op = id,
        Prototype = KeyboardPlacement.Pattern(HexDir.East, Sig(angles)),
    };

    public static void Run()
    {
        Console.WriteLine("=== 附属 Hexcessible：键盘绘制 ===");

        // Utils.angle：q 左 w 直 e 右 a 左后 s 回头 d 右后，大小写都认；能画的只有 q w e a d
        Check("字母与角度互转", "qweasd".All(c => KeyboardPlacement.LetterOf(KeyboardPlacement.AngleOf(c)!.Value) == c)
            && KeyboardPlacement.AngleOf('Q') == HexAngle.Left && KeyboardPlacement.AngleOf('x') is null);
        Check("s 不是画笔字母（它是撤销）", !KeyboardPlacement.IsDrawLetter('s') && KeyboardPlacement.IsDrawLetter('D'));

        // hexDirs(start)：从起笔方向起顺时针
        Check("方向轮换从起笔方向开始", KeyboardPlacement.DirsFrom(HexDir.West).SequenceEqual(
            new[] { HexDir.West, HexDir.NorthWest, HexDir.NorthEast, HexDir.East, HexDir.SouthEast, HexDir.SouthWest }));

        // findClosestAvailable：空画布就放在原处、用原来的起笔方向
        var qaq = KeyboardPlacement.Pattern(HexDir.East, Sig("qaq"));
        var here = KeyboardPlacement.FindClosestAvailable(new HexCoord(3, -2), qaq, Free);
        Check("空画布放在原处", here is { } h && Eq(h.Coord, new HexCoord(3, -2)) && h.StartDir == HexDir.East);

        // 原处被占：原点本身被占就换一个格点（BFS 第一圈里的第一个是 原点 + 起笔方向）
        var origin = new HexCoord(0, 0);
        var moved = KeyboardPlacement.FindClosestAvailable(origin, qaq, c => Eq(c, origin));
        Check("原点被占就挪到相邻格点", moved is { } m && Eq(m.Coord, origin + HexDir.East) && m.StartDir == HexDir.East,
            moved?.ToString());

        // 只有起笔方向那条路被占：同一格点换个起笔方向
        var blocked = origin + HexDir.East;
        var turned = KeyboardPlacement.FindClosestAvailable(origin, KeyboardPlacement.Pattern(HexDir.East, Sig("w")), c => Eq(c, blocked));
        Check("起笔方向被挡就在原处换方向", turned is { } t && Eq(t.Coord, origin) && t.StartDir == HexDir.SouthEast, turned?.ToString());

        Check("处处被占就放不下", KeyboardPlacement.FindClosestAvailable(origin, qaq, _ => true) is null);

        // Utils.finalPos：起点 + 起笔方向 + 每个转角后的方向
        Check("终点 = 起点沿各笔走完", Eq(KeyboardPlacement.FinalPos(origin, KeyboardPlacement.Pattern(HexDir.East, Sig("w"))),
            origin + HexDir.East + HexDir.East));

        // KeyboardDrawing：第一笔从 (0,0) 朝东
        var k = new KeyboardDrawingState(Sig("w"), Free);
        Check("键盘绘制从 (0,0) 朝东开始", Eq(k.Start, origin) && k.StartDir == HexDir.East && Eq(k.End, origin + HexDir.East + HexDir.East)
            && k.EndDir == HexDir.East);

        // 下一笔能去的格点：回头（s）永远不行，其余五个都行
        var letters = k.NextPoints(Free).Select(p => p.Letter).OrderBy(c => c).ToArray();
        Check("下一笔提示是 a d e q w", new string(letters) == "adeqw", new string(letters));
        var ahead = k.End!.Value + HexDir.East;
        Check("被占的格点不提示", !k.NextPoints(c => Eq(c, ahead)).Any(p => p.Letter == 'w'));

        Check("s 撤销一笔", k.Type('s', Free) && k.Sig.Count == 0);
        Check("撤销到空再撤销没变化", !k.Undo(Free));

        // 五个 q 绕成六边形回到起点；第六个 q 会走回第一笔，上游 isValidPatternAddition 不许
        var hex = new KeyboardDrawingState(Sig("q"), Free);
        foreach (var c in "qqqq") hex.Type(c, Free);
        Check("五个 q 画成六边形", hex.Sig.Count == 5 && Eq(hex.End, hex.Start));
        Check("六边形再 q 会重叠，不收", !hex.CanGo(HexAngle.Left) && !hex.Type('q', Free) && hex.Sig.Count == 5);
        Check("大写字母也能画", hex.Type('W', Free) && hex.Sig.Count == 6);

        // rotate：r 顺时针一格，Shift+r 逆时针，绕回来
        var r = new KeyboardDrawingState(Sig("w"), Free);
        r.Rotate(1, Free);
        Check("r 顺时针转一格", r.OriginDir == HexDir.SouthEast && r.StartDir == HexDir.SouthEast);
        r.Rotate(-3, Free);
        Check("逆时针转过 0 绕回去", r.OriginDir == HexDir.NorthWest);

        // moveOrigin：出屏就不挪
        var mv = new KeyboardDrawingState(Sig("w"), Free);
        mv.MoveOrigin(1, 0, _ => true, Free);
        Check("l 往右挪一格", Eq(mv.Origin, new HexCoord(1, 0)) && Eq(mv.Start, new HexCoord(1, 0)));
        mv.MoveOrigin(0, 1, _ => false, Free);
        Check("挪出屏幕就不挪", Eq(mv.Origin, new HexCoord(1, 0)));

        // submit：放不下就不放
        var full = new KeyboardDrawingState(Sig("w"), Free);
        Check("放不下时 submit 什么也不放", full.Submit(_ => true) is null && full.Start is null);
        var ok = new KeyboardDrawingState(Sig("qaq"), Free).Submit(Free);
        Check("submit 给出图案与起点", ok is { } o && Eq(o.Start, origin) && o.Pattern.AnglesSignature() == "qaq");

        // 一次排好几条：queuedCount
        var chain = new KeyboardDrawingState(origin, new[] { Sig("w"), Sig("qaq"), Sig("e") }, HexDir.West, Free);
        Check("排队数 = 后面的条数", chain.QueuedCount == 2 && chain.Next!.QueuedCount == 1 && chain.OriginDir == HexDir.West);

        // PatternEntries：签名 → 名字 / 参数行（真实图案表 + 假书）
        var book = new BookDocument();
        var entry = new BookEntry { Id = "test:entry", Advancement = "test:adv" };
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "entity | null", Text = "$(l:a#b)$(action)甲/$乙 x _y" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Text, PatternId = "" });
        book.EntryById[entry.Id] = entry;
        var real = new PatternEntries(PatternRegistry.All, book, PatternRegistry.IsPerWorld);
        var caster = PatternRegistry.FindById("hexcasting:get_caster")!;
        var e = real.FromSig(Sig(caster.Angles));
        Check("按签名认出图案", e is { } en && en.Id == caster.Id && en.Impls.Count == 2);
        Check("参数行 = (in + \" -> \" + out).strip()，每页一行", e is { } a1 && a1.Impls.Select(i => i.Args).SequenceEqual(new[] { "-> entity | null", "->" }),
            e is null ? null : string.Join(" | ", e.Impls.Select(i => i.Args)));
        Check("说明去掉 $(...) 与 /$，空白后的 _ 变空格", e is { } a2 && a2.Impls[0].CleanDesc == "甲乙 x y", e?.Impls[0].CleanDesc);
        Check("签名写法 <EAST,qaq>", e is { } en2 && en2.Signature == "<" + PatternEntries.JavaDirName(caster.StartDir) + "," + caster.Angles + ">"
            && en2.ToString() == en2.Signature + " " + en2.Name);
        Check("方向名是上游 Java 枚举名", PatternEntries.JavaDirName(HexDir.NorthEast) == "NORTH_EAST" && PatternEntries.JavaDirName(HexDir.SouthWest) == "SOUTH_WEST");
        Check("书条目锁住 = 图案锁住；书里没有的不锁", real.IsLocked(caster.Id, _ => false) && !real.IsLocked(caster.Id, _ => true)
            && !real.IsLocked("hexcasting:add", _ => false));

        // 大法术：没学会就没有签名，按本世界画法也认不出来；列表里照样显示注册时的标准画法（上游 toSignature 用原始字段）
        var great = PatternRegistry.All.FirstOrDefault(PatternRegistry.IsPerWorld);
        if (great is not null)
        {
            var sigNow = PatternRegistry.PatternInThisWorld(great).AnglesSignature();
            Check("大法术的画法不会在提示里露出名字", real.FromSig(Sig(sigNow)) is null);
            var ge = real.All.First(x => x.Id == great.Id);
            Check("没学会的大法术没有签名、但列表显示标准画法", ge.Sigs is null && ge.Signature.Contains(great.Angles));
            var learned = new PatternEntries(PatternRegistry.All, book, PatternRegistry.IsPerWorld, d => PatternRegistry.PatternInThisWorld(d));
            Check("学会了就按本世界画法认", learned.FromSig(Sig(sigNow))?.Id == great.Id);
        }
        else
        {
            Check("找得到大法术来测", false);
        }

        // Utils.fluffySearch
        Check("模糊搜索：逐字命中、连续加分、开头加分", FluffySearch.Score("add", "add") == 60 && FluffySearch.Score("ad", "a_d") == 20
            && FluffySearch.Score("AB", "abc") == 40 && FluffySearch.Score("x", "abc") == 0 && FluffySearch.Score("", "abc") == 0,
            $"{FluffySearch.Score("add", "add")} {FluffySearch.Score("ad", "a_d")} {FluffySearch.Score("AB", "abc")}");

        // 假图案表：搜索排序、锁定、取窗、删词
        var defs = new[] { "test:alpha_beta", "test:beta", "test:gamma" }.Select((id, n) => Def(id, new[] { "qaq", "ede", "wwq" }[n])).ToList();
        var idx = new PatternEntries(defs, null, d => d.Id == "test:gamma");
        Check("搜索：名字 × 3 + id（: _ / 当空格），0 分去掉", idx.Search("ab").Select(x => x.Id).SequenceEqual(new[] { "test:alpha_beta" }));
        Check("搜索：同分保持原顺序", idx.Search("a").Select(x => x.Id).SequenceEqual(new[] { "test:alpha_beta", "test:beta", "test:gamma" }));
        var smart = new PatternEntries.Entry("smart:beta", "beta", HexDir.East, new[] { (IReadOnlyList<HexAngle>)Sig("w") }, new[] { (IReadOnlyList<HexAngle>)Sig("w") },
            Array.Empty<PatternEntries.Impl>(), 1);
        Check("搜索：z 高的（智能签名）排最前", idx.Search("beta", new[] { smart })[0].Id == "smart:beta");
        Check("空查询 = 全部", idx.Search("").Count == 3);

        var ac = new AutoCompleteState(new HexCoord(2, 1), idx, x => x.Id == "test:beta");
        Check("自动补全：刚开始不打扰（只一行淡字）", ac.NoDistract && ac.Query == "");
        ac.SetQuery("a");
        Check("自动补全：打字后弹候选，锁住的不算", !ac.NoDistract && ac.Unlocked().Count == 2 && ac.LockedCount == 1);
        ac.OffsetChosen(-1);
        Check("自动补全：上移从第一项绕到最后一项", ac.Chosen == 1 && ac.ChosenEntry?.Id == "test:gamma");
        Check("自动补全：大法术没学会时选中项没有签名", ac.ChosenEntry?.Sigs is null);
        ac.OffsetChosen(1);
        Check("自动补全：下移绕回第一项", ac.Chosen == 0);
        ac.SetQuery("zzz", allow: false);
        Check("自动补全：关了就不改查询", ac.Query == "a");

        ac.SetQuery("foo bar ");
        ac.DeleteWord();
        Check("Ctrl+退格删一个词（Java split 丢末尾空串）", ac.Query == "foo", ac.Query);
        ac.DeleteWord();
        ac.DeleteWord();
        Check("删到空为止", ac.Query == "");
        ac.SetQuery("   ");
        ac.DeleteWord();
        Check("全是空格时删成空（上游这里会抛异常）", ac.Query == "");

        var many = new PatternEntries(Enumerable.Range(0, 10).Select(n => Def("test:e" + n, new string('w', n + 1))), null, _ => false);
        var win = new AutoCompleteState(origin, many, _ => false);
        win.SetQuery("e");
        for (int n = 0; n < 5; n++) win.OffsetChosen(1);
        Check("取窗：选中项上面留 2 项，不越过末尾", win.Window(7) == (3, 10) && win.Window(3) == (3, 6) && win.Window(2) == (5, 7),
            $"{win.Window(7)} {win.Window(3)} {win.Window(2)}");
        win.OffsetChosen(-5);
        Check("取窗：在开头时从 0 开始", win.Window(7) == (0, 7));

        var docs = new AutoCompleteState(origin, real, _ => false);
        docs.SetQuery("get caster");   // id 里的 _ 在匹配前换成了空格，所以要用空格搜
        for (int n = 0; n < docs.Unlocked().Count && docs.ChosenEntry?.Id != "hexcasting:get_caster"; n++) docs.OffsetChosen(1);
        Check("按 id 的词搜得到", docs.ChosenEntry?.Id == "hexcasting:get_caster");
        docs.OffsetChosenDoc(1);
        Check("左右翻书页：两页之间来回", docs.ChosenDoc == 1);
        docs.OffsetChosenDoc(1);
        Check("翻过最后一页回到第一页", docs.ChosenDoc == 0);
    }
}
