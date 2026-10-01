using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Config;

/// <summary>
/// 附属兼容 · 客户端开关（纯界面的附属）。每个人自己决定，随时改、立即生效，联机时互不影响 ——
/// 上游这些附属本来就是纯客户端模组。见 ADDONS.md「开关的实际效果」。
/// </summary>
public sealed class HexAddonsClientConfig : ModConfig
{
    public static HexAddonsClientConfig Instance => ModContent.GetInstance<HexAddonsClientConfig>();

    public override ConfigScope Mode => ConfigScope.ClientSide;

    /// <summary>Hexcessible（Ruby / tizu）：键盘画图、按名字搜索图案、别名、悬停说明。</summary>
    [DefaultValue(false)]
    public bool Hexcessible { get; set; }

    /// <summary>Hexcessible 的配置项（上游 HexcessibleConfig）。</summary>
    public Addons.Hexcessible.Game.HexcessibleOptions HexcessibleOptions { get; set; } = new();

    /// <summary>HexDebug 的客户端配置项（上游 HexDebugClientConfig 的外部调试端口；HexDebug 开关在服务端那一页）。</summary>
    public Addons.HexDebug.Game.HexDebugClientOptions HexDebugOptions { get; set; } = new();

    public override void OnChanged()
    {
        HexcessibleOptions.Apply();
        Addons.HexDebug.Game.HexDebugProxy.Reconfigure();
    }
}
