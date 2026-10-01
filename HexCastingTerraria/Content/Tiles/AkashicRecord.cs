using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexCastingTerraria.Core.Casting.Akashic;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 阿卡夏记录。对应源项目 `hexcasting:akashic_record`（BlockAkashicRecord）。
///
/// 照原版：它只是图书馆的**入口**，自己什么都不存，也不传导 —— 内容存在相连的书架上
/// （<see cref="AkashicBookshelf"/>，规则见 <see cref="AkashicLibrary"/>）。挖掉记录，书架上的东西还在，
/// 在网络旁边重新放一个就又能读写。整块的实心方块，原版亮度 15。
/// </summary>
public sealed class AkashicRecord : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileMergeDirt[Type] = false;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Dig;
        AddMapEntry(new Microsoft.Xna.Framework.Color(96, 72, 130));
    }

    /// <summary>原版亮度 15（满）。</summary>
    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        r = 0.9f;
        g = 0.7f;
        b = 1f;
    }
}

/// <summary>泰拉世界里的图书馆：图格类型 + 书架实体。</summary>
internal sealed class TerrariaAkashicView : IAkashicLibraryView
{
    public static readonly TerrariaAkashicView Instance = new();

    public AkashicBlock BlockAt(int x, int y)
    {
        if (!WorldGen.InWorld(x, y)) return AkashicBlock.None;
        var tile = Main.tile[x, y];
        if (!tile.HasTile) return AkashicBlock.None;
        int type = tile.TileType;
        if (type == ModContent.TileType<AkashicRecord>()) return AkashicBlock.Record;
        if (type == ModContent.TileType<AkashicBookshelf>()) return AkashicBlock.Bookshelf;
        if (type == ModContent.TileType<AkashicLigature>()) return AkashicBlock.Ligature;
        return AkashicBlock.None;
    }

    public HexPattern? KeyAt(int x, int y) => AkashicBookshelfEntity.FindAt(x, y)?.Pattern;
}

/// <summary>从某块记录出发读写图书馆（服务端 / 单机）。</summary>
internal static class AkashicNetwork
{
    /// <summary>原版 lookupPattern：找到存着这个键的书架就给它的 iota，否则 null。</summary>
    public static Iota? Lookup(int x, int y, HexPattern key)
        => AkashicLibrary.FindKey(x, y, TerrariaAkashicView.Instance, key) is { } at
            ? AkashicBookshelfEntity.FindAt(at.X, at.Y)?.Datum
            : null;

    /// <summary>原版 addNewDatum：已有这个键、或者没有空书架，都静默不写（返回 false）。</summary>
    public static bool Write(int x, int y, HexPattern key, Iota datum)
    {
        var at = AkashicLibrary.FindWriteSlot(x, y, TerrariaAkashicView.Instance, key, () => Main.rand.NextFloat());
        if (at is not { } p) return false;
        AkashicBookshelfEntity.SetMapping(p.X, p.Y, key, datum);
        return true;
    }
}

/// <summary>
/// 旧版的记录实体（2026-10-01 之前移植版把全部条目存在记录方块自己身上）。现在只用来读旧存档：
/// 进世界时由 <see cref="AkashicRecordMigration"/> 把条目照原版的写入规则搬到相连的空书架上，然后删掉。
/// 类名不能改 —— 存档里按类名认实体。
/// </summary>
public sealed class AkashicRecordEntity : ModTileEntity
{
    private readonly List<(string Signature, Iota Value)> _legacy = new();

    internal IReadOnlyList<(string Signature, Iota Value)> LegacyEntries => _legacy;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<AkashicRecord>();

    public override void SaveData(TagCompound tag)
    {
        // 正常情况下进世界就搬走、删掉了，存不到这里；万一还在，原样写回，不丢
        tag["keys"] = _legacy.Select(e => e.Signature).ToList();
        tag["values"] = _legacy.Select(e => Net.IotaTag.ToTag(e.Value)).ToList();
    }

    public override void LoadData(TagCompound tag)
    {
        _legacy.Clear();
        if (!tag.ContainsKey("keys") || !tag.ContainsKey("values")) return;
        var keys = tag.GetList<string>("keys");
        var values = tag.GetList<TagCompound>("values");
        if (keys.Count != values.Count) return;
        for (int i = 0; i < keys.Count; i++)
        {
            if (Net.IotaTag.TryFromTag(values[i], out var iota)) _legacy.Add((keys[i], iota));
        }
    }

    public override void NetSend(BinaryWriter writer) { }

    public override void NetReceive(BinaryReader reader) { }
}

/// <summary>进世界时（单机 / 服务端）把旧版记录实体里的条目搬到书架上。</summary>
public sealed class AkashicRecordMigration : ModSystem
{
    public override void PostWorldLoad()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        var olds = TileEntity.ByID.Values.OfType<AkashicRecordEntity>().ToList();
        if (olds.Count == 0) return;

        int moved = 0, lost = 0;
        foreach (var old in olds)
        {
            int x = old.Position.X, y = old.Position.Y;
            foreach (var (sig, value) in old.LegacyEntries)
            {
                // 旧版只存了角度序列；起笔方向只影响书架上画出来的朝向，查找只比角度序列
                if (HexPattern.TryFromAngles(sig, HexDir.East, out var key, out _) && key != null
                    && AkashicNetwork.Write(x, y, key, value))
                {
                    moved++;
                }
                else
                {
                    lost++;
                }
            }
            ModContent.GetInstance<AkashicRecordEntity>().Kill(x, y);
        }
        Mod.Logger.Info($"[HexCasting] 旧版阿卡夏记录：{olds.Count} 块，搬到书架 {moved} 条，没地方放丢掉 {lost} 条");
    }
}
