using HexCastingTerraria.Client;
using HexCastingTerraria.Config;
using Terraria;
using Terraria.ModLoader;

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

    private HexcessibleCanvas? _canvas;

    /// <summary>
    /// 纯客户端附属：开关随时能改，所以不论开没开都登记画布扩展（专用服务器上不登记），
    /// 扩展自己每帧读开关（<see cref="HexcessibleCanvas.Enabled"/>）。
    /// </summary>
    public override void OnLoad(Mod mod)
    {
        if (Main.dedServ) return;
        HexAddonsClientConfig.Instance?.HexcessibleOptions.Apply();
        _canvas = new HexcessibleCanvas();
        CanvasExtensions.All.Add(_canvas);
    }

    public override void OnUnload()
    {
        if (_canvas is not null) CanvasExtensions.All.Remove(_canvas);
        _canvas = null;
        HexcessibleIndex.Reset();
    }
}
