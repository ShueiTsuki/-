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

    static readonly SmartSigText TestText = new("NUM {0}", "MASK {0}", "P:", "drop{0}", "drop1", "keep{0}", "keep1", "+", ".");

    /// <summary>用本体自己的数字之精思 / 算术图案把一串图案算出来（栈机，只认数字与 加 乘 除 幂）。</summary>
    static double? EvalNumberPatterns(IReadOnlyList<IReadOnlyList<HexAngle>> sigs, out string why)
    {
        why = "";
        var stack = new Stack<double>();
        foreach (var sig in sigs)
        {
            var key = new string(sig.Select(KeyboardPlacement.LetterOf).ToArray());
            if (SpecialPatterns.TryNumber(key, out var v)) { stack.Push(v); continue; }
            var id = PatternRegistry.Match(key)?.Id;
            if (stack.Count < 2) { why = "栈不够：" + key; return null; }
            double b = stack.Pop(), a = stack.Pop();
            switch (id)
            {
                case "hexcasting:add": stack.Push(a + b); break;
                case "hexcasting:mul": stack.Push(a * b); break;
                case "hexcasting:div": stack.Push(a / b); break;
                case "hexcasting:pow": stack.Push(Math.Pow(a, b)); break;
                default: why = "不认识的图案：" + key + " -> " + id; return null;
            }
        }
        if (stack.Count != 1) { why = "最后栈里有 " + stack.Count + " 项"; return null; }
        return stack.Pop();
    }

    static void SmartSigTests(PatternEntries real)
    {
        // Java 数字细节
        bool P(string q, float want) => JavaNum.TryParseFloat(q, out var v) && (float.IsNaN(want) ? float.IsNaN(v) : v == want);
        Check("Float.parseFloat：空白、结尾 f、.5、指数、NaN", P("5", 5) && P(" 2.5f ", 2.5f) && P(".5", 0.5f) && P("1e3", 1000) && P("5.", 5)
            && P("NaN", float.NaN) && P("-Infinity", float.NegativeInfinity));
        Check("Float.parseFloat：不是数字的不认", !JavaNum.TryParseFloat("abc", out _) && !JavaNum.TryParseFloat("+-1", out _)
            && !JavaNum.TryParseFloat("-", out _) && !JavaNum.TryParseFloat("1,000", out _) && !JavaNum.TryParseFloat("", out _));
        var fs = new[] { (5f, "5.0"), (0.5f, "0.5"), (1e7f, "1.0E7"), (12345678f, "1.2345678E7"), (0.001f, "0.001"), (0.0001f, "1.0E-4"),
            (-3.25f, "-3.25"), (1234567f, "1234567.0"), (100f, "100.0"), (0f, "0.0"), (0.1f, "0.1"), (-0.25f, "-0.25") };
        var badFs = fs.Where(x => JavaNum.FloatToString(x.Item1) != x.Item2).Select(x => x.Item2 + "=>" + JavaNum.FloatToString(x.Item1)).ToList();
        Check("Float.toString：小数 / 科学计数的分界与写法", badFs.Count == 0, string.Join(" ", badFs));
        Check("Math.round(float)：floor(x + 0.5)", JavaNum.Round(2.5f) == 3 && JavaNum.Round(-2.5f) == -2 && JavaNum.Round(float.NaN) == 0
            && JavaNum.Round(3e9f) == int.MaxValue);

        // 数字：生成的笔顺交给本体去算，结果必须等于要的数
        var targets = new[] { 0f, 1f, 5f, 37f, 2000f, -1f, -7f, -2000f, 2001f, 4096f, 12345f, -12345f, 1000000f, 99999999f, 2147483000f,
            0.5f, 2.25f, -3.5f, 1f / 3f, 0.1f, 123.75f };
        var bad = new List<string>();
        foreach (var t in targets)
        {
            var sigs = SmartSigs.NumberSigs(t);
            if (sigs is null) { bad.Add(t + ": null"); continue; }
            var got = EvalNumberPatterns(sigs, out var why);
            if (got is null) { bad.Add(t + ": " + why); continue; }
            double tol = t == MathF.Round(t) ? 1e-9 * Math.Max(1, Math.Abs(t)) : 1e-3;
            if (Math.Abs(got.Value - t) > tol) bad.Add(t + " 算出 " + got);
        }
        Check("数字：生成的笔顺用本体的数字之精思与算术图案算出来正好是这个数（" + targets.Length + " 个）", bad.Count == 0, string.Join("; ", bad));
        Check("数字：2000 以内一条就够（查表）", SmartSigs.NumberSigs(1999f)!.Count == 1 && SmartSigs.NumberSigs(-37f)!.Count == 1
            && new string(SmartSigs.NumberSigs(-37f)![0].Select(KeyboardPlacement.LetterOf).ToArray()).StartsWith("dedd", StringComparison.Ordinal));

        var smart = new SmartSigs(TestText);
        var n3 = smart.NumberFromSig(Sig("aqaaedwd"));
        Check("按签名认数字：名字用 Float.toString、参数行 -> 3.0、排在前面（z = 1）", n3 is { } e3 && e3.Name == "NUM 3.0" && e3.Id == "hexcessible:number/3"
            && e3.Z == 1 && e3.Impls[0].Args == "-> 3.0" && e3.Dir == HexDir.SouthEast, n3?.Name);
        Check("dedd 开头是负数", smart.NumberFromSig(Sig("ddeddedwd"))?.Name is null && smart.NumberFromSig(Sig("deddedwd"))?.Name == "NUM -3.0");
        Check("按查询给数字；超出 int 范围不给", smart.NumberFromQuery("5")?.Sigs is { Count: 1 } && smart.NumberFromQuery("1e10") is null
            && smart.NumberFromQuery("abc") is null);

        // 簿记员之策略：生成的笔顺交给本体的 TryMask，丢 / 留必须一一对上（本体 true = 留下）
        var badMask = new List<string>();
        foreach (var q in new[] { "v", "-", "v-", "-v", "vv", "--v-vv-", "v-v-v", "vvv---" })
        {
            var entry = smart.BookkeeperFromQuery(q);
            if (entry is null) { badMask.Add(q + ": null"); continue; }
            var pat = KeyboardPlacement.Pattern(HexDir.East, entry.Sigs![0]);
            if (!SpecialPatterns.TryMask(pat, out var mask)) { badMask.Add(q + ": 本体认不出 " + pat.AnglesSignature()); continue; }
            var want = q.Select(c => c == '-').ToArray();
            if (!mask.SequenceEqual(want)) badMask.Add(q + ": 本体读成 " + new string(mask.Select(m => m ? '-' : 'v').ToArray()));
            if (q.Length > 1 || q == "v")
            {
                var back = smart.BookkeeperFromSig(entry.Sigs[0]);
                if (back?.Id != entry.Id) badMask.Add(q + ": 按签名反查得到 " + back?.Id);
            }
        }
        Check("簿记员：生成的笔顺本体读出来丢 / 留一致，按签名能反查回来", badMask.Count == 0, string.Join("; ", badMask));
        var bk = smart.BookkeeperFromQuery("-v-")!;
        Check("簿记员：参数行与名字", bk.Impls[0].Args == "1, _, 3 -> 1, 3" && bk.Name == "MASK -v-", bk.Impls[0].Args);
        Check("簿记员：说明按连续段计数", smart.BookkeeperFromQuery("--v")!.Impls[0].Desc == "P:keep2+drop1." && smart.BookkeeperFromQuery("vvv-")!.Impls[0].Desc == "P:drop3+keep1.",
            smart.BookkeeperFromQuery("--v")!.Impls[0].Desc);
        Check("簿记员：查询里有别的字就不是", smart.BookkeeperFromQuery("v-x") is null && smart.BookkeeperFromQuery("") is null);
        Check("簿记员：画不成簿记员的签名认不出", smart.BookkeeperFromSig(Sig("qaq")) is null);

        // 接进索引：按签名先问智能签名，搜数字时数字排第一
        real.Smart = smart;
        real.InvalidateCaches();
        Check("索引：数字之精思的签名认成数字", real.FromSig(Sig("aqaaq"))?.Name == "NUM 5.0");
        Check("索引：搜 12 第一项是数字", real.Search("12").FirstOrDefault()?.Id == "hexcessible:number/12");
        Check("转义四个图案的名字后面带符号，按符号搜得到", real.All.First(x => x.Id == "hexcasting:open_paren").Name.EndsWith(" ({", StringComparison.Ordinal)
            && real.Search("(").Any(x => x.Id == "hexcasting:open_paren") && real.All.First(x => x.Id == "hexcasting:escape").Name.EndsWith(" " + (char)92, StringComparison.Ordinal));
        real.Smart = null;
        real.InvalidateCaches();
    }

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
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "entity | null", Text = "$(l:a#b)$(action)甲/$乙 x _y", Anchor = "hexcasting:get_caster" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Text, PatternId = "" });
        book.EntryById[entry.Id] = entry;
        var real = new PatternEntries(PatternRegistry.All, book, PatternRegistry.IsPerWorld);
        var caster = PatternRegistry.FindById("hexcasting:get_caster")!;
        var e = real.FromSig(Sig(caster.Angles));
        Check("按签名认出图案", e is { } en && en.Id == caster.Id && en.Impls.Count == 2);
        Check("参数行 = (in + \" -> \" + out).strip()，每页一行", e is { } a1 && a1.Impls.Select(i => i.Args).SequenceEqual(new[] { "-> entity | null", "->" }),
            e is null ? null : string.Join(" | ", e.Impls.Select(i => i.Args)));
        Check("书页记下所在条目与锚点（按 N 翻到那一页用）", e is { } a3 && a3.Impls[0].EntryId == "test:entry" && a3.Impls[0].Anchor == "hexcasting:get_caster");
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

        // 别名（上游 AliasChanging + Entry.name / isAliased）
        var aliases = new Dictionary<string, string>();
        var ai = new PatternEntries(defs, null, _ => false) { AliasOf = id => aliases.TryGetValue(id, out var v) ? v : null };
        var ae = ai.All[0];
        var edit = new AliasEditState(ae);
        Check("别名：没起过时输入框是空的、名字是原名", edit.IsBlank && ae.Name == "test:alpha_beta" && !ae.IsAliased);
        edit.Alias = "  first second  ";
        Check("别名：存的时候去掉首尾空白", edit.ValueToStore == "first second");
        aliases[ae.Id] = edit.ValueToStore;
        ai.InvalidateCaches();
        Check("别名：起过就用别名显示、搜得到", ae.Name == "first second" && ae.IsAliased && ai.Search("second").Any(x => x.Id == ae.Id)
            && ae.ToString().EndsWith(" first second", StringComparison.Ordinal));
        var again = new AliasEditState(ae);
        Check("别名：再改时输入框里是现在的别名", again.Alias == "first second" && again.Original == "test:alpha_beta");
        again.DeleteWord();
        Check("别名：Ctrl+退格删一个词", again.Alias == "first");
        again.Alias = "   ";
        Check("别名：清空后存回原名（上游写法，等于去掉别名）", again.IsBlank && again.ValueToStore == "test:alpha_beta");

        SmartSigTests(real);

        // 显示选项：签名开关（tooltipRenderSigs）、减少动效（makeZappy 直接给原始折线）
        var casterEntry = real.All.First(x => x.Id == "hexcasting:get_caster");
        HexcessibleSettings.Current = new HexcessibleSettings { TooltipRenderSigs = false };
        Check("关掉渲染图案：名字前不带签名", casterEntry.ToString() == casterEntry.Name);
        HexcessibleSettings.Current = new HexcessibleSettings();
        Check("默认带签名", casterEntry.ToString().StartsWith("<", StringComparison.Ordinal));
        var bare = new List<HexCastingTerraria.Core.Casting.Math.Vec2f> { new(0, 0), new(30, 0), new(60, 10) };
        var zappy = HexCastingTerraria.Core.Canvas.PatternGeometry.MakeZappy(bare, null, 10, 2.5f, 0.1f, 0.2f, 0.2f, 0.8f, 1, 5);
        HexCastingTerraria.Core.Canvas.PatternGeometry.ReducedMotion = () => true;
        var calm = HexCastingTerraria.Core.Canvas.PatternGeometry.MakeZappy(bare, null, 10, 2.5f, 0.1f, 0.2f, 0.2f, 0.8f, 1, 5);
        HexCastingTerraria.Core.Canvas.PatternGeometry.ReducedMotion = null;
        Check("减少动效：makeZappy 直接给原始折线", calm.SequenceEqual(bare) && zappy.Count > bare.Count);

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
