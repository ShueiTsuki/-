using System.Collections.Generic;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Config;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;
using Terraria.ModLoader;

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

    public override IEnumerable<PatternData> Patterns => HexDebugPatterns.All;

    public override void OnLoad(Mod mod)
    {
        IotaSerializer.RegisterKind(CognitohazardIota.KindTag, CognitohazardIota.Read);
        foreach (var (id, name) in HexDebugPatterns.Names) PatternDisplay.RegisterAddonName(id, name);

        // 剪接台的 16 个图案随剪接台一起实现
        var actions = new Dictionary<string, IAction>
        {
            ["hexdebug:const/cognitohazard"] = new OpCognitohazard(),
            ["hexdebug:const/debugging"] = new OpIsDebugging(),
            ["hexdebug:breakpoint/before"] = new OpBreakpoint(true),
            ["hexdebug:breakpoint/after"] = new OpBreakpoint(false),
        };
        foreach (var (id, action) in actions) PatternRegistry.RegisterAction(id, action);
    }

    public override void OnUnload()
    {
        IotaSerializer.UnregisterKind(CognitohazardIota.KindTag);
        foreach (var id in HexDebugPatterns.Names.Keys) PatternDisplay.UnregisterAddonName(id);
    }
}
