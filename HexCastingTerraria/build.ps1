<#
.SYNOPSIS
    编译并打包咒法学模组，并自动处理「游戏正在运行导致 .tmod 被锁」的情况。

.DESCRIPTION
    踩过的坑（1.4.5 实测）：
      1. tModLoader 命令行打包会写入 tModLoader-dev\Mods\*.tmod；
         如果游戏正在运行，该文件被锁，打包报
         "TML003: Please close tModLoader or disable the mod in-game to build mods directly."
      2. 游戏进程名不是 tModLoader，而是 dotnet.exe
         （命令行形如 .../tModLoader/dotnet/dotnet tModLoader.dll），
         所以 Get-Process -Name tModLoader 查不到，必须查命令行。
      3. 只做语法检查时用 -p:BuildMod=false，此时不打包、不碰 .tmod，游戏开着也能跑。

.EXAMPLE
    # 只查语法（游戏可以开着）
    powershell -File .\build.ps1 -CompileOnly

    # 完整打包（需要先关游戏）
    powershell -File .\build.ps1
#>

[CmdletBinding()]
param(
    # 只编译不打包。不触碰 .tmod，游戏运行中也能安全执行。
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'

# 确保 dotnet 在 PATH 中（PowerShell 函数必须先定义后使用）
function Ensure-DotnetPath {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    }
}

$source     = $PSScriptRoot
$modName    = Split-Path -Leaf $source
$modSources = Join-Path $env:USERPROFILE 'Documents\My Games\Terraria\tModLoader\ModSources'
$projectDir = Join-Path $modSources $modName
$csproj     = Join-Path $projectDir "$modName.csproj"
$tmodOut    = Join-Path $env:USERPROFILE "Documents\My Games\Terraria\tModLoader-dev\Mods\$modName.tmod"

Write-Host "源目录  : $source"
Write-Host "构建目录: $projectDir"
Write-Host ''

# --- 检测游戏是否在运行（按命令行匹配，因为进程名是 dotnet.exe） ---
function Get-GameProcesses {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'tModLoader\.dll' }
}

$gameProcs = Get-GameProcesses
if ($gameProcs) {
    $ids = ($gameProcs | ForEach-Object { $_.ProcessId }) -join ', '
    if ($CompileOnly) {
        Write-Host "检测到 tModLoader 正在运行（PID: $ids）—— 只编译模式，不打包，可以继续。" -ForegroundColor Yellow
    }
    else {
        Write-Host "检测到 tModLoader 正在运行（PID: $ids）。" -ForegroundColor Yellow
        Write-Host "打包会写入 $tmodOut，该文件被游戏锁定，必须先关闭游戏。" -ForegroundColor Yellow
        Write-Host ''
        $answer = Read-Host "是否现在结束游戏进程？(y/N)"
        if ($answer -eq 'y' -or $answer -eq 'Y') {
            foreach ($p in $gameProcs) {
                Write-Host "结束进程 PID $($p.ProcessId) ..."
                Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            }
            Start-Sleep -Seconds 3
        }
        else {
            throw "请先关闭 tModLoader 再打包；或改用 -CompileOnly 只做语法检查。"
        }
    }
}

# --- 同步源码到 ModSources ---
& robocopy $source $projectDir /MIR /XD bin obj .vs .git /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy 同步失败，退出码 $LASTEXITCODE"
}

# --- 编译 ---
Ensure-DotnetPath
$env:PATH = "C:\Program Files\dotnet;$env:PATH"

$buildArgs = @('build', $csproj, '-v:m')
if ($CompileOnly) {
    # BuildMod=false 时不打包；TargetFramework 已在 csproj 里显式声明，不会报 NETSDK1013
    $buildArgs += '-p:BuildMod=false'
}

& dotnet @buildArgs
$code = $LASTEXITCODE

if ($code -ne 0) {
    throw "编译失败，退出码 $code"
}

if (-not $CompileOnly) {
    if (Test-Path $tmodOut) {
        $item = Get-Item $tmodOut
        Write-Host ''
        Write-Host ("打包成功: {0}  ({1} 字节)" -f $item.Name, $item.Length) -ForegroundColor Green
        Write-Host "位置: $tmodOut"
    }
    else {
        Write-Warning "编译通过但未找到 $tmodOut"
    }
}
