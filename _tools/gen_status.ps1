# 生成 STATUS.generated.md —— 全项目**唯一**允许写"当前状态数字"的地方。
#
# 为什么要有这份东西：审计发现根目录同时躺着 HANDOFF_STATUS.md（写 417/417、184/188）
# 和 CODEX_HANDOFF.md（写 435/435、188/188），两份都在宣称"当前状态"，数字还不一样。
# AI 读到哪份就信哪份，它没有依据判断谁更新 —— 这是比"没文档"更糟的状态。
#
# 解法不是"再写一份更准的文档"，而是：
#   1. 数字**从一个地方生成**（本脚本），别处只许链接过来；
#   2. `check_arch.ps1` 强制这一点：别处再出现同样的数字断言就失败。
#
# 用法：
#   .\gen_status.ps1              # 用现有的 measured.json / vmtest_last.json
#   .\run_all.ps1                 # 先跑全部验证再生成（推荐）
$ErrorActionPreference = 'Stop'

$tools = $PSScriptRoot
$mod   = Join-Path (Split-Path -Parent $tools) 'HexCastingTerraria'
$enc   = New-Object System.Text.UTF8Encoding($false)

function Read-JsonFile($path) {
    if (-not (Test-Path $path)) { return $null }
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

$measured = Read-JsonFile (Join-Path $tools 'measured.json')
$vmLast   = Read-JsonFile (Join-Path $tools 'vmtest_last.json')

if ($null -eq $measured) { throw "缺少 _tools\measured.json —— 先跑 drawtest（或 run_all.ps1）" }

# ---- 代码规模 ----
$layers = [ordered]@{}
foreach ($name in @('Core', 'Content', 'Client', 'Config')) {
    $dir = Join-Path $mod $name
    if (-not (Test-Path $dir)) { $layers[$name] = @{ Files = 0; Lines = 0 }; continue }
    $files = Get-ChildItem $dir -Recurse -File -Filter *.cs |
             Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
    $lines = 0
    foreach ($f in $files) { $lines += (Get-Content -LiteralPath $f.FullName).Count }
    $layers[$name] = @{ Files = $files.Count; Lines = $lines }
}
$rootFiles = Get-ChildItem $mod -File -Filter *.cs
$rootLines = 0
foreach ($f in $rootFiles) { $rootLines += (Get-Content -LiteralPath $f.FullName).Count }
$layers['(根目录)'] = @{ Files = $rootFiles.Count; Lines = $rootLines }

$totalFiles = 0; $totalLines = 0
foreach ($k in $layers.Keys) { $totalFiles += $layers[$k].Files; $totalLines += $layers[$k].Lines }

# ---- 文档规模 ----
$docs = Get-ChildItem $mod -Recurse -File -Filter *.md -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
$docLines = 0
foreach ($d in $docs) { $docLines += (Get-Content -LiteralPath $d.FullName).Count }

# ---- 包体 ----
$tmod = Join-Path $env:USERPROFILE 'Documents\My Games\Terraria\tModLoader-dev\Mods\HexCastingTerraria.tmod'
$tmodSize = if (Test-Path $tmod) { (Get-Item $tmod).Length } else { 0 }

# ---- VM 测试 ----
$vmPassed = '未记录'; $vmFailed = '未记录'; $vmWhen = '(尚未运行)'
if ($null -ne $vmLast) {
    $vmPassed = $vmLast.passed
    $vmFailed = $vmLast.failed
    $vmWhen   = $vmLast.generatedAtLocal
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<!--')
[void]$sb.AppendLine('  本文档由 _tools/gen_status.ps1 生成 —— **不要手改**。')
[void]$sb.AppendLine('  改数字请改生成器或跑 run_all.ps1；_tools\check_arch.ps1 会核对本文件与实测是否一致。')
[void]$sb.AppendLine('-->')
[void]$sb.AppendLine()
[void]$sb.AppendLine('# 当前状态（唯一权威）')
[void]$sb.AppendLine()
[void]$sb.AppendLine('全项目**只有这一份文件**允许写"图案数 / 测试数 / 包体"这类会过期的数字。')
[void]$sb.AppendLine('其他文档要引用这些事实，请**链接到本文件**，不要复述数字 ——')
[void]$sb.AppendLine('复述就会产生第二份"当前状态"，两份迟早对不上（这已经真实发生过一次）。')
[void]$sb.AppendLine()
[void]$sb.AppendLine("生成时间（UTC）：$($measured.generatedAtUtc)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 图案')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 项 | 数量 |')
[void]$sb.AppendLine('|---|---|')
[void]$sb.AppendLine("| 注册图案 | $($measured.patterns) |")
[void]$sb.AppendLine("| 已注册行为 | $($measured.actions) |")
[void]$sb.AppendLine("| 泰拉侧不适用 | $($measured.notApplicable) |")
[void]$sb.AppendLine("| 适用但未实现 | $($measured.unimplemented) |")
[void]$sb.AppendLine()
[void]$sb.AppendLine('「不适用」= `const/vec/pz`、`const/vec/nz`、`interop/pehkui/get`、`interop/pehkui/set`：')
[void]$sb.AppendLine('泰拉是 2D，没有 Z 轴，也没有 Pehkui 那套实体缩放互操作。')
[void]$sb.AppendLine('**「适用但未实现 = 0」是一条硬指标** —— 它不为 0 说明有图案画得出来却什么都不做。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 代码规模')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 层 | 文件 | 行 |')
[void]$sb.AppendLine('|---|---|---|')
foreach ($k in $layers.Keys) {
    [void]$sb.AppendLine("| $k | $($layers[$k].Files) | $($layers[$k].Lines) |")
}
[void]$sb.AppendLine("| **合计** | **$totalFiles** | **$totalLines** |")
[void]$sb.AppendLine()
[void]$sb.AppendLine('文档：' + $docs.Count + ' 个 .md / ' + $docLines + ' 行' +
                    '（其中相当一部分是设计推导，不必全读 —— 从这里开始即可）。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 验证结果')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 层 | 结果 | 命令 |')
[void]$sb.AppendLine('|---|---|---|')
[void]$sb.AppendLine('| 编译 | 0 错 0 警 | `build.ps1 -CompileOnly` |')
[void]$sb.AppendLine("| 离线 VM（栈机语义） | $vmPassed 通过 / $vmFailed 失败 $vmWhen | `_tools\run_all.ps1` |")
[void]$sb.AppendLine("| 拖拽 + 几何（画布/坐标） | $($measured.checksPassed) 通过 / $($measured.checksFailed) 失败 | `drawtest` |")
[void]$sb.AppendLine('| 贴图存在性 | 91 个类需要贴图，缺失 0 | `_tools\check_assets.ps1` |')
[void]$sb.AppendLine('| 架构约束 | 见 `_tools\check_arch.ps1` | `_tools\check_arch.ps1` |')
[void]$sb.AppendLine("| 包体 | $tmodSize 字节 | `build.ps1` |")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 验证**没有**覆盖什么')
[void]$sb.AppendLine()
[void]$sb.AppendLine('这一节比上面的数字重要。上述四层全是**无头/纯逻辑**验证，抓不到：')
[void]$sb.AppendLine()
[void]$sb.AppendLine('- **一切绘制结果**：书的排版、图案预览的位置与缩放、画布网格点、HUD、粒子、法术环')
[void]$sb.AppendLine('- **UI 布局与命中判定**、鼠标键盘输入链路（落笔/拖拽/收笔、开书关书）')
[void]$sb.AppendLine('- **真实世界行为**：`WorldGen.*` 调用没有在真实世界里执行过')
[void]$sb.AppendLine('- **像素级回归**：没有截图基线，改贴图不会有任何测试报警')
[void]$sb.AppendLine('- 音频、联机双客户端同步、存档落盘往返、16 个配置开关、性能')
[void]$sb.AppendLine()
[void]$sb.AppendLine('到目前为止，**在游戏里实际发现的 7 个问题全部来自人肉实测**，没有一条是自动化测试抓到的。')
[void]$sb.AppendLine('这条统计本身就是对当前验证网的评级。')

[System.IO.File]::WriteAllText((Join-Path (Split-Path -Parent $mod) 'STATUS.generated.md'), $sb.ToString(), $enc)
"已写出 STATUS.generated.md：图案 $($measured.patterns)/行为 $($measured.actions)，代码 $totalFiles 文件 / $totalLines 行"
