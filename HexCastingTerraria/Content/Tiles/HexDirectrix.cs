using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Circles;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 导线的公共基类。对应源项目 `BlockEmptyDirectrix` / `BlockBooleanDirectrix` /
/// `BlockRedstoneDirectrix` 三者的共同部分。
///
/// **导线的共同点（逐条抄自源码）**：
///   - **只能沿一个轴传导**：`possibleExitDirections` 只有 `{FACING, FACING.Opposite()}`
///   - **不能沿轴进入**：`enterDir != FACING &amp;&amp; enterDir != FACING.Opposite()`
///     —— 也就是只能从**垂直于轴**的方向进入
///
/// 三者的差别只在「出哪一端」：
///
/// | 导线 | 出口选择 |
/// |---|---|
/// | 空导线 | **随机**（`world.random.nextBoolean()`） |
/// | 布尔导线 | 弹栈顶布尔值：真出 `FACING.Opposite()`，假出 `FACING` |
/// | 红石导线 | 通电出 `FACING`，否则出 `FACING.Opposite()` |
/// </summary>
public abstract class HexDirectrixBase : ModTile
{
    /// <summary>本导线对应的部件种类。</summary>
    public abstract CircleComponentKind Kind { get; }

    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Microsoft.Xna.Framework.Color(100, 84, 130));

        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = new AnchorData(
            AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table,
            TileObjectData.newTile.Width, 0);
        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<HexDirectrixEntity>().Hook_AfterPlacement, -1, 0, false);
        TileObjectData.addTile(Type);
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        var entity = HexDirectrixEntity.FindAt(i, j);
        bool lit = entity is { IsRunning: true };
        if (lit) { r = 0.9f; g = 0.7f; b = 1.0f; }
        else { r = 0.12f; g = 0.08f; b = 0.18f; }
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ModContent.GetInstance<HexDirectrixEntity>().Kill(i, j);
    }

    /// <summary>
    /// 红石触发。移植自源项目 `BlockRedstoneDirectrix` 的 `POWERED` 状态。
    ///
    /// ⚠️ 与 `RightClick` 的关键区别：**`HitWire` 跑在服务端**，
    /// 而 `RightClick` 跑在本地客户端（所以那个要上报）。
    /// 这里直接落地即可，不需要网络层。
    ///
    /// 泰拉不像 MC 那样能直接查「这格当前是否通电」——
    /// 电线信号是瞬时的。所以用「收到信号后保持若干 tick」来近似，
    /// 足以让环在一次传导过程中看到稳定的电平。
    /// </summary>
    public override void HitWire(int i, int j)
    {
        var entity = HexDirectrixEntity.FindAt(i, j);
        if (entity == null) return;

        entity.SetPowered();
        entity.Sync();
    }

    public override bool RightClick(int i, int j)
    {
        var entity = HexDirectrixEntity.FindAt(i, j);
        if (entity == null) return true;

        entity.RotateFacing();

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Content.Net.HexNetSync.RequestDirectrixFacing(i, j, (byte)entity.Facing);
        }
        else
        {
            entity.Sync();
        }

        Main.NewText($"导线轴向：{entity.Facing} ↔ {entity.Facing.Opposite()}");
        return true;
    }
}

/// <summary>空导线：随机出轴的一端。</summary>
public sealed class HexDirectrixEmpty : HexDirectrixBase
{
    public override CircleComponentKind Kind => CircleComponentKind.DirectrixEmpty;
}

/// <summary>布尔导线：弹栈顶布尔值决定出口。</summary>
public sealed class HexDirectrixBoolean : HexDirectrixBase
{
    public override CircleComponentKind Kind => CircleComponentKind.DirectrixBool;
}

/// <summary>红石导线：按红石信号决定出口。</summary>
public sealed class HexDirectrixRedstone : HexDirectrixBase
{
    public override CircleComponentKind Kind => CircleComponentKind.DirectrixRedstone;
}

/// <summary>导线的数据：轴向 + 运行中状态（供发光）。</summary>
public sealed class HexDirectrixEntity : ModTileEntity
{
    /// <summary>传导轴的一端。另一端是它的反方向。</summary>
    public CircleDir Facing { get; private set; } = CircleDir.Right;

    /// <summary>是否正在被环走过（仅供发光显示）。</summary>
    public bool IsRunning { get; set; }

    /// <summary>红石信号是否有效。见 `HitWire` 的说明（泰拉没有直接的「当前通电」查询）。</summary>
    public bool IsPowered { get; private set; }

    /// <summary>供电保持的 tick 数。</summary>
    private int _powerTicks;

    /// <summary>供电保持时长。够环走完一次传导即可。</summary>
    private const int PowerHoldTicks = 20;

    /// <summary>收到红石信号。</summary>
    public void SetPowered()
    {
        IsPowered = true;
        _powerTicks = PowerHoldTicks;
    }

    public override void PostGlobalUpdate()
    {
        if (!IsPowered) return;

        if (--_powerTicks <= 0)
        {
            IsPowered = false;
            Sync();
        }
    }

    public override bool IsTileValidForEntity(int x, int y)
    {
        if (!Main.tile[x, y].HasTile) return false;
        int t = Main.tile[x, y].TileType;
        return t == ModContent.TileType<HexDirectrixEmpty>()
            || t == ModContent.TileType<HexDirectrixBoolean>()
            || t == ModContent.TileType<HexDirectrixRedstone>();
    }

    public static HexDirectrixEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as HexDirectrixEntity
            : null;

    /// <summary>顺时针转 90°（轴向随之改变）。</summary>
    public void RotateFacing()
        => Facing = Facing switch
        {
            CircleDir.Up => CircleDir.Right,
            CircleDir.Right => CircleDir.Down,
            CircleDir.Down => CircleDir.Left,
            _ => CircleDir.Up,
        };

    public void SetFacing(CircleDir dir) => Facing = dir;

    public void Sync()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        tag["facing"] = (byte)Facing;
        // 供电状态**不存档**：读档时信号早已消失，恢复一个「还在通电」的状态是错的
    }

    public override void LoadData(TagCompound tag)
    {
        byte d = tag.ContainsKey("facing") ? tag.GetByte("facing") : (byte)CircleDir.Right;
        Facing = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;
        IsRunning = false;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write((byte)Facing);
        writer.Write(IsRunning);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        byte d = reader.ReadByte();
        Facing = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;
        IsRunning = reader.ReadBoolean();
    }
}
