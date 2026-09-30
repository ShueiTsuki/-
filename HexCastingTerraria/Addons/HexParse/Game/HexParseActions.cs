using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// HexParse 里要碰游戏的三个图案（上游 actions/ActionCode2Focus.kt、ActionFocus2Code.kt、ActionLearnGreatPatterns.kt）。
/// 施法在服务端跑（联机）/ 本地跑（单人）；剪贴板和显示在施法者客户端，所以联机时发消息过去。
/// </summary>
public static class HexParseActions
{
    /// <summary>上游 `env !is StaffCastEnv`：只能用法杖施放（打包的法术、法术环都不行）。</summary>
    private static Player RequireStaff(CastingEnvironment env)
        => env is PlayerCastingEnvironment p and not PackagedSpellEnvironment ? p.Player : throw new MishapDisallowedSpell();

    /// <summary>
    /// 上游 ActionLearnGreatPatterns：从两只手的物品（先另一只手、再施法的手）里找大法术的图案 ——
    /// 能存 iota 的读出来那一个、装着咒术的读整段 —— 递归进列表；是本世界的大法术就解锁，返回这次新学会的。
    /// </summary>
    public static List<Iota> LearnFromHeld(Player player)
    {
        var learned = new List<PatternIota>();
        foreach (int slot in new PlayerCastingEnvironment(player).PrimarySlots())
        {
            if (slot < 0 || slot >= player.inventory.Length || player.inventory[slot] is not { IsAir: false } item) continue;
            IEnumerable<Iota>? targets = item.ModItem switch
            {
                ItemIotaStorage s when s.Read() is { } inner => new[] { inner },
                ItemPackagedSpell spell => spell.Program,
                _ => null,
            };
            if (targets != null) Process(targets, learned);
        }
        if (learned.Count > 0) HexParseWorld.Sync();
        return learned.Cast<Iota>().ToList();
    }

    private static void Process(IEnumerable<Iota> seq, List<PatternIota> output)
    {
        foreach (var iota in seq)
        {
            if (iota is ListIota list) { Process(list.Items, output); continue; }
            if (iota is not PatternIota p) continue;
            var (kind, def) = PatternRegistry.MatchPattern(p.Pattern);
            if (kind != PatternMatchKind.PerWorld || def == null) continue;
            if (output.Any(o => o.ValueEquals(p))) continue;
            if (HexParseWorld.Unlock(def.Id)) output.Add(p);
        }
    }

    /// <summary>解码之策略：读施法者的剪贴板，解析进他手上的核心（只能法杖）。</summary>
    public sealed class Code2Focus : ConstMediaAction
    {
        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var player = RequireStaff(env);
            if (Main.netMode == NetmodeID.Server) HexParseNet.SendPullClipboard(player.whoAmI, ClipboardMode.Default, null);
            else if (player.whoAmI == Main.myPlayer) HexParseIO.HandleClipboard(player, ClipboardMode.Default, null);
            return System.Array.Empty<Iota>();
        }
    }

    /// <summary>编码之策略：把施法者手上核心的内容写成代码显示出来（只能法杖）。</summary>
    public sealed class Focus2Code : ConstMediaAction
    {
        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var player = RequireStaff(env);
            string? code = HexParseIO.ReadHeld(player, false, Core.IotaWriter.ReadDefault);
            if (code != null)
            {
                if (Main.netMode == NetmodeID.Server) HexParseNet.SendDisplayCode(player.whoAmI, code);
                else if (player.whoAmI == Main.myPlayer) HexParseIO.DisplayCode(code);
            }
            return System.Array.Empty<Iota>();
        }
    }

    /// <summary>内化卓越法术：学手上物品里的大法术，返回这次新学会的列表。施法者得是玩家。</summary>
    public sealed class LearnGreatPatterns : ConstMediaAction
    {
        public override int Argc => 0;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            if (env is not PlayerCastingEnvironment p) throw new MishapDisallowedSpell();
            return new Iota[] { new ListIota(LearnFromHeld(p.Player)) };
        }
    }
}
