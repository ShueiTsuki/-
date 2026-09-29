# 从 tModLoader.dll 里**只读元数据**导出原版 ID 表（ItemID / TileID / NPCID / BuffID / DustID ...）。
#
# 为什么需要这个：配方对齐时最怕「凭记忆写一个 ItemID」，写出来的常量不存在就是编译错误，
# 或者存在但语义不对（比如把「紫晶石块」记成 AmethystStoneBlock）。
# 用 Mono.Cecil 读元数据不需要加载 Terraria 本体、不需要解决 FNA 依赖，离线可用。
#
# 用法：pwsh -File _tools\dump_vanilla_ids.ps1
# 产出：_tools\vanilla_ids.json  { "ItemID": {"Wood": 9, ...}, "TileID": {...}, ... }
param(
    [string]$TmlDir = 'D:\steam\steamapps\common\tModLoader',
    [string]$Out    = ''
)

$ErrorActionPreference = 'Stop'

if (-not $Out) {
    $Out = Join-Path $PSScriptRoot 'vanilla_ids.json'
}

$cecil = Join-Path $TmlDir 'Libraries\mono.cecil\0.11.6\lib\netstandard2.0\Mono.Cecil.dll'
$asmPath = Join-Path $TmlDir 'tModLoader.dll'
foreach ($p in @($cecil, $asmPath)) {
    if (-not (Test-Path $p)) { throw "找不到：$p" }
}

Add-Type -Path $cecil

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath)
$wanted = @(
    'Terraria.ID.ItemID',
    'Terraria.ID.TileID',
    'Terraria.ID.NPCID',
    'Terraria.ID.BuffID',
    'Terraria.ID.DustID',
    'Terraria.ID.SoundID',
    'Terraria.ID.ProjectileID',
    'Terraria.ID.WallID',
    'Terraria.ID.RecipeGroupID'
)

$result = [ordered]@{}
foreach ($full in $wanted) {
    $t = $asm.MainModule.GetType($full)
    if ($null -eq $t) { Write-Warning "类型不存在：$full"; continue }

    $map = [ordered]@{}
    foreach ($f in $t.Fields) {
        if (-not $f.HasConstant) { continue }
        # 只收数值型常量（ID 都是 short/int）
        try { $v = [int]$f.Constant } catch { continue }
        $map[$f.Name] = $v
    }
    $result[$full.Split('.')[-1]] = $map
    Write-Host ("{0,-14} {1,6} 个常量" -f $full.Split('.')[-1], $map.Count)
}

$json = $result | ConvertTo-Json -Depth 4 -Compress
[System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "-> $Out  ($([math]::Round((Get-Item $Out).Length / 1KB, 1)) KB)"
