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
            // 上游 OpMakePackagedSpell(调试杖, 10 * CRYSTAL_UNIT)：手上要有空的那种调试杖
            ["hexdebug:craft/debugger"] = new global::HexCastingTerraria.Core.Casting.Actions.OpMakePackagedSpell(
                "hexdebug:debugger", 10 * global::HexCastingTerraria.Core.Media.MediaConstants.CrystalUnit, "一根空的调试杖"),
            ["hexdebug:craft/quenched_debugger"] = new global::HexCastingTerraria.Core.Casting.Actions.OpMakePackagedSpell(
                "hexdebug:quenched_debugger", 10 * global::HexCastingTerraria.Core.Media.MediaConstants.CrystalUnit, "一根空的淬灵调试杖"),
        };
        foreach (var (id, action) in actions) PatternRegistry.RegisterAction(id, action);
        foreach (var (id, action) in Splicing.SplicingPatterns.All()) PatternRegistry.RegisterAction(id, action);

        // 上游 brainsweep/enlightened_splicing_table：剪接台 + 3 级工具匠（泰拉：哥布林工匠，同本体促动石的映射），1000000 媒质
        _enlighten = new global::HexCastingTerraria.Core.Casting.Actions.BrainsweepRecipe(
            ModContent.TileType<Splicing.SplicingTableTile>(),
            global::HexCastingTerraria.Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(Terraria.ID.NPCID.GoblinTinkerer),
            ModContent.TileType<Splicing.EnlightenedSplicingTableTile>(), -1,
            10 * global::HexCastingTerraria.Core.Media.MediaConstants.CrystalUnit);
        global::HexCastingTerraria.Core.Casting.Actions.BrainsweepRules.Add(_enlighten.Value);
    }

    private global::HexCastingTerraria.Core.Casting.Actions.BrainsweepRecipe? _enlighten;

    public override IEnumerable<int> DevKitItems => new[]
    {
        ModContent.ItemType<Debugger>(),
        ModContent.ItemType<Evaluator>(),
        ModContent.ItemType<Splicing.SplicingTableItem>(),
        ModContent.ItemType<Splicing.FocusHolderItem>(),
    };

    public override void AddBookContent(global::HexCastingTerraria.Core.Ui.BookDocument book) => HexDebugBook.AddTo(book);

    public override void HandlePacket(System.IO.BinaryReader reader, int whoAmI) => HexDebugNet.Handle(reader, whoAmI);

    public override void OnUnload()
    {
        if (_enlighten is { } recipe) global::HexCastingTerraria.Core.Casting.Actions.BrainsweepRules.Remove(recipe);
        _enlighten = null;
        HexDebugSessions.Clear();
        HexDebugClient.Clear();
        IotaSerializer.UnregisterKind(CognitohazardIota.KindTag);
        foreach (var id in HexDebugPatterns.Names.Keys) PatternDisplay.UnregisterAddonName(id);
    }
}
