using HexCastingTerraria.Config;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 附属的入口：施法界面的无障碍操作：键盘画图、按名字搜索图案、别名、悬停说明（Ruby / tizu，JSON License）。
/// 功能 → 文件对照见同目录上一级的 addon.json，玩法与偏差见 README.md。
/// </summary>
public sealed class HexcessibleAddon : HexAddon
{
    public override string Id => "hexcessible";

    public override string Name => "Hexcessible";

    public override AddonSide Side => AddonSide.Client;

    public override bool IsEnabled => HexAddonsClientConfig.Instance.Hexcessible;
}
