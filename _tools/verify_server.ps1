<#
.SYNOPSIS
    无人值守验证：用独立存档目录 + 自动生成小世界启动 tModLoader 专用服务器，
    检查模组能否正常加载并进入世界，然后关闭。退出码 0 = 通过，1 = 失败。

.DESCRIPTION
    为什么不直接用客户端：客户端进世界需要人工点击（选角色、选世界），
    而服务器可以 autocreate 自动建世界，全程无需交互。
    用 -tmlsavedirectory 指到临时目录，绝不触碰玩家真实存档。

    判定以 tModLoader 自己的日志 tModLoader-Logs\server.log 为准（UTF-8），
    **不是**进程 stdout：stdout 只有本地化后的简略文字，异常堆栈只写进 server.log。

    通过条件（全部满足）：
      1. 出现「Mod Load Completed」
      2. 出现本模组的自动加载行，且本模组的自检日志报告「失败 0 条」
      3. 出现「服务器已启动 / Server started」（世界生成 + 加载都走完了）
      4. 没有任何 ERROR/FATAL 级日志，没有 Disabling Mod / MissingResource，
         没有任何堆栈里含 HexCastingTerraria 的异常
    已知噪音（白名单，**逐条写明原因**）：
      - ConsolePal.* 的 IOException「句柄无效」：stdout 被重定向后 Console.Clear() 失败，
        tML 自己静默捕获，与模组无关。
    注意：服务器在 Steam 安装目录下写日志，**客户端开着时**也会用同一目录（client.log），互不覆盖。
#>

[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 300,
    [string]$WorkDir = "D:\DeepSeekHarness\tmod\_verify",
    # 保留自动生成的世界（下次启动直接加载，快很多）。-FreshWorld 强制重新生成。
    [switch]$FreshWorld
)

$ErrorActionPreference = 'Stop'

$tml     = "D:\steam\steamapps\common\tModLoader"
$dll     = Join-Path $tml "tModLoader.dll"
$dotnet  = Join-Path $tml "dotnet\dotnet.exe"
$tmlLog  = Join-Path $tml "tModLoader-Logs\server.log"
$modName = 'HexCastingTerraria'

# --- 准备隔离的存档目录 ---
$saveDir   = Join-Path $WorkDir "save"
$modsDir   = Join-Path $saveDir "Mods"
$worldsDir = Join-Path $saveDir "Worlds"
New-Item -ItemType Directory -Force -Path $modsDir, $worldsDir | Out-Null
if ($FreshWorld) { Get-ChildItem $worldsDir -Filter '_verify_world*' | Remove-Item -Force }

$modSrc = Join-Path $env:USERPROFILE "Documents\My Games\Terraria\tModLoader-dev\Mods\$modName.tmod"
if (-not (Test-Path $modSrc)) { throw "找不到打包产物 $modSrc —— 先跑 build.ps1（不带 -CompileOnly）" }
Copy-Item $modSrc $modsDir -Force
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $modsDir "enabled.json"), "[`"$modName`"]", $utf8)

$cfg = Join-Path $WorkDir "serverconfig.txt"
[IO.File]::WriteAllLines($cfg, [string[]]@(
    "worldpath=$worldsDir"
    "worldname=_verify_world"
    "autocreate=1"
    "world=$worldsDir\_verify_world.wld"
    "maxplayers=1"
    "port=7799"
    "noupnp=1"
    "language=zh-Hans"
), $utf8)

$stdout = Join-Path $WorkDir "server.log"
$startedAt = Get-Date

Write-Host "tML 日志: $tmlLog"
Write-Host "启动服务器..."
$proc = Start-Process -FilePath $dotnet -WorkingDirectory $tml -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $stdout -RedirectStandardError "$stdout.err" -ArgumentList @(
        "`"$dll`"", "-server", "-config", "`"$cfg`"",
        "-tmlsavedirectory", "`"$saveDir`"", "-modpath", "`"$modsDir`"", "-nosteam", "-console")
Write-Host "PID = $($proc.Id)，最多等待 $TimeoutSeconds 秒"

function Read-TmlLog {
    # 旧日志会在启动时被移进 Old\；只认本次启动之后写入的文件
    if (-not (Test-Path $tmlLog)) { return '' }
    if ((Get-Item $tmlLog).LastWriteTime -lt $startedAt) { return '' }
    try {
        $fs = [IO.File]::Open($tmlLog, 'Open', 'Read', 'ReadWrite')
        try { return (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}

$deadline = $startedAt.AddSeconds($TimeoutSeconds)
$log = ''
$timedOut = $true
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $log = Read-TmlLog
    if ($proc.HasExited) { $timedOut = $false; break }
    if ($log -match '服务器已启动|Server started|Disabling Mod|/FATAL\]') { $timedOut = $false; break }
}
Start-Sleep -Seconds 1
$log = Read-TmlLog

if (-not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    $proc.WaitForExit(10000) | Out-Null
}

# --- 把日志切成条目（一条 = 带时间戳的首行 + 后续堆栈行）---
$entries = New-Object System.Collections.Generic.List[string]
$cur = $null
foreach ($line in ($log -split "`r?`n")) {
    if ($line -match '^\[\d\d:\d\d:\d\d\.\d+\]') {
        if ($null -ne $cur) { $entries.Add($cur) }
        $cur = $line
    } elseif ($null -ne $cur) { $cur += "`n" + $line }
}
if ($null -ne $cur) { $entries.Add($cur) }

$benign = @(
    @{ Why = 'stdout 重定向后 Console.Clear 失败（tML 自己捕获）'; Re = 'IOException[\s\S]*System\.ConsolePal\.' }
)
function Test-Benign([string]$e) { foreach ($b in $benign) { if ($e -match $b.Re) { return $b.Why } }; return $null }

$problems = New-Object System.Collections.Generic.List[string]
$ignored  = @{}
foreach ($e in $entries) {
    $bad = ($e -match '/(ERROR|FATAL)\]') -or ($e -match 'Disabling Mod|MissingResource') -or
           ($e -match 'Exception' -and $e -match $modName) -or
           ($e -match "\[$modName\]" -and $e -match '/WARN\]') -or
           ($e -match '/WARN\]' -and $e -match 'Exception|异常')
    if (-not $bad) { continue }
    $why = Test-Benign $e
    if ($why) { $ignored[$why] = 1 + [int]$ignored[$why]; continue }
    $problems.Add($e)
}

$checks = [ordered]@{
    '模组加载完成（Mod Load Completed）' = ($log -match 'Mod Load Completed')
    "本模组被自动加载（$modName）"       = ($log -match "自动加载中：$modName|Autoloading: $modName")
    '图案数据自检失败 0 条'              = ($log -match '图案数据自检：.*失败 0 条')
    '服务器进入世界（服务器已启动）'     = ($log -match '服务器已启动|Server started')
    '没有未登记的错误/异常'              = ($problems.Count -eq 0)
    '未超时'                             = (-not $timedOut)
}

Write-Host ''
Write-Host '================ 服务器加载验证 ================'
$fail = 0
foreach ($k in $checks.Keys) {
    if ($checks[$k]) { Write-Host "  PASS  $k" -ForegroundColor Green }
    else { Write-Host "  FAIL  $k" -ForegroundColor Red; $fail++ }
}
foreach ($k in $ignored.Keys) { Write-Host "  (忽略 $($ignored[$k]) 条已知噪音：$k)" -ForegroundColor DarkGray }
foreach ($m in ($log -split "`r?`n" | Where-Object { $_ -match "\[$modName\]" })) {
    Write-Host "  $($m -replace '^\[[^\]]*\] \[[^\]]*\] ', '')"
}
if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host '---- 问题条目 ----' -ForegroundColor Red
    $problems | Select-Object -First 10 | ForEach-Object { Write-Host $_ }
}
Write-Host ''
if ($fail -eq 0) { Write-Host '服务器验证：通过' -ForegroundColor Green; exit 0 }
Write-Host "服务器验证：失败（$fail 项）" -ForegroundColor Red
exit 1
