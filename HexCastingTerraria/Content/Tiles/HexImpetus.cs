using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
/// ⚠️ `ForbiddenEntry` 必须在这里正确给出：**促动石与普通部件的规则来源不同**。
/// 普通部件用 `normal` 的反方向；促动石用**出口方向的反方向**。
/// </summary>
public sealed class TerrariaCircleWorld : ICircleWorld
{
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

        // 促动石（三种会启动的 + 空白促动石）
        if (TileLoader.GetTile(tile.TileType) is HexImpetusBase impetus)
        {
            var dir = HexImpetusEntity.FindAt(x, y)?.StartDir ?? CircleDir.Right;
            return impetus.Kind == ImpetusKind.Empty
                ? CircleComponent.EmptyImpetus(dir)
                : CircleComponent.Impetus(dir);
        }

        int type = tile.TileType;

        // 石板
        if (type == ModContent.TileType<HexSlate>())
        {
            var entity = HexSlateEntity.FindAt(x, y);
            var normal = entity?.Normal ?? CircleDir.Up;

            return CircleComponent.Ordinary(CircleComponentKind.Slate, normal);
        }

        // 三种导向石
        if (TileLoader.GetTile(type) is HexDirectrixBase directrix)
        {
            var facing = HexDirectrixEntity.FindAt(x, y)?.Facing ?? CircleDir.Right;
            return CircleComponent.Directrix(directrix.Kind, facing);
        }

        return null;
    }

    public Core.Casting.Math.HexPattern? GetSlatePattern(int x, int y)
        => HexSlateEntity.FindAt(x, y)?.Pattern;

    public bool IsPowered(int x, int y) => HexDirectrixEntity.FindAt(x, y)?.IsPowered ?? false;
}

/// <summary>促动石的种类（原版 impetus/empty、rightclick、look、redstone）。</summary>
public enum ImpetusKind : byte
{
    /// <summary>空白促动石：「其实不是促动石」，只传导；脑叶切除的原料。</summary>
    Empty,

    /// <summary>工具匠促动石：不潜行时右键启动。</summary>
    RightClick,

    /// <summary>制箭师促动石：被盯着看 30 刻（1.5 秒）后启动。</summary>
    Look,

    /// <summary>牧师促动石：收到信号（泰拉：电线）时启动，施法者 = 绑定的玩家。</summary>
    Redstone,
}

/// <summary>
/// 促动石。对应源项目 `BlockAbstractImpetus` 及三个子类 + `BlockEmptyImpetus` —— **法术环的「CPU」**。
///
/// 它**不含任何图案**（图案在石板上），只负责媒质、执行状态、以及触发。与原版逐条对照：
///
/// | | 原版 | 泰拉 |
/// |---|---|---|
/// | 出口方向 | 放置时按视线方向（潜行反过来），之后不能改 | 同：按鼠标相对角色的方向取最近的上下左右，潜行反过来 |
/// | 媒质 | 新放的是 0，用漏斗等容器塞媒质物品进去（整件抽干） | 泰拉没有漏斗：**拿着媒质物品右键**塞进去，规则相同 |
/// | 触发 | 工具匠：不潜行右键；制箭师：被盯着 30 刻；牧师：红石信号上升沿 | 工具匠同；制箭师：视线（鼠标方向）射线打中它；牧师：电线信号 |
/// | 施法者 | 启动它的玩家 / 牧师绑定的玩家 | 同 |
/// | 消息 | 显示在促动石上，探知透镜里看 | 同（见 Client/ScryingOverlay.cs） |
/// </summary>
public abstract class HexImpetusBase : ModTile
{
    public abstract ImpetusKind Kind { get; }

    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(120, 70, 150));

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
        if (HexImpetusEntity.FindAt(i, j) is { IsRunning: true })
        {
            r = 0.85f; g = 0.60f; b = 1.00f;
        }
        else
        {
            r = 0.20f; g = 0.12f; b = 0.28f;
        }
    }

    /// <summary>贴图第 2 帧（x = 18）是亮着的样子（原版 *_lit.png），运行时切过去。</summary>
    public override void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset)
    {
        if (HexImpetusEntity.FindAt(i, j) is { IsRunning: true }) frameXOffset = 18;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ModContent.GetInstance<HexImpetusEntity>().Kill(i, j);
    }

    /// <summary>原版 placeStateDirAndSneak：出口 = 放置时视线最接近的方向，潜行反过来。</summary>
    public override void PlaceInWorld(int i, int j, Item item)
        => HexImpetusEntity.Request(i, j, ImpetusAction.SetDir, (byte)CircleFacing.FromPlacement(Main.LocalPlayer));

    /// <summary>2D 看不见「哪一面是正面」，画一个小箭头标出媒质流出的方向。</summary>
    public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
        => CircleFacing.DrawArrow(spriteBatch, i, j, HexImpetusEntity.FindAt(i, j)?.StartDir ?? CircleDir.Right);

    public override bool RightClick(int i, int j)
    {
        if (Kind == ImpetusKind.Empty) return false;
        var player = Main.LocalPlayer;
        var held = player.HeldItem;

        // 泰拉没有漏斗：拿着媒质物品右键 = 把它塞进去（原版漏斗 → insertMedia）
        if (HexImpetusEntity.IsMediaItem(held))
        {
            HexImpetusEntity.Request(i, j, ImpetusAction.InsertMedia, (byte)player.selectedItem);
            return true;
        }

        switch (Kind)
        {
            case ImpetusKind.RightClick:
                // 原版 BlockRightClickImpetus.use：`if (!pPlayer.isShiftKeyDown())`
                if (HexPlayer.ShiftHeld()) return false;
                HexImpetusEntity.Request(i, j, ImpetusAction.Start, 0);
                return true;

            case ImpetusKind.Redstone:
                // 原版：空手潜行右键 = 解绑；拿着存有玩家的载体右键 = 绑定那个玩家
                if (held.IsAir && HexPlayer.ShiftHeld())
                {
                    HexImpetusEntity.Request(i, j, ImpetusAction.ClearBinding, 0);
                    return true;
                }
                if (held.ModItem is Items.ItemIotaStorage storage
                    && storage.Read() is EntityIota { Target: EntityIota.EntityKind.Player } who
                    && who.Index >= 0 && who.Index < Main.maxPlayers && Main.player[who.Index] is { active: true })
                {
                    HexImpetusEntity.Request(i, j, ImpetusAction.Bind, (byte)who.Index);
                    return true;
                }
                return false;
        }
        return false;
    }

    /// <summary>牧师促动石：电线信号 = 原版的红石上升沿（泰拉的电线本来就是一次脉冲）。跑在服务端 / 单机。</summary>
    public override void HitWire(int i, int j)
    {
        if (Kind != ImpetusKind.Redstone) return;
        if (HexImpetusEntity.FindAt(i, j) is { } entity) entity.TryStart(entity.BoundPlayer());
    }
}

/// <summary>工具匠促动石（原版 impetus/rightclick）。类名沿用旧的，已有世界里放着的不会丢。</summary>
public sealed class HexImpetus : HexImpetusBase
{
    public override ImpetusKind Kind => ImpetusKind.RightClick;
}

/// <summary>制箭师促动石（原版 impetus/look）。</summary>
public sealed class HexImpetusLook : HexImpetusBase
{
    public override ImpetusKind Kind => ImpetusKind.Look;
}

/// <summary>牧师促动石（原版 impetus/redstone）。</summary>
public sealed class HexImpetusRedstone : HexImpetusBase
{
    public override ImpetusKind Kind => ImpetusKind.Redstone;
}

/// <summary>空白促动石（原版 impetus/empty）。</summary>
public sealed class HexImpetusEmpty : HexImpetusBase
{
    public override ImpetusKind Kind => ImpetusKind.Empty;
}

/// <summary>客户端 → 服务端的促动石操作。</summary>
public enum ImpetusAction : byte
{
    SetDir,
    InsertMedia,
    Start,
    Bind,
    ClearBinding,
}

/// <summary>促动石上显示的那一行的图标（原版 postDisplay 的 displayItem）。</summary>
public enum ImpetusDisplay : byte
{
    None,
    Print,      // 书（postPrint）
    Mishap,     // 唱片 11（postMishap）
    NoExit,     // 告示牌
    NoClosure,  // 拴绳
}

/// <summary>促动石的数据：媒质、出口方向、走环状态、显示、牧师绑定。</summary>
public sealed class HexImpetusEntity : ModTileEntity
{
    /// <summary>原版 MAX_CAPACITY。</summary>
    public const long MaxCapacity = 9_000_000_000_000_000_000L;

    /// <summary>
    /// 媒质。**负数 = 无限**（源项目约定）。新放的促动石是 0（原版 `protected long media = 0`）——
    /// 这里曾经白送 10 晶体，而且没有任何办法往里加。
    /// </summary>
    public long Media { get; private set; }

    /// <summary>媒质流出的方向。</summary>
    public CircleDir StartDir { get; private set; } = CircleDir.Right;

    public bool IsRunning { get; private set; }

    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }

    private CircleDir _enteredFrom;

    public int ReachedCount { get; private set; }

    private int _tickCounter;

    /// <summary>原版 displayMsg / displayItem。</summary>
    public string? DisplayMsg { get; private set; }

    public ImpetusDisplay DisplayIcon { get; private set; }

    /// <summary>牧师促动石绑定的玩家名（原版存 GameProfile；泰拉按名字认人）。</summary>
    public string? BoundName { get; private set; }

    private CastingVM? _vm;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && TileLoader.GetTile(Main.tile[x, y].TileType) is HexImpetusBase;

    public static HexImpetusEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) ? te as HexImpetusEntity : null;

    public ImpetusKind? Kind => TileLoader.GetTile(Main.tile[Position.X, Position.Y].TileType) is HexImpetusBase b ? b.Kind : null;

    /// <summary>牧师绑定的玩家（在线才算；原版 getStoredPlayer 同样要求在线）。</summary>
    public Player? BoundPlayer()
    {
        if (BoundName is null) return null;
        foreach (var p in Main.player)
        {
            if (p is { active: true } && p.name == BoundName) return p;
        }
        return null;
    }

    // ── 媒质 ───────────────────────────────────────────────────────

    /// <summary>媒质支取。返回**还未付清**的量。对应源项目 CircleCastEnv.extractMediaEnvironment。</summary>
    public long ExtractMedia(long cost, bool simulate)
    {
        if (cost <= 0) return 0;

        // 调试开关：环不消耗媒质
        if (HexClientConfig.Instance.FreeSpellCircles) return 0;

        if (Media < 0) return 0;   // 无限

        long take = System.Math.Min(cost, Media);
        if (!simulate) { Media -= take; }
        return cost - take;
    }

    /// <summary>能塞进促动石的东西：媒质材料、媒质之瓶（原版 extractMedia(stack, drainForBatteries = true) > 0）。</summary>
    public static bool IsMediaItem(Item item)
        => !item.IsAir && (item.ModItem is Items.MediaMaterial || item.ModItem is Items.MediaFlask { Media: > 0 });

    /// <summary>
    /// 原版 insertMedia：`extractMedia(stack, remainingCapacity, drainForBatteries = true)`，
    /// 材料按整件抽（容量几乎无限，等于整堆抽干），瓶子按量抽。物品归本人客户端 —— 走 PlayerEffects。
    /// </summary>
    private void InsertFrom(Player p, int slot)
    {
        if (Media < 0 || slot < 0 || slot >= p.inventory.Length) return;
        var item = p.inventory[slot];
        long room = MaxCapacity - Media;
        switch (item.ModItem)
        {
            case Items.MediaMaterial m:
            {
                int n = (int)System.Math.Min(item.stack, room / m.MediaValue);
                if (n <= 0) return;
                PlayerEffects.ConsumeSlot(p, slot, n);
                Media += n * m.MediaValue;
                break;
            }
            case Items.MediaFlask f:
            {
                long take = System.Math.Min(f.Media, room);
                if (take <= 0) return;
                PlayerEffects.BatteryDelta(p, slot, -take);
                Media += take;
                break;
            }
            default:
                return;
        }
        Sync();
    }

    // ── 显示（原版 postDisplay）────────────────────────────────────

    private void Display(string? msg, ImpetusDisplay icon)
    {
        DisplayMsg = msg;
        DisplayIcon = msg is null ? ImpetusDisplay.None : icon;
        Sync();
    }

    /// <summary>坐标按法术坐标显示（+Y 朝上），和法术里拿到的一致。</summary>
    private static string Pos(int x, int y) => $"({x}, {HexSpaceWorld.BlockY(y)})";

    // ── 启动 ───────────────────────────────────────────────────────

    /// <summary>
    /// 原版 startExecution(player)：已在运行就不管；闭包校验失败 → 显示原因 + 失败音；
    /// 成功 → 清掉旧显示，开始走环。施法者可以是 null（没人绑定的牧师促动石）。
    /// </summary>
    public void TryStart(Player? caster)
    {
        if (IsRunning || Kind is null or ImpetusKind.Empty) return;

        var world = new TerrariaCircleWorld(Position.X, Position.Y, StartDir);
        var closure = CircleTraversal.Validate(world, Position.X, Position.Y, StartDir);

        if (!closure.IsClosed)
        {
            // 原版：出口都没有 → no_exit（显示在促动石处）；回不来 → no_closure（显示在断开处）
            if (closure.Error == CircleClosureError.NoExits)
            {
                Display($"{Pos(Position.X, Position.Y)}处的媒质流无法找到出口", ImpetusDisplay.NoExit);
            }
            else
            {
                Display($"{Pos(closure.ErrorX, closure.ErrorY)}处的媒质流无法返回促动石", ImpetusDisplay.NoClosure);
            }
            PlayFail();
            return;
        }

        var live = caster is { active: true, dead: false } ? caster : null;
        var circleWorld = new HexSpaceWorld(TerrariaCastingWorld.ForCircle(
            closure.MinX, closure.MinY, closure.MaxX, closure.MaxY, live));

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

        var casterEnv = live is null ? null : new PlayerCastingEnvironment(live) { CircleHands = true };
        var env = new CircleCastingEnvironment(circleWorld, state, ExtractMedia, casterEnv,
            (msg, mishap) => Display(msg, mishap ? ImpetusDisplay.Mishap : ImpetusDisplay.Print));
        _vm = CastingVM.Empty(env);

        IsRunning = true;
        ReachedCount = 0;
        _tickCounter = 0;

        var (sx, sy) = StartDir.Offset(Position.X, Position.Y);
        CurrentX = sx;
        CurrentY = sy;
        _enteredFrom = StartDir;

        DisplayMsg = null;
        DisplayIcon = ImpetusDisplay.None;
        Sync();
    }

    // ── 走环 ───────────────────────────────────────────────────────

    /// <summary>
    /// 每帧推进。对应源项目 tickExecution + getTickSpeed：
    /// **一格一格走**，且**环走得越深越快**（起步 10 MC 刻/格 = 30 帧，最低 2 刻 = 6 帧）。
    /// </summary>
    public override void PostGlobalUpdate()
    {
        if (!IsRunning) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;   // 服务端权威

        int speed = HexClientConfig.Instance.FastSpellCircles ? 1 : CircleTraversal.TickSpeedFrames(ReachedCount);
        if (++_tickCounter < speed) return;
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
            Fail($"{Pos(CurrentX, CurrentY)}处的媒质流无法找到出口", ImpetusDisplay.NoExit);
            return;
        }

        CircleCursor.MarkAndBroadcast(CurrentX, CurrentY);

        // 环每走一格轻响一下（原版 spellcircle.find_block）
        var at = new Vector2((CurrentX + 0.5f) * 16f, (CurrentY + 0.5f) * 16f);
        if (Main.netMode == NetmodeID.Server) SpellSounds.Broadcast("spellcircle.find_block", at.X, at.Y);
        else SpellSounds.Play("spellcircle.find_block", at);

        ReachedCount++;

        // 走到促动石 = 环闭合，正常结束（原版促动石的 acceptControlFlow 返回 Stop）
        if (comp.Value.Kind == CircleComponentKind.Impetus)
        {
            End();
            return;
        }

        // 石板：执行上面的图案（空石板直通）。原版 BlockSlate.acceptControlFlow
        var slatePattern = world.GetSlatePattern(CurrentX, CurrentY);
        if (slatePattern != null && _vm != null)
        {
            var outcome = _vm.QueueExecute(_vm.Image, new Iota[] { new PatternIota(slatePattern) });

            SpellSounds.EmitEval(outcome.Sound, at);
            SpellVisuals.Broadcast(outcome.Particles, at.X, at.Y);

            if (!outcome.ResolutionType.IsSuccess())
            {
                // mishap 已经由环境的 PostExecution 显示到促动石上了（原版同样只停下，不另发消息）
                Fail(null, ImpetusDisplay.None);
                return;
            }

            // 每走完一格重置算力（源项目 withOverriddenUsedOps(0)）
            _vm.SetImage(outcome.Image.WithOverriddenUsedOps(0));
        }

        // 导向石：出口由它自己决定
        if (comp.Value.Kind is CircleComponentKind.DirectrixEmpty
            or CircleComponentKind.DirectrixBool
            or CircleComponentKind.DirectrixRedstone)
        {
            var picked = PickDirectrixExit(comp.Value, world, out bool shouldStop);
            if (shouldStop) return;
            if (picked == null)
            {
                Fail($"{Pos(CurrentX, CurrentY)}处的媒质流无法找到出口", ImpetusDisplay.NoExit);
                return;
            }
            Advance(picked.Value);
            return;
        }

        // 石板 / 空白促动石：出口必须**恰好 1 个**（原版 many_exits / no_exit）
        CircleDir? found = null;
        int validCount = 0;
        foreach (var dir in CircleTraversal.ExitDirections(comp.Value, _enteredFrom))
        {
            var (nx, ny) = dir.Offset(CurrentX, CurrentY);
            var next = world.GetComponent(nx, ny);
            if (next == null || !next.Value.AllowedEntries.Has(dir)) continue;
            validCount++;
            found = dir;
        }

        if (validCount == 0)
        {
            Fail($"{Pos(CurrentX, CurrentY)}处的媒质流无法找到出口", ImpetusDisplay.NoExit);
            return;
        }
        if (validCount > 1)
        {
            Fail($"{Pos(CurrentX, CurrentY)}处的媒质流的可选去路过多", ImpetusDisplay.NoExit);
            return;
        }

        Advance(found!.Value);
    }

    private void Advance(CircleDir dir)
    {
        var (x, y) = dir.Offset(CurrentX, CurrentY);
        CurrentX = x;
        CurrentY = y;
        _enteredFrom = dir;
        Sync();
    }

    /// <summary>
    /// 导向石决定出口。移植自源项目三种导向石的 `acceptControlFlow`：
    ///   空白 → 随机出轴的一端；牧羊人（布尔）→ 弹栈顶，真出反方向、假出正方向；石匠（红石）→ 通电出正方向
    /// </summary>
    private CircleDir? PickDirectrixExit(CircleComponent comp, ICircleWorld world, out bool shouldStop)
    {
        shouldStop = false;

        CircleDir chosen = comp.Kind switch
        {
            CircleComponentKind.DirectrixEmpty => Main.rand.NextBool() ? comp.Facing : comp.Facing.Opposite(),
            CircleComponentKind.DirectrixRedstone => world.IsPowered(CurrentX, CurrentY) ? comp.Facing : comp.Facing.Opposite(),
            CircleComponentKind.DirectrixBool => PickByBool(comp, ref shouldStop),
            _ => comp.Facing,
        };

        if (shouldStop) return null;

        var (nx, ny) = chosen.Offset(CurrentX, CurrentY);
        var next = world.GetComponent(nx, ny);
        if (next == null || !next.Value.AllowedEntries.Has(chosen)) return null;
        return chosen;
    }

    /// <summary>牧羊人导向石：弹栈顶。出错是 mishap（显示在促动石上），惩罚是把导向石打掉（destroyBlock(pos, true)）。</summary>
    private CircleDir PickByBool(CircleComponent comp, ref bool shouldStop)
    {
        if (_vm == null) { shouldStop = true; return comp.Facing; }

        var stack = new List<Iota>(_vm.Image.Stack);
        if (stack.Count == 0 || stack[^1] is not BooleanIota b)
        {
            string msg = stack.Count == 0
                ? $"{Pos(CurrentX, CurrentY)}处的栈为空栈"
                : $"{Pos(CurrentX, CurrentY)}处的iota实际为{stack[^1]}，而非布尔值";
            BreakDirectrix();
            Fail(msg, ImpetusDisplay.Mishap);
            shouldStop = true;
            return comp.Facing;
        }

        stack.RemoveAt(stack.Count - 1);
        _vm.SetImage(_vm.Image.WithStack(stack));
        return b.Value ? comp.Facing.Opposite() : comp.Facing;
    }

    private void BreakDirectrix()
    {
        WorldGen.KillTile(CurrentX, CurrentY, fail: false, effectOnly: false, noItem: false);
        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, CurrentX, CurrentY);
        }
    }

    /// <summary>出错停下：显示原因（null = 已经显示过了）+ 失败音。</summary>
    private void Fail(string? msg, ImpetusDisplay icon)
    {
        if (msg is not null) { DisplayMsg = msg; DisplayIcon = icon; }
        PlayFail();
        End();
    }

    /// <summary>原版 endExecution：停下、熄灭。正常走完不发任何消息。</summary>
    private void End()
    {
        IsRunning = false;
        _vm = null;
        CircleCursor.Clear();
        Sync();
    }

    private void PlayFail()
    {
        var at = new Vector2(Position.X * 16f + 8f, Position.Y * 16f + 8f);
        if (Main.netMode == NetmodeID.Server) SpellSounds.Broadcast("spellcircle.fail", at.X, at.Y);
        else SpellSounds.Play("spellcircle.fail", at);
    }

    // ── 客户端请求 → 服务端（单机直接执行）──────────────────────────

    /// <summary>发起一个促动石操作。单机 / 服务端直接做，联机客户端发包。</summary>
    public static void Request(int x, int y, ImpetusAction action, byte arg)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var packet = HexCastingTerraria.Instance?.GetPacket();
            if (packet is null) return;
            packet.Write((byte)Net.HexMessage.ImpetusAction);
            packet.Write((short)x);
            packet.Write((short)y);
            packet.Write((byte)action);
            packet.Write(arg);
            packet.Send();
            return;
        }
        Apply(x, y, action, arg, Main.LocalPlayer);
    }

    /// <summary>服务端收到 <see cref="Net.HexMessage.ImpetusAction"/>。</summary>
    public static void Handle(System.IO.BinaryReader r, int whoAmI)
    {
        int x = r.ReadInt16(), y = r.ReadInt16();
        var action = (ImpetusAction)r.ReadByte();
        byte arg = r.ReadByte();
        if (whoAmI < 0 || whoAmI >= Main.maxPlayers || Main.player[whoAmI] is not { active: true } p) return;
        if (!WorldGen.InWorld(x, y, 1)) return;
        Apply(x, y, action, arg, p);
    }

    private static void Apply(int x, int y, ImpetusAction action, byte arg, Player from)
    {
        var entity = FindAt(x, y);
        if (entity is null) return;
        switch (action)
        {
            case ImpetusAction.SetDir:
                if (arg <= (byte)CircleDir.Right) { entity.StartDir = (CircleDir)arg; entity.Sync(); }
                break;
            case ImpetusAction.InsertMedia:
                entity.InsertFrom(from, arg);
                break;
            case ImpetusAction.Start:
                entity.TryStart(from);
                break;
            case ImpetusAction.Bind:
                if (arg < Main.maxPlayers && Main.player[arg] is { active: true } who)
                {
                    entity.BoundName = who.name;
                    entity.Sync();
                    SpellSounds.PlayOrBroadcast("impetus.redstone.register", new Vector2(x * 16f + 8f, y * 16f + 8f));
                }
                break;
            case ImpetusAction.ClearBinding:
                entity.BoundName = null;
                entity.Sync();
                SpellSounds.PlayOrBroadcast("impetus.redstone.clear", new Vector2(x * 16f + 8f, y * 16f + 8f));
                break;
        }
    }

    // ── 同步与存档 ─────────────────────────────────────────────────

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
        if (DisplayMsg is not null)
        {
            tag["displayMsg"] = DisplayMsg;
            tag["displayIcon"] = (byte)DisplayIcon;
        }
        if (BoundName is not null) tag["bound"] = BoundName;
        // 走环状态**不存档**：世界重载后控制流位置已无意义（原版会存执行状态，这里是有意简化）
    }

    public override void LoadData(TagCompound tag)
    {
        Media = tag.ContainsKey("media") ? tag.GetLong("media") : 0L;
        byte d = tag.ContainsKey("dir") ? tag.GetByte("dir") : (byte)CircleDir.Right;
        StartDir = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;
        DisplayMsg = tag.TryGet("displayMsg", out string msg) ? msg : null;
        DisplayIcon = DisplayMsg is null ? ImpetusDisplay.None : (ImpetusDisplay)tag.GetByte("displayIcon");
        BoundName = tag.TryGet("bound", out string name) ? name : null;
        IsRunning = false;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write(Media);
        writer.Write((byte)StartDir);
        writer.Write(IsRunning);
        writer.Write((short)CurrentX);
        writer.Write((short)CurrentY);
        writer.Write(DisplayMsg ?? string.Empty);
        writer.Write((byte)DisplayIcon);
        writer.Write(BoundName ?? string.Empty);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        Media = reader.ReadInt64();
        byte d = reader.ReadByte();
        StartDir = d <= (byte)CircleDir.Right ? (CircleDir)d : CircleDir.Right;
        IsRunning = reader.ReadBoolean();
        CurrentX = reader.ReadInt16();
        CurrentY = reader.ReadInt16();
        string msg = reader.ReadString();
        DisplayMsg = msg.Length == 0 ? null : msg;
        DisplayIcon = (ImpetusDisplay)reader.ReadByte();
        string name = reader.ReadString();
        BoundName = name.Length == 0 ? null : name;
    }
}

/// <summary>法术环部件的朝向：放置时怎么定、画在哪。</summary>
public static class CircleFacing
{
    /// <summary>
    /// 原版 placeStateDirAndSneak：`ctx.getNearestLookingDirection()`，潜行时反过来。
    /// 2D：鼠标相对角色的方向，取最接近的上下左右（原版还有前后，泰拉没有）。
    /// </summary>
    public static CircleDir FromPlacement(Player player)
    {
        var d = Main.MouseWorld - player.Center;
        CircleDir dir = System.Math.Abs(d.X) >= System.Math.Abs(d.Y)
            ? (d.X >= 0 ? CircleDir.Right : CircleDir.Left)
            : (d.Y >= 0 ? CircleDir.Down : CircleDir.Up);
        return HexPlayer.ShiftHeld() ? dir.Opposite() : dir;
    }

    /// <summary>在图格边缘画一个小三角，尖朝出口方向。</summary>
    public static void DrawArrow(SpriteBatch sb, int i, int j, CircleDir dir)
    {
        // 图格层画在带 offScreenRange 边距的渲染目标上（tML 里 drawToScreen 恒为 false）
        var zero = new Vector2(Main.offScreenRange);
        var center = new Vector2(i * 16 + 8, j * 16 + 8) - Main.screenPosition + zero;
        var (dx, dy) = dir.Step();
        var fwd = new Vector2(dx, dy);
        var side = new Vector2(-dy, dx);
        var tip = center + fwd * 7f;
        var baseMid = center + fwd * 3f;
        var color = new Color(230, 200, 255, 220) * 0.9f;
        Client.HexPixel.DrawLine(sb, baseMid + side * 3f, tip, 1.5f, color);
        Client.HexPixel.DrawLine(sb, baseMid - side * 3f, tip, 1.5f, color);
    }
}
