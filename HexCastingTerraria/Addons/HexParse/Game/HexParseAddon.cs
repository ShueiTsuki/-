using System.Collections.Generic;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Config;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;
using Terraria.ModLoader;

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

    public override IEnumerable<PatternData> Patterns => HexParsePatterns.All;

    public override void OnLoad(Mod mod)
    {
        IotaSerializer.RegisterKind(CommentIota.KindTag, CommentIota.Read);
        foreach (var (id, name) in HexParsePatterns.Names) PatternDisplay.RegisterAddonName(id, name);

        var actions = new Dictionary<string, IAction>
        {
            ["hexparse:code2focus"] = new HexParseActions.Code2Focus(),
            ["hexparse:focus2code"] = new HexParseActions.Focus2Code(),
            ["hexparse:remove_comments"] = new ActionRemoveComments(),
            ["hexparse:learn_patterns"] = new HexParseActions.LearnGreatPatterns(),
            ["hexparse:create_linebreak"] = new ActionCreateLineBreak(),
            ["hexparse:donate"] = new ActionDonate(),
            // 上游没装 MoreIotas（字符串 iota）时这两个就是空动作；泰拉侧没有 MoreIotas
            ["hexparse:compile"] = ActionNull.Instance,
            ["hexparse:switch_comment"] = ActionNull.Instance,
        };
        foreach (var (id, action) in actions) PatternRegistry.RegisterAction(id, action);

        HexAddonsConfig.Instance.HexParseOptions.Apply();
        IotaDisplay.Decorators.Add(Display);
    }

    /// <summary>嵌套列表 / 括号彩色显示、注释旁不加逗号（上游 mixin/iota/*）。</summary>
    private static readonly NestedDisplay Display = new();

    public override void OnUnload()
    {
        IotaSerializer.UnregisterKind(CommentIota.KindTag);
        foreach (var id in HexParsePatterns.Names.Keys) PatternDisplay.UnregisterAddonName(id);
        IotaDisplay.Decorators.Remove(Display);
    }

    public override void HandlePacket(System.IO.BinaryReader reader, int whoAmI) => HexParseNet.Handle(reader, whoAmI);

    public override void AddBookContent(BookDocument book) => HexParseBook.AddTo(book);
}
