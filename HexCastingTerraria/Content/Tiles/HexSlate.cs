using System.Collections.Generic;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 石板贴在哪（原版 AttachFace + FACING）。原版石板是 1/16 格厚的薄板，贴在旁边某个方块的一个面上，
/// 朝向 = 那一面朝外的方向。泰拉的画面是一个平面，于是有五种：
/// 贴背景墙（朝屏幕外），贴下面 / 上面 / 左边 / 右边的方块（朝上 / 朝下 / 朝右 / 朝左）。
/// 存档按字节存，顺序不能改。
/// </summary>
public enum SlateAttach : byte
{
    Wall = 0,
    Floor = 1,
    Ceiling = 2,
    LeftBlock = 3,
    RightBlock = 4,
}

/// <summary>
/// 石板。对应源项目 `hexcasting:slate` —— **法术环的「指令」**。
///
/// 注意：关键机制（读 `BlockSlate.acceptControlFlow` 得到）：
/// **石板存一个图案，走环时执行它**；空石板是直通（只改流向，不执行任何东西）。
/// 所以环是**可改写的物理程序** —— 换掉某块石板就改了程序。
///
/// ## 贴法决定控制流怎么走（照原版 BlockSlate）
///
/// 朝向（normalDir）= 贴着的那一面朝外的方向：不能往朝向出去，不能顺着朝向的反方向进来。
///   - 贴背景墙：朝屏幕外，画面里上下左右都能走 —— 原版平铺在地上的环就是这样（石板朝上，环在水平面里随便走）；
///   - 贴下面的方块（地面）：朝上，不往上出去；贴天花板朝下；贴左边方块朝右；贴右边方块朝左。
/// 贴法由**贴在哪**决定（<see cref="SlateAttach"/>）：放下时有背景墙先贴墙，没有就贴地面、天花板、左、右里第一个撑得住的；
/// 锤子敲一下在撑得住的几种之间轮换（用户定，2026-10-02）；撑着它的方块或墙没了就掉下来（原版 canSurvive）。
/// 这里曾经是「只能放在实心方块上面，朝向默认朝上、空手右键随便转」：朝向和贴在哪没关系，
/// 最上面一排石板下面是空的放不下，闭合的环其实搭不出来（2026-10-02 用户和群友指出）。
///
/// ## 样子
///
/// 贴背景墙画整格正面；贴在方块上从侧面看，画成 4 像素厚的薄板（原版 1/16 格 = 1 像素，泰拉里看不见，用户定加厚）。
/// 原版是薄板，人能走过去，这里也不挡路（不是实心方块）。
/// 原版把图案画在石板正面；泰拉一格只有 16 像素看不清，用户定改成「空白 / 刻了图案」两种样子，图案在鼠标悬停时看
///（<see cref="TileHoverPanel"/>）。法术环正走到这块时发亮。
/// </summary>
public sealed class HexSlate : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = false;
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;
        Main.tileNoAttach[Type] = true;
        // 不是实心方块，锤子默认敲不到；登记成「能敲」，敲的时候走 Slope 换贴法
        TileID.Sets.CanBeSloped[Type] = true;
        // 拆掉背景墙时泰拉只对登记了的方块重算这一格（WorldGen.KillWall）：贴墙的石板要靠它发现墙没了
        TileID.Sets.FramesOnKillWall[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Microsoft.Xna.Framework.Color(88, 76, 116));

        // 不用泰拉的锚点：五种贴法由 CanPlace / TileFrame 自己判断（锚点表达不了「贴在哪」，还会和图集的帧打架）
        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
        TileObjectData.newTile.HookPostPlaceMyPlayer = ModContent.GetInstance<HexSlateEntity>().Generic_HookPostPlaceMyPlayer;   // 1.4.4 的 Hook_AfterPlacement 默认什么都不放，见 TileEntityRepair
        TileObjectData.addTile(Type);
    }

    // ── 贴法 ──────────────────────────────────────────────────────────

    /// <summary>轮换顺序，也是放下时挑选的优先顺序。</summary>
    private static readonly SlateAttach[] Order =
        { SlateAttach.Wall, SlateAttach.Floor, SlateAttach.Ceiling, SlateAttach.LeftBlock, SlateAttach.RightBlock };

    /// <summary>贴法对应的朝向（在画面里）；贴背景墙朝屏幕外 → null。</summary>
    public static CircleDir? NormalOf(SlateAttach attach) => attach switch
    {
        SlateAttach.Floor => CircleDir.Up,
        SlateAttach.Ceiling => CircleDir.Down,
        SlateAttach.LeftBlock => CircleDir.Right,
        SlateAttach.RightBlock => CircleDir.Left,
        _ => null,
    };

    /// <summary>(x, y) 这块石板按这种贴法撑不撑得住：贴墙要有背景墙，贴方块要那一侧是实心方块（地面也认平台的顶面）。</summary>
    public static bool Supported(int x, int y, SlateAttach attach) => attach switch
    {
        SlateAttach.Wall => Main.tile[x, y].WallType > WallID.None,
        SlateAttach.Floor => SolidFace(x, y + 1, allowTopOnly: true),
        SlateAttach.Ceiling => SolidFace(x, y - 1, allowTopOnly: false),
        SlateAttach.LeftBlock => SolidFace(x - 1, y, allowTopOnly: false),
        _ => SolidFace(x + 1, y, allowTopOnly: false),
    };

    private static bool SolidFace(int x, int y, bool allowTopOnly)
    {
        if (!WorldGen.InWorld(x, y)) return false;
        var t = Main.tile[x, y];
        if (!t.HasTile || t.IsActuated || !Main.tileSolid[t.TileType]) return false;
        return allowTopOnly || !Main.tileSolidTop[t.TileType];
    }

    /// <summary>放下时的贴法：按优先顺序第一个撑得住的；一个都没有（不该发生，CanPlace 拦过了）就当贴墙。</summary>
    public static SlateAttach DefaultAttach(int x, int y)
    {
        foreach (var a in Order)
        {
            if (Supported(x, y, a)) return a;
        }
        return SlateAttach.Wall;
    }

    /// <summary>锤子敲一下：下一种撑得住的贴法（只有一种就不变）。</summary>
    public static SlateAttach NextAttach(int x, int y, SlateAttach current)
    {
        int at = System.Array.IndexOf(Order, current);
        for (int k = 1; k <= Order.Length; k++)
        {
            var a = Order[(at + k) % Order.Length];
            if (Supported(x, y, a)) return a;
        }
        return current;
    }

    public static string Describe(SlateAttach attach) => attach switch
    {
        SlateAttach.Wall => "贴在背景墙上",
        SlateAttach.Floor => "贴在下面的方块上",
        SlateAttach.Ceiling => "贴在上面的方块上",
        SlateAttach.LeftBlock => "贴在左边的方块上",
        _ => "贴在右边的方块上",
    };

    /// <summary>原版 canSurvive：至少有一种贴法撑得住才放得下（不能浮空）。</summary>
    public override bool CanPlace(int i, int j)
    {
        foreach (var a in Order)
        {
            if (Supported(i, j, a)) return true;
        }
        return false;
    }

    /// <summary>
    /// 邻格变了（挖掉方块、拆掉背景墙都会走到这里）：撑着它的东西没了就掉下来（原版 canSurvive 失败 → 方块被破坏，照常掉落）。
    /// 分区重画时泰拉会带着 noBreak 调，这时不动；联机客户端不动，由服务端拆。
    /// </summary>
    public override bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak)
    {
        if (noBreak || Main.netMode == NetmodeID.MultiplayerClient) return false;
        if (HexSlateEntity.FindAt(i, j) is { } entity && !Supported(i, j, entity.ResolveAttach()))
        {
            WorldGen.KillTile(i, j);
            if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, i, j);
        }
        return false;
    }

    /// <summary>
    /// 锤子敲一下：换成下一种撑得住的贴法（用户定，2026-10-02；原版放下以后改不了，只能拆了重放）。
    /// 泰拉锤子敲方块会先问 Slope，返回 false 就不做斜坡。只在挥锤的本地客户端跑，联机发给服务端改。
    /// </summary>
    public override bool Slope(int i, int j)
    {
        if (HexSlateEntity.FindAt(i, j) is not { } entity) return false;
        var next = NextAttach(i, j, entity.ResolveAttach());
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            HexNetSync.RequestSlateAttach(i, j, next);
        }
        else
        {
            entity.Attach = next;
            entity.Sync();
        }
        SoundEngine.PlaySound(SoundID.Dig, new Microsoft.Xna.Framework.Vector2(i * 16 + 8, j * 16 + 8));
        return false;
    }

    // ── 样子 ──────────────────────────────────────────────────────────

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 正在执行的石板会亮一档 —— 这是「法术跑到哪了」最直接的视觉反馈
        if (CircleCursor.IsActive(i, j))
        {
            r = 0.42f;
            g = 0.36f;
            b = 0.55f;
            return;
        }

        r = 0.14f;
        g = 0.09f;
        b = 0.22f;
    }

    /// <summary>图集：列 = [空白, 刻了图案]，行 = 贴法（顺序同 <see cref="SlateAttach"/>），见 _tools/gen_textures.py。</summary>
    public override void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset)
    {
        if (HexSlateEntity.FindAt(i, j) is not { } entity) return;
        if (entity.Pattern != null) frameXOffset = 18;
        frameYOffset = (int)entity.ResolveAttach() * 18;
    }

    /// <summary>鼠标指着刻了图案的石板：旁边弹出图案（照泰拉告示牌，离多远都看得到）。</summary>
    public override void MouseOver(int i, int j) => TileHoverPanel.Hover(i, j);

    public override void MouseOverFar(int i, int j) => TileHoverPanel.Hover(i, j);

    // ── 挖掉、放下、右键 ──────────────────────────────────────────────

    /// <summary>
    /// 挖掉：刻着图案的石板掉「有图案的石板」（原版掉落表把方块里的图案复制到物品上），空的掉空白石板。
    /// 掉落只在单机 / 服务端生成（联机客户端也跑这个钩子，在那边生成会多掉一份）。
    /// </summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        if (!noItem && Main.netMode != NetmodeID.MultiplayerClient && HexSlateEntity.FindAt(i, j)?.Pattern is { } pattern)
        {
            noItem = true;
            int idx = Item.NewItem(new EntitySource_TileBreak(i, j), new Microsoft.Xna.Framework.Vector2(i * 16, j * 16),
                new Microsoft.Xna.Framework.Vector2(16, 16), ModContent.ItemType<Items.HexSlateItem>(), 1, noBroadcast: true);
            if (Main.item[idx].ModItem is Items.HexSlateItem slate) slate.ForceWrite(new PatternIota(pattern));
            if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.SyncItem, -1, -1, null, idx, 1f);
        }
        ModContent.GetInstance<HexSlateEntity>().Kill(i, j);
    }

    /// <summary>
    /// 放下：定贴法（单机；联机时服务端的图格实体第一次用到时按同样的规则定，见 <see cref="HexSlateEntity.ResolveAttach"/>），
    /// 有图案的石板图案跟着进方块（见 <see cref="Items.HexSlateItem.ApplyToPlaced"/>）。
    /// </summary>
    public override void PlaceInWorld(int i, int j, Item item)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient && HexSlateEntity.FindAt(i, j) is { } entity)
        {
            entity.Attach = DefaultAttach(i, j);
        }
        Items.HexSlateItem.ApplyToPlaced(i, j, item);
    }

    /// <summary>手上拿着能存图案的物品时右键：把图案**写进石板**（移植版的便利操作；原版要先写进石板物品再放）。</summary>
    public override bool RightClick(int i, int j)
    {
        var entity = HexSlateEntity.FindAt(i, j);
        if (entity == null) return false;

        var held = Main.LocalPlayer.HeldItem;
        if (held.ModItem is not Content.Items.ItemIotaStorage storage || storage.Read() is not PatternIota pi) return false;

        entity.Pattern = pi.Pattern;

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            // 右击跑在**本地客户端**，所以必须上报服务端 ——
            // 否则只有自己看到石板变了（方块实体是服务端权威的）
            HexNetSync.RequestSlatePattern(i, j, pi.Pattern);
        }
        else
        {
            entity.Sync();
        }

        Content.SpellSounds.Play("scroll.scribble",
            new Microsoft.Xna.Framework.Vector2(i * 16f + 8f, j * 16f + 8f));
        Main.NewText($"已把图案写入石板：{pi.Pattern.AnglesSignature()}");
        return true;
    }
}

/// <summary>石板的数据：一个图案 + 贴法。</summary>
public sealed class HexSlateEntity : ModTileEntity
{
    /// <summary>石板上存的图案。null = 空石板（走环时直通）。</summary>
    public Core.Casting.Math.HexPattern? Pattern { get; set; }

    /// <summary>贴法。null = 还没定（刚由放置消息建出来的，或者旧存档）：第一次用到时定，见 <see cref="ResolveAttach"/>。</summary>
    public SlateAttach? Attach { get; set; }

    /// <summary>旧存档里那个随便转的朝向（2026-10-02 之前），定贴法时尽量照它。</summary>
    private CircleDir? _legacyNormal;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<HexSlate>();

    /// <summary>
    /// 按坐标找实体。命名成 FindAt 而不是 Find：`ModTileEntity` 已有实例方法 `Find`，
    /// 重名会遮蔽基类成员（编译器警告 CS0108）。
    /// </summary>
    public static HexSlateEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as HexSlateEntity
            : null;

    /// <summary>
    /// 当前贴法；还没定就现在定：旧存档的石板有背景墙就贴墙（画面里四个方向都通，原来走得通的环照样走得通），
    /// 没有就照旧朝向对应的那一侧（撑得住的话），再不行按放下时的规则挑。
    /// </summary>
    public SlateAttach ResolveAttach()
    {
        if (Attach is { } a) return a;
        int x = Position.X, y = Position.Y;
        SlateAttach chosen = HexSlate.DefaultAttach(x, y);
        if (chosen != SlateAttach.Wall && _legacyNormal is { } n)
        {
            var wanted = n switch
            {
                CircleDir.Up => SlateAttach.Floor,
                CircleDir.Down => SlateAttach.Ceiling,
                CircleDir.Right => SlateAttach.LeftBlock,
                _ => SlateAttach.RightBlock,
            };
            if (HexSlate.Supported(x, y, wanted)) chosen = wanted;
        }
        Attach = chosen;
        return chosen;
    }

    /// <summary>把变化同步出去（联机时必须，否则只有改的人看得见）。</summary>
    public void Sync()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        tag["attach"] = (byte)ResolveAttach();
        if (Pattern != null)
        {
            // 图案用现有的信封格式存，直接落进 TagCompound
            tag["pattern"] = IotaTag.ToTag(new PatternIota(Pattern));
        }
    }

    public override void LoadData(TagCompound tag)
    {
        Attach = null;
        _legacyNormal = null;
        if (tag.ContainsKey("attach") && tag.GetByte("attach") <= (byte)SlateAttach.RightBlock)
        {
            Attach = (SlateAttach)tag.GetByte("attach");
        }
        else if (tag.ContainsKey("normal") && tag.GetByte("normal") <= (byte)CircleDir.Right)
        {
            _legacyNormal = (CircleDir)tag.GetByte("normal");
        }

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
        writer.Write((byte)ResolveAttach());
        writer.Write(Pattern != null);
        if (Pattern != null)
        {
            IotaWire.WritePattern(writer, Pattern);
        }
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        byte a = reader.ReadByte();
        Attach = a <= (byte)SlateAttach.RightBlock ? (SlateAttach)a : SlateAttach.Wall;

        Pattern = reader.ReadBoolean() ? IotaWire.ReadPattern(reader) : null;
    }
}
