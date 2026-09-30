using System.ComponentModel;
using HexCastingTerraria.Addons.HexParse.Core;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// HexParse 的配置项，挂在服务端「附属兼容」页的 HexParse 开关下面（上游 fabric/config/HexParseConfigFabric.java，默认值照搬）。
/// 不标 ReloadRequired：房主在游戏里随时能改，改完 <see cref="Apply"/> 写进 <see cref="HexParseSettings.Current"/>。
/// 上游还有 fairPlayPropNames / syncDisplayToClient 两项：前者只给 Hexcellular 用、后者是 .hexpattern 的服务端名表同步，泰拉侧用不上，不做。
/// </summary>
public sealed class HexParseOptions
{
    [DefaultValue(HexParseSettings.GreatMode.ByScroll)]
    public HexParseSettings.GreatMode ParseGreatSpells { get; set; } = HexParseSettings.GreatMode.ByScroll;

    [DefaultValue(HexParseSettings.CommentMode.Manual)]
    public HexParseSettings.CommentMode CommentParsing { get; set; } = HexParseSettings.CommentMode.Manual;

    [DefaultValue(HexParseSettings.CommentMode.Manual)]
    public HexParseSettings.CommentMode IndentParsing { get; set; } = HexParseSettings.CommentMode.Manual;

    [DefaultValue(true)]
    public bool AddIndentInsideMacro { get; set; } = true;

    [DefaultValue(true)]
    public bool AlwaysShortName { get; set; } = true;

    [DefaultValue(0)]
    [Range(0, 100000)]
    public int ParserBaseCost { get; set; }

    [DefaultValue(true)]
    public bool ShowColorfulNested { get; set; } = true;

    [DefaultValue(HexParseSettings.UnknownMode.KeepNbt)]
    public HexParseSettings.UnknownMode ShowUnknownNbt { get; set; } = HexParseSettings.UnknownMode.KeepNbt;

    [DefaultValue(0)]
    [Range(0, 100)]
    public int MaxBlankLineCount { get; set; }

    [DefaultValue(3)]
    [Range(0, 3)]
    public int AttachCodeMeta { get; set; } = 3;

    /// <summary>写进解析器用的那份设置（配置改了就调一次）。</summary>
    public void Apply()
    {
        HexParseSettings.Current = new HexParseSettings
        {
            ParseGreatSpells = ParseGreatSpells,
            CommentParsing = CommentParsing,
            IndentParsing = IndentParsing,
            AddIndentInsideMacro = AddIndentInsideMacro,
            AlwaysShortName = AlwaysShortName,
            ParserBaseCost = ParserBaseCost,
            ShowColorfulNested = ShowColorfulNested,
            ShowUnknownNbt = ShowUnknownNbt,
            MaxBlankLineCount = MaxBlankLineCount,
            AttachCodeMeta = AttachCodeMeta,
        };
    }

    public override bool Equals(object? obj) => obj is HexParseOptions o
        && o.ParseGreatSpells == ParseGreatSpells && o.CommentParsing == CommentParsing && o.IndentParsing == IndentParsing
        && o.AddIndentInsideMacro == AddIndentInsideMacro && o.AlwaysShortName == AlwaysShortName && o.ParserBaseCost == ParserBaseCost
        && o.ShowColorfulNested == ShowColorfulNested && o.ShowUnknownNbt == ShowUnknownNbt && o.MaxBlankLineCount == MaxBlankLineCount
        && o.AttachCodeMeta == AttachCodeMeta;

    public override int GetHashCode() => System.HashCode.Combine(ParseGreatSpells, CommentParsing, IndentParsing, ParserBaseCost, MaxBlankLineCount, AttachCodeMeta);
}
