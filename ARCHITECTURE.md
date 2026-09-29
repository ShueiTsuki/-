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
| `HexClientConfig.cs` | 妯＄粍鐨勫鎴风閰嶇疆锛屼細鍑虹幇鍦?tModLoader 鐨勩€岃缃?鈫?妯＄粍閰嶇疆銆嶉噷銆?/// |
| `HexServerConfig.cs` | 鏈嶅姟绔厤缃€?/// |

### Core/

| 文件 | 职责 |
|---|---|
| `Canvas/PatternDrawer.cs` | 鐢诲竷涓婂凡缁忕敾瀹岀殑涓€鏉″浘妗堛€?see cref="Type"/> 鍦ㄦ眰鍊肩粨鏋滃洖鏉ヤ箣鍓嶆槸 Unresolved锛堢伆鑹诧級銆?/summary> |
| `Canvas/PatternGeometry.cs` | 涓€涓甫棰滆壊鐨勯《鐐广€傞鑹蹭负 0xAARRGGBB锛堥潪棰勪箻 alpha锛夈€備笁涓竴缁勬瀯鎴愪笁瑙掑舰銆?/summary> |
| `Canvas/SimplexNoise.cs` | Minecraft 鐨?`SimplexNoise` + `SingleThreadedRandomSource`锛岄€愯绉绘銆?/// |
| `Casting/Actions/AkashicActions.cs` | `akashic/read`锛氫粠鏌愪釜鍧愭爣鐨勯樋鍗″璁板綍鏂瑰潡涓婏紝鎸?*鍥炬**鏌ヤ竴涓?iota銆?/// 绉绘鑷簮椤圭洰 akashic/OpAkashicRead.kt銆?/// |
| `Casting/Actions/BasicActions.cs` | 甯告暟鍥炬锛氫笉鍙栧弬鏁帮紝寰€鏍堜笂鍘嬩竴涓浐瀹氬€笺€?/// 瀵瑰簲婧愰」鐩?Action.makeConstantOp(x)銆?/// </summary> |
| `Casting/Actions/BlockActions.cs` | `conjure_block` 与 `conjure_light`：**凭空**造出一块方块 / 一盏光。 |
| `Casting/Actions/BrainsweepActions.cs` | `brainsweep`锛氳剳鍙跺垏闄?鈥斺€?鎶婁竴鍙敓鐗┿€屽鐞嗐€嶆帀锛屾崲鏉ヤ竴鍧楁洿楂樼骇鐨勬柟鍧椼€?/// 绉绘鑷簮椤圭洰 `OpBrainsweep`锛堝ぇ娉曟湳锛岄渶瑕佸惎钂欙級銆?/// |
| `Casting/Actions/BrainsweepRules.cs` | 涓€鏉°€岃剳鍙跺垏闄ゃ€嶉厤鏂广€傜Щ妞嶈嚜婧愰」鐩?`common/recipe/BrainsweepRecipe.java`銆?/// |
| `Casting/Actions/ColorizeAction.cs` | `colorize`锛氭嬁涓€浠?*棰滄枡**锛屾妸鑷繁涔嬪悗鎵€鏈夋硶鏈殑閰嶈壊鎹㈡垚瀹冦€?/// 绉绘鑷簮椤圭洰 `OpColorize`銆?/// |
| `Casting/Actions/CompareActions.cs` | `compare_entity`锛氫袱涓疄浣?*鏄笉鏄悓绫?*銆?/// 绉绘鑷簮椤圭洰 `OpEntityEquality`銆?/// |
| `Casting/Actions/CraftActions.cs` | `craft/cypher` / `craft/trinket` / `craft/artifact`锛?/// 鎶婁竴涓插浘妗堜笌鍦颁笂濯掕川鐗╁搧閲岀殑濯掕川涓€璧峰皝杩涙墜鎸佺殑绌哄鍣ㄣ€?/// 绉绘鑷簮椤圭洰 `OpMakePackagedSpell`銆?/// |
| `Casting/Actions/EntitySelectActions.cs` | `get_entity/*`锛氬彇**鏌愪釜鍧愭爣涓?*鐨勫疄浣擄紙鏈€杩戠殑涓€涓級銆?/// 绉绘鑷簮椤圭洰 selectors/OpGetEntityAt.kt銆?/// |
| `Casting/Actions/EvalActions.cs` | if锛氫笁鐩繍绠椼€傚悆 (鏉′欢:bool, 鐪熷€? 鍋囧€?锛屽悙閫変腑鐨勯偅涓€?/// 绉绘鑷?common/casting/actions/math/logic/OpBoolIf.kt锛坅rgc = 3锛夈€?/// </summary> |
| `Casting/Actions/FlightActions.cs` | 椋炶鐨勩€屽嵄闄╁害銆嶈绠椼€傜Щ妞嶈嚜婧愰」鐩?`OpFlight.getDanger`銆?/// |
| `Casting/Actions/ListActions.cs` | 寮€鎷彿锛氳繘鍏ュ垪琛ㄦ瀯寤烘ā寮忋€?/// 绉绘鑷?common/casting/actions/escaping/OpOpenParen.kt銆?/// |
| `Casting/Actions/LogicActions.cs` | `equals` / `not_equals`锛氭瘮杈冧袱涓?iota 鏄惁鐩哥瓑銆?/// 绉绘鑷簮椤圭洰 `OpEquality`銆?/// |
| `Casting/Actions/MathActions.cs` | 运算符图案（add / sub / mul / div / abs / pow / and / or / greater ...）。 |
| `Casting/Actions/PotionActions.cs` | 鑽按鏁堟灉鐨勭绫汇€傚搴旀簮椤圭洰娉ㄥ唽鐨?10 涓?MC `MobEffects`銆?/// |
| `Casting/Actions/RaycastActions.cs` | 三个射线图案的共同部分。 |
| `Casting/Actions/ReadWriteActions.cs` | 銆屽彧鍋氫竴浠朵簨銆嶇殑娉曟湳锛屼絾浣滅敤瀵硅薄鏄?*鏂芥硶鐜**锛堟墜鎸佺墿鍝侊級鑰屼笉鏄笘鐣屻€?/// |
| `Casting/Actions/SentinelActions.cs` | `sentinel/create` 涓?`sentinel/create/great`锛氬湪鎸囧畾浣嶇疆鏀剧疆鍝ㄥ崼銆?/// 绉绘鑷簮椤圭洰 `OpCreateSentinel`銆?/// |
| `Casting/Actions/SimpleSpellActions.cs` | `beep`锛氬湪鎸囧畾浣嶇疆鏁蹭竴涓煶绗︺€?/// 绉绘鑷簮椤圭洰 `OpBeep`銆?/// |
| `Casting/Actions/SpellActions.cs` | `add_motion`：给目标实体施加一次推力。 |
| `Casting/Actions/StackUtilActions.cs` | `open_n_parens`锛氫竴娆″紑 n 灞傛嫭鍙凤紝n 浠庢爤椤跺彇銆?/// 绉绘鑷簮椤圭洰 `OpOpenNParens`銆?/// |
| `Casting/Actions/StorageActions.cs` | `read_into_parens`锛氫粠**鎵嬫寔鐨勬暟鎹浇浣?*璇诲嚭涓€涓?iota 骞舵斁杩涙嫭鍙峰垪琛ㄣ€?/// 绉绘鑷簮椤圭洰 escaping/OpReadIntoParens.kt銆?/// |
| `Casting/Actions/WorldActions.cs` | `get_caster`：把施法者自身压上栈。 |
| `Casting/Actions/WorldEffectActions.cs` | 涓栫晫鏁堟灉绫诲浘妗堢殑鍏叡閮ㄥ垎銆?/summary> |
| `Casting/Actions/ZoneActions.cs` | 鍖哄煙鏌ヨ鐨勭瓫閫夌绫汇€傚搴旀簮椤圭洰 `OpGetEntitiesBy` 鐨勪簲涓皳璇嶃€?/summary> |
| `Casting/Arithmetic/ArithmeticEngine.cs` | 涓€绉嶇畻鏈疄鐜帮紙瀵瑰簲婧愰」鐩?Arithmetic 鎺ュ彛锛夈€?/// |
| `Casting/Arithmetic/ListArithmetic.cs` | 鍒楄〃绠楁湳銆傚搴旀簮椤圭洰 ListArithmetic.kt銆?/// |
| `Casting/Castables/Action.cs` | 一条图案的行为。 |
| `Casting/Castables/ActionTypes.cs` | 涓€鏉″浘妗堢殑**鍙傛暟绫诲瀷濂戠害**锛氬畠娑堣€椾粈涔堢被鍨嬨€佷骇鍑轰粈涔堢被鍨嬨€?/// |
| `Casting/Castables/SpellAction.cs` | 会**作用于世界**的图案的行为基类。 |
| `Casting/Circles/CircleCastingEnvironment.cs` | 娉曟湳鐜殑鎵ц鐘舵€侊紙渚?`circle/*` 涓変釜鍥炬璇诲彇锛夈€?/// 瀵瑰簲婧愰」鐩?`CircleExecutionState` 閲岃鍥炬鐢ㄥ埌鐨勯偅鍑犱釜瀛楁銆?/// </summary> |
| `Casting/Circles/CircleComponent.cs` | 鏂瑰悜闆嗗悎鐨勪綅鎺╃爜銆?/summary> |
| `Casting/Circles/CircleDir.cs` | 娉曟湳鐜帶鍒舵祦鐨?*鏂瑰悜**銆?/// |
| `Casting/Circles/CircleMessages.cs` | 法术环的消息出口。 |
| `Casting/Circles/CircleTraversal.cs` | 娉曟湳鐜涓栫晫鐨勮闂€?/// |
| `Casting/Eval/CastingEnvironment.cs` | 鎵撳寘娉曟湳鐨勭绫汇€傚搴旀簮椤圭洰鐨勪笁涓墿鍝侊細cypher锛堢绾革紝涓€娆℃€э級銆? |
| `Casting/Eval/CastResult.cs` | 瀵规柦娉?VM 鍋氫竴娆℃搷浣滅殑缁撴灉銆?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.CastResult銆?/// |
| `Casting/Eval/ICastingWorld.cs` | 鏂芥硶鐜瀵广€屼笘鐣屻€嶇殑**鍙**璁块棶鎶借薄銆?/// |
| `Casting/Eval/Mishaps/CommonMishaps.cs` | 鏍堜笂鐨勫弬鏁颁笉澶熴€傛儵缃氾細鎶婄己鐨勯偅鍑犱釜琛ユ垚鍨冨溇鍊硷紙婧愰」鐩悓锛夈€?/summary> |
| `Casting/Eval/Mishaps/Mishap.cs` | mishap 鐨勪笂涓嬫枃锛氬嚭閿欑殑鍥炬涓庯紙鍙兘鐨勶級鍥炬鍚嶃€?/// 绉绘鑷?at.petrak.hexcasting.api.casting.mishaps.Mishap.Context銆?/// </summary> |
| `Casting/Eval/OperationResult.cs` | 鍥炬鎵ц鍚庣殑閫氱敤缁撴灉鎺ュ彛銆?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.IOperationResult銆?/// </summary> |
| `Casting/Eval/ResolvedPatternType.cs` | 涓€鏉″浘妗堣姹傚€煎悗鐨勮В鏋愮姸鎬併€?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.ResolvedPatternType銆?/// </summary> |
| `Casting/Eval/SideEffects/EvalSound.cs` | 姹傚€奸煶鏁堢绫汇€傜Щ妞嶈嚜 at.petrak.hexcasting.api.casting.eval.sideeffects.EvalSound銆?/// </summary> |
| `Casting/Eval/SideEffects/OperatorSideEffect.cs` | 鏂芥硶瀹屾垚鍚庡彂鐢熺殑鍓綔鐢ㄣ€?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.sideeffects.OperatorSideEffect銆?/// </summary> |
| `Casting/Eval/SideEffects/ParticleSpray.cs` | 涓€娆＄矑瀛愬柗鍙戙€傜Щ妞嶈嚜婧愰」鐩?`ParticleSpray`銆?/// |
| `Casting/Eval/SpellList.cs` | 鍑芥暟寮忥紙鎸佷箙鍖栵級鍒楄〃銆傜Щ妞嶈嚜 at.petrak.hexcasting.api.casting.SpellList銆?/// |
| `Casting/Eval/Vm/CastingImage.cs` | 涓€瀵广€屾嫭鍙峰唴鐨?iota + 鏄惁鐢?Consideration 杞箟鑰屾潵銆嶃€?/// escaped 渚?OpUndo 鍒ゆ柇鎾ら攢璇?iota 鏃舵槸鍚﹂渶瑕佽皟鏁存嫭鍙疯鏁?/// 锛堣杞箟鐨勬嫭鍙蜂笉褰卞搷璁℃暟锛夈€?/// </summary> |
| `Casting/Eval/Vm/CastingVM.cs` | 鏂芥硶铏氭嫙鏈恒€?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.vm.CastingVM銆?/// |
| `Casting/Eval/Vm/CastUserData.cs` | 鏈鏂芥硶鐨勪复鏃舵暟鎹銆?/// 绉绘鑷簮椤圭洰 `CastingImage.userData`锛堥偅杈规槸涓€涓?NBT `CompoundTag`锛夈€?/// |
| `Casting/Eval/Vm/ContinuationFrame.cs` | 姹傚€艰繃绋嬩腑鐨勪竴涓€屽抚銆嶃€?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.vm.ContinuationFrame銆?/// |
| `Casting/Eval/Vm/FrameForEach.cs` | Thoth锛坒or_each / eval_breakable锛夋眰鍊煎抚銆?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.vm.FrameForEach銆?/// |
| `Casting/Eval/Vm/SpellContinuation.cs` | 鏂芥硶杩囩▼涓殑缁欢锛堝抚鏍堬級銆?/// 绉绘鑷?at.petrak.hexcasting.api.casting.eval.vm.SpellContinuation銆?/// |
| `Casting/GreatTeleportRules.cs` | 澶ф硶鏈殑涓栫晫瑙勫垯銆?*鐢辨父鎴忎晶娉ㄥ叆**锛孋ore 鍙銆?/// |
| `Casting/HexMathUtil.cs` | 鏁板€煎畨鍏ㄥ伐鍏枫€?/// |
| `Casting/HexUnits.cs` | 鍗曚綅鎹㈢畻甯搁噺銆?/// |
| `Casting/Iota/CompositeIotas.cs` | 鍒楄〃 iota銆傛簮锛歀istIota銆?/// |
| `Casting/Iota/Iota.cs` | Iota 绫诲瀷鏍囩銆傚搴旀簮椤圭洰 IotaType 鍗曚緥鐨勯泦鍚堬紙娉ㄥ唽浜?HexIotaTypes.java锛夈€?/// |
| `Casting/Iota/IotaSerializer.cs` | iota 鐨勫簭鍒楀寲涓庡弽搴忓垪鍖栥€?/// |
| `Casting/Iota/PrimitiveIotas.cs` | 绌哄€?iota銆傛簮锛歂ullIota銆傚崟渚嬨€?/summary> |
| `Casting/Iota/StoragePolicy.cs` | 鏁版嵁杞戒綋鐨勫瓨鍌ㄧ瓥鐣ャ€?/summary> |
| `Casting/Math/EulerPathFinder.cs` | 缁欎竴涓浘妗堟壘鍙︿竴绉嶇敾娉曪細**褰㈢姸锛堣竟闆嗭級瀹屽叏鐩稿悓锛岀瑪椤轰笉鍚?*銆?/// 绉绘鑷簮椤圭洰 EulerPathFinder.kt 鈥斺€?澶ф硶鏈殑銆屾瘡涓笘鐣岀殑绗旈『銆嶅氨鏄敤瀹冧粠鏍囧噯鍥炬 + 涓栫晫绉嶅瓙鐢熸垚鐨勩€?/// |
| `Casting/Math/HexAngle.cs` | 鍏杞悜瑙掋€傞『搴忎笌婧愰」鐩竴鑷达紝搴忓彿鍙備笌妯¤繍绠楋紝涓嶅彲閲嶆帓銆?/// 婧愶細at.petrak.hexcasting.api.casting.math.HexAngle |
| `Casting/Math/HexCoord.cs` | 鍏竟褰㈢綉鏍间笂鐨勮酱鍧愭爣锛坅xial coordinate锛夈€?/// 婧愶細at.petrak.hexcasting.api.casting.math.HexCoord |
| `Casting/Math/HexDir.cs` | 鍏竟褰㈢綉鏍肩殑鍏釜鏂瑰悜銆傞『搴忎笌婧愰」鐩竴鑷达紙椤烘椂閽堬紝浠庝笢鍖楀紑濮嬶級锛?/// 搴忓彿鍙備笌妯¤繍绠楋紝涓嶅彲閲嶆帓銆?/// 婧愶細at.petrak.hexcasting.api.casting.math.HexDir |
| `Casting/Math/HexGrid.cs` | 鍏竟褰㈢綉鏍肩殑鍍忕礌鎹㈢畻涓庨亶鍘嗐€?/// 绉绘鑷?at.petrak.hexcasting.api.utils.HexUtils锛坈oordToPx / pxToCoord锛?/// 涓?HexCoord.kt 鐨?rangeAround銆?/// |
| `Casting/Math/HexPattern.cs` | 涓€鏉″拻鏈浘妗堬細璧峰鏂瑰悜 + 涓€涓茶浆鍚戣銆?/// |
| `Casting/Math/ParseError.cs` | 鍥炬瑙ｆ瀽澶辫触鐨勪綅缃笌鍘熷洜銆?/// 浠?HexPattern 閲屾彁鍑烘潵鍋氶《灞傜被鍨嬶紝渚夸簬璋冪敤鏂规棤闇€闄愬畾鍚嶅氨鑳藉紩鐢ㄣ€?/// </summary> |
| `Casting/Math/PatternSuggestion.cs` | 銆屼綘鐢荤殑杩欐潯鏈€鎺ヨ繎鍝釜鍥炬銆嶃€?/// |
| `Casting/Math/SpecialPatterns.cs` | **鐗规畩鍥炬**锛坰pecial patterns锛夛細涓嶅湪 188 鏉℃敞鍐岃〃閲岋紝浣嗚兘琚瘑鍒垚鍔ㄤ綔鐨勫浘妗堛€?/// 绉绘鑷簮椤圭洰 `SpecialHandler` 鐨勪袱涓疄鐜帮紙`SpecialHandlerNumberLiteral` / `SpecialHandlerMask`锛夈€?/// |
| `Casting/Math/Vec2f.cs` | 鏈€灏忎簩缁存诞鐐瑰悜閲忥細鍙湁 `Core` 鐪熸鐢ㄥ埌鐨勯偅鍑犱釜鎿嶄綔銆?/// |
| `Dev/SampleHexes.cs` | 娉曟湳閲岀殑涓€姝ワ細涓€鏉″叿鍚嶅浘妗堬紝鎴栦竴涓暟瀛楀瓧闈㈤噺锛堟暟瀛楀浘妗?aqaa鈥?鐢?<see cref="SpecialPatterns.EncodeNumber"/> 鐢熸垚锛夈€?/summary> |
| `Media/MediaConstants.cs` | 濯掕川鍗曚綅鎹㈢畻銆傜Щ妞嶈嚜 at.petrak.hexcasting.api.misc.MediaConstants锛堟暟鍊奸€愰」瀵归綈锛夈€?/// |
| `Media/MediaPaymentPlanner.cs` | 鎵ｈ垂浼樺厛绾с€傞€愬瓧鐓ф妱婧愰」鐩?ADMediaHolder锛氭暟鍊艰秺澶ц秺鍏堟墸銆?/// </summary> |
| `Media/Overcast.cs` | 杩囪浇锛堢敤鐢熷懡鎹㈠獟璐級涓庛€屽惎钂欍€嶇殑鍒ゅ畾锛岀収鍘熺増 PlayerBasedCastEnv.extractMediaFromInventory |
| `Registry/GeneratedPatternData.cs` | 鍥炬鐨勯潤鎬佹暟鎹細id銆佽搴︾鍚嶃€佽捣濮嬫柟鍚戙€佹簮瀹炵幇绫诲悕銆?/summary> |
| `Registry/PatternDisplay.cs` | 鍥炬鐨?*鏄剧ず鍚?*銆傜晫闈竴寰嬭蛋杩欓噷锛屼笉瑕佸悇鑷嫾 `Id.Replace("hexcasting:", "")`銆?/// |
| `Registry/PatternNames.Generated.cs` | 图案的**正式中文名**（例如 get_caster → 「意识之精思」）。 |
| `Registry/PatternRegistry.cs` | 鍥炬鍖归厤缁撴灉锛屽搴旀簮椤圭洰鐨?PatternShapeMatch 鑱斿悎绫诲瀷銆?/// </summary> |
| `Ui/BookCanvas.cs` | 涔︾敤鍒扮殑璐村浘銆備笌鍘熺増鍚屽悕鍚屽昂瀵革紝婧愮煩褰㈢敤鍘熺増璐村浘鍧愭爣銆?/summary> |
| `Ui/BookContent.Generated.cs` | 鏈枃浠剁敱 _tools/gen_book_content.py 鐢熸垚锛?*涓嶈鎵嬫敼**锛氭敼鐢熸垚鍣紝鐒跺悗閲嶆柊璺戣剼鏈€?//     鍐呭锛氬師鐗堝拻娉曞鎵嬪唽锛團allingColors/HexMod v0.11.4锛孧IT锛夌殑缁撴瀯 + 瀹樻柟绠€浣撲腑鏂囨鏂囥€?// </auto-generated> |
| `Ui/BookModel.cs` | 椤甸潰绫诲瀷銆傚搴?Patchouli 娉ㄥ唽鍦?<c>patchouli:鈥?/c> 鍚嶄笅鐨勪竴缁勯〉闈㈢被锛?/// 杩欓噷鍙繚鐣欐嘲鎷変晶**鐪熶細鐢ㄥ埌**鐨勯偅鍑犵锛圡C 鐗规湁鐨勫鏂瑰潡棰勮 / 瀹炰綋棰勮 / 杩涘害浠诲姟宸插墧闄わ級銆?/// </summary> |
| `Ui/BookText.cs` | 閲忎竴娈垫枃鏈湁澶氬銆傜湡瀹炴覆鏌撴椂浼犲瓧浣撴祴閲忥紝绂荤嚎娴嬭瘯鏃朵紶鍋囧嚱鏁般€?/summary> |
| `Ui/BookUnlocks.cs` | 鐜╁鍦ㄤ功鐨勮В閿佷笂鐢ㄥ緱鍒扮殑杩涘害銆傛父鎴忓唴鐢?Client 濉紝绂荤嚎娴嬭瘯鎵嬪～銆?/summary> |
| `Ui/BookView.cs` | 鍒嗙被椤电涓€璺ㄩ〉鍙抽〉鑳芥斁鐨勬潯鐩暟锛圥atchouli ENTRIES_IN_FIRST_PAGE锛夈€?/summary> |
| `Ui/PatchouliRenderer.cs` | 涔︿笂鍙偣鐨勪笢瑗裤€?/summary> |
| `World/AmethystLoot.cs` | 鏅剁皣鐨勭敓闀块樁娈点€傚搴?MC 鐨勫洓涓柟鍧椼€?/summary> |
| `World/HexAxes.cs` | 娉曟湳鍧愭爣锛堝師鐗堢害瀹氾細鏂瑰潡鍗曚綅銆?Y 鏈濅笂锛変笌娉版媺鍥炬牸鍧愭爣锛?Y 鏈濅笅锛変箣闂寸殑鎹㈢畻鍏紡銆?/// 绾嚱鏁帮紝H = 涓栫晫楂樺害锛堝浘鏍兼暟锛夈€傛嘲鎷変晶鐨?HexSpaceWorld 鐢?Main.maxTilesY 璋冨畠浠紱绂荤嚎娴嬭瘯鐩存帴璋冦€?/// |
| `World/LookResolver.cs` | 瑙嗙嚎瑙ｆ瀽鐨勮緭鍏ャ€?/// |
| `World/SegmentSweep.cs` | 涓€涓疄浣撶殑鍒ゅ畾绠憋紝鍧愭爣鍗曚綅锛?*鍥炬牸**銆?/// 鐢辨父鎴忎晶濉ソ鍚庝氦缁?<see cref="SegmentSweep"/>锛屼簬鏄壂鎺犻€昏緫鍙互绂荤嚎娴嬭瘯銆?/// </summary> |
| `World/TileRaycast.cs` | 浜岀淮鍥炬牸缃戞牸涓婄殑灏勭嚎姹備氦锛圓manatides &amp; Woo 鐨?DDA 绠楁硶锛夈€?/// |

### Content/

| 文件 | 职责 |
|---|---|
| `DevTextureDump.cs` | 寮€鍙戠敤锛氭妸**鍘熺増娉版媺鐨?UI 璐村浘**瀵煎嚭鎴?PNG锛屼緵绂荤嚎姣斿銆?/// |
| `HexChestLoot.cs` | 寰€涓栫晫閲岀殑绠卞瓙濉炲師鐗堢殑涓夌鎴樺埄鍝侊紙婧愰」鐩?HexLootHandler锛夛細 |
| `HexGlobalNPC.cs` | 涓烘瘡涓?NPC 缁存姢涓€涓寔涔呫€岃绾挎柟鍚戙€嶃€?/// |
| `HexGlobalProjectile.cs` | 涓烘瘡涓脊骞曠淮鎶や竴涓寔涔呫€岃绾挎柟鍚戙€嶏紝鍗抽琛屾柟鍚戙€?/// |
| `HexPlayer.cs` | 鐜╁渚у拻鏈暟鎹€?/// |
| `HexSpaceWorld.cs` | 娉曟湳鍧愭爣 鈫?娉版媺鍧愭爣鐨?*鍞竴**鎹㈢畻灞傦紙濂楀湪 <see cref="TerrariaCastingWorld"/> 澶栭潰锛夈€?/// |
| `Items/Abacus.cs` | 闃垮崱澶忚褰曠殑**鐗╁搧褰㈡€?*銆?/// |
| `Items/AncientLoot.cs` | 杩滃彜鍗疯酱锛堟簮椤圭洰 ItemScroll + TAG_OP_ID锛屻€?s涔嬭繙鍙ゅ嵎杞淬€嶏級锛氬啓鐫€**鏈笘鐣?*鏌愪釜澶ф硶鏈殑绗旈『銆?/// 澶ф硶鏈殑绗旈『姣忎釜涓栫晫涓嶅悓锛堣 PatternRegistry.PerWorldIds锛夛紝涔﹂噷鍙敾褰㈢姸 鈥斺€?鏈笘鐣岀殑鐢绘硶瑕侀潬瀹冨銆?/// 鍙湪绠卞瓙閲屾壘寰楀埌锛堣 HexChestLoot锛夛紝娌℃湁閰嶆柟銆?/// |
| `Items/DevKit.cs` | 寮€鍙戣€呮祴璇曞寘锛氫竴娆℃妸姣忎釜瀛愮郴缁熺殑浠ｈ〃鎬х墿鍝佸悇鍙戜竴浠姐€?/// |
| `Items/DevStaff.cs` | 寮€鍙戣€呮硶鏉栵紙Dev Staff锛夈€?/// |
| `Items/EdifiedFurniture.cs` | 鍚开鏈ㄧ殑銆屽鍏峰瑁呫€嶏細鐢ㄥ惎杩湪鏉垮幓鍚堟垚**鍘熺増瀹跺叿**銆?/// |
| `Items/HexBookItem.cs` | 鍜掓硶瀛︿箣涔︺€傚搴旀簮椤圭洰鐨?`hexcasting:thehexbook`锛圥atchouli 鍐欑殑閭ｆ湰寮曞涔︼級銆?/// |
| `Items/HexConditions.cs` | 本模组的配方条件。</summary> |
| `Items/HexDecoBlockItem.cs` | 寤烘潗鏂瑰潡鐗╁搧鐨勫叕鍏卞疄鐜帮紙閰嶅悎 <see cref="HexDecoBlock"/>锛夈€?/// |
| `Items/HexDirectrixItems.cs` | 瀵肩嚎鐨勫叕鍏辩墿鍝佸熀绫汇€傚搴旀簮椤圭洰涓夋牴 `*Directrix` 鐨勭墿鍝佸舰鎬併€?/// |
| `Items/HexImpetusItem.cs` | 鍘熷姩鍔涳紙鐗╁搧褰㈡€侊級銆傚搴旀簮椤圭洰 `hexcasting:impetus/*`銆?/// |
| `Items/HexRecipeGroups.cs` | 寤烘潗鏃忕殑**閰嶆柟缁?*銆?/// |
| `Items/HexSlateItem.cs` | 鐭虫澘锛堢墿鍝佸舰鎬侊級銆傚搴旀簮椤圭洰 `hexcasting:slate`銆?/// |
| `Items/HexStaff.cs` | 娉曟潠鍩虹被銆傜Щ妞嶈嚜婧愰」鐩?`common/items/ItemStaff.java`銆?/// |
| `Items/ItemIotaStorage.cs` | 銆屾暟鎹浇浣撱€嶇墿鍝佺殑鍩虹被锛氳兘瀛樹竴涓?iota銆?/// 绉绘鑷簮椤圭洰 `common/items/storage/ItemDataHolder`锛坒ocus / abacus / spellbook 绛夛級銆?/// |
| `Items/ItemPackagedSpell.cs` | 鎵撳寘娉曟湳鐗╁搧鐨勫熀绫伙細鎶婁竴涓插浘妗堜笌涓€浠藉獟璐ㄥ皝鍦ㄩ噷闈紝鍙抽敭鍗冲彲鏂芥斁銆?/// 绉绘鑷簮椤圭洰 `common/items/magic/ItemPackagedHex.java`銆?/// |
| `Items/ItemScroll.cs` | 鍗疯酱銆傜Щ妞嶈嚜婧愰」鐩?`common/items/storage/ItemScroll.java`銆?/// |
| `Items/JewelerHammer.cs` | 鐝犲疂鍖犻敜銆傚搴旀簮椤圭洰 `hexcasting:jeweler_hammer`銆?/// |
| `Items/MediaFlask.cs` | 濯掕川鐡讹紙婧愰」鐩?ItemMediaBattery锛屻€屽獟璐ㄤ箣鐡?/ phial of media銆嶏級銆?/// |
| `Items/MediaMaterials.cs` | 濯掕川鏉愭枡鐗╁搧鐨勫熀绫伙紙婧愰」鐩?CCMediaHolder.Static锛夈€?/// |
| `Items/MiscDecoItems.cs` | 鍗疯酱绾革紙鐗╁搧锛夈€?/summary> |
| `Items/PackagedSpellCast.cs` | 鎵撳寘娉曟湳涓撶敤鐜锛氬獟璐?*浠庣墿鍝佽嚜宸辩殑姹犲瓙閲屾墸**锛屼笉鏄粠鐜╁韬笂銆?/// 绉绘鑷簮椤圭洰 `ItemPackagedHex` 閲岀殑 `PackagedHexCastEnv`銆?/// |
| `Items/ScryingLens.cs` | 鎺㈡湳閫忛暅銆傚搴旀簮椤圭洰 `hexcasting:lens`锛坄ItemLens`锛岃澶囧湪**澶撮儴**瑁呭浣嶏級銆?/// |
| `Items/Spellbook.cs` | 娉曟湳涔︺€傚搴旀簮椤圭洰 `hexcasting:spellbook`锛?*涓嶆槸**閭ｆ湰寮曞涔?`thehexbook`锛夈€?/// |
| `Items/WallScrollFrames.cs` | 鍗疯酱鎸傛澘鐨勫熀绫汇€傚搴旀簮椤圭洰閲屻€屾妸鍗疯酱鎸傚埌澧欎笂銆嶉偅涓€姝ユ墍闇€鐨勮浇浣撱€?/// |
| `Net/HexNet.cs` | 缃戠粶娑堟伅绫诲瀷銆?/// </summary> |
| `Net/HexNetSync.cs` | 鏂瑰潡浜や簰鐨勪笂鎶ヨ緟鍔┿€? |
| `Net/ServerCastState.cs` | **鏈嶅姟绔?*鐨勬柦娉曠姸鎬侊細姣忎釜鐜╁涓€浠?VM銆?/// |
| `PerWorldPatternSystem.cs` | 澶ф硶鏈€屾瘡涓笘鐣岀殑绗旈『銆嶇殑瀛樻。涓庡悓姝ワ紙婧愰」鐩?ScrungledPatternsSave锛夈€?/// |
| `PlayerCastingEnvironment.cs` | 鐜╁鏂芥硶鐜锛氭妸 VM 鐨勬娊璞￠渶姹傛帴鍒版嘲鎷夌帺瀹惰韩涓娿€?/// |
| `PlayerEffects.cs` | 浣滅敤鍦ㄣ€岀帺瀹惰嚜宸辩殑涓滆タ銆嶄笂鐨勬晥鏋滐細鐢熷懡銆佽儗鍖呫€佹墜鎸佺墿鍝併€佹哀姘斻€?/// |
| `SpellSounds.cs` | 鍜掓硶瀛︾殑闊虫晥灞傘€?/// |
| `SpellVisuals.cs` | 娉曟湳绮掑瓙鐨勮〃鐜板眰锛氭妸 Core 绠楀嚭鏉ョ殑 <see cref="ParticleSpray"/> 鍙樻垚鐪熸鐨?dust銆?/// |
| `TerrariaCastingWorld.cs` | <see cref="ICastingWorld"/> 鐨勬嘲鎷夌憺浜氬疄鐜般€?/// |
| `Tiles/AkashicRecord.cs` | 闃垮崱澶忚褰曟柟鍧椼€傚搴旀簮椤圭洰 `hexcasting:akashic_record`銆?/// |
| `Tiles/AmethystDustBlock.cs` | 绱按鏅剁矇鍧椼€傚搴旀簮椤圭洰 `hexcasting:amethyst_dust_block`锛堣楗版柟鍧楋級銆?/// |
| `Tiles/AmethystGeode.cs` | 鏅舵礊姣嶅博銆傚搴?MC 鐨?`budding_amethyst`銆?/// |
| `Tiles/CircleCursor.cs` | 娉曟湳鐜殑銆屾墽琛屾父鏍囥€嶏細褰撳墠姝ｅ湪鎵ц鍝竴鏍笺€?/// |
| `Tiles/ConjuredBlock.cs` | 琚彫鍞ゅ嚭鏉ョ殑鏂瑰潡/鍏夋簮鐨勫瓨娲荤鐞嗐€?/// |
| `Tiles/DecoBlocks.Generated.cs` | 寤烘潗鏂瑰潡鐨勫叕鍏卞疄鐜帮紙P2-6 瑁呴グ鏂瑰潡瀹舵棌锛夈€? |
| `Tiles/HexDirectrix.cs` | 瀵肩嚎鐨勫叕鍏卞熀绫汇€傚搴旀簮椤圭洰 `BlockEmptyDirectrix` / `BlockBooleanDirectrix` / |
| `Tiles/HexImpetus.cs` | 娉曟湳鐜娉版媺涓栫晫鐨勮闂疄鐜般€?/// |
| `Tiles/HexSlate.cs` | 鐭虫澘銆傚搴旀簮椤圭洰 `hexcasting:slate` 鈥斺€?**娉曟湳鐜殑銆屾寚浠ゃ€?*銆?/// |
| `Tiles/MiscDeco.cs` | 璐村湪澧欎笂鐨勮楗?鍏夋簮鏂瑰潡鐨勫叕鍏卞疄鐜般€?/// |
| `Tiles/WallScroll.cs` | 澹佹寕鍗疯酱銆傚搴旀簮椤圭洰鐨?`EntityWallScroll`銆?/// |
| `Worldgen/GeodeWorldGen.cs` | 绱按鏅舵櫠娲炵殑涓栫晫鐢熸垚銆?/// |

### Client/

| 文件 | 职责 |
|---|---|
| `HexCanvasState.cs` | 鐢诲竷涓?HUD 鐨勫鎴风鍏变韩鐘舵€併€?/// |
| `HexClientSystem.cs` | 瀹㈡埛绔郴缁燂細鐢诲竷杈撳叆銆佺敾甯冪粯鍒躲€丠UD锛堝獟璐ㄦ寚绀?+ 鍥炬璇嗗埆鍙嶉锛夈€?/// |
| `HexColors.cs` | 鍜掓硶瀛︾殑琛ㄧ幇灞傞厤鑹层€?/// |
| `HexDebugOverlay.cs` | 寮€鍙戣€呰皟璇曞彔鍔犲眰锛氭妸銆岀湅涓嶈浣嗗繀椤荤‘璁ゃ€嶇殑涓滆タ鐢诲嚭鏉ャ€?/// |
| `HexPigment.cs` | 娉曟湳閰嶈壊锛堛€岄鏂欍€嶏級銆?/// |
| `HexPixel.cs` | 鍏变韩鐨?1脳1 鐧借壊璐村浘锛岀敤浜庣敾绾挎涓庢柟鍧楃偣銆?/// |
| `HexVec.cs` | `Core` 鐨?<see cref="Vec2f"/> 鈫?XNA 鐨?<see cref="Vector2"/> 浜掕浆銆?/// |
| `HexVmState.cs` | 瀹㈡埛绔晶鐨?VM 鐘舵€併€?/// |
| `UI/DevPanel.cs` | 寮€鍙戣€呴潰鏉匡紙榛樿 F7锛夛細涓嶇煡閬撹鏀句粈涔堟硶鏈椂鐢ㄣ€?/// |
| `UI/HexBook.cs` | 鍜掓硶瀛︿箣涔︼紙甯曠鑾夋墜鍐岋級鐨勫紑鍏炽€佽緭鍏ヤ笌缁樺埗銆傚唴瀹逛笌鎺掔増鍦?Core锛圔ookContent / PatchouliRenderer锛夈€? |
| `UI/HexCanvas.cs` | 宸茬敾瀹岀殑涓€鏉″浘妗?+ 娉ㄥ唽琛ㄥ尮閰嶇粨鏋滐紙null = 鏈懡涓級+ 姹傚€肩粨鏋滐紙鍐冲畾棰滆壊锛夈€?/summary> |
| `UI/PatternIconElement.cs` | 鎶婁竴涓?*鍥炬**鐢绘垚涓€涓?UIElement锛堢缉鐣ュ浘锛夈€?/// |
| `UI/PatternRenderer.cs` | 鍥炬鐨勯潤鎬侀瑙堬紙涔︺€佺煶鏉裤€佸嵎杞淬€侀樋鍗″璁板綍锛変笌鍥炬妫€绱㈣緟鍔┿€?/// 鐢诲竷涓婄殑鐢靛厜绾垮瀷鍦?<see cref="Core.Canvas.PatternGeometry"/>锛堥€愯绉绘 RenderLib锛夈€?/// </summary> |
| `UI/PrimitiveBatch.cs` | 鎶?<see cref="PatternGeometry"/> 浜у嚭鐨勪笁瑙掑舰鐩存帴浜ょ粰鏄惧崱銆?/// |
| `UI/SpriteBatchBookCanvas.cs` | <see cref="IBookCanvas"/> 鐨勬父鎴忓唴瀹炵幇锛氬叏閮ㄧ敤 <c>Main.spriteBatch</c> 鐢伙紙涓嶅垏鍒板浘鍏冪粯鍒讹紝 |

### （模组根目录）

| 文件 | 职责 |
|---|---|
| `HexCastingTerraria.cs` | 妯＄粍涓诲叆鍙ｃ€?.4.5 鐨?tModLoader 浼氳嚜鍔ㄥ彂鐜?[Mod] 绫汇€?    /// |

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
