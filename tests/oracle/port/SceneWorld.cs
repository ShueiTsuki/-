using HexCastingTerraria.Core.Casting.Actions;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.World;
using static HexCastingTerraria.Core.Casting.Iotas.EntityIota;

/// <summary>
/// 对拍用的测试场景，照原版那边的 mc/src/hexoracle/Scene.java 一样布置（坐标就是 MC 的坐标：y 朝上、一格 = 1）：
///   施法者：脚底 (0.5, -60)，眼高 1.62，身高 1.8，面朝 +x；它不在世界的实体列表里（MC 的假玩家没放进世界）
///   猪 "pig"：脚底 (3.5, -60)，0.9 × 0.9，眼高 0.765，面朝 +x
///   掉落物 "item"：脚底 (-2.5, -60)，0.25 × 0.25，3 个紫水晶粉
/// 尺寸照 MC 存成单精度（猪身高 0.9f 读出来是 0.8999999761581421），两边才对得上。
///   方块：平坦世界，基岩 y=-64，泥土 -63、-62，草方块 -61；石头 (2, -60)
///
/// 世界的规则（施法范围、按位置找实体、区域找实体……）照原版写在这里 —— 移植版这些规则在泰拉那一侧（TerrariaCastingWorld），
/// 离线测不到，这里测的是核心里图案的逻辑：先查什么、报哪种事故、返回什么、射线和扫掠的几何。
/// 会改世界的方法只记一笔，不改场景（现阶段的用例里法术的位置都在施法范围外）。
/// </summary>
sealed class SceneWorld : ICastingWorld
{
    sealed record SceneEntity(string Name, EntityIota Iota, double X, double Y, double Width, double Height, double Eye,
        bool InWorldList, bool Animal, bool Living, bool IsItem, bool IsPlayer);

    private static readonly SceneEntity Self = new("self", new EntityIota(EntityKind.Player, 0), 0.5, -60, 0.6f, 1.8f, 1.62f,
        InWorldList: false, Animal: false, Living: true, IsItem: false, IsPlayer: true);
    private static readonly SceneEntity Pig = new("pig", new EntityIota(EntityKind.Npc, 1), 3.5, -60, 0.9f, 0.9f, 0.9f * 0.85f,
        InWorldList: true, Animal: true, Living: true, IsItem: false, IsPlayer: false);
    private static readonly SceneEntity Item = new("item", new EntityIota(EntityKind.Item, 2), -2.5, -60, 0.25f, 0.25f, 0.25f * 0.85f,
        InWorldList: true, Animal: false, Living: false, IsItem: true, IsPlayer: false);
    private static readonly SceneEntity[] All = { Self, Pig, Item };

    /// <summary>原版 DEFAULT_AMBIT_RADIUS。</summary>
    private const double Ambit = 32.0;

    /// <summary>改世界的调用记在这里（不真改）。</summary>
    public List<string> Trace { get; } = new();

    public static EntityIota? ByName(string name) => All.FirstOrDefault(e => e.Name == name)?.Iota;

    public static string? NameOf(EntityIota e) => All.FirstOrDefault(s => s.Iota.Target == e.Target && s.Iota.Index == e.Index)?.Name;

    private static SceneEntity Of(EntityIota e)
        => All.FirstOrDefault(s => s.Iota.Target == e.Target && s.Iota.Index == e.Index)
           ?? throw new InvalidOperationException("场景里没有这个实体");

    // ---- 实体 ----

    public EntityIota? Caster => Self.Iota;

    public bool IsAlive(EntityIota entity) => true;

    /// <summary>原版 isEntityInRange：玩家照默认配置（trueNameHasAmbit）任何距离都算在范围内；别的看脚底在不在施法范围和世界里。</summary>
    public bool IsInRange(EntityIota entity)
    {
        var e = Of(entity);
        return e.IsPlayer || (IsVecInWorld(e.X, e.Y) && IsVecInRange(e.X, e.Y));
    }

    public (double X, double Y) FeetPosition(EntityIota entity) => (Of(entity).X, Of(entity).Y);

    public (double X, double Y) EyePosition(EntityIota entity) => (Of(entity).X, Of(entity).Y + Of(entity).Eye);

    public (double X, double Y) Velocity(EntityIota entity) => (0, 0);

    public (double X, double Y) Look(EntityIota entity) => (1, 0);

    public double EntityHeight(EntityIota entity) => Of(entity).Height;

    // ---- 范围、方块 ----

    /// <summary>原版 PlayerBasedCastEnv.isVecInRangeEnvironment：到施法者脚底的距离 ≤ 32（再加 1e-11 防误差）。</summary>
    public bool IsVecInRange(double x, double y) => IsVecInRange(x, y, 0);

    public bool IsVecInRange(double x, double y, double z)
    {
        double dx = x - Self.X, dy = y - Self.Y;
        return dx * dx + dy * dy + z * z <= Ambit * Ambit + 0.00000000001;
    }

    /// <summary>原版 isVecInWorld：方块坐标在建筑高度 [-64, 320) 里（世界边界远得很，不用管）。</summary>
    public bool IsVecInWorld(double x, double y)
    {
        double by = Math.Floor(y);
        return by >= -64 && by < 320;
    }

    private static string BlockAt(int x, int y) => y switch
    {
        -64 => "bedrock",
        -63 or -62 => "dirt",
        -61 => "grass_block",
        -60 when x == 2 => "stone",
        _ => "air",
    };

    private static string BlockAt(double x, double y) => BlockAt((int)Math.Floor(x), (int)Math.Floor(y));

    public bool IsTileSolid(int tileX, int tileY) => BlockAt(tileX, tileY) != "air";

    public int TileTypeAt(double x, double y) => BlockAt(x, y) switch
    {
        "air" => -1,
        "bedrock" => 1,
        "dirt" => 2,
        "grass_block" => 3,
        _ => 4,
    };

    public bool CompareBlocks(double x1, double y1, double x2, double y2, bool exact) => BlockAt(x1, y1) == BlockAt(x2, y2);

    public bool IsReplaceable(double x, double y) => BlockAt(x, y) == "air";

    public bool CanEditAt(double x, double y) => true;

    // ---- 区域、射线 ----

    private static IEnumerable<SceneEntity> InWorld => All.Where(e => e.InWorldList);

    private static bool BoxIntersects(SceneEntity e, double minX, double minY, double maxX, double maxY)
        => e.X + e.Width / 2 >= minX && e.X - e.Width / 2 <= maxX && e.Y + e.Height >= minY && e.Y <= maxY;

    public IReadOnlyList<EntityBox> EntitiesInArea(double minX, double minY, double maxX, double maxY)
        => InWorld.Where(e => BoxIntersects(e, minX, minY, maxX, maxY))
            .Select(e => new EntityBox { Entity = e.Iota, MinX = e.X - e.Width / 2, MaxX = e.X + e.Width / 2, MinY = e.Y, MaxY = e.Y + e.Height })
            .ToList();

    private static double Dist2(SceneEntity e, double x, double y) => (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y);

    private static bool Matches(SceneEntity e, ZoneEntityFilter filter) => filter switch
    {
        ZoneEntityFilter.Animal => e.Animal,
        ZoneEntityFilter.Monster => false,
        ZoneEntityFilter.Item => e.IsItem,
        ZoneEntityFilter.Player => e.IsPlayer,
        ZoneEntityFilter.Living => e.Living,
        _ => true,
    };

    /// <summary>原版 OpGetEntitiesBy：以点为中心、边长 2r 的方框先粗筛，再要求脚底到点的距离 ≤ r；按距离排序。</summary>
    public IReadOnlyList<EntityIota> QueryEntities(ZoneEntityFilter filter, bool negate, double x, double y, double radius)
        => InWorld.Where(e => BoxIntersects(e, x - radius, y - radius, x + radius, y + radius)
                              && Dist2(e, x, y) <= radius * radius && Matches(e, filter) != negate)
            .OrderBy(e => Dist2(e, x, y)).Select(e => e.Iota).ToList();

    /// <summary>原版 OpGetEntityAt：以点为中心、边长 1 的方框碰碰撞箱，取脚底离点最近的。</summary>
    public EntityIota? QueryNearestEntity(double x, double y)
        => InWorld.Where(e => BoxIntersects(e, x - 0.5, y - 0.5, x + 0.5, y + 0.5))
            .OrderBy(e => Dist2(e, x, y)).Select(e => e.Iota).FirstOrDefault();

    public bool HasEntityEyeExactlyAt(double x, double y) => InWorld.Any(e => e.X == x && e.Y + e.Eye == y);

    public double NextDouble() => 0.5;

    // ---- 物品与数据 ----

    public bool HasHeldItem(EntityIota entity) => Of(entity).IsItem;

    public bool CompareItems(EntityIota a, EntityIota b, bool exact) => Of(a).IsItem && Of(b).IsItem;

    public ItemStackInfo? ItemStackOf(EntityIota item) => Of(item).IsItem ? new ItemStackInfo("紫水晶粉", 3) : null;

    public bool IsSameEntityType(EntityIota a, EntityIota b) => Of(a).Name == Of(b).Name;

    public int EntitySpeciesOf(EntityIota entity) => Array.IndexOf(All, Of(entity));

    public bool IsEntityIotaHolder(EntityIota entity) => false;

    public bool IsEntityIotaWritable(EntityIota entity) => false;

    public Iota? ReadEntityIota(EntityIota entity) => null;

    public bool WriteEntityIota(EntityIota entity, Iota value) => Record(false, "write_entity");

    public long ItemEntityMedia(EntityIota itemEntity, bool forBattery) => Of(itemEntity).IsItem ? 3 * 10000 : 0;

    public long DrainItemEntity(EntityIota itemEntity, long cost, bool forBattery) => Record(0L, "drain_item");

    public bool IsAkashicRecord(double x, double y) => false;

    public Iota? LookupAkashic(double x, double y, HexPattern key) => null;

    public bool IsBrainsweepable(EntityIota entity) => false;

    public bool IsBrainswept(EntityIota entity) => false;

    public bool HasHexFlight(EntityIota target) => false;

    public bool IsSaplingAt(double x, double y) => false;

    public bool IsCheapToBreak(double x, double y) => BlockAt(x, y) != "bedrock";

    // ---- 改世界：只记一笔 ----

    private T Record<T>(T result, string what)
    {
        Trace.Add(what);
        return result;
    }

    public void ApplyMotion(EntityIota entity, double mx, double my) => Trace.Add("motion");
    public void TeleportBy(EntityIota entity, double dx, double dy) => Trace.Add("teleport");
    public void ScatterInventory(EntityIota entity, double distanceTiles) => Trace.Add("scatter");
    public void WriteAkashic(double x, double y, HexPattern key, Iota value) => Trace.Add("akashic_write");
    public void ApplyPotion(EntityIota entity, PotionEffectKind effect, int ticks, int potency) => Trace.Add("potion");
    public void Explode(double x, double y, double strength, bool fire) => Trace.Add("explode");
    public void ConjureBlock(double x, double y, bool light) => Trace.Add("conjure");
    public bool BreakBlockAt(double x, double y) => Record(true, "break");
    public void SetRain(bool rain, int minMinutes, int maxMinutes) => Trace.Add("rain");
    public void IgniteEntity(EntityIota entity) => Trace.Add("ignite_entity");
    public void IgniteAt(double x, double y) => Trace.Add("ignite");
    public void ExtinguishAt(double x, double y, int maxCount) => Trace.Add("extinguish");
    public void CreateWaterAt(double x, double y) => Trace.Add("water");
    public void DestroyWaterAt(double x, double y, int maxCount) => Trace.Add("destroy_water");
    public void SpawnLightning(double x, double y) => Trace.Add("lightning");
    public void ApplyBonemeal(double x, double y) => Trace.Add("bonemeal");
    public void Beep(double x, double y, int instrument, int note) => Trace.Add("beep");
    public void CreateLavaAt(double x, double y) => Trace.Add("lava");
    public bool GrowTreeAt(double x, double y) => Record(false, "grow_tree");
    public bool PlaceBlockAt(double x, double y) => Record(true, "place");
    public void LaunchUp(EntityIota target) => Trace.Add("launch");
    public void GrantFlight(EntityIota target, int ticks, double originX, double originY, double radius, int graceTicks) => Trace.Add("flight");
    public void Brainsweep(double x, double y, EntityIota target, BrainsweepRecipe recipe) => Trace.Add("brainsweep");
}
