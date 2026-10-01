# 咒法学 → 泰拉瑞亚 2D 适配方案

> **2026-09-30 更正**：用户要求「一切以原版为基准」。下文里「全程用泰拉原生坐标」的方案已废弃，
> 改为法术坐标与原版一致（见 §1.4 与 `AUDIT_VS_ORIGINAL.md` 的「3D → 2D 适配」一节）。
> 同一天向量也按原版恢复了三维（`±Z` 常量、叉积得向量、三分量拆装，世界是 z = 0 的平面）：§1「向量 iota 的 2D 化」、§2 的叉积降阶、
> §7 里「删除 `const/vec/pz` / `nz`」都已作废，现在只有 `interop/pehkui/*` 两条不适用（见 `STATUS.generated.md`）；
> §7 其余要「重设计」的图案后来都实现了（见 `AUDIT_VS_ORIGINAL.md`）。

> 本文只讲**「3D → 2D 怎么改」**。网格/画布/渲染见 `HEXCASTING_MECHANICS.md`，
> VM 求值链路见 `CASTING_ENGINE_SPEC.md`，物品/方块清单见 `CONTENT_INVENTORY.md`。
>
> 源：`FallingColors/HexMod` v0.11.4（commit `6b64165be3`），MIT。
> 所有结论均标注源文件路径与行号；泰拉侧 API 已用 Mono.Cecil 读取
> `D:\steam\steamapps\common\tModLoader\tModLoader.dll` 元数据核实。

---

## 0. 结论先行

| 类别 | 数量 | 含义 |
|---|---|---|
| **A 可直接对应** | **101** | 逻辑与维度无关（栈/列表/控制流/标量数学/物品/药水），**零改动** |
| **B 需改语义** | **64** | 涉及位置/向量/射线，**代码必须改成 2D**，但玩法语义保留 |
| **C 删除或重设计** | **23** | MC 独有概念，泰拉无对应物，**要么删、要么从零设计** |
| 合计 | 188 | 与 `PATTERN_CATALOG.json` 一致 |

**核心适配面只有 6 处**：向量 iota、向量算术、射线检测、实体空间属性、粒子、方块坐标。

---

## 1. 向量 iota 的 2D 化

### 1.1 原作 `Vec3Iota` 完整 API

来源：`Common/src/main/java/at/petrak/hexcasting/api/casting/iota/Vec3Iota.java`

| 成员 | 行号 | 实现 | 2D 处置 |
|---|---|---|---|
| 构造 | 16-18 | 持有 `net.minecraft.world.phys.Vec3`（3×double） | 改为持有 `Vector2`（2×float） |
| `getVec3()` | 20-27 | 逐分量过 `HexUtils.fixNAN` | 保留，逐分量 `fixNAN` |
| `isTruthy()` | 29-33 | **三分量全 0 才为假** | 改为二分量全 0 为假 |
| `toleratesOther()` | 35-40 | `distanceToSqr < TOLERANCE²`（`DoubleIota.TOLERANCE = 0.0001`） | 保留，改用 2D 距离 |
| `serialize()` | 42-45 | 走 `HexUtils.serializeToNBT(Vec3)` | 见 1.3，**已有现成 2D 版本** |
| `display()` | 75-82 | `"(%.2f, %.2f, %.2f)"` | 改为 `"(%.2f, %.2f)"` |
| `color()` | 59-62 | `0xff_ff3030`（红） | 保留 |

序列化细节（`api/utils/HexUtils.kt`）：
- `Vec3.serializeToNBT()` 行 33-39：写 `x`/`y`/`z` 三个 double
- `vecFromNBT(LongArray)` 行 41-46：**要求数组长度恰好 3**，否则返回 `Vec3.ZERO`
- `vecFromNBT(CompoundTag)` 行 47-52：要求 `x`/`y`/`z` 三键齐全
- **`fixNAN`** 行 66：非有限值归零

### 1.2 C# 2D 设计建议

```csharp
// Core/Casting/Iota/VectorIota.cs
public sealed class VectorIota : Iota
{
    // 用泰拉原生 Vector2（float x/y），与世界坐标/velocity/Hitbox 天然一致
    public Vector2 Value { get; }

    public VectorIota(Vector2 v) => Value = FixNan(v);

    private static Vector2 FixNan(Vector2 v)
        => new(float.IsFinite(v.X) ? v.X : 0f, float.IsFinite(v.Y) ? v.Y : 0f);

    public override bool IsTruthy() => Value.X != 0f || Value.Y != 0f;

    public override bool IsSameValueAs(Iota other)
        => other is VectorIota v
           && Vector2.DistanceSquared(Value, v.Value) < Tolerance * Tolerance;

    public override object? Serialize() => new[] { (double)Value.X, (double)Value.Y };
}
```

**关键取舍：`float` 还是 `double`？**

- 原作 `Vec3Iota` 用 `double`（MC 的 `Vec3` 是 double）
- 泰拉的 `Vector2` 是 **float**，且 `Entity.position` / `velocity` / `Hitbox` 全是 float
- **建议用 `Vector2`（float）**：与世界坐标零转换成本；精度足够（泰拉世界坐标量级 10^4~10^5，float 有 7 位有效数字，误差 < 0.01 像素）
- 若坚持 double，则每次读写实体位置都要来回转换，且**容差比较**（`TOLERANCE=0.0001`）在 float 下需要放宽到约 `1e-4f`（否则浮点误差会让"相等"判断随机失败）

**注意：容差必须重新标定**：`DoubleIota.TOLERANCE = 0.0001` 是给 double 的。
float 下建议 **`Tolerance = 0.001f`**（泰拉 1 像素 = 1 单位，0.001 像素远小于可视精度）。

### 1.3 序列化：已有一份现成的 2D 实现

`api/utils/HexUtils.kt` 行 54-61 **已经存在 Vec2 的序列化**，可直接借鉴：

```kotlin
fun Vec2.serializeToNBT(): LongArrayTag =
    LongArrayTag(longArrayOf(this.x.toDouble().toRawBits(), this.y.toDouble().toRawBits()))

fun vec2FromNBT(tag: LongArray): Vec2 = if (tag.size != 2) Vec2.ZERO else
    Vec2(Double.fromBits(tag[0]).toFloat(), Double.fromBits(tag[1]).toFloat())
```

即：**2 个 double 的原始位（raw bits）打包成长整型数组**，长度不等于 2 时回退零向量。
C# 侧对应（存档用 `TagCompound`）：

```csharp
// 存：把两个 float 的位模式放进 long
tag["vec"] = new long[] {
    BitConverter.SingleToInt32Bits(Value.X),
    BitConverter.SingleToInt32Bits(Value.Y)
};
// 读：长度必须为 2，否则回退 Vector2.Zero
```

### 1.4 必须注意的语义变化

| 变化 | 说明 |
|---|---|
| **分量数 3 → 2** | `isTruthy`、`display`、序列化长度、容差距离全要改 |
| **叉积降阶** | 见 §2，`div` 从"返回向量"变成"返回标量"，**这是破坏性变更** |
| **`const/vec/pz`、`const/vec/nz` 无意义** | 删（见 §7） |
| **Y 轴方向** | ~~本移植全程用泰拉原生坐标，`py` = 下~~（**2026-09-30 已改**）。现在法术坐标与原版一致：+Y 朝上、单位方块、速度「方块/刻」；世界边界由 `Content/HexSpaceWorld.cs` 统一换算（位置 y ↔ 世界高 − y，方向 y ↔ −y，速度 ×3），公式在 `Core/World/HexAxes.cs` 并有离线测试。原先「已写进书」的说法不属实，书用的是官方文本，本来就按 +Y 朝上写 |
| **`pack`/`unpack` 参数个数变** | 见 §2.2 |

---

## 2. 算术运算的 2D 化

### 2.1 架构：算术是"多态分派"，不是一条图案一个实现

关键结构（`common/lib/hex/HexArithmetics.java`）：
- 行 38-43：注册 6 个算术族 —— `double` / `vec3` / `list` / `bool` / `list_set` / `bitwise_set`
- 行 45-51：`make(name, arithmetic)` 按名字注册

而**图案侧**（`common/lib/hex/HexActions.java` 行 148-172）用 `OperationAction` 包装，例如：

```java
public static final ActionRegistryEntry ADD = make("add",
    new OperationAction(HexPattern.fromAngles("waaw", HexDir.NORTH_EAST)));
public static final ActionRegistryEntry MUL_DOT = make("mul", ...);   // 点积
public static final ActionRegistryEntry DIV_CROSS = make("div", ...); // 叉积
public static final ActionRegistryEntry CONSTRUCT_VEC = make("construct_vec", ...);
public static final ActionRegistryEntry DECONSTRUCT_VEC = make("deconstruct_vec", ...);
```

**含义**：`add`/`sub`/`mul`/`div`/`abs`/`pow`/`floor`/`ceil` 是**多态**的 ——
同一图案按参数类型分派到 double 版或 vec3 版。
所以 C# 侧必须保留这个分派层，而不是给向量单独做一套图案。

### 2.2 `Vec3Arithmetic` 逐条对照

来源：`common/casting/arithmetic/Vec3Arithmetic.java`

| 图案（`OPS` 行 26-38） | 行号 | 3D 实现 | 2D 对应 | 影响 |
|---|---|---|---|---|
| `PACK`（construct_vec） | 54-56 | 3 个 double → `Vec3(x,y,z)` | **2 个 double → `Vector2(x,y)`** | 注意：**参数个数 3→2** |
| `UNPACK`（deconstruct_vec） | 56-57 | `Vec3` → 3 个 double | **`Vector2` → 2 个 double** | 注意：**压栈个数 3→2** |
| `ADD` | 58-60 | 逐分量加（`make2Fallback`） | 逐分量加 | 无（分量数变） |
| `SUB` | 60-62 | 逐分量减 | 逐分量减 | 无 |
| `MUL`（**点积**） | 62-64 | `Vec3::dot` → **double** | `Vector2.Dot` → **double** | 语义不变 |
| `DIV`（**叉积**） | 64-66 | `Vec3::cross` → **Vec3** | `u.X*v.Y - u.Y*v.X` → **double** | 注意：**返回值类型改变（破坏性）** |
| `ABS`（长度） | 66-68 | `Vec3::length` → double | `Value.Length()` → double | |
| `POW`（投影） | 68-70 | `v.normalize().scale(u.dot(v.normalize()))` | 同式，2D | |
| `FLOOR` | 70-72 | 逐分量 `Math.floor` | 逐分量 `MathF.Floor` | 无 |
| `CEIL` | 72-74 | 逐分量 `Math.ceil` | 逐分量 `MathF.Ceiling` | 无 |
| `MOD` | 74-76 | 逐分量取模（fallback 到 double 版） | 逐分量取模 | 无 |

**分量配对逻辑**（`operator/vec/OperatorVec3Delegating.java` 行 37-56）：
- 两个都是向量 → 走向量运算（行 42-44）
- 一方是标量 → 用 `triplicate` **广播**成 `(d,d,d)`（行 45-46、58-60）
- 逐分量结果再组装回向量（行 47-52、`TripleIterable`）

**2D 版就是 `triplicate(d) => new Vector2(d, d)`，并把 `TripleIterable`（3 元）改成 2 元。**

**除零**：行 53-55 捕获 `MishapDivideByZero` 并补上操作数信息，**这段逻辑要原样保留**（2D 同样会除零）。

### 2.3 一元运算的逐分量处理

`api/casting/ActionUtils.kt` 行 305-309：

```kotlin
fun aplKinnie(operatee: Either<Double, Vec3>, fn: DoubleUnaryOperator): Iota =
    operatee.map(
        { num -> DoubleIota(fn.applyAsDouble(num)) },
        { vec -> Vec3Iota(Vec3(fn(vec.x), fn(vec.y), fn(vec.z))) }
    )
```

2D 版：`VectorIota(new Vector2(fn(v.X), fn(v.Y)))`。**注意这个 helper 名字很随意但被 floor/ceil 等复用，别漏掉。**

### 2.4 算术之外的向量用法

- `asActionResult`（`ActionUtils.kt` 行 311-323）定义了值 → iota 的转换：
  - 行 318：`BlockPos.asActionResult` = `Vec3Iota(Vec3.atCenterOf(this))` ← **方块位置以"方块中心"的向量形式进入栈**，2D 版见 §6
  - 行 320：`Vec3.asActionResult` = `Vec3Iota(this)`
- `getVec3(idx, argc)`（行 62-69）：取参失败抛 `MishapNotEnoughArgs`；类型不对抛 `MishapInvalidIota.ofType(..., "vector")`
  - **2D 版改名建议**：保留 `GetVector`，错误信息里的类型名从 `"vector"` 保持（玩家视角无差别）

---

## 3. 射线检测的 2D 化

### 3.1 原作两条射线

**(a) 方块射线** `common/casting/actions/raycast/OpBlockRaycast.kt` 行 24-43：

```kotlin
val blockHitResult = env.world.clip(
    ClipContext(
        origin,
        Action.raycastEnd(origin, look),   // origin + look.normalize() * 32.0
        ClipContext.Block.COLLIDER,
        ClipContext.Fluid.NONE,
        env.castingEntity
    )
)
```

- `Action.RAYCAST_DISTANCE = 32.0`（`castables/Action.kt` 行 77）
- 命中返回 `blockHitResult.blockPos.asActionResult`（**方块中心**），否则 `NullIota`
- 还要 `env.isVecInRange(atCenterOf(blockPos))` 二次确认（行 35）

**(b) 方块轴射线** `OpBlockAxisRaycast.kt` 行 35-40：同上，但返回 `blockHitResult.direction.step()` —— 命中面的法向。
**2D 版**：命中面只有 4 个（左/右/上/下），返回对应单位向量。

**(c) 实体射线** `OpEntityRaycast.kt` 行 28-43 + 45-83：
- 构造 `AABB(origin, endp)`，取盒内实体
- 对每个实体：`hitBox = entity.boundingBox.inflate(pickRadius)`，做**射线-盒相交**（行 56 `hitBox.clip(startPos, endPos)`）
- 取最近命中（行 66 `sqrDist < sqrLength`）
- 行 67 还有"同一载具不重复选中"的 MC 特有逻辑

### 3.2 泰拉侧对应（已核实 API）

| 原作 | 泰拉 | 说明 |
|---|---|---|
| `world.clip(...)` 找第一个实心方块 | **`Utils.PlotTileLine(Vector2 start, Vector2 end, float width, TileActionAttempt action)`** | 沿线段逐图格回调，可在回调里判断实心并提前结束 |
| 判断某图格是否阻挡 | **`Collision.SolidCollision(Vector2 Position, int Width, int Height)`** / `Collision.EmptyTile(int x, int y, bool)`, `Collision.CanHit(...)` | `EmptyTile` 判空；`SolidCollision` 判实心 |
| 实体射线（3D AABB 相交） | **`Rectangle` 相交**：实体的 `Entity.Hitbox` 是 `Rectangle` | 2D 射线-矩形相交是十来行代码，比原作的 `clip` 简单得多 |
| `env.castingEntity` 视线判定 | **`Collision.CanHit(Entity, Entity)`** / `Collision.CanHit(Vector2, int, int, Vector2, int, int)` | 泰拉自带"两点间是否可直视" |

已核实的元数据（Mono.Cecil 读 tModLoader.dll）：
```
Terraria.Collision:
  TileCollision(Vector2, Vector2, Int32, Int32, Boolean, Boolean, Int32, Boolean, Boolean, Boolean)
  CanHit(Vector2, Int32, Int32, Vector2, Int32, Int32)
  CanHit(Entity, Entity)
  SolidCollision(Vector2, Int32, Int32)
  EmptyTile(Int32, Int32, Boolean)
Terraria.Utils:
  PlotTileLine(Vector2, Vector2, Single, TileActionAttempt)
  PlotTileLine(Vector2D, Vector2D, Double, TileActionAttempt)
```

### 3.3 建议实现（可直接复用原作逻辑的部分）

```csharp
// 2D 射线：返回第一个实心图格中心的 Vector2；无命中返回 null
public static Vector2? RaycastTile(Vector2 origin, Vector2 look, float maxDist = 32f)
{
    var dir = SafeNormalize(look, Vector2.UnitX);
    var end = origin + dir * maxDist;
    Vector2? hit = null;

    Utils.PlotTileLine(origin, end, 1f, (x, y) =>
    {
        if (!Collision.EmptyTile(x, y, true))   // false = 有实心块
        {
            hit = new Vector2(x * 16 + 8, y * 16 + 8);  // 图格中心
            return false;   // 提前结束
        }
        return true;
    });
    return hit;
}
```

**可直接复用原作逻辑的部分**：
- `raycastEnd(origin, look) = origin + normalize(look) * 32` —— 距离常量 32 可原样保留（泰拉图格同样 16 像素，尺度接近）
- "命中后返回**方块中心**而不是命中点"这个决策（`OpBlockRaycast.kt` 行 36-38 的注释解释了原因：返回命中点会让 `break_block` 打不中目标块）—— **这条经验必须保留**
- `env.isVecInRange` 的二次确认
- 无命中返回 `NullIota`

**必须重写的部分**：
- 射线-盒相交：从 3D `AABB.clip` 改为 2D 射线-`Rectangle` 相交
- 删掉 MC 特有分支：`inflate(pickRadius)` 的额外膨胀量、行 67 的"同载具"判断

---

## 4. 实体位置 / 视线 / 速度的 2D 化

### 4.1 原作实现

| 图案 | 文件:行 | 读的是什么 |
|---|---|---|
| `entity_pos/foot` | `queryentity/OpEntityPos.kt:15` | **`e.position()`** |
| `entity_pos/eye` | `queryentity/OpEntityPos.kt:15` | **`e.eyePosition`** |
| `get_entity_look` | `queryentity/OpEntityLook.kt:17` | `HexAPI.getEntityLookDirSpecial(e)` |
| `get_entity_velocity` | `queryentity/OpEntityVelocity.kt:17` | `HexAPI.getEntityVelocitySpecial(e)` |
| `get_entity_height` | `queryentity/OpEntityHeight.kt:15` | **`e.bbHeight`** |

底层实现（`common/impl/HexAPIImpl.java`）：
- 行 38-51 `getEntityLookDirSpecial`：**`entity.getLookAngle()`** —— MC 里由持久 yaw/pitch 算出单位向量；另外对 `AbstractHurtingProjectile`/`ShulkerBullet`/`Projectile`/`Phantom` 做了**符号翻转的 bug 修正**
- 行 62-71 `getEntityVelocitySpecial`：默认 `entity.getDeltaMovement()`，可注册特例

### 4.2 注意：最关键的设计问题：「视线方向」在 2D 下没有天然定义

MC 的每个实体都有**持久 yaw/pitch**，所以 `getLookAngle()` 永远有效。
**泰拉实体没有持久视线**：`Entity` 只有 `int direction`（±1 左右朝向），没有俯仰角。
已核实（Mono.Cecil）：
```
Terraria.Entity: Vector2 position(左上角) / velocity / Center / Hitbox / int width / int height / int direction
```

若直接拿 `velocity` 当视线，**静止实体返回零向量**，`normalize()` 会产生 NaN，
污染整个 VM（NaN 会一路传播并把 `isTruthy` 判假、比较全失败）。

**建议的退化策略（按实体类型分派）**：

| 实体类型 | 视线定义 | 理由 |
|---|---|---|
| **弹幕 `Projectile`** | `velocity.SafeNormalize(Vector2.UnitX)` | 飞行物天然有方向，与原作语义最接近 |
| **玩家 `Player`** | `(Main.MouseWorld - player.Center).SafeNormalize(new Vector2(player.direction, 0))` | 玩家"看"的是鼠标；**仅客户端可得** |
| **NPC** | `velocity.LengthSquared() > 阈值 ? velocity 归一化 : new Vector2(npc.direction, 0)` | 有速度用速度，静止用朝向 |
| **掉落物 `Item`** | `velocity.SafeNormalize(new Vector2(0, 1))` | 掉落物受重力，默认朝下 |

**注意：多人游戏问题**：`Main.MouseWorld` 只在客户端有效，而原作明确"所有求值都在服务端"
（`castables/Action.kt` 行 22-28 的注释）。
若要在服务器端拿到玩家瞄准方向，必须**自己同步**：
```
ModPlayer: 客户端每帧把 (鼠标世界坐标 - 玩家中心) 的归一化方向存入字段
        → NetSend / NetReceive 同步到服务端
        → 服务端 VM 读取该字段作为"视线"
```
建议**先只支持单机**（服务端即本机，可直接读 `Main.MouseWorld`），把同步留作后续。

**建议的兜底**：写一个 `SafeNormalize(v, fallback)`，**任何 `normalize` 调用都必须带兜底**。
原作没有这个问题（MC 向量永不为零），泰拉会大量遇到零向量 —— 这是最容易崩的点。

### 4.3 位置语义：MC 脚底中心 vs 泰拉左上角

| 概念 | MC | 泰拉 | 换算 |
|---|---|---|---|
| `entity_pos/foot` | `position()` = **脚底中心** | `Entity.position` = **左上角**；`Entity.Bottom` = 底边中心 | 用 `new Vector2(e.Center.X, e.Bottom.Y)`，或 `e.position + new Vector2(e.width/2f, e.height)` |
| `entity_pos/eye` | `eyePosition` = 脚底 + `eyeHeight` | 无 eyeHeight；`Entity.Center` 是碰撞箱中心，`Entity.Top` 是顶边中心 | 建议 `e.Center`（近似眼睛高度）；或用 `e.Top` |
| `get_entity_height` | `bbHeight`（double） | `Entity.height`（**int**） | 直接取 `(double)e.height` |

**注意：这是全局最容易出错的点**：所有写回位置的法术（blink/teleport/add_motion）
都必须遵守"MC 用脚底、泰拉用左上角"的差异，否则**整个身体的偏移量会差半个身位**。
建议在 C# 侧封装一对 helper 并**全局只用它们**：
```csharp
static Vector2 FeetPosition(Entity e) => new(e.Center.X, e.Bottom.Y);
static void SetFeetPosition(Entity e, Vector2 feet)
    => e.position = feet - new Vector2(e.width / 2f, e.height);
```

### 4.4 速度

`get_entity_velocity` → 泰拉 `Entity.velocity`（`Vector2`），**语义完全一致**，只需改成 2 分量。
原作 `getEntityVelocitySpecial` 的特例注册表（`HexAPIImpl.java:53-60`）**可以整段删除**
（那是为 MC 某些实体速度读不准做的补丁，泰拉不需要）。

---

## 5. 粒子的 2D 化

### 5.1 原作粒子模型

`api/casting/ParticleSpray.kt` 行 13-28：

```kotlin
data class ParticleSpray(
    val pos: Vec3,        // 球心
    val vel: Vec3,        // 基准方向
    val fuzziness: Double,// pos 周围随机球半径
    val spread: Double,   // 相对 vel 的最大偏角（弧度）
    val count: Int = 20
)
// 便捷构造：
fun burst(pos, size, count = 20) = ParticleSpray(pos, Vec3(size, 0, 0), 0.0, 3.14, count)  // 全向爆开
fun cloud(pos, size, count = 20) = ParticleSpray(pos, Vec3(0, 0.001, 0), size, 0.0, count) // 原地弥漫
```

- `sprayParticles` 行 26-28：**发包给 128 格内的客户端**，客户端生成粒子
- 用法见 `OperatorSideEffect.kt` 行 49-54（`Particles`）与行 56-71（`DoMishap` 喷红+主色）
- 法术自身也内联粒子，例如 `OpExplode.kt:46` `ParticleSpray.burst(pos, strength, 50)`、
  `OpBlink.kt:59-60` 两段（起点 cloud + 终点 burst）

### 5.2 泰拉侧对应（已核实 API）

已核实元数据：
```
Terraria.Dust:
  NewDust(Vector2, Int32 type, Int32, Int32, Single, Single speedX, speedY, Int32 Alpha, Color, Single Scale)
  NewDustDirect(Vector2, Int32, Int32, Int32, Single, Single, Int32, Color, Single)
  NewDustPerfect(Vector2, Int32, Nullable<Vector2>, Int32, Color, Single)
```

对应方案：

| 原作概念 | 泰拉实现 |
|---|---|
| `pos` | `Vector2` 世界坐标 |
| `count` | 循环次数（**建议上限 20~30**，见下方性能） |
| `fuzziness` | `pos + Main.rand.NextVector2Circular(fuzziness, fuzziness)` |
| `vel` + `spread` | 把 `vel` 旋转 `Main.rand.NextFloat(-spread, spread)` 弧度后作为速度 |
| `FrozenPigment` 颜色 | `Dust.NewDustPerfect(pos, dustType, velocity, alpha, color, scale)` 的 **color 参数** |
| `burst` | 在 `[0, 2π)` 均匀取随机角度，速度大小 = `size` |
| `cloud` | 速度取极小值（如 `Main.rand.NextVector2Circular(0.1f,0.1f)`），位置散布 = `size` |
| `DoMishap` 双色喷雾 | 一半粒子用 `accentColor`，一半用红色（`OperatorSideEffect.kt:59-67` 同逻辑） |

**`DustID` 选择建议**（对应咒法学紫色主题）：
- 常规施法粒子：`DustID.PurpleMoss` / `DustID.Vortex` / `DustID.ShadowbeamStaff`
- 媒质相关：`DustID.Amethyst`（泰拉有现成的紫晶尘）
- 雾状：`DustID.Smoke` / `DustID.ViciousPowder`
- 反噬/错误：`DustID.Blood` 或 `DustID.Firework_Red`

**性能优化（必须做）**：
1. **限制粒子总数**：原作 `count` 常给 50/100（如 `OpBlink` 起终点各 50/100、`OpExplode` 50）。
   泰拉 `Dust` 是**全局数组**（上限 `Main.maxDust`），过量会挤掉其它模组的粒子甚至卡顿。
   **建议把 `count` 统一乘 0.3~0.5 并设硬上限（如 40）**。
2. **距离裁剪**：泰拉不需要原作那样发包，但要在**距离玩家 > 屏幕对角线**时跳过生成。
3. **不要每帧生成**：`Particles` 副作用发生在**一次施法**（非持续），保持一次性即可。
4. **多人同步**：泰拉 `Dust` 在客户端生成；服务端求值时若需给别人看，要走 `ModPacket`
   （对应原作 `MsgCastParticleS2C` 的 `sendPacketNear(pos, 128)`）。

**"喷发方向"概念的对应**：泰拉的 `Dust.NewDust` 自带随机散射，**不需要**原作的
`spread` 球面均匀采样。简化做法：把 `spread` 只用于**转速**（旋转基准方向），
随机性交给 `NewDust` 自带的 `SpeedX/SpeedY` 抖动。

---

## 6. 方块坐标的 2D 化

原作把**方块位置编码成"方块中心"的向量 iota**（`ActionUtils.kt:318`：
`BlockPos.asActionResult = Vec3Iota(Vec3.atCenterOf(this))`）。
反向的 `getBlockPos(idx)` 则把向量取整回方块坐标。

泰拉侧对应（图格 16 像素）：

```csharp
// 图格 → 向量 iota（图格中心）
static Vector2 TileToVector(int tx, int ty) => new(tx * 16f + 8f, ty * 16f + 8f);

// 向量 → 图格
static (int tx, int ty) VectorToTile(Vector2 v)
    => ((int)MathF.Floor(v.X / 16f), (int)MathF.Floor(v.Y / 16f));
```

**必须保留原作的决策**：`OpBlockRaycast` 返回的是**方块中心**而非命中点
（`OpBlockRaycast.kt:36-38` 注释：返回命中点会让 `OpBreakBlock` 打不中目标块）。
2D 下同样成立 —— 泰拉的 `WorldGen.KillTile(x, y)` 用图格坐标，不是世界坐标。

**涉及方块坐标的 B 类图案（16 条）**：
`coerce_axial`、`break_block`、`place_block`、`create_water`、`create_lava`、`destroy_water`、
`ignite`、`extinguish`、`conjure_block`、`conjure_light`、`bonemeal`、`edify`、
`explode`、`explode/fire`、`compare_block/lenient`、`compare_block/strict`

其中泰拉 API 对应建议：
| 图案 | 泰拉实现 |
|---|---|
| `break_block` | `WorldGen.KillTile(tx, ty)` + `NetMessage.SendData` 同步 |
| `place_block` | `WorldGen.PlaceTile` / `WorldGen.PlaceObject` |
| `conjure_block`/`conjure_light` | 泰拉无"临时幻块"，改用**临时物块**（`Projectile` 撑起的 `Tile`）或自建 `ModTile` + 计时器 |
| `create_water`/`create_lava` | `WorldGen.PlaceLiquid` / 直接写 `Main.tile[x,y].liquid` |
| `destroy_water` | 清 `liquid` |
| `ignite` | `WorldGen.PlaceTile(..., TileID.Torches)` 或给 NPC 上 `BuffID.OnFire` |
| `extinguish` | 清除 `BuffID.OnFire` / 灭火 |
| `bonemeal` | `WorldGen.GrowTree` 系列 / 促进作物 |
| `edify` | 泰拉无"启迪木"，需自建方块 |
| `explode` | 泰拉爆炸是 `Projectile`（如 `ProjectileID.Grenade`）或 `WorldGen.KillTile` 范围破坏；**注意泰拉无"爆炸保护"原生概念**，需自己判定 `Main.tile` 是否可破坏 |

**注意：`explode` 的 `canEditBlockAt` 语义**：原作 `OpExplode.kt:53` 检查
`env.canEditBlockAt(BlockPos.containing(pos))`（MC 的冒险模式保护）。
泰拉对应：检查玩家是否有**修改世界权限**（`Main.playerInventory` 无关；
多人下看 `Main.netMode`/`NPC.downedBoss` 等；单机恒为 true）。

---

## 7. 必须改设计 / 必须删除的图案清单（C 类 23 条）

| # | 图案 Id | 理由（源文件证据） | 建议处置 |
|---|---|---|---|
| 1-2 | `const/vec/pz`、`const/vec/nz` | z 轴 ±1 向量，2D 无 z 轴（`Vec3Iota`） | **删除** |
| 3-4 | `interop/pehkui/get`、`interop/pehkui/set` | Pehkui 是 MC 的**体型缩放** mod（`interop/pehkui/OpGetScale`）；泰拉无对应 | **删除**（如将来要体型缩放需另立项） |
| 5-6 | `fisherman`、`fisherman/copy` | MC 钓鱼掉落表机制（`OpFisherman`/`OpFishermanButItCopies`）；泰拉钓鱼是 `Player.fishingLevel` + 独立掉落表 | **重设计**：改为"读取泰拉钓鱼掉落表" |
| 7 | `brainsweep` | 把 MC **村民**转成方块（`OpBrainsweep.kt`，8 个配方）；泰拉 NPC 无等级/职业 | **重设计**或删除 |
| 8-11 | `circle/impetus_pos`、`circle/impetus_dir`、`circle/bounds/min`、`circle/bounds/max` | 法术环 = **多方块结构 + 有向图媒质波**（`api/casting/circles/**`）；泰拉**完全没有多方块概念** | **重设计**（自建 `ModTile` + 图遍历执行器） |
| 12-16 | `sentinel/create`、`sentinel/create/great`、`sentinel/destroy`、`sentinel/get_pos`、`sentinel/wayfind` | 哨兵是 MC 里的**持久实体**（`api/player/Sentinel`），支持寻路（`OpGetSentinelWayfind`）；泰拉无此概念 | **重设计**：用泰拉 `Projectile` 或 `ModPlayer` 存档坐标代替 |
| 17-18 | `akashic/read`、`akashic/write` | 阿卡夏书架用 `HexPattern` 作**复合键**索引 iota，靠 `AkashicFloodfiller` 连成共享存储网络（多方块） | **重设计**：泰拉箱子按格子索引，需自建键值存储 |
| 19-22 | `flight`、`flight/range`、`flight/time`、`flight/can_fly` | MC **创造模式飞行**（`OpAltiora`/`OpFlight`/`OpCanEntityHexFly`）；泰拉飞行是翅膀/坐骑/火箭靴，机制不同 | **重设计**：改为授予临时飞行 buff（`BuffID.Winged` 类自建 buff）+ 剩余时间查询 |
| 23 | `lightning` | MC 雷击是**实体**（`EntityType.LIGHTNING_BOLT`，`OpLightning.kt`）；**泰拉原版没有雷击**（只有雷雨天背景） | **重设计**：自建雷击弹幕（伤害 + 落点效果） |

### 其中特别说明三个"整体重设计"的系统

1. **法术环（4 条图案 + 8 个方块）**
   原作三条硬规则：环必须**闭合**、出口必须**唯一**、作用范围限于包围盒
   （`CircleExecutionState.java:95-159`，闭合判定伪码见 `CONTENT_INVENTORY.md` 第 6 节）。
   泰拉最接近的是"有效房屋"和"晶塔网络"，但**都不可扩展**。
   需自建 `ICircleComponent` 等价物（`ModTile` + `ModTileEntity`）+ 独立图遍历执行器。
   **建议保留那三条判定规则**——它们是玩法核心。

2. **哨兵（5 条图案）**
   原作哨兵是跨维度持久的实体。泰拉最省事的替代：把哨兵存成 `ModPlayer` 里的一个
   `Vector2` 坐标 + 一个可见的 `Projectile` 表现，`wayfind` 改为"向哨兵方向的单位向量"。

3. **阿卡夏记录（2 条图案）**
   泰拉可用 `ModPlayer` 存一个 `Dictionary<string, Iota>`（键 = 角度签名）。
   这比原作的多方块共享存储简单，但也失去了"多人共享图书馆"的玩法。

---

## 7.5 注意：单位换算：所有"距离常量"都必须按 16 倍缩放

这是**极易被忽略但影响全局**的一点。

| 单位 | MC | 泰拉 |
|---|---|---|
| 1 个方块 / 图格 | **1.0** world unit | **16 像素** |
| 视野半径（施法范围） | `PlayerBasedCastEnv.DEFAULT_AMBIT_RADIUS = 32.0`（32 格） | 32 格 = **512 像素** |
| 射线距离 | `Action.RAYCAST_DISTANCE = 32.0`（32 格） | 32 格 = **512 像素** |

来源：
- `api/casting/eval/env/PlayerBasedCastEnv.java:48` `DEFAULT_AMBIT_RADIUS = 32.0`
- `api/casting/castables/Action.kt:77` `RAYCAST_DISTANCE: Double = 32.0`
- 范围判定 `PlayerBasedCastEnv.java:128-139`：`vec.distanceToSqr(caster.position()) <= ambitRadius²`

**如果直接把 `32.0` 抄到泰拉**，施法范围会是 **2 个图格** —— 玩家一动就"实体太远"
（`MishapEntityTooFarAway`），射线也只能打到脚边。

**建议**：在 C# 侧定义 `HexUnits` 常量类，**所有距离一律以"图格"为单位书写，内部乘 16**：

```csharp
public static class HexUnits
{
    public const float PixelsPerTile = 16f;

    /// <summary>施法视野半径（图格）。原作为 32 格。</summary>
    public const float AmbitRadiusTiles = 32f;
    public static float AmbitRadiusPixels => AmbitRadiusTiles * PixelsPerTile;   // 512

    /// <summary>射线最大距离（图格）。原作为 32 格。</summary>
    public const float RaycastDistanceTiles = 32f;
    public static float RaycastDistancePixels => RaycastDistanceTiles * PixelsPerTile;  // 512
}
```

**同理需要缩放的还有**（凡是以"世界单位"表示距离/速度的地方）：
- `OpAddMotion` 的 `MAX_MOTION = 8192.0`（`OpAddMotion.kt:21`）—— MC 速度单位，泰拉 `velocity` 单位不同，需按手感重标
- `OpExplode` 的 `strength`（0~10）—— MC 爆炸半径（格），泰拉爆炸半径需换算
- `OpBlink` 的 `delta`（位移距离，单位=格）→ 泰拉像素
- 粒子 `fuzziness` / `burst size`（`OpExplode.kt:46` 用 `strength` 当粒子尺寸）—— 同样要乘 16

**判定标准**：任何从原作抄来的数值，只要它的语义是"距离/半径/速度"，就要问一句
**"这个数是格还是像素"**。以格为单位的值一律 ×16。

---

## 8. 2D 化中最容易做错的 3 个点

### ① 坐标原点语义不一致（影响面最大）

- **MC**：`entity.position()` 是**脚底中心**；`eyePosition` = 脚底 + `eyeHeight`
- **泰拉**：`Entity.position` 是**左上角**；`Entity.Center` 才是中心；`Entity.Bottom` 是底边中心
  （已核实：`Terraria.Entity` 有 `position` 字段与 `Center`/`Left`/`Right`/`Top`/`Bottom`/`Size` 属性）

**后果**：`blink`/`teleport`/`add_motion`/`entity_pos/*` 全部会偏**半个身位**
（x 偏 `width/2`、y 偏 `height`）。而且这个偏移**看起来"差不多对"**，不会立刻报错，
只是所有位移法术都歪一点 —— 极难定位。

**对策**：C# 侧封装 `FeetPosition(entity)` / `SetFeetPosition(entity, v)` 两个 helper，
**全局强制只用它们**读写实体位置，禁止直接摸 `entity.position`。

### ② 叉积降阶导致的静默类型错

- **MC**：`div` = 叉积 = `Vec3 × Vec3 → Vec3`（垂直于两者的向量）
- **2D**：叉积退化为**标量** `u.x*v.y - u.y*v.x`（有向面积 / 旋转方向）

**后果**：若为了"保持接口一致"而硬造一个 2D 向量返回（比如错误地返回 `(-v.y, v.x)`），
得到的是"旋转 90°"，**语义完全不同**，所有依赖 `div` 的几何法术会静默算错。

**对策**：接受破坏性变更 —— 2D 下 `div` 返回 **double**（标量）。
若需要"垂直向量"功能，**另开一条图案**（例如 `perp`）而不是污染 `div`。

### ③ 零向量归一化产生 NaN 并污染整个 VM

- **MC**：`getLookAngle()` 由 yaw/pitch 算出，**永远有效**；向量运算几乎不会碰到零向量
- **泰拉**：NPC **没有持久朝向**（只有 `int direction` ±1）。
  若用 `velocity` 当视线，**静止的 NPC 返回零向量** → `normalize()` → **NaN**

**后果**：NaN 会一路传播 —— `isTruthy()` 判假、所有 `tolerates`/相等比较失败、
`distanceToSqr` 变 NaN、范围检查全失败。表现为"法术莫名其妙不生效"，
而且**没有异常、没有日志**。

**对策**：
1. 写 `SafeNormalize(Vector2 v, Vector2 fallback)`，**所有归一化必须带兜底**
2. 视线方向按实体类型分派（见 §4.2 表格），并对静止实体退化到 `direction`
3. 在 `VectorIota` 构造时做 **NaN 清洗**（原作 `HexUtils.fixNAN` 行 66 就是为此存在，
   2D 版要保留并扩展到 `Vector2`）

---

## 9. 附：泰拉侧 API 对照速查表（已用 Mono.Cecil 核实）

| 用途 | MC（原作） | 泰拉 |
|---|---|---|
| 实体中心 | `entity.position()` + eyeHeight | `Entity.Center` (`Vector2`) |
| 实体左上角 | — | `Entity.position` (`Vector2`) |
| 实体底边中心 | `entity.position()`（≈脚底） | `new Vector2(e.Center.X, e.Bottom.Y)` |
| 实体包围盒 | `AABB`（3D） | `Entity.Hitbox` (`Rectangle`) |
| 实体尺寸 | `bbWidth` / `bbHeight` (double) | `Entity.width` / `Entity.height` (int) |
| 实体速度 | `getDeltaMovement()` | `Entity.velocity` (`Vector2`) |
| 实体朝向 | `getLookAngle()`（yaw+pitch） | `Entity.direction` (int, ±1) |
| 射线找方块 | `world.clip(ClipContext)` | `Utils.PlotTileLine(...)` + `Collision.EmptyTile` |
| 视线判定 | `world.clip(...)` | `Collision.CanHit(Vector2,int,int,Vector2,int,int)` |
| 实心判定 | `BlockState.isSolid()` | `Collision.SolidCollision(Vector2,int,int)` |
| 粒子 | `ParticleSpray` + 发包 | `Dust.NewDust` / `NewDustPerfect` |
| 降雨 | `level.isRaining()` / `setRaining` | `Main.raining` / `Main.rainTime` / `Main.maxRaining` / `Main.cloudAlpha` / `Main.windSpeedCurrent` |
| 图格尺寸 | 1 block = 1 unit | **1 图格 = 16 像素** |
| 方块坐标 → 中心 | `Vec3.atCenterOf(pos)` | `new Vector2(tx*16+8, ty*16+8)` |

---

## 10. 建议的落地顺序（按风险从低到高）

1. **先做 A 类 101 条**（零改动，直接实现，用来验证 VM 正确性）
2. **再做 B 类中的"机械改动"**（分量 3→2、方块坐标映射）：算术、`const/vec/*`、`beep`、`compare_block/*`
3. **然后做空间类**：`entity_pos/*`、`get_entity_velocity`、`get_entity_height`、
   `get_entity*`/`zone_entity*`（2D 矩形相交，比 3D 简单）
4. **再做射线**（`raycast*`）—— 需要 `PlotTileLine` + 自写 2D 射线-矩形相交
5. **最后做 C 类 23 条**（每条都是独立小项目：法术环、哨兵、阿卡夏、飞行、雷击）

**不建议**在 §4.2 的"玩家视线同步（多人）"问题解决前，就去做 `blink`/`teleport`/`add_motion`
—— 否则单机能跑、联机会歪，返工成本高。
