# 一条命令跑完全部验证并刷新生成物。
#
# 顺序不是随意的：
#   编译 → 离线 VM → 拖拽/几何（产出 measured.json）→ 生成状态表 → 架构断言
# 架构断言放在最后，因为它要拿前面产出的实测数字去对拍文档 ——
# 这正是审计要求的「把测试输出喂给断言」，而不是让文档自己说自己对。
#
# 用法：
#   .\run_all.ps1              # 全跑
#   .\run_all.ps1 -SkipBuild   # 已经编译过时跳过编译
param(
    [switch]$SkipBuild,
    [switch]$SkipVmTest
)

$ErrorActionPreference = 'Stop'
$tools  = $PSScriptRoot
$tmod   = Split-Path -Parent $tools
$modDir = Join-Path $tmod 'HexCastingTerraria'
$enc    = New-Object System.Text.UTF8Encoding($false)

$steps = New-Object System.Collections.Generic.List[object]

function Step([string]$name, [scriptblock]$body) {
    Write-Host ''
    Write-Host "──────── $name ────────" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $ok = & $body
        $sw.Stop()
        $steps.Add([pscustomobject]@{ Name = $name; Ok = [bool]$ok; Ms = $sw.ElapsedMilliseconds })
        return $ok
    } catch {
        $sw.Stop()
        Write-Host "  异常：$($_.Exception.Message)" -ForegroundColor Red
        $steps.Add([pscustomobject]@{ Name = $name; Ok = $false; Ms = $sw.ElapsedMilliseconds })
        return $false
    }
}

# ── 1. 编译 ─────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    [void](Step '编译（0 错 0 警）' {
        Push-Location $modDir
        try {
            $out = & "$modDir\build.ps1" -CompileOnly 2>&1 | Out-String
            Write-Host ($out -split "`n" | Select-String -Pattern '个错误|个警告' | ForEach-Object { $_.Line.Trim() } | Select-Object -Last 2)
            return ($out -match '已成功生成')
        } finally { Pop-Location }
    })
}

# ── 2. 离线 VM 测试 ─────────────────────────────────────────────────
if (-not $SkipVmTest) {
    [void](Step '离线 VM 测试（栈机语义）' {
        & "$tools\vmtest_setup.ps1" | Out-Null
        Push-Location 'D:\DeepSeekHarness\vmtest'
        try {
            $out = & dotnet run -c Release 2>&1 | Out-String
        } finally { Pop-Location }

        $m = [regex]::Match($out, '通过\s+(\d+)\s*/\s*失败\s+(\d+)')
        if (-not $m.Success) { Write-Host '  解析不到结果行' -ForegroundColor Red; return $false }

        $passed = [int]$m.Groups[1].Value
        $failed = [int]$m.Groups[2].Value
        $json = @"
{
  "passed": $passed,
  "failed": $failed,
  "generatedAtLocal": "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}
"@
        [System.IO.File]::WriteAllText((Join-Path $tools 'vmtest_last.json'), $json, $enc)
        Write-Host "  通过 $passed / 失败 $failed"
        return ($failed -eq 0)
    })
}

# ── 3. 拖拽 + 几何（产出 measured.json）─────────────────────────────
[void](Step '拖拽 + 几何验证（画布/坐标）' {
    Push-Location 'D:\DeepSeekHarness\drawtest'
    try {
        $out = & dotnet run -c Release 2>&1 | Out-String
    } finally { Pop-Location }
    $tail = ($out -split "`n" | Where-Object { $_ -match '通过 \d+ / 失败 \d+' } | Select-Object -Last 1)
    Write-Host "  $($tail.Trim())"
    return ($out -match '失败 0')
})

# ── 4. 贴图存在性 ───────────────────────────────────────────────────
[void](Step '贴图存在性（缺失必须为 0）' {
    $out = & "$tools\check_assets.ps1" 2>&1 | Out-String
    Write-Host (($out -split "`n" | Where-Object { $_ -match '缺失' }) -join ' ')
    return ($out -match '缺失：0')
})

# ── 5. 生成状态表 ───────────────────────────────────────────────────
[void](Step '生成 STATUS.generated.md' {
    & "$tools\gen_status.ps1"
    return $true
})

# ── 6. 生成架构入口 ─────────────────────────────────────────────────
[void](Step '生成 ARCHITECTURE.md' {
    & "$tools\gen_architecture.ps1"
    return $true
})

# ── 6b. 生成阶段表（肉前/肉后/月后；同时与代码里的配方对拍）─────────
[void](Step '生成 PROGRESSION.generated.md' {
    $out = & "$tools\gen_progression.ps1" 2>&1 | Out-String
    Write-Host (($out -split "`n" | Where-Object { $_ -match '阶段表：' }) -join ' ')
    return (($LASTEXITCODE -eq 0) -and ($out -notmatch '对不上'))
})

# ── 7. 架构断言 ─────────────────────────────────────────────────────
$archOk = $false
[void](Step '架构约束断言' {
    & "$tools\check_arch.ps1"
    $script:archOk = ($LASTEXITCODE -eq 0)
    return $script:archOk
})

# ── 汇总 ────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '================ run_all 汇总 ================' -ForegroundColor Cyan
foreach ($s in $steps) {
    $mark = if ($s.Ok) { 'OK  ' } else { 'FAIL' }
    $color = if ($s.Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1,-34} {2,6:N0} ms" -f $mark, $s.Name, $s.Ms) -ForegroundColor $color
}
$bad = @($steps | Where-Object { -not $_.Ok }).Count
Write-Host ''
if ($bad -eq 0) {
    Write-Host '全部通过。' -ForegroundColor Green
    exit 0
} else {
    Write-Host "$bad 步失败。" -ForegroundColor Red
    exit 1
}
