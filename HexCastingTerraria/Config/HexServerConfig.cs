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

    /// <summary>
    /// 爆炸法术是否破坏方块。原版就是真的 MC 爆炸（允许破坏时会炸掉方块），默认开启；
    /// 按泰拉炸弹的规则判定（地牢砖、神庙砖、箱子等炸不动）。不想让法术拆家可以关掉。
    /// </summary>
    [DefaultValue(true)]
    public bool ExplosionsBreakBlocks { get; set; } = true;

    /// <summary>
    /// 原版 trueNameHasAmbit（默认开）：玩家的「真名」不受施法范围限制 —— 拿到某个玩家的引用，
    /// 就能在任何距离对他施法（区域查询除外）。关掉后玩家和其它实体一样受 32 格范围限制。
    /// </summary>
    [DefaultValue(true)]
    public bool TrueNameHasAmbit { get; set; } = true;

    /// <summary>
    /// 联机时**所有**玩家能不能用 /hexcasting 指令（发大法术古卷、重生成笔顺、剖念）。
    /// 原版这些指令要管理员 / 游戏管理员权限；泰拉没有权限系统，默认只有**服务器控制台**和**房主**
    /// （「创建并游玩」开服的人）能用。单人游戏不受限制。
    /// </summary>
    [DefaultValue(false)]
    public bool PlayersCanUseCommands { get; set; }
}
