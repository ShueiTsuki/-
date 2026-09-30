# 内容阶段表（自动生成）

<!-- 本文件由 _tools/gen_progression.ps1 生成，不要手改。
     阶段写在 _tools/progression_stages.json；合成站与材料是从代码里读出来的。 -->

这张表的用途：**做进度流程**。它把「这个东西该在什么时候出现」和
「它实际上怎么做出来」放在同一行，两者对不上就会在生成期红掉。

阶段的依据来自源项目的真实门槛，见 `_tools/progression_stages.json` 顶部的说明。

共 **122** 个可合成物品。

## 肉前（104 项）

紫水晶 → 媒质 → 石板 → 法杖 → 工具与存储。源项目在这一段没有任何进度门槛。

| 物品类 | 产出 | 材料 | 合成站 | 依据 |
|---|---|---|---|---|
| `AmethystDust` | 10 | Amethyst×1 | WorkBenches | 源 ItemJewelerHammer：珠宝匠锤敲碎紫水晶（无手工配方）→ 泰拉：紫晶研磨 |
| `AmethystShard` | 1 | AmethystDust×5 | WorkBenches | 源 MC 原生水晶碎片（晶洞掉落）→ 泰拉：由粉压制（保底，等晶洞世界生成补齐） |
| `AmethystShard` | 5 | AmethystShard×1 | WorkBenches | 源 MC 原生水晶碎片（晶洞掉落）→ 泰拉：由粉压制（保底，等晶洞世界生成补齐） |
| `ChargedAmethyst` | 1 | AmethystDust×10 | WorkBenches | 源：晶洞里的紫水晶簇小概率掉落 → 泰拉：粉 ×10 或 紫晶 + 坠落之星 |
| `ChargedAmethyst` | 1 | Amethyst×1 + FallenStar×1 | WorkBenches | 源：晶洞里的紫水晶簇小概率掉落 → 泰拉：粉 ×10 或 紫晶 + 坠落之星 |
| `HexSlateItem` | 6 | AmethystDust×1 + StoneBlock×3 | WorkBenches | 源 238-243 "A"/"SSS"：粉1 + 深板岩3 → 6；泰拉深板岩对应物 = 石块 |
| `SlateBlockItem` | 8 | AmethystDust×1 + StoneBlock×8 | WorkBenches | 源 281 ringAll(深板岩, 粉)：8 深板岩 + 1 粉 → 8 板岩块 |
| `SlateTilesItem` | 4 | SlateBricksItem×4 | WorkBenches | 源 605-643 stoneSet：瓦 ← 砖 4:4（另加切石机 1:1 走重型工作台） |
| `SlateTilesItem` | 1 | 〔HexRecipeGroups.SlateBlocks〕×1 | HeavyWorkBench | 源 605-643 stoneSet：瓦 ← 砖 4:4（另加切石机 1:1 走重型工作台） |
| `SlateBricksItem` | 4 | SlateBlockItem×4 | WorkBenches | 源 605-643 stoneSet：砖 ← 基底块 4:4，且砖 ← 小砖 1:1（回炉） |
| `SlateBricksItem` | 1 | SlateBricksSmallItem×1 | WorkBenches | 源 605-643 stoneSet：砖 ← 基底块 4:4，且砖 ← 小砖 1:1（回炉） |
| `SlateBricksItem` | 1 | 〔HexRecipeGroups.SlateBlocks〕×1 | HeavyWorkBench | 源 605-643 stoneSet：砖 ← 基底块 4:4，且砖 ← 小砖 1:1（回炉） |
| `SlateBricksSmallItem` | 1 | SlateBricksItem×1 | WorkBenches | 源 605-643 stoneSet：小砖 ← 砖 1:1 |
| `SlateBricksSmallItem` | 1 | 〔HexRecipeGroups.SlateBlocks〕×1 | HeavyWorkBench | 源 605-643 stoneSet：小砖 ← 砖 1:1 |
| `SlatePillarItem` | 2 | SlateBlockItem×2 | WorkBenches | 源 605-643 stoneSet：柱 ← 基底块 2:2 |
| `SlatePillarItem` | 1 | 〔HexRecipeGroups.SlateBlocks〕×1 | HeavyWorkBench | 源 605-643 stoneSet：柱 ← 基底块 2:2 |
| `AmethystDustBlockItem` | 1 | AmethystDust×4 | WorkBenches | 源 284 packing：4 粉 ↔ 1 粉块（比例是 4:1，不是 MC 常见的 9:1） |
| `AmethystDustBlockItem` | 4 | AmethystDustBlockItem×1 | WorkBenches | 源 284 packing：4 粉 ↔ 1 粉块（比例是 4:1，不是 MC 常见的 9:1） |
| `AmethystTilesItem` | 4 | AmethystBricksItem×4 | WorkBenches | 源 432 stoneSet（基底块是 MC 的紫水晶块）→ 泰拉基底块 = 紫水晶粉块 |
| `AmethystTilesItem` | 1 | 〔HexRecipeGroups.AmethystBlocks〕×1 | HeavyWorkBench | 源 432 stoneSet（基底块是 MC 的紫水晶块）→ 泰拉基底块 = 紫水晶粉块 |
| `AmethystBricksItem` | 4 | AmethystDustBlockItem×4 | WorkBenches | 源 432 stoneSet |
| `AmethystBricksItem` | 1 | AmethystBricksSmallItem×1 | WorkBenches | 源 432 stoneSet |
| `AmethystBricksItem` | 1 | 〔HexRecipeGroups.AmethystBlocks〕×1 | HeavyWorkBench | 源 432 stoneSet |
| `AmethystBricksSmallItem` | 1 | AmethystBricksItem×1 | WorkBenches | 源 432 stoneSet |
| `AmethystBricksSmallItem` | 1 | 〔HexRecipeGroups.AmethystBlocks〕×1 | HeavyWorkBench | 源 432 stoneSet |
| `AmethystPillarItem` | 2 | AmethystDustBlockItem×2 | WorkBenches | 源 432 stoneSet |
| `AmethystPillarItem` | 1 | 〔HexRecipeGroups.AmethystBlocks〕×1 | HeavyWorkBench | 源 432 stoneSet |
| `SlateAmethystTilesItem` | 2 | SlateTilesItem×1 + AmethystTilesItem×1 | WorkBenches | 源 441-459：板岩瓦 + 紫晶瓦 → 2（shapeless 混料，四条并列） |
| `SlateAmethystBricksItem` | 2 | SlateBricksItem×1 + AmethystBricksItem×1 | WorkBenches | 源 441-459 混料 |
| `SlateAmethystBricksSmallItem` | 2 | SlateBricksSmallItem×1 + AmethystBricksSmallItem×1 | WorkBenches | 源 441-459 混料 |
| `SlateAmethystPillarItem` | 2 | SlatePillarItem×1 + AmethystPillarItem×1 | WorkBenches | 源 441-459 混料 |
| `WoodStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 576-586：木棍 ×3 + 对应木板 ×1 + 充能紫水晶 ×1（14 把法杖共用这一个形状） |
| `OakStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（橡木 = 泰拉木材） |
| `BorealStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（云杉 = 北地木） |
| `PalmStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（竹子 = 棕榈木） |
| `MahoganyStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（丛林木 = 红木） |
| `EbonwoodStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（深色橡木 = 乌木） |
| `ShadewoodStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（绯红木 = 阴影木） |
| `DynastyStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（金合欢 = 王朝木，旅商处可买，肉前可得） |
| `AshStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 同 WoodStaff（诡异木 = 灰烬木，地狱可得，肉前） |
| `EdifiedLogItem` | 1 | 〔RecipeGroups.Wood〕×1 | WorkBenches | 源：启迪树苗法术长出的阿卡夏树产出（**无合成配方**）→ 泰拉保底：任意木材 1:1 |
| `EdifiedLogAmethystItem` | 1 | EdifiedLogItem×1 + Amethyst×1 | WorkBenches | 同上；彩色变体在源项目里来自不同树种，泰拉用宝石定色 |
| `EdifiedLogAventurineItem` | 1 | EdifiedLogItem×1 + Emerald×1 | WorkBenches | 同上（翡翠 ≈ 东陵玉） |
| `EdifiedLogCitrineItem` | 1 | EdifiedLogItem×1 + Topaz×1 | WorkBenches | 同上（黄玉 ≈ 黄晶） |
| `EdifiedLogPurpleItem` | 1 | EdifiedLogItem×1 + AmethystDust×1 | WorkBenches | 同上（紫水晶粉） |
| `StrippedEdifiedLogItem` | 1 | 〔HexRecipeGroups.EdifiedLogs〕×1 | WorkBenches | 源 HexStrippables：5 种启迪原木都去皮成同一种（配方组输入） |
| `EdifiedWoodItem` | 3 | EdifiedLogItem×4 | WorkBenches | 源 319-323：启迪木 ×3 ← 启迪原木 ×4 |
| `StrippedEdifiedWoodItem` | 3 | StrippedEdifiedLogItem×4 | WorkBenches | 源 319-323：去皮启迪木 ×3 ← 去皮启迪原木 ×4 |
| `EdifiedPlanksItem` | 4 | 〔HexRecipeGroups.EdifiedLogs〕×1 | WorkBenches | 源 312-314：启迪木板 ×4 ← 1 个任意启迪原木（tag） |
| `EdifiedPanelItem` | 9 | EdifiedPlanksItem×9 | WorkBenches | 源 326-331：面板 ×9 ← 启迪木板 ×9 |
| `EdifiedTileItem` | 6 | EdifiedPlanksItem×6 | WorkBenches | 源 333-338：瓷砖 ×6 ← 启迪木板 ×6 |
| `AmethystEdifiedLeavesItem` | 4 | EdifiedPlanksItem×1 + Amethyst×1 | WorkBenches | 源：树叶由 Edify 转化（无配方）→ 保底：启迪木板定形 + 宝石定色 |
| `AventurineEdifiedLeavesItem` | 4 | EdifiedPlanksItem×1 + Emerald×1 | WorkBenches | 同上 |
| `CitrineEdifiedLeavesItem` | 4 | EdifiedPlanksItem×1 + Topaz×1 | WorkBenches | 同上 |
| `EdifiedStaff` | 1 | EdifiedPlanksItem×1 + ChargedAmethyst×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 89 staffRecipe(MINDSPLICE…) 一族：这一把的「W」槽是 EDIFIED_PLANKS |
| `JewelerHammer` | 1 | AmethystShard×1 + 〔RecipeGroups.IronBar〕×1 + 〔RecipeGroups.Wood〕×2 | Anvils | 源 245-253：铁锭 + 铁粒 + 紫水晶碎片 + 木棍 ×2 |
| `ScryingLens` | 1 | AmethystDust×1 + Glass×4 | WorkBenches | 源 153 ringCornerless：玻璃 ×4 + 粉 ×1 |
| `Focus` | 1 | ChargedAmethyst×1 + FallenStar×4 + Silk×4 | WorkBenches | 源 98-117：萤石粉 ×4 + 皮革 ×2 + 纸 ×2 + 充能紫水晶 ×1 |
| `ThoughtKnot` | 1 | AmethystDust×1 + Silk×1 | Loom | 源 93-97：粉 ×1 + 线 ×1（徒手；泰拉用织布机） |
| `ThoughtKnot` | 1 | Hive×1 | WorkBenches | 源 93-97：粉 ×1 + 线 ×1（徒手；泰拉用织布机） |
| `Abacus` | 1 | Amethyst×2 + 〔RecipeGroups.Wood〕×6 | WorkBenches | 源 156-163 "WAW"/"SAS"/"WAW"：木板 ×4 + 紫水晶碎片 ×2 + 木棍 ×2 |
| `Cypher` | 1 | AmethystDust×1 + CopperBar×4 | WorkBenches | 源 131-135 ringCornerless：铜锭 ×4 + 粉 ×1 |
| `Trinket` | 1 | AmethystShard×1 + 〔RecipeGroups.IronBar〕×4 | Anvils | 源 137-141 ringCornerless：铁锭 ×4 + 紫水晶碎片 ×1 |
| `Artifact` | 1 | ChargedAmethyst×1 + GoldBar×4 + Diamond×1 | Anvils | 源 143-151：金锭 ×4 + 充能紫水晶 ×1 + 唱片 ×1（唱片 → 泰拉用钻石代替） |
| `ScrollSmall` | 1 | AmethystDust×1 + Silk×1 | WorkBenches | 源 215-220：纸 ×1 + 粉 ×1 |
| `ScrollMedium` | 1 | AmethystDust×1 + Silk×4 | WorkBenches | 源 222-228：纸 ×4 + 粉 ×1 |
| `ScrollLarge` | 1 | AmethystDust×1 + Silk×8 | WorkBenches | 源 230-236：纸 ×8 + 粉 ×1 |
| `ScrollPaperItem` | 4 | Silk×1 | WorkBenches | 源 287 ringAll(纸, 紫水晶碎片)：8 纸 + 1 碎片 → 8 卷轴纸 |
| `AncientScrollPaperItem` | 4 | ScrollPaperItem×4 + AmethystDust×1 | WorkBenches | 源 290-293：棕色染料 + 卷轴纸 ×8 → 8 |
| `ScrollPaperLanternItem` | 2 | ScrollPaperItem×2 + Torch×1 | WorkBenches | 源 295-296 stack(卷轴纸, 火把) |
| `AncientScrollPaperLanternItem` | 2 | AncientScrollPaperItem×2 + Torch×1 | WorkBenches | 源 298-305：古卷轴纸 + 火把 / 棕色染料染色 |
| `AmethystSconceItem` | 1 | Amethyst×2 + Torch×1 | WorkBenches | 源 307-310 stack：充能紫水晶 + 铜锭 → 4 |
| `WallScrollFrameSmall` | 2 | Wood×6 + Silk×2 | WorkBenches | 源项目无对应物：泰拉侧为「可放置的壁挂卷轴」补的框，纯肉前木工活 |
| `WallScrollFrameMedium` | 2 | Wood×12 + Silk×4 | WorkBenches | 同上 |
| `WallScrollFrameLarge` | 2 | Wood×20 + Silk×8 | WorkBenches | 同上 |
| `HexBookItem` | 1 | Wood×10 + Hay×10 | Bookcases | 源项目没有这本书：泰拉侧的引导书，让玩家能查到图案怎么画 |
| `DevStaff` | 1 | Wood×20 | WorkBenches | 开发者工具（泰拉自创，仅用于测试） |
| `PigmentItem` | 1 | AmethystShard×1 | WorkBenches | 颜料（染色剂）基类：源 HexplatRecipes 176-213，全部挂 has_item(紫水晶粉)，无门槛 → 肉前。材料见 AUDIT_VS_ORIGINAL.md「颜料」 |
| `PigmentDyeWhite` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeOrange` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeMagenta` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeLightBlue` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeYellow` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeLime` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyePink` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeGray` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeLightGray` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeCyan` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyePurple` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeBlue` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeBrown` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeGreen` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeRed` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentDyeBlack` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 同色染料 |
| `PigmentPrideAgender` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideAroace` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideAromantic` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideAsexual` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideBisexual` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideDemiboy` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideDemigirl` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideGay` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideGenderfluid` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideGenderqueer` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideIntersex` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideLesbian` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideNonbinary` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPridePansexual` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPridePlural` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentPrideTransgender` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 一件泰拉常见物（全年可得） |
| `PigmentDefault` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 紫水晶碎片 |
| `PigmentAncient` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×4 + 铜锭 |
| `PigmentSoulglimmer` | 1 | AmethystShard×1 | WorkBenches | 同 PigmentItem：紫水晶粉 ×8 + 紫水晶碎片 |
| `SubSandwich` | 1 | — | WorkBenches | 源 165-174 潜艇三明治：has_item(紫水晶碎片)，无门槛 → 肉前；熟牛肉 → 烤松鼠、面包 → 干草 |
| `EdifiedDoorItem` | 3 | EdifiedPlanksItem×6 | WorkBenches | 源 edified_door：启迪木板 ×6 → 3（木门配方，无门槛）→ 肉前 |
| `EdifiedFenceItem` | 4 | — | WorkBenches | 源 edified_fence：启迪木板 → 泰拉栅栏墙（1 → 4，泰拉木栅栏的比例） |
| `EdifiedButtonItem` | 1 | — | WorkBenches | 源 edified_button：启迪木板 ×1 → 1 |
| `EdifiedPressurePlateItem` | 1 | EdifiedPlanksItem×2 | WorkBenches | 源 edified_pressure_plate：启迪木板 ×2 → 1 |

## 肉后（9 项）

淬灵系（泰拉对应物是神圣地妖精）、法术书（源项目要末地合唱果）、珍珠木法杖。

| 物品类 | 产出 | 材料 | 合成站 | 依据 |
|---|---|---|---|---|
| `QuenchedAllayItem` | 1 | QuenchedAllayShard×4 | WorkBenches | 淬灵块：原版只能脑叶切除（紫水晶块 + 悦灵）得到，敲掉掉 2~4 片碎片、精准采集才掉方块。泰拉没有精准采集：4 碎片合一块代替。碎片本身没有配方（脑叶切除小精灵 Pixie —— 神圣地肉后敌怪，用户定的进度 —— 要启蒙） |
| `QuenchedAllayTilesItem` | 4 | QuenchedAllayBricksItem×4 | WorkBenches | 源 433 stoneSet（这一族没有柱） |
| `QuenchedAllayTilesItem` | 1 | 〔HexRecipeGroups.QuenchedAllayBlocks〕×1 | HeavyWorkBench | 源 433 stoneSet（这一族没有柱） |
| `QuenchedAllayBricksItem` | 4 | QuenchedAllayItem×4 | WorkBenches | 源 433 stoneSet |
| `QuenchedAllayBricksItem` | 1 | QuenchedAllayBricksSmallItem×1 | WorkBenches | 源 433 stoneSet |
| `QuenchedAllayBricksItem` | 1 | 〔HexRecipeGroups.QuenchedAllayBlocks〕×1 | HeavyWorkBench | 源 433 stoneSet |
| `QuenchedAllayBricksSmallItem` | 1 | QuenchedAllayBricksItem×1 | WorkBenches | 源 433 stoneSet |
| `QuenchedAllayBricksSmallItem` | 1 | 〔HexRecipeGroups.QuenchedAllayBlocks〕×1 | HeavyWorkBench | 源 433 stoneSet |
| `QuenchedStaff` | 1 | QuenchedAllayShard×1 + ChargedAmethyst×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 90 staffRecipe：这一把的「W」槽是 QUENCHED_SHARD 本身 |
| `Spellbook` | 1 | ChargedAmethyst×2 + Book×1 + GoldBar×1 + CrystalShard×5 | Bookcases | 源 119-129：要求合唱果（末地特产）。泰拉无末地档，取中间阶段肉后；对应物 = 水晶碎块 |
| `CherryStaff` | 1 | ChargedAmethyst×1 + Pearlwood×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 87 staffRecipe(樱花木)。泰拉没有樱花木，用珍珠木代替 —— 而珍珠木只长在肉后的神圣地，所以实际门槛是肉后 |
| `SpookyStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 86 staffRecipe(红树)。泰拉用**阴森木**代替 —— 阴森木只在肉后的南瓜月掉落，门槛自带 |
| `PearlwoodStaff` | 1 | ChargedAmethyst×1 + {WoodType}×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 82 staffRecipe(白桦)。泰拉用**珍珠木**代替 —— 珍珠木只长在肉后的神圣地，门槛自带 |

## 肉后 · 启蒙（9 项）

源项目挂「启蒙」门槛的那一族：原动力、导线、阿卡夏三件、剖念法杖。启蒙 = 一次过载用掉 ≥80% 生命、只剩不到半颗心（按原版实现）；泰拉侧再要求肉后的秘银砧/山铜砧。

| 物品类 | 产出 | 材料 | 合成站 | 依据 |
|---|---|---|---|---|
| `HexImpetusEmptyItem` | 1 | SlateBlockItem×4 + ChargedAmethyst×1 + CrystalShard×2 + IronFence×2 | MythrilAnvil + 已启蒙 | 源 390-398：配方挂 enlightenment 门槛 → 泰拉：秘银砧 + 已启蒙；紫珀块（末地）→ 水晶碎块（同法术书） |
| `HexDirectrixEmptyItem` | 1 | SlateBlockItem×4 + ChargedAmethyst×1 + Wire×20 | MythrilAnvil + 已启蒙 | 源 400-408：同样挂 enlightenment → 秘银砧 + 已启蒙；比较器/观察者 → 电线（泰拉红石本体） |
| `HexDirectrixItemBase` | 1 | HexDirectrixEmptyItem×1 | MythrilAnvil + 已启蒙 | 布尔/红石导线：源项目是 brainsweep(空导线 + 村民)，启蒙大战法术之一 |
| `HexDirectrixBooleanItem` | 1 | HexDirectrixEmptyItem×1 | MythrilAnvil + 已启蒙 | 同 HexDirectrixItemBase（配方继承自基类的 RegisterRecipe） |
| `HexDirectrixRedstoneItem` | 1 | HexDirectrixEmptyItem×1 | MythrilAnvil + 已启蒙 | 同 HexDirectrixItemBase |
| `AkashicBookshelfItem` | 1 | EdifiedPlanksItem×2 + Book×3 + 〔HexRecipeGroups.EdifiedLogs〕×4 | MythrilAnvil + 已启蒙 | 源 410-417：启迪原木 + 启迪木板 + 书，挂 enlightenment → 秘银砧 + 已启蒙 |
| `AkashicLigatureItem` | 4 | EdifiedPlanksItem×2 + AmethystDust×1 + AmethystShard×1 + ChargedAmethyst×1 + 〔HexRecipeGroups.EdifiedLogs〕×4 | MythrilAnvil + 已启蒙 | 源 419-428：启迪原木 + 启迪木板 + 三种紫水晶料 ×4，挂 enlightenment → 秘银砧 + 已启蒙 |
| `AkashicRecordItem` | 1 | AkashicLigatureItem×1 + Book×3 | MythrilAnvil + 已启蒙 | 源 497-501 brainsweep(阿卡夏系带 + 图书管理员)：启蒙大战法术 → 秘银砧 + 已启蒙 |
| `MindspliceStaff` | 1 | HexDirectrixRedstoneItem×1 + ChargedAmethyst×1 + 〔RecipeGroups.Wood〕×3 | WorkBenches | 源 91：这一把的「W」槽是 MINDFLAYED_CIRCLE_COMPONENTS，即脑叶切除产物 |

## 与源项目的阶段差异（有意为之）

| 源项目阶段 | 泰拉阶段 | 为什么 |
|---|---|---|
| 序幕（无门槛） | 肉前 | 紫水晶在泰拉一开局就能挖到 |
| 启蒙（濒死过载施法） | 肉后 · 启蒙 | 启蒙按原版实现（Core/Media/Overcast.cs）：配方条件「已启蒙」，再叠加肉后的秘银砧。曾经定在月后，是因为当时启蒙拿不到 |
| 末地（合唱果 → 法术书） | 肉后 | 泰拉没有末地，取中间阶段；对应物用肉后的水晶碎块 |
| 脑叶切除（启蒙大战法术） | 肉后 | 泰拉侧的对应物是**神圣地妖精**，只在肉后出现 —— 门槛由材料自带 |
| 启迪树苗 → 阿卡夏树 | 肉前 | 源项目里 edify **不在**启蒙名单里，是普通法术；泰拉暂用保底配方 |

