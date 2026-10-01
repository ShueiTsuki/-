# 法术环实现进度

> **2026-10-01 改写**：这份文件原来是 DeepSeek 阶段（2026-09-29 仓库基线之前）的进度日志，停在「步骤 4 是当前的缺口：
> 走环只推进位置，还没有执行石板上的图案」。那之后环已经会执行石板；2026-09-29 / 09-30 又按原版重做了节拍、促动石、施法者、
> 媒质、朝向和消息显示。旧日志里「环里无人施法（`CastingEntity = null`）」「朝向一律空手右键旋转」「每 tick 一格」等说法都已不成立。
> 下面按现在的代码重写。
>
> 与原版的逐条对照和全部偏差：`AUDIT_VS_ORIGINAL.md`（第 25–27 条、「法术环」一节）。原版机制：`SPELL_CIRCLE_MECHANICS.md`。
> 测试数等数字只看 `STATUS.generated.md`。

## 已完成

| 步骤 | 内容 | 代码 |
|---|---|---|
| 1 | **石板方块 + 方块实体**（环的「指令」）：存图案与朝向（`Normal`），方块上画出图案 | `Content/Tiles/HexSlate.cs` |
| 2 | **促动石**（环的「CPU」）：工具匠（不潜行右键）/ 制箭师（被盯着 30 刻）/ 牧师（电线信号，可绑定玩家）三种 + 只传导的空白促动石；媒质、出口方向、走环状态、显示、绑定 | `Content/Tiles/HexImpetus.cs`、`Content/Tiles/FletcherGaze.cs` |
| 3 | **4 个环图案**：`circle/impetus_pos`、`circle/impetus_dir`、`circle/bounds/min`、`circle/bounds/max` | `Core/Casting/Circles/CircleCastingEnvironment.cs`（`CircleActions`） |
| 4 | **环里执行石板上的图案**：每走到一块石板就把它的图案交给栈机求值，栈随环往下传；出错就停，事故显示在促动石上；空石板直通 | `HexImpetusEntity.StepOnce` |
| 5 | **导向石 3 种**：空白（随机出轴的一端）、牧羊人（弹栈顶布尔，真出反方向、假出正方向；栈顶不是布尔就把导向石打掉）、石匠（看电线信号）；牧师促动石接电线 | `Content/Tiles/HexDirectrix.cs`、`HexImpetusEntity.PickDirectrixExit` |
| 6 | **表现**：执行游标（带 TTL，自己会消）、促动石运行时亮起来（贴图亮帧 + 发光）、出口小箭头；探知透镜看促动石的媒质 / 消息 / 绑定的人 | `Content/Tiles/CircleCursor.cs`、`Client/ScryingOverlay.cs` |
| — | **Core 侧遍历**：闭包校验、出口方向、节拍，纯逻辑，离线用例在 `tests/vmtest` | `Core/Casting/Circles/CircleTraversal.cs`、`CircleComponent.cs` |
| — | **施法环境**：媒质从促动石扣；范围 = 环的包围盒 + 施法者身边 + 他的大哨卫；施法者 = 启动它的人 / 牧师绑定的人，可以没有 | `CircleCastingEnvironment`、`TerrariaCastingWorld.ForCircle` |
| — | **联机**：放置朝向、塞媒质、启动、绑定都由客户端发给服务端执行；走环只在服务端跑，状态随方块实体同步 | `HexImpetusEntity.Request` / `Handle`（`HexMessage.ImpetusAction`） |
| — | 附属 HexDebug 的调试法术环（开着附属才有） | `Addons/HexDebug/Game/CircleDebugging.cs` |

## 关键实现点（都来自源码核对）

| 点 | 实现 |
|---|---|
| 图案在**石板**上，不在促动石上 | 石板方块实体存 `Pattern` |
| **促动石的进入规则与普通部件不同** | `CircleComponent` 用 `AllowedEntries` / `ExitMask` 两个掩码：石板不能从 `Normal` 的反方向进、不能往 `Normal` 出；促动石不能从出口的反方向进；导向石只能从垂直于轴的方向进、只从轴的两端出 |
| **不能原路返回** | `CircleTraversal.ExitDirections` 减去来路的反方向 |
| **石板 / 空白促动石恰好 1 个出口** | 0 个（找不到出口）/ 2 个以上（去路过多）都停下，并在促动石上报坐标 |
| **长度上限** | `CircleTraversal.DefaultMaxLength = 1024`（原版默认值；曾误写 512） |
| **走得越深越快** | `TickSpeed(n) = max(2, 10 - (n-1)/3)` 个 MC 刻，`TickSpeedFrames` ×3 换成泰拉帧（曾把 MC 刻直接当帧用，快了 3 倍） |
| **每格重置算力** | 每块石板求值后 `WithOverriddenUsedOps(0)` |
| **媒质负数 = 无限** | `ExtractMedia` 里 `if (Media < 0) return 0`；新放的促动石是 0 |
| 逐格驱动 | `ModTileEntity.PostGlobalUpdate`（对应 MC 的 `scheduleTick`），只在服务端 / 单机跑 |
| 走回促动石 = 环闭合 | 正常结束、熄灭，不发任何消息（原版促动石的 `acceptControlFlow` 返回 Stop） |

## 与原版的差异（都写在代码注释里；全部偏差以 `AUDIT_VS_ORIGINAL.md` 为准）

| 差异 | 原因 |
|---|---|
| 只有 4 个控制流方向 | 2D 没有第三轴（原版 6 向退化成 4 向） |
| 石板的朝向放下时默认朝上，**空手右键循环旋转**；促动石 / 导向石按放置时鼠标相对角色的方向定（潜行反过来，同原版），之后不能改 | 泰拉图格不记录「贴在哪个面」，石板没法像原版那样按贴的面自动定朝向 |
| 没有漏斗：拿着媒质物品右键塞进促动石 | 泰拉没有漏斗 |
| 牧师促动石接电线、按玩家名绑定 | 泰拉没有红石；也没有 GameProfile |
| 制箭师的计数每个客户端各数各的 | 泰拉的「视线」是鼠标方向，只有本人客户端知道 |
| 走环到一半存档时，栈上的跳转 iota 换成垃圾 | 移植版的跳转 iota 还存不了档（原版能存）；走环状态本身照原版存档，读档后接着走 |

## 尚未做

目前没有已知缺口。新发现的偏差记到 `AUDIT_VS_ORIGINAL.md`，不再记在这里。
