using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Content.Net;

/// <summary>
/// **服务端**的施法状态：每个玩家一份 VM。
///
/// ## 为什么必须放在服务端
///
/// 单机时求值在客户端跑没问题（客户端就是权威）。
/// 但联机时必须由服务端权威求值，否则：
///   - 每个客户端各算各的，结果会不一样
///   - 客户端可以改内存伪造媒质、伪造实体 id、伪造传送
///
/// 所以联机流程是：
/// ```
/// 客户端画完一条图案 → CastPattern 包（图案 + 瞄准方向）
///     → 服务端取出该玩家的 VM 求值（权威）
///         → StackSync 包回给该玩家（HUD 显示）
///         → SpellVisual 包广播（瞄准点粒子）
/// ```
///
/// 瞄准方向**随包一起发**：服务端拿不到客户端的鼠标，
/// 而瞄准在打开画布时已冻结（见 LOOK_DIRECTION_DESIGN.md 第八节），
/// 所以客户端只需在第一条图案时带一次。
/// </summary>
internal static class ServerCastState
{
    private static readonly Dictionary<int, CastingVM> Vms = new();
    private static readonly Dictionary<int, PlayerCastingEnvironment> Envs = new();

    /// <summary>取（或建立）某个玩家的服务端 VM。</summary>
    public static CastingVM GetVm(Player player)
    {
        int who = player.whoAmI;
        if (Vms.TryGetValue(who, out var vm) && Envs.TryGetValue(who, out var env)
            && env.Player == player)
        {
            return vm;
        }

        env = new PlayerCastingEnvironment(player);
        vm = CastingVM.Empty(env);
        Vms[who] = vm;
        Envs[who] = env;
        return vm;
    }

    /// <summary>更新某个玩家的 VM（求值后必须写回，否则状态丢失）。</summary>
    public static void SetVm(Player player, CastingVM vm) => Vms[player.whoAmI] = vm;

    /// <summary>取该玩家的服务端施法环境（世界图案要用它）。</summary>
    public static PlayerCastingEnvironment GetEnv(Player player)
    {
        GetVm(player);
        return Envs[player.whoAmI];
    }

    /// <summary>清空某个玩家的施法状态。</summary>
    public static void Reset(Player player)
    {
        int who = player.whoAmI;
        Vms.Remove(who);
        Envs.Remove(who);
    }

    /// <summary>
    /// 玩家断线/离开时清理。
    ///
    /// 不清理的话，索引会被下一个进服的玩家复用 ——
    /// 新玩家会**继承上一个玩家的栈**，而且完全无声。
    /// </summary>
    public static void Clear(int whoAmI)
    {
        Vms.Remove(whoAmI);
        Envs.Remove(whoAmI);
    }

    public static void ClearAll()
    {
        Vms.Clear();
        Envs.Clear();
    }

    /// <summary>
    /// 服务端权威求值一条图案。返回求值结果供发包。
    /// </summary>
    public static (IReadOnlyList<Iota> Stack, ResolvedPatternType Resolution) EvaluatePattern(
        Player player, Core.Casting.Math.HexPattern pattern)
    {
        var vm = GetVm(player);

        CastOutcome outcome;
        try
        {
            outcome = vm.QueueExecute(vm.Image, new Iota[] { new PatternIota(pattern) });
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[HexCasting] 服务端求值异常：{e.Message}");
            return (vm.Image.Stack, ResolvedPatternType.Errored);
        }

        // 粒子与音效：求值在服务端跑，服务端的表现客户端看不到/听不到 —— 都广播出去
        SpellVisuals.Broadcast(outcome.Particles, player);
        SpellSounds.EmitEval(outcome.Sound, player.Center);

        SetVm(player, vm);
        return (outcome.Image.Stack, outcome.ResolutionType);
    }
}
