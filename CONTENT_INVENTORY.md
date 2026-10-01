# 咒法学（Hex Casting）完整内容清单

> 用途：供泰拉瑞亚 tModLoader 移植开发逐条照做。
> 数据来源（全部为本地只读文件，每条结论均可追溯）：
> - 源码：`D:\DeepSeekHarness\hexsrc`（FallingColors/HexMod v0.11.4，commit `6b64165be3`，MIT）
> - 解包产物：`D:\DeepSeekHarness\hexwork\jar`
> - 已提取图案：`D:\DeepSeekHarness\tmod\PATTERN_CATALOG.json`（188 条）
>
> 统计口径：内部 ID 以 `HexItems.java` / `HexBlocks.java` 的注册调用为准；
> 显示名以 `assets/hexcasting/lang/en_us.json` 与 `zh_cn.json` 为准；
> 实现类由注册调用的 `new Xxx(` 解析得出，并已与源文件一一对应。

---

## 1. 汇总统计

| 类别 | 数量 |
|---|---|
| **物品（Item）合计** | **70** |
| 　法杖 | 14 |
| 　媒质材料与容器 | 4 |
| 　卷轴 | 3 |
| 　iota 存储 | 5 |
| 　封装法术（Cypher/Trinket/Artifact） | 4 |
| 　颜料（含 16 染料 + 16 骄傲 + 3 特殊） | 35 |
| 　工具与杂项 | 5 |
| **方块（Block）合计** | **58** |
| 　其中带物品形态（BlockItem） | 57 |
| 　其中无物品形态 | 1（`slate`） |
| 　法术环部件 | 8 |
| 　阿卡夏系统 | 3 |
| 　启迪木建材 | 22 |
| 　板岩装饰 | 9 |
| 　装饰 | 14 |
| 　法术生成 | 2 |
| **咒术图案** | **188**（见 `PATTERN_CATALOG.json`） |
| 其中带 `per_world_pattern` 标签（世界专属） | 15 |
| **多方块结构类型** | **1 类**（法术环，由 8 种部件自由组合） |
| **brainsweep（洗脑）配方** | 8 |
| **战利品表** | 49 |
| **配方文件** | 96（主配方目录）+ 14 法杖 + 11 切石 + 8 切制 + 8 洗脑 + 3 淬灵分解 等 |

计数溯源：
- 物品 70 = `HexItems.java` 中 38 处 `make("<id>", ...)` + 循环生成的 16 个 `dye_colorizer_*` + 16 个 `pride_colorizer_*`
  （`en_us.json` 中 `item.hexcasting.*` 共 82 键，减去 14 个 tooltip/子键即为 68；第 69、70 个物品 `slate` 借用方块语言键）
- 方块 58 = `HexBlocks.java` 中 57 处 `blockItem(...)` + 1 处 `blockNoItem("slate", ...)`
- 颜料 35 = 3（`default_colorizer` / `ancient_colorizer` / `uuid_colorizer`，直接注册）+ 16 染料 + 16 骄傲

---

## 2. 物品机制总述（继承体系）

物品机制集中在 `common/items/**`，继承链如下（**这是移植时最该先复刻的部分**）：

```
Item
├── ItemMediaHolder            (magic/ItemMediaHolder.java)   媒质容器基类
│   └── ItemPackagedHex        (magic/ItemPackagedHex.java)   存"图案序列"并可施放
│       ├── ItemCypher         (magic/ItemCypher.java)        一次性
│       ├── ItemTrinket        (magic/ItemTrinket.java)       可重复充能
│       └── ItemArtifact       (magic/ItemArtifact.java)      可用背包媒质
│   └── ItemMediaBattery       (magic/ItemMediaBattery.java)  纯电池
├── Item (直接)
│   ├── ItemStaff              (ItemStaff.java)               打开绘制界面
│   ├── ItemLens               (ItemLens.java)                头部饰品，提供缩放/透视
│   ├── ItemJewelerHammer      (ItemJewelerHammer.java)       可破坏特殊方块
│   ├── ItemLoreFragment       (ItemLoreFragment.java)        给随机未获得剧情
│   └── ItemCreativeUnlocker   (magic/ItemCreativeUnlocker.java) 创造模式全解锁+无限媒质
├── IotaHolderItem（接口，存储单个 iota）
│   ├── ItemAbacus             (storage/ItemAbacus.java)
│   ├── ItemThoughtKnot        (storage/ItemThoughtKnot.java)
│   ├── ItemFocus              (storage/ItemFocus.java)
│   ├── ItemSpellbook          (storage/ItemSpellbook.java)
│   ├── ItemScroll             (storage/ItemScroll.java)
│   └── ItemSlate extends BlockItem (storage/ItemSlate.java)
└── PigmentItem（接口，提供施法颜色）
    ├── ItemDyePigment / ItemPridePigment / ItemAmethystPigment
    └── ItemAmethystAndCopperPigment / ItemUUIDPigment
```

### 2.1 媒质容器：`ItemMediaHolder`

源文件：`common/items/magic/ItemMediaHolder.java`

- NBT 键：`hexcasting:media`（当前）、`hexcasting:start_media`（上限）
- 覆写 `isBarVisible` / `getBarColor` / `getBarWidth` → 物品栏显示**媒质条**（颜色与宽度由 `MediaHelper.mediaBarColor/Width` 计算）
- `canBeDepleted() = false`（媒质物品永不"用坏"，除 Cypher 逻辑外）
- Tooltip：`hexcasting.tooltip.media_amount.advanced`，显示「当前 / 上限 / 百分比」，颜色 `0xb38ef3`（咒法学紫）
- 主色常量：`HEX_COLOR = 0xb38ef3`

### 2.2 封装法术：`ItemPackagedHex`

源文件：`common/items/magic/ItemPackagedHex.java`

- NBT 键：`patterns`（iota 列表）、`pigment`（冻结颜料）
- `hasHex` / `getHex` / `writeHex` / `clearHex`（子类 `ItemAncientCypher` 额外加 `hex_name`，用于显示预设法术名）
- **右键行为**：若有图案 → 构造 `PackagedItemCastEnv` + `CastingVM.empty(env)` → `queueExecuteAndWrapIotas` 执行
- 执行后：施加冷却；若 `breakAfterDepletion()` 且媒质为 0 → **物品消耗掉 1 个**
- 成功时喷 `ParticleSpray`（半径 0.4、角度 π/3、30 个粒子）
- 使用动画：`UseAnim.BLOCK`

三个子类的差异：

| 类 | `canDrawMediaFromInventory` | `breakAfterDepletion` | 冷却来源 |
|---|---|---|---|
| `ItemCypher` | false | **true**（一次性） | `HexConfig.cypherCooldown` |
| `ItemTrinket` | false | false | `HexConfig.trinketCooldown` |
| `ItemArtifact` | **true** | false | `HexConfig.artifactCooldown` |

三者都实现 `VariantItem`，变体数 = `ItemFocus.NUM_VARIANTS` = **8**。

### 2.3 电池：`ItemMediaBattery`

源文件：`common/items/magic/ItemMediaBattery.java`

- `canProvideMedia = true`、`canRecharge = true`（唯一能双向的容器）
- 无固定容量上限：容量由 NBT 决定。创造模式提供 5 档预设：
  | 预设 | 单个容量 |
  |---|---|
  | `BATTERY_DUST_STACK` | `DUST_UNIT × 64` = 640,000 |
  | `BATTERY_SHARD_STACK` | `SHARD_UNIT × 64` = 3,200,000 |
  | `BATTERY_CRYSTAL_STACK` | `CRYSTAL_UNIT × 64` = 6,400,000 |
  | `BATTERY_QUENCHED_SHARD_STACK` | `QUENCHED_SHARD_UNIT × 64` = 19,200,000 |
  | `BATTERY_QUENCHED_BLOCK_STACK` | `QUENCHED_BLOCK_UNIT × 64` = 76,800,000 |

### 2.4 法杖：`ItemStaff`

源文件：`common/items/ItemStaff.java`（14 个变体共用同一个类）

- **右键**：服务端构造 `StaffCastEnv` + VM，把「已保存图案 + 栈描述」通过 `MsgOpenSpellGuiS2C` 发给客户端打开绘制界面
- **潜行 + 右键**：`clearCastingData` + 广播 `MsgClearSpiralPatternsS2C` 清空
- 施法者有 `HexAttributes.FEEBLE_MIND > 0` 时**返回 fail，无法使用**
- 全部 14 种材质**功能完全一致**，只有贴图与稀有度差异（`quenched`/`mindsplice` 为 UNCOMMON）

### 2.5 iota 存储类

| 类 | 源文件 | 机制 |
|---|---|---|
| `ItemAbacus` | `storage/ItemAbacus.java` | 存一个 `DoubleIota`（NBT `value`）。**只读**：`writeable=false`。右键输出数值到聊天 |
| `ItemThoughtKnot` | `storage/ItemThoughtKnot.java` | 存一个任意 iota（NBT `data`）。写入后 `writeable` 变 false（一次性） |
| `ItemFocus` | `storage/ItemFocus.java` | 存一个 iota（NBT `data`）+ `sealed` 布尔。**封印**后不可再写。`NUM_VARIANTS=8`。封印状态改变显示名（`.sealed` 后缀） |
| `ItemSpellbook` | `storage/ItemSpellbook.java` | 多页存储：`pages`（列表）、`page_idx`（1 起）、`page_names`、`sealed_pages`。**`MAX_PAGES = 64`**。可逐页封印/翻页。`NUM_VARIANTS=8` |
| `ItemScroll` | `storage/ItemScroll.java` | 存一个图案。`blockSize` 决定贴墙显示尺寸（`scroll_small`=1、`scroll_medium`=2、`scroll`=3）。右键贴墙生成 `EntityWallScroll`。NBT：`op_id`（世界专属图案）、`pattern`、`recalc_warning`、`needs_purchase` |
| `ItemSlate` | `storage/ItemSlate.java` | `extends BlockItem`，放置为 `BlockSlate`。图案存在**方块实体**里（`BlockEntitySlate.TAG_PATTERN`），因此 `getPattern(stack)` 要读方块实体 |

### 2.6 颜料（Pigment）

- 接口 `PigmentItem#provideColor(stack, owner)` 返回 `ColorProvider`
- `ItemDyePigment`：16 种 MC 染料色
- `ItemPridePigment`：16 种骄傲旗配色，每种是**颜色数组**用于渐变（定义见 `ItemPridePigment.Type`）
- `ItemAmethystPigment`（`default_colorizer`）：紫水晶色
- `ItemAmethystAndCopperPigment`（`ancient_colorizer`）：多色数组，`ADPigment.morphBetweenColors(COLORS, Vec3(0.1,0.1,0.1), time/600, position)` —— **随时间与位置流动的渐变**
- `ItemUUIDPigment`（`uuid_colorizer`）：按 `owner` 的 UUID 生成稳定颜色（每个玩家不同）

### 2.7 工具与杂项

| 物品 | 源文件 | 机制 |
|---|---|---|
| `lens` | `ItemLens.java` | 可装备到**头部/主手/副手**。提供 `GRID_ZOOM = 0.33`（乘法）与 `SCRY_SIGHT = 1.0`（加法）两个属性修饰符。是 `HexBaubleItem`（饰品） |
| `jeweler_hammer` | `ItemJewelerHammer.java` | `extends PickaxeItem`（铁级伤害、钻石级耐久）。`shouldFailToBreak(player, state, pos)` 静态方法判定哪些方块不能被它破坏 |
| `sub_sandwich` | 无独立类（`new Item`） | 食物：`nutrition(14)`、`saturationMod(1.2)` |
| `lore_fragment` | `ItemLoreFragment.java` | 右键消耗。从 8 个剧情成就中**随机挑一个未获得的**授予；全部获得则给 20 经验并提示 `lore_fragment.all` |
| `creative_unlocker` | `magic/ItemCreativeUnlocker.java` | 创造模式解锁器。**无限媒质**、`isFoil` 发光、可食用（nutrition 20）。是 `DebugUnlockerHolder` 的实现方 |

### 2.8 媒质材料

| 物品 | 媒质值 | 说明 |
|---|---|---|
| `amethyst_dust`（紫水晶粉） | `DUST_UNIT = 10,000` | 基础单位 |
| `charged_amethyst`（充能紫水晶） | `CRYSTAL_UNIT = 100,000` | 单件提供 10 粉 |
| `quenched_allay_shard`（淬灵晶屑） | `QUENCHED_SHARD_UNIT = 300,000` | 单件 30 粉，UNCOMMON |
| `battery`（媒质之瓶） | 由 NBT 决定 | 见 2.3 |

---

## 3. 物品全清单（70）
### 3.1 法杖（14）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `staff/acacia` | Acacia Staff | 金合欢木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/bamboo` | Bamboo Staff | 竹法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/birch` | Birch Staff | 白桦木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/cherry` | Cherry Staff | 樱花木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/crimson` | Crimson Staff | 绯红木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/dark_oak` | Dark Oak Staff | 深色橡木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/edified` | Edified Staff | 启迪木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/jungle` | Jungle Staff | 丛林木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/mangrove` | Mangrove Staff | 红树木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/mindsplice` | Mindsplice Staff | 剖念法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/oak` | Oak Staff | 橡木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/quenched` | Quenched Shard Staff | 淬灵晶法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/spruce` | Spruce Staff | 云杉木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |
| `staff/warped` | Warped Staff | 诡异木法杖 | `ItemStaff` | `common\items\ItemStaff.java` |

### 3.2 媒质材料与容器（4）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `amethyst_dust` | Amethyst Dust | 紫水晶粉 | `Item` | `(内联)` |
| `battery` | Phial of Media | 媒质之瓶 | `ItemMediaBattery` | `common\items\magic\ItemMediaBattery.java` |
| `charged_amethyst` | Charged Amethyst | 充能紫水晶 | `Item` | `(内联)` |
| `quenched_allay_shard` | Shard of Quenched Allay | 淬灵晶碎片 | `Item` | `(内联)` |

### 3.3 卷轴（3）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `scroll` | Large Scroll | 大型卷轴 | `ItemScroll` | `common\items\storage\ItemScroll.java` |
| `scroll_medium` | Medium Scroll | 中型卷轴 | `ItemScroll` | `common\items\storage\ItemScroll.java` |
| `scroll_small` | Small Scroll | 小型卷轴 | `ItemScroll` | `common\items\storage\ItemScroll.java` |

### 3.4 iota 存储类（5）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `abacus` | Abacus | 算盘 | `ItemAbacus` | `common\items\storage\ItemAbacus.java` |
| `focus` | Focus | 核心 | `ItemFocus` | `common\items\storage\ItemFocus.java` |
| `slate` | Slate |  | `ItemSlate` | `common\items\storage\ItemSlate.java` |
| `spellbook` | Spellbook | 法术书 | `ItemSpellbook` | `common\items\storage\ItemSpellbook.java` |
| `thought_knot` | Thought-Knot | 结念绳 | `ItemThoughtKnot` | `common\items\storage\ItemThoughtKnot.java` |

### 3.5 封装法术类（4）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `ancient_cypher` | Ancient Cypher | 远古杂件 | `ItemAncientCypher` | `common\items\magic\ItemAncientCypher.java` |
| `artifact` | Artifact | 造物 | `ItemArtifact` | `common\items\magic\ItemArtifact.java` |
| `cypher` | Cypher | 杂件 | `ItemCypher` | `common\items\magic\ItemCypher.java` |
| `trinket` | Trinket | 缀品 | `ItemTrinket` | `common\items\magic\ItemTrinket.java` |

### 3.6 颜料（35）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `ancient_colorizer` | Ancient Pigment | 远古染色剂 | `ItemAmethystAndCopperPigment` | `common\items\pigment\ItemAmethystAndCopperPigment.java` |
| `default_colorizer` | Vacant Pigment | 空无染色剂 | `ItemAmethystPigment` | `common\items\pigment\ItemAmethystPigment.java` |
| `dye_colorizer_black` | Black Pigment | 黑色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_blue` | Blue Pigment | 蓝色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_brown` | Brown Pigment | 棕色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_cyan` | Cyan Pigment | 青色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_gray` | Gray Pigment | 灰色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_green` | Green Pigment | 绿色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_light_blue` | Light Blue Pigment | 淡蓝色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_light_gray` | Light Gray Pigment | 淡灰色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_lime` | Lime Pigment | 黄绿色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_magenta` | Magenta Pigment | 品红色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_orange` | Orange Pigment | 橙色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_pink` | Pink Pigment | 粉红色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_purple` | Purple Pigment | 紫色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_red` | Red Pigment | 红色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_white` | White Pigment | 白色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `dye_colorizer_yellow` | Yellow Pigment | 黄色染色剂 | `ItemDyePigment` | `common\items\pigment\ItemDyePigment.java` |
| `pride_colorizer_agender` | Agender Pigment | 无性别染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_aroace` | Aroace Pigment | 无浪漫倾向无性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_aromantic` | Aromantic Pigment | 无浪漫倾向染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_asexual` | Asexual Pigment | 无性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_bisexual` | Bisexual Pigment | 双性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_demiboy` | Demiboy Pigment | 部分男性染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_demigirl` | Demigirl Pigment | 部分女性染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_gay` | Gay Pigment | 男同性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_genderfluid` | Genderfluid Pigment | 性别流体染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_genderqueer` | Genderqueer Pigment | 性别酷儿染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_intersex` | Intersex Pigment | 双性人染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_lesbian` | Lesbian Pigment | 女同性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_nonbinary` | Non-Binary Pigment | 非二元性别染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_pansexual` | Pansexual Pigment | 泛性恋染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_plural` | Plural Pigment | 多重人格染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `pride_colorizer_transgender` | Transgender Pigment | 跨性别染色剂 | `ItemPridePigment` | `common\items\pigment\ItemPridePigment.java` |
| `uuid_colorizer` | Soulglimmer Pigment | 灵魂闪光染色剂 | `ItemUUIDPigment` | `common\items\pigment\ItemUUIDPigment.java` |

### 3.7 工具与杂项（5）

| 内部 ID | 英文名 | 中文名 | 实现类 | 源文件 |
|---|---|---|---|---|
| `creative_unlocker` | The Media Cube | 媒质立方 | `ItemCreativeUnlocker` | `common\items\magic\ItemCreativeUnlocker.java` |
| `jeweler_hammer` | Jeweler's Hammer | 珠宝匠锤 | `ItemJewelerHammer` | `common\items\ItemJewelerHammer.java` |
| `lens` | Scrying Lens | 探知透镜 | `ItemLens` | `common\items\ItemLens.java` |
| `lore_fragment` | Lore Fragment | 故事残卷 | `ItemLoreFragment` | `common\items\ItemLoreFragment.java` |
| `sub_sandwich` | Submarine Sandwich | 潜艇三明治 | `Item` | `(内联)` |

---

## 4. 方块全清单（58）
### 4.1 法术环部件（8）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `directrix/boolean` | Shepherd Directrix | 牧羊人导向石 | 是 | `BlockBooleanDirectrix` | `common\blocks\circles\directrix\BlockBooleanDirectrix.java` |
| `directrix/empty` | Empty Directrix | 空白导向石 | 是 | `BlockEmptyDirectrix` | `common\blocks\circles\directrix\BlockEmptyDirectrix.java` |
| `directrix/redstone` | Mason Directrix | 石匠导向石 | 是 | `BlockRedstoneDirectrix` | `common\blocks\circles\directrix\BlockRedstoneDirectrix.java` |
| `impetus/empty` | Empty Impetus | 空白促动石 | 是 | `BlockEmptyImpetus` | `common\blocks\circles\BlockEmptyImpetus.java` |
| `impetus/look` | Fletcher Impetus | 制箭师促动石 | 是 | `BlockLookingImpetus` | `common\blocks\circles\impetuses\BlockLookingImpetus.java` |
| `impetus/redstone` | Cleric Impetus | 牧师促动石 | 是 | `BlockRedstoneImpetus` | `common\blocks\circles\impetuses\BlockRedstoneImpetus.java` |
| `impetus/rightclick` | Toolsmith Impetus | 工具匠促动石 | 是 | `BlockRightClickImpetus` | `common\blocks\circles\impetuses\BlockRightClickImpetus.java` |
| `slate` | Slate | 石板 | 否 | `BlockSlate` | `common\blocks\circles\BlockSlate.java` |

### 4.2 阿卡夏系统（3）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `akashic_bookshelf` | Akashic Bookshelf | 阿卡夏书架 | 是 | `BlockAkashicBookshelf` | `common\blocks\akashic\BlockAkashicBookshelf.java` |
| `akashic_connector` | Akashic Ligature | 阿卡夏桥接块 | 是 | `BlockAkashicLigature` | `common\blocks\akashic\BlockAkashicLigature.java` |
| `akashic_record` | Akashic Record | 阿卡夏记录 | 是 | `BlockAkashicRecord` | `common\blocks\akashic\BlockAkashicRecord.java` |

### 4.3 启迪木建材（22）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `amethyst_edified_leaves` | Amethyst Edified Leaves | 晶紫启迪树叶 | 是 | `BlockAkashicLeaves` | `common\blocks\decoration\BlockAkashicLeaves.java` |
| `aventurine_edified_leaves` | Aventurine Edified Leaves | 砂蓝启迪树叶 | 是 | `BlockAkashicLeaves` | `common\blocks\decoration\BlockAkashicLeaves.java` |
| `citrine_edified_leaves` | Citrine Edified Leaves | 晶黄启迪树叶 | 是 | `BlockAkashicLeaves` | `common\blocks\decoration\BlockAkashicLeaves.java` |
| `edified_button` | Edified Button | 启迪木按钮 | 是 | `BlockHexWoodButton` | `common\blocks\decoration\BlockHexWoodButton.java` |
| `edified_door` | Edified Door | 启迪木门 | 是 | `BlockHexDoor` | `common\blocks\decoration\BlockHexDoor.java` |
| `edified_fence` | Edified Fence | 启迪木栅栏 | 是 | `BlockHexFence` | `common\blocks\decoration\BlockHexFence.java` |
| `edified_fence_gate` | Edified Fence Gate | 启迪木栅栏门 | 是 | `BlockHexFenceGate` | `common\blocks\decoration\BlockHexFenceGate.java` |
| `edified_log` | Edified Log | 启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `edified_log_amethyst` | Amethyst Edified Log | 晶紫启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `edified_log_aventurine` | Aventurine Edified Log | 砂蓝启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `edified_log_citrine` | Citrine Edified Log | 晶黄启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `edified_log_purple` | Purple Edified Log | 紫色启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `edified_panel` | Edified Panel | 启迪木块 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `edified_planks` | Edified Planks | 启迪木板 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `edified_pressure_plate` | Edified Pressure Plate | 启迪木压力板 | 是 | `BlockHexPressurePlate` | `common\blocks\decoration\BlockHexPressurePlate.java` |
| `edified_slab` | Edified Slab | 启迪木台阶 | 是 | `BlockHexSlab` | `common\blocks\decoration\BlockHexSlab.java` |
| `edified_stairs` | Edified Stairs | 启迪木楼梯 | 是 | `BlockHexStairs` | `common\blocks\decoration\BlockHexStairs.java` |
| `edified_tile` | Edified Tile | 启迪木方砖 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `edified_trapdoor` | Edified Trapdoor | 启迪木活板门 | 是 | `BlockHexTrapdoor` | `common\blocks\decoration\BlockHexTrapdoor.java` |
| `edified_wood` | Edified Wood | 启迪木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `stripped_edified_log` | Stripped Edified Log | 去皮启迪原木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |
| `stripped_edified_wood` | Stripped Edified Wood | 去皮启迪木 | 是 | `BlockAkashicLog` | `common\blocks\decoration\BlockAkashicLog.java` |

### 4.4 板岩装饰（9）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `slate_amethyst_bricks` | Slate & Amethyst Bricks | 板岩紫晶砖 | 是 | `Block` | `(内联)` |
| `slate_amethyst_bricks_small` | Small Slate & Amethyst Bricks | 板岩紫晶小型砖 | 是 | `Block` | `(内联)` |
| `slate_amethyst_pillar` | Slate & Amethyst Pillar | 板岩紫晶柱 | 是 | `RotatedPillarBlock` | `(内联)` |
| `slate_amethyst_tiles` | Slate & Amethyst Tiles | 板岩紫晶瓦 | 是 | `Block` | `(内联)` |
| `slate_block` | Block of Slate | 板岩块 | 是 | `Block` | `(内联)` |
| `slate_bricks` | Slate Bricks | 板岩砖 | 是 | `Block` | `(内联)` |
| `slate_bricks_small` | Small Slate Bricks | 板岩小型砖 | 是 | `Block` | `(内联)` |
| `slate_pillar` | Slate Pillar | 板岩柱 | 是 | `RotatedPillarBlock` | `(内联)` |
| `slate_tiles` | Slate Tiles | 板岩瓦 | 是 | `Block` | `(内联)` |

### 4.5 装饰（14）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `amethyst_bricks` | Amethyst Bricks | 紫水晶砖 | 是 | `AmethystBlock` | `(内联)` |
| `amethyst_bricks_small` | Small Amethyst Bricks | 紫水晶小型砖 | 是 | `AmethystBlock` | `(内联)` |
| `amethyst_dust_block` | Block of Amethyst Dust | 紫水晶粉块 | 是 | `SandBlock` | `(内联)` |
| `amethyst_pillar` | Amethyst Pillar | 紫水晶柱 | 是 | `BlockAmethystDirectional` | `common\blocks\decoration\BlockAmethystDirectional.java` |
| `amethyst_sconce` | Amethyst Sconce | 紫水晶灯台 | 是 | `BlockSconce` | `common\blocks\decoration\BlockSconce.java` |
| `amethyst_tiles` | Amethyst Tiles | 紫水晶瓦 | 是 | `AmethystBlock` | `(内联)` |
| `ancient_scroll_paper` | Ancient Scroll Paper | 远古纸卷轴 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `ancient_scroll_paper_lantern` | Ancient Paper Lantern | 远古纸灯笼 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `quenched_allay` | Quenched Allay | 淬灵晶块 | 是 | `BlockQuenchedAllay` | `common\blocks\BlockQuenchedAllay.java` |
| `quenched_allay_bricks` | Quenched Allay Bricks | 淬灵晶砖 | 是 | `BlockQuenchedAllay` | `common\blocks\BlockQuenchedAllay.java` |
| `quenched_allay_bricks_small` | Small Quenched Allay Bricks | 淬灵晶小型砖 | 是 | `BlockQuenchedAllay` | `common\blocks\BlockQuenchedAllay.java` |
| `quenched_allay_tiles` | Quenched Allay Tiles | 淬灵晶瓦 | 是 | `BlockQuenchedAllay` | `common\blocks\BlockQuenchedAllay.java` |
| `scroll_paper` | Scroll Paper | 纸卷轴 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |
| `scroll_paper_lantern` | Paper Lantern | 纸灯笼 | 是 | `BlockFlammable` | `common\blocks\BlockFlammable.java` |

### 4.6 法术生成（2）

| 内部 ID | 英文名 | 中文名 | 带物品 | 实现类 | 源文件 |
|---|---|---|---|---|---|
| `conjured_block` | Conjured Block | 构筑的方块 | 是 | `BlockConjured` | `common\blocks\BlockConjured.java` |
| `conjured_light` | Conjured Light | 构筑的光源 | 是 | `BlockConjuredLight` | `common\blocks\BlockConjuredLight.java` |



---

## 5. 方块机制详述

### 5.1 法术环部件（8 种）

源目录：`common/blocks/circles/**`、`api/block/circle/**`、`api/casting/circles/**`

| 部件 | 内部 ID | 实现类 | 作用 |
|---|---|---|---|
| 石板 | `slate` | `BlockSlate` + `BlockEntitySlate` | **法术环的执行单元**。石板上的图案会按顺序被 VM 执行 |
| 空导向石 | `directrix/empty` | `BlockEmptyDirectrix` | 空基座，输出方向**随机**（受微小扰动决定） |
| 石匠导向石 | `directrix/redstone` | `BlockRedstoneDirectrix` | 按**红石信号**选择出口：无信号走"媒质色"侧，有信号走"红石色"侧 |
| 牧羊人导向石 | `directrix/boolean` | `BlockBooleanDirectrix` | 按**栈顶布尔**选择出口：`True` 走后侧、`False` 走前侧；栈空或非布尔 → mishap |
| 空促动石 | `impetus/empty` | `BlockEmptyImpetus` | 空基座。本身不工作，但会改变媒质流的**平面/方向** |
| 工匠促动石 | `impetus/rightclick` | `BlockRightClickImpetus` | **主动力源**。右键启动 |
| 牧师促动石 | `impetus/redstone` | `BlockRedstoneImpetus` | **主动力源**。红石信号启动；可用 `Focus` 之类**绑定玩家** |
| 制箭师促动石 | `impetus/look` | `BlockLookingImpetus` | **主动力源**。被注视一段时间后启动 |

相关的 3 个物品也是法术环专用图案的返回值来源：
`circle/impetus_pos`、`circle/impetus_dir`、`circle/bounds/min`、`circle/bounds/max`
（见 `common/casting/actions/circles/OpCircleBounds.kt`、`OpImpetusDir.kt`、`OpImpetusPos.kt`）

### 5.2 阿卡夏系统（3 种）

| 方块 | 内部 ID | 实现类 | 机制 |
|---|---|---|---|
| 阿卡夏记录 | `akashic_record` | `BlockAkashicRecord` | 图书馆的入口，**自己不存东西、不传导**：`addNewDatum` / `lookupPattern` 从它出发泛洪找书架。对应图案 `akashic/read`、`akashic/write` |
| 阿卡夏桥接块 | `akashic_connector` | `BlockAkashicLigature`（实现 `AkashicFloodfiller`） | 不存东西，只传导，把书架连得更远 |
| 阿卡夏书架 | `akashic_bookshelf` | `BlockAkashicBookshelf`（实现 `AkashicFloodfiller`） | **每格存一条**「图案 → iota」（BlockEntityAkashicBookshelf）；卷轴右键抄键、潜行空手右键清空；有附魔之力与比较器输出 |

`AkashicFloodfiller`（`common/blocks/akashic/AkashicFloodfiller.java`）是"网络扩散"接口，定义哪些方块参与同一个阿卡夏网络。

### 5.3 启迪木建材（22 种）

源目录：`common/blocks/decoration/**`

完整清单（全部带物品形态）：
`edified_log`、`edified_wood`、`stripped_edified_log`、`stripped_edified_wood`、
`edified_planks`、`edified_panel`、`edified_tile`、`edified_slab`、`edified_stairs`、
`edified_fence`、`edified_fence_gate`、`edified_door`、`edified_trapdoor`、
`edified_button`、`edified_pressure_plate`、
`edified_log_purple`、`edified_log_amethyst`、`edified_log_aventurine`、`edified_log_citrine`、
`amethyst_edified_leaves`、`aventurine_edified_leaves`、`citrine_edified_leaves`

实现类：`BlockHexDoor`、`BlockHexFence`、`BlockHexFenceGate`、`BlockHexPressurePlate`、
`BlockHexSlab`、`BlockHexStairs`、`BlockHexTrapdoor`、`BlockHexWoodButton`、
`BlockAkashicLeaves`（三种叶共用）、`BlockAkashicLog`（四种原木共用）、`BlockAxis`、`BlockAmethystDirectional`

### 5.4 板岩装饰（9 种）

`slate_block`、`slate_tiles`、`slate_bricks`、`slate_bricks_small`、`slate_pillar`、
`slate_amethyst_tiles`、`slate_amethyst_bricks`、`slate_amethyst_bricks_small`、`slate_amethyst_pillar`

均为普通装饰方块（无特殊机制），源文件在 `HexBlocks.java` 内联构造。

### 5.5 装饰（14 种）

| 方块 | 内部 ID | 机制 |
|---|---|---|
| 紫水晶砖（多种） | `amethyst_bricks`、`amethyst_bricks_small`、`amethyst_tiles`、`amethyst_pillar` | 装饰 |
| 紫水晶粉块 | `amethyst_dust_block` | 装饰 |
| 紫水晶烛台 | `amethyst_sconce` | `BlockSconce`，可放光源 |
| 卷轴纸 | `scroll_paper` | `BlockFlammable`，可点燃 |
| 卷轴纸灯笼 | `scroll_paper_lantern` | 装饰 + 发光 |
| 古代卷轴纸 | `ancient_scroll_paper` | `BlockFlammable` |
| 古代纸灯笼 | `ancient_scroll_paper_lantern` | 装饰 + 发光 |
| 淬灵灵体 | `quenched_allay` | `BlockQuenchedAllay` + `BlockEntityQuenchedAllay` |
| 淬灵砖（3 种） | `quenched_allay_bricks`、`quenched_allay_bricks_small`、`quenched_allay_tiles` | 装饰 |

### 5.6 法术生成（2 种）

| 方块 | 内部 ID | 实现类 | 机制 |
|---|---|---|---|
| 咒造方块 | `conjured_block` | `BlockConjured` + `BlockEntityConjured` | **由法术生成**，有生命周期，到期消失 |
| 咒造光源 | `conjured_light` | `BlockConjuredLight` | 由法术生成的光源，`lightLevel = 15`，无碰撞、无掉落 |

---

## 6. 多方块结构：法术环（Spell Circle）

这是本模组**唯一的多方块结构类型**，但形状完全自由。

### 6.1 核心接口

源文件：
- `api/block/circle/BlockCircleComponent.java`（基类）
- `api/block/circle/BlockAbstractImpetus.java`（促动石基类）
- `api/casting/circles/ICircleComponent.java`（部件接口）
- `api/casting/circles/CircleExecutionState.java`（环状态与启动）
- `api/casting/circles/BlockEntityAbstractImpetus.java`（促动石方块实体）

`ICircleComponent` 要求实现：

| 方法 | 作用 |
|---|---|
| `acceptControlFlow(imageIn, env, enterDir, pos, bs, world)` | **核心**。就地修改施法环境，返回 `ControlFlow.Continue(update, exits)` 或 `ControlFlow.Stop`。返回的出口方向**必须恰好 1 个可被接受**，否则 mishap |
| `canEnterFromDirection(enterDir, pos, bs, world)` | 能否从该方向进入 |
| `possibleExitDirections(pos, bs, world)` | 静态可达出口集合（启动时判定环是否闭合） |
| `startEnergized` / `isEnergized` / `endEnergized` | 充能状态（对应方块状态 `energized`） |

`BlockCircleComponent` 提供 `ENERGIZED` 布尔状态、`normalDir`（"朝外"方向）、`particleHeight`，
并有红石输出：充能时输出 15。

### 6.2 闭合判定算法（关键）

源文件：`api/casting/circles/CircleExecutionState.java` 第 95–159 行

```
创建：
  起点 = 促动石位置沿 startDirection 偏移一格
  todo 栈 = [(startDirection, 起点)]
  while todo 非空:
      pop (enterDir, herePos)
      hereBs = 扫描该坐标的方块
      若非 ICircleComponent            → 跳过
      若 !canEnterFromDirection        → 跳过
      若该坐标未见过:
          记录 sawGood，更新包围盒（正/负角）
          对 possibleExitDirections 的每个 out:
              push (out, herePos + out)
      若已见集合大小 ≥ maxSpellCircleLength → 返回 Err(null)
  若最后一个方块位置 == 促动石位置          → Err(null)
  若已见集合不包含促动石位置                → Err(lastBlockPos)   // 无法回流
```

失败时对应三条提示（`en_us.json`）：
- `hexcasting.tooltip.circle.no_closure` = "The flow of media will not be able to return to the impetus at %s"
- `hexcasting.tooltip.circle.no_exit` = "The flow of media could not find an exit at %s"
- `hexcasting.tooltip.circle.many_exits` = "The flow of media had too many exits at %s"

### 6.3 运行规则（来自游戏内手册原文）

来自 `assets/hexcasting/patchouli_books/thehexbook/en_us/entries/greatwork/spellcircles.json`：

1. 需要一个**促动石（Impetus）**产生自维持的媒质波；波沿石板等部件传播，执行遇到的每个图案
2. **波必须能回到促动石**，否则立刻失败
3. **任何部件的出口方向必须无歧义**，否则在该方块处因"出口过多"失败
4. 因此环的轮廓可以是**任意闭合形状**（凹/凸皆可），朝向任意方向，甚至可以跨三维
5. 媒质**不从玩家背包或身上扣**——必须用漏斗之类的装置把媒质**喂进促动石**
6. 用**占卜透镜（Scrying Lens）**查看促动石内的媒质，单位是"粉"
7. **环内法术无法影响环的包围盒之外的东西**（包围盒 = 包含全部组成方块的最小长方体；凹形环的凹陷部分仍在盒内）
8. 波能经过的方块数有上限（`maxSpellCircleLength`）
9. 有一类图案**只能从环内施放**（`circle/*` 系列），用 Staff 施放会失败

### 6.4 三种促动石的差异

| 促动石 | 激活条件 | 备注 |
|---|---|---|
| 工匠（rightclick） | 右键 | 最直接 |
| 牧师（redstone） | 红石信号 | **可绑定玩家**：用带玩家引用的物品（如 Focus）右键方块。绑定后该玩家及其周围一小片区域**始终对环可见**，如同站在环内 |
| 制箭师（look） | 被注视一段时间 | — |

### 6.5 三种导向石的差异

| 导向石 | 出口选择 |
|---|---|
| 空（empty） | **随机**（由媒质波与环境的微观扰动决定） |
| 石匠（redstone） | 按红石信号：无信号走"媒质色"侧，有信号走"红石色"侧 |
| 牧羊人（boolean） | 按栈顶布尔：`True` 走后侧、`False` 走前侧；栈空/非布尔 → mishap（`MishapBoolDirectrixEmptyStack` / `MishapBoolDirectrixNotBool`） |

### 6.6 配方

- 空促动石（`recipes/impetus/empty.json`）：`PSS / BAB / SSP`，P=紫珀块、S=板岩块、B=铁栏杆、A=充能紫水晶
- 空导向石（`recipes/directrix/empty.json`）：`CSS / OAO / SSC`，C=比较器、S=板岩块、O=观察者、A=充能紫水晶
- 其余促动石/导向石由 **brainsweep（洗脑）** 配方从空基座 + 村民转化而来

---

## 7. brainsweep（洗脑）系统

源文件：`datagen/recipe/builders/BrainsweepRecipeBuilder.java`，配方在 `data/hexcasting/recipes/brainsweep/`

配方结构（以 `akashic_record.json` 为例）：

```json
{
  "type": "hexcasting:brainsweep",
  "blockIn": { "type": "block", "block": "hexcasting:akashic_connector" },
  "cost": 1000000,
  "entityIn": { "type": "villager", "minLevel": 5, "profession": "librarian" },
  "result": { "name": "hexcasting:akashic_record" }
}
```

即：**在指定方块上消耗指定媒质，把指定条件的村民转化为结果方块**。

8 个洗脑配方：

| 配方文件 | 输入方块 | 村民条件 | 媒质消耗 | 产出 |
|---|---|---|---|---|
| `akashic_record.json` | `akashic_connector` | 图书管理员，minLevel 5 | 1,000,000 | `akashic_record` |
| `impetus_storedplayer.json` | （空促动石） | — | — | `impetus/redstone`（牧师促动石） |
| `impetus_rightclick.json` | （空促动石） | — | — | `impetus/rightclick`（工匠促动石） |
| `impetus_look.json` | （空促动石） | — | — | `impetus/look`（制箭师促动石） |
| `directrix_redstone.json` | （空导向石） | — | — | `directrix/redstone`（石匠导向石） |
| `directrix_boolean.json` | （空导向石） | — | — | `directrix/boolean`（牧羊人导向石） |
| `budding_amethyst.json` | `minecraft:amethyst_block` | minLevel 3 | 1,000,000 | `minecraft:budding_amethyst`（紫水晶母岩） |
| `quench_allay.json` | — | — | — | 淬灵灵体相关 |

**注意**：`cost: 1000000` = 1,000,000 媒质 = **100 粉 = 10 晶体**。

相关 mishap：`MishapBadBrainsweep`、`MishapAlreadyBrainswept`（`api/casting/mishaps/`）

---

## 8. 研究 / 解锁系统

### 8.1 结论：咒法学**没有**线性的"研究解锁"系统

这是移植时最容易搞错的一点。原作的实际机制是：

| 机制 | 实现 | 源文件 |
|---|---|---|
| **图案是否可用** | 靠**成就/标签**，不是研究进度 | `api/casting/iota/PatternIota.java` |
| **"启蒙"（Enlightenment）** | 是一个**成就**，通过**过载施法**获得 | `data/hexcasting/advancements/enlightenment.json` |
| **剧情文本** | 靠成就系统，由 `lore_fragment` 授予 | `common/items/ItemLoreFragment.java` |
| **手册内容** | Patchouli 手册，随成就解锁章节 | `data/hexcasting/patchouli_books/**` |
| **全解锁（调试）** | 创造模式物品直接授予全部成就 | `common/items/magic/ItemCreativeUnlocker.java` |

### 8.2 启蒙（Enlightenment）的真实来源

源文件：`data/hexcasting/advancements/enlightenment.json`

```json
{
  "parent": "hexcasting:opened_eyes",
  "criteria": {
    "health_used": {
      "conditions": {
        "health_used": { "min": 0.8 },
        "mojang_i_am_begging_and_crying_please_add_an_entity_health_criterion": {
          "max": 1.0, "min": 2.2250738585072014E-308
        }
      },
      "trigger": "hexcasting:overcast"
    }
  },
  "display": { "frame": "challenge", "hidden": true, "icon": { "item": "minecraft:music_disc_11" } }
}
```

**含义**：玩家必须**用生命值换取媒质**（"过载施法"），且**单次过载消耗的生命值比例 ≥ 0.8**（即掉 80% 最大生命）且剩余生命 > 0。

过载实现：`api/casting/eval/env/PlayerBasedCastEnv.java` 第 150–200 行

```
extractMediaFromInventory(costLeft, allowOvercast, simulate):
    ...从背包/物品抽媒质...
    if costLeft > 0 && allowOvercast:
        if caster.isInvulnerableTo(OVERCAST 伤害类型): return costLeft   // 无敌者不能过载
        healthToRemove = costLeft / mediaToHealthRate
        Mishap.trulyHurt(caster, OVERCAST, healthToRemove)
        OVERCAST_TRIGGER.trigger(caster, actuallyTaken)
        awardStat(MEDIA_OVERCAST, actuallyTaken)
```

- 换算率：`HexConfig.common().mediaToHealthRate()`
- 专用伤害类型：`hexcasting:overcast`（`common/lib/HexDamageTypes.java`）
- 触发器：`api/advancements/OvercastTrigger.java`
- 统计项：`api/mod/HexStatistics.java` 的 `MEDIA_OVERCAST`

### 8.3 图案的可用性判定

源文件：`api/casting/iota/PatternIota.java` 第 105–120 行

```
reqsEnlightenment = 图案在 requires_enlightenment 标签中
castedName = getActionI18n(key, reqsEnlightenment)
if (reqsEnlightenment && !vm.getEnv().isEnlightened()):
    throw new MishapUnenlightened()
```

- `isEnlightened()`：`api/casting/eval/CastingEnvironment.java` 第 251 行
  ```java
  var adv = this.world.getServer().getAdvancements().getAdvancement(modLoc("enlightenment"));
  return adv != null && player has completed it;
  ```
  **即"启蒙"就是一个成就的完成状态**
- 例外：`CircleCastEnv.isEnlightened()` 第 154 行 —— 法术环**默认视为已启蒙**（"have unbound circles be enlightened"）
- 相关的动作标签（`data/hexcasting/tags/action/`）：
  - `requires_enlightenment.json`：需要启蒙才能用
  - `per_world_pattern.json`（15 条）：世界专属图案
  - `cannot_modify_cost.json`：不能改消耗
  - `can_start_enlighten.json`：可触发启蒙

### 8.4 反噬 / 疯狂的实现

- 媒质不足时的通用 mishap：`MishapNotEnoughMedia`（提示 `hexcasting.message.cant_overcast`）
- 施法失败的副作用集中在 `api/casting/mishaps/**`（27 个具体 `Mishap` 类，见 `INTERFACE_CONTRACT_v1.md`）
- 反噬会调用 `env.mishapEnvironment` 的 `dropHeldItems()` 等，即**掉落手持物**

---

## 9. MC 有而泰拉瑞亚没有、需要重新设计的机制

以下 5 项是移植时**无法直接对应**的，必须重新设计。

### 9.1 过载施法：用生命值换媒质（影响最大）

- **MC 侧**：媒质不足时可选择"过载"，把缺口按 `mediaToHealthRate` 换算成生命值扣除，并且**启蒙成就**（解锁大法术的唯一门槛）就绑定在这个机制上（需单次掉 80% 最大生命）
- **泰拉侧没有对应物**：
  - 泰拉生命值是 100 起步、可到 500+ 且**分段显示**（红心），与 MC 的 20 点制完全不同
  - 泰拉已有"魔力（Mana）"资源，但那是法师武器专用，语义与"用血换媒质"冲突
  - MC 的 `isInvulnerableTo` 语义（无敌帧）在泰拉是 `player.immune` / `immuneTime`，**不能直接照搬**
- **必须重新设计**：建议改成"消耗最大生命上限的一定比例"或"施加一个减益 buff 换取媒质"，并重新定义"启蒙"的达成条件

### 9.2 以"图案"为键的 iota 存储（阿卡夏记录）

- **MC 侧**：`BlockAkashicRecord` 用 `HexPattern` 作为**复合键**（角度序列 + 起始方向）索引 iota，并由 `AkashicFloodfiller` 把多个方块连成网络共享存储
- **泰拉侧没有对应物**：
  - 泰拉没有"以任意复合对象为键的方块存储"概念
  - 泰拉的 ModTile 没有内建的"多方块共享同一存储空间"能力，`AkashicFloodfiller` 的网络扩散要自建（需要自己维护一个网络 id 与合并/拆分逻辑）
  - 泰拉的箱子系统是**按格子索引**的，不是按键索引
- **必须重新设计**：需要自建一个「图案 → iota」的字典结构 + 网络归属追踪，并考虑多人同步

### 9.3 法术环：可闭合的有向图 + 媒质波执行

- **MC 侧**：环是一个**有向图**，媒质波沿 `possibleExitDirections` 流动，逐格执行石板上的图案；出口必须无歧义；必须能回到促动石；包围盒限制作用范围
- **泰拉侧没有对应物**：
  - 泰拉**完全没有多方块结构概念**。最接近的是"有效房屋"（NPC 住房判定）和"晶塔网络"，但两者都是硬编码规则、不可扩展
  - 泰拉没有"方块之间的有向连接"这种数据模型
  - 泰拉的 Tile 系统没有 `Direction enterDir` 这样的"进入方向"语义
- **必须重新设计**：需要自建 `ICircleComponent` 等价物（用 `ModTile` + `ModTileEntity`），并自建一个**独立的图遍历执行器**（不能依赖泰拉的任何现有机制）
- 建议保留原作的判定规则（闭合性、出口唯一性、包围盒限制），因为这三条是这个玩法好玩的核心

### 9.4 图案与 iota 的物品内存储（NBT 复合结构）

- **MC 侧**：`ItemPackagedHex` 把一串 iota 序列化进 `patterns` NBT 列表，`ItemSpellbook` 存 64 页、`ItemFocus` 存单值 + 封印位
- **泰拉侧差异**：
  - 泰拉物品用 `TagCompound`，**结构上可以对应**，但：
    - 泰拉的物品栏堆叠语义不同（`Item.stack`），**带 NBT 的物品不能堆叠**——原作这些物品都是 `unstackable()`，这点一致，可以接受
    - 泰拉**没有原生的"物品子变体"渲染谓词**（MC 的 `ItemProperties` predicate，如 `has_patterns`、`written`、`sealed`、`variant`、`media`）；贴图切换要改用 `ModItem.Texture` + `Hooks` 或在 `PreDraw` 里手动绘制
  - 泰拉有 `Item.Clone()`/存档机制，但**新增 NBT 时必须同时处理多人同步**（`NetSend`/`NetReceive`），MC 的物品 NBT 同步是框架自动做的
- **需要重新设计**：贴图变体系统（原作有 `has_patterns`/`written`/`sealed`/`variant`/`media`/`max_media`/`ancient`/`overlay_layer` 等多种谓词）

### 9.5 brainsweep：把村民转化成方块

- **MC 侧**：`brainsweep` 是自定义配方类型，输入"方块 + 村民（含等级与职业条件）+ 媒质"，输出一个新方块
- **泰拉侧没有对应物**：
  - 泰拉的 **NPC 没有"等级/职业"体系**（有 `NPC.townNPC`、名字、幸福度，但没有 level/profession）
  - 泰拉**没有"配方消耗世界中的实体"这种概念**——合成配方只处理物品
  - 泰拉也没有"把方块转化为另一个方块并消耗媒质"的现成配方类型
- **必须重新设计**：建议改为「玩家对特定 NPC 施法」的交互（例如右键 NPC + 消耗媒质），而不是配方系统；或者改造成一个需要玩家满足条件后手动完成的多步骤仪式

---

## 10. 移植实现优先级建议

| 优先级 | 内容 | 理由 |
|---|---|---|
| P0 | 媒质单位与容器（`MediaConstants` / `IMediaStorage`） | 所有法术与物品的基础 |
| P0 | 图案与 VM（`PATTERN_CATALOG.json` 的 188 条 + `Iota` + `HexStack`） | 核心玩法 |
| P0 | 法杖 + 绘制画布 | 玩家入口 |
| P1 | 14 种法杖、颜料（决定视觉多样性与玩家表达） | 体验 |
| P1 | iota 存储类（卷轴、法术书、Focus、石板、算盘、心结） | 图案的保存与分享 |
| P1 | 阿卡夏系统（档案 + 连线 + 书架） | 中后期自动化 |
| P2 | 法术环（8 种部件 + 图遍历执行器） | 后期玩法，工作量大 |
| P2 | brainsweep 的替代仪式 | 与法术环配套 |
| P3 | 启迪木建材 22 种、板岩与装饰方块 | 纯内容，可批量生成 |
| P3 | 剧情/手册系统 | 需要替代 Patchouli |

---

## 附录 A：数据来源文件清单

| 用途 | 路径 |
|---|---|
| 物品注册 | `hexsrc\Common\src\main\java\at\petrak\hexcasting\common\lib\HexItems.java` |
| 方块注册 | `hexsrc\...\common\lib\HexBlocks.java` |
| 英文/中文显示名 | `hexwork\jar\assets\hexcasting\lang\en_us.json`、`zh_cn.json` |
| 图案注册 | `hexsrc\...\common\lib\hex\HexActions.java` |
| 图案清单（已提取） | `tmod\PATTERN_CATALOG.json` |
| 物品实现 | `hexsrc\...\common\items\**` |
| 方块实现 | `hexsrc\...\common\blocks\**` |
| 法术环接口 | `hexsrc\...\api\casting\circles\**`、`api\block\circle\**` |
| 过载实现 | `hexsrc\...\api\casting\eval\env\PlayerBasedCastEnv.java` |
| 启蒙成就 | `hexwork\jar\data\hexcasting\advancements\enlightenment.json` |
| 洗脑配方 | `hexwork\jar\data\hexcasting\recipes\brainsweep\**` |
| 手册原文 | `hexwork\jar\assets\hexcasting\patchouli_books\thehexbook\en_us\entries\**` |
