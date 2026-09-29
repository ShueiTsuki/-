using System.Collections.Generic;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 客户端侧的 VM 状态。
///
/// 关键设计（对齐原作）：**已画图案的求值是增量的**——
/// 每画完一条图案就把它送进 VM 求值一次，栈在图案之间持续累积。
/// 这正是「先读一次 = 快照、后面复用」能成立的原因（见 LOOK_DIRECTION_DESIGN.md）。
/// </summary>
public static class HexVmState
{
    private static CastingVM? _vm;
    private static PlayerCastingEnvironment? _env;

    /// <summary>
    /// 联机时由服务端同步过来的栈。
    ///
    /// 联机下客户端**不做权威求值** —— 只上报「我画了什么」，
    /// 服务端算完把栈发回来。所以 HUD 显示的是这一份，而不是本地 VM 的。
    /// </summary>
    private static List<Iota> _syncedStack = new();

    /// <summary>是否处于「服务端权威」模式（联机客户端）。</summary>
    private static bool ServerAuthoritative => Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient;

    /// <summary>当前 VM 持有的栈（只读快照，供 HUD 显示）。</summary>
    public static IReadOnlyList<Iota> Stack
        => ServerAuthoritative ? _syncedStack : (_vm?.Image.Stack ?? System.Array.Empty<Iota>());

    /// <summary>最近一次求值的解析状态。</summary>
    public static ResolvedPatternType LastResolution { get; private set; } = ResolvedPatternType.Unresolved;

    /// <summary>最近一次求值的错误消息（若有）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>已消耗算力。</summary>
    public static long OpsConsumed => _vm?.Image.OpsConsumed ?? 0;

    /// <summary>括号层数与转义标记（供 HUD 显示列表构建状态）。</summary>
    public static int ParenCount => _vm?.Image.ParenCount ?? 0;
    public static bool EscapeNext => _vm?.Image.EscapeNext ?? false;

    /// <summary>
    /// 栈是否已清空（空栈 + 无括号 + 无待转义）。
    /// 对应源项目 ExecutionClientView.isStackClear ——
    /// 为真时原作会**自动关闭画布**，表示这一轮施法已结算完毕。
    /// </summary>
    public static bool IsStackClear => _vm?.Image.IsStackClear() ?? true;

    /// <summary>清空 VM（潜行右键重开时调用）。</summary>
    public static void Reset()
    {
        _vm = null;
        _env = null;
        _syncedStack = new List<Iota>();
        LastResolution = ResolvedPatternType.Unresolved;
        LastError = null;

        // 联机时也要告诉服务端清空 —— 否则服务端那份栈会一直留着，
        // 玩家「重开」后画的第一条图案会接在旧栈上，表现成「清了但没清干净」。
        if (ServerAuthoritative)
        {
            SendReset();
        }
    }

    /// <summary>把「清空」上报服务端。</summary>
    private static void SendReset()
    {
        try
        {
            var packet = HexCastingTerraria.Instance?.GetPacket();
            if (packet == null) return;
            packet.Write((byte)Content.Net.HexMessage.ResetCast);
            packet.Send();
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[HexCasting] 发送清空包失败：{e.Message}");
        }
    }

    /// <summary>把一条图案上报服务端（联机客户端用）。</summary>
    private static void SendPattern(Core.Casting.Math.HexPattern pattern, Player player)
    {
        try
        {
            var packet = HexCastingTerraria.Instance?.GetPacket();
            if (packet == null) return;

            packet.Write((byte)Content.Net.HexMessage.CastPattern);
            Content.Net.IotaWire.WritePattern(packet, pattern);

            // 瞄准方向随包一起发：服务端拿不到客户端的鼠标。
            // 用打开画布时冻结的那个方向（见 LOOK_DIRECTION_DESIGN.md 第八节）。
            var aim = Content.HexPlayer.Get(player).FrozenAim ?? new Microsoft.Xna.Framework.Vector2(1f, 0f);
            packet.Write(aim.X);
            packet.Write(aim.Y);

            packet.Send();
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[HexCasting] 发送图案包失败：{e.Message}");
            LastResolution = ResolvedPatternType.Errored;
            LastError = "无法与服务器通信";
        }
    }

    /// <summary>
    /// 接收服务端同步回来的栈。由 <c>HexCastingTerraria.HandlePacket</c> 调用。
    /// </summary>
    public static void ApplySyncedStack(System.IO.BinaryReader reader)
    {
        var resolution = (ResolvedPatternType)reader.ReadByte();
        int count = reader.ReadUInt16();

        var stack = new List<Iota>(count);
        for (int i = 0; i < count; i++)
        {
            stack.Add(Content.Net.IotaWire.Read(reader));
        }

        _syncedStack = stack;
        LastResolution = resolution;
        LastError = resolution switch
        {
            ResolvedPatternType.Invalid => "图案无效或行为未实现",
            ResolvedPatternType.Errored => "执行出错（参数不足／类型不符／媒质不足等）",
            _ => null,
        };
    }

    private static CastingVM EnsureVm(Player player)
    {
        if (_vm == null || _env == null || _env.Player != player)
        {
            _env = new PlayerCastingEnvironment(player);
            _vm = CastingVM.Empty(_env);
        }
        return _vm;
    }

    /// <summary>
    /// 对一条刚画完的图案执行求值。
    /// 对应原作的 MsgNewSpellPatternC2S —— 每画一条就求值一次，而不是攒着一起算。
    /// </summary>
    public static void EvaluatePattern(Player player, Core.Casting.Math.HexPattern pattern)
    {
        var vm = EnsureVm(player);
        var iota = new PatternIota(pattern);

        // === 诊断：把「画出的签名 / 匹配结果 / 注册表规模」写进日志 ===
        // 用于定位「游戏里所有图案都判无效」这类问题（离线测试通过但游戏内失败）。
        var diagDef = Core.Registry.PatternRegistry.Match(pattern);
        bool diagHasAction = diagDef != null && Core.Registry.PatternRegistry.HasAction(diagDef);
        try
        {
            HexCastingTerraria.Instance?.Logger.Info(
                $"[HexCasting/诊断] 签名='{pattern.AnglesSignature()}' 起始={pattern.StartDir} "
                + $"长度={pattern.Length} 匹配={(diagDef?.Id ?? "null")} 有行为={diagHasAction} "
                + $"注册图案数={Core.Registry.PatternRegistry.Count} 已实现行为数={Core.Registry.PatternRegistry.RegisteredActionCount}");
        }
        catch { /* 诊断日志失败不影响求值 */ }

        try
        {
            // 联机客户端：**不做本地求值**，只上报，等服务端回栈。
            // 本地也算一遍会与服务端结果分叉（媒质、实体 id、范围判定都不同），
            // 表现成「我这边显示成功但服务端说媒质不够」这类灵异现象。
            if (ServerAuthoritative)
            {
                SendPattern(pattern, player);
                return;
            }

            var outcome = vm.QueueExecute(vm.Image, new Iota[] { iota });
            LastResolution = outcome.ResolutionType;

            // 音效与粒子：单人在本地直接出
            {
                var localPlayer = Terraria.Main.LocalPlayer;
                if (localPlayer is { active: true })
                {
                    Content.SpellSounds.EmitEval(outcome.Sound, localPlayer.Center);
                    Content.SpellVisuals.SpawnLocal(outcome.Particles, HexPigment.Current);
                }
            }
            LastError = outcome.ResolutionType switch
            {
                ResolvedPatternType.Invalid => "图案无效或行为未实现",
                ResolvedPatternType.Errored => "执行出错（参数不足／类型不符／算力耗尽等）",
                _ => null,
            };
        }
        catch (System.Exception e)
        {
            // VM 内部不应抛异常（都该转成 mishap）；这里兜底以防漏网
            LastResolution = ResolvedPatternType.Errored;
            LastError = e.Message;
        }
    }

    /// <summary>把 iota 渲染成 HUD 用的短文本。</summary>
    public static string Describe(Iota iota) => iota switch
    {
        NullIota => "null",
        BooleanIota b => b.Value ? "true" : "false",
        DoubleIota d => d.Value.ToString("0.####"),
        VectorIota v => $"vec({v.X:0.##}, {v.Y:0.##})",
        PatternIota p => "pattern",
        ListIota l => $"list[{l.Count}]",
        GarbageIota => "garbage",
        ContinuationIota => "跳转目标",
        _ => iota.TypeName,
    };
}
