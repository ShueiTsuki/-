# 生成 PROGRESSION.generated.md（阶段表），并把「阶段表 ↔ 代码里的配方」对拍。
#
# 为什么要有这个：
#   之前配方是「一个一个单独看」的，于是同一个族里各写各的比例（瓦从基底块 1:1、
#   砖漏了回炉配方），而且**没有任何东西记录这些物品属于哪个阶段** ——
#   肉前的东西和月后的东西混在一起，做进度流程时无从下手。
#
#   现在分两层：
#     1. _tools/progression_stages.json —— **人工**定的阶段（唯一真源，改这里）
#     2. 本脚本 —— 从代码里**机器**读出每个类的合成站与材料，和上面那份对拍
#
# 对拍会挡住两类回归：
#   - 新增了一个有配方的类，却没给它定档  → 生成失败
#   - 阶段表里写了一个不存在的类          → 生成失败
#
# 用法：pwsh -File _tools\gen_progression.ps1
# 产出：PROGRESSION.generated.md
param(
    [string]$ModDir = ''
)
$ErrorActionPreference = 'Stop'

$tools = $PSScriptRoot
$root  = Split-Path -Parent $tools
if (-not $ModDir) { $ModDir = Join-Path $root 'HexCastingTerraria' }
$stagesPath = Join-Path $tools 'progression_stages.json'
$outPath    = Join-Path $root 'PROGRESSION.generated.md'

$cfg = Get-Content $stagesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stageOrder = @($cfg.stages)

# ── 从代码里读出每个「有配方的类」的合成站与材料 ──────────────────────
$inventory = [ordered]@{}
$baseOf    = @{}          # 类名 -> 基类名（用来识别「配方写在基类里」的子类）

function Get-StationText([string]$body) {
    $tiles = [regex]::Matches($body, '\.AddTile\(\s*TileID\.(\w+)\s*\)') | ForEach-Object { $_.Groups[1].Value }
    $text = if ($tiles.Count -eq 0) { '（无：徒手）' } else { ($tiles | Select-Object -Unique) -join ' / ' }
    if ($body -match '\.AddCondition\(\s*HexConditions\.Enlightened\s*\)') { $text += ' + 已启蒙' }
    return $text
}

function Get-IngredientText([string]$body) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($body, '\.AddIngredient<(\w+)>\((\d+)\)')) {
        $list.Add("$($m.Groups[1].Value)×$($m.Groups[2].Value)")
    }
    foreach ($m in [regex]::Matches($body, '\.AddIngredient\(\s*(?:ItemID|TileID)\.(\w+)\s*,\s*(\d+)\s*\)')) {
        $list.Add("$($m.Groups[1].Value)×$($m.Groups[2].Value)")
    }
    # 材料写成抽象属性时（如 WoodStaff 的 `.AddIngredient(WoodType, 1)`）也要收进来 ——
    # 否则文档会漏报一整格材料，看起来像配方少了个东西。子类决定的内容标出来。
    foreach ($m in [regex]::Matches($body, '\.AddIngredient\(\s*([A-Z]\w*)\s*,\s*(\d+)\s*\)')) {
        if ($m.Groups[1].Value -in @('ItemID', 'TileID')) { continue }
        $list.Add("{$($m.Groups[1].Value)}×$($m.Groups[2].Value)")
    }
    foreach ($m in [regex]::Matches($body, '\.AddRecipeGroup\(\s*([\w\.]+)\s*,\s*(\d+)\s*\)')) {
        $list.Add("〔$($m.Groups[1].Value)〕×$($m.Groups[2].Value)")
    }
    if ($list.Count -eq 0) { return '—' }
    return ($list -join ' + ')
}

$files = Get-ChildItem $ModDir -Recurse -File -Filter *.cs |
         Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

foreach ($f in $files) {
    $txt = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($txt, '(?m)^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?::\s*([\w\.]+))?')) {
        if ($m.Groups[2].Success) { $baseOf[$m.Groups[1].Value] = $m.Groups[2].Value }
        else { $baseOf[$m.Groups[1].Value] = '' }
    }
    $cls = [regex]::Matches($txt, '(?m)^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)')
    for ($i = 0; $i -lt $cls.Count; $i++) {
        $name = $cls[$i].Groups[1].Value
        $s = $cls[$i].Index
        $e = if ($i + 1 -lt $cls.Count) { $cls[$i + 1].Index } else { $txt.Length }
        $body = $txt.Substring($s, $e - $s)

        $recipes = [regex]::Matches($body, 'CreateRecipe\(([^)]*)\)(?<c>.*?)\.Register\(\)',
                                    [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($recipes.Count -eq 0) { continue }

        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($r in $recipes) {
            $out = $r.Groups[1].Value.Trim()
            if (-not $out) { $out = '1' }
            $rows.Add([pscustomobject]@{
                Out = $out
                Ing = Get-IngredientText $r.Groups['c'].Value
                Tile = Get-StationText $r.Groups['c'].Value
            })
        }
        $inventory[$name] = $rows
    }
}

# 把「配方写在基类里」的子类也算进清单 —— 否则布尔/红石导线会被判成「定了档但没有配方」
foreach ($name in @($baseOf.Keys)) {
    if ($inventory.Contains($name)) { continue }
    $b = $baseOf[$name]
    if ($b -and $inventory.Contains($b)) { $inventory[$name] = $inventory[$b] }
}

# ── 对拍 ──────────────────────────────────────────────────────────────
$declared = @{}
foreach ($st in $stageOrder) {
    foreach ($p in $cfg.entries.$st.PSObject.Properties) { $declared[$p.Name] = $st }
}

$errors = New-Object System.Collections.Generic.List[string]
# OrderedDictionary 只有 Contains()，没有 ContainsKey() —— 用 .Contains 才不炸
foreach ($k in $inventory.Keys) {
    if (-not $declared.ContainsKey($k)) { $errors.Add("有配方但没定档：$k") }
}
foreach ($k in $declared.Keys) {
    if (-not $inventory.Contains($k)) { $errors.Add("定了档但没有配方：$k") }
}

# 阶段与合成站的合理性：写在「肉后 · 启蒙」的东西必须**真的有这两道门槛** ——
# 配方条件是「已启蒙」且合成站是秘银砧（肉后），或者材料里有同阶段的物品（如剖念法杖用红石导线）。
# 只在表里写阶段、配方却是「木材×3 @ 工作台」的话，这张表就是自欺欺人。
$gateStage = '肉后 · 启蒙'
$gated = @{}
foreach ($p in $cfg.entries.$gateStage.PSObject.Properties) { $gated[$p.Name] = $true }
foreach ($cls in @($gated.Keys)) {
    if (-not $inventory.Contains($cls)) { continue }
    $tiles = ($inventory[$cls] | ForEach-Object { $_.Tile }) -join ' '
    $ing   = ($inventory[$cls] | ForEach-Object { $_.Ing }) -join ' '
    if ($tiles -match 'MythrilAnvil' -and $tiles -match '已启蒙') { continue }
    $byMaterial = $false
    foreach ($k in $gated.Keys) {
        if ($k -ne $cls -and $ing -match [regex]::Escape($k)) { $byMaterial = $true; break }
    }
    if (-not $byMaterial) {
        $errors.Add("「$gateStage」条目 $cls 既不是「秘银砧 + 已启蒙」，材料里也没有同阶段物品（站：$tiles）")
    }
}

if ($errors.Count -gt 0) {
    Write-Host '阶段表和代码对不上：' -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── 生成文档 ──────────────────────────────────────────────────────────
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 内容阶段表（自动生成）')
[void]$sb.AppendLine()
[void]$sb.AppendLine('<!-- 本文件由 _tools/gen_progression.ps1 生成，不要手改。')
[void]$sb.AppendLine('     阶段写在 _tools/progression_stages.json；合成站与材料是从代码里读出来的。 -->')
[void]$sb.AppendLine()
[void]$sb.AppendLine('这张表的用途：**做进度流程**。它把「这个东西该在什么时候出现」和')
[void]$sb.AppendLine('「它实际上怎么做出来」放在同一行，两者对不上就会在生成期红掉。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('阶段的依据来自源项目的真实门槛，见 `_tools/progression_stages.json` 顶部的说明。')
[void]$sb.AppendLine()
[void]$sb.AppendLine("共 **$($inventory.Count)** 个可合成物品。")
[void]$sb.AppendLine()

foreach ($st in $stageOrder) {
    $items = @($cfg.entries.$st.PSObject.Properties)
    [void]$sb.AppendLine("## $st（$($items.Count) 项）")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($cfg.stageNotes.$st)
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| 物品类 | 产出 | 材料 | 合成站 | 依据 |')
    [void]$sb.AppendLine('|---|---|---|---|---|')
    foreach ($p in $items) {
        $cls = $p.Name
        if (-not $inventory.Contains($cls)) { continue }
        foreach ($r in $inventory[$cls]) {
            $note = $p.Value -replace '\|', '\|'
            [void]$sb.AppendLine("| ``$cls`` | $($r.Out) | $($r.Ing) | $($r.Tile) | $note |")
        }
    }
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine('## 与源项目的阶段差异（有意为之）')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 源项目阶段 | 泰拉阶段 | 为什么 |')
[void]$sb.AppendLine('|---|---|---|')
[void]$sb.AppendLine('| 序幕（无门槛） | 肉前 | 紫水晶在泰拉一开局就能挖到 |')
[void]$sb.AppendLine('| 启蒙（濒死过载施法） | 肉后 · 启蒙 | 启蒙按原版实现（Core/Media/Overcast.cs）：配方条件「已启蒙」，再叠加肉后的秘银砧。曾经定在月后，是因为当时启蒙拿不到 |')
[void]$sb.AppendLine('| 末地（合唱果 → 法术书） | 肉后 | 泰拉没有末地，取中间阶段；对应物用肉后的水晶碎块 |')
[void]$sb.AppendLine('| 脑叶切除（启蒙大战法术） | 肉后 | 泰拉侧的对应物是**神圣地妖精**，只在肉后出现 —— 门槛由材料自带 |')
[void]$sb.AppendLine('| 启迪树苗 → 阿卡夏树 | 肉前 | 源项目里 edify **不在**启蒙名单里，是普通法术；泰拉暂用保底配方 |')
[void]$sb.AppendLine()

[System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("阶段表：$($inventory.Count) 个物品（" + (($stageOrder | ForEach-Object { "$_ $(@($cfg.entries.$_.PSObject.Properties).Count)" }) -join ' / ') + '）')
Write-Host "-> $outPath"

# 注意：必须显式 exit 0：`.ps1` 用 `&` 调用时如果**没有** exit，`$LASTEXITCODE` 会
# **保留上一个命令的值**。调用方（check_arch / run_all）拿它判成败，
# 于是脚本明明成功了却因为前一条命令失败而报红 —— 一个只在特定调用顺序下出现的假红。
exit 0
