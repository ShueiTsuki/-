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
| `Addons/` | 附属（HexParse / Hexcessible / HexDebug…）：每个附属一个目录，内部再分 Core / Game / Client，规矩见 ADDONS.md，功能 -> 文件见 ADDONS.generated.md |

这两条约束由 `_tools/check_arch.ps1` **零白名单**强制。
（曾经有 3 个例外被写进文档当成"设计如此" —— 违规一旦被文档化，
读文档的人就会认为那是规范，永远不会去修。所以例外清零、断言收紧。）

## 2. 每个文件的一句话职责

从文件头注释自动提取。**空职责那几行就是文档缺口**。

### Config/

| 文件 | 职责 |
|---|---|
| `HexAddonsClientConfig.cs` | 附属兼容 · 客户端开关（纯界面的附属）。每个人自己决定，随时改、立即生效，联机时互不影响 —— |
| `HexAddonsConfig.cs` | 附属兼容 · 服务端开关（有物品 / 方块 / 图案 / 指令的附属）。见 ADDONS.md「开关的实际效果」。 |
| `HexClientConfig.cs` | 模组的客户端配置，会出现在 tModLoader 的「设置 → 模组配置」里。 |
| `HexServerConfig.cs` | 服务端配置。 |

### Core/

| 文件 | 职责 |
|---|---|
| `Canvas/PatternDrawer.cs` | 画布上已经画完的一条图案。<see cref="Type"/> 在求值结果回来之前是 Unresolved（灰色）。</summary> |
| `Canvas/PatternGeometry.cs` | 一个带颜色的顶点。颜色为 0xAARRGGBB（非预乘 alpha）。三个一组构成三角形。</summary> |
| `Canvas/SimplexNoise.cs` | Minecraft 的 `SimplexNoise` + `SingleThreadedRandomSource`，逐行移植。 |
| `Canvas/StaticPatternArt.cs` | 图案的「形状与摆放」设置。移植自源项目 client/render/PatternSettings（PositionSettings + StrokeSettings + ZappySettings）。 |
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
| `Casting/Eval/ICastDebugObserver.cs` | 附属调试器（HexDebug）在施法环境上的挂点。本体只在三处通知它（上游 HexDebug 用 mixin 注入的同样三处）： |
| `Casting/Eval/ICastingWorld.cs` | 施法环境对「世界」的**只读**访问抽象。 |
| `Casting/Eval/Mishaps/CommonMishaps.cs` | 栈上的参数不够。惩罚：把缺的那几个补成垃圾值（源项目同）。</summary> |
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
| `Casting/Iota/DisplayText.cs` | 上游 Minecraft Component（带颜色、可嵌套的文字）的最小替身。iota 的显示（上游 IotaType.display）都返回它。 |
| `Casting/Iota/Iota.cs` | Iota 类型标签。对应源项目 IotaType 单例的集合（注册于 HexIotaTypes.java）。 |
| `Casting/Iota/IotaSerializer.cs` | iota 的序列化与反序列化。 |
| `Casting/Iota/PrimitiveIotas.cs` | 空值 iota。源：NullIota。单例。</summary> |
| `Casting/Iota/UnknownIota.cs` | 不认识种类的 iota —— 多半来自一个**关掉了的附属**（例如 HexParse 的注释 iota）。 |
| `Casting/Math/EulerPathFinder.cs` | 给一个图案找另一种画法：**形状（边集）完全相同，笔顺不同**。 |
| `Casting/Math/HexAngle.cs` | 六种转向角。顺序与源项目一致，序号参与模运算，不可重排。 |
| `Casting/Math/HexCoord.cs` | 六边形网格上的轴坐标（axial coordinate）。 |
| `Casting/Math/HexDir.cs` | 六边形网格的六个方向。顺序与源项目一致（顺时针，从东北开始）， |
| `Casting/Math/HexGrid.cs` | 六边形网格的像素换算与遍历。 |
| `Casting/Math/HexPattern.cs` | 一条咒术图案：起始方向 + 一串转向角。 |
| `Casting/Math/ParseError.cs` | 图案解析失败的位置与原因。 |
| `Casting/Math/PatternSuggestion.cs` | 「你画的这条最接近哪个图案」。 |
| `Casting/Math/SpecialPatterns.cs` | **特殊图案**（special patterns）：不在 188 条注册表里，但能被识别成动作的图案。 |
| `Casting/Math/Vec2f.cs` | 最小二维浮点向量：只有 `Core` 真正用到的那几个操作。 |
| `Dev/SampleHexes.cs` | 法术里的一步：一条具名图案，或一个数字字面量（数字图案 aqaa… 由 <see cref="SpecialPatterns.EncodeNumber"/> 生成）。</summary> |
| `Media/MediaConstants.cs` | 媒质单位换算。移植自 at.petrak.hexcasting.api.misc.MediaConstants（数值逐项对齐）。 |
| `Media/MediaPaymentPlanner.cs` | 扣费优先级。逐字照抄源项目 ADMediaHolder：数值越大越先扣。 |
| `Media/Overcast.cs` | 过载（用生命换媒质）与「启蒙」的判定，照原版 PlayerBasedCastEnv.extractMediaFromInventory |
| `Media/Pigments.cs` | 一种颜料（原版 <c>PigmentItem</c> 的 <c>ColorProvider</c>）。颜色一律 0xRRGGBB。 |
| `Registry/GeneratedPatternData.cs` | 图案的静态数据：id、角度签名、起始方向、源实现类名。</summary> |
| `Registry/PatternDisplay.cs` | 图案的**显示名**。界面一律走这里，不要各自拼 `Id.Replace("hexcasting:", "")`。 |
| `Registry/PatternNames.Generated.cs` | 图案的**正式中文名**（例如 get_caster → 「意识之精思」）。 |
| `Registry/PatternRegistry.cs` | 图案匹配结果，对应源项目的 PatternShapeMatch 联合类型。 |
| `Ui/BookCanvas.cs` | 书用到的贴图。与原版同名同尺寸，源矩形用原版贴图坐标。</summary> |
| `Ui/BookContent.Generated.cs` | 本文件由 _tools/gen_book_content.py 生成，**不要手改**：改生成器，然后重新跑脚本。 |
| `Ui/BookModel.cs` | 页面类型。对应 Patchouli 注册在 <c>patchouli:…</c> 名下的一组页面类； |
| `Ui/BookText.cs` | 量一段文本有多宽。真实渲染时传字体测量，离线测试时传假函数。</summary> |
| `Ui/BookUnlocks.cs` | 玩家在书的解锁上用得到的进度。游戏内由 Client 填，离线测试手填。</summary> |
| `Ui/BookView.cs` | 分类页第一跨页右页能放的条目数（Patchouli ENTRIES_IN_FIRST_PAGE）。</summary> |
| `Ui/PatchouliRenderer.cs` | 书上可点的东西。</summary> |
| `World/AmethystLoot.cs` | 晶簇的生长阶段。对应 MC 的四个方块。</summary> |
| `World/HexAxes.cs` | 法术坐标（原版约定：方块单位、+Y 朝上）与泰拉图格坐标（+Y 朝下）之间的换算公式。 |
| `World/LookResolver.cs` | 视线解析的输入。 |
| `World/SegmentSweep.cs` | 一个实体的判定箱，坐标单位：**图格**。 |
| `World/TileRaycast.cs` | 二维图格网格上的射线求交（Amanatides &amp; Woo 的 DDA 算法）。 |

### Content/

| 文件 | 职责 |
|---|---|
| `DevTextureDump.cs` | 开发用：把**原版泰拉的 UI 贴图**导出成 PNG，供离线比对。 |
| `HexChestLoot.cs` | 往世界里的箱子塞原版的三种战利品（源项目 HexLootHandler）： |
| `HexCommands.cs` | /hexcasting 指令。移植自源项目 common/command（HexCommands.register）： |
| `HexGlobalNPC.cs` | 为每个 NPC 维护一个持久「视线方向」。 |
| `HexGlobalProjectile.cs` | 为每个弹幕维护一个持久「视线方向」，即飞行方向。 |
| `HexPlayer.cs` | 玩家侧咒术数据。 |
| `HexSpaceWorld.cs` | 法术坐标 ↔ 泰拉坐标的**唯一**换算层（套在 <see cref="TerrariaCastingWorld"/> 外面）。 |
| `IotaDisplaySetup.cs` | 给 Core 的 iota 显示接上游戏：实体叫什么（上游 EntityIota 显示实体名）、Shift 按没按着（HexParse 的注释按住 Shift 不显示）。 |
| `Items/Abacus.cs` | 阿卡夏记录的**物品形态**。 |
| `Items/AncientLoot.cs` | 远古卷轴（源项目 ItemScroll + TAG_OP_ID，「%s之远古卷轴」）：写着**本世界**某个大法术的笔顺。 |
| `Items/CreativeUnlocker.cs` | 媒质立方（原版 ItemCreativeUnlocker，创造模式物品，没有配方）。 |
| `Items/DevKit.cs` | 开发者测试包：一次把每个子系统的代表性物品各发一份。 |
| `Items/DevStaff.cs` | 开发者法杖（Dev Staff）。 |
| `Items/EdifiedFurniture.cs` | 启迪木的家具。原版 HexBlocks 里有 8 种：楼梯、台阶、栅栏、栅栏门、门、活板门、按钮、压力板。 |
| `Items/HexBookItem.cs` | 咒法学之书。对应源项目的 `hexcasting:thehexbook`（Patchouli 写的那本引导书）。 |
| `Items/HexConditions.cs` | 本模组的配方条件。</summary> |
| `Items/HexDecoBlockItem.cs` | 建材方块物品的公共实现（配合 <see cref="HexDecoBlock"/>）。 |
| `Items/HexDirectrixItems.cs` | 导线的公共物品基类。对应源项目三根 `*Directrix` 的物品形态。 |
| `Items/HexImpetusItem.cs` | 促动石（物品形态）的公共部分。对应源项目 `hexcasting:impetus/*`。 |
| `Items/HexRecipeGroups.cs` | 建材族的**配方组**。 |
| `Items/HexSlateItem.cs` | 石板（物品形态）。对应源项目 `hexcasting:slate`。 |
| `Items/HexStaff.cs` | 法杖基类。移植自源项目 `common/items/ItemStaff.java`。 |
| `Items/IShiftScrollable.cs` | 手上的物品接「潜行 + 滚轮」（上游 HexDebug items/base/ShiftScrollable：调试杖换步进模式、淬灵的再加 Ctrl 换线程）。 |
| `Items/ItemIotaStorage.cs` | 「数据载体」物品的基类：能存一个 iota。 |
| `Items/ItemPackagedSpell.cs` | 打包法术物品的基类：把一串图案与一份媒质封在里面，右键即可施放。 |
| `Items/ItemScroll.cs` | 卷轴。移植自源项目 `common/items/storage/ItemScroll.java`。 |
| `Items/ItemStateArt.cs` | 按物品**实例**的状态换贴图（原版的 item model overrides + 叠层着色）。 |
| `Items/JewelerHammer.cs` | 珠宝匠锤。对应源项目 `hexcasting:jeweler_hammer`。 |
| `Items/MediaFlask.cs` | 媒质瓶（源项目 ItemMediaBattery，「媒质之瓶 / phial of media」）。 |
| `Items/MediaMaterials.cs` | 媒质材料物品的基类（源项目 CCMediaHolder.Static）。 |
| `Items/MiscDecoItems.cs` | 卷轴纸（物品）。</summary> |
| `Items/PackagedSpellCast.cs` | 打包法术专用环境：媒质**从物品自己的池子里扣**，不是从玩家身上。 |
| `Items/PigmentItem.cs` | 颜料（染色剂）。移植自源项目 <c>common/items/pigment/*</c>： |
| `Items/PigmentItems.Generated.cs` | 35 种颜料（原版 ItemDyePigment ×16、ItemPridePigment ×16、空无 / 远古 / 灵魂闪光）。 |
| `Items/ScryingLens.cs` | 探知透镜。对应源项目 `hexcasting:lens`（ItemLens）。 |
| `Items/Spellbook.cs` | 法术书。对应源项目 `hexcasting:spellbook`（ItemSpellbook，**不是**那本引导书）。 |
| `Items/SubSandwich.cs` | 潜艇三明治。原版 HexItems.SUBMARINE_SANDWICH：食物（饥饿值 14、饱和度 1.2 —— 比牛排还顶饱）， |
| `Items/WallScrollFrames.cs` | 卷轴挂板的基类。对应源项目里「把卷轴挂到墙上」那一步所需的载体。 |
| `Net/HexNet.cs` | 网络消息类型。 |
| `Net/HexNetSync.cs` | 方块交互的上报辅助。 |
| `Net/IotaTag.cs` | iota ↔ 存档（<see cref="TagCompound"/>）。**所有存 iota 的物品 / 方块实体都走这里**。 |
| `Net/ServerCastState.cs` | **服务端**的施法状态：每个玩家一份 VM。 |
| `PerWorldPatternSystem.cs` | 大法术「每个世界的笔顺」的存档与同步（源项目 ScrungledPatternsSave）。 |
| `PlayerCastingEnvironment.cs` | 玩家施法环境：把 VM 的抽象需求接到泰拉玩家身上。 |
| `PlayerEffects.cs` | 作用在「玩家自己的东西」上的效果：生命、背包、手持物品、氧气。 |
| `SpellSounds.cs` | 咒法学的音效层。 |
| `SpellVisuals.cs` | 法术粒子的表现层：把 Core 算出来的 <see cref="ParticleSpray"/> 变成真正的 dust。 |
| `TerrariaCastingWorld.cs` | <see cref="ICastingWorld"/> 的泰拉瑞亚实现。 |
| `Tiles/AkashicRecord.cs` | 阿卡夏记录方块。对应源项目 `hexcasting:akashic_record`。 |
| `Tiles/AmethystDustBlock.cs` | 紫水晶粉块。对应源项目 `hexcasting:amethyst_dust_block`（装饰方块）。 |
| `Tiles/AmethystGeode.cs` | 晶洞母岩。对应 MC 的 `budding_amethyst`。 |
| `Tiles/CircleCursor.cs` | 法术环的「执行游标」：当前正在执行哪一格。 |
| `Tiles/ConjuredBlock.cs` | 被召唤出来的方块/光源的存活管理。 |
| `Tiles/DecoBlocks.Generated.cs` | 建材方块的公共实现（P2-6 装饰方块家族）。 |
| `Tiles/EdifiedFurnitureTiles.cs` | 启迪木门（关）。原版 edified_door（BlockHexDoor，木门：手就能开）。</summary> |
| `Tiles/FletcherGaze.cs` | 制箭师促动石的「被盯着」计数（原版 BlockEntityLookingImpetus.serverTick）： |
| `Tiles/HexDirectrix.cs` | 导线的公共基类。对应源项目 `BlockEmptyDirectrix` / `BlockBooleanDirectrix` / |
| `Tiles/HexImpetus.cs` | 法术环对泰拉世界的访问实现。 |
| `Tiles/HexSlate.cs` | 石板。对应源项目 `hexcasting:slate` —— **法术环的「指令」**。 |
| `Tiles/MiscDeco.cs` | 贴在墙上的装饰/光源方块的公共实现。 |
| `Tiles/QuenchedAllayDrops.cs` | 淬灵块的掉落（原版 loot_tables/blocks/quenched_allay.json）： |
| `Tiles/WallScroll.cs` | 壁挂卷轴。对应源项目的 `EntityWallScroll`。 |
| `Worldgen/GeodeWorldGen.cs` | 紫水晶晶洞的世界生成。 |

### Client/

| 文件 | 职责 |
|---|---|
| `CanvasExtensions.cs` | 画布这一帧的情况，交给 <see cref="ICanvasExtension"/>。坐标一律是屏幕像素（和画布同一套）。 |
| `HexCanvasState.cs` | 画布与 HUD 的客户端共享状态。 |
| `HexClientSystem.cs` | 客户端系统：画布输入、画布绘制、HUD（媒质指示 + 图案识别反馈）。 |
| `HexColors.cs` | 咒法学的表现层配色。 |
| `HexDebugOverlay.cs` | 开发者调试叠加层：把「看不见但必须确认」的东西画出来。 |
| `HexPigment.cs` | 法术配色（「颜料 / 染色剂」）的取色。 |
| `HexPixel.cs` | 共享的 1×1 白色贴图，用于画线段与方块点。 |
| `HexVec.cs` | `Core` 的 <see cref="Vec2f"/> ↔ XNA 的 <see cref="Vector2"/> 互转。 |
| `HexVmState.cs` | 客户端侧的 VM 状态。 |
| `ScryingOverlay.cs` | 探知透镜（原版 ItemLens）的两个效果： |
| `SentinelRenderer.cs` | 画自己的哨卫（原版 HexAdditionalRenderers.renderSentinel）：一个内接于单位球的**正二十面体线框**， |
| `UI/DevPanel.cs` | 开发者面板（默认 F7）：不知道该放什么法术时用。 |
| `UI/HexBook.cs` | 咒法学之书（帕秋莉手册）的开关、输入与绘制。内容与排版在 Core（BookContent / PatchouliRenderer）。 |
| `UI/HexCanvas.cs` | 已画完的一条图案 + 注册表匹配结果（null = 未命中）+ 求值结果（决定颜色）。</summary> |
| `UI/PatternArt.cs` | 按原版画法画静态图案（<see cref="StaticPatternArt"/>）的两个入口： |
| `UI/PrimitiveBatch.cs` | 把 <see cref="PatternGeometry"/> 产出的三角形直接交给显卡。 |
| `UI/RichText.cs` | 把 iota 的显示（<see cref="DisplayText"/>）画进泰拉： |
| `UI/SpriteBatchBookCanvas.cs` | <see cref="IBookCanvas"/> 的游戏内实现：全部用 <c>Main.spriteBatch</c> 画（不切到图元绘制， |

### Addons/

| 文件 | 职责 |
|---|---|
| `AddonContent.cs` | 附属的物品。</summary> |
| `AddonRegistry.cs` | 所有附属的登记表 —— 唯一的总入口（ADDONS.md「目录」）。 |
| `HexAddon.cs` | 附属跑在哪一侧：决定它的开关放在服务端还是客户端配置（ADDONS.md「开关的位置」）。</summary> |
| `Hexcessible/Core/AliasEditState.cs` | 改别名（上游 drawstate/AliasChanging.java 去掉渲染的部分）：输入框里的字、原名、签名、确认时存什么。 |
| `Hexcessible/Core/AutoCompleteState.cs` | 自动补全的状态（上游 drawstate/AutoCompleting.java 去掉渲染的部分）：查询串、候选、选中项、选中的书页。 |
| `Hexcessible/Core/FluffySearch.cs` | 上游 Utils.fluffySearch：按顺序逐字命中（不必连续），连续命中加分，开头就对上再加分；有字没命中 = 0。</summary> |
| `Hexcessible/Core/HexcessibleSettings.cs` | Hexcessible 的配置项（上游 HexcessibleConfig.java，默认值照搬）。纯数据：游戏侧从客户端附属配置抄进 <see cref="Current"/>。 |
| `Hexcessible/Core/JavaNum.cs` | 上游 Java 的几个数字细节（智能签名的结果取决于它们）：Float.parseFloat、Float.toString、Math.round(float)、(int) 强转。 |
| `Hexcessible/Core/KeyboardDrawingState.cs` | 键盘绘制的状态（上游 drawstate/KeyboardDrawing.java 去掉渲染的部分）：当前签名、光标处的起点与起笔方向、 |
| `Hexcessible/Core/KeyboardPlacement.cs` | 键盘绘制的纯逻辑（上游 accessor/CastRef.java 的 findClosestAvailable / fits / isValidPatternAddition + Utils.java 的角度字母表）。 |
| `Hexcessible/Core/KnownWorldPatterns.cs` | 学会的大法术画法（上游 config.knownWorldPatterns，一行一条「世界 图案id 签名」，按世界分开）。 |
| `Hexcessible/Core/NumberTable.Generated.cs` | 上游 numbers.txt：第 i 项是数字 i + 1 在「数字之精思」前缀（aqaa / dedd）后面的最短笔顺，共 2000 项。</summary> |
| `Hexcessible/Core/PatternEntries.cs` | 图案索引（上游 entries/PatternEntries.java + entries/BookEntries.java）：每个图案的名字、签名、书里的图案页 |
| `Hexcessible/Core/SmartSigs.cs` | 智能签名用到的文字（格式串里的 {0} 对应上游的 %s）。</summary> |
| `Hexcessible/Game/HexcessibleAddon.cs` | Hexcessible 附属的入口：施法界面的无障碍操作：键盘画图、按名字搜索图案、别名、悬停说明（Ruby / tizu，JSON License）。 |
| `Hexcessible/Game/HexcessibleCanvas.cs` | Hexcessible 在画布上的状态机（上游 drawstate/DrawState + Idling + MouseDrawing + KeyboardDrawing + AutoCompleting， |
| `Hexcessible/Game/HexcessibleIndex.cs` | 游戏侧的图案索引：本体 + 已开的附属图案 + 书（含附属书页），书条目锁不锁看当前玩家的解锁进度。 |
| `Hexcessible/Game/HexcessibleOptions.cs` | Hexcessible 的配置项，挂在客户端「附属兼容」页的 Hexcessible 开关下面（上游 HexcessibleConfig，默认值照搬）。 |
| `Hexcessible/Game/HexcessibleStore.cs` | Hexcessible 自己记的东西（上游放在配置文件里、界面上不显示的 patternAliases / knownWorldPatterns）： |
| `Hexcessible/Game/TooltipBox.cs` | 画 Minecraft 样式的提示框（上游用 DrawContext.drawTooltip：深紫底、紫色渐变边框，贴在给定点右上方，出屏就往回挪）。 |
| `HexDebug/Core/DebugEnvironment.cs` | 调试输出的类别（上游 OutputCategory：普通输出 / 错误）。</summary> |
| `HexDebug/Core/DebugTypes.cs` | 上游 debugger/Enums.kt DebuggerState。</summary> |
| `HexDebug/Core/FrameBreakpoint.cs` | 断点帧（上游 casting/eval/FrameBreakpoint.kt）：不调试时什么都不做；调试器看到它就停。 |
| `HexDebug/Core/HexDebugActions.cs` | 认知危害 iota（上游 casting/iotas/CognitohazardIota.kt）：被调试器登记到就结束调试；平时求值什么都不做。 |
| `HexDebug/Core/HexDebugBook.Generated.cs` | 本文件由 _tools/gen_addon_book.py 生成，**不要手改**：改生成器，然后重新跑脚本。 |
| `HexDebug/Core/HexDebugger.cs` | 调用栈里的一帧（上游 DAP StackFrame）：帧名、指向的源码位置；虚拟帧是尾调用省掉的 FrameFinishEval。</summary> |
| `HexDebug/Core/HexDebugPatterns.cs` | HexDebug 的 22 个图案（上游 registry/HexDebugActions.kt，形状照抄）。开关关着也要登记形状（本世界大法术笔顺不许和它们撞）； |
| `HexDebug/Core/IotaText.cs` | iota 转成文字（上游 utils/Extensions.kt 的 displayWithPatternName / toHexpatternSource / getI18nOrNull / simpleString）。 |
| `HexDebug/Core/Splicing/Selection.cs` | 剪接台的选区（上游 splicing/Selection.kt）：要么是闭区间 [from, to]（可以反着拖），要么是两个格子之间的一条缝（光标）。 |
| `HexDebug/Core/Splicing/SplicingTableAction.cs` | 剪接台的按钮操作（上游 splicing/SplicingTableAction.kt，顺序与名字照搬）。</summary> |
| `HexDebug/Core/Splicing/SplicingTableData.cs` | 剪接台槽位里的物品能不能读写 iota（核心、法术书……）。由游戏侧实现。</summary> |
| `HexDebug/Core/Splicing/SplicingTableState.cs` | 剪接台方块实体里与游戏无关的那部分（上游 blocks/splicing/SplicingTableBlockEntity.kt 的 |
| `HexDebug/Game/CircleDebugging.cs` | 调试法术环（上游 debugger/circles/CircleDebugEnv.kt + MixinBlockEntityAbstractImpetus / MixinCircleExecutionState / MixinBlockSlate）： |
| `HexDebug/Game/HexDebugAddon.cs` | HexDebug 附属的入口：调试杖逐步执行咒术、剪接台编辑咒术（object-Object，MIT）。 |
| `HexDebug/Game/HexDebugClient.cs` | 客户端这边的调试状态：每个线程最新的样子（调试面板画它）、输出记录、运行杖的画布。 |
| `HexDebug/Game/HexDebugItems.cs` | 贴图（上游 jar 里的 16×16 放大两倍，_tools/gen_hexdebug_art.py 生成）。</summary> |
| `HexDebug/Game/HexDebugNet.cs` | HexDebug 的联机消息。调试在服务端（单机就是本地）跑，客户端只发请求、收调试面板要显示的东西。 |
| `HexDebug/Game/HexDebugOptions.cs` | HexDebug 的服务端配置项，挂在「附属兼容」页的 HexDebug 开关下面（上游 HexDebugServerConfig，默认值照搬）。 |
| `HexDebug/Game/HexDebugPanel.cs` | 游戏内调试面板（偏差：上游把这些交给外部编辑器 VS Code 通过 DAP 显示）。拿着调试杖 / 运行杖、或运行杖画布开着时显示： |
| `HexDebug/Game/HexDebugSessions.cs` | 玩家用调试杖的调试来历（上游 core api SimplePlayerBasedDebugEnv）：跑完不接着跑；重启 = 用同一串 iota 重新开始。 |
| `HexDebug/Game/HexDebugText.cs` | HexDebug 的文字（上游 lang 文件的官方译文；调试面板的标签是移植版自己的，跟随游戏语言）。</summary> |
| `HexDebug/Game/HexDebugView.cs` | 一个调试线程此刻的样子，服务端算好发给本人客户端的游戏内调试面板。 |
| `HexDebug/Game/Splicing/FocusHolder.cs` | 核心框架（上游 blocks/focusholder/）：在世界里放一个 iota 载体（核心、法术书……）。 |
| `HexDebug/Game/Splicing/SplicingPatterns.cs` | 剪接台的 16 个图案（上游 casting/actions/splicing/*）：从咒术里读写剪接台的选区、视野、剪贴板、 |
| `HexDebug/Game/Splicing/SplicingTable.cs` | 剪接台 / 制念台（上游 blocks/splicing/SplicingTableBlock.kt）：1×1，右键打开编辑界面。</summary> |
| `HexDebug/Game/Splicing/SplicingTableCastEnv.cs` | 制念台施放融注咒术时的施法环境（上游 casting/eval/SplicingTableCastEnv.kt）：施法者还是按下按钮的玩家， |
| `HexDebug/Game/Splicing/SplicingTableNet.cs` | 剪接台的请求（上游 MsgSplicingTableActionC2S / SelectIndexC2S / NewStaffPatternC2S、制念台的施法按钮； |
| `HexDebug/Game/Splicing/SplicingTableUI.cs` | 剪接台的界面（上游 gui/splicing/SplicingTableScreen.kt）：像箱子一样，打开时背包也开着，用泰拉的物品格子放东西。 |
| `HexDebug/Interop/HexParseCommentRenderer.cs` | 联动（HexParse 开着才会有注释 iota）：剪接台格子里画 HexParse 的注释（上游 hexparse compat/hexdebug/CommentRenderer.kt）—— |
| `HexParse/Core/CodeCutter.cs` | 分词：把一段代码切成符号（上游 parsers/CodeCutter.kt，逐行照搬）。 |
| `HexParse/Core/CodeParser.cs` | 代码 -> iota 列表（上游 parsers/ParserMain.java 的 ParseCode + str2nbt/* 全部符号解析器 + macro/MacroProcessor.java）。 |
| `HexParse/Core/CommentIota.cs` | 注释 iota（上游 hooks/CommentIota.java + CommentIotaType.java）。 |
| `HexParse/Core/DotHexPattern.cs` | <c>.hexpattern</c> 格式 -> HexParse 代码（上游 parsers/hexpattern/DotHexPatternMapper.kt + TriePrefixMap.kt）。 |
| `HexParse/Core/FallbackBinary.cs` | <c>nbt_…</c>：没有文本写法的 iota 整个编码进代码（上游 parsers/FallbackBinaryParser.kt）。 |
| `HexParse/Core/HexParseBook.Generated.cs` | 本文件由 _tools/gen_addon_book.py 生成，**不要手改**：改生成器，然后重新跑脚本。 |
| `HexParse/Core/HexParsePatterns.cs` | HexParse 的 8 个图案（上游 actions/HexParsePatterns.java）：形状、官方中文名，以及不碰游戏世界的那几个行为。 |
| `HexParse/Core/HexParseSettings.cs` | HexParse 的配置项（上游 config/HexParseConfig.java + fabric/HexParseConfigFabric.java 的默认值）。 |
| `HexParse/Core/IHexParseHost.cs` | 消息的样式（上游用聊天颜色区分）。</summary> |
| `HexParse/Core/IotaWriter.cs` | iota -> 代码（上游 ParserMain.ParseIotaNbt + nbt2str/* + misc/StringProcessors.java + parsers/meta/MetaHolder.java）。 |
| `HexParse/Core/NestedDisplay.cs` | 上游 mixin/iota/* + mixin_interface/NestedCounter：显示 iota 时 |
| `HexParse/Core/NumEvaluator.cs` | 数字 -> 数字之精思的笔画（上游 misc/NumEvaluatorBrute.java，逐行照搬），以及数字的最短文本写法。 |
| `HexParse/Core/PatternNames.cs` | 图案名 -> 图案（上游 hooks/PatternMapper.java）。 |
| `HexParse/Core/StringEscaper.cs` | 注释字符串的转义 / 反转义（上游 misc/StringEscaper.kt，逐行照搬）。</summary> |
| `HexParse/Game/HexParseActions.cs` | HexParse 里要碰游戏的三个图案（上游 actions/ActionCode2Focus.kt、ActionFocus2Code.kt、ActionLearnGreatPatterns.kt）。 |
| `HexParse/Game/HexParseAddon.cs` | HexParse 附属的入口：代码文本与 iota 列表互转、/hexParse 指令（YukkuriC，MIT）。 |
| `HexParse/Game/HexParseCommand.cs` | 指令 <c>/hexParse</c>（上游 hooks/HexParseCommands.java + commands/*）。子指令照搬上游： |
| `HexParse/Game/HexParseHost.cs` | 解析器在游戏里的宿主（<see cref="IHexParseHost"/>）：施法者是谁、大法术解锁表、宏、实体、消息往哪发。 |
| `HexParse/Game/HexParseIO.cs` | 剪贴板读进来之后干什么（上游 network/ClipboardMsgMode.java）。</summary> |
| `HexParse/Game/HexParseMacros.cs` | 宏与别名（上游 macro/MacroClient.java + MacroManager.java）：存在**客户端**，所有世界通用。 |
| `HexParse/Game/HexParseNet.cs` | HexParse 的联机消息（走本体的「附属消息」，见 HexAddon.GetPacket）。上游 network/*： |
| `HexParse/Game/HexParseOptions.cs` | HexParse 的配置项，挂在服务端「附属兼容」页的 HexParse 开关下面（上游 fabric/config/HexParseConfigFabric.java，默认值照搬）。 |
| `HexParse/Game/HexParseWorld.cs` | HexParse 存在世界里的东西： |

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
