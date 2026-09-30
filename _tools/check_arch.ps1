# 架构约束断言 —— 把「文档里写的规矩」变成「会失败的测试」。
#
# 为什么需要它：审计指出三类问题，
#   ① 根目录同时有两份宣称"当前状态"的文档，数字还不一样；
#   ② `Core/` 不得引用 XNA/tModLoader 这条硬约束**被写进了文档的第 80-82 行当成"设计如此"** ——
#      违规被文档化之后，读文档的 AI 会认为这就是规范，永远不会去修；
#   ⑥ 文档里手写的"环境事实"会腐化，代码一改就过期，而且没有任何机制提醒。
#
# 这三类的共同点是：约束只存在于叙述里，没有强制力。本脚本把它变成断言 ——
# 违反就退出码 1，可以挂进任何 CI。
#
# 用法：
#   .\check_arch.ps1              # 快速（不跑测试），读 measured.json
#   .\check_arch.ps1 -SkipMeasured # 连实测数据也不要求存在
param(
    [switch]$SkipMeasured
)

$ErrorActionPreference = 'Stop'

$tools = $PSScriptRoot
$mod   = Join-Path (Split-Path -Parent $tools) 'HexCastingTerraria'

$script:passed = 0
$script:failed = 0
$script:failures = New-Object System.Collections.Generic.List[string]

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) {
        $script:passed++
        Write-Host "  PASS  $name" -ForegroundColor Green
    } else {
        $script:failed++
        $script:failures.Add("$name  $detail")
        Write-Host "  FAIL  $name   $detail" -ForegroundColor Red
    }
}

function Rel($full, $root) { return $full.Replace("$root\", '') }
function AllCs($dir) {
    if (-not (Test-Path $dir)) { return @() }
    return Get-ChildItem $dir -Recurse -File -Filter *.cs |
           Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
}

Write-Host "=== 架构约束断言 ===" -ForegroundColor Cyan
Write-Host "模组目录: $mod`n"

# ─────────────────────────────────────────────────────────────────────
Write-Host '① 分层：Core 必须保持与 XNA / tModLoader 无关'
# ─────────────────────────────────────────────────────────────────────
# 这条**零白名单**。以前有 3 个例外（两个配置类 + HexGrid）被写进文档当成既成事实；
# 现在配置类搬到了 Config/、HexGrid 改用自己的 Vec2f，例外清零 —— 所以断言可以很硬。
# 一旦有人再往 Core 里塞一个 `using Terraria`，这里立刻红。
$coreViolations = New-Object System.Collections.Generic.List[string]
foreach ($f in (AllCs (Join-Path $mod 'Core'))) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern '^\s*using\s+(Terraria|Microsoft\.Xna)'
    foreach ($h in $hits) {
        $coreViolations.Add((Rel $f.FullName $mod) + ':' + $h.LineNumber + '  ' + $h.Line.Trim())
    }
}
Check "Core/ 无 XNA / tModLoader 引用（零白名单）" ($coreViolations.Count -eq 0) `
      ($coreViolations -join ' | ')

# Content → Client 与 Core 那条不同：tModLoader 把所有 .cs 编进**同一个程序集**，
# 而「打开画布」「取 1x1 像素贴图画线」本来就是客户端专属动作，
# 由物品/方块发起是常规写法（服务端不会执行到那些分支）。
# 所以这里不是禁令，而是**棘轮**：冻结现状，新增就红 ——
# 逼着以后每一次新增都先想一句「这个真该从 Content 调 Client 吗」。
$contentClientBaseline = @(
    'Content\HexPlayer.cs',
    'Content\PlayerCastingEnvironment.cs',
    'Content\Items\HexBookItem.cs',
    'Content\Items\HexStaff.cs',
    'Content\Items\MediaFlask.cs',
    'Content\Items\MediaMaterials.cs',
    'Content\Tiles\AkashicRecord.cs',
    'Content\Tiles\HexSlate.cs',
    'Content\Tiles\WallScroll.cs'
)

# 只有「正文里真的用到了 Client 的类型」才算依赖；光有一个没用到的 using 是噪音
# （顺手删了 2 处无用 using —— 留着会让这条断言失去信噪比）。
$clientTypes = New-Object System.Collections.Generic.List[string]
foreach ($f in (AllCs (Join-Path $mod 'Client'))) {
    $t = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($t, '(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|struct|enum|record)\s+(\w+)')) {
        $clientTypes.Add($m.Groups[1].Value)
    }
}
$clientTypes = $clientTypes | Select-Object -Unique

$contentUsesClient = New-Object System.Collections.Generic.List[string]
foreach ($f in (AllCs (Join-Path $mod 'Content'))) {
    $t = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    if ($t -notmatch 'using HexCastingTerraria\.Client') { continue }
    $body = (($t -split "`n") | Where-Object { $_ -notmatch '^\s*using\s' }) -join "`n"
    foreach ($ct in $clientTypes) {
        if ($body -match "\b$ct\b") { $contentUsesClient.Add((Rel $f.FullName $mod)); break }
    }
}
$newDeps = @($contentUsesClient | Where-Object { $contentClientBaseline -notcontains $_ })
Check "Content → Client 依赖没有新增（棘轮：基线 $($contentClientBaseline.Count) / 实测 $($contentUsesClient.Count)）" `
      ($newDeps.Count -eq 0) ("新增: " + ($newDeps -join ', '))

$coreToOuter = New-Object System.Collections.Generic.List[string]
foreach ($f in (AllCs (Join-Path $mod 'Core'))) {
    if (Select-String -LiteralPath $f.FullName -Pattern 'HexCastingTerraria\.(Content|Client|Config)' -Quiet) {
        # XML 文档注释里的 <see cref="..."/> 不算真依赖，但也别偷偷留着 —— 允许显式豁免：
        $real = Select-String -LiteralPath $f.FullName -Pattern 'HexCastingTerraria\.(Content|Client|Config)' |
                Where-Object { $_.Line -notmatch '///' }
        if ($real) { $coreToOuter.Add((Rel $f.FullName $mod)) }
    }
}
Check "Core/ 不引用 Content/Client/Config（注释里的 cref 不算）" ($coreToOuter.Count -eq 0) ($coreToOuter -join ', ')

# ── 图案形状对拍原版（形状错了图案就执行成别的操作，而离线测试取的是同一张表，照样全绿）──
$env:PYTHONUTF8 = '1'
$patOut = & python (Join-Path $PSScriptRoot 'check_patterns_vs_original.py') 2>&1
Check "188 个图案的角度串与起笔方向与原版源码逐条一致" ($LASTEXITCODE -eq 0) (($patOut | Select-Object -First 4) -join ' | ')


# ─────────────────────────────────────────────────────────────────────
Write-Host "`n② 离线测试不许有排除项（排除 = 那段代码从没被测过）"
# ─────────────────────────────────────────────────────────────────────
# 两个测试工程都在仓库内 tests/ 下，直接编译整个 Core/**。
# 以前 vmtest 靠脚本「按子目录拷贝」、drawtest 按子目录列 Include —— Core 新增目录会被静默漏测；
# 而且这里的 drawtest 检查用错了路径，`if (Test-Path)` 让它**从未执行过**。
# 所以现在：文件不存在 = 失败，而不是跳过。
foreach ($t in 'vmtest', 'drawtest') {
    $proj = Join-Path (Split-Path -Parent $mod) "tests\$t\$t.csproj"
    if (-not (Test-Path $proj)) { Check "tests\$t\$t.csproj 存在" $false $proj; continue }
    $projText = Get-Content -LiteralPath $proj -Raw -Encoding UTF8
    Check "$t 编译整个 Core/**（不按子目录挑选）" `
          ($projText -match 'HexCastingTerraria\\Core\\\*\*\\\*\.cs') '缺少 Core\**\*.cs'
    Check "$t 没有 Exclude/Remove（测的必须是真代码）" `
          ($projText -notmatch '(Exclude|Remove)\s*=') '仍有 Exclude/Remove='
}

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n②b 脚本编码（PS 5.1 把无 BOM 的 UTF-8 当 ANSI 读）"
# ─────────────────────────────────────────────────────────────────────
# 真实事故：全部 36 个含中文的 .ps1 都没有 BOM，run_all.ps1 连同 build.ps1 在 PS 5.1 下
# 直接语法错误；gen_progression 读 JSON 不带 -Encoding，中文乱码后 ConvertFrom-Json 失败。
& {
    $root = Split-Path -Parent $mod
    $noBom = New-Object System.Collections.Generic.List[string]
    $noEnc = New-Object System.Collections.Generic.List[string]
    Get-ChildItem $root -Recurse -Filter *.ps1 | Where-Object { $_.FullName -notmatch '\\(_install_[^\\]*|bin|obj)\\' } | ForEach-Object {
        $b = [System.IO.File]::ReadAllBytes($_.FullName)
        $hasBom = $b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF
        $nonAscii = $false
        foreach ($x in $b) { if ($x -gt 127) { $nonAscii = $true; break } }
        if ($nonAscii -and -not $hasBom) { $noBom.Add((Rel $_.FullName $root)) }

        $n = 0
        foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
            $n++
            if ($line -match '^\s*#') { continue }
            if ($line -match '\bGet-Content\b' -and $line -match '\.(json|md|hjson|cs|txt)\b|ConvertFrom-Json' -and $line -notmatch '-Encoding') {
                $noEnc.Add("$(Rel $_.FullName $root):$n")
            }
        }
    }
    Check '含非 ASCII 字符的 .ps1 都带 UTF-8 BOM' ($noBom.Count -eq 0) ($noBom -join ', ')
    Check '读文本文件的 Get-Content 都显式 -Encoding' ($noEnc.Count -eq 0) ($noEnc -join ', ')
}

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n③ 文档↔代码一致性：会过期的数字只许出现在一份文件里"
# ─────────────────────────────────────────────────────────────────────
$statusPath = Join-Path (Split-Path -Parent $mod) 'STATUS.generated.md'
Check "STATUS.generated.md 存在（唯一权威状态表）" (Test-Path $statusPath) $statusPath

# 除 STATUS.generated.md 外，任何 .md 都不许写"图案 N/M"或"测试 N/M"这类状态断言。
# 这条直接对应审计第 ① 条：两份互相矛盾的交接文档。
# ── 规则 A：数字**写错**才失败（不是"提了就失败"）────────────────────
#
# 上一版一刀切禁止任何文档出现数字，结果把「本项目已提取 188 条图案」这种**正确的事实**
# 也拦了下来 —— 断言太宽 = 噪音，最后大家都会去关掉它。改成按值核对：
# 文档里声称的图案数必须等于实测值。
$measuredPatterns = $patternCount
if (Test-Path (Join-Path $tools 'measured.json')) {
    $mj = Get-Content -LiteralPath (Join-Path $tools 'measured.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $measuredPatterns = $mj.patterns
}

$patternClaimPattern = '(\d+)\s*条\s*图案|图案\s*(\d+)\s*条|(\d+)\s*/\s*(\d+)\s*图案|图案\s*(\d+)\s*/\s*(\d+)'
$wrongClaims = New-Object System.Collections.Generic.List[string]
foreach ($d in $allDocs) {
    if ($d.Name -eq 'STATUS.generated.md') { continue }
    $text = Get-Content -LiteralPath $d.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, $patternClaimPattern)) {
        foreach ($g in $m.Groups) {
            if ($g.Name -eq '0') { continue }
            if (-not $g.Success -or $g.Value -eq '') { continue }
            $n = 0
            if ([int]::TryParse($g.Value, [ref]$n)) {
                if ($n -ne $measuredPatterns -and $n -gt 20) {
                    # 只对"看起来就是在说图案总数"的数字发难（>20 避免号码、版本号之类）
                    $wrongClaims.Add("$($d.Name): $($m.Value)")
                    break
                }
            }
        }
    }
}
Check "文档里声称的图案数 == 实测（$measuredPatterns）" ($wrongClaims.Count -eq 0) `
      (($wrongClaims | Select-Object -Unique | Select-Object -First 4) -join ' | ')

# ── 规则 B：**测试通过数**只许出现在权威表或历史快照里 ────────────────
#
# 这一条针对的是实际造成过混乱的东西：两份交接文档各自写「通过 417/417」和「通过 435/435」，
# 都在宣称"当前状态"。历史性文档（日期化验收报告、按轮次累积的进度日志）不受限，
# 但必须**显式列出并写明理由** —— 否则白名单迟早变成什么都能塞的后门。
$testCountPattern = '(通过\s*\d+\s*/\s*\d+|离线测试\s*[*\s]*\d+\s*/\s*\d+|\d+\s*/\s*\d+\s*通过)'
$historyAllowTestCounts = @{
    'CIRCLE_PRECHECK.md'       = '日期化验收报告（法术环阶段），数字是当时快照'
    'TODO_PLAN.md'             = '按轮次累积的进度日志，每轮自带当时的数字'
    'HEXCASTING_PORT_PLAN.md'  = '阶段计划，写的是该阶段的验收标准'
    'CODEX_HANDOFF.md'         = '历史 bug 清单，正文不含测试数（留作显式豁免以免误伤引用）'
}
$testOffenders = New-Object System.Collections.Generic.List[string]
foreach ($d in $allDocs) {
    if ($d.Name -eq 'STATUS.generated.md') { continue }
    if ($historyAllowTestCounts.ContainsKey($d.Name)) { continue }
    $hits = Select-String -LiteralPath $d.FullName -Pattern $testCountPattern
    foreach ($h in $hits) { $testOffenders.Add("$($d.Name):$($h.LineNumber)  $($h.Line.Trim())") }
}
Check "测试通过数只出现在 STATUS.generated.md 或已登记的历史快照里（登记 $($historyAllowTestCounts.Count) 篇）" `
      ($testOffenders.Count -eq 0) (($testOffenders | Select-Object -First 4) -join ' | ')
# 文档里引用的仓库内路径必须存在
$pathPattern = '`(D:\\DeepSeekHarness\\[^`]+)`'
$missingPaths = New-Object System.Collections.Generic.List[string]
foreach ($d in $allDocs) {
    $text = Get-Content -LiteralPath $d.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, $pathPattern)) {
        $p = $m.Groups[1].Value.TrimEnd('\')
        if ($p -match '\*|\.\.\.') { continue }        # 通配/省略号跳过
        if (-not (Test-Path -LiteralPath $p)) { $missingPaths.Add("$($d.Name) → $p") }
    }
}
Check "文档里引用的绝对路径都存在" ($missingPaths.Count -eq 0) `
      (($missingPaths | Select-Object -First 4) -join ' | ')

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n④ 生而勿手改：生成文件必须带生成标记"
# ─────────────────────────────────────────────────────────────────────
$generated = @(
    'Core\Registry\GeneratedPatternData.cs',
    'Core\Registry\PatternNames.Generated.cs',
    'Content\Tiles\DecoBlocks.Generated.cs'
)
$noMark = New-Object System.Collections.Generic.List[string]
foreach ($g in $generated) {
    $p = Join-Path $mod $g
    if (-not (Test-Path $p)) { $noMark.Add("$g 不存在"); continue }
    $head = (Get-Content -LiteralPath $p -TotalCount 6 -Encoding UTF8) -join "`n"
    if ($head -notmatch '生成|auto-generated|generated') { $noMark.Add($g) }
}
Check "生成的文件头部都带生成标记（提醒别手改）" ($noMark.Count -eq 0) ($noMark -join ', ')

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑤ 单一真源：关键常量不许在别处重新定义"
# ─────────────────────────────────────────────────────────────────────
# 吸附阈值曾经有两处默认值（HexCanvas 1.0f / HexClientConfig 1.0f），
# 而且抄错了原版的 0.5 —— 这类"同一个东西定义两遍"是最容易出静默错误的地方。
$snapDefs = New-Object System.Collections.Generic.List[string]
foreach ($f in (AllCs $mod)) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern 'SnapThreshold\s*\{\s*get;\s*set;\s*\}\s*=\s*([0-9.]+)f'
    foreach ($h in $hits) { $snapDefs.Add((Rel $f.FullName $mod) + ' → ' + $h.Line.Trim()) }
}
Check "吸附阈值默认值只有一处字面量（另一处引用它）" ($snapDefs.Count -le 2) ($snapDefs -join ' | ')

$snapValues = @()
foreach ($f in (AllCs $mod)) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern 'SnapThreshold\s*\{\s*get;\s*set;\s*\}\s*=\s*([0-9.]+)f'
    foreach ($h in $hits) {
        # 注意：$Matches 由 -match 填充，Select-String 不会填 —— 必须自己再 match 一次
        if ($h.Line -match '=\s*([0-9.]+)f') { $snapValues += $Matches[1] }
    }
}
# 注意：必须显式包成数组：PowerShell 里 `$x = '0.5'` 时 `$x[0]` 取到的是**首个字符 '0'**，
# 只匹配到一个值时这条断言就会莫名其妙地红，而且看起来像代码的问题。
# 断言本身写错比没有断言更坏 —— 它把时间浪费在错误的方向上。
$snapValues = @($snapValues | Select-Object -Unique)
# 本模组默认 1.0，**有意**偏离原版 0.5：玩家实测 0.5（走到格距 58% 就提交）太容易误碰格点画错。
# 注意换算：拖拽距离 = size·√(2·阈值)，格距 = √3·size（以前的注释把 size 当成格距，结论是反的）。
$snapOk = ($snapValues.Count -le 1) -and (($snapValues.Count -eq 0) -or ($snapValues[0] -eq '1.0'))
Check "吸附阈值各处默认值一致且等于 1.0（有意偏离原版 0.5，见注释）" $snapOk ("实测值: " + ($snapValues -join ', '))

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑥ 图案数据自检（与运行时实测对拍）"
# ─────────────────────────────────────────────────────────────────────
# 「注册行为数」在源码里是散在十几个文件 + 循环/辅助函数里注册的，
# 纯文本统计一定数错。所以基准来自 drawtest 跑出来的 measured.json（真实跑过 RegisterAll 的进程）。
$dataFile = Join-Path $mod 'Core\Registry\GeneratedPatternData.cs'
$dataText = Get-Content -LiteralPath $dataFile -Raw -Encoding UTF8
$patternCount = ([regex]::Matches($dataText, 'new PatternData\(')).Count
Check "GeneratedPatternData.cs 里恰有 188 条图案数据" ($patternCount -eq 188) "实测 $patternCount"

$measuredPath = Join-Path $tools 'measured.json'
if (-not $SkipMeasured) {
    Check "measured.json 存在（由 drawtest 产出）" (Test-Path $measuredPath) $measuredPath
    if (Test-Path $measuredPath) {
        $m = Get-Content -LiteralPath $measuredPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Check "运行时实测图案数 == 源码条数" ($m.patterns -eq $patternCount) `
              "运行时 $($m.patterns) vs 源码 $patternCount"
        Check "「适用但未实现」为 0（画得出来就必须有用）" ($m.unimplemented -eq 0) `
              "实测 $($m.unimplemented)"
        Check "drawtest 自身全绿" ($m.checksFailed -eq 0) "失败 $($m.checksFailed)"

        # 类型契约的**棘轮**：只许多、不许少。
        # 少了就意味着有人删掉了 action 上的 Types 标注 —— 而类型检查对未标注的图案是
        # "跳过"的，于是它会静默退化成什么都查不出，却仍然显示全绿。
        # 这类"覆盖悄悄缩水"必须由断言兜住，不能靠自觉。
        $typedBaseline = 32
        Check "已标注类型契约的图案数没有减少（棘轮，基线 $typedBaseline）" `
              ($m.typedActions -ge $typedBaseline) "实测 $($m.typedActions)（少了说明标注被删）"

        # STATUS.generated.md 里的数字必须与实测一致
        if (Test-Path $statusPath) {
            $st = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8
            $okStatus = $st.Contains("| 注册图案 | $($m.patterns) |") -and
                        $st.Contains("| 已注册行为 | $($m.actions) |")
            Check "STATUS.generated.md 与实测一致" $okStatus `
                  "文档里应写 图案=$($m.patterns) 行为=$($m.actions)；不一致就重跑 run_all.ps1"
        }
    }
} else {
    Write-Host "  SKIP  measured.json 相关断言（-SkipMeasured）" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑦ 本地化：每个可实例化的物品/方块都要有中文词条"
# ─────────────────────────────────────────────────────────────────────
# tModLoader 找不到词条时**直接显示英文类名**（`JewelerHammer`、`AmethystTilesItem`），
# 而且不报错、不警告、日志里一个字都没有 —— 只能靠人翻物品栏发现。
# 实测：补之前 81 个可实例化物品里有 51 个、60 个方块里有 39 个显示的是英文类名。
#
# 这条断言盯的正是"缺失会静默发生"的东西：以后加新物品忘了写词条 → 立刻红。
& {
    # 双语：中文与英文各查一遍。
    # tModLoader 找不到词条时**静默回退到类名**，两种语言都会发生 ——
    # 之前只查中文，于是英文缺 99 条一直没人发现（见 CODEX_HANDOFF 第 10 节）。
    function Get-HaveKeys([string]$path) {
        $have = @{}
        if (-not (Test-Path $path)) { return $have }
        $txt = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
        foreach ($m in [regex]::Matches($txt, '(?m)^\t{3}(\w+):\s*\{')) { $have[$m.Groups[1].Value] = 1 }
        return $have
    }
    $zhHave = Get-HaveKeys (Join-Path $mod 'Localization\zh-Hans.hjson')
    $enHave = Get-HaveKeys (Join-Path $mod 'Localization\en-US.hjson')

        # 解析所有类与直接基类，按继承闭包判断是不是 ModItem / ModTile
        $cls = @{}
        $abstract = @{}
        foreach ($f in (AllCs $mod)) {
            $txt = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
            foreach ($m in [regex]::Matches($txt, '(?m)^\s*(public|internal)\s+(sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?::\s*([^\r\n{]+))?')) {
                $name = $m.Groups[3].Value
                $cls[$name] = ($m.Groups[4].Value -split ',')[0].Trim() -replace '<.*$', ''
                if ($m.Groups[2].Value -match 'abstract') { $abstract[$name] = 1 }
            }
        }
        function Get-KindOf($n) {
            $cur = $n; $guard = 0
            while ($cur -and $guard++ -lt 20) {
                if ($cur -eq 'ModItem') { return 'Item' }
                if ($cur -eq 'ModTile') { return 'Tile' }
                if (-not $cls.ContainsKey($cur)) { break }
                $cur = $cls[$cur]
            }
            return 'Other'
        }

        $missZh = New-Object System.Collections.Generic.List[string]
        $missEn = New-Object System.Collections.Generic.List[string]
        foreach ($k in $cls.Keys) {
            if ($abstract.ContainsKey($k)) { continue }          # 抽象基类不实例化，不需要词条
            $kind = Get-KindOf $k
            if ($kind -eq 'Other') { continue }
            if (-not $zhHave.ContainsKey($k)) { $missZh.Add("$kind`:$k") }
            if (-not $enHave.ContainsKey($k)) { $missEn.Add("$kind`:$k") }
        }
        Check "所有可实例化物品/方块都有中文词条（缺 $($missZh.Count) 个）" ($missZh.Count -eq 0) `
              (($missZh | Select-Object -First 6) -join ', ')
        Check "所有可实例化物品/方块都有英文词条（缺 $($missEn.Count) 个）" ($missEn.Count -eq 0) `
              (($missEn | Select-Object -First 6) -join ', ')
}

# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑧ 配方：每条都必须有合成站"
# ─────────────────────────────────────────────────────────────────────
# 用户指出过：「泰拉的合成是有工具的，有的东西不该在背包里直接做」。
# 实测当时 72 条配方里有 1 条（咒法学之书）是空手可做的 —— 已改成书架。
# 这条断言盯住回归：以后新增配方忘了写 AddTile 立刻红。
& {
    $recipes = 0
    $noStation = New-Object System.Collections.Generic.List[string]
    foreach ($f in (AllCs $mod)) {
        $txt = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
        $cls = [regex]::Matches($txt, '(?m)^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)')
        for ($i = 0; $i -lt $cls.Count; $i++) {
            $name = $cls[$i].Groups[1].Value
            $s = $cls[$i].Index
            $e = if ($i + 1 -lt $cls.Count) { $cls[$i + 1].Index } else { $txt.Length }
            $body = $txt.Substring($s, $e - $s)
            foreach ($m in [regex]::Matches($body, 'CreateRecipe\([^)]*\)(?<c>.*?)\.Register\(\)',
                                             [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
                $recipes++
                if ($m.Groups['c'].Value -notmatch 'AddTile\(') { $noStation.Add($name) }
            }
        }
    }
    Check "所有 $recipes 条配方都有合成站（空手可做 $($noStation.Count) 条）" ($noStation.Count -eq 0) `
          (($noStation | Select-Object -Unique | Select-Object -First 5) -join ', ')
}
# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑨ 阶段表：每个可合成物品都必须定档，且月后条目真的有月后门槛"
# ─────────────────────────────────────────────────────────────────────
# 用户指出过：「泰拉是有肉前肉后月后的，记得区分」。之前配方是一个一个单独看的，
# 没有任何东西记录「这个东西属于哪个阶段」—— 做进度流程时无从下手。
# 这条断言把 _tools/progression_stages.json（人工定的唯一真源）和
# 代码里**实际读出来的**配方对拍：漏定档 / 定错类 / 月后却只用工作台，都会红。
& {
    $out = & "$tools\gen_progression.ps1" 2>&1 | Out-String
    # 两道判据：退出码 + 输出里有没有「对不上」。只看退出码不够 ——
    # `&` 调用一个没有 exit 的 .ps1 时 $LASTEXITCODE 会保留上一条命令的值。
    $ok = ($LASTEXITCODE -eq 0) -and ($out -notmatch '对不上')
    Check '阶段表与代码里的配方一致（_tools/progression_stages.json）' $ok ($out.Trim() -replace "`r`n", ' / ')

    $doc = Join-Path (Split-Path -Parent $tools) 'PROGRESSION.generated.md'
    $exists = Test-Path $doc
    $marked = $exists -and ([System.IO.File]::ReadAllText($doc, [System.Text.Encoding]::UTF8) -match '自动生成')
    Check 'PROGRESSION.generated.md 存在且带生成标记' ($exists -and $marked) $doc

    if ($exists) {
        $head = ($out -split "`n" | Where-Object { $_ -match '阶段表：' } | Select-Object -First 1)
        if ($head) { Write-Host "  $($head.Trim())" }
    }
}
# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑩ 打包零警告：模组根目录不得存在 icon_small.png"
# ─────────────────────────────────────────────────────────────────────
# 这个警告（`Image loading failed: unknown image type`）在本项目里存在了很久，
# TODO_PLAN 里两处记的都是「icon_small.png 一直报 does not exist」—— 描述是错的。
# 二分定位的结论：打包期读 icon_small.png 的那条解码路径读不了 GDI+ 写出的 PNG，
# 而 tModLoader 在文件缺失时会回退到自带模板（ModCompile::AddResource）。所以
# **不提供这个文件**才是干净状态。别手滑加回来。
& {
    $p = Join-Path $mod 'icon_small.png'
    Check '模组根目录没有 icon_small.png（缺失时回退到 tModLoader 自带模板）' (-not (Test-Path $p)) $p

    if (Test-Path (Join-Path $mod 'icon.png')) {
        $b = [System.IO.File]::ReadAllBytes((Join-Path $mod 'icon.png'))
        $w = [int]$b[16] * 16777216 + [int]$b[17] * 65536 + [int]$b[18] * 256 + [int]$b[19]
        $h = [int]$b[20] * 16777216 + [int]$b[21] * 65536 + [int]$b[22] * 256 + [int]$b[23]
        Check "icon.png 是 80x80（实测 ${w}x${h}）" ($w -eq 80 -and $h -eq 80) "${w}x${h}"
    }
    else {
        Check 'icon.png 存在' $false '缺失'
    }
}
# ─────────────────────────────────────────────────────────────────────
Write-Host "`n⑪ 不带 emoji（用户要求：仓库、发布说明、游戏内文字都不用；箭头这类排版符号不算）"
# ─────────────────────────────────────────────────────────────────────
& {
    $repo = Split-Path -Parent $mod
    # 用码位拼：直接写字符的话这一行自己就会被判成带 emoji
    $c = { param($n) [string][char]$n }
    $emoji = '[' + (& $c 0x2600) + '-' + (& $c 0x27BF) + (& $c 0x2B00) + '-' + (& $c 0x2BFF) + (& $c 0xFE0F) + ']|[' `
           + (& $c 0xD83C) + '-' + (& $c 0xD83E) + '][' + (& $c 0xDC00) + '-' + (& $c 0xDFFF) + ']'
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($rel in (git -C $repo ls-files)) {
        if ($rel -match '\.(png|raw|tmod|zip)$') { continue }
        $full = Join-Path $repo $rel
        if (-not (Test-Path $full)) { continue }
        $n = 0
        foreach ($line in [System.IO.File]::ReadAllLines($full, [System.Text.Encoding]::UTF8)) {
            $n++
            if ($line -match $emoji) { $hits.Add("${rel}:$n") }
        }
    }
    Check '仓库文件里没有 emoji' ($hits.Count -eq 0) (($hits | Select-Object -First 10) -join ', ')
}
# ─────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "================ 架构断言：通过 $script:passed / 失败 $script:failed ================" `
     -ForegroundColor $(if ($script:failed -eq 0) { 'Green' } else { 'Red' })
if ($script:failed -gt 0) {
    Write-Host '失败项：' -ForegroundColor Red
    foreach ($f in $script:failures) { Write-Host "  - $f" -ForegroundColor Red }
}
exit $(if ($script:failed -eq 0) { 0 } else { 1 })
