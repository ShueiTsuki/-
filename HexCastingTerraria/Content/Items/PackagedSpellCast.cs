using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 打包法术专用环境：媒质**从物品自己的池子里扣**，不是从玩家身上。
/// 移植自源项目 `ItemPackagedHex` 里的 `PackagedHexCastEnv`。
///
/// 为什么不能复用玩家环境：源项目里打包法术的意义就是「提前把媒质灌进去」——
/// 如果还是从玩家扣，那这件东西就只是一张「图案备忘录」，
/// 而它本来可以给一个没有媒质的玩家用（比如送人）。
///
/// 法器例外：它自己的池子空了之后可以继续从背包扣（`canDrawMediaFromInventory`）。
/// 这不是偷懒，是源项目里区分「法器」与「饰品」的那一条。
/// </summary>
public sealed class PackagedSpellEnvironment : PlayerCastingEnvironment
{
    private readonly ItemPackagedSpell _storage;

    public PackagedSpellEnvironment(Player player, ItemPackagedSpell storage) : base(player)
    {
        _storage = storage;
        CanFallBackToPlayer = storage.CanDrawFromInventory;
    }

    /// <summary>池子不够时能不能退回玩家身上（只有法器）。</summary>
    public bool CanFallBackToPlayer { get; }

    protected override long ExtractMediaEnvironment(long cost, bool simulate)
    {
        if (cost <= 0) return 0;

        long fromItem = System.Math.Min(cost, _storage.Media);
        long remaining = cost - fromItem;

        if (remaining > 0 && !CanFallBackToPlayer)
        {
            // 不够就是不够 —— 而且**一点都不能扣**，否则玩家会白亏一部分媒质
            return cost;
        }

        if (simulate)
        {
            if (remaining <= 0) return 0;

            // 退回到玩家侧试算（法器）：超出的部分由父类判断
            return base.ExtractMediaEnvironment(remaining, simulate: true);
        }

        if (fromItem > 0)
        {
            _storage.Spend(fromItem);
        }

        if (remaining <= 0)
        {
            return 0;
        }

        return base.ExtractMediaEnvironment(remaining, simulate: false);
    }
}

/// <summary>
/// 打包法术的施放入口。
///
/// 单人：直接权威执行（客户端就是权威）。
/// 联机：把「第几个物品栏槽位」发给服务端，由服务端执行 ——
/// 与法杖施法同一条思路（见 `ServerCastState` 的注释）。
/// </summary>
internal static class PackagedSpellCast
{
    public static void Request(Player player, Item item)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Net.HexNetSync.RequestCastPackaged(player.selectedItem);
            return;
        }

        Cast(player, item);
    }

    /// <summary>真正执行。服务端与单机都走这里。</summary>
    public static void Cast(Player player, Item item)
    {
        if (item.ModItem is not ItemPackagedSpell storage || storage.IsEmpty) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        var env = new PackagedSpellEnvironment(player, storage);

        // 冷却：源项目用 MC 的 item cooldown，泰拉这边手动记一个 tick
        if (player.GetModPlayer<HexPlayer>().PackagedCooldown > 0) return;

        var vm = CastingVM.Empty(env);
        var image = vm.Image;

        var queue = new List<Iota>(storage.Patterns.Count);
        foreach (var pattern in storage.Patterns)
        {
            queue.Add(new PatternIota(pattern));
        }

        if (queue.Count == 0) return;

        var outcome = vm.QueueExecute(image, queue);

        // 粒子：这里跑在服务端（或单机），服务端要广播给附近玩家
        SpellVisuals.Broadcast(outcome.Particles, player.Center.X, player.Center.Y);

        // 符纸：用完就消失。源项目是 `breakAfterDepletion() && getMedia == 0`，
        // 即「池子空了」才算用完 —— 而不是「放了一次就消失」。
        if (storage.BreakAfterDepletion && storage.Media == 0)
        {
            item.TurnToAir();
        }

        HexPlayer.Get(player).StartPackagedCooldown(storage.CooldownTicks);

        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.SyncPlayer, -1, -1, null, player.whoAmI);
        }
    }
}
