# 查询 _tools\vanilla_ids.json 里的原版 ID。用法：
#   .\_tools\qid.ps1 ItemID 'Amethyst|Pixie'
#   .\_tools\qid.ps1 TileID 'WorkBench|Sawmill'
param(
    [string]$Table = 'ItemID',
    [string]$Pattern = '.',
    [switch]$Names
)
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'vanilla_ids.json'
$j = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $j.PSObject.Properties.Name -contains $Table) {
    throw "没有表 $Table；可选：$(($j.PSObject.Properties.Name) -join ', ')"
}
$rows = $j.$Table.PSObject.Properties | Where-Object { $_.Name -match $Pattern }
if ($Names) { $rows | ForEach-Object { $_.Name } | Sort-Object }
else { $rows | ForEach-Object { '{0,-42} {1}' -f $_.Name, $_.Value } | Sort-Object }
"--- $($rows.Count) 条 ---"
