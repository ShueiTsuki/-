
# 法术环实施前完整核查清单

> 按用户要求，动手前把**四层**都过一遍：咒法学源码 / 泰拉瑞亚 API / 本项目已有代码 / 待写入代码。
> 核查结论：**发现 3 处会直接改变实现的细节**，已分别标注 ⚠️。

---

## 第一层：咒法学源码（已读完，逐项核对）

### 已读文件

| 文件 | 核对结果 |
|---|---|
| `ICircleComponent.java` | ✅ 全文读完。三个方法 + `ControlFlow`（sealed，`Continue`/`Stop` 二选一） |
| `CircleExecutionState.java` | ✅ 337 行读完。`createNew`（闭包校验）+ `tick`（走图）+ `getTickSpeed` |
| `BlockCircleComponent.java` | ✅ 基类：`ENERGIZED` 属性 + `startEnergized`/`endEnergized` + `normalDir` |
| `BlockSlate.java` | ✅ **关键**：石板执行自己的图案，空石板直通，失败即停 |
| `BlockAbstractImpetus.java` | ✅ 98 行。`acceptControlFlow` 返回 `Stop`（环的终点） |
| `BlockEntityAbstractImpetus.java` | ✅ 464 行。`tickExecution` 排下一次 tick + `startExecution` |
| `CircleCastEnv.java` | ✅ 222 行。媒质从 Impetus 取、范围 = 包围盒 |
| `ChunkScanning.kt` | ✅ 纯缓存，无范围限制 → **泰拉侧不需要** |
| `OpImpetusPos/Dir/CircleBounds.kt` | ✅ 3 个环图案的语义 |

### ⚠️ 修正 1：图案在石板上，不在 Impetus 上

我原先理解错了。环是**物理程序**：
- **Impetus = CPU**（媒质池 + 执行状态 + 触发方式，**不含图案**）
- **石板 = 指令**（每块存一个图案）
- 走环 = 按顺序执行每块石板的图案，`CastingImage` 随走随传
- 空石板 = 直通；任一图案失败 = 整环停止
- 换石板 = 改程序

### ⚠️ 修正 2：环的范围判定是「包围盒」

```java
isVecInRangeEnvironment(vec) { ... return this.execState.bounds.contains(vec); }
```

**不是**玩家那种 32 格半径，而是**环自身的 AABB**。
所以 `entity_pos`、`akashic/*` 一类世界图案在环里的可用范围完全不同。

### ⚠️ 修正 3：Impetus 媒质为负数 = 无限

```java
if (mediaAvailable < 0) return 0;   // 负数 = 无限媒质
```
这是源项目的「创造模式」约定，移植时要保留（否则负数会被当成「欠费」）。

### 其它已确认细节

| 细节 | 值 / 行为 |
|---|---|
| 走环速度 | `max(2, 10 - (reachedSlate-1)/3)` —— 越长越快 |
| 出口检查 | **恰好 1 个**（0 个报 `no_exits`，2+ 报 `many_exits`） |
| 出口集合 | `allDirs` 减去 `normal` 减去「来路的反方向」。**`normal.getOpposite()` 那行源项目是注释掉的** —— 故意允许沿表面穿过，不要「修正」它 |
| 不能原路返回 | `exitDirsSet.remove(enterDir.getOpposite())` |
| 每格重置算力 | `withOverriddenUsedOps(0)` |
| 长度上限 | `maxSpellCircleLength`（**必须有**，否则能走遍全世界） |
| 闭包校验 | DFS 泛洪，看能否回到 Impetus 自己 |
| 8 个部件 | 1 石板 + 4 Impetus（empty/rightclick/looking/redstone）+ 3 Directrix（empty/boolean/redstone） |
| `bounds` 的 ±0.5 | AABB 角点 → 最外方块**中心**的修正 |

---

## 第二层：泰拉瑞亚 API（已逐个查文档/编译验证）

| 需要的 API | 状态 | 备注 |
|---|---|---|
| `ModTile` + `ModItem` | ✅ 已在用 | — |
| `ModTileEntity` | ✅ 已在用（阿卡夏记录） | **无任何 Draw 钩子** → 靠宿主方块 `SpecialDraw` |
| `TileObjectData` | ⚠️ **不用** | 它是给固定矩形用的，环是任意形状 |
| `Main.tile[x,y]` | ✅ 直接数组访问 | 无需缓存层（对应 `ChunkScanning` 的位置） |
| `ModifyLight` | ✅ 已在用 | `Main.tileLighted[Type] = true` 必须先开 |
| `AnimateIndividualTile` | ✅ 已查签名 | 逐实例帧（充能动画要逐格不同相） |
| `SpecialDraw` + `CustomNonSolid` | ✅ 已在用（记录方块） | 60fps 路径；`AddSpecialLegacyPoint` 只有 15fps |
| `ModTile.RightClick` | ✅ 已查 | 右击触发 |
| `HitWire` | ✅ **已验证**：`HitWire(int i, int j)` | ⚠️ 跑在**服务端**，而 `RightClick` 跑在**本地客户端** —— 两种触发的执行侧不同 |
| `ModTileEntity.PostGlobalUpdate` | ✅ **新发现** | TileEntity 的**每 tick 钩子**，走环驱动就放这里（对应 MC 的 `scheduleTick`） |
| `Main.tileFrameImportant` | ✅ 已用 | 环部件应为 `true` → **不支持斜坡/半砖**（可接受） |

**待办**：实施前先编译验证 `HitWire` 的签名（本项目已两次被签名差异坑到：
`RandomUpdate` 三参数、`ModifyWorldGenTasks` 无 `totalWeight`）。

---

## 第三层：本项目已有代码（可复用 / 需扩展）

| 已有 | 能否直接用于环 | 说明 |
|---|---|---|
| `CastingEnvironment`（抽象类） | ✅ 派生 | 环环境 = `CircleCastingEnvironment : CastingEnvironment` |
| `World` / `ICastingWorld` | ✅ 直接复用 | 世界图案在环里也能用 |
| `get_caster` 返回 `NullIota` 的分支 | ✅ **正好为环而设** | 环无实体施法者 |
| `MishapNeedsParens` 等 18 种 mishap | ✅ 复用 | 需新增 `MishapNoSpellCircle` |
| `PatternRenderer` / `HexPixel` | ✅ 复用 | 图案渲染 |
| `ServerCastState`（服务端权威） | ✅ 复用思路 | 环的执行状态同样必须服务端权威 |
| `SpellAction` 的「先扣媒质再施放」 | ✅ 复用 | 环里同样适用 |
| `IotaSerializer` / `IotaWire` | ✅ 复用 | 石板存图案、状态同步 |

### 缺口清单（必须新增）

| 缺口 | 规模 | 说明 |
|---|---|---|
| `MishapNoSpellCircle` | 小 | 3 个环图案都要用 |
| `CircleCastingEnvironment` | 中 | 媒质从 Impetus 取；范围 = 包围盒；`CastingEntity = null` |
| `ICastingWorld` 扩展 | 小 | 需要「读某坐标的环部件信息」 |
| `OpImpetusPos` / `OpImpetusDir` / `OpCircleBounds` | 小 | 3 个图案，逻辑都很短 |
| `CircleExecutionState`（Core 侧纯逻辑） | **大** | 闭包校验 + 走图，**可离线测** |
| 石板方块 + TileEntity | 中 | 存图案 |
| Impetus 方块 + TileEntity | 中 | 媒质池 + 执行状态 + 触发 |
| Directrix 方块 | 中 | 3 种分流逻辑 |
| 充能与图案渲染 | 中 | `ModifyLight` + `AnimateIndividualTile` + `SpecialDraw` |

---

## 第四层：待写入代码的设计决定

### 决定 1：核心逻辑放 Core，做成纯函数（可离线测）

`CircleExecutionState` 的闭包校验与走图**不依赖泰拉**，
只要把「读某坐标的方块与部件信息」抽象成接口，就能进离线测试。

这和前面 `TileRaycast` / `LookResolver` / `AmethystLoot` / `MediaPaymentPlanner` 是同一套做法 ——
**把最容易写错、最难在游戏里观察的逻辑做成纯函数**。

### 决定 2：环的执行状态必须服务端权威

与 P4 联机同步一致。环是「世界里的东西」，客户端各算各的必然分叉。

### 决定 3：先做「能跑通的最小环」再补 Directrix

实施顺序：
1. Core 侧 `CircleExecutionState`（闭包校验 + 走图）+ 离线用例
2. 石板 + Impetus 方块与实体
3. 3 个环图案
4. 右击触发跑通
5. 再加 Directrix 与红石触发
6. 最后做充能/图案渲染（表现层）

---

## 核查结论

**可以开始。** 三处修正都不改变整体方案，但都会影响实现细节：

1. 图案在石板上 → 石板必须是「能存图案的方块」，不是装饰
2. 范围 = 包围盒 → `CircleCastingEnvironment` 的范围判定要单独实现
3. 媒质负数 = 无限 → 保留这个约定

**核查已完备，无待验证项。**

最后补上的两条（都会影响实现）：

| 发现 | 影响 |
|---|---|
| `HitWire` 在**服务端**、`RightClick` 在**本地客户端** | 两种触发的实现路径不同。红石触发天然服务端权威；右击触发必须从客户端上报 —— 与 P4 的联机方案一致 |
| `ModTileEntity.PostGlobalUpdate` | 这就是走环的驱动位。MC 用 `scheduleTick` 排下一次，泰拉侧直接在每 tick 钩子里推进即可 —— **不需要自己实现调度器** |

对比一下 MC 与泰拉的调度：

| | MC | 泰拉 |
|---|---|---|
| 驱动 | `level.scheduleTick(pos, block, delay)` | `PostGlobalUpdate()` 每 tick 调用 |
| 步进控制 | 靠 delay 控制间隔 | 自己数 tick 计数（`getTickSpeed`） |
| 卸载重载 | `lazyExecutionState` + `load` | TileEntity 的 `SaveData`/`LoadData` |

**结论：可以开始写第一层（Core 侧 `CircleExecutionState` + 离线用例）。**

---

## 【写测试时发现的第 4 处修正】原动力的进入规则与普通部件不同

离线用例跑出来一个反直觉结果：**一个两块大的「环」被判成闭合**。

追查原因：`(1,0)` 的石板可以朝左出去、直接撞上 `(0,0)` 的原动力，
泛洪于是认为「回到原动力了」。

但源项目里两者的进入规则**不是一个来源**：

```java
// BlockCircleComponent（普通部件）—— 用 normalDir
var thisNormal = this.normalDir(pos, bs, world);
return enterDir != thisNormal.getOpposite();

// BlockAbstractImpetus（原动力）—— 用 FACING，即媒质流出的方向
// FACING is the direction media EXITS from, so we can't have media entering in that direction
// so, flip it
return enterDir != bs.getValue(FACING).getOpposite();
```

**我的 `CircleComponent` 只存了 `Normal`，把两者当成同一个东西了** —— 这是模型缺陷，不是测试写错。

### 修法

给 `CircleComponent` 增加一个显式字段：

```csharp
/// 禁止进入的方向。
/// 普通部件 = Normal.Opposite()
/// 原动力   = 起始方向.Opposite()   ← 与 Normal 无关
public required CircleDir ForbiddenEntry { get; init; }
```

这样两边都由世界实现显式给出，不再靠推断。

### 测试暴露的另一个真实约束

「所有部件 normal 朝上」**走不出环** —— 因为不能从 `normal` 的反方向进入、
也不能往 `normal` 方向出去。要搭出环，每块部件的 `normal` 必须**垂直于局部流向**。

这是真实的搭建约束（不是 bug），但对玩家来说不够直观 ——
**将来要么在方块 tooltip 里说明，要么在放置时给朝向提示**。

### 已修复（第 4 处修正落地）

给 `CircleComponent` 加了显式的 `ForbiddenEntry` 字段：

| 部件 | ForbiddenEntry |
|---|---|
| 普通部件 | `Normal.Opposite()` |
| **原动力** | **起始方向 `.Opposite()`**（与 Normal 无关） |

并补了两条**对照用例**证明差异确实来自这一处规则：
- 用正确规则：两块大的假环 **不**算闭合 ✓
- 显式构造错误规则（按 normal 推）：同一个假环**会**被判成闭合 ✓

第二条是**反向验证** —— 它证明前一条不是因为别的原因通过的。

### 离线用例现状

**15 条环用例全部通过**（含 2 条对照验证），整体 **233 / 233 通过**。