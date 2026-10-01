using System.IO;
using HexCastingTerraria.Addons.HexDebug.Core.Splicing;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>
/// 剪接台的请求（上游 MsgSplicingTableActionC2S / SelectIndexC2S / NewStaffPatternC2S、制念台的施法按钮；
/// 槽位物品上游走 MC 的容器同步，这里由客户端把点过的格子发给服务端，和泰拉箱子的做法一样）。
/// 服务端改完，方块实体整个同步给所有客户端。
/// </summary>
internal static class SplicingTableNet
{
    private const float MaxDistance = 16 * 8;

    private static void Send(HexDebugNet.Msg msg, int x, int y, System.Action<BinaryWriter> body)
        => HexDebugNet.ToServer(msg, w =>
        {
            w.Write((short)x);
            w.Write((short)y);
            body(w);
        });

    public static void SetSlot(int x, int y, int slot, Item item) => Send(HexDebugNet.Msg.SpliceSetSlot, x, y, w =>
    {
        w.Write((byte)slot);
        ItemIO.Send(item, w, writeStack: true);
    });

    public static void Action(int x, int y, SplicingTableAction action) => Send(HexDebugNet.Msg.SpliceAction, x, y, w => w.Write((byte)action));

    public static void Select(int x, int y, int index, bool shift, bool isIota) => Send(HexDebugNet.Msg.SpliceSelect, x, y, w =>
    {
        w.Write(index);
        w.Write(shift);
        w.Write(isIota);
    });

    public static void Draw(int x, int y, HexPattern pattern) => Send(HexDebugNet.Msg.SpliceDraw, x, y, w => IotaWire.Write(w, new PatternIota(pattern)));

    public static void Cast(int x, int y) => Send(HexDebugNet.Msg.SpliceCast, x, y, _ => { });

    /// <summary>核心框架：客户端换下来的东西（手上的那件已经在客户端换好了，和泰拉箱子一样）。</summary>
    public static void SetFocusHolder(int x, int y, Item item) => Send(HexDebugNet.Msg.FocusHolderSet, x, y, w => ItemIO.Send(item, w, writeStack: true));

    public static void HandleServer(HexDebugNet.Msg msg, BinaryReader r, Player player)
    {
        int x = r.ReadInt16();
        int y = r.ReadInt16();
        var te = SplicingTableEntity.FindAt(x, y);
        // 读完整个包再判断，免得后面的数据错位
        switch (msg)
        {
            case HexDebugNet.Msg.SpliceSetSlot:
            {
                int slot = r.ReadByte();
                var item = ItemIO.Receive(r, readStack: true);
                if (te is not null && InReach(player, x, y)) te.SetSlot(slot, item);
                break;
            }
            case HexDebugNet.Msg.SpliceAction:
            {
                var action = (SplicingTableAction)r.ReadByte();
                if (te is not null && InReach(player, x, y)) te.RunAction(player, action);
                break;
            }
            case HexDebugNet.Msg.SpliceSelect:
            {
                int index = r.ReadInt32();
                bool shift = r.ReadBoolean();
                bool isIota = r.ReadBoolean();
                if (te is not null && InReach(player, x, y)) te.SelectIndex(player, index, shift, isIota);
                break;
            }
            case HexDebugNet.Msg.SpliceDraw:
            {
                var iota = IotaWire.Read(r);
                if (te is null || !InReach(player, x, y) || iota is not PatternIota p) break;
                var type = te.DrawPattern(player, p.Pattern);
                HexDebugNet.ToClient(player.whoAmI, HexDebugNet.Msg.SpliceDrawResult, w => w.Write((byte)type));
                break;
            }
            case HexDebugNet.Msg.SpliceCast:
                if (te is not null && InReach(player, x, y)) te.CastHex(player);
                break;
            case HexDebugNet.Msg.FocusHolderSet:
            {
                var item = ItemIO.Receive(r, readStack: true);
                if (FocusHolderEntity.FindAt(x, y) is { } holder && InReach(player, x, y)) holder.SetItem(item);
                break;
            }
        }
    }

    /// <summary>离得太远不处理（上游容器界面的 stillValid：8 格内）。</summary>
    public static bool InReach(Player player, int x, int y)
        => Microsoft.Xna.Framework.Vector2.Distance(player.Center, new Microsoft.Xna.Framework.Vector2(x * 16 + 8, y * 16 + 8)) <= MaxDistance;
}
