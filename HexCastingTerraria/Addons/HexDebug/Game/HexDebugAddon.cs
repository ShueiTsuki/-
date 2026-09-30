using HexCastingTerraria.Config;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// HexDebug 附属的入口：调试杖逐步执行咒术、剪接台编辑咒术（object-Object，MIT）。
/// 功能 → 文件对照见同目录上一级的 addon.json，玩法与偏差见 README.md。
/// </summary>
public sealed class HexDebugAddon : HexAddon
{
    public override string Id => "hexdebug";

    public override string Name => "HexDebug";

    public override AddonSide Side => AddonSide.Both;

    public override bool IsEnabled => HexAddonsConfig.Instance.HexDebug;
}
