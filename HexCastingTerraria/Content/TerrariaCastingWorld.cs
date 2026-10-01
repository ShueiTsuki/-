using HexCastingTerraria.Core.Casting;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using HexCastingTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ModLoader;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Content;

/// <summary>
/// <see cref="ICastingWorld"/> 的泰拉瑞亚实现。
///
/// 这是「Core 逻辑 / 游戏取值」的分界线：所有**像素 ↔ 图格**换算与
/// **左上角 ↔ 中心**换算都只在本文件里做一次，
/// 上层（世界图案）拿到的永远是「图格单位、中心语义」的值。
///
/// 详见 <see cref="ICastingWorld"/> 顶部关于单位与坐标原点的说明。
/// </summary>
public sealed class TerrariaCastingWorld : ICastingWorld
{
    private readonly Player? _caster;

    /// <summary>
    /// 法术环的范围（图格）。设了它就用它做范围判定，**不再用施法者半径**。
    ///
    /// 源项目 `CircleCastEnv.isVecInRangeEnvironment` 用的就是环的包围盒 ——
    /// 环里的可用范围与玩家施法完全不同。
    /// </summary>
    private readonly (int MinX, int MinY, int MaxX, int MaxY)? _circleBounds;

    /// <summary>不以施法者为心、而以某一点为心的施法范围（图格坐标与半径）。附属的方块施法用（HexDebug 制念台，上游 splicingTableAmbit）。</summary>
    public (double X, double Y, double Radius)? Ambit { get; init; }

    public TerrariaCastingWorld(Player? caster, (int MinX, int MinY, int MaxX, int MaxY)? circleBounds = null)
    {
        _caster = caster;
        _circleBounds = circleBounds;
    }

    /// <summary>
    /// 法术环专用的世界访问：范围 = 环的包围盒（再加施法者身边与他的大哨卫，见 InRange）。
    /// 施法者 = 启动它的玩家 / 牧师促动石绑定的玩家；没有就是 null，`get_caster` 吐 `NullIota`。
    /// </summary>
    public static TerrariaCastingWorld ForCircle(int minX, int minY, int maxX, int maxY, Player? caster = null)
        => new(caster, (minX, minY, maxX, maxY));

    /// <summary>
    /// 施法者。玩家不存活时返回 null —— 对应源项目 `castingEntity` 可为 null 的语义，
    /// `get_caster` 会因此吐 NullIota。
    /// </summary>
    public EntityIota? Caster
        => _caster is { active: true } && !_caster.dead
            ? new EntityIota(EntityIota.EntityKind.Player, _caster.whoAmI)
            : null;

    /// <summary>
    /// 实体的关键读数，已统一成**图格单位 + 中心语义**。
    /// 用一个私有结构承载，避免把 Player / NPC / Projectile 硬塞进同一个类型。
    /// </summary>
    private readonly struct Reading
    {
        public required Vector2 CenterTile { get; init; }

        /// <summary>脚底中心（对应 MC 的 position()）。</summary>
        public required Vector2 FeetTile { get; init; }

        /// <summary>眼睛（对应 MC 的 eyePosition = 脚底 + eyeHeight）。</summary>
        public required Vector2 EyeTile { get; init; }

        /// <summary>图格/帧。</summary>
        public required Vector2 VelocityTiles { get; init; }

        /// <summary>单位向量，由 LookResolver 保证非 NaN、非零。</summary>
        public required Vector2 Look { get; init; }

        /// <summary>图格。</summary>
        public required float HeightTiles { get; init; }
    }

    /// <summary>
    /// 把实体 iota 解析成读数。索引越界或实体不存活 → false。
    ///
    /// 【为什么必须校验索引合法性】泰拉用 whoAmI 索引标识实体，
    /// 实体死亡后**索引会被新实体复用**。不校验就会读到「另一个实体」，
    /// 而且完全无声 —— 法术作用到错误的怪身上，玩家只会觉得「这法术有 bug」。
    /// </summary>
    private static bool TryRead(EntityIota iota, out Reading reading)
    {
        reading = default;

        switch (iota.Target)
        {
            case EntityIota.EntityKind.Player:
            {
                if (iota.Index < 0 || iota.Index >= Main.maxPlayers) return false;
                var p = Main.player[iota.Index];
                if (p is not { active: true } || p.dead) return false;
                reading = Make(p.Center, p.Bottom, p.velocity, HexPlayer.Get(p).Look, p.height, PlayerEyeFraction);
                return true;
            }

            case EntityIota.EntityKind.Npc:
            {
                if (iota.Index < 0 || iota.Index >= Main.maxNPCs) return false;
                var n = Main.npc[iota.Index];
                if (n is not { active: true }) return false;
                reading = Make(n.Center, n.Bottom, n.velocity,
                    n.GetGlobalNPC<HexGlobalNPC>().Look, n.height);
                return true;
            }

            case EntityIota.EntityKind.Projectile:
            {
                if (iota.Index < 0 || iota.Index >= Main.maxProjectiles) return false;
                var pr = Main.projectile[iota.Index];
                if (pr is not { active: true }) return false;
                reading = Make(pr.Center, pr.Bottom, pr.velocity,
                    pr.GetGlobalProjectile<HexGlobalProjectile>().Look, pr.height);
                return true;
            }

            case EntityIota.EntityKind.ItemFrame:
            {
                // 原版物品展示框：位置在框的正中、眼高 0、不会动。泰拉的框挂在墙上朝着屏幕外，平面内没有朝向，视线取 (1, 0)
                if (FrameOf(iota) is not { } f) return false;
                var c = FrameBox(f).Center.ToVector2();
                reading = Make(c, c, Vector2.Zero, new Vector2(1f, 0f), FrameBox(f).Height, eyeFraction: 0f);
                return true;
            }

            case EntityIota.EntityKind.Item:
            {
                if (iota.Index < 0 || iota.Index >= Main.maxItems) return false;
                var it = Main.item[iota.Index];
                if (it is not { active: true }) return false;
                // 掉落物没有速度与视线可言，用其速度方向作视线（静止则退回 (1,0)）
                var look = it.velocity.LengthSquared() > 0.01f
                    ? Microsoft.Xna.Framework.Vector2.Normalize(it.velocity)
                    : new Microsoft.Xna.Framework.Vector2(1f, 0f);
                reading = Make(it.Center, new Microsoft.Xna.Framework.Vector2(it.Center.X, it.Bottom.Y),
                    it.velocity, look, it.height);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 像素 → 图格的唯一换算点。
    /// 泰拉实体的宽度/高度是 int 像素，速度是像素/帧，位置是像素。
    /// </summary>
    /// <summary>MC 玩家眼高 1.62 / 身高 1.8。</summary>
    private const float PlayerEyeFraction = 0.9f;

    /// <summary>MC 一般实体的默认眼高 = 身高 × 0.85（Entity.getEyeHeight 的默认实现）。</summary>
    private const float DefaultEyeFraction = 0.85f;

    private static Reading Make(Vector2 center, Vector2 bottom, Vector2 velocity, Vector2 look, int height,
        float eyeFraction = DefaultEyeFraction)
        => new()
        {
            CenterTile = center / HexUnits.PixelsPerTile,
            FeetTile = bottom / HexUnits.PixelsPerTile,
            EyeTile = new Vector2(bottom.X, bottom.Y - height * eyeFraction) / HexUnits.PixelsPerTile,
            VelocityTiles = velocity / HexUnits.PixelsPerTile,
            Look = look,
            HeightTiles = height / HexUnits.PixelsPerTile,
        };

    public bool IsAlive(EntityIota entity) => TryRead(entity, out _);

    /// <summary>施法者中心（图格）。范围判定全部以它为圆心。</summary>
    /// <summary>源项目 ambit 的圆心是 caster.position() —— **脚底**（曾经用身体中心）。</summary>
    private (float X, float Y) CasterFeetTiles()
        => _caster == null
            ? (0f, 0f)   // 环环境不会走到这里（InRange 优先用包围盒）
            : (_caster.Bottom.X / HexUnits.PixelsPerTile, _caster.Bottom.Y / HexUnits.PixelsPerTile);

    /// <summary>
    /// 点是否在施法范围内。用平方比较省一次开方。
    /// 半径取 <see cref="HexUnits.AmbitRadiusTiles"/>（32 格），与源项目 `DEFAULT_AMBIT_RADIUS` 对齐。
    ///
    /// **大哨卫会把范围延伸过去**：源项目 `PlayerBasedCastEnv.isVecInRangeEnvironment`
    /// 的第一段分支就是「在大哨卫 16 格内 -> 也算在范围内」。
    /// 这是 `sentinel/create/great` 存在的全部理由，漏掉它这个图案就白做了。
    /// </summary>
    private bool InRange(double tileX, double tileY) => InRange(tileX, tileY, 0.0);

    /// <summary>三维距离：施法者、哨卫都在世界平面上（z = 0），点的 z 算进距离。法术环的包围盒 z 范围是 [0, 1)。</summary>
    private bool InRange(double tileX, double tileY, double z)
    {
        // 法术环（源项目 CircleCastEnv.isVecInRangeEnvironment）：
        //   ① 施法者身边：到脚底的距离 ≤ 身高
        //   ② 施法者的大哨卫：半径 SENTINEL_RADIUS
        //   ③ 环的包围盒
        if (_circleBounds is { } b)
        {
            if (_caster is { active: true, dead: false })
            {
                var (fx, fy) = CasterFeetTiles();
                double h = _caster.height / HexUnits.PixelsPerTile;
                double cdx = tileX - fx, cdy = tileY - fy;
                if (cdx * cdx + cdy * cdy + z * z <= h * h) return true;
                if (HexPlayer.Get(_caster).Sentinel is { Great: true } cs)
                {
                    double sdx = tileX - cs.X, sdy = tileY - HexSpaceWorld.TileY(cs.Y);
                    const double csr = CastingEnvironment.SentinelRadiusTiles;
                    if (sdx * sdx + sdy * sdy + z * z <= csr * csr + 1e-10) return true;
                }
            }
            // 源项目 bounds.contains(vec)：[min, max + 1)，不多放半格（这里曾经四周各放宽半格）
            return tileX >= b.MinX && tileX < b.MaxX + 1
                && tileY >= b.MinY && tileY < b.MaxY + 1
                && z >= 0 && z < 1;
        }

        // 大哨卫：以哨卫为心 16 格（对齐 DEFAULT_SENTINEL_RADIUS）
        if (_caster is not null && HexPlayer.Get(_caster).Sentinel is { Great: true } s)
        {
            // 哨卫存的是法术坐标（Y 朝上），换回泰拉图格
            double sdx = tileX - s.X;
            double sdy = tileY - HexSpaceWorld.TileY(s.Y);
            const double sr = CastingEnvironment.SentinelRadiusTiles;
            if (sdx * sdx + sdy * sdy + z * z <= sr * sr + 1e-10)
            {
                return true;
            }
        }

        // 附属的方块施法（HexDebug 制念台）：范围以方块为心（大哨卫照样延伸，见上）
        if (Ambit is { } a)
        {
            double adx = tileX - a.X, ady = tileY - a.Y;
            return adx * adx + ady * ady + z * z <= a.Radius * a.Radius + 1e-10;
        }

        // 玩家：以自身为中心 32 格半径
        var (cx, cy) = CasterFeetTiles();
        double dx = tileX - cx;
        double dy = tileY - cy;
        const double r = HexUnits.AmbitRadiusTiles;
        return dx * dx + dy * dy + z * z <= r * r + 1e-10;
    }

    /// <summary>
    /// 源项目 isEntityInRange：玩家有「真名」，默认**永远在范围内**（HexConfig trueNameHasAmbit = true）；
    /// 其它实体看脚底（e.position()）在不在范围里。
    /// </summary>
    public bool IsInRange(EntityIota entity)
    {
        if (!TryRead(entity, out var reading)) return false;
        if (entity.Target == EntityIota.EntityKind.Player && Config.HexServerConfig.Instance.TrueNameHasAmbit) return true;
        return InRange(reading.FeetTile.X, reading.FeetTile.Y);
    }

    public bool IsVecInRange(double x, double y) => InRange(x, y);

    public bool IsVecInRange(double x, double y, double z) => InRange(x, y, z);

    /// <summary>
    /// 图格是否阻挡射线。**所有**射线相关的实心判定都必须走这里，包括客户端的瞄准预览 ——
    /// 否则预览会指向一个位置、真正施法却打到别处，而且两边都不报错。
    ///
    /// 取 `WorldGen.SolidTile` 的语义（= `tileSolid` 且非 `tileSolidTop`），
    /// 对应 MC 的 `ClipContext.Block.COLLIDER` —— 平台、金属架这类「可穿过的实心顶」
    /// **不算**阻挡，否则站在平台上往下打射线会立刻命中脚下的平台。
    /// </summary>
    public static bool SolidAt(int tileX, int tileY)
    {
        // 世界外当作实心：否则射线飞出世界边界会一路走到坐标溢出
        if (!WorldGen.InWorld(tileX, tileY, 1)) return true;
        return WorldGen.SolidTile(tileX, tileY);
    }

    public bool IsTileSolid(int tileX, int tileY) => SolidAt(tileX, tileY);

    /// <summary>
    /// 枚举区域内的实体判定箱（图格单位）。
    ///
    /// 这里把三类实体都算上：玩家、NPC、弹幕。
    /// 源项目的 `getEntities` 同样涵盖所有 Entity，我们保持一致。
    /// 故意**不**做范围外过滤 —— 那句「命中者也要在范围内」的检查在 Core 里做，
    /// 免得两处判断标准不一致。
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<EntityBox> EntitiesInArea(
        double minX, double minY, double maxX, double maxY)
    {
        var list = new System.Collections.Generic.List<EntityBox>();

        void Add(EntityIota iota, Rectangle hitbox)
        {
            // 判定箱与查询区域无重叠 → 不可能被射线扫到，跳过
            double bMinX = hitbox.X / (double)HexUnits.PixelsPerTile;
            double bMinY = hitbox.Y / (double)HexUnits.PixelsPerTile;
            double bMaxX = (hitbox.X + hitbox.Width) / (double)HexUnits.PixelsPerTile;
            double bMaxY = (hitbox.Y + hitbox.Height) / (double)HexUnits.PixelsPerTile;

            if (bMaxX < minX || bMinX > maxX || bMaxY < minY || bMinY > maxY) return;

            list.Add(new EntityBox
            {
                Entity = iota,
                MinX = bMinX,
                MinY = bMinY,
                MaxX = bMaxX,
                MaxY = bMaxY,
            });
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true } || p.dead) continue;
            Add(new EntityIota(EntityIota.EntityKind.Player, i), p.Hitbox);
        }

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n is not { active: true }) continue;
            Add(new EntityIota(EntityIota.EntityKind.Npc, i), n.Hitbox);
        }

        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            var pr = Main.projectile[i];
            if (pr is not { active: true }) continue;
            Add(new EntityIota(EntityIota.EntityKind.Projectile, i), pr.Hitbox);
        }

        // 物品框（原版物品展示框是实体，射线能打中它）
        foreach (var f in AllFrames()) Add(new EntityIota(EntityIota.EntityKind.ItemFrame, f.ID), FrameBox(f));

        return list;
    }

    // ── 物品框（原版的物品展示框实体；泰拉是带图格实体的方块）────────────

    /// <summary>编号是图格实体 ID；框没了、或者那里已经不是物品框 → null。</summary>
    private static TEItemFrame? FrameOf(EntityIota e)
    {
        if (e.Target != EntityIota.EntityKind.ItemFrame) return null;
        if (!TileEntity.ByID.TryGetValue(e.Index, out var te) || te is not TEItemFrame f) return null;
        return IsLiveFrame(f) ? f : null;
    }

    private static bool IsLiveFrame(TEItemFrame f)
        => WorldGen.InWorld(f.Position.X, f.Position.Y, 2) && TEItemFrame.ValidTile(f.Position.X, f.Position.Y);

    /// <summary>框占 2×2 格。</summary>
    private static Rectangle FrameBox(TEItemFrame f) => new(f.Position.X * 16, f.Position.Y * 16, 32, 32);

    private static System.Collections.Generic.List<TEItemFrame> AllFrames()
    {
        var list = new System.Collections.Generic.List<TEItemFrame>();
        foreach (var te in TileEntity.ByID.Values)
        {
            if (te is TEItemFrame f && IsLiveFrame(f)) list.Add(f);
        }
        return list;
    }

    /// <summary>
    /// 原版 HangingEntity.push：挂着的东西被推一下就掉下来，框和里面的东西都掉。
    /// 泰拉打物品框第一下掉出里面的东西、第二下才拆框，这里两步一起做。
    /// </summary>
    private static void BreakFrame(TEItemFrame f)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
        int x = f.Position.X, y = f.Position.Y;
        if (!f.item.IsAir) f.DropItem();
        WorldGen.KillTile(x, y);
        if (Main.netMode == Terraria.ID.NetmodeID.Server) NetMessage.SendTileSquare(-1, x, y, 2, 2);
    }

    public (double X, double Y) FeetPosition(EntityIota entity)
        => TryRead(entity, out var r) ? (r.FeetTile.X, r.FeetTile.Y) : (0.0, 0.0);

    public (double X, double Y) EyePosition(EntityIota entity)
        // 原版 eyePosition = 脚底 + 眼高（曾经取身体中心 —— 射线从胸口打出去）
        => TryRead(entity, out var r) ? (r.EyeTile.X, r.EyeTile.Y) : (0.0, 0.0);

    public (double X, double Y) Velocity(EntityIota entity)
        => TryRead(entity, out var r) ? (r.VelocityTiles.X, r.VelocityTiles.Y) : (0.0, 0.0);

    public (double X, double Y) Look(EntityIota entity)
        // 取不到时退回正方向，而不是零向量 —— 零向量进 VM 会被归一化成 NaN
        => TryRead(entity, out var r) ? (r.Look.X, r.Look.Y) : (1.0, 0.0);

    public double EntityHeight(EntityIota entity)
        => TryRead(entity, out var r) ? r.HeightTiles : 0.0;

    // ── 写入类：会改变世界状态 ──────────────────────────────────────

    /// <summary>
    /// 坐标是否在世界内。
    /// 留 1 格余量，避免把实体送到地图边缘外导致它永久掉出世界。
    /// </summary>
    public bool IsVecInWorld(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);
        return WorldGen.InWorld(tx, ty, 1);
    }

    /// <summary>
    /// 施加推力。图格/帧 → 像素/帧。
    ///
    /// 三类实体的处理：
    ///   - 玩家：直接改 velocity（泰拉玩家速度由自身逻辑接管，通常下一帧就衰减）
    ///   - NPC：改 velocity 并置 `netUpdate = true`，否则联机下客户端看不到
    ///   - 弹幕：直接改 velocity
    /// </summary>
    public void ApplyMotion(EntityIota entity, double mx, double my)
    {
        // 注意：速度单位要换算，不能直接乘 16。
        //
        // 源项目的 motion 单位是 **MC 的格/tick**：1 单位 = 1 格/tick = 20 格/秒。
        // 泰拉的 velocity 单位是 **像素/帧**：1 px/帧 = 60 px/秒 = 3.75 格/秒。
        //
        // 所以 1 个 MC 单位 = 20 格/秒 = 20×16/60 px/帧 = 5.3333 px/帧。
        // 直接乘 PixelsPerTile（=16）的话，**所有推动都会猛 3 倍** ——
        // 原版轻轻一推，我们这边直接把人射上天。这个 3 是 16 / (16/3)，不是拍脑袋来的。
        const float mcUnitToPixelsPerFrame = HexUnits.PixelsPerTile / 3f;

        var delta = new Vector2(
            (float)(mx * mcUnitToPixelsPerFrame),
            (float)(my * mcUnitToPixelsPerFrame));

        switch (entity.Target)
        {
            case EntityIota.EntityKind.Player:
            {
                if (entity.Index < 0 || entity.Index >= Main.maxPlayers) return;
                var p = Main.player[entity.Index];
                if (p is not { active: true } || p.dead) return;
                // 泰拉的玩家移动由本人客户端说了算：服务端改 velocity 不会生效，要发给那个客户端
                if (Main.netMode == Terraria.ID.NetmodeID.Server) { Net.HexNetSync.SendPlayerMotion(p.whoAmI, delta.X, delta.Y, false); }
                else { p.velocity += delta; }
                break;
            }

            case EntityIota.EntityKind.Npc:
            {
                if (entity.Index < 0 || entity.Index >= Main.maxNPCs) return;
                var n = Main.npc[entity.Index];
                if (n is not { active: true }) return;
                n.velocity += delta;
                // 不置 netUpdate 的话，联机下这次推动只发生在服务端
                n.netUpdate = true;
                break;
            }

            case EntityIota.EntityKind.Projectile:
            {
                if (entity.Index < 0 || entity.Index >= Main.maxProjectiles) return;
                var pr = Main.projectile[entity.Index];
                if (pr is not { active: true }) return;
                pr.velocity += delta;
                pr.netUpdate = true;
                break;
            }

            case EntityIota.EntityKind.ItemFrame:
                // 原版 HangingEntity.push：只要推力不是零就掉下来
                if ((mx != 0 || my != 0) && FrameOf(entity) is { } f) BreakFrame(f);
                break;
        }
    }

    /// <summary>
    /// 瞬移一段位移（图格 → 像素）。
    ///
    /// 泰拉的玩家碰撞箱（约 1.25×2.6 格）比 MC（0.6×1.8 格）大得多，按原位落点一塞就容易卡墙。
    /// 做法：落点被挡时，在**附近几格**（先往上 1~3 格，再下 1 格、左右 1 格）找一个放得下的位置；
    /// 都放不下才取消。以前是被挡就静默取消 —— 而媒质已经扣了，表现为「闪现没反应」。
    ///
    /// 玩家走 <see cref="Player.Teleport"/>：它会重置坠落起点（否则闪现下崖后落地，
    /// 摔伤按**闪现前的高度**算）、解除钩爪；联机时由该玩家的客户端执行。
    /// </summary>
    public void TeleportBy(EntityIota entity, double dx, double dy)
    {
        var offset = new Vector2(
            (float)(dx * HexUnits.PixelsPerTile),
            (float)(dy * HexUnits.PixelsPerTile));

        if (!TryGetEntity(entity, out var target) || target == null) return;

        var wanted = target.Center + offset;
        Vector2? destination = null;
        foreach (var (ox, oy) in new[] { (0, 0), (0, -1), (0, -2), (0, -3), (0, 1), (-1, 0), (1, 0) })
        {
            var c = wanted + new Vector2(ox * HexUnits.PixelsPerTile, oy * HexUnits.PixelsPerTile);
            if (IsAreaClear(c, target.width, target.height)) { destination = c; break; }
        }
        if (destination is not { } dest) return;

        var topLeft = dest - new Vector2(target.width / 2f, target.height / 2f);
        switch (entity.Target)
        {
            case EntityIota.EntityKind.Player:
            {
                var p = (Player)target;
                if (Main.netMode == Terraria.ID.NetmodeID.Server) { Net.HexNetSync.SendPlayerMotion(p.whoAmI, topLeft.X, topLeft.Y, true); }
                else { TeleportPlayerLocal(p, topLeft); }
                break;
            }
            case EntityIota.EntityKind.Npc:
                target.Center = dest;
                Main.npc[entity.Index].netUpdate = true;
                break;
            case EntityIota.EntityKind.Projectile:
                target.Center = dest;
                Main.projectile[entity.Index].netUpdate = true;
                break;
        }
        SpellSounds.At("spell.teleport", dest);
    }

    /// <summary>在本地执行玩家传送（单机，或联机时玩家自己的客户端）。保留速度 —— 原版闪现也不清速度。</summary>
    internal static void TeleportPlayerLocal(Player p, Vector2 topLeft)
    {
        var velocity = p.velocity;
        p.Teleport(topLeft, 1);
        p.velocity = velocity;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
        {
            NetMessage.SendData(Terraria.ID.MessageID.TeleportEntity, -1, -1, null, 0, p.whoAmI, topLeft.X, topLeft.Y, 1);
        }
    }

    /// <summary>取实体对象。三类分别处理，取不到返回 false。</summary>
    private static bool TryGetEntity(EntityIota iota, out Entity? entity)
    {
        entity = null;
        switch (iota.Target)
        {
            case EntityIota.EntityKind.Player:
                if (iota.Index >= 0 && iota.Index < Main.maxPlayers)
                {
                    var p = Main.player[iota.Index];
                    if (p is { active: true } && !p.dead) { entity = p; return true; }
                }
                return false;

            case EntityIota.EntityKind.Npc:
                if (iota.Index >= 0 && iota.Index < Main.maxNPCs)
                {
                    var n = Main.npc[iota.Index];
                    if (n is { active: true }) { entity = n; return true; }
                }
                return false;

            case EntityIota.EntityKind.Projectile:
                if (iota.Index >= 0 && iota.Index < Main.maxProjectiles)
                {
                    var pr = Main.projectile[iota.Index];
                    if (pr is { active: true }) { entity = pr; return true; }
                }
                return false;
        }

        return false;
    }

    /// <summary>以 center 为中心、给定尺寸的矩形是否完全没有实心图格。</summary>
    private static bool IsAreaClear(Vector2 center, int width, int height)
    {
        int minX = (int)System.Math.Floor((center.X - width * 0.5f) / HexUnits.PixelsPerTile);
        int maxX = (int)System.Math.Floor((center.X + width * 0.5f - 1f) / HexUnits.PixelsPerTile);
        int minY = (int)System.Math.Floor((center.Y - height * 0.5f) / HexUnits.PixelsPerTile);
        int maxY = (int)System.Math.Floor((center.Y + height * 0.5f - 1f) / HexUnits.PixelsPerTile);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (SolidAt(x, y)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 大传送的代价：按距离概率把**施法者自己**的物品震落在地。
    /// 移植自源项目 `OpTeleport.Spell.cast` 的掉落分支。
    ///
    /// 三条照抄原版的规则（都很重要）：
    ///   ① 掉率 = 传送距离(图格) / 分母（默认 10000）—— 传得越远掉得越多
    ///   ② **永不掉落主手物品** —— 源项目注释写明：如果主手是饰品，
    ///      掉落后会被复制（这是原版的 bug 规避，不是随意的选择）
    ///   ③ 快捷栏与护甲的掉率打折（×0.5 / ×0.25）——
    ///      这些东西掉出来最烦人，而且设定上「施法者对自己常用的东西更有意识」
    ///
    /// 只对**施法者自己**生效（调用方检查，原版 teleportee == castingEntity）。
    /// 背包归本人客户端：掷骰和丢出都在那边做（PlayerEffects.Scatter）。
    /// </summary>
    public void ScatterInventory(EntityIota entity, double distanceTiles)
    {
        if (entity.Target != EntityIota.EntityKind.Player) return;
        if (entity.Index < 0 || entity.Index >= Main.maxPlayers) return;

        var player = Main.player[entity.Index];
        if (player is not { active: true } || player.dead) return;

        double divisor = Core.Casting.GreatTeleportRules.DropDivisor();
        if (divisor <= 0) return;

        double baseChance = distanceTiles / divisor;
        if (baseChance <= 0) return;

        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
        PlayerEffects.Scatter(player, baseChance);
    }

    /// <summary>
    /// 查询区域内的实体。**按距离升序**返回 —— 源项目要求排序，
    /// 而距离信息只有世界侧有，所以排序必须在这里做。
    ///
    /// 过滤条件（照抄源项目 `OpGetEntitiesBy.isReasonablySelectable`）：
    ///   - 存活
    ///   - **在施法范围内** —— 源项目注释特别注明这一条是为了修复 #792：
    ///     不加范围限制就能「把全世界的玩家一网打尽」
    ///   - 距离 ≤ 半径
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<EntityIota> QueryEntities(
        Core.Casting.Actions.ZoneEntityFilter filter, bool negate, double x, double y, double radius)
    {
        var found = new System.Collections.Generic.List<(EntityIota Iota, double DistSq)>();

        double r2 = radius * radius;

        void Consider(EntityIota iota, Microsoft.Xna.Framework.Vector2 center)
        {
            if (!TryRead(iota, out var reading)) return;
            // 源项目 isReasonablySelectable：isEntityInRange(e, ignoreTruename = true) —— 这里不放玩家特权
            if (!InRange(reading.FeetTile.X, reading.FeetTile.Y)) return;

            // 源项目 it.distanceToSqr(pos)：实体位置 = 脚底
            double dx = reading.FeetTile.X - x;
            double dy = reading.FeetTile.Y - y;
            double d2 = dx * dx + dy * dy;
            if (d2 > r2) return;

            found.Add((iota, d2));
        }

        // 泰拉侧的生物分类：
        //   动物（critter）-> npc.CountsAsACritter
        //   怪物           -> 非友好、非 critter、非城镇 NPC
        //   活物           -> 玩家或 NPC
        // 这些字段 Core 层拿不到，所以「分类 + 谓词判定」都在这里做。
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true } || p.dead) continue;

            bool match = filter is Core.Casting.Actions.ZoneEntityFilter.Player
                         or Core.Casting.Actions.ZoneEntityFilter.Living
                         or Core.Casting.Actions.ZoneEntityFilter.Any;
            if (match != negate)
            {
                Consider(new EntityIota(EntityIota.EntityKind.Player, i), p.Center);
            }
        }

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n is not { active: true }) continue;

            bool isCritter = n.CountsAsACritter;
            bool isMonster = !n.friendly && !n.townNPC && !isCritter;

            bool match = filter switch
            {
                Core.Casting.Actions.ZoneEntityFilter.Animal => isCritter,
                Core.Casting.Actions.ZoneEntityFilter.Monster => isMonster,
                Core.Casting.Actions.ZoneEntityFilter.Living => true,
                Core.Casting.Actions.ZoneEntityFilter.Any => true,
                _ => false,
            };

            if (match != negate)
            {
                Consider(new EntityIota(EntityIota.EntityKind.Npc, i), n.Center);
            }
        }

        // 物品：只有「物品」这一个筛选会匹配
        if ((filter is Core.Casting.Actions.ZoneEntityFilter.Item or Core.Casting.Actions.ZoneEntityFilter.Any) != negate)
        {
            for (int i = 0; i < Main.maxItems; i++)
            {
                var it = Main.item[i];
                if (it is not { active: true }) continue;
                Consider(new EntityIota(EntityIota.EntityKind.Item, i), it.Center);
            }
        }

        // 弹幕：源项目没有「弹幕」这个筛选，只有「任意」或取反时才会被选中（「不是动物」之类）
        if (negate || filter == Core.Casting.Actions.ZoneEntityFilter.Any)
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                var pr = Main.projectile[i];
                if (pr is not { active: true }) continue;
                Consider(new EntityIota(EntityIota.EntityKind.Projectile, i), pr.Center);
            }

            // 物品框：原版物品展示框不是生物、不是掉落物，同样只在「任意」或取反时被选中
            foreach (var f in AllFrames())
            {
                Consider(new EntityIota(EntityIota.EntityKind.ItemFrame, f.ID), FrameBox(f).Center.ToVector2());
            }
        }

        // 按距离升序 —— 源项目的 `.sortedBy { it.distanceToSqr(pos) }`
        found.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));

        var result = new System.Collections.Generic.List<EntityIota>(found.Count);
        foreach (var (iota, _) in found)
        {
            result.Add(iota);
        }
        return result;
    }
    /// <summary>清除 NPC 的某个 buff。泰拉 NPC 的 buff 是并行数组，没有现成的 ClearBuff。</summary>
    private static void ClearNpcBuff(Terraria.NPC npc, int buffType)
    {
        for (int i = 0; i < npc.buffType.Length; i++)
        {
            if (npc.buffType[i] == buffType)
            {
                npc.buffType[i] = 0;
                npc.buffTime[i] = 0;
            }
        }
    }

    // ── 比较类 ──────────────────────────────────────────────────────

    /// <summary>两个实体是不是同类。泰拉按「种类 + 具体物种 ID」判定。</summary>
    public bool IsSameEntityType(EntityIota a, EntityIota b)
    {
        if (a.Target != b.Target) return false;

        switch (a.Target)
        {
            case EntityIota.EntityKind.Player:
                return true;   // 玩家都是同一种

            case EntityIota.EntityKind.Npc:
            {
                if (a.Index < 0 || a.Index >= Main.maxNPCs) return false;
                if (b.Index < 0 || b.Index >= Main.maxNPCs) return false;
                var na = Main.npc[a.Index];
                var nb = Main.npc[b.Index];
                if (na is not { active: true } || nb is not { active: true }) return false;
                return na.netID == nb.netID;   // 同一个物种
            }

            case EntityIota.EntityKind.Projectile:
            {
                if (a.Index < 0 || a.Index >= Main.maxProjectiles) return false;
                if (b.Index < 0 || b.Index >= Main.maxProjectiles) return false;
                var pa = Main.projectile[a.Index];
                var pb = Main.projectile[b.Index];
                if (pa is not { active: true } || pb is not { active: true }) return false;
                return pa.type == pb.type;
            }

            case EntityIota.EntityKind.Item:
            {
                if (a.Index < 0 || a.Index >= Main.maxItems) return false;
                if (b.Index < 0 || b.Index >= Main.maxItems) return false;
                var ia = Main.item[a.Index];
                var ib = Main.item[b.Index];
                if (ia is not { active: true } || ib is not { active: true }) return false;
                return ia.type == ib.type;
            }

            case EntityIota.EntityKind.ItemFrame:
                return FrameOf(a) is not null && FrameOf(b) is not null;

            default:
                return false;
        }
    }

    /// <summary>
    /// 两个位置的方块是否相同。
    /// strict 比「方块 + 帧 + 斜坡 + 油漆」，lenient 只比方块种类。
    /// </summary>
    public bool CompareBlocks(double x1, double y1, double x2, double y2, bool exact)
    {
        int ax = (int)System.Math.Floor(x1), ay = (int)System.Math.Floor(y1);
        int bx = (int)System.Math.Floor(x2), by = (int)System.Math.Floor(y2);

        if (!WorldGen.InWorld(ax, ay, 1) || !WorldGen.InWorld(bx, by, 1)) return false;

        var ta = Main.tile[ax, ay];
        var tb = Main.tile[bx, by];

        if (ta.HasTile != tb.HasTile) return false;
        if (!ta.HasTile) return true;            // 两边都是空气 -> 相同

        if (ta.TileType != tb.TileType) return false;

        if (!exact) return true;

        // strict：帧、斜坡、油漆都要一致
        return ta.TileFrameX == tb.TileFrameX
            && ta.TileFrameY == tb.TileFrameY
            && ta.Slope == tb.Slope
            && ta.IsHalfBlock == tb.IsHalfBlock
            && ta.TileColor == tb.TileColor;
    }

    /// <summary>
    /// 原版 HexItemHolderHandlers：掉落物 = 它自己；物品展示框（泰拉：物品框）= 框里的东西；
    /// 玩家 = 主手，主手空了看副手（泰拉：手持物品，空了看快捷栏中它右边一格）。别的实体、或者是空的 → null。
    /// </summary>
    private static Item? HeldItemOf(EntityIota e)
    {
        Item? it = null;
        switch (e.Target)
        {
            case EntityIota.EntityKind.Item:
                if (e.Index >= 0 && e.Index < Main.maxItems && Main.item[e.Index] is { active: true } w) it = w;
                break;
            case EntityIota.EntityKind.ItemFrame:
                it = FrameOf(e)?.item;
                break;
            case EntityIota.EntityKind.Player:
                if (e.Index >= 0 && e.Index < Main.maxPlayers && Main.player[e.Index] is { active: true, dead: false } p)
                {
                    it = p.HeldItem;
                    if (it is null || it.IsAir) it = p.selectedItem is >= 0 and < 10 ? p.inventory[(p.selectedItem + 1) % 10] : null;
                }
                break;
        }
        return it is { IsAir: false } ? it : null;
    }

    public bool HasHeldItem(EntityIota entity) => HeldItemOf(entity) is not null;

    public bool CompareItems(EntityIota a, EntityIota b, bool exact)
    {
        if (HeldItemOf(a) is not { } wa || HeldItemOf(b) is not { } wb) return false;
        if (wa.type != wb.type) return false;
        if (!exact) return true;

        // strict：原版 ItemStack.isSameItemSameTags —— 同种物品 + 同 NBT，数量不参与比较。
        // 泰拉里对应 NBT 的是前缀，以及载体里存的 iota（原版存在 NBT 里）。
        if (wa.prefix != wb.prefix) return false;
        if (wa.ModItem is Items.ItemIotaStorage sa && wb.ModItem is Items.ItemIotaStorage sb)
        {
            var ia = sa.Read();
            var ib = sb.Read();
            return ia is null ? ib is null : ib is not null && ia.ValueEquals(ib);
        }
        return true;
    }

    /// <summary>[0, 1) 的随机数。</summary>
    public double NextDouble() => Main.rand.NextDouble();

    // ── 实体身上的数据载体（read/entity 与 write/entity）──────────────

    /// <summary>
    /// 取实体身上的 iota 载体（原版 ItemDelegatingEntityIotaHolder）：
    /// **掉在地上、本身就是载体的物品**（核心、念珠、卷轴），和**物品框里放着的载体**（原版 ToItemFrame）。
    ///
    /// 注意：载体的状态挂在 <see cref="ModItem"/> 实例上（`ModItem` 是 per-Item 的），
    /// 所以必须从 `Main.item[i].ModItem` 取，不能自己 new 一个。
    /// </summary>
    private static Items.ItemIotaStorage? FindEntityStorage(EntityIota entity)
    {
        if (entity.Target == EntityIota.EntityKind.ItemFrame)
            return FrameOf(entity) is { item: { IsAir: false } framed } ? framed.ModItem as Items.ItemIotaStorage : null;
        if (entity.Target != EntityIota.EntityKind.Item) return null;
        if (entity.Index < 0 || entity.Index >= Main.maxItems) return null;

        Item world = Main.item[entity.Index];
        if (world is null || !world.active || world.IsAir) return null;

        return world.ModItem as Items.ItemIotaStorage;
    }

    public bool IsEntityIotaHolder(EntityIota entity) => FindEntityStorage(entity) is not null;

    public bool IsEntityIotaWritable(EntityIota entity)
        => FindEntityStorage(entity) is { Writeable: true };

    public bool CanWriteEntityIota(EntityIota entity, Iota datum)
        => FindEntityStorage(entity)?.WriteIota(datum, simulate: true) ?? false;

    public Iota? ReadEntityIota(EntityIota entity) => FindEntityStorage(entity)?.Read();

    public bool WriteEntityIota(EntityIota entity, Iota value)
    {
        if (FindEntityStorage(entity) is not { } storage || !storage.WriteIota(value, simulate: false)) return false;
        // 地上的掉落物 / 物品框归服务端：写完广播一次（NetSend 带着里面的 iota），不然别人看到、拿到的还是旧的
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            if (FrameOf(entity) is { } f) NetMessage.SendData(Terraria.ID.MessageID.TileEntitySharing, -1, -1, null, f.ID, f.Position.X, f.Position.Y);
            else NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, entity.Index);
        }
        return true;
    }

    // ── 世界效果 ────────────────────────────────────────────────────

    /// <summary>改变天气。泰拉用 `Main.raining` + `Main.rainTime`（tick）。</summary>
    public void SetRain(bool rain, int minMinutes, int maxMinutes)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        // 泰拉 1 分钟 = 3600 tick
        int minutes = Main.rand.Next(minMinutes, maxMinutes);
        Main.raining = rain;
        Main.rainTime = minutes * 3600;
        Main.maxRaining = rain ? 1f : 0f;

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendData(Terraria.ID.MessageID.WorldData);
        }
    }

    /// <summary>点燃实体。</summary>
    public void IgniteEntity(EntityIota entity)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        switch (entity.Target)
        {
            case EntityIota.EntityKind.Player:
                if (entity.Index >= 0 && entity.Index < Main.maxPlayers)
                {
                    AddPlayerBuff(Main.player[entity.Index], Terraria.ID.BuffID.OnFire, 300);
                }
                break;
            case EntityIota.EntityKind.Npc:
                if (entity.Index >= 0 && entity.Index < Main.maxNPCs)
                {
                    Main.npc[entity.Index].AddBuff(Terraria.ID.BuffID.OnFire, 300);
                }
                break;
        }
    }

    /// <summary>
    /// 点燃一个位置。
    /// 注意：泰拉没有 MC 那样的火焰方块，所以实现为「烧这一格附近的实体 + 撒火粒子」。
    /// </summary>
    public void IgniteAt(double x, double y)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        float cx = (float)(x * HexUnits.PixelsPerTile);
        float cy = (float)(y * HexUnits.PixelsPerTile);

        foreach (var entity in QueryEntities(
            Core.Casting.Actions.ZoneEntityFilter.Living, negate: false, x, y, 1.5))
        {
            IgniteEntity(entity);
        }

        for (int k = 0; k < 12; k++)
        {
            var d = Terraria.Dust.NewDustPerfect(
                new Microsoft.Xna.Framework.Vector2(cx + Main.rand.Next(-8, 9), cy + Main.rand.Next(-8, 9)),
                Terraria.ID.DustID.Torch,
                new Microsoft.Xna.Framework.Vector2(0f, -Main.rand.NextFloat() * 1.5f));
            d.noGravity = true;
        }
    }

    /// <summary>
    /// 扑灭一片区域。泰拉没有火焰方块，所以实际做的是
    /// 「扑灭附近实体身上的着火状态」—— 泛洪的规模限制保留（源项目 1024 格）。
    /// </summary>
    public void ExtinguishAt(double x, double y, int maxCount)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        // 用查询半径近似 source 的泛洪范围（避免遍历上百万格）
        double radius = System.Math.Min(System.Math.Sqrt(maxCount) / 2.0, 16.0);

        foreach (var entity in QueryEntities(
            Core.Casting.Actions.ZoneEntityFilter.Living, negate: false, x, y, radius))
        {
            switch (entity.Target)
            {
                case EntityIota.EntityKind.Player:
                    Main.player[entity.Index].ClearBuff(Terraria.ID.BuffID.OnFire);
                    Main.player[entity.Index].ClearBuff(Terraria.ID.BuffID.OnFire3);
                    break;
                case EntityIota.EntityKind.Npc:
                    ClearNpcBuff(Main.npc[entity.Index], Terraria.ID.BuffID.OnFire);
                    ClearNpcBuff(Main.npc[entity.Index], Terraria.ID.BuffID.OnFire3);
                    break;
            }
        }

        // 视觉：一圈水汽
        float cx = (float)(x * HexUnits.PixelsPerTile);
        float cy = (float)(y * HexUnits.PixelsPerTile);
        for (int k = 0; k < 20; k++)
        {
            var d = Terraria.Dust.NewDustPerfect(
                new Microsoft.Xna.Framework.Vector2(
                    cx + (float)(Main.rand.NextDouble() * 2 - 1) * (float)radius * 16f,
                    cy + (float)(Main.rand.NextDouble() * 2 - 1) * (float)radius * 16f),
                Terraria.ID.DustID.Smoke, Microsoft.Xna.Framework.Vector2.Zero);
            d.noGravity = true;
        }
    }

    /// <summary>造出一格水。泰拉用 `tile.LiquidAmount` + `LiquidType`。</summary>
    public void CreateWaterAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        var tile = Main.tile[tx, ty];
        if (tile.HasTile) return;   // 有方块就不放水

        tile.LiquidAmount = 255;
        tile.LiquidType = Terraria.ID.LiquidID.Water;
        // 只改数值不会让液体流动：要登记给液体模拟、并刷新周围的方块外观
        Liquid.AddWater(tx, ty);
        WorldGen.SquareTileFrame(tx, ty, true);
        SpellSounds.At("spell.liquid", new Vector2(tx * 16 + 8, ty * 16 + 8));

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendTileSquare(-1, tx, ty, 1);
        }
    }

    /// <summary>抽干一片水域（泛洪，上限 maxCount 格）。</summary>
    public void DestroyWaterAt(double x, double y, int maxCount)
    {
        int sx = (int)System.Math.Floor(x);
        int sy = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(sx, sy, 1)) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        // 广度优先抽水：只走「有水且无方块」的格子
        var todo = new System.Collections.Generic.Queue<(int X, int Y)>();
        var seen = new System.Collections.Generic.HashSet<(int X, int Y)>();
        todo.Enqueue((sx, sy));
        seen.Add((sx, sy));

        int drained = 0;
        var minX = sx; var maxX = sx; var minY = sy; var maxY = sy;

        while (todo.Count > 0 && drained < maxCount)
        {
            var (cx, cy) = todo.Dequeue();
            if (!WorldGen.InWorld(cx, cy, 1)) continue;

            var tile = Main.tile[cx, cy];
            if (tile.HasTile || tile.LiquidAmount == 0) continue;

            tile.LiquidAmount = 0;
            WorldGen.SquareTileFrame(cx, cy, true);   // 邻格的液体要重新计算流向
            drained++;

            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy;
            if (cy > maxY) maxY = cy;

            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                var k = (cx + dx, cy + dy);
                if (seen.Add(k)) todo.Enqueue(k);
            }
        }

        if (drained > 0 && Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendTileSquare(-1, minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }

    /// <summary>
    /// 召下一道闪电。泰拉没有可生成的闪电实体（`Main.lightning` 只是背景闪光强度），
    /// 所以用「雷声 + 闪光 + 从天而降的电光 + 伤害」组合。
    ///
    /// 原版是一道真正的 MC 闪电：劈中范围内**所有**生物（包括施法者自己）造成伤害并点燃。
    /// 伤害按比例换算：MC 闪电 5 点 = 满血 20 的 25%，这里对玩家取最大生命的 25%；
    /// 对 NPC 取 80（旧实现的数值，早中期怪一击重伤、后期只是点燃）。
    /// </summary>
    public void SpawnLightning(double x, double y)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        float cx = (float)(x * HexUnits.PixelsPerTile);
        float cy = (float)(y * HexUnits.PixelsPerTile);

        Main.lightning = 1f;
        SpellSounds.At("spell.lightning", new Vector2(cx, cy));

        // 电光：从上方 30 格斜劈下来的一串电火花
        float topY = cy - (30 * HexUnits.PixelsPerTile);
        float xOff = 0f;
        for (float py = topY; py < cy; py += 6f)
        {
            xOff += Main.rand.NextFloat(-3f, 3f);
            var d = Terraria.Dust.NewDustPerfect(new Vector2(cx + xOff * (cy - py) / (cy - topY), py),
                Terraria.ID.DustID.Electric, Vector2.Zero, 0, default, 1.3f);
            d.noGravity = true;
        }
        for (int k = 0; k < 25; k++)
        {
            var d = Terraria.Dust.NewDustPerfect(
                new Vector2(cx + Main.rand.Next(-10, 11), cy + Main.rand.Next(-10, 6)),
                Terraria.ID.DustID.Electric,
                new Vector2(Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat() * 3f));
            d.noGravity = true;
        }

        foreach (var entity in QueryEntities(
            Core.Casting.Actions.ZoneEntityFilter.Living, negate: false, x, y, 2.0))
        {
            if (entity.Target == EntityIota.EntityKind.Npc)
            {
                var npc = Main.npc[entity.Index];
                if (npc.dontTakeDamage) continue;
                npc.SimpleStrikeNPC(80, 0, false, 0f);
                npc.AddBuff(Terraria.ID.BuffID.OnFire, 300);
            }
            else if (entity.Target == EntityIota.EntityKind.Player)
            {
                var p = Main.player[entity.Index];
                p.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(
                    Terraria.Localization.NetworkText.FromLiteral(p.name + "被雷劈中了")),
                    System.Math.Max(1, p.statLifeMax2 / 4), 0);
                AddPlayerBuff(p, Terraria.ID.BuffID.OnFire, 300);
            }
        }
    }

    /// <summary>
    /// 催熟（原版 edify 以外的「骨粉」效果：BoneMealItem.growCrop）。泰拉的对应物：
    ///   - 树苗 → 长成树（原版树苗生长逻辑）
    ///   - 未成熟草药（ImmatureHerbs）→ 成熟草药（MatureHerbs）：泰拉草药按**方块类型**分阶段，
    ///     帧 X 表示的是**草药种类**
    ///   - 草地（上方为空）→ 长出一株草（原版骨粉撒在草方块上会冒花草）
    ///
    /// 注意：旧实现把「任意非实心方块」的帧 X 减 36 当作「推进生长」：
    /// 对椅子、桌子、门、火把会把贴图帧改坏；对草药会把一种草药变成另一种。
    /// </summary>
    public void ApplyBonemeal(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        var tile = Main.tile[tx, ty];
        bool changed = false;

        if (tile.HasTile && Terraria.ID.TileID.Sets.CommonSapling[tile.TileType])
        {
            changed = WorldGen.AttemptToGrowTreeFromSapling(tx, ty, false);
        }
        else if (tile.HasTile && tile.TileType == Terraria.ID.TileID.ImmatureHerbs)
        {
            tile.TileType = Terraria.ID.TileID.MatureHerbs;
            WorldGen.SquareTileFrame(tx, ty, true);
            changed = true;
        }
        else
        {
            // 瞄准草方块本身，或草方块上方的空格：在草上长一株草
            int gy = tile.HasTile && tile.TileType == Terraria.ID.TileID.Grass ? ty : ty + 1;
            if (WorldGen.InWorld(tx, gy, 1) && Main.tile[tx, gy].HasTile && Main.tile[tx, gy].TileType == Terraria.ID.TileID.Grass
                && !Main.tile[tx, gy - 1].HasTile)
            {
                changed = WorldGen.PlaceTile(tx, gy - 1, Terraria.ID.TileID.Plants, true, false, -1, Main.rand.Next(6, 11));
                ty = gy - 1;
            }
        }

        if (!changed) return;
        SpellSounds.At("spell.grow", new Vector2(tx * 16 + 8, ty * 16 + 8));
        for (int k = 0; k < 8; k++)
        {
            Terraria.Dust.NewDust(new Vector2(tx * 16, ty * 16), 16, 16, Terraria.ID.DustID.GrassBlades);
        }
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendTileSquare(-1, tx - 1, ty - 1, 3);
        }
    }

    // ── 单点法术（beep / create_lava / edify / place_block / recharge）──

    /// <summary>
    /// 泰拉侧的乐器表。索引 = `beep` 的乐器参数。
    ///
    /// 注意：MC 有 16 种音符盒乐器，泰拉没有音符盒 —— 这里用的是**泰拉真实存在**的音效：
    /// 竖琴（`Item153`，原版里它跟随 `Main.musicPitch`）+ 6 个吉他和弦 + 7 件鼓组。
    /// 编号与 MC 的乐器列表**不对应**，这是无法消除的差异；
    /// 能保证的是「同一编号永远是同一种音色」。
    /// </summary>
    private static readonly Terraria.Audio.SoundStyle[] Instruments =
    {
        Terraria.ID.SoundID.Item153,           // 0  竖琴
        Terraria.ID.SoundID.GuitarC,           // 1  吉他 C
        Terraria.ID.SoundID.GuitarD,           // 2  吉他 D
        Terraria.ID.SoundID.GuitarEm,          // 3  吉他 Em
        Terraria.ID.SoundID.GuitarG,           // 4  吉他 G
        Terraria.ID.SoundID.GuitarBm,          // 5  吉他 Bm
        Terraria.ID.SoundID.GuitarAm,          // 6  吉他 Am
        Terraria.ID.SoundID.DrumKick,          // 7  底鼓
        Terraria.ID.SoundID.DrumTamaSnare,     // 8  军鼓
        Terraria.ID.SoundID.DrumClosedHiHat,   // 9  闭合踩镲
        Terraria.ID.SoundID.DrumCymbal1,       // 10 吊镲
        Terraria.ID.SoundID.DrumTomHigh,       // 11 高音通鼓
        Terraria.ID.SoundID.DrumTomMid,        // 12 中音通鼓
        Terraria.ID.SoundID.DrumTomLow,        // 13 低音通鼓
    };

    /// <summary>
    /// 播放一个音符。
    ///
    /// 音高映射：源项目的音高是 0~24（两个八度），
    /// 泰拉 `SoundStyle.Pitch` 的范围也是 -1.0（低一个八度）~ 1.0（高一个八度），
    /// 所以 `(note - 12) / 12` 正好铺满整个音域，不需要截断。
    ///
    /// 联机：声音是纯客户端表现，服务端播不了 —— 所以服务端走 <see cref="Net.HexNetSync"/>
    /// 广播给附近玩家（对齐源项目的 `MsgBeepS2C`，那边也是发给 128 格内的玩家）。
    /// </summary>
    public void Beep(double x, double y, int instrument, int note)
    {
        if (instrument < 0 || instrument >= Instruments.Length) return;
        if (note < 0 || note > Core.Casting.Actions.OpBeep.MaxNote) return;

        float px = (float)(x * HexUnits.PixelsPerTile);
        float py = (float)(y * HexUnits.PixelsPerTile);
        float pitch = (note - 12) / 12f;

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Net.HexNetSync.BroadcastBeep(px, py, (byte)instrument, (byte)note);
            return;
        }

        PlayBeep(px, py, instrument, note, pitch);
    }

    /// <summary>客户端侧的真正播放。服务端与联机客户端都走这里（联机客户端由广播包触发）。</summary>
    internal static void PlayBeep(float px, float py, int instrument, int note, float pitch)
    {
        if (Main.dedServ) return;
        if (instrument < 0 || instrument >= Instruments.Length) return;

        var style = Instruments[instrument] with { Pitch = pitch };
        Terraria.Audio.SoundEngine.PlaySound(style, new Vector2(px, py));
    }

    /// <summary>造出一格岩浆。与 <see cref="CreateWaterAt"/> 同构，只是液体类型不同。</summary>
    public void CreateLavaAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        var tile = Main.tile[tx, ty];
        if (tile.HasTile) return;

        tile.LiquidAmount = 255;
        tile.LiquidType = Terraria.ID.LiquidID.Lava;
        // 只改数值不会让液体流动：要登记给液体模拟、并刷新周围的方块外观
        Liquid.AddWater(tx, ty);
        WorldGen.SquareTileFrame(tx, ty, true);
        SpellSounds.At("spell.liquid", new Vector2(tx * 16 + 8, ty * 16 + 8));

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendTileSquare(-1, tx, ty, 1);
        }
    }

    /// <summary>该格是不是树苗。对应源项目的 `BlockTags.SAPLINGS`。</summary>
    public bool IsSaplingAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;

        var tile = Main.tile[tx, ty];
        if (!tile.HasTile) return false;

        return Terraria.ID.TileID.Sets.CommonSapling[tile.TileType];
    }

    /// <summary>把树苗催成树。用的是原版自己的树苗生长逻辑，所以长出来的树与自然生长一致。</summary>
    public bool GrowTreeAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return false;

        return WorldGen.AttemptToGrowTreeFromSapling(tx, ty, false);
    }

    /// <summary>
    /// 从施法者背包里找一件可放置的物品，放到该格。
    ///
    /// 「可放置」的判定用 `Item.createTile >= 0` —— 这正是泰拉自己的判定，
    /// 比维护一张白名单可靠（模组物品只要有 createTile 就自动支持）。
    ///
    /// 放之前再查一次可替换性：媒质预检与实际施放之间隔了一帧，
    /// 期间别人可能已经把方块放上去了。
    /// </summary>
    public bool PlaceBlockAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (_caster is null) return false;
        if (!WorldGen.InWorld(tx, ty, 1)) return false;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return false;

        int slot = FindPlaceableSlot(_caster);
        if (slot < 0) return false;

        if (!IsReplaceable(x, y)) return false;

        Item item = _caster.inventory[slot];
        int style = item.placeStyle;

        bool placed = WorldGen.PlaceTile(tx, ty, item.createTile, mute: false, forced: true,
            plr: _caster.whoAmI, style: style);

        if (!placed) return false;
        SpellSounds.At("spell.place", new Vector2(tx * 16 + 8, ty * 16 + 8));

        // 用掉一件。刻意不用 Player.ConsumeItem（会顺手触发拾取 / 消耗统计与音效）。
        // 走 PlayerEffects：联机时背包归本人客户端管，服务端扣了会被忽略 ——
        // 这里曾经在服务端扣 + 发 SyncPlayer，结果客户端手里的方块一块不少（刷方块）。
        PlayerEffects.ConsumeSlot(_caster, slot, 1);

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendTileSquare(-1, tx, ty, 1);
        }

        return true;
    }

    /// <summary>
    /// 找要放的方块。照原版 CastingEnvironment.getUsableStacksForPlayer(QUERY)：
    /// **只看快捷栏**，从手持物品（法杖）**右边一格**开始往右绕一圈 —— 玩家把想放的方块摆在法杖右边就行。
    /// 旧实现扫整个背包取第一个可放置物，结果可能是箱子、雕像、家具。
    /// </summary>
    private static int FindPlaceableSlot(Player player)
    {
        const int hotbar = 10;
        for (int d = 1; d <= hotbar; d++)
        {
            int i = (player.selectedItem + d) % hotbar;
            Item item = player.inventory[i];
            if (item is null || item.IsAir || item.stack <= 0) continue;
            // `createTile` 的「不可放置」值是 -1（原版 Item.createTile 的约定）
            if (item.createTile == -1) continue;
            return i;
        }

        return -1;
    }

    /// <summary>地上那个掉落物（活着的）；不是掉落物 → null。</summary>
    private static Item? GroundItem(EntityIota e)
    {
        if (e.Target != EntityIota.EntityKind.Item || e.Index < 0 || e.Index >= Main.maxItems) return null;
        Item w = Main.item[e.Index];
        return w is null || !w.active || w.IsAir ? null : w;
    }

    /// <summary>源项目 withdrawMedia(-1, simulate)：媒质材料 = 单件 × 数量；媒质瓶 = 存量（造瓶 / 打包时不算瓶子）。</summary>
    public long ItemEntityMedia(EntityIota itemEntity, bool forBattery)
    {
        var w = GroundItem(itemEntity);
        if (w is null) return 0;
        return w.ModItem switch
        {
            Items.MediaMaterial m => m.MediaValue * w.stack,
            Items.MediaFlask f when !forBattery => f.Media,
            _ => 0,
        };
    }

    /// <summary>
    /// 源项目 extractMedia(stack, cost, drainForBatteries)：媒质材料按整件扣（ceil(cost / 单件)，多的浪费），
    /// 媒质瓶按量扣；cost &lt; 0 = 全部。抽空了掉落物消失。
    /// </summary>
    public long DrainItemEntity(EntityIota itemEntity, long cost, bool forBattery)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return 0;
        var w = GroundItem(itemEntity);
        if (w is null) return 0;
        Item item = w;
        long got;
        switch (item.ModItem)
        {
            case Items.MediaMaterial m:
            {
                long unit = m.MediaValue;
                int used = cost < 0 ? item.stack : (int)System.Math.Min((cost + unit - 1) / unit, item.stack);
                item.stack -= used;
                got = used * unit;
                break;
            }
            case Items.MediaFlask f when !forBattery:
                got = f.Withdraw(cost < 0 ? f.Media : cost);
                break;
            default:
                return 0;
        }
        if (item.stack <= 0)
        {
            w.TurnToAir();
        }
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, itemEntity.Index);
        }
        return got;
    }

    // ── 咒法飞行（flight 系列）────────────────────────────────────

    /// <summary>
    /// 向上弹一下。对应源项目 `target.push(0, 1.5, 0)`。
    ///
    /// 数值换算：源项目是 `push(0, 1.5, 0)`，即 **1.5 格/tick**。
    /// 按 <see cref="ApplyMotion"/> 里那条换算（1 格/tick = 16/3 px/帧）：
    /// 1.5 × 16/3 = 8 px/帧。
    ///
    /// 注意：之前这里写的是 12（凭手感），与 `add_motion` 的换算**不一致** ——
    /// 于是「法术弹一下」和「Altiora 起飞」是两套尺度。现在统一走同一条换算。
    /// </summary>
    public void LaunchUp(EntityIota target)
    {
        var player = FindPlayer(target);
        if (player is null) return;

        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        player.velocity.Y = -1.5f * (HexUnits.PixelsPerTile / 3f);
        player.fallStart = (int)(player.position.Y / 16f);   // 免得落地时按「从弹起点坠落」算伤害
        player.fallStart2 = player.fallStart;

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendData(Terraria.ID.MessageID.SyncPlayer, -1, -1, null, player.whoAmI);
        }
    }

    public void GrantFlight(EntityIota target, int ticks, double originX, double originY, double radius, int graceTicks)
    {
        var player = FindPlayer(target);
        if (player is null) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        HexPlayer.Get(player).GrantFlight(ticks, originX, originY, radius, graceTicks);
    }

    public bool HasHexFlight(EntityIota target)
    {
        var player = FindPlayer(target);
        return player is not null && HexPlayer.Get(player).FlightActive;
    }

    /// <summary>把实体 iota 解析成玩家；不是玩家（或索引失效）返回 null。</summary>
    private static Player? FindPlayer(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Player) return null;
        if (entity.Index < 0 || entity.Index >= Main.maxPlayers) return null;

        var player = Main.player[entity.Index];
        return player is { active: true } ? player : null;
    }

    // ── 脑叶切除（brainsweep）──────────────────────────────────────

    /// <summary>
    /// 该位置能不能被改动。
    ///
    /// 泰拉侧没有 MC 的「冒险模式 / 领地保护」概念，所以这里的对应物是
    /// **出生点保护区**（`Main.spawnTileX/Y` 周围）——
    /// 那是原版唯一自带「你不能动这里」语义的地方。
    /// </summary>
    public bool CanEditAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;

        // 出生点 40 格内不允许（新城堡/地牢这类特殊区域不改，避免误伤建筑）
        int dx = System.Math.Abs(tx - Main.spawnTileX);
        int dy = System.Math.Abs(ty - Main.spawnTileY);
        return dx > 40 || dy > 40;
    }

    /// <summary>该位置的方块类型；-1 表示空气。</summary>
    public int TileTypeAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return -1;

        var tile = Main.tile[tx, ty];
        return tile.HasTile ? tile.TileType : -1;
    }

    /// <summary>
    /// 实体的「种类编号」。
    ///
    /// 城镇 NPC 走负数编码（<see cref="Core.Casting.Actions.BrainsweepRules.TownNpcSpecies"/>），
    /// 因为配方表里有「任意城镇 NPC」这一档 —— 普通怪物的 netID 全是正数，
    /// 用符号区分比维护一张白名单可靠（模组的城镇 NPC 也自动落进来）。
    /// </summary>
    public int EntitySpeciesOf(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Npc) return 0;
        if (entity.Index < 0 || entity.Index >= Main.maxNPCs) return 0;

        var npc = Main.npc[entity.Index];
        if (npc is not { active: true }) return 0;

        return npc.townNPC
            ? Core.Casting.Actions.BrainsweepRules.TownNpcSpecies(npc.netID)
            : npc.netID;
    }

    /// <summary>
    /// 能不能被切除。
    ///
    /// 原版：任何 Mob 都行，只排除 `NO_BRAINSWEEPING` 标签（默认是空的）；没有配方的生物照样报 MishapBadBrainsweep。
    /// 泰拉：任何非 Boss 的 NPC（小精灵这种敌怪也行 —— 它就是悦灵的对应物）。
    /// 这里曾经只放行城镇 NPC 和小动物，是移植版自己加的限制。
    /// </summary>
    public bool IsBrainsweepable(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Npc) return false;
        if (entity.Index < 0 || entity.Index >= Main.maxNPCs) return false;

        var npc = Main.npc[entity.Index];
        if (npc is not { active: true } || npc.life <= 0) return false;

        // Boss 永远不切 —— 这一条即使开了调试开关也保留：
        // 切掉 Boss 会让世界状态变得很难恢复（事件计数、进度标记都已写入）
        if (npc.boss) return false;

        return true;
    }

    public bool IsBrainswept(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Npc) return false;
        if (entity.Index < 0 || entity.Index >= Main.maxNPCs) return false;

        var npc = Main.npc[entity.Index];
        return npc is { active: true } && npc.GetGlobalNPC<HexGlobalNPC>().Brainswept;
    }

    /// <summary>
    /// 执行切除：换方块、杀掉生物、掉落产物。
    ///
    /// 顺序照抄源项目：**先换方块，再处理生物**。
    /// 反过来的话，如果生物死亡触发了什么（掉落、事件计数），
    /// 那些逻辑会看到一个还没变的方块状态。
    /// </summary>
    public void Brainsweep(double x, double y, EntityIota target, Core.Casting.Actions.BrainsweepRecipe recipe)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        // ① 方块
        if (recipe.ResultTile >= 0 && WorldGen.InWorld(tx, ty, 1))
        {
            var tile = Main.tile[tx, ty];
            tile.HasTile = true;
            tile.TileType = (ushort)recipe.ResultTile;
            tile.TileFrameX = 0;
            tile.TileFrameY = 0;

            if (Main.netMode == Terraria.ID.NetmodeID.Server)
            {
                Terraria.NetMessage.SendTileSquare(-1, tx, ty, 1);
            }
        }

        // ② 产物
        if (recipe.ResultItem >= 0)
        {
            // 用 (source, position, size, type, stack) 这个重载 ——
            // 它接像素坐标与尺寸，正好对上 Core 给的图格中心。
            Item.NewItem(new Terraria.DataStructures.EntitySource_Misc("HexBrainsweep"),
                new Vector2((float)(x * HexUnits.PixelsPerTile), (float)(y * HexUnits.PixelsPerTile)),
                new Vector2(16, 16), recipe.ResultItem, 1);
        }

        // ③ 生物：**失去意识，但活着**（原版 HexAPI.brainsweep：AI 停止、不出声、不能交互，见 HexGlobalNPC）。
        //    这里曾经直接杀掉 —— 原版只有对「已经失去意识」的生物再施放一次（MishapAlreadyBrainswept）才会杀死。
        //    原版还会放一声它的死亡音效 + 升级音效。
        if (target.Target == EntityIota.EntityKind.Npc
            && target.Index >= 0 && target.Index < Main.maxNPCs)
        {
            var npc = Main.npc[target.Index];
            if (npc is { active: true })
            {
                HexGlobalNPC.MakeBrainswept(npc);
                if (Main.netMode != Terraria.ID.NetmodeID.Server)
                {
                    Terraria.Audio.SoundEngine.PlaySound(npc.DeathSound, npc.Center);
                    Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.Item4 with { Volume = 0.5f, Pitch = -0.2f }, npc.Center);
                }
            }
        }
    }

    // ── 方块操作 ────────────────────────────────────────────────────

    /// <summary>该格是否可以被替换。</summary>
    public bool IsReplaceable(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;

        var tile = Main.tile[tx, ty];
        if (!tile.HasTile) return true;                      // 空气

        // 只认「草、花、藤」这类贴地小物件；实心方块不算可替换
        return !Main.tileSolid[tile.TileType] && tile.TileType != ModContent.TileType<ConjuredBlock>();
    }

    /// <summary>凭空造出召唤方块/光源。替换前先确认是空气。</summary>
    public void ConjureBlock(double x, double y, bool light)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;   // 服务端权威

        var tile = Main.tile[tx, ty];
        if (tile.HasTile) return;

        tile.HasTile = true;
        tile.TileType = (ushort)(light ? ModContent.TileType<ConjuredLight>() : ModContent.TileType<ConjuredBlock>());

        WorldGen.SquareTileFrame(tx, ty, true);   // 直接写方块数据后要刷新帧，否则和邻格的外观接不上
        ConjuredBlocks.Track(tx, ty, (int)(ConjuredBlocks.DefaultLifetimeSeconds * 60));

        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendTileSquare(-1, tx, ty, 1);
        }
    }

    /// <summary>
    /// 是否是「廉价可挖」的方块。
    /// 判据：非实心（草、花、藤这类装饰）—— 与源项目的方块标签作用一致。
    /// </summary>
    public bool IsCheapToBreak(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;

        var tile = Main.tile[tx, ty];
        if (!tile.HasTile) return true;

        return !Main.tileSolid[tile.TileType];
    }

    /// <summary>
    /// 该格现在能不能挖。不能时给出**人话原因** ——
    /// 这条原因会直接出现在施法失败的提示里，是「破坏魔法没反应」唯一的排查线索。
    /// </summary>
    public bool CanBreakBlockAt(double x, double y, out string reason)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1))
        {
            reason = "超出世界范围";
            return false;
        }

        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
        {
            // 联机时世界改动由服务端权威执行，客户端自己改会跟服务端打架
            reason = "联机客户端不直接改世界";
            return false;
        }

        if (!Main.tile[tx, ty].HasTile)
        {
            reason = "那一格是空的";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 挖掉该格的方块。
    ///
    /// 注意：走 `WorldGen.KillTile` 而不是 `tile.ClearTile()`：
    ///   · KillTile 会掉落物品、放挖掘特效、跑 ModTile.Kill / GlobalTile.Kill 钩子，
    ///     与源项目 `destroyBlock(pos, dropItems = true)` 的行为一致；
    ///   · ClearTile 是「无声抹掉」，不掉东西、不触发钩子。
    ///
    /// 「挖不动」的情形（基岩、未解锁的地牢砖、被其它模组保护的方块）由 KillTile 自己拒绝。
    /// 这里靠「挖完再看一眼」如实返回结果，而不是不管三七二十一都返回 true ——
    /// 后者会让上层以为成功，问题就再也浮不上来了。
    /// </summary>
    public bool BreakBlockAt(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!CanBreakBlockAt(x, y, out _))
        {
            return false;
        }

        // 挖之前取消召唤登记，避免倒计时表留下悬空条目
        ConjuredBlocks.Untrack(tx, ty);

        WorldGen.KillTile(tx, ty, fail: false, effectOnly: false, noItem: false);

        bool broke = !Main.tile[tx, ty].HasTile;

        if (broke && Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendTileSquare(-1, tx, ty, 1);
        }

        return broke;
    }

    /// <summary>取该坐标上最近的实体（不筛选种类）。</summary>
    public EntityIota? QueryNearestEntity(double x, double y)
    {
        EntityIota? best = null;
        double bestD2 = 0.25;   // 判定盒 pos±0.5 -> 半径 0.5

        void Consider(EntityIota iota)
        {
            if (!TryRead(iota, out var reading)) return;
            if (!IsInRange(iota)) return;

            double dx = reading.CenterTile.X - x;
            double dy = reading.CenterTile.Y - y;
            double d2 = dx * dx + dy * dy;
            if (d2 > bestD2) return;

            best = iota;
            bestD2 = d2;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true } || p.dead) continue;
            Consider(new EntityIota(EntityIota.EntityKind.Player, i));
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n is not { active: true }) continue;
            Consider(new EntityIota(EntityIota.EntityKind.Npc, i));
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            var pr = Main.projectile[i];
            if (pr is not { active: true }) continue;
            Consider(new EntityIota(EntityIota.EntityKind.Projectile, i));
        }
        for (int i = 0; i < Main.maxItems; i++)
        {
            var it = Main.item[i];
            if (it is not { active: true }) continue;
            Consider(new EntityIota(EntityIota.EntityKind.Item, i));
        }
        foreach (var f in AllFrames()) Consider(new EntityIota(EntityIota.EntityKind.ItemFrame, f.ID));

        return best;
    }

    /// <summary>
    /// 是否有实体的眼睛位置恰好落在该坐标上。
    ///
    /// 泰拉的「眼位」用实体中心近似（与 `EyePosition` 的取法一致）——
    /// 2D 侧视下眼高没有明确对应物，这条判定本来就是「贴脸」的近似。
    /// </summary>
    public bool HasEntityEyeExactlyAt(double x, double y)
    {
        const double eps = 0.01;

        bool Near(double cx, double cy)
            => System.Math.Abs(cx - x) < eps && System.Math.Abs(cy - y) < eps;

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true } || p.dead) continue;
            if (TryRead(new EntityIota(EntityIota.EntityKind.Player, i), out var rp) && Near(rp.EyeTile.X, rp.EyeTile.Y)) return true;
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n is not { active: true }) continue;
            if (TryRead(new EntityIota(EntityIota.EntityKind.Npc, i), out var rn) && Near(rn.EyeTile.X, rn.EyeTile.Y)) return true;
        }

        return false;
    }

    /// <summary>
    /// 爆炸。照原版（MC 的 Explosion）：
    ///   - 伤害随距离衰减：impact = 1 - 距离/(2×威力)，伤害 = (impact² + impact)/2 × 7 × 2×威力 + 1（MC 生命点）
    ///     换算到泰拉：MC 满血 20 → 这里按「玩家最大生命的比例」算；对 NPC 按 100 生命制（×5）算。
    ///   - **会炸掉方块**（原版在允许破坏时就是这样）：按泰拉炸弹的规则（地牢砖、神庙砖、箱子等炸不动），
    ///     范围 = 威力格。服务端设置「爆炸破坏方块」可以关掉。
    ///   - 火焰爆炸额外点燃。
    /// 旧实现是「伤害 = 威力 × 30、不衰减、不破坏方块」。
    /// </summary>
    public void Explode(double x, double y, double strength, bool fire)
        => ExplodeCore(x, y, strength, fire, Config.HexServerConfig.Instance.ExplosionsBreakBlocks);

    /// <summary>MishapBadBlock：0.25 强度、不破坏方块（原版 ExplosionInteraction.NONE）。</summary>
    public void MishapExplosion(double x, double y) => ExplodeCore(x, y, 0.25, fire: false, breakBlocks: false);

    /// <summary>MishapBadItem：掉落物往上弹（原版 +0.75 格/刻 → 16/3 换算 = 4 像素/帧）。</summary>
    public void MishapLaunchItem(EntityIota item)
    {
        if (item.Target != EntityIota.EntityKind.Item || item.Index < 0 || item.Index >= Main.maxItems) return;
        var it = Main.item[item.Index];
        if (it is not { active: true }) return;
        it.velocity.Y -= 0.75f * 16f / 3f;
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, item.Index);
        }
    }

    /// <summary>MishapBadBrainsweep（伤 1/20 生命）/ MishapAlreadyBrainswept（直接杀死）。</summary>
    public void MishapHurtEntity(EntityIota entity, bool kill)
    {
        if (entity.Target != EntityIota.EntityKind.Npc || entity.Index < 0 || entity.Index >= Main.maxNPCs) return;
        var n = Main.npc[entity.Index];
        if (n is not { active: true }) return;
        if (kill) { n.StrikeInstantKill(); }
        else { n.SimpleStrikeNPC(System.Math.Max(1, n.lifeMax / 20), 0); }
    }

    /// <summary>源项目 tag cannot_teleport（末影龙、凋灵这类）→ 泰拉：Boss、Boss 的身体部件、传送器不能传的 NPC（NPCID.Sets.TeleportationImmune）。</summary>
    public bool IsTeleportImmune(EntityIota entity)
    {
        if (entity.Target != EntityIota.EntityKind.Npc || entity.Index < 0 || entity.Index >= Main.maxNPCs) return false;
        var n = Main.npc[entity.Index];
        if (n is not { active: true }) return false;
        if (n.boss || Terraria.ID.NPCID.Sets.TeleportationImmune[n.type]) return true;   // 泰拉传送器的免疫集合，语义最接近
        return n.realLife >= 0 && n.realLife < Main.maxNPCs && Main.npc[n.realLife] is { active: true, boss: true };
    }

    public bool HasPlaceableInHotbar() => _caster is not null && FindPlaceableSlot(_caster) >= 0;

    private void ExplodeCore(double x, double y, double strength, bool fire, bool breakBlocks)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        float cx = (float)(x * HexUnits.PixelsPerTile);
        float cy = (float)(y * HexUnits.PixelsPerTile);
        double reach = 2.0 * strength;   // 伤害半径（格）
        float reachPx = (float)(reach * HexUnits.PixelsPerTile);

        double McDamage(float distPx)
        {
            double impact = 1.0 - (distPx / HexUnits.PixelsPerTile) / reach;
            if (impact <= 0) return 0;
            return ((impact * impact + impact) / 2.0 * 7.0 * reach) + 1.0;
        }

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n is not { active: true } || n.dontTakeDamage) continue;
            float dist = Vector2.Distance(n.Center, new Vector2(cx, cy));
            double dmg = McDamage(dist);
            if (dmg <= 0) continue;
            n.SimpleStrikeNPC((int)System.Math.Ceiling(dmg * 5.0), n.Center.X >= cx ? 1 : -1, false, (float)strength * 2f);
            if (fire) n.AddBuff(Terraria.ID.BuffID.OnFire, 300);
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true } || p.dead || p.creativeGodMode) continue;
            float dist = Vector2.Distance(p.Center, new Vector2(cx, cy));
            double dmg = McDamage(dist);
            if (dmg <= 0) continue;
            int hurt = System.Math.Max(1, (int)System.Math.Ceiling(dmg / 20.0 * p.statLifeMax2));
            p.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(
                Terraria.Localization.NetworkText.FromLiteral(p.name + "被炸飞了")), hurt, p.Center.X >= cx ? 1 : -1);
            if (fire) AddPlayerBuff(p, Terraria.ID.BuffID.OnFire, 300);
        }

        if (breakBlocks)
        {
            int r = (int)System.Math.Ceiling(strength);
            int ix = (int)System.Math.Floor(x), iy = (int)System.Math.Floor(y);
            var rules = new Projectile();   // CanExplodeTile 只读图格数据，借一个空弹幕实例来问泰拉的炸弹规则
            for (int tx = ix - r; tx <= ix + r; tx++)
            {
                for (int ty = iy - r; ty <= iy + r; ty++)
                {
                    if (!WorldGen.InWorld(tx, ty, 2)) continue;
                    if ((tx - x) * (tx - x) + (ty - y) * (ty - y) > strength * strength) continue;
                    var t = Main.tile[tx, ty];
                    if (!t.HasTile || !rules.CanExplodeTile(tx, ty)) continue;
                    WorldGen.KillTile(tx, ty, false, false, false);
                    if (!Main.tile[tx, ty].HasTile && Main.netMode == Terraria.ID.NetmodeID.Server)
                    {
                        NetMessage.SendData(Terraria.ID.MessageID.TileManipulation, -1, -1, null, 0, tx, ty);
                    }
                }
            }
        }

        SpellSounds.At("spell.explode", new Vector2(cx, cy));
        for (int k = 0; k < 30; k++)
        {
            var vel = new Vector2(
                (float)(Main.rand.NextDouble() * 2 - 1),
                (float)(Main.rand.NextDouble() * 2 - 1)) * (float)strength * 2f;
            var d = Terraria.Dust.NewDustPerfect(new Vector2(cx, cy),
                fire ? Terraria.ID.DustID.Torch : Terraria.ID.DustID.Smoke, vel, 0, default, 1.6f);
            d.noGravity = true;
        }
        for (int g = 0; g < 3; g++)
        {
            Gore.NewGore(new Terraria.DataStructures.EntitySource_Misc("HexExplode"), new Vector2(cx - 24, cy - 24),
                new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(-1f, 1f)), Main.rand.Next(61, 64));
        }
        _ = reachPx;
    }

    /// <summary>
    /// 给玩家加 buff。联机时服务端的 AddBuff 只改服务端那份，不会到达玩家客户端（buff 归客户端管）；
    /// 原版唯一的「给别的玩家加 buff」消息（AddPlayerBuffPvP）只认 PvP buff，所以走自己的包。
    /// </summary>
    internal static void AddPlayerBuff(Player p, int buffType, int ticks)
    {
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Net.HexNetSync.SendPlayerBuff(p.whoAmI, buffType, ticks);
        }
        else
        {
            p.AddBuff(buffType, ticks);
        }
    }

    /// <summary>
    /// 施加药水效果。MC 的 10 个 `MobEffects` → 泰拉 buff 的映射。
    ///
    /// 有几条**没有精确对应物**，选了语义最接近的并注明：
    ///   - 飘浮（LEVITATION）→ 羽毛缓落：泰拉没有「向上飘」的 buff
    ///   - 凋零（WITHER）   → 诅咒地狱：同样是高伤持续伤害
    ///   - 吸收（ABSORPTION）→ 铁皮：泰拉没有「伤害护盾」类 buff，取防御向
    /// </summary>
    public void ApplyPotion(EntityIota entity, Core.Casting.Actions.PotionEffectKind effect,
                            int ticks, int potency)
    {
        if (ticks <= 0) return;

        int buffType = effect switch
        {
            Core.Casting.Actions.PotionEffectKind.Weakness => Terraria.ID.BuffID.Weak,
            Core.Casting.Actions.PotionEffectKind.Levitation => Terraria.ID.BuffID.Featherfall,
            Core.Casting.Actions.PotionEffectKind.Wither => Terraria.ID.BuffID.CursedInferno,
            Core.Casting.Actions.PotionEffectKind.Poison => Terraria.ID.BuffID.Poisoned,
            Core.Casting.Actions.PotionEffectKind.Slowness => Terraria.ID.BuffID.Slow,
            Core.Casting.Actions.PotionEffectKind.Regeneration => Terraria.ID.BuffID.Regeneration,
            Core.Casting.Actions.PotionEffectKind.NightVision => Terraria.ID.BuffID.NightOwl,
            Core.Casting.Actions.PotionEffectKind.Absorption => Terraria.ID.BuffID.Ironskin,
            Core.Casting.Actions.PotionEffectKind.Haste => Terraria.ID.BuffID.Mining,
            Core.Casting.Actions.PotionEffectKind.Strength => Terraria.ID.BuffID.Wrath,
            _ => -1,
        };

        if (buffType < 0) return;

        switch (entity.Target)
        {
            case EntityIota.EntityKind.Player:
            {
                if (entity.Index < 0 || entity.Index >= Main.maxPlayers) return;
                var p = Main.player[entity.Index];
                if (p is not { active: true } || p.dead) return;
                AddPlayerBuff(p, buffType, ticks);
                break;
            }

            case EntityIota.EntityKind.Npc:
            {
                if (entity.Index < 0 || entity.Index >= Main.maxNPCs) return;
                var n = Main.npc[entity.Index];
                if (n is not { active: true }) return;
                // 泰拉 NPC 的 buff 用 AddBuff；等级由 buff 类型本身决定（没有 amplifier 概念）
                n.AddBuff(buffType, ticks);
                break;
            }
        }
    }
    // ── 阿卡夏记录 ──────────────────────────────────────────────────

    /// <summary>该坐标是不是阿卡夏记录方块。</summary>
    public bool IsAkashicRecord(double x, double y)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        if (!WorldGen.InWorld(tx, ty, 1)) return false;

        var tile = Main.tile[tx, ty];
        if (!tile.HasTile) return false;

        return tile.TileType == Terraria.ModLoader.ModContent.TileType<AkashicRecord>();
    }

    /// <summary>按图案查记录。没有该键返回 null。</summary>
    public Core.Casting.Iotas.Iota? LookupAkashic(double x, double y, Core.Casting.Math.HexPattern key)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);
        return AkashicRecordEntity.FindAt(tx, ty)?.Lookup(key);
    }

    /// <summary>按图案写记录。</summary>
    public void WriteAkashic(double x, double y, Core.Casting.Math.HexPattern key, Core.Casting.Iotas.Iota value)
    {
        int tx = (int)System.Math.Floor(x);
        int ty = (int)System.Math.Floor(y);

        var entity = AkashicRecordEntity.FindAt(tx, ty);
        if (entity == null) return;

        entity.Store(key, value);

        // 联机：把变化同步出去，否则只有写入者的客户端能看到
        if (Main.netMode == Terraria.ID.NetmodeID.Server)
        {
            Terraria.NetMessage.SendData(Terraria.ID.MessageID.TileEntitySharing, -1, -1, null, entity.ID, tx, ty);
        }
    }
}
