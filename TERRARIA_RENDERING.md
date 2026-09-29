
# 泰拉渲染体系研究

> 用途：法术环的充能发光、阿卡夏记录的内容显示、画布与粒子——这些都需要走泰拉的渲染管线。
> 结论先行：**方块有两条额外绘制路径，帧率差 4 倍**，选错会又卡又抖。

---

## 一、方块的绘制流程

```
Main.DrawTiles()
  └─ 逐块（从上到下、从左到右）：
       PreDraw        ← 返回 false 可完全接管绘制
       默认精灵图绘制
       PostDraw       ← 紧跟**这一块**之后
       DrawEffects    ← 火焰之类的附加效果
  └─ 全部画完后：
       SpecialDraw    ← 只对「注册过」的坐标调用
```

### 关键陷阱：绘制顺序

> 方块是**从上到下、从左到右逐块**绘制的。
> `PostDraw` 里画的东西会被**后面绘制的方块盖住**。

所以「画在所有方块之上」的内容（比如记录方块上方的图案、法术环的光晕）
**必须**走 `SpecialDraw`，不能图省事写在 `PostDraw`。

---

## 二、两条额外绘制路径（帧率差 4 倍）

| 注册方式 | 帧率 | 采样 | 坐标修正 |
|---|---|---|---|
| `Main.instance.TilesRenderer.AddSpecialLegacyPoint(i, j)` | **15 fps** | LinearClamp | 需要 `Main.offScreenRange` |
| `AddSpecialPoint(i, j, TileCounterType.CustomSolid / CustomNonSolid)` | **60 fps** | PointClamp | 不需要 |

`TileCounterType` 的完整取值：
`CustomSolid` / `CustomNonSolid` / `MultiTileGrass` / `MultiTileVine` / `ReverseVine` / `Vine` / `WindyGrass`

**选择原则**：
- 静态装饰、跟随 15fps 网格的效果 → Legacy（省性能）
- **会动的东西**（法术环逐格点亮、旋转的符印）→ `CustomNonSolid`（60fps）

> 官方文档还警告：走 tile render target 的动画**要按 4 帧的倍数**，
> 否则会看出抖动。这也是 60fps 那条路径存在的理由。

---

## 三、帧与动画

| 概念 | 说明 |
|---|---|
| `TileFrameX/Y` | **帧坐标**，不是像素坐标。像素 = 帧值 × 18（默认） |
| `SetDrawPositions` | 逐块改绘制位置 / 尺寸 / 帧偏移 |
| `AnimateTile(ref frame, ref frameCounter)` | 动画帧，**同类型所有方块共享** |
| `AnimationFrameHeight` | 必须设，否则 `AnimateTile` 的时间不影响绘制 |
| `AnimateIndividualTile(type, i, j, ref frameX, ref frameY)` | **逐实例**动画（各块可以不同步） |
| `ModifyFrameMerge(...)` | 连通性合并（泥土/石头那种边缘融合） |

- 共享动画适合「所有这类方块一起闪」
- 逐实例适合「每块独立相位」——**法术环的逐格点亮属于这一类**

---

## 四、光照

| API | 说明 |
|---|---|
| `Main.tileLighted[Type] = true` | 必须开，否则 `ModifyLight` 不被调用 |
| `ModifyLight(i, j, ref r, ref g, ref b)` | 设该格发出的光（已在晶簇与记录方块上用） |

> 注意：`ModifyLight` 是**逐格**调用的，对远处未加载的格子不会调用 ——
> 不需要自己做距离剔除。

---

## 五、TileEntity 没有绘制钩子

查了 `ModTileEntity` 的全部成员：**没有任何 Draw 方法**。

所以要渲染方块实体的内容（记录方块的图案、法术环的状态），
**必须通过宿主方块的 `SpecialDraw` / `PreDraw`**，在回调里用坐标反查 TileEntity。

---

## 六、世界坐标 → 屏幕坐标

```
screenPos = worldPos - Main.screenPosition
```

- UI 层（`PostDrawInterface`）用这个
- 走 tile render target 的路径还要额外处理 `Main.offScreenRange`
- 走 `SpecialDraw`（60fps 路径）**不需要**处理 —— 官方文档明确说了

---

## 七、可用的绘制入口一览

| 入口 | 时机 | 坐标系 |
|---|---|---|
| `ModTile.PreDraw` / `PostDraw` | 逐块 | 世界 |
| `ModTile.SpecialDraw` | 全部方块之后 | 世界 |
| `ModSystem.PostDrawTiles()` | 方块之后 | 世界 |
| `ModSystem.PostDrawInterface(SpriteBatch)` | UI 之后 | 屏幕 |
| `ModPlayer` / `ModProjectile` 的 `Draw*` | 各自时机 | 屏幕 |

---

## 八、本项目的应用计划

| 需求 | 走哪条路 | 理由 |
|---|---|---|
| **阿卡夏记录显示存储的图案** | 宿主方块 `SpecialDraw` | 要画在方块之上，且是静态的 |
| **法术环逐格充能发光** | `AddSpecialPoint(CustomNonSolid)` + 逐实例帧 | 会动，15fps 会抖 |
| 晶簇的自发光 | `ModifyLight`（已实现） | 普通光照即可 |
| 画布 | `PostDrawInterface`（已实现） | UI 层 |
| 瞄准点粒子 | `Dust`（已实现） | 不需要自定义绘制 |

### 立即实施：阿卡夏记录显示图案

这是**用渲染知识换功能**的最小闭环 —— 记录方块现在只能右键看文字，
改成在方块上方画出存进去的图案，玩家一眼就能认出「这块写了什么」。

复用已有的 `PatternRenderer.DrawStaticPreview`，不新增渲染代码。
