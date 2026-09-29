# 一条命令跑完全部验证并刷新生成物。退出码 0 = 全部通过。
#
# 顺序不是随意的：
#   环境指纹 → 编译 → 离线 VM → 拖拽/几何（产出 measured.json）→ 贴图
#   →（可选）打包 + 专用服务器加载 → 生成状态表 → 架构断言
# 架构断言放在最后，因为它要拿前面产出的实测数字去对拍文档。
#
# 用法：
#   .\run_all.ps1              # 全部离线验证
#   .\run_all.ps1 -Package     # 另外打包 .tmod 并在专用服务器里真实加载（需先关游戏）
#   .\run_all.ps1 -SkipBuild   # 已经编译过时跳过编译（汇总里记为 SKIP，不算全绿）
#
# 判定规则（上一版在这里栽过的坑，逐条写明）：
#   - 每一步都以**子进程退出码**为准，而不是在输出里搜关键字。
#     旧版 drawtest 用 `$out -match '失败 0'` 判定 —— 中途一行「（失败 0 条）」就能让
#     最终「失败 5」的汇总被判为通过。
#   - 编译步骤要求 0 错误 **且** 0 警告（旧版名字写着 0 警，实际只查「已成功生成」）。
#   - 生成器步骤也看退出码（旧版无条件 return $true）。
#   - 被跳过的步骤显示 SKIP，最终结论不会是「全部通过」。
#   - 子脚本一律用 `powershell -File` 子进程跑：未捕获的 throw 会变成退出码 1，
#     不依赖每个脚本自己记得写 `exit`（`&` 调用时没写 exit 的脚本会留下陈旧的 $LASTEXITCODE）。
param(
    [switch]$SkipBuild,
    [switch]$SkipVmTest,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$tools  = $PSScriptRoot
$tmod   = Split-Path -Parent $tools
$modDir = Join-Path $tmod 'HexCastingTerraria'
$tml    = 'D:\steam\steamapps\common\tModLoader'
$enc    = New-Object System.Text.UTF8Encoding($false)

$steps = New-Object System.Collections.Generic.List[object]
# 每一步的实测事实 —— 写进 run_last.json，gen_status.ps1 只从这里取数（不许写死）
$facts = [ordered]@{ at = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') }

function Step([string]$name, [scriptblock]$body) {
    Write-Host ''
    Write-Host "──────── $name ────────" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $state = 'FAIL'
    try {
        $ok = & $body
        $state = if ($ok) { 'OK' } else { 'FAIL' }
    } catch {
        Write-Host "  异常：$($_.Exception.Message)" -ForegroundColor Red
    }
    $sw.Stop()
    $steps.Add([pscustomobject]@{ Name = $name; State = $state; Ms = $sw.ElapsedMilliseconds })
}
function Skip([string]$name, [string]$why) {
    $steps.Add([pscustomobject]@{ Name = $name; State = 'SKIP'; Ms = 0 })
    Write-Host ''
    Write-Host "──────── $name ── 跳过（$why）" -ForegroundColor DarkYellow
}

# 以子进程跑一个 .ps1；返回 @{ Code; Out }
function Invoke-Ps1([string]$path, [string[]]$argv = @()) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path @argv 2>&1 | Out-String -Width 400
    return @{ Code = $LASTEXITCODE; Out = $out }
}

# 跑一个 dotnet 测试工程；以退出码 + **最后一条**汇总行判定
function Invoke-TestProject([string]$dir) {
    # 测试程序在输出被重定向时固定写 UTF-8（tests/Shared/Utf8Console.cs），这里按 UTF-8 解码；
    # 不设的话解码跟随控制台代码页，从不同终端启动会得到乱码、解析不到汇总行。用完还原。
    $prevEnc = [Console]::OutputEncoding
    Push-Location $dir
    try {
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $out = & dotnet run -c Release 2>&1 | Out-String -Width 400; $code = $LASTEXITCODE
    }
    finally { [Console]::OutputEncoding = $prevEnc; Pop-Location }
    $ms = [regex]::Matches($out, '通过\s+(\d+)\s*/\s*失败\s+(\d+)\s*=')
    if ($ms.Count -eq 0) {
        Write-Host '  解析不到汇总行（多半是测试工程编译失败）：' -ForegroundColor Red
        ($out -split "`n" | Where-Object { $_ -match 'error' } | Select-Object -First 10) | ForEach-Object { Write-Host "    $($_.Trim())" }
        return @{ Ok = $false; Passed = 0; Failed = -1 }
    }
    $last = $ms[$ms.Count - 1]
    $p = [int]$last.Groups[1].Value; $f = [int]$last.Groups[2].Value
    Write-Host "  通过 $p / 失败 $f   (退出码 $code)"
    if ($f -gt 0) {
        ($out -split "`n" | Where-Object { $_ -match '^\s*FAIL' } | Select-Object -First 15) | ForEach-Object { Write-Host "    $($_.Trim())" -ForegroundColor Red }
    }
    return @{ Ok = ($code -eq 0 -and $f -eq 0 -and $p -gt 0); Passed = $p; Failed = $f }
}

# ── 0. 环境指纹：tML 1.4.5-dev 随每次上游提交自动更新，API 会变 ────────
$lastGreenPath = Join-Path $tools 'last_green.json'
$tmlCommit = ''
$commitsFile = Join-Path $tml 'RecentGitHubCommits.txt'
if (Test-Path $commitsFile) {
    $tmlCommit = ((Get-Content $commitsFile -TotalCount 1 -Encoding UTF8) -split ' ')[0]
}
$tmlBuilt = (Get-Item (Join-Path $tml 'tModLoader.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
Write-Host "tModLoader: commit $($tmlCommit.Substring(0, [Math]::Min(10, $tmlCommit.Length)))  dll $tmlBuilt"
if (Test-Path $lastGreenPath) {
    $lg = [System.IO.File]::ReadAllText($lastGreenPath, $enc) | ConvertFrom-Json
    if ($lg.tmlCommit -ne $tmlCommit) {
        Write-Host "  ⚠ tModLoader 自上次全绿（$($lg.at)，commit $($lg.tmlCommit.Substring(0,10))）以来已更新 —— 编译错误多半来自上游 API 变动。" -ForegroundColor Yellow
    }
}

# ── 1. 编译：0 错 0 警 ───────────────────────────────────────────────
if ($SkipBuild) { Skip '编译（0 错 0 警）' '-SkipBuild' }
else {
    Step '编译（0 错 0 警）' {
        $r = Invoke-Ps1 (Join-Path $modDir 'build.ps1') @('-CompileOnly')
        $diag = [regex]::Matches($r.Out, '[^\s\\]+\.cs\(\d+,\d+\): (warning|error) [A-Z]+\d+:[^\[\r\n]*') |
                ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique
        $errs  = @($diag | Where-Object { $_ -match ': error ' })
        $warns = @($diag | Where-Object { $_ -match ': warning ' })
        Write-Host "  错误 $($errs.Count) / 警告 $($warns.Count)   (退出码 $($r.Code))"
        $script:facts.compile = [ordered]@{ ok = ($r.Code -eq 0); errors = $errs.Count; warnings = $warns.Count }
        $diag | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" -ForegroundColor $(if ($_ -match ': error ') { 'Red' } else { 'Yellow' }) }
        return ($r.Code -eq 0 -and $errs.Count -eq 0 -and $warns.Count -eq 0)
    }
}

# ── 2. 离线 VM 测试 ─────────────────────────────────────────────────
if ($SkipVmTest) { Skip '离线 VM 测试（栈机语义）' '-SkipVmTest；STATUS 里的 VM 数字将是旧的' }
else {
    Step '离线 VM 测试（栈机语义）' {
        $r = Invoke-TestProject (Join-Path $tmod 'tests\vmtest')
        $json = @"
{
  "passed": $($r.Passed),
  "failed": $($r.Failed),
  "generatedAtLocal": "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}
"@
        [System.IO.File]::WriteAllText((Join-Path $tools 'vmtest_last.json'), $json, $enc)
        $script:facts.vmtest = [ordered]@{ passed = $r.Passed; failed = $r.Failed }
        return $r.Ok
    }
}

# ── 3. 拖拽 + 几何（产出 measured.json）─────────────────────────────
Step '拖拽 + 几何验证（画布/坐标）' {
    $d = Invoke-TestProject (Join-Path $tmod 'tests\drawtest')
    $script:facts.drawtest = [ordered]@{ passed = $d.Passed; failed = $d.Failed }
    return $d.Ok
}

# ── 4. 贴图存在性 ───────────────────────────────────────────────────
Step '贴图存在性（缺失必须为 0）' {
    $r = Invoke-Ps1 (Join-Path $tools 'check_assets.ps1')
    Write-Host (($r.Out -split "`n" | Where-Object { $_ -match '缺失' }) -join ' ')
    $need = [regex]::Match($r.Out, '需要贴图的具体类：(\d+)')
    $miss = [regex]::Match($r.Out, '缺失：(\d+)')
    $script:facts.assets = [ordered]@{
        needTexture = if ($need.Success) { [int]$need.Groups[1].Value } else { -1 }
        missing     = if ($miss.Success) { [int]$miss.Groups[1].Value } else { -1 } }
    return ($r.Code -eq 0 -and $r.Out -match '缺失：0 个')
}

# ── 5.（可选）打包 + 专用服务器真实加载 ────────────────────────────
if ($Package) {
    $packed = $false
    Step '打包 .tmod' {
        $r = Invoke-Ps1 (Join-Path $modDir 'build.ps1') @('-NoPrompt')
        ($r.Out -split "`n" | Where-Object { $_ -match '打包成功|异常|Exception|TML\d+|: error ' } | Select-Object -First 8) |
            ForEach-Object { Write-Host "  $($_.Trim())" }
        $script:packed = ($r.Code -eq 0)
        $tm = Join-Path $env:USERPROFILE 'Documents\My Games\Terraria\tModLoader-dev\Mods\HexCastingTerraria.tmod'
        $script:facts.package = [ordered]@{ ok = $script:packed; bytes = $(if (Test-Path $tm) { (Get-Item $tm).Length } else { 0 }) }
        return $script:packed
    }
    if ($packed) {
        Step '专用服务器加载 + 进入世界' {
            $r = Invoke-Ps1 (Join-Path $tools 'verify_server.ps1')
            ($r.Out -split "`n" | Where-Object { $_ -match 'PASS|FAIL|忽略|问题' }) | ForEach-Object { Write-Host "  $($_.Trim())" }
            $script:facts.server = [ordered]@{ ok = ($r.Code -eq 0) }
            return ($r.Code -eq 0)
        }
    } else { Skip '专用服务器加载 + 进入世界' '打包失败' }
} else {
    Skip '打包 + 专用服务器加载' '未指定 -Package'
}

# ── 6. 生成物 ───────────────────────────────────────────────────────
$facts.tml = [ordered]@{ commit = $tmlCommit; dllBuilt = $tmlBuilt }
$facts.skipped = @($steps | Where-Object { $_.State -eq 'SKIP' } | ForEach-Object { $_.Name })
[System.IO.File]::WriteAllText((Join-Path $tools 'run_last.json'), ($facts | ConvertTo-Json -Depth 4), $enc)
foreach ($g in @(
        @{ Name = '生成 STATUS.generated.md';      Script = 'gen_status.ps1' },
        @{ Name = '生成 ARCHITECTURE.md';          Script = 'gen_architecture.ps1' },
        @{ Name = '生成 PROGRESSION.generated.md'; Script = 'gen_progression.ps1' })) {
    $gg = $g
    Step $gg.Name {
        $r = Invoke-Ps1 (Join-Path $tools $gg.Script)
        ($r.Out -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 2) | ForEach-Object { Write-Host "  $($_.Trim())" }
        return ($r.Code -eq 0 -and $r.Out -notmatch '对不上')
    }
}

# ── 7. 架构断言 ─────────────────────────────────────────────────────
Step '架构约束断言' {
    $r = Invoke-Ps1 (Join-Path $tools 'check_arch.ps1')
    ($r.Out -split "`n" | Where-Object { $_ -match 'FAIL|架构断言：' }) | ForEach-Object { Write-Host "  $($_.Trim())" }
    return ($r.Code -eq 0)
}

# ── 汇总 ────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '================ run_all 汇总 ================' -ForegroundColor Cyan
foreach ($s in $steps) {
    $color = switch ($s.State) { 'OK' { 'Green' } 'SKIP' { 'DarkYellow' } default { 'Red' } }
    Write-Host ("  [{0,-4}] {1,-34} {2,8:N0} ms" -f $s.State, $s.Name, $s.Ms) -ForegroundColor $color
}
$bad     = @($steps | Where-Object { $_.State -eq 'FAIL' }).Count
$skipped = @($steps | Where-Object { $_.State -eq 'SKIP' }).Count
Write-Host ''
if ($bad -gt 0) {
    Write-Host "$bad 步失败。" -ForegroundColor Red
    exit 1
}
if ($skipped -gt 0) {
    Write-Host "执行的步骤全部通过，但有 $skipped 步被跳过 —— 这不是完整的全绿。" -ForegroundColor Yellow
} else {
    Write-Host '全部通过。' -ForegroundColor Green
}
# 只有真正「编译 + 测试」都跑过才记录为全绿基线
if (-not $SkipBuild -and -not $SkipVmTest) {
    $json = @"
{
  "at": "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
  "tmlCommit": "$tmlCommit",
  "tmlDllBuilt": "$tmlBuilt",
  "packagedAndServerLoaded": $(if ($Package) { 'true' } else { 'false' })
}
"@
    [System.IO.File]::WriteAllText($lastGreenPath, $json, $enc)
}
exit 0
