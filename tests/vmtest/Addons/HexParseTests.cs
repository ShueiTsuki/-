using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

namespace Addons;

/// <summary>
/// HexParse 附属的离线用例。期望值按上游源码（addons_src/hexparse）逐条推出来，不是照着移植版的输出抄的。
/// </summary>
static class HexParseTests
{
    sealed class FakeHost : IHexParseHost
    {
        public readonly List<(string Text, HexParseMessageKind Kind)> Messages = new();
        public readonly Dictionary<string, string> Macros = new();
        public readonly HashSet<string> Unlocked = new();
        public EntityIota? Self { get; set; } = new(EntityIota.EntityKind.Player, 0);
        public string AuthorName => "Tester";
        public bool IsGreatUnlocked(string longId) => Unlocked.Contains(longId);
        public string? ManualShortName(string shortName) => null;
        public string? GetMacro(string key) => Macros.TryGetValue(key, out var v) ? v : null;
        public Iota? ResolveEntity(string node) => node == "entity_npc_3" ? new EntityIota(EntityIota.EntityKind.Npc, 3) : null;
        public string EntityToCode(EntityIota entity) => $"entity_{entity.Target.ToString().ToLowerInvariant()}_{entity.Index}";
        public void Message(string text, HexParseMessageKind kind) => Messages.Add((text, kind));
    }

    static void Check(string name, bool ok, string? detail = null) => Program.Check("HexParse：" + name, ok, detail);

    static (CodeParser Parser, FakeHost Host, HexParseSettings Settings) Make(Action<HexParseSettings>? tweak = null, Action<FakeHost>? host = null)
    {
        var s = new HexParseSettings();
        tweak?.Invoke(s);
        var h = new FakeHost();
        host?.Invoke(h);
        var names = PatternNames.Build(PatternRegistry.PatternInThisWorld, h.ManualShortName);
        return (new CodeParser(h, s, names), h, s);
    }

    static string Id(Iota i) => i is PatternIota p ? PatternRegistry.Match(p.Pattern)?.Id ?? "_" + p.Pattern.AnglesSignature() : i.ToString();
    static string Ids(ListIota l) => string.Join(" ", l.Items.Select(i => i is ListIota sub ? "[" + Ids(sub) + "]" : Id(i)));

    public static void Run()
    {
        Console.WriteLine("=== 附属 HexParse：分词 / 解析 / 反向输出 ===");
        IotaSerializer.RegisterKind(CommentIota.KindTag, CommentIota.Read);

        Tokenizer();
        Parsing();
        Macros();
        Writer();

        IotaSerializer.UnregisterKind(CommentIota.KindTag);
    }

    static List<string> Cut(string code, Action<HexParseSettings>? tweak = null)
    {
        var s = new HexParseSettings();
        tweak?.Invoke(s);
        return CodeCutter.Split(code, s, out _);
    }

    static void Tokenizer()
    {
        Check("分词：逗号 / 分号 / 空格都是分隔符", string.Join("|", Cut("add, sub;  mul")) == "add|sub|mul");
        Check("分词：默认（注释 MANUAL）丢掉 // 与 /* */，换行不出缩进",
            string.Join("|", Cut("add // 注释\nsub /* 块 */ mul")) == "add|sub|mul", string.Join("|", Cut("add // 注释\nsub /* 块 */ mul")));
        var all = Cut("add // hi", s => s.CommentParsing = HexParseSettings.CommentMode.All);
        Check("分词：注释 = ALL 时 // hi 变成 c\" hi\"（带前导空格）", all.Count == 2 && all[1] == "c\" hi\"", string.Join("|", all));
        var ind = Cut("a\n  b", s => s.IndentParsing = HexParseSettings.CommentMode.All);
        Check("分词：缩进 = ALL 时换行 + 两个空格 = tab_2", string.Join("|", ind) == "a|tab_2|b", string.Join("|", ind));
        var blanks = Cut("a\n\n\nb", s => s.IndentParsing = HexParseSettings.CommentMode.All);
        Check("分词：连续空行超过上限（0）只留一个缩进", string.Join("|", blanks) == "a|tab_0|b", string.Join("|", blanks));
        Check("分词：字符串整体一个符号", Cut("\"a b\"").SequenceEqual(new[] { "\"a b\"" }));
        Check("分词：方括号单独成符号", string.Join("|", Cut("[add]")) == "[|add|]");
        Check("分词：开头认不出的字符直接跳过（上游正则不锚定）", string.Join("|", Cut("@add")) == "add");
        bool threw = false;
        var rec = new List<string>();
        try { CodeCutter.Split("add \"没闭合", new HexParseSettings(), out rec); } catch (ArgumentException) { threw = true; }
        Check("分词：字符串没闭合抛错，已切好的部分保住", threw && rec.SequenceEqual(new[] { "add" }), string.Join("|", rec));
    }

    static void Parsing()
    {
        var (p, h, _) = Make();
        Check("解析：短名、长名、元符号括号", Ids(p.ParseCode("add hexcasting:sub ( mul )")) == "hexcasting:add hexcasting:sub hexcasting:open_paren hexcasting:mul hexcasting:close_paren",
            Ids(p.ParseCode("add hexcasting:sub ( mul )")));

        var consts = p.ParseCode("1.5 -2 1e3 vec_1_2 true FALSE null garbage self");
        Check("解析：数字 / 向量 / 布尔（不分大小写）/ null / garbage / self",
            consts.Count == 9 && consts.Items[0] is DoubleIota { Value: 1.5 } && consts.Items[1] is DoubleIota { Value: -2 }
            && consts.Items[2] is DoubleIota { Value: 1000 } && consts.Items[3] is VectorIota { X: 1, Y: 2, Z: 0 }
            && consts.Items[4] is BooleanIota { Value: true } && consts.Items[5] is BooleanIota { Value: false }
            && consts.Items[6] is NullIota && consts.Items[7] is GarbageIota
            && consts.Items[8] is EntityIota { Target: EntityIota.EntityKind.Player, Index: 0 }, consts.ToString());

        // mask_：上游 TO_MASK 生成的笔画，要和本体的簿记员识别对得上。
        // 上游循环从 substring(6) 开始 —— 第一个字符由起笔那一段表示（- = 起笔东方直线，v = 起笔东南 + a），不再追加笔画
        var masks = p.ParseCode("mask_-v mask_v mask_--");
        bool maskOk = masks.Items[0] is PatternIota m1 && m1.Pattern.AnglesSignature() == "ea" && m1.Pattern.StartDir == HexDir.East
            && SpecialPatterns.TryMask(m1.Pattern, out var b1) && b1.SequenceEqual(new[] { true, false })
            && masks.Items[1] is PatternIota m2 && m2.Pattern.AnglesSignature() == "a" && m2.Pattern.StartDir == HexDir.SouthEast
            && SpecialPatterns.TryMask(m2.Pattern, out var b2) && b2.SequenceEqual(new[] { false })
            && masks.Items[2] is PatternIota m3 && m3.Pattern.AnglesSignature() == "w" && SpecialPatterns.TryMask(m3.Pattern, out var b3) && b3.SequenceEqual(new[] { true, true });
        Check("解析：mask_-v / mask_v / mask_-- 的笔画能被簿记员识别成同样的掩码", maskOk, masks.ToString());

        // num_：上游 NumEvaluatorBrute 的笔画，要能被本体的数字之精思读回同一个数
        var bad = new List<string>();
        foreach (double x in new[] { 0, 1, 5, 7, 10, 42, 1000, -3, 2.5, 0.25, -0.75, 123456 })
        {
            string angles = NumEvaluator.AnglesFromNum(x);
            if (!SpecialPatterns.TryNumber(angles, out double readBack) || readBack != x) bad.Add($"{x}->{angles}->{readBack}");
        }
        Check("解析：num_ 生成的数字之精思笔画读回来是同一个数（12 个数）", bad.Count == 0, string.Join(", ", bad));
        Check("解析：num_5 = aqaaq，起笔东南", p.ParseCode("num_5").Items[0] is PatternIota { Pattern: { StartDir: HexDir.SouthEast } } n5 && n5.Pattern.AnglesSignature() == "aqaaq");
        Check("解析：num_-3 起笔东北、前缀 dedd", p.ParseCode("num_-3").Items[0] is PatternIota { Pattern: { StartDir: HexDir.NorthEast } } n3 && n3.Pattern.AnglesSignature().StartsWith("dedd"));

        Check("解析：_角度串 = 起笔东方的那条图案", p.ParseCode("_qaq").Items[0] is PatternIota { Pattern: { StartDir: HexDir.East } } q && q.Pattern.AnglesSignature() == "qaq");
        Check("解析：内置别名 hermes / thoth / pop / 1.19 旧名 concat", Ids(p.ParseCode("hermes thoth concat")) == "hexcasting:eval hexcasting:for_each hexcasting:add"
            && p.ParseCode("pop").Items[0] is PatternIota pop && pop.Pattern.AnglesSignature() == "a", Ids(p.ParseCode("hermes thoth concat")));
        Check("解析：嵌套列表", p.ParseCode("[1,[2]]") is { Count: 1 } nest && nest.Items[0] is ListIota { Count: 2 } outer && outer.Items[1] is ListIota { Count: 1 });
        Check("解析：entity_ 交给宿主（泰拉偏差：编号）", p.ParseCode("entity_npc_3 entity_npc_99") is var ents
            && ents.Items[0] is EntityIota { Target: EntityIota.EntityKind.Npc, Index: 3 } && ents.Items[1] is NullIota);

        h.Messages.Clear();
        var unk = p.ParseCode("add 我不认识 sub");
        Check("解析：未知符号发警告并跳过", Ids(unk) == "hexcasting:add hexcasting:sub" && h.Messages.Any(x => x.Kind == HexParseMessageKind.Warning && x.Text.Contains("我不认识")));
        h.Messages.Clear();
        var extra = p.ParseCode("1 ] 2");
        Check("解析：右括号过多：报错并保住前面的", extra.Count == 1 && extra.Items[0] is DoubleIota && h.Messages.Any(x => x.Text.Contains("右括号过多")), extra.ToString());
        h.Messages.Clear();
        var open = p.ParseCode("[1");
        Check("解析：左括号过多：报错并把没闭合的列表补上", open.Count == 1 && open.Items[0] is ListIota { Count: 1 } && h.Messages.Any(x => x.Text.Contains("左括号过多")), open.ToString());

        Check("解析：tab_3 = 换行 + 3 个空格的注释", p.ParseCode("tab_3").Items[0] is CommentIota { Comment: "\n   " });
        Check("解析：comment_你好", p.ParseCode("comment_你好").Items[0] is CommentIota { Comment: "你好" });
        var (pOff, _, _) = Make(s => s.CommentParsing = HexParseSettings.CommentMode.Disabled);
        Check("解析：注释 = DISABLED 时 comment_ 被静默忽略", pOff.ParseCode("add comment_x").Count == 1);

        // 大法术：按古卷解锁
        var (pg, hg, _) = Make();
        var locked = pg.ParseCode("lightning");
        hg.Unlocked.Add("hexcasting:lightning");
        var unlocked = pg.ParseCode("lightning");
        var (pAll, _, _) = Make(s => s.ParseGreatSpells = HexParseSettings.GreatMode.All);
        Check("解析：大法术没解锁 = <lightning?> 占位注释；解锁后 = 本世界的画法；ALL 模式直接给",
            locked.Items[0] is CommentIota { Comment: "<lightning?>" }
            && unlocked.Items[0] is PatternIota u && PatternRegistry.Match(u.Pattern)?.Id == "hexcasting:lightning"
            && pAll.ParseCode("lightning").Items[0] is PatternIota, $"{locked} / {unlocked}");

        // 注释 iota 执行时什么都不做
        var env = new TestEnv();
        var img = new CastingImage(new Iota[] { new DoubleIota(1) });
        var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { new CommentIota("说明") });
        Check("注释 iota：执行时不压栈、结果 Escaped", r.ResolutionType == ResolvedPatternType.Escaped && r.Image.Stack.Count == 1, r.ResolutionType.ToString());
        Check("注释 iota：存档往返（登记了种类）", IotaSerializer.TryDeserialize(new CommentIota("x").Serialize(), out var back) && back is CommentIota { Comment: "x" });
    }

    static void Macros()
    {
        var (p, h, _) = Make(host: x =>
        {
            x.Macros["#m"] = "add sub";
            x.Macros["#self"] = "#self";
            x.Macros["plus"] = "add";
            x.Macros["#tab"] = "tab_2 add";
        });
        Check("宏：#m 展开成 add sub", Ids(p.ParseCode("#m mul")) == "hexcasting:add hexcasting:sub hexcasting:mul", Ids(p.ParseCode("#m mul")));
        Check("别名：plus -> add（一个符号换一个符号）", Ids(p.ParseCode("plus")) == "hexcasting:add");
        h.Messages.Clear();
        p.ParseCode("#self");
        Check("宏：自己引用自己报「宏已使用」", h.Messages.Any(x => x.Text.Contains("宏#self已使用")), string.Join(" / ", h.Messages.Select(x => x.Text)));
        var tabbed = p.ParseCode("tab_4 #tab");
        Check("宏：宏里的 tab_2 叠加外层的 tab_4 = tab_6",
            tabbed.Count == 3 && tabbed.Items[0] is CommentIota { Comment: "\n    " } && tabbed.Items[1] is CommentIota { Comment: "\n      " }, tabbed.ToString());
    }

    static void Writer()
    {
        var (p, h, s) = Make(t => t.AttachCodeMeta = 0);
        var w = new IotaWriter(h, s);
        var code = "add(1,vec_1_2[2]true,null,garbage,comment_x";
        var list = p.ParseCode(code);
        string written = w.Write(list, false, IotaWriter.ReadDefault);
        Check("反向：读出来的代码（括号旁的逗号被去掉）", written == code, written);
        Check("反向：读出来的代码再解析回去完全一样", p.ParseCode(written).ValueEquals(list));

        var specials = p.ParseCode("num_5 mask_-v _qwqwqwqwqa");
        Check("反向：数字 -> num_5、簿记员 -> mask_-v、不认识的图案 -> _角度串",
            w.Write(specials, false, IotaWriter.ReadDefault) == "num_5,mask_-v,_qwqwqwqwqa", w.Write(specials, false, IotaWriter.ReadDefault));
        Check("反向：read_signatures 一律角度串", w.Write(p.ParseCode("add"), true, IotaWriter.ReadDefault) == "_waaw");

        Check("反向：数字去掉多余的 0（上游 %.4f 再裁剪）",
            NumEvaluator.DisplayMinimal(1.0) == "1" && NumEvaluator.DisplayMinimal(0.5) == "0.5" && NumEvaluator.DisplayMinimal(1.23456) == "1.2346"
            && NumEvaluator.DisplayMinimal(100) == "100" && NumEvaluator.DisplayMinimal(-0.0) == "-0",
            $"{NumEvaluator.DisplayMinimal(1.23456)} {NumEvaluator.DisplayMinimal(-0.0)}");

        var unknown = new ListIota(new Iota[] { new UnknownIota("someaddon:thing", new List<object?> { 1.0, "x" }) });
        string enc = w.Write(unknown, false, IotaWriter.ReadDefault);
        Check("反向：没有写法的 iota 写成 nbt_…，再解析回来原样", enc.StartsWith("nbt_") && p.ParseCode(enc).ValueEquals(unknown), enc);

        var (pm, hm, sm) = Make(t => t.AttachCodeMeta = 3);
        string meta = new IotaWriter(hm, sm).Write(pm.ParseCode("add comment_x"), false, IotaWriter.ReadDefault);
        Check("反向：开头附作者与用到的附属", meta == "// Author: Tester\n// Requires: hexparse\nadd,comment_x", meta.Replace("\n", "\\n"));

        Check("lehmer：0 1 2 = 0，2 1 0 = 5", IotaWriter.Lehmer(new[] { 0, 1, 2 }) == 0 && IotaWriter.Lehmer(new[] { 2, 1, 0 }) == 5);
    }
}
