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

    /// <summary>具体环境的媒质扣除（玩家媒质池 / 物品 / 法术环等）。</summary>
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

    /// <summary>
    /// 把一条消息发给施法者（聊天框）。
    /// 对应源项目 CastingEnvironment.printMessage。
    /// 用于：mishap 错误消息、print 图案的输出、未启蒙提示。
    /// </summary>
    public virtual void PrintMessage(string message) { }

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
    public void AssertVecInRange(double x, double y)
    {
        if (!RequireWorld().IsVecInRange(x, y))
        {
            throw new MishapLocationTooFarAway(x, y);
        }
    }

    /// <summary>
    /// 读取施法者**手持**的数据载体物品里的一个 iota。
    /// 对应源项目 `env.getHeldItemToOperateOn` + `DataHolder.readIota`。
    ///
    /// 返回 null 表示「手上没有可读的东西」，调用方应当报 mishap。
    /// 默认环境（纯 VM）没有手持物品，返回 null。
    /// </summary>
    public virtual Iota? ReadHeldIota() => null;

    /// <summary>
    /// 把一个 iota 写进手持的数据载体。返回 false 表示写不进去。
    /// </summary>
    public virtual bool WriteHeldIota(Iota value) => false;

    /// <summary>
    /// 手上是否拿着一个**数据载体**（不管里面有没有东西）。
    ///
    /// `readable` / `writable` 必须区分「没拿载体」与「拿了但内容是空的」——
    /// 只看 <see cref="ReadHeldIota"/> 的话两者都是 null，判定会错。
    /// </summary>
    public virtual bool HasHeldStorage() => false;

    /// <summary>手持载体是否**可写**。只读载体（卷轴）返回 false，对应源项目 `writeable()`。</summary>
    public virtual bool IsHeldWritable() => false;

    /// <summary>
    /// 清空手持载体里的 iota（`erase` 图案）。
    /// 返回是否有东西被清掉 —— 空载体应当报 mishap 而不是静默成功。
    /// </summary>
    public virtual bool ClearHeldIota() => false;

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

    /// <summary>手持的是不是「空瓶」一类的可充能容器。对应源项目 `PHIAL_BASE` 标签。</summary>
    public virtual bool IsHeldPhialBase() => false;

    /// <summary>把图案与媒质装进手持的打包法术物品。返回是否成功。</summary>
    public virtual bool FillHeldPackagedSpell(System.Collections.Generic.IReadOnlyList<Iota> patterns, long media)
        => false;

    /// <summary>把手持的空瓶换成一个装满的媒质瓶（`craft/battery`）。返回是否成功。</summary>
    public virtual bool CraftBatteryHeld(long media) => false;

    /// <summary>
    /// 把手持物品的「变体编号」推进一格（`cycle_variant`）。
    /// 返回 false 表示手上的东西没有变体。
    /// </summary>
    public virtual bool CycleHeldVariant() => false;

    /// <summary>手持物品有没有「变体」可切。对应源项目 `findVariantHolder(stack) != null`。</summary>
    public virtual bool HeldHasVariants() => false;

    /// <summary>
    /// 施法者身上有没有可用的**颜料**（泰拉侧 = 染料）。返回物品类型，0 = 没有。
    /// 对应源项目 `env.getHeldItemToOperateOn(IXplatAbstractions::isPigment)`。
    /// </summary>
    public virtual int FindPigmentItem() => 0;

    /// <summary>消耗一份颜料，并把施法者的法术配色换成它。</summary>
    public virtual void ApplyPigment(int itemType) { }
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
