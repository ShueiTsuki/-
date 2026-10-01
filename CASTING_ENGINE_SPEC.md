# 咒法学施法核心机制拆解（Casting Engine Spec）

> 目标读者：未读过原项目、需要照着实现一份等价 C# 虚拟机的人。
> 本文只覆盖**施法求值链路**。网格几何、画布交互、zappy 渲染、媒质单位见 `HEXCASTING_MECHANICS.md`，不重复。
>
> 源：`FallingColors/HexMod` v0.11.4（commit `6b64165be3`），MIT。
> 所有结论标注 `文件:行号`。凡是通读源码后发现的、与文档/直觉不符的地方，均单独标出。

---

## 0. 术语与三件核心对象

源码里有一份官方 README，是理解本链路的最佳入口：`api/casting/eval/README.md:1-90`。它把三个对象的关系讲得很清楚：

| 对象 | 角色 | 文件 | 是否持久化 |
|---|---|---|---|
| `CastingVM` | **模拟器**（决定下一步做什么） | `api/casting/eval/vm/CastingVM.kt:25` | 否，每次重建 |
| `CastingImage` | **模拟器的内存快照**（栈 + 括号 + 用户数据） | `api/casting/eval/vm/CastingImage.kt:19` | **是，唯一被序列化的东西** |
| `CastingEnvironment` | **"谁在施法"的抽象**（法杖/饰品/法术环） | `api/casting/eval/CastingEnvironment.java:50` | 否 |

> 注意：README 与实际实现有差异，不要照抄 README：
> - README 说入口是 `CastingVM#queueAndExecuteIotas`（`README.md:18`），实际方法是 **`queueExecuteAndWrapIotas`**（`CastingVM.kt:41`）。
> - README 的 11 步流水线（`README.md:15-37`）描述的是意图，实际控制流以 `CastingVM.queueExecuteAndWrapIotas`（`CastingVM.kt:41-102`）为准。

---

## 1. 求值主循环

### 1.1 入口

```kotlin
// CastingVM.kt:41-102
fun queueExecuteAndWrapIotas(iotas: List<Iota>, world: ServerLevel): ExecutionClientView {
    var continuation = SpellContinuation.Done.pushFrame(FrameEvaluate(SpellList.LList(0, iotas), false))
    val info = TempControllerInfo(earlyExit = false)
    var lastResolutionType = ResolvedPatternType.UNRESOLVED
    while (continuation is SpellContinuation.NotDone && !info.earlyExit) {
        val next = continuation.frame
        val image2 = next.evaluate(continuation.next, world, this).let { result -> /* 见 1.3 两道保险 */ }
        if (image2.newData != null) this.image = image2.newData
        this.env.postExecution(image2)
        continuation = image2.continuation
        lastResolutionType = image2.resolutionType
        try { performSideEffects(image2.sideEffects) }
        catch (e: Exception) { /* 转成 MishapInternalException */ }
        info.earlyExit = info.earlyExit || !lastResolutionType.success
    }
    if (continuation is SpellContinuation.NotDone) { /* 更新 lastResolutionType */ }
    val (stackDescs, ravenmind) = generateDescs()
    val isStackClear = image.stack.isEmpty() && image.parenCount == 0 && !image.escapeNext && ravenmind == null
    this.env.postCast(image)
    return ExecutionClientView(isStackClear, lastResolutionType, stackDescs, ravenmind)
}
```

**循环不变式**：`continuation` 是"待执行帧栈"；每一轮取栈顶帧执行一次，产出新的 `CastingImage` + 新的 `continuation` + 副作用列表。帧栈为空（`Done`）或遇到失败（`!resolutionType.success`）时退出。

### 1.2 一个"帧"是什么

`SpellContinuation` 是一个**不可变单链表**（`SpellContinuation.kt:12-17`）：

```kotlin
sealed interface SpellContinuation {
    object Done : SpellContinuation
    data class NotDone(val frame: ContinuationFrame, val next: SpellContinuation) : SpellContinuation
    fun pushFrame(frame: ContinuationFrame) = NotDone(frame, this)
}
```

`ContinuationFrame` 有三种实现（`eval/README.md:51-68`）：

| 帧 | 作用 | 关键文件 |
|---|---|---|
| `FrameEvaluate` | 一串**待执行**图案，逐个消费 | `vm/FrameEvaluate.kt:22` |
| `FrameForEach` | Thoth（`foreach`）的迭代状态 | `vm/FrameForEach.kt:28` |
| `FrameFinishEval` | 空操作，仅作 Charon（halt）的**中止边界标记** | `vm/FrameFinishEval.kt:16` |

`FrameEvaluate.evaluate` 是理解 **TCO（尾调用优化）** 的关键（`FrameEvaluate.kt:27-49`）：

```kotlin
override fun evaluate(continuation, level, harness): CastResult =
    if (list.nonEmpty) {
        val newCont = if (list.cdr.nonEmpty) continuation.pushFrame(FrameEvaluate(list.cdr, isMetacasting))
                      else continuation          // ← 最后一个元素不再压帧：这就是 TCO
        val update = harness.executeInner(list.car, level, newCont)
        if (isMetacasting && update.sound != MISHAP) update.copy(sound = HERMES) else update
    } else {
        CastResult(ListIota(list), continuation, null, listOf(), EVALUATED, HERMES)
    }
```

**要点**：`list.cdr` 非空才压新帧；若只剩一个元素就不压——因此长的图案序列执行时帧栈深度恒定为 O(1)，不会随图案数量增长。

### 1.3 两道保险（尺寸与算力）

主循环对每个 `CastResult` 做两次检查（`CastingVM.kt:53-72`）：

```kotlin
if (result.newData != null && IotaType.isTooLargeToSerialize(result.newData.stack)) {
    // → MishapStackSize，resolutionType = ERRORED，newData = null
} else if (result.newData != null && result.newData.opsConsumed > env.maxOpCount()) {
    // → MishapEvalTooMuch，resolutionType = ERRORED，newData = null
}
```

- `isTooLargeToSerialize`：`depth() >= MAX_SERIALIZATION_DEPTH (=256)` 或累计 `size() >= MAX_SERIALIZATION_TOTAL (=1024)`（`IotaType.java:83-95`、`HexIotaTypes.java`）
- `maxOpCount()`：来自服务端配置（`CastingEnvironment.java:107-109` → `HexConfig.server().maxOpCount()`）

> **移植要点**：`newData = null` 表示"丢弃这次状态更新"，主循环里 `if (image2.newData != null) this.image = image2.newData`（`CastingVM.kt:75-77`）会保留旧 image。C# 侧必须用可空语义，不能用"就地修改"。

### 1.4 单 iota 执行：`executeInner`

`CastingVM.executeInner`（`CastingVM.kt:108-162`）是所有 iota 的分派点，**刻意不抛异常**（注释 `CastingVM.kt:104-107`）：

```kotlin
fun executeInner(iota, world, continuation): CastResult {
  try {
    // ① Consideration 转义（不可被覆写，所以放在 VM 而非 Iota）
    if (image.escapeNext) {
        if (image.parenCount > 0) newImage = image.copy(escapeNext=false).withNewParenthesized(iota, escaped=true)
        else                      newImage = image.copy(stack = stack + iota, escapeNext = false)
        return CastResult(iota, continuation, newImage, listOf(), ESCAPED, NORMAL_EXECUTE)
    }
    // ② 括号内 vs 括号外
    return if (image.parenCount > 0) iota.executeInParens(this, world, continuation)
           else                      iota.execute(this, world, continuation)
  } catch (exception: Exception) {
    // ③ 任何未捕获异常 → MishapInternalException，resolutionType = ERRORED
    return CastResult(iota, continuation, null, listOf(DoMishap(MishapInternalException(exception), ctx)), ERRORED, MISHAP)
  }
}
```

**这三个分支的顺序不能变**：转义优先级最高（`escapeNext` 一置位，下一个 iota 无论是什么都被"逃逸"而不是执行）。

### 1.5 Iota 如何真正执行图案：`PatternIota.lookupAndOperate`

`PatternIota.execute` / `executeInParens` 都转发到 `lookupAndOperate(vm, cont, inParens)`（`iota/PatternIota.java:70-185`）：

```java
var lookup = PatternRegistryManifest.matchPattern(this.getPattern(), vm.getEnv());
vm.getEnv().precheckAction(lookup);        // ← 可能抛 MishapDisallowedSpell

Action action;
if (lookup instanceof Normal || lookup instanceof PerWorld) {
    key = ...;
    var reqsEnlightenment = isOfTag(actionRegistry, key, HexTags.Actions.REQUIRES_ENLIGHTENMENT);
    action = registry.get(key).action();
    if (reqsEnlightenment && !vm.getEnv().isEnlightened()) throw new MishapUnenlightened();
} else if (lookup instanceof Special special) {
    action = special.handler.act();
} else if (lookup instanceof Nothing) {
    if (inParens) {
        // 括号内遇到无效图案：不报错，加入括号列表，resolutionType = ESCAPED
        return new CastResult(this, continuation, image.withNewParenthesized(this,false), List.of(), ESCAPED, NORMAL_EXECUTE);
    } else {
        throw new MishapInvalidPattern(this.getPattern());   // 括号外才报错
    }
}

IOperationResult result = inParens
    ? action.operateInParens(env, image, continuation, this)   // → ParenthesizedOperationResult
    : action.operate(env, image, continuation);                // → OperationResult
```

`catch (Mishap mishap)` 分支（`PatternIota.java:170-184`）负责把 mishap 折成 `DoMishap` 副作用，并决定是否清空括号：

```java
boolean wipeParens = continuation instanceof NotDone cnd
                  && cnd.getFrame() instanceof FrameEvaluate fe && fe.isMetacasting();
return new CastResult(this, continuation,
    wipeParens ? vm.getImage().withResetEscape() : null,   // ← 元编程中的 mishap 会清空括号
    List.of(new DoMishap(mishap, new Context(this.getPattern(), castedName.get()))),
    mishap.resolutionType(vm.getEnv()), MISHAP);
```

**关键规则**：非元编程（staffcast）时 mishap 后 `newData = null`，括号计数**保持不变**——所以玩家仍处于"列表构建模式"。这条在 `Action.kt:56` 也有对应注释。

### 1.6 栈的状态如何变化

`CastingImage` 是**不可变 data class**（`CastingImage.kt:19-29`），所有修改都是 `copy()`：

```kotlin
data class CastingImage(
    val stack: List<Iota>,              // 数据栈（栈底在前，栈顶在末尾）
    val parenCount: Int,                // 打开的括号层数
    val parenthesized: List<ParenthesizedIota>,  // 括号内已收集的 iota
    val escapeNext: Boolean,            // 下一个 iota 是否要逃逸
    val opsConsumed: Long,              // 已消耗操作数（算力预算）
    val userData: CompoundTag           // 施法者自定义数据（如 ravenmind）
)
```

常用方法（`CastingImage.kt:62-88`）：`withUsedOp()`、`withUsedOps(n)`、`withOverriddenUsedOps(n)`、`withResetEscape()`、`withNewParenthesized(iota, escaped)`。

`ParenthesizedIota` 的 `escaped` 标志（`CastingImage.kt:31-40`）用途明确：**`OpUndo` 用它判断回溯时是否需要调整括号计数**——转义进来的括号不影响计数。

### 1.7 序列化

**只有 `CastingImage` 会被存盘**（`CastingImage.kt:90-99`）：

```
stack          → ListTag of IotaType.serialize(iota)
open_parens    → int (parenCount)
parenthesized  → { iotas: ListIota, escaped: ByteArray }
escape_next    → bool
ops_consumed   → long
userdata       → CompoundTag
```

读档时若任何异常，回退到全新 `CastingImage()`（`CastingImage.kt:139-142`）。

**`SpellContinuation` 不持久化**——这就是为什么"每个图案一笔"能成立：每画一笔，服务端从存盘的 `CastingImage` 重建 VM，然后把新图案作为**只有一个元素的 `FrameEvaluate`** 执行（见 §6）。

> **移植要点**：continuation 不存盘，但 `ContinuationIota` 会把 continuation 序列化进栈里（`ContinuationIota.serialize()` → `getContinuation().serializeToNBT()`，`ContinuationIota.java:44-47`）。所以 continuation 的序列化格式**仍然必须实现**，否则 `OpEvalBreakable` 产生的续延 iota 无法存盘。

---

## 2. 续延、括号与惰性求值

### 2.1 转义（escaping）机制总览

三个状态字段协同工作：`parenCount`、`parenthesized`、`escapeNext`。

| 图案 | 作用 | 括号外行为 | 括号内行为 | 文件 |
|---|---|---|---|---|
| `open_paren` | 开始列表 | `parenCount + 1` | 加入列表 **且** `parenCount + 1` | `OpOpenParen.kt:13-30` |
| `close_paren` | 结束列表 | **抛 `MishapNeedsParens`** | 见下 | `OpCloseParen.kt:16-43` |
| `escape`（Consideration） | 使下一个 iota 逃逸 | `escapeNext = true` | 同左 | `OpEscape.kt:13-26` |
| `undo`（Retrospection） | 回退上一个 | **抛 `MishapNeedsParens`** | 弹出 `parenthesized` 末尾，必要时修正 `parenCount` | `OpUndo.kt:16-41` |
| `read_into_parens` | 从手持物读 iota 入列表 | **抛 `MishapNeedsParens`** | 读数据并把 iota 以 `escaped=true` 加入 | `OpReadIntoParens.kt:16-45` |

### 2.2 `MishapNeedsParens` 的确切触发点

**只有三个 Action 在 `operate`（括号外）里无条件抛它**：

- `OpCloseParen.operate` — `OpCloseParen.kt:17-19`
- `OpUndo.operate` — `OpUndo.kt:17-19`
- `OpReadIntoParens.operate` — `OpReadIntoParens.kt:17-19`

语义：**这些图案只能在括号内使用**。括号外画一个"闭括号"是没有意义的，因为没有任何东西可供闭合。

`MishapNeedsParens.execute` 会把图案本身推回栈（`MishapNeedsParens.kt`）：

```kotlin
override fun execute(env, errorCtx, stack) {
    if (errorCtx.pattern != null) stack.add(PatternIota(errorCtx.pattern))
}
```

### 2.3 `close_paren` 的两种括号内路径

`OpCloseParen.operateInParens`（`OpCloseParen.kt:21-43`）按 `newParenCount` 是否归零分叉：

```kotlin
val newParenCount = image.parenCount - 1
if (newParenCount == 0) {
    // 最外层闭合：把收集到的 iota 打包成 ListIota 压栈，清空 parenthesized
    newStack.add(ListIota(image.parenthesized.map { it.iota }))
    image2 = image.withUsedOp().copy(stack=newStack, parenCount=0, parenthesized=listOf())
    return ParenthesizedOperationResult(image2, ..., ResolvedPatternType.EVALUATED)
} else {
    // 形如 "(()"：闭合符号本身作为普通元素进入列表
    newParens.add(ParenthesizedIota(thisIota, false))
    image2 = image.copy(parenCount = newParenCount, parenthesized = newParens)
    return ParenthesizedOperationResult(image2, ..., ResolvedPatternType.ESCAPED)
}
```

**注意 `resolutionType` 的差异**：外层闭合是 `EVALUATED`，内层是 `ESCAPED`。客户端用这个区分图案颜色。

### 2.4 `OpUndo` 的括号计数修正

`OpUndo.operateInParens`（`OpUndo.kt:21-41`）是本链路最微妙的逻辑之一：

```kotlin
val last = newParens.removeLastOrNull()
var newParenCount = image.parenCount
if (last == null) {
    newParenCount = 0            // 列表为空 → 直接归零（处理 open-n-parens 的情况）
} else if (last.iota is PatternIota && !last.escaped) {
    when (last.iota.pattern.angles) {
        OPEN_PAREN.prototype.angles  -> newParenCount--   // 撤销了一个开括号
        CLOSE_PAREN.prototype.angles -> newParenCount++   // 撤销了一个闭括号
    }
}
```

`escaped` 在这里的作用体现出来：**转义进来的括号不参与计数修正**。

`resolutionType` 固定为 `UNDONE`（`OpUndo.kt:40`）。

### 2.5 `OpEval`：惰性求值与 tail-call

`OpEval.operate`（`OpEval.kt:17-21`）弹出栈顶，交给 `exec`：

```kotlin
fun exec(env, image, continuation, newStack, iota): OperationResult {
    val instrs = evaluatable(iota, 0)      // Iota → SpellList
    val newCont =
        if (instrs.left().isPresent ||       // 单个可执行 iota
            (continuation is NotDone && continuation.frame is FrameFinishEval))
            continuation                       // 不装边界
        else
            continuation.pushFrame(FrameFinishEval)   // 装中止边界（Charon 到此为止）

    val instrsList = instrs.map({ SpellList.LList(0, listOf(it)) }, { it })
    val frame = FrameEvaluate(instrsList, true)       // ← isMetacasting = true
    val image2 = image.withUsedOp().copy(stack = newStack)
    return OperationResult(image2, listOf(), newCont.pushFrame(frame), HERMES)
}
```

`evaluatable` 的定义（`ActionUtils.kt:290-298`）：

```kotlin
fun evaluatable(datum: Iota, reverseIdx: Int): Either<Iota, SpellList> =
    when (datum) {
        is ListIota -> Either.right(datum.list)               // 列表：展开执行
        else -> if (datum.executable()) Either.left(datum)    // 可执行 iota（PatternIota / ContinuationIota）
                else throw MishapInvalidIota(datum, reverseIdx, "...evaluatable")
    }
```

**`isMetacasting = true` 的后果**：`FrameEvaluate.evaluate` 里会把声音改成 `HERMES`（`FrameEvaluate.kt:40-44`），并且 mishap 时会清空括号（见 §1.5）。

### 2.6 `OpEvalBreakable` / `OpEval` / `OpHalt` / `OpForEach` 的区别

| 图案 | 语义 | 关键实现 |
|---|---|---|
| `eval`（Hermes' Gambit） | 执行栈顶的 iota/列表 | `OpEval.kt:17-38` |
| `eval_breakable`（Charon's Gambit 的搭档） | 先**把当前续延作为 `ContinuationIota` 压栈**，再 eval | `OpEvalBreakable.kt:11-18` |
| `halt`（Charon's Gambit） | 向上弹帧直到 `breakDownwards` 返回 `stop=true` | `OpHalt.kt:11-31` |
| `for_each`（Thoth's Gambit） | 对列表每项执行代码块 | `OpForEach.kt:15-30` |

`OpHalt` 的弹帧循环（`OpHalt.kt:16-27`）：

```kotlin
while (!done && newCont is NotDone) {
    val newInfo = newCont.frame.breakDownwards(newStack)   // (stop, newStack)
    done = newInfo.first
    newStack = newInfo.second
    newCont = newCont.next
}
if (!done) newStack = listOf()      // 没找到边界 → 清空栈以强制退出
```

`breakDownwards` 在三种帧上的行为：
- `FrameEvaluate` → `false to stack`（继续往下弹）— `FrameEvaluate.kt:24`
- `FrameFinishEval` → `true to stack`（**在此停止**）— `FrameFinishEval.kt:18`
- `FrameForEach` → `true to (baseStack + ListIota(acc + stack))`（停止并把迭代结果展开）— `FrameForEach.kt:47-51`

### 2.7 `ContinuationIota`

`ContinuationIota` 就是把 `SpellContinuation` 当值来用（`ContinuationIota.java:22-88`）：

```java
@Override public CastResult execute(...) {
    return new CastResult(this, this.getContinuation(), vm.getImage(), List.of(), EVALUATED, HERMES);
}
@Override public boolean executable() { return true; }
@Override public int size() {
    // 累加帧栈深度与各帧 size()，最后 Math.min(size, 1)（注释说这是有意为之）
}
```

`execute` 直接**替换整个续延**——这是"把执行状态当一等值"的实现方式。

---

## 3. Iota 类型体系

### 3.1 基类契约

`Iota`（`iota/Iota.java:23-162`）是所有值的基类：

| 成员 | 语义 | 行号 |
|---|---|---|
| `payload` | 实际承载的数据 | `Iota.java:25` |
| `type` | `IotaType` 单例（含颜色、显示、反序列化） | `Iota.java:27` |
| `isTruthy()` | 真值判定 | `Iota.java:38` |
| `toleratesOther(Iota)` | 容差相等（浮点用 `TOLERANCE`） | `Iota.java:43` |
| `serialize()` | 序列化 payload（**不含 type 包装**） | `Iota.java:50` |
| `execute(vm, world, cont)` | 括号外执行；**默认抛 `MishapUnescapedValue`** | `Iota.java:56-69` |
| `executeInParens(...)` | 括号内执行；**默认加入 parenthesized** | `Iota.java:78-86` |
| `executable()` | 是否可执行；**默认 false** | `Iota.java:91-93` |
| `subIotas()` | 子 iota 枚举（用于尺寸校验） | `Iota.java:102-104` |
| `size()` / `depth()` | 尺寸/深度（序列化上限用） | `Iota.java:131-137` |

**只有 `PatternIota` 和 `ContinuationIota` 把 `executable()` 覆写为 true**（`PatternIota.java:80-82`、`ContinuationIota.java:54-57`）。

### 3.2 九种 Iota

注册处：`common/lib/hex/HexIotaTypes.java`。颜色是 `IotaType.color()` 的返回值。

| 类型 | 注册名 | payload | `isTruthy()` | 颜色 | `serialize()` | 文件 |
|---|---|---|---|---|---|---|
| `NullIota` | `null` | 无 | `false` | `0xffaaaaaa` | 空 | `NullIota.java:49` |
| `BooleanIota` | `boolean` | `boolean` | 原值 | `0xffffffff`→见下 | `ByteTag` | `BooleanIota.java:57` |
| `DoubleIota` | `double` | `double` | `!= 0.0` | `0xff55ff55` | `DoubleTag` | `DoubleIota.java:58` |
| `Vec3Iota` | `vec3` | `Vec3` | 非零向量 | `0xffff3030` | `HexUtils.serializeToNBT(Vec3)` | `Vec3Iota.java:71` |
| `EntityIota` | `entity` | `Entity` | 恒 `true` | `0xff55ffff` | `{name: <json>}`（**不存实体本身**） | `EntityIota.java:95` |
| `ListIota` | `list` | `List<Iota>` | 见下 | `0xffaa00aa` | `ListTag` | `ListIota.java:149` |
| `PatternIota` | `pattern` | `HexPattern` | 恒 `true` | `0xffffaa00` | `HexPattern.serializeToNBT()` | `PatternIota.java:205` |
| `GarbageIota` | `garbage` | 无 | `false` | `0xff505050` | 空 | `GarbageIota.java:51` |
| `ContinuationIota` | `continuation` | `SpellContinuation` | 恒 `true` | `0xffcc0000` | `getContinuation().serializeToNBT()` | `ContinuationIota.java:74` |

具体数值校正（逐文件核对）：
- `BooleanIota.color()` = `0xff_ffff55`（不是 ffffffff）
- `DoubleIota.TOLERANCE = 0.0001`（`DoubleIota.java:14`），`getDouble()` 会先过 `HexUtils.fixNAN`
- `Vec3Iota.isTruthy()` = `!(x==0 && y==0 && z==0)`（`Vec3Iota.java:30-32`）

`ListIota` 的 `size()` / `depth()` 在构造时预计算（`ListIota.java:33-40`）：`depth = max(子 depth) + 1`，`size = 1 + Σ 子 size`。

### 3.3 `IotaType`：类型单例

`IotaType`（`iota/IotaType.java:21-192`）是每个类型的元数据容器：

```java
public abstract T deserialize(Tag tag, ServerLevel world);
public abstract Component display(Tag tag);   // 客户端用，不带 world
public abstract int color();
public boolean usesListCommas() { return true; }
public Component typeName();
```

**序列化的外层包装**（`IotaType.java:62-81`）：

```
{
  "type": "hexcasting:double",
  "data": <iota.serialize() 的结果>
}
```

`IotaType.serialize` 有一个重要的自愈逻辑（`IotaType.java:71-75`）：若 iota 太大，就**递归序列化一个 `GarbageIota` 取代它**。

`IotaType.deserialize`（`IotaType.java:127-144`）在 type 缺失/无法识别时返回 `GarbageIota`，在 `data` 缺失时同样返回 `GarbageIota`，在 `deserialize` 抛 `IllegalArgumentException` 时也返回 `GarbageIota`。

### 3.4 类型签名如何参与图案匹配

> 注意：**重要纠偏**：图案匹配**不依赖 iota 的类型签名**。

实际匹配流程（`PatternRegistryManifest.matchPattern`，`common/casting/PatternRegistryManifest.java:92-119`）：

```java
var sig = pat.getAngles();                    // 纯几何：角度序列
if (NORMAL_ACTION_LOOKUP.containsKey(sig)) return Normal(key);      // O(1) 哈希查表
var entry = perWorldPatterns.lookup(pat.anglesSignature());          // 世界专属
if (entry != null) return PerWorld(entry.key(), true);
var shMatch = matchPatternToSpecialHandler(pat, environment);        // SpecialHandler
if (shMatch != null) return Special(...);
return Nothing;
```

匹配键是 **`List<HexAngle>`（角度序列）**，由 `ActionRegistryEntry.prototype().getAngles()` 索引（`ActionRegistryEntry.java:14`、`PatternRegistryManifest.java:51`）。**起始方向不参与匹配键**——注释说明 `startDir` 是"书里显示的规范起始方向"（`ActionRegistryEntry.java:9-11`）。

类型检查发生在**执行阶段**，由各 Action 自己在 `operate` 里做（如 `ConstMediaAction` 用 `getList`、`getDouble` 等辅助函数抛 `MishapInvalidIota`）。

> 这与本仓库 `PATTERN_CATALOG.json` 的匹配键口径需要统一：我们当前用「起始方向 + 角度签名」做键，比原项目**更严格**。若要与原版行为一致，应改为**只用角度签名**。
> （2026-10-01 核对：已改。`PatternRegistry` 现在只按角度签名匹配，`HexPattern.MatchKey()` 就是 `AnglesSignature()`。）

---

## 4. Mishap 全表

基类 `Mishap`（`mishaps/Mishap.kt:23-118`）是 `RuntimeException`：

```kotlin
abstract fun accentColor(env, errorCtx): FrozenPigment          // 粒子颜色
open fun particleSpray(env): ParticleSpray                       // 默认向上喷 40 个粒子
open fun resolutionType(env) = ResolvedPatternType.ERRORED       // 默认失败
abstract fun execute(env, errorCtx, stack: MutableList<Iota>)    // 实际效果（可改栈）
protected abstract fun errorMessage(env, errorCtx): Component?
fun executeReturnStack(env, errorCtx, stack) = stack.also { execute(...) }
```

`MishapEnvironment`（`eval/MishapEnvironment.java:16-49`）提供六种标准惩罚原语：

| 原语 | 玩家实现（`env/PlayerBasedMishapEnv.java`） | 行号 |
|---|---|---|
| `yeetHeldItemsTowards(pos)` | 双手物品抛出，速度朝 `pos` 方向 0.5 并带随机抖动 | `:19-28` |
| `dropHeldItems()` | 朝视线方向抛 | `:31-34` |
| `damage(healthProportion)` | `trulyHurt(health * proportion)`，伤害类型 OVERCAST | `:37-39` |
| `drown()` | 氧气 <200 时先扣 2 血，再把氧气置 0 | `:42-47` |
| `removeXp(amount)` | `giveExperiencePoints(-amount)` | `:50-52` |
| `blind(ticks)` | 施加失明效果 | `:55-57` |

`Mishap.trulyHurt`（`Mishap.kt:83-116`）是"无视无敌帧的强制伤害"实现——绕过 `invulnerableTime`，必要时直接改 `health`。

### 4.1 全部 31 种具体 Mishap

> 注：源目录含基类共 **32 个文件**；**具体 Mishap 为 31 种**。
> 分布：`mishaps/` 目录 28 个 + `mishaps/circle/` 子目录 3 个
> （`MishapBoolDirectrixEmptyStack`、`MishapBoolDirectrixNotBool`、`MishapNoSpellCircle`）。
> 此前的规划文档写的"27 种"是早期按类名 grep 的漏计，以本表为准。
> 排查提示：只列 `mishaps/` 目录会漏掉那 3 个——必须 `-Recurse` 到 `circle/` 子目录。

#### A. 栈 / 参数类

| # | 类 | 触发条件 | 后果 | 特殊 resolutionType | 文件 |
|---|---|---|---|---|---|
| 1 | `MishapNotEnoughArgs(expected, got)` | `argc > stack.size`（如 `ConstMediaAction.kt:34-35`） | 补 `expected-got` 个 `GarbageIota` | — | `MishapNotEnoughArgs.kt` |
| 2 | `MishapInvalidOperatorArgs(perpetrators)` | 算术运算符参数类型不符 | 把栈顶 n 个替换为 `GarbageIota` | — | `MishapInvalidOperatorArgs.kt` |
| 3 | `MishapStackSize` | 栈过大无法序列化（`CastingVM.kt:55-61`） | `stack.clear()` + 一个 `GarbageIota` | `ERRORED` | `MishapStackSize.kt` |
| 4 | `MishapNeedsParens` | `close_paren`/`undo`/`read_into_parens` 在括号外（§2.2） | 把 `errorCtx.pattern` 推回栈 | — | `MishapNeedsParens.kt` |

#### B. 类型类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 5 | `MishapInvalidIota(perpetrator, reverseIdx, expected)` | 期望某类型但拿到别的（`ActionUtils` 各 `getXxx`） | 把 `stack[size-1-reverseIdx]` 换成 `GarbageIota` | `MishapInvalidIota.kt` |
| 6 | `MishapInvalidSpellDatumType(perpetrator)` | iota 承载了不认识的数据类型 | **NO-OP** | `MishapInvalidSpellDatumType.kt` |
| 7 | `MishapUnescapedValue(perpetrator)` | 括号外执行了非可执行 iota（`Iota.java:56-69` 默认实现） | **NO-OP**（TODO 未实现） | `MishapUnescapedValue.kt` |
| 8 | `MishapBoolDirectrixEmptyStack(pos)` | 布尔导向石求值时栈为空 | **销毁该导向石方块**（`destroyBlock(pos, true)`） | `circle/MishapBoolDirectrixEmptyStack.kt:18-20` |
| 9 | `MishapBoolDirectrixNotBool(perpetrator, pos)` | 布尔导向石拿到的不是布尔 | **销毁该导向石方块** | `circle/MishapBoolDirectrixNotBool.kt:19-21` |

#### C. 媒质类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 10 | `MishapNotEnoughMedia(cost)` | `env.extractMedia(cost, simulate=true) > 0`（`ConstMediaAction.kt:41-42`） | **真的扣掉这笔媒质**（`extractMedia(cost, false)`） | `MishapNotEnoughMedia.kt` |

> `MishapNotEnoughMedia` 的 `resolutionType` 显式覆写为 `ERRORED`。

#### D. 图案类

| # | 类 | 触发条件 | 后果 | resolutionType | 文件 |
|---|---|---|---|---|---|
| 11 | `MishapInvalidPattern(pattern)` | 括号外匹配到 `Nothing`（`PatternIota.java:133`） | 推 `GarbageIota` | **`INVALID`** | `MishapInvalidPattern.kt` |
| 12 | `MishapDisallowedSpell(type, actionKey)` | 配置禁用该图案（`CastingEnvironment.java:196-198`） | **NO-OP** | **`INVALID`** | `MishapDisallowedSpell.kt` |
| 13 | `MishapNoSpellCircle` | 需要法术环但不在环内 | 把**施法者背包、副手、护甲全部掉落**（带绑定诅咒的护甲除外） | — | `circle/MishapNoSpellCircle.kt:28-38` |

#### E. 位置 / 世界类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 14 | `MishapBadLocation(location, type)` | `assertVecInRange` 等失败（`CastingEnvironment.java:344-349,385-389`） | 朝该位置抛出手中物品 | `MishapBadLocation.kt` |
| 15 | `MishapLocationInWrongDimension(properDimension)` | 位置属于另一个维度 | 推 `GarbageIota` | `MishapLocationInWrongDimension.kt` |

#### F. 实体类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 16 | `MishapBadEntity(entity, wanted)` | 实体类型不符 | 朝该实体抛出手中物品 | `MishapBadEntity.kt` |
| 17 | `MishapEntityTooFarAway(entity)` | `assertEntityInRange` 失败（`CastingEnvironment.java:370-380`） | 朝该实体抛出手中物品 | `MishapEntityTooFarAway.kt` |
| 18 | `MishapImmuneEntity(entity)` | 实体免疫该效果 | 朝该实体抛出手中物品 | `MishapImmuneEntity.kt` |

#### G. 物品类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 19 | `MishapBadItem(itemEntity, wanted)` | 掉落物类型不符 | 给该掉落物一个向上的速度 | `MishapBadItem.kt` |
| 20 | `MishapBadOffhandItem(itemStack, wanted)` | 副手物品不符（如 `OpReadIntoParens.kt:30,33,37`） | `dropHeldItems()` | `MishapBadOffhandItem.kt` |
| 21 | `MishapLackingHotbarItem(wanted)` | 快捷栏缺少所需物品 | `dropHeldItems()` | `MishapLackingHotbarItem.kt` |

#### H. 方块类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 22 | `MishapBadBlock(pos, expected)` | 方块类型不符 | 在该位置生成强度 0.25 的**无破坏爆炸** | `MishapBadBlock.kt` |

#### I. 施法者状态类

| # | 类 | 触发条件 | 后果 | resolutionType | 文件 |
|---|---|---|---|---|---|
| 23 | `MishapBadCaster` | 施法者无效 | **NO-OP** | — | `MishapBadCaster.kt` |
| 24 | `MishapUnenlightened` | 图案需要启蒙但玩家未达成（`PatternIota.java:114-117`） | `dropHeldItems()` + 系统消息 + 玻璃破碎音 + 触发失败成就 | **`INVALID`** | `MishapUnenlightened.kt` |
| 25 | `MishapOthersName(confidant)` | iota 中含他人真名 | **失明**：本人 5 秒 / 他人 60 秒 | — | `MishapOthersName.kt` |
| 26 | `MishapNoAkashicRecord(pos)` | 该位置无阿卡夏记录 | `removeXp(100)` | — | `MishapNoAkashicRecord.kt` |

#### J. 灌注 / 仪式类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 27 | `MishapBadBrainsweep(mob, pos)` | 灌注条件不满足 | 对 mob 造成 1 点 OVERCAST 伤害 | `MishapBadBrainsweep.kt` |
| 28 | `MishapAlreadyBrainswept(mob)` | 该生物已被灌注过 | 对 mob 造成等于其**全部生命值**的伤害（秒杀） | `MishapAlreadyBrainswept.kt` |

#### K. 运算类

| # | 类 | 触发条件 | 后果 | 文件 |
|---|---|---|---|---|
| 29 | `MishapDivideByZero(operand1, operand2, suffix)` | 除零 / 指数运算非法 | 推 `GarbageIota` + `damage(0.5f)`（**掉一半血**） | `MishapDivideByZero.kt` |

#### L. 内部类

| # | 类 | 触发条件 | 后果 | resolutionType | 文件 |
|---|---|---|---|---|---|
| 30 | `MishapInternalException(exception)` | 任何未捕获异常（`CastingVM.kt:142-161`、`CastingVM.kt:84-87`） | **NO-OP**，但把堆栈打到玩家聊天框 | — | `MishapInternalException.kt` |
| 31 | `MishapEvalTooMuch` | `opsConsumed > maxOpCount()`（`CastingVM.kt:62-68`） | `drown()`（放干氧气） | — | `MishapEvalTooMuch.kt` |

### 4.2 `resolutionType` 汇总

默认 `ERRORED`（`Mishap.kt:34`）。显式覆写为其他值的有四种：

| resolutionType | Mishap | `success` |
|---|---|---|
| `INVALID` | `MishapInvalidPattern`、`MishapDisallowedSpell`、`MishapUnenlightened` | `false` |
| `ERRORED` | `MishapNotEnoughMedia`、`MishapStackSize` | `false` |
| （其余） | 默认 `ERRORED` | `false` |

`ResolvedPatternType` 全枚举（`eval/ResolvedPatternType.kt:5-11`）：

| 值 | color | fadeColor | success |
|---|---|---|---|
| `UNRESOLVED` | `0x7f7f7f` | `0xcccccc` | false |
| `EVALUATED` | `0x7385de` | `0xfecbe6` | true |
| `ESCAPED` | `0xddcc73` | `0xfffae5` | true |
| `UNDONE` | `0xb26b6b` | `0xcca88e` | true |
| `ERRORED` | `0xde6262` | `0xffc7a0` | false |
| `INVALID` | `0xb26b6b` | `0xcca88e` | false |

**主循环用 `success` 决定是否 earlyExit**（`CastingVM.kt:88`）——所以 `ESCAPED`/`UNDONE` 不会中断施法，`ERRORED`/`INVALID` 会。

---

## 5. 媒质消耗

### 5.1 声明与扣除的完整链路

`ConstMediaAction`（`castables/ConstMediaAction.kt:17-51`）是声明式媒质消耗的接口：

```kotlin
interface ConstMediaAction : Action {
    val argc: Int
    val mediaCost: Long get() = 0
    fun execute(args: List<Iota>, env: CastingEnvironment): List<Iota>
    fun executeWithOpCount(args, env): CostMediaActionResult = CostMediaActionResult(this.execute(args, env))
}

override fun operate(env, image, continuation): OperationResult {
    val stack = image.stack.toMutableList()
    if (this.argc > stack.size) throw MishapNotEnoughArgs(this.argc, stack.size)
    val args = stack.takeLast(this.argc)
    repeat(this.argc) { stack.removeLast() }              // ① 先弹参数
    val result = this.executeWithOpCount(args, env)       // ② 执行业务逻辑
    stack.addAll(result.resultStack)                      // ③ 压回结果
    if (env.extractMedia(this.mediaCost, true) > 0)       // ④ **试算**媒质
        throw MishapNotEnoughMedia(this.mediaCost)
    val sideEffects = mutableListOf(OperatorSideEffect.ConsumeMedia(this.mediaCost))  // ⑤ 登记副作用
    val image2 = image.copy(stack = stack, opsConsumed = image.opsConsumed + result.opCount)
    return OperationResult(image2, sideEffects, continuation, HexEvalSounds.NORMAL_EXECUTE)
}
```

**四步走**：弹参 → 执行 → **试算**（`simulate=true`，不真扣） → 登记 `ConsumeMedia` 副作用。

**真正的扣除发生在副作用阶段**（`sideeffects/OperatorSideEffect.kt:43-47`）：

```kotlin
data class ConsumeMedia(val amount: Long) : OperatorSideEffect() {
    override fun performEffect(harness: CastingVM) {
        harness.env.extractMedia(this.amount, false)      // ← simulate = false
    }
}
```

### 5.2 `extractMedia` 的两阶段与成本修正

`CastingEnvironment.extractMedia`（`CastingEnvironment.java:271-279`）：

```java
public long extractMedia(long cost, boolean simulate) {
    cost = (long) (cost * costModifier);                                  // ① 成本修正系数
    for (var c : preMediaExtract)  cost = c.onExtractMedia(cost, simulate); // ② 前置扩展
    cost = extractMediaEnvironment(cost, simulate);                        // ③ 具体环境（抽象）
    for (var c : postMediaExtract) cost = c.onExtractMedia(cost, simulate); // ④ 后置扩展
    return cost;   // 返回值 > 0 表示还差多少
}
```

`costModifier` 在 `precheckAction` 里被设置（`CastingEnvironment.java:191-213`）：

```java
public void precheckAction(PatternShapeMatch match) throws Mishap {
    ResourceLocation loc = actionKey(match);
    if (!HexConfig.server().isActionAllowed(loc)) throw new MishapDisallowedSpell("disallowed", loc);
    costModifier = (loc != null) ? this.getCostModifier(loc) : 1.0;
}

protected double getCostModifier(ResourceLocation loc) {
    if (isOfTag(registry, loc, HexTags.Actions.CANNOT_MODIFY_COST))
        return HexConfig.server().getActionCostScaling(loc);      // 不吃全局缩放
    return HexConfig.server().getActionCostScaling(loc) * HexConfig.server().globalCostScaling();
}
```

玩家环境再叠一层属性修正（`env/PlayerBasedCastEnv.java:75-81`）：

```java
protected double getCostModifier(ResourceLocation loc) {
    var base = super.getCostModifier(loc);
    if (isOfTag(registry, loc, HexTags.Actions.CANNOT_MODIFY_COST)) return base;
    return base * this.caster.getAttributeValue(HexAttributes.MEDIA_CONSUMPTION_MODIFIER);
}
```

### 5.3 超载（overcast）：用血量抵媒质

`PlayerBasedCastEnv.extractMediaFromInventory`（`PlayerBasedCastEnv.java:150-196`）是玩家施法的核心：

1. `MediaHelper.scanPlayerForMediaStuff(caster)` 扫描背包里所有媒质容器
2. 逐个 `MediaHelper.extractMedia(source, costLeft, false, simulate)`，累减 `costLeft`
3. 若 `costLeft > 0 && allowOvercast`：把缺口按 `mediaToHealthRate` 换成血量
   - `healthToRemove = max(costLeft / mediaToHealthRate, 0.5)`
   - 模拟时：`simulatedRemovedMedia = ceil(min(health, healthToRemove) * mediaToHealthRate)`，免疫则记 0
   - 实际时：`trulyHurt(caster, OVERCAST, healthToRemove)`，再按实际掉血折算已支付媒质
4. `costLeft <= 0` 表示够用

`canOvercast()` 依赖成就 `y_u_no_cast_angy`（`PlayerBasedCastEnv.java:198-202`）——**没达成这个成就就不能超载**。

创造模式直接返回 0（`StaffCastEnv.java:62-68`）。

### 5.4 `OpThanos`：查询剩余算力

`OpThanos`（`actions/eval/OpThanos.kt:12-21`）把 `maxOpCount() - opsConsumed` 作为 `DoubleIota` 压栈——玩家可用它自查还剩多少操作预算。

---

## 6. 服务端 / 客户端分工

### 6.1 总原则

`Action` 接口的文档注释说得很直白（`castables/Action.kt:22-28`）：

> Instances of this can exist on the client, but they should NEVER be used there. They only exist on the client because Minecraft's registry system demands they do; any information the client needs about them is stored elsewhere.

即：**所有求值都在服务端**。客户端只负责绘制、图案匹配显示、以及把画好的图案发上去。

### 6.2 消息清单

| 消息 | 方向 | 载荷 | 文件 |
|---|---|---|---|
| `MsgOpenSpellGuiS2C` | S→C | `hand`, `patterns[]`, `stack[]`, `ravenmind`, `parenCount` | `common/msgs/MsgOpenSpellGuiS2C.java:19-23` |
| `MsgNewSpellPatternC2S` | C→S | `handUsed`, `pattern`, `resolvedPatterns[]` | `common/msgs/MsgNewSpellPatternC2S.java:22-24` |
| `MsgNewSpellPatternS2C` | S→C | `ExecutionClientView info`, `index` | `common/msgs/MsgNewSpellPatternS2C.java:19` |
| `MsgClearSpiralPatternsS2C` | S→C | `playerUUID` | `common/msgs/MsgClearSpiralPatternsS2C.java` |
| `MsgNewSpiralPatternsS2C` | S→C | `playerUUID`, `patterns[]`, `duration` | `common/msgs/MsgNewSpiralPatternsS2C.java` |
| `MsgShiftScrollC2S` | C→S | 滚轮翻页（法术书） | `common/msgs/MsgShiftScrollC2S.java` |

### 6.3 打开画布

`ItemStaff.use` → 服务端构造 VM 与描述，发 `MsgOpenSpellGuiS2C`（`common/items/ItemStaff.java:43-51`）：

```java
var vm = IXplatAbstractions.INSTANCE.getStaffcastVM(serverPlayer, hand);
var patterns = IXplatAbstractions.INSTANCE.getPatternsSavedInUi(serverPlayer);
var descs = vm.generateDescs();
sendPacketToPlayer(serverPlayer, new MsgOpenSpellGuiS2C(hand, patterns, descs.getFirst(), descs.getSecond(), 0));
```

客户端收到后直接 `mc.setScreen(new GuiSpellcasting(...))`（`MsgOpenSpellGuiS2C.java:58-68`）。

**潜行右键**：`clearCastingData` + 广播 `MsgClearSpiralPatternsS2C`（`ItemStaff.java:32-41`）。
**`FEEBLE_MIND` 属性 > 0** 时直接 `InteractionResultHolder.fail`，连 GUI 都不开（`ItemStaff.java:29-31`）。

### 6.4 一笔图案的完整往返

这是整个链路最关键的一环（`env/StaffCastEnv.java:80-139`）：

```java
public static void handleNewPatternOnServer(ServerPlayer sender, MsgNewSpellPatternC2S msg) {
    // ① 反作弊：新图案的格点不能与之前任何图案的格点重叠
    boolean cheatedPatternOverlap = false;
    ... // 收集前 n-1 条图案的所有 positions，与当前图案 positions 求交
    if (cheatedPatternOverlap) return;

    sender.awardStat(HexStatistics.PATTERNS_DRAWN);

    // ② 从存盘状态重建 VM
    var vm = IXplatAbstractions.INSTANCE.getStaffcastVM(sender, msg.handUsed());

    // ③ 只执行这一个图案
    ExecutionClientView clientInfo = vm.queueExecuteAndWrapIota(new PatternIota(msg.pattern()), sender.serverLevel());

    // ④ 决定保存还是清空
    if (clientInfo.isStackClear()) {
        setStaffcastImage(sender, null);
        setPatterns(sender, List.of());
    } else {
        setStaffcastImage(sender, vm.getImage().withOverriddenUsedOps(0));   // ← 算力预算重置为 0
        resolvedPatterns.get(resolvedPatterns.size()-1).setType(clientInfo.getResolutionType());
        setPatterns(sender, resolvedPatterns);
    }

    // ⑤ 回包
    sendPacketToPlayer(sender, new MsgNewSpellPatternS2C(clientInfo, resolvedPatterns.size() - 1));
    // ⑥ 螺旋图案展示包
    IMessage packet = clientInfo.isStackClear()
        ? new MsgClearSpiralPatternsS2C(sender.getUUID())
        : new MsgNewSpiralPatternsS2C(sender.getUUID(), List.of(msg.pattern()), Integer.MAX_VALUE);
    sendPacketToPlayer(sender, packet);
    sendPacketTracking(sender, packet);
    // ⑦ 成功则喷粒子
    if (clientInfo.getResolutionType().getSuccess()) new ParticleSpray(...).sprayParticles(...);
}
```

**四个必须知道的细节**：

1. **反作弊在服务端**：客户端发的 `resolvedPatterns` 会被校验格点重叠，重叠直接丢弃消息（`StaffCastEnv.java:82-100`）。
2. **每个图案一次新 VM**：`getStaffcastVM` 从存盘的 `CastingImage` 重建（见 6.5），所以**每画一笔就是一轮独立的 `queueExecuteAndWrapIotas`**。
3. **算力预算每个图案重置**：`withOverriddenUsedOps(0)`（`StaffCastEnv.java:114`）。注意 `opsConsumed` 在一次 `queueExecuteAndWrapperIotas` 内部仍然累计（用于 `EvalTooMuch`），只是跨图案被清掉。
4. **`isStackClear` 决定是否关闭 GUI**：主循环算出 `stack.isEmpty() && parenCount == 0 && !escapeNext && ravenmind == null`（`CastingVM.kt:98`），为真则清空状态，客户端据此关闭界面。

### 6.5 状态存放位置

`IXplatAbstractions` 的五个方法（`xplat/IXplatAbstractions.java:99-115`）：

```java
void setStaffcastImage(ServerPlayer target, @Nullable CastingImage image);
void setPatterns(ServerPlayer target, List<ResolvedPattern> patterns);
CastingVM getStaffcastVM(ServerPlayer player, InteractionHand hand);
List<ResolvedPattern> getPatternsSavedInUi(ServerPlayer player);
void clearCastingData(ServerPlayer player);
```

Fabric 实现挂在玩家的 Cardinal Component 上（`Fabric/.../cc/CCStaffcastImage.java`）：

```java
public CastingVM getVM(InteractionHand hand) {
    var img = this.lazyLoadedTag.isEmpty() ? new CastingImage()
                                          : CastingImage.loadFromNbt(this.lazyLoadedTag, this.owner.serverLevel());
    return new CastingVM(img, new StaffCastEnv(this.owner, hand));
}
public void setImage(@Nullable CastingImage image) {
    this.lazyLoadedTag = image == null ? new CompoundTag() : image.serializeToNbt();
}
```

**要点**：状态以 **NBT（`CompoundTag`）形式懒存**，每次使用才反序列化成 `CastingImage`。这保证了跨登录/跨维度也能恢复。

### 6.6 客户端拿到什么

`ExecutionClientView`（`eval/ExecutionClientView.kt:8-16`）——注释解释了为什么传 NBT 而不是文本：

```kotlin
data class ExecutionClientView(
    val isStackClear: Boolean,
    val resolutionType: ResolvedPatternType,
    // These must be tags so the wrapping of the text can happen on the client
    // otherwise we don't know when to stop rendering
    val stackDescs: List<CompoundTag>,
    val ravenmind: CompoundTag?,
)
```

客户端 `GuiSpellcasting.recvServerUpdate`（`client/gui/GuiSpellcasting.kt:67-86`）处理：
- `isStackClear` → `setScreen(null)` 关闭界面
- `resolutionType == UNDONE` → 找最后一条可撤销的图案标记为 `UNDONE`，把刚画的那条标为 `EVALUATED`
- 否则更新 `patterns[index].type`
- 刷新 `cachedStack` / `cachedRavenmind`

`stackDescs` 由 `generateDescs()` 生成（`CastingVM.kt:173-179`）：把 `image.stack` 逐个 `IotaType.serialize`，并取出 `userData` 里的 ravenmind。

---

## 7. 移植到 C# 的建议接口划分

### 7.1 命名空间映射

建议在现有 `HexCastingTerraria` 下按此划分（与现有 `Core/Casting`、`Core/Registry` 结构衔接）：

| 源（Java/Kotlin） | 建议 C# 位置 | 说明 |
|---|---|---|
| `api.casting.eval.vm.CastingVM` | `Core.Casting.Eval.Vm.CastingVm` | 主循环 + `ExecuteInner` |
| `api.casting.eval.vm.CastingImage` | `Core.Casting.Eval.Vm.CastingImage` | 用 C# `record` 表达不可变 |
| `api.casting.eval.vm.ContinuationFrame` | `Core.Casting.Eval.Vm.IContinuationFrame` | 接口 + 三个实现 |
| `api.casting.eval.vm.SpellContinuation` | `Core.Casting.Eval.Vm.SpellContinuation` | 建议用抽象类 + `Done` 单例 |
| `api.casting.eval.vm.FrameEvaluate/FrameForEach/FrameFinishEval` | 同命名空间 | 三个 `sealed class` |
| `api.casting.SpellList` | `Core.Casting.SpellList` | **必须用不可变链表**（见 §7.3 风险 1） |
| `api.casting.iota.*` | `Core.Casting.Iota.*` | 已有 `Iota` 基类，需扩展 |
| `api.casting.mishaps.*` | `Core.Casting.Mishaps.*` | 31 个类 + `MishapEnvironment` |
| `api.casting.eval.CastingEnvironment` | `Core.Casting.Eval.CastingEnvironment` | 抽象基类 |
| `api.casting.eval.env.*` | `Core.Casting.Eval.Env.*` | `StaffCastEnv`、`PlayerBasedCastEnv` 等 |
| `api.casting.castables.*` | `Core.Casting.Castables.*` | `IAction`、`IConstMediaAction` |
| `api.casting.eval.sideeffects.OperatorSideEffect` | `Core.Casting.Eval.SideEffects.OperatorSideEffect` | sealed 层次 |
| `api.casting.ActionRegistryEntry` | 已在 `Core.Registry` | 与 `PatternDef` 合并 |
| `api.casting.PatternShapeMatch` | `Core.Registry.PatternMatch` | 已有 `PatternMatchKind` 枚举 |
| `common.msgs.*` | `Content.Net.*` | 用 tModLoader `ModPacket` |

### 7.2 可以合并 / 可以省略的部分

**建议合并**：

| 源结构 | 合并方式 | 理由 |
|---|---|---|
| `IotaType` 注册表 + `IXplatAbstractions` 间接层 | 直接一个 `IotaRegistry` 静态类 | 原项目的间接层是为了同时支持 Forge/Fabric，C# 侧只有一个加载器 |
| `preMediaExtract` / `postMediaExtract` / `isVecInRanges` / `hasEditPermissionsAts` 四组扩展回调 | 合并成一个 `ICastEnvExtension` 接口，按需分发 | 原项目把它们拆成四个 `List<>` 纯属性能优化，模组规模下无必要 |
| `ConstMediaAction` / `OperationAction` / `SpellAction` | 保留 `IConstMediaAction`（声明式）与 `IAction`（底层）两个即可 | `OperationAction` 只是 `ConstMediaAction` 的薄包装（`castables/OperationAction.kt:22 行`） |
| `MishapEnvironment` + `PlayerBasedMishapEnv` | 可合并为一个 `MishapEffects` 类，用委托注入玩家/世界 | 原项目的抽象是为了支持法术环等非玩家施法者；若初期只做法杖可暂合并 |

**建议省略（初期）**：

- `PerWorld` 图案与 `ScrungledPatternsSave`（世界专属随机图案）——`PatternRegistryManifest.java:102-107`
- `SpecialHandler` 的动态匹配（数字字面量除外，这个要保留，否则连数字都画不出来）
- `ContinuationIota` 的完整序列化（先内存实现，**但必须留好接口**，见 §7.3 风险 2）
- `Pigment`/`FrozenPigment` 染色系统（粒子颜色可先用固定色）

### 7.3 移植到 C# 时最危险的 3 个点

#### 风险 1：`SpellList` 的不可变链表语义 —— 用 `List` 会静默错错

**为什么危险**：
`SpellList` 是**持久化不可变链表**，`cdr` 每次返回一个新对象而不是修改自身（`SpellList.kt:20-27`）：

```kotlin
class LList(val idx: Int, val list: List<Iota>) : SpellList() {
    override val cdr: SpellList get() = LList(idx + 1, list)
}
```

`FrameEvaluate.evaluate`（`FrameEvaluate.kt:33-44`）依赖这个语义：它把 `list.cdr` 压入新帧，**同时**用 `list.car` 执行——如果 C# 侧用可变 `List<T>` + 索引，两处共享状态会在 `OpEvalBreakable` 把 continuation 存进 `ContinuationIota` 后被后续执行破坏。更隐蔽的是 `FrameForEach` 会把 `code`（一个 `SpellList`）反复重新执行（`FrameForEach.kt:75`），可变实现会导致第二轮迭代看到被消费过的列表。

**具体后果**：法术在简单情况下正常，一旦用到 `for_each` 或 `eval_breakable` 就出现"某些迭代丢失/重复"，且**极难定位**。

**建议**：C# 侧实现 `SpellList` 为 `abstract class` + `LPair(car, cdr)` / `LList(list, index)`，`Cdr` 返回新实例。不要用 `IEnumerator`（`FrameForEach` 需要多次独立遍历同一 `SpellList`）。

#### 风险 2：`opsConsumed` 的生命周期 —— 跨图案被重置、图案内累计

**为什么危险**：
- **图案内**：`CastingVM` 每轮检查 `result.newData.opsConsumed > env.maxOpCount()`（`CastingVM.kt:62`），超了抛 `MishapEvalTooMuch`。
- **跨图案**：`StaffCastEnv` 每笔结束都把 `opsConsumed` **清零**（`StaffCastEnv.java:114` 的 `withOverriddenUsedOps(0)`）。

如果 C# 侧把 `opsConsumed` 当成"全局累计"（比如放在 `ModPlayer` 上），那么**一个玩家画几笔之后就会永久触发 `EvalTooMuch`**，且表现为"前几笔正常、之后所有法术都失败"。反过来，如果每笔都新建 `CastingImage`（而不是从存盘恢复），则 `for_each` 的迭代计数会在迭代中途被清零，导致"迭代无限进行到算力耗尽"。

**建议**：`opsConsumed` 严格放在 `CastingImage` 里；跨图案重置**只能在 `StaffCastEnv` 那一层**做，且必须与 `setStaffcastImage` 同时发生。

#### 风险 3：`executeInner` 的三分支顺序 + mishap 时 `newData = null` 的语义

**为什么危险**：
两件事耦合在一起：

1. `executeInner` 的分支顺序是 **escapeNext → inParens → execute**（`CastingVM.kt:115-141`）。若把 `escapeNext` 检查放到括号分支之后，`escape` + `open_paren` 的组合行为就会错。
2. **mishap 时 `CastResult.newData` 为 `null`，主循环保留旧 image**（`CastingVM.kt:75-77`）。但 `PatternIota.lookupAndOperate` 在**元编程中** mishap 时会返回 `withResetEscape()` 的 image 而不是 null（`PatternIota.java:174-180`）。

C# 若用"就地修改 `CastingImage`"而不是不可变 `copy`，这两条都无法表达：
- 无法表达"这次不更新状态"（null 语义）
- 无法表达"这次更新为清空括号的版本"

**具体后果**：括号状态在 mishap 后错乱（玩家卡在"列表构建模式"或意外退出），且这类 bug 只在特定图案组合下出现。

**建议**：`CastingImage` 必须是 C# `sealed record`（或至少所有字段 `init`-only），`CastResult.NewData` 必须是 `CastingImage?`。**禁止**任何 `void` 修改 `CastingImage` 的方法。

### 7.4 建议的实现顺序

1. `CastingImage`（不可变 record）+ `Iota` 体系 + `SpellList`
2. `ContinuationFrame` 三实现 + `SpellContinuation` + `CastingVm` 主循环（先不接 Action，用假 Action 跑通帧栈）
3. `IAction` / `IConstMediaAction` + `Mishap` 基类 + `MishapEnvironment`
4. `CastingEnvironment` + `StaffCastEnv` + `PatternRegistry`（已有 188 条数据）
5. 序列化（`CastingImage` ↔ `TagCompound`）+ `ModPlayer` 挂钩
6. 先接 `stack/`、`eval/`、`escaping/`、`local/` 四类图案（这几类不碰世界，纯栈操作，最适合验证 VM 正确性）
7. 再接 `math/`、`lists/`、`types/`，最后才是碰世界的 `spells/`、`rw/`、`raycast/`

---

## 附录 A：本文引用的文件清单（均已通读）

| 文件 | 行数 |
|---|---|
| `api/casting/eval/README.md` | 90 |
| `api/casting/eval/vm/CastingVM.kt` | 191 |
| `api/casting/eval/vm/CastingImage.kt` | 157 |
| `api/casting/eval/vm/SpellContinuation.kt` | 46 |
| `api/casting/eval/vm/ContinuationFrame.kt` | 108 |
| `api/casting/eval/vm/FrameEvaluate.kt` | 74 |
| `api/casting/eval/vm/FrameForEach.kt` | 126 |
| `api/casting/eval/vm/FrameFinishEval.kt` | 46 |
| `api/casting/eval/CastingEnvironment.java` | 639 |
| `api/casting/eval/MishapEnvironment.java` | 50 |
| `api/casting/eval/env/PlayerBasedMishapEnv.java` | 58 |
| `api/casting/eval/env/PlayerBasedCastEnv.java` | 241 |
| `api/casting/eval/env/PlayerBasedSpiralPatternCastEnv.java` | 41 |
| `api/casting/eval/env/StaffCastEnv.java` | 140 |
| `api/casting/eval/CastResult.kt` / `OperationResult.kt` / `ExecutionClientView.kt` / `ResolvedPattern.kt` / `ResolvedPatternType.kt` | 共 110 |
| `api/casting/eval/sideeffects/OperatorSideEffect.kt` | 72 |
| `api/casting/SpellList.kt` | 93 |
| `api/casting/ActionUtils.kt`（`evaluatable` 段） | 323 |
| `api/casting/ActionRegistryEntry.java` / `PatternShapeMatch.java` | 共 72 |
| `api/casting/castables/Action.kt` / `ConstMediaAction.kt` / `SpecialHandler.java` | 共 166 |
| `api/casting/iota/Iota.java` | 163 |
| `api/casting/iota/IotaType.java` | 192 |
| `api/casting/iota/PatternIota.java` | 234 |
| `api/casting/iota/ContinuationIota.java` | 89 |
| `api/casting/iota/ListIota.java` | 172 |
| `api/casting/iota/{Boolean,Double,Vec3,Entity,Null,Garbage}Iota.java` | 共 381 |
| `api/casting/mishaps/*.kt`（32 个文件，含 `circle/` 子目录 3 个） | 902 |
| `common/casting/actions/eval/{OpEval,OpEvalBreakable,OpHalt,OpForEach,OpThanos}.kt` | 共 123 |
| `common/casting/actions/escaping/{OpOpenParen,OpCloseParen,OpEscape,OpUndo,OpReadIntoParens}.kt` | 共 172 |
| `common/casting/PatternRegistryManifest.java` | 140 |
| `common/msgs/{MsgOpenSpellGuiS2C,MsgNewSpellPatternC2S,MsgNewSpellPatternS2C}.java` | 共 175 |
| `common/items/ItemStaff.java` | 59 |
| `xplat/IXplatAbstractions.java`（VM 段） | 签名级 |
| `Fabric/.../cc/CCStaffcastImage.java` | 全文 |

## 附录 B：核心常量速查

| 常量 | 值 | 来源 |
|---|---|---|
| `MAX_SERIALIZATION_DEPTH` | 256 | `HexIotaTypes.java` |
| `MAX_SERIALIZATION_TOTAL` | 1024 | `HexIotaTypes.java` |
| `DoubleIota.TOLERANCE` | 0.0001 | `DoubleIota.java:14` |
| `Action.RAYCAST_DISTANCE` | 32.0 | `Action.kt:77` |
| `PlayerBasedCastEnv.DEFAULT_AMBIT_RADIUS` | 32.0 | `PlayerBasedCastEnv.java:48` |
| `PlayerBasedCastEnv.DEFAULT_SENTINEL_RADIUS` | 16.0 | `PlayerBasedCastEnv.java:50` |
| `maxOpCount` | 服务端配置 | `HexConfig.server().maxOpCount()` |
| `mediaToHealthRate` | 通用配置 | `HexConfig.common().mediaToHealthRate()` |
| 超载成就 | `hexcasting:y_u_no_cast_angy` | `PlayerBasedCastEnv.java:199` |
| 启蒙成就 | `hexcasting:enlightenment` | `CastingEnvironment.java:252` |
| 成本修正标签 | `HexTags.Actions.CANNOT_MODIFY_COST` | `CastingEnvironment.java:208` |
| 需启蒙标签 | `HexTags.Actions.REQUIRES_ENLIGHTENMENT` | `PatternIota.java:109` |
| 每世界图案标签 | `HexTags.Actions.PER_WORLD_PATTERN` | `PatternRegistryManifest.java:50` |
| 法杖物品标签 | `HexTags.Items.STAVES` | `GuiSpellcasting.kt:145` |
