using System.Collections.Generic;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using System.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 阿卡夏记录方块。对应源项目 `hexcasting:akashic_record`。
///
/// 它是一块**以图案为键的存储**：
///   - `akashic/write` 把一个 iota 写在某个图案名下
///   - `akashic/read` 再按同一个图案取回来
///
/// 原版的深层用途是「每个世界可以有自己发明的图案」——
/// 静态注册表里没有的图案，可以靠世界里的记录解析。
/// 那部分（逐世界图案解析）尚未实现，见 TODO_PLAN.md。
/// </summary>
public sealed class AkashicRecord : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;

        // tileContainer 让泰拉把它当成「有实体的方块」处理：
        // 放置/破坏时会自动建立/清理 TileEntity，不用自己写那一堆帧逻辑。
        Main.tileContainer[Type] = true;
        Main.tileNoFail[Type] = false;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Microsoft.Xna.Framework.Color(96, 72, 130));

        // 1×1 的方块实体
        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = new AnchorData(
            AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table,
            TileObjectData.newTile.Width, 0);
        // 放图格实体用 tML 的通用钩子（实体自带的 Hook_AfterPlacement 在 1.4.4 默认什么都不放）
        TileObjectData.newTile.HookPostPlaceMyPlayer = ModContent.GetInstance<AkashicRecordEntity>().Generic_HookPostPlaceMyPlayer;   // 1.4.4 的 Hook_AfterPlacement 默认什么都不放，见 TileEntityRepair
        TileObjectData.addTile(Type);
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 记录方块自发光：洞穴里要能看见
        r = 0.22f;
        g = 0.12f;
        b = 0.34f;
    }

    /// <summary>挖掉时清掉实体，否则存档里会留下悬空记录。</summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ModContent.GetInstance<AkashicRecordEntity>().Kill(i, j);
    }

    /// <summary>
    /// 注册「在所有方块之后绘制」。
    ///
    /// 为什么必须走这条路：方块是**从上到下、从左到右逐块**绘制的，
    /// `PostDraw` 里画的东西会被后面绘制的方块盖住。
    /// 要在方块**之上**画图案只能靠 `SpecialDraw`。
    ///
    /// 用 `CustomNonSolid` 而不是 `AddSpecialLegacyPoint`：
    /// 后者走 tile render target，只有 **15fps**，而且还得自己处理
    /// `Main.offScreenRange`；前者是 60fps 且不需要坐标修正。
    /// </summary>
    public override void DrawEffects(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
                                     ref Terraria.DataStructures.TileDrawInfo drawData)
    {
        if (Main.dedServ) return;
        if (AkashicRecordEntity.FindAt(i, j)?.FirstPattern() == null) return;

        Main.instance.TilesRenderer.AddSpecialPoint(i, j,
            Terraria.GameContent.Drawing.TileDrawing.TileCounterType.CustomNonSolid);
    }

    /// <summary>
    /// 在方块上方画出记录里存着的第一个图案。
    ///
    /// 这是「用渲染换可用性」：右键看文字需要玩家主动操作，
    /// 而直接画出符印，走过路过一眼就知道这块写了什么。
    /// </summary>
    public override void SpecialDraw(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        if (Main.dedServ) return;

        var pattern = AkashicRecordEntity.FindAt(i, j)?.FirstPattern();
        if (pattern == null) return;

        // 原版 renderPatternForAkashicBookshelf：画在方块正面（WORLDLY，默认配色）
        PatternArt.QueueWorld(pattern, new Microsoft.Xna.Framework.Vector2(i * 16f, j * 16f), 16f,
            Core.Canvas.PatternStyle.Worldly, Core.Canvas.PatternPalette.Default, (i * 31) ^ (j * 17));
    }

    /// <summary>右键查看内容（方便调试：不打开界面也能确认写了什么）。</summary>
    public override bool RightClick(int i, int j)
    {
        var entity = AkashicRecordEntity.FindAt(i, j);
        if (entity == null)
        {
            Main.NewText("这块阿卡夏记录是空的");
            return true;
        }

        if (entity.Count == 0)
        {
            Main.NewText("这块阿卡夏记录还没有写入任何内容");
            return true;
        }

        Main.NewText($"这块阿卡夏记录存有 {entity.Count} 条：");
        foreach (var (key, value) in entity.Entries())
        {
            Main.NewText($"  {key} → {value}");
        }
        return true;
    }
}

/// <summary>
/// 阿卡夏记录方块的数据：图案签名 → iota。
/// </summary>
public sealed class AkashicRecordEntity : ModTileEntity
{
    private readonly Dictionary<string, Iota> _entries = new();

    /// <summary>存了多少条。</summary>
    public int Count => _entries.Count;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<AkashicRecord>();

    /// <summary>
    /// 按坐标找实体。
    ///
    /// 命名成 FindAt 而不是 Find：ModTileEntity 已经有一个 Find(int, int)，
    /// 重名会遮蔽基类成员（编译器警告 CS0108），换个名字更清楚。
    /// </summary>
    public static AkashicRecordEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as AkashicRecordEntity
            : null;

    /// <summary>
    /// 取出第一个存着的图案（供方块上方显示用）。没有内容返回 null。
    ///
    /// 起始方向统一用 East：图案的**形状**由角度签名决定，
    /// 起始方向只影响整体旋转 —— 显示用途下无差别。
    /// </summary>
    public Core.Casting.Math.HexPattern? FirstPattern()
    {
        foreach (var key in _entries.Keys)
        {
            if (Core.Casting.Math.HexPattern.TryFromAngles(
                    key, Core.Casting.Math.HexDir.East, out var parsed, out _) && parsed != null)
            {
                return parsed;
            }
        }
        return null;
    }

    /// <summary>按图案取。</summary>
    public Iota? Lookup(HexPattern key)
        => _entries.TryGetValue(key.AnglesSignature(), out var v) ? v : null;

    /// <summary>按图案写。</summary>
    public void Store(HexPattern key, Iota value) => _entries[key.AnglesSignature()] = value;

    public IEnumerable<(string Key, Iota Value)> Entries()
    {
        foreach (var kv in _entries)
        {
            yield return (kv.Key, kv.Value);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        var keys = new List<string>();
        var values = new List<TagCompound>();

        foreach (var kv in _entries)
        {
            keys.Add(kv.Key);
            values.Add(Net.IotaTag.ToTag(kv.Value));
        }

        // 键值分成两个平行列表存：
        // TagCompound 不直接支持 Dictionary，而 List<TagCompound> 嵌套又会多一层。
        // 平行列表最简单，且键与值的对应关系靠下标保证。
        tag["keys"] = keys;
        tag["values"] = values;
    }

    public override void LoadData(TagCompound tag)
    {
        _entries.Clear();

        if (!tag.ContainsKey("keys") || !tag.ContainsKey("values")) return;

        var keys = tag.GetList<string>("keys");
        var values = tag.GetList<TagCompound>("values");

        // 两个列表长度不一致说明存档被改坏了 —— 直接放弃整份数据，
        // 而不是按较短的那个截断（截断会悄悄丢掉玩家的记录）。
        if (keys.Count != values.Count)
        {
            HexCastingTerraria.Instance?.Logger.Warn(
                $"[HexCasting] 阿卡夏记录存档损坏：键 {keys.Count} 条、值 {values.Count} 条，已丢弃");
            return;
        }

        for (int i = 0; i < keys.Count; i++)
        {
            // 单条读不出来就跳过这一条，不影响其它条目
            if (Net.IotaTag.TryFromTag(values[i], out var iota))
            {
                _entries[keys[i]] = iota;
            }
        }
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write((ushort)_entries.Count);
        foreach (var kv in _entries)
        {
            writer.Write(kv.Key);
            Net.IotaWire.Write(writer, kv.Value);
        }
    }

    public override void NetReceive(BinaryReader reader)
    {
        _entries.Clear();
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            string key = reader.ReadString();
            _entries[key] = Net.IotaWire.Read(reader);
        }
    }

    public override void OnNetPlace() { /* 放置时无内容，不需要推送 */ }
}
