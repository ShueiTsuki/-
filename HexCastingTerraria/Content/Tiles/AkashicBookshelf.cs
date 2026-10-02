using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 阿卡夏书架。对应源项目 `hexcasting:akashic_bookshelf`（BlockAkashicBookshelf + BlockEntityAkashicBookshelf）。
///
/// 图书馆真正存东西的地方：每个书架存**一条**「图案 → iota」，能传导（见 <see cref="Core.Casting.Akashic.AkashicLibrary"/>）。
/// 照原版：
///   - 存了东西就「有书」：书脊按 iota 类型着色（四种摆法按位置挑一种），正面画出键图案，发微光（亮度 4）；
///   - 拿卷轴右键：把这格的键图案抄到卷轴上；
///   - 潜行（Shift）+ 空手右键：清空这一格；
///   - 挖掉就丢了这一条（掉的是空书架）。
/// 空书架没有图格实体，写进第一条时才建（服务端 / 单机），清空就删掉 —— 泰拉的放置钩子在 1.4.4 上靠不住，这样也不用补。
/// </summary>
public sealed class AkashicBookshelf : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileMergeDirt[Type] = false;

        MinPick = 0;
        DustType = DustID.WoodFurniture;
        HitSound = SoundID.Dig;
        AddMapEntry(new Color(96, 74, 108));
    }

    /// <summary>原版：有书时亮度 4，空书架不发光（和桥接块同一档）。</summary>
    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        if (AkashicBookshelfEntity.FindAt(i, j)?.Pattern == null) return;
        r = 0.16f;
        g = 0.12f;
        b = 0.24f;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        if (AkashicBookshelfEntity.FindAt(i, j) != null)
        {
            ModContent.GetInstance<AkashicBookshelfEntity>().Kill(i, j);
        }
    }

    /// <summary>书脊叠层（原版 akashic_bookshelf_overlay_1..4，tintindex 0 = iota 类型的颜色）。</summary>
    public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
    {
        var entity = AkashicBookshelfEntity.FindAt(i, j);
        if (entity?.Pattern == null) return;
        var tex = ModContent.Request<Texture2D>("HexCastingTerraria/Content/Tiles/AkashicBookshelf_Books").Value;
        var zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
        var pos = new Vector2(i * 16, j * 16) - Main.screenPosition + zero;
        // 原版四种「有书」模型随机挑（MC 按方块位置定），这里按位置哈希挑一种，不会闪
        int variant = (int)((uint)(i * 73856093 ^ j * 19349663) % 4);
        var tint = Items.ItemStateArt.IotaColor(entity.Datum).MultiplyRGB(Lighting.GetColor(i, j));
        spriteBatch.Draw(tex, pos, new Rectangle(variant * 18, 0, 16, 16), tint);
    }

    /// <summary>
    /// 鼠标指着存了东西的书架：旁边弹出键图案和存的内容（照泰拉告示牌，离多远都看得到）。
    /// 原版 renderPatternForAkashicBookshelf 把键图案画在书架正面；泰拉一格只有 16 像素看不清，用户定改成悬停时看（2026-10-02）。
    /// 书脊照旧按 iota 类型着色。
    /// </summary>
    public override void MouseOver(int i, int j) => Client.UI.TileHoverPanel.Hover(i, j);

    public override void MouseOverFar(int i, int j) => Client.UI.TileHoverPanel.Hover(i, j);

    /// <summary>
    /// 原版 BlockAkashicBookshelf.use：拿卷轴 → 把这格的键抄到卷轴上；潜行 + 空手 → 清空这一格。
    /// 空书架拿卷轴点不写（原版会把空图案写进卷轴，那是个会出错的分支）。
    /// </summary>
    public override bool RightClick(int i, int j)
    {
        var player = Main.LocalPlayer;
        var held = player.HeldItem;
        var pos = new Vector2(i * 16f + 8f, j * 16f + 8f);

        if (held.ModItem is Items.ItemScroll scroll)
        {
            var key = AkashicBookshelfEntity.FindAt(i, j)?.Pattern;
            if (key == null) return false;
            scroll.WriteIota(new PatternIota(key), simulate: false);
            held.NetStateChanged();
            SpellSounds.Play("scroll.scribble", pos);
            return true;
        }

        if (HexPlayer.ShiftHeld() && held.IsAir)
        {
            AkashicBookshelfEntity.RequestClear(i, j);
            return true;
        }
        return false;
    }
}

/// <summary>书架上存的那一条（原版 BlockEntityAkashicBookshelf：pattern + iota）。</summary>
public sealed class AkashicBookshelfEntity : ModTileEntity
{
    /// <summary>键。只有存了东西的书架才有实体，所以正常情况下不为 null。</summary>
    public HexPattern? Pattern { get; private set; }

    public Iota? Datum { get; private set; }

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<AkashicBookshelf>();

    public static AkashicBookshelfEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) ? te as AkashicBookshelfEntity : null;

    /// <summary>原版 setNewMapping（服务端 / 单机）：没有实体就建一个。</summary>
    internal static void SetMapping(int x, int y, HexPattern key, Iota datum)
    {
        var entity = FindAt(x, y);
        if (entity == null)
        {
            int id = ModContent.GetInstance<AkashicBookshelfEntity>().Place(x, y);
            entity = TileEntity.ByID[id] as AkashicBookshelfEntity;
            if (entity == null) return;
        }
        entity.Pattern = key;
        entity.Datum = datum;
        entity.Sync();
    }

    /// <summary>原版 clearIota（服务端 / 单机）：清空 = 删掉实体，空书架不留实体。</summary>
    internal static void Clear(int x, int y)
    {
        var entity = FindAt(x, y);
        if (entity == null) return;
        int id = entity.ID;
        ModContent.GetInstance<AkashicBookshelfEntity>().Kill(x, y);
        if (Main.netMode == NetmodeID.Server)
        {
            // TileEntitySharing 发一个已经不存在的编号 = 让客户端也删掉
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, id, x, y);
        }
    }

    /// <summary>右键清空跑在本地客户端：联机发给服务端做（服务端放音效给所有人），单机直接做。</summary>
    internal static void RequestClear(int x, int y)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            Clear(x, y);
            SpellSounds.Play("scroll.scribble", new Vector2(x * 16f + 8f, y * 16f + 8f));
            return;
        }
        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet is null) return;
        packet.Write((byte)HexMessage.AkashicShelfClear);
        packet.Write((short)x);
        packet.Write((short)y);
        packet.Send();
    }

    /// <summary>服务端收到 <see cref="HexMessage.AkashicShelfClear"/>：只认够得着这格的玩家。</summary>
    internal static void HandleClear(System.IO.BinaryReader r, int whoAmI)
    {
        int x = r.ReadInt16(), y = r.ReadInt16();
        if (whoAmI < 0 || whoAmI >= Main.maxPlayers || Main.player[whoAmI] is not { active: true } p) return;
        if (!WorldGen.InWorld(x, y, 1)) return;
        if (Vector2.Distance(p.Center, new Vector2(x * 16f + 8f, y * 16f + 8f)) > 16f * 20f) return;
        Clear(x, y);
        SpellSounds.Broadcast("scroll.scribble", x * 16f + 8f, y * 16f + 8f);
    }

    public void Sync()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        if (Pattern == null || Datum == null) return;
        tag["pattern"] = IotaTag.ToTag(new PatternIota(Pattern));
        tag["iota"] = IotaTag.ToTag(Datum);
    }

    public override void LoadData(TagCompound tag)
    {
        Pattern = tag.ContainsKey("pattern") && IotaTag.TryFromTag(tag["pattern"], out var p) && p is PatternIota pi ? pi.Pattern : null;
        Datum = tag.ContainsKey("iota") && IotaTag.TryFromTag(tag["iota"], out var d) ? d : null;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        bool has = Pattern != null && Datum != null;
        writer.Write(has);
        if (!has) return;
        IotaWire.WritePattern(writer, Pattern!);
        IotaWire.Write(writer, Datum!);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            Pattern = null;
            Datum = null;
            return;
        }
        Pattern = IotaWire.ReadPattern(reader);
        Datum = IotaWire.Read(reader);
    }
}
