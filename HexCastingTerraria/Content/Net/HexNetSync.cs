using HexCastingTerraria.Core.Casting.Math;
using Terraria;

namespace HexCastingTerraria.Content.Net;

/// <summary>
/// 方块交互的上报辅助。
///
/// 为什么需要它：`ModTile.RightClick` 跑在**本地客户端**，
/// 而方块实体是**服务端权威**的 —— 客户端直接改只有自己看得见。
/// 所以每次交互都要上报，由服务端落地并广播。
///
/// （对比：`ModTile.HitWire` 本身就跑在服务端，不需要这一层。）
/// </summary>
internal static class HexNetSync
{
    /// <summary>请求把图案写进某块石板。</summary>
    public static void RequestSlatePattern(int x, int y, HexPattern pattern)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.SlatePattern);
        packet.Write((short)x);
        packet.Write((short)y);
        IotaWire.WritePattern(packet, pattern);
        packet.Send();
    }

    /// <summary>请求把某块石板的朝向改成指定方向。</summary>
    public static void RequestSlateNormal(int x, int y, byte normal)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.SlateNormal);
        packet.Write((short)x);
        packet.Write((short)y);
        packet.Write(normal);
        packet.Send();
    }

    /// <summary>请求改某根导线的轴向。</summary>
    public static void RequestDirectrixFacing(int x, int y, byte facing)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.DirectrixFacing);
        packet.Write((short)x);
        packet.Write((short)y);
        packet.Write(facing);
        packet.Send();
    }

    /// <summary>服务端处理：改导线轴向。</summary>
    public static void HandleDirectrixFacing(System.IO.BinaryReader reader, int whoAmI)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        byte facing = reader.ReadByte();

        if (!WorldGen.InWorld(x, y, 1)) return;
        if (facing > (byte)Core.Casting.Circles.CircleDir.Right) return;

        var entity = Tiles.HexDirectrixEntity.FindAt(x, y);
        if (entity == null) return;

        entity.SetFacing((Core.Casting.Circles.CircleDir)facing);
        entity.Sync();
    }

    /// <summary>请求启动某个原动力的法术环。</summary>
    public static void RequestStartCircle(int x, int y, int whoAmI)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.StartCircle);
        packet.Write((short)x);
        packet.Write((short)y);
        packet.Send();
    }

    /// <summary>服务端处理：启动法术环。</summary>
    public static void HandleStartCircle(System.IO.BinaryReader reader, int whoAmI)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();

        if (!WorldGen.InWorld(x, y, 1)) return;
        if (whoAmI < 0 || whoAmI >= Main.maxPlayers) return;

        var caster = Main.player[whoAmI];
        if (caster is not { active: true }) return;

        Tiles.HexImpetusEntity.FindAt(x, y)?.TryStart(caster);
    }

    /// <summary>服务端处理：写入图案。</summary>
    public static void HandleSlatePattern(System.IO.BinaryReader reader, int whoAmI)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        var pattern = IotaWire.ReadPattern(reader);

        if (pattern == null) return;
        if (!WorldGen.InWorld(x, y, 1)) return;

        var entity = Tiles.HexSlateEntity.FindAt(x, y);
        if (entity == null) return;

        entity.Pattern = pattern;
        entity.Sync();
    }

    /// <summary>服务端处理：改朝向。</summary>
    public static void HandleSlateNormal(System.IO.BinaryReader reader, int whoAmI)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        byte normal = reader.ReadByte();

        if (!WorldGen.InWorld(x, y, 1)) return;
        if (normal > (byte)Core.Casting.Circles.CircleDir.Right) return;

        var entity = Tiles.HexSlateEntity.FindAt(x, y);
        if (entity == null) return;

        entity.SetNormal((Core.Casting.Circles.CircleDir)normal);
        entity.Sync();
    }

    /// <summary>
    /// 服务端 → 附近客户端：某处敲了一个音符（`beep` 法术）。
    ///
    /// 为什么必须走网络：声音是纯客户端表现，服务端上 `Main.dedServ` 为真、
    /// 播放调用什么都不会发生 —— 不广播的话，联机时只有服务端"听见"了。
    /// 对齐源项目的 `MsgBeepS2C`（那边也是发给 128 格内的玩家）。
    /// </summary>
    public static void BroadcastBeep(float px, float py, byte instrument, byte note)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
        if (instrument >= Core.Casting.Actions.OpBeep.InstrumentCount) return;

        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.Beep);
        packet.Write(px);
        packet.Write(py);
        packet.Write(instrument);
        packet.Write(note);

        // 只发给附近玩家：音符是本地表现，全服广播在人多时会变成噪音源。
        // 半径对齐源项目 MsgBeepS2C 的 128 格。
        const float radiusPx = 128f * Core.Casting.HexUnits.PixelsPerTile;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true }) continue;
            if (System.Math.Abs(other.Center.X - px) > radiusPx) continue;
            if (System.Math.Abs(other.Center.Y - py) > radiusPx) continue;

            packet.Send(i);
        }
    }

    /// <summary>服务端 → 该玩家的客户端：加速度（<paramref name="teleport"/> = false）或传送到左上角坐标（true）。</summary>
    public static void SendPlayerMotion(int player, float x, float y, bool teleport)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;
        packet.Write((byte)HexMessage.PlayerMotion);
        packet.Write(teleport);
        packet.Write(x);
        packet.Write(y);
        packet.Send(player);
    }

    /// <summary>客户端：对本地玩家执行服务端算好的推动 / 传送。</summary>
    public static void HandlePlayerMotion(System.IO.BinaryReader reader)
    {
        bool teleport = reader.ReadBoolean();
        var v = new Microsoft.Xna.Framework.Vector2(reader.ReadSingle(), reader.ReadSingle());
        var p = Main.LocalPlayer;
        if (p is not { active: true } || p.dead) return;
        if (teleport) { TerrariaCastingWorld.TeleportPlayerLocal(p, v); }
        else { p.velocity += v; }
    }

    public static void SendPlayerBuff(int player, int buffType, int ticks)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;
        packet.Write((byte)HexMessage.PlayerBuff);
        packet.Write(buffType);
        packet.Write(ticks);
        packet.Send(player);
    }

    public static void HandlePlayerBuff(System.IO.BinaryReader reader)
    {
        int type = reader.ReadInt32();
        int ticks = reader.ReadInt32();
        if (Main.LocalPlayer is { active: true, dead: false } p) { p.AddBuff(type, ticks); }
    }

    /// <summary>客户端处理：播放广播来的音符。</summary>
    public static void HandleBeep(System.IO.BinaryReader reader)
    {
        float px = reader.ReadSingle();
        float py = reader.ReadSingle();
        byte instrument = reader.ReadByte();
        byte note = reader.ReadByte();

        if (Main.dedServ) return;
        if (instrument >= Core.Casting.Actions.OpBeep.InstrumentCount) return;

        // 与服务端同一套映射：(note-12)/12 铺满 -1 ~ +1 两个八度
        float pitch = (note - 12) / 12f;
        TerrariaCastingWorld.PlayBeep(px, py, instrument, note, pitch);
    }

    /// <summary>客户端 → 服务端：请求施放打包法术（只发槽位号，内容由服务端自己读）。</summary>
    public static void RequestCastPackaged(int slot)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.CastPackaged);
        packet.Write((byte)slot);
        packet.Send();
    }

    /// <summary>
    /// 服务端处理：施放打包法术。
    ///
    /// 只收槽位号、由服务端自己读物品 —— 与法杖施法同一条原则：
    /// **客户端说的不算**。否则客户端可以声称「我手上有一个装满 100 万媒质的法器」。
    /// </summary>
    public static void HandleCastPackaged(System.IO.BinaryReader reader, int whoAmI)
    {
        int slot = reader.ReadByte();

        if (whoAmI < 0 || whoAmI >= Main.maxPlayers) return;

        var player = Main.player[whoAmI];
        if (player is not { active: true }) return;
        if (slot < 0 || slot >= player.inventory.Length) return;

        Items.PackagedSpellCast.Cast(player, player.inventory[slot]);
    }

    /// <summary>
    /// 服务端 → 附近客户端：法术环执行游标。
    ///
    /// 为什么必须广播：环的推进跑在服务端，而**方块是客户端画的** ——
    /// 不广播的话，联机时只有主机看得见「现在走到哪一格」，
    /// 其他人看到的是一个静止的环，完全不知道法术进行到哪了。
    /// </summary>
    public static void BroadcastCircleCursor(int x, int y)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.Server) return;

        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.CircleCursor);
        packet.Write((short)x);
        packet.Write((short)y);

        // 附近广播：环是局部结构，全服广播在人多时会变成噪音
        const float radiusPx = 64f * Core.Casting.HexUnits.PixelsPerTile;
        float px = (x + 0.5f) * Core.Casting.HexUnits.PixelsPerTile;
        float py = (y + 0.5f) * Core.Casting.HexUnits.PixelsPerTile;

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true }) continue;
            if (System.Math.Abs(other.Center.X - px) > radiusPx) continue;
            if (System.Math.Abs(other.Center.Y - py) > radiusPx) continue;

            packet.Send(i);
        }
    }

    /// <summary>客户端处理：登记执行游标。</summary>
    public static void HandleCircleCursor(System.IO.BinaryReader reader)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();

        if (Main.dedServ) return;
        if (!WorldGen.InWorld(x, y, 1)) return;

        Tiles.CircleCursor.Receive(x, y);
    }

    /// <summary>客户端 → 服务端：请求改壁挂卷轴上挂的图案（null = 取下来）。</summary>
    public static void RequestWallScroll(int x, int y, Core.Casting.Math.HexPattern? pattern, string ancientOp)
    {
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        packet.Write((byte)HexMessage.WallScroll);
        packet.Write((short)x);
        packet.Write((short)y);
        packet.Write(pattern != null);
        if (pattern != null)
        {
            IotaWire.WritePattern(packet, pattern);
        }
        packet.Write(ancientOp);

        packet.Send();
    }

    /// <summary>服务端处理：改壁挂卷轴的图案。</summary>
    public static void HandleWallScroll(System.IO.BinaryReader reader, int whoAmI)
    {
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        bool has = reader.ReadBoolean();
        var pattern = has ? IotaWire.ReadPattern(reader) : null;
        string ancientOp = reader.ReadString();

        if (!WorldGen.InWorld(x, y, 1)) return;

        var entity = Tiles.WallScrollEntity.FindAt(x, y);
        if (entity == null) return;

        entity.Pattern = pattern;
        entity.AncientOp = pattern == null ? "" : ancientOp;
        entity.Sync();
    }
}
