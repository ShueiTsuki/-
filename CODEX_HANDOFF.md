# 交接：HexCastingTerraria（历史问题清单 + 踩坑）

> **状态数字**（图案数 / 测试数 / 包体）看 [STATUS.generated.md](STATUS.generated.md) —— 脚本生成，唯一权威。
> **架构、分层、每个文件的职责**看 [ARCHITECTURE.md](ARCHITECTURE.md) —— 也是脚本生成。
> 本文**只留不会过期的东西**：真实发生过的 bug、验证的能力边界、环境坑。
> 这三分工由 `_tools/check_arch.ps1` 强制（本文再写状态数字会让断言失败）。

---

## 1. 项目在哪

| 项 | 路径 |
|---|---|
| 模组源码 | `D:\DeepSeekHarness\tmod\HexCastingTerraria` |
| 构建同步目录 | `C:\Users\MSI-PC\Documents\My Games\Terraria\tModLoader\ModSources\HexCastingTerraria` |
| 打包产物 | `C:\Users\MSI-PC\Documents\My Games\Terraria\tModLoader-dev\Mods\HexCastingTerraria.tmod` |
| 工具脚本 | `D:\DeepSeekHarness\tmod\_tools`（**不是** `HexCastingTerraria\_tools`） |
| 离线 VM 测试工程 | `D:\DeepSeekHarness\tmod\tests\vmtest`（直接编译整个 `Core/**`，不复制） |
| 拖拽/几何验证工程 | `D:\DeepSeekHarness\tmod\tests\drawtest`（同上） |
| 原版参考源码 | `D:\DeepSeekHarness\hexsrc`（HexMod 的 Kotlin/Java，**权威依据**） |
| 用户素材 | `D:\DeepSeekHarness\图案工具`、`D:\DeepSeekHarness\咒法学图案` |
| tModLoader | `D:\steam\steamapps\common\tModLoader`（1.4.5-dev，随上游提交自动更新；验证时的 commit 见 STATUS.generated.md） |

---

## 2. 一页命令

```powershell
cd D:\DeepSeekHarness\tmod
.\_tools\run_all.ps1            # 编译(0错0警) → 离线 VM → 拖拽/几何 → 贴图 → 生成物 → 架构断言
.\_tools\run_all.ps1 -Package   # 另外打包，并在专用服务器里真实加载 + 进世界（需先关游戏）
```

每一步都以**子进程退出码**判定；被跳过的步骤显示 SKIP，不算全绿。
各步实测事实写进 `_tools\run_last.json`，`STATUS.generated.md` 只从那里取数。

单独跑某一层：

```powershell
cd D:\DeepSeekHarness\tmod\HexCastingTerraria; .\build.ps1 -CompileOnly   # 编译
cd D:\DeepSeekHarness\tmod\tests\vmtest;       dotnet run -c Release      # 离线 VM（栈机语义）
cd D:\DeepSeekHarness\tmod\tests\drawtest;     dotnet run -c Release      # 拖拽 + 几何 + 栈
cd D:\DeepSeekHarness\tmod;                    .\_tools\check_arch.ps1    # 架构断言
cd D:\DeepSeekHarness\tmod;                    .\_tools\check_assets.ps1  # 贴图存在性
cd D:\DeepSeekHarness\tmod;                    .\_tools\verify_server.ps1 # 专用服务器加载
```

**客户端验证必须人工**：贴图缺失只有客户端会报（`TransferAllAssets` 是客户端专属），
专用服务器永远不会发现。启动后看 `D:\steam\steamapps\common\tModLoader\tModLoader-Logs\client.log`。

---

## 3. 游戏里**实际发现过**的问题（全部来自人肉实测）

这张表是本文最有价值的部分。注意最后一行统计的含义。

| # | 现象 | 根因 | 状态 |
|---|---|---|---|
| 1 | 打开书后返回无法移动 | 书设了 `Main.blockInput = true`，却没设 `HexCanvasState.BlockedInput`，唯一的解锁路径永不执行 | 已修 |
| 2 | 画 `pi` 被识别成少一个字符 | ① 用户的 `draw.py` **少画最后一段**（原版 `HexPattern.positions()` 在循环外还有一步），据此生成的图案表每张都少一笔；② 吸附阈值默认值抄成 `1.0`（原版 `DEFAULT_GRID_SNAP_THRESHOLD = 0.5`），每步要拖 √2 格距，图案永远落后鼠标 41% | 已修 |
| 3 | 「意识之精思」无法检测 | 闭合图形换个角起笔，角度签名会**循环移位**（`qaq` → `aqa`），而图上没标清起点 | 已修（预览改成红点 + 起始方向箭头） |
| 4 | 破坏魔法"成功"但方块不动 | `BreakBlockAt` 手写 `Main.tileNoFail` 判定**语义搞反**（该数组是「挖起来不会失败」＝能挖，被当成「挖不动」），木板直接走进 `else { return false; }`；而这个返回值**没有任何人看** | 已修（改走 `WorldGen.KillTile` + 扣费前预检带原因） |
| 5 | 挖了树却没挖到瞄准的方块 | **法术序列栈错**：`get_caster → entity_pos/eye → get_entity_look → raycast → break_block`。`entity_pos/eye` 把实体换成向量，后两步依次报错但**栈不变**，`break_block` 拿到残留的眼位向量 → 破坏了**玩家自己脚下那一格** | 已修（要取两次施法者）；`drawtest` 加了栈平衡检查并把这个错序列留作**反例** |
| 6 | 13 把法杖图标一模一样 | 掩码杖身写成 `'++'`（= 次材料色），`-Body` 的木材色**从未生效** | 已修 |
| 7 | 打包报 `warning : Image loading failed: unknown image type` | **未定因**。已排除：全部 PNG 可解码、全为 8 位非隔行、客户端加载 0 错 | 2026-09-29 用当天 tML 打包**未复现**，观察中 |
| 8 | tML 自动更新后编译失败（4 错） | 1.4.5-dev 上游把 `Item.active` 移到 `WorldItem` 外壳上。顺带发现：`ExtractMediaFromItem` 对 `inner` 调 `TurnToAir()`，**地上的掉落物实体不会失活**（应对外壳调用） | 已修；run_all 现在会提示「tML 自上次全绿以来已更新」 |

> **统计**：上述条目里 1–7 **没有一条是自动化测试发现的**。这条统计本身就是对当前验证网的评级 ——
> 第 4 节说明了为什么。

### 验证工具自身的缺陷（2026-09-29 审计，已修）

验证网本身出过错，而且都是「静默地报绿」那一类 —— 比没有测试更糟：

| 缺陷 | 后果 |
|---|---|
| 36 个含中文的 `.ps1` 都没有 UTF-8 BOM | PS 5.1 下 `run_all.ps1`、`build.ps1` 直接语法错误，整条流水线跑不起来。现由 `check_arch` 断言 |
| `Get-Content` 读 JSON 不带 `-Encoding` | 中文乱码 → `ConvertFrom-Json` 失败。现由 `check_arch` 断言 |
| drawtest 判定写成 `$out -match '失败 0'` | 中途一行「（失败 0 条）」就能让最终「失败 5」判为通过 |
| 编译步骤名为「0 错 0 警」，只查「已成功生成」 | 警告直接放行 |
| 所有步骤都不看退出码；生成器步骤无条件 `return $true` | 生成器崩了也是绿的 |
| `STATUS.generated.md` 的编译行、贴图行是**写死的字符串** | 不管实测如何，状态表永远写「0 错 0 警」「缺失 0」 |
| `check_arch` 查 drawtest.csproj 的路径写错，外面包了 `if (Test-Path)` | 这条断言**从未执行过** |
| vmtest 靠脚本按子目录拷贝 Core；两个测试工程都在仓库外 | Core 新增目录会被静默漏测；测试代码不受版本控制 |
| `verify_server.ps1` 读 stdout（不是 tML 日志）、编码错、无退出码 | 要么干等 300 秒超时，要么凭运气给结论 |

修法的共同原则：**以退出码为准，文件不存在 = 失败而不是跳过，数字只来自实测**。
改完后做过变异测试（植入 1 条警告 + 改坏一个 Core 常量）：编译与 VM 两步都正确判红。

---

## 4. 验证的能力边界（比"有多少测试"重要）

四层自动化**全是有头/无头逻辑验证**。它们能抓：编译、栈机语义、图案几何、坐标换算、
贴图**存在性**、服务器加载。抓不到：

| 缺失项 | 说明 |
|---|---|
| **一切绘制结果** | `Client/UI/*.cs` 与 `Client/HexClientSystem.cs` 的绘制部分**零自动化覆盖** —— SpriteBatch/Vector2 依赖 XNA。书的排版、预览位置与缩放、网格点、HUD、粒子、法术环全靠眼睛 |
| **UI 布局与命中判定** | `PanelRect`、目录网格单元矩形、页面返回热区、`HandleInput` —— **都是纯数学，可测，但还没抽出来** |
| **输入链路** | 鼠标键盘 → 状态机（落笔/拖拽/收笔、开书关书、Esc、背包压制） |
| **真实世界行为** | `WorldGen.*` 调用只在服务器加载时跑过，没在真实世界里执行过施法 |
| **像素级回归** | 没有截图基线，改贴图不会有任何测试报警 |
| 音频 / 联机双客户端 / 存档落盘 / 16 个配置开关 / 性能 | 全无覆盖 |

### 已经补上的（2026-09-14 那一轮审计之后）

- **坐标换算回到测试网**：`HexGrid` 曾经因为用 `Vector2` 被排除在离线工程外，
  验证工程里测的是**手抄的公式副本** —— 真代码改错一个符号测试照样全绿。
  现在 `HexGrid` 用自己的 `Core/Casting/Math/Vec2f`，排除表清空，往返/吸附/边界用例测的是真代码。
- **架构约束变成可执行断言**：`_tools/check_arch.ps1`。此前 `Core/` 不得引用 XNA/tModLoader
  这条约束有 3 个例外，而且**被写进文档当成"设计如此"** —— 违规一旦被文档化，读文档的人
  就认为那是规范。现在两个配置类搬到 `Config/`、`HexGrid` 改 `Vec2f`，例外清零，
  断言零白名单。
- **状态数字只有一份**：见 [STATUS.generated.md](STATUS.generated.md)。
  此前根目录有两份互相矛盾的"当前状态"文档，读的人无从判断谁更新。

### 截图基线：渲染层验证的地基（工具已就绪，基线还没建）

`_tools/capture_window.ps1` 能抓任意窗口的内容图（`PrintWindow` + `PW_RENDERFULLCONTENT`，
窗口被遮挡也能抓；泰拉建议窗口模式，独占全屏可能是黑图）：

```powershell
.\_tools\capture_window.ps1 -List                       # 先找窗口标题
.\_tools\capture_window.ps1 -TitleMatch "tModLoader" -OutDir "D:\DeepSeekHarness\tmod\_shots" -Count 3
```

已实测可用（抓到一个记事本窗口，748×713、319 色，内容正确）。

**还没做的是"基线"**：需要人工把游戏开到各个界面各抓一张，存成基线，之后改动再抓、差分比对。
要抓的关键界面：书目录页 / 图案详情页 / HUD（媒质环 + 实时笔迹读数）/ 画布（网格点 + 已画图案）/
粒子与法术环。这一步只能人工操作，但工具已经有了。

### 建议的下一步（按性价比）

1. **把 Client/UI 的纯数学抽到 Core**（无 Vector2/XNA）并加离线测试：
   `BookLayout`（面板矩形、网格单元、命中判定）、`CanvasGeometry`（已在 HexGrid 里）、
   `PreviewLayout`（图案居中与缩放包围盒）。一次覆盖"书点不中""预览画歪"两类。
2. **截图回归**：客户端跑起来抓窗口存 PNG，给关键界面留基线（书目录 / 图案详情 / 画布 / HUD）。
3. **类型推断版的法术检查**：现在的栈检查只看深度，看不出类型不符（见下）。
4. **多人双客户端验证**：符号位置粒子共享、法术书封装、法术环同步。

### 曾经是盲区，现已闭合：类型不匹配

旧版 `drawtest` 的栈检查只算深度，所以这类问题查不出：

```
get_caster → entity_pos/eye → const/vec/ny → add_motion
深度： 1   →      1        →      2       →  需要 2 ✓ 不欠账
但 add_motion 第 1 个参数要的是【实体】，此时已被换成【坐标】→ 运行时报「参数不是实体」
```

**这**正是问题 #5 那一类："能编译、能过全部测试、只在游戏里炸"。

现在闭合了，机制是：

- 每个 action 可声明 `ActionTypes`（消耗什么类型 / 产出什么类型）；
- `drawtest` 据此做轻量**类型推断**，栈上带类型标签、逐条比对；
- 已标注 **32 条图案**，覆盖常用法术链（`get_caster`、`entity_pos/*`、`get_entity_look`、
  `raycast`、`add_motion`、`blink`、`const/*`、`print`、`break_block`、`conjure_block`、栈重排类）；
- 对**未标注**的图案按"未知"跳过，并在 trace 里显式标出「这是假设」——
  绝不因为"有人忘了标注"就报假错（假错会让断言变成噪音，最后被关掉）。

验证：历史 bug #5 的序列现在被直接检出 ——
`get_entity_look 第 1 个参数要 entity，实际是 vec`。

**改这块代码前必读的两个坑**：

1. `IAction.Types` 只写成**默认接口成员**是不够的：**默认接口成员不会被派生类的同名成员
   重新绑定**。在子类里写 `public ActionTypes Types => ...` 能编译、看着也对，
   但以 `IAction` 引用调用时永远走默认值 —— 类型检查静默失效，而且全绿。
   必须在 `ConstMediaAction` / `SpellAction` 上声明 `virtual` 成员。
   `drawtest` 里留了一条专门的断言盯这件事。
2. **`SpellAction` 的净栈效果是 `-Argc`**（弹出 Argc、什么都不压回），不是 `1 - Argc`。
   检查器曾经按后者算 —— 又是一处"手抄模型"。
3. 覆盖只能靠**加标注**扩展。`check_arch.ps1` 里有棘轮断言：
   已标注数只许多、不许少（少了说明有人删标注，类型检查会悄悄退化）。


---

## 5. 环境 / 工具踩坑清单（会浪费时间的那些）

1. **`_tools` 的真实位置**是 `D:\DeepSeekHarness\tmod\_tools`。在 `HexCastingTerraria\` 下另建
   一个 `_tools` 会让脚本跑旧副本，症状是"改了没生效"，极难察觉。
2. **PowerShell 5.1 里 `[System.IO.File]::XXX("相对路径")` 用的是进程初始 CWD**，不是 `cd`
   之后的位置（`Get-Item` 这类 cmdlet 用的是 PS location）。**一律用绝对路径**。
3. **无 BOM 的 UTF-8 `.ps1` 在 PS 5.1 下中文会乱码**（可能吃掉引号导致语法错）。写完补 BOM。
4. **`param()` 必须是脚本第一条语句**，`$ErrorActionPreference` 都不能在它前面。
5. **PowerShell 的坑**：`$x = '0.5'` 时 `$x[0]` 得到的是**首个字符 `'0'`**，不是字符串。
   只匹配到一个值时，断言会莫名其妙地红，而且看起来像代码的问题。
   `$Matches` 只由 `-match` 填充，`Select-String` 不填。
   `Select-Object -First N` 截断管道会让进程以退出码 1 结束，别用它收尾测试输出。
6. **`build.ps1` 检测到 tModLoader 在运行时会拒绝打包**（.tmod 被占用），只编译。要先关游戏。
7. **`Main.tile[x, y]`**：索引器**只读且按值返回**（`Main.tile[x,y] = t` → CS0200；
   `Main.tile[x,y].HasTile = false` → CS1612）。但 `Tile` 是 4 字节句柄（单个 `UInt32 TileId`），
   所以 `var tile = Main.tile[x,y]; tile.HasTile = false;` **是生效的**。
   不要被 CS1612 误导去"修"这十几处写法。
8. **`PatternRegistry.Match(string)` 是按角度签名匹配**，不是按 id。按 id 查要在 `All` 里找。
9. **`HexActions.RegisterAll()` 必须显式调用**，否则所有图案都显示"未实现"。
10. `Terraria.Utils.MeasureString` 不存在，用 `FontAssets.MouseText.Value.MeasureString`；
    `ItemID.WoodenFence`（不是 `WoodFence`）；`Main.magicPixel` 用 `HexPixel.Value` 代替。

---

## 6. 用户的工作方式

- 要求**连续推进**：不要每完成一步就停下汇报，做完直接做下一项。
- 只有**真的卡住**（缺信息/缺权限/冲突）才停下来问。
- 用户会直接在游戏里实测并把现象发回来。**这是目前唯一能覆盖渲染与交互的手段** ——
  第 3 节那 7 条全靠它。要珍惜这类反馈，并尽量把每一条都变成可复现的断言。

---

## 7. 源项目的阶段门槛（配方对齐的唯一依据）

**做任何配方前先读这一节。** 之前是凭感觉写材料，结果是数量比原版差一个数量级，
还把源项目里**并列**的三个物品写成了链式升级。

### 「启蒙（enlightenment）」到底是什么

`HexAdvancements.ENLIGHTEN`（一个 `OvercastTrigger`）：**一次过载消耗掉 ≥80% 最大生命，
且施法后剩余生命不足 1 点**。它不是进度点，是玩家主动做的**濒死仪式**。
`CastingEnvironment.isEnlightened()` 查的就是这个成就。

### 只有 15 个「大战法术」需要启蒙

`HexActionTagProvider`（写进 `REQUIRES_ENLIGHTENMENT` 的那批）：

```
lightning / flight / create_lava / teleport:great / sentinel:create:great
dispel_rain / summon_rain / brainsweep / craft:battery
potion:regeneration / night_vision / absorption / haste / strength
```

**`edify`（启迪树苗）不在名单里** —— 它是普通法术：把树苗启迪成启迪树苗，
种下去长成阿卡夏树，产出启迪原木与彩色启迪树叶。所以**启迪木是肉前内容**，
一开始按「启蒙后」把它放到月后是错的。

### 由此得到的泰拉阶段映射

| 源项目门槛 | 泰拉阶段 | 依据 |
|---|---|---|
| 无（`unlockedBy` 只是 `has_item` / `staves`） | 肉前 | 紫水晶在泰拉一开局就能挖到 |
| 合唱果（末地特产） | 肉后 | 泰拉没有末地档，取中间阶段；对应物用肉后的水晶碎块 |
| brainsweep（启蒙大战法术）→ 淬灵 | 肉后 | 泰拉侧对应物是**神圣地妖精**，只在肉后出现 —— 门槛由材料自带 |
| enlightenment（原动力 / 导线 / 阿卡夏三件 / 剖念法杖） | 月后 | 泰拉没有过载机制，门槛落在终局；合成站 = 远古操控器 |

阶段表分两层：
`_tools/progression_stages.json`（**人工**定的阶段，唯一真源）→
`PROGRESSION.generated.md`（脚本生成，站与材料是从代码里读出来的）。
`check_arch.ps1` 的断言⑨ 会拿两者对拍：漏定档、定错类、**月后却只用工作台**，都会红。

---

## 8. 配方对齐的方法论（踩过的坑）

1. **`ring` / `ringCornerless` / `ringAll` 是 Paucal 库的 helper**，定义不在 `hexsrc` 里。
   语义：`ring` = 8 环 + 1 心；`ringCornerless` = **4** 条边中点 + 1 心；
   `ringAll` = 8 环 + 1 心（产出按批，如「8 深板岩 + 1 粉 → 8 板岩块」）。
   数材料时按这三个来，别猜。
2. **泰拉的配方没有形状、不看摆放，只数材料数量。** MC 的 shaped 配方对齐时只保留数量比。
3. **不要凭记忆写 ItemID。** `_tools/dump_vanilla_ids.ps1` 用 Mono.Cecil 从
   `tModLoader.dll` **离线**导出 ItemID(6195) / TileID(753) / NPCID / BuffID / DustID /
   ProjectileID / WallID 到 `_tools/vanilla_ids.json`，用 `_tools/qid.ps1 ItemID '正则'` 查。
   已确认**不存在**：`ItemID.LivingWood`（生命木只有家具和 `LivingWoodWand`，
   所以「用生命木当材料」这条路是死的）。
4. **原版配方组用 `RecipeGroups.X`**（`Terraria.ID.RecipeGroups`：Wood / IronBar / Sand /
   Stone / Fragment …），不要写死 `ItemID.IronBar` —— 铅世界会做不出来，而这是泰拉玩家的默认预期。
   模组自己的配方组在 `Content/Items/HexRecipeGroups.cs`（建材族那 4 个，等价于源项目的 tag）。
5. **同族配方的比例要按「角色」推，不能各写各的。**
   `_tools/gen_deco_blocks.ps1` 里的配方已经从「行内三列」挪到独立的配方表 ——
   塞在行里时表达不了「同族各造型之间固定的比例与互相关系」，实际就长歪了：
   瓦从基底块 1:1（应为砖 4:4）、砖漏了「砖 ← 小砖」的回炉、切石机那一整条路径干脆没有。
6. **源项目没有配方的东西要如实标注。** 启迪原木/树叶、晶洞产物都属于「由机制产出」；
   保底配方是必要的，但注释里必须写清「源项目无配方 + 为什么选这个材料」，
   否则下一个人会以为抄漏了。
7. **`Recipe.AddTile()` 无参 = 徒手**。断言⑧ 要求所有配方都有合成站，
   原版徒手的配方（念珠、透镜、赛符…）要显式挑一个语义最贴的站。

---

## 9. 打包期那条 `Image loading failed: unknown image type`

存在了很久，`TODO_PLAN.md` 里两处把它记成「`icon_small.png` 一直报 `does not exist`」——
**描述是错的**，照那句话去查会一直查不到。

### 症状与定位

每次打包多一条警告，不带文件名。二分定位（把候选文件逐个移走再打包）：

| 试验 | 警告 |
|---|---|
| 移走 `icon_small.png` | **消失** |
| 移走其它任何贴图（含 33x32、24x24 这些非标准尺寸的） | 还在 |
| 用 tModLoader **内嵌模板**的 `icon_small.png` 内容替换 | **消失** |
| 保留我们的画，只删掉 `gAMA` / `pHYs` 两个 chunk | 还在 |
| 用 GDI+ 重新编码成 30x30 / 32x32 / 16x16 | 全都还在 |

结论：**打包期读 `icon_small.png` 的那条解码路径，读不了 GDI+（System.Drawing）写出的 PNG**；
tModLoader 内嵌模板那份（同样 30x30 / 8bit / RGBA / 非隔行）能读。
报错文本来自 `FNA3D.dll`（`stb_image` 的 `unknown image type`），
调用链在 `Terraria.ModLoader.Core.ModCompile::AddResource`（它同时引用了
`icon_small.png` 和 `Terraria/ModLoader/Templates/icon_small.png`）。

**为什么其余 90 多张 GDI+ 贴图没事**：它们是**运行期**加载的，走另一条路；
这条只发生在打包期对 `icon_small.png` 的那次读取上。

### 修法

**不要提供 `icon_small.png`。** 文件缺失时 tModLoader 回退到自带模板，
既没有警告也不缺图标（`Mod::Autoload` 里的 `'Failed to load icon_small.png. Reason: '` 就是这条回退路径的日志）。

- `_tools/gen_block_art.ps1` 已停止生成它，末尾写清了原因
- `check_arch.ps1` 断言⑩ 盯着：根目录出现 `icon_small.png` 立刻红；顺带校验 `icon.png` 是 80x80

### 顺带记下的工具链事实

`icon.png` / `icon_small.png` **确实是** tModLoader 的约定（`Mod::Autoload`、
`ModCompile::AddResource`、`UIModSourceItem::PublishMod` 等都引用），
不要因为「没见过」就把 `icon.png` 也删掉。

---

## 10. 已解决：en-US 词条缺口（99 条）

> **已完成（2026-09-14）。** 下面是当时查清的过程与结论，留作方法记录。
> 生成器：`_tools/gen_localization_en.ps1`（只补缺失键，不覆盖已有条目）。
> 防复发：断言⑦ 已扩成**双语** —— 中英各查一遍，缺哪边都红。
> 英文名的取舍写在生成器顶部的表里；用词照着源项目 `patchouli_books/thehexbook/en_us/`
> 的中文版对应内容核对风格。

### 还剩一个小口子：`Keybinds` 段

补完 99 条物品/方块词条后，把两份文件的 3 缩进键对齐一比，**英文只差 `Keybinds` 这一整段**
（`zh-Hans.hjson` 有，`en-US.hjson` 没有）。它的后果和物品词条一样但更隐蔽：
**按键绑定界面里会显示原始键名**，而不是「绘制法阵」这种可读文本。

断言⑦ 查不到它 —— 那条断言只遍历 `ModItem` / `ModTile` 的类名，而 `Keybinds` 是
tModLoader 的另一个本地化分类。要补的话：
1. 读 `zh-Hans.hjson` 的 `Keybinds: { ... }` 段，把键名抄进 `en-US.hjson` 同位置；
2. 顺带把断言⑦ 再扩一条：两个文件在 `Items` / `Tiles` / `Keybinds` 三个段下的键集合必须相等。

### 现状（实测）

| 文件 | 键数 |
|---|---|
| `Localization/zh-Hans.hjson` | 313 |
| `Localization/en-US.hjson` | **124** |
| `Localization/zh-Hans_Mods.HexCastingTerraria.Configs.hjson` | 74 |
| `Localization/en-US_Mods.HexCastingTerraria.Configs.hjson` | 74 |

**中文有、英文没有的键：99 个**（英文有而中文没有的：0 个 —— 所以是单向缺口）。
缺的都是物品：`Abacus` / `JewelerHammer` / `ScryingLens` / `Spellbook` / `Cypher` /
`Trinket` / `Artifact` / `Focus` / `ThoughtKnot` / 三种 `Scroll*` / `Akashic*` /
14 把法杖 / 31 个建材方块的物品形态与 MapEntry 等。

后果与中文那边**同一类 bug 但方向相反**：tModLoader 找不到词条时**静默回退到类名**，
所以英文环境里会看到 `AmethystDustBlockItem` 这种字样，而不是 "Amethyst Dust Block"。
中文那边早先就是这个症状，已修（断言⑦ 现在只查中文，查不到这类英文缺口）。

### 我已经查过的

- `D:\DeepSeekHarness\hexsrc` 里**没有** `assets/hexcasting/lang/` 目录，
  所以拿不到原版官方的 `en_us.json`（`doc/resources/assets/` 下只有 farmersdelight 的）。
- 全盘找过 `hexcasting*.jar`：没有。
- `Common/src/main/resources/assets/hexcasting/patchouli_books/thehexbook/en_us/` **在**，
  里面是英文手册正文与图案名 —— 可以当**英文用词风格**的依据（例如它怎么称呼
  "Amethyst Dust"、"Spellbook"、"Impetus"、"Directrix"），但不能直接当词条表用。

### 建议做法

1. 以 `_tools/hexlang_zh.json`（官方 zh_cn 展平表）的键为准，
   配一张**类名 → 原版蛇形 id** 的映射（`gen_localization.ps1` 里已有中文那版映射逻辑，直接复用）。
2. 英文名优先用 Hex Casting 的官方叫法（从英文手册正文里核对用词），
   自创物品（`HexBookItem`、`WallScrollFrame*`、`DevStaff`）自己起名。
3. 写成 `_tools/gen_localization_en.ps1`，与中文那版同构：**只补缺失键**，不动已有条目。
4. 把断言⑦ 扩成**双语**：每个可实例化物品/方块在 zh-Hans 与 en-US 里都要有词条。
   这一条是防复发的关键，否则下次加物品又会只写中文。

> 注意 hjson 的坑（踩过）：**不要**把 `}   Key: {` 挤在一行，断言的正则认不出来；
> 文件保持无 BOM UTF-8 会让 PS 5.1 读中文出问题 —— 这两个文件带 BOM。
