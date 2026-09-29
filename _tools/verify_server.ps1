<#
.SYNOPSIS
    无人值守验证：用独立存档目录 + 自动生成小世界启动 tModLoader 服务器，
    检查模组能否正常加载，然后关闭并清理。

.DESCRIPTION
    为什么不直接用客户端：客户端进世界需要人工点击（选角色、选世界），
    而服务器可以 -autocreate 自动建世界，全程无需交互。
    用 -tmlsavedirectory 指到临时目录，绝不触碰玩家真实存档。
#>

[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 300,
    [string]$WorkDir = "D:\DeepSeekHarness\tmod\_verify"
)

$ErrorActionPreference = 'Stop'

$tml = "D:\steam\steamapps\common\tModLoader"
$dll = Join-Path $tml "tModLoader.dll"
$dotnet = Join-Path $tml "dotnet\dotnet.exe"

# --- 准备隔离的存档目录 ---
$saveDir = Join-Path $WorkDir "save"
$modsDir = Join-Path $saveDir "Mods"
$worldsDir = Join-Path $saveDir "Worlds"
New-Item -ItemType Directory -Force -Path $modsDir, $worldsDir | Out-Null

# 把要验证的 mod 复制进去
$modSrc = Join-Path $env:USERPROFILE "Documents\My Games\Terraria\tModLoader-dev\Mods\HexCastingTerraria.tmod"
Copy-Item $modSrc $modsDir -Force
Set-Content (Join-Path $modsDir "enabled.json") '["HexCastingTerraria"]' -Encoding UTF8

# --- 写服务器配置 ---
$cfg = Join-Path $WorkDir "serverconfig.txt"
@(
    "worldpath=$worldsDir"
    "worldname=_verify_world"
    "autocreate=1"
    "world=$worldsDir\_verify_world.wld"
    "maxplayers=1"
    "port=7799"
    "nosteam=1"
    "noupnp=1"
    "language=zh-Hans"
) | Set-Content $cfg -Encoding UTF8

$logFile = Join-Path $WorkDir "server.log"
if (Test-Path $logFile) { Remove-Item $logFile -Force }

Write-Host "存档目录: $saveDir"
Write-Host "配置    : $cfg"
Write-Host "日志    : $logFile"
Write-Host "启动服务器..."

$args = @(
    $dll
    "-server"
    "-config", $cfg
    "-tmlsavedirectory", $saveDir
    "-modpath", $modsDir
    "-nosteam"
    "-console"
)

$proc = Start-Process -FilePath $dotnet -ArgumentList $args -WorkingDirectory $tml `
    -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -WindowStyle Hidden

Write-Host "PID = $($proc.Id)，最多等待 $TimeoutSeconds 秒"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$loaded = $false
$failed = $false

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if ($proc.HasExited) { break }

    $content = if (Test-Path $logFile) { Get-Content $logFile -Raw -ErrorAction SilentlyContinue } else { "" }
    # 注意：日志里用的是本地化显示名（咒法学），不是内部名 HexCastingTerraria，
    # 两者都要匹配，否则会误判为「未加载」。
    if ($content -match 'HexCastingTerraria|咒法学') { $loaded = $true }
    if ($content -match 'Disabling Mod:.*(HexCastingTerraria|咒法学)' -or $content -match 'MissingResourceException') { $failed = $true; break }
    if ($content -match 'Listening on port|正在侦听端口') { break }
}

Start-Sleep -Seconds 2
$content = if (Test-Path $logFile) { Get-Content $logFile -Raw -ErrorAction SilentlyContinue } else { "" }

# --- 关闭服务器 ---
if (-not $proc.HasExited) {
    Write-Host "关闭服务器 PID $($proc.Id) ..."
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

# --- 结论 ---
Write-Host ""
Write-Host "================ 验证结果 ================"
if ($failed) {
    Write-Host "结果: 失败 —— 模组被禁用或有缺失资源" -ForegroundColor Red
}
elseif ($loaded) {
    Write-Host "结果: 成功 —— 模组已在服务器端加载" -ForegroundColor Green
}
else {
    Write-Host "结果: 未确认 —— 日志中未出现模组名" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "---- 模组相关日志 ----"
($content -split "`n") | Where-Object { $_ -match 'HexCasting|图案|Disabling|MissingResource|Exception|error' } |
    Select-Object -First 30 | ForEach-Object { $_.Trim() }

Write-Host ""
Write-Host "---- 日志尾部 ----"
($content -split "`n") | Select-Object -Last 12 | ForEach-Object { $_.Trim() }
