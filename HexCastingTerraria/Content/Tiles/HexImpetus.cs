using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 法术环对泰拉世界的访问实现。
///
/// 把「读某图格的环部件」这件事从 Core 的逻辑里分离出来 ——
/// Core 侧的闭包校验与走图因此可以离线测试。
///
/// ⚠️ `ForbiddenEntry` 必须在这里正确给出：**原动力与普通部件的规则来源不同**。
/// 普通部件用 `normal` 的反方向；原动力用**起始方向的反方向**。
/// 把两者混为一谈会让「两块大的假环」被判成闭合（这个缺陷是写用例时跑出来的）。
/// </summary>
public sealed class TerrariaCircleWorld : ICircleWorld
{
    /// <summary>原动力的坐标与起始方向。用于给原动力算 ForbiddenEntry。</summary>
    private readonly int _impetusX;
    private readonly int _impetusY;
    private readonly CircleDir _startDir;

    public TerrariaCircleWorld(int impetusX, int impetusY, CircleDir startDir)
    {
        _impetusX = impetusX;
        _impetusY = impetusY;
        _startDir = startDir;
    }

    public CircleComponent? GetComponent(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 1)) return null;

        var tile = Main.tile[x, y];
        if (!tile.HasTile) return null;

        int type = tile.TileType;

        // 原动力
        if (type == ModContent.TileType<HexImpetus>())
        {
            var entity = HexImpetusEntity.FindAt(x, y);
            var dir = entity?.StartDir ?? CircleDir.Right;

            // 原动力是环的**终点**：禁止从「起始方向的反方向」进入
            return CircleComponent.Impetus(dir);
        }

        // 石板
        if (type == ModContent.TileType<HexSlate>())
        {
            var entity = HexSlateEntity.FindAt(x, y);
            var normal = entity?.Normal ?? CircleDir.Up;

            return CircleComponent.Ordinary(CircleComponentKind.Slate, normal);
        }

        // 三根导线
        if (type == ModContent.TileType<HexDirectrixEmpty>()
            || type == ModContent.TileType<HexDirectrixBoolean>()
            || type == ModContent.TileType<HexDirectrixRedstone>())
        {
            var entity = HexDirectrixEntity.FindAt(x, y);
            var facing = entity?.Facing ?? CircleDir.Right;

            var kind = type == ModContent.TileType<HexDirectrixEmpty>()
                ? CircleComponentKind.DirectrixEmpty
                : type == ModContent.TileType<HexDirectrixBoolean>()
                    ? CircleComponentKind.DirectrixBool
                    : CircleComponentKind.DirectrixRedstone;

            return CircleComponent.Directrix(kind, facing);
        }

        return null;
    }

    public Core.Casting.Math.HexPattern? GetSlatePattern(int x, int y)
        => HexSlateEntity.FindAt(x, y)?.Pattern;

    public bool IsPowered(int x, int y) => HexDirectrixEntity.FindAt(x, y)?.IsPowered ?? false;
}

/// <summary>
/// 原动力。对应源项目 `BlockAbstractImpetus` —— **法术环的「CPU」**。
///
/// ⚠️ 它**不含任何图案**：图案在石板上。原动力只负责
/// 媒质池、执行状态、以及触发。
/// </summary>
public sealed class HexImpetus : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Microsoft.Xna.Framework.Color(120, 70, 150));

        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = new AnchorData(
            AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table,
            TileObjectData.newTile.Width, 0);
        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<HexImpetusEntity>().Hook_AfterPlacement, -1, 0, false);
        TileObjectData.addTile(Type);
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 充能时亮起来（对应源项目 ENERGIZED -> lightLevel 15）
        var entity = HexImpetusEntity.FindAt(i, j);
        if (entity is { IsRunning: true })
        {
            r = 0.85f; g = 0.60f; b = 1.00f;
        }
        else
        {
            r = 0.20f; g = 0.12f; b = 0.28f;
        }
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ModContent.GetInstance<HexImpetusEntity>().Kill(i, j);
    }

    /// <summary>
    /// 右击触发：校验闭包 → 开始走环。
    ///
    /// ⚠️ 右击跑在**本地客户端**，而环是服务端权威的 ——
    /// 所以只有服务端（或单机）真正启动，客户端只上报。
    /// </summary>
    public override bool RightClick(int i, int j)
    {
        var entity = HexImpetusEntity.FindAt(i, j);
        if (entity == null) return true;

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Content.Net.HexNetSync.RequestStartCircle(i, j, Main.myPlayer);
            return true;
        }

        entity.TryStart(Main.LocalPlayer);
        return true;
    }
}

/// <summary>原动力的数据：媒质池 + 起始方向 + 走环状态。</summary>
public sealed class HexImpetusEntity : ModTileEntity
{
    /// <summary>
    /// 媒质池。**负数 = 无限**（源项目约定：`if (mediaAvailable < 0) return 0;`）。
    /// 不保留这个约定的话负数会被当成「欠费」，环直接跑不动。
    /// </summary>
    public long Media { get; private set; } = MediaConstants.CrystalUnit * 10;

    /// <summary>媒质流出的方向。决定原动力的 ForbiddenEntry。</summary>
    public CircleDir StartDir { get; private set; } = CircleDir.Right;

    /// <summary>是否正在走环。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>当前走到哪一格。</summary>
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }

    /// <summary>进入当前格的方向。</summary>
    private CircleDir _enteredFrom;

    /// <summary>已走过的格数（用于加速曲线与显示）。</summary>
    public int ReachedCount { get; private set; }

    /// <summary>距离下一格的 tick 计数。</summary>
    private int _tickCounter;

    /// <summary>环的包围盒（范围判定要用）。</summary>
    private CircleClosure? _closure;

    /// <summary>
    /// 环的执行状态（VM）。**整个栈随走随传** —— 对应源项目的 `currentImage`。
    ///
    /// 前面石板压的栈，后面的石板能读到 —— 这是法术环能「编程」的基础。
    /// </summary>
    private CastingVM? _vm;

    /// <summary>环的施法环境（媒质取自本原动力、范围 = 包围盒、无施法者）。</summary>
    private CircleCastingEnvironment? _env;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<HexImpetus>();

    public static HexImpetusEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
            ? te as HexImpetusEntity
            : null;

    public void RotateStartDir()
        => StartDir = StartDir switch
        {
            CircleDir.Up => CircleDir.Right,
            CircleDir.Right => CircleDir.Down,
            CircleDir.Down => CircleDir.Left,
            _ => CircleDir.Up,
        };

    public void SetStartDir(CircleDir dir) => StartDir = dir;

    /// <summary>媒质支取。返回**还未付清**的量。对应源项目 `extractMediaEnvironment`。</summary>
    public long ExtractMedia(long cost, bool simulate)
    {
        if (cost <= 0) return 0;

        // 调试开关：环不消耗媒质。
        // 放在最前面 —— 测长环时媒质会先耗尽，那之后所有石板都报「媒质不足」，
        // 根本测不到后面的图案。
        if (HexClientConfig.Instance.FreeSpellCircles) return 0;

        // 负数 = 无限媒质（源项目约定）
        if (Media < 0) return 0;

        long take = System.Math.Min(cost, Media);
        if (!simulate) Media -= take;
        return cost - take;
    }

    public void AddMedia(long amount)
    {
        if (Media < 0) return;
        Media += amount;
    }

    /// <summary>
    /// 校验闭包并开始走环。
    /// 校验失败会把原因告诉玩家 —— 否则玩家只会觉得「右键没反应」。
    /// </summary>
    public void TryStart(Player caster)
    {
        var world = new TerrariaCircleWorld(Position.X, Position.Y, StartDir);
        var closure = CircleTraversal.Validate(world, Position.X, Position.Y, StartDir);

        if (!closure.IsClosed)
        {
            string msg = closure.Error switch
            {
                CircleClosureError.NoExits => "原动力旁边没有环部件",
                CircleClosureError.TooLong => $"环太长（超过 {CircleTraversal.DefaultMaxLength} 格）",
                _ => "环没有闭合（控制流回不到原动力）",
            };
            Main.NewText($"法术环启动失败：{msg}");
            return;
        }

        _closure = closure;

        // 建立环的施法环境：媒质取自本原动力、范围 = 包围盒、**无施法者**
        var circleWorld = new HexSpaceWorld(TerrariaCastingWorld.ForCircle(
            closure.MinX, closure.MinY, closure.MaxX, closure.MaxY));

        // 给法术看的坐标一律是法术坐标（Y 朝上，见 HexSpaceWorld）：上下翻转后，泰拉的 MaxY 变成最小的那个
        var state = new CircleState
        {
            ImpetusX = Position.X,
            ImpetusY = HexSpaceWorld.BlockY(Position.Y),
            ImpetusDir = StartDir,
            YUp = true,
            MinX = closure.MinX, MinY = HexSpaceWorld.BlockY(closure.MaxY),
            MaxX = closure.MaxX, MaxY = HexSpaceWorld.BlockY(closure.MinY),
        };

        _env = new CircleCastingEnvironment(circleWorld, state, ExtractMedia);
        _vm = CastingVM.Empty(_env);

        IsRunning = true;
        ReachedCount = 0;
        _tickCounter = 0;

        // 从起始方向的相邻格开始
        var (sx, sy) = StartDir.Offset(Position.X, Position.Y);
        CurrentX = sx;
        CurrentY = sy;
        _enteredFrom = StartDir;

        Main.NewText($"法术环启动（{closure.Reached.Count} 格）");
        Sync();
    }

    /// <summary>
    /// 每 tick 由 <see cref="PostGlobalUpdate"/> 调用，推进一环。
    ///
    /// 对应源项目的 `tickExecution` + `getTickSpeed`：
    /// **一格一格走**，且**环走得越深越快**（起步 10 MC 刻/格 = 30 帧，最低 2 刻 = 6 帧）。
    /// </summary>
    public override void PostGlobalUpdate()
    {
        if (!IsRunning) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;   // 服务端权威

        // 调试开关：环每 tick 走一格，用来快速验证长环
        // （正常是 10 → 2 tick，环越长越快；长环测一次要等十几秒）
        // 源项目：走完一格后按「已走格数」排下一次（tickExecution → scheduleTick(getTickSpeed())）
        int speed = HexClientConfig.Instance.FastSpellCircles
            ? 1
            : CircleTraversal.TickSpeedFrames(ReachedCount);

        if (++_tickCounter < speed)
        {
            return;
        }
        _tickCounter = 0;

        StepOnce();
    }

    /// <summary>走一格。移植自源项目 `CircleExecutionState.tick`。</summary>
    private void StepOnce()
    {
        var world = new TerrariaCircleWorld(Position.X, Position.Y, StartDir);
        var comp = world.GetComponent(CurrentX, CurrentY);

        if (comp == null || comp.Value.Kind == CircleComponentKind.None)
        {
            Stop("环上有一格不是部件了");
            return;
        }

        // 表现层：标出「现在跑到这一格」。
        // 放在最前面 —— 后面任何一条失败分支都会 Stop，而玩家最需要看到的
        // 恰恰是「它卡在哪一格」。
        CircleCursor.MarkAndBroadcast(CurrentX, CurrentY);

        // 环每走一格轻响一下（原版 spellcircle.find_block）
        {
            var at = new Microsoft.Xna.Framework.Vector2(
                (CurrentX + 0.5f) * Core.Casting.HexUnits.PixelsPerTile,
                (CurrentY + 0.5f) * Core.Casting.HexUnits.PixelsPerTile);
            if (Main.netMode == NetmodeID.Server) SpellSounds.Broadcast("spellcircle.find_block", at.X, at.Y);
            else SpellSounds.Play("spellcircle.find_block", at);
        }

        ReachedCount++;

        // 走到原动力 = 环闭合，正常结束
        // （源项目里原动力的 acceptControlFlow 返回 Stop）
        if (comp.Value.Kind == CircleComponentKind.Impetus)
        {
            Stop("法术环完成");
            return;
        }

        // 执行石板上的图案（空石板直通）。
        // ⚠️ 这是法术环的核心：**图案在石板上**，不是原动力上。
        // 源项目 BlockSlate.acceptControlFlow 就是这么做的。
        var slatePattern = world.GetSlatePattern(CurrentX, CurrentY);
        if (slatePattern != null && _vm != null)
        {
            var outcome = _vm.QueueExecute(_vm.Image,
                new Iota[] { new Core.Casting.Iotas.PatternIota(slatePattern) });

            // 石板图案的粒子（环跑在服务端，所以要广播）
            SpellSounds.EmitEval(outcome.Sound, new Microsoft.Xna.Framework.Vector2(
                (CurrentX + 0.5f) * Core.Casting.HexUnits.PixelsPerTile,
                (CurrentY + 0.5f) * Core.Casting.HexUnits.PixelsPerTile));

            SpellVisuals.Broadcast(outcome.Particles,
                (CurrentX + 0.5f) * Core.Casting.HexUnits.PixelsPerTile,
                (CurrentY + 0.5f) * Core.Casting.HexUnits.PixelsPerTile);

            if (!outcome.ResolutionType.IsSuccess())
            {
                Stop($"({CurrentX},{CurrentY}) 的图案执行失败：{outcome.ResolutionType}");
                return;
            }

            // ⚠️ 每走完一格**重置算力**（源项目 withOverriddenUsedOps(0)）。
            // 不重置的话长环跑到一半就会算力耗尽中断。
            _vm.SetImage(outcome.Image.WithOverriddenUsedOps(0));
        }

        // 导线：出口由导线自己决定，**不走「恰好 1 个」的判定**。
        // 源项目里导线的 acceptControlFlow 直接返回唯一的出口，与石板的语义不同。
        if (comp.Value.Kind is CircleComponentKind.DirectrixEmpty
            or CircleComponentKind.DirectrixBool
            or CircleComponentKind.DirectrixRedstone)
        {
            var picked = PickDirectrixExit(comp.Value, world, out bool shouldStop);
            if (shouldStop)
            {
                Stop($"({CurrentX},{CurrentY}) 的导线无法决定出口");
                return;
            }

            if (picked == null)
            {
                Stop($"({CurrentX},{CurrentY}) 的导线出口方向被堵住");
                return;
            }

            var (dx2, dy2) = picked.Value.Offset(CurrentX, CurrentY);
            CurrentX = dx2;
            CurrentY = dy2;
            _enteredFrom = picked.Value;
            Sync();
            return;
        }

        // 石板/原动力：出口判定 —— **恰好 1 个**（源项目语义）
        CircleDir? found = null;
        int validCount = 0;

        foreach (var dir in CircleTraversal.ExitDirections(comp.Value, _enteredFrom))
        {
            var (nx, ny) = dir.Offset(CurrentX, CurrentY);
            var next = world.GetComponent(nx, ny);

            if (next == null) continue;
            if (!next.Value.AllowedEntries.Has(dir)) continue;

            validCount++;
            found = dir;
        }

        if (validCount == 0)
        {
            Stop($"({CurrentX},{CurrentY}) 没有可走出口");
            return;
        }

        if (validCount > 1)
        {
            // 源项目报 many_exits：出口歧义会让控制流无法确定去向
            Stop($"({CurrentX},{CurrentY}) 有 {validCount} 个可用出口，必须恰好 1 个");
            return;
        }

        // 前进
        var (fx, fy) = found!.Value.Offset(CurrentX, CurrentY);
        CurrentX = fx;
        CurrentY = fy;
        _enteredFrom = found.Value;

        // ⚠️ 每走完一格重置算力（源项目 `withOverriddenUsedOps(0)`）。
        // 不重置的话长环跑到一半就会算力耗尽中断。
        // 算力计数在 CastingImage 里 —— 环的执行环境每次新建 image，
        // 所以这里天然是重置状态；等接上真正的 VM 求值时要在那里显式清零。

        Sync();
    }

    /// <summary>
    /// 导线决定出口。移植自源项目三根导线的 `acceptControlFlow`。
    ///
    /// 三者的差别只在这里：
    ///   空导线   → **随机**出轴的一端
    ///   布尔导线 → 弹栈顶布尔值，真出 `Facing.Opposite()`、假出 `Facing`
    ///   红石导线 → 通电出 `Facing`，否则 `Facing.Opposite()`
    /// </summary>
    private CircleDir? PickDirectrixExit(CircleComponent comp, ICircleWorld world, out bool shouldStop)
    {
        shouldStop = false;

        CircleDir chosen = comp.Kind switch
        {
            CircleComponentKind.DirectrixEmpty =>
                Main.rand.NextBool() ? comp.Facing : comp.Facing.Opposite(),

            CircleComponentKind.DirectrixRedstone =>
                world.IsPowered(CurrentX, CurrentY) ? comp.Facing : comp.Facing.Opposite(),

            CircleComponentKind.DirectrixBool => PickByBool(comp, ref shouldStop),

            _ => comp.Facing,
        };

        if (shouldStop) return null;

        var (nx, ny) = chosen.Offset(CurrentX, CurrentY);
        var next = world.GetComponent(nx, ny);
        if (next == null) return null;
        if (!next.Value.AllowedEntries.Has(chosen)) return null;

        return chosen;
    }

    /// <summary>布尔导线：弹栈顶。</summary>
    private CircleDir PickByBool(CircleComponent comp, ref bool shouldStop)
    {
        if (_vm == null) { shouldStop = true; return comp.Facing; }

        var stack = new List<Iota>(_vm.Image.Stack);
        if (stack.Count == 0)
        {
            // 源项目 MishapBoolDirectrixEmptyStack：惩罚是把这根导线打掉（destroyBlock(pos, true)，掉落物品）
            CircleMessages.Post($"({CurrentX},{CurrentY}) 布尔导线：栈是空的");
            BreakDirectrix();
            shouldStop = true;
            return comp.Facing;
        }

        var top = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);

        if (top is not BooleanIota b)
        {
            // 源项目 MishapBoolDirectrixNotBool：同样把导线打掉
            CircleMessages.Post($"({CurrentX},{CurrentY}) 布尔导线：栈顶不是布尔值");
            BreakDirectrix();
            shouldStop = true;
            return comp.Facing;
        }

        // 弹掉栈顶（源项目用 imageOut 承载减过元素的栈）
        _vm.SetImage(_vm.Image.WithStack(stack));

        // 真 -> Facing.Opposite()；假 -> Facing
        return b.Value ? comp.Facing.Opposite() : comp.Facing;
    }

    /// <summary>布尔导线出错的惩罚：打掉当前这一格并掉落（源项目 world.destroyBlock(pos, true)）。</summary>
    private void BreakDirectrix()
    {
        WorldGen.KillTile(CurrentX, CurrentY, fail: false, effectOnly: false, noItem: false);
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, CurrentX, CurrentY);
        }
    }

    private void Stop(string reason)
    {
        IsRunning = false;
        _closure = null;
        _vm = null;
        _env = null;

        // 表现层：环停了就把游标高亮清掉。
        // 清不掉的话那格会一直亮着，玩家会以为环还在跑。
        CircleCursor.Clear();

        // 环停了也给个音（原版 spellcircle.fail）
        if (Main.netMode == NetmodeID.Server) SpellSounds.Broadcast("spellcircle.fail", Position.X * 16f, Position.Y * 16f);
        else SpellSounds.Play("spellcircle.fail", new Microsoft.Xna.Framework.Vector2(Position.X * 16f + 8f, Position.Y * 16f + 8f));

        Main.NewText($"法术环停止：{reason}");
        Sync();
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
        tag["media"] = Media;
        tag["dir"] = (byte)StartDir;
    }

    public override void LoadData(TagCompound tag)
    {
        Media = tag.ContainsKey("media") ? tag.GetLong("media") : 0L;
        byte d = tag.ContainsKey("dir") ? tag.GetByte("dir") : (byte)CircleDir.Right;
        StartDir = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;

        // 走环状态**不存档**：世界重载后控制流位置已无意义，
        // 强行恢复会得到一个「停在半路」的环，比重新开始更糟。
        IsRunning = false;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write(Media);
        writer.Write((byte)StartDir);
        writer.Write(IsRunning);
        writer.Write((short)CurrentX);
        writer.Write((short)CurrentY);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        Media = reader.ReadInt64();
        byte d = reader.ReadByte();
        StartDir = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;
        IsRunning = reader.ReadBoolean();
        CurrentX = reader.ReadInt16();
        CurrentY = reader.ReadInt16();
    }
}
