# 和原版对拍

同一批咒术，原版咒法学（HexMod 0.11.4，Fabric 1.20.1）和移植版各跑一遍，逐步比对。原版就是标准答案。

## 比什么

每个用例是「初始栈 + 一串 iota」。照原版法杖的方式一个一个执行：每步新建施法环境，施法镜像接着用，栈清空就换新镜像。每一步比：

- 结果类型（执行了 / 转义了 / 出错了 / 不是图案……）
- 栈（数按相对误差 1e-9 比）
- 事故（类名；移植版改了名、意思一样的在对拍程序里登记别名）
- 扣了多少媒质、用了多少操作数、括号层数、转义状态、栈是否清空、渡鸦之思

消息文字不比：原版服务器只有英文，移植版是中文。

## 文件

| 路径 | 作用 | 进仓库 |
|---|---|---|
| `mc/` | 测试模组（Java），放进原版服务器，执行用例、导出结果 | 是 |
| `original.py` | 编译测试模组、起没有界面的 MC 服务器跑原版 | 是 |
| `gen_cases.py` | 生成用例：图案笔顺一律取自原版运行时导出的注册表 | 是 |
| `golden/registry.json` | 原版运行时的图案注册表（id、起始方向、笔顺、是不是本世界图案、实现类、是不是法术） | 是 |
| `golden/basic.json.gz` | 原版跑 `cases/basic.json` 的结果，即标准答案 | 是 |
| `port/` | 移植版这一侧：直接编译模组的 `Core/`，跑同样的用例并比对 | 是 |
| `cases/`、`out/` | 生成的用例、比对报告 | 否 |

`_tools/run_all.ps1` 里的「和原版对拍」一步只跑移植版这一侧，拿入库的标准答案比，不需要 MC。

## 原版那边的运行目录

MC 本体、依赖、世界存档都不进仓库，放在 `D:/DeepSeekHarness/oracle_run`，可以用环境变量 `HEXORACLE_RUN` 改位置。里面要有：

- `minecraft-1.20.1.jar`：MC 1.20.1 本体。客户端 jar 也行，它里面带着专用服务器的入口；
- `classpath.txt`：Fabric 加载器和 MC 的库，分号分隔；
- `mods/`：咒法学 0.11.4 和它的依赖，包括 fabric-api、fabric-language-kotlin、cardinal-components、paucal、cloth-config、patchouli、inline；
- `eula.txt`（`eula=true`）和 `server.properties`：平坦世界、离线模式、关掉生物生成。

第一次起服务器时，Fabric 会把 MC 本体转成中间名，放在 `.fabric/remappedJars/` 里，测试模组就拿它来编译。

## 用例改了以后

```
python tests/oracle/gen_cases.py
python tests/oracle/original.py build
python tests/oracle/original.py run tests/oracle/cases/basic.json tests/oracle/golden/basic.json.gz
```

改了生成器却没重跑原版，对拍会报「原版没有结果」并判红。

## 范围

- **已经比**：所有不改世界的图案，包括运算、列表、栈操作、逻辑、转义、元求值和读写渡鸦之思，配几十种初始栈；数字和簿记员的特殊图案；内省、反思、赫尔墨斯、伊里斯、托特、卡戎组成的多步程序。
- **还没比**：
  - 会改世界的法术、卓越法术；
  - 要用到世界和施法者的图案，比如实体、方块、哨卫、射线。

  这些在移植版这边要先搭一个和 MC 测试场景对应的假世界。现在它们在移植版报的是「没有世界 / 施法者不对」，单独计数，不算差异。
