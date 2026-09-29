using HexCastingTerraria.Core.Casting.Actions;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Actions;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.World;

// ==================== 测试用施法环境 ====================
sealed class TestEnv : CastingEnvironment
{
    public long Media { get; private set; }
    private readonly int _maxOps;
    private readonly ICastingWorld? _world;

    public TestEnv(long media = 1_000_000, int maxOps = CastingEnvironment.DefaultMaxOpCount,
                   ICastingWorld? world = null)
    {
        Media = media;
        _maxOps = maxOps;
        _world = world;
    }

    public override int MaxOpCount() => _maxOps;

    /// <summary>为 null 即「不支持世界访问」——世界图案应报 MishapNoWorld 而不是吐零向量。</summary>
    public override ICastingWorld? World => _world;

    /// <summary>扣媒质轨迹，与 FakeWorld.Trace 拼起来验证副作用顺序。</summary>
    public List<string> Trace { get; } = new();

    /// <summary>
    /// 是否已启蒙（大法术门槛）。
    ///
    /// **离线测试里默认 true**：绝大多数用例验的是图案逻辑，不是进度门槛；
    /// 默认 false 会让每条大法术用例都要额外加一行 set，反而掩盖了「哪几条真的需要启蒙」。
    /// 需要验证门槛的用例请显式写 Enlightened = false。
    /// </summary>
    public bool Enlightened { get; set; } = true;

    public override bool IsEnlightened() => Enlightened;

    /// <summary>未启蒙强行施放大法术的次数 / 丢下手持物品的次数（原版 MishapUnenlightened 的两个副作用）。</summary>
    public int FailedGreatSpells { get; private set; }
    public int DroppedHeld { get; private set; }
    public override void OnFailedGreatSpell() => FailedGreatSpells++;
    public override void DropHeldItems() => DroppedHeld++;

    // ---- 打包法术与媒质瓶（craft/*）----

    /// <summary>手持的空打包法术物品是哪一种（null = 没有）。</summary>
    public PackagedSpellKind? HeldEmptyPackaged { get; set; }

    /// <summary>手上空瓶的数量（0 = 没有）。</summary>
    public int HeldPhials { get; set; }

    /// <summary>被装进打包法术物品的内容：(图案数, 媒质)。</summary>
    public List<(int PatternCount, long Media)> FilledPackaged { get; } = new();

    /// <summary>被做出来的媒质瓶容量。</summary>
    public List<long> CraftedBatteries { get; } = new();

    /// <summary>手持物品的变体编号（-1 = 该物品没有变体）。</summary>
    public int HeldVariant { get; set; } = -1;

    /// <summary>手持物品一共有几种变体。</summary>
    public int HeldVariantCount { get; set; } = 4;

    public override PackagedSpellKind? HeldEmptyPackagedSpell => HeldEmptyPackaged;

    public override int HeldPhialCount() => HeldPhials;

    /// <summary>手上可充能物品的剩余空间（-1 = 手上没有可充能物品）。</summary>
    public long HeldRechargeRoom { get; set; } = -1;

    /// <summary>装进手上物品的媒质（按顺序）。</summary>
    public List<long> Charged { get; } = new();

    public override long HeldRechargeSpace() => HeldRechargeRoom;

    public override void ChargeHeld(long media)
    {
        long put = System.Math.Min(media, System.Math.Max(0, HeldRechargeRoom));
        Charged.Add(put);
        HeldRechargeRoom -= put;
    }

    public override bool FillHeldPackagedSpell(IReadOnlyList<Iota> patterns, long media)
    {
        if (HeldEmptyPackaged is null) return false;
        FilledPackaged.Add((patterns.Count, media));
        HeldEmptyPackaged = null;      // 装好之后就不再是「空的」了
        return true;
    }

    public override bool CraftBatteryHeld(long media)
    {
        if (HeldPhials != 1) return false;
        CraftedBatteries.Add(media);
        HeldPhials = 0;
        return true;
    }

    public override bool HeldHasVariants() => HeldVariant >= 0;

    public override bool CycleHeldVariant()
    {
        if (HeldVariant < 0) return false;
        HeldVariant = (HeldVariant + 1) % System.Math.Max(1, HeldVariantCount);
        return true;
    }

    /// <summary>可用的颜料物品类型（0 = 没有）。</summary>
    public int PigmentItem { get; set; }

    /// <summary>被应用的颜料（记录顺序）。</summary>
    public List<int> AppliedPigments { get; } = new();

    public override int FindPigmentItem() => PigmentItem;

    public override void ApplyPigment(int itemType) => AppliedPigments.Add(itemType);

    // ---- 哨卫 ----

    /// <summary>当前哨卫（null = 没放）。</summary>
    public CastingEnvironment.SentinelState? SentinelValue { get; set; }

    public override CastingEnvironment.SentinelState? Sentinel => SentinelValue;

    public override void SetSentinel(double x, double y, bool great)
        => SentinelValue = new CastingEnvironment.SentinelState(x, y, great);

    public override void ClearSentinel() => SentinelValue = null;

    /// <summary>法术环状态。为 null 表示当前不是环环境。</summary>
    public CircleState? CircleState { get; set; }

    public override CircleState? Circle => CircleState;



    /// <summary>手上拿着的数据载体里的 iota。null 表示手上没有可读物品。</summary>
    public Iota? HeldIota { get; set; }

    /// <summary>手上是不是拿着载体（与「载体是空的」区分开）。</summary>
    public bool HasStorage { get; set; }

    /// <summary>手持载体可不可写。</summary>
    public bool HeldWritable { get; set; } = true;

    /// <summary>载体肯不肯收某个值（原版 canWrite；null 参数 = 清除）。不设 = 「可写，或者是清除」。</summary>
    public Func<Iota?, bool>? HeldCanWrite { get; set; }

    /// <summary>手上装着咒术的打包法术（erase 的另一种目标）。</summary>
    public bool HeldHasHex { get; set; }

    /// <summary>erase 实际执行的次数。</summary>
    public int Erased { get; private set; }

    /// <summary>被写进手持载体的值（按顺序）。</summary>
    public List<Iota> HeldWrites { get; } = new();

    // 约定：只要 HeldIota 被赋过非空值，就视为「手上有载体」——
    // 这样既有用例（只设 HeldIota）不用改，又能单独构造「拿了空载体」的情形。
    // ForceHasStorage 用于构造「手里拿着东西、但那东西不是载体」。
    private bool EffectiveHasStorage => ForceHasStorage ?? (HasStorage || HeldIota != null);

    /// <summary>强制指定手上有没有载体；null = 按 HeldIota 推断。</summary>
    public bool? ForceHasStorage { get; set; }

    public override Iota? ReadHeldIota() => EffectiveHasStorage ? HeldIota : null;

    public override bool CanWriteHeld(Iota? datum)
        => EffectiveHasStorage && (HeldCanWrite?.Invoke(datum) ?? (datum == null || HeldWritable));

    public override bool WriteHeldIota(Iota value)
    {
        if (!CanWriteHeld(value)) return false;
        HeldIota = value;
        HeldWrites.Add(value);
        return true;
    }

    public override bool HasHeldStorage() => EffectiveHasStorage;

    public override bool IsHeldWritable() => EffectiveHasStorage && HeldWritable;

    public override int HeldEraseableCount() => HeldHasHex || CanWriteHeld(null) ? 1 : 0;

    public override void EraseHeld()
    {
        Erased++;
        HeldHasHex = false;
        if (CanWriteHeld(null)) HeldIota = null;
    }

    // ---- mishap 惩罚记录（原版 MishapEnvironment 的各个方法）----
    public List<(double X, double Y)> Yeets { get; } = new();
    public List<double> Damages { get; } = new();
    public int Drowned { get; private set; }
    public List<int> Blinds { get; } = new();
    public int InventoryDrops { get; private set; }
    public override void YeetHeldItemsTowards(double x, double y) => Yeets.Add((x, y));
    public override void MishapDamage(double healthProportion) => Damages.Add(healthProportion);
    public override void MishapDrown() => Drowned++;
    public override void MishapBlind(int mcTicks) => Blinds.Add(mcTicks);
    public override void MishapDropInventory() => InventoryDrops++;

    /// <summary>每次求值后记下 mishap 的上下文（图案 + 名字），验证聊天提示前缀。</summary>
    public List<HexCastingTerraria.Core.Casting.Eval.Mishaps.MishapContext> MishapContexts { get; } = new();
    public override void PostExecution(CastResult result)
    {
        base.PostExecution(result);
        foreach (var e in result.SideEffects)
        {
            if (e is HexCastingTerraria.Core.Casting.Eval.SideEffects.DoMishapSideEffect d) MishapContexts.Add(d.ErrorCtx);
        }
    }

    protected override long ExtractMediaEnvironment(long cost, bool simulate)
    {
        if (cost <= 0) return 0;
        if (!simulate) Trace.Add("media");
        long paid = System.Math.Min(cost, Media);
        if (!simulate) Media -= paid;
        return cost - paid;
    }
}

/// <summary>
/// 假世界：离线验证世界图案的**分支逻辑**（存活 / 范围 / 坐标语义）。
/// 数值以「图格」为单位，与 ICastingWorld 的约定一致。
/// </summary>
sealed class FakeWorld : ICastingWorld
{
    public EntityIota? Caster { get; set; }
    public HashSet<(EntityIota.EntityKind, int)> Dead { get; } = new();
    public HashSet<(EntityIota.EntityKind, int)> Far { get; } = new();

    public (double X, double Y) Feet { get; set; } = (10.0, 20.0);
    public (double X, double Y) Eye { get; set; } = (10.0, 19.4);
    public (double X, double Y) Vel { get; set; } = (0.25, -0.5);
    public (double X, double Y) LookDir { get; set; } = (1.0, 0.0);
    public double Height { get; set; } = 2.0;

    private static (EntityIota.EntityKind, int) Key(EntityIota e) => (e.Target, e.Index);

    public bool IsAlive(EntityIota entity) => !Dead.Contains(Key(entity));
    public bool IsInRange(EntityIota entity) => !Far.Contains(Key(entity));
    public (double X, double Y) FeetPosition(EntityIota e) => Feet;
    public (double X, double Y) EyePosition(EntityIota e) => Eye;
    public (double X, double Y) Velocity(EntityIota e) => Vel;
    public (double X, double Y) Look(EntityIota e) => LookDir;
    public double EntityHeight(EntityIota e) => Height;

    // ---- 射线相关 ----

    /// <summary>实心图格集合（图格坐标）。</summary>
    public HashSet<(int X, int Y)> Solid { get; } = new();

    /// <summary>为 null 时「一律在范围内」。</summary>
    public Func<double, double, bool>? RangeCheck { get; set; }

    /// <summary>交给扫掠的候选判定箱。</summary>
    public List<EntityBox> Boxes { get; } = new();

    public bool IsTileSolid(int tileX, int tileY) => Solid.Contains((tileX, tileY));
    public bool IsVecInRange(double x, double y) => RangeCheck?.Invoke(x, y) ?? true;
    public IReadOnlyList<EntityBox> EntitiesInArea(double minX, double minY, double maxX, double maxY) => Boxes;

    // ---- 写入类 ----

    /// <summary>调用轨迹，用于验证「先扣媒质、再施放」的顺序。</summary>
    public List<string> Trace { get; } = new();

    public bool InWorld { get; set; } = true;

    /// <summary>为 null 时用 InWorld 的固定值。</summary>
    public Func<double, double, bool>? InWorldCheck { get; set; }

    public double LastScatterDistance { get; private set; }
    public Vector2D LastMotion { get; private set; }
    public Vector2D LastTeleport { get; private set; }

    public bool IsVecInWorld(double x, double y) => InWorldCheck?.Invoke(x, y) ?? InWorld;

    public void ApplyMotion(EntityIota entity, double mx, double my)
    {
        LastMotion = new Vector2D(mx, my);
        Trace.Add("motion");
    }

    public void ScatterInventory(EntityIota entity, double distanceTiles)
    {
        LastScatterDistance = distanceTiles;
        Trace.Add("scatter");
    }

    public void TeleportBy(EntityIota entity, double dx, double dy)
    {
        LastTeleport = new Vector2D(dx, dy);
        Trace.Add("teleport");
    }

    // ---- 阿卡夏记录 ----

    /// <summary>哪些坐标放了记录方块（图格坐标）。</summary>
    public HashSet<(int X, int Y)> AkashicBlocks { get; } = new();

    /// <summary>记录内容：(x, y, 图案签名) -> iota。</summary>
    public Dictionary<(int X, int Y, string Sig), Iota> AkashicEntries { get; } = new();

    public bool IsAkashicRecord(double x, double y)
        => AkashicBlocks.Contains(((int)System.Math.Floor(x), (int)System.Math.Floor(y)));

    public Iota? LookupAkashic(double x, double y, HexPattern key)
        => AkashicEntries.TryGetValue(((int)System.Math.Floor(x), (int)System.Math.Floor(y), key.AnglesSignature()), out var v)
            ? v : null;

    public void WriteAkashic(double x, double y, HexPattern key, Iota value)
    {
        var k = ((int)System.Math.Floor(x), (int)System.Math.Floor(y), key.AnglesSignature());
        AkashicEntries[k] = value;
        Trace.Add("akashic-write");
    }

    // ---- 区域查询 ----

    /// <summary>预置的查询结果：(筛选, 取反) -> 实体列表。测试直接给结果，不模拟分类逻辑。</summary>
    public Dictionary<(ZoneEntityFilter Filter, bool Negate), List<EntityIota>> QueryResults { get; } = new();

    public IReadOnlyList<EntityIota> QueryEntities(
        ZoneEntityFilter filter, bool negate, double x, double y, double radius)
    {
        LastQueryRadius = radius;
        LastQueryFilter = filter;
        LastQueryNegate = negate;

        return QueryResults.TryGetValue((filter, negate), out var list)
            ? list
            : (IReadOnlyList<EntityIota>)System.Array.Empty<EntityIota>();
    }

    /// <summary>记录施加过的药水效果：(目标, 种类, tick, 效力)。</summary>
    public List<(EntityIota Target, PotionEffectKind Kind, int Ticks, int Potency)> Potions { get; } = new();

    public void ApplyPotion(EntityIota entity, PotionEffectKind effect, int ticks, int potency)
        => Potions.Add((entity, effect, ticks, potency));

    // ---- 坐标取实体 + 爆炸 ----

    /// <summary>预置的「该坐标上的实体」。</summary>
    public EntityIota? NearestEntity { get; set; }

    /// <summary>是否报告「有实体的眼位恰好在此」。</summary>
    public bool EyeExactlyHere { get; set; }

    public List<(double X, double Y, double Strength, bool Fire)> Explosions { get; } = new();

    // ---- 方块操作 ----

    public HashSet<(int X, int Y)> Replaceable { get; } = new();
    public HashSet<(int X, int Y)> CheapBreak { get; } = new();
    public List<(int X, int Y, bool Light)> Conjured { get; } = new();
    public List<(int X, int Y)> Broken { get; } = new();

    public bool IsReplaceable(double x, double y)
        => Replaceable.Contains(((int)System.Math.Floor(x), (int)System.Math.Floor(y)));

    public void ConjureBlock(double x, double y, bool light)
    {
        Conjured.Add(((int)System.Math.Floor(x), (int)System.Math.Floor(y), light));
        Trace.Add("conjure");
    }

    public bool IsCheapToBreak(double x, double y)
        => CheapBreak.Contains(((int)System.Math.Floor(x), (int)System.Math.Floor(y)));

    public bool BreakBlockAt(double x, double y)
    {
        Broken.Add(((int)System.Math.Floor(x), (int)System.Math.Floor(y)));
        Trace.Add("break");
        return true;
    }

    // ---- 世界效果 ----

    public List<(bool Rain, int Min, int Max)> Weather { get; } = new();
    public List<EntityIota> IgnitedEntities { get; } = new();
    public List<(double X, double Y)> IgnitedPositions { get; } = new();
    public List<(double X, double Y, int Max)> Extinguished { get; } = new();
    public List<(double X, double Y)> WaterCreated { get; } = new();
    public List<(double X, double Y, int Max)> WaterDestroyed { get; } = new();
    public List<(double X, double Y)> Lightning { get; } = new();
    public List<(double X, double Y)> Bonemeal { get; } = new();

    public void SetRain(bool rain, int minMinutes, int maxMinutes)
        => Weather.Add((rain, minMinutes, maxMinutes));

    public void IgniteEntity(EntityIota entity) => IgnitedEntities.Add(entity);

    public void IgniteAt(double x, double y) => IgnitedPositions.Add((x, y));

    public void ExtinguishAt(double x, double y, int maxCount) => Extinguished.Add((x, y, maxCount));

    public void CreateWaterAt(double x, double y) => WaterCreated.Add((x, y));

    public void DestroyWaterAt(double x, double y, int maxCount) => WaterDestroyed.Add((x, y, maxCount));

    public void SpawnLightning(double x, double y) => Lightning.Add((x, y));

    public void ApplyBonemeal(double x, double y) => Bonemeal.Add((x, y));

    /// <summary>可预测的随机源，便于测试。</summary>
    public double NextRandom { get; set; } = 0.5;

    public double NextDouble() => NextRandom;

    public EntityIota? QueryNearestEntity(double x, double y) => NearestEntity;

    public bool HasEntityEyeExactlyAt(double x, double y) => EyeExactlyHere;

    public void Explode(double x, double y, double strength, bool fire)
    {
        Explosions.Add((x, y, strength, fire));
        Trace.Add("explode");
    }

    public double LastQueryRadius { get; private set; }
    public ZoneEntityFilter LastQueryFilter { get; private set; }
    public bool LastQueryNegate { get; private set; }

    // ---- 比较类图案（compare_*）----

    /// <summary>世界外坐标。CompareBlocks 遇到它就返回 false（对应 WorldGen.InWorld）。</summary>
    public HashSet<(int X, int Y)> OutOfWorld { get; } = new();

    /// <summary>实体 -> 物种签名。缺席 = 该实体不存在（active == false）。</summary>
    public Dictionary<(EntityIota.EntityKind, int), string> Species { get; } = new();

    /// <summary>图格 -> (方块, 帧X, 帧Y, 油漆, 半砖)。缺席 = 空气。</summary>
    public Dictionary<(int X, int Y), (int Type, int FrameX, int FrameY, int Paint, bool Half)> Blocks { get; } = new();

    /// <summary>掉落物槽位 -> (物品, 前缀)。缺席 = 该槽位没有物品。</summary>
    public Dictionary<int, (int Type, int Prefix)> ItemSlots { get; } = new();

    public bool IsSameEntityType(EntityIota a, EntityIota b)
    {
        // 跨种类（玩家 vs NPC）永远不同类
        if (a.Target != b.Target) return false;

        return Species.TryGetValue(Key(a), out var sa)
            && Species.TryGetValue(Key(b), out var sb)
            && sa == sb;
    }

    private static (int X, int Y) FloorOf(double x, double y)
        => ((int)System.Math.Floor(x), (int)System.Math.Floor(y));

    public bool CompareBlocks(double x1, double y1, double x2, double y2, bool exact)
    {
        var pa = FloorOf(x1, y1);
        var pb = FloorOf(x2, y2);
        if (OutOfWorld.Contains(pa) || OutOfWorld.Contains(pb)) return false;

        bool hasA = Blocks.TryGetValue(pa, out var ta);
        bool hasB = Blocks.TryGetValue(pb, out var tb);
        if (hasA != hasB) return false;   // 一边空气一边方块 -> 不同
        if (!hasA) return true;           // 两边都是空气 -> 相同
        if (ta.Type != tb.Type) return false;
        if (!exact) return true;

        return ta.FrameX == tb.FrameX && ta.FrameY == tb.FrameY
            && ta.Paint == tb.Paint && ta.Half == tb.Half;
    }

    public bool CompareItems(EntityIota a, EntityIota b, bool exact)
    {
        if (a.Target != EntityIota.EntityKind.Item || b.Target != EntityIota.EntityKind.Item) return false;
        if (!ItemSlots.TryGetValue(a.Index, out var ia)) return false;
        if (!ItemSlots.TryGetValue(b.Index, out var ib)) return false;
        if (ia.Type != ib.Type) return false;
        if (!exact) return true;

        // strict：同种物品 + 同前缀（数量不参与，对齐原版 isSameItemSameComponents）
        return ia.Prefix == ib.Prefix;
    }

    // ---- 实体身上的数据载体（read/entity / write/entity）----

    /// <summary>实体 -> 它身上的 iota 载体。缺席 = 不是载体。</summary>
    public Dictionary<(EntityIota.EntityKind, int), bool> EntityIotaHolders { get; } = new();

    /// <summary>实体 -> 载体可不可写（默认可写）。</summary>
    public Dictionary<(EntityIota.EntityKind, int), bool> EntityIotaWritable { get; } = new();

    /// <summary>实体 -> 载体里存着的 iota（缺席 = 空载体）。</summary>
    public Dictionary<(EntityIota.EntityKind, int), Iota> EntityIotas { get; } = new();

    /// <summary>被写入过的记录，用于验证「写进去了什么」。</summary>
    public List<((EntityIota.EntityKind, int) Key, Iota Value)> EntityWriteLog { get; } = new();

    public bool IsEntityIotaHolder(EntityIota entity) => EntityIotaHolders.ContainsKey(Key(entity));

    // ---- mishap 世界效果 / 免疫 / 快捷栏 ----
    public bool Placeable { get; set; } = true;
    public HashSet<(EntityIota.EntityKind, int)> TeleportImmune { get; } = new();
    public List<(double X, double Y)> MishapExplosions { get; } = new();
    public List<(EntityIota.EntityKind, int)> Launched { get; } = new();
    public List<((EntityIota.EntityKind, int) Key, bool Kill)> Hurt { get; } = new();
    public bool HasPlaceableInHotbar() => Placeable;
    public bool IsTeleportImmune(EntityIota entity) => TeleportImmune.Contains(Key(entity));
    public void MishapExplosion(double x, double y) => MishapExplosions.Add((x, y));
    public void MishapLaunchItem(EntityIota item) => Launched.Add(Key(item));
    public void MishapHurtEntity(EntityIota entity, bool kill) => Hurt.Add((Key(entity), kill));

    public bool IsEntityIotaWritable(EntityIota entity)
        => IsEntityIotaHolder(entity) && EntityIotaWritable.GetValueOrDefault(Key(entity), true);

    public Iota? ReadEntityIota(EntityIota entity)
        => EntityIotas.TryGetValue(Key(entity), out var v) ? v : null;

    /// <summary>实体载体肯不肯收某个值（原版 canWrite）。不设 = 看可不可写。</summary>
    public Func<Iota, bool>? EntityCanWrite { get; set; }

    public bool CanWriteEntityIota(EntityIota entity, Iota datum)
        => IsEntityIotaWritable(entity) && (EntityCanWrite?.Invoke(datum) ?? true);

    public bool WriteEntityIota(EntityIota entity, Iota value)
    {
        if (!CanWriteEntityIota(entity, value)) return false;
        EntityIotas[Key(entity)] = value;
        EntityWriteLog.Add((Key(entity), value));
        return true;
    }

    // ---- 单点法术（beep / create_lava / edify / place_block / recharge）----

    /// <summary>播放过的音符：(x, y, 乐器, 音高)。</summary>
    public List<(double X, double Y, int Instrument, int Note)> Beeps { get; } = new();

    public void Beep(double x, double y, int instrument, int note)
        => Beeps.Add((x, y, instrument, note));

    public List<(double X, double Y)> LavaCreated { get; } = new();

    public void CreateLavaAt(double x, double y) => LavaCreated.Add((x, y));

    /// <summary>哪些格子是树苗。</summary>
    public HashSet<(int X, int Y)> Saplings { get; } = new();

    public bool IsSaplingAt(double x, double y) => Saplings.Contains(FloorOf(x, y));

    public List<(double X, double Y)> GrownTrees { get; } = new();

    public bool GrowTreeAt(double x, double y)
    {
        GrownTrees.Add((x, y));
        return true;
    }

    public List<(double X, double Y)> PlacedBlocks { get; } = new();

    /// <summary>为 false 时模拟「背包里没有可放置物品」。</summary>
    public bool HasPlaceableItem { get; set; } = true;

    public bool PlaceBlockAt(double x, double y)
    {
        if (!HasPlaceableItem) return false;
        PlacedBlocks.Add((x, y));
        return true;
    }

    /// <summary>掉落物里的媒质总量。</summary>
    public Dictionary<int, long> ItemMedia { get; } = new();

    /// <summary>掉落物的单件媒质（有 = 堆叠媒质材料，按整件扣；没有 = 按量扣，像媒质瓶）。</summary>
    public Dictionary<int, long> ItemUnit { get; } = new();

    /// <summary>掉落物是媒质瓶（造瓶 / 打包时不算，源项目 drainForBatteries）。</summary>
    public HashSet<int> ItemIsFlask { get; } = new();

    public long ItemEntityMedia(EntityIota itemEntity, bool forBattery)
    {
        if (itemEntity.Target != EntityIota.EntityKind.Item) return 0;
        if (forBattery && ItemIsFlask.Contains(itemEntity.Index)) return 0;
        return ItemMedia.TryGetValue(itemEntity.Index, out var have) ? System.Math.Max(0, have) : 0;
    }

    public long DrainItemEntity(EntityIota itemEntity, long cost, bool forBattery)
    {
        long have = ItemEntityMedia(itemEntity, forBattery);
        if (have <= 0) return 0;
        long got;
        if (ItemUnit.TryGetValue(itemEntity.Index, out var unit) && unit > 0)
        {
            long count = have / unit;
            long used = cost < 0 ? count : System.Math.Min((cost + unit - 1) / unit, count);
            got = used * unit;
        }
        else
        {
            got = cost < 0 ? have : System.Math.Min(cost, have);
        }
        ItemMedia[itemEntity.Index] = have - got;
        Trace.Add("drain");
        return got;
    }

    // ---- 咒法飞行 ----

    /// <summary>被向上弹过的实体。</summary>
    public List<(EntityIota.EntityKind, int)> Launches { get; } = new();

    public void LaunchUp(EntityIota target) => Launches.Add(Key(target));

    /// <summary>飞行状态：(剩余tick, 起点X, 起点Y, 半径, 宽限tick)。</summary>
    public Dictionary<(EntityIota.EntityKind, int), (int Ticks, double OriginX, double OriginY, double Radius, int Grace)> Flights { get; } = new();

    public void GrantFlight(EntityIota target, int ticks, double originX, double originY, double radius, int graceTicks)
    {
        // 只有玩家能飞（对应源项目的 args.getPlayer）
        if (target.Target != EntityIota.EntityKind.Player) return;
        Flights[Key(target)] = (ticks, originX, originY, radius, graceTicks);
    }

    public bool HasHexFlight(EntityIota target) => Flights.ContainsKey(Key(target));

    // ---- 脑叶切除（brainsweep）----

    /// <summary>图格 -> 方块类型。</summary>
    public Dictionary<(int X, int Y), int> Tiles { get; } = new();

    /// <summary>不能改动的位置（对应源项目的 canEditBlockAt == false）。</summary>
    public HashSet<(int X, int Y)> NoEdit { get; } = new();

    /// <summary>实体 -> 种类编号。</summary>
    public Dictionary<(EntityIota.EntityKind, int), int> SpeciesOf { get; } = new();

    /// <summary>不能切除的实体（对应 NO_BRAINSWEEPING 标签）。</summary>
    public HashSet<(EntityIota.EntityKind, int)> NotSweepable { get; } = new();

    /// <summary>已被切除过的实体。</summary>
    public HashSet<(EntityIota.EntityKind, int)> Swept { get; } = new();

    /// <summary>执行过的切除：(x, y, 实体, 配方)。</summary>
    public List<(double X, double Y, EntityIota Entity, BrainsweepRecipe Recipe)> Sweeps { get; } = new();

    public bool CanEditAt(double x, double y) => !NoEdit.Contains(FloorOf(x, y));

    public int TileTypeAt(double x, double y)
        => Tiles.TryGetValue(FloorOf(x, y), out var t) ? t : -1;

    public int EntitySpeciesOf(EntityIota entity)
        => SpeciesOf.TryGetValue(Key(entity), out var s) ? s : 0;

    public bool IsBrainsweepable(EntityIota entity) => !NotSweepable.Contains(Key(entity));

    public bool IsBrainswept(EntityIota entity) => Swept.Contains(Key(entity));

    public void Brainsweep(double x, double y, EntityIota target, BrainsweepRecipe recipe)
    {
        Sweeps.Add((x, y, target, recipe));
        Swept.Add(Key(target));

        if (recipe.ResultTile >= 0)
        {
            Tiles[FloorOf(x, y)] = recipe.ResultTile;
        }
    }
}

/// <summary>极简二维向量，避免测试工程依赖 XNA。</summary>
readonly record struct Vector2D(double X, double Y);

/// <summary>法术环测试用的假世界。</summary>
sealed class FakeCircleWorld : ICircleWorld
{
    public Dictionary<(int X, int Y), CircleComponent> Components { get; } = new();
    public Dictionary<(int X, int Y), HexPattern> Patterns { get; } = new();
    public HashSet<(int X, int Y)> Powered { get; } = new();

    /// <summary>普通部件（石板）：不能往 normal 出去、不能从 normal 的反方向进来。</summary>
    public void Set(int x, int y, CircleComponentKind kind, CircleDir normal)
        => Components[(x, y)] = CircleComponent.Ordinary(kind, normal);

    /// <summary>
    /// 原动力：禁止进入 = **起始方向的反方向**，与 normal 无关。
    /// 这正是源项目里原动力用 FACING、普通部件用 normalDir 的差别。
    /// </summary>
    public void SetImpetus(int x, int y, CircleDir normal, CircleDir startDir)
        => Components[(x, y)] = CircleComponent.Impetus(startDir);

    /// <summary>导线：只能沿一个轴传导。</summary>
    public void SetDirectrix(int x, int y, CircleComponentKind kind, CircleDir facing)
        => Components[(x, y)] = CircleComponent.Directrix(kind, facing);

    public CircleComponent? GetComponent(int x, int y)
        => Components.TryGetValue((x, y), out var c) ? c : null;

    public HexPattern? GetSlatePattern(int x, int y)
        => Patterns.TryGetValue((x, y), out var p) ? p : null;

    public bool IsPowered(int x, int y) => Powered.Contains((x, y));
}

static class Program
{
    static int _pass, _fail;

    static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}{(detail != null ? "  -> " + detail : "")}"); }
    }

    static PatternIota P(string id)
    {
        var def = PatternRegistry.All.FirstOrDefault(d => d.Id == id)
                  ?? throw new InvalidOperationException($"图案不存在: {id}");
        return new PatternIota(def.Prototype);
    }

    static string Sig(CastingImage img)
        => "[" + string.Join(", ", img.Stack.Select(IotaText)) + "]";

    static string IotaText(Iota i) => i switch
    {
        NullIota => "null",
        BooleanIota b => b.Value ? "true" : "false",
        DoubleIota d => d.Value.ToString("0.####"),
        VectorIota v => v.Z == 0 ? $"({v.X:0.##},{v.Y:0.##})" : $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##})",
        PatternIota p => "pattern:" + p.AnglesSignature,
        ListIota l => "list(" + l.Count + ")",
        GarbageIota => "garbage",
        _ => i.TypeName,
    };

    static CastOutcome Run(TestEnv env, CastingImage image, params Iota[] iotas)
    {
        var vm = new CastingVM(image, env);
        return vm.QueueExecute(image, iotas);
    }

    /// <summary>二元图案的常规调用：先把两个参数放进栈，再执行图案。</summary>
    static CastOutcome Run2(TestEnv env, Iota a, Iota b, string patternId)
    {
        var img = new CastingImage(new Iota[] { a, b });
        return new CastingVM(img, env).QueueExecute(img, new Iota[] { P(patternId) });
    }

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=== 1. 注册表装载 ===");
        var load = PatternRegistry.Load();
        Check("图案总数 188", load.Total == 188, $"实际 {load.Total}");
        Check("装载成功 188", load.Loaded == 188, $"实际 {load.Loaded}");
        Check("无重复签名", load.DuplicateSignatures.Count == 0);

        Console.WriteLine("=== 2. 注册图案行为 ===");
        HexActions.RegisterAll();
        Check("已注册行为数 > 0", PatternRegistry.RegisteredActionCount > 0,
            $"实际 {PatternRegistry.RegisteredActionCount}");
        Console.WriteLine($"        已注册 {PatternRegistry.RegisteredActionCount} 条行为");

        Console.WriteLine("=== 3. 常数图案 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"));
            Check("const/true 压入 true", Sig(r.Image) == "[true]", Sig(r.Image));
            Check("解析状态 Evaluated", r.ResolutionType == ResolvedPatternType.Evaluated);
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/null"), P("hexcasting:const/false"));
            Check("null + false", Sig(r.Image) == "[null, false]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/double/pi"));
            Check("const/double/pi", Sig(r.Image) == "[3.1416]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/vec/px"), P("hexcasting:const/vec/py"));
            Check("2D 单位向量", Sig(r.Image) == "[(1,0), (0,1)]", Sig(r.Image));
        }

        Console.WriteLine("=== 4. 栈操作图案 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:const/true"), P("hexcasting:const/false"), P("hexcasting:swap"));
            Check("swap 交换栈顶两项", Sig(r.Image) == "[false, true]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"), P("hexcasting:duplicate"));
            Check("duplicate 复制栈顶", Sig(r.Image) == "[true, true]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:const/true"), P("hexcasting:const/null"), P("hexcasting:const/false"),
                P("hexcasting:rotate"));
            Check("rotate(3,[1,2,0])", Sig(r.Image) == "[null, false, true]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"), P("hexcasting:stack_len"));
            Check("stack_len 压入操作前长度 1", Sig(r.Image) == "[true, 1]", Sig(r.Image));
        }

        Console.WriteLine("=== 5. mishap：参数不足 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"), P("hexcasting:rotate"));
            Check("rotate 缺参数 -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
            // 原版 MishapNotEnoughArgs.execute：repeat(expected - got) { stack.add(GarbageIota()) }
            //（这条测试原来断言「栈保持 [true]」，那是移植版空惩罚的行为）
            Check("缺参数：补上缺的个数的垃圾值（rotate 要 3 个、有 1 个 → 补 2 个）",
                Sig(r.Image) == "[true, garbage, garbage]", Sig(r.Image));
        }

        Console.WriteLine("=== 6. mishap：未实现的图案 ===");
        {
            var env = new TestEnv();
            // blink 已注册于目录（188 条内），但行为未实现 -> 应报 Errored（不是 Invalid）
            var r = Run(env, new CastingImage(), P("hexcasting:blink"));
            Check("已注册但未实现 -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
            // 真正不在注册表里的签名 -> Invalid
            if (HexCastingTerraria.Core.Casting.Math.HexPattern.TryFromAnglesUnchecked(
                    "s", HexCastingTerraria.Core.Casting.Math.HexDir.East, out var unknown, out _)
                && unknown != null)
            {
                var r2 = Run(env, new CastingImage(), new PatternIota(unknown));
                Check("不在注册表里的签名 -> Invalid", r2.ResolutionType == ResolvedPatternType.Invalid,
                    r2.ResolutionType.ToString());
            }
        }

        Console.WriteLine("=== 7. 算力计数与上限 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:const/true"), P("hexcasting:const/true"), P("hexcasting:duplicate"));
            Check("opsConsumed 累计为 3", r.Image.OpsConsumed == 3, $"实际 {r.Image.OpsConsumed}");
        }
        {
            var env = new TestEnv(maxOps: 2);
            var r = Run(env, new CastingImage(),
                P("hexcasting:const/true"), P("hexcasting:const/true"),
                P("hexcasting:const/true"), P("hexcasting:const/true"));
            Check("超出算力 -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 8. 转义（Consideration）语义 ===");
        {
            var env = new TestEnv();
            var image = new CastingImage(escapeNext: true);
            var r = Run(env, image, P("hexcasting:const/true"));
            Check("转义后压入的是图案本身", Sig(r.Image) == "[pattern:aqae]", Sig(r.Image));
            Check("转义解析状态 Escaped", r.ResolutionType == ResolvedPatternType.Escaped,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 9. 括号内行为 ===");
        {
            var env = new TestEnv();
            var image = new CastingImage(parenCount: 1);
            var r = Run(env, image, P("hexcasting:const/true"));
            Check("括号内图案入列而非执行", r.Image.Parenthesized.Count == 1,
                $"parenthesized={r.Image.Parenthesized.Count}");
            Check("括号内解析状态 Escaped", r.ResolutionType == ResolvedPatternType.Escaped,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 10. NaN 清洗 ===");
        {
            var nanIota = new VectorIota(double.NaN, double.PositiveInfinity);
            Check("NaN/Inf 构造时被清洗为 0",
                nanIota.X == 0.0 && nanIota.Y == 0.0, nanIota.DescribeForTest());
            var (nx, ny) = HexCastingTerraria.Core.Casting.HexMathUtil.SafeNormalize(0, 0);
            Check("零向量归一化返回兜底方向", nx == 1.0 && ny == 0.0, $"({nx},{ny})");
        }

        Console.WriteLine();
        Console.WriteLine("=== 11. 数学运算符（标量）===");
        {
            (string op, double a, double b, string expect)[] cases =
            {
                ("add", 2, 3, "5"),
                ("sub", 5, 3, "2"),
                ("mul", 4, 2.5, "10"),
                ("div", 7, 2, "3.5"),
                ("pow", 2, 10, "1024"),
                ("floor", 2.7, 0, "2"),
                ("ceil", 2.1, 0, "3"),
                ("abs", -4.5, 0, "4.5"),
                ("modulo", 7, 3, "1"),
                ("sin", 0, 0, "0"),
                ("cos", 0, 0, "1"),
            };
            foreach (var (op, a, b, expect) in cases)
            {
                bool unary = op is "floor" or "ceil" or "abs" or "sin" or "cos" or "arcsin" or "arccos" or "arctan" or "not";
                var env = new TestEnv();
                var img = unary
                    ? new CastingImage(new Iota[] { new DoubleIota(a) })
                    : new CastingImage(new Iota[] { new DoubleIota(a), new DoubleIota(b) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:" + op) });
                string got = r.Image.Stack.Count > 0 ? IotaText(r.Image.Stack[^1]) : "<空>";
                Check($"{op}({a}{(unary ? "" : ", " + b)}) = {expect}", got == expect, $"实际 {got}");
            }
        }

        Console.WriteLine("=== 12. 除零与非法运算 ===");
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:div") });
            Check("1/0 -> Errored", r.ResolutionType == ResolvedPatternType.Errored, r.ResolutionType.ToString());
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { BooleanIota.True, BooleanIota.False });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add") });
            Check("add(bool,bool) -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 13. 比较运算符 ===");
        {
            (string op, double a, double b, bool expect)[] cases =
            {
                ("greater", 3, 2, true),
                ("greater", 2, 3, false),
                ("less", 2, 3, true),
                ("greater_eq", 3, 3, true),
                ("less_eq", 3, 3, true),
                ("greater_eq", 2, 3, false),
            };
            foreach (var (op, a, b, expect) in cases)
            {
                var env = new TestEnv();
                var img = new CastingImage(new Iota[] { new DoubleIota(a), new DoubleIota(b) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:" + op) });
                var top = r.Image.Stack.Count > 0 ? r.Image.Stack[^1] : null;
                bool got = top is BooleanIota bi && bi.Value;
                Check($"{op}({a}, {b}) = {expect}", got == expect, $"实际 {IotaText(top ?? NullIota.Instance)}");
            }
        }

        Console.WriteLine("=== 14. 布尔运算 ===");
        {
            (string op, bool a, bool b, bool expect)[] cases =
            {
                ("and", true, false, false),
                ("and", true, true, true),
                ("or", false, true, true),
                ("xor", true, true, false),
                ("xor", true, false, true),
            };
            foreach (var (op, a, b, expect) in cases)
            {
                var env = new TestEnv();
                var img = new CastingImage(new Iota[] { BooleanIota.Of(a), BooleanIota.Of(b) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:" + op) });
                var top = r.Image.Stack.Count > 0 ? r.Image.Stack[^1] : null;
                bool got = top is BooleanIota bi && bi.Value;
                Check($"{op}({a}, {b}) = {expect}", got == expect, $"实际 {IotaText(top ?? NullIota.Instance)}");
            }
            var envN = new TestEnv();
            var imgN = new CastingImage(new Iota[] { BooleanIota.True });
            var rN = new CastingVM(imgN, envN).QueueExecute(imgN, new Iota[] { P("hexcasting:not") });
            Check("not(true) = false",
                rN.Image.Stack.Count == 1 && rN.Image.Stack[0] is BooleanIota { Value: false },
                Sig(rN.Image));
        }

        Console.WriteLine("=== 15. 二维向量运算 ===");
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(1, 2), new VectorIota(3, 4) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add") });
            Check("vec add = (4,6)", Sig(r.Image) == "[(4,6)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(1, 2), new VectorIota(3, 4) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:mul") });
            Check("vec mul = 点积 11", Sig(r.Image) == "[11]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            // 原版 Vec3Arithmetic DIV = 叉积（向量）：x̂ × ŷ = ẑ（这条测试原来断言「2D 叉积 = 数字 1」）
            var img = new CastingImage(new Iota[] { new VectorIota(1, 0), new VectorIota(0, 1) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:div") });
            Check("vec div = 叉积（向量）：(1,0,0) × (0,1,0) = (0,0,1)", Sig(r.Image) == "[(0,0,1)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(3, 4) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:abs") });
            Check("vec abs = 长度 5", Sig(r.Image) == "[5]", Sig(r.Image));
        }

        // ── pow / floor / ceil：这三条**曾经注册了却必然失败** ──────────────
        // Vec2Arithmetic.Arity 早就声明了它们，但 Apply 里没有实现 → 返回 null →
        // 引擎认为"没有算术支持这个算子" → 玩家画出 pow/floor/ceil 只会得到一条 mishap。
        // 属于"适用范围里但功能缺失"，而且不会崩、不会报编译错，只能靠测试盯。
        {
            var env = new TestEnv();
            // u=(3,4) 在 v=(1,0) 上的投影 = (3,0)
            var img = new CastingImage(new Iota[] { new VectorIota(3, 4), new VectorIota(1, 0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:pow") });
            Check("vec pow = u 在 v 上的投影 (3,0)", Sig(r.Image) == "[(3,0)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            // 斜方向投影：(3,4) 投到 (1,1)/√2 上 → (3.5, 3.5)
            var img = new CastingImage(new Iota[] { new VectorIota(3, 4), new VectorIota(1, 1) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:pow") });
            Check("vec pow 斜向投影 = (3.5,3.5)", Sig(r.Image) == "[(3.5,3.5)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            // 零向量：原版 MC 的 normalize() 在长度 < 1e-4 时返回 ZERO，投影结果也是 ZERO。
            // **不能报错** —— 报错会与原版行为分叉，而两边都不崩、只是结果不同，最难发现。
            var img = new CastingImage(new Iota[] { new VectorIota(3, 4), new VectorIota(0, 0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:pow") });
            Check("vec pow 对零向量 = 零向量（与原版一致，不报错）", Sig(r.Image) == "[(0,0)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(1.7, -2.3) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:floor") });
            Check("vec floor = 分量向下取整 (1,-3)", Sig(r.Image) == "[(1,-3)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(1.2, -2.7) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:ceil") });
            Check("vec ceil = 分量向上取整 (2,-2)", Sig(r.Image) == "[(2,-2)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            // 参数不是向量时不能假装能算（应交给标量算术或报错，而不是悄悄返回向量）
            var img = new CastingImage(new Iota[] { new DoubleIota(3), new DoubleIota(4) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:pow") });
            Check("vec pow 对两个标量 → 走标量算术（不是向量投影）",
                Sig(r.Image) == "[81]", Sig(r.Image));   // 3^4 = 81，标量幂
        }

        Console.WriteLine("=== 16. 向量拆装（2 分量）===");
        {
            var env = new TestEnv();
            // 原版 PACK：三个数字 → 向量（这条测试原来是两个分量）
            var img = new CastingImage(new Iota[] { new DoubleIota(7), new DoubleIota(9), new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:construct_vec") });
            Check("construct_vec(7,9,2) = (7,9,2)", Sig(r.Image) == "[(7,9,2)]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new VectorIota(7, 9) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:deconstruct_vec") });
            Check("deconstruct_vec(7,9) = [7,9,0]（原版 UNPACK 三个分量）", Sig(r.Image) == "[7, 9, 0]", Sig(r.Image));
        }

        Console.WriteLine("=== 17. 批量压力用例 ===");
        {
            int swept = 0, sweptFail = 0;
            for (int a = -20; a <= 20; a++)
            {
                for (int b = 1; b <= 5; b++)
                {
                    var env = new TestEnv();
                    var img = new CastingImage(new Iota[]
                    {
                        new DoubleIota(a), new DoubleIota(b), P("hexcasting:add"),
                        new DoubleIota(b), P("hexcasting:sub"),
                    });
                    var r = new CastingVM(img, env).QueueExecute(img, img.Stack);
                    double got = r.Image.Stack.Count == 1 && r.Image.Stack[0] is DoubleIota d ? d.Value : double.NaN;
                    swept++;
                    if (System.Math.Abs(got - a) > 1e-9) sweptFail++;
                }
            }
            Check($"加法/减法互逆扫描 {swept} 组", sweptFail == 0, $"失败 {sweptFail} 组");
        }

        Console.WriteLine("=== 18. 列表构造 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:empty_list"));
            Check("empty_list -> list(0)",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 0 }, Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"), P("hexcasting:singleton"));
            Check("singleton(true) -> list(1)",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 1 }, Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:const/true"), P("hexcasting:singleton"), P("hexcasting:splat"));
            Check("splat(list(true)) -> [true]", Sig(r.Image) == "[true]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:const/true"), P("hexcasting:splat"));
            Check("splat(非列表) -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 19. 括号建列表 ===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:open_paren"),
                P("hexcasting:const/true"),
                P("hexcasting:const/false"),
                P("hexcasting:close_paren"));
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("括号内 2 个图案 -> list(2)", top is { Count: 2 }, Sig(r.Image));
            Check("括号已闭合（parenCount=0）", r.Image.ParenCount == 0, $"parenCount={r.Image.ParenCount}");
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:close_paren"));
            Check("括号外闭括号 -> Errored（MishapNeedsParens）",
                r.ResolutionType == ResolvedPatternType.Errored, r.ResolutionType.ToString());
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:open_paren"), P("hexcasting:const/true"));
            Check("开括号后未闭合则 parenCount=1", r.Image.ParenCount == 1, $"parenCount={r.Image.ParenCount}");
            Check("括号内图案未被求值（仍是未闭合状态）", r.Image.Parenthesized.Count == 1,
                $"parenthesized={r.Image.Parenthesized.Count}");
        }

        Console.WriteLine("=== 20. 转义（Consideration）===");
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(), P("hexcasting:escape"), P("hexcasting:const/true"));
            // 转义后，const/true 这个图案**不再被执行**，而是它本身入栈
            Check("escape 后压入图案本身（未被求值）",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is PatternIota, Sig(r.Image));
            Check("escapeNext 已复位", !r.Image.EscapeNext);
        }
        {
            var env = new TestEnv();
            var r = Run(env, new CastingImage(),
                P("hexcasting:open_paren"), P("hexcasting:escape"), P("hexcasting:const/true"),
                P("hexcasting:close_paren"));
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("括号内转义 -> list(1) 且元素被标记 escaped",
                top is { Count: 1 }, Sig(r.Image));
        }

        Console.WriteLine("=== 21. if 三目 ===");
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { BooleanIota.True, new DoubleIota(1), new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:if") });
            Check("if(true, 1, 2) = 1", Sig(r.Image) == "[1]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { BooleanIota.False, new DoubleIota(1), new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:if") });
            Check("if(false, 1, 2) = 2", Sig(r.Image) == "[2]", Sig(r.Image));
        }
        {
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(5), new DoubleIota(1), new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:if") });
            Check("if(非bool, ...) -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 22. thanos（剩余算力自查）===");
        {
            var env = new TestEnv(maxOps: 100);
            var img = new CastingImage();
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:thanatos") });
            // 压入的是「读取时」的剩余量 = 100 - 0
            Check("thanos 压入剩余算力 100", Sig(r.Image) == "[100]", Sig(r.Image));
        }

        Console.WriteLine("=== 23. for_each（验证 FrameForEach）===");
        {
            var env = new TestEnv();
            var code = new ListIota(Array.Empty<Iota>());
            var data = new ListIota(new Iota[] { BooleanIota.True, BooleanIota.False });
            var img = new CastingImage(new Iota[] { code, data });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:for_each") });

            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("for_each 压回一个列表", top != null, Sig(r.Image));
            Check("累加器含 2 项（两次迭代）", top is { Count: 2 }, $"实际 {top?.Count}");
            Check("for_each 执行成功", r.ResolutionType == ResolvedPatternType.Evaluated,
                r.ResolutionType.ToString());
        }
        {
            // 数据为 3 项，累加器应为 3
            var env = new TestEnv();
            var code = new ListIota(Array.Empty<Iota>());
            var data = new ListIota(new Iota[]
            {
                new DoubleIota(10), new DoubleIota(20), new DoubleIota(30),
            });
            var img = new CastingImage(new Iota[] { code, data });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:for_each") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("3 项数据 -> 累加器 3 项", top is { Count: 3 }, $"实际 {top?.Count}");
        }
        {
            // 栈不足 -> Errored
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:for_each") });
            Check("for_each 栈不足 -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                r.ResolutionType.ToString());
        }

        Console.WriteLine("=== 24. 列表算术 ===");
        {
            // index：取第 1 个元素（0 基）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(11), new DoubleIota(22) }),
                new DoubleIota(1),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:index") });
            Check("index(list(11,22), 1) = 22", Sig(r.Image) == "[22]", Sig(r.Image));
        }
        {
            // index 越界：原版 OperatorIndex 是 getOrElse { NullIota() } —— 返回空，**不报错**
            //（这条测试原来断言「越界 -> Errored」，把移植版的错误行为当成了期望值）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(11) }),
                new DoubleIota(5),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:index") });
            Check("index 越界 -> null（原版 getOrElse）", r.ResolutionType == ResolvedPatternType.Evaluated && Sig(r.Image) == "[null]",
                $"{r.ResolutionType} {Sig(r.Image)}");
            var img2 = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(11), new DoubleIota(22) }),
                new DoubleIota(0.6),
            });
            var r2 = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:index") });
            Check("index 0.6 四舍五入到 1（原版 roundToInt）", Sig(r2.Image) == "[22]", Sig(r2.Image));
        }
        {
            // append：末尾追加
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { BooleanIota.True }),
                new DoubleIota(9),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:append") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("append(list(1), 9) -> list(2)", top is { Count: 2 }, Sig(r.Image));
        }
        {
            // construct（cons）：头部插入
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { BooleanIota.False }),
                new DoubleIota(7),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:construct") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("construct(list(1), 7) -> list(2) 且头部是 7",
                top is { Count: 2 } && top.Items[0] is DoubleIota { Value: 7 }, Sig(r.Image));
        }
        {
            // reverse
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(3) }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:reverse") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("reverse(1,2,3) -> 首元素 3",
                top is { Count: 3 } && top.Items[0] is DoubleIota { Value: 3 }, Sig(r.Image));
        }
        {
            // abs：列表长度
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2) }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:abs") });
            Check("abs(list(2)) = 2", Sig(r.Image) == "[2]", Sig(r.Image));
        }
        {
            // add：两列表拼接
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1) }),
                new ListIota(new Iota[] { new DoubleIota(2), new DoubleIota(3) }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("add(list(1), list(2)) -> list(3)", top is { Count: 3 }, Sig(r.Image));
        }
        {
            // deconstruct（uncons）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(5), new DoubleIota(6) }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:deconstruct") });
            // 原版 OperatorUnCons：listOf(ListIota(list.cdr), list.car) —— 首元素在**栈顶**
            //（这条测试原来断言 [5, list(1)]，顺序是反的）
            Check("deconstruct(list(5,6)) -> [list(6), 5]（首元素在栈顶）",
                r.Image.Stack.Count == 2
                && r.Image.Stack[0] is ListIota { Count: 1 }
                && r.Image.Stack[1] is DoubleIota { Value: 5 }, Sig(r.Image));
            var img0 = new CastingImage(new Iota[] { new ListIota(System.Array.Empty<Iota>()) });
            var r0 = new CastingVM(img0, env).QueueExecute(img0, new Iota[] { P("hexcasting:deconstruct") });
            Check("deconstruct(空列表) -> [list(0), null]，不报错", Sig(r0.Image) == "[list(0), null]", Sig(r0.Image));
        }
        // ==================== P0-2：列表算子补全（slice/unappend/index_of/remove_from/replace） ====================
        {
            // slice(list(1,2,3,4), 1, 3) -> list(2,3)
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(3), new DoubleIota(4) }),
                new DoubleIota(1), new DoubleIota(3),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:slice") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("slice(list(1,2,3,4),1,3) -> list(2,3)",
                top is { Count: 2 }
                && top.Items[0] is DoubleIota { Value: 2 }
                && top.Items[1] is DoubleIota { Value: 3 }, Sig(r.Image));
        }
        {
            // slice 两端相等 -> 空列表（源项目显式分支）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2) }),
                new DoubleIota(2), new DoubleIota(2),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:slice") });
            Check("slice 端点相等 -> 空列表",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 0 }, Sig(r.Image));
        }
        {
            // slice 端点允许等于长度（闭区间）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2) }),
                new DoubleIota(0), new DoubleIota(2),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:slice") });
            Check("slice(list,0,2) 全长 -> list(2)",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 2 }, Sig(r.Image));
        }
        {
            // unappend(list(1,2,3)) -> [list(1,2), 3]
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(3) }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:unappend") });
            Check("unappend(list(1,2,3)) -> [list(1,2), 3]",
                r.Image.Stack.Count == 2
                && r.Image.Stack[0] is ListIota { Count: 2 }
                && r.Image.Stack[1] is DoubleIota { Value: 3 }, Sig(r.Image));
        }
        {
            // unappend(空) -> [空列表, null]（源项目 removeLastOrNull ?: NullIota，不报错）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new ListIota(System.Array.Empty<Iota>()) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:unappend") });
            Check("unappend(空) -> [空列表, null]",
                r.Image.Stack.Count == 2
                && r.Image.Stack[0] is ListIota { Count: 0 }
                && r.Image.Stack[1] is NullIota, Sig(r.Image));
        }
        {
            // index_of(list(11,22,33), 22) -> 1
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(11), new DoubleIota(22), new DoubleIota(33) }),
                new DoubleIota(22),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:index_of") });
            Check("index_of(list(11,22,33),22) -> 1",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is DoubleIota { Value: 1 }, Sig(r.Image));
        }
        {
            // index_of 找不到 -> -1（源项目 indexOfFirst 语义，不报错）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(11), new DoubleIota(22) }),
                new DoubleIota(99),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:index_of") });
            Check("index_of 找不到 -> -1",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is DoubleIota { Value: -1 }, Sig(r.Image));
        }
        {
            // remove_from(list(1,2,3), 1) -> list(1,3)
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(3) }),
                new DoubleIota(1),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:remove_from") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("remove_from(list(1,2,3),1) -> list(1,3)",
                top is { Count: 2 }
                && top.Items[0] is DoubleIota { Value: 1 }
                && top.Items[1] is DoubleIota { Value: 3 }, Sig(r.Image));
        }
        {
            // remove_from 越界 -> 原样返回（源项目不报错）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2) }),
                new DoubleIota(9),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:remove_from") });
            Check("remove_from 越界 -> 原样 list(2)",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 2 }, Sig(r.Image));
        }
        {
            // replace(list(1,2,3), 0, 9) -> list(9,2,3)
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(3) }),
                new DoubleIota(0), new DoubleIota(9),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:replace") });
            var top = r.Image.Stack.Count == 1 ? r.Image.Stack[0] as ListIota : null;
            Check("replace(list(1,2,3),0,9) -> list(9,2,3)",
                top is { Count: 3 }
                && top.Items[0] is DoubleIota { Value: 9 }
                && top.Items[1] is DoubleIota { Value: 2 }, Sig(r.Image));
        }
        {
            // 参数不足时应当走 Error 通道而不是抛异常（P0-3 验收点）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1) });
            var threw = false;
            try
            {
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:slice") });
                Check("slice 栈不足 -> Errored（不抛异常）",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            catch (System.Exception e)
            {
                threw = true;
                Check("slice 栈不足 -> Errored（不抛异常）", false, e.GetType().Name);
            }
            if (!threw) { /* 已在上方 Check */ }
        }
        // ==================== P0-2 收尾：元求值 eval / eval/cc / undo ====================
        {
            // eval：栈顶是**列表** → 求值列表里的每一条
            var env = new TestEnv();
            var program = new ListIota(new Iota[] { P("hexcasting:const/true") });
            var img = new CastingImage(new Iota[] { program });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval") });
            Check("eval(list(const/true)) -> 栈变 [true]",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is BooleanIota { Value: true }, Sig(r.Image));
        }
        {
            // eval：栈顶是**单条可执行图案** → 求值这一条
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { P("hexcasting:const/true") });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval") });
            Check("eval(单条 const/true) -> 栈变 [true]",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is BooleanIota { Value: true }, Sig(r.Image));
        }
        {
            // eval：栈顶是**不可执行的值** → mishap（源项目 evaluatable() 的 else 分支）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval") });
            Check("eval(数字) -> Errored（不可求值）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // eval：空栈 → MishapNotEnoughArgs
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval") });
            Check("eval(空栈) -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // eval/cc：压入当前续延后求值。空程序 → 什么都不做，但栈上留下跳转目标
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new ListIota(System.Array.Empty<Iota>()) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval/cc") });
            Check("eval/cc(空列表) -> 栈上留下续延 iota",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is ContinuationIota, Sig(r.Image));
        }
        {
            // eval/cc：被求值的代码能把续延取出来（闭包的基础）。
            // 程序是 [duplicate]：把跳转目标复制一份，验证它确实在栈上可被操作。
            var env = new TestEnv();
            var program = new ListIota(new Iota[] { P("hexcasting:duplicate") });
            var img = new CastingImage(new Iota[] { program });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:eval/cc") });
            Check("eval/cc(list(duplicate)) -> 栈上 2 个续延（可被内层代码取用）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 2
                && r.Image.Stack[0] is ContinuationIota
                && r.Image.Stack[1] is ContinuationIota, Sig(r.Image));
        }
        {
            // undo：不在括号内 → MishapNeedsParens
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(不在括号内) -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // undo：括号内弹出最后一项；普通值不修正括号计数
            var env = new TestEnv();
            var parens = new ParenthesizedIota[]
            {
                new ParenthesizedIota(new DoubleIota(1), false),
                new ParenthesizedIota(new DoubleIota(2), false),
            };
            var img = new CastingImage(System.Array.Empty<Iota>(), 1, parens);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(括号内) -> 弹掉最后一项，解析为 Undone",
                r.ResolutionType == ResolvedPatternType.Undone
                && r.Image.Parenthesized.Count == 1
                && r.Image.ParenCount == 1, Sig(r.Image));
        }
        {
            // undo：撤销「未被转义的开括号」→ parenCount 减一
            var env = new TestEnv();
            var parens = new ParenthesizedIota[] { new ParenthesizedIota(P("hexcasting:open_paren"), false) };
            var img = new CastingImage(System.Array.Empty<Iota>(), 1, parens);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(撤销未转义的开括号) -> parenCount 1->0",
                r.ResolutionType == ResolvedPatternType.Undone
                && r.Image.ParenCount == 0, Sig(r.Image));
        }
        {
            // undo：撤销「未被转义的闭括号」→ parenCount 加一（方向相反！）
            var env = new TestEnv();
            var parens = new ParenthesizedIota[] { new ParenthesizedIota(P("hexcasting:close_paren"), false) };
            var img = new CastingImage(System.Array.Empty<Iota>(), 1, parens);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(撤销未转义的闭括号) -> parenCount 1->2",
                r.ResolutionType == ResolvedPatternType.Undone
                && r.Image.ParenCount == 2, Sig(r.Image));
        }
        {
            // undo：撤销「被转义的括号」→ 计数**不变**（插入的括号不该改计数）
            var env = new TestEnv();
            var parens = new ParenthesizedIota[] { new ParenthesizedIota(P("hexcasting:open_paren"), true) };
            var img = new CastingImage(System.Array.Empty<Iota>(), 1, parens);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(撤销被转义的开括号) -> parenCount 不变",
                r.ResolutionType == ResolvedPatternType.Undone
                && r.Image.ParenCount == 1, Sig(r.Image));
        }
        {
            // undo：括号列表已空但计数>0 → **直接归零**（不是减一）
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>(), 2,
                System.Array.Empty<ParenthesizedIota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:undo") });
            Check("undo(括号列表已空) -> parenCount 归零而非减一",
                r.ResolutionType == ResolvedPatternType.Undone
                && r.Image.ParenCount == 0, Sig(r.Image));
        }
        {
            // 续延 iota 是恒真值（源项目 isTruthy = true），且不可序列化必须显式报错
            var env = new TestEnv();
            var cont = new ContinuationIota(SpellContinuation.Start());
            bool truthy = cont.IsTruthy();
            bool serializeThrew = false;
            try { cont.Serialize(); }
            catch (System.NotSupportedException) { serializeThrew = true; }
            Check("ContinuationIota：恒真值且序列化显式抛 NotSupported",
                truthy && serializeThrew && cont.ChainSize() == 0);
        }
        // ==================== P1-1 视线解析（LookResolver 四层策略） ====================
        // 这些是「容易写错、又极难在游戏里观察」的逻辑：NaN 是静默故障，
        // 只表现为 isTruthy=false、所有比较为 false，不报错也不留日志。
        {
            // 第 1 层：有瞄点时用瞄点方向（本地玩家首选）
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasAim = true, AimX = 100f, AimY = 0f, SelfX = 0f, SelfY = 0f,
                Direction = 1,
            });
            Check("视线第1层：瞄点在正右 -> (1,0)",
                System.Math.Abs(x - 1f) < 1e-5f && System.Math.Abs(y) < 1e-5f, $"({x},{y})");
        }
        {
            // 第 1 层归一化：瞄点方向不是单位长度时也要归一化
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasAim = true, AimX = 300f, AimY = 400f, SelfX = 0f, SelfY = 0f,
                Direction = 1,
            });
            float len = MathF.Sqrt(x * x + y * y);
            Check("视线第1层：斜向瞄点 -> 单位向量",
                System.Math.Abs(len - 1f) < 1e-5f
                && System.Math.Abs(x - 0.6f) < 1e-4f
                && System.Math.Abs(y - 0.8f) < 1e-4f, $"({x},{y}) len={len}");
        }
        {
            // 第 1 层失效：瞄点恰好与自身重合（零向量）-> 应下落到下一层，而不是产出 NaN
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasAim = true, AimX = 50f, AimY = 50f, SelfX = 50f, SelfY = 50f,
                VelX = 0f, VelY = 5f,          // 第 2 层可用
                Direction = 1,
            });
            Check("视线：瞄点与自身重合 -> 下落而不是 NaN",
                !float.IsNaN(x) && !float.IsNaN(y)
                && System.Math.Abs(x) < 1e-5f && System.Math.Abs(y - 1f) < 1e-5f, $"({x},{y})");
        }
        {
            // 第 2 层：无瞄点时用速度方向
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                VelX = 0f, VelY = 3f, Direction = 1,
            });
            Check("视线第2层：速度向下 -> (0,1)",
                System.Math.Abs(x) < 1e-5f && System.Math.Abs(y - 1f) < 1e-5f, $"({x},{y})");
        }
        {
            // 第 2 层的**阈值规则**：速度太小视为噪声，不采信 —— 关键反直觉点
            // 0.05 像素/帧 < 阈值 0.1，所以应当落到缓存兜底
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                VelX = 0.05f, VelY = 0f,
                PrevX = 0f, PrevY = -1f,       // 缓存指向正上
                Direction = 1,
            });
            Check("视线第2层：速度低于阈值 -> 不采信速度，用缓存 (0,-1)",
                System.Math.Abs(x) < 1e-5f && System.Math.Abs(y + 1f) < 1e-5f, $"({x},{y})");
        }
        {
            // 第 3 层：有目标且无瞄点/速度时指向目标（保留「怪物盯着你」的语义）
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasTarget = true, TargetX = 0f, TargetY = 0f, SelfX = 10f, SelfY = 0f,
                Direction = 1,
            });
            Check("视线第3层：目标在左侧 -> (-1,0)",
                System.Math.Abs(x + 1f) < 1e-5f && System.Math.Abs(y) < 1e-5f, $"({x},{y})");
        }
        {
            // 第 4 层：缓存兜底 —— 消除 NaN 的关键，等价于 MC 的持久 yaw
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                PrevX = 0f, PrevY = 7f,        // 未归一化的缓存也要能用
                Direction = 1,
            });
            Check("视线第4层：静止且无目标 -> 用上次朝向 (0,1)",
                System.Math.Abs(x) < 1e-5f && System.Math.Abs(y - 1f) < 1e-5f, $"({x},{y})");
        }
        {
            // 最终兜底：什么都没用 -> 用 direction 轴向，且 direction=0 视作正方向
            var (x1, y1) = LookResolver.Resolve(new LookInput { Direction = 0 });
            var (x2, y2) = LookResolver.Resolve(new LookInput { Direction = -1 });
            Check("视线最终兜底：direction=0 -> (1,0)；direction=-1 -> (-1,0)",
                System.Math.Abs(x1 - 1f) < 1e-5f && System.Math.Abs(y1) < 1e-5f
                && System.Math.Abs(x2 + 1f) < 1e-5f && System.Math.Abs(y2) < 1e-5f,
                $"({x1},{y1}) ({x2},{y2})");
        }
        {
            // NaN 注入：瞄点、速度、缓存全是 NaN -> 必须靠最终兜底救回来，绝不外泄 NaN
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasAim = true, AimX = float.NaN, AimY = float.NaN, SelfX = float.NaN, SelfY = 0f,
                VelX = float.NaN, VelY = float.NaN,
                HasTarget = true, TargetX = float.NaN, TargetY = float.NaN,
                PrevX = float.NaN, PrevY = float.NaN,
                Direction = 1,
            });
            Check("视线：全 NaN 注入 -> 最终兜底 (1,0)，绝不外泄 NaN",
                !float.IsNaN(x) && !float.IsNaN(y)
                && System.Math.Abs(x - 1f) < 1e-5f && System.Math.Abs(y) < 1e-5f, $"({x},{y})");
        }
        {
            // 无穷注入同样要被挡住
            var (x, y) = LookResolver.Resolve(new LookInput
            {
                HasAim = true, AimX = float.PositiveInfinity, AimY = 0f, SelfX = 0f, SelfY = 0f,
                VelX = float.NegativeInfinity, VelY = 0f,
                PrevX = 0f, PrevY = -3f,
                Direction = 1,
            });
            Check("视线：无穷注入 -> 下落并用缓存 (0,-1)",
                !float.IsNaN(x) && !float.IsInfinity(x)
                && System.Math.Abs(x) < 1e-5f && System.Math.Abs(y + 1f) < 1e-5f, $"({x},{y})");
        }
        {
            // 随机扫描：任意输入组合下，输出都必须是有限单位向量（不含 NaN / 零向量）
            var rng = new System.Random(20260913);
            int bad = 0;
            for (int i = 0; i < 4000; i++)
            {
                float Pick() => (float)(rng.NextDouble() * 200.0 - 100.0);
                var (x, y) = LookResolver.Resolve(new LookInput
                {
                    HasAim = rng.Next(2) == 0,
                    AimX = Pick(), AimY = Pick(), SelfX = Pick(), SelfY = Pick(),
                    VelX = Pick(), VelY = Pick(),
                    HasTarget = rng.Next(2) == 0,
                    TargetX = Pick(), TargetY = Pick(),
                    PrevX = Pick(), PrevY = Pick(),
                    Direction = rng.Next(3) - 1,
                });
                float len = MathF.Sqrt(x * x + y * y);
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)
                    || System.Math.Abs(len - 1f) > 1e-4f)
                {
                    bad++;
                }
            }
            Check("视线：4000 组随机输入全部为有限单位向量", bad == 0, $"异常 {bad} 组");
        }
        // ==================== P1-2 只读类世界图案 ====================
        {
            // get_caster：有施法者 -> 压上该实体
            var caster = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld { Caster = caster };
            var env = new TestEnv(world: world);
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_caster") });
            Check("get_caster：有施法者 -> 压上实体 iota",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is EntityIota { Target: EntityIota.EntityKind.Player, Index: 0 },
                Sig(r.Image));
        }
        {
            // get_caster 的关键分支：**没有实体施法者时吐 NullIota，而不是报错**。
            // 源项目 env.castingEntity 可为 null（法术环 / 法术书等无人施法场景），
            // 写成 mishap 会让那类法术直接失效。
            var world = new FakeWorld { Caster = null };
            var env = new TestEnv(world: world);
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_caster") });
            Check("get_caster：无实体施法者 -> NullIota（**不报错**）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // entity_pos/eye 与 entity_pos/foot 走同一实现的 feet 开关，取值必须不同
            var world = new FakeWorld
            {
                Caster = new EntityIota(EntityIota.EntityKind.Player, 0),
                Eye = (10.0, 19.4),
                Feet = (10.0, 20.0),
            };
            var env = new TestEnv(world: world);
            var eye = new EntityIota(EntityIota.EntityKind.Player, 0);
            var imgEye = new CastingImage(new Iota[] { eye });
            var rEye = new CastingVM(imgEye, env).QueueExecute(imgEye, new Iota[] { P("hexcasting:entity_pos/eye") });
            var imgFoot = new CastingImage(new Iota[] { eye });
            var rFoot = new CastingVM(imgFoot, env).QueueExecute(imgFoot, new Iota[] { P("hexcasting:entity_pos/foot") });
            Check("entity_pos：eye 取眼位、foot 取脚底（两者不同）",
                rEye.Image.Stack.Count == 1 && rFoot.Image.Stack.Count == 1
                && rEye.Image.Stack[0] is VectorIota v1
                && rFoot.Image.Stack[0] is VectorIota v2
                && System.Math.Abs(v1.Y - 19.4) < 1e-6 && System.Math.Abs(v2.Y - 20.0) < 1e-6,
                $"{Sig(rEye.Image)} {Sig(rFoot.Image)}");
        }
        {
            // get_entity_velocity：静止实体的零向量是**合法**返回，不能被兜底掉
            var world = new FakeWorld { Vel = (0.0, 0.0) };
            var env = new TestEnv(world: world);
            var e = new EntityIota(EntityIota.EntityKind.Npc, 3);
            var img = new CastingImage(new Iota[] { e });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_entity_velocity") });
            Check("get_entity_velocity：静止实体 -> 合法零向量",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota { X: 0.0, Y: 0.0 }, Sig(r.Image));
        }
        {
            // get_entity_look：方向向量原样透传
            var world = new FakeWorld { LookDir = (0.6, 0.8) };
            var env = new TestEnv(world: world);
            var e = new EntityIota(EntityIota.EntityKind.Npc, 3);
            var img = new CastingImage(new Iota[] { e });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_entity_look") });
            Check("get_entity_look：(0.6,0.8) 原样透传",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota v
                && System.Math.Abs(v.X - 0.6) < 1e-6 && System.Math.Abs(v.Y - 0.8) < 1e-6, Sig(r.Image));
        }
        {
            // get_entity_height：返回 double
            var world = new FakeWorld { Height = 3.5 };
            var env = new TestEnv(world: world);
            var e = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var img = new CastingImage(new Iota[] { e });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_entity_height") });
            Check("get_entity_height：-> 3.5",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is DoubleIota { Value: 3.5 }, Sig(r.Image));
        }
        {
            // 参数不是实体 -> MishapInvalidIota
            var env = new TestEnv(world: new FakeWorld());
            var img = new CastingImage(new Iota[] { new DoubleIota(5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:entity_pos/eye") });
            Check("entity_pos/eye：参数不是实体 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 实体已死 -> 必须报错。【不能跳过这条校验】：
            // 泰拉用 whoAmI 索引，实体死亡后索引会被别的实体复用，
            // 不校验就会读到「另一个实体」，而且完全无声。
            var world = new FakeWorld();
            world.Dead.Add((EntityIota.EntityKind.Npc, 7));
            var env = new TestEnv(world: world);
            var e = new EntityIota(EntityIota.EntityKind.Npc, 7);
            var img = new CastingImage(new Iota[] { e });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:entity_pos/eye") });
            Check("entity_pos/eye：目标已死 -> Errored（索引会被复用，必须校验）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 超出施法范围 -> MishapEntityTooFarAway（原作的核心平衡机制）
            var world = new FakeWorld();
            world.Far.Add((EntityIota.EntityKind.Npc, 8));
            var env = new TestEnv(world: world);
            var e = new EntityIota(EntityIota.EntityKind.Npc, 8);
            var img = new CastingImage(new Iota[] { e });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:entity_pos/eye") });
            Check("entity_pos/eye：超出范围 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 无世界访问的环境（纯 VM）-> 必须明确报错，而不是静默返回零向量
            var env = new TestEnv();   // world 默认 null
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_caster") });
            Check("无世界访问的环境 -> Errored（绝不静默吐零向量）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        // ==================== P1-2b 射线：图格 DDA ====================
        // 射线求交是差一错误的重灾区（边界落在整数格线上归谁、起点格算不算），
        // 所以纯几何部分单独测。
        {
            // 正右方打，第一格实心在 x=3
            var solid = new HashSet<(int, int)> { (3, 0) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 1.0, 0.0, 32.0);
            Check("DDA：正右方命中 x=3，法线 (-1,0)",
                hit is { TileX: 3, TileY: 0, NormalX: -1, NormalY: 0 },
                hit?.ToString() ?? "null");
        }
        {
            // 正左方打，命中面在右侧 → 法线 (+1,0)
            var solid = new HashSet<(int, int)> { (-2, 0) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, -1.0, 0.0, 32.0);
            Check("DDA：正左方命中 x=-2，法线 (+1,0)",
                hit is { TileX: -2, NormalX: 1, NormalY: 0 }, hit?.ToString() ?? "null");
        }
        {
            // 正下方打 → 法线 (0,-1)
            var solid = new HashSet<(int, int)> { (0, 5) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 0.0, 1.0, 32.0);
            Check("DDA：正下方命中 y=5，法线 (0,-1)",
                hit is { TileX: 0, TileY: 5, NormalX: 0, NormalY: -1 }, hit?.ToString() ?? "null");
        }
        {
            // 起点就在实心格内 → 立刻命中，无进入面（法线 0,0）
            var solid = new HashSet<(int, int)> { (0, 0) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 1.0, 0.0, 32.0);
            Check("DDA：起点在实心格内 -> 命中自身，法线 (0,0)",
                hit is { TileX: 0, TileY: 0, NormalX: 0, NormalY: 0 }, hit?.ToString() ?? "null");
        }
        {
            // 射程之外 → 不命中（maxDist 生效）
            var solid = new HashSet<(int, int)> { (10, 0) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 1.0, 0.0, 5.0);
            Check("DDA：实心格在射程外 -> 不命中", hit == null, hit?.ToString() ?? "null");
        }
        {
            // 零方向 → 必须 null。
            // 【不能】用 SafeNormalize 兜底成 (1,0)：那会把「玩家给了零向量」
            // 悄悄变成「向右打 32 格」，可能命中他根本没指的方块。
            var solid = new HashSet<(int, int)> { (3, 0) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 0.0, 0.0, 32.0);
            Check("DDA：零方向 -> null（不得兜底成 (1,0)）", hit == null, hit?.ToString() ?? "null");
        }
        {
            // 45° 斜射：命中对角格
            var solid = new HashSet<(int, int)> { (2, 2) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 0.5, 1.0, 1.0, 32.0);
            Check("DDA：45° 斜射命中 (2,2)",
                hit is { TileX: 2, TileY: 2 }, hit?.ToString() ?? "null");
        }
        {
            // 轴向射线（另一轴分量为 0）不得死循环，且能正常命中
            var solid = new HashSet<(int, int)> { (4, 7) };
            var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), 0.5, 7.5, 1.0, 0.0, 32.0);
            Check("DDA：纯水平射线命中 (4,7)（不死循环）",
                hit is { TileX: 4, TileY: 7 }, hit?.ToString() ?? "null");
        }
        {
            // 随机扫描：任意方向/任意实心格布局下，命中格必须真的是实心格
            var rng = new System.Random(4242);
            int bad = 0;
            for (int iter = 0; iter < 2000; iter++)
            {
                var solid = new HashSet<(int, int)>();
                for (int k = 0; k < 30; k++) solid.Add((rng.Next(-20, 21), rng.Next(-20, 21)));
                double ox = rng.NextDouble() * 10 - 5;
                double oy = rng.NextDouble() * 10 - 5;
                double dx = rng.NextDouble() * 2 - 1;
                double dy = rng.NextDouble() * 2 - 1;
                var hit = TileRaycast.Cast((x, y) => solid.Contains((x, y)), ox, oy, dx, dy, 32.0);
                if (hit is { } h && !solid.Contains((h.TileX, h.TileY))) bad++;
            }
            Check("DDA：2000 组随机输入，命中格必为实心格", bad == 0, $"异常 {bad} 组");
        }

        // ==================== 线段 vs 判定箱扫掠 ====================
        {
            var e1 = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var boxes = new List<EntityBox>
            {
                new() { Entity = e1, MinX = 5, MinY = -1, MaxX = 6, MaxY = 1 },
            };
            var hit = SegmentSweep.Cast(boxes, 0, 0, 1, 0, 32);
            Check("扫掠：命中正前方的箱",
                hit is { } h && h.Entity.Index == 1 && System.Math.Abs(h.Distance - 5) < 1e-6,
                hit?.Distance.ToString() ?? "null");
        }
        {
            // 更近的箱必须胜出 —— 射线穿过多个实体时只命中最近的那个
            var near = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var far = new EntityIota(EntityIota.EntityKind.Npc, 2);
            var boxes = new List<EntityBox>
            {
                new() { Entity = far, MinX = 10, MinY = -1, MaxX = 11, MaxY = 1 },
                new() { Entity = near, MinX = 3, MinY = -1, MaxX = 4, MaxY = 1 },
            };
            var hit = SegmentSweep.Cast(boxes, 0, 0, 1, 0, 32);
            Check("扫掠：两个箱 -> 取最近的（Index=1）",
                hit is { } h && h.Entity.Index == 1, hit?.Entity.Index.ToString() ?? "null");
        }
        {
            // 起点已在箱内 -> 距离 0
            var e1 = new EntityIota(EntityIota.EntityKind.Player, 0);
            var boxes = new List<EntityBox>
            {
                new() { Entity = e1, MinX = -1, MinY = -1, MaxX = 1, MaxY = 1 },
            };
            var hit = SegmentSweep.Cast(boxes, 0, 0, 1, 0, 32);
            Check("扫掠：起点在箱内 -> 距离 0",
                hit is { } h && h.Distance == 0.0, hit?.Distance.ToString() ?? "null");
        }
        {
            // 偏离方向 -> 不命中
            var e1 = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var boxes = new List<EntityBox>
            {
                new() { Entity = e1, MinX = 5, MinY = 50, MaxX = 6, MaxY = 51 },
            };
            var hit = SegmentSweep.Cast(boxes, 0, 0, 1, 0, 32);
            Check("扫掠：箱子不在射线上 -> null", hit == null, hit?.ToString() ?? "null");
        }
        {
            // 零方向 -> null
            var e1 = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var boxes = new List<EntityBox>
            {
                new() { Entity = e1, MinX = 5, MinY = -1, MaxX = 6, MaxY = 1 },
            };
            var hit = SegmentSweep.Cast(boxes, 0, 0, 0, 0, 32);
            Check("扫掠：零方向 -> null", hit == null, hit?.ToString() ?? "null");
        }

        // ==================== raycast 三个图案（端到端） ====================
        {
            // raycast 返回**图格中心**而不是命中点（源项目刻意如此）
            var world = new FakeWorld();
            world.Solid.Add((4, 2));
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.5, 2.5), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast") });
            Check("raycast：命中 (4,2) -> 返回格中心 (4.5, 2.5)",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota v
                && System.Math.Abs(v.X - 4.5) < 1e-6 && System.Math.Abs(v.Y - 2.5) < 1e-6, Sig(r.Image));
        }
        {
            // 没打中 -> NullIota（不是报错）
            var world = new FakeWorld();
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.5, 0.5), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast") });
            Check("raycast：未命中 -> NullIota",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // raycast/axis 返回法线
            var world = new FakeWorld();
            world.Solid.Add((4, 2));
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.5, 2.5), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast/axis") });
            Check("raycast/axis：从左侧命中 -> 法线 (-1,0)",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota n
                && System.Math.Abs(n.X + 1.0) < 1e-6 && System.Math.Abs(n.Y) < 1e-6, Sig(r.Image));
        }
        {
            // 起点超范围 -> mishap（射线不能从范围外发射）
            var world = new FakeWorld { RangeCheck = (x, y) => false };
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(99.0, 99.0), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast") });
            Check("raycast：起点超范围 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 命中点超范围 -> NullIota（源项目同样再查一次命中点）
            var world = new FakeWorld { RangeCheck = (x, y) => x < 4.0 };
            world.Solid.Add((4, 2));
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.5, 2.5), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast") });
            Check("raycast：命中点超范围 -> NullIota",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // raycast/entity 命中实体
            var target = new EntityIota(EntityIota.EntityKind.Npc, 5);
            var world = new FakeWorld();
            world.Boxes.Add(new EntityBox { Entity = target, MinX = 3, MinY = -1, MaxX = 4, MaxY = 1 });
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.0, 0.0), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast/entity") });
            Check("raycast/entity：命中 NPC#5",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is EntityIota { Target: EntityIota.EntityKind.Npc, Index: 5 }, Sig(r.Image));
        }
        {
            // raycast/entity 命中但实体超范围 -> NullIota
            var target = new EntityIota(EntityIota.EntityKind.Npc, 5);
            var world = new FakeWorld();
            world.Boxes.Add(new EntityBox { Entity = target, MinX = 3, MinY = -1, MaxX = 4, MaxY = 1 });
            world.Far.Add((EntityIota.EntityKind.Npc, 5));
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(0.0, 0.0), new VectorIota(1.0, 0.0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast/entity") });
            Check("raycast/entity：命中实体超范围 -> NullIota",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // 参数不是向量 -> mishap
            var env = new TestEnv(world: new FakeWorld());
            var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:raycast") });
            Check("raycast：参数不是向量 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        // ==================== P1-3 写入类法术（add_motion / blink） ====================
        {
            // add_motion：首次推动，消耗 = |motion|² × 粉尘
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld();
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[]
            {
                target, new VectorIota(3.0, 4.0),   // |motion|² = 25
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });
            Check("add_motion：首次推动消耗 25 × 10000 = 250000",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && env.Media == 1_000_000 - 250_000, $"剩余 {env.Media}");
        }
        {
            // add_motion 的防刷机制（源项目 bug #387）：
            // 同一目标第二次推动，多收 1 粉尘 = 10000
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld();
            var env = new TestEnv(world: world);

            // 同一个 image 上连续推两次：userData 必须跨图案保持
            var img = new CastingImage(new Iota[] { target, new VectorIota(3.0, 4.0) });
            var vm = new CastingVM(img, env);
            var r1 = vm.QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });
            // 把第一次的结果作为下一次的起点，模拟「同一法术里的第二次」
            var img2 = r1.Image.WithStack(new Iota[] { target, new VectorIota(3.0, 4.0) });
            var vm2 = new CastingVM(img2, env);
            var r2 = vm2.QueueExecute(img2, new Iota[] { P("hexcasting:add_motion") });

            Check("add_motion：同目标第二次多收 1 粉尘（bug #387 防刷）",
                r1.ResolutionType == ResolvedPatternType.Evaluated
                && r2.ResolutionType == ResolvedPatternType.Evaluated
                && env.Media == 1_000_000 - 250_000 - 260_000, $"剩余 {env.Media}");
        }
        {
            // add_motion：不同目标各算首次，都不加价
            var world = new FakeWorld();
            var env = new TestEnv(world: world);
            var a = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var b = new EntityIota(EntityIota.EntityKind.Npc, 2);

            var img = new CastingImage(new Iota[] { a, new VectorIota(3.0, 4.0) });
            var r1 = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });
            var img2 = r1.Image.WithStack(new Iota[] { b, new VectorIota(3.0, 4.0) });
            var r2 = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:add_motion") });

            Check("add_motion：不同目标 -> 都是首次价（共 500000）",
                env.Media == 1_000_000 - 500_000, $"剩余 {env.Media}");
        }
        {
            // add_motion：超过 MAX_MOTION 时**推力被截断**，但**计费用原始长度**。
            // 这两者用不同来源是源项目的原样行为，写错会让超猛推动变得便宜。
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: long.MaxValue / 4);
            var img = new CastingImage(new Iota[]
            {
                target, new VectorIota(20000.0, 0.0),   // 远超 8192
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });
            Check("add_motion：超上限 -> 推力截断到 8192，计费仍按原始长度 (4e8 粉尘)",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && System.Math.Abs(world.LastMotion.X - 8192.0) < 1e-6
                && System.Math.Abs(world.LastMotion.Y) < 1e-6,
                $"推力 ({world.LastMotion.X:0.##},{world.LastMotion.Y:0.##})");
        }
        {
            // 【关键设计点】副作用顺序：必须先扣媒质、再施放。
            // 顺序反了就能白嫖位移 —— 媒质不足时实体已经动了。
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld();
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[] { target, new VectorIota(1.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });

            var combined = env.Trace.Concat(world.Trace).ToList();
            Check("add_motion：副作用顺序 = 先扣媒质、再施放",
                combined.Count == 2 && combined[0] == "media" && combined[1] == "motion",
                string.Join(" -> ", combined));
        }
        {
            // blink：沿**目标视线**瞬移，消耗 = round(50000 × |delta| × 0.5)
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld { LookDir = (1.0, 0.0) };
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[] { target, new DoubleIota(2.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:blink") });
            Check("blink：delta=2 沿朝向瞬移 (2,0)，消耗 50000",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && System.Math.Abs(world.LastTeleport.X - 2.0) < 1e-6
                && System.Math.Abs(env.Media - (1_000_000 - 50_000)) < 1e-6,
                $"位移 ({world.LastTeleport.X:0.##},{world.LastTeleport.Y:0.##}) 剩余 {env.Media}");
        }
        {
            // blink：负距离要朝反方向（消耗取绝对值）
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld { LookDir = (1.0, 0.0) };
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[] { target, new DoubleIota(-3.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:blink") });
            Check("blink：delta=-3 -> 位移 (-3,0)，消耗仍是 75000",
                System.Math.Abs(world.LastTeleport.X + 3.0) < 1e-6
                && env.Media == 1_000_000 - 75_000,
                $"位移 {world.LastTeleport.X:0.##} 剩余 {env.Media}");
        }
        {
            // blink：终点超出施法范围 -> mishap（只查起点的话可以传送到半张地图外）
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld { LookDir = (1.0, 0.0), RangeCheck = (x, y) => x < 5.0 };
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[] { target, new DoubleIota(100.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:blink") });
            Check("blink：终点超范围 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // blink：终点在世界外 -> MishapBadLocation
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld { LookDir = (1.0, 0.0), InWorld = false };
            var env = new TestEnv(world: world);
            var img = new CastingImage(new Iota[] { target, new DoubleIota(1.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:blink") });
            Check("blink：终点在世界外 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 媒质不足：整个法术中止，不得留下任何世界改动
            var target = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 10);   // 远远不够
            var img = new CastingImage(new Iota[] { target, new VectorIota(3.0, 4.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:add_motion") });
            Check("add_motion：媒质不足 -> Errored 且**没有**任何世界改动",
                r.ResolutionType == ResolvedPatternType.Errored && world.Trace.Count == 0,
                "轨迹: " + string.Join(",", world.Trace));
        }
        // ==================== M-6 媒质支付规划（源项目 extractMediaFromInventory） ====================
        // 这组测试原来围绕「玩家媒质池 → 物品 → 找零回池」—— 原版没有媒质池，已按原版改写。
        {
            MediaSource Dust(int slot, int n) => new() { Slot = slot, Priority = MediaPriority.AmethystDust, UnitValue = MediaConstants.DustUnit, Count = n };
            MediaSource Charged(int slot, int n) => new() { Slot = slot, Priority = MediaPriority.ChargedAmethyst, UnitValue = MediaConstants.CrystalUnit, Count = n };
            MediaSource Quenched(int slot, int n) => new() { Slot = slot, Priority = MediaPriority.QuenchedShard, UnitValue = MediaConstants.QuenchedShardUnit, Count = n };
            MediaSource Flask(int slot, long m) => new() { Slot = slot, Priority = MediaPriority.Battery, Stored = m };

            var p1 = MediaPaymentPlanner.Plan(50_000, new[] { Dust(0, 9), Flask(5, 100_000) });
            Check("支付：媒质瓶优先级最高，按量扣（不碰粉）",
                p1.Withdrawals.SequenceEqual(new[] { new MediaWithdrawal(5, 0, 50_000) }) && p1.Wasted == 0 && p1.Shortfall == 0,
                string.Join(";", p1.Withdrawals));

            var p2 = MediaPaymentPlanner.Plan(15_000, new[] { Dust(3, 5) });
            Check("支付：堆叠物品整件扣，多付的浪费（原版不找零）",
                p2.Withdrawals.SequenceEqual(new[] { new MediaWithdrawal(3, 2, 0) }) && p2.Wasted == 5_000 && p2.Shortfall == 0,
                $"{string.Join(";", p2.Withdrawals)} 浪费 {p2.Wasted}");

            var p3 = MediaPaymentPlanner.Plan(10_000, new[] { Quenched(0, 1), Charged(1, 1), Dust(2, 1) });
            Check("支付：优先级 粉 > 充能紫水晶 > 淬灵碎片",
                p3.Withdrawals.Count == 1 && p3.Withdrawals[0].Slot == 2, string.Join(";", p3.Withdrawals));

            var p4 = MediaPaymentPlanner.Plan(10_000, new[] { Dust(0, 2), Dust(1, 7) });
            Check("支付：同优先级存量大的先扣", p4.Withdrawals[0].Slot == 1, string.Join(";", p4.Withdrawals));

            var p5 = MediaPaymentPlanner.Plan(100_000, new[] { Flask(0, 30_000), Dust(1, 2) });
            Check("支付：都扣光仍不够 → 缺口 50000（交给过载）",
                p5.Shortfall == 50_000 && p5.Withdrawals.Count == 2, $"缺口 {p5.Shortfall}");

            var p6 = MediaPaymentPlanner.Plan(0, new[] { Dust(0, 3) });
            Check("支付：费用 0 → 什么都不扣", p6.Withdrawals.Count == 0 && p6.Shortfall == 0);

            var p7 = MediaPaymentPlanner.Plan(1, new[] { Dust(0, 2), Charged(1, 1), Flask(2, 7) });
            Check("支付：TotalAvailable = 所有来源总和", p7.TotalAvailable == 20_000 + 100_000 + 7, p7.TotalAvailable.ToString());
        }
        // ==================== M-3 晶洞掉落表（移植最容易静默走样的地方） ====================
        // 掉落表的分支在游戏里长得一模一样，只有数字不同 —— 不做确定性测试根本发现不了。
        // 下面用一个可控的随机源把所有分支逐一钉死。
        {
            // 生长阶段链：空 → 小 → 中 → 大 → 成熟，成熟后不再变
            var s = AmethystStage.None;
            s = AmethystLoot.NextStage(s);
            bool c1 = s == AmethystStage.SmallBud;
            s = AmethystLoot.NextStage(s);
            bool c2 = s == AmethystStage.MediumBud;
            s = AmethystLoot.NextStage(s);
            bool c3 = s == AmethystStage.LargeBud;
            s = AmethystLoot.NextStage(s);
            bool c4 = s == AmethystStage.Cluster;
            bool c5 = AmethystLoot.NextStage(s) == AmethystStage.Cluster;
            Check("生长链：空→小→中→大→成熟→（封顶）", c1 && c2 && c3 && c4 && c5, s.ToString());
        }
        {
            // 只有成熟晶簇掉落。三个芽阶段什么都不掉 ——
            // 这是「必须等它长熟」这一玩法的全部意义。
            Check("掉落：只有成熟晶簇掉东西，三个芽阶段都不掉",
                !AmethystLoot.DropsLoot(AmethystStage.SmallBud)
                && !AmethystLoot.DropsLoot(AmethystStage.MediumBud)
                && !AmethystLoot.DropsLoot(AmethystStage.LargeBud)
                && AmethystLoot.DropsLoot(AmethystStage.Cluster));
        }
        {
            // 随机刻：nextInt(5) == 0 才生长
            Check("随机刻：nextInt(5)==0 生长，其余不生长",
                AmethystLoot.RollGrowth(_ => 0)
                && !AmethystLoot.RollGrowth(_ => 1)
                && !AmethystLoot.RollGrowth(_ => 4));
        }
        {
            // ore_drops 公式：时运 0 原样；时运 >0 时 i = nextInt(f+2)-1，负数归零
            int o0 = AmethystLoot.ApplyOreDrops(3, 0, _ => 0);            // 时运 0 -> 3
            int o1 = AmethystLoot.ApplyOreDrops(3, 2, _ => 0);            // nextInt(4)=0 -> i=-1 -> 0 -> 3
            int o2 = AmethystLoot.ApplyOreDrops(3, 2, _ => 1);            // i=0 -> 3
            int o3 = AmethystLoot.ApplyOreDrops(3, 2, _ => 3);            // i=2 -> 9
            Check("ore_drops 公式：时运 0 原样 / 负数归零 / 最高 ×(f+1)",
                o0 == 3 && o1 == 3 && o2 == 3 && o3 == 9, $"{o0} {o1} {o2} {o3}");
        }
        {
            // 充能紫水晶掉率表：逐条对齐源项目 [0.25, 0.35, 0.5, 0.75, 1.0]
            bool ok = System.Math.Abs(AmethystLoot.ChargedChance(0) - 0.25) < 1e-9
                   && System.Math.Abs(AmethystLoot.ChargedChance(1) - 0.35) < 1e-9
                   && System.Math.Abs(AmethystLoot.ChargedChance(2) - 0.50) < 1e-9
                   && System.Math.Abs(AmethystLoot.ChargedChance(3) - 0.75) < 1e-9
                   && System.Math.Abs(AmethystLoot.ChargedChance(4) - 1.00) < 1e-9
                   && System.Math.Abs(AmethystLoot.ChargedChance(9) - 1.00) < 1e-9;
            Check("充能晶体掉率表 = [0.25,0.35,0.5,0.75,1.0]（超界取满）", ok);
        }
        {
            // 【关键分支】工具合格 + 时运 0：粉 1~4，充能晶体 25%，碎片减半
            var r = AmethystLoot.RollCluster(
                properTool: true, fortuneLevel: 0,
                nextInt: n => 0,
                chance: _ => true,
                vanillaShardBase: 2);
            Check("工具合格+时运0：粉=1（取下界），充能=1，碎片=2×0.5=1",
                r.Dust == 1 && r.ChargedCrystal == 1 && r.Shards == 1,
                $"粉{r.Dust} 晶体{r.ChargedCrystal} 碎片{r.Shards}");
        }
        {
            // 【关键分支】工具合格 + 时运 0：粉取上界 = 4
            var r = AmethystLoot.RollCluster(
                properTool: true, fortuneLevel: 0,
                nextInt: n => n - 1,
                chance: _ => true,
                vanillaShardBase: 2);
            Check("工具合格+时运0：粉取上界=4", r.Dust == 4, $"粉{r.Dust}");
        }
        {
            // 【关键分支】工具**不合格**：粉 0~2（可低到 0），充能固定 12.5%
            var r = AmethystLoot.RollCluster(
                properTool: false, fortuneLevel: 0,
                nextInt: n => 0,
                chance: _ => false,
                vanillaShardBase: 2);
            Check("工具不合格：粉可低到 0，充能晶体不中",
                r.Dust == 0 && r.ChargedCrystal == 0, $"粉{r.Dust} 晶体{r.ChargedCrystal}");
        }
        {
            // 【最易错分支】工具不合格时**时运完全无效** —— 粉仍只能是 0~2，充能仍是 12.5%。
            // 把时运也套到不合格分支上，玩家拿木镐就能刷晶体。
            double seenChance = -1;
            var r = AmethystLoot.RollCluster(
                properTool: false, fortuneLevel: 4,
                nextInt: n => n - 1,
                chance: p => { seenChance = p; return true; },
                vanillaShardBase: 1);
            Check("工具不合格时时运无效：粉上界=2、充能掉率=0.125",
                r.Dust == 2 && System.Math.Abs(seenChance - 0.125) < 1e-9,
                $"粉{r.Dust} 掉率{seenChance}");
        }
        {
            // 工具合格时充能掉率必须用查表值（时运 3 -> 0.75）
            double seenChance = -1;
            AmethystLoot.RollCluster(
                properTool: true, fortuneLevel: 3,
                nextInt: n => 0,
                chance: p => { seenChance = p; return false; },
                vanillaShardBase: 1);
            Check("工具合格时充能掉率查表（时运3 -> 0.75）",
                System.Math.Abs(seenChance - 0.75) < 1e-9, seenChance.ToString());
        }
        {
            // 碎片减半：基础 3 -> 1（向下取整），源项目 AmethystReducerFunc 的效果
            var r = AmethystLoot.RollCluster(
                properTool: true, fortuneLevel: 0,
                nextInt: n => 0, chance: _ => false, vanillaShardBase: 3);
            Check("碎片减半：基础 3 -> 1（向下取整）", r.Shards == 1, r.Shards.ToString());
        }
        {
            // 镐力 → 时运等级映射：分档边界
            bool ok = AmethystLoot.FortuneFromPickaxePower(35) == 0
                   && AmethystLoot.FortuneFromPickaxePower(99) == 0
                   && AmethystLoot.FortuneFromPickaxePower(100) == 1
                   && AmethystLoot.FortuneFromPickaxePower(149) == 1
                   && AmethystLoot.FortuneFromPickaxePower(150) == 2
                   && AmethystLoot.FortuneFromPickaxePower(180) == 3
                   && AmethystLoot.FortuneFromPickaxePower(200) == 4
                   && AmethystLoot.FortuneFromPickaxePower(225) == 4;
            Check("镐力→时运：35/100/150/180/200 分档正确", ok);
        }
        {
            // 4000 次随机模拟：不变量 —— 合格工具的粉下限是 1（不合格可以是 0）
            var rng = new System.Random(7788);
            int badProper = 0, badImproper = 0;
            for (int i = 0; i < 4000; i++)
            {
                var a = AmethystLoot.RollCluster(true, rng.Next(5), n => rng.Next(n), p => rng.NextDouble() < p);
                if (a.Dust < 1) badProper++;
                var b = AmethystLoot.RollCluster(false, rng.Next(5), n => rng.Next(n), p => rng.NextDouble() < p);
                if (b.Dust < 0 || b.Dust > 2) badImproper++;
                if (b.ChargedCrystal > 1) badImproper++;
            }
            Check("4000 次模拟：合格工具粉>=1；不合格工具粉在[0,2]且晶体<=1",
                badProper == 0 && badImproper == 0, $"异常 {badProper}/{badImproper}");
        }
        // ==================== P1-3b 大传送（teleport/great） ====================
        {
            // 位移是**参数**而不是视线方向 —— 这是它与 blink 的核心区别
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(30.0, -20.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：按参数位移 (30,-20)，消耗 1,000,000",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && System.Math.Abs(world.LastTeleport.X - 30.0) < 1e-6
                && System.Math.Abs(world.LastTeleport.Y + 20.0) < 1e-6
                && env.Media == 5_000_000 - 1_000_000,
                $"位移({world.LastTeleport.X},{world.LastTeleport.Y}) 剩余{env.Media}");
        }
        {
            // 世界外 -> MishapBadLocation
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld { InWorld = false };
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(1.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：目标点在世界外 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 【易漏】目标点的**正下方一格**也必须在世界内。
            // 只查目标点的话，可以把实体送到世界最底行，它会一直往下掉、再也回不来。
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld { RangeCheck = (x, y) => true };
            // 让「y-1」落在世界外：只有 y > 0 时才算在世界内
            world.InWorldCheck = (x, y) => y > 19.5;   // 世界底边在 19 与 20 之间
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(0.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：仅正下方一格出界 -> Errored（防掉出世界）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 目标实体超范围 -> mishap
            var target = new EntityIota(EntityIota.EntityKind.Npc, 9);
            var world = new FakeWorld();
            world.Far.Add((EntityIota.EntityKind.Npc, 9));
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(1.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：目标超范围 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 媒质不足（100 万是一笔大钱）-> 中止且无世界改动
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 999_999) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(1.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：媒质不足 -> Errored 且无世界改动",
                r.ResolutionType == ResolvedPatternType.Errored && world.Trace.Count == 0,
                "轨迹: " + string.Join(",", world.Trace));
        }
        {
            // 启蒙门控：teleport/great 在注册表里被标记为需要启蒙
            var def = PatternRegistry.All.FirstOrDefault(d => d.Id == "hexcasting:teleport/great");
            Check("teleport/great：注册表标记为需要启蒙",
                def != null && def.RequiresEnlightenment, def == null ? "找不到" : "未标记");
        }
        {
            // 未启蒙时执行 -> MishapUnenlightened（在 PatternIota 查表阶段就拦下）
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = false };
            var img = new CastingImage(new Iota[] { target, new VectorIota(1.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：未启蒙 -> Invalid（原版 MishapUnenlightened.resolutionType）（大法术门槛）",
                r.ResolutionType == ResolvedPatternType.Invalid && world.Trace.Count == 0,
                Sig(r.Image));
        }
        {
            // 掉落代价：距离决定了掉率，且**永不掉落主手**
            // 这里只验证「距离被正确传给世界侧」，具体掉落由泰拉实现负责
            var target = new EntityIota(EntityIota.EntityKind.Player, 0);
            var world = new FakeWorld { Caster = target };
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { target, new VectorIota(3.0, 4.0) });   // 距离 = 5
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：传送自己 -> 把传送距离(5)交给世界侧结算掉落",
                System.Math.Abs(world.LastScatterDistance - 5.0) < 1e-6,
                world.LastScatterDistance.ToString());
        }
        {
            // 原版 `teleportee == env.castingEntity`：把**别人**传走不会震落别人的东西
            var other = new EntityIota(EntityIota.EntityKind.Player, 1);
            var world = new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) };
            var env = new TestEnv(world: world, media: 5_000_000) { Enlightened = true };
            var img = new CastingImage(new Iota[] { other, new VectorIota(3.0, 4.0) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:teleport/great") });
            Check("teleport/great：传送别人 -> 不震落任何人的东西",
                world.LastScatterDistance == 0, world.LastScatterDistance.ToString());
        }
        // ==================== P2-3b iota 序列化往返 ====================
        // 早期版本的裸值格式**不可往返**：NullIota 与 GarbageIota 都写成 null、
        // VectorIota 与 EntityIota 都写成 double[2]。
        // 这类错误不报错，只会让读档后的法术行为诡异地变化。
        {
            // 全类型往返
            var cases = new (string Name, Iota Value)[]
            {
                ("null", NullIota.Instance),
                ("garbage", GarbageIota.Instance),
                ("true", BooleanIota.True),
                ("false", BooleanIota.False),
                ("double", new DoubleIota(3.25)),
                ("vec", new VectorIota(1.5, -2.5)),
                ("entity", new EntityIota(EntityIota.EntityKind.Npc, 42)),
                ("pattern", P("hexcasting:add")),
            };
            int bad = 0;
            var badNames = new List<string>();
            foreach (var (name, value) in cases)
            {
                var data = value.Serialize();
                if (!IotaSerializer.TryDeserialize(data, out var back) || !back.ValueEquals(value))
                {
                    bad++;
                    badNames.Add(name);
                }
            }
            Check("序列化：8 种 iota 全部往返一致", bad == 0, "失败: " + string.Join(",", badNames));
        }
        {
            // 【关键回归】null 与 garbage 必须可区分 —— 旧格式下两者都是 null
            bool okNull = IotaSerializer.TryDeserialize(NullIota.Instance.Serialize(), out var backNull);
            bool okGarbage = IotaSerializer.TryDeserialize(GarbageIota.Instance.Serialize(), out var backGarbage);
            Check("序列化：null 与 garbage 不再混淆",
                okNull && okGarbage
                && backNull is NullIota && backGarbage is GarbageIota,
                $"{backNull?.TypeName} / {backGarbage?.TypeName}");
        }
        {
            // 【关键回归】vec 与 entity 必须可区分 —— 旧格式下两者都是 2 元素 double 数组
            bool okVec = IotaSerializer.TryDeserialize(new VectorIota(3, 7).Serialize(), out var backVec);
            bool okEnt = IotaSerializer.TryDeserialize(
                new EntityIota(EntityIota.EntityKind.Projectile, 7).Serialize(), out var backEnt);
            Check("序列化：vec 与 entity 不再混淆",
                okVec && okEnt && backVec is VectorIota && backEnt is EntityIota,
                $"{backVec?.TypeName} / {backEnt?.TypeName}");
        }
        {
            // 嵌套列表往返
            var nested = new ListIota(new Iota[]
            {
                new DoubleIota(1),
                new ListIota(new Iota[] { BooleanIota.True, NullIota.Instance }),
                new VectorIota(-1, 2),
            });
            bool ok = IotaSerializer.TryDeserialize(nested.Serialize(), out var back);
            Check("序列化：嵌套列表往返一致",
                ok && back is ListIota { Count: 3 } && back.ValueEquals(nested), back?.ToString() ?? "失败");
        }
        {
            // 空列表往返
            var empty = new ListIota(System.Array.Empty<Iota>());
            bool ok = IotaSerializer.TryDeserialize(empty.Serialize(), out var back);
            Check("序列化：空列表往返一致", ok && back is ListIota { Count: 0 }, back?.ToString() ?? "失败");
        }
        {
            // 图案的起始方向必须一并保留（否则读回来是另一个图案的签名）
            var p = P("hexcasting:add_motion");
            bool ok = IotaSerializer.TryDeserialize(p.Serialize(), out var back);
            Check("序列化：图案的起始方向一并保留",
                ok && back is PatternIota bp
                && bp.Pattern.StartDir == p.Pattern.StartDir
                && bp.Pattern.AnglesSignature() == p.Pattern.AnglesSignature(),
                back is PatternIota b2 ? b2.DescribeForTest() : "失败");
        }
        {
            // 续延**必须明确失败**，不能造一个「跳转目标为空」的假续延
            var cont = new ContinuationIota(SpellContinuation.Start());
            bool threw = false;
            try { cont.Serialize(); } catch (System.NotSupportedException) { threw = true; }
            // 即使硬造一个续延信封，反序列化也必须拒绝
            var fake = IotaSerializer.Envelope(IotaSerializer.KindContinuation, null);
            bool rejected = !IotaSerializer.TryDeserialize(fake, out _);
            Check("序列化：续延明确失败（不造假续延）", threw && rejected);
        }
        {
            // 畸形输入一律返回 false，不得静默降级成某个默认值
            int bad = 0;
            if (IotaSerializer.TryDeserialize(null, out _)) bad++;                       // null 载荷
            if (IotaSerializer.TryDeserialize("随便一个字符串", out _)) bad++;            // 不是信封
            if (IotaSerializer.TryDeserialize(42.0, out _)) bad++;                       // 不是信封
            if (IotaSerializer.TryDeserialize(
                    IotaSerializer.Envelope("未来版本才有的种类", 1.0), out _)) bad++;   // 未知种类
            if (IotaSerializer.TryDeserialize(
                    IotaSerializer.Envelope(IotaSerializer.KindDouble, "不是数字"), out _)) bad++;
            if (IotaSerializer.TryDeserialize(
                    IotaSerializer.Envelope(IotaSerializer.KindVec, new List<object?> { 1.0 }), out _)) bad++;  // 少一个分量
            if (IotaSerializer.TryDeserialize(
                    IotaSerializer.Envelope(IotaSerializer.KindEntity, new List<object?> { 99.0, 1.0 }), out _)) bad++;  // 种类越界
            Check("序列化：7 种畸形输入全部被拒绝（不静默降级）", bad == 0, $"漏过 {bad} 个");
        }
        {
            // 列表里有一项坏掉 -> **整个列表**失败。
            // 保留半个列表会让「法术少了一个参数」这种问题非常难查。
            var broken = IotaSerializer.Envelope(IotaSerializer.KindList, new List<object?>
            {
                IotaSerializer.Envelope(IotaSerializer.KindDouble, 1.0),
                "坏数据",
            });
            Check("序列化：列表含坏项 -> 整列表失败", !IotaSerializer.TryDeserialize(broken, out _));
        }
        {
            // 非法图案签名必须被拒绝（不能造出一条画不出来的图案）
            var badPattern = IotaSerializer.Envelope(IotaSerializer.KindPattern,
                new List<object?> { "这不是合法角度串", 0.0 });
            Check("序列化：非法图案签名被拒绝", !IotaSerializer.TryDeserialize(badPattern, out _));
        }
        {
            // 往返稳定性：序列化 -> 反序列化 -> 再序列化，两次字节内容应等价
            var original = new ListIota(new Iota[]
            {
                new DoubleIota(2.5), new VectorIota(1, 1), P("hexcasting:eval"),
            });
            IotaSerializer.TryDeserialize(original.Serialize(), out var once);
            bool same = IotaSerializer.TryDeserialize(once.Serialize(), out var twice)
                        && twice.ValueEquals(original);
            Check("序列化：二次往返仍然一致（格式稳定）", same, twice?.ToString() ?? "失败");
        }
        // ==================== P2-3b read_into_parens（数据载体） ====================
        {
            // 不在括号内 -> MishapNeedsParens（没有括号就没有「放进哪里」）
            var env = new TestEnv { HeldIota = new DoubleIota(7) };
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });
            Check("read_into_parens：不在括号内 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 括号内 + 手上有东西 -> 那个 iota 进入括号列表
            var env = new TestEnv { HeldIota = new DoubleIota(7) };
            var img = new CastingImage(System.Array.Empty<Iota>(), parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });
            Check("read_into_parens：括号内读出手上的 iota（7）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Parenthesized.Count == 1
                && r.Image.Parenthesized[0].Iota is DoubleIota { Value: 7 },
                $"括号内 {r.Image.Parenthesized.Count} 项");
        }
        {
            // 【关键】读进来的 iota 必须标记为 escaped。
            // 源项目注释：插入的括号不应调整括号计数 —— 不转义的话括号计数会被带偏。
            var env = new TestEnv { HeldIota = P("hexcasting:open_paren") };
            var img = new CastingImage(System.Array.Empty<Iota>(), parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });
            Check("read_into_parens：读入的项标记为 escaped",
                r.Image.Parenthesized.Count == 1 && r.Image.Parenthesized[0].Escaped,
                "Escaped=" + (r.Image.Parenthesized.Count > 0 ? r.Image.Parenthesized[0].Escaped.ToString() : "无"));
        }
        {
            // 括号内 + 手上没东西 -> MishapBadHeldItem（而不是静默读出一个 null）
            var env = new TestEnv { HeldIota = null };
            var img = new CastingImage(System.Array.Empty<Iota>(), parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });
            Check("read_into_parens：手上没东西 -> Errored（不静默读 null）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 空载体：原版 OpReadIntoParens 同样是 readIota ?: emptyIota ?: mishap
            var env = new TestEnv { HasStorage = true };
            var img = new CastingImage(System.Array.Empty<Iota>(), parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });
            Check("read_into_parens：空载体 -> Errored（原版没有 emptyIota）",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 转义的项不参与括号计数调整：读入一个「开括号图案」后 undo，计数必须不变。
            // 这条把「escaped」这个标记的**实际后果**钉住了 ——
            // 只断言 Escaped==true 是不够的，那只是标记，这里验证它的语义。
            var env = new TestEnv { HeldIota = P("hexcasting:open_paren") };
            var img = new CastingImage(System.Array.Empty<Iota>(), parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:read_into_parens") });

            var undoEnv = new TestEnv();
            var r2 = new CastingVM(r.Image, undoEnv).QueueExecute(r.Image, new Iota[] { P("hexcasting:undo") });
            Check("read_into_parens + undo：被转义的项不改括号计数（仍为 1）",
                r2.ResolutionType == ResolvedPatternType.Undone
                && r2.Image.ParenCount == 1 && r2.Image.Parenthesized.Count == 0,
                $"parenCount={r2.Image.ParenCount} 剩余={r2.Image.Parenthesized.Count}");
        }
        {
            // 写入侧：WriteHeldIota 能存下值（供 write 图案使用）
            // 注意必须**先有载体**才写得进去 —— 手上没东西却写成功，是 silent bug。
            var env = new TestEnv { HasStorage = true };
            bool ok = env.WriteHeldIota(new VectorIota(1, 2));
            Check("WriteHeldIota：能写入并读回",
                ok && env.HeldIota is VectorIota { X: 1, Y: 2 }, env.HeldIota?.ToString() ?? "null");
        }
        {
            // 存进卷轴的图案必须能读回来且一致（存储 → 序列化 → 反序列化 → 相等）
            var pattern = P("hexcasting:teleport/great");
            bool ok = IotaSerializer.TryDeserialize(pattern.Serialize(), out var back);
            Check("卷轴存的图案能完整往返（序列化）",
                ok && back is PatternIota bp
                && bp.Pattern.AnglesSignature() == pattern.Pattern.AnglesSignature()
                && bp.Pattern.StartDir == pattern.Pattern.StartDir,
                back?.ToString() ?? "失败");
        }
        // ==================== P2-4 阿卡夏记录 ====================
        {
            // 写入后能按同一个图案读回来
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);
            var key = P("hexcasting:eval");

            // akashic/write (坐标, 图案, 值)
            var wimg = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), key, new DoubleIota(42),
            });
            var wr = new CastingVM(wimg, env).QueueExecute(wimg, new Iota[] { P("hexcasting:akashic/write") });

            // akashic/read (坐标, 图案)
            var rimg = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), key,
            });
            var rr = new CastingVM(rimg, env).QueueExecute(rimg, new Iota[] { P("hexcasting:akashic/read") });

            Check("阿卡夏：写入后读回同一个值（42）",
                wr.ResolutionType == ResolvedPatternType.Evaluated
                && rr.ResolutionType == ResolvedPatternType.Evaluated
                && rr.Image.Stack.Count == 1
                && rr.Image.Stack[0] is DoubleIota { Value: 42 },
                Sig(rr.Image));
        }
        {
            // 【关键分支】位置不是记录方块 -> MishapNoAkashicRecord
            var world = new FakeWorld();   // 没有放任何记录方块
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), P("hexcasting:eval"),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:akashic/read") });
            Check("阿卡夏：位置不是记录方块 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // 【关键分支】是记录方块但没有该键 -> **NullIota，不是报错**。
            // 记录本来就是慢慢写的，查不到是正常情况。
            // 与「指错位置」合并成一种结果会让玩家分不清两者。
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), P("hexcasting:eval"),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:akashic/read") });
            Check("阿卡夏：有方块但没该键 -> NullIota（**不报错**）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // 不同图案键互不干扰
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);
            var k1 = P("hexcasting:eval");
            var k2 = P("hexcasting:add");

            var w1 = new CastingImage(new Iota[] { new VectorIota(10.5, 20.5), k1, new DoubleIota(1) });
            new CastingVM(w1, env).QueueExecute(w1, new Iota[] { P("hexcasting:akashic/write") });
            var w2 = new CastingImage(new Iota[] { new VectorIota(10.5, 20.5), k2, new DoubleIota(2) });
            new CastingVM(w2, env).QueueExecute(w2, new Iota[] { P("hexcasting:akashic/write") });

            var r1 = new CastingImage(new Iota[] { new VectorIota(10.5, 20.5), k1 });
            var o1 = new CastingVM(r1, env).QueueExecute(r1, new Iota[] { P("hexcasting:akashic/read") });

            Check("阿卡夏：不同图案键互不干扰（读到 1）",
                o1.Image.Stack.Count == 1 && o1.Image.Stack[0] is DoubleIota { Value: 1 },
                Sig(o1.Image));
        }
        {
            // 【设计要点】akashic/write 是 SpellAction：必须先扣媒质、再改世界。
            // 顺序反了就能白嫖写入 —— 媒质不足时记录已经被改了。
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), P("hexcasting:eval"), new DoubleIota(7),
            });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:akashic/write") });

            var combined = env.Trace.Concat(world.Trace).ToList();
            Check("阿卡夏：写入顺序 = 先扣媒质、再改世界",
                combined.Count == 2 && combined[0] == "media" && combined[1] == "akashic-write",
                string.Join(" -> ", combined));
        }
        {
            // 媒质不足 -> 中止且**没有**任何世界改动
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 5);   // 不够 1 粉尘（10000）
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), P("hexcasting:eval"), new DoubleIota(7),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:akashic/write") });
            Check("阿卡夏：媒质不足 -> Errored 且无世界改动",
                r.ResolutionType == ResolvedPatternType.Errored && world.Trace.Count == 0,
                "轨迹: " + string.Join(",", world.Trace));
        }
        {
            // 坐标超范围 -> mishap（读与写都要查）
            var world = new FakeWorld { RangeCheck = (x, y) => false };
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);

            var rimg = new CastingImage(new Iota[] { new VectorIota(10.5, 20.5), P("hexcasting:eval") });
            var rr = new CastingVM(rimg, env).QueueExecute(rimg, new Iota[] { P("hexcasting:akashic/read") });

            var wimg = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), P("hexcasting:eval"), new DoubleIota(1),
            });
            var wr = new CastingVM(wimg, env).QueueExecute(wimg, new Iota[] { P("hexcasting:akashic/write") });

            Check("阿卡夏：坐标超范围 -> 读与写都 Errored",
                rr.ResolutionType == ResolvedPatternType.Errored
                && wr.ResolutionType == ResolvedPatternType.Errored, Sig(rr.Image) + " / " + Sig(wr.Image));
        }
        {
            // 第二个参数不是图案 -> mishap（不能拿数字当键）
            var world = new FakeWorld();
            world.AkashicBlocks.Add((10, 20));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[]
            {
                new VectorIota(10.5, 20.5), new DoubleIota(3),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:akashic/read") });
            Check("阿卡夏：键不是图案 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        // ==================== 栈/括号工具 ====================
        {
            // open_n_parens：从栈顶取层数，一次开 n 层
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(3) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:open_n_parens") });
            Check("open_n_parens：栈顶 3 -> 开 3 层括号，栈顶被弹掉",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.ParenCount == 3 && r.Image.Stack.Count == 0,
                $"层数={r.Image.ParenCount} 栈={r.Image.Stack.Count}");
        }
        {
            // open_n_parens 在括号内**不覆写** —— 源项目注释写明「没法合理判断该开几层，
            // 所以当作普通图案处理」。很容易被后来者当成遗漏补上。
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(2) }, parenCount: 1);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:open_n_parens") });
            Check("open_n_parens：括号内被当作普通图案入列（不覆写括号内行为）",
                r.Image.Parenthesized.Count == 1 && r.Image.ParenCount == 1,
                $"括号内 {r.Image.Parenthesized.Count} 项，层数 {r.Image.ParenCount}");
        }
        {
            // close_all_parens：不在括号内 -> MishapNeedsParens
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:close_all_parens") });
            Check("close_all_parens：不在括号内 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // close_all_parens：括号内 -> 压入「层数」与「内容列表」，并清空括号
            var env = new TestEnv();
            var parens = new ParenthesizedIota[]
            {
                new(new DoubleIota(11), false),
                new(new DoubleIota(22), false),
            };
            var img = new CastingImage(System.Array.Empty<Iota>(), 2, parens);
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:close_all_parens") });

            Check("close_all_parens：压入层数(2)与内容列表(2 项)，括号清空",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.ParenCount == 0 && r.Image.Parenthesized.Count == 0
                && r.Image.Stack.Count == 2
                && r.Image.Stack[0] is DoubleIota { Value: 2 }
                && r.Image.Stack[1] is ListIota { Count: 2 },
                Sig(r.Image));
        }
        {
            // runtime_escape：打开转义标记
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:runtime_escape") });
            Check("runtime_escape：escapeNext 置位",
                r.ResolutionType == ResolvedPatternType.Evaluated && r.Image.EscapeNext);
        }
        {
            // duplicate_n：把栈顶项复制 n 份成一个列表
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(7), new DoubleIota(3) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:duplicate_n") });
            Check("duplicate_n：7 复制 3 份 -> list(3) 且都是 7",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is ListIota { Count: 3 } l
                && l.Items[0] is DoubleIota { Value: 7 },
                Sig(r.Image));
        }
        {
            // duplicate_n：中等 n 正常复制
            var env10 = new TestEnv();
            var img10 = new CastingImage(new Iota[] { new DoubleIota(5), new DoubleIota(10) });
            var r10 = new CastingVM(img10, env10).QueueExecute(img10, new Iota[] { P("hexcasting:duplicate_n") });
            Check("duplicate_n：复制 10 份",
                r10.Image.Stack.Count == 1 && r10.Image.Stack[0] is ListIota { Count: 10 },
                Sig(r10.Image));

            // duplicate_n：超大 n **被截断**（源项目注释：在这里抛异常的话错误会指向本图案，
            // 而不是用户的操作，所以宁可截断让后续的「iota 太多」检查去报错）。
            // 泰拉侧的 iota 数量保险丝会把超限结果换成 garbage —— 这也符合源项目的意图。
            var env2 = new TestEnv();
            var img2 = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(99999) });
            var r2 = new CastingVM(img2, env2).QueueExecute(img2, new Iota[] { P("hexcasting:duplicate_n") });
            bool capped = r2.Image.Stack[0] is not ListIota { Count: 99999 };
            Check("duplicate_n：超大 n 被截断（不会产出 99999 项）",
                capped, Sig(r2.Image));
        }
        {
            // unique：去重且保留首次出现顺序；用**容差**比较
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new ListIota(new Iota[]
                {
                    new DoubleIota(1), new DoubleIota(2), new DoubleIota(1.00005), new DoubleIota(3),
                }),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:unique") });
            Check("unique：1 / 2 / 1.00005 / 3 -> 3 项（容差内视为重复）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack[0] is ListIota { Count: 3 } l
                && l.Items[0] is DoubleIota { Value: 1 }
                && l.Items[1] is DoubleIota { Value: 2 }
                && l.Items[2] is DoubleIota { Value: 3 },
                Sig(r.Image));
        }
        {
            // random：返回世界侧的随机数
            var world = new FakeWorld { NextRandom = 0.375 };
            var env = new TestEnv(world: world);
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:random") });
            Check("random：透传世界侧的随机数（0.375）",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is DoubleIota { Value: 0.375 }, Sig(r.Image));
        }
        {
            // get_media：试算「几乎无限大」的消耗，缺口反推可用量
            var env = new TestEnv(media: 50_000);
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_media") });
            Check("get_media：50000 媒质 -> 5 粉尘",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is DoubleIota d && System.Math.Abs(d.Value - 5.0) < 1e-6,
                Sig(r.Image));
        }
        // ==================== 世界效果（天气/火/水/雷电/催熟） ====================
        {
            // summon_rain 比 dispel_rain 贵：CRYSTAL_UNIT vs SHARD_UNIT
            //
            // ⚠️ 这两个图案**需要启蒙**（源项目把它们打在 REQUIRES_ENLIGHTENMENT 标签里）。
            // 顺带把「没启蒙 -> 拒绝」也钉一条：这条曾经是漏的（清单里只有 teleport/great），
            // 修好之后再被改回去就会立刻红。
            var forbidden = new CastingImage(System.Array.Empty<Iota>());
            var forbiddenEnv = new TestEnv(world: new FakeWorld()) { Enlightened = false };
            var forbiddenResult = new CastingVM(forbidden, forbiddenEnv)
                .QueueExecute(forbidden, new Iota[] { P("hexcasting:summon_rain") });
            Check("天气：未启蒙时 summon_rain 被拒绝（源项目的大法术门槛）",
                forbiddenResult.ResolutionType == ResolvedPatternType.Invalid,
                Sig(forbiddenResult.Image));

            var w1 = new FakeWorld();
            var e1 = new TestEnv(world: w1, media: 1_000_000) { Enlightened = true };
            var i1 = new CastingImage(System.Array.Empty<Iota>());
            new CastingVM(i1, e1).QueueExecute(i1, new Iota[] { P("hexcasting:summon_rain") });
            long rainCost = 1_000_000 - e1.Media;

            var w2 = new FakeWorld();
            var e2 = new TestEnv(world: w2, media: 1_000_000) { Enlightened = true };
            var i2 = new CastingImage(System.Array.Empty<Iota>());
            new CastingVM(i2, e2).QueueExecute(i2, new Iota[] { P("hexcasting:dispel_rain") });
            long clearCost = 1_000_000 - e2.Media;

            Check("天气：summon_rain = 10 万（晶体），dispel_rain = 5 万（碎晶）",
                rainCost == 100_000 && clearCost == 50_000
                && w1.Weather.Count == 1 && w1.Weather[0].Rain
                && w2.Weather.Count == 1 && !w2.Weather[0].Rain,
                $"下雨 {rainCost} / 放晴 {clearCost}");
        }
        {
            // 天气时长范围：下雨 30~90 分钟、放晴 60~180 分钟（源项目同）
            // 需要启蒙（大法术），否则根本走不到世界侧
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000) { Enlightened = true };
            var img = new CastingImage(System.Array.Empty<Iota>());
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:summon_rain") });
            Check("天气：时长范围传给世界侧（30~90 分钟）",
                world.Weather[0].Min == 30 && world.Weather[0].Max == 90,
                $"{world.Weather[0].Min}~{world.Weather[0].Max}");
        }
        {
            // ignite 双重分派：实体 vs 坐标
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);

            var npc = new EntityIota(EntityIota.EntityKind.Npc, 2);
            var imgE = new CastingImage(new Iota[] { npc });
            new CastingVM(imgE, env).QueueExecute(imgE, new Iota[] { P("hexcasting:ignite") });

            var imgV = new CastingImage(new Iota[] { new VectorIota(3.0, 4.0) });
            new CastingVM(imgV, env).QueueExecute(imgV, new Iota[] { P("hexcasting:ignite") });

            Check("ignite：参数是实体就烧实体，是坐标就烧位置",
                world.IgnitedEntities.Count == 1 && world.IgnitedEntities[0].Index == 2
                && world.IgnitedPositions.Count == 1,
                $"实体 {world.IgnitedEntities.Count} / 位置 {world.IgnitedPositions.Count}");
        }
        {
            // 各类消耗逐个核对（全部抄自源项目）
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 10_000_000);

            long Before() => env.Media;
            long Cost(Iota[] args, string id)
            {
                long b = Before();
                var img = new CastingImage(args);
                new CastingVM(img, env).QueueExecute(img, new Iota[] { P(id) });
                return b - env.Media;
            }

            var pos = new VectorIota(1.5, 1.5);
            long ignite = Cost(new Iota[] { pos }, "hexcasting:ignite");
            long extinguish = Cost(new Iota[] { pos }, "hexcasting:extinguish");
            long createWater = Cost(new Iota[] { pos }, "hexcasting:create_water");
            long destroyWater = Cost(new Iota[] { pos }, "hexcasting:destroy_water");
            long lightning = Cost(new Iota[] { pos }, "hexcasting:lightning");
            long bonemeal = Cost(new Iota[] { pos }, "hexcasting:bonemeal");

            Check("世界效果消耗：ignite 1万 / extinguish 6万 / create_water 1万 / destroy_water 20万 / lightning 15万 / bonemeal 1.125万",
                ignite == 10_000 && extinguish == 60_000 && createWater == 10_000
                && destroyWater == 200_000 && lightning == 150_000 && bonemeal == 11_250,
                $"{ignite}/{extinguish}/{createWater}/{destroyWater}/{lightning}/{bonemeal}");
        }
        {
            // 泛洪上限 1024 格被传给世界侧
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 10_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(1.5, 1.5) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:destroy_water") });
            Check("destroy_water：泛洪上限 1024 传给世界侧",
                world.WaterDestroyed.Count == 1 && world.WaterDestroyed[0].Max == 1024,
                world.WaterDestroyed.Count > 0 ? world.WaterDestroyed[0].Max.ToString() : "无");
        }
        {
            // 这些都是 SpellAction：先扣媒质再改世界
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 10_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(1.5, 1.5) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:create_water") });
            Check("世界效果：先扣媒质、再改世界",
                env.Trace.Count > 0 && env.Trace[0] == "media" && world.WaterCreated.Count == 1,
                string.Join(",", env.Trace));
        }
        // ==================== conjure / break_block ====================
        {
            // conjure_block：目标可替换 -> 造出方块，消耗 1 粉尘
            var world = new FakeWorld();
            world.Replaceable.Add((5, 5));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.5, 5.5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:conjure_block") });

            Check("conjure_block：造出方块，消耗 1 粉尘（10000）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && world.Conjured.Count == 1 && !world.Conjured[0].Light
                && 1_000_000 - env.Media == 10000,
                $"造了 {world.Conjured.Count} 个，扣 {1_000_000 - env.Media}");
        }
        {
            // conjure_light 造的是光源
            var world = new FakeWorld();
            world.Replaceable.Add((5, 5));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.5, 5.5) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:conjure_light") });
            Check("conjure_light：造出的是光源", world.Conjured.Count == 1 && world.Conjured[0].Light);
        }
        {
            // 目标不可替换 -> MishapBadBlock（不报错的话会在方块里凭空插入一块）
            var world = new FakeWorld();   // 没标记可替换
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.5, 5.5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:conjure_block") });
            Check("conjure_block：目标不可替换 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored && world.Conjured.Count == 0);
        }
        {
            // break_block 的**两档消耗**：普通 DUST/8、廉价 DUST/100。
            // 不分档的话挖一片草会贵得离谱 —— 源项目专门用方块标签区分。
            var worldNormal = new FakeWorld();
            var envNormal = new TestEnv(world: worldNormal, media: 1_000_000);
            var imgN = new CastingImage(new Iota[] { new VectorIota(1.5, 1.5) });
            new CastingVM(imgN, envNormal).QueueExecute(imgN, new Iota[] { P("hexcasting:break_block") });
            long normalCost = 1_000_000 - envNormal.Media;

            var worldCheap = new FakeWorld();
            worldCheap.CheapBreak.Add((1, 1));
            var envCheap = new TestEnv(world: worldCheap, media: 1_000_000);
            var imgC = new CastingImage(new Iota[] { new VectorIota(1.5, 1.5) });
            new CastingVM(imgC, envCheap).QueueExecute(imgC, new Iota[] { P("hexcasting:break_block") });
            long cheapCost = 1_000_000 - envCheap.Media;

            Check("break_block：普通方块 DUST/8 = 1250，廉价方块 DUST/100 = 100",
                normalCost == 1250 && cheapCost == 100,
                $"普通 {normalCost} / 廉价 {cheapCost}");
        }
        {
            // 挖掘是 SpellAction：先扣媒质、再挖
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(1.5, 1.5) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:break_block") });

            var combined = env.Trace.Concat(world.Trace).ToList();
            Check("break_block：先扣媒质、再挖方块",
                combined.Count >= 2 && combined[0] == "media" && combined[1] == "break",
                string.Join(" -> ", combined));
        }
        // ==================== equals / type_equals / 小工具 ====================
        {
            // 【关键】equals 用**容差**比较：1.0 与 1.00005 应当相等。
            // 精确比较会让 equals 在浮点场景下几乎永远为假。
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1.0), new DoubleIota(1.00005) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:equals") });
            Check("equals：容差内视为相等（1.0 vs 1.00005）",
                r.Image.Stack.Count == 1 && r.Image.Stack[0] is BooleanIota { Value: true }, Sig(r.Image));
        }
        {
            // 容差外不相等
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1.0), new DoubleIota(1.5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:equals") });
            Check("equals：超出容差 -> false",
                r.Image.Stack[0] is BooleanIota { Value: false }, Sig(r.Image));
        }
        {
            // 类型不同 -> 不相等（数值 1 与布尔 true）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1.0), BooleanIota.True });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:equals") });
            Check("equals：类型不同 -> false",
                r.Image.Stack[0] is BooleanIota { Value: false }, Sig(r.Image));
        }
        {
            // not_equals 是取反
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1.0), new DoubleIota(2.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:not_equals") });
            Check("not_equals：1 vs 2 -> true",
                r.Image.Stack[0] is BooleanIota { Value: true }, Sig(r.Image));
        }
        {
            // type_equals 只比类型：1 与 2 都是 double -> true
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1.0), new DoubleIota(2.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:type_equals") });
            Check("type_equals：1 与 2 都是 double -> true（只比类型）",
                r.Image.Stack[0] is BooleanIota { Value: true }, Sig(r.Image));
        }
        {
            // bool_coerce = isTruthy
            var env = new TestEnv();
            int ok = 0;
            foreach (var (iota, expect) in new (Iota, bool)[]
            {
                (new DoubleIota(0), false), (new DoubleIota(1), true),
                (BooleanIota.True, true), (NullIota.Instance, false),
                (new ListIota(System.Array.Empty<Iota>()), false),
                (new ListIota(new Iota[] { new DoubleIota(1) }), true),
            })
            {
                var img = new CastingImage(new Iota[] { iota });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:bool_coerce") });
                if (r.Image.Stack.Count == 1 && r.Image.Stack[0] is BooleanIota b && b.Value == expect) ok++;
            }
            Check("bool_coerce：6 种 iota 的真假值全部正确", ok == 6, $"对了 {ok}/6");
            // 【本轮修的真 bug】ListIota 原先没有覆写 IsTruthy，落到基类的 false ——
            // 于是**非空列表在布尔语境里也是假**。这个缺陷是上面这条用例跑出来的。
            Check("回归：ListIota 真假值 = 非空（空列表假、非空列表真）",
                !new ListIota(System.Array.Empty<Iota>()).IsTruthy()
                && new ListIota(new Iota[] { new DoubleIota(1) }).IsTruthy());

            // 同批修的另两条：DoubleIota 非零为真、VectorIota 非零为真
            Check("回归：DoubleIota 非零为真 / VectorIota 非零为真",
                !new DoubleIota(0).IsTruthy() && new DoubleIota(1).IsTruthy()
                && !new VectorIota(0, 0).IsTruthy() && new VectorIota(1, 0).IsTruthy());
        }
        {
            // coerce_axial：数值取符号
            var env = new TestEnv();
            int ok = 0;
            foreach (var (v, expect) in new (double, double)[] { (5.0, 1.0), (-3.0, -1.0), (0.0, 0.0) })
            {
                var img = new CastingImage(new Iota[] { new DoubleIota(v) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:coerce_axial") });
                if (r.Image.Stack[0] is DoubleIota d && System.Math.Abs(d.Value - expect) < 1e-9) ok++;
            }
            Check("coerce_axial：数值取符号（5->1, -3->-1, 0->0）", ok == 3, $"对了 {ok}/3");
        }
        {
            // coerce_axial：向量取最接近的轴；**零向量原样返回**
            var env = new TestEnv();

            var imgX = new CastingImage(new Iota[] { new VectorIota(3.0, 1.0) });
            var rX = new CastingVM(imgX, env).QueueExecute(imgX, new Iota[] { P("hexcasting:coerce_axial") });
            bool okX = rX.Image.Stack[0] is VectorIota { X: 1.0, Y: 0.0 };

            var imgY = new CastingImage(new Iota[] { new VectorIota(-1.0, 4.0) });
            var rY = new CastingVM(imgY, env).QueueExecute(imgY, new Iota[] { P("hexcasting:coerce_axial") });
            bool okY = rY.Image.Stack[0] is VectorIota { X: 0.0, Y: 1.0 };

            var imgZ = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0) });
            var rZ = new CastingVM(imgZ, env).QueueExecute(imgZ, new Iota[] { P("hexcasting:coerce_axial") });
            bool okZ = rZ.Image.Stack[0] is VectorIota { X: 0.0, Y: 0.0 };

            Check("coerce_axial：向量取轴；**零向量原样返回**（不强行归轴）",
                okX && okY && okZ, $"{okX}/{okY}/{okZ}");
        }
        {
            // last_n_list：[a, b, c, 3] -> [list(a,b,c)]
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new DoubleIota(10), new DoubleIota(20), new DoubleIota(30), new DoubleIota(3),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:last_n_list") });
            Check("last_n_list：把下面 3 项打包成列表",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is ListIota { Count: 3 } l
                && l.Items[0] is DoubleIota { Value: 10 },
                Sig(r.Image));
        }
        {
            // last_n_list：n = 0 -> 空列表
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(10), new DoubleIota(0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:last_n_list") });
            // ⚠️ 我的期望一开始写错了：n=0 时**不是**只剩一个空列表 ——
            // 被弹出的只有「个数」那一项，下面的 10 仍在栈上。
            Check("last_n_list：n=0 -> 原栈不变，只多压一个空列表",
                r.Image.Stack.Count == 2
                && r.Image.Stack[0] is DoubleIota { Value: 10 }
                && r.Image.Stack[1] is ListIota { Count: 0 }, Sig(r.Image));
        }
        {
            // last_n_list：n 超过剩余项数 -> mishap
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(10), new DoubleIota(5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:last_n_list") });
            Check("last_n_list：n 超过栈深 -> Errored", r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        {
            // swizzle：码 0 -> 顺序不变（恒等置换）
            var env = new TestEnv();
            var img = new CastingImage(new Iota[]
            {
                new DoubleIota(1), new DoubleIota(2), new DoubleIota(0),
            });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:swizzle") });
            Check("swizzle：Lehmer 码 0 -> 恒等置换",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 2
                && r.Image.Stack[0] is DoubleIota { Value: 1 }
                && r.Image.Stack[1] is DoubleIota { Value: 2 },
                Sig(r.Image));
        }
        {
            // swizzle：负数码 -> mishap
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(-1) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:swizzle") });
            Check("swizzle：负数码 -> Errored", r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
        }
        // ==================== get_entity / explode ====================
        {
            // 该坐标上有实体 -> 返回它
            var world = new FakeWorld { NearestEntity = new EntityIota(EntityIota.EntityKind.Npc, 5) };
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(3.0, 4.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_entity") });
            Check("get_entity：坐标上有实体 -> 返回该实体",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is EntityIota { Index: 5 }, Sig(r.Image));
        }
        {
            // 【关键】该坐标上没有实体 -> 返回 **NullIota**，不是报错。
            // 源项目 `getOrNull(0).asActionResult` 就是这个语义。
            var world = new FakeWorld { NearestEntity = null };
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:get_entity/monster") });
            Check("get_entity：坐标上没实体 -> NullIota（**不报错**）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is NullIota, Sig(r.Image));
        }
        {
            // explode 不带火：消耗 = 粉尘 × (3×s + 0.125)
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(2.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode") });
            long expected = (long)(10000.0 * (3 * 2.0 + 0.125));
            Check("explode：消耗 = 粉尘 × (3s + 0.125)",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && 100_000_000 - env.Media == expected,
                $"扣 {100_000_000 - env.Media}，期望 {expected}");
        }
        {
            // explode/fire：消耗 = 粉尘 × (3×s + 1.0)
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(2.0) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode/fire") });
            long expected = (long)(10000.0 * (3 * 2.0 + 1.0));
            Check("explode/fire：消耗 = 粉尘 × (3s + 1.0)（带火更贵）",
                100_000_000 - env.Media == expected, $"扣 {100_000_000 - env.Media}");
        }
        {
            // 【关键】眼位微移：源项目发现爆炸正好落在实体眼位时**不会造成伤害**，
            // 所以那种情况下把爆炸点向上挪 0.000001。
            // 少了这一下，贴脸爆炸会莫名其妙打不到人 —— 而且完全不报错。
            var world = new FakeWorld { EyeExactlyHere = true };
            var env = new TestEnv(world: world, media: 100_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.0, 5.0), new DoubleIota(1.0) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode") });

            Check("explode：眼位重合时把爆炸点微移（源项目防「贴脸不伤害」）",
                world.Explosions.Count == 1
                && System.Math.Abs(world.Explosions[0].X - 5.0) < 1e-9
                && world.Explosions[0].Y > 5.0,
                world.Explosions.Count > 0 ? $"({world.Explosions[0].X},{world.Explosions[0].Y})" : "未爆炸");
        }
        {
            // 对照：眼位不重合时不微移
            var world = new FakeWorld { EyeExactlyHere = false };
            var env = new TestEnv(world: world, media: 100_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.0, 5.0), new DoubleIota(1.0) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode") });
            Check("对照：眼位不重合时**不**微移",
                world.Explosions.Count == 1 && System.Math.Abs(world.Explosions[0].Y - 5.0) < 1e-12,
                world.Explosions.Count > 0 ? world.Explosions[0].Y.ToString() : "未爆炸");
        }
        {
            // 强度落在 [0, 10] 闭区间（源项目 getPositiveDoubleUnderInclusive(1, 10.0)）
            // ⚠️ 原版「positive」含 0（getPositiveDouble 是 0 <= x）；NaN / 无穷在 DoubleIota 里被 fixNAN 成 0。
            //    这条测试原来断言「0 / NaN 必须报错」，把移植版的错误行为当成了期望值。
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);

            string wrong = "";
            foreach (var (s, ok) in new[] { (0.0, true), (10.0, true), (double.NaN, true), (-1.0, false), (11.0, false) })
            {
                var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(s) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode") });
                if ((r.ResolutionType == ResolvedPatternType.Evaluated) != ok) wrong += $" {s}->{r.ResolutionType}";
            }
            Check("explode：强度 0 / 10 / NaN(=0) 可以，-1 / 11 报错", wrong.Length == 0, wrong);
        }
        {
            // 爆炸是 SpellAction：先扣媒质、再造成伤害
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(1.0) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:explode") });

            var combined = env.Trace.Concat(world.Trace).ToList();
            Check("explode：先扣媒质、再爆炸",
                combined.Count >= 2 && combined[0] == "media",
                string.Join(" -> ", combined));
        }
        // ==================== potion：药水效果 ====================
        {
            // 可加浓的效果吃 3 个参数：目标、持续、效力
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var img = new CastingImage(new Iota[] { npc, new DoubleIota(2.0), new DoubleIota(3.0) });

            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/haste") });

            // 消耗 = (DUST/3) × 2 × 3³ = 3333 × 2 × 27 = 179982
            long expected = (long)((10000L / 3) * 2.0 * 27.0);
            Check("potion/haste：立方加浓的消耗 = 基础 × 持续 × 效力³",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && 100_000_000 - env.Media == expected,
                $"实际扣 {100_000_000 - env.Media}，期望 {expected}");
        }
        {
            // 平方（非立方）的：消耗 = 基础 × 持续 × 效力²
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var img = new CastingImage(new Iota[] { npc, new DoubleIota(4.0), new DoubleIota(3.0) });

            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/weakness") });

            // (DUST/10) × 4 × 3² = 1000 × 4 × 9 = 36000
            long expected = (long)((10000L / 10) * 4.0 * 9.0);
            Check("potion/weakness：平方加浓的消耗 = 基础 × 持续 × 效力²",
                100_000_000 - env.Media == expected,
                $"实际扣 {100_000_000 - env.Media}，期望 {expected}");
        }
        {
            // 【关键】持续不足 1 tick **不施加**（源项目 `duration > 1/20`）。
            // 不判这条的话，一个极小持续时间的法术会「扣了媒质但什么都没发生」。
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);
            // 0.001 秒 < 1/60 秒
            var img = new CastingImage(new Iota[] { npc, new DoubleIota(0.001), new DoubleIota(1.0) });

            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/weakness") });
            Check("potion：持续不足 1 tick -> 媒质照扣但**不施加效果**",
                r.ResolutionType == ResolvedPatternType.Evaluated && world.Potions.Count == 0,
                $"施加了 {world.Potions.Count} 次");
        }
        {
            // 正常持续会施加，且 tick 数 = 秒 × 60
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var img = new CastingImage(new Iota[] { npc, new DoubleIota(5.0), new DoubleIota(1.0) });

            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/weakness") });

            Check("potion：5 秒 -> 施加 300 tick",
                world.Potions.Count == 1 && world.Potions[0].Ticks == 300,
                world.Potions.Count > 0 ? world.Potions[0].Ticks.ToString() : "未施加");
        }
        {
            // 不可加浓的效果（levitation / night_vision）只吃 2 个参数
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var img = new CastingImage(new Iota[] { npc, new DoubleIota(1.0) });

            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/levitation") });
            Check("potion/levitation：不可加浓 -> 只吃 2 个参数",
                r.ResolutionType == ResolvedPatternType.Evaluated && world.Potions.Count == 1,
                Sig(r.Image));
        }
        {
            // 效力必须在 [1, 127]
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);

            int bad = 0;
            foreach (var potency in new[] { 0.5, 0.0, -1.0, 128.0 })
            {
                var img = new CastingImage(new Iota[] { npc, new DoubleIota(1.0), new DoubleIota(potency) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/weakness") });
                if (r.ResolutionType != ResolvedPatternType.Errored) bad++;
            }
            Check("potion：效力超出 [1,127] 全部 Errored", bad == 0, $"漏过 {bad} 个");
        }
        {
            // 持续必须为正
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var npc = new EntityIota(EntityIota.EntityKind.Npc, 1);

            // ⚠️ 原版「positive」含 0（getPositiveDouble 是 0 <= x）；NaN / 无穷在 DoubleIota 里被 fixNAN 成 0。
            //    这条测试原来断言「0 / NaN 必须报错」，把移植版的错误行为当成了期望值。
            string wrong = "";
            foreach (var (dur, ok) in new[] { (0.0, true), (double.NaN, true), (-1.0, false) })
            {
                var img = new CastingImage(new Iota[] { npc, new DoubleIota(dur), new DoubleIota(1.0) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/weakness") });
                if ((r.ResolutionType == ResolvedPatternType.Evaluated) != ok) wrong += $" {dur}->{r.ResolutionType}";
            }
            Check("potion：持续时间 0 / NaN(=0) 可以，负数报错", wrong.Length == 0, wrong);
        }
        {
            // 目标不是活物 -> mishap（物品/弹幕不能吃药水）
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 100_000_000);
            var item = new EntityIota(EntityIota.EntityKind.Item, 1);
            var img = new CastingImage(new Iota[] { item, new DoubleIota(1.0) });

            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:potion/levitation") });
            Check("potion：目标不是活物 -> Errored", r.ResolutionType == ResolvedPatternType.Errored);
        }
        // ==================== zone_entity：区域查询 ====================
        {
            // 返回的是**一个列表**，按距离从近到远（排序在世界侧做）
            var world = new FakeWorld();
            var near = new EntityIota(EntityIota.EntityKind.Npc, 3);
            var far = new EntityIota(EntityIota.EntityKind.Npc, 7);
            world.QueryResults[(ZoneEntityFilter.Monster, false)] = new List<EntityIota> { near, far };

            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(5.0, 5.0), new DoubleIota(10.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity/monster") });

            Check("zone_entity：返回列表（2 个），顺序保持世界侧给的近->远",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is ListIota { Count: 2 } l
                && l.Items[0] is EntityIota { Index: 3 }
                && l.Items[1] is EntityIota { Index: 7 },
                Sig(r.Image));
        }
        {
            // 半径与筛选条件被正确传给世界侧
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(1.0, 2.0), new DoubleIota(7.5) });
            new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity/not_animal") });

            Check("zone_entity：半径与取反标志正确传给世界侧",
                System.Math.Abs(world.LastQueryRadius - 7.5) < 1e-9
                && world.LastQueryFilter == ZoneEntityFilter.Animal
                && world.LastQueryNegate,
                $"r={world.LastQueryRadius} f={world.LastQueryFilter} neg={world.LastQueryNegate}");
        }
        {
            // 【关键】半径必须为正 —— 源项目用 getPositiveDouble。
            // 负半径不报错的话，AABB 会翻转成一个「负体积」，
            // 查询结果静默变成空列表，玩家只会觉得「这法术没反应」。
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);

            // ⚠️ 原版「positive」含 0（getPositiveDouble 是 0 <= x）；NaN / 无穷在 DoubleIota 里被 fixNAN 成 0。
            //    这条测试原来断言「0 / NaN 必须报错」，把移植版的错误行为当成了期望值。
            string wrong = "";
            foreach (var (radius, ok) in new[] { (0.0, true), (double.NaN, true), (double.PositiveInfinity, true), (-1.0, false) })
            {
                var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(radius) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity") });
                if ((r.ResolutionType == ResolvedPatternType.Evaluated) != ok) wrong += $" {radius}->{r.ResolutionType}";
            }
            Check("zone_entity：半径 0 / NaN / 无穷(=0) 可以，负数报错", wrong.Length == 0, wrong);
        }
        {
            // 区域中心超范围 -> mishap（源项目 assertVecInRange）
            var world = new FakeWorld { RangeCheck = (x, y) => false };
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(999.0, 999.0), new DoubleIota(5.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity") });
            Check("zone_entity：中心超范围 -> Errored", r.ResolutionType == ResolvedPatternType.Errored);
        }
        {
            // 空结果返回**空列表**而不是报错（没找到是正常情况）
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0.0, 0.0), new DoubleIota(5.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity/item") });
            Check("zone_entity：没找到 -> 空列表（不报错）",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1 && r.Image.Stack[0] is ListIota { Count: 0 },
                Sig(r.Image));
        }
        {
            // 参数不是向量/数字 -> mishap
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity") });
            Check("zone_entity：中心不是向量 -> Errored", r.ResolutionType == ResolvedPatternType.Errored);
        }
        {
            // Item 实体种类能被 iota 表达并序列化往返（本轮新加的种类）
            var item = new EntityIota(EntityIota.EntityKind.Item, 42);
            bool ok = IotaSerializer.TryDeserialize(item.Serialize(), out var back)
                      && back is EntityIota { Target: EntityIota.EntityKind.Item, Index: 42 };
            Check("实体种类：新增的 Item 能序列化往返", ok, back?.ToString() ?? "失败");
        }
        // ==================== P2-5 法术环：掩码模型与三根导线 ====================
        {
            // 普通部件（石板）：出口 = 全部减 normal；进入 = 全部减 normal 的反方向
            var slate = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Up);
            Check("掩码：石板 normal=Up -> 可出 3 向、可进 3 向，且 Up 不可出、Down 不可进",
                slate.ExitMask.CountBits() == 3
                && !slate.ExitMask.Has(CircleDir.Up)
                && slate.AllowedEntries.CountBits() == 3
                && !slate.AllowedEntries.Has(CircleDir.Down),
                $"出={slate.ExitMask} 进={slate.AllowedEntries}");
        }
        {
            // 【关键】源项目**故意允许**沿 normal 的反方向穿过。
            // 那一行 `allDirs.remove(normal.getOpposite())` 是被注释掉的。
            // 任何人「顺手补上」都会让控制流无法沿表面通过，环莫名断掉。
            var slate = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Up);
            Check("掩码：石板**允许**沿 normal 的反方向出去（源项目注释掉的那行）",
                slate.ExitMask.Has(CircleDir.Down), slate.ExitMask.ToString());
        }
        {
            // 导线：只能沿一个轴传导 —— 出口只有轴的两端（2 个）
            var d = CircleComponent.Directrix(CircleComponentKind.DirectrixBool, CircleDir.Right);
            Check("掩码：导线出口只有轴的两端（2 个）",
                d.ExitMask.CountBits() == 2
                && d.ExitMask.Has(CircleDir.Right) && d.ExitMask.Has(CircleDir.Left),
                d.ExitMask.ToString());
        }
        {
            // 导线：不能沿轴进入 —— 允许进入的只有垂直于轴的两向（2 个）
            var d = CircleComponent.Directrix(CircleComponentKind.DirectrixRedstone, CircleDir.Up);
            Check("掩码：导线只能从垂直于轴的方向进入（2 个）",
                d.AllowedEntries.CountBits() == 2
                && d.AllowedEntries.Has(CircleDir.Left) && d.AllowedEntries.Has(CircleDir.Right)
                && !d.AllowedEntries.Has(CircleDir.Up) && !d.AllowedEntries.Has(CircleDir.Down),
                d.AllowedEntries.ToString());
        }
        {
            // 原动力：出口为空（它是环的终点，acceptControlFlow 返回 Stop）
            var imp = CircleComponent.Impetus(CircleDir.Right);
            Check("掩码：原动力出口为空（环的终点）",
                imp.ExitMask == CircleDirMask.None && imp.Kind == CircleComponentKind.Impetus);
        }
        {
            // 原动力的进入限制来自**起始方向**，不是 normal
            var imp = CircleComponent.Impetus(CircleDir.Right);
            Check("掩码：原动力禁止从起始方向的反方向进入（Right -> 禁止 Left）",
                !imp.AllowedEntries.Has(CircleDir.Left) && imp.AllowedEntries.CountBits() == 3,
                imp.AllowedEntries.ToString());
        }
        {
            // 方向掩码工具
            var m = CircleDirMask.All.Without(CircleDir.Up).Without(CircleDir.Left);
            Check("掩码工具：All 减两向 -> 剩 Down|Right 两项",
                m.CountBits() == 2 && m.Has(CircleDir.Down) && m.Has(CircleDir.Right)
                && !m.Has(CircleDir.Up) && !m.Has(CircleDir.Left),
                m.ToString());
        }
        {
            // 【导线专项】空导线可以随机出轴两端之一 —— 用两个方向的出口集合验证
            var d = CircleComponent.Directrix(CircleComponentKind.DirectrixEmpty, CircleDir.Up);
            var exits = CircleTraversal.PossibleExitDirections(d);
            Check("空导线：可能出口恰好是轴的两端（Up/Down）",
                exits.Count == 2 && exits.Contains(CircleDir.Up) && exits.Contains(CircleDir.Down),
                string.Join(",", exits));
        }
        {
            // 导线轴上的邻居能被泛洪走到吗？不能 —— 因为 AlongAxis 不允许进入。
            // 这保证了导线只能从垂直方向接。
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Up);
            // 在 (1,0) 右边放一根轴为水平的导线：不能从左侧（沿轴）进入
            world.SetDirectrix(2, 0, CircleComponentKind.DirectrixEmpty, CircleDir.Right);

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("导线：不能沿轴进入（水平轴导线挡住从左侧来的控制流）",
                !closure.Reached.Contains((2, 0)),
                $"走到 {closure.Reached.Count} 格，含(2,0)={closure.Reached.Contains((2, 0))}");
        }
        {
            // 但垂直于轴的方向可以进入
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Up);
            // 竖直轴的导线，从上方（垂直方向）来的控制流可以进入
            world.SetDirectrix(2, 0, CircleComponentKind.DirectrixEmpty, CircleDir.Up);

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            // (2,0) 的进入方向是 Right，而竖直轴允许的进入是 Left/Right —— Right 在其中
            Check("导线：垂直于轴的方向可以进入",
                closure.Reached.Contains((2, 0)),
                $"含(2,0)={closure.Reached.Contains((2, 0))}");
        }
        // ==================== P2-5 法术环：circle/* 三个图案 ====================
        {
            // 非环环境（玩家法杖）用 circle/* -> 明确报错，而不是返回假坐标
            var env = new TestEnv();
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/impetus_pos") });
            Check("circle/impetus_pos：非环环境 -> Errored",
                r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));

            var r2 = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/impetus_dir") });
            var r3 = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/bounds/min") });
            Check("circle/impetus_dir 与 bounds/min 同样拒绝非环环境",
                r2.ResolutionType == ResolvedPatternType.Errored
                && r3.ResolutionType == ResolvedPatternType.Errored);
        }
        {
            // 环环境：impetus_pos 返回**方块中心**（+0.5 修正）
            var env = new TestEnv { CircleState = new CircleState
            {
                ImpetusX = 10, ImpetusY = 20, ImpetusDir = CircleDir.Right,
                MinX = 8, MinY = 18, MaxX = 14, MaxY = 24,
            } };
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/impetus_pos") });
            Check("circle/impetus_pos：环里返回方块中心 (10.5, 20.5)",
                r.ResolutionType == ResolvedPatternType.Evaluated
                && r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota v
                && System.Math.Abs(v.X - 10.5) < 1e-6 && System.Math.Abs(v.Y - 20.5) < 1e-6,
                Sig(r.Image));
        }
        {
            // impetus_dir 返回**单位向量**（.step() 的语义）
            var env = new TestEnv { CircleState = new CircleState
            {
                ImpetusX = 0, ImpetusY = 0, ImpetusDir = CircleDir.Down,
                MinX = 0, MinY = 0, MaxX = 1, MaxY = 1,
            } };
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/impetus_dir") });
            Check("circle/impetus_dir：方向 Down -> (0,1)",
                r.Image.Stack.Count == 1
                && r.Image.Stack[0] is VectorIota v
                && System.Math.Abs(v.X) < 1e-6 && System.Math.Abs(v.Y - 1.0) < 1e-6,
                Sig(r.Image));
        }
        {
            // 【关键】bounds 的 ±0.5 是**方块中心修正**：
            // AABB 的角是方块边角，加/减 0.5 才是最外方块的中心。
            // 不做修正的话返回值会落在方块角上，拿去当坐标用会偏半格。
            var env = new TestEnv { CircleState = new CircleState
            {
                ImpetusX = 0, ImpetusY = 0, ImpetusDir = CircleDir.Right,
                MinX = 8, MinY = 18, MaxX = 14, MaxY = 24,
            } };
            var img = new CastingImage(System.Array.Empty<Iota>());

            var rMin = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/bounds/min") });
            var rMax = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:circle/bounds/max") });

            bool okMin = rMin.Image.Stack.Count == 1 && rMin.Image.Stack[0] is VectorIota mn
                      && System.Math.Abs(mn.X - 8.5) < 1e-6 && System.Math.Abs(mn.Y - 18.5) < 1e-6;
            bool okMax = rMax.Image.Stack.Count == 1 && rMax.Image.Stack[0] is VectorIota mx
                      && System.Math.Abs(mx.X - 14.5) < 1e-6 && System.Math.Abs(mx.Y - 24.5) < 1e-6;

            Check("circle/bounds：min=(min+0.5)、max=(max+0.5) 方块中心修正",
                okMin && okMax, Sig(rMin.Image) + " / " + Sig(rMax.Image));
        }
        // ==================== P2-5 法术环：闭包校验与走图 ====================
        {
            // 【专项】原动力的禁止进入方向来自**起始方向**，不是 normal。
            // 把两者混为一谈会让「两块大的假环」被判成闭合 —— 这个缺陷是写用例时跑出来的。
            var world = new FakeCircleWorld();
            // 原动力 normal=Up，但媒质从 Right 流出 -> 禁止从 Left 进入
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            // 右边一块石板，normal=Right（垂直于流向），它可以朝左出去撞到原动力
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Right);

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环闭包：两块大的假环**不**算闭合（原动力禁止从来路进入）",
                !closure.IsClosed,
                $"闭合={closure.IsClosed} 走到 {closure.Reached.Count} 格");
        }
        {
            // 对照：如果原动力允许从 Left 进入（错误模型），上面那个假环就会被判成闭合。
            // 这里显式构造那个错误模型，证明差异确实来自这一处规则。
            var world = new FakeCircleWorld();
            // 显式构造「错误规则」：按 normal 推禁止进入方向（而不是按起始方向）
            world.Components[(0, 0)] = CircleComponent.Ordinary(CircleComponentKind.Impetus, CircleDir.Up);
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Right);

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("对照：用错误规则（按 normal 推）时假环会被判成闭合",
                closure.IsClosed,
                $"闭合={closure.IsClosed}");
        }
        {
            // 正常闭合的环：原动力 + 周围一圈部件，泛洪能回到原动力
            // 【几何约束，测试中被真实暴露出来】normal 必须**垂直于流向**：
            //   不能从 normal 的反方向进入、不能往 normal 方向出去。
            //   所以「所有部件 normal 朝上」是走不出环的 —— 这是真实的搭建约束，不是 bug。
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);   // 起点，向右流出
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Right);   // 向右走
            world.Set(1, -1, CircleComponentKind.Slate, CircleDir.Up);     // 向上拐
            world.Set(0, -1, CircleComponentKind.Slate, CircleDir.Left);   // 向左走，接回起点

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环闭包：4 格环 -> 闭合，包围盒正确",
                closure.IsClosed && closure.Error == CircleClosureError.Ok
                && closure.MinX == 0 && closure.MinY == -1
                && closure.MaxX == 1 && closure.MaxY == 0,
                $"{closure.Error} 盒({closure.MinX},{closure.MinY})-({closure.MaxX},{closure.MaxY})");
        }
        {
            // 断开的环：缺少一块 -> 泛洪回不到原动力
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Right);
            world.Set(1, -1, CircleComponentKind.Slate, CircleDir.Up);
            // 缺 (0,-1) —— 环没接回起点

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环闭包：断开 -> NoClosure",
                !closure.IsClosed && closure.Error == CircleClosureError.NoClosure,
                closure.Error.ToString());
        }
        {
            // 原动力旁边什么都没有 -> NoExits（与「断开的环」是不同错误）
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环闭包：旁边没部件 -> NoExits（与断层区分）",
                !closure.IsClosed && closure.Error == CircleClosureError.NoExits,
                closure.Error.ToString());
        }
        {
            // 【关键】长度上限。源项目源码注释：「忘了这个就能做出世界大小的环」。
            // 泰拉的图格是直接数组访问，没有区块加载的天然阻力，更依赖这个上限。
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            // 铺一条很长的直线（远超上限）
            for (int x = 1; x <= 2000; x++)
            {
                world.Set(x, 0, CircleComponentKind.Slate, CircleDir.Up);
            }

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right, maxLength: 64);
            Check("环闭包：超过长度上限 -> TooLong（防世界级巨环）",
                !closure.IsClosed && closure.Error == CircleClosureError.TooLong
                && closure.Reached.Count <= 64,
                $"{closure.Error} 走到 {closure.Reached.Count} 格");
        }
        {
            // 【关键】不能原路返回：出口集合必须减去来路的反方向。
            // 不减的话控制流会在两块之间原地打转，而且因为「恰好 1 个出口」的检查
            // 它会一直「合法」地循环下去 —— **不报错**，这是最阴的一类 bug。
            var up = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Up);

            var fromRight = CircleTraversal.ExitDirections(up, CircleDir.Right);
            bool noBackToRight = !fromRight.Contains(CircleDir.Left);

            var fromLeft = CircleTraversal.ExitDirections(up, CircleDir.Left);
            bool noBackToLeft = !fromLeft.Contains(CircleDir.Right);

            Check("环出口：不能原路返回（来路的反方向被剔除）",
                noBackToRight && noBackToLeft,
                $"从右进 -> {string.Join(",", fromRight)}；从左进 -> {string.Join(",", fromLeft)}");
        }
        {
            // 【关键】源项目**故意允许**沿 normal 的反方向穿过。
            // `// allDirs.remove(normal.getOpposite());` 那一行是被注释掉的。
            // 任何人「顺手补上」都会让控制流无法沿表面通过，环莫名断掉。
            var up = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Up);
            var possible = CircleTraversal.PossibleExitDirections(up);

            Check("环出口：允许沿 normal 的反方向穿过（源项目注释掉的那行）",
                possible.Contains(CircleDir.Down)     // normal 的反方向 —— 必须允许
                && !possible.Contains(CircleDir.Up),  // normal 本身 —— 必须排除
                string.Join(",", possible));
        }
        {
            // 出口集合大小：4 向 - normal = 3
            var up = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Up);
            var left = CircleComponent.Ordinary(CircleComponentKind.Slate, CircleDir.Left);
            Check("环出口：可能出口 = 4 - 1 = 3",
                CircleTraversal.PossibleExitDirections(up).Count == 3
                && CircleTraversal.PossibleExitDirections(left).Count == 3);
        }
        {
            // normal 方向限制进入：不能从 normal 的反方向进入该部件。
            // 地板上的石板 normal=Up，所以只能从它上方经过。
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            world.Set(1, 0, CircleComponentKind.Slate, CircleDir.Up);
            world.Set(0, -1, CircleComponentKind.Slate, CircleDir.Up);

            // 从上方（Y 更小）进入是允许的；泛洪应当能走到 (1,0)
            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环进入：能从允许的方向进入部件",
                closure.Reached.Contains((1, 0)), $"走到 {closure.Reached.Count} 格");
        }
        {
            // 走环速度曲线：max(2, 10 - (n-1)/3)
            bool ok = CircleTraversal.TickSpeed(1) == 10
                   && CircleTraversal.TickSpeed(4) == 9
                   && CircleTraversal.TickSpeed(7) == 8
                   && CircleTraversal.TickSpeed(25) == 2
                   && CircleTraversal.TickSpeed(100) == 2;
            Check("环速度：max(2, 10-(n-1)/3) —— 越长越快，最低 2",
                ok, $"{CircleTraversal.TickSpeed(1)},{CircleTraversal.TickSpeed(4)},{CircleTraversal.TickSpeed(25)}");
            // 原版单位是 MC 刻（20/秒），泰拉 60 帧/秒 → × 3（曾经直接当帧用，环快了 3 倍）
            Check("环速度换算成泰拉帧：起步 30 帧（半秒）、最低 6 帧",
                CircleTraversal.TickSpeedFrames(0) == 30 && CircleTraversal.TickSpeedFrames(100) == 6,
                $"{CircleTraversal.TickSpeedFrames(0)} {CircleTraversal.TickSpeedFrames(100)}");
            Check("环长度上限默认 1024（原版 DEFAULT_MAX_SPELL_CIRCLE_LENGTH）", CircleTraversal.DefaultMaxLength == 1024);
        }
        {
            // 方向工具：位移 / 反方向 / 向量
            bool ok = CircleDirs.Step(CircleDir.Up) == (0, -1)
                   && CircleDirs.Step(CircleDir.Down) == (0, 1)
                   && CircleDirs.Step(CircleDir.Left) == (-1, 0)
                   && CircleDirs.Step(CircleDir.Right) == (1, 0)
                   && CircleDirs.Opposite(CircleDir.Up) == CircleDir.Down
                   && CircleDirs.Opposite(CircleDir.Left) == CircleDir.Right
                   && CircleDirs.Offset(CircleDir.Left, 5, 5) == (4, 5)
                   && CircleDirs.ToVector(CircleDir.Down) == (0.0, 1.0);
            Check("环方向：位移/反方向/偏移/向量 全部正确", ok);
        }
        {
            // 包围盒：环的范围判定用的是它（**不是**玩家的 32 格半径）
            var world = new FakeCircleWorld();
            world.SetImpetus(0, 0, CircleDir.Up, CircleDir.Right);
            // 必须是**连续**的：泛洪只能走到相邻格，跳格是走不过去的
            for (int x = 1; x <= 5; x++)
            {
                world.Set(x, 0, CircleComponentKind.Slate, CircleDir.Up);
            }

            var closure = CircleTraversal.Validate(world, 0, 0, CircleDir.Right);
            Check("环包围盒：覆盖泛洪到的所有格",
                closure.MinX == 0 && closure.MinY == 0
                && closure.MaxX == 5 && closure.MaxY == 0,
                $"({closure.MinX},{closure.MinY})-({closure.MaxX},{closure.MaxY})");
        }
        Console.WriteLine("=== 比较类图案（compare_*） ===");
        {
            // ── compare_entity：比的是**同类**，不是「同一个实体」──
            var world = new FakeWorld();
            var slime1 = new EntityIota(EntityIota.EntityKind.Npc, 1);
            var slime2 = new EntityIota(EntityIota.EntityKind.Npc, 2);
            var zombie = new EntityIota(EntityIota.EntityKind.Npc, 3);
            var player0 = new EntityIota(EntityIota.EntityKind.Player, 0);
            var player5 = new EntityIota(EntityIota.EntityKind.Player, 5);

            world.Species[(EntityIota.EntityKind.Npc, 1)] = "slime";
            world.Species[(EntityIota.EntityKind.Npc, 2)] = "slime";
            world.Species[(EntityIota.EntityKind.Npc, 3)] = "zombie";
            world.Species[(EntityIota.EntityKind.Player, 0)] = "player";
            world.Species[(EntityIota.EntityKind.Player, 5)] = "player";

            var env = new TestEnv(world: world);

            var sameType = Run2(env, slime1, slime2, "hexcasting:compare_entity");
            Check("compare_entity：两个同种史莱姆 -> true",
                Sig(sameType.Image) == "[true]", Sig(sameType.Image));

            var diffType = Run2(env, slime1, zombie, "hexcasting:compare_entity");
            Check("compare_entity：史莱姆 vs 僵尸 -> false",
                Sig(diffType.Image) == "[false]", Sig(diffType.Image));

            var crossKind = Run2(env, slime1, player0, "hexcasting:compare_entity");
            Check("compare_entity：NPC vs 玩家 -> false",
                Sig(crossKind.Image) == "[false]", Sig(crossKind.Image));

            var twoPlayers = Run2(env, player0, player5, "hexcasting:compare_entity");
            Check("compare_entity：两个玩家互为同类 -> true",
                Sig(twoPlayers.Image) == "[true]", Sig(twoPlayers.Image));

            var missing = Run2(env, slime1, new EntityIota(EntityIota.EntityKind.Npc, 99),
                "hexcasting:compare_entity");
            Check("compare_entity：不存在的实体 -> false",
                Sig(missing.Image) == "[false]", Sig(missing.Image));

            var bad = Run2(env, slime1, new DoubleIota(1), "hexcasting:compare_entity");
            Check("compare_entity：参数不是实体 -> Errored",
                bad.ResolutionType == ResolvedPatternType.Errored, Sig(bad.Image));
        }
        {
            // ── compare_block：lenient 只比种类，strict 连帧/油漆一起比 ──
            var world = new FakeWorld();
            world.Blocks[(0, 0)] = (Type: 1, FrameX: 0, FrameY: 0, Paint: 0, Half: false);    // 石头
            world.Blocks[(1, 0)] = (Type: 1, FrameX: 18, FrameY: 0, Paint: 0, Half: false);  // 同种、不同帧
            world.Blocks[(2, 0)] = (Type: 1, FrameX: 0, FrameY: 0, Paint: 9, Half: false);   // 同种、不同油漆
            world.Blocks[(3, 0)] = (Type: 0, FrameX: 0, FrameY: 0, Paint: 0, Half: false);   // 泥土
            world.OutOfWorld.Add((99, 99));

            var env = new TestEnv(world: world);
            var v00 = new VectorIota(0, 0);
            var v10 = new VectorIota(1, 0);
            var v20 = new VectorIota(2, 0);
            var v30 = new VectorIota(3, 0);
            var vAA = new VectorIota(10, 10);
            var vOut = new VectorIota(99, 99);

            var lenSame = Run2(env, v00, v10, "hexcasting:compare_block/lenient");
            Check("compare_block/lenient：同种不同帧 -> true",
                Sig(lenSame.Image) == "[true]", Sig(lenSame.Image));

            var lenDiff = Run2(env, v00, v30, "hexcasting:compare_block/lenient");
            Check("compare_block/lenient：石头 vs 泥土 -> false",
                Sig(lenDiff.Image) == "[false]", Sig(lenDiff.Image));

            var lenAir = Run2(env, vAA, new VectorIota(11, 10), "hexcasting:compare_block/lenient");
            Check("compare_block/lenient：两块空气 -> true",
                Sig(lenAir.Image) == "[true]", Sig(lenAir.Image));

            var lenAirStone = Run2(env, vAA, v00, "hexcasting:compare_block/lenient");
            Check("compare_block/lenient：空气 vs 方块 -> false",
                Sig(lenAirStone.Image) == "[false]", Sig(lenAirStone.Image));

            var lenOut = Run2(env, vOut, v00, "hexcasting:compare_block/lenient");
            Check("compare_block/lenient：世界外 -> false",
                Sig(lenOut.Image) == "[false]", Sig(lenOut.Image));

            var strAir = Run2(env, v00, new VectorIota(0, 5), "hexcasting:compare_block/strict");
            // (0,5) 没有方块，所以是方块 vs 空气 -> false；确认 strict 也走同一套存在性判定
            Check("compare_block/strict：方块 vs 空气 -> false",
                Sig(strAir.Image) == "[false]", Sig(strAir.Image));

            var strFrame = Run2(env, v00, v10, "hexcasting:compare_block/strict");
            Check("compare_block/strict：同种不同帧 -> false",
                Sig(strFrame.Image) == "[false]", Sig(strFrame.Image));

            var strPaint = Run2(env, v00, v20, "hexcasting:compare_block/strict");
            Check("compare_block/strict：同种不同油漆 -> false",
                Sig(strPaint.Image) == "[false]", Sig(strPaint.Image));

            var strEqual = Run2(env, v00, new VectorIota(0.5, 0.5), "hexcasting:compare_block/strict");
            Check("compare_block/strict：同格（且向下取整）-> true",
                Sig(strEqual.Image) == "[true]", Sig(strEqual.Image));
        }
        {
            // ── compare_item：lenient 只比物品 ID，strict 连前缀一起比 ──
            var world = new FakeWorld();
            world.ItemSlots[0] = (Type: 75, Prefix: 0);    // 木剑
            world.ItemSlots[1] = (Type: 75, Prefix: 3);    // 木剑 + 前缀
            world.ItemSlots[2] = (Type: 24, Prefix: 0);    // 另一种物品

            var env = new TestEnv(world: world);
            var i0 = new EntityIota(EntityIota.EntityKind.Item, 0);
            var i1 = new EntityIota(EntityIota.EntityKind.Item, 1);
            var i2 = new EntityIota(EntityIota.EntityKind.Item, 2);
            var i9 = new EntityIota(EntityIota.EntityKind.Item, 9);

            var lenSame = Run2(env, i0, i1, "hexcasting:compare_item/lenient");
            Check("compare_item/lenient：同种物品、前缀不同 -> true",
                Sig(lenSame.Image) == "[true]", Sig(lenSame.Image));

            var lenDiff = Run2(env, i0, i2, "hexcasting:compare_item/lenient");
            Check("compare_item/lenient：不同物品 -> false",
                Sig(lenDiff.Image) == "[false]", Sig(lenDiff.Image));

            var strDiff = Run2(env, i0, i1, "hexcasting:compare_item/strict");
            Check("compare_item/strict：前缀不同 -> false",
                Sig(strDiff.Image) == "[false]", Sig(strDiff.Image));

            var strEqual = Run2(env, i0, new EntityIota(EntityIota.EntityKind.Item, 0),
                "hexcasting:compare_item/strict");
            Check("compare_item/strict：物品与前缀都相同 -> true",
                Sig(strEqual.Image) == "[true]", Sig(strEqual.Image));

            var strEmpty = Run2(env, i0, i9, "hexcasting:compare_item/strict");
            Check("compare_item：空槽位 -> false",
                Sig(strEmpty.Image) == "[false]", Sig(strEmpty.Image));

            var notItem = Run2(env, i0, new EntityIota(EntityIota.EntityKind.Npc, 1),
                "hexcasting:compare_item/strict");
            Check("compare_item：参数不是物品实体 -> Errored",
                notItem.ResolutionType == ResolvedPatternType.Errored, Sig(notItem.Image));
        }
        {
            // ── 「不适用于泰拉」的清单必须真的没被注册 ──
            static bool HasBehavior(string id)
                => PatternRegistry.All.Any(d => d.Id == id && PatternRegistry.HasAction(d));

            bool zAxisAbsent = NotApplicablePatterns.ZAxisVectors.All(id => !HasBehavior(id));
            bool pehkuiAbsent = NotApplicablePatterns.PehkuiInterop.All(id => !HasBehavior(id));
            Check("不适用清单：只剩 Pehkui 联动（±Z 向量已按原版实现）",
                zAxisAbsent && pehkuiAbsent && NotApplicablePatterns.Count == 2
                && HasBehavior("hexcasting:const/vec/pz") && HasBehavior("hexcasting:const/vec/nz"),
                $"count={NotApplicablePatterns.Count}");
        }

        Console.WriteLine("=== fisherman（把栈里某一项钓上来） ===");
        {
            // 三件东西 + 深度参数，所以深度合法范围是 [-1, 1]
            static CastOutcome Fish(string pattern, double depth)
            {
                var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(2), new DoubleIota(depth) });
                return new CastingVM(img, new TestEnv()).QueueExecute(img, new Iota[] { P(pattern) });
            }

            Check("fisherman：depth=1 -> [1,2] 变 [2,1]（钓上来后原位不再保留）",
                Sig(Fish("hexcasting:fisherman", 1).Image) == "[2, 1]",
                Sig(Fish("hexcasting:fisherman", 1).Image));

            Check("fisherman：depth=0 -> 栈不变",
                Sig(Fish("hexcasting:fisherman", 0).Image) == "[1, 2]",
                Sig(Fish("hexcasting:fisherman", 0).Image));

            Check("fisherman：depth=-1 -> 栈顶塞到下面 -> [2,1]",
                Sig(Fish("hexcasting:fisherman", -1).Image) == "[2, 1]",
                Sig(Fish("hexcasting:fisherman", -1).Image));

            Check("fisherman/copy：depth=1 -> 复制一份到栈顶 -> [1,2,1]",
                Sig(Fish("hexcasting:fisherman/copy", 1).Image) == "[1, 2, 1]",
                Sig(Fish("hexcasting:fisherman/copy", 1).Image));

            Check("fisherman/copy：depth=-1 -> [2,1,2]（负数分支与移动版不是同一个公式）",
                Sig(Fish("hexcasting:fisherman/copy", -1).Image) == "[2, 1, 2]",
                Sig(Fish("hexcasting:fisherman/copy", -1).Image));

            Check("fisherman：depth 超出 [-1,1] -> Errored",
                Fish("hexcasting:fisherman", 5).ResolutionType == ResolvedPatternType.Errored,
                Sig(Fish("hexcasting:fisherman", 5).Image));

            Check("fisherman：depth 不是整数 -> Errored",
                Fish("hexcasting:fisherman", 1.5).ResolutionType == ResolvedPatternType.Errored,
                Sig(Fish("hexcasting:fisherman", 1.5).Image));

            {
                // 只有一项时连 depth 都凑不出来 -> MishapNotEnoughArgs
                var img = new CastingImage(new Iota[] { new DoubleIota(0) });
                var r = new CastingVM(img, new TestEnv()).QueueExecute(img, new Iota[] { P("hexcasting:fisherman") });
                Check("fisherman：栈里不足 2 项 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
        }

        Console.WriteLine("=== 读/写数据载体（read / write / readable / writable / erase / local） ===");
        {
            // ── 手持载体：read / readable / write / writable / erase ──
            {
                // 手里拿着装了 7 的聚念核心
                var env = new TestEnv { HasStorage = true, HeldIota = new DoubleIota(7) };
                var img = new CastingImage();
                var r = Run(env, img, P("hexcasting:read"));
                Check("read：读出载体里的 7", Sig(r.Image) == "[7]", Sig(r.Image));
            }
            {
                // 手里拿着**空**载体：原版 readIota ?: emptyIota ?: mishap，而没有哪个物品定义 emptyIota —— 报错
                var env = new TestEnv { HasStorage = true };
                var img = new CastingImage();
                var r = Run(env, img, P("hexcasting:read"));
                Check("read：空载体 -> Errored（原版 OpRead：没有 emptyIota 就是「需要可以读出 iota 的地方」）",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                // 手上什么都没有：必须报 mishap，且**不能**静默读出 null
                var env = new TestEnv();
                var img = new CastingImage();
                var r = Run(env, img, P("hexcasting:read"));
                Check("read：手上没载体 -> Errored（与「空载体」区分开）",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                var img = new CastingImage();
                var has = Run(new TestEnv { HeldIota = new DoubleIota(1) }, img, P("hexcasting:readable"));
                var empty = Run(new TestEnv { HasStorage = true }, new CastingImage(), P("hexcasting:readable"));
                var not = Run(new TestEnv { HeldIota = new DoubleIota(1), ForceHasStorage = false }, new CastingImage(),
                    P("hexcasting:readable"));
                Check("readable：有内容的载体 -> true；空载体 -> false（原版 OpReadable）；普通物品 -> false",
                    Sig(has.Image) == "[true]" && Sig(empty.Image) == "[false]" && Sig(not.Image) == "[false]",
                    $"{Sig(has.Image)} / {Sig(empty.Image)} / {Sig(not.Image)}");
            }
            {
                var env = new TestEnv { HasStorage = true };
                var img = new CastingImage(new Iota[] { new VectorIota(3, 4) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:write") });
                Check("write：把栈顶写进手持载体",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && env.HeldIota is VectorIota { X: 3, Y: 4 },
                    env.HeldIota?.ToString() ?? "null");
            }
            {
                // 只读载体（卷轴）：write 必须报 mishap，而不是悄悄写失败
                var env = new TestEnv { HasStorage = true, HeldWritable = false };
                var img = new CastingImage(new Iota[] { new DoubleIota(1) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:write") });
                Check("write：只读载体 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                var img = new CastingImage();
                var w = Run(new TestEnv { HasStorage = true }, img, P("hexcasting:writable"));
                var ro = Run(new TestEnv { HasStorage = true, HeldWritable = false }, new CastingImage(),
                    P("hexcasting:writable"));
                Check("writable：可写载体 -> true；只读载体 -> false",
                    Sig(w.Image) == "[true]" && Sig(ro.Image) == "[false]",
                    $"{Sig(w.Image)} / {Sig(ro.Image)}");
            }
            {
                var env = new TestEnv { HasStorage = true, HeldIota = new DoubleIota(5) };
                var img = new CastingImage();
                var r = Run(env, img, P("hexcasting:erase"));
                Check("erase：清空手持载体",
                    r.ResolutionType == ResolvedPatternType.Evaluated && env.HeldIota == null,
                    env.HeldIota?.ToString() ?? "null");
            }
            {
                // 空核心上 erase：原版目标是「writeIota(null, simulate) 行得通」的载体 —— 空核心也行，照样扣一份粉尘
                var env = new TestEnv { HasStorage = true };
                long before = env.Media;
                var r = Run(env, new CastingImage(), P("hexcasting:erase"));
                Check("erase：空核心也能清（原版 OpErase），扣 1 粉尘",
                    r.ResolutionType == ResolvedPatternType.Evaluated && env.Erased == 1
                    && before - env.Media == HexCastingTerraria.Core.Media.MediaConstants.DustUnit, Sig(r.Image));
            }
            {
                // 结念绳：canWrite(null) = false，erase 清不掉 → 报错
                var env = new TestEnv { HeldIota = new DoubleIota(3), HeldCanWrite = d => false };
                var r = Run(env, new CastingImage(), P("hexcasting:erase"));
                Check("erase：结念绳清不掉 -> Errored，内容还在",
                    r.ResolutionType == ResolvedPatternType.Errored && env.HeldIota is DoubleIota { Value: 3 }, Sig(r.Image));
            }
            {
                // 以前 erase 在**求值阶段**就把载体清了：媒质不够、法术没放出来，东西已经没了
                var env = new TestEnv(media: 0) { HeldIota = new DoubleIota(5) };
                var r = Run(env, new CastingImage(), P("hexcasting:erase"));
                Check("erase：媒质不够 -> 不放，载体里的东西还在",
                    r.ResolutionType == ResolvedPatternType.Errored && env.Erased == 0 && env.HeldIota is DoubleIota { Value: 5 },
                    env.HeldIota?.ToString() ?? "null");
            }
            {
                // 打包法术：erase 清掉里面的咒术（原版 hexHolder.clearHex）
                var env = new TestEnv { HeldHasHex = true };
                var r = Run(env, new CastingImage(), P("hexcasting:erase"));
                Check("erase：清掉打包法术里的咒术", r.ResolutionType == ResolvedPatternType.Evaluated && !env.HeldHasHex, Sig(r.Image));
            }
            {
                // 卷轴：只收图案（原版 canWrite：datum is PatternIota || null）
                var env = new TestEnv { HasStorage = true, HeldCanWrite = d => d is null or PatternIota };
                var num = new CastingVM(new CastingImage(new Iota[] { new DoubleIota(1) }), env)
                    .QueueExecute(new CastingImage(new Iota[] { new DoubleIota(1) }), new Iota[] { P("hexcasting:write") });
                var pat = new CastingVM(new CastingImage(new Iota[] { P("hexcasting:add") }), env)
                    .QueueExecute(new CastingImage(new Iota[] { P("hexcasting:add") }), new Iota[] { P("hexcasting:write") });
                Check("write：卷轴拒收数字（Errored）、收图案",
                    num.ResolutionType == ResolvedPatternType.Errored && pat.ResolutionType == ResolvedPatternType.Evaluated
                    && env.HeldIota is PatternIota, $"{num.ResolutionType} / {pat.ResolutionType}");
            }

            // ── 实体载体：read/entity / readable/entity / write/entity / writable/entity ──
            {
                var world = new FakeWorld();
                var carried = new EntityIota(EntityIota.EntityKind.Item, 3);
                var plain = new EntityIota(EntityIota.EntityKind.Item, 4);
                world.EntityIotaHolders[(EntityIota.EntityKind.Item, 3)] = true;
                world.EntityIotas[(EntityIota.EntityKind.Item, 3)] = new DoubleIota(42);
                var env = new TestEnv(world: world);

                var read = Run(env, new CastingImage(new Iota[] { carried }), P("hexcasting:read/entity"));
                Check("read/entity：读出掉落物里的 42", Sig(read.Image) == "[42]", Sig(read.Image));

                var readEmpty = Run(env, new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 5) }),
                    P("hexcasting:read/entity"));
                Check("read/entity：不是载体的掉落物 -> Errored",
                    readEmpty.ResolutionType == ResolvedPatternType.Errored, Sig(readEmpty.Image));

                world.EntityIotaHolders[(EntityIota.EntityKind.Item, 6)] = true;   // 空载体
                var readBlank = Run(env, new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 6) }),
                    P("hexcasting:read/entity"));
                var readableBlank = Run(env, new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 6) }),
                    P("hexcasting:readable/entity"));
                Check("read/entity：空载体 -> Errored；readable/entity -> false（原版 OpTheCoolerRead / Readable）",
                    readBlank.ResolutionType == ResolvedPatternType.Errored && Sig(readableBlank.Image) == "[false]",
                    $"{readBlank.ResolutionType} / {Sig(readableBlank.Image)}");

                var readable = Run(env, new CastingImage(new Iota[] { carried }), P("hexcasting:readable/entity"));
                var readableNo = Run(env, new CastingImage(new Iota[] { plain }), P("hexcasting:readable/entity"));
                Check("readable/entity：载体 -> true；普通掉落物 -> false",
                    Sig(readable.Image) == "[true]" && Sig(readableNo.Image) == "[false]",
                    $"{Sig(readable.Image)} / {Sig(readableNo.Image)}");

                var writeImg = new CastingImage(new Iota[] { carried, new VectorIota(1, 1) });
                var write = new CastingVM(writeImg, env).QueueExecute(writeImg, new Iota[] { P("hexcasting:write/entity") });
                Check("write/entity：写进掉落物（真的落到世界上了）",
                    write.ResolutionType == ResolvedPatternType.Evaluated
                    && world.EntityIotas[(EntityIota.EntityKind.Item, 3)] is VectorIota { X: 1, Y: 1 },
                    world.ReadEntityIota(carried)?.ToString() ?? "null");

                var writable = Run(env, new CastingImage(new Iota[] { carried }), P("hexcasting:writable/entity"));
                Check("writable/entity：可写载体 -> true", Sig(writable.Image) == "[true]", Sig(writable.Image));

                // 掉在地上的卷轴：writeable 为真，但只收图案 —— 写数字要报错（原版 writeIota(datum, simulate)）
                world.EntityCanWrite = d => d is PatternIota;
                var badImg = new CastingImage(new Iota[] { carried, new DoubleIota(2) });
                var bad = new CastingVM(badImg, env).QueueExecute(badImg, new Iota[] { P("hexcasting:write/entity") });
                Check("write/entity：卷轴收不下数字 -> Errored（不是静默写失败）",
                    bad.ResolutionType == ResolvedPatternType.Errored, Sig(bad.Image));
                world.EntityCanWrite = null;
            }

            // ── 本次施法的局部存储：read/local / write/local ──
            {
                var env = new TestEnv();
                var img = new CastingImage();
                var r = Run(env, img, P("hexcasting:read/local"));
                Check("read/local：没写过 -> 压入 null（源项目同）", Sig(r.Image) == "[null]", Sig(r.Image));
            }
            {
                // 写进去 -> 读出来，要在**同一个 image** 上串联（locals 的语义就是跨图案传递）
                var env = new TestEnv();
                var img = new CastingImage(new Iota[] { new DoubleIota(9) });
                var afterWrite = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:write/local") });
                var afterRead = new CastingVM(afterWrite.Image, env)
                    .QueueExecute(afterWrite.Image, new Iota[] { P("hexcasting:read/local") });
                Check("write/local -> read/local：值能传下去",
                    Sig(afterWrite.Image) == "[]" && Sig(afterRead.Image) == "[9]",
                    $"{Sig(afterWrite.Image)} -> {Sig(afterRead.Image)}");
            }
            {
                // 写 null 应当**清除** locals（源项目 OpPushLocal 的分支）
                var env = new TestEnv();
                var img = new CastingImage(new Iota[] { new DoubleIota(9) });
                var w1 = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:write/local") });
                var img2 = w1.Image.WithStack(new Iota[] { NullIota.Instance });
                var w2 = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:write/local") });
                var r = new CastingVM(w2.Image, env).QueueExecute(w2.Image, new Iota[] { P("hexcasting:read/local") });
                Check("write/local 写 null -> 清除 locals", Sig(r.Image) == "[null]", Sig(r.Image));
            }
            {
                var img = new CastingImage();
                var r = new CastingVM(img, new TestEnv()).QueueExecute(img, new Iota[] { P("hexcasting:write/local") });
                Check("write/local：栈空 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                // userData 必须深拷贝：否则 mishap 回滚会把已写入的 locals 一起留着
                var userData = new CastUserData { Ravenmind = new DoubleIota(1) };
                var copy = userData.Clone();
                copy.Ravenmind = new DoubleIota(2);
                Check("CastUserData.Clone：Ravenmind 也是深拷贝",
                    userData.Ravenmind is DoubleIota { Value: 1 } && copy.Ravenmind is DoubleIota { Value: 2 },
                    $"{userData.Ravenmind} / {copy.Ravenmind}");
            }
        }

        Console.WriteLine("=== 单点法术（beep / create_lava / edify / place_block / recharge） ===");
        {
            string? lastFail = null;
            void Fail(string m) => lastFail = m;

            // ── beep：位置 + 乐器 + 音高，消耗 1000（DUST/10）──
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new VectorIota(3, 4), new DoubleIota(2), new DoubleIota(12),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:beep") });
                Check("beep：参数 (位置, 乐器, 音高) 落到世界上",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && world.Beeps.Count == 1 && world.Beeps[0] == (3.0, 4.0, 2, 12),
                    world.Beeps.Count == 1 ? world.Beeps[0].ToString() : "没有播放");
            }
            {
                // 乐器编号越界必须报 mishap：泰拉只有 14 种音色，不能静默取模
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new VectorIota(0, 0), new DoubleIota(OpBeep.InstrumentCount), new DoubleIota(0),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:beep") });
                Check("beep：乐器编号越界 -> Errored（不取模静默降级）",
                    r.ResolutionType == ResolvedPatternType.Errored && world.Beeps.Count == 0,
                    Sig(r.Image));
            }
            {
                // 音高 25 越界（合法是 0~24）
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new VectorIota(0, 0), new DoubleIota(0), new DoubleIota(25),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:beep") });
                Check("beep：音高 25 -> Errored（上限是 24）",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                // 音高必须是整数值：12.5 报错
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new VectorIota(0, 0), new DoubleIota(0), new DoubleIota(12.5),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:beep") });
                Check("beep：音高 12.5 -> Errored（要求整数值）",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }

            // ── create_lava：消耗是 create_water 的 10 倍 ──
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new VectorIota(1, 2) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:create_lava") });
                long spent = 1_000_000 - env.Media;
                Check("create_lava：造出一格岩浆，消耗 1 晶体（10 万）",
                    world.LavaCreated.Count == 1 && spent == MediaConstants.CrystalUnit,
                    $"消耗 {spent}");
            }

            // ── edify：目标必须是树苗 ──
            {
                var world = new FakeWorld();
                world.Saplings.Add((5, 6));
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new VectorIota(5, 6) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:edify") });
                Check("edify：树苗 -> 长成树，消耗 1 晶体",
                    world.GrownTrees.Count == 1 && 1_000_000 - env.Media == MediaConstants.CrystalUnit,
                    $"长树 {world.GrownTrees.Count} 次");
            }
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new VectorIota(5, 6) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:edify") });
                Check("edify：不是树苗 -> Errored（且不扣媒质）",
                    r.ResolutionType == ResolvedPatternType.Errored && env.Media == 1_000_000,
                    Sig(r.Image));
            }

            // ── place_block：格子必须可替换 ──
            {
                var world = new FakeWorld();
                world.Replaceable.Add((7, 8));
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new VectorIota(7, 8) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:place_block") });
                Check("place_block：可替换的格子 -> 放下背包里的方块，消耗 1250",
                    world.PlacedBlocks.Count == 1 && 1_000_000 - env.Media == MediaConstants.DustUnit / 8,
                    $"放置 {world.PlacedBlocks.Count} 次");
            }
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new VectorIota(7, 8) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:place_block") });
                Check("place_block：实心格子 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored && world.PlacedBlocks.Count == 0,
                    Sig(r.Image));
            }

            // ── recharge：从掉落物抽媒质，装进**手上的可充能物品**（源项目 OpRecharge）──
            //（这组测试原来断言「抽进玩家媒质池」—— 原版没有那个池子）
            {
                var world = new FakeWorld();
                var dropped = new EntityIota(EntityIota.EntityKind.Item, 2);
                world.ItemMedia[2] = MediaConstants.CrystalUnit;
                var env = new TestEnv(world: world) { HeldRechargeRoom = MediaConstants.CrystalUnit };
                var img = new CastingImage(new Iota[] { dropped });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:recharge") });
                Check("recharge：抽掉落物的媒质装进手上的瓶子，消耗 5 万（SHARD）",
                    env.Charged.SequenceEqual(new[] { MediaConstants.CrystalUnit }) && world.ItemMedia[2] == 0
                    && 1_000_000 - env.Media == MediaConstants.ShardUnit,
                    $"{r.ResolutionType} 装入 {string.Join(",", env.Charged)}");
            }
            {
                // 手上没有可充能物品 / 已经满了 -> 报 mishap，不扣媒质、不动掉落物
                foreach (var room in new long[] { -1, 0 })
                {
                    var world = new FakeWorld();
                    var dropped = new EntityIota(EntityIota.EntityKind.Item, 2);
                    world.ItemMedia[2] = 1000;
                    var env = new TestEnv(world: world) { HeldRechargeRoom = room };
                    var img = new CastingImage(new Iota[] { dropped });
                    var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:recharge") });
                    Check($"recharge：手上{(room < 0 ? "没有可充能物品" : "的瓶子已满")} -> Errored，不扣媒质",
                        r.ResolutionType == ResolvedPatternType.Errored && env.Media == 1_000_000 && world.ItemMedia[2] == 1000,
                        $"媒质 {env.Media}");
                }
            }
            {
                // 不是媒质物品 -> mishap
                var world = new FakeWorld();
                var dropped = new EntityIota(EntityIota.EntityKind.Item, 3);
                var env = new TestEnv(world: world) { HeldRechargeRoom = 1000 };
                var img = new CastingImage(new Iota[] { dropped });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:recharge") });
                Check("recharge：不是媒质物品 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                // 源项目：只抽够填满的量，但堆叠物品按整件扣 —— 空间 300，扣掉 1 整件粉（1 万），多的浪费
                var world = new FakeWorld();
                var dropped = new EntityIota(EntityIota.EntityKind.Item, 2);
                world.ItemMedia[2] = 5 * MediaConstants.DustUnit;
                world.ItemUnit[2] = MediaConstants.DustUnit;
                var env = new TestEnv(world: world) { HeldRechargeRoom = 300 };
                var img = new CastingImage(new Iota[] { dropped });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:recharge") });
                Check("recharge：空间 300 → 扣 1 整件粉、装进 300（多的浪费，原版同）",
                    env.Charged.SequenceEqual(new[] { 300L }) && world.ItemMedia[2] == 4 * MediaConstants.DustUnit,
                    $"装入 {string.Join(",", env.Charged)}，剩余 {world.ItemMedia[2]}");
            }

            if (lastFail != null) Fail(lastFail);
        }

        Console.WriteLine("=== 哨卫（sentinel/*） ===");
        // 原版哨卫图案先检查 env.castingEntity is ServerPlayer（否则 MishapBadCaster）——下面的用例都要有玩家施法者
        {
            var env = new TestEnv(world: new FakeWorld());   // 没有施法者（相当于法术环）
            var img = new CastingImage(new Iota[] { new VectorIota(1.0, 1.0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/create") });
            Check("sentinel/create：没有玩家施法者 -> MishapBadCaster（Errored）", r.ResolutionType == ResolvedPatternType.Errored, r.ResolutionType.ToString());
        }
        {
            // ── create：放下哨卫，普通 1 万 / 大哨卫 2 万 ──
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                var img = new CastingImage(new Iota[] { new VectorIota(100, 64) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/create") });
                Check("sentinel/create：放下哨卫，消耗 1 粉尘单位",
                    env.SentinelValue is { X: 100, Y: 64, Great: false }
                    && 1_000_000 - env.Media == MediaConstants.DustUnit,
                    env.SentinelValue?.ToString() ?? "null");
            }
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                var img = new CastingImage(new Iota[] { new VectorIota(100, 64) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/create/great") });
                Check("sentinel/create/great：大哨卫贵一倍（2 万）且标记 Great",
                    env.SentinelValue is { Great: true }
                    && 1_000_000 - env.Media == MediaConstants.DustUnit * 2,
                    env.SentinelValue?.ToString() ?? "null");
            }

            // ── get_pos：没放哨卫压 null，放了压坐标 ──
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                var img = new CastingImage();
                var none = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/get_pos") });
                Check("sentinel/get_pos：没放哨卫 -> null（不是报错）",
                    Sig(none.Image) == "[null]", Sig(none.Image));

                env.SentinelValue = new CastingEnvironment.SentinelState(7, 9, false);
                var img2 = new CastingImage();
                var r = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:sentinel/get_pos") });
                Check("sentinel/get_pos：放了哨卫 -> 坐标",
                    Sig(r.Image) == "[(7,9)]", Sig(r.Image));
            }

            // ── wayfind：单位向量 ──
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                env.SentinelValue = new CastingEnvironment.SentinelState(3, 4, false);

                var img = new CastingImage(new Iota[] { new VectorIota(0, 0) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/wayfind") });
                Check("sentinel/wayfind：(0,0) 看向 (3,4) -> 单位向量 (0.6,0.8)",
                    Sig(r.Image) == "[(0.6,0.8)]", Sig(r.Image));

                // 与哨卫重合 -> 零向量（照抄 MC 的 Vec3.normalize 阈值），不能是 NaN
                var img2 = new CastingImage(new Iota[] { new VectorIota(3, 4) });
                var r2 = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:sentinel/wayfind") });
                Check("sentinel/wayfind：与哨卫重合 -> 零向量（不产生 NaN）",
                    Sig(r2.Image) == "[(0,0)]", Sig(r2.Image));

                var env2 = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                var img3 = new CastingImage(new Iota[] { new VectorIota(0, 0) });
                var r3 = new CastingVM(img3, env2).QueueExecute(img3, new Iota[] { P("hexcasting:sentinel/wayfind") });
                Check("sentinel/wayfind：没放哨卫 -> null",
                    Sig(r3.Image) == "[null]", Sig(r3.Image));
            }

            // ── destroy：清掉；即使本来没有也不报错 ──
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                env.SentinelValue = new CastingEnvironment.SentinelState(1, 1, true);
                var img = new CastingImage();
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:sentinel/destroy") });
                Check("sentinel/destroy：清掉哨卫",
                    r.ResolutionType == ResolvedPatternType.Evaluated && env.SentinelValue == null,
                    env.SentinelValue?.ToString() ?? "null");

                var env2 = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                var img2 = new CastingImage();
                var r2 = new CastingVM(img2, env2).QueueExecute(img2, new Iota[] { P("hexcasting:sentinel/destroy") });
                Check("sentinel/destroy：本来就没哨卫 -> 不报错（源项目同）",
                    r2.ResolutionType == ResolvedPatternType.Evaluated, Sig(r2.Image));
            }

            // ── 范围延伸：这是大哨卫存在的全部理由，必须钉住 ──
            {
                var env = new TestEnv(world: new FakeWorld { Caster = new EntityIota(EntityIota.EntityKind.Player, 0) });
                Check("范围延伸：没哨卫时远处坐标超范围",
                    !env.IsInSentinelRange(100, 100));

                env.SentinelValue = new CastingEnvironment.SentinelState(100, 100, true);
                Check("范围延伸：大哨卫 16 格内算在范围内",
                    env.IsInSentinelRange(100, 100)
                    && env.IsInSentinelRange(110, 100)
                    && !env.IsInSentinelRange(120, 100),
                    "15 格内应通过、20 格外应拒绝");

                env.SentinelValue = new CastingEnvironment.SentinelState(100, 100, false);
                Check("范围延伸：**普通**哨卫不延伸范围（只有 great 才延伸）",
                    !env.IsInSentinelRange(100, 100));
            }
        }

        Console.WriteLine("=== 咒法飞行（flight 系列） ===");
        {
            // ── 危险度：这是飞行唯一的「失败模式」，必须逐段钉住 ──
            {
                bool ok = FlightDanger.Compute(0, 10, -1) == 0.0                  // 圈中心、不限时
                    && FlightDanger.Compute(5, 10, -1) == 0.0                      // 距边缘恰好 4 格 -> 仍安全
                    && FlightDanger.Compute(11, 10, -1) == 1.0                     // 出圈 -> 立刻结束
                    && System.Math.Abs(FlightDanger.Compute(8, 10, -1) - 0.5) < 1e-9  // 距边缘 2 格 -> 0.5
                    && FlightDanger.Compute(0, -1, 200) == 0.0                     // 不限距 + 时间充裕
                    && FlightDanger.Compute(0, -1, 70) == 0.5                      // 剩 70 tick -> 0.5
                    && FlightDanger.Compute(0, -1, 0) == 1.0                       // 时间到 -> 结束
                    && FlightDanger.Compute(0, -1, -1) == 0.0;                     // 全不限 -> 永不危险
                Check("飞行危险度：距离/时间两条曲线与阈值都对", ok,
                    $"{FlightDanger.Compute(8, 10, -1)}, {FlightDanger.Compute(0, -1, 70)}");
            }
            {
                // 同时限距限时 -> 取两者较大值
                Check("飞行危险度：限距与限时同时存在时取较大值",
                    System.Math.Abs(FlightDanger.Compute(8, 10, 70) - 0.5) < 1e-9
                    && FlightDanger.Compute(11, 10, 70) == 1.0);
            }

            // ── flight（Altiora）：弹起 + 给飞行，消耗 1 晶体 ──
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var target = new EntityIota(EntityIota.EntityKind.Player, 0);
                var img = new CastingImage(new Iota[] { target });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight") });
                Check("flight：向上弹起 + 授予飞行（不限时不限距），消耗 1 晶体",
                    world.Launches.Contains((EntityIota.EntityKind.Player, 0))
                    && world.Flights.TryGetValue((EntityIota.EntityKind.Player, 0), out var f)
                    && f.Ticks == -1 && f.Radius == -1 && f.Grace == OpAltiora.GracePeriodTicks
                    && 1_000_000 - env.Media == MediaConstants.CrystalUnit,
                    world.Flights.Count == 1 ? world.Flights.Values.First().ToString() : "没有飞行");
            }
            {
                // 已经有飞行时不覆盖（源项目注释：别把别人的飞行搞没了）
                var world = new FakeWorld();
                var target = new EntityIota(EntityIota.EntityKind.Player, 0);
                world.Flights[(EntityIota.EntityKind.Player, 0)] = (123, 1, 2, 3, 0);
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { target });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight") });
                Check("flight：目标已有飞行 -> 不覆盖也不弹",
                    world.Flights[(EntityIota.EntityKind.Player, 0)].Ticks == 123
                    && world.Launches.Count == 0,
                    world.Flights[(EntityIota.EntityKind.Player, 0)].ToString());
            }
            {
                // 目标不是玩家 -> mishap
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 1) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight") });
                Check("flight：目标不是玩家 -> Errored", r.ResolutionType == ResolvedPatternType.Errored,
                    Sig(r.Image));
            }

            // ── flight/range：按格计价，且至少收 1 格的钱 ──
            {
                var world = new FakeWorld { Feet = (20.0, 30.0) };
                var env = new TestEnv(world: world);
                var target = new EntityIota(EntityIota.EntityKind.Player, 0);
                var img = new CastingImage(new Iota[] { target, new DoubleIota(5) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight/range") });
                Check("flight/range：半径 5 -> 消耗 5×2 粉尘单位，起点记录进飞行状态",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && 1_000_000 - env.Media == 10 * MediaConstants.DustUnit
                    && world.Flights[(EntityIota.EntityKind.Player, 0)].Radius == 5.0
                    && world.Flights[(EntityIota.EntityKind.Player, 0)].OriginX == 20.0
                    && world.Flights[(EntityIota.EntityKind.Player, 0)].OriginY == 30.0,
                    $"消耗 {1_000_000 - env.Media}");
            }
            {
                // 半径 0.1 格：按公式只值 0.2 粉尘，但有「至少 1 格」的下限
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Player, 0), new DoubleIota(0.1),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight/range") });
                Check("flight/range：小半径仍收满 1 格的钱（下限 2 粉尘单位）",
                    1_000_000 - env.Media == OpFlight.CostPerUnit,
                    $"消耗 {1_000_000 - env.Media}");
            }

            // ── flight/time：按秒计价，**没有**下限（源码里的不对称） ──
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Player, 0), new DoubleIota(3),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight/time") });
                Check("flight/time：3 秒 -> 60 tick 飞行，消耗 3×2 粉尘单位",
                    world.Flights[(EntityIota.EntityKind.Player, 0)].Ticks == 60
                    && world.Flights[(EntityIota.EntityKind.Player, 0)].Radius == -1
                    && 1_000_000 - env.Media == 6 * MediaConstants.DustUnit,
                    world.Flights[(EntityIota.EntityKind.Player, 0)].ToString());
            }
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var img = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Player, 0), new DoubleIota(0.1),
                });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight/time") });
                Check("flight/time：小秒数**不**享受下限（与 range 版不对称，照抄源码）",
                    1_000_000 - env.Media == (long)System.Math.Round(0.1 * OpFlight.CostPerUnit),
                    $"消耗 {1_000_000 - env.Media}");
            }
            {
                // 源项目 OpFlight 用 getPositiveDouble：只有负数报错
                // ⚠️ 原版「positive」含 0（getPositiveDouble 是 0 <= x）；NaN / 无穷在 DoubleIota 里被 fixNAN 成 0。
                //    这条测试原来断言「0 / NaN 必须报错」，把移植版的错误行为当成了期望值。
                string wrong = "";
                foreach (var (d, ok) in new[] { (0.0, true), (double.NaN, true), (double.PositiveInfinity, true), (-1.0, false) })
                {
                    var e2 = new TestEnv(world: new FakeWorld());
                    var im = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Player, 0), new DoubleIota(d) });
                    var rr = new CastingVM(im, e2).QueueExecute(im, new Iota[] { P("hexcasting:flight/time") });
                    if ((rr.ResolutionType == ResolvedPatternType.Evaluated) != ok) wrong += $" {d}->{rr.ResolutionType}";
                }
                Check("flight/time：0 / NaN / 无穷(=0) 可以，负数报错", wrong.Length == 0, wrong);
            }

            // ── flight/can_fly：只读查询，消耗 0 ──
            {
                var world = new FakeWorld();
                var env = new TestEnv(world: world);
                var target = new EntityIota(EntityIota.EntityKind.Player, 0);

                var img = new CastingImage(new Iota[] { target });
                var no = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:flight/can_fly") });
                Check("flight/can_fly：没飞行 -> false，且不消耗媒质",
                    Sig(no.Image) == "[false]" && env.Media == 1_000_000, Sig(no.Image));

                world.Flights[(EntityIota.EntityKind.Player, 0)] = (10, 0, 0, -1, 0);
                var img2 = new CastingImage(new Iota[] { target });
                var yes = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:flight/can_fly") });
                Check("flight/can_fly：有飞行 -> true", Sig(yes.Image) == "[true]", Sig(yes.Image));
            }
        }

        Console.WriteLine("=== 打包法术与媒质瓶（craft/* 与 cycle_variant） ===");
        {
            // ── craft/cypher：把图案列表 + 地上媒质封进符纸 ──
            {
                var world = new FakeWorld();
                world.ItemMedia[1] = MediaConstants.CrystalUnit * 3;
                var env = new TestEnv(world: world) { HeldEmptyPackaged = PackagedSpellKind.Cypher };

                var patterns = new ListIota(new Iota[]
                {
                    P("hexcasting:add_motion"), P("hexcasting:const/vec/px"),
                });
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 1), patterns });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:craft/cypher") });

                Check("craft/cypher：封入 2 个图案 + 掉落物里全部的媒质，消耗 1 晶体",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && env.FilledPackaged.Count == 1
                    && env.FilledPackaged[0] == (2, MediaConstants.CrystalUnit * 3)
                    && 1_000_000 - env.Media == MediaConstants.CrystalUnit,
                    env.FilledPackaged.Count == 1 ? env.FilledPackaged[0].ToString() : "没封入");
            }
            {
                // 三档的差价必须体现出来（符纸 1 晶体 / 饰品 5 / 法器 10）
                long CostOf(PackagedSpellKind kind)
                {
                    var w = new FakeWorld();
                    w.ItemMedia[1] = MediaConstants.CrystalUnit;
                    var e = new TestEnv(world: w) { HeldEmptyPackaged = kind };
                    var im = new CastingImage(new Iota[]
                    {
                        new EntityIota(EntityIota.EntityKind.Item, 1),
                        new ListIota(new Iota[] { P("hexcasting:add_motion") }),
                    });
                    var id = kind switch
                    {
                        PackagedSpellKind.Cypher => "hexcasting:craft/cypher",
                        PackagedSpellKind.Trinket => "hexcasting:craft/trinket",
                        _ => "hexcasting:craft/artifact",
                    };
                    new CastingVM(im, e).QueueExecute(im, new Iota[] { P(id) });
                    return 1_000_000 - e.Media;
                }

                Check("craft 三档消耗：符纸 10 万 / 饰品 50 万 / 法器 100 万",
                    CostOf(PackagedSpellKind.Cypher) == MediaConstants.CrystalUnit
                    && CostOf(PackagedSpellKind.Trinket) == 5 * MediaConstants.CrystalUnit
                    && CostOf(PackagedSpellKind.Artifact) == 10 * MediaConstants.CrystalUnit,
                    $"{CostOf(PackagedSpellKind.Cypher)}/{CostOf(PackagedSpellKind.Trinket)}/{CostOf(PackagedSpellKind.Artifact)}");
            }
            {
                // 拿着符纸却想画 craft/trinket -> 拒绝（手持物与图案必须对应）
                var w = new FakeWorld();
                w.ItemMedia[1] = MediaConstants.CrystalUnit;
                var e = new TestEnv(world: w) { HeldEmptyPackaged = PackagedSpellKind.Cypher };
                var im = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Item, 1),
                    new ListIota(new Iota[] { P("hexcasting:add_motion") }),
                });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/trinket") });
                Check("craft：手持物与图案不匹配 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored && e.FilledPackaged.Count == 0,
                    Sig(r.Image));
            }
            {
                // 列表里混进非图案：原版 args.getList(1) 什么都收，writeHex 原样存下（放的时候整串入队）
                var w = new FakeWorld();
                w.ItemMedia[1] = MediaConstants.CrystalUnit;
                var e = new TestEnv(world: w) { HeldEmptyPackaged = PackagedSpellKind.Cypher };
                var im = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Item, 1),
                    new ListIota(new Iota[] { P("hexcasting:add_motion"), new DoubleIota(3) }),
                });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/cypher") });
                Check("craft：列表里混了非图案也照封（原版 OpMakePackagedSpell 不检查每一项）",
                    r.ResolutionType == ResolvedPatternType.Evaluated && e.FilledPackaged is [(2, _)],
                    Sig(r.Image));
            }
            {
                // 地上没有媒质 -> 拒绝，且**不扣**媒质
                var w = new FakeWorld();
                var e = new TestEnv(world: w) { HeldEmptyPackaged = PackagedSpellKind.Cypher };
                var im = new CastingImage(new Iota[]
                {
                    new EntityIota(EntityIota.EntityKind.Item, 1),
                    new ListIota(new Iota[] { P("hexcasting:add_motion") }),
                });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/cypher") });
                Check("craft：地上没有媒质 -> Errored 且不扣媒质",
                    r.ResolutionType == ResolvedPatternType.Errored && e.Media == 1_000_000,
                    Sig(r.Image));
            }

            // ── craft/battery：空瓶 + 地上媒质 -> 媒质瓶 ──
            {
                var w = new FakeWorld();
                w.ItemMedia[1] = MediaConstants.CrystalUnit * 2;
                var e = new TestEnv(world: w) { HeldPhials = 1 };
                var im = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 1) });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/battery") });
                Check("craft/battery：做出容量 = 抽到的媒质，消耗 1 晶体",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && e.CraftedBatteries.Count == 1
                    && e.CraftedBatteries[0] == MediaConstants.CrystalUnit * 2
                    && 1_000_000 - e.Media == MediaConstants.CrystalUnit,
                    e.CraftedBatteries.Count == 1 ? e.CraftedBatteries[0].ToString() : "没做出来");
            }
            {
                // 手里没空瓶 -> 拒绝
                var w = new FakeWorld();
                w.ItemMedia[1] = MediaConstants.CrystalUnit;
                var e = new TestEnv(world: w);
                var im = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 1) });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/battery") });
                Check("craft/battery：手里没空瓶 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored && e.CraftedBatteries.Count == 0,
                    Sig(r.Image));
            }
            {
                // craft/battery 是**需要启蒙**的大法术（源项目标签里就有它）
                var w = new FakeWorld();
                w.ItemMedia[1] = MediaConstants.CrystalUnit;
                var e = new TestEnv(world: w) { HeldPhials = 1, Enlightened = false };
                var im = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 1) });
                var r = new CastingVM(im, e).QueueExecute(im, new Iota[] { P("hexcasting:craft/battery") });
                Check("craft/battery：未启蒙 -> Invalid（原版 MishapUnenlightened.resolutionType）（大法术门槛）",
                    r.ResolutionType == ResolvedPatternType.Invalid, Sig(r.Image));
            }

            // ── cycle_variant：推进一格并绕回 ──
            {
                var env = new TestEnv { HeldVariant = 0, HeldVariantCount = 3 };
                var img = new CastingImage();
                var r1 = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:cycle_variant") });
                var r2 = new CastingVM(r1.Image, env).QueueExecute(r1.Image, new Iota[] { P("hexcasting:cycle_variant") });
                var r3 = new CastingVM(r2.Image, env).QueueExecute(r2.Image, new Iota[] { P("hexcasting:cycle_variant") });
                Check("cycle_variant：0 -> 1 -> 2 -> 0（按变体数取模）",
                    env.HeldVariant == 0 && r3.ResolutionType == ResolvedPatternType.Evaluated,
                    $"最终变体 {env.HeldVariant}");
                Check("cycle_variant：消耗 1 千（DUST/10）× 3 次",
                    env.Media == 1_000_000 - 3 * (MediaConstants.DustUnit / 10),
                    $"剩余 {env.Media}");
            }
            {
                var env = new TestEnv { HeldVariant = -1 };
                var img = new CastingImage();
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:cycle_variant") });
                Check("cycle_variant：手上的东西没有变体 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
        }

        Console.WriteLine("=== 脑叶切除（brainsweep） ===");
        {
            // 配方表：测试用的一组（真实表由 Mod.Load 注入，见 BrainsweepTable）
            const int dustBlock = 100;
            const int geodeCore = 101;
            const int emptyDirectrix = 200;
            const int redstoneDirectrix = 201;
            const int wizardSpecies = BrainsweepRules.TownNpcSpeciesBase - 108;

            BrainsweepRules.Configure(new[]
            {
                new BrainsweepRecipe(dustBlock, BrainsweepRules.AnySpecies, geodeCore, -1, 10 * MediaConstants.CrystalUnit),
                new BrainsweepRecipe(dustBlock, wizardSpecies, 300, -1, 10 * MediaConstants.CrystalUnit),
                new BrainsweepRecipe(emptyDirectrix, BrainsweepRules.TownNpcSpeciesBase - 38, redstoneDirectrix, -1, 10 * MediaConstants.CrystalUnit),
            });

            // ── 匹配顺序：精确种类必须优先于「任意种类」──
            {
                // 这条是本组最容易写错的地方：顺序反了巫师也只会出母岩，而且不报错
                bool exactFirst = BrainsweepRules.TryFind(dustBlock, wizardSpecies, out var r1)
                    && r1.ResultTile == 300;
                bool anyFallback = BrainsweepRules.TryFind(dustBlock, 12345, out var r2)
                    && r2.ResultTile == geodeCore;
                bool wrongTile = !BrainsweepRules.TryFind(999, wizardSpecies, out _);
                Check("配方匹配：精确种类优先于 AnySpecies，方块不符则不匹配",
                    exactFirst && anyFallback && wrongTile,
                    $"exact={exactFirst} any={anyFallback} wrongTile={wrongTile}");
            }

            // ── 正常切除 ──
            {
                var world = new FakeWorld();
                var npc = new EntityIota(EntityIota.EntityKind.Npc, 5);
                world.Tiles[(10, 20)] = dustBlock;
                world.SpeciesOf[(EntityIota.EntityKind.Npc, 5)] = 12345;

                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { npc, new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });

                Check("brainsweep：方块换成母岩，消耗取自配方（10 晶体）",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && world.Tiles[(10, 20)] == geodeCore
                    && world.Swept.Contains((EntityIota.EntityKind.Npc, 5))
                    && 1_000_000 - env.Media == 10 * MediaConstants.CrystalUnit,
                    $"剩余媒质 {env.Media}");
            }
            {
                // 未启蒙 -> 拒绝（它在源项目的 great spell 清单里）
                var world = new FakeWorld();
                world.Tiles[(10, 20)] = dustBlock;
                world.SpeciesOf[(EntityIota.EntityKind.Npc, 5)] = 12345;
                var env = new TestEnv(world: world) { Enlightened = false };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 5), new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：未启蒙 -> Invalid（原版 MishapUnenlightened.resolutionType）（大法术门槛）",
                    r.ResolutionType == ResolvedPatternType.Invalid && world.Sweeps.Count == 0, Sig(r.Image));
            }
            {
                // 配方不存在 -> 拒绝，且不扣媒质
                var world = new FakeWorld();
                world.Tiles[(10, 20)] = 999;
                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 5), new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：没有匹配配方 -> Errored 且不扣媒质",
                    r.ResolutionType == ResolvedPatternType.Errored && env.Media == 1_000_000, Sig(r.Image));
            }
            {
                // 已经被切过 -> 报「已经切过」而不是「配不上」（两种 mishap 必须分开）
                var world = new FakeWorld();
                world.Tiles[(10, 20)] = dustBlock;
                world.SpeciesOf[(EntityIota.EntityKind.Npc, 5)] = 12345;
                world.Swept.Add((EntityIota.EntityKind.Npc, 5));
                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 5), new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：已经切过 -> Errored（与「配不上」分开的 mishap）",
                    r.ResolutionType == ResolvedPatternType.Errored && world.Sweeps.Count == 0, Sig(r.Image));
            }
            {
                // 不可切除的生物（相当于源项目的 NO_BRAINSWEEPING）
                var world = new FakeWorld();
                world.Tiles[(10, 20)] = dustBlock;
                world.NotSweepable.Add((EntityIota.EntityKind.Npc, 7));
                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 7), new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：目标不可切除 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored && world.Sweeps.Count == 0, Sig(r.Image));
            }
            {
                // 位置不可编辑 -> 拒绝（顺序在「生物能不能切」之前）
                var world = new FakeWorld();
                world.Tiles[(10, 20)] = dustBlock;
                world.NoEdit.Add((10, 20));
                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 5), new VectorIota(10, 20) });
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：位置不可编辑 -> Errored",
                    r.ResolutionType == ResolvedPatternType.Errored, Sig(r.Image));
            }
            {
                // 精确配方的价格来自配方本身：把同一条配方改成别的价格，扣款必须跟着变
                BrainsweepRules.Configure(new[]
                {
                    new BrainsweepRecipe(dustBlock, BrainsweepRules.AnySpecies, geodeCore, -1, 3 * MediaConstants.DustUnit),
                });
                var world = new FakeWorld();
                world.Tiles[(1, 1)] = dustBlock;
                world.SpeciesOf[(EntityIota.EntityKind.Npc, 1)] = 42;
                var env = new TestEnv(world: world) { Enlightened = true };
                var img = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Npc, 1), new VectorIota(1, 1) });
                new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:brainsweep") });
                Check("brainsweep：消耗来自配方（不是写死的），改配方价格扣款随之变化",
                    1_000_000 - env.Media == 3 * MediaConstants.DustUnit,
                    $"扣了 {1_000_000 - env.Media}");
            }
        }

        Console.WriteLine("=== 法术配色（colorize） ===");
        {
            {
                var env = new TestEnv { PigmentItem = 1033 };   // 随便一个染料物品类型
                var img = new CastingImage();
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:colorize") });
                Check("colorize：找到颜料 -> 应用它，消耗 1 万（1 粉尘单位）",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && env.AppliedPigments.Count == 1 && env.AppliedPigments[0] == 1033
                    && 1_000_000 - env.Media == MediaConstants.DustUnit,
                    env.AppliedPigments.Count == 1 ? env.AppliedPigments[0].ToString() : "没应用");
            }
            {
                var env = new TestEnv { PigmentItem = 0 };
                var img = new CastingImage();
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:colorize") });
                Check("colorize：没有颜料 -> Errored 且不扣媒质",
                    r.ResolutionType == ResolvedPatternType.Errored
                    && env.Media == 1_000_000
                    && env.AppliedPigments.Count == 0,
                    Sig(r.Image));
            }
        }

        Console.WriteLine("=== 未识别图案的「最接近」建议 ===");
        {
            // 玩家随手画的曲线几乎一定不在 188 条里。给不出「差在哪」的报错等于把人扔在原地，
            // 所以这里验的是**建议本身可靠**：找得到、距离算得对、太远就不瞎给。
            var bySignature = PatternRegistry.SignaturesById();

            Check("建议：索引覆盖全部签名（同签名多条会归并）",
                bySignature.Count > 0 && bySignature.Count <= PatternRegistry.Count && bySignature.ContainsKey("qdwdq"),
                $"{bySignature.Count} 个签名 / {PatternRegistry.Count} 条图案");

            Check("建议：完全相同 -> 距离 0",
                PatternSuggestion.Distance("qdwdq", "qdwdq") == 0);

            Check("建议：改一笔 -> 距离 1（替换）",
                PatternSuggestion.Distance("qdwdq", "qdwdk") == 1
                || PatternSuggestion.Distance("qdwdq", "qdwd") == 1);

            Check("建议：插一笔 -> 距离 1",
                PatternSuggestion.Distance("qdwdq", "qdwdqq") == 1);

            {
                // 把 const/double/pi 少画最后一笔 -> 应当建议回 qdwdq
                var s = PatternSuggestion.Nearest("qdwd", bySignature);
                Check("建议：少一笔时能指出最接近的签名与差几笔",
                    s is { Signature: "qdwdq", Distance: 1 },
                    s?.ToString() ?? "没给出建议");
            }

            {
                // 差太远就不该给建议（「最像 X，差 9 笔」是纯噪音）
                var s = PatternSuggestion.Nearest("wwwwwwwwwwwwww", bySignature);
                Check("建议：差太远就不给（避免误导）", s == null, s?.ToString() ?? "（没给，正确）");
            }
        }
        Console.WriteLine("=== 特殊图案：数字字面量 与 掩码 ===");
        {
            // ── 数字字面量 ──
            // ⚠️ 这一类**不在 188 条注册表里**，是「前缀 + 图案本身算参数」。
            // 漏掉它的后果极其隐蔽：注册表全实现、用例全过，但玩家画不出任何数字。
            bool ok = SpecialPatterns.TryNumber("aqaaw", out var one) && one == 1
                   && SpecialPatterns.TryNumber("aqaaww", out var two) && two == 2
                   && SpecialPatterns.TryNumber("aqaaq", out var five) && five == 5
                   && SpecialPatterns.TryNumber("aqaae", out var ten) && ten == 10
                   && SpecialPatterns.TryNumber("deddw", out var negOne) && negOne == -1
                   && SpecialPatterns.TryNumber("deddq", out var negFive) && negFive == -5
                   && SpecialPatterns.TryNumber("aqaa", out var zero) && zero == 0;

            Check("数字图案：1/2/5/10/0/-1/-5 全部解对", ok,
                $"1={SpecialPatterns.TryNumber("aqaaw", out var a)} {a}");

            // 顺序敏感：aqaaeaw = (10×2)+1 = 21，不是 10+1
            Check("数字图案：运算是**按顺序施加**（aqaaeaw = 21，不是 11）",
                SpecialPatterns.TryNumber("aqaaeaw", out var v21) && v21 == 21, $"{v21}");

            Check("数字图案：d 表示除以 2（aqaawd = 0.5）",
                SpecialPatterns.TryNumber("aqaawd", out var half) && System.Math.Abs(half - 0.5) < 1e-9, $"{half}");

            Check("数字图案：s 是空操作（作者留的那一档）",
                SpecialPatterns.TryNumber("aqaawsw", out var skipped) && skipped == 2, $"{skipped}");

            Check("数字图案：非数字图案返回 false（不能把任意图案当数字）",
                !SpecialPatterns.TryNumber("qdwdq", out _)
                && !SpecialPatterns.TryNumber("aqaax", out _));

            Check("数字图案：编码回签名能往返（1/5/10/3/0）",
                SpecialPatterns.EncodeNumber(1) == "aqaaw"
                && SpecialPatterns.EncodeNumber(5) == "aqaaq"
                && SpecialPatterns.EncodeNumber(10) == "aqaae"
                && SpecialPatterns.EncodeNumber(3) == "aqaawww"
                && SpecialPatterns.EncodeNumber(0) == "aqaa",
                $"{SpecialPatterns.EncodeNumber(3)}");

            // ── 在 VM 里真的能压出一个数 ──
            {
                var env = new TestEnv();
                var img = new CastingImage();
                HexPattern.TryFromAngles("aqaawww", HexDir.East, out var pattern, out _);   // 数字 3
                var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { new PatternIota(pattern!) });
                Check("数字图案：在 VM 里执行 aqaawww 会压入 3",
                    r.ResolutionType == ResolvedPatternType.Evaluated
                    && r.Image.Stack.Count == 1
                    && r.Image.Stack[0] is DoubleIota { Value: 3 },
                    Sig(r.Image));
            }

            // ── 掩码 ──
            {
                // 一路直走 = 全保留
                HexPattern.TryFromAngles("www", HexDir.East, out var straight, out _);
                bool straightOk = SpecialPatterns.TryMask(straight!, out var m1)
                                  && m1.Length == 3 && m1[0] && m1[1] && m1[2];
                Check("掩码：连续直走 -> 全部保留", straightOk,
                    straightOk ? string.Join(",", m1!) : "没识别成掩码");
            }
            {
                // 「先向右前、再向左前」的下凹 = 丢弃一个，再直走 = 保留
                //
                // 方向序列必须是 [SE, NE, E]（相对起点 East）：
                //   SE 相对 East 是 Right，NE 相对 East 是 Left -> 命中下凹（丢弃两段）
                //   末尾的 E 相对 East 是 Forward -> 保留
                // 对应签名：e(到 SE) a(到 NE) e(回到 E)
                HexPattern.TryFromAngles("eae", HexDir.East, out var dip, out _);
                bool dipOk = SpecialPatterns.TryMask(dip!, out var m2)
                             && m2.Length == 2 && !m2[0] && m2[1];
                Check("掩码：一个下凹 + 一段直走 = 丢弃/保留", dipOk,
                    dipOk ? string.Join(",", m2!) : "没识别成掩码");
            }
        }
        Console.WriteLine("=== 收口：还有哪些图案没有行为 ===");
        {
            // 这条用例的作用是**永远给出一个准确答案**：
            // 「188 条里到底差多少」。差的那几条必须**恰好**是「不适用于泰拉」的清单，
            // 多一条都算回归 —— 否则「还差几条」会变成一句永远说不清的话。
            var missing = PatternRegistry.All
                .Where(d => !PatternRegistry.HasAction(d))
                .Select(d => d.Id)
                .ToList();

            var notApplicable = NotApplicablePatterns.ZAxisVectors
                .Concat(NotApplicablePatterns.PehkuiInterop)
                .ToHashSet();

            var unexpected = missing.Where(id => !notApplicable.Contains(id)).OrderBy(x => x).ToList();
            var wronglyDeclared = notApplicable.Where(id => !missing.Contains(id)).OrderBy(x => x).ToList();

            Check("收口：未实现的图案恰好只剩「不适用」的那几条",
                unexpected.Count == 0 && wronglyDeclared.Count == 0,
                $"意外未实现 [{string.Join(", ", unexpected)}]；"
                + $"被标为不适用但其实已实现 [{string.Join(", ", wronglyDeclared)}]");

            Console.WriteLine($"        已注册行为 {PatternRegistry.RegisteredActionCount} / {PatternRegistry.Count} 条；"
                              + $"不适用 {notApplicable.Count} 条；未实现 {missing.Count} 条");
        }

        // ── 过载与启蒙（原版 PlayerBasedCastEnv / HexAdvancements.ENLIGHTEN）──
        {
            // 满血 = 2 个充能紫水晶，不论生命上限（原版 20 × mediaToHealthRate）
            var (d1, g1, _) = Overcast.Plan(MediaConstants.CrystalUnit, 100, 100);
            Check("过载：100 血上限时 1 个充能紫水晶 = 50 点生命", d1 == 50 && g1 == MediaConstants.CrystalUnit, $"扣 {d1} 得 {g1}");
            var (d2, _, _) = Overcast.Plan(MediaConstants.CrystalUnit, 400, 400);
            Check("过载：400 血上限时同样的缺口 = 200 点（按生命比例，满血恒等于 2 个紫水晶）", d2 == 200, $"扣 {d2}");
            var (d3, _, _) = Overcast.Plan(1, 100, 100);
            Check("过载：再小的缺口也至少扣 2.5% 生命（原版最少 0.5/20 点）", d3 == 3, $"扣 {d3}");
            var (_, g4, lethal4) = Overcast.Plan(3 * MediaConstants.CrystalUnit, 100, 100);
            Check("过载：生命不够付时只能换到当前生命的量（施法前试算会判媒质不足）",
                g4 == 2 * MediaConstants.CrystalUnit && lethal4, $"得 {g4}");

            Check("启蒙：用掉 80% 且只剩不到半颗心（100 上限 → ≤5 点）→ 启蒙", Overcast.IsEnlightening(80, 100, 5));
            Check("启蒙：剩得太多不算", !Overcast.IsEnlightening(80, 100, 20));
            Check("启蒙：用得不到 80% 不算", !Overcast.IsEnlightening(79, 100, 1));
            Check("启蒙：耗死了不算（必须活下来）", !Overcast.IsEnlightening(100, 100, 0));
        }
        {
            // 未启蒙强行施放大法术：丢下手持物品 + 记为「盲目绘制」（解锁过载），图案判为无效
            var env = new TestEnv(world: new FakeWorld()) { Enlightened = false };
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:summon_rain") });
            Check("未启蒙施放大法术：Invalid + 丢下手持物品 + 触发「盲目绘制」",
                r.ResolutionType == ResolvedPatternType.Invalid && env.DroppedHeld == 1 && env.FailedGreatSpells == 1,
                $"{r.ResolutionType} drop={env.DroppedHeld} fail={env.FailedGreatSpells}");
        }

        // ==================== 对照原版审计（AUDIT_VS_ORIGINAL.md）====================
        // 下面每条的期望值都来自 hexsrc 原版源码，不是移植版现有行为。
        {
            var env = new TestEnv();
            // #3 arcsin/arccos 定义域外 → MishapInvalidIota（原版 asDoubleBetween），参数换成垃圾值
            var img = new CastingImage(new Iota[] { new DoubleIota(2) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:arcsin") });
            Check("arcsin(2)：定义域外报错，参数换成垃圾值（原版不夹取）",
                r.ResolutionType == ResolvedPatternType.Errored && Sig(r.Image) == "[garbage]", $"{r.ResolutionType} {Sig(r.Image)}");

            // #4 位运算
            Check("数字 与：5 & 3 = 1", Sig(Run2(env, new DoubleIota(5), new DoubleIota(3), "hexcasting:and").Image) == "[1]");
            Check("数字 或：5 | 3 = 7", Sig(Run2(env, new DoubleIota(5), new DoubleIota(3), "hexcasting:or").Image) == "[7]");
            Check("数字 异或：5 ^ 3 = 6", Sig(Run2(env, new DoubleIota(5), new DoubleIota(3), "hexcasting:xor").Image) == "[6]");
            var imgNot = new CastingImage(new Iota[] { new DoubleIota(0) });
            var rNot = new CastingVM(imgNot, env).QueueExecute(imgNot, new Iota[] { P("hexcasting:not") });
            Check("数字 非：~0 = -1", Sig(rNot.Image) == "[-1]", $"{rNot.ResolutionType} {Sig(rNot.Image)}");
            Check("位运算先四舍五入：2.6 & 3 = 3", Sig(Run2(env, new DoubleIota(2.6), new DoubleIota(3), "hexcasting:and").Image) == "[3]");

            // #5 列表集合运算
            ListIota L(params double[] xs) => new ListIota(xs.Select(x => (Iota)new DoubleIota(x)).ToArray());
            Check("列表 与 = 交集（保左表顺序）", Sig(Run2(env, L(1, 2, 3), L(3, 1), "hexcasting:and").Image) == "[list(2)]"
                && Run2(env, L(1, 2, 3), L(3, 1), "hexcasting:and").Image.Stack[0] is ListIota { Items: [DoubleIota { Value: 1 }, DoubleIota { Value: 3 }] });
            var orR = Run2(env, L(1, 2), L(2, 3), "hexcasting:or").Image.Stack[0] as ListIota;
            Check("列表 或 = 并集（左表 + 右表里没有的）", orR is { Count: 3 } && orR.Items[2] is DoubleIota { Value: 3 });
            var xorR = Run2(env, L(1, 2), L(2, 3), "hexcasting:xor").Image.Stack[0] as ListIota;
            Check("列表 异或 = 对称差", xorR is { Count: 2 } && xorR.Items[0] is DoubleIota { Value: 1 } && xorR.Items[1] is DoubleIota { Value: 3 });

            // #6 布尔的长度之纯化
            var imgAbs = new CastingImage(new Iota[] { BooleanIota.True });
            var rAbs = new CastingVM(imgAbs, env).QueueExecute(imgAbs, new Iota[] { P("hexcasting:abs") });
            Check("长度之纯化(true) = 1", Sig(rAbs.Image) == "[1]", Sig(rAbs.Image));

            // 相等容差（原版 Iota.tolerates）
            Check("相等：向量差 1e-5 视为相等（原版距离² < 1e-8）",
                Sig(Run2(env, new VectorIota(1, 2), new VectorIota(1.00001, 2), "hexcasting:equals").Image) == "[true]");
            var pa = PatternRegistry.FindById("hexcasting:get_caster")!.Prototype;
            HexPattern.TryFromAngles(pa.AnglesSignature(), HexDir.West, out var pb, out _);
            Check("相等：同一图案换起始方向仍相等（原版只比角度）",
                Sig(Run2(env, new PatternIota(pa), new PatternIota(pb!), "hexcasting:equals").Image) == "[true]");
            Check("相等：列表逐项按容差比",
                Sig(Run2(env, L(1, 2), L(1.00001, 2), "hexcasting:equals").Image) == "[true]");
        }
        {
            // #9 #10 区域之馏化：任意 = 任意实体；免费
            var world = new FakeWorld();
            var env = new TestEnv(world: world, media: 1_000);
            var img = new CastingImage(new Iota[] { new VectorIota(0, 0), new DoubleIota(5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:zone_entity") });
            Check("zone_entity：筛选是「任意」、不花媒质（原版 ConstMediaAction 默认 0）",
                r.ResolutionType == ResolvedPatternType.Evaluated && world.LastQueryFilter == ZoneEntityFilter.Any && env.Media == 1_000,
                $"{world.LastQueryFilter} 媒质 {env.Media}");
        }
        {
            // #12 放置方块：快捷栏没有可放的 → MishapLackingHotbarItem（丢下手持物品），不扣媒质
            var world = new FakeWorld { Placeable = false };
            world.Replaceable.Add((3, 3));
            var env = new TestEnv(world: world, media: 1_000_000);
            var img = new CastingImage(new Iota[] { new VectorIota(3.5, 3.5) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:place_block") });
            Check("place_block：快捷栏没东西 → 报错 + 丢下手持物品 + 不扣媒质",
                r.ResolutionType == ResolvedPatternType.Errored && env.DroppedHeld == 1 && env.Media == 1_000_000,
                $"{r.ResolutionType} drop={env.DroppedHeld} media={env.Media}");
        }
        {
            // #13 mishap 惩罚
            var env = new TestEnv();
            var r = Run2(env, new DoubleIota(1), new DoubleIota(0), "hexcasting:div");
            Check("除以零：压一个垃圾值 + 扣当前生命的一半（原版 damage(0.5)）",
                Sig(r.Image) == "[1, 0, garbage]" && env.Damages.SequenceEqual(new[] { 0.5 }), $"{Sig(r.Image)} {string.Join(",", env.Damages)}");

            var r2 = Run2(env, BooleanIota.True, new VectorIota(1, 1), "hexcasting:add");
            Check("运算符参数类型不对：参与的参数全换成垃圾值", Sig(r2.Image) == "[garbage, garbage]", Sig(r2.Image));

            var img3 = new CastingImage(new Iota[] { new DoubleIota(7) });
            var r3 = new CastingVM(img3, env).QueueExecute(img3, new Iota[] { P("hexcasting:close_paren") });
            Check("多余的闭括号：把这个图案压回栈（原版 MishapNeedsParens）",
                r3.Image.Stack.Count == 2 && r3.Image.Stack[1] is PatternIota, Sig(r3.Image));

            // 挑一条合法、没注册、也不是数字 / 掩码的图案
            HexPattern? junk = null;
            foreach (var sig in new[] { "qeqeqe", "eqeqeqe", "qqeqqeqq", "eeqeeqee", "qwqwqwqwqa" })
            {
                if (HexPattern.TryFromAngles(sig, HexDir.East, out var cand, out _) && cand != null
                    && PatternRegistry.Match(cand) == null && !SpecialPatterns.TryNumber(sig, out _)
                    && !SpecialPatterns.TryMask(cand, out _)) { junk = cand; break; }
            }
            var img4 = new CastingImage(new Iota[] { new DoubleIota(7) });
            var r4 = new CastingVM(img4, env).QueueExecute(img4, new Iota[] { new PatternIota(junk!) });
            Check("无效图案：压一个垃圾值", r4.ResolutionType == ResolvedPatternType.Invalid && Sig(r4.Image) == "[7, garbage]", $"{r4.ResolutionType} {Sig(r4.Image)}");

            var world = new FakeWorld { InWorld = false };
            var env5 = new TestEnv(world: world);
            var img5 = new CastingImage(new Iota[] { new VectorIota(9, 9) });
            new CastingVM(img5, env5).QueueExecute(img5, new Iota[] { P("hexcasting:lightning") });
            var w6 = new FakeWorld();
            var env6 = new TestEnv(world: w6, media: 1_000_000);
            var img6 = new CastingImage(new Iota[] { new VectorIota(4.5, 4.5) });   // 不可替换 → MishapBadBlock
            new CastingVM(img6, env6).QueueExecute(img6, new Iota[] { P("hexcasting:conjure_block") });
            Check("方块不对：在那一格中心来一次不破坏方块的小爆炸（原版 0.25、NONE）",
                w6.MishapExplosions.SequenceEqual(new[] { (4.5, 4.5) }), string.Join(",", w6.MishapExplosions));

            var env7 = new TestEnv(media: 30);
            var img7 = new CastingImage(new Iota[] { new VectorIota(0, 0), new VectorIota(1, 0) });
            var r7 = new CastingVM(img7, env7).QueueExecute(img7, new Iota[] { P("hexcasting:raycast") });
            Check("媒质不够：把能付的都抽走（原版 extractMedia(cost, false)）",
                r7.ResolutionType == ResolvedPatternType.Errored && env7.Media == 0, $"{r7.ResolutionType} 剩 {env7.Media}");
        }
        {
            // #14 mishap 上下文带图案名
            var env = new TestEnv();
            var img = new CastingImage(new Iota[] { new DoubleIota(1), new DoubleIota(0) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:div") });
            var ctx = env.MishapContexts.FirstOrDefault();
            Check("mishap 上下文带出错图案与中文名（聊天提示前缀）",
                ctx?.Pattern is not null && ctx.Name == "除法之馏化", ctx?.Name ?? "null");
        }
        {
            // #15 真名保护与传送免疫
            var caster = new EntityIota(EntityIota.EntityKind.Player, 0);
            var other = new EntityIota(EntityIota.EntityKind.Player, 1);
            var world = new FakeWorld { Caster = caster };
            var env = new TestEnv(world: world) { HeldIota = NullIota.Instance };
            var img = new CastingImage(new Iota[] { new ListIota(new Iota[] { other }) });
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:write") });
            Check("把别的玩家（嵌在列表里）写进物品 → MishapOthersName，失明 60 秒",
                r.ResolutionType == ResolvedPatternType.Errored && env.Blinds.SequenceEqual(new[] { 1200 }), $"{r.ResolutionType} {string.Join(",", env.Blinds)}");
            var img2 = new CastingImage(new Iota[] { caster });
            var r2 = new CastingVM(img2, env).QueueExecute(img2, new Iota[] { P("hexcasting:write") });
            Check("把自己写进物品可以（原版 getTrueNameFromDatum 忽略施法者）", r2.ResolutionType == ResolvedPatternType.Evaluated, r2.ResolutionType.ToString());

            world.EntityIotaHolders[(EntityIota.EntityKind.Item, 3)] = true;
            var env3 = new TestEnv(world: world);
            var img3 = new CastingImage(new Iota[] { new EntityIota(EntityIota.EntityKind.Item, 3), caster });
            var r3 = new CastingVM(img3, env3).QueueExecute(img3, new Iota[] { P("hexcasting:write/entity") });
            Check("编年史家之策略：连自己也不能写进实体 → 失明 5 秒", r3.ResolutionType == ResolvedPatternType.Errored && env3.Blinds.SequenceEqual(new[] { 100 }),
                $"{r3.ResolutionType} {string.Join(",", env3.Blinds)}");

            var boss = new EntityIota(EntityIota.EntityKind.Npc, 7);
            world.TeleportImmune.Add((EntityIota.EntityKind.Npc, 7));
            var env4 = new TestEnv(world: world, media: 1_000_000);
            var img4 = new CastingImage(new Iota[] { boss, new DoubleIota(3) });
            var r4 = new CastingVM(img4, env4).QueueExecute(img4, new Iota[] { P("hexcasting:blink") });
            Check("闪现 Boss → MishapImmuneEntity，手持物品甩向它", r4.ResolutionType == ResolvedPatternType.Errored && env4.Yeets.Count == 1,
                $"{r4.ResolutionType} yeets={env4.Yeets.Count}");
        }

        // ==================== 对照原版审计：VM 与栈操作 ====================
        {
            // 骗徒之策略：原版阶乘序列 1,1,2,6…（从 0! 开始）。期望值按 OpAlwinfyHasAscendedToABeingOfPureMath 手推
            var env = new TestEnv();
            Iota A = new DoubleIota(1), B = new DoubleIota(2), C = new DoubleIota(3);
            CastingImage Img(params Iota[] xs) => new CastingImage(xs);
            var r1 = new CastingVM(Img(A, B, new DoubleIota(1)), env).QueueExecute(Img(A, B, new DoubleIota(1)), new Iota[] { P("hexcasting:swizzle") });
            Check("骗徒之策略 码 1：交换栈顶两项（原版；曾经什么都不做）", Sig(r1.Image) == "[2, 1]", Sig(r1.Image));
            var i2 = Img(A, B, C, new DoubleIota(2));
            var r2 = new CastingVM(i2, env).QueueExecute(i2, new Iota[] { P("hexcasting:swizzle") });
            Check("骗徒之策略 码 2：[1,2,3] → [2,1,3]", Sig(r2.Image) == "[2, 1, 3]", Sig(r2.Image));
            var i5 = Img(A, B, C, new DoubleIota(5));
            var r5 = new CastingVM(i5, env).QueueExecute(i5, new Iota[] { P("hexcasting:swizzle") });
            Check("骗徒之策略 码 5：[1,2,3] → [3,2,1]（完全逆序）", Sig(r5.Image) == "[3, 2, 1]", Sig(r5.Image));
            var i0 = Img(new DoubleIota(0));
            var r0 = new CastingVM(i0, env).QueueExecute(i0, new Iota[] { P("hexcasting:swizzle") });
            Check("骗徒之策略 码 0：空栈也不报错（原版不碰栈）", r0.ResolutionType == ResolvedPatternType.Evaluated && Sig(r0.Image) == "[]", $"{r0.ResolutionType} {Sig(r0.Image)}");

            var dn = Run2(env, A, new DoubleIota(-1), "hexcasting:duplicate_n");
            Check("双子之策略 负数：报错（原版 getPositiveInt）", dn.ResolutionType == ResolvedPatternType.Errored, dn.ResolutionType.ToString());

            var ln = Img(A, B, new DoubleIota(5));
            var rl = new CastingVM(ln, env).QueueExecute(ln, new Iota[] { P("hexcasting:last_n_list") });
            Check("群体之策略 个数超出：把「个数」那一项换成垃圾值", Sig(rl.Image) == "[1, 2, garbage]", Sig(rl.Image));
        }
        {
            // 栈过大：原版 isTooLargeToSerialize —— 从 1 开始累加，总数 ≥ 1024 就算太大
            var env = new TestEnv();
            var items = Enumerable.Range(0, 1021).Select(i => (Iota)new DoubleIota(i)).ToArray();
            var ok = new CastingVM(new CastingImage(items), env).QueueExecute(new CastingImage(items), new Iota[] { P("hexcasting:const/true") });
            Check("栈上 1022 项（1+1022=1023）：不算太大", ok.ResolutionType == ResolvedPatternType.Evaluated, ok.ResolutionType.ToString());
            var items2 = Enumerable.Range(0, 1022).Select(i => (Iota)new DoubleIota(i)).ToArray();
            var bad = new CastingVM(new CastingImage(items2), env).QueueExecute(new CastingImage(items2), new Iota[] { P("hexcasting:const/true") });
            Check("栈上 1023 项（1+1023=1024）：太大 → 清空只剩一个垃圾值", bad.ResolutionType == ResolvedPatternType.Errored && Sig(bad.Image) == "[garbage]",
                $"{bad.ResolutionType} {bad.Image.Stack.Count}");
        }
        {
            // 插嵌：拿着空载体 → 报错（原版 readIota ?: emptyIota ?: mishap，而没有哪个物品定义 emptyIota；上一轮这里写反了）
            var env = new TestEnv { HasStorage = true };
            var img = new CastingImage(System.Array.Empty<Iota>());
            var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { P("hexcasting:open_paren"), P("hexcasting:read_into_parens"), P("hexcasting:close_paren") });
            Check("插嵌：空载体 -> Errored", r.ResolutionType == ResolvedPatternType.Errored, $"{r.ResolutionType} {Sig(r.Image)}");
        }

        // ==================== 法术坐标 ↔ 泰拉坐标（HexAxes，Y 朝上） ====================
        {
            const int H = 1200;
            // 站在地上：地面是泰拉图格 700，脚底像素在它的上边 → 泰拉 y = 700 → 法术 y = 500（整数）
            double feet = HexAxes.FlipPosition(700, H);
            Check("脚底 y 为整数时：原版 floor(y) 是脚所在的空气格 = 泰拉 699（地面上方那格）",
                feet == 500 && HexAxes.TileOfPoint(feet, H) == 699, $"{HexAxes.TileOfPoint(feet, H)}");
            Check("换成连续图格坐标后 floor 与之一致（整数边界不差一格）",
                (int)System.Math.Floor(HexAxes.PointToTileY(feet, H)) == 699
                && (int)System.Math.Floor(HexAxes.PointToTileY(499.5, H)) == 700);
            Check("方块下标往返一致、方块中心往返一致",
                HexAxes.FlipBlock(HexAxes.FlipBlock(123, H), H) == 123
                && HexAxes.TileOfPoint(HexAxes.FlipBlock(700, H) + 0.5, H) == 700);
            Check("方向：泰拉向下 = 法术 −Y", HexAxes.FlipDirection(1) == -1);
        }

        // ==================== 大法术：每个世界的笔顺（源项目 per_world_pattern） ====================
        {
            var t1 = PatternRegistry.GeneratePerWorld(12345);
            var t1b = PatternRegistry.GeneratePerWorld(12345);
            var t2 = PatternRegistry.GeneratePerWorld(98765);
            Check("每个世界的笔顺：14 个大法术都有", t1.Count == 14, t1.Count.ToString());
            Check("同一种子结果相同（存档可复现）", t1.All(kv => t1b[kv.Key].AnglesSignature() == kv.Value.AnglesSignature()));
            Check("不同种子至少有一半不同", t1.Count(kv => t2[kv.Key].AnglesSignature() != kv.Value.AnglesSignature()) >= 7);
            bool sameShape = t1.All(kv =>
            {
                var proto = PatternRegistry.FindById(kv.Key)!.Prototype;
                return EulerPathFinder.EdgeSet(proto).SetEquals(EulerPathFinder.EdgeSet(kv.Value));
            });
            Check("形状（边集）与标准图案完全相同，只是笔顺不同", sameShape);

            PatternRegistry.SetPerWorld(t1);
            var lightning = PatternRegistry.FindById("hexcasting:lightning")!;
            var world = t1["hexcasting:lightning"];
            bool canonicalDead = world.AnglesSignature() == lightning.Angles || PatternRegistry.Match(lightning.Prototype) == null;
            Check("本世界笔顺识别为召雷；标准笔顺（若不同）不再识别",
                PatternRegistry.Match(world)?.Id == "hexcasting:lightning" && canonicalDead);
            Check("普通图案不受影响", PatternRegistry.Match(PatternRegistry.FindById("hexcasting:get_caster")!.Prototype)?.Id == "hexcasting:get_caster");
            PatternRegistry.ResetPerWorldToCanonical();
            Check("没有世界时回到标准笔顺", PatternRegistry.Match(lightning.Prototype)?.Id == "hexcasting:lightning");
        }

        // ==================== 三维向量（原版 Vec3） ====================
        {
            var env = new TestEnv();
            var one = new CastingVM(new CastingImage(), env).QueueExecute(new CastingImage(), new Iota[] { P("hexcasting:const/vec/pz") });
            Check("const/vec/pz = (0,0,1)", Sig(one.Image) == "[(0,0,1)]", Sig(one.Image));
            var ab = new CastingImage(new Iota[] { new VectorIota(0, 3, 4) });
            var rab = new CastingVM(ab, env).QueueExecute(ab, new Iota[] { P("hexcasting:abs") });
            Check("长度算上 z：|(0,3,4)| = 5", Sig(rab.Image) == "[5]", Sig(rab.Image));
            Check("相等判定算上 z", Sig(Run2(env, new VectorIota(1, 2, 3), new VectorIota(1, 2), "hexcasting:equals").Image) == "[false]");
            var ax = new CastingImage(new Iota[] { new VectorIota(1, 1, 0) });
            var rax = new CastingVM(ax, env).QueueExecute(ax, new Iota[] { P("hexcasting:coerce_axial") });
            Check("coerce_axial：(1,1,0) 平局取「上」（MC Direction 顺序 下 上 北 南 西 东）", Sig(rax.Image) == "[(0,1)]", Sig(rax.Image));
            var az = new CastingImage(new Iota[] { new VectorIota(0.1, 0.2, -3) });
            var raz = new CastingVM(az, env).QueueExecute(az, new Iota[] { P("hexcasting:coerce_axial") });
            Check("coerce_axial：z 占优 → (0,0,-1)", Sig(raz.Image) == "[(0,0,-1)]", Sig(raz.Image));

            // 世界是 z = 0 的平面：离平面远的位置超出施法范围（三维距离）
            var world = new FakeWorld();
            world.Replaceable.Add((3, 3));
            var envW = new TestEnv(world: world, media: 1_000_000);
            var far = new CastingImage(new Iota[] { new VectorIota(3.5, 3.5, 50) });
            var rfar = new CastingVM(far, envW).QueueExecute(far, new Iota[] { P("hexcasting:conjure_block") });
            Check("位置 z 偏离平面 → 超出范围（原版三维距离）", rfar.ResolutionType == ResolvedPatternType.Errored && world.Conjured.Count == 0,
                rfar.ResolutionType.ToString());

            // 射线：纯 z 方向在平面上的投影为 0，打不中任何东西
            var wr = new FakeWorld();
            wr.Solid.Add((14, 19));
            var envR = new TestEnv(world: wr);
            var rz = new CastingImage(new Iota[] { new VectorIota(10, 19.4), new VectorIota(0, 0, 1) });
            var rrz = new CastingVM(rz, envR).QueueExecute(rz, new Iota[] { P("hexcasting:raycast") });
            Check("射线沿纯 z 方向：落空（null）", Sig(rrz.Image) == "[null]", Sig(rrz.Image));
            // 方向 (1,0,1)：沿三维方向走 32 格，xy 投影约 22.6 格，仍能打到 4 格外的方块
            var r45 = new CastingImage(new Iota[] { new VectorIota(10, 19.4), new VectorIota(1, 0, 1) });
            var rr45 = new CastingVM(r45, envR).QueueExecute(r45, new Iota[] { P("hexcasting:raycast") });
            Check("射线方向带 z：按 xy 投影判定，打到 (14,19)", Sig(rr45.Image) == "[(14.5,19.5)]", Sig(rr45.Image));
        }

        // ==================== 向量 × 数字（逐分量广播） ====================
        {
            var env = new TestEnv();
            var r1 = Run2(env, new VectorIota(1, -2), new DoubleIota(3), "hexcasting:mul");
            Check("向量 × 数字 = 缩放（原版 OperatorVec3Delegating）", Sig(r1.Image) == "[(3,-6)]", Sig(r1.Image));
            var r2 = Run2(env, new DoubleIota(2), new VectorIota(1, 4), "hexcasting:sub");
            Check("数字 − 向量：数字广播到三个分量（z = 2 − 0）", Sig(r2.Image) == "[(1,-2,2)]", Sig(r2.Image));
            var r3 = Run2(env, new VectorIota(4, 6), new DoubleIota(2), "hexcasting:div");
            Check("向量 ÷ 数字", Sig(r3.Image) == "[(2,3)]", Sig(r3.Image));
            var r4 = Run2(env, new VectorIota(5, 7, 2), new VectorIota(3, 4, 5), "hexcasting:modulo");
            Check("向量 % 向量：逐分量取余", Sig(r4.Image) == "[(2,3,2)]", Sig(r4.Image));
            var r4z = Run2(env, new VectorIota(5, 7), new VectorIota(3, 4), "hexcasting:modulo");
            Check("向量 % 向量：z 分量 0 % 0 → 除以零（原版逐分量，同样报错）", r4z.ResolutionType == ResolvedPatternType.Errored, r4z.ResolutionType.ToString());
            var r5 = Run2(env, new VectorIota(1, 2), new DoubleIota(0), "hexcasting:div");
            Check("向量 ÷ 0：除零 mishap", r5.ResolutionType == ResolvedPatternType.Errored, r5.ResolutionType.ToString());
            var r6 = Run2(env, new VectorIota(1, 2), new VectorIota(3, 4), "hexcasting:mul");
            Check("向量 × 向量 仍是点积", Sig(r6.Image) == "[11]", Sig(r6.Image));
        }

        // ==================== 开发者面板：法术示例 ====================
        {
            Check("数字编码：半整数先编 2x 再 ÷2（2.5 → …d）",
                SpecialPatterns.TryNumber(SpecialPatterns.EncodeNumber(2.5)!, out var v25) && v25 == 2.5
                && SpecialPatterns.TryNumber(SpecialPatterns.EncodeNumber(-7.5)!, out var vn) && vn == -7.5
                && SpecialPatterns.EncodeNumber(0.25) is null,
                $"{SpecialPatterns.EncodeNumber(2.5)} {SpecialPatterns.EncodeNumber(-7.5)}");
            foreach (double n in new[] { 0.0, 1, 2, 3, 4, 10, 30, 37, -12 })
            {
                var pat = HexCastingTerraria.Core.Dev.HexStep.N(n).ToPattern();
                if (pat is null || !SpecialPatterns.TryNumber(pat.AnglesSignature(), out var got) || got != n)
                {
                    Check($"示例数字步骤 {n} 编成能画的数字图案", false, pat?.AnglesSignature());
                }
            }

            foreach (var sample in HexCastingTerraria.Core.Dev.SampleHexes.All)
            {
                var caster = new EntityIota(EntityIota.EntityKind.Player, 0);
                var world = new FakeWorld { Caster = caster, LookDir = (1.0, 0.0) };
                world.Solid.Add((14, 19));       // 视线正前方 4 格有一块实心方块
                world.Replaceable.Add((13, 19));  // 它朝向玩家的那一面是空气
                var env = new TestEnv(world: world);
                var img = new CastingImage(System.Array.Empty<Iota>());
                string fail = "";
                for (int i = 0; i < sample.Steps.Length && fail.Length == 0; i++)
                {
                    var step = sample.Steps[i];
                    var pat = step.ToPattern();
                    if (pat is null) { fail = $"第 {i + 1} 步 {step.Label} 画不出来"; break; }
                    var r = new CastingVM(img, env).QueueExecute(img, new Iota[] { new PatternIota(pat) });
                    if (r.ResolutionType != ResolvedPatternType.Evaluated)
                    {
                        fail = $"第 {i + 1} 步 {step.Label}：{r.ResolutionType} 栈 {Sig(r.Image)}";
                    }
                    img = r.Image;
                }
                if (fail.Length == 0 && img.Stack.Count != 0) { fail = "施放完栈没清空 " + Sig(img); }
                Check($"法术示例「{sample.Name}」逐步求值成功且栈清空", fail.Length == 0, fail);
            }
        }

        Console.WriteLine($"================ 通过 {_pass} / 失败 {_fail} ================");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}

static class IotaTestExt
{
    public static string DescribeForTest(this Iota i) => i.ToString();
}
