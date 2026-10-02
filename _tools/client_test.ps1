<#
.SYNOPSIS
    客户端一键测试：真开一个 tModLoader 客户端进测试世界，模组里的自测跑完全部检查、截图、写报告，然后自动退出。
    退出码 0 = 全部通过，1 = 有失败，2 = 游戏开着没法测。

.DESCRIPTION
    和专用服务器测试（verify_server.ps1）互补：服务器不画图、不放方块、不进客户端那些代码路径，
    贴图、放置、图格实体、画布、真实世界里的施法、存档读档只有客户端测得到。

    做法（说明见 tests/client/README.md）：
      1. 打包（-NoBuild 跳过）；
      2. 世界用专用服务器测试生成的那个（_verify/save/Worlds/_verify_world），没有就先跑一遍 verify_server.ps1；
      3. 独立的存档目录 _verify/client/save（-tmlsavedirectory），只放这一个模组、测试用的设置（1280x720 窗口、简体中文、静音），
         绝不碰玩家自己的存档和设置；
      4. 第一段 setup：-skipselect 直接进世界，模组的 ClientTestSystem 布置场地、检查、存档后退出；
      5. 第二段 verify：再开一次读同一个存档，检查存下来的东西，再挖掉场地看掉落；
      6. 读两段的报告（_verify/client/out/report-*.json）和 client.log，汇总。
    截图在 _verify/client/out/*.png。

    运行时会弹出游戏窗口，每段一两分钟。键盘鼠标不会影响测试（测试期间屏蔽了玩家的操作）。
#>

[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 420,
    # 不重新打包，直接用上次打好的 .tmod
    [switch]$NoBuild,
    # 重新生成测试世界
    [switch]$FreshWorld,
    # 只跑一段：setup / verify（verify 要求上一次 setup 留下的存档还在）
    [ValidateSet('all', 'setup', 'verify')]
    [string]$Phase = 'all'
)

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$tml     = 'D:/steam/steamapps/common/tModLoader'
$dll     = Join-Path $tml 'tModLoader.dll'
$dotnet  = Join-Path $tml 'dotnet/dotnet.exe'
$needMajor = ((Get-Content (Join-Path $tml 'tModLoader.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json).runtimeOptions.framework.version -split '[.]')[0]
if (-not (Get-ChildItem (Join-Path $tml 'dotnet/shared/Microsoft.NETCore.App') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$needMajor.*" })) {
    Write-Host "自带 dotnet 没有 .NET $needMajor 运行时，改用系统 dotnet"
    $dotnet = (Get-Command dotnet).Source
}
$tmlLog  = Join-Path $tml 'tModLoader-Logs/client.log'
$modName = 'HexCastingTerraria'
$work    = Join-Path $repo '_verify/client'
$save    = Join-Path $work 'save'
$mods    = Join-Path $save 'Mods'
$out     = Join-Path $work 'out'
$utf8    = New-Object System.Text.UTF8Encoding($false)

# --- 游戏开着就不测：日志会混在一起，打包也会被锁 ---
$running = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match 'tModLoader[.]dll' }
if ($running) {
    Write-Host "tModLoader 正在运行（PID $(($running | ForEach-Object { $_.ProcessId }) -join ', ')），先关掉游戏再测。" -ForegroundColor Yellow
    exit 2
}

# --- 打包 ---
if (-not $NoBuild) {
    Write-Host '打包...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'HexCastingTerraria/build.ps1') -NoPrompt | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '打包失败' }
}
$tmod = Join-Path $env:USERPROFILE "Documents/My Games/Terraria/tModLoader/Mods/$modName.tmod"
if (-not (Test-Path $tmod)) { throw "找不到打包产物 $tmod" }

# --- 测试世界：用专用服务器测试生成的 ---
$serverWorlds = Join-Path $repo '_verify/save/Worlds'
if ($FreshWorld -or -not (Test-Path (Join-Path $serverWorlds '_verify_world.wld'))) {
    Write-Host '生成测试世界（专用服务器测试）...'
    $vsArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify_server.ps1'))
    if ($FreshWorld) { $vsArgs += '-FreshWorld' }
    & powershell @vsArgs | Out-Host
    if (-not (Test-Path (Join-Path $serverWorlds '_verify_world.wld'))) { throw '测试世界没生成出来' }
}

# --- 独立的存档目录（每次从头建；只跑 verify 时沿用上一次 setup 留下的） ---
if ($Phase -ne 'verify') {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $mods, (Join-Path $save 'Worlds'), (Join-Path $save 'Players'), $out | Out-Null
    Copy-Item $tmod $mods -Force
    [IO.File]::WriteAllText((Join-Path $mods 'enabled.json'), "[`"$modName`"]", $utf8)
    Copy-Item (Join-Path $serverWorlds '_verify_world.wld'), (Join-Path $serverWorlds '_verify_world.twld') (Join-Path $save 'Worlds') -Force

    # 测试用的设置：窗口 1280x720（场地一屏正好 80 格宽）、简体中文、静音、失去焦点也不降帧、界面 / 镜头缩放 1。
    # 「第一次启动」类的提示照玩家自己的设置抄过来（只读），免得弹窗
    $cfg = [ordered]@{
        Language = 'zh-Hans'; Fullscreen = $false; WindowMaximized = $false; WindowBorderless = $false
        DisplayWidth = 1280; DisplayHeight = 720; ThrottleWhenInactive = $false; AutoPause = $false
        Zoom = 1.0; UIScale = 1.0; LightingMode = 0; FrameSkipMode = 0; GraphicsQuality = 0
        VolumeSound = 0.0; VolumeAmbient = 0.0; VolumeMusic = 0.0
        SettingsEnabled_TilesSwayInWind = $false; SmartCursorToggle = $false; MapEnabled = $true
    }
    $userCfg = Join-Path $env:USERPROFILE 'Documents/My Games/Terraria/tModLoader/config.json'
    if (Test-Path $userCfg) {
        $u = Get-Content $userCfg -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($k in 'LastLaunchedVersion', 'LastLaunchedTModLoaderVersion', 'SeenFirstLaunchModderWelcomeMessage',
                       'BetaUpgradeWelcomed144', 'LastPreviewFreezeNotificationSeen', 'WarnedFamilyShareDontShowAgain', 'LatestNewsTimestamp') {
            if ($null -ne $u.$k) { $cfg[$k] = $u.$k }
        }
    }
    [IO.File]::WriteAllText((Join-Path $save 'config.json'), ($cfg | ConvertTo-Json), $utf8)
}

function Read-TmlLog([datetime]$since) {
    if (-not (Test-Path $tmlLog)) { return '' }
    if ((Get-Item $tmlLog).LastWriteTime -lt $since) { return '' }
    try {
        $fs = [IO.File]::Open($tmlLog, 'Open', 'Read', 'ReadWrite')
        try { return (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}

function Invoke-Phase([string]$name) {
    $report = Join-Path $out "report-$name.json"
    if (Test-Path $report) { Remove-Item $report -Force }
    $argv = @("`"$dll`"", '-tmlsavedirectory', "`"$save`"", '-modpath', "`"$mods`"",
              '-skipselect', 'HexTest:_verify_world', '-hexclienttest', "`"$out`"", '-hexclientphase', $name)
    $startedAt = Get-Date
    Write-Host ''
    Write-Host "== $name：启动客户端（最多等 $TimeoutSeconds 秒）==" -ForegroundColor Cyan
    $proc = Start-Process -FilePath $dotnet -WorkingDirectory $tml -PassThru -ArgumentList $argv
    $deadline = $startedAt.AddSeconds($TimeoutSeconds)
    $timedOut = $true
    $aborted = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($proc.HasExited) { $timedOut = $false; break }
        $log = Read-TmlLog $startedAt
        if ($log -match 'Disabling Mod|/FATAL\]') { $aborted = '日志里出现了 FATAL / Disabling Mod'; $timedOut = $false; break }
    }
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $proc.WaitForExit(10000) | Out-Null
    }
    Start-Sleep -Seconds 1
    $log = Read-TmlLog $startedAt
    [IO.File]::WriteAllText((Join-Path $out "client-$name.log"), $log, $utf8)
    $data = $null
    if (Test-Path $report) { $data = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json }
    Write-Host "   用时 $([int]((Get-Date) - $startedAt).TotalSeconds) 秒"
    return [pscustomobject]@{ Name = $name; TimedOut = $timedOut; Aborted = $aborted; Report = $data; Log = $log }
}

$phases = switch ($Phase) { 'all' { @('setup', 'verify') } default { @($Phase) } }
$runs = @()
foreach ($ph in $phases) {
    $r = Invoke-Phase $ph
    $runs += $r
    if (-not $r.Report) { break }   # 前一段没跑完，后一段读不到存档，不用跑了
}

# --- 汇总 ---
$fail = 0
foreach ($r in $runs) {
    Write-Host ''
    Write-Host "================ 客户端测试：$($r.Name) ================"
    if ($r.TimedOut) { Write-Host '  FAIL  超时，游戏被强制关掉' -ForegroundColor Red; $fail++ }
    if ($r.Aborted) { Write-Host "  FAIL  $($r.Aborted)" -ForegroundColor Red; $fail++ }
    if (-not $r.Report) {
        Write-Host '  FAIL  没有报告（模组的测试没跑起来或者没跑完）' -ForegroundColor Red
        $fail++
    } else {
        $section = ''
        foreach ($c in $r.Report.results) {
            if ($c.Section -ne $section) { $section = $c.Section; Write-Host "  [$section]" }
            if ($c.Ok) { Write-Host "    PASS  $($c.Name)" -ForegroundColor Green }
            else {
                Write-Host "    FAIL  $($c.Name)" -ForegroundColor Red
                if ($c.Detail) { Write-Host "          $($c.Detail)" -ForegroundColor DarkYellow }
                $fail++
            }
        }
        Write-Host "  共 $($r.Report.total) 项，失败 $($r.Report.failed) 项"
    }

    # 日志里的错误：本模组的异常、任何 ERROR / FATAL（测试自己记的失败上面已经列过）
    $entries = New-Object System.Collections.Generic.List[string]
    $cur = $null
    foreach ($line in ($r.Log -split "`r?`n")) {
        if ($line -match '^\[\d\d:\d\d:\d\d[.]\d+\]') {
            if ($null -ne $cur) { $entries.Add($cur) }
            $cur = $line
        } elseif ($null -ne $cur) { $cur += "`n" + $line }
    }
    if ($null -ne $cur) { $entries.Add($cur) }
    # 已知噪音（逐条写明原因）：
    #   icon_small.png：本模组故意不带这个文件（check_arch 第 10 条：打包时读不了 GDI+ 写的 PNG，缺了 tML 用自带模板），
    #   客户端在模组列表里找它时记一条 WARN，不影响加载和游戏
    $benign = @('Failed to load icon_small[.]png[.] Reason: icon_small[.]png does not exist')
    $problems = @($entries | Where-Object {
        $e = $_
        -not ($benign | Where-Object { $e -match $_ }) -and
        ($_ -notmatch 'HexCasting/客户端测试') -and (
            ($_ -match '/(ERROR|FATAL)\]') -or ($_ -match 'Disabling Mod|MissingResource') -or
            ($_ -match 'Exception' -and $_ -match $modName) -or ($_ -match "\[$modName\]" -and $_ -match '/WARN\]'))
    })
    if ($problems.Count -gt 0) {
        Write-Host "  FAIL  日志里有 $($problems.Count) 条错误" -ForegroundColor Red
        $problems | Select-Object -First 8 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
        $fail++
    } elseif ($r.Log) {
        Write-Host '  PASS  日志里没有错误' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host "截图：$out"
Get-ChildItem $out -Filter *.png -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ''
if ($fail -eq 0) { Write-Host '客户端测试：通过' -ForegroundColor Green; exit 0 }
Write-Host "客户端测试：失败（$fail 项）" -ForegroundColor Red
exit 1
