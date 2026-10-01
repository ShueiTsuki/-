using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 打包法术的种类。对应源项目的三个物品：cypher（符纸，一次性）、
/// trinket（饰品，可重复用）、artifact（法器，容量更大）。
///
/// 三者的**行为差别只在容量与是否消耗**，所以用同一个枚举而不是三个类。
/// </summary>
public enum PackagedSpellKind
{
    /// <summary>符纸：一次性，用完就没。对应 `hexcasting:cypher`。</summary>
    Cypher,

    /// <summary>饰品：可重复使用，用完里面存的媒质就罢工。对应 `hexcasting:trinket`。</summary>
    Trinket,

    /// <summary>法器：容量最大的可重复使用版本。对应 `hexcasting:artifact`。</summary>
    Artifact,
}

/// <summary>
/// 施法环境：抽象出「谁在施法、媒质从哪来、能不能用大法术」。
/// 移植自 at.petrak.hexcasting.api.casting.eval.CastingEnvironment（简化掉 MC 专属部分）。
///
/// 设计：本类**不引用任何 Terraria 类型**，以便离线测试 VM；
/// 具体实现（玩家施法环境）放在 Content/ 下。
/// </summary>
public abstract class CastingEnvironment
{
    /// <summary>求值步数上限。超过则触发 MishapEvalTooMuch（对应源项目 maxOpCount）。</summary>
    public virtual int MaxOpCount() => DefaultMaxOpCount;

    /// <summary>源项目配置默认值。</summary>
    public const int DefaultMaxOpCount = 100_000;

    private double _costModifier = 1.0;

    /// <summary>
    /// 图案执行前的检查。源项目在这里查禁用表并设置成本修正系数；
    /// 泰拉侧暂无禁用表，只重置系数为 1。
    /// </summary>
    public virtual void PrecheckAction(PatternDef? def)
    {
        _costModifier = 1.0;
    }

    /// <summary>
    /// 尝试扣除媒质，返回**还未付清**的量（&lt;=0 表示足够）。
    /// 移植自源项目 extractMedia：先乘成本修正，再交给具体环境。
    /// </summary>
    public long ExtractMedia(long cost, bool simulate)
    {
        cost = (long)(cost * _costModifier);
        return ExtractMediaEnvironment(cost, simulate);
    }

    /// <summary>具体环境的媒质扣除（玩家背包里的媒质物品 / 打包法术自带的媒质 / 法术环的原动力等）。</summary>
    protected abstract long ExtractMediaEnvironment(long cost, bool simulate);

    /// <summary>是否能施放「大法术」（源项目里对应 enlightenment 成就）。</summary>
    public virtual bool IsEnlightened() => false;

    /// <summary>每条图案执行后回调。</summary>
    public virtual void PostExecution(CastResult result) { }

    /// <summary>整次施法结束后回调。</summary>
    public virtual void PostCast(CastingImage image) { }

    /// <summary>
    /// 法术环的状态；**非环环境返回 null**。
    ///
    /// 用虚属性而不是 `env is CircleCastingEnvironment` 类型判定：
    /// 后者会让离线测试无法构造「环环境」（测试环境继承的是本基类，不是那个具体类型），
    /// 而 `circle/*` 三个图案的逻辑恰恰值得离线测。
    /// </summary>
    public virtual Circles.CircleState? Circle => null;

    /// <summary>本环境是否允许过载（用生命换媒质）。</summary>
    public virtual bool CanOvercast() => false;

    /// <summary>
    /// 未启蒙却施放了大法术（原版 FAIL_GREAT_SPELL_TRIGGER → 进度「盲目绘制」）。
    /// 原版里这是**解锁过载**的前提：玩家环境据此记下，以后才允许用生命换媒质。
    /// </summary>
    public virtual void OnFailedGreatSpell() { }

    /// <summary>原版 mishapEnvironment.dropHeldItems：把手上的东西丢出去。没有实体施法者的环境什么都不做。</summary>
    public virtual void DropHeldItems() { }

    // ── mishap 惩罚（源项目 MishapEnvironment / PlayerBasedMishapEnv）───────────
    // 没有实体施法者的环境（法术环）默认什么都不做；玩家环境在 Content/PlayerCastingEnvironment.cs 里实现。

    /// <summary>原版 yeetHeldItemsTowards：把手上的东西朝某个位置（图格）甩出去。</summary>
    public virtual void YeetHeldItemsTowards(double x, double y) { }

    /// <summary>原版 damage(healthProportion)：扣掉**当前**生命的这个比例（无视护甲）。</summary>
    public virtual void MishapDamage(double healthProportion) { }

    /// <summary>原版 drown()：氧气清零；本来就缺氧时再扣一点血。</summary>
    public virtual void MishapDrown() { }

    /// <summary>原版 blind(ticks)：失明。参数是 MC 游戏刻（20/秒）。</summary>
    public virtual void MishapBlind(int mcTicks) { }

    /// <summary>原版 removeXp(amount)。泰拉没有经验值 —— 默认不做（见 AUDIT_VS_ORIGINAL.md）。</summary>
    public virtual void MishapRemoveXp(int amount) { }

    /// <summary>原版 MishapNoSpellCircle：把整个背包（含盔甲）掉出来。</summary>
    public virtual void MishapDropInventory() { }

    /// <summary>
    /// 把一条消息发给施法者（聊天框）。
    /// 对应源项目 CastingEnvironment.printMessage。
    /// 用于：mishap 错误消息、print 图案的输出、未启蒙提示。
    /// </summary>
    public virtual void PrintMessage(string message) { }

    /// <summary>附属调试器（HexDebug）：正在调试这次施法时不为 null，见 <see cref="ICastDebugObserver"/>。</summary>
    public ICastDebugObserver? DebugObserver { get; set; }

    /// <summary>
    /// 世界访问。
    ///
    /// 默认 **null** = 「本环境不支持世界图案」—— 离线测试与纯 VM 场景就是这种状态。
    /// 世界图案必须先 <see cref="RequireWorld"/>，取不到就报 mishap，
    /// 而不是静默返回零向量（那会变成 NaN 静默故障）。
    /// </summary>
    public virtual ICastingWorld? World => null;

    /// <summary>
    /// 取得世界访问，取不到则抛 mishap。
    /// 世界图案一律通过它拿 <see cref="ICastingWorld"/>，避免各处重复判空。
    /// </summary>
    public ICastingWorld RequireWorld()
        => World ?? throw new MishapNoWorld();

    /// <summary>
    /// 解析实体 iota 并校验存活与范围，返回可直接查询的实体 iota。
    /// 对应源项目的 `args.getEntity(idx, argc)` + `assertEntityInRange`。
    ///
    /// 两条校验都**必须做**：索引会因实体死亡而失效（不校验会读到别的实体），
    /// 范围校验则是原作的核心平衡机制（不能隔着半张地图操作别人的怪）。
    /// </summary>
    public EntityIota ResolveEntity(Iota iota)
    {
        var world = RequireWorld();

        if (iota is not EntityIota entity)
        {
            throw new MishapInvalidIota(iota, "实体");
        }

        if (!world.IsAlive(entity))
        {
            throw new MishapInvalidIota(iota, "仍存活的实体");
        }

        if (!world.IsInRange(entity))
        {
            throw new MishapEntityTooFarAway(entity);
        }

        return entity;
    }

    /// <summary>
    /// 校验一个世界坐标（**图格单位**）在施法范围内。
    /// 对应源项目的 `assertVecInRange`。
    ///
    /// 射线类图案必须先校验**起点**，命中点还要再校验一次 ——
    /// 否则可以站在范围边缘把射线打向远处，隔着半张地图探测地形。
    /// </summary>
    public void AssertVecInRange(double x, double y, double z = 0.0)
    {
        // 世界是 z = 0 的平面：z 不为 0 的点与施法者的距离要把 z 算进去（原版就是三维距离）
        if (!RequireWorld().IsVecInRange(x, y, z))
        {
            throw new MishapLocationTooFarAway(x, y);
        }
    }

    // ── 手持数据载体（read / write / erase …）──────────────────────────
    //
    // 原版 getHeldItemToOperateOn(谓词)：按 [另一只手, 施法的手] 的顺序，找第一个满足谓词的物品。
    // 下面每个方法都是「在这两个位置里找」—— 具体是哪两格由环境决定
    //（玩家：见 PlayerCastingEnvironment.PrimarySlots；法术环没有手，全是默认值）。
    //
    // 载体的规则照原版 IotaHolderItem：
    //   readIota     —— 存着的东西；原版没有哪个物品定义「空值」（emptyIota），空载体 read 就是 mishap
    //   writeable()  —— `writable` 图案问的就是它
    //   canWrite(d)  —— 肯不肯收 d；d = null 表示清除（核心：封了也能清，清完顺带解封；念珠：不能清）

    /// <summary>`read`：第一个**读得出东西**的载体里的 iota。都读不出 → null（调用方报 mishap）。</summary>
    public virtual Iota? ReadHeldIota() => null;

    /// <summary>手上（两个位置之一）有没有数据载体，不管空不空。</summary>
    public virtual bool HasHeldStorage() => false;

    /// <summary>`writable`：第一个载体的 writeable()。</summary>
    public virtual bool IsHeldWritable() => false;

    /// <summary>有没有载体肯收 <paramref name="datum"/>（原版 writeIota(datum, simulate: true)）。null = 清除。</summary>
    public virtual bool CanWriteHeld(Iota? datum) => false;

    /// <summary>`write`：写进第一个肯收的载体。返回是否写进去了。</summary>
    public virtual bool WriteHeldIota(Iota value) => false;

    /// <summary>
    /// `erase` 的目标：第一个「装着咒术的打包法术」或「肯被清除的载体」。
    /// 返回它的堆叠数（原版消耗 = 粉尘 × 堆叠数），0 = 没有可清除的东西。
    /// </summary>
    public virtual int HeldEraseableCount() => 0;

    /// <summary>`erase`：清掉那个目标里的咒术和 / 或 iota。</summary>
    public virtual void EraseHeld() { }

    /// <summary>
    /// 施法者的「哨卫」（`sentinel/*` 图案）。
    /// 移植自源项目 `Sentinel(extendsRange, position, dimension)` —— 那边挂在玩家身上。
    ///
    /// 它是一个**持久**的坐标标记：放下去之后跨施法、跨重登都还在，
    /// 大哨卫还能把施法范围延伸到它周围（见 <see cref="SentinelRadiusTiles"/>）。
    /// </summary>
    public readonly record struct SentinelState(double X, double Y, bool Great);

    /// <summary>哨卫半径（图格）。对齐源项目 `PlayerBasedCastEnv.DEFAULT_SENTINEL_RADIUS = 16.0`。</summary>
    public const double SentinelRadiusTiles = 16.0;

    /// <summary>当前哨卫。null = 没放。</summary>
    public virtual SentinelState? Sentinel => null;

    // ── 打包法术与媒质瓶（craft/* 图案）────────────────────────────

    /// <summary>
    /// 手持的**空的**打包法术物品是哪一种。null = 手上没有（或那个已经装过东西了）。
    /// 对应源项目 `env.getHeldItemToOperateOn { isValid && !hexHolder.hasHex() }`。
    /// </summary>
    public virtual PackagedSpellKind? HeldEmptyPackagedSpell => null;

    /// <summary>手上（另一只手优先）第一件空的打包物品的制作键（见 ItemPackagedSpell.CraftKey）；没有为 null。</summary>
    public virtual string? HeldEmptyPackagedKey => HeldEmptyPackagedSpell?.ToString();

    /// <summary>手上（两个位置之一）第一个「空瓶」的堆叠数；没有 → 0。对应源项目 `PHIAL_BASE` 标签 + `count != 1` 检查。</summary>
    public virtual int HeldPhialCount() => 0;

    /// <summary>把图案与媒质装进手持的打包法术物品。返回是否成功。</summary>
    public virtual bool FillHeldPackagedSpell(System.Collections.Generic.IReadOnlyList<Iota> patterns, long media)
        => false;

    /// <summary>把手持的空瓶换成一个装满的媒质瓶（`craft/battery`）。返回是否成功。</summary>
    public virtual bool CraftBatteryHeld(long media) => false;

    /// <summary>
    /// 手上可充能物品（媒质瓶 / 装过法术的打包法术）还能装多少媒质（源项目 canRecharge + insertMedia(-1, true)）。
    /// 手上没有可充能物品 → -1。
    /// </summary>
    public virtual long HeldRechargeSpace() => -1;

    /// <summary>往手上的可充能物品里装媒质（`recharge`），装不下的截掉。</summary>
    public virtual void ChargeHeld(long media) { }

    /// <summary>
    /// 把手持物品的「变体编号」推进一格（`cycle_variant`）。
    /// 返回 false 表示手上的东西没有变体。
    /// </summary>
    public virtual bool CycleHeldVariant() => false;

    /// <summary>手持物品有没有「变体」可切。对应源项目 `findVariantHolder(stack) != null`。</summary>
    public virtual bool HeldHasVariants() => false;

    /// <summary>
    /// 施法者手上有没有可用的**颜料**（染色剂）。返回物品类型，0 = 没有。
    /// 对应源项目 `env.getHeldItemToOperateOn(IXplatAbstractions::isPigment)`。
    /// </summary>
    public virtual int FindPigmentItem() => 0;

    /// <summary>消耗一份颜料，并把施法者的法术配色换成它。</summary>
    public virtual void ApplyPigment(int itemType) { }

    /// <summary>从施法者身上扣掉一份这种颜料（原版 withdrawItem）；扣到了返回 true。</summary>
    public virtual bool WithdrawPigment(int itemType) => false;

    /// <summary>放置哨卫。</summary>
    public virtual void SetSentinel(double x, double y, bool great) { }

    /// <summary>移除哨卫。</summary>
    public virtual void ClearSentinel() { }

    /// <summary>
    /// 某个点是否落在**大哨卫**的延伸范围里。
    /// 移植自源项目 `PlayerBasedCastEnv.isVecInRangeEnvironment` 的第一段分支。
    ///
    /// 注意判定里那个 `+1e-10`：源项目注释写明是为了「特定角度下的浮点误差」，
    /// 少了它会出现「明明在圈内却偶尔失败」的偶发 bug。
    /// </summary>
    public bool IsInSentinelRange(double x, double y)
    {
        if (Sentinel is not { Great: true } s) return false;

        double dx = x - s.X;
        double dy = y - s.Y;
        return dx * dx + dy * dy <= SentinelRadiusTiles * SentinelRadiusTiles + 1e-10;
    }
    /// <summary>
    /// 从参数里取一个向量分量。对应源项目的 `args.getVec3(idx, argc)`。
    /// 参数不是向量时报 MishapInvalidIota，而不是静默当成零向量。
    /// </summary>
    public static (double X, double Y) RequireVec(Iota iota, string what)
    {
        if (iota is not VectorIota v)
        {
            throw new MishapInvalidIota(iota, what);
        }
        return (v.X, v.Y);
    }

    /// <summary>同上，带 z（源项目的向量是三维的；作为位置时 z 参与范围判定）。</summary>
    public static (double X, double Y, double Z) RequireVec3(Iota iota, string what)
    {
        if (iota is not VectorIota v)
        {
            throw new MishapInvalidIota(iota, what);
        }
        return (v.X, v.Y, v.Z);
    }

    /// <summary>
    /// 取一个整数（容差判定）。对应源项目 `getPositiveInt` / `getPositiveIntUnder` 系列。
    /// **要求是整数值的双精度** —— `3.7` 报错而不是截断。
    /// </summary>
    public static int RequireIndex(Iota iota)
    {
        if (iota is DoubleIota d)
        {
            double rounded = System.Math.Round(d.Value, System.MidpointRounding.AwayFromZero);
            if (System.Math.Abs(d.Value - rounded) <= DoubleIota.Tolerance
                && rounded >= int.MinValue && rounded <= int.MaxValue)
            {
                return (int)rounded;
            }
        }
        throw new MishapInvalidIota(iota, "整数");
    }

    /// <summary>同上，但返回 long（`swizzle` 的 Lehmer 码可能很大）。</summary>
    public static long RequireIndexLong(Iota iota)
    {
        if (iota is DoubleIota d)
        {
            double rounded = System.Math.Round(d.Value, System.MidpointRounding.AwayFromZero);
            if (System.Math.Abs(d.Value - rounded) <= DoubleIota.Tolerance
                && rounded >= long.MinValue && rounded <= long.MaxValue)
            {
                return (long)rounded;
            }
        }
        throw new MishapInvalidIota(iota, "整数");
    }

    /// <summary>
    /// 源项目 `getPositiveDouble`：`0 <= x`。注意：原版的「positive」**包含 0** ——
    /// 这里曾在多处手写成 `x <= 0` 报错（爆炸威力、药水时长、区域半径、飞行参数），把 0 错杀了。
    /// </summary>
    public static double RequirePositiveDouble(Iota iota, string what)
    {
        if (iota is DoubleIota d && d.Value >= 0)
        {
            return d.Value;
        }
        throw new MishapInvalidIota(iota, what);
    }

    /// <summary>源项目 `getPositiveDoubleUnderInclusive`：`0 <= x <= max`（闭区间）。</summary>
    public static double RequirePositiveDoubleUnderInclusive(Iota iota, double max, string what)
    {
        if (iota is DoubleIota d && d.Value >= 0 && d.Value <= max)
        {
            return d.Value;
        }
        throw new MishapInvalidIota(iota, what);
    }

    /// <summary>源项目 `getDoubleBetween`：`min <= x <= max`（闭区间）。</summary>
    public static double RequireDoubleBetween(Iota iota, double min, double max, string what)
    {
        if (iota is DoubleIota d && d.Value >= min && d.Value <= max)
        {
            return d.Value;
        }
        throw new MishapInvalidIota(iota, what);
    }

    /// <summary>
    /// 从参数里取一个数值。对应源项目的 `args.getDouble(idx, argc)`。
    /// **注意**：与列表索引不同，这里不要求「整数值」——
    /// 距离、倍率一类参数本来就允许小数。
    /// </summary>
    public static double RequireDouble(Iota iota, string what)
    {
        if (iota is not DoubleIota d)
        {
            throw new MishapInvalidIota(iota, what);
        }
        return d.Value;
    }
}
