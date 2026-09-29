using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Config;

/// <summary>
/// 模组的客户端配置，会出现在 tModLoader 的「设置 → 模组配置」里。
///
/// ## 这一节的定位
///
/// `DevGrants` / `DevRules` / `DevDisplay` 三组是**开发者调试开关**：
/// 它们不是「玩法选项」，而是为了**能单独验证每一条机制**。
/// 咒法学有 188 条图案、几十种物品与多套子系统（媒质 / 环 / 飞行 / 脑叶切除…），
/// 靠正常玩法一个个试到，成本高到不现实 —— 所以每个子系统都给一个「直接跳到最后一步」的开关。
///
/// 它们默认**全部关闭**：默认值就是「正常玩法」，不会误伤普通玩家。
///
/// 本地化键（tModLoader 约定）：
///   Mods.HexCastingTerraria.Configs.HexClientConfig.{成员名}.Label
///   Mods.HexCastingTerraria.Configs.HexClientConfig.{成员名}.Tooltip
/// 对应 Localization/zh-Hans_Mods.HexCastingTerraria.Configs.hjson
/// </summary>
public sealed class HexClientConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    /// <summary>
    /// 配置实例的便捷访问。
    ///
    /// 用静态缓存时要小心：模组加载极早期配置可能还没注册，
    /// 因此不在字段初始化器里取，而是首次访问时惰性获取并缓存。
    /// </summary>
    private static HexClientConfig? _instance;

    public static HexClientConfig Instance
        => _instance ??= ModContent.GetInstance<HexClientConfig>();

    // ── 开发者：直接给状态 ─────────────────────────────────────────

    [Header("DevGrants")]
    [DefaultValue(false)]
    public bool InfiniteMedia { get; set; }

    /// <summary>
    /// 进世界时自动发放一整套测试物品（每个主要子系统各一份）。
    /// 也可以随时按快捷键 K 手动再发一次。
    /// </summary>
    [DefaultValue(false)]
    public bool GiveDevKitOnEnter { get; set; }

    /// <summary>始终视为已启蒙 —— 用来测 14 条大法术，不必真的去触发过载。</summary>
    [DefaultValue(false)]
    public bool AlwaysEnlightened { get; set; }

    /// <summary>过载施法不扣血（媒质不够时照常施放，且不会掉血、不会死）。</summary>
    [DefaultValue(false)]
    public bool NoOvercastDamage { get; set; }

    /// <summary>每次施法结束后把媒质补满 —— 连续测多条法术时不用一直补。</summary>
    [DefaultValue(false)]
    public bool RefillMediaAfterCast { get; set; }

    /// <summary>咒法学之书全部解锁（正常按进度解锁，见 Core/Ui/BookUnlocks.cs）。</summary>
    [DefaultValue(false)]
    public bool UnlockWholeBook { get; set; }

    // ── 开发者：放宽机制限制 ───────────────────────────────────────

    /// <summary>打包法术无冷却。</summary>
    [DefaultValue(false)]
    public bool NoPackagedCooldown { get; set; }

    /// <summary>咒法飞行永不结束（不落地、不计时、不限距）。</summary>
    [DefaultValue(false)]
    public bool InfiniteFlight { get; set; }

    /// <summary>法术环每 tick 走一格（正常是 10 → 2 tick，越长越快）。</summary>
    [DefaultValue(false)]
    public bool FastSpellCircles { get; set; }

    /// <summary>法术环不消耗媒质。</summary>
    [DefaultValue(false)]
    public bool FreeSpellCircles { get; set; }

    /// <summary>
    /// 脑叶切除放宽限制：任意 NPC 都能切（正常只允许城镇 NPC 与小动物）。
    /// 用来测 5 条配方，不必每次都去凑对应的 NPC。
    /// </summary>
    [DefaultValue(false)]
    public bool LooseBrainsweepTargets { get; set; }

    /// <summary>晶簇立即长成（正常的生长间隔被压到 1 tick）。</summary>
    [DefaultValue(false)]
    public bool InstantCrystalGrowth { get; set; }

    // ── 开发者：显示 ───────────────────────────────────────────────

    [Header("DevDisplay")]
    [DefaultValue(true)]
    public bool ShowMediaRing { get; set; } = true;

    /// <summary>
    /// 画布上的开发诊断信息（实时角度串、「已命中可以松手」、最近识别结果、调试面板、临摹引导）。
    /// 原版画布上只有笔迹、栈和渡鸦之思；这些是开发期排错用的，默认关闭。
    /// </summary>
    [DefaultValue(false)]
    public bool ShowDebugPanel { get; set; } = false;



    /// <summary>
    /// 显示「瞄准标记」：用粒子标出鼠标指向的世界位置。
    ///
    /// 设计说明（见 LOOK_DIRECTION_DESIGN.md）：
    /// 泰拉侧「施法者视线」= 鼠标方向；射线命中点 = 法术指向的位置。
    /// 单人时纯客户端即可显示；联机时把 aimDirection 随施法包一起广播，
    /// 其他客户端就能在同一位置画标记（不需要额外同步通道）。
    /// </summary>
    [DefaultValue(true)]
    public bool ShowAimMarker { get; set; } = true;

    /// <summary>瞄准标记单次最多生成多少粒子（防止刷爆 dust 数组）。</summary>
    [Range(1, 64)]
    [Increment(1)]
    [DefaultValue(8)]
    public int AimMarkerDustCount { get; set; } = 8;

    /// <summary>显示鼠标指向的实体坐标（种类#索引）—— 调试 `get_entity` / `zone_entity` 必备。</summary>
    [DefaultValue(false)]
    public bool ShowEntityIds { get; set; }

    /// <summary>画出瞄准射线与命中格（调试射线类图案必备）。</summary>
    [DefaultValue(false)]
    public bool ShowAimRay { get; set; }

    /// <summary>显示法术环的运行时状态（当前格 / 已走格数 / 速度 / 剩余媒质）。</summary>
    [DefaultValue(false)]
    public bool ShowCircleDebug { get; set; }

    /// <summary>画布上显示识别结果（图案 id / 签名 / 参数个数）。</summary>
    [DefaultValue(true)]
    public bool ShowPatternId { get; set; } = true;

    // ── 画布与性能 ─────────────────────────────────────────────────

    /// <summary>
    /// 求值步数上限（对应原作 maxOpCount）。
    /// 单次施法累计消耗的操作数超过此值即触发「算力耗尽」mishap。
    /// </summary>
    [Header("Display")]
    [Range(100, 10000000)]
    [Increment(1000)]
    [DefaultValue(100000)]
    public int MaxOpCount { get; set; } = 100000;

    [Range(0.5f, 2.0f)]
    [Increment(0.1f)]
    [DefaultValue(1.0f)]
    public float GridZoom { get; set; } = 1.0f;

    /// <summary>
    /// 画布吸附阈值。对应源项目 `gridSnapThreshold`（原版默认 0.5，范围 0.5~1.0）。
    ///
    /// 每记一步所需的拖拽距离 = hexSize × √(2 × 阈值)；相邻格点相距 √3 × hexSize。
    /// 0.5 → 走到格距的 58% 就提交（原版默认，玩家反馈太容易碰到点画错）；
    /// 1.0 → 82%（本模组默认）；1.15 → 88%（上限，再高带手抖的画法会出错，见 PatternDrawer）。
    /// ⚠️ 这里曾写「0.5 = 恰好 1 个格距、1.0 = 1.41 个格距」—— 把 hexSize 当成了格距，是错的。
    /// </summary>
    [Range(Core.Canvas.PatternDrawer.MinSnapThreshold, Core.Canvas.PatternDrawer.MaxSnapThreshold)]
    [Increment(0.05f)]
    [DefaultValue(1.0f)]
    public float GridSnapThreshold { get; set; } = 1.0f;

    /// <summary>
    /// 画布笔迹粗细（线宽、节点、背景引导点一起缩放）。
    /// 1.0 = 原版在 MC 界面缩放 4（1080p 自动）下的比例；玩家反馈偏粗，默认取 0.5（≈ MC 界面缩放 2）。
    /// </summary>
    [Range(0.2f, 1.5f)]
    [Increment(0.05f)]
    [DefaultValue(0.5f)]
    public float StrokeScale { get; set; } = 0.5f;

    /// <summary>
    /// 电光抖动强度。1.0 = 原版（variance 2.5）。抖动幅度按线段长度算、与线宽无关，
    /// 线调细以后同样的抖动显得更强，所以默认也取 0.5。0 = 完全不抖。
    /// </summary>
    [Range(0f, 1.5f)]
    [Increment(0.05f)]
    [DefaultValue(0.5f)]
    public float WobbleScale { get; set; } = 0.5f;

    /// <summary>
    /// 咒法学之书的大小。1.0 = 书高占屏幕约 88%（按整数倍放大，像素最清楚）；
    /// 其它值按半格步进，太大放不下时自动缩回。
    /// </summary>
    [Range(0.5f, 1.2f)]
    [Increment(0.05f)]
    [DefaultValue(1.0f)]
    public float BookSize { get; set; } = 1.0f;
}
