using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.World;
using Terraria;

namespace HexCastingTerraria.Content;

/// <summary>
/// 法术坐标 ↔ 泰拉坐标的**唯一**换算层（套在 <see cref="TerrariaCastingWorld"/> 外面）。
///
/// 原版是 MC 的世界坐标：单位是方块、**+Y 朝上**、速度是「方块/刻」（20 刻/秒）。
/// 泰拉是：单位像素（这里的内层已经换成了图格）、**+Y 朝下**、速度「像素/帧」（60 帧/秒）。
/// 法术里的一切向量都按原版的约定（「向量之精思，+Y 型」是上、「驱动」+Y 往上推、书里的讲解也都成立），
/// 这一层负责翻过来：
///
/// | 量 | 法术 → 泰拉（内层） | 泰拉 → 法术 |
/// |---|---|---|
/// | 位置 | (x, H − y) | (x, H − y) |
/// | 方向 / 位移 | (x, −y) | (x, −y) |
/// | 速度 | — | (vx·3, −vy·3)（图格/帧 → 方块/刻） |
/// | 方块下标 | ty = H − 1 − by | by = H − 1 − ty |
///
/// H = <c>Main.maxTilesY</c>：世界底 = 0，越往上越大，与 MC 的高度方向一致，而且坐标永远是正数。
///
/// ⚠️ 方块下标不能用 floor(H − y)：y 恰好是整数时（人站在地上，脚底 y 就是整数）会差一格。
/// 原版「点在哪个方块里」是 floor(y)，对应泰拉 H − 1 − floor(y)；这里给点减一个极小量，
/// 让内层的 floor(H − y − ε) 恰好等于它（ε 远小于一个像素，不影响位置）。
///
/// ⚠️ 这里曾经没有这一层：整个移植直接用泰拉坐标（+Y 朝下），与原版方向相反，
/// 速度也少乘了 3（按帧读、按刻写，读出来再推回去只有 1/3）。
/// </summary>
public sealed class HexSpaceWorld : ICastingWorld
{
    private readonly TerrariaCastingWorld _inner;

    public HexSpaceWorld(TerrariaCastingWorld inner) => _inner = inner;

    private static double H => Main.maxTilesY;

    /// <summary>法术位置的 y → 内层图格 y（floor 后落在正确的格子里，见 HexAxes）。</summary>
    private static double InY(double y) => HexAxes.PointToTileY(y, H);

    /// <summary>内层图格 y → 法术位置的 y。</summary>
    private static double OutY(double y) => HexAxes.FlipPosition(y, H);

    /// <summary>MC 刻 / 泰拉帧。</summary>
    private const double TicksPerFrameRatio = 3.0;

    public EntityIota? Caster => _inner.Caster;

    public bool IsAlive(EntityIota entity) => _inner.IsAlive(entity);

    public bool IsInRange(EntityIota entity) => _inner.IsInRange(entity);

    public (double X, double Y) FeetPosition(EntityIota entity)
    {
        var (x, y) = _inner.FeetPosition(entity);
        return (x, OutY(y));
    }

    public (double X, double Y) EyePosition(EntityIota entity)
    {
        var (x, y) = _inner.EyePosition(entity);
        return (x, OutY(y));
    }

    public (double X, double Y) Velocity(EntityIota entity)
    {
        var (x, y) = _inner.Velocity(entity);
        return (x * TicksPerFrameRatio, -y * TicksPerFrameRatio);
    }

    public (double X, double Y) Look(EntityIota entity)
    {
        var (x, y) = _inner.Look(entity);
        return (x, -y);
    }

    public double EntityHeight(EntityIota entity) => _inner.EntityHeight(entity);

    public bool IsVecInRange(double x, double y) => _inner.IsVecInRange(x, InY(y));

    public bool IsTileSolid(int tileX, int tileY) => _inner.IsTileSolid(tileX, HexAxes.FlipBlock(tileY, Main.maxTilesY));

    public IReadOnlyList<EntityBox> EntitiesInArea(double minX, double minY, double maxX, double maxY)
    {
        var boxes = _inner.EntitiesInArea(minX, OutY(maxY), maxX, OutY(minY));
        var result = new List<EntityBox>(boxes.Count);
        foreach (var b in boxes)
        {
            result.Add(new EntityBox { Entity = b.Entity, MinX = b.MinX, MaxX = b.MaxX, MinY = OutY(b.MaxY), MaxY = OutY(b.MinY) });
        }
        return result;
    }

    public bool IsVecInWorld(double x, double y) => _inner.IsVecInWorld(x, InY(y));

    public void ApplyMotion(EntityIota entity, double mx, double my) => _inner.ApplyMotion(entity, mx, -my);

    public void TeleportBy(EntityIota entity, double dx, double dy) => _inner.TeleportBy(entity, dx, -dy);

    public void ScatterInventory(EntityIota entity, double distanceTiles) => _inner.ScatterInventory(entity, distanceTiles);

    public bool IsAkashicRecord(double x, double y) => _inner.IsAkashicRecord(x, InY(y));

    public Iota? LookupAkashic(double x, double y, Core.Casting.Math.HexPattern key) => _inner.LookupAkashic(x, InY(y), key);

    public void WriteAkashic(double x, double y, Core.Casting.Math.HexPattern key, Iota value) => _inner.WriteAkashic(x, InY(y), key, value);

    public IReadOnlyList<EntityIota> QueryEntities(Core.Casting.Actions.ZoneEntityFilter filter, bool negate, double x, double y, double radius)
        => _inner.QueryEntities(filter, negate, x, InY(y), radius);

    public void ApplyPotion(EntityIota entity, Core.Casting.Actions.PotionEffectKind effect, int ticks, int potency)
        => _inner.ApplyPotion(entity, effect, ticks, potency);

    public EntityIota? QueryNearestEntity(double x, double y) => _inner.QueryNearestEntity(x, InY(y));

    public bool HasEntityEyeExactlyAt(double x, double y) => _inner.HasEntityEyeExactlyAt(x, H - y);   // 精确比较，不加 ε

    public void Explode(double x, double y, double strength, bool fire) => _inner.Explode(x, InY(y), strength, fire);

    public bool IsTeleportImmune(EntityIota entity) => _inner.IsTeleportImmune(entity);

    public bool HasPlaceableInHotbar() => _inner.HasPlaceableInHotbar();

    public void MishapExplosion(double x, double y) => _inner.MishapExplosion(x, InY(y));

    public void MishapLaunchItem(EntityIota item) => _inner.MishapLaunchItem(item);

    public void MishapHurtEntity(EntityIota entity, bool kill) => _inner.MishapHurtEntity(entity, kill);

    public bool IsReplaceable(double x, double y) => _inner.IsReplaceable(x, InY(y));

    public void ConjureBlock(double x, double y, bool light) => _inner.ConjureBlock(x, InY(y), light);

    public bool IsCheapToBreak(double x, double y) => _inner.IsCheapToBreak(x, InY(y));

    public bool BreakBlockAt(double x, double y) => _inner.BreakBlockAt(x, InY(y));

    public bool CanBreakBlockAt(double x, double y, out string reason) => _inner.CanBreakBlockAt(x, InY(y), out reason);

    public void SetRain(bool rain, int minMinutes, int maxMinutes) => _inner.SetRain(rain, minMinutes, maxMinutes);

    public void IgniteEntity(EntityIota entity) => _inner.IgniteEntity(entity);

    public void IgniteAt(double x, double y) => _inner.IgniteAt(x, InY(y));

    public void ExtinguishAt(double x, double y, int maxCount) => _inner.ExtinguishAt(x, InY(y), maxCount);

    public void CreateWaterAt(double x, double y) => _inner.CreateWaterAt(x, InY(y));

    public void DestroyWaterAt(double x, double y, int maxCount) => _inner.DestroyWaterAt(x, InY(y), maxCount);

    public void SpawnLightning(double x, double y) => _inner.SpawnLightning(x, InY(y));

    public void ApplyBonemeal(double x, double y) => _inner.ApplyBonemeal(x, InY(y));

    public double NextDouble() => _inner.NextDouble();

    public bool IsSameEntityType(EntityIota a, EntityIota b) => _inner.IsSameEntityType(a, b);

    public bool CompareBlocks(double x1, double y1, double x2, double y2, bool exact) => _inner.CompareBlocks(x1, InY(y1), x2, InY(y2), exact);

    public bool CompareItems(EntityIota a, EntityIota b, bool exact) => _inner.CompareItems(a, b, exact);

    public bool IsEntityIotaHolder(EntityIota entity) => _inner.IsEntityIotaHolder(entity);

    public bool IsEntityIotaWritable(EntityIota entity) => _inner.IsEntityIotaWritable(entity);

    public Iota? ReadEntityIota(EntityIota entity) => _inner.ReadEntityIota(entity);

    public bool WriteEntityIota(EntityIota entity, Iota value) => _inner.WriteEntityIota(entity, value);

    public void Beep(double x, double y, int instrument, int note) => _inner.Beep(x, InY(y), instrument, note);

    public void CreateLavaAt(double x, double y) => _inner.CreateLavaAt(x, InY(y));

    public bool IsSaplingAt(double x, double y) => _inner.IsSaplingAt(x, InY(y));

    public bool GrowTreeAt(double x, double y) => _inner.GrowTreeAt(x, InY(y));

    public bool PlaceBlockAt(double x, double y) => _inner.PlaceBlockAt(x, InY(y));

    public long ItemEntityMedia(EntityIota itemEntity, bool forBattery) => _inner.ItemEntityMedia(itemEntity, forBattery);

    public long DrainItemEntity(EntityIota itemEntity, long cost, bool forBattery) => _inner.DrainItemEntity(itemEntity, cost, forBattery);

    public void LaunchUp(EntityIota target) => _inner.LaunchUp(target);

    public void GrantFlight(EntityIota target, int ticks, double originX, double originY, double radius, int graceTicks)
        => _inner.GrantFlight(target, ticks, originX, InY(originY), radius, graceTicks);

    public bool HasHexFlight(EntityIota target) => _inner.HasHexFlight(target);

    public bool CanEditAt(double x, double y) => _inner.CanEditAt(x, InY(y));

    public int TileTypeAt(double x, double y) => _inner.TileTypeAt(x, InY(y));

    public int EntitySpeciesOf(EntityIota entity) => _inner.EntitySpeciesOf(entity);

    public bool IsBrainsweepable(EntityIota entity) => _inner.IsBrainsweepable(entity);

    public bool IsBrainswept(EntityIota entity) => _inner.IsBrainswept(entity);

    public void Brainsweep(double x, double y, EntityIota target, Core.Casting.Actions.BrainsweepRecipe recipe)
        => _inner.Brainsweep(x, InY(y), target, recipe);

    // ── 给泰拉侧其它代码用的换算（哨卫、粒子、法术环） ─────────────────

    /// <summary>法术坐标 → 世界像素（位置）。</summary>
    public static Microsoft.Xna.Framework.Vector2 ToWorldPixels(double x, double y)
        => new((float)(x * 16.0), (float)((H - y) * 16.0));

    /// <summary>泰拉图格下标 → 法术方块下标（y）。</summary>
    public static int BlockY(int tileY) => HexAxes.FlipBlock(tileY, Main.maxTilesY);

    /// <summary>法术位置 y → 泰拉图格 y（连续值）。</summary>
    public static double TileY(double hexY) => H - hexY;
}
