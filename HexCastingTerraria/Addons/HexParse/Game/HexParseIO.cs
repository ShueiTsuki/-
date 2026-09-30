using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Iotas;
using Microsoft.Xna.Framework;
using ReLogic.OS;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>剪贴板读进来之后干什么（上游 network/ClipboardMsgMode.java）。</summary>
public enum ClipboardMode : byte
{
    /// <summary>当代码解析进手上的核心。</summary>
    Default = 0,

    /// <summary>只挑出里面所有 "wedsaq" 形式的笔画串。</summary>
    AnglesOnly = 1,

    /// <summary>定义成宏（rename = 宏名）。</summary>
    MacroDefine = 2,

    /// <summary>压进思维栈。</summary>
    PushMind = 5,

    /// <summary>.hexpattern 格式（实验性，上游也只支持一部分）。</summary>
    HexPatternFormat = 6,
}

/// <summary>
/// 读写手上的物品、剪贴板、显示结果（上游 misc/IOMethod.kt + misc/CodeHelpers.java + network/MsgPullClipboard.java 的客户端部分）。
///
/// 这些都在**本人客户端**做：泰拉的物品归玩家客户端、剪贴板在客户端、宏在客户端。
/// 能写的物品：核心、念珠（thought knot）、法术书（当前页）。「另一只手」= 快捷栏里手上那格右边一格，优先用它（上游副手优先）。
/// 上游直接改物品数据，不看密封、念珠也能覆盖 —— 照搬（ItemIotaStorage.ForceWrite）。
/// </summary>
public static class HexParseIO
{
    /// <summary>上游 MsgPullClipboard.MAX_LENGTH = 100 × MAX_SERIALIZATION_TOTAL；原文再放宽 10 倍。</summary>
    private const int MaxLength = 100 * CodeParser.MaxTokens;
    private const int MaxLengthRaw = MaxLength * 10;
    private static readonly Regex Angles = new("(?<=\")[wedsaq]*(?=\")");

    /// <summary>上游 getItemIO：副手（右边一格）优先，其次手上。</summary>
    public static ItemIotaStorage? HeldIO(Player player)
    {
        static ItemIotaStorage? Io(Item? it) => it is { IsAir: false, ModItem: Focus or ThoughtKnot or Spellbook } ? (ItemIotaStorage)it.ModItem : null;
        int main = player.selectedItem;
        var mainIo = Io(player.HeldItem);
        var offIo = main is >= 0 and < 10 ? Io(player.inventory[(main + 1) % 10]) : null;
        return offIo ?? mainIo;
    }

    /// <summary>上游 CodeHelpers.doParse：解析后写进手上的物品，可顺便改名；按配置扣媒质。</summary>
    public static void WriteHeld(Player player, System.Func<CodeParser, ListIota> parse, string? rename)
    {
        var target = HeldIO(player);
        if (target == null) return;
        var host = new HexParseHost(player);
        var parser = host.NewParser();
        var list = parse(parser);
        target.ForceWrite(list);
        if (rename != null) target.CustomName = rename;
        target.Item.NetStateChanged();
        ChargeCost(player, parser.TotalCost);
    }

    /// <summary>上游 readHand：手上物品的内容写成代码；没有能读的物品 / 物品是空的 -> null。</summary>
    public static string? ReadHeld(Player player, bool forceSignatures, System.Func<string, string> post)
    {
        var iota = HeldIO(player)?.Read();
        if (iota == null) return null;
        return new HexParseHost(player).NewWriter().Write(iota, forceSignatures, post);
    }

    /// <summary>上游 CostTracker.close：解析费（默认 0）从背包的媒质里扣，扣不起就过载（同施法）。</summary>
    public static void ChargeCost(Player player, long cost)
    {
        if (cost <= 0) return;
        new PlayerCastingEnvironment(player).ExtractMedia(cost, simulate: false);
    }

    /// <summary>
    /// 上游 displayCode：「结果：…」，可以点一下复制。
    /// 泰拉聊天不能点击复制（偏差）—— 直接写进剪贴板，并说一声。
    /// </summary>
    public static void DisplayCode(string? code)
    {
        if (code == null) return;
        Main.NewText("结果：" + code, new Color(85, 255, 85));
        SetClipboard(code);
        Main.NewText("已复制到剪贴板", new Color(170, 170, 170));
    }

    public static string GetClipboard()
    {
        try { return Platform.Get<IClipboard>().Value ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static void SetClipboard(string text)
    {
        try { Platform.Get<IClipboard>().Value = text; }
        catch { /* 没有剪贴板（专用服务器）就算了 */ }
    }

    /// <summary>
    /// 上游 MsgPullClipboard.handle + MsgPushClipboard.handle：读剪贴板 -> 按模式预处理 -> 客户端预检（只留认得出的符号）-> 按模式落地。
    /// 单人 / 联机都在本人客户端执行（联机时服务端发「拉剪贴板」过来）。
    /// </summary>
    public static void HandleClipboard(Player player, ClipboardMode mode, string? rename)
    {
        string code = GetClipboard();
        if (string.IsNullOrWhiteSpace(code)) return;
        if (code.Length > MaxLengthRaw)
        {
            HexParseHost.Show($"代码过长（{code.Length}）", HexParseMessageKind.Error);
            return;
        }
        if (mode == ClipboardMode.AnglesOnly)
        {
            code = string.Join(" ", Angles.Matches(code).Select(m => "_" + m.Value));
        }
        else if (mode == ClipboardMode.HexPatternFormat)
        {
            code = DotHexPattern.ProcessCode(code);
        }
        if (code.Length > MaxLength)
        {
            HexParseHost.Show($"代码过长（{code.Length}）", HexParseMessageKind.Error);
            return;
        }

        var host = new HexParseHost(player);
        var tokens = host.NewParser().PreMatch(code, t => HexParseMacros.Contains(t));
        switch (mode)
        {
            case ClipboardMode.MacroDefine:
                if (rename != null) HexParseCommand.DefineMacro(rename, string.Join(",", tokens));
                break;
            case ClipboardMode.PushMind:
                PushMind(player, host.NewParser().ParseTokens(tokens));
                break;
            default:
                WriteHeld(player, p => p.ParseTokens(tokens), rename);
                break;
        }
    }

    /// <summary>上游 writeStackWithIota：压进思维栈（法杖施法的栈）。联机时发给服务端压。</summary>
    public static void PushMind(Player player, Iota iota)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) HexParseNet.SendPushMind(iota);
        else Client.HexVmState.PushIota(player, iota);
    }
}
