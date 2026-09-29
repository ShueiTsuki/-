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
/// ⚠️ 关键机制（读 `BlockSlate.acceptControlFlow` 得到）：
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
/// ⚠️ 这一点对玩家不直观 —— 见下方 RightClick 的「空手右键旋转」设计。
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
        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<HexSlateEntity>().Hook_AfterPlacement, -1, 0, false);
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
    /// 用 `CustomNonSolid` 而不是 `AddSpecialLegacyPoint`：后者走 tile render target、
    /// 只有 15fps。这条经验是从记录方块那边抄过来的，见 `AkashicRecord.DrawEffects`。
    /// </summary>
    public override void DrawEffects(int i, int j, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,
                                     ref Terraria.DataStructures.TileDrawInfo drawData)
    {
        if (Main.dedServ) return;
        if (HexSlateEntity.FindAt(i, j)?.Pattern == null) return;

        Main.instance.TilesRenderer.AddSpecialPoint(i, j,
            Terraria.GameContent.Drawing.TileDrawing.TileCounterType.CustomNonSolid);
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

        bool active = CircleCursor.IsActive(i, j);

        var center = new Microsoft.Xna.Framework.Vector2(i * 16f + 8f, j * 16f + 8f) - Main.screenPosition;

        // 正在执行的那块画得更大更亮，并且带一圈底色，远处也能一眼找到
        var color = active
            ? new Microsoft.Xna.Framework.Color(255, 245, 200)
            : new Microsoft.Xna.Framework.Color(196, 170, 240);

        float radius = active ? 7.5f : 6.0f;

        PatternRenderer.DrawStaticPreview(
            (a, b, w, c) => HexPixel.DrawLine(spriteBatch, a, b, w, c),
            (p, r, c) => HexPixel.DrawDot(spriteBatch, p, r, c),
            pattern,
            center,
            radius,
            color);
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ModContent.GetInstance<HexSlateEntity>().Kill(i, j);
    }

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
        CircleDir.Up => "上（控制流从下方以外进出）",
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
            tag["pattern"] = new PatternIota(Pattern).Serialize()!;
        }
    }

    public override void LoadData(TagCompound tag)
    {
        byte n = tag.ContainsKey("normal") ? tag.GetByte("normal") : (byte)0;
        Normal = n <= (byte)CircleDir.Right ? (CircleDir)n : CircleDir.Up;

        Pattern = null;
        if (tag.ContainsKey("pattern")
            && IotaSerializer.TryDeserialize(tag["pattern"], out var iota)
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
