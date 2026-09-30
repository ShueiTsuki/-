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

        // PatternEntries：签名 → 名字 / 参数行
        var book = new BookDocument();
        var entry = new BookEntry { Id = "test:entry" };
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "entity | null" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Pattern, PatternId = "hexcasting:get_caster", Input = "", Output = "" });
        entry.Pages.Add(new BookPage { Kind = BookPageKind.Text, PatternId = "" });
        book.EntryById[entry.Id] = entry;
        var args = PatternEntries.ArgsOf("hexcasting:get_caster", book);
        Check("参数行 = (in + \" -> \" + out).strip()，每页一行", args.SequenceEqual(new[] { "-> entity | null", "->" }), string.Join(" | ", args));

        var caster = PatternRegistry.FindById("hexcasting:get_caster")!;
        var e = PatternEntries.FromSig(Sig(caster.Angles), book);
        Check("按签名认出图案", e is { } en && en.Id == caster.Id && en.Args.Count == 2);
        Check("签名写法 <EAST,qaq>", e is { } en2 && en2.Signature == "<" + PatternEntries.JavaDirName(caster.StartDir) + "," + caster.Angles + ">"
            && en2.ToString() == en2.Signature + " " + en2.Name);
        Check("方向名是上游 Java 枚举名", PatternEntries.JavaDirName(HexDir.NorthEast) == "NORTH_EAST" && PatternEntries.JavaDirName(HexDir.SouthWest) == "SOUTH_WEST");

        // 大法术不认（上游只认手持远古卷轴学过的，那是单独一项功能）
        var great = PatternRegistry.All.FirstOrDefault(PatternRegistry.IsPerWorld);
        if (great is not null)
        {
            var sigNow = PatternRegistry.PatternInThisWorld(great).AnglesSignature();
            Check("大法术的画法不会在提示里露出名字", PatternEntries.FromSig(Sig(sigNow), book) is null);
        }
        else
        {
            Check("找得到大法术来测", false);
        }
        PatternEntries.Invalidate();
    }
}
