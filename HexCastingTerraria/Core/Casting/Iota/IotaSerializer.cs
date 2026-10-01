using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>
/// iota 的序列化与反序列化。
///
/// ## 为什么需要类型标签
///
/// 早期版本的 `Serialize()` 直接吐裸值，结果是**不可往返的**：
///
/// | iota | 裸值 | 冲突 |
/// |---|---|---|
/// | `NullIota` | `null` | 与 `GarbageIota` **完全相同** |
/// | `GarbageIota` | `null` | 同上 |
/// | `VectorIota` | `double[2]` | 与 `EntityIota` **完全相同** |
/// | `EntityIota` | `double[2]` | 同上 |
///
/// 也就是说：存一个空值再读出来，可能变成「垃圾」；
/// 存一个坐标再读出来，可能变成「某个实体的引用」。
/// 这类错误**不会报错**，只会让法术行为诡异地变化 —— 属于最难查的一类。
///
/// 现在统一用带标签的信封：`{ "t": <种类>, "v": <载荷> }`。
///
/// ## 允许的载荷类型
///
/// `null` / `bool` / `double` / `string` / `List&lt;object?&gt;` / `Dictionary&lt;string, object?&gt;`
///
/// 注意：这**不是** `TagCompound` 能直接收的形状（它不收字典、不收 null、列表必须同类型）——
/// 存档一律走游戏侧的 `Content/Net/IotaTag`，不要把信封直接塞进 tag。
/// </summary>
public static class IotaSerializer
{
    /// <summary>信封里的「种类」键。</summary>
    public const string KindKey = "t";

    /// <summary>信封里的「载荷」键。</summary>
    public const string ValueKey = "v";

    // 种类标签。用短字符串而不是枚举，是为了存档里可读、跨版本也好兼容。
    public const string KindNull = "null";
    public const string KindGarbage = "garbage";
    public const string KindBool = "bool";
    public const string KindDouble = "double";
    public const string KindVec = "vec";
    public const string KindEntity = "entity";
    public const string KindPattern = "pattern";
    public const string KindList = "list";
    public const string KindContinuation = "continuation";

    /// <summary>构造一个信封。</summary>
    public static Dictionary<string, object?> Envelope(string kind, object? value)
        => new() { [KindKey] = kind, [ValueKey] = value };

    // ── 附属的 iota 种类 ─────────────────────────────────────────────
    //
    // 附属（HexParse、HexDebug…）打开时登记自己的种类（键用带命名空间的标签，如 "hexparse:comment"）。
    // 没登记的种类（附属关着）读出来是 UnknownIota，原样保管，不会丢（ADDONS.md「开关的实际效果」）。

    private static readonly Dictionary<string, System.Func<object?, Iota?>> AddonKinds = new();

    /// <summary>登记一个附属的 iota 种类。<paramref name="read"/> 收载荷，载荷畸形就返回 null。</summary>
    public static void RegisterKind(string kind, System.Func<object?, Iota?> read) => AddonKinds[kind] = read;

    /// <summary>取消登记（模组卸载 / 离线测试用）。</summary>
    public static void UnregisterKind(string kind) => AddonKinds.Remove(kind);

    /// <summary>两棵信封树是否完全一样（null / bool / double / string / 列表 / 字典）。</summary>
    public static bool SameTree(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (List<object?> x, List<object?> y) => x.Count == y.Count && System.Linq.Enumerable.All(
            System.Linq.Enumerable.Range(0, x.Count), i => SameTree(x[i], y[i])),
        (Dictionary<string, object?> x, Dictionary<string, object?> y) => x.Count == y.Count && System.Linq.Enumerable.All(
            x, kv => y.TryGetValue(kv.Key, out var v) && SameTree(kv.Value, v)),
        _ => Equals(a, b),
    };

    /// <summary>
    /// 反序列化。
    ///
    /// 不认识的种类不算「无法解析」：返回一个原样保管信封的 <see cref="UnknownIota"/>。
    /// 返回 false 表示数据无法解析 —— 调用方应当**丢弃这个 iota**
    /// （例如把物品变成空的），而不是把它当成某个默认值。
    /// 静默降级成默认值会让「存档里的东西悄悄变了」这种问题极难发现。
    /// </summary>
    public static bool TryDeserialize(object? data, out Iota iota)
    {
        iota = NullIota.Instance;

        if (data is not Dictionary<string, object?> envelope)
        {
            return false;
        }

        if (envelope.TryGetValue(KindKey, out var kindObj) is false || kindObj is not string kind)
        {
            return false;
        }

        envelope.TryGetValue(ValueKey, out var value);

        switch (kind)
        {
            case KindNull:
                iota = NullIota.Instance;
                return true;

            case KindGarbage:
                iota = GarbageIota.Instance;
                return true;

            case KindBool:
                if (value is bool b)
                {
                    iota = BooleanIota.Of(b);
                    return true;
                }
                return false;

            case KindDouble:
                if (value is double d)
                {
                    iota = new DoubleIota(d);
                    return true;
                }
                return false;

            case KindVec:
                // 载荷是 [x, y, z]；旧存档（二维时期）是 [x, y]，z 取 0
                if (value is List<object?> vec && (vec.Count == 2 || vec.Count == 3)
                    && vec[0] is double vx && vec[1] is double vy)
                {
                    double vz = vec.Count == 3 && vec[2] is double z ? z : 0.0;
                    iota = new VectorIota(vx, vy, vz);
                    return true;
                }
                return false;

            case KindEntity:
                if (value is List<object?> ent && ent.Count == 2
                    && ent[0] is double kindNum && ent[1] is double indexNum)
                {
                    int k = (int)kindNum;
                    if (k < 0 || k > (int)EntityIota.EntityKind.ItemFrame) return false;
                    iota = new EntityIota((EntityIota.EntityKind)k, (int)indexNum);
                    return true;
                }
                return false;

            case KindPattern:
                if (value is List<object?> pat && pat.Count == 2
                    && pat[0] is string angles && pat[1] is double dirNum)
                {
                    int dirVal = (int)dirNum;
                    if (dirVal < 0 || dirVal > (int)HexDir.NorthWest) return false;

                    // 用 TryFromAngles 而不是裸构造：签名串可能来自被改坏的存档，
                    // 非法角度必须被拒绝，不能造出一条画不出来的图案。
                    if (!HexPattern.TryFromAngles(angles, (HexDir)dirVal, out var parsed, out _)
                        || parsed == null)
                    {
                        return false;
                    }

                    iota = new PatternIota(parsed);
                    return true;
                }
                return false;

            case KindList:
                if (value is List<object?> items)
                {
                    var children = new List<Iota>(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        // 列表里任何一项解析失败 → 整个列表失败。
                        // 保留半个列表会让「法术少了一个参数」这种问题非常难查。
                        if (!TryDeserialize(items[i], out var child)) return false;
                        children.Add(child);
                    }
                    iota = new ListIota(children);
                    return true;
                }
                return false;

            case KindContinuation:
                // 续延含帧栈，目前不支持序列化（源项目用 NBT 存帧栈）。
                // 明确失败，而不是造一个「跳转目标为空」的假续延 —— 那会跳进未定义状态。
                return false;

            default:
                if (AddonKinds.TryGetValue(kind, out var read))
                {
                    // 登记过的附属种类：载荷畸形照样拒绝（和内置种类一个标准）
                    var addonIota = read(value);
                    if (addonIota is null) return false;
                    iota = addonIota;
                    return true;
                }
                // 不认识的种类：原样保管（以前是拒绝 = 数据丢失；原版是变垃圾 = 也丢）
                iota = new UnknownIota(kind, value);
                return true;
        }
    }
}
