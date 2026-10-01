# 咒法学（Hex Casting）机制全解

> 本文是**通读源项目源码**后的机制总结，不是搜索片段拼凑。
> 源：`FallingColors/HexMod` v0.11.4（commit `6b64165be3`），MIT。
> 已通读的关键文件：
> - `client/gui/GuiSpellcasting.kt`（569 行，绘制界面）
> - `client/render/RenderLib.kt`（465 行，图案渲染与 zappy 算法）
> - `client/render/PatternSettings.java`（142 行，渲染参数）
> - `api/casting/math/HexPattern.kt` / `HexCoord.kt` / `HexDir.kt` / `HexAngle.kt`
> - `api/utils/HexUtils.kt`（像素↔格点换算）
> - `api/misc/MediaConstants.java`、`api/item/MediaHolderItem.java`
> - `common/items/ItemStaff.java`、`common/lib/hex/HexActions.java`
> - `common/casting/PatternRegistryManifest.java`

---

## 1. 六边形网格几何

### 方向与角度

```
HexDir（顺时针，序号参与模运算）
  NorthEast=0, East=1, SouthEast=2, SouthWest=3, West=4, NorthWest=5

HexAngle（转向角）
  FORWARD=0('w'), RIGHT=1('e'), RIGHT_BACK=2('d'), BACK=3('s'), LEFT_BACK=4('a'), LEFT=5('q')
```

运算规则：
- `dir.rotatedBy(angle)` = `(dir + angle) mod 6`
- `dir1.angleFrom(dir2)` = `(dir1 - dir2) mod 6`
- 方向位移：`NE(1,-1) E(1,0) SE(0,1) SW(-1,1) W(-1,0) NW(0,-1)`

### 轴坐标 → 像素（flat-top 六边形）

```kotlin
fun coordToPx(coord, size, offset) = Vec2(
    SQRT_3 * coord.q + SQRT_3/2 * coord.r,
    1.5 * coord.r
).scale(size).add(offset)
```

反变换 `pxToCoord` 用立方坐标取整（取整后比较 q、r 残差绝对值，大者修正）。

### 网格尺寸（画布）

```kotlin
hexSize = sqrt(width * height / 512.0) / GRID_ZOOM属性
coordsOffset = Vec2(width/2, height/2)      // 原点在屏幕正中
```

注解：512 是原作给的「约 512 格面积」预算。

---

## 2. 图案（HexPattern）

数据 = **起始方向 + 一串转向角**。

### 两条绘制禁令

1. **不能重走已画过的边**（含反向边）
2. **不能立刻上溯**（下一个角度不能是 `BACK`）

两者由 `tryAppendDir` 保证；失败返回 false 且不修改图案。

### 关键方法

| 方法 | 作用 |
|---|---|
| `tryAppendDir(newDir)` | 尝试延长一笔，含两条禁令校验 |
| `positions(start)` | 图案经过的全部格点（长度 = 角度数 + 2） |
| `directions()` | 每段的方向（长度 = 角度数 + 1） |
| `finalDir()` | 落笔时的朝向 |
| `anglesSignature()` | 角度签名，如 `"aqaa"` |
| `fromAngles(sig, dir)` | 带重叠校验的构造 |
| `fromAnglesUnchecked` | 不校验（加载内置数据用） |

---

## 3. 绘制画布（GuiSpellcasting）

### 视觉

- **全屏透明，没有背景板**
- 原点在屏幕中心
- 只有「引导点 + 图案线」；iota 列表区才有半透明黑框 `0x50_303030`

### 引导点（鼠标附近格点）

```kotlin
radius = 3                          // 显示半径
for (dotCoord in mouseCoord.rangeAround(3)) {
    if (dotCoord in usedSpots) continue
    delta = dotPx.distanceTo(mouse)
    scaledDist = clamp(1 - (delta - hexSize) / (3 * hexSize), 0, 1)   // 中心 1.0，边缘 0
    drawSpot(dotPx, scaledDist * 2f, ...)   // 半径与亮度都随 scaledDist 变化
}
```

### 状态机（三态）

```
BetweenPatterns  → 等待落笔
JustStarted(start)  → 已落笔、还没画出第一段
Drawing(start, current, wipPattern)  → 绘制中
```

**`current` 是图案当前的终点格，`drawMove` 的锚点取自它。**

### 落笔（drawStart）

- 鼠标位置 clamp 到画面范围
- 计算 `pxToCoord`
- **若该格已在 `usedSpots` 中则不能落笔**
- 否则进入 `JustStarted`

### 延展（drawMove）

```kotlin
anchorCoord = when (state) {
    BetweenPatterns -> null
    JustStarted -> state.start
    Drawing -> state.current          // ← 终点，不是下一格
}
snapDist = hexSize² * 2.0 * clamp(gridSnapThreshold, 0.5, 1.0)
if (anchor.distanceToSqr(mouse) >= snapDist) {     // ← 平方比平方！
    angle = atan2(delta.y, delta.x)
    snappedAngle = (angle / TAU).mod(6.0)          // 注意：mod 6，不是 ×6
    newdir = HexDir[(snappedAngle * 6).roundToInt() + 1 mod 6]
    idealNextLoc = anchorCoord + newdir
    if (idealNextLoc !in usedSpots) {
        if (JustStarted) → Drawing(anchorCoord, idealNextLoc, HexPattern(newdir))
        else {
            lastDir = wipPattern.finalDir()
            if (newdir == lastDir.rotatedBy(BACK)) {
                // 反方向 = 回溯
                if (angles.isEmpty()) state = JustStarted(current + newdir)
                else { current += newdir; angles.removeLast() }
            } else if (wipPattern.tryAppendDir(newdir)) current = idealNextLoc
        }
    }
}
```

**两个极易写错的点**：
1. `snapDist` 与 `distanceToSqr` 比较，两者都是**平方量**（不要再开方）
2. `angle / TAU` 先 `.mod(6.0)` 再 `×6` 取整 —— 等价于角度映射到 6 个扇区

### 收笔（drawEnd）

- `JustStarted` → 什么都没画出来，回 `BetweenPatterns`
- `Drawing` → 把图案加入列表、把 `positions(start)` 全加入 `usedSpots`、发包给服务端

### 其它交互

- `mouseMoved` 与 `mouseDragged` **都**驱动 `drawMove`
- `onClose`：若正在绘制则只结束当前图案，不关界面
- 每 tick 检查手持物是否仍是法杖，否则关闭
- 滚轮/快捷键翻页、网格缩放走 `GRID_ZOOM` 属性
- 有环境音效 `GridSoundInstance`（随鼠标位置变化）

---

## 4. 图案渲染与特效（RenderLib.kt）

### zappy（电光抖动）算法 —— 特效的核心

对每条线段细分 `hops` 个点，每点做噪声扰动：

```kotlin
hopDist = segLen / hops
maxVariance = hopDist * variance

for j in 1..hops:
    progress = j / (hops + 1)
    pos = lerp(src, target, progress)
    // 用 (段号 i, 子段号 j, 时间) 作为 Perlin 噪声输入
    minorPerturb = getNoise(i, j, sin(zSeed)) * flowIrregular
    theta = 3 * getNoise(i + progress + minorPerturb - zSeed, 1337, seed) * TAU
    r = getNoise(i + progress - zSeed, 69420, seed) * maxVariance * scaleVariance(progress)
    randomHop = Vec2(r*cos(theta), r*sin(theta))
    zappyPts.add(pos + randomHop)

scaleVariance(p) = min(1.0, 8 * (0.5 - abs(0.5 - p)))   // 段内两端小、中间大
```

- `zSeed = totalTicks * speed` → 抖动随时间流动
- 噪声：`SimplexNoise(9001)` 且结果 `/2`（注释：perlin 输出基本在 -0.5~0.5）

### 线型

- **画两遍**：外层宽 `5`（原色）、内层宽 `2`（`screen()` 提亮一半）
  → 这就是发光观感的来源
- `screen(n) = (n + 255) / 2`、`dodge(n) = n * 0.9`
- 圆点用 **6 段近似圆**（不是真圆）
- 拐角处按 `joinAngles` 做圆角补片，`CAP_THETA = 18°` 一段

### 参数预设（PatternSettings）

| 预设 | hops | variance | speed | flowIrregular | readabilityOffset | lastSegmentLen |
|---|---|---|---|---|---|---|
| STATIC | 10 | 0.5 | 0 | 0.2 | 0 | 1.0 |
| READABLE | 10 | 0.5 | 0 | 0.2 | 0.2 | 0.8 |
| WOBBLY | 10 | 2.5 | 0.1 | 0.2 | 0 | 1.0 |

线宽预设：`fromStroke(s)` → inner `s*2/5`、outer `s`、startDot `0.8*s*2/5`、gridDot `0.4*s*2/5`。

### 画布上的配色

| 对象 | 尾部色 | 头部色 |
|---|---|---|
| 正在绘制 | `0xff64c8ff`（亮蓝） | `0xfffecbe6`（粉） |
| 已命中 | 青系渐变 | 亮青 |
| 未命中 | 红色系 | 亮红 |

### iota 显示

- 左侧 `LHS_IOTAS_ALLOCATION = 0.7` 宽度的半透明黑框，列栈内容
- 右侧 `RHS_IOTAS_ALLOCATION = 0.15`（×1.5 缩放）显示阿卡夏记录，**透明度随时间正弦呼吸**（150~255）

---

## 5. 媒质（Media）

### 单位

| 名称 | 媒质 |
|---|---|
| 紫水晶粉 DustUnit | 10,000 |
| 碎晶 ShardUnit | 50,000 |
| 晶体 CrystalUnit | 100,000 |
| 淬灵碎晶 QuenchedShard | 300,000 |
| 淬灵块 QuenchedBlock | 1,200,000 |

### 存储接口（ADMediaHolder + MediaHolderItem）

- `getMedia / getMaxMedia / setMedia`
- `canProvideMedia`（能否向外供能）、`canRecharge`（能否被充能）
- `withdrawMedia(stack, cost, simulate)`：`cost < 0` 表示尽量全取
- `insertMedia(stack, amount, simulate)`：`amount < 0` 表示尽量全存
- `getConsumptionPriority`：多来源时决定从哪个扣（电池优先级）

**玩家本身不存媒质** —— 媒质来自背包里的紫水晶粉 / 充能紫水晶 / 淬灵晶簇等物品。

---

## 6. 法杖（ItemStaff）

- 右键 → 打开绘制界面（服务端把 VM 状态、已保存图案、栈描述发给客户端）
- **潜行 + 右键** → 清空施法数据与已保存图案
- 施法者有 `FEEBLE_MIND` 属性（>0）时**无法使用**
- **14 种材质功能完全相同**，只有外观差异：

| 材质 | 说明 |
|---|---|
| oak / spruce / birch / jungle / acacia / dark_oak | 六种原版木材 |
| crimson / warped | 下界木 |
| mangrove / cherry / bamboo | 1.19–1.20 新增 |
| edified | 咒法学自己的木材 |
| quenched | 淬灵晶簇杖 |
| mindsplice | 心织杖（终局） |

---

## 7. 图案注册表（HexActions + PatternRegistryManifest）

- 图案**硬编码在代码里**，不是数据驱动：
  ```java
  public static final ActionRegistryEntry SWAP = make("swap",
      new ActionRegistryEntry(HexPattern.fromAngles("aawdd", HexDir.EAST), new OpTwiddling(2, new int[]{1,0})));
  ```
- 匹配：`signature → action` 的哈希表做 **O(1) 查找**（键是 `entry.prototype().getAngles()`）
- 三类匹配结果：`Normal` / `PerWorld`（本世界专属随机图案）/ `Special`（SpecialHandler，如数字字面量）
- 重复签名会打 warning 并覆盖
- **本项目已提取 188 条**，见 `PATTERN_CATALOG.json`，严格校验全部通过（当前数字见 STATUS.generated.md）

---

## 8. 移植到泰拉瑞亚时的对应关系

| MC 概念 | 泰拉瑞亚对应 |
|---|---|
| Screen（全屏 GUI） | `ModSystem.ModifyInterfaceLayers` 界面层 |
| `ItemStack` 的 NBT | `Item` 的 `ModItem` + 存档 TagCompound |
| 紫水晶粉 / 充能紫水晶 | 紫晶（Amethyst）等宝石 |
| `FEEBLE_MIND` 属性 | 暂未接（可用减益 buff 替代） |
| 环境音 `GridSoundInstance` | `SoundEngine.PlaySound` |
| 网络包 `MsgNewSpellPatternC2S` | tModLoader `ModPacket` |
| Patchouli 手册 | 自建图鉴 UI |

**MC 有而泰拉没有的东西**（需重新设计）：
- 附魔 / 药水效果体系（泰拉是 buff，语义相近但 API 不同）
- 数据包 / 标签系统（`tags/action/*.json`）
- 多方块结构（泰拉无原生概念，要自建 TileEntity + 结构检测）
- 维度传送（泰拉的生物群落与世界结构不同）
- `Component` 富文本（泰拉用 `TooltipLine` 与颜色标记）

---

## 9. 已知踩坑记录（本项目实际遇到的）

| 现象 | 根因 |
|---|---|
| 画不出线 | 吸附阈值多开了一次方（应为平方比平方） |
| 画布每帧开关闪烁 | `UseItem` 开启画布后，同帧 `PostUpdateInput` 又把它关了 |
| 退出画布后无法移动 | `Main.blockInput = true` 设在 `IsOpen` 判断外、且关闭时不恢复 |
| 法杖名显示成 key | 本地化文件名写成 `en-US_Mods.X.hjson`（那是 Configs 专用格式），应为 `en-US.hjson` |
| 模组被禁用 | 在 `Mod.Load()` 里 `new Texture2D` → FNA3D 要求图形 API 只在主线程调用 |
| 打包报 TML003 | 游戏运行中锁着 `.tmod` 文件；且游戏进程名是 `dotnet.exe` 而非 `tModLoader` |
| `Math.Abs` 编译不过 | 命名空间以 `.Math` 结尾会劫持 BCL 的 `Math`，须写 `System.Math` |
| `-p:BuildMod=false` 报 NETSDK1013 | 该模式下 targets 不设 `TargetFramework`，需在 csproj 显式声明 |
