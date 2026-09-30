namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// HexParse 的配置项（上游 config/HexParseConfig.java + fabric/HexParseConfigFabric.java 的默认值）。
///
/// 这里是纯数据：游戏侧从 HexAddonsConfig 的 HexParse 子配置抄进 <see cref="Current"/>，离线测试直接改它。
/// 注意上游 HexParseConfig 的说明文字写「缩进默认 ALL」，但 Fabric 实现里的默认值是 MANUAL —— 以代码为准。
/// </summary>
public sealed class HexParseSettings
{
    /// <summary>上游 ParseGreatPatternMode。</summary>
    public enum GreatMode
    {
        /// <summary>全部允许：不用古卷就能直接解析大法术。</summary>
        All,

        /// <summary>全部禁止（上游文档写反了，代码里 DISABLED = 全禁）。</summary>
        Disabled,

        /// <summary>按古卷解锁（默认）：用「内化卓越法术」或 learn_great 学过的才能解析。</summary>
        ByScroll,
    }

    /// <summary>上游 CommentParsingMode：注释 / 缩进怎么变成 iota。</summary>
    public enum CommentMode
    {
        /// <summary>完全不要。</summary>
        Disabled,

        /// <summary>只认手写的 comment_xxx / tab_N（默认）。</summary>
        Manual,

        /// <summary>连代码里的 // 注释、/* */ 注释、换行缩进都转成 iota。</summary>
        All,
    }

    /// <summary>上游 UnknownNbtHandlingMode：读出来的代码里，没有对应写法的 iota 怎么表示。</summary>
    public enum UnknownMode
    {
        /// <summary>只写 UNKNOWN。</summary>
        Simple,

        /// <summary>写 UNKNOWN(内部数据)。</summary>
        ShowNbt,

        /// <summary>编码成 nbt_xxx，能原样解析回来（默认）。</summary>
        KeepNbt,
    }

    public static HexParseSettings Current { get; set; } = new();

    public GreatMode ParseGreatSpells { get; set; } = GreatMode.ByScroll;

    public CommentMode CommentParsing { get; set; } = CommentMode.Manual;

    public CommentMode IndentParsing { get; set; } = CommentMode.Manual;

    /// <summary>宏里面的 tab_N 叠加外层的缩进。</summary>
    public bool AddIndentInsideMacro { get; set; } = true;

    /// <summary>附属的图案也强制用短名（id 的路径部分）输出。</summary>
    public bool AlwaysShortName { get; set; } = true;

    /// <summary>每解析一个符号收的媒质（上游单位：媒质点数，粉 = 10000）。</summary>
    public int ParserBaseCost { get; set; }

    /// <summary>嵌套列表 / 括号彩色显示。</summary>
    public bool ShowColorfulNested { get; set; } = true;

    public UnknownMode ShowUnknownNbt { get; set; } = UnknownMode.KeepNbt;

    /// <summary>连续空行最多保留几行，多的丢掉。</summary>
    public int MaxBlankLineCount { get; set; }

    /// <summary>读出的代码开头附带的信息：1 = 作者，2 = 用到的附属列表（位标志）。</summary>
    public int AttachCodeMeta { get; set; } = 3;
}
