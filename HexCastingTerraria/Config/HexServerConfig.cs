using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Config;

/// <summary>
/// 服务端配置。
///
/// 为什么需要单独一份：<see cref="HexClientConfig"/> 是 ClientSide 的，
/// 但「大传送会不会把你的东西震掉」是**世界规则**，
/// 必须由服务端决定 —— 否则联机时每个客户端各说各话。
/// </summary>
public sealed class HexServerConfig : ModConfig
{
    public static HexServerConfig Instance => ModContent.GetInstance<HexServerConfig>();

    public override ConfigScope Mode => ConfigScope.ServerSide;

    /// <summary>
    /// 大传送（`teleport/great`）是否会把施法者的物品震落。
    ///
    /// 对应源项目配置项 `doesGreaterTeleportSplatItems`，**默认开启** ——
    /// 这是大传送原本的代价：传得越远，东西掉得越多。
    /// 关掉它大传送就变成了「无代价的免费位移」，与原版平衡性不符。
    /// </summary>
    [DefaultValue(true)]
    public bool GreatTeleportDropsItems { get; set; } = true;

    /// <summary>
    /// 大传送把物品震落的距离分母。源项目为 `10000.0`
    /// （即掉落概率 = 传送距离 / 10000，按**图格**计）。
    /// </summary>
    [DefaultValue(10000.0)]
    [Range(100, 1000000)]
    public double GreatTeleportDropDivisor { get; set; } = 10000.0;
}
