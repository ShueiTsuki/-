using HexCastingTerraria.Config;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// HexParse 附属的入口：代码文本与 iota 列表互转、/hexParse 指令（YukkuriC，MIT）。
/// 功能 → 文件对照见同目录上一级的 addon.json，玩法与偏差见 README.md。
/// </summary>
public sealed class HexParseAddon : HexAddon
{
    public override string Id => "hexparse";

    public override string Name => "HexParse";

    public override AddonSide Side => AddonSide.Both;

    public override bool IsEnabled => HexAddonsConfig.Instance.HexParse;
}
