using System.ComponentModel;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Config;

/// <summary>
/// 附属兼容 · 服务端开关（有物品 / 方块 / 图案 / 指令的附属）。见 ADDONS.md「开关的实际效果」。
///
/// - 总开关标 [ReloadRequired]：关着 = 没装（内容不加载）。联机时由开服的人决定，别人进服 tML 自动同步并提示重载；
///   联机游玩中 tML 会拒绝保存需要重载的改动，要改就停服改配置。
/// - 各附属的子选项不标 ReloadRequired，房主能在游戏里随时改。
/// - 只接受房主的修改（泰拉没有权限系统，和 /hexcasting 指令同一个规矩）。
/// </summary>
public sealed class HexAddonsConfig : ModConfig
{
    public static HexAddonsConfig Instance => ModContent.GetInstance<HexAddonsConfig>();

    public override ConfigScope Mode => ConfigScope.ServerSide;

    /// <summary>HexParse（YukkuriC）：代码文本与 iota 列表互转、/hexParse 指令。</summary>
    [DefaultValue(false)]
    [ReloadRequired]
    public bool HexParse { get; set; }

    /// <summary>HexParse 的配置项（开关打开才有意义；房主随时能改）。</summary>
    public Addons.HexParse.Game.HexParseOptions HexParseOptions { get; set; } = new();

    /// <summary>HexDebug（object-Object）：调试杖逐步执行咒术、剪接台编辑咒术。</summary>
    [DefaultValue(false)]
    [ReloadRequired]
    public bool HexDebug { get; set; }

    /// <summary>配置改了（包括进服时收到服务器的值）：把附属的子配置写进各自的设置。</summary>
    public override void OnChanged() => HexParseOptions.Apply();

    public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
    {
        if (Main.countsAsHostForGameplay[whoAmI]) return true;
        message = NetworkText.FromKey("Mods.HexCastingTerraria.Misc.OnlyHostCanChangeAddons");   // 按收到的那个客户端的语言显示
        return false;
    }
}
