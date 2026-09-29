# 书本 UI 改造设计（照原版 Patchouli，再优化）

> 用户原话：「书本的 UI 丑爆了，抄原版的，或者抄完优化一下变成更好看（禁止土气）」。

---

## 1. 原版的 `thehexbook` 到底是什么（已核过源码）

路径：`hexsrc\Common\src\main\resources\assets\hexcasting\patchouli_books\thehexbook\en_us\`

**它是 Patchouli 手册，不是图案清单。** 我们的书目前是「188 图案列表」，两者不是同一个东西 ——
这是"丑"的第一层原因：信息结构就不对。

### 分类（7 个）

`categories/{basics,casting,greatwork,interop,items,lore,patterns}.json`，每个只有四个字段：

```json
{ "name": "hexcasting.category.items",
  "icon": "hexcasting:focus",          // 分类格上画的是**一个物品图标**
  "description": "hexcasting.category.items.desc",
  "sortnum": 2 }
```

→ 所以原版**没有分类美术资源**，落地页就是「3 列物品图标格 + 标题 + 一行描述」。

### 条目（82 个）

`entries/<分类>/<名字>.json`：

```json
{ "name": "hexcasting.entry.jeweler_hammer",
  "category": "hexcasting:items",
  "icon": "hexcasting:jeweler_hammer",
  "sortnum": 9, "advancement": "hexcasting:root",
  "pages": [ { "type": "patchouli:text",     "text": "…" },
             { "type": "patchouli:crafting", "recipe": "hexcasting:jeweler_hammer", "text": "…" } ] }
```

条目数：basics 4 · casting 7 · greatwork 8 · interop 2 · **items 18** · lore 8 · **patterns 18（另有 great_spells 8 / spells 9）**。

### 页面类型

- `patchouli:text` —— 正文（可带 `title`）
- `patchouli:crafting` —— 配方 + 说明（**我们要的合成表展示**）
- 自定组件 `at.petrak.hexcasting.interop.patchouli.ManualPatternComponent` —— **把图案画在页面上**
- 模板 `templates/{pattern, manual_pattern, manual_pattern_nosig, crafting_multi, brainsweep}.json`
  用 `header` / `separator` / `custom` / `text` 组件拼版

**注意：参考源码里没有 `book.json`，也没有任何书本贴图**（Patchouli 的贴图走资源包）。
所以「抄原版」只能抄**结构与交互**，美术必须我们自己画 —— 这反而好：泰拉是像素风，
硬贴 MC 的 GUI 贴图会非常突兀。

---

## 2. 目标：三个视图

| 视图 | 内容 | 交互 |
|---|---|---|
| **封面** | 皮革封面 + 压印书名 + 「按 E 打开」 | 点击任意处 → 分类网格 |
| **分类网格** | 7 格，每格 = 物品图标(32×32) + 名称 + 一行描述，3 列 | 点格 → 条目列表；Esc → 关书 |
| **条目双页** | 左页：标题 + 正文；右页：下一页正文 / 配方 / 图案绘制 | ← → 翻页，Esc 返回，底部页码点 |

条目列表不单独做一屏 —— 原版也是「分类页列出条目缩略」，直接用同一套双页排版更省事也更好看。

---

## 3. 禁止土气的硬规则（这是本次的核心要求）

泰拉模组书最容易土的地方是「大面积高饱和色块 + 粗黑描边 + 居中大字」。逐条钉死：

1. **不用纯黑描边。** 一律用半透明深棕 `rgba(46,42,36,180)`，1px。
2. **边框双线**：外 1px 深棕 + 内 1px 浅棕，中间留 1px 空隙。**禁止** ≥2px 的粗边。
3. **色块饱和度上限 60%**。铺满区域的色一律降饱和（和建材那条规则同源）。
4. **内容区留白 ≥8px，不许贴边**；双页中缝 ≥8px。
5. **分隔线**用 1px 渐变（两端透明），不用实心粗线。
6. **选中态**：整行淡紫底 `rgba(110,90,158,60)` + 左侧 2px 亮条 `#A98FD9`。**禁止反白**。
7. **禁止居中大标题**：标题左对齐，字号只比正文大一档，字距 +1px。
8. **圆角 2px 切角**，不用大圆角。
9. 纸面加**极轻**的噪点（±6 明度、稀疏），不要明显的"做旧"污渍。
10. 所有尺寸**整数缩放**，非整数缩放会让像素字发糊 —— 宁可容器大一点。

### 调色板

| 用途 | 值 |
|---|---|
| 纸面 | `#E8DCC0` / 明暗两档 `#DCCDA8` |
| 纸边 | `#C4B08A` |
| 皮革 | `#6B4A2F` / 暗 `#4E3520` |
| 压印金 | `#C9A227`（低饱和，不发光） |
| 正文墨 | `#2E2A24`；次级 `#6A6152` |
| 咒法学紫（强调） | `#6E5A9E` / 亮 `#A98FD9` |

---

## 4. 尺寸（按 UI 缩放 1.0 设计，渲染时整体整数缩放）

```
书体      520 × 300
封面内框  496 × 276（留 12px 皮边）
双页      每页 240 宽 × 268 高，中缝 8
页边距    16
正文行高  16，行宽 ≤ 208
分类格    152 × 76，3 列 × 3 行（7 格）
页码点    4×4，间距 6，居中于底部
翻页箭头  16×16，左右各一，距底 12
```

---

## 5. 数据来源（不新造轮子）

| 需要 | 现成的 |
|---|---|
| 分类与条目结构 | **照抄原版 7 分类 / 82 条目**（`name`/`icon`/`sortnum`/`pages`），正文先留占位，逐步填 |
| 分类图标 | 原版 `icon` 指向物品（如 `hexcasting:focus`）→ 映射到我们的 `Focus` 等物品 |
| 图案绘制 | `patterns_all.json` + `Core/Casting/Math/HexGrid.PatternLinePoints`（已有） |
| 配方展示 | 从代码扫出来的配方（`_tools/gen_progression.ps1` 已经在做这件事）→ 加一个 JSON 导出即可 |
| 图案中文名 | `_tools/pattern_names_zh.json`（已有） |

---

## 6. 实现拆分

1. `Core/Ui/BookView.cs`（新）—— 视图状态机（`Cover / Categories / Spread`）+ 导航，**纯逻辑、无 XNA**，可离线测
2. `Core/Ui/BookLayout.cs`（扩）—— `CategoryCell` / `Spread(left,right)` / `PageArrow` / `PageDots` / `HitTest`
3. `Core/Ui/BookModel.cs`（新）—— 分类 / 条目 / 页 的数据类型 + JSON 加载
4. `Client/UI/HexBook.cs`（重写渲染）—— 纸/皮革/双线边框/淡紫选中条，全部按第 3 节规则
5. `_tools/gen_book_art.ps1`（新）—— 生成纸面、皮革、箭头、页码点（16/32 像素风、低饱和）
6. `_tools/patdraw_Program.cs` 扩 —— **离屏把三个视图各渲染成 PNG**

### 第 6 条是关键

「禁止土气」这种要求，靠读代码判断不了。**先把它渲成图，我自己看，不满意就改，改到满意再交给你在游戏里实测。**
这样你在游戏里看到的第一版就已经是筛过的，不会浪费你的验证时间。

---

## 7. 完成判据

- [ ] 三个视图可用键鼠完成导航（← → 翻页、Esc 逐级返回、点分类格/条目）
- [ ] 版面在 UI 缩放 1.0 / 1.5 / 2.0 下都清晰（整数缩放，无半像素模糊）
- [ ] 第 3 节 10 条硬规则逐条在离屏渲图里核对通过
- [ ] 离线断言覆盖视图状态机（进入/返回/翻页边界/命中测试不重叠）
- [ ] 我出过一张「三视图总览图」，你过目后再进游戏

---

## 8. 顺带要修的（已确认，别忘）

`Keybinds` 本地化段**中文这边缩进错了一层**：`en-US.hjson` 在 2 缩进（正确层级
`Mods` > `HexCastingTerraria` > `Keybinds`），`zh-Hans.hjson` 的在 3 缩进，被塞进了 `Tiles` 里。
后果：**中文玩家的按键绑定界面显示原始键名**。修的时候顺便把断言⑦ 扩成
「`Items` / `Tiles` / `Keybinds` 三段键集合中英必须相等」。

---

## 9. 调研结论：tModLoader 没有 Patchouli 的对应物（2026-09-14）

按用户定的规矩「先看有没有现成的」，查过一轮：

### 没有「数据驱动的图鉴框架」这类模组

所有叫 "Guidebook API / 图鉴 API" 的东西都在 **Minecraft** 那边，tModLoader 侧一个都没有：

- [Lavender](https://github.com/wisp-forest/lavender) —— 自述就是 "A Guidebook API and alternative to Patchouli"，**Fabric/MC**
- [GuidebookAPI](https://github.com/realleoxian/GuidebookAPI) —— **MC**
- [Guidebook Library](https://forum.luanti.org/viewtopic.php?f=11&hilit=books&t=23569) —— **Luanti（原 Minetest）**
- 官方 [tModLoader Useful Resources](https://github-wiki-see.page/m/tmodloader/tmodloader/wiki/Useful-Resources)
  整页是工具链与教程，**没有任何 UI / 手册类库**

### 最接近的三个，但都不适合当依赖

| 模组 | 像在哪 | 为什么不能当依赖 |
|---|---|---|
| [BossChecklist](https://github.com/JavidPack/BossChecklist) | 有「注册 API + 分类 + 条目 + 进度勾选」的完整模型 | 它是**Boss 进度清单**，不是书；为了手册让玩家多装一个模组，对一个移植模组来说是实打实的劝退 |
| Recipe Browser | 数据浏览 UI 做得好，是这方面最好的先例 | 只浏览配方，没有页面/正文/图案绘制的概念 |
| [SerousCommonLib](https://catalogue.smods.ru/archives/219289) | 一堆 UI 与 tML 工具函数 | 是通用工具库，不提供书；可以**读它的做法**，不必依赖 |

### 那用什么

**tModLoader 自带的 UI 框架**就够，[UIState](http://docs.tmodloader.net/docs/preview/class_u_i_state.html) /
[UI Framework](https://deepwiki.com/tModLoader/tModLoader/4.1-ui-framework) 提供了滚动、悬停、点击这些底盘。
BossChecklist 和 Recipe Browser 也都是直接在 `UIState` 上写的 —— 这就是泰拉这边的"正统做法"，
不存在需要外部库的空档。另有
[Terraria Interface for Dummies](https://forums.terraria.org/index.php?threads/terraria-interface-for-dummies.79356/)
这份教程可参考。

### 结论与做法

1. **自己写**，但**不写"框架"**：我们的书只服务自己那 82 个条目，
   现在做一套「别的模组可以注册进来」的公共 API 是过度设计。
2. 底盘用 tModLoader 自带的 `UIState` / `UIElement`，**不重复造轮子**。
3. 只把「**内容**」做成数据驱动（hjson，字段照抄 Patchouli 的
   `name` / `icon` / `sortnum` / `pages[]` / `type`），理由有三：
   82 个条目写死在 C# 里是灾难；和原版的作者工作流一致；本地化天然干净。
4. **布局与视图状态机留在 `Core/`**（无 XNA），这样能离线测、也能离屏渲图。
5. 将来若真要拆成独立附属模组：因为第 3、4 条已经把**内容层 / 布局层 / 渲染层**分开了，
   拆出去是机械操作，不需要现在付代价。

---

## 10. Patchouli 源码已取到本地 + 许可证结论（2026-09-14）

参考源：`D:\DeepSeekHarness\patchouli`（`git clone --depth 1 https://github.com/VazkiiMods/Patchouli.git`）。

### 许可证：两边都查清了，结论是「可以搬」

| 来源 | 协议 | 对我们的约束 |
|---|---|---|
| **咒法学**（HexMod） | **MIT** | 保留版权声明与许可文本即可；本身允许商用/闭源 |
| **Patchouli** | **CC BY-NC-SA 3.0** | **BY** 署名 · **NC** 禁商用 · **SA** 相同方式共享 |

咒法学的 MIT 是从本地 `fabric.mod.json` 的 `"license": "MIT"` 与
[Modrinth 页面](https://modrinth.com/mod/hex-casting)（Licensed MIT）两处确认的
（`hexsrc` 里**没有 LICENSE 文件**，所以是从这两处反查的）。

合并后的结论：

- MIT 部分**可以**放进 CC BY-NC-SA 的作品里（MIT 宽松，允许再许可），但要**保留 MIT 声明**
- 一旦引入 Patchouli 的代码或贴图，**整个作品必须 CC BY-NC-SA 3.0**
- 用户已明确「**不盈利、会开源**」→ NC 与 SA 两条都满足 → **可以直接移植**

### 必须交付的东西（合规四件套）

1. `LICENSE` —— 整包 CC BY-NC-SA 3.0
2. `CREDITS.md` / `NOTICE` —— 署名 **Vazkii / Patchouli**（CC BY-NC-SA 3.0）
   与 **FallingColors / HexMod**（MIT，附 MIT 全文 + 版权行）
3. `description.txt` 与 Workshop 页面同样标注
4. **不商用**：免费发布、无广告、无付费墙

### 要如实知道的代价

- **SA 会传染且不可逆**：以后想把模组改成 MIT 或闭源，只要有 Patchouli 的代码在就**不行**。
  唯一出路是把 Patchouli 派生部分**全部重写干净**（clean-room）。这是现在就要接受的取舍。
- CC 协议**不是为软件设计的**（没有专利授权条款，也不区分源码/二进制）。
  在软件上用它有争议，但 MC 模组圈普遍这么用，实务上没出过事。

### 于是做法调整为「可直接移植」（贴图分开决定）

- **代码**：可逐处移植；Java + Minecraft API → C# + tModLoader 仍需改写，但**算法与常量可以照搬**
  （尤其排版、换行、翻页溢出、书签布局这些自己推很费劲的部分）
- **贴图**：`book_*.png` **可以**直接用（署名 + 同协议）。而且这里正好有个技术理由让两套皮肤各取所需：

| 皮肤 | 贴图从哪来 | 为什么 |
|---|---|---|
| **帕秋莉版** | **直接用 Patchouli 原图集**（512×256），整数倍缩放渲染 | 要的就是原味；合法；零美术成本 |
| **原版版** | 泰拉自带的 UI 贴图（面板 / 按钮 / 滚动条） | 要的是泰拉本土观感，两者的像素密度与配色都不同 |

> ⚠️ 但**两套共用同一套几何**是不行的 —— MC 的 GUI 在 GUI scale 2~3 下画，
> 泰拉 UI 默认 1x，像素密度不是一个量级。所以 `BookSkin` 契约里 `Measure` 必须各自实现，
> 不能只换贴图。

---

## 12. 移植清单（已按行数量好，下一轮照这个开工）

源目录：`D:\DeepSeekHarness\patchouli\Xplat\src\main\java\vazkii\patchouli\`

按**移植价值**排序（行数是实测）：

| 行为数 | 文件 | 作用 | 我们落到哪 |
|---:|---|---|---|
| 218 | `client/book/text/TextLayouter.java` | **文字换行 + 分页溢出** —— 自己推最费劲的一支 | `Core/Ui/BookText.cs` |
| 358 | `client/book/text/BookTextParser.java` | 排版标记解析（`$(l)`/`$(br)`/颜色/宏） | 同上 |
| 124 | `client/book/text/SpanState.java` | 富文本状态（粗斜体/颜色/链接） | 同上 |
| 70 | `client/book/text/Word.java` | 排版单位 | 同上 |
| 478 | `client/book/gui/GuiBook.java` | 书主体：翻页动画、书签、页码、命中 | `Core/Ui/BookView.cs` + `BookSkin` |
| 207 | `client/book/gui/GuiBookLanding.java` | 落地页：分类网格 | 分类网格布局 |
| 279 | `client/book/gui/GuiBookEntry.java` | 条目双页视图 | 双页布局 |
| 196 | `client/book/gui/GuiBookEntryList.java` | 条目列表 | 条目列表布局 |
| 82 | `client/book/gui/BookTextRenderer.java` | 文本绘制 | `BookSkin.DrawText` |
| 264 | `client/book/BookEntry.java` | 条目数据模型 | `Core/Ui/BookModel.cs` |
| 158 | `client/book/BookCategory.java` | 分类模型 | 同上 |
| 110 | `client/book/BookContents.java` | 内容容器 | 同上 |
| 230 | `common/book/Book.java` | 书的元数据 | 同上 |
| 168 | `client/book/BookContentsBuilder.java` | 内容装配 | 加载器 |
| 144 | `client/book/template/BookTemplate.java` | **模板**（原版 5 个模板用这套拼版） | 页面组件系统 |
| 77 | `client/book/template/TemplateComponent.java` | 模板组件基类 | 同上 |

**约 2700 行 Java**，但不需要全搬：

- `page/PageMultiblock`(230) / `PageEntity`(107) / `PageQuest`(74) —— **不搬**，
  这些是 MC 特有的（多方块结构预览、实体渲染、进度任务），泰拉侧没有对应物
- `template/VariableAssigner`(166) / `TemplateInclusion`(110) —— 先不搬，
  那是给"模板套模板"的高级功能，我们 82 个条目用不上
- `BookContentResourceListenerLoader` / `BookContentResourceDirectLoader` / `ClientBookRegistry`
  —— 换成从 hjson 加载，不搬

### 建议的移植顺序（每步都能单独验证）

1. **数据模型**（BookEntry / BookCategory / BookContents / Book）→ `Core/Ui/BookModel.cs`，写 hjson 加载
2. **文本引擎**（TextLayouter / BookTextParser / SpanState / Word）→ `Core/Ui/BookText.cs`
   —— 这一步能**纯离线测**（给一段带标记的文本，断言它被切成几页、每页几行）
3. **视图状态机**（GuiBook 的导航与翻页部分）→ `Core/Ui/BookView.cs`，无 XNA，可离线测
4. **两套 Skin** → `Client/UI/`，只负责量尺寸与画
5. 离屏出图，我自己看图定稿

第 2 步的离线断言是整件事的关键：**排版正确性不看图也能测**，
而它恰恰是最容易出"字被切掉""最后一页溢出"这类问题的地方。

---

## 13. 进度（2026-09-14）

> 测试数字不写在这里 —— 按断言③ 的规定，会过期的数字只许出现在 `STATUS.generated.md`。

| 步 | 状态 | 产物 |
|---|---|---|
| 1 数据模型 + 内容骨架 | ✅ 完成 | `Core/Ui/BookModel.cs` · `Core/Ui/BookContent.Generated.cs` · `_tools/gen_book_content.ps1` |
| 2 排版引擎 | ✅ 完成 | `Core/Ui/BookText.cs`（+ 离线断言已接入 drawtest） |
| 3 视图状态机 | ✅ 完成 | `Core/Ui/BookView.cs`（+ 离线断言已接入 drawtest） |
| 4 两套 Skin | 🔶 契约已完成 | `Core/Ui/BookSkin.cs`（`IBookCanvas` / `BookMetrics` / `BookSkin` / `Color32` / `BookHit`）<br>`PatchouliSkin` / `VanillaSkin` 待写 |
| 5 离屏出图 | ⬜ 未开工 | 见下面「第 5 步怎么做」 |

---

### 第 5 步（离屏出图）的具体做法

**目的**：让两套皮肤在**不启动游戏**的情况下渲成 PNG，这样才能「画 → 看图 → 改」地迭代，
否则「禁止土气」这条要求只能靠用户在游戏里反复试，那是最费他时间的做法。

### 放哪、怎么建

放在 `drawtest`（它已经用 `<Compile Include="…\Core\Ui\**\*.cs" />` 直接引用 Core 源码，
新增一个文件即可参与编译，不需要改构建脚本）。

### 两个技术选择（都已踩过点，避免走弯路）

1. **PNG 编码不要引第三方包**。`System.Drawing.Common` 从 .NET 7 起不再随框架分发，
   它是 Windows-only 且需要 NuGet 包，离线环境下 restore 不一定成功。
   改用 .NET 自带的 `System.IO.Compression.ZLibStream`（net6+）+ 自己写 PNG 的
   IHDR/IDAT/IEND 与 CRC32 —— 约 80 行，零依赖。生成物是无过滤的 RGBA8，标准 PNG。
   （本项目的 PowerShell 工具一直在用 `System.Drawing`，那是 Windows PowerShell 5.1
   自带的 .NET Framework，和 net10.0 的 drawtest 不是一回事，别混。）

2. **字体是唯一的难点**。泰拉用的是它自己的像素字体（`FontAssets.MouseText`），
   离屏环境里没有。两个办法：
   - **先用替代方案把版面跑通**：`DrawText` 先画「按字符宽度等比撑开的灰块」，
     用来核**版面**（行宽、行数、分页、对齐、留白、热区）—— 这些恰恰是「土不土」的大头
   - **再补真字**：把泰拉的位图字体烘成一张位图 + 一张字符宽度表（脚本生成，
     与 `_tools/gen_*.ps1` 的既有做法一致），离屏渲染就能画出与游戏里**同形**的字
   两步分开的好处：版面问题先解决，字体是纯替换，不会互相阻塞。

#### 完成判据

- [x] 一条命令产出 PNG（`DRAWTEST_RENDER=1 dotnet run` → `_tools/book_probe.png`）
- [x] 不带第三方依赖（自带 PNG 编码器，CRC32 手写 + `ZLibStream`）
- [ ] 两套皮肤各出一套（共 6 张），能并排比对
- [ ] 版面断言：分类格/条目行/页码点**互不重叠、不越界**（两套皮肤各测一遍）

### 字体怎么解决（下一步的唯一入口）

`drawtest/BookRender.cs` 里的 `DrawText` 现在画的是**按字符宽度撑开的占位方块** ——
版面（行宽、行数、分页、对齐、留白、热区）已经能核了，但看不出真正的观感。
要把真字补上，有两条路：

| 方案 | 做法 | 评价 |
|---|---|---|
| **A. 烘一份字体图集**（推荐） | 写 `_tools/gen_font_atlas.ps1`：用 PowerShell 的 `System.Drawing`（**它自带 .NET Framework，所以能用**）把一套等宽像素字体按固定字号渲染成 `book_font.png` + 一张字符宽度表 `book_font.json`；C# 侧的 `DrawText` 改成查表贴图 | 与项目里 `gen_*.ps1` 的既有做法一致；零运行时依赖；将来换成泰拉真字体只需重跑脚本 |
| B. 从泰拉本体抽字体 | 泰拉的字体是 `Fonts/*.xnb`，XNB 用的是 LZX 压缩，解析成本很高 | 不值得，除非 A 的观感不可接受 |

**注意 A 的字形不是泰拉原版字体**，所以它只能用来定**版面与配色**，
最后的字号/字距微调仍需在游戏里对着真字体做一次。这条要如实写在结论里，
别让"离屏图好看"被当成"游戏里就好看"。

### 已经验证过的

- 画布 → PNG 管线通了：一条命令产出有效 PNG，绘制调用计数正常
  （`_tools/book_probe.png`，我读回来目视确认过双线边框、选中态、分类格、页码点都对）
- 探针只在 `DRAWTEST_RENDER=1` 时运行 —— 日常 `run_all` 不产生文件、计数不变

### 第 1–3 步实际做出来的东西

- 内容骨架：**7 分类 / 82 条目 / 524 页**，从源项目的手册 JSON 生成（跳过的只有 4 页：
  1 张图片页 + 3 个超链接页，泰拉侧不需要）
- 排版引擎支持源项目真用到的标记子集：`$(br)` `$(br2)` `$(p)` `$(bold)` `$(italic)`
  `$(0)`–`$(9)` `$(l:目标)` `$(/l)` `$()`
- 状态机：封面 → 分类网格 → 条目双页，逐级返回、翻页边界、跨页配对、跳页

### 生成过程中揪出的三个坑（都已写进生成器注释）

1. `ArrayList.Add` 的返回值会漏进管道（输出刷出一片裸数字），要 `[void]`
2. 源项目里有 **81 页是裸字符串**（`"hexcasting.page.interop.1"`），那是 Patchouli 对
   `patchouli:text` 的简写 —— 当成"未知类型"跳过会**静默丢掉整页**，表现是条目数 60 而不是 82
3. `patterns/great_spells` / `patterns/spells` 是 **Patchouli 的子分类**，我们的模型是平的，
   要归到父分类，否则 11 个条目会因"分类对不上"被丢掉

### 下一步（第 4 步）要做的决定

1. 先写 `BookSkin` 抽象（`Measure` / `DrawFrame` / `DrawCategoryCell` / `DrawEntryRow` /
   `DrawPage` / `HitTest`），**在两套实现之前定契约** —— 否则两套会各自长歪
2. **`Measure` 必须两套各自实现**：MC 的 GUI 在 GUI scale 2~3 下画、泰拉 UI 默认 1x，
   像素密度不是一个量级，只换贴图会又大又糊
3. Patchouli 版直接用它的 512×256 图集（合法，见第 10 节）；原版版用泰拉自带 UI 贴图
4. 两套都要能被第 5 步的离屏渲染驱动 —— 所以 `DrawXxx` 不能直接调 `Main.spriteBatch`，
   要给一个最小的绘制接口（这样离屏渲染才能喂假实现）

### 唯一还缺的内容

**正文**。源文件里 `text` 存的是本地化键（`hexcasting.page.jeweler_hammer.1`），
真正的英文正文在 mod 的 lang 目录里，而 `hexsrc` 没有那份文件（第 10 节已确认）。
生成器把每个页面的原键**保留在紧随其后的注释里**，补正文时照着填。

### 结论

用户的原话是「把代码下载下来自己抄一个」。既然协议允许（不盈利 + 开源），就**按上面那张表直接移植** ——
不再需要 clean-room。唯一保留的自我约束是：**排版/翻页这些核心逻辑仍要读懂再写**，
因为 Java + Minecraft API 到 C# + tModLoader 之间没有自动翻译，照抄编译不过，只会留下一堆看不懂的代码。

### 已经量出来的规格

| 资源 | 尺寸 |
|---|---|
| `textures/gui/book_brown.png`（7 种颜色：blue/brown/cyan/gray/green/purple/red） | **512 × 256** |
| `textures/gui/crafting.png` | 512 × 256 |
| `textures/gui/page_filler.png` | 512 × 256 |
| `textures/gui/inventory_button.png` | 18062 B |

即：一本 GUI 图集，书体、书签、箭头、按钮全在这 512×256 里切片。
我们要按这个**切片布局**自己画一套像素图 —— 尺寸照搬、画面自绘。

---

## 11. 双 UI：开发者设置里的皮肤开关

用户要求：**在开发者设置做一个双 UI 配置，做两套** —— 一套帕秋莉版、一套原版。

### 两套是什么

| 皮肤 | 观感 | 依据 |
|---|---|---|
| **帕秋莉版**（`Patchouli`） | 皮革封面 + 双页羊皮纸 + 书签条 + MC 式排版；横跨整屏的书 | 上面量出来的 512×256 图集布局 |
| **原版版**（`Vanilla`） | 泰拉自带的 UI 观感：木/蓝面板、原版按钮与滚动条体系、单页 | 泰拉自己的 `Main.Assets` UI 贴图与控件风格 |

**内容层完全共用**（分类 / 条目 / 页面数据与本地化都是同一份），
只有**布局与渲染**分两套实现。这样才不会出现「两套内容各写一遍、然后慢慢对不上」。

### 分层（这是能做出两套又不失控的关键）

```
Core/Ui/BookModel.cs      分类/条目/页 的数据类型 + hjson 加载      ← 两套共用
Core/Ui/BookLayout.cs     几何：两套各自一套 Layout 实现            ← 皮肤相关
Client/UI/HexBook*.cs     渲染：BookSkin 抽象基类 + 两个子类        ← 皮肤相关
```

`BookSkin`（抽象）定下：`Measure` / `DrawFrame` / `DrawCategoryCell` / `DrawEntryRow` /
`DrawPage` / `HitTest`。`PatchouliSkin` 与 `VanillaSkin` 各自实现。

### 配置

`Config/HexClientConfig.cs` 里加一个**开发者开关**（默认走帕秋莉版）：

```
书外观：帕秋莉版 / 原版泰拉       （仅开发者模式下显示）
```

判据：
- [ ] 两套皮肤都能完成同一套导航动作（三视图 + 翻页 + 返回）
- [ ] 切换皮肤**不重建**内容数据（只换布局与绘制），切回来状态不丢
- [ ] 离线断言：两套 Layout 在相同内容下的**热区不重叠、不越界**
- [ ] 离屏渲图：同一条目在两套皮肤下各出一张 PNG，供比对

> 说明：这里把「原版」理解为**泰拉原版的 UI 观感**（不是原版咒法学 —— 咒法学的手册本身就是
> Patchouli，两者是同一个东西）。若理解有偏差，改的是 `VanillaSkin` 这一层，不影响其余结构。

---

## 15. 重新规划（2026-09-14，查证官方文档之后）

前面连着出了六个 bug：崩游戏、书自己开、列表溢出、关不掉、点不动、文字不吃裁剪。
**根因不是画法，是我没按泰拉自己的 UI 机制做** —— 我另起了一套画布、布局、命中测试、输入处理，
再硬接到旧书的绘制调用点上。于是每个接线细节都成了坑。

这次把官方 wiki 读完了（[Advanced guide to custom UI](https://github.com/tModLoader/tModLoader/wiki/Advanced-guide-to-custom-UI)、
[Basic UI Element](https://github.com/tModLoader/tModLoader/wiki/Basic-UI-Element)），
以及 [UICommon 类参考](http://docs.tmodloader.net/docs/stable/class_u_i_common.html)、
[原版界面层清单](https://github.com/tModLoader/tModLoader/wiki/Vanilla-Interface-layers-values)。结论如下。

### 15.1 正确的骨架（查证过的，不是猜的）

```
[Autoload(Side = ModSide.Client)]  ModSystem          ← UI 只在客户端
    UserInterface   _interface
    UIState         _bookState
    GameTime        _lastUpdateUiGameTime

Load()                 { _bookState.Activate(); _interface = new UserInterface(); }
UpdateUI(gameTime)     { _lastUpdateUiGameTime = gameTime; _interface?.Update(gameTime); }
ModifyInterfaceLayers(layers)
    → 在 "Vanilla: Mouse Text" 之前 Insert 一个
      new LegacyGameInterfaceLayer("HexCastingTerraria: Book",
          () => { _interface.Draw(Main.spriteBatch, _lastUpdateUiGameTime); return true; },
          InterfaceScaleType.UI)          ← 这一项负责 UI 缩放，我上一版完全没有
```

层插在 `"Vanilla: Mouse Text"` **之前**，这样 `Main.hoverItemName` 设的悬浮文字还能正常显示。

### 15.2 输入封锁（我完全漏掉的两条）

```csharp
protected override void DrawSelf(SpriteBatch sb) {
    base.DrawSelf(sb);
    if (ContainsPoint(Main.MouseScreen)) { Main.LocalPlayer.mouseInterface = true; }        // 点击别穿透去用武器
    if (IsMouseHovering) { PlayerInput.LockVanillaMouseScroll("HexCastingTerraria/Book"); } // 滚轮别换快捷栏
}
```

文档明确提醒：**别加在 UIState 上**（它铺满整屏），要加在面板/列表这些容器上。

### 15.3 事件与命中测试交给框架

`UIElement` 自带 `OnLeftClick` / `OnRightClick` / `OnScrollWheel` / `OnMouseOver` / `OnMouseOut`，
并且**子元素事件会冒泡到父元素** —— 按钮只需给容器挂一次处理器。
我手写的 `HitTest` 是在重造这个。

### 15.4 原版皮肤改用**泰拉现成控件**，不要再自绘

本次规划最重要的修正：

| 我原来做的 | 官方做法 |
|---|---|
| NinePatch 手搓 `panel_bg` + `panel_border` | **`UIPanel`** —— 一个控件就把真面板画好 |
| 猜 tint `#3E4C6B` / `#7C93BF` | `UICommon.DefaultUIBlue = Color(73,94,171)`、`MainPanelBackground = Color(33,43,79)*0.8f`、`DefaultUIBorder = Color.Black` |
| 自己写滚动/列表 | `UIList` + `UIScrollbar`（`UICommon.WithView`） |
| 自己排文本 | `UIText`（自带缩放与对齐） |
| 自己判命中 | `UIElement.ContainsPoint` / `IsMouseHovering` |

**原版皮肤 = 用 `UIPanel` + `UIText` + `UIList` 搭一棵元素树。** 这样它**必然**是原版观感 ——
画的就是原版那套代码。

### 15.5 帕秋莉皮肤保留自绘

`UIPanel` 给不了 MC 那本皮革书。所以帕秋莉皮肤继续用已有的
`IBookCanvas` + `PatchouliSkin`（离屏出图已验证观感），但改成
**从一个 `UIElement.DrawSelf` 里驱动** —— 这样它同样自动获得命中测试、输入封锁与 UI 缩放。

### 15.6 已有资产怎么处置

| 文件 | 处置 |
|---|---|
| `Core/Ui/BookModel.cs` | **留** —— 数据模型与皮肤无关 |
| `Core/Ui/BookContent.Generated.cs` | **留** —— 内容骨架 |
| `Core/Ui/BookText.cs` | **留** —— 排版/分页，离线断言有价值，`UIText` 替不了 |
| `Core/Ui/BookView.cs` | **留** —— 状态机，帕秋莉皮肤用 |
| `Core/Ui/BookSkin.cs` / `BookMetrics` / `IBookCanvas` | **只服务帕秋莉皮肤**，不再当两套的共同基础 |
| `Core/Ui/NinePatch.cs` | 留（`UIPanel` 优先，自绘时才用） |
| `Core/Ui/VanillaSkin.cs` | **废掉自绘版**，改用 `UIPanel` 元素树重写 |
| `Client/UI/SpriteBatchBookCanvas.cs` | **降级**为只给帕秋莉皮肤用；裁剪与输入封锁交给框架 |
| `Client/UI/HexBook.Themed.cs` | 改成挂到新的客户端 `ModSystem`，不再挂在旧书的调用点 |

### 15.7 实施顺序（每步都打包给你进游戏看）

1. **搭骨架**：客户端 `ModSystem` + `UserInterface` + `UIState` + 界面层 +
   `mouseInterface` / `LockVanillaMouseScroll`。先用一个**最简面板**验证
   「位置对、点得住、滚轮不换快捷栏、Esc 能关」—— 这四条正是上一轮全部的坑。
2. **原版皮肤**：`UIPanel` + `UIText` + `UIList` 搭真元素树，颜色用 `UICommon` 常量。
3. **帕秋莉皮肤**：把 `PatchouliSkin` 接进 `DrawSelf`，复用已验证的几何与配色。
4. **开关**：配置项在两套之间切。
5. 旧图案浏览器退役（信息已全部进新模型）。

### 15.8 参考链接

- [Advanced guide to custom UI](https://github.com/tModLoader/tModLoader/wiki/Advanced-guide-to-custom-UI)
- [Basic UI Element](https://github.com/tModLoader/tModLoader/wiki/Basic-UI-Element)
- [UICommon 类参考](http://docs.tmodloader.net/docs/stable/class_u_i_common.html)（**真实颜色常量**）
- [原版界面层清单](https://github.com/tModLoader/tModLoader/wiki/Vanilla-Interface-layers-values)
- ExampleMod 的 `ExampleCoinsUI`（官方推荐的完整范例）
