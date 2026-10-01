# 附属（addon）兼容：结构、开关与三个附属的移植计划

目标：以咒法学为中心，把附属集成进**同一个 tmod**，每个附属单独开关、能快速找到「某个功能在哪个文件」、能单独对拍上游。
以后还要适配几十个附属，所以下面的规矩在框架（P0）落地时一起做成 `_tools/check_arch.ps1` 的断言；没写进断言的规矩等于没有。

当前批次（2026-10-01，用户指定，来源 https://addons.hexxy.media/）：**HexParse、Hexcessible、HexDebug**。

**进度（2026-10-01 核对）**：三个附属的 P0–P3d 都已做完。每个附属哪些功能做了、在哪个文件，看自动生成的 [ADDONS.generated.md](ADDONS.generated.md)：
标「（待做）」的只剩依赖其他还没移植的附属的插件 / 智能签名；HexParse 表里「与 HexDebug 联动」一行也标着待做，但代码在被联动的
HexDebug 那边（`Addons/HexDebug/Interop/HexParseCommentRenderer.cs`），已经做了。下面各附属表里的「泰拉侧打算」和「做的顺序」是动工前的计划，
留作设计记录；与上游的偏差以各附属自己的 `README.md` 为准。

## 上游与版本（钉死，与本地 jar 同一版本）

| 附属 | 版本（Modrinth 1.20.1 Fabric 最新） | 本地 jar（`D:\DeepSeekHarness\mod\`） | 源码（`D:\DeepSeekHarness\addons_src\`） | 许可 | 规模 |
|---|---|---|---|---|---|
| HexParse（YukkuriC） | 1.20.1-1.11.2 | `hexParse-fabric-1.20.1-1.11.2.jar` | `hexparse` @ `272b689`（提交名 v1.11.2） | MIT | Java/Kotlin 约 5300 行 |
| Hexcessible（Ruby / tizu，ElNico56） | 0.3.1 | `hexcessible-0.3.1.jar` | `hexcessible` @ tag `v0.3.1` | The JSON License（MIT + 「用于善」条款） | Java 约 3600 行，**纯客户端** |
| HexDebug（object-Object） | 0.9.0+1.20.1 | `hexdebug-fabric-0.9.0+1.20.1.jar` | `hexdebug` @ tag `v0.9.0+1.20.1` | MIT | Kotlin 约 10400 行 |

三个许可都允许并入本模组（本模组整体 CC BY-NC-SA 3.0）；条件是保留各自的版权声明与许可全文 → `HexCastingTerraria/LICENSE-<附属>.txt` + CREDITS.md 一节。
中文文本：HexParse、HexDebug 的 jar / 源码里有官方 `zh_cn`；Hexcessible 也有 `zh_cn.json`。一律用官方中文，和本体书的做法一致。

## 决定：集成进同一个 tmod，开关放在附属「运行的那一侧」

- 附属代码直接用本模组的图案注册表、iota、栈机、画布 —— 拆成独立 tmod 就得把这些做成公开 API，tML 一改接口几十个 tmod 一起改。集成后共用离线测试、共用打包，联机不会出现版本对不上。
- **开关的位置**按上游附属本身跑在哪一侧决定：

| 类型 | 附属 | 开关在哪 | 关掉 = |
|---|---|---|---|
| 有物品 / 方块 / 图案 / 指令（两端都要有） | HexParse、HexDebug | 服务端配置 `HexAddonsConfig`，**需要重载**（`[ReloadRequired]`） | **没装**：`IsLoadingEnabled` 返回 false，物品、方块、图案、指令、书的分类都不加载；联机时 tML 自动让客户端的开关跟服务器一致 |
| 纯客户端界面 | Hexcessible | 客户端配置 `HexAddonsClientConfig`，不用重载 | 画布恢复原版操作；只影响自己，联机别人装不装无所谓（上游就是纯客户端模组） |

- 默认**全部关**（本体保持原版体验，「一切以原版为基准」）；每个附属还带自己的子开关（上游有几项配置就照搬几项），放在同一页里、按附属分组。

### 开关的实际效果（已对照 tML 1.4.4.9 源码确认：ModNet.SyncClientMods、ConfigManager.HandleInGameChangeConfigPacket、WorldIO.LoadModData）

- **建世界**：这三个附属都不改世界生成、不往箱子里加战利品 → 开不开附属建出来的世界一样。区别只在之后存进去的东西（放下的剪接台、HexParse 的大法术解锁表）。
- **关掉以后再进旧世界 / 旧角色**：附属的物品变成 tML 的「未加载物品」、方块变成「未加载方块」、世界数据由 tML 的 UnloadedSystem 原样保管；重新打开开关全部恢复，不会丢。
- **联机**：服务端开关由**开服的人**决定（「创建并游玩」= 房主自己的设置；专用服务器 = 服务器的配置文件）。别人进服时，tML 把服务器的开关发过来；和自己本地不一样就弹「需要重载」，点一下自动重载再进服，不用手动改，也不会改动他自己本地保存的设置。所有人用的是同一个 tmod，不用另外下载。
- **游玩中改**：需要重载的开关，联机时服务器会直接拒绝（tML 提示「这些更改需要重载，无法保存」）→ 要改就停服、改配置、重开；单人可以在配置菜单里改，tML 会提示重载模组后生效。Hexcessible 这种客户端开关每个人随时自己改，立即生效，互不影响。
- **开关在加载内容之前就读好了**：tML 先 `AutoloadConfig`（把配置从磁盘读进来；联机重载时用服务器发来的值）再 `Autoload` 内容，所以附属的 `IsLoadingEnabled` 读 `HexAddonsConfig.Instance` 是可靠的（Mod.cs / ConfigManager.Add 已确认）。只有「总开关」标 `[ReloadRequired]`；各附属的子选项不标，房主能在游戏里随时改。
  **例外是配置自己的 `OnChanged`**：tML 在加载配置的过程中就会调它，这时别的配置还是 null。2026-10-01 客户端配置的 OnChanged 去读服务端的附属开关，空引用让整个模组被禁用（c7958a7 修）。现在 OnChanged 只用自己的字段，附属的 `IsEnabled` 读配置一律空安全（配置还没加载时当作关），`check_arch` 有一条断言：OnChanged 和它调到的方法在判断 `gameMenu` 之前不许碰别的配置。
- **每个世界的大法术笔顺**：生成时要避开所有已有图案的签名。这里必须避开**全部附属**的图案（不管开没开），否则一个在附属关着时建的世界，可能生成一条和附属图案撞车的大法术笔顺，开了附属以后那条大法术就画不出来（查表时普通图案优先）。附属图案的签名是编译期就确定的静态数据，登记表里一直都有，只是没开时不参与识别。
- **本体物品里存着附属的 iota**（例如核心里存了 HexParse 的注释 iota，然后把 HexParse 关了）：原版 Hex Casting 遇到不认识的 iota 类型会变成垃圾，数据就丢了。本模组要求更严：P0 的 iota 扩展点必须把**不认识的 iota 原样保留**（显示成「未加载的 iota」，执行时当垃圾处理，存档时原样写回），重新打开附属就恢复。这条做进 IotaTag 的自检。附属的图案本身只是一串角度，关掉后画出来就是无效图案，和原版没装这个附属时一样。

## 目录（每个附属一模一样）

```
HexCastingTerraria/Addons/
├── AddonRegistry.cs              所有附属的登记表（Id → 开关 → 注册入口），唯一的总入口
├── AddonContent.cs               IsLoadingEnabled 的公共基类（附属的 ModItem / ModTile 继承它）
└── <附属名>/                     HexParse/ Hexcessible/ HexDebug/
    ├── addon.json                机器可读的清单：上游、版本、许可、命名空间、功能 → 文件表
    ├── README.md                 给人看的：这个附属是什么、泰拉侧怎么玩、与上游的偏差
    ├── Core/                     纯逻辑（零 Terraria / XNA 引用，和主 Core 同一条断言），离线测试直接编译
    ├── Game/                     游戏侧：图案注册胶水、指令、物品、方块、联机消息、ModSystem
    ├── Client/                   （可选）纯客户端绘制 / 输入（实际三个附属都没建，客户端代码也放在 Game/）
    ├── Interop/                  （可选）跨附属联动（目前只有 HexDebug 有）
    └── Assets/                   （可选）贴图（目前只有 HexDebug 有）
tests/vmtest/Addons/<附属名>Tests.cs   该附属的离线用例
Config/HexAddonsConfig.cs              服务端开关 + 各附属的服务端子配置
Config/HexAddonsClientConfig.cs        客户端开关 + 各附属的客户端子配置
```

- 图案 id 用上游命名空间（`hexparse:code2focus`、`hexdebug:breakpoint/before`）；`PatternRegistry` 按命名空间登记，附属没加载 = 这些签名不参与识别。
- 书：附属的分类 / 条目带 `Addon = "<id>"`，没加载就不显示；书页由 `_tools/gen_book_content.py` 从上游 jar 的 `patchouli_books` + 官方中文生成。
- 贴图：和本体一样从上游 jar 取（`_tools/gen_textures.py` 加一个附属入口），最近邻 ×2。
- `build.txt` 的 `buildIgnore` 加上 `Addons/*/addon.json, Addons/*/README.md`，不打进 .tmod。

## addon.json（清单）

```json
{
  "id": "hexparse",
  "name": "HexParse",
  "side": "both",
  "upstream": { "repo": "https://github.com/YukkuriC/HexParseMod", "version": "1.20.1-1.11.2", "commit": "272b689",
                "license": "MIT", "authors": ["YukkuriC"], "jar": "D:/DeepSeekHarness/mod/hexParse-fabric-1.20.1-1.11.2.jar",
                "src": "D:/DeepSeekHarness/addons_src/hexparse" },
  "namespace": "hexparse",
  "patterns": { "file": "common/src/main/java/io/yukkuric/hexparse/actions/HexParsePatterns.java" },
  "requires": [],
  "features": [ { "name": "代码 → iota 解析器", "files": ["Core/Parser.cs", "Core/Tokenizer.cs"], "upstream": ["parsers/ParserMain.java", "parsers/CodeCutter.kt"] } ]
}
```

断言（check_arch，P0 落地）：
1. 每个 `Addons/<名>/` 都有 `addon.json` 和 `README.md`，且在 `AddonRegistry` 里登记；`side` 与开关所在的配置一致。
2. **文件夹里每个 .cs 都出现在某个 feature 的 files 里**（没有孤儿文件 —— 这就是「快速找到功能在哪」的保证），列出的文件都存在。
3. `Addons/*/Core/` 零 Terraria / XNA 引用；两个测试工程编译 `Addons/*/Core/**`。
4. 图案形状对拍上游：`check_patterns_vs_original.py` 读每个清单的 `patterns.file`，逐条比角度串和起笔方向。
5. 许可：`LICENSE-<名>.txt` 存在，CREDITS.md 里有该附属一节。
6. 附属的 ModItem / ModTile / ModCommand 都走 `AddonContent`（开关关着时不加载），不许漏。

`_tools/gen_addons_index.py` 从所有清单生成 `ADDONS.generated.md`：附属一览（开关名、上游版本、完成度）+ 每个附属的「功能 → 文件」表。

---

## 共享基础设施

P0 只做三个附属都要用的（已完成的标「P0 已做」）；只有某个附属才用的，放到第一个用它的阶段开头做。

| 基础设施 | 谁用 | 说明 |
|---|---|---|
| 附属框架（AddonRegistry、两份配置、AddonContent、清单断言、测试工程编译 Addons/*/Core、索引生成）（P0 已做） | 全部 | 上面「目录」「断言」两节 |
| `PatternRegistry` 按命名空间登记附属图案 + 附属图案的动作注册（P0 已做） | HexParse、HexDebug | 现在 188 条是生成数据一次装进去的；加一个 `RegisterAddonPatterns(ns, …)`，大法术 / 每世界笔顺不受影响 |
| 书：附属分类 / 条目（P0 已做入口 HexAddon.AddBookContent；生成器读附属 jar 在 P1 做） | HexParse、HexDebug | Hexcessible 没有书页 |
| **画布输入状态机**（空闲 / 鼠标绘制 / 键盘绘制 / 自动补全 / 改别名）（P2 已做：`Addons/Hexcessible/Game/HexcessibleCanvas.cs`） | Hexcessible（HexDebug 的剪接台小画布也要） | 照 Hexcessible 的 `DrawState` 拆：现在画布的输入逻辑都堆在 HexClientSystem 里，先抽成状态机，本体行为不变 |
| **文本输入框**（泰拉里打字：拦住按键不触发游戏操作、中文输入法）（已做：Hexcessible 画布、HexDebug 剪接台界面 `SplicingTableUI.cs`） | Hexcessible 自动补全 / 改别名、HexDebug 剪接台 | 用泰拉自己的文本输入（`Main.GetInputText` + `PlayerInput.WritingText`） |
| **剪贴板**（读 / 写系统剪贴板）（P1 已做：`Addons/HexParse/Game/HexParseIO.cs`、联机走 `HexParseNet.cs`） | HexParse、HexDebug 剪接台 | `ReLogic.OS.Platform.Get<IClipboard>()`；联机时服务端向客户端要（HexParse 的 MsgPull/PushClipboard） |
| 新 iota 类型的扩展点（序列化、联机编码、显示）（P0 已做） | HexParse 注释 iota、HexDebug 认知危害 iota | `IotaSerializer` / `IotaWire` / `IotaTag` 目前是封闭的 switch，改成可登记；**不认识的类型原样保留**（见「开关的实际效果」最后一条） |
| 服务端附属开关只许房主改（P0 已做） | HexParse、HexDebug | `HexAddonsConfig.AcceptClientChanges`：只接受房主（`Main.countsAsHostForGameplay`）的修改；需要重载的项 tML 本来就拒绝 |
| 多格方块 + 带界面的方块实体模板 | HexDebug 剪接台、核心框架 | 本体已有板岩 / 原动力的方块实体，照那个模式。P3b 已做：剪接台 / 制念台 / 核心框架都是 1×1 带方块实体（`AddonTileEntity`），没用到多格 |

---

## HexParse（服务端开关，side = both）

「代码文本 <-> iota 列表」互转的工具附属：在聊天栏打 `/hexParse <代码>` 就把代码写进手上的核心 / 法术书，反过来也能把核心里的咒术读成代码。

| 功能 | 上游文件 | 泰拉侧打算 |
|---|---|---|
| 分词（逗号 / 空格 / 换行 → 缩进、`//` `/* */` 注释、`"…"` 字符串） | parsers/CodeCutter.kt | 照搬（Core） |
| 解析主循环（`[ ]` 嵌套、未知符号提示、括号不配对恢复、每个 iota 的媒质消耗） | ParserMain.java, CostTracker.kt | 照搬（Core） |
| token：图案名（长 / 短 id）、`( ) { } \ del undo`、大法术（按解锁表，未解锁留 `<id?>` 占位注释） | str2nbt/ToPattern.java, hooks/PatternMapper.java | 照搬 |
| token：`num_x`、`mask_-v`、`_wedsaq`、数字、`vec_x_y_z`、true/false/null/garbage、self | ConstParsers.java, ToMiscConst.kt, misc/NumEvaluatorBrute.java | 照搬 |
| token：`entity_<uuid>` | ToEntity.java | **偏差**：泰拉实体没有 UUID —— 一律按编号：`entity_player_编号`、`entity_npc_编号`、`entity_item_编号`、`entity_projectile_编号`、`entity_itemframe_编号`（物品框，2026-10-01 物品框当实体以后加的） |
| 别名（内置 hermes / iris / thoth / pop / 1.19 旧名…）+ 玩家自定义；宏 `#名` 展开（防递归、缩进叠加） | ToDialect.java, macro/* | 照搬；宏存客户端 `Main.SavePath/HexParse/macro.json`。**偏差**：泰拉侧解析在本人客户端做，宏不用发给服务端（上游在服务端解析，进服时才要发） |
| 反向：iota → 文本（图案名 / `num_` / `mask_` / `_角度`、数字、向量、注释、实体…；未知类型 `nbt_<base64>`） | nbt2str/*, FallbackBinaryParser.kt | 照搬；`nbt_` 用本模组的 IotaTag 编码 |
| 注释 iota（绿色文字、执行时跳过；换行缩进 `tab_N`） | hooks/CommentIota*.java | 新 iota 类型（走 P0 的 iota 扩展点） |
| 大法术解锁表（BY_SCROLL / ALL / DISABLED；代码里 DISABLED = 全禁，文档写反了，以代码为准） | hooks/GreatPatternUnlocker.java | 存世界 |
| 图案 ×8：解码之策略、编码之策略、内化卓越法术、压缩注释之纯化、换行之纯化、捐赠、编译术 / 注释转换（上游没装 MoreIotas 时是空操作） | actions/*.kt | 照搬，命名空间 hexparse |
| 指令 `/hexParse`：`<代码> [重命名]`、read / read_signatures / read_hexbug / share、clipboard / clipboard_angles / clipboard_hexpattern、mind_stack peek/push/push_clipboard、macro / dialect list/define/define_clipboard/remove、conflict、lehmer、donate、learn_great、unlock_great | commands/* | 照搬；**偏差**：泰拉聊天不能点击复制 → 读出来的代码直接写进剪贴板并提示；泰拉物品不能改名 → 核心上存一个自定义名显示在名字行 |
| 剪贴板（服务端向客户端要剪贴板、客户端预检后回传） | network/MsgPull/PushClipboard.java | 照搬（单人直接读） |
| `.hexpattern` 格式（按英文图案名映射） | parsers/hexpattern/* | 照搬（用 hexsrc_assets 的 en_us 名） |
| 嵌套列表 / 括号彩色显示 | mixin/iota/* | 已做（2026-10-01，本体 iota 显示按原版重做以后接上）。**偏差**：开关是服务端「HexParse 设置」里的「嵌套彩色显示」（房主定），不是上游那样各人的客户端设置，见 HexParse README |
| 书：「HexParse指令」分类 4 条 + 图案条目 | jar assets/…/thehexbook, lang/zh_cn.json | 生成器读 jar |
| 配置 10 项 | config/HexParseConfig.java | 照搬到 HexAddonsConfig.HexParse |
| 其他附属的插件解析（MoreIotas / Hexal / Hexcellular / HexPose / Oneironaut / Ephemera …） | str2nbt/plugins, nbt2str/plugins | 那些附属移植后再接；现在不做 |

验收：离线用例覆盖解析 / 反向输出往返（上游仓库的示例代码逐条对拍）；游戏里 `/hexParse` 写核心、读核心、剪贴板往返。

---

## Hexcessible（客户端开关，side = client）

给施法界面加「无障碍」操作：键盘画图、按名字搜索图案、别名、悬停说明。上游是纯客户端模组，不加物品、不加图案。

| 功能 | 上游文件 | 泰拉侧打算 |
|---|---|---|
| 键盘绘制：q w e a d 画、s / 退格撤一笔、h j k l / 方向键移起点、r / Shift+r / 滚轮转起笔方向、Enter / 空格 / Tab / 左键施放、按键提示、虚影预览、找最近的空位放 | drawstate/KeyboardDrawing.java, accessor/CastRef.java, Utils.java | 照搬；**泰拉适配**：画布开着时这些键不能再触发移动 / 跳跃 / 快捷栏（P0 的文本输入拦截） |
| 自动补全（Ctrl+空格）：按名字 / 别名 / 签名搜索图案，分页选择，放到光标处；每世界大法术要手持对应远古卷轴才补全 | drawstate/AutoCompleting.java, entries/PatternEntries.java | 照搬；名字用本模组的中文图案名 + 英文 id |
| 别名（Ctrl+E 给悬停的图案起名，搜索时可用） | drawstate/AliasChanging.java | 照搬；别名存客户端 `Main.SavePath/Hexcessible/aliases.json` |
| 智能签名：数字（输入数值 → 最短数字图案，查 numbers.txt）、簿记员（输入 `-v-` → 掩码图案）、转义 | smartsig/Number.java, Bookkeeper.java, Escape.java, numbers.txt | 照搬（Core，可离线测）；其余 smartsig 是给别的附属的（Overevaluate、Complexhex、Hexical、HexThings），那些附属移植后再接 |
| 悬停说明：鼠标停在已画的图案上 → 名字 + 参数；画的时候显示当前识别结果 | drawstate/Idling.java, MouseDrawing.java | 照搬；和本体已有的「识别到：」提示合并，不重复显示 |
| 按 N 打开书里对应条目 | mixin/KeyDocsScreenMixin.java, entries/BookEntries.java | 照搬（开咒法学之书并跳到条目） |
| 显示选项：界面变暗、显示全部格点、隐藏玩家周围飘着的图案、签名大写、左下角快捷键提示、提示框固定在左上 | mixin/DimmedMixin.java, ShowAllDotsMixin.java, FloatiesMixin.java, HexcessibleConfig.java | 照搬成客户端子开关 |
| Hexical 相关（禁止行走 / 禁止 Evoke） | NoHexical*Mixin.java | 不做（没有 Hexical） |
| HexDebug 剪接台小画布里也能键盘画 | DrawStateHexdbgInterop*Mixin.java | HexDebug 剪接台做完后接上（跨附属联动，见下） |

验收：离线用例覆盖数字 / 簿记员智能签名、最近空位查找、签名合法性；游戏里键盘画一条图案并施放、Ctrl+空格搜「召雷」放出来。

---

## HexDebug（服务端开关，side = both）

调试与编辑咒术。两大块：**调试杖**（一步一步执行咒术、看栈 / 调用栈 / 断点）和**剪接台**（像文本编辑器一样编辑核心 / 法术书里的咒术）。

| 功能 | 上游文件 | 泰拉侧打算 |
|---|---|---|
| 物品：调试杖、淬灵调试杖（装一段咒术，每用一次按当前步进模式执行；淬灵版能用 Ctrl + 潜行 + 滚轮切换调试线程，同时调试多段） | items/DebuggerItem.kt, debugger/HexDebugger.kt | 照搬（HexDebugger 的步进逻辑放 Core，可离线测） |
| 物品：运行杖、淬灵运行杖（调试暂停时，往被暂停的栈机里临时施法） | items/EvaluatorItem.kt, casting/eval/* | 照搬 |
| 步进模式：单步进入 / 跳过 / 跳出、继续、重启、停止（潜行 + 滚轮切换模式） | debugger/HexDebugger.kt, Enums.kt | 照搬；**泰拉适配**：模式切换用本体已有的「潜行 + 滚轮」 |
| 调试信息：栈、渡鸦之思、已执行图案数、调用栈（由续延推出来） | debugger/HexDebugger.kt | **偏差**：上游是在外部编辑器里看；泰拉侧做一个游戏内调试面板（HUD），显示同样的内容 |
| 断点：图案「前断点」「后断点」；「未捕获的事故」时自动暂停 | casting/actions/OpBreakpoint.kt, 续延类型 breakpoint | 照搬（新续延帧类型） |
| 图案 ×22：认知危害、是否在调试、前 / 后断点、制作调试杖 ×2、剪接台读写（选区、视野、列表 / 剪贴板的法术书页码、剪贴板、制念台的咒术） | registry/HexDebugActions.kt, casting/actions/* | 照搬，命名空间 hexdebug；形状对拍上游 |
| iota：认知危害（被读到就让读的人出事故） | casting/iotas/CognitohazardIota.kt | 新 iota 类型（P0 扩展点） |
| 方块：剪接台、制念台（启迪版，多一个「咒术」槽，能用图案读写） | blocks/splicing/*, gui/splicing/SplicingTableScreen.kt, splicing/* | 照搬：放核心 / 法术书进去，界面里选中、移动、复制、删除、剪贴板槽、撤销、小画布画图案、媒质条；**泰拉适配**：界面用本体书的绘制工具重写 |
| 方块：核心框架（放一个核心，给法术环读写用） | blocks/focusholder/*, items/FocusHolderBlockItem.kt | 照搬 |
| 调试法术环（调试杖对着原动力用） | debugger/circles/* | 照搬（本体法术环已有） |
| 外部调试：调试适配协议（DAP）服务器，VSCode 的 hex-casting 插件连上来单步、看变量 | adapter/*, adapter/proxy/* | 已做（2026-10-01）：客户端配置里的「开放调试端口」，默认关、只监听本机（上游默认开、4444）；游戏内调试面板照常可用 |
| 配置：客户端（颜色列表、剪接台显示）、服务端（调试器范围等） | config/HexDebugClientConfig.kt, HexDebugServerConfig.kt | 照搬到两份附属配置 |
| 书：HexDebug 分类（中文官方） | resources/…/patchouli_books, lang/zh_cn.flatten.json5 | 生成器读 |
| 合成：调试杖 / 剪接台 / 核心框架的配方、「往核心框架里装核心」的特殊配方 | datagen/recipes/*, recipes/FocusHolderFillingShapedRecipe.kt | 照搬，材料按本体已有的对应规则换成泰拉物品 |

验收：离线用例覆盖步进（进入 / 跳过 / 跳出在嵌套 Hermes / Thoth 里的停点）、断点、调用栈生成；游戏里调试杖逐步执行一段咒术并在面板里看到栈变化；剪接台里编辑核心的咒术并保存。

---

## 跨附属联动（两边都开着才生效）

| 联动 | 上游位置 | 何时做 |
|---|---|---|
| Hexcessible 在 HexDebug 剪接台的施法界面里：照常能用（上游只显示悬停 / 手画提示、不能打字；按玩家反馈放开，见 Hexcessible README） | hexcessible `DrawStateHexdbgInterop*Mixin` | 已做 |
| HexParse 的 `read_hexbug`（按 hexbug 格式读） | hexparse commands | 已做（随 HexParse）。其实不算跨附属：hexbug 指的是 Discord 上 HexBug 机器人 `/patterns hex` 的格式，不依赖 HexDebug |
| HexDebug 剪接台里画 HexParse 的注释 iota | hexparse `compat/hexdebug/CommentRenderer.kt` | 已做（HexDebug/Interop） |

（2026-10-01 核对上游源码：HexParse 没有「读写剪接台」的 IO 方式，原表那一行是计划时的误记，已删。）

联动代码放在**被联动的那个附属**目录里，`addon.json` 的 `requires` 里写明「可选依赖」，框架保证两边都加载才注册。
实际落地：HexParse 注释在剪接台里画 → `Addons/HexDebug/Interop/HexParseCommentRenderer.cs`（HexDebug 的 `requires` 写了 hexparse）；
Hexcessible 在剪接台画布里能用 → 是 Hexcessible 自己的画布逻辑，在 `Addons/Hexcessible/Game/HexcessibleCanvas.cs`。

---

## 做的顺序（每一步结束都跑 `run_all.ps1 -Package` 全绿、提交；游戏内验证步骤写进 TESTING_CHECKLIST）

| 阶段 | 内容 | 为什么这个顺序 |
|---|---|---|
| **P0 框架** | 附属框架（登记表、入口基类、Addon* 内容基类）、两份开关配置（只许房主改服务端的）、三个附属的清单 / README / 许可、附属图案按命名空间声明与启用、iota 扩展点（不认识的原样保管、联机不丢）、书的附属入口、check_arch 第 12 项、ADDONS.generated.md | 三个附属都依赖；先把规矩做成断言，后面每个文件都有归属 |
| **P1 HexParse** | Core（分词、解析、反向、宏 / 别名、智能数字）+ 离线用例 → 游戏侧（指令、8 个图案、注释 iota、大法术解锁、书、配置） | 纯文本、最好测，离线用例能覆盖大部分；也最能帮玩家写复杂咒术 |
| **P2 Hexcessible** | 键盘绘制 → 智能签名 → 自动补全 / 别名 → 悬停说明 / 按 N 查书 → 显示选项 | 纯客户端、依赖 P0 的状态机和文本输入；玩家已经在问键盘画图 |
| **P3a HexDebug 调试** | 步进核心（Core）+ 调试杖 / 运行杖 + 断点 + 游戏内调试面板 + 22 个图案 + 认知危害 iota + 书 | 最大的附属，先做不需要新界面的部分 |
| **P3b HexDebug 剪接台** | 剪接台 / 制念台 / 核心框架 + 编辑界面 + 配方 | 需要一整套新界面 |
| **P3c 跨附属联动** | 上一节的三条 | 两边都做完才有意义 |
| **P3d DAP（可选）** | 外部调试服务器，默认关（已做，2026-10-01） | 需求最小、牵涉本地端口，最后做 |

预计规模（C#）：P0 约 1500 行，HexParse 约 3500 行，Hexcessible 约 2000 行，HexDebug 约 5000 行（不含 DAP）。

## 新增一个附属的步骤（以后的几十个都照这个来）

1. 在 addons.hexxy.media 找到上游，下载与 Modrinth 1.20.1 Fabric 最新版一致的 jar 放 `D:\DeepSeekHarness\mod\`，源码克隆到 `addons_src/<名>` 并钉到同一版本；读许可。
2. 抄模板建目录、写 `addon.json`（先把上游的功能逐项列进 features，files 留空 = 待做）；判断 side，决定开关放服务端还是客户端配置。
3. 在 `AddonRegistry` 和对应配置里各加一行。
4. 先做 Core（纯逻辑 + 离线用例），再做 Game / Client。
5. 偏差写进该附属的 `README.md`；许可文件 + CREDITS；跑 `run_all.ps1 -Package`。
