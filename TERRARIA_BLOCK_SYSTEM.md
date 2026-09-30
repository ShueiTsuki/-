
# 泰拉方块体系对接设计

> 回答三个问题：**子形态怎么处理 / 工具与材料怎么接 / 多方块结构怎么做**
>
> 结论先行：**第三个问题的前提是错的 —— 法术环在原版根本不是多方块结构。**

---

## 一、法术环：原版就不是「多方块结构」

我读了 `ICircleComponent.java` 与 `BlockEntityAbstractImpetus.java`，原版的做法是：

> **每个环方块是一个独立的 1×1 方块，环是运行时走出来的图。**

每个环方块实现 `ICircleComponent`：

| 方法 | 作用 |
|---|---|
| `acceptControlFlow(imageIn, env, enterDir, pos, bs, world)` | 接收控制流，返回**能往哪些方向出去** |
| `canEnterFromDirection(enterDir, pos, bs, world)` | 能不能从那个方向进来 |
| `possibleExitDirections(pos, bs, world)` | 执行开始前调用，**用来判断这个环到底闭不闭合** |

控制流逐块前进，每块决定出口。源项目注释写得很清楚：

> *The circle environment will mishap if not exactly 1 of the returned directions can be accepted from.*
> （可用出口不是恰好 1 个时报 mishap）

`BlockEntityAbstractImpetus`（6 种：空 / 红石 / 注视 × 右击触发）持有
`CircleExecutionState`，负责走图与执行。`BlockSlate` 则用 `AttachFace`
（FLOOR / CEILING / WALL）决定朝向，并把**图案存在 TileEntity 里**。

### 所以泰拉侧怎么做

**不用 `TileObjectData`。** 泰拉的多方块结构是给**固定矩形**用的（箱子 2×2、门 1×3、床 3×2），
而法术环是任意形状 —— 硬套只会做不出来。

正确做法：

```
每个环部件 = 1×1 ModTile + ModTileEntity（存图案 / 部件类型）
Impetus    = 1×1 ModTile + ModTileEntity（存 CircleExecutionState + 媒质）
环的发现   = 从 Impetus 出发，用 Main.tile[x,y] 做 BFS / 逐块走图
```

**2D 反而更简单**：MC 是六个方向，泰拉只有四个（上下左右）；
`AttachFace` 的「地/顶/墙」在 2D 里天然对应「贴在下表面/上表面/侧面」。

---

## 二、泰拉的多方块结构（真正的用途）

固定矩形结构用 `TileObjectData`：

| 要素 | 说明 |
|---|---|
| `newTile.Width/Height` | 结构尺寸 |
| `newTile.Origin` | 原点在结构中的位置 |
| `AnchorBottom/Top/Left/Right` | 放置时的锚定条件 |
| `CoordinateHeights/Widths` | **每个子块的不同高度/宽度**（椅子、桌子这类不规则外形） |
| `HookPostPlaceMyPlayer` | 放置后建立 TileEntity |
| `ModTile.KillMultiTile` | 任意一块被破坏时掉正确的物品 |

泰拉用 `TileFrameX/Y` 编码「我属于结构的哪一块」，帧运算由 `TileObjectData` 托管。

> 本模组目前**只有阿卡夏记录用到了 TileEntity**（1×1），
> 还没有真正需要 `TileObjectData` 多方块的东西。
> 将来的「启迪木门 / 桌子 / 书架」会用到。

---

## 三、方块子形态：**大部分是白送的**

这是好消息。以下子形态**由 vanilla 自动处理**，只要方块是 `ModTile` 且不刻意对抗：

| 子形态 | 谁处理 | 我要做什么 |
|---|---|---|
| 斜坡 / 半砖（锤子） | vanilla | **什么都不用做** |
| 制动（Actuator，可穿透） | vanilla | **什么都不用做** |
| 油漆 31 色 | vanilla | **什么都不用做**（但贴图有要求，见下） |
| 回声涂层 / 夜明涂层 | vanilla | **什么都不用做** |
| 电线 / 逻辑门 | vanilla | 可选实现 `HitWire` |
| 液体淹没 / 蜂蜜 | vanilla | **什么都不用做** |

### 但有两个前提不能违反

**① `tileFrameImportant = true` 的方块不支持斜坡/半砖。**
这是泰拉自己的限制（帧化方块是「物件」不是「物块」）。所以：

| 方块类别 | `tileFrameImportant` | 锤子斜坡 | 理由 |
|---|---|---|---|
| 建材（启迪木、板岩） | `false` | 支持 | 玩家会拿来盖房子 |
| 机械（法术环部件、记录方块） | `true` | 不支持 | 本来就不该敲成斜坡 |

**② 油漆要求贴图是低饱和/灰阶的。**
泰拉原版建材贴图基本都是灰阶 —— 因为油漆是**乘算**染色，
彩色贴图染上去会变成一坨脏色。

> 注意：**这是新增的美术要求，已并入 [美术欠账] 一节。**
> 当前的启迪木/板岩若按紫色生成，油漆功能等于废掉。

---

## 四、工具与材料：哪些要我自己实现

| 工具 | 生效条件 | 我要实现的 | 备注 |
|---|---|---|---|
| **镐** | 所有 `HasTile` 方块 | `MinPick` / `MineResist` / `CanKillTile` / `CanExplode` | 必须做 |
| **锤** | 仅 `tileSolid` 且非 framed | 无 | 自动 |
| **斧** | 仅 `Main.tileAxe[type]` | 无（本模组没有树） | — |
| **制动器** | 所有方块 | 无 | 自动 |
| **油漆/涂层** | 所有方块 | 无 | 但受贴图要求约束（见上） |
| **电线** | 所有方块 | 可选 `HitWire` | 法术环接红石触发时会用到 |

### 已有的实现对照

| 方块 | MinPick | CanKillTile | 说明 |
|---|---|---|---|
| 晶洞母岩 | 1000 | **false** | 与原版一致：母岩不可获得 |
| 紫水晶簇 | 0 | 默认 | 芽阶段不给掉落（`noItem`） |
| 阿卡夏记录 | 0 | 默认 | — |

---

## 五、结论：法术环的实现路径

1. **环部件**：1×1 `ModTile`，`tileFrameImportant = true`，
   配 `ModTileEntity` 存「部件类型 + 图案」
2. **Impetus**：1×1 `ModTile` + `ModTileEntity`，
   存 `CircleExecutionState` + 媒质池（源项目 `MAX_CAPACITY = 9e18`）
3. **控制流**：4 个方向（上下左右），逐块走
   - 进入某块 → 问它「能从哪些方向出去」
   - 可用出口 **≠ 1** → mishap（照抄源项目语义）
   - 走回起点 → 环闭合，可以执行
4. **触发**：右击 / 红石 / 注视（原版三种）
5. **红石触发**复用泰拉的 `HitWire` —— **不用自己写红石系统**

这样既忠实原版机制，又完全落在泰拉已有的设施上。
