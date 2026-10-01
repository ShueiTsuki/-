# 咒法学 → 泰拉瑞亚 tModLoader 移植计划

> **历史快照（2026-10-01 加注）**：这是项目最早的移植计划（Phase 0，写于 2026-09-14 之前），不是当前状态，下面的「状态：进行中（Phase 0 准备）」早已过时。
> 已经变了的大事：目标平台 2026-10-01 退回 **tModLoader 1.4.4.9 stable**（net8.0 / C# 12，见 `HexCastingTerraria/HexCastingTerraria.csproj`），不再是 1.4.5；
> 媒质按原版全部来自背包物品，没有「自定义玩家资源」；图案、物品、法术环、书都已做完，附属也并进来了。
> 现在的状态看 `STATUS.generated.md`、`ADDONS.generated.md`，与原版的对照看 `AUDIT_VS_ORIGINAL.md`。
> 文中引用的 `ENVIRONMENT_AND_TOOLCHAIN.md`、`PHASE0_REPORT.md` 在 .gitignore 里，不在仓库中。

> 状态：**进行中**（Phase 0 准备）
> 源项目：[FallingColors/HexMod](https://github.com/FallingColors/HexMod) v0.11.4（commit `6b64165be3`），MIT 协议
> **目标平台：tModLoader 1.4.5**（`1.4.5-dev` beta 分支，net10.0 / C# 14）— 用户已确认
> 本文档是活文档，每完成一步回填状态。
>
> 环境与工具链清单见 `ENVIRONMENT_AND_TOOLCHAIN.md`；接口契约见 `INTERFACE_CONTRACT_v1.md`。

---

## 0. 已核实的事实（不是推测）

| 项目 | 事实 |
|---|---|
| 源规模 | 3374 个仓库条目；**已取得源码 417 个 `.java` + 193 个 `.kt`（2.58 MB）** |
| **Kotlin 占比** | **193/610 ≈ 32%**，且集中在最关键处：`CastingVM.kt`、`Action.kt`、`Operator.kt`、`SpellList.kt`、`ActionUtils.kt`。Kotlin 的空安全/协程/扩展函数/伴生对象在 C# 中需换写法，**是额外工作量** |
| 包结构 | `at.petrak.hexcasting.{api,common,client,datagen,interop,mixin,xplat}` |
| 需移植的核心包 | `api/**`（286 类）、`common/**`（327 类）、`client/**`（58 类） |
| **不需要移植** | `mixin/**`（23）、`datagen/**`（20）、`interop/**`（43）、`xplat/**`（7）、`fabric/**`（74） |
| **图案清单** | **已提取：188 条图案**（`HexActions.java` 中 188 处 `make()`，解析失败 0），产物 `PATTERN_CATALOG.json` / `.csv`，含 `Id` / `Angles` / `StartDir` / `Op类` |
| 图案注册机制 | 代码内 `make("名字", new ActionRegistryEntry(HexPattern.fromAngles("角度串", HexDir.方向), Op类))`；签名匹配按 `entry.prototype().getAngles()` 做 O(1) 查表；另有 **SpecialHandler** 注册表处理动态图案（数字字面量等）与 per-world 图案 |
| 内容量（mcmod 资料页） | 367 条 = 195 图案 + 143 物品/方块 + 3 多方块 + 10 设定 + 16 剧情（与代码实测 188 条图案接近，差额在 SpecialHandler 与附属） |
| 协议 | MIT，允许移植与再分发，需保留版权声明 |
| 目标环境 | **tModLoader 1.4.5（`1.4.5-dev`）net10.0 / C# 14**；原版 Terraria 1.4.5.8 匹配；.NET SDK 10.0.401 已装 |

**核心判断**：这不是"移植"，是**用 C# 重写**。Java 与 C#/tModLoader 之间没有一行可直接复用；
MC 的 `ItemStack`/`BlockEntity`/`Level`/`Component`/`ResourceLocation` 在 Terraria 侧全部需要重新设计。

---

## 1. 技术架构决策（需你确认 D1~D5）

| 编号 | 决策点 | 结论 | 需你确认 |
|---|---|---|---|
| **D1** | 目标版本 | **已定：tModLoader 1.4.5**（`1.4.5-dev` beta 分支，net10.0 / C# 14）。原版 Terraria 1.4.5.8 已匹配，**不需要切回 1.4.4** | 已确认 |
| **D2** | 首个交付范围 | **垂直切片**：绘制 → 图案识别 → 栈虚拟机 → 媒质 → 阿卡夏记录 → 20 条核心图案 | |
| **D3** | 媒质（Media）实现 | 自定义玩家资源（照搬原作"媒质"），不复用 Terraria 法力 | |
| **D4** | 绘制交互 | 鼠标在六边形网格上按住拖拽画线，与原作一致；不改为 Terraria 式按钮菜单 | |
| **D5** | 是否保留反噬/疯狂机制 | 保留（这是原作气质的一部分），但数值可调 | |
| **D6** | 资源对应 | MC 紫水晶 ↔ Terraria 钻石/紫晶；MC 书 ↔ 书；具体由我在实现时逐条给出对应表 | |

---

## 2. 多 Agent 分工方案

| Agent | 角色 | 职责 | 产出物 |
|---|---|---|---|
| **A0 主控** | 架构 + 集成 + 验收 | 定义接口契约、审阅所有产出、跑编译、每阶段验收、与用户确认 | 架构文档、可编译工程、阶段报告 |
| **A1 核心 VM** | `Iota` 类型体系 + `HexCastStack` 虚拟机 + 图案库注册表 | C# 版 iota/stack/registry，附单元测试 | `Core/Casting/**` + xUnit 测试通过 |
| **A2 图案内容** | 把 195 条图案的逻辑从 Java 翻译为 C# | 分批（每批 10~15 条），先做核心 20 条 | `Patterns/*.cs` |
| **A3 客户端 UI** | 六边形网格绘制画布、图案匹配、法杖 HUD、阿卡夏记录界面 | tModLoader `ModSystem` + 自绘 UI | `Client/UI/**` |
| **A4 游戏内容** | 法杖、卷轴、书、饰品、方块、多方块 → Terraria 物品/方块/`ModPlayer` | 内容映射表 + 实现 | `Content/**` |
| **A5 资源管线** | 从源仓库提取 539 张 png / 11 个 ogg，转成 tModLoader 目录结构与命名 | 资源清单 + 转换脚本 | `Assets/**` |
| **A6 验证** | 编译、跑测试、进游戏冒烟、回归 | 每阶段验收报告 | 测试报告 |

**协作契约**：A0 先冻结 `IIota` / `IPattern` / `IMediaStorage` 三个接口，A1~A4 全部按契约实现，
以此保证多 agent 并行产出能拼在一起而不冲突。

---

## 3. 分阶段计划（每阶段结束都要你确认）

### Phase 0 — 可跑骨架 `预计 1~3 个工作日`
- 建 tModLoader 1.4.5 工程 `HexCastingTerraria`（net10.0 / C#14），编译通过并产出 `.tmod`
- HexMath 六边形网格数学（`HexAngle`/`HexDir`/`HexCoord`/`HexPattern`）
- 188 条图案数据提取 + 严格校验（188/188 通过、0 重复、往返无损）+ 注册表
- 法杖物品（右键打开画布）
- 六边形网格绘制 UI（鼠标拖拽画线 + 实时识别）
- 中英文本地化骨架

**验收**：`dotnet build` 0 错误 → 进游戏能拿到法杖 → 右键弹出网格 → 画对"数字 0"图案被识别（效果先只打日志）。
**进度**：数据与算法层已完成并离线验证；UI 层待做。详见 `PHASE0_REPORT.md`。
**确认点 ①** （当前所在位置）（等待用户确认 D2~D6 后继续 UI 层）

### Phase 1 — 施法闭环（核心） `预计 1~2 周`
- `Iota` 类型体系（数、向量、布尔、实体、列表、图案、null）
- `HexCastStack` 虚拟机 + 栈操作 + 类型签名匹配
- 媒质系统（`ModPlayer` 存档持久化）
- 阿卡夏记录（Ravenmind）：持久化 iota 存储
- 20 条核心图案真实生效（能放出第一个实用法术）

**验收**：进游戏能用 20 条图案组成一个完整法术并看到效果；存档退出重进媒质与阿卡夏记录不丢。
**确认点 ②**

### Phase 2 — 内容铺开 `预计 1~2 个月`
- 剩余 175 条图案分批接入
- 全部法杖、卷轴、法术书、灌魔物品、媒质相关方块
- 反噬/疯狂机制、图案不可用状态

**验收**：每批图案均通过编译与冒烟；抽查 10 条与原作行为逐项对照。
**确认点 ③（分批确认）**

### Phase 3 — 大后期内容 `预计 2~3 个月`
- 卓越法术（Great Spells）
- 法术环 / 多方块结构（3 个）
- 阿卡夏图书馆、传媒体系统、剧情 16 条与进度系统
- 图案书（Patchouli 手册 → Terraria 自建图鉴 UI）

**确认点 ④**

### Phase 4 — 完整对齐（长期，无时间承诺）
- 367 条资料全量核对
- 与 Terraria 生态的平衡性调整
- 性能与多人联机同步验证

---

## 4. 风险清单

| 风险 | 影响 | 应对 |
|---|---|---|
| 1.4.5 迁移 | 未来 API 变动导致返工 | 目标锁 1.4.4.9；代码中隔离版本相关调用，便于日后按官方 MigrationGuide 迁移 |
| 工作量 | 数月级，可能中途停滞 | 垂直切片先行；每阶段产出可独立验收、可玩 |
| MC 概念无对应 | 部分系统需重新设计（多方块/数据包/组件） | 逐条给映射表，无法对应时集中向你确认 |
| 资源版权 | 539 张图与音频来自源项目 | MIT 允许使用，保留 NOTICE 与原作者署名 |
| 多 agent 产出冲突 | 接口不一致导致拼不起来 | A0 先冻结接口契约，A6 每阶段跑编译+测试 |
| Terraria 版本不匹配 | 原版已升 1.4.5.8，1.4.4 tML 可能无法进游戏 | **需要你确认是否把原版切回 1.4.4**（否则只能编译、不能实测） |

---

## 5. 需要你确认的事项（第 1 步的确认清单）

1. **D1~D6 六个决策点**的取值（或直接说"全按你的默认"）
2. **原版 Terraria 是否切回 1.4.4**（不切则无法进游戏验证，只能做编译级验证）
3. **Phase 0 是否现在开工**（确认后我立即用多 agent 展开）

> 回复方式：直接说"确认 D1~D6 默认，原版切回 1.4.4，Phase 0 开工"即可；
> 或指出要改的条目编号。
