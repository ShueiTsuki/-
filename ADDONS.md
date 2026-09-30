# 附属（addon）兼容：结构与规矩

目标：以后要适配几十个咒法学附属。每个附属都要能**快速找到「某个功能在哪个文件」**、能单独开关、能单独对拍上游。
下面的规矩在第一个附属（HexParse）落地时一起做成 `_tools/check_arch.ps1` 的断言；没写进断言的规矩等于没有。

## 决定：集成进同一个 tmod，每个附属一个开关

- 附属代码要直接用本模组的图案注册表、iota、栈机、手持物品 —— 这些是内部类，拆成独立 tmod 就得把它们做成公开 API，
  而 tML-dev 每次更新都会动接口，几十个 tmod 要一起跟着改。
- 集成后共用离线测试、共用打包；联机不会出现「两个 mod 版本对不上」。
- **每个附属在服务端配置「附属兼容」页里有一个开关，默认关**：关着 = 没装（它的图案画出来是无效图案、指令拒绝、书里的分类不出现）。

## 目录（每个附属一模一样）

```
HexCastingTerraria/Addons/
├── AddonRegistry.cs              所有附属的登记表（Id → 开关 → 注册入口），唯一的总入口
└── <附属名>/                     例：HexParse/
    ├── addon.json                机器可读的清单（见下）：上游、版本、许可、命名空间、功能 → 文件表
    ├── README.md                 给人看的：这个附属是什么、泰拉侧怎么玩、与上游的偏差
    ├── Core/                     纯逻辑（零 Terraria / XNA 引用，和主 Core 同一条断言），离线测试直接编译
    ├── Game/                     游戏侧：图案注册胶水、指令、物品、联机消息、ModSystem
    └── Client/                   （可选）纯客户端绘制 / 输入
tests/vmtest/Addons/<附属名>Tests.cs   该附属的离线用例（Program 里调用）
```

- 图案 id 用上游的命名空间（`hexparse:code2focus`），`PatternRegistry` 按命名空间查开关：关着的附属，图案不参与识别。
- 书：附属的分类 / 条目带 `Addon = "<id>"`，关着时不显示；书页由 `_tools/gen_book_content.py` 从上游 jar 的 `patchouli_books` + 官方中文生成。
- 配置：`Config/HexAddonsConfig.cs`（服务端）—— 每个附属一个 `bool` 开关 + 一个同名的设置子对象（上游有几项配置就照搬几项）。
- 上游源码放在仓库外 `D:\DeepSeekHarness\addons_src\<附属名>`（git clone，钉在和 jar 同一个版本），路径写进 `addon.json`。

## addon.json（清单）

```json
{
  "id": "hexparse",
  "name": "HexParse",
  "upstream": { "repo": "https://github.com/YukkuriC/HexParseMod", "version": "1.20.1-1.11.2", "commit": "272b689",
                "license": "MIT", "authors": ["YukkuriC"], "jar": "D:/DeepSeekHarness/mod/hexParse-fabric-1.20.1-1.11.2.jar",
                "src": "D:/DeepSeekHarness/addons_src/hexparse" },
  "namespace": "hexparse",
  "patterns": { "file": "common/src/main/java/io/yukkuric/hexparse/actions/HexParsePatterns.java" },
  "features": [ { "name": "代码 → iota 解析器", "files": ["Core/Parser.cs", "Core/Tokenizer.cs"], "upstream": ["parsers/ParserMain.java", "parsers/CodeCutter.kt"] } ]
}
```

断言（check_arch）：
1. 每个 `Addons/<名>/` 都有 `addon.json` 和 `README.md`，且在 `AddonRegistry` 里登记。
2. **文件夹里每个 .cs 都出现在某个 feature 的 files 里**（没有孤儿文件 —— 这就是「快速找到功能在哪」的保证），列出的文件都存在。
3. `Addons/*/Core/` 零 Terraria / XNA 引用；两个测试工程编译 `Addons/*/Core/**`。
4. 图案形状对拍上游：`check_patterns_vs_original.py` 读每个清单的 `patterns.file`，逐条比角度串和起笔方向。
5. 许可：`NOTICE.txt` 与 `CREDITS.md` 里有该附属的署名（MIT 等许可的全文放 `LICENSE-<名>.txt`）。

`_tools/gen_addons_index.py` 从所有清单生成 `ADDONS.generated.md`：附属一览（开关名、上游版本、状态）+ 每个附属的「功能 → 文件」表。

## 新增一个附属的步骤

1. 克隆上游到 `addons_src/<名>`，钉版本；读许可。
2. 抄模板建目录、写 `addon.json`（先把上游的功能逐项列进 features，files 留空 = 待做）。
3. 在 `AddonRegistry` 和 `HexAddonsConfig` 里各加一行。
4. 先做 Core（纯逻辑 + 离线用例），再做 Game。
5. 偏差写进该附属的 `README.md`；跑 `run_all.ps1 -Package`。

---

## 第一个：HexParse（YukkuriC，MIT，v1.20.1-1.11.2，上游 5300 行）—— 功能清单（待做）

「代码文本 ↔ iota 列表」互转的工具附属。上游源码已克隆到 `D:\DeepSeekHarness\hexparse_src`（待移到 `addons_src/hexparse`）。

| 功能 | 上游文件 | 泰拉侧打算 |
|---|---|---|
| 分词（逗号/空格/换行→缩进、`//` `/* */` 注释、`"…"` 字符串） | parsers/CodeCutter.kt | 照搬（Core） |
| 解析主循环（`[ ]` 嵌套、未知符号提示、括号不配对恢复、每个 iota 的媒质消耗） | ParserMain.java, CostTracker.kt | 照搬（Core） |
| token：图案名（长/短 id）、`( ) { } \ del undo`、大法术（按解锁表，未解锁留 `<id?>` 占位注释） | str2nbt/ToPattern.java, hooks/PatternMapper.java | 照搬 |
| token：`num_x`、`mask_-v`、`_wedsaq`、数字、`vec_x_y_z`、true/false/null/garbage、self | ConstParsers.java, ToMiscConst.kt, misc/NumEvaluatorBrute.java | 照搬 |
| token：`entity_<uuid>` | ToEntity.java | **偏差**：泰拉实体没有 UUID —— 玩家 `entity_player_<名>`（别人的名字报 others_name），NPC `entity_npc_<编号>` |
| 别名（内置：hermes/iris/thoth/pop/1.19 旧名…）+ 玩家自定义；宏 `#名` 展开（防递归、缩进叠加） | ToDialect.java, macro/* | 照搬；宏存客户端 `Main.SavePath/HexParse/macro.json`，进服时发给服务端 |
| 反向：iota → 文本（图案名/`num_`/`mask_`/`_角度`、数字、向量、注释、实体…；未知类型 `nbt_<base64>`） | nbt2str/*, FallbackBinaryParser.kt | 照搬；`nbt_` 用本模组的序列化 |
| 注释 iota（绿色文字、执行时跳过；换行缩进 `tab_N`） | hooks/CommentIota*.java | 新 iota 类型（Core + 显示 + 联机编解码） |
| 大法术解锁表（BY_SCROLL / ALL / DISABLED；代码里 DISABLED = 全禁，文档写反了，以代码为准） | hooks/GreatPatternUnlocker.java | 存世界 |
| 图案 ×8：解码之策略、编码之策略、内化卓越法术、压缩注释之纯化、换行之纯化、捐赠、编译术 / 注释转换（上游没装 MoreIotas 时是空操作） | actions/*.kt | 照搬，命名空间 hexparse |
| 指令 `/hexParse`：`<代码> [重命名]`、read / read_signatures / read_hexbug / share、clipboard / clipboard_angles / clipboard_hexpattern、mind_stack peek/push/push_clipboard、macro / dialect list/define/define_clipboard/remove、conflict、lehmer、donate、learn_great、unlock_great | commands/* | 照搬；**偏差**：泰拉聊天不能点击复制 → 读出来的代码直接写进剪贴板并提示；泰拉物品不能改名 → 核心上存一个自定义名显示在名字行 |
| 剪贴板（服务端向客户端要剪贴板、客户端预检后回传） | network/MsgPull/PushClipboard.java | 照搬（单人直接读） |
| `.hexpattern` 格式（按英文图案名映射） | parsers/hexpattern/* | 照搬（用 hexsrc_assets 的 en_us 名） |
| 嵌套列表 / 括号彩色显示 | mixin/iota/* | 客户端配置开关 |
| 书：「HexParse指令」分类 4 条 + 图案条目 | jar assets/…/thehexbook, lang/zh_cn.json | 生成器读 jar |
| 配置 10 项 | config/HexParseConfig.java | 照搬到 HexAddonsConfig.HexParse |
| 其他附属的插件解析（MoreIotas / Hexal / Hexcellular / HexPose / Oneironaut / Ephemera …） | str2nbt/plugins, nbt2str/plugins | 那些附属移植后再接；现在不做 |
