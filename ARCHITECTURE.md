<!--
  本文档由 _tools/gen_architecture.ps1 从代码生成 —— **不要手改**。
  改了代码请重跑生成器；_tools/check_arch.ps1 会核对文档与代码是否一致。
-->

# 架构入口

这是**唯一**的架构入口。源码 2 万行塞不进任何单次上下文，所以这里只放三样东西：
分层与依赖方向、每个文件的一句话职责、以及"什么住在哪里"的单一真源表。
**会过期的数字**（图案数、测试数、包体）一律不在这里，去 [STATUS.generated.md](STATUS.generated.md)。

## 1. 分层与依赖方向（可机械校验）

```
        Client/  ──┐
                  ├──▶  Core/        （Core 谁都不依赖）
        Content/ ─┤
                  │
        Config/  ─┘

禁止：Core →(Content|Client|Config)、Content → Client
```

| 层 | 职责 |
|---|---|
| `Config/` | 模组配置（依赖 tModLoader，所以**不在 Core 里**） |
| `Core/` | 纯逻辑：栈机 / 图案 / 几何 / 世界接口 / UI 布局数学。**不得引用 XNA 与 tModLoader** |
| `Content/` | 泰拉侧实现：物品、方块、玩家、世界适配 |
| `Client/` | 客户端表现：画布、书、HUD、调试叠加层 |

这两条约束由 `_tools/check_arch.ps1` **零白名单**强制。
（曾经有 3 个例外被写进文档当成"设计如此" —— 违规一旦被文档化，
读文档的人就会认为那是规范，永远不会去修。所以例外清零、断言收紧。）

## 2. 每个文件的一句话职责

从文件头注释自动提取。**空职责那几行就是文档缺口**。

### Config/

| 文件 | 职责 |
|---|---|
| `HexClientConfig.cs` | 模组的客户端配置，会出现在 tModLoader 的「设置 → 模组配置」里。 |
| `HexServerConfig.cs` | 服务端配置。 |

### Core/

| 文件 | 职责 |
|---|---|
| `Casting/Actions/AkashicActions.cs` | `akashic/read`：从某个坐标的阿卡夏记录方块上，按**图案**查一个 iota。 |
| `Casting/Actions/BasicActions.cs` | 常数图案：不取参数，往栈上压一个固定值。 |
| `Casting/Actions/BlockActions.cs` | `conjure_block` 与 `conjure_light`：**凭空**造出一块方块 / 一盏光。 |
| `Casting/Actions/BrainsweepActions.cs` | `brainsweep`：脑叶切除 —— 把一只生物「处理」掉，换来一块更高级的方块。 |
| `Casting/Actions/BrainsweepRules.cs` | 一条「脑叶切除」配方。移植自源项目 `common/recipe/BrainsweepRecipe.java`。 |
| `Casting/Actions/ColorizeAction.cs` | `colorize`：拿一份**颜料**，把自己之后所有法术的配色换成它。 |
| `Casting/Actions/CompareActions.cs` | `compare_entity`：两个实体**是不是同类**。 |
| `Casting/Actions/CraftActions.cs` | `craft/cypher` / `craft/trinket` / `craft/artifact`： |
| `Casting/Actions/EntitySelectActions.cs` | `get_entity/*`：取**某个坐标上**的实体（最近的一个）。 |
| `Casting/Actions/EvalActions.cs` | if：三目运算。吃 (条件:bool, 真值, 假值)，吐选中的那个。 |
| `Casting/Actions/FlightActions.cs` | 飞行的「危险度」计算。移植自源项目 `OpFlight.getDanger`。 |
| `Casting/Actions/ListActions.cs` | 开括号：进入列表构建模式。 |
| `Casting/Actions/LogicActions.cs` | `equals` / `not_equals`：比较两个 iota 是否相等。 |
| `Casting/Actions/MathActions.cs` | 运算符图案（add / sub / mul / div / abs / pow / and / or / greater ...）。 |
| `Casting/Actions/PotionActions.cs` | 药水效果的种类。对应源项目注册的 10 个 MC `MobEffects`。 |
| `Casting/Actions/RaycastActions.cs` | 三个射线图案的共同部分。 |
| `Casting/Actions/ReadWriteActions.cs` | 「只做一件事」的法术，但作用对象是**施法环境**（手持物品）而不是世界。 |
| `Casting/Actions/SentinelActions.cs` | `sentinel/create` 与 `sentinel/create/great`：在指定位置放置哨卫。 |
| `Casting/Actions/SimpleSpellActions.cs` | `beep`：在指定位置敲一个音符。 |
| `Casting/Actions/SpellActions.cs` | `add_motion`：给目标实体施加一次推力。 |
| `Casting/Actions/StackUtilActions.cs` | `open_n_parens`：一次开 n 层括号，n 从栈顶取。 |
| `Casting/Actions/StorageActions.cs` | `read_into_parens`：从**手持的数据载体**读出一个 iota 并放进括号列表。 |
| `Casting/Actions/WorldActions.cs` | `get_caster`：把施法者自身压上栈。 |
| `Casting/Actions/WorldEffectActions.cs` | 世界效果类图案的公共部分。</summary> |
| `Casting/Actions/ZoneActions.cs` | 区域查询的筛选种类。对应源项目 `OpGetEntitiesBy` 的五个谓词。</summary> |
| `Casting/Arithmetic/ArithmeticEngine.cs` | 一种算术实现（对应源项目 Arithmetic 接口）。 |
| `Casting/Arithmetic/ListArithmetic.cs` | 列表算术。对应源项目 ListArithmetic.kt。 |
| `Casting/Castables/Action.cs` | 一条图案的行为。 |
| `Casting/Castables/ActionTypes.cs` | 一条图案的**参数类型契约**：它消耗什么类型、产出什么类型。 |
| `Casting/Castables/SpellAction.cs` | 会**作用于世界**的图案的行为基类。 |
| `Casting/Circles/CircleCastingEnvironment.cs` | 法术环的执行状态（供 `circle/*` 三个图案读取）。 |
| `Casting/Circles/CircleComponent.cs` | 方向集合的位掩码。</summary> |
| `Casting/Circles/CircleDir.cs` | 法术环控制流的**方向**。 |
| `Casting/Circles/CircleMessages.cs` | 法术环的消息出口。 |
| `Casting/Circles/CircleTraversal.cs` | 法术环对世界的访问。 |
| `Casting/Eval/CastingEnvironment.cs` | 打包法术的种类。对应源项目的三个物品：cypher（符纸，一次性）、 |
| `Casting/Eval/CastResult.cs` | 对施法 VM 做一次操作的结果。 |
| `Casting/Eval/ICastingWorld.cs` | 施法环境对「世界」的**只读**访问抽象。 |
| `Casting/Eval/Mishaps/CommonMishaps.cs` | 栈上的参数不够。栈不变。</summary> |
| `Casting/Eval/Mishaps/Mishap.cs` | mishap 的上下文：出错的图案与（可能的）图案名。 |
| `Casting/Eval/OperationResult.cs` | 图案执行后的通用结果接口。 |
| `Casting/Eval/ResolvedPatternType.cs` | 一条图案被求值后的解析状态。 |
| `Casting/Eval/SideEffects/EvalSound.cs` | 求值音效种类。移植自 at.petrak.hexcasting.api.casting.eval.sideeffects.EvalSound。 |
| `Casting/Eval/SideEffects/OperatorSideEffect.cs` | 施法完成后发生的副作用。 |
| `Casting/Eval/SideEffects/ParticleSpray.cs` | 一次粒子喷发。移植自源项目 `ParticleSpray`。 |
| `Casting/Eval/SpellList.cs` | 函数式（持久化）列表。移植自 at.petrak.hexcasting.api.casting.SpellList。 |
| `Casting/Eval/Vm/CastingImage.cs` | 一对「括号内的 iota + 是否由 Consideration 转义而来」。 |
| `Casting/Eval/Vm/CastingVM.cs` | 施法虚拟机。 |
| `Casting/Eval/Vm/CastUserData.cs` | 本次施法的临时数据袋。 |
| `Casting/Eval/Vm/ContinuationFrame.cs` | 求值过程中的一个「帧」。 |
| `Casting/Eval/Vm/FrameForEach.cs` | Thoth（for_each / eval_breakable）求值帧。 |
| `Casting/Eval/Vm/SpellContinuation.cs` | 施法过程中的续延（帧栈）。 |
| `Casting/GreatTeleportRules.cs` | 大法术的世界规则。**由游戏侧注入**，Core 只读。 |
| `Casting/HexMathUtil.cs` | 数值安全工具。 |
| `Casting/HexUnits.cs` | 单位换算常量。 |
| `Casting/Iota/CompositeIotas.cs` | 列表 iota。源：ListIota。 |
| `Casting/Iota/Iota.cs` | Iota 类型标签。对应源项目 IotaType 单例的集合（注册于 HexIotaTypes.java）。 |
| `Casting/Iota/IotaSerializer.cs` | iota 的序列化与反序列化。 |
| `Casting/Iota/PrimitiveIotas.cs` | 空值 iota。源：NullIota。单例。</summary> |
| `Casting/Iota/StoragePolicy.cs` | 数据载体的存储策略。</summary> |
| `Casting/Math/HexAngle.cs` | 六种转向角。顺序与源项目一致，序号参与模运算，不可重排。 |
| `Casting/Math/HexCoord.cs` | 六边形网格上的轴坐标（axial coordinate）。 |
| `Casting/Math/HexDir.cs` | 六边形网格的六个方向。顺序与源项目一致（顺时针，从东北开始）， |
| `Casting/Math/HexGrid.cs` | 六边形网格的像素换算与遍历。 |
| `Casting/Math/HexPattern.cs` | 一条咒术图案：起始方向 + 一串转向角。 |
| `Casting/Math/ParseError.cs` | 图案解析失败的位置与原因。 |
| `Casting/Math/PatternSuggestion.cs` | 「你画的这条最接近哪个图案」。 |
| `Casting/Math/SpecialPatterns.cs` | **特殊图案**（special patterns）：不在 188 条注册表里，但能被识别成动作的图案。 |
| `Casting/Math/Vec2f.cs` | 最小二维浮点向量：只有 `Core` 真正用到的那几个操作。 |
| `Media/IMediaStorage.cs` | 媒质容器。移植自 at.petrak.hexcasting.api.addldata.ADMediaHolder 与 |
| `Media/MediaConstants.cs` | 媒质单位换算。移植自 at.petrak.hexcasting.api.misc.MediaConstants（数值逐项对齐）。 |
| `Media/MediaPaymentPlanner.cs` | 背包里一堆可提供媒质的物品。</summary> |
| `Media/MediaPool.cs` | 一个简单的媒质池实现。玩家自身的媒质储量、物品内的媒质都复用它。 |
| `Registry/GeneratedPatternData.cs` | 图案的静态数据：id、角度签名、起始方向、源实现类名。</summary> |
| `Registry/PatternDisplay.cs` | 图案的**显示名**。界面一律走这里，不要各自拼 `Id.Replace("hexcasting:", "")`。 |
| `Registry/PatternNames.Generated.cs` | 图案的**正式中文名**（例如 get_caster → 「意识之精思」）。 |
| `Registry/PatternRegistry.cs` | 图案匹配结果，对应源项目的 PatternShapeMatch 联合类型。 |
| `Ui/BookContent.Generated.cs` | 书本内容骨架：从源项目的 Patchouli 手册（7 分类 / 82 条目）生成。 |
| `Ui/BookLayout.cs` | 浮点矩形。Core 不许引用 XNA（也就不能用 `Rectangle`/`Vector2`），所以自带一个。</summary> |
| `Ui/BookModel.cs` | 页面类型。对应 Patchouli 注册在 <c>patchouli:…</c> 名下的一组页面类； |
| `Ui/BookText.cs` | 量一段文本有多宽。真实渲染时传字体测量，离线测试时传假函数。</summary> |
| `World/AmethystLoot.cs` | 晶簇的生长阶段。对应 MC 的四个方块。</summary> |
| `World/LookResolver.cs` | 视线解析的输入。 |
| `World/SegmentSweep.cs` | 一个实体的判定箱，坐标单位：**图格**。 |
| `World/TileRaycast.cs` | 二维图格网格上的射线求交（Amanatides &amp; Woo 的 DDA 算法）。 |

### Content/

| 文件 | 职责 |
|---|---|
| `HexGlobalNPC.cs` | 为每个 NPC 维护一个持久「视线方向」。 |
| `HexGlobalProjectile.cs` | 为每个弹幕维护一个持久「视线方向」，即飞行方向。 |
| `HexPlayer.cs` | 玩家侧咒术数据。 |
| `Items/Abacus.cs` | 阿卡夏记录的**物品形态**。 |
| `Items/DevKit.cs` | 开发者测试包：一次把每个子系统的代表性物品各发一份。 |
| `Items/DevStaff.cs` | 开发者法杖（Dev Staff）。 |
| `Items/EdifiedFurniture.cs` | 启迪木的「家具套装」：用启迪木板去合成**原版家具**。 |
| `Items/HexBookItem.cs` | 咒法学之书。对应源项目的 `hexcasting:thehexbook`（Patchouli 写的那本引导书）。 |
| `Items/HexDecoBlockItem.cs` | 建材方块物品的公共实现（配合 <see cref="HexDecoBlock"/>）。 |
| `Items/HexDirectrixItems.cs` | 导线的公共物品基类。对应源项目三根 `*Directrix` 的物品形态。 |
| `Items/HexImpetusItem.cs` | 原动力（物品形态）。对应源项目 `hexcasting:impetus/*`。 |
| `Items/HexRecipeGroups.cs` | 建材族的**配方组**。 |
| `Items/HexSlateItem.cs` | 石板（物品形态）。对应源项目 `hexcasting:slate`。 |
| `Items/HexStaff.cs` | 法杖基类。移植自源项目 `common/items/ItemStaff.java`。 |
| `Items/ItemIotaStorage.cs` | 「数据载体」物品的基类：能存一个 iota。 |
| `Items/ItemPackagedSpell.cs` | 打包法术物品的基类：把一串图案与一份媒质封在里面，右键即可施放。 |
| `Items/ItemScroll.cs` | 卷轴。移植自源项目 `common/items/storage/ItemScroll.java`。 |
| `Items/JewelerHammer.cs` | 珠宝匠锤。对应源项目 `hexcasting:jeweler_hammer`。 |
| `Items/MediaFlask.cs` | 媒质瓶：把宝石蓄入自身的媒质池。 |
| `Items/MediaMaterials.cs` | 媒质材料物品的基类。 |
| `Items/MiscDecoItems.cs` | 卷轴纸（物品）。</summary> |
| `Items/PackagedSpellCast.cs` | 打包法术专用环境：媒质**从物品自己的池子里扣**，不是从玩家身上。 |
| `Items/ScryingLens.cs` | 探术透镜。对应源项目 `hexcasting:lens`（`ItemLens`，装备在**头部**装备位）。 |
| `Items/Spellbook.cs` | 法术书。对应源项目 `hexcasting:spellbook`（**不是**那本引导书 `thehexbook`）。 |
| `Items/WallScrollFrames.cs` | 卷轴挂板的基类。对应源项目里「把卷轴挂到墙上」那一步所需的载体。 |
| `Net/HexNet.cs` | 网络消息类型。 |
| `Net/HexNetSync.cs` | 方块交互的上报辅助。 |
| `Net/ServerCastState.cs` | **服务端**的施法状态：每个玩家一份 VM。 |
| `PlayerCastingEnvironment.cs` | 玩家施法环境：把 VM 的抽象需求接到泰拉玩家身上。 |
| `SpellSounds.cs` | 咒法学的音效层。 |
| `SpellVisuals.cs` | 法术粒子的表现层：把 Core 算出来的 <see cref="ParticleSpray"/> 变成真正的 dust。 |
| `TerrariaCastingWorld.cs` | <see cref="ICastingWorld"/> 的泰拉瑞亚实现。 |
| `Tiles/AkashicRecord.cs` | 阿卡夏记录方块。对应源项目 `hexcasting:akashic_record`。 |
| `Tiles/AmethystDustBlock.cs` | 紫水晶粉块。对应源项目 `hexcasting:amethyst_dust_block`（装饰方块）。 |
| `Tiles/AmethystGeode.cs` | 晶洞母岩。对应 MC 的 `budding_amethyst`。 |
| `Tiles/CircleCursor.cs` | 法术环的「执行游标」：当前正在执行哪一格。 |
| `Tiles/ConjuredBlock.cs` | 被召唤出来的方块/光源的存活管理。 |
| `Tiles/DecoBlocks.Generated.cs` | 建材方块的公共实现（P2-6 装饰方块家族）。 |
| `Tiles/HexDirectrix.cs` | 导线的公共基类。对应源项目 `BlockEmptyDirectrix` / `BlockBooleanDirectrix` / |
| `Tiles/HexImpetus.cs` | 法术环对泰拉世界的访问实现。 |
| `Tiles/HexSlate.cs` | 石板。对应源项目 `hexcasting:slate` —— **法术环的「指令」**。 |
| `Tiles/MiscDeco.cs` | 贴在墙上的装饰/光源方块的公共实现。 |
| `Tiles/WallScroll.cs` | 壁挂卷轴。对应源项目的 `EntityWallScroll`。 |
| `Worldgen/GeodeWorldGen.cs` | 紫水晶晶洞的世界生成。 |

### Client/

| 文件 | 职责 |
|---|---|
| `HexCanvasState.cs` | 画布与 HUD 的客户端共享状态。 |
| `HexClientSystem.cs` | 客户端系统：画布输入、画布绘制、HUD（媒质指示 + 图案识别反馈）。 |
| `HexColors.cs` | 咒法学的表现层配色。 |
| `HexDebugOverlay.cs` | 开发者调试叠加层：把「看不见但必须确认」的东西画出来。 |
| `HexPigment.cs` | 法术配色（「颜料」）。 |
| `HexPixel.cs` | 共享的 1×1 白色贴图，用于画线段与方块点。 |
| `HexVec.cs` | `Core` 的 <see cref="Vec2f"/> ↔ XNA 的 <see cref="Vector2"/> 互转。 |
| `HexVmState.cs` | 客户端侧的 VM 状态。 |
| `UI/HexBook.cs` | 咒法学之书。 |
| `UI/HexCanvas.cs` | 已画完的一条图案：图案本体、起始格点、匹配到的定义（null 表示未命中）。 |
| `UI/PatternRenderer.cs` | 图案渲染：忠实移植源项目的「zappy（电光抖动）」线型。 |

### （模组根目录）

| 文件 | 职责 |
|---|---|
| `HexCastingTerraria.cs` | 模组主入口。1.4.5 的 tModLoader 会自动发现 [Mod] 类。 |

## 3. 单一真源表（改东西之前先看这里）

| 事实 | 唯一出处 | 别处不许重定义 |
|---|---|---|
| 图案清单（id / 签名 / 起始方向） | `Core/Registry/GeneratedPatternData.cs` | 任何地方手写图案串 |
| 图案中文名 | `Core/Registry/PatternNames.Generated.cs` | 界面里硬写名字 |
| 图案行为注册 | `Core/Casting/Actions/*.cs` 的 `RegisterAll()` | 在别处 `RegisterAction` |
| 坐标换算 / 网格几何 | `Core/Casting/Math/HexGrid.cs` | 手抄一份公式（**曾经发生过**） |
| 书的布局 / 格子命中 | `Core/Ui/BookLayout.cs` | 在 HexBook 里再写一遍 col/row（**曾经发生过**） |
| 画布吸附阈值 | `Client/UI/HexCanvas.cs` + `Config/HexClientConfig.cs`（默认 0.5，对齐原版） | 任何第三处默认值 |
| 世界接口 | `Core/Casting/Eval/ICastingWorld.cs` | Content 里另立接口 |
| 当前状态数字 | `STATUS.generated.md`（生成） | 任何手写文档复述数字 |
| 架构约束 | `_tools/check_arch.ps1`（可执行断言） | 文档里写"我们约定…" |

## 4. 怎么验一遍

```powershell
cd D:\DeepSeekHarness\tmod
.\_tools\run_all.ps1      # 编译 → 离线 VM → 拖拽/几何 → 生成状态表 → 架构断言
```

被验证覆盖不到的层（渲染、输入、联机、存档）见 [STATUS.generated.md](STATUS.generated.md) 的最后一节。

## 5. 参考实现

原版咒法学源码在 `D:\DeepSeekHarness\hexsrc`（Kotlin/Java）。
**凡是有疑问的地方，以原版源码为准，不要以本模组的注释为准** ——
本模组已经出现过几次"注释写的语义和原版相反"（`tileNoFail`、`draw.py` 的段数）。
