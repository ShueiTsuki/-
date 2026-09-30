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

    /// <summary>显式颜色 0xRRGGBB；-1 = 用书的默认正文色。<c>$(#rrggbb)</c> 与 <c>$(0)…$(f)</c> 都落到这里。</summary>
    public int Rgb { get; set; } = -1;

    /// <summary>下划线（<c>$(n)</c>）。</summary>
    public bool Underline { get; set; }

    /// <summary>
    /// 同一个 <c>$(l:…)</c> 的编号（0 = 不是链接）。换行、按字切词后同一链接会散成多段，
    /// 原版 Word.linkCluster：悬停其中任一段，整条链接一起变色。
    /// </summary>
    public int LinkId { get; set; }

    /// <summary>悬停提示（原版 SpanState.tooltip：<c>$(k:…)</c>、<c>$(t:…)</c> 设置，<c>$()</c>/<c>$(/l)</c>/<c>$(/t)</c> 清空）。</summary>
    public string Tooltip { get; set; } = string.Empty;
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
///   $(k:名字)    插入快捷键的按键  $(t:文字)   以下带悬停提示（$(/t) 结束）
///   $()          清空所有样式
/// </code>
/// </summary>
public static class BookTextLayout
{
    /// <summary>
    /// Patchouli 的内置宏（<c>BookTextParser</c> 的默认表）。先展开宏再解析，和原版顺序一致。
    /// 书本自己的宏（咒法学 book.json 里的 <c>$(thing)</c> 等）见 <see cref="BookMacros"/>，先于这里展开。
    /// </summary>
    private static readonly (string From, string To)[] DefaultMacros =
    {
        ("$(obf)", "$(k)"), ("$(bold)", "$(l)"), ("$(strike)", "$(m)"),
        ("$(italic)", "$(o)"), ("$(italics)", "$(o)"), ("$(list", "$(li"),
        ("$(reset)", "$()"), ("$(clear)", "$()"), ("$(2br)", "$(br2)"), ("$(p)", "$(br2)"),
        ("/$", "$()"), ("<br>", "$(br)"), ("$(nocolor)", "$(0)"),
        ("$(item)", "$(#b0b)"), ("$(thing)", "$(#490)"),
    };

    /// <summary>
    /// 书本级宏，照咒法学 <c>thehexbook/book.json</c> 的 <c>macros</c>。
    /// <c>$(thing)</c> 在这里被咒法学覆盖成紫色，所以书本宏必须先于默认宏展开。
    /// </summary>
    public static readonly (string From, string To)[] BookMacros =
    {
        ("$(thing)", "$(#8d6acc)"), ("$(action)", "$(#fc77be)"), ("$(media)", "$(#74b3f2)"),
        ("$(hex)", "$(#b38ef3)"),
        ("_Media", "$(#74b3f2)Media/$"), ("_media", "$(#74b3f2)media/$"),
        ("_Hexcasters", "$(#b38ef3)Hexcasters/$"), ("_Hexcaster", "$(#b38ef3)Hexcaster/$"),
        ("_Hexcasting", "$(#b38ef3)Hexcasting/$"), ("_Hexes", "$(#b38ef3)Hexes/$"), ("_Hex", "$(#b38ef3)Hex/$"),
    };

    /// <summary>MC 的 16 色（<c>$(0)</c>…<c>$(f)</c>）。</summary>
    private static readonly int[] McColors =
    {
        0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xFFAA00, 0xAAAAAA,
        0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF,
    };

    /// <summary>
    /// <c>$(k:名字)</c>：快捷键名 → 当前绑定的按键文字（原版 BookTextParser 的 "k" 函数，
    /// 返回 <c>getTranslatedKeyMessage()</c>）。返回 null = 没有这个快捷键（原版显示 N/A）。
    /// 游戏内由 Client 换成读泰拉当前按键设置的版本；这里的默认值给离线测试用。
    /// </summary>
    public static System.Func<string, string?> KeyName { get; set; } = DefaultKeyName;

    public static string? DefaultKeyName(string key) => key switch
    {
        "use" => "鼠标左键",
        "sneak" => "Shift",
        "sprint" => "Ctrl",
        "jump" => "空格",
        _ => null,
    };

    /// <summary>快捷键的动作名（原版提示框「快捷键：%s」里的 %s，取 MC 的 key.* 中文名）。</summary>
    private static string KeyAction(string key) => key switch
    {
        "use" => "使用物品",
        "sneak" => "潜行",
        "sprint" => "疾跑",
        "jump" => "跳跃",
        _ => key,
    };

    public static string ExpandMacros(string markup)
    {
        foreach (var (from, to) in BookMacros) { markup = markup.Replace(from, to); }
        foreach (var (from, to) in DefaultMacros) { markup = markup.Replace(from, to); }
        return markup;
    }

    /// <summary>
    /// 把一串标记文本拆成同质段。不做换行，不做分页。
    /// 支持 Patchouli 的文本标记：<c>$(br) $(br2) $(li)</c>、<c>$(#rgb)/$(#rrggbb)</c>、
    /// <c>$(0)…$(f)</c>、<c>$(k/l/m/n/o/r)</c>、<c>$(l:目标)…$(/l)</c>、<c>$()</c>（及 <c>/$</c> 等宏）。
    /// 不认识的标记与未闭合的 <c>$(</c> 按原文保留 —— 宁可难看也不静默丢字。
    /// </summary>
    public static System.Collections.Generic.List<BookTextSegment> Parse(string markup)
    {
        var result = new System.Collections.Generic.List<BookTextSegment>();
        if (string.IsNullOrEmpty(markup)) { return result; }
        markup = ExpandMacros(markup);

        bool bold = false, italic = false, underline = false;
        int color = 0, rgb = -1, linkId = 0, links = 0;
        string link = string.Empty, tooltip = string.Empty;
        var pending = new System.Text.StringBuilder();

        void Flush()
        {
            if (pending.Length == 0) { return; }
            result.Add(new BookTextSegment
            {
                Text = pending.ToString(), Bold = bold, Italic = italic, Underline = underline,
                ColorCode = color, Rgb = rgb, LinkTarget = link, LinkId = linkId, Tooltip = tooltip,
            });
            pending.Clear();
        }

        void Break()
        {
            Flush();
            result.Add(new BookTextSegment { Text = "\n", Bold = bold, Italic = italic, ColorCode = color, Rgb = rgb });
        }

        int i = 0;
        while (i < markup.Length)
        {
            if (markup[i] != '$' || i + 1 >= markup.Length || markup[i + 1] != '(')
            {
                pending.Append(markup[i]);
                i++;
                continue;
            }

            int close = markup.IndexOf(')', i + 2);
            if (close < 0)
            {
                pending.Append(markup[i]);
                i++;
                continue;
            }

            string token = markup.Substring(i + 2, close - i - 2);
            bool handled = true;

            if (token.StartsWith("l:", System.StringComparison.Ordinal))
            {
                Flush();
                link = token.Substring(2);
                linkId = ++links;
                tooltip = string.Empty;
            }
            else if (token.StartsWith("k:", System.StringComparison.Ordinal))
            {
                // 原版：插入按键名，提示「快捷键：动作名」；提示和原版一样一直带到下一次 $() / $(/l) / $(/t)
                Flush();
                string key = token.Substring(2);
                string? name = KeyName(key);
                tooltip = name is null ? $"找不到该快捷键：{key}" : $"快捷键：{KeyAction(key)}";
                pending.Append(name ?? "N/A");
            }
            else if (token.StartsWith("t:", System.StringComparison.Ordinal)
                     || token.StartsWith("tooltip:", System.StringComparison.Ordinal))
            {
                Flush();
                tooltip = token.Substring(token.IndexOf(':') + 1);
            }
            else if (token.Length > 1 && token[0] == '#' && TryHex(token.Substring(1), out int hex))
            {
                Flush();
                rgb = hex;
            }
            else
            {
                switch (token)
                {
                    case "br": Break(); break;
                    case "br2": Break(); Break(); break;
                    case "li": case "li2": case "li3": Break(); pending.Append("• "); break;
                    case "/l": Flush(); link = string.Empty; linkId = 0; tooltip = string.Empty; break;
                    case "/t": Flush(); tooltip = string.Empty; break;
                    case "": case "r":
                        Flush();
                        bold = italic = underline = false;
                        color = 0;
                        rgb = -1;
                        link = string.Empty;
                        linkId = 0;
                        tooltip = string.Empty;
                        break;
                    case "l": Flush(); bold = true; break;
                    case "o": Flush(); italic = true; break;
                    case "n": Flush(); underline = true; break;
                    case "k": case "m": Flush(); break;   // 乱码 / 删除线：泰拉字体画不出，忽略样式、保留文字
                    default:
                        if (token.Length == 1 && System.Uri.IsHexDigit(token[0]))
                        {
                            Flush();
                            int idx = System.Convert.ToInt32(token, 16);
                            color = idx <= 9 ? idx : 0;
                            rgb = idx == 0 ? -1 : McColors[idx];
                        }
                        else
                        {
                            handled = false;
                        }
                        break;
                }
            }

            if (!handled) { pending.Append(markup, i, close - i + 1); }
            i = close + 1;
        }

        Flush();
        return result;
    }

    private static bool TryHex(string h, out int rgb)
    {
        rgb = 0;
        if (h.Length == 3)
        {
            h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
        }
        if (h.Length != 6) { return false; }
        foreach (char c in h) { if (!System.Uri.IsHexDigit(c)) { return false; } }
        rgb = System.Convert.ToInt32(h, 16);
        return true;
    }

    /// <summary>
    /// 按宽度把段折成行。
    ///
    /// 注意：**必须有防死循环的保证**：如果一个词本身比整行还宽（超长单词、连写的 CJK、
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
            // 空格后可断行；中日韩字符之间也可断行（中文没有空格，否则整段会被当成一个"词"硬切）。
            // 行首禁则：中文标点不单独起一行，跟在前一个字后面。
            bool nextOk = i + 1 >= chunk.Length || !IsNoLineStart(chunk[i + 1]);
            bool breakAfter = (chunk[i] == ' ' || IsCjk(chunk[i])
                               || (i + 1 < chunk.Length && IsCjk(chunk[i + 1]))) && nextOk;
            if (breakAfter)
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
        Underline = src.Underline,
        ColorCode = src.ColorCode,
        Rgb = src.Rgb,
        LinkTarget = src.LinkTarget,
        LinkId = src.LinkId,
        Tooltip = src.Tooltip,
    };

    private static bool IsCjk(char c)
        => (c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF)
           || (c >= 0x3000 && c <= 0x303F);

    /// <summary>不能出现在行首的标点（中文排版的行首禁则）。</summary>
    private static bool IsNoLineStart(char c) => "，。、；：？！）》」』】〉”’…—·,.;:?!)]}".IndexOf(c) >= 0;

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
