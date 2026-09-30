using System.Collections.Generic;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 壁挂卷轴。对应源项目的 `EntityWallScroll`。
///
/// ## 它是干什么的
///
/// 把一段图案**挂在墙上**当地图/笔记/装饰。原版里这是玩家之间交流咒术的主要方式
/// —— 贴一面墙的卷轴，就是一本可以走进去看的书。
///
/// ## 与源项目的实现差异（有意为之）
///
/// 源项目是「拿着卷轴对着墙面右键 → 生成一个悬挂实体」，
/// 卷轴**本身**变成墙上的东西（实体带着 ItemStack）。
///
/// 泰拉侧走的是**两段式**，与我们的石板保持同一套交互语言：
///   ① 用**卷轴框**（可合成）贴在墙前，得到一块空的挂板
///   ② 拿着**存了图案的卷轴**右键挂板 → 把图案挂上去（卷轴被消耗）
///   ③ 空手右键 → 把卷轴取回来（带着图案的卷轴物品）
///
/// 为什么不照抄一步到位：泰拉的「放置时携带数据」没有官方钩子
/// （1.4.5 的 `ModTile` 已经没有 `PlaceInWorld`，`HookPostPlaceMyPlayer` 也拿不到
/// 客户端手上的物品内容，联机时更是只有服务端在跑）。两段式能用现有的、
/// 已经被石板验证过的「写 TileEntity + 上报服务端」路径，代价是多一个合成物品。
/// </summary>
public abstract class WallScrollTile : ModTile
{
    /// <summary>方形尺寸（2/3/4）。对应源项目的 `blockSize` 1/2/3。</summary>
    public abstract int ObjectSize { get; }

    /// <summary>取回时给哪个卷轴物品。</summary>
    public abstract int ScrollItemType { get; }

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = true;
        Main.tileNoFail[Type] = true;
        Main.tileLavaDeath[Type] = true;

        DustType = DustID.Silk;
        HitSound = SoundID.Grass;
        AddMapEntry(new Microsoft.Xna.Framework.Color(212, 196, 160), CreateMapEntryName());

        // 挂墙：`Style3x3Wall` 就是原版画作的样式，直接用。
        // 其它尺寸没有现成的「挂墙」样式，所以拷一个基础样式再把锚点改成墙。
        switch (ObjectSize)
        {
            case 3:
                TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3Wall);
                break;

            case 2:
                TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
                TileObjectData.newTile.AnchorWall = true;
                TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
                break;

            default:
                // 1.4.4 没有 4×4 的现成样式：从 3×3 挂墙样式放大
                TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3Wall);
                TileObjectData.newTile.Width = 4;
                TileObjectData.newTile.Height = 4;
                TileObjectData.newTile.CoordinateHeights = new[] { 16, 16, 16, 16 };
                break;
        }

        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<WallScrollEntity>().Hook_AfterPlacement, -1, 0, false);
        TileObjectData.addTile(Type);
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        if (WallScrollEntity.FindAt(i, j)?.Pattern == null) return;

        // 挂了图案的卷轴会发一点点光，晚上也能看清
        r = 0.20f;
        g = 0.18f;
        b = 0.28f;
    }

    public override void KillMultiTile(int i, int j, int frameX, int frameY)
    {
        ModContent.GetInstance<WallScrollEntity>().Kill(i, j);
    }

    /// <summary>画图案只能靠 `SpecialDraw`（理由见 <see cref="AkashicRecord.DrawEffects"/>）。</summary>
    public override void DrawEffects(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
                                     ref TileDrawInfo drawData)
    {
        if (Main.dedServ) return;

        // 只在**左上角那一格**登记一次，否则 4x4 的卷轴会被画 16 遍
        if (!IsTopLeft(i, j)) return;
        if (WallScrollEntity.FindAt(i, j)?.Pattern == null) return;

        Main.instance.TilesRenderer.AddSpecialPoint(i, j,
            Terraria.GameContent.Drawing.TileDrawing.TileCounterType.CustomNonSolid);
    }

    public override void SpecialDraw(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        if (Main.dedServ) return;
        if (!IsTopLeft(i, j)) return;

        var pattern = WallScrollEntity.FindAt(i, j)?.Pattern;
        if (pattern == null) return;

        // 原版 renderPatternForScroll：图案画满整张卷轴（SCROLL_SETTINGS，默认配色）
        PatternArt.QueueWorld(pattern, new Microsoft.Xna.Framework.Vector2(i * 16f, j * 16f), ObjectSize * 16f,
            Core.Canvas.PatternStyle.Worldly, Core.Canvas.PatternPalette.Default, (i * 31) ^ (j * 17));
    }

    /// <summary>这一格是不是多格物体的左上角。用帧值判定（泰拉的标准做法）。</summary>
    private bool IsTopLeft(int i, int j)
    {
        var tile = Main.tile[i, j];
        if (!tile.HasTile || tile.TileType != Type) return false;

        for (int x = i; x >= i - ObjectSize + 1; x--)
        {
            for (int y = j; y >= j - ObjectSize + 1; y--)
            {
                if (x < 0 || y < 0) continue;
                if (!WorldGen.InWorld(x, y, 1)) continue;

                var t = Main.tile[x, y];
                if (!t.HasTile || t.TileType != Type) continue;

                int relX = (t.TileFrameX % (ObjectSize * 18)) / 18;
                int relY = (t.TileFrameY % (ObjectSize * 18)) / 18;
                if (relX == 0 && relY == 0) return x == i && y == j;
            }
        }

        return false;
    }

    /// <summary>
    /// 右键：
    ///   - 拿着**存了图案的卷轴/载体** → 挂上去（消耗那个物品）
    ///   - 空手 → 把卷轴取回来（带着图案）
    ///   - 挂着东西时空手右键 = 「取回」，没挂东西时 = 提示
    /// </summary>
    public override bool RightClick(int i, int j)
    {
        var entity = WallScrollEntity.FindAt(i, j);
        if (entity == null) return true;

        var held = Main.LocalPlayer.HeldItem;

        if (held.ModItem is Items.ItemIotaStorage storage && storage.Read() is PatternIota pi)
        {
            entity.Pattern = pi.Pattern;
            SyncEntity(entity);

            held.stack--;
            if (held.stack <= 0) held.TurnToAir();

            Main.NewText($"已挂上图案：{pi.Pattern.AnglesSignature()}");
            return true;
        }

        if (entity.Pattern == null)
        {
            Main.NewText("这块挂板还是空的：拿着存了图案的卷轴右键它");
            return true;
        }

        // 取回：把图案带走，挂板留下
        var scroll = new Item(ScrollItemType);
        if (scroll.ModItem is Items.ItemIotaStorage target)
        {
            target.WriteIota(new PatternIota(entity.Pattern), simulate: false);
        }

        Main.LocalPlayer.QuickSpawnItem(
            Main.LocalPlayer.GetSource_Misc("HexWallScroll"), scroll);

        entity.Pattern = null;
        SyncEntity(entity);

        Main.NewText("已取下卷轴");
        return true;
    }

    private static void SyncEntity(WallScrollEntity entity)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Content.Net.HexNetSync.RequestWallScroll(entity.Position.X, entity.Position.Y,
                entity.Pattern);
        }
        else
        {
            entity.Sync();
        }
    }
}

/// <summary>小卷轴挂板（2x2）。</summary>
public sealed class WallScrollSmall : WallScrollTile
{
    public override int ObjectSize => 2;
    public override int ScrollItemType => ModContent.ItemType<Items.ScrollSmall>();
}

/// <summary>中卷轴挂板（3x3）。</summary>
public sealed class WallScrollMedium : WallScrollTile
{
    public override int ObjectSize => 3;
    public override int ScrollItemType => ModContent.ItemType<Items.ScrollMedium>();
}

/// <summary>大卷轴挂板（4x4）。</summary>
public sealed class WallScrollLarge : WallScrollTile
{
    public override int ObjectSize => 4;
    public override int ScrollItemType => ModContent.ItemType<Items.ScrollLarge>();
}

/// <summary>挂板上存的图案。三种尺寸共用一个 TileEntity 类型（尺寸由方块类型决定）。</summary>
public sealed class WallScrollEntity : ModTileEntity
{
    public HexPattern? Pattern { get; set; }

    public override bool IsTileValidForEntity(int x, int y)
    {
        if (!Main.tile[x, y].HasTile) return false;

        int type = Main.tile[x, y].TileType;
        return type == ModContent.TileType<WallScrollSmall>()
            || type == ModContent.TileType<WallScrollMedium>()
            || type == ModContent.TileType<WallScrollLarge>();
    }

    /// <summary>按坐标找实体（避开基类的实例方法 `Find`）。</summary>
    public static WallScrollEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as WallScrollEntity
            : null;

    public void Sync()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        if (Pattern != null)
        {
            tag["pattern"] = IotaTag.ToTag(new PatternIota(Pattern));
        }
    }

    public override void LoadData(TagCompound tag)
    {
        Pattern = null;

        if (tag.ContainsKey("pattern")
            && IotaTag.TryFromTag(tag["pattern"], out var iota)
            && iota is PatternIota pi)
        {
            Pattern = pi.Pattern;
        }
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write(Pattern != null);
        if (Pattern != null)
        {
            Content.Net.IotaWire.WritePattern(writer, Pattern);
        }
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        Pattern = reader.ReadBoolean() ? Content.Net.IotaWire.ReadPattern(reader) : null;
    }
}
