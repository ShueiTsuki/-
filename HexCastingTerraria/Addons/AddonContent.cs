using Terraria.ModLoader;

namespace HexCastingTerraria.Addons;

// 附属的游戏内容一律继承这里的基类：开关关着时 IsLoadingEnabled 返回 false，tML 就不加载它 ——
// 这就是「关着 = 没装」。已经存在的物品 / 方块会被 tML 保管成「未加载」，重新打开就恢复。
// check_arch 断言：Addons/<名>/ 下不许直接继承 ModItem / ModTile / ModCommand …，必须走这些基类。
// 缺哪种基类就在这里补一个，照同一个写法。

/// <summary>附属的物品。</summary>
public abstract class AddonItem : ModItem
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}

/// <summary>附属的方块。</summary>
public abstract class AddonTile : ModTile
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}

/// <summary>附属的方块实体。</summary>
public abstract class AddonTileEntity : ModTileEntity
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}

/// <summary>附属的聊天 / 控制台指令。</summary>
public abstract class AddonCommand : ModCommand
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}

/// <summary>附属的系统（世界数据、每帧逻辑、界面）。关着时它存进世界的数据由 tML 原样保管。</summary>
public abstract class AddonSystem : ModSystem
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}

/// <summary>附属的玩家数据。</summary>
public abstract class AddonPlayer : ModPlayer
{
    public abstract string AddonId { get; }

    public override bool IsLoadingEnabled(Mod mod) => AddonRegistry.IsEnabled(AddonId);
}
