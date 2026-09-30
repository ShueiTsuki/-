using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// 分词：把一段代码切成符号（上游 parsers/CodeCutter.kt，逐行照搬）。
///
/// - 分隔符：<c>; , 空格 制表符</c>；换行在「缩进 = ALL」时变成 <c>tab_N</c>（N = 行首空白长度）
/// - <c>// 行注释</c>、<c>/* 块注释 */</c>：「注释 = ALL」时变成 <c>c"…"</c> 注释符号，否则丢掉
/// - <c>"…"</c>：字符串（支持反斜杠转义）
/// - 其余：<c>\ ( ) { } [ ]</c> 单字符，或一串 <c>[A-Za-z0-9_./\-:#]</c> 与 U+0100 以上的字符
/// - 连续空行超过 MaxBlankLineCount 的丢掉
///
/// 两处和上游一致的「怪行为」保留：符号的正则不锚定开头（开头认不出的字符直接跳过）；
/// 一个字符都没吃掉时强行切掉一个字符，避免死循环。
/// </summary>
public sealed class CodeCutter
{
    // Java 的 \w 只有 ASCII（没开 UNICODE_CHARACTER_CLASS），.NET 的 \w 含 Unicode 字母 —— 所以这里写死 ASCII 区间
    private static readonly Regex Tokens = new(@"\\|\(|\)|\{|\}|\[|]|[A-Za-z0-9_./\-:#\u0100-\uffff]+", RegexOptions.CultureInvariant);
    // Java 的 . 不匹配任何行终止符（\r 也不行），.NET 的 . 只排除 \n —— Windows 换行下注释会多带一个 \r，所以写成显式字符类
    private static readonly Regex CommentLine = new("//[^\r\n\u0085\u2028\u2029]*");
    private static readonly Regex LineBreak = new(@"\r?\n");
    private static readonly Regex CommentBlock = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex LineStart = new(@"^[ \t]*");
    private static readonly HashSet<char> Whitespace = new() { ';', ',', ' ', '\t' };

    private readonly int _maxBlankLines;
    private bool _newLineThisTurn;
    private int _seqNewLineCount;

    /// <summary>出错时已经切好的那部分（上游 tryRecoverSplittedCode：内层出错也尽量保住前面的）。</summary>
    public List<string> Recovered { get; private set; } = new();

    private CodeCutter(int maxBlankLines) => _maxBlankLines = maxBlankLines;

    /// <summary>按当前配置分词（上游 splitCode(code)）。出错时抛 ArgumentException，已切好的在 <paramref name="recovered"/>。</summary>
    public static List<string> Split(string code, HexParseSettings settings, out List<string> recovered)
    {
        var cutter = new CodeCutter(settings.MaxBlankLineCount);
        try
        {
            return cutter.SplitCode(code,
                settings.IndentParsing == HexParseSettings.CommentMode.All,
                settings.CommentParsing == HexParseSettings.CommentMode.All);
        }
        finally
        {
            recovered = cutter.Recovered;
        }
    }

    private List<string> SplitCode(string code, bool addIndent, bool commentsToIota)
    {
        var list = new List<string>();
        Recovered = list;
        _seqNewLineCount = 0;
        while (code.Length > 0)
        {
            _newLineThisTurn = false;
            var (token, newCode) = code[0] switch
            {
                '/' => ConsumeComment(code, commentsToIota),
                '"' => ConsumeString(code),
                var c when Whitespace.Contains(c) => ConsumeWhiteSpace(code),
                '\r' or '\n' => ConsumeNewline(code, addIndent),
                _ => ConsumeToken(code),
            };
            // 连续空行裁剪
            if (token != null)
            {
                if (_newLineThisTurn)
                {
                    _seqNewLineCount++;
                    if (_seqNewLineCount > _maxBlankLines)
                    {
                        // 替换上一个缩进，而不是丢掉这一个
                        if (list.Count == 0) token = null;
                        else list.RemoveAt(list.Count - 1);
                    }
                }
                else
                {
                    _seqNewLineCount = -1;
                }
            }
            if (code == newCode)
            {
                // 一个字符都没吃掉 = 死循环：切掉出问题的那个字符
                newCode = code.Substring(1);
            }
            code = newCode;
            if (token != null) list.Add(token);
        }
        return list;
    }

    private static string ToIndent(string match) => "tab_" + match.Length;

    private static (string?, string) ConsumeToken(string code)
    {
        var m = Tokens.Match(code);
        if (!m.Success) throw new ArgumentException("无法识别的字符：" + code);
        return (m.Value, code.Substring(m.Index + m.Length));
    }

    private static string CommentToCommentString(string comment) => "c\"" + StringEscaper.Escape(comment) + "\"";

    private static (string?, string) ConsumeLineComment(string code, bool commentsToIota)
    {
        var m = CommentLine.Match(code);
        string contents = m.Value.Substring(2);
        return (commentsToIota ? CommentToCommentString(contents) : null, code.Substring(m.Index + m.Length));
    }

    private static (string?, string) ConsumeBlockComment(string code, bool commentsToIota)
    {
        var m = CommentBlock.Match(code);
        if (!m.Success) throw new ArgumentException("块注释没有闭合：" + code);
        string rest = code.Substring(m.Index + m.Length);
        return commentsToIota
            ? (CommentToCommentString(m.Value.Substring(2, m.Value.Length - 4)), rest)
            : (null, rest);
    }

    private static (string?, string) ConsumeWhiteSpace(string code)
    {
        int i = 0;
        while (i < code.Length && Whitespace.Contains(code[i])) i++;
        return (null, code.Substring(i));
    }

    private (string?, string) ConsumeNewline(string code, bool addIndent)
    {
        string input = LineBreak.Replace(code, "", 1);   // 上游 replaceFirst：去掉（第一个）换行
        _newLineThisTurn = true;
        var m = LineStart.Match(input);
        return (addIndent ? ToIndent(m.Value) : null, input.Substring(m.Index + m.Length));
    }

    private static (string?, string) ConsumeString(string code)
    {
        int index = 1;   // 跳过开头的 "
        if (index >= code.Length) throw new ArgumentException("unclosed string literal: " + code);
        while (code[index] != '"')
        {
            if (code[index] == '\\') index++;   // 跳过被转义的字符
            index++;
            if (index >= code.Length) throw new ArgumentException("unclosed string literal: " + code);
        }
        return (code.Substring(0, index + 1), code.Substring(index + 1));
    }

    private static (string?, string) ConsumeComment(string code, bool commentsToIota)
    {
        return (code.Length > 1 ? code[1] : '\0') switch
        {
            '*' => ConsumeBlockComment(code, commentsToIota),
            '/' => ConsumeLineComment(code, commentsToIota),
            _ => throw new ArgumentException("invalid comment: " + code),
        };
    }
}
