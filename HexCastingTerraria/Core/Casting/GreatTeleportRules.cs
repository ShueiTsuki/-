using System;

namespace HexCastingTerraria.Core.Casting;

/// <summary>
/// 大法术的世界规则。**由游戏侧注入**，Core 只读。
///
/// 为什么要有这一层：`SpellActions` 位于 `Core/Casting/`，
/// 而离线测试工程整目录拷贝 `Casting` —— 只要这里直接引用 tModLoader 的
/// `ModConfig` / `ModContent`，**全部离线测试立刻编译不过**。
///
/// 所以规则本身留在 Core（纯数据 + 默认值），实际取值由
/// `HexCastingTerraria.Load()` 把服务端配置接进来。
///
/// > 这是同类问题的第三次（前两次是 `MediaConstants.OvercastColor`
/// > 与 `ParticleSpray` 的 Vector2）。规则：**`Core/` 不得引用 XNA 或 tModLoader**。
/// </summary>
public static class GreatTeleportRules
{
    /// <summary>大传送是否会把施法者的物品震落。默认开启（与原版一致）。</summary>
    public static Func<bool> DropsItems { get; set; } = static () => true;

    /// <summary>掉落概率的分母（传送距离 / 该值）。默认 10000，与原版一致。</summary>
    public static Func<double> DropDivisor { get; set; } = static () => 10000.0;
}
