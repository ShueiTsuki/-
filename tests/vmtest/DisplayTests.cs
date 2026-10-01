using System.Linq;
using System.Text.RegularExpressions;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;

/// <summary>
/// iota 的显示（上游 IotaType.display）：文字、颜色、列表逗号，以及写成聊天标记再读回来。
/// 期望值按上游源码推：数字 %.2f 绿、向量 (%.2f, %.2f, %.2f) 红、True 深绿 / False 深红、Null 灰、
/// 列表「[%s]」深紫且只有相邻两个里至少一个要逗号时才加「, 」（图案不要）、续体「[Jump]」红。
/// </summary>
static class DisplayTests
{
    static void Check(string name, bool ok, string? detail = null) => Program.Check("显示：" + name, ok, detail);

    static HexPattern P(string sig, HexDir dir = HexDir.East)
    {
        HexPattern.TryFromAnglesUnchecked(sig, dir, out var p, out _);
        return p!;
    }

    // 泰拉 ChatManager.Regexes.Format 原样照抄：标记必须能被它一个不漏地切出来
    static readonly Regex TerrariaTag = new(@"(?<!\\)\[(?<tag>[a-zA-Z]{1,10})(\/(?<options>[^:]+))?:(?<text>.+?)(?<!\\)\]");

    public static void Run()
    {
        System.Console.WriteLine("=== iota 显示（上游 display）===");

        Check("数字两位小数", new DoubleIota(1).Display() == "1.00" && new DoubleIota(1.0 / 3).Display() == "0.33"
            && new DoubleIota(-2.5).Display() == "-2.50", new DoubleIota(1).Display());
        Check("数字是绿色", new DoubleIota(1).DisplayRich().Spans(McColors.White).Single().Color == McColors.Green);
        Check("向量三个分量两位小数、红色", new VectorIota(1.5, -2).Display() == "(1.50, -2.00, 0.00)"
            && new VectorIota(1, 2).DisplayRich().Spans(McColors.White).Single().Color == McColors.Red, new VectorIota(1.5, -2).Display());
        Check("布尔 True 深绿 / False 深红", BooleanIota.True.Display() == "True" && BooleanIota.False.Display() == "False"
            && BooleanIota.True.DisplayRich().Spans(0).Single().Color == McColors.DarkGreen
            && BooleanIota.False.DisplayRich().Spans(0).Single().Color == McColors.DarkRed);
        Check("Null 灰色", NullIota.Instance.Display() == "Null" && NullIota.Instance.DisplayRich().Spans(0).Single().Color == McColors.Gray);
        var garbage = GarbageIota.Instance.DisplayRich().Spans(0).Single();
        Check("垃圾：16 个随机字母、深灰", garbage.Text!.Length == 16 && garbage.Text.All(c => c is >= 'a' and <= 'z') && garbage.Color == McColors.DarkGray);
        Check("实体找不到时「未知实体」", new EntityIota(EntityIota.EntityKind.Npc, 3).Display() == "未知实体");

        var nums = new ListIota(new DoubleIota(1), new DoubleIota(2));
        Check("列表显示内容，数字之间有逗号", nums.Display() == "[1.00, 2.00]", nums.Display());
        var a = P("qaq");
        var b = P("aa");
        var pats = new ListIota(new PatternIota(a), new PatternIota(b));
        Check("两个图案之间不加逗号", pats.Display() == "[" + a + b + "]", pats.Display());
        var mixed = new ListIota(new PatternIota(a), new DoubleIota(1));
        Check("图案和数字之间加逗号", mixed.Display() == "[" + a + ", 1.00]", mixed.Display());
        IotaDisplay.AlwaysShowListCommas = true;
        Check("「列表总显示逗号」打开时图案之间也加", pats.Display() == "[" + a + ", " + b + "]", pats.Display());
        IotaDisplay.AlwaysShowListCommas = false;
        Check("空列表", new ListIota().Display() == "[]");

        var nested = new ListIota(new ListIota(new DoubleIota(1)), new PatternIota(a));
        var spans = nested.DisplayRich().Spans(McColors.White);
        Check("嵌套列表：方括号和逗号用列表的深紫，里面的数字保持绿色、图案白色",
            spans[0].Text == "[" && spans[0].Color == McColors.DarkPurple && spans[1].Text == "[" && spans[1].Color == McColors.DarkPurple
            && spans[2].Text == "1.00" && spans[2].Color == McColors.Green
            && spans.Any(s => s.Pattern is not null && s.Color == McColors.White),
            string.Join(" | ", spans.Select(s => (s.Text ?? "<p>") + ":" + s.Color.ToString("X6"))));

        // 聊天标记
        string tagged = DisplayTags.Of(nested);
        Check("写成标记再去掉，等于纯文字", DisplayTags.Strip(tagged) == nested.Display(), DisplayTags.Strip(tagged));
        var matches = TerrariaTag.Matches(tagged);
        Check("泰拉的标记解析能把每一段完整切出来（没有被方括号截断）",
            string.Concat(matches.Select(m => m.Value)) == tagged && matches.All(m => m.Groups["tag"].Value is DisplayTags.TextTag or DisplayTags.GlyphTag),
            tagged);
        string tricky = "a]b[c%d" + (char)92 + "e\nf";
        var lit = DisplayText.Literal(tricky, McColors.Gold);
        var one = TerrariaTag.Match(DisplayTags.Of(lit));
        Check("特殊字符（] [ % 反斜杠 换行）转义后能原样读回",
            one.Success && one.Value == DisplayTags.Of(lit) && DisplayTags.Unescape(one.Groups["text"].Value) == tricky
            && one.Groups["options"].Value == "FFAA00", DisplayTags.Of(lit));
        var glyph = TerrariaTag.Match(DisplayTags.Of(new PatternIota(P("wqaed", HexDir.SouthWest))));
        Check("图案标记读回同一个图案", glyph.Success && glyph.Groups["tag"].Value == DisplayTags.GlyphTag
            && DisplayTags.ParseGlyph(glyph.Groups["text"].Value) is { } back && back.AnglesSignature() == "wqaed" && back.StartDir == HexDir.SouthWest);
        Check("不认识的方括号原样保留", DisplayTags.Strip("[1, 2] [hext/FFFFFF:x]") == "[1, 2] x");
    }
}
