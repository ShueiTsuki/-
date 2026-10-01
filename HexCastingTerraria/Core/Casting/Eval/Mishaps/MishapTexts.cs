using System.Globalization;

namespace HexCastingTerraria.Core.Casting.Eval.Mishaps;

/// <summary>
/// MishapInvalidIota 的「本应接受什么」：上游 hexcasting.mishap.invalid_value.* 的官方中文。
///
/// 图案报「参数不对」时一律从这里取，不要再各处手写（曾经有「列表」「非负整数」「0 ~ 13 之间的乐器编号」之类自造的说法，
/// 和原版的说法、量词都对不上）。带数字的几条照上游传参：整数原样，小数按 Java 的 Double.toString（10.0 写成「10.0」）。
/// </summary>
public static class InvalidValue
{
    /// <summary>class.double</summary>
    public const string Double = "一个数";
    /// <summary>class.boolean</summary>
    public const string Boolean = "一个布尔值";
    /// <summary>class.vector</summary>
    public const string Vector = "一个向量";
    /// <summary>class.list</summary>
    public const string List = "一个列表";
    /// <summary>class.pattern</summary>
    public const string Pattern = "一个图案";
    /// <summary>class.continuation</summary>
    public const string Continuation = "一个跳转iota";
    /// <summary>class.garbage</summary>
    public const string Garbage = "垃圾";
    /// <summary>class.null</summary>
    public const string Null = "Null";
    /// <summary>class.entity</summary>
    public const string Entity = "一个实体";
    /// <summary>class.entity.mob（上游 getMob）</summary>
    public const string EntityMob = "一个生物";
    /// <summary>class.entity.item（上游 getItemEntity）</summary>
    public const string EntityItem = "一个物品实体";
    /// <summary>class.entity.player（上游 getPlayer）</summary>
    public const string EntityPlayer = "一个玩家";
    /// <summary>class.entity.living（上游 getLivingEntityButNotArmorStand）</summary>
    public const string EntityLiving = "一个生物实体";
    /// <summary>class.entity.item_holder（上游 OpItemEquality）</summary>
    public const string EntityItemHolder = "一个持有物品的实体";
    /// <summary>class.entity_or_vector（上游 OpIgnite）</summary>
    public const string EntityOrVector = "一个实体或向量";
    /// <summary>class.unknown</summary>
    public const string ClassUnknown = "（未知，这是个漏洞）";
    /// <summary>numvec（上游 getNumOrVec）</summary>
    public const string NumVec = "一个数或向量";
    /// <summary>numlist（上游 getLongOrList）</summary>
    public const string NumList = "一个整数或列表";
    /// <summary>double.positive（上游 getPositiveDouble；注意原版的「正」含 0）</summary>
    public const string DoublePositive = "一个正数";
    /// <summary>int（上游 getInt / getLong）</summary>
    public const string Int = "一个整数";
    /// <summary>int.positive（上游 getPositiveInt / getPositiveLong；含 0）</summary>
    public const string IntPositive = "一个正整数";
    /// <summary>evaluatable（上游 evaluatable()：Hermes / Thoth 要执行的东西）</summary>
    public const string Evaluatable = "可运行的事物";
    /// <summary>bool_commute</summary>
    public const string BoolCommute = "一个布尔值、0或1";

    /// <summary>double.positive.less「一个小于%d的正数」</summary>
    public static string DoublePositiveLess(double max) => $"一个小于{JavaDouble(max)}的正数";

    /// <summary>double.positive.less.equal「一个小于等于%d的正数」</summary>
    public static string DoublePositiveLessEqual(double max) => $"一个小于等于{JavaDouble(max)}的正数";

    /// <summary>double.between「一个介于%d和%d之间的数」</summary>
    public static string DoubleBetween(double min, double max) => $"一个介于{JavaDouble(min)}和{JavaDouble(max)}之间的数";

    /// <summary>int.positive.less「一个小于%d的正整数」</summary>
    public static string IntPositiveLess(long max) => $"一个小于{max}的正整数";

    /// <summary>int.positive.less.equal「一个小于等于%d的正整数」</summary>
    public static string IntPositiveLessEqual(long max) => $"一个小于等于{max}的正整数";

    /// <summary>int.between「一个介于%d和%d之间的整数」</summary>
    public static string IntBetween(long min, long max) => $"一个介于{min}和{max}之间的整数";

    /// <summary>
    /// 上游把 Double 直接塞进翻译参数，MC 用 String.valueOf 转成文字（Java 的 Double.toString）：
    /// 整数值带「.0」（10.0、-1.0），其他按最短表示。
    /// </summary>
    public static string JavaDouble(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        if (d == System.Math.Floor(d) && System.Math.Abs(d) < 1e7)
        {
            return d.ToString("0", CultureInfo.InvariantCulture) + ".0";
        }
        return d.ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 「需要什么」：上游 hexcasting.mishap.bad_item.* / bad_block.* 的官方中文
///（MishapBadHeldItem、MishapBadItem、MishapBadEntity、MishapBadBlock、MishapLackingHotbarItem 的参数）。
/// </summary>
public static class Wanted
{
    /// <summary>bad_item.iota</summary>
    public const string IotaHolder = "一个可以存储iota的地方";
    /// <summary>bad_item.iota.read</summary>
    public const string IotaRead = "一个可以读出iota的地方";
    /// <summary>bad_item.iota.write</summary>
    public const string IotaWrite = "一个可以写入iota的地方";
    /// <summary>bad_item.iota.readonly「一个能够接受%s的地方」，%s 是要写的 iota 的显示</summary>
    public static string IotaReadonly(string datumDisplay) => $"一个能够接受{datumDisplay}的地方";
    /// <summary>bad_item.media（上游 OpRecharge）</summary>
    public const string Media = "含有媒质的物品";
    /// <summary>bad_item.media_for_battery（上游 OpMakeBattery / OpMakePackagedSpell）</summary>
    public const string MediaForBattery = "天然含有媒质的物品";
    /// <summary>bad_item.only_one</summary>
    public const string OnlyOne = "仅一个物品";
    /// <summary>bad_item.eraseable</summary>
    public const string Eraseable = "一个可清除的物品";
    /// <summary>bad_item.bottle</summary>
    public const string Bottle = "一个玻璃瓶";
    /// <summary>bad_item.rechargable</summary>
    public const string Rechargeable = "一个可重新充能的物品";
    /// <summary>bad_item.colorizer</summary>
    public const string Colorizer = "一个染色剂";
    /// <summary>bad_item.variant</summary>
    public const string Variant = "一个有变种的物品";
    /// <summary>bad_item.placeable（上游 OpPlaceBlock 找快捷栏）</summary>
    public const string Placeable = "一个可放置的物品";

    /// <summary>bad_block.replaceable（上游 OpConjureBlock / OpPlaceBlock）</summary>
    public const string Replaceable = "一个可放置方块的地方";
    /// <summary>bad_block.sapling（上游 OpEdifySapling）</summary>
    public const string Sapling = "一个树苗";
}
