
# 法术环机制（源码核对版）

> 本文是读完 `ICircleComponent` / `CircleExecutionState` / `BlockSlate` /
> `BlockCircleComponent` / `ChunkScanning` 之后的**修正版**理解。
>
> 注意：**修正了一处重要误解**：我原先以为图案存在 Impetus 上。**错了。**

---

## 一、核心机制：环是一个「物理程序」

```
Impetus（CPU）  = 媒质池 + 执行状态 + 触发方式，**不含任何图案**
石板 Slate      = 一条「指令」——存一个图案
Directrix       = 控制流分流（条件/红石）
```

走环 = **按顺序执行每块石板上的图案**，`CastingImage`（整个 VM 栈）随走随传。

### `BlockSlate.acceptControlFlow` 的原文逻辑

```java
var exitDirsSet = this.possibleExitDirections(pos, bs, world);
exitDirsSet.remove(enterDir.getOpposite());        // ① 不能原路返回

if (pattern == null)
    return new ControlFlow.Continue(imageIn, exitDirs);   // ② 空石板 = 直通

var vm = new CastingVM(imageIn, env);
var result = vm.queueExecuteAndWrapIota(new PatternIota(pattern), world);  // ③ 执行图案
return result.getResolutionType().getSuccess()
    ? new ControlFlow.Continue(vm.getImage(), exitDirs)   // ④ 带新状态继续
    : new ControlFlow.Stop();                             // ⑤ 失败则整环停止
```

四个要点：

| 要点 | 含义 |
|---|---|
| **不能原路返回** | 出口集合要减去来路，否则控制流会原地打转 |
| **空石板直通** | 空石板不执行任何东西，只改流向 —— 用来「铺路」 |
| **带状态继续** | `CastingImage` 被一路带走，所以前面的石板能给后面的留栈 |
| **失败即停** | 任一图案求值失败，整个环停下来（不是跳过继续） |

> 所以法术环是**可改写的程序**：换掉某块石板就改了程序。

---

## 二、每个环部件都有 `ENERGIZED` 状态

`BlockCircleComponent`（所有环部件的基类）持有：

```java
public static final BooleanProperty ENERGIZED = BooleanProperty.create("energized");
```

走环时逐块 `startEnergized` → 方块发光（Impetus 的 `lightLevel` 是 `ENERGIZED ? 15 : 0`）。

**泰拉侧对应**：`ModifyLight` 里判断该格是否处于充能状态
（状态存在 TileEntity 或 `TileFrameX` 里）。

---

## 三、`canEnterFromDirection`：流向的几何约束

```java
// BlockCircleComponent
var thisNormal = this.normalDir(pos, bs, world);
return enterDir != thisNormal.getOpposite();

// BlockAbstractImpetus（注释写明了原因）
// FACING is the direction media EXITS from, so we can't have media entering in that direction
return enterDir != bs.getValue(FACING).getOpposite();
```

`normalDir` = 「这个方块哪一面朝外」。地板上的石板 normal 是 UP，
所以**只能从上方进入** —— 控制流在地板方块的顶上走。

`normalDirOfOther(pos, world, recursionLeft)` 带递归：
墙上的方块要顺着邻居找自己的朝向（递归上限 16，超了默认 UP）。

---

## 四、闭包校验：DFS 泛洪

`CircleExecutionState.createNew` 的算法：

```
栈里放 (起始方向, impetus 相邻格)
while 栈非空:
    弹出 (enterDir, pos)
    若该格不是 ICircleComponent            -> 跳过
    若 !canEnterFromDirection(enterDir)     -> 跳过
    若是新格子:
        标记已见、更新包围盒
        把 possibleExitDirections 全部压栈
    若 已见格子数 >= maxSpellCircleLength   -> 失败（防世界级巨环）
最后：若见过 Impetus 自己 -> 闭合成功，否则失败
```

**关键**：闭包 = **泛洪能不能回到 Impetus 自己**。

### `ChunkScanning` 不用移植

它是 MC 的**区块读取缓存**（MC 不擅长任意区块读取）。
**没有任何范围限制** —— 唯一的范围保护就是 `maxSpellCircleLength`。

> 泰拉的 `Main.tile[x,y]` 是直接二维数组访问，**不需要缓存层**。
> 但**更需要注意长度上限** —— 没有区块加载的天然阻力，
> 一个跨半张地图的环会真的走完几百万格。**这个上限必须有。**

---

## 五、执行是 tick 驱动的

```java
public void tickExecution() {
    var shouldContinue = state.tick(this);
    if (!shouldContinue) endExecution();
    else level.scheduleTick(..., state.getTickSpeed());   // 排下一次
}

protected int getTickSpeed() {
    return Math.max(2, (int) (10 - (this.reachedSlate - 1) / 3));
}
```

- **每 tick 走一格**，不是一次算完 → 玩家能看见控制流逐格前进
- **环越长走得越快**：起步 10 MC 刻/格（半秒 = 泰拉 30 帧），每 3 格减 1，最低 2 刻（6 帧）

### `tick` 里的三个细节

```java
// ① 恰好 1 个可用出口
for (var exit : cont.exits) {
    if (目标块是 ICircleComponent && canEnterFromDirection(...)) {
        if (found != null) { postDisplay("many_exits"); halt = true; break; }
        else found = exit;
    }
}
if (found == null) { postNoExits(...); halt = true; }   // ② 没有出口

// ③ 每块走完后重置算力
currentImage = cont.update.withOverriddenUsedOps(0);
```

① **可用出口必须恰好 1 个**（0 个、2 个以上都报错）
② 没有出口时报 `no_exits`
③ **每块走完后 `withOverriddenUsedOps(0)` 重置算力** ——
   不重置的话长环跑到一半就会算力耗尽中断

---

## 六、八个环部件（核对无误）

| 类别 | 数量 | 作用 |
|---|---|---|
| `BlockSlate` | 1 | **存图案**，环的「指令」 |
| Impetus | 4 | `empty`（空白：不会启动，只传导，是脑叶切除的原料）/ `rightclick` / `looking`（注视）/ `redstone` —— CPU + 触发方式 |
| Directrix | 3 | `empty` / `boolean` / `redstone` —— 控制流分流 |

---

## 七、泰拉侧实现要点（按上述修正）

| 事项 | 做法 |
|---|---|
| 石板 | 1×1 `ModTile` + TileEntity 存图案；`tileFrameImportant = true` |
| Impetus | 1×1 `ModTile` + TileEntity 存媒质与执行状态 + 触发方式 |
| 闭包校验 | DFS 泛洪，回到 Impetus 即闭合；**必须有长度上限** |
| 走环 | 每 tick 一格；恰好 1 个出口；**每格重置算力** |
| 不能原路返回 | 出口集合减来路 |
| 充能发光 | `ModifyLight` + 逐实例帧（`AnimateIndividualTile`） |
| 触发 | 右击 / `HitWire`（复用泰拉红石）/ 注视 |
| 执行环境 | `CircleCastEnv` → 泰拉侧 `CircleCastingEnvironment`。施法者 = 启动它的玩家 / 牧师促动石绑定的玩家，没有时为 **null**（2026-09-30 按原版改；这里早先写的是一律无人施法） |

> 最后一条正好验证了早先实现 `get_caster` 时特意保留的分支：
> **无实体施法者时返回 `NullIota` 而不是报错** —— 没有施法者的法术环（比如没人绑定的牧师促动石）就是那个场景。
