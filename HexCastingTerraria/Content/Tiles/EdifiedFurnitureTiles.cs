using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent.ObjectInteractions;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace HexCastingTerraria.Content.Tiles;

// 启迪木的家具（原版 HexBlocks：edified_door / fence / button / pressure_plate），做成泰拉里对应的东西：
//   门 → 门（关 1×3、开 2×3）；栅栏 → 栅栏墙（泰拉的栅栏就是墙）；按钮 → 开关（点一下给电线一个信号）；压力板 → 压力板。
// 泰拉没有对应物的不做：栅栏门、活板门（泰拉的活板门开合写死在原版代码里，没有模组接口）；
// 楼梯 / 台阶靠启迪木板合成的平台与锤出来的半砖（见 Content/Items/EdifiedFurniture.cs）。
// 贴图由 _tools/gen_textures.py 从原版贴图生成。

/// <summary>启迪木门（关）。原版 edified_door（BlockHexDoor，木门：手就能开）。</summary>
public sealed class EdifiedDoorClosed : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileSolid[Type] = true;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;
        TileID.Sets.NotReallySolid[Type] = true;
        TileID.Sets.DrawsWalls[Type] = true;
        TileID.Sets.HasOutlines[Type] = true;
        TileID.Sets.DisableSmartCursor[Type] = true;
        TileID.Sets.OpenDoorID[Type] = ModContent.TileType<EdifiedDoorOpen>();
        TileID.Sets.RoomNeeds.CountsAsDoor[Type] = true;   // 1.4.5：这是 BoolListSet，不再是数组（ExampleMod 1.4.4 的 AddToArray 写法编不过）

        DustType = DustID.Shadewood;
        AdjTiles = new int[] { TileID.ClosedDoor };
        AddMapEntry(new Color(84, 57, 138), CreateMapEntryName());

        TileObjectData.newTile.CopyFrom(TileObjectData.GetTileData(TileID.ClosedDoor, 0));
        TileObjectData.addTile(Type);
    }

    public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

    public override void NumDust(int i, int j, bool fail, ref int num) => num = 1;

    public override void MouseOver(int i, int j)
    {
        var player = Main.LocalPlayer;
        player.noThrow = 2;
        player.cursorItemIconEnabled = true;
        player.cursorItemIconID = ModContent.ItemType<Items.EdifiedDoorItem>();
    }
}

/// <summary>启迪木门（开）。</summary>
public sealed class EdifiedDoorOpen : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileLavaDeath[Type] = true;
        Main.tileNoSunLight[Type] = true;
        TileID.Sets.HousingWalls[Type] = true;
        TileID.Sets.HasOutlines[Type] = true;
        TileID.Sets.DisableSmartCursor[Type] = true;
        TileID.Sets.CloseDoorID[Type] = ModContent.TileType<EdifiedDoorClosed>();
        TileID.Sets.DrawTileInSolidLayer[Type] = true;
        TileID.Sets.RoomNeeds.CountsAsDoor[Type] = true;   // 1.4.5：这是 BoolListSet，不再是数组（ExampleMod 1.4.4 的 AddToArray 写法编不过）

        DustType = DustID.Shadewood;
        AdjTiles = new int[] { TileID.OpenDoor };
        RegisterItemDrop(ModContent.ItemType<Items.EdifiedDoorItem>(), 0);
        AddMapEntry(new Color(84, 57, 138), CreateMapEntryName());

        // 开着的门 2×3，往右开 / 往左开两种（同原版木门的物体数据）
        TileObjectData.newTile.CopyFrom(TileObjectData.GetTileData(TileID.OpenDoor, 0));
        TileObjectData.addTile(Type);
    }

    public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

    public override void NumDust(int i, int j, bool fail, ref int num) => num = 1;

    public override void MouseOver(int i, int j)
    {
        var player = Main.LocalPlayer;
        player.noThrow = 2;
        player.cursorItemIconEnabled = true;
        player.cursorItemIconID = ModContent.ItemType<Items.EdifiedDoorItem>();
    }
}

/// <summary>启迪木栅栏。原版 edified_fence 是方块；泰拉的栅栏（木栅栏等）是**墙**，照泰拉的做。</summary>
public sealed class EdifiedFence : ModWall
{
    public override void SetStaticDefaults()
    {
        Main.wallHouse[Type] = true;
        DustType = DustID.Shadewood;
        AddMapEntry(new Color(70, 48, 115));
    }
}

/// <summary>
/// 启迪木按钮。原版 edified_button（木按钮：按一下发一个短红石脉冲）。
/// 泰拉的电线是「来一个信号就切换一次」，所以按一下 = 给电线一个信号，和泰拉的开关一样；贴附规则照泰拉开关。
/// </summary>
public sealed class EdifiedButton : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;
        TileID.Sets.HasOutlines[Type] = true;
        TileID.Sets.DisableSmartCursor[Type] = true;
        DustType = DustID.Shadewood;
        AddMapEntry(new Color(84, 57, 138), CreateMapEntryName());

        TileObjectData.newTile.CopyFrom(TileObjectData.GetTileData(TileID.Switches, 0));
        TileObjectData.addTile(Type);
    }

    public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

    public override void MouseOver(int i, int j)
    {
        var player = Main.LocalPlayer;
        player.noThrow = 2;
        player.cursorItemIconEnabled = true;
        player.cursorItemIconID = ModContent.ItemType<Items.EdifiedButtonItem>();
    }

    public override bool RightClick(int i, int j)
    {
        SoundEngine.PlaySound(SoundID.Mech, new Vector2(i * 16, j * 16));
        EdifiedWiring.Trigger(i, j);
        return true;
    }
}

/// <summary>
/// 启迪木压力板。原版 edified_pressure_plate（木压力板：任何实体都能踩）。
/// 泰拉：玩家或敌怪 / 小动物踩上去的那一刻给电线一个信号（和泰拉的压力板一样只在踩上时触发）。
/// </summary>
public sealed class EdifiedPressurePlate : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;
        DustType = DustID.Shadewood;
        AddMapEntry(new Color(84, 57, 138), CreateMapEntryName());

        TileObjectData.newTile.CopyFrom(TileObjectData.GetTileData(TileID.PressurePlates, 0));
        TileObjectData.addTile(Type);
    }
}

/// <summary>按钮 / 压力板给电线信号，以及压力板的「踩上去」检测。</summary>
public sealed class EdifiedWiring : ModSystem
{
    /// <summary>每个实体上一刻踩着的压力板（没踩 = null）：只在踩上去的那一刻触发。</summary>
    private readonly Dictionary<int, Point?> _playerOn = new();
    private readonly Dictionary<int, Point?> _npcOn = new();

    /// <summary>
    /// 给 (i, j) 所在的电线一个信号。原版开关的做法：本地执行，再让其他端也执行一遍
    /// （泰拉的 Wiring.HitSwitch 只认原版开关的类型，模组方块要直接 TripWire）。
    /// </summary>
    public static void Trigger(int i, int j)
    {
        Wiring.TripWire(i, j, 1, 1);
        if (Main.netMode == NetmodeID.SinglePlayer) return;
        Send(i, j, -1, -1);
    }

    private static void Send(int i, int j, int toWho, int ignoreWho)
    {
        var packet = HexCastingTerraria.Instance!.GetPacket();
        packet.Write((byte)Net.HexMessage.TripWire);
        packet.Write((short)i);
        packet.Write((short)j);
        packet.Send(toWho, ignoreWho);
    }

    /// <summary>收到 <see cref="Net.HexMessage.TripWire"/>：执行；服务端再转给其他客户端。</summary>
    public static void Receive(System.IO.BinaryReader reader, int whoAmI)
    {
        int i = reader.ReadInt16(), j = reader.ReadInt16();
        if (!WorldGen.InWorld(i, j)) return;
        if (Main.netMode == NetmodeID.Server)
        {
            Wiring.SetCurrentUser(whoAmI);
            Wiring.TripWire(i, j, 1, 1);
            Wiring.SetCurrentUser();
            Send(i, j, -1, whoAmI);
        }
        else
        {
            Wiring.TripWire(i, j, 1, 1);
        }
    }

    public override void PostUpdateEverything()
    {
        int plate = ModContent.TileType<EdifiedPressurePlate>();

        // 玩家：由本人客户端检测（单人同理）
        if (Main.netMode != NetmodeID.Server && Main.LocalPlayer is { active: true, dead: false } me)
        {
            Step(_playerOn, me.whoAmI, PlateUnder(me.Hitbox, plate));
        }

        // 敌怪 / 小动物：由服务端（或单人）检测
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            for (int n = 0; n < Main.maxNPCs; n++)
            {
                var npc = Main.npc[n];
                Step(_npcOn, n, npc.active && !npc.noGravity && !npc.noTileCollide ? PlateUnder(npc.Hitbox, plate) : null);
            }
        }
    }

    private static void Step(Dictionary<int, Point?> last, int who, Point? now)
    {
        last.TryGetValue(who, out var before);
        if (now is { } p && before != now)
        {
            SoundEngine.PlaySound(SoundID.Mech, p.ToWorldCoordinates());
            Trigger(p.X, p.Y);
        }
        last[who] = now;
    }

    /// <summary>实体脚下那一排（身体最下面一格）有没有压力板。</summary>
    private static Point? PlateUnder(Rectangle box, int plate)
    {
        int y = (box.Bottom - 1) / 16;
        for (int x = box.Left / 16; x <= (box.Right - 1) / 16; x++)
        {
            if (!WorldGen.InWorld(x, y)) continue;
            var t = Main.tile[x, y];
            if (t.HasTile && t.TileType == plate) return new Point(x, y);
        }
        return null;
    }
}
