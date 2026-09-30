# 法术环实现进度

> 关联：`SPELL_CIRCLE_MECHANICS.md`（机制）、`CIRCLE_PRECHECK.md`（实施前核查）

## 已完成

| 步骤 | 内容 | 状态 |
|---|---|---|
| 1 | **石板方块 + TileEntity**（环的「指令」，存图案与朝向） | |
| 2 | **原动力方块 + TileEntity**（环的「CPU」：媒质池 + 起始方向 + 走环驱动） | |
| — | **Core 侧环遍历**（闭包校验 / 出口方向 / 加速曲线） | 纯逻辑，15 条离线用例 |
| — | **双向网络同步**（右击在客户端 → 上报服务端落地） | |

## 关键实现点（都来自源码核对）

| 点 | 实现 |
|---|---|
| 图案在**石板**上，不在原动力上 | 石板 TileEntity 存 `Pattern` |
| **原动力的进入规则与普通部件不同** | `CircleComponent.ForbiddenEntry` 显式区分；原动力用起始方向 |
| **不能原路返回** | `ExitDirections` 减去来路反方向 |
| **不能往 normal 方向出去**，但**允许**往其反方向穿过 | 只 `remove(normal)`，不加 `remove(normal.Opposite())` |
| **恰好 1 个出口** | 0 个 / 2+ 个都停下并报出坐标 |
| **长度上限** | `CircleTraversal.DefaultMaxLength = 1024`（原版默认值；曾误写 512） |
| **走环越快** | `TickSpeed(n) = max(2, 10-(n-1)/3)` |
| **每格重置算力** | 环每步新建执行状态；接 VM 求值时要在那里显式清零 |
| **媒质负数 = 无限** | `ExtractMedia` 里 `if (Media < 0) return 0` |
| 逐格驱动 | `ModTileEntity.PostGlobalUpdate`（对应 MC 的 `scheduleTick`） |

## 与源项目的差异（有意为之，已写进代码注释与 tooltip）

| 差异 | 原因 |
|---|---|
| 朝向由**空手右键旋转**决定，而非 `AttachFace` 自动判定 | 泰拉图格不记录「贴在哪个面」，没有等价物 |
| 只有 4 个控制流方向 | 2D 没有第三轴（源项目 6 向退化成 4 向） |

## 尚未实现

| 步骤 | 内容 | 依赖 |
|---|---|---|
| 3 | **3 个环图案**（`circle/impetus_pos` / `impetus_dir` / `bounds`） | 需要 `CircleCastingEnvironment` |
| 4 | **环里真正执行石板上的图案** | 需要 `CircleCastingEnvironment` + 把 VM 接进走环 |
| 5 | Directrix（3 种分流）+ 红石触发 | 步骤 3 之后 |
| 6 | 充能渲染（逐格点亮、图案显示） | 表现层 |

### 步骤 4 是当前的缺口

现在走环**只推进位置**，还没有真正执行石板上的图案。
要做到那一步需要 `CircleCastingEnvironment`：

```
媒质来源 = 原动力的媒质池（负数 = 无限）
范围判定 = 环的包围盒（不是玩家的 32 格半径）
CastingEntity = null（无人施法）
```

> 这三条都已在 `CircleCastEnv.java` 里核对过。
> `CastingEntity = null` 正好用上早先 `get_caster` 特意保留的
> 「无实体施法者 -> NullIota」分支。
