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
///
/// ## 当实体
///
/// 原版的壁挂卷轴是实体：区域 / 射线 / 某处的实体都能找到它，编年史家之纯化能读出上面的图案。
/// 这里对应 `EntityIota.EntityKind.WallScroll`（编号 = <see cref="WallScrollEntity"/> 的图格实体 ID），
/// 落地在 TerrariaCastingWorld：只读（原版 ItemDelegatingEntityIotaHolder.ToWallScroll）、
/// 推一下就掉（原版 HangingEntity.push），掉的时候卷轴带着图案一起掉（原版 EntityWallScroll.dropItem，见 <see cref="KillMultiTile"/>）。
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

        // 图格实体放在多格方块的左上角。1.4.4 的 ModTileEntity.Hook_AfterPlacement 默认什么都不放（直接返回 -1），
        // 所以用 Generic_HookPostPlaceMyPlayer；之前接的是前者，挂板放下去没有图格实体，图案挂不上、实体类图案也找不到它
        TileObjectData.newTile.HookPostPlaceMyPlayer = ModContent.GetInstance<WallScrollEntity>().Generic_HookPostPlaceMyPlayer;
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

    /// <summary>
    /// 挂板被拆掉（挖掉，或者被驱动推了一下）：挂着的卷轴带着图案一起掉出来 —— 原版 EntityWallScroll.dropItem 掉的就是卷轴本身
    /// （之前这里只清图格实体，挖掉挂板图案就没了）。挂轴框本身照常由 tML 按 createTile 掉落。
    /// (i, j) 是左上角，图格实体就在那里；掉落物只在服务端 / 单机生成。
    /// </summary>
    public override void KillMultiTile(int i, int j, int frameX, int frameY)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient && WallScrollEntity.FindAt(i, j)?.Pattern is { } pattern)
        {
            Item.NewItem(new EntitySource_TileBreak(i, j), i * 16, j * 16, ObjectSize * 16, ObjectSize * 16, MakeScroll(pattern));
        }
        ModContent.GetInstance<WallScrollEntity>().Kill(i, j);
    }

    /// <summary>这种尺寸的卷轴物品，里面写着 <paramref name="pattern"/>（取回、拆掉时给的就是它）。</summary>
    public Item MakeScroll(HexPattern pattern)
    {
        var scroll = new Item(ScrollItemType);
        if (scroll.ModItem is Items.ItemIotaStorage target)
        {
            target.WriteIota(new PatternIota(pattern), simulate: false);
        }
        return scroll;
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
        // 点到挂板的哪一格都行（图格实体在左上角）
        var entity = WallScrollEntity.FindCovering(i, j);
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
        var scroll = MakeScroll(entity.Pattern);

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

/// <summary>
/// 挂板上存的图案。三种尺寸共用一个 TileEntity 类型（尺寸由方块类型决定），放在多格方块的左上角。
/// 实体类图案看到的「壁挂卷轴」就是它（EntityIota.EntityKind.WallScroll，编号 = 图格实体 ID）。
/// </summary>
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

    /// <summary>按坐标找实体（避开基类的实例方法 `Find`）。坐标要正好是左上角。</summary>
    public static WallScrollEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as WallScrollEntity
            : null;

    /// <summary>挂板任意一格上的实体：先换算到多格方块的左上角再找。</summary>
    public static WallScrollEntity? FindCovering(int x, int y)
    {
        if (!WorldGen.InWorld(x, y)) return null;
        var topLeft = TileObjectData.TopLeft(x, y);
        return FindAt(topLeft.X, topLeft.Y);
    }

    /// <summary>
    /// 按图格实体 ID 找（壁挂卷轴当实体时 EntityIota 的编号就是它，见 EntityIota.EntityKind.WallScroll）。
    /// 挂板已经被拆掉、或者那里已经不是挂板 → null。
    /// </summary>
    public static WallScrollEntity? ById(int id)
        => TileEntity.ByID.TryGetValue(id, out var te) && te is WallScrollEntity s && s.ScrollTile is not null ? s : null;

    /// <summary>挂板这块多格方块（决定尺寸、对应哪种卷轴）；方块已经不在了 → null。</summary>
    public WallScrollTile? ScrollTile
        => WorldGen.InWorld(Position.X, Position.Y) && IsTileValidForEntity(Position.X, Position.Y)
            ? TileLoader.GetTile(Main.tile[Position.X, Position.Y].TileType) as WallScrollTile
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
