# 咒法学 · 泰拉瑞亚移植（HexCastingTerraria）

把 Minecraft 模组 [Hex Casting（咒法学）](https://github.com/FallingColors/HexMod) 移植到泰拉瑞亚（tModLoader 1.4.4.9 stable）。
玩家在六边形网格上画图案，每个图案是一条栈机指令，串起来就是一段咒术。

**原则：一切以原版为基准。** 只有泰拉真的没有对应概念（3D → 2D、没有村民、没有末地……）时才偏离，
每一处偏离都记在 [AUDIT_VS_ORIGINAL.md](AUDIT_VS_ORIGINAL.md)。

## 目录

```
tmod/
├── HexCastingTerraria/      模组本体
│   ├── Core/                纯逻辑，不引用 Terraria（可离线测试）
│   │   ├── Casting/         栈机、图案行为、Iota、法术环
│   │   ├── Canvas/          画布、图案几何与渲染数据
│   │   ├── Media/ World/    媒质、世界接口（由 Content 实现）
│   │   ├── Registry/        图案注册表
│   │   └── Ui/              咒术笔记（Patchouli 手册）的内容、排版与渲染
│   ├── Content/             游戏侧：物品、方块、玩家、联机同步、世界生成
│   ├── Client/              客户端绘制：画布、书、HUD、哨卫、探知透镜
│   ├── Config/              模组设置
│   └── Localization/        本地化（官方简体中文）
├── tests/
│   ├── vmtest/              离线栈机测试（对照原版语义）
│   └── drawtest/            离线画布 / 几何 / 书排版 / 图案渲染测试
└── _tools/                  验证、生成器（贴图、书内容、状态表）
```

分层规则：Core 不碰 Terraria；游戏相关的都在 Content / Client，通过接口接进 Core。
这条由 `_tools/check_arch.ps1` 检查。

## 构建与验证

一条命令跑完全部验证（在 PowerShell 里跑）：

```powershell
powershell -ExecutionPolicy Bypass -File _tools\run_all.ps1
```

依次是：编译（要求 0 错 0 警）→ 离线栈机测试 → 画布 / 几何测试 → 贴图检查 → 生成状态表 → 架构断言。
加 `-Package` 会另外打包 `.tmod`，并在专用服务器里真实加载一次（**需要先关游戏**）。
包输出到 `Documents\My Games\Terraria\tModLoader\Mods\`。

提交前跑一次 `python _tools/fix_eol.py` 统一换行。

## 文档

- [STATUS.generated.md](STATUS.generated.md)：当前状态（图案数、测试数、代码规模），自动生成，唯一权威
- [AUDIT_VS_ORIGINAL.md](AUDIT_VS_ORIGINAL.md)：与原版的逐项对照和所有偏差
- [PROGRESSION.generated.md](PROGRESSION.generated.md)：每个物品在哪个进度阶段出现、怎么合成
- [ARCHITECTURE.md](ARCHITECTURE.md)：每个源文件是干什么的
- [TERRARIA_RENDERING_NOTES.md](TERRARIA_RENDERING_NOTES.md)：泰拉渲染的坑

## 许可

非官方移植，与原作者无关联。原项目 HexMod 为 MIT License（Copyright (c) 2021 gamma-delta）；
书的排版移植自 Patchouli（CC BY-NC-SA 3.0），因此本模组整体以 CC BY-NC-SA 3.0 发布、不可商用。
许可证全文见 [LICENSE](LICENSE)，逐文件来源与署名见 [CREDITS.md](CREDITS.md)。

- 署名：转载或修改请保留原作者（Hex Casting：gamma-delta 及贡献者；Patchouli：Vazkii 及贡献者）和本移植的署名
- 非商业：本模组免费发布，不得用于商业用途
- 相同方式共享：分发修改版须同样采用 CC BY-NC-SA 3.0
- 来自 Hex Casting 的部分同时保留其 MIT 版权声明（[LICENSE-HexMod.txt](HexCastingTerraria/LICENSE-HexMod.txt)）

Terraria 是 Re-Logic 的商标；本仓库不包含泰拉瑞亚或 tModLoader 的任何美术与代码资源。
如果原作者或任何权利人有异议，请在本仓库提 Issue，会按要求修改、补充署名或下架。
