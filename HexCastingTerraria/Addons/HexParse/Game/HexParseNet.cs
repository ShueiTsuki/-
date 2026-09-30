using System.IO;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// HexParse 的联机消息（走本体的「附属消息」，见 HexAddon.GetPacket）。上游 network/*：
/// - 服务端 -> 客户端：世界表（解锁 / 短名）、拉剪贴板（解码之策略）、显示代码（编码之策略）、消息
/// - 客户端 -> 服务端：压思维栈、改世界表（unlock_great / conflict set，只许房主）、分享（广播）
/// </summary>
public static class HexParseNet
{
    public enum Msg : byte
    {
        WorldState = 0,
        PullClipboard = 1,
        DisplayCode = 2,
        Message = 3,
        PushMind = 4,
        WorldOp = 5,
        Share = 6,
    }

    /// <summary>改世界表的操作（客户端请求，服务端执行）。</summary>
    public enum Op : byte
    {
        UnlockAll = 0,
        LockAll = 1,
        Unlock = 2,
        Lock = 3,
        ConflictSet = 4,

        /// <summary>learn_great：从请求者手上的物品学大法术（人人可用，服务端按它身上同步过来的物品算）。</summary>
        Learn = 5,
    }

    private static HexAddon Addon => System.Linq.Enumerable.First(AddonRegistry.All, x => x.Id == "hexparse");

    public static ModPacket NewPacket(Msg msg)
    {
        var p = Addon.GetPacket();
        p.Write((byte)msg);
        return p;
    }

    public static void SendPullClipboard(int toWho, ClipboardMode mode, string? rename)
    {
        var p = NewPacket(Msg.PullClipboard);
        p.Write((byte)mode);
        p.Write(rename ?? string.Empty);
        p.Send(toWho);
    }

    public static void SendDisplayCode(int toWho, string code)
    {
        var p = NewPacket(Msg.DisplayCode);
        p.Write(code);
        p.Send(toWho);
    }

    public static void SendMessage(int toWho, string text, HexParseMessageKind kind)
    {
        var p = NewPacket(Msg.Message);
        p.Write(text);
        p.Write((byte)kind);
        p.Send(toWho);
    }

    public static void SendPushMind(Iota iota)
    {
        var p = NewPacket(Msg.PushMind);
        IotaWire.Write(p, iota);
        p.Send();
    }

    public static void SendWorldOp(Op op, string a = "", string b = "")
    {
        var p = NewPacket(Msg.WorldOp);
        p.Write((byte)op);
        p.Write(a);
        p.Write(b);
        p.Send();
    }

    public static void SendShare(string text)
    {
        var p = NewPacket(Msg.Share);
        p.Write(text);
        p.Send();
    }

    public static void Handle(BinaryReader r, int whoAmI)
    {
        var msg = (Msg)r.ReadByte();
        bool server = Main.netMode == NetmodeID.Server;
        switch (msg)
        {
            case Msg.WorldState when !server:
                HexParseWorld.Read(r);
                break;
            case Msg.PullClipboard when !server:
            {
                var mode = (ClipboardMode)r.ReadByte();
                string rename = r.ReadString();
                HexParseIO.HandleClipboard(Main.LocalPlayer, mode, rename.Length > 0 ? rename : null);
                break;
            }
            case Msg.DisplayCode when !server:
                HexParseIO.DisplayCode(r.ReadString());
                break;
            case Msg.Message when !server:
            {
                string text = r.ReadString();
                HexParseHost.Show(text, (HexParseMessageKind)r.ReadByte());
                break;
            }
            case Msg.PushMind when server:
            {
                var iota = IotaWire.Read(r);
                var player = Main.player[whoAmI];
                var stack = Content.Net.ServerCastState.PushIota(player, iota);
                Content.Net.ServerCastState.SendStackSync(whoAmI, stack, ResolvedPatternType.Evaluated);
                break;
            }
            case Msg.WorldOp when server:
            {
                var op = (Op)r.ReadByte();
                string a = r.ReadString(), b = r.ReadString();
                string reply = HexParseCommand.ApplyWorldOp(Main.player[whoAmI], op, a, b);
                SendMessage(whoAmI, reply, HexParseMessageKind.Info);
                break;
            }
            case Msg.Share when server:
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(r.ReadString()), new Color(85, 255, 85));
                break;
        }
    }
}
