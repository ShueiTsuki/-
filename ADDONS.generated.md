# 附属一览（自动生成，勿手改）

由 `_tools/gen_addons_index.py` 从各附属的 `addon.json` 生成。规矩与计划见 [ADDONS.md](ADDONS.md)。
「已做」= 功能表里列了本模组的文件；没列文件的功能还没做。

| 附属 | 上游版本 | 许可 | 开关 | 功能已做 |
|---|---|---|---|---|
| [HexDebug](#hexdebug) | 0.9.0+1.20.1 | MIT | 模组配置「咒法学 · 附属兼容（服务端）」，需要重载；联机由开服的人决定 | 5 / 15 |
| [HexParse](#hexparse) | 1.20.1-1.11.2 | MIT | 模组配置「咒法学 · 附属兼容（服务端）」，需要重载；联机由开服的人决定 | 15 / 18 |
| [Hexcessible](#hexcessible) | 0.3.1 | The JSON License | 模组配置「咒法学 · 附属兼容（客户端）」，随时改；只影响自己 | 10 / 12 |

<a id="hexdebug"></a>
## HexDebug

上游：https://github.com/object-Object/HexDebug（0.9.0+1.20.1，object-Object）；本地源码 `D:/DeepSeekHarness/addons_src/hexdebug`；
代码目录 `HexCastingTerraria/Addons/HexDebug/`；图案 / iota 命名空间 `hexdebug`。

| 功能 | 本模组文件 | 上游文件（相对 `Common/src/main/kotlin/gay/object/hexdebug/`） |
|---|---|---|
| 入口与开关 | `Game/HexDebugAddon.cs` | `HexDebug.kt`<br>`config/HexDebugServerConfig.kt`<br>`config/HexDebugClientConfig.kt` |
| 步进核心（单步进入 / 跳过 / 跳出、继续、重启、停止、调用栈） | `Core/DebugTypes.cs`<br>`Core/HexDebugger.cs`<br>`Core/DebugEnvironment.cs`<br>`Core/IotaText.cs` | `debugger/HexDebugger.kt`<br>`debugger/DebugStepResult.kt`<br>`debugger/Enums.kt`<br>`debugger/IotaMetadata.kt`<br>`debugger/SharedDebugState.kt`<br>`debugger/allocators/Allocator.kt`<br>`debugger/allocators/SourceAllocator.kt`<br>`debugger/allocators/VariablesAllocator.kt` |
| 调试杖 / 淬灵调试杖 | （待做） | `items/DebuggerItem.kt`<br>`items/base/ShiftScrollable.kt`<br>`items/base/ItemPredicateProvider.kt`<br>`casting/eval/DebuggerCastEnv.kt` |
| 运行杖 / 淬灵运行杖 | （待做） | `items/EvaluatorItem.kt`<br>`casting/eval/FakeCastEnv.kt` |
| 断点 | `Core/FrameBreakpoint.cs`<br>`Core/HexDebugActions.cs` | `casting/actions/OpBreakpoint.kt`<br>`casting/eval/FrameBreakpoint.kt`<br>`registry/HexDebugContinuationTypes.kt` |
| 游戏内调试面板（偏差：上游在外部编辑器里看） | （待做） | `adapter/DebugAdapter.kt` |
| 图案 x22 | `Core/HexDebugPatterns.cs` | `registry/HexDebugActions.kt`<br>`casting/actions/OpIsDebugging.kt`<br>`casting/actions/splicing/` |
| 认知危害 iota | `Core/HexDebugActions.cs` | `casting/iotas/CognitohazardIota.kt`<br>`registry/HexDebugIotaTypes.kt` |
| 剪接台 / 制念台 | （待做） | `blocks/splicing/`<br>`gui/splicing/`<br>`splicing/`<br>`casting/eval/SplicingTableCastEnv.kt`<br>`networking/msg/MsgSplicingTable*`<br>`resources/splicing/SplicingTableIotasResourceReloadListener.kt`<br>`api/client/splicing/`<br>`api/splicing/` |
| 核心框架 | （待做） | `blocks/focusholder/`<br>`items/FocusHolderBlockItem.kt`<br>`recipes/FocusHolderFillingShapedRecipe.kt` |
| 调试法术环 | （待做） | `debugger/circles/`<br>`mixin/MixinBlockEntityAbstractImpetus.java`<br>`mixin/MixinCircleExecutionState.java` |
| 联机消息（调试状态、运行杖） | （待做） | `networking/HexDebugNetworking.kt`<br>`networking/handler/`<br>`networking/msg/MsgDebuggerStateS2C.kt`<br>`networking/msg/MsgEvaluatorClientInfoS2C.kt`<br>`networking/msg/MsgEvaluatorStateS2C.kt`<br>`networking/msg/MsgPrintDebuggerStatusS2C.kt` |
| 配方 | （待做） | `datagen/recipes/`<br>`recipes/FlyswatterQuenchingShapedRecipe.kt` |
| 书：调试 / 剪接台 / 核心框架条目 | （待做） | `assets/hexcasting/patchouli_books/thehexbook`<br>`assets/hexdebug/lang/zh_cn.flatten.json5` |
| 外部调试 DAP 服务器（最后做，默认关） | （待做） | `adapter/DebugAdapterManager.kt`<br>`adapter/IHexDebugLauncher.kt`<br>`adapter/LaunchArgs.kt`<br>`adapter/proxy/`<br>`networking/msg/MsgDebugAdapterProxy.kt` |

<a id="hexparse"></a>
## HexParse

上游：https://github.com/YukkuriC/HexParseMod（1.20.1-1.11.2，YukkuriC）；本地源码 `D:/DeepSeekHarness/addons_src/hexparse`；
代码目录 `HexCastingTerraria/Addons/HexParse/`；图案 / iota 命名空间 `hexparse`。

| 功能 | 本模组文件 | 上游文件（相对 `common/src/main/java/io/yukkuric/hexparse/`） |
|---|---|---|
| 入口与开关 | `Game/HexParseAddon.cs`<br>`Game/HexParseOptions.cs` | `HexParse.java`<br>`config/HexParseConfig.java` |
| 分词（逗号 / 空格 / 换行、注释、字符串） | `Core/CodeCutter.cs`<br>`Core/StringEscaper.cs` | `parsers/CodeCutter.kt`<br>`misc/StringEscaper.kt`<br>`misc/StringProcessors.java` |
| 解析主循环（嵌套、未知符号、括号恢复、媒质消耗） | `Core/CodeParser.cs`<br>`Core/HexParseSettings.cs`<br>`Core/IHexParseHost.cs` | `parsers/ParserMain.java`<br>`parsers/CostTracker.kt`<br>`parsers/IotaFactory.java`<br>`parsers/meta/IMetaCollector.java`<br>`parsers/meta/MetaHolder.java`<br>`parsers/interfaces/ConfigNums.java`<br>`parsers/interfaces/IConfigNumReceiver.java` |
| token：图案名、元符号、大法术占位 | `Core/PatternNames.cs` | `parsers/str2nbt/ToPattern.java`<br>`hooks/PatternMapper.java`<br>`parsers/str2nbt/IStr2Nbt.java` |
| token：常量（num_ / mask_ / _角度 / 数字 / 向量 / 布尔 / null / garbage / self） | `Core/NumEvaluator.cs` | `parsers/str2nbt/ConstParsers.java`<br>`parsers/str2nbt/BaseConstParser.java`<br>`parsers/str2nbt/ToMiscConst.kt`<br>`misc/NumEvaluatorBrute.java` |
| token：实体（泰拉偏差：没有 UUID，entity_player_编号 / entity_npc_编号） | `Game/HexParseHost.cs` | `parsers/str2nbt/ToEntity.java`<br>`parsers/IPlayerBinder.java` |
| 别名与宏 | `Core/CodeParser.cs`<br>`Game/HexParseMacros.cs` | `parsers/str2nbt/ToDialect.java`<br>`macro/MacroClient.java`<br>`macro/MacroClientHandler.java`<br>`macro/MacroManager.java`<br>`macro/MacroProcessor.java`<br>`network/macro/MsgPushMacro.java`<br>`network/macro/MsgUpdateClientMacro.java` |
| 反向：iota 到文本 | `Core/IotaWriter.cs`<br>`Core/FallbackBinary.cs` | `parsers/nbt2str/INbt2Str.java`<br>`parsers/nbt2str/BoolParser.java`<br>`parsers/nbt2str/CommentParser.java`<br>`parsers/nbt2str/EntityParser.java`<br>`parsers/nbt2str/GarbageParser.java`<br>`parsers/nbt2str/NullParser.java`<br>`parsers/nbt2str/NumParser.java`<br>`parsers/nbt2str/PatternParser.java`<br>`parsers/nbt2str/VecParser.java`<br>`parsers/FallbackBinaryParser.kt`<br>`misc/CodeHelpers.java`<br>`misc/CodeHelpersKt.kt` |
| 注释 iota | `Core/CommentIota.cs` | `hooks/CommentIota.java`<br>`hooks/CommentIotaType.java` |
| 大法术解锁表 | `Game/HexParseWorld.cs` | `hooks/GreatPatternUnlocker.java` |
| 图案 x8 | `Core/HexParsePatterns.cs`<br>`Game/HexParseActions.cs` | `actions/HexParsePatterns.java`<br>`actions/ActionCode2Focus.kt`<br>`actions/ActionFocus2Code.kt`<br>`actions/ActionRemoveComments.kt`<br>`actions/ActionLearnGreatPatterns.kt`<br>`actions/ActionCreateLineBreak.kt`<br>`actions/ActionDonate.kt`<br>`actions/ActionCompile.kt`<br>`actions/ActionCommentSwitcher.kt` |
| 指令 /hexParse | `Game/HexParseCommand.cs`<br>`Game/HexParseIO.cs` | `hooks/HexParseCommands.java`<br>`commands/CommandWrite.java`<br>`commands/CommandRead.java`<br>`commands/CommandClipboard.java`<br>`commands/CommandMindStackIO.kt`<br>`commands/CommandMacro.java`<br>`commands/CommandConflictResolver.kt`<br>`commands/CommandLehmerHelper.java`<br>`commands/CommandDonate.kt`<br>`commands/CommandLearnGreat.kt`<br>`commands/CommandGreatPatternUnlock.java`<br>`commands/CommandPropertyIO.java`<br>`misc/IOMethod.kt` |
| 剪贴板与显示同步（联机） | `Game/HexParseNet.cs` | `network/MsgPullClipboard.java`<br>`network/MsgPushClipboard.java`<br>`network/ClipboardMsgMode.java`<br>`network/MsgSyncDisplayMap.java`<br>`network/MsgHandlers.java`<br>`network/MsgHelpers.java`<br>`network/ISenderClient.java`<br>`network/ISenderServer.java` |
| .hexpattern 格式 | `Core/DotHexPattern.cs` | `parsers/hexpattern/DotHexPatternMapper.kt`<br>`parsers/hexpattern/TriePrefixMap.kt` |
| 嵌套列表 / 括号彩色显示 | （待做） | `mixin/iota/MixinListIotaDisplay.java`<br>`mixin/iota/MixinPatternIota.java`<br>`mixin_interface/NestedCounter.java` |
| 书：HexParse 指令分类 + 图案条目 | `Core/HexParseBook.Generated.cs` | `assets/hexcasting/patchouli_books/thehexbook`<br>`assets/hexparse/lang/zh_cn.json` |
| 与 HexDebug 联动：剪接台里画注释 iota（HexDebug 做完后） | （待做） | `compat/hexdebug/CommentRenderer.kt`<br>`compat/hexdebug/CommentRendererButIgnoresOverride.java` |
| 其他附属的插件解析（那些附属移植后再接，现在不做） | （待做） | `parsers/str2nbt/plugins/PluginConstParsers.java`<br>`parsers/nbt2str/plugins/`<br>`parsers/PluginIotaFactory.java`<br>`parsers/str2nbt/unsafe/hexal/`<br>`parsers/nbt2str/unsafe/hexal/` |

<a id="hexcessible"></a>
## Hexcessible

上游：https://github.com/tizu69/hexcessible（0.3.1，Ruby (tizu), ElNico56）；本地源码 `D:/DeepSeekHarness/addons_src/hexcessible`；
代码目录 `HexCastingTerraria/Addons/Hexcessible/`；图案 / iota 命名空间 `hexcessible`。

| 功能 | 本模组文件 | 上游文件（相对 `src/main/java/dev/tizu/hexcessible/`） |
|---|---|---|
| 入口与开关 | `Game/HexcessibleAddon.cs`<br>`Core/HexcessibleSettings.cs`<br>`Game/HexcessibleOptions.cs` | `Hexcessible.java`<br>`HexcessibleConfig.java` |
| 画布状态机（空闲 / 鼠标 / 键盘 / 自动补全 / 改别名） | `Game/HexcessibleCanvas.cs`<br>`Game/TooltipBox.cs` | `drawstate/DrawState.java`<br>`mixin/DrawStateMixin.java`<br>`mixin/DrawStateParentElemMixin.java`<br>`mixin/DrawStateScreenMixin.java`<br>`accessor/CastRef.java`<br>`accessor/CastingInterfaceAccessor.java` |
| 键盘绘制 | `Core/KeyboardPlacement.cs`<br>`Core/KeyboardDrawingState.cs`<br>`Core/PatternEntries.cs` | `drawstate/KeyboardDrawing.java`<br>`Utils.java` |
| 自动补全 | `Core/FluffySearch.cs`<br>`Core/AutoCompleteState.cs`<br>`Game/HexcessibleIndex.cs` | `drawstate/AutoCompleting.java`<br>`entries/PatternEntries.java` |
| 别名 | `Core/AliasEditState.cs`<br>`Game/HexcessibleStore.cs` | `drawstate/AliasChanging.java` |
| 智能签名：数字 / 簿记员 / 转义 | `Core/SmartSigs.cs`<br>`Core/JavaNum.cs`<br>`Core/NumberTable.Generated.cs` | `smartsig/SmartSig.java`<br>`smartsig/Number.java`<br>`smartsig/Bookkeeper.java`<br>`smartsig/Escape.java`<br>`numbers.txt` |
| 悬停说明与绘制提示 | `Game/HexcessibleCanvas.cs`<br>`Game/TooltipBox.cs` | `drawstate/Idling.java`<br>`drawstate/MouseDrawing.java` |
| 按 N 查书 | `Game/HexcessibleCanvas.cs` | `mixin/KeyDocsScreenMixin.java`<br>`entries/BookEntries.java` |
| 显示选项（变暗 / 全部格点 / 隐藏飘浮图案 / 大写签名 / 快捷键提示） | `Core/HexcessibleSettings.cs`<br>`Game/HexcessibleCanvas.cs` | `mixin/DimmedMixin.java`<br>`mixin/ShowAllDotsMixin.java`<br>`mixin/FloatiesMixin.java`<br>`mixin/RenderLibMixin.java` |
| 每世界大法术：手持远古卷轴才补全 | `Core/KnownWorldPatterns.cs`<br>`Game/HexcessibleStore.cs`<br>`Game/HexcessibleCanvas.cs` | `mixin/PerWorldLearnMixin.java` |
| 与 HexDebug 联动：剪接台小画布（HexDebug 做完后） | （待做） | `mixin/DrawStateHexdbgInteropMixin.java`<br>`mixin/DrawStateHexdbgInteropParentElemMixin.java` |
| 其他附属的智能签名 / Hexical 相关（那些附属移植后再接，现在不做） | （待做） | `smartsig/ComplexhexLong.java`<br>`smartsig/HexicalMacro.java`<br>`smartsig/HexThingsIntrojection.java`<br>`smartsig/HexThingsPatience.java`<br>`smartsig/OverevalGeb.java`<br>`smartsig/OverevalNephthys.java`<br>`smartsig/OverevalNut.java`<br>`smartsig/OverevalSekhmet.java`<br>`mixin/NoHexicalEvokeMixin.java`<br>`mixin/NoHexicalWalkMixin.java` |
