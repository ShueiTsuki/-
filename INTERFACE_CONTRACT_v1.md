# 接口契约 v1（A1~A4 必须遵守）

> 依据：`hexcasting-fabric-1.20.1-0.11.4.jar` 中 842 个 class 的真实清单（已提取核对，非推测）
> 目的：让多个 subagent 并行产出的代码能拼在一起。**任何实现偏离本契约，必须先在本文档提 PR 修订。**
> 状态：**待你确认**（与 `HEXCASTING_PORT_PLAN.md` 同一确认批次）

---

## 1. 命名空间与目录

```
HexCastingTerraria/
├── Core/
│   ├── Casting/
│   │   ├── Iota/            Iota 类型体系
│   │   ├── Eval/            虚拟机、执行状态、环境
│   │   ├── Mishaps/         错误类型
│   │   ├── Math/            六边形网格数学
│   │   ├── Arithmetic/      算术类型类
│   │   └── SpellList/       列表/惰性求值
│   ├── Registry/            图案注册表（PM）
│   └── Media/               媒质系统
├── Patterns/                195 条图案实现（按类别分子目录）
├── Content/                 物品、方块、饰品、ModPlayer
├── Client/                  UI 与渲染
└── Assets/                  贴图与音频
```

命名空间根：`HexCastingTerraria`。源包 `at.petrak.hexcasting.api.casting.iota.DoubleIota`
→ 目标 `HexCastingTerraria.Core.Casting.Iota.DoubleIota`。

---

## 2. Iota 类型体系（源：`api.casting.iota`，20 个 class）

源类清单已确认：`Iota`（抽象基类）、`IotaType`、`BooleanIota`、`DoubleIota`、`Vec3Iota`、
`EntityIota`、`ListIota`、`PatternIota`、`NullIota`、`GarbageIota`、`ContinuationIota`。

C# 侧契约：

```csharp
namespace HexCastingTerraria.Core.Casting.Iota;

public enum IotaKind { Null, Boolean, Double, Vector, Entity, List, Pattern, Garbage, Continuation }

public abstract class Iota
{
    public abstract IotaKind Kind { get; }
    public abstract string TypeName { get; }        // 对应 IotaType 的显示名
    public abstract Iota DeepCopy();
    public abstract bool ValueEquals(Iota other);
    public abstract object? Serialize();            // 存档用，必须是无宿主引用的纯数据
    public static Iota Deserialize(object? data);   // 与上面配对
}
```

具体类型（必须严格成对实现 `Serialize`/`Deserialize`）：

| 类 | 承载 | 备注 |
|---|---|---|
| `NullIota` | 无 | 单例，`Serialize` 返回 null |
| `BooleanIota` | `bool` | |
| `DoubleIota` | `double` | 注意：源是 double，不是 float |
| `Vec3Iota` | `Vector3-ish` | **Terraria 无 Vector3 语义**，用自建 `HexVec`（double x/y/z）；不要直接塞 `Microsoft.Xna.Framework.Vector3`（float，精度与序列化都不合适） |
| `EntityIota` | 实体引用 | Terraria 侧用 `(kind, whoAmI)` 记录 NPC/玩家/弹幕，**不持有对象引用** |
| `ListIota` | `List<Iota>` | 不可变语义：修改返回新实例 |
| `PatternIota` | 指向图案定义 | 存图案的稳定 id 字符串 |
| `GarbageIota` | 标记 | 表示"此值已被消费/不可用" |
| `ContinuationIota` | 执行续延 | 源用于 `MishapNeedsParens`/惰性求值，见第 5 节 |

---

## 3. 图案定义与注册表（源：`ActionRegistryEntry`、`PatternShapeMatch*`）

```csharp
public sealed class PatternDef
{
    public string Id { get; init; }              // 例 "hexcasting:add/numbers"
    public string DisplayName { get; init; }
    public string Signature { get; init; }       // 角度签名，例 "aqaa"
    public long StartDir { get; init; }          // 起始方向，六边形网格上的角度编码
    public bool IsPerWorld { get; init; }        // 对应 per_world_pattern 标签
    public bool RequiresEnlightenment { get; init; }
    public bool CannotModifyCost { get; init; }
    public IReadOnlyList<string> Args { get; init; }   // iota 类型签名，例 ["number","number"]
}
```

`PatternShapeMatch` 在源里有四种：`Normal` / `Nothing` / `PerWorld` / `Special`，
C# 侧对应 `enum PatternMatch { Nothing, Normal, PerWorld, Special }`。

注册表契约：

```csharp
public interface IPatternRegistry
{
    void Register(PatternDef def, IPatternExecutor executor);
    PatternMatch Match(string signature, long startDir, out PatternDef? def);
    IReadOnlyList<PatternDef> All { get; }
}
```

**A2 的硬约束**：图案的 `Id`、`Signature`、`StartDir` **必须与源仓库逐字一致**，
否则画出来的图案与原作对不上，也失去了"复刻"的意义。这三个值只能从源码抄，不能自己编。

---

## 4. Mishaps（源：`api.casting.mishaps`，27 个具体类）

已确认的 27 个 Mishap 全部需要 C# 对应实现。基类契约：

```csharp
public abstract class Mishap : Exception
{
    public abstract void Apply(MishapContext ctx);   // 造成反噬效果（掉血/疯狂/位移等）
    public virtual Iota? FallbackValue => null;      // 供栈恢复用，源中部分 Mishap 提供
}

public sealed class MishapContext
{
    public Player Caster { get; init; }              // 施法者
    public HexStack Stack { get; init; }             // 出错时的栈快照
    public Vector2 Position { get; init; }
}
```

必须实现的 27 个（源名 → 语义分组）：

- **栈/参数**：`NotEnoughArgs`、`InvalidOperatorArgs`、`StackSize`、`NeedsParens`、`UnescapedValue`
- **类型**：`InvalidIota`、`InvalidSpellDatumType`、`BoolDirectrixEmptyStack`、`BoolDirectrixNotBool`
- **媒质**：`NotEnoughMedia`
- **图案**：`InvalidPattern`、`DisallowedSpell`、`NoSpellCircle`
- **世界/位置**：`BadLocation`、`LocationInWrongDimension`
- **实体**：`BadEntity`、`EntityTooFarAway`、`ImmuneEntity`
- **物品**：`BadItem`、`BadOffhandItem`、`LackingHotbarItem`
- **方块**：`BadBlock`
- **施法者状态**：`BadCaster`、`Unenlightened`、`OthersName`、`NoAkashicRecord`
- **灌注/仪式**：`BadBrainsweep`、`AlreadyBrainswept`
- **运算**：`DivideByZero`
- **内部**：`InternalException`、`EvalTooMuch`（求值步数上限）

**A1 的验收**：这 27 个 Mishap 每个都要有单元测试用例。

---

## 5. 虚拟机（源：`api.casting.eval`，64 个 class）

```csharp
public sealed class HexStack
{
    private readonly List<Iota> _items;
    public int Count { get; }
    public void Push(Iota iota);
    public Iota Pop();                       // 空栈 → throw MishapNotEnoughArgs
    public Iota Peek(int depth = 0);
    public void Clear();                     // 对应 dropAll
    public IReadOnlyList<Iota> Snapshot();   // 存档/调试用，返回不可变副本
}

public interface IPatternExecutor
{
    /// 返回 null 表示"本图案不压栈"（如纯副作用类图案）
    Iota? Execute(HexStack stack, CastingContext ctx);
}

public sealed class CastingContext
{
    public Player Caster { get; init; }
    public IMediaStorage Media { get; init; }
    public IReadOnlyList<Iota> Ravenmind { get; init; }   // 阿卡夏记录（只读快照）
    public int EvalBudget { get; init; }                   // 对应 EvalTooMuch 上限
}
```

**关键约束（必须实现，否则会死循环）**：
- `EvalBudget` 递减计数，耗尽抛 `MishapEvalTooMuch`
- `ContinuationIota` 用于惰性求值与括号语义，`MishapNeedsParens` 与之配套
- 栈深必须有上限（对应 `MishapStackSize`）

---

## 6. 媒质系统（源：`api` 中 media 相关 + `common/blocks/akashic`）

```csharp
public interface IMediaStorage
{
    long Media { get; }
    long MaxMedia { get; }
    bool TrySpend(long amount);              // 不足返回 false，由调用方抛 MishapNotEnoughMedia
    void SetMedia(long value);
    void Save(TagCompound tag);
    void Load(TagCompound tag);
}
```

Terraria 侧实现挂 `ModPlayer`。**存档必须持久化**（Phase 1 验收项之一）。

---

## 7. 分工边界（避免多 agent 冲突）

| Agent | 只许改这些目录 |
|---|---|
| A1 | `Core/Casting/**`、`Core/Registry/**`、`Core/Media/**` |
| A2 | `Patterns/**` |
| A3 | `Client/**` |
| A4 | `Content/**` |
| A5 | `Assets/**` |
| A6 | 不写业务代码，只写 `Tests/**` 与报告 |

`Core/Casting/**` 的接口一旦冻结，A2/A3/A4 只允许调用，不允许修改。需要改接口 → 提给 A0。

---

## 8. 待补充（依赖源码到位）

- [ ] 195 条图案的 `Id`/`Signature`/`StartDir`/参数签名完整清单（必须从 Java 源码提取）
- [ ] `arithmetic` 包 27 个类的运算符表（+、-、×、÷、位运算等在 iota 类型上的分派规则）
- [ ] `math` 包 15 个类的六边形网格算法（角度编码、图案归一化、旋转）
- [ ] `SpellList` 的惰性求值细节
