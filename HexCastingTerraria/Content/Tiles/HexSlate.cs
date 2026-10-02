using System.Collections.Generic;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 石板。对应源项目 `hexcasting:slate` —— **法术环的「指令」**。
///
/// 注意：关键机制（读 `BlockSlate.acceptControlFlow` 得到）：
/// **石板存一个图案，走环时执行它**；空石板是直通（只改流向，不执行任何东西）。
/// 所以环是**可改写的物理程序** —— 换掉某块石板就改了程序。
///
/// ## 朝向决定控制流怎么走
///
/// 每块石板有一个「朝外」方向 `Normal`，它同时决定：
///   - **能往哪出去**：不能往 `Normal` 本身出去
///   - **不能从哪进来**：不能从 `Normal` 的反方向进来
///
/// 所以部件的 `Normal` 必须**垂直于局部流向**，否则会挡住控制流。
/// 注意：这一点对玩家不直观 —— 见下方 RightClick 的「空手右键旋转」设计。
///
/// ## 与源项目的差异
///
/// 源项目用 `AttachFace`（地/顶/墙）+ `FACING` 四个朝向。
/// 泰拉侧**没有等价的自动判定**（图格不记录「贴在哪个面」），
/// 所以改成：放置时默认为「朝上」，**空手右键循环旋转**四向。
/// 这是有意的简化，换来的是朝向完全可控、且不依赖帧运算。
/// </summary>
public sealed class HexSlate : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;   // 需要存朝向 -> framed，代价是不支持斜坡

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Microsoft.Xna.Framework.Color(88, 76, 116));

        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = new AnchorData(
            AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table,
            TileObjectData.newTile.Width, 0);
        TileObjectData.newTile.HookPostPlaceMyPlayer = ModContent.GetInstance<HexSlateEntity>().Generic_HookPostPlaceMyPlayer;   // 1.4.4 的 Hook_AfterPlacement 默认什么都不放，见 TileEntityRepair
        TileObjectData.addTile(Type);
    }

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

    /// <summary>
    /// 要在方块**之上**画图案，只能靠 `SpecialDraw`（方块是从上到下逐块画的，
    /// `PostDraw` 里画的东西会被后面的方块盖住）。
    ///
    /// 用 `AddSpecialPoint` 而不是 `AddSpecialLegacyPoint`：后者走 tile render target、只有 15fps。
    /// 石板是实心方块，计数类型必须是 `CustomSolid`：实心方块的 DrawEffects 在「实心那一遍」里调用，
    /// 紧接着「非实心那一遍」开头会把 `CustomNonSolid` 的点清零（TileDrawing.PreDrawTiles）—— 这里曾经用 CustomNonSolid，
    /// 刻上去的图案一帧都画不出来（2026-10-02 客户端测试截图发现）。`CustomSolid` 的点在实心方块画完后每帧画一次。
    /// </summary>
    public override void DrawEffects(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
                                     ref Terraria.DataStructures.TileDrawInfo drawData)
    {
        if (Main.dedServ) return;
        if (HexSlateEntity.FindAt(i, j)?.Pattern == null) return;

        Main.instance.TilesRenderer.AddSpecialPoint(i, j,
            Terraria.GameContent.Drawing.TileDrawing.TileCounterType.CustomSolid);
    }

    /// <summary>
    /// 把石板上存的图案画出来。
    ///
    /// 为什么这件「纯表现」的事优先级很高：法术环是**可改写的物理程序**，
    /// 而玩家看不见每块石板上写了什么 —— 那就等于在盲改一台机器。
    /// 右键虽然能读到文字，但那要求玩家一块一块去点。
    /// </summary>
    public override void SpecialDraw(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        if (Main.dedServ) return;

        var pattern = HexSlateEntity.FindAt(i, j)?.Pattern;
        if (pattern == null) return;

        // 原版 renderPatternForSlate：图案画满石板那一面（WORLDLY：留 2/16 边、线宽 0.8/16）；
        // 法术环正走到这块（充能）时换成抖动的紫色电光（WOBBLY + SLATE_WOBBLY_PURPLE_COLOR）
        bool active = CircleCursor.IsActive(i, j);
        PatternArt.QueueWorld(pattern, new Microsoft.Xna.Framework.Vector2(i * 16f, j * 16f), 16f,
            active ? Core.Canvas.PatternStyle.Wobbly : Core.Canvas.PatternStyle.Worldly,
            active ? Core.Canvas.PatternPalette.SlatePurple : Core.Canvas.PatternPalette.Default,
            (i * 31) ^ (j * 17));
    }

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

    /// <summary>放下有图案的石板：图案跟着进方块（见 <see cref="Items.HexSlateItem.ApplyToPlaced"/>）。</summary>
    public override void PlaceInWorld(int i, int j, Item item) => Items.HexSlateItem.ApplyToPlaced(i, j, item);

    /// <summary>
    /// 空手右键：把朝向**顺时针转 90°**。
    ///
    /// 为什么这么设计：朝向必须垂直于流向，配错了环就走不通，
    /// 而泰拉没有「贴在哪个面」的自动判定。给一个显式的旋转操作，
    /// 玩家能立刻纠正，也比去猜帧值友好得多。
    ///
    /// 手上拿着能存图案的物品时：把图案**写进石板**（这是「编程」动作）。
    /// </summary>
    public override bool RightClick(int i, int j)
    {
        var entity = HexSlateEntity.FindAt(i, j);
        if (entity == null) return true;

        var held = Main.LocalPlayer.HeldItem;

        // 拿着存了图案的物品 -> 写进石板
        if (held.ModItem is Content.Items.ItemIotaStorage storage
            && storage.Read() is PatternIota pi)
        {
            entity.Pattern = pi.Pattern;

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                // 右击跑在**本地客户端**，所以必须上报服务端 ——
                // 否则只有自己看到石板变了（方块实体是服务端权威的）
                Content.Net.HexNetSync.RequestSlatePattern(i, j, pi.Pattern);
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

        // 空手（或拿着别的东西）-> 旋转朝向
        entity.RotateNormal();
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Content.Net.HexNetSync.RequestSlateNormal(i, j, (byte)entity.Normal);
        }
        else
        {
            entity.Sync();
        }

        Main.NewText($"石板朝向：{DescribeNormal(entity.Normal)}");
        return true;
    }

    private static string DescribeNormal(CircleDir dir) => dir switch
    {
        CircleDir.Up => "上",
        CircleDir.Down => "下",
        CircleDir.Left => "左",
        _ => "右",
    };
}

/// <summary>石板的数据：一个图案 + 一个朝向。</summary>
public sealed class HexSlateEntity : ModTileEntity
{
    /// <summary>石板上存的图案。null = 空石板（走环时直通）。</summary>
    public Core.Casting.Math.HexPattern? Pattern { get; set; }

    /// <summary>该石板「朝外」的方向。见 <see cref="HexSlate"/> 的说明。</summary>
    public CircleDir Normal { get; private set; } = CircleDir.Up;

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

    /// <summary>顺时针转 90°。</summary>
    public void RotateNormal()
        => Normal = Normal switch
        {
            CircleDir.Up => CircleDir.Right,
            CircleDir.Right => CircleDir.Down,
            CircleDir.Down => CircleDir.Left,
            _ => CircleDir.Up,
        };

    public void SetNormal(CircleDir dir) => Normal = dir;

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
        tag["normal"] = (byte)Normal;
        if (Pattern != null)
        {
            // 图案用现有的信封格式存，直接落进 TagCompound
            tag["pattern"] = IotaTag.ToTag(new PatternIota(Pattern));
        }
    }

    public override void LoadData(TagCompound tag)
    {
        byte n = tag.ContainsKey("normal") ? tag.GetByte("normal") : (byte)0;
        Normal = n <= (byte)CircleDir.Right ? (CircleDir)n : CircleDir.Up;

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
        writer.Write((byte)Normal);
        writer.Write(Pattern != null);
        if (Pattern != null)
        {
            IotaWire.WritePattern(writer, Pattern);
        }
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        byte n = reader.ReadByte();
        Normal = n <= (byte)CircleDir.Right ? (CircleDir)n : CircleDir.Up;

        Pattern = reader.ReadBoolean() ? IotaWire.ReadPattern(reader) : null;
    }
}
