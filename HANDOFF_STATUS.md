<!--
  已废弃（2026-09-14）：这份文件曾经是"当前状态"，现在不是了。
  保留文件名只是为了不让旧链接 404，内容只剩一个指路牌。
-->
# 已迁移 —— 请看 [STATUS.generated.md](STATUS.generated.md)

这份 `HANDOFF_STATUS.md` 曾经写过「图案 184/188、离线测试 417/417」这类状态。

**它已经过期，而且不会再有第二份这样的文件。** 原因很直接：根目录同时躺着两份都宣称
"当前状态"的文档时，它们迟早对不上 —— 实际就发生了：这份写 417/417，
`CODEX_HANDOFF.md` 写 435/435，读的人无从判断谁更新。

现在是：

| 你想要什么 | 去哪里 |
|---|---|
| 当前状态数字（图案数 / 测试数 / 包体） | [STATUS.generated.md](STATUS.generated.md) —— **唯一权威，脚本生成** |
| 架构、分层、每个文件的职责、单一真源表 | [ARCHITECTURE.md](ARCHITECTURE.md) —— 脚本生成 |
| 历史上真实发生过的 bug 与其根因 | [CODEX_HANDOFF.md](CODEX_HANDOFF.md) |
| 设计推导（为什么这么做） | `TODO_PLAN.md`、`TERRARIA_2D_ADAPTATION.md`、`CASTING_ENGINE_SPEC.md` 等 |
| 架构约束到底有没有被遵守 | 跑 `_tools\check_arch.ps1` —— 是**会失败的断言**，不是文档里的一句约定 |

这条"数字只许出现在一份文件里"的规则由 `_tools/check_arch.ps1` 强制：
除 `STATUS.generated.md` 外，任何文档复述「N/M 图案」「通过 N/M」这类会过期的断言，
断言本身就会失败。
