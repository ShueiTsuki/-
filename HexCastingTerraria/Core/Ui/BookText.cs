namespace HexCastingTerraria.Core.Ui;

// ── 文本排版引擎 ──────────────────────────────────────────────────────
//
// 移植自 Patchouli 的 `client/book/text/`（TextLayouter / BookTextParser / SpanState / Word，
// VazkiiMods/Patchouli，CC BY-NC-SA 3.0，见 CREDITS.md）。
//
// **为什么这一支要最先做**：它是这类 UI 里唯一「不看图也能测」的部分。
// 「字被切掉」「最后一页溢出」「某个词把整行撑爆」这些毛病肉眼极难发现，
// 但用一段带标记的文本就能断言出来 —— 给出每页几行、每行多少字，逐条对。
//
// 这里**不碰任何字体或绘制**：宽度由调用方通过 MeasureWidth 回调提供。
// 于是离线测试可以喂一个「一个字符 = 1 单位」的假测量函数，把算法单独钉死；
// 真渲染时再传真正的字体测量。Core/ 也因此仍然零 XNA 依赖。

/// <summary>量一段文本有多宽。真实渲染时传字体测量，离线测试时传假函数。</summary>
public delegate int MeasureWidth(string text);

/// <summary>
/// 一段同样式、同链接的文本。
///
/// 用「段」而不是「字符」是刻意的：排版时按段搬移，避免每个字符都带一份样式，
/// 也让渲染端可以整段一次绘制。
/// </summary>
public sealed class BookTextSegment
{
    public string Text { get; set; } = string.Empty;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    /// <summary>颜色代号（0 = 默认墨色）。对应 Patchouli 的 <c>$(0)</c>…<c>$(9)</c>。</summary>
    public int ColorCode { get; set; }

    /// <summary>链接目标（条目 id 或分类 id）。空 = 不是链接。</summary>
    public string LinkTarget { get; set; } = string.Empty;
}

/// <summary>排好的一行。</summary>
public sealed class BookTextLine
{
    public System.Collections.Generic.List<BookTextSegment> Segments { get; }
        = new System.Collections.Generic.List<BookTextSegment>();

    /// <summary>本行总宽（由 <see cref="MeasureWidth"/> 累加而来）。</summary>
    public int Width { get; set; }

    /// <summary>把本行的纯文本拼出来（测试与无障碍读屏都用得上）。</summary>
    public string PlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in Segments) { sb.Append(s.Text); }
        return sb.ToString();
    }
}

/// <summary>排好的一页。</summary>
public sealed class BookTextPage
{
    public System.Collections.Generic.List<BookTextLine> Lines { get; }
        = new System.Collections.Generic.List<BookTextLine>();
}

/// <summary>
/// 排版标记的解析与分页。
///
/// 支持的标记（源项目里真用到的子集，其余遇到按普通文本处理，**不丢字**）：
/// <code>
///   $(br)        换行
///   $(br2)       换行 + 一个空行（段落间距）
///   $(p)         同上
///   $(bold)      以下加粗          $(italic)   以下斜体
///   $(0)…$(9)    切换颜色代号
///   $(l:目标)    以下是链接        $(/l)       链接结束
///   $()          清空所有样式
/// </code>
/// </summary>
public static class BookTextLayout
{
    /// <summary>把一串标记文本拆成同质段。不做换行，不做分页。</summary>
    public static System.Collections.Generic.List<BookTextSegment> Parse(string markup)
    {
        var result = new System.Collections.Generic.List<BookTextSegment>();
        if (string.IsNullOrEmpty(markup)) { return result; }

        bool bold = false, italic = false;
        int color = 0;
        string link = string.Empty;

        int i = 0;
        var pending = new System.Text.StringBuilder();

        void Flush()
        {
            if (pending.Length == 0) { return; }
            result.Add(new BookTextSegment
            {
                Text = pending.ToString(),
                Bold = bold,
                Italic = italic,
                ColorCode = color,
                LinkTarget = link,
            });
            pending.Clear();
        }

        while (i < markup.Length)
        {
            // 只有 "$(" 才是标记；单独的 '$' 当普通字符（金额之类）
            if (markup[i] == '$' && i + 1 < markup.Length && markup[i + 1] == '(')
            {
                int close = markup.IndexOf(')', i + 2);
                if (close < 0)
                {
                    // 没有闭合 —— 当普通文本，绝不吞掉后面的内容
                    pending.Append(markup[i]);
                    i++;
                    continue;
                }

                string token = markup.Substring(i + 2, close - i - 2);

                if (token.StartsWith("l:"))
                {
                    Flush();
                    link = token.Substring(2);
                    i = close + 1;
                    continue;
                }

                switch (token)
                {
                    case "br":
                    case "br2":
                    case "p":
                        Flush();
                        result.Add(new BookTextSegment { Text = "\n", Bold = bold, Italic = italic, ColorCode = color, LinkTarget = string.Empty });
                        i = close + 1;
                        continue;

                    case "bold": Flush(); bold = true; i = close + 1; continue;
                    case "italic": Flush(); italic = true; i = close + 1; continue;

                    case "/l":
                        Flush();
                        link = string.Empty;
                        i = close + 1;
                        continue;

                    case "":
                        Flush();
                        bold = false;
                        italic = false;
                        color = 0;
                        link = string.Empty;
                        i = close + 1;
                        continue;
                }

                if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
                {
                    Flush();
                    color = token[0] - '0';
                    i = close + 1;
                    continue;
                }

                // 不认识的标记：整段当普通文本保留（宁可显示难看的原文，也不静默丢内容）
                pending.Append(markup, i, close - i + 1);
                i = close + 1;
                continue;
            }

            pending.Append(markup[i]);
            i++;
        }

        Flush();
        return result;
    }

    /// <summary>
    /// 按宽度把段折成行。
    ///
    /// ⚠️ **必须有防死循环的保证**：如果一个词本身比整行还宽（超长单词、连写的 CJK、
    /// 或者调用方给的宽度是 0），贪心换行会一行都放不下它，于是原地打转。
    /// 这里的规则是：过长的词按字符硬切；**任何一次迭代都必须让输入前进**，
    /// 否则把剩下的全部塞进当前行收尾。
    /// </summary>
    public static System.Collections.Generic.List<BookTextLine> Wrap(
        System.Collections.Generic.List<BookTextSegment> segments,
        int lineWidth,
        MeasureWidth measure)
    {
        var lines = new System.Collections.Generic.List<BookTextLine>();
        if (lineWidth <= 0) { lineWidth = 1; }   // 防调用方传 0

        // 全空的输入直接返回 0 行 —— 否则会往下走出一条空行、再变成一页空白页，
        // 书里就会多出一张什么都没有的纸。离线断言把这条钉住了。
        bool hasAny = false;
        foreach (var s in segments)
        {
            if (!string.IsNullOrEmpty(s.Text)) { hasAny = true; break; }
        }
        if (!hasAny) { return lines; }

        var line = new BookTextLine();
        lines.Add(line);

        foreach (var seg in segments)
        {
            // 段内的硬换行单独处理：\n 直接断行
            var chunks = seg.Text.Split('\n');
            for (int c = 0; c < chunks.Length; c++)
            {
                if (c > 0)
                {
                    line = new BookTextLine();
                    lines.Add(line);
                }

                WrapChunk(chunks[c], seg, lineWidth, measure, lines, ref line);
            }
        }

        // 末尾若只剩一个空行且它前面还有内容，去掉它（避免多出一个空页）
        if (lines.Count > 1 && lines[lines.Count - 1].Segments.Count == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static void WrapChunk(
        string chunk,
        BookTextSegment style,
        int lineWidth,
        MeasureWidth measure,
        System.Collections.Generic.List<BookTextLine> lines,
        ref BookTextLine line)
    {
        if (chunk.Length == 0) { return; }

        // 按空格切词，但**保留尾随空格**（否则行尾单词会粘在一起）
        var words = new System.Collections.Generic.List<string>();
        int start = 0;
        for (int i = 0; i < chunk.Length; i++)
        {
            if (chunk[i] == ' ')
            {
                words.Add(chunk.Substring(start, i - start + 1));
                start = i + 1;
            }
        }
        if (start < chunk.Length) { words.Add(chunk.Substring(start)); }

        foreach (var word in words)
        {
            int w = measure(word);

            // 放不下就换行 —— 但**当前行已经有内容**才换，否则会死循环
            if (line.Segments.Count > 0 && line.Width + w > lineWidth)
            {
                line = new BookTextLine();
                lines.Add(line);
            }

            // 单词本身比整行还宽：按字符硬切，保证一定有进展
            if (w > lineWidth)
            {
                foreach (var piece in HardSplit(word, lineWidth, measure))
                {
                    int pw = measure(piece);
                    if (line.Segments.Count > 0 && line.Width + pw > lineWidth)
                    {
                        line = new BookTextLine();
                        lines.Add(line);
                    }
                    line.Segments.Add(Clone(style, piece));
                    line.Width += pw;
                }
                continue;
            }

            line.Segments.Add(Clone(style, word));
            line.Width += w;
        }
    }

    private static System.Collections.Generic.List<string> HardSplit(string word, int lineWidth, MeasureWidth measure)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (word.Length <= 1)
        {
            parts.Add(word);   // 单个字符都放不下：仍然放，宁可溢出也不转圈
            return parts;
        }

        var cur = new System.Text.StringBuilder();
        foreach (var ch in word)
        {
            cur.Append(ch);
            if (measure(cur.ToString()) >= lineWidth)
            {
                parts.Add(cur.ToString());
                cur.Clear();
            }
        }
        if (cur.Length > 0) { parts.Add(cur.ToString()); }

        // 兜底：一个部件都没切出来（例如测量函数恒返回 0）时，整词放行
        if (parts.Count == 0) { parts.Add(word); }
        return parts;
    }

    private static BookTextSegment Clone(BookTextSegment src, string text) => new()
    {
        Text = text,
        Bold = src.Bold,
        Italic = src.Italic,
        ColorCode = src.ColorCode,
        LinkTarget = src.LinkTarget,
    };

    /// <summary>把行切成每页 <paramref name="linesPerPage"/> 行。</summary>
    public static System.Collections.Generic.List<BookTextPage> Paginate(
        System.Collections.Generic.List<BookTextLine> lines,
        int linesPerPage)
    {
        var pages = new System.Collections.Generic.List<BookTextPage>();
        if (linesPerPage <= 0) { linesPerPage = 1; }
        if (lines.Count == 0) { return pages; }   // 没有行就没有页（同 Wrap 里的说明）

        var page = new BookTextPage();
        pages.Add(page);

        foreach (var l in lines)
        {
            if (page.Lines.Count >= linesPerPage)
            {
                page = new BookTextPage();
                pages.Add(page);
            }
            page.Lines.Add(l);
        }

        if (pages.Count > 1 && pages[pages.Count - 1].Lines.Count == 0)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        return pages;
    }

    /// <summary>一步到位：标记文本 → 分好页的行。离线断言主要打这个入口。</summary>
    public static System.Collections.Generic.List<BookTextPage> Layout(
        string markup,
        int lineWidth,
        int linesPerPage,
        MeasureWidth measure)
        => Paginate(Wrap(Parse(markup), lineWidth, measure), linesPerPage);
}
