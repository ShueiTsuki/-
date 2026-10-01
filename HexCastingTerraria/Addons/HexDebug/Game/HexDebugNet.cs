using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Content.Net;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// HexDebug 的联机消息。调试在服务端（单机就是本地）跑，客户端只发请求、收调试面板要显示的东西。
/// 单机也走同一套读写（写进内存再读出来），这样单机就能把联机的编码一起测到。
/// 上游对应：MsgDebuggerStateS2C、MsgEvaluatorStateS2C、MsgEvaluatorClientInfoS2C、MsgPrintDebuggerStatusS2C、
/// 以及运行杖改走调试会话的 MsgNewSpellPatternC2S；其余上游走 DAP 的信息（栈、源码、调用栈）合成 View。
/// </summary>
internal static class HexDebugNet
{
    public enum Msg : byte
    {
        // 客户端 → 服务端
        UseDebugger = 0,
        EvalOpen = 1,
        EvalPattern = 2,
        ToggleBreakpoint = 3,

        // 剪接台（客户端 → 服务端）
        SpliceSetSlot = 4,
        SpliceAction = 5,
        SpliceSelect = 6,
        SpliceDraw = 7,
        SpliceCast = 8,

        // 服务端 → 客户端
        View = 10,
        RemoveThread = 11,
        Output = 12,
        Status = 13,
        EvalOpened = 14,
        EvalResult = 15,

        // 剪接台（服务端 → 客户端）：画的图案上什么色
        SpliceDrawResult = 16,
    }

    private static HexAddon Addon => AddonRegistry.All.First(x => x.Id == "hexdebug");

    private static bool IsSinglePlayer => Main.netMode == NetmodeID.SinglePlayer;

    // ==================== 发送 ====================

    /// <summary>客户端 → 服务端。单机直接交给本地的服务端处理。</summary>
    public static void ToServer(Msg msg, Action<BinaryWriter> body)
    {
        if (IsSinglePlayer)
        {
            Loopback(msg, body, server: true, Main.myPlayer);
            return;
        }
        var p = Addon.GetPacket();
        p.Write((byte)msg);
        body(p);
        p.Send();
    }

    /// <summary>服务端 → 某个客户端。单机直接交给本地客户端。</summary>
    public static void ToClient(int who, Msg msg, Action<BinaryWriter> body)
    {
        if (IsSinglePlayer)
        {
            Loopback(msg, body, server: false, who);
            return;
        }
        if (Main.netMode != NetmodeID.Server) return;
        var p = Addon.GetPacket();
        p.Write((byte)msg);
        body(p);
        p.Send(who);
    }

    private static void Loopback(Msg msg, Action<BinaryWriter> body, bool server, int who)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) body(w);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        if (server) HandleServer(msg, r, who);
        else HandleClient(msg, r);
    }

    // ==================== 接收 ====================

    public static void Handle(BinaryReader r, int whoAmI)
    {
        var msg = (Msg)r.ReadByte();
        if (Main.netMode == NetmodeID.Server) HandleServer(msg, r, whoAmI);
        else HandleClient(msg, r);
    }

    private static void HandleServer(Msg msg, BinaryReader r, int who)
    {
        var player = Main.player[who];
        if (player is not { active: true }) return;
        switch (msg)
        {
            case Msg.UseDebugger:
                HexDebugSessions.UseDebugger(player, r.ReadByte());
                break;
            case Msg.EvalOpen:
                HexDebugSessions.OpenEvaluator(player, r.ReadByte(), r.ReadBoolean());
                break;
            case Msg.EvalPattern:
            {
                int thread = r.ReadByte();
                if (IotaWire.Read(r) is PatternIota p) HexDebugSessions.Evaluate(player, thread, p.Pattern);
                break;
            }
            case Msg.ToggleBreakpoint:
                HexDebugSessions.ToggleBreakpoint(player, r.ReadInt32(), r.ReadInt32());
                break;
            case Msg.SpliceSetSlot:
            case Msg.SpliceAction:
            case Msg.SpliceSelect:
            case Msg.SpliceDraw:
            case Msg.SpliceCast:
                Splicing.SplicingTableNet.HandleServer(msg, r, player);
                break;
        }
    }

    private static void HandleClient(Msg msg, BinaryReader r)
    {
        switch (msg)
        {
            case Msg.View:
                HexDebugClient.SetView(HexDebugView.Read(r));
                break;
            case Msg.RemoveThread:
                HexDebugClient.RemoveThread(r.ReadByte());
                break;
            case Msg.Output:
                HexDebugClient.AddOutput(r.ReadByte(), r.ReadString(), (OutputCategory)r.ReadByte());
                break;
            case Msg.Status:
                HexDebugClient.ShowStatus(r.ReadString());
                break;
            case Msg.EvalOpened:
            {
                int thread = r.ReadByte();
                bool reset = r.ReadBoolean();
                HexDebugClient.OpenEvaluator(thread, reset, ReadLines(r));
                break;
            }
            case Msg.SpliceDrawResult:
                Splicing.SplicingTableUI.DrawResult((ResolvedPatternType)r.ReadByte());
                break;
            case Msg.EvalResult:
            {
                int thread = r.ReadByte();
                var type = (ResolvedPatternType)r.ReadByte();
                bool clear = r.ReadBoolean();
                HexDebugClient.EvaluatorResult(thread, type, clear, ReadLines(r));
                break;
            }
        }
    }

    public static void WriteLines(BinaryWriter w, IReadOnlyList<string> lines)
    {
        w.Write((ushort)lines.Count);
        foreach (var s in lines) w.Write(s);
    }

    private static List<string> ReadLines(BinaryReader r)
    {
        int n = r.ReadUInt16();
        var list = new List<string>(n);
        for (int i = 0; i < n; i++) list.Add(r.ReadString());
        return list;
    }
}
