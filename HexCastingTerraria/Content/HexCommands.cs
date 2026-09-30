using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content;

/// <summary>
/// /hexcasting 指令。移植自源项目 common/command（HexCommands.register）：
///
/// | 原版 | 这里 |
/// |---|---|
/// | /hexcasting perWorldPatterns list | 同名：列出本世界大法术的笔顺 |
/// | /hexcasting perWorldPatterns give &lt;图案&gt; [玩家] | 同名：给一张那个大法术的远古卷轴（原版的大卷轴 + 图案 id） |
/// | /hexcasting perWorldPatterns giveAll [玩家] | 同名：本世界全部大法术的远古卷轴各一张 |
/// | /hexcasting recalcPatterns | 同名：按世界种子重新生成本世界的笔顺 |
/// | /hexcasting brainsweep &lt;目标&gt; | 同名；泰拉没有实体选择器：目标给 NPC 编号，不给就取离自己最近的生物 |
/// | textureToggle / textureRepaint | 不做：那是 MC 图案贴图缓存的调试开关，泰拉这边没有那套缓存 |
///
/// 用法：聊天栏输入 /hexcasting …（CommandType.World：单人在本地执行；联机时 tML 把这行发给服务器执行，
/// 结果回到聊天栏），或服务器控制台输入 hexcasting …（CommandType.Console）。同一套代码两边都能用。
///
/// 权限：原版要管理员 / 游戏管理员。泰拉没有权限系统，最接近的是「房主」（Main.countsAsHostForGameplay，
/// 「创建并游玩」开服的那名玩家，旅途模式的房主权限也看它）—— 单人随便用；联机时控制台和房主能用，
/// 其他玩家要服务端配置 PlayersCanUseCommands 打开。提示文字取原版官方中文（command.hexcasting.*）。
/// </summary>
public sealed class HexCommands : ModCommand
{
    public override CommandType Type => CommandType.World | CommandType.Console;

    public override string Command => "hexcasting";

    public override string Usage =>
        "/hexcasting perWorldPatterns list\n"
        + "/hexcasting perWorldPatterns give <图案> [玩家]\n"
        + "/hexcasting perWorldPatterns giveAll [玩家]\n"
        + "/hexcasting recalcPatterns\n"
        + "/hexcasting brainsweep [NPC编号]";

    public override string Description => "咒法学指令：大法术的笔顺与远古卷轴、重生成笔顺、剖念生物";

    private static readonly Color Ok = new(116, 179, 242);
    private static readonly Color Bad = new(255, 110, 110);

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (!Allowed(caller))
        {
            caller.Reply("联机时 /hexcasting 只有房主和服务器控制台能用（服务端配置「联机时所有玩家可用 /hexcasting 指令」可以放开）。", Bad);
            return;
        }

        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "perworldpatterns":
                PerWorld(caller, args.Skip(1).ToArray());
                break;
            case "recalcpatterns":
                Recalc(caller);
                break;
            case "brainsweep":
                Brainsweep(caller, args.Skip(1).ToArray());
                break;
            default:
                caller.Reply(Usage, Bad);
                break;
        }
    }

    /// <summary>单人：可以；联机：控制台、房主，或服务端配置放开了。</summary>
    private static bool Allowed(CommandCaller caller)
    {
        if (Main.netMode != NetmodeID.Server) return true;
        if (caller.CommandType == CommandType.Console) return true;
        if (caller.Player is { } p && p.whoAmI >= 0 && p.whoAmI < Main.countsAsHostForGameplay.Length
            && Main.countsAsHostForGameplay[p.whoAmI]) return true;
        return Config.HexServerConfig.Instance.PlayersCanUseCommands;
    }

    private static void PerWorld(CommandCaller caller, string[] args)
    {
        string op = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (op)
        {
            case "list":
            {
                // 原版：「此世界特有的图案：」+ 每行 id: 图案
                caller.Reply("此世界特有的图案：", Ok);
                foreach (var id in PatternRegistry.PerWorldIds.OrderBy(i => i, System.StringComparer.Ordinal))
                {
                    var def = PatternRegistry.FindById(id);
                    if (def is null) continue;
                    var p = PatternRegistry.PatternInThisWorld(def);
                    caller.Reply($"{id}（{def.DisplayName()}）: {p.StartDir} {p.AnglesSignature()}");
                }
                break;
            }
            case "give":
            {
                if (args.Length < 2) { caller.Reply("用法：/hexcasting perWorldPatterns give <图案> [玩家]", Bad); return; }
                var def = FindPerWorld(args[1]);
                if (def is null) { caller.Reply($"未知图案资源 {args[1]}", Bad); return; }
                var targets = Targets(caller, args.Length > 2 ? args[2] : null);
                if (targets is null) return;
                foreach (var player in targets) { GiveScroll(player, def.Id); }
                // 原版 specific.success：「给予%3$sID为%2$s的%1$s」
                caller.Reply($"给予{Who(targets)}ID为{def.Id}的{def.DisplayName()}之远古卷轴", Ok);
                break;
            }
            case "giveall":
            {
                var targets = Targets(caller, args.Length > 1 ? args[1] : null);
                if (targets is null) return;
                int count = 0;
                foreach (var player in targets)
                {
                    foreach (var id in PatternRegistry.PerWorldIds)
                    {
                        GiveScroll(player, id);
                        count++;
                    }
                }
                // 原版 pats.all：「给予%2$s所有%1$d张卷轴」
                caller.Reply($"给予{Who(targets)}所有{count}张卷轴", Ok);
                break;
            }
            default:
                caller.Reply("用法：/hexcasting perWorldPatterns list | give <图案> [玩家] | giveAll [玩家]", Bad);
                break;
        }
    }

    /// <summary>原版 RecalcPatternsCommand：按世界种子从头生成（ScrungledPatternsSave.createFromScratch(seed)）。</summary>
    private static void Recalc(CommandCaller caller)
    {
        PatternRegistry.SetPerWorld(PatternRegistry.GeneratePerWorld(Main.ActiveWorldFileData.Seed));
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.WorldData);   // 笔顺表随世界数据同步（PerWorldPatternSystem.NetSend）
        }
        caller.Reply("已重生成此世界特有图案", Ok);
    }

    /// <summary>原版 BrainsweepCommand：直接让目标失去意识（不需要配方、不产出方块）。</summary>
    private static void Brainsweep(CommandCaller caller, string[] args)
    {
        NPC? npc = null;
        if (args.Length > 0)
        {
            if (int.TryParse(args[0], out int idx) && idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].active) npc = Main.npc[idx];
            else { caller.Reply($"{args[0]}不是生物", Bad); return; }
        }
        else if (caller.Player is { } me)
        {
            float best = 50 * 16f;
            foreach (var n in Main.npc)
            {
                if (!n.active) continue;
                float d = Vector2.Distance(n.Center, me.Center);
                if (d < best) { best = d; npc = n; }
            }
            if (npc is null) { caller.Reply("附近 50 格内没有生物（也可以写 NPC 编号）", Bad); return; }
        }
        else
        {
            caller.Reply("控制台里要写 NPC 编号：/hexcasting brainsweep <NPC编号>", Bad);
            return;
        }

        var g = npc.GetGlobalNPC<HexGlobalNPC>();
        if (g.Brainswept)
        {
            caller.Reply($"{npc.GivenOrTypeName}早已没有了意识", Bad);
            return;
        }
        HexGlobalNPC.MakeBrainswept(npc);
        caller.Reply($"已清除{npc.GivenOrTypeName}的意识", Ok);
    }

    private static PatternDef? FindPerWorld(string name)
    {
        foreach (var id in PatternRegistry.PerWorldIds)
        {
            var def = PatternRegistry.FindById(id);
            if (def is null) continue;
            if (id == name || id == "hexcasting:" + name || def.DisplayName() == name) return def;
        }
        return null;
    }

    /// <summary>给谁：写了名字就找那个玩家；没写就是自己（原版 getDefaultTarget）。</summary>
    private static List<Player>? Targets(CommandCaller caller, string? name)
    {
        if (name is not null)
        {
            var found = Main.player.Where(p => p.active && string.Equals(p.name, name, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0) { caller.Reply($"找不到玩家 {name}", Bad); return null; }
            return found;
        }
        if (caller.Player is { active: true } me) return new List<Player> { me };
        caller.Reply("控制台里要写玩家名", Bad);
        return null;
    }

    private static string Who(List<Player> targets) => targets.Count == 1 ? targets[0].name : $"{targets.Count} 名玩家";

    /// <summary>原版给的是「大卷轴 + 图案 id」，也就是远古卷轴；泰拉侧就是 <see cref="Items.AncientScroll"/>。</summary>
    private static void GiveScroll(Player player, string id)
    {
        var item = new Item(ModContent.ItemType<Items.AncientScroll>());
        ((Items.AncientScroll)item.ModItem).SetOp(id);
        player.QuickSpawnItem(player.GetSource_Misc("HexCommand"), item);
    }
}
