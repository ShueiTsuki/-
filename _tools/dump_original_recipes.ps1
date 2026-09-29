# 从原版 HexplatRecipes.java 抽出配方表（结果 ← 材料），压成紧凑文本。
#
# 为什么需要：原版配方是 3x3 shaped/shapeless 的 Java builder 链，600+ 行，
# 逐行读进上下文既慢又容易被形状细节淹没。而"每个合成表是否符合原版"只需比对
# **材料与数量**；形状在泰拉侧本来就不可能一一对应（泰拉没有 3x3 摆放）。
#
# 输出：每条一行 —— `结果 <- 材料1, 材料2, ...`，并标注形状/是否需要原版工作台。
$ErrorActionPreference = 'Stop'
$f = 'D:\DeepSeekHarness\hexsrc\Common\src\main\java\at\petrak\hexcasting\datagen\recipe\HexplatRecipes.java'
$lines = Get-Content -LiteralPath $f

$results = New-Object System.Collections.Generic.List[string]
$cur = $null
$buf = New-Object System.Collections.Generic.List[string]

function Flush {
    if (-not $script:cur) { return }
    $head = $script:cur
    # 结果名
    $res = ''
    $m = [regex]::Match($head, '(?:shaped|shapeless)\([^,]+,\s*([^,)]+)')
    if ($m.Success) { $res = $m.Groups[1].Value.Trim() }
    elseif ($head -match 'ringCornerless\([^,]+,\s*([^,]+)') { $res = $Matches[1].Trim() }
    elseif ($head -match 'staffRecipe\(recipes,\s*([^,]+),') { $res = $Matches[1].Trim() }
    elseif ($head -match 'stoneSet\(recipes,\s*([^,]+),') { $res = $Matches[1].Trim() + ' (整族)' }
    elseif ($head -match 'BrainsweepRecipeBuilder') { $res = '(脑叶切除)' }
    else { $res = $head.Substring(0, [Math]::Min(60, $head.Length)) }

    $ings = @()
    foreach ($b in $script:buf) {
        if ($b -match "\.define\('(\w)',\s*([^)]+)\)") { $ings += "$($Matches[1])=$($Matches[2].Trim())" }
        elseif ($b -match '\.requires\(([^)]+)\)') { $ings += $Matches[1].Trim() }
        elseif ($b -match '\.pattern\("([^"]+)"\)') { $ings += "[$($Matches[1])]" }
    }
    $script:results.Add(("{0,-46} <- {1}" -f $res, ($ings -join ', ')))
    $script:cur = $null
    $script:buf.Clear()
}

foreach ($l in $lines) {
    $t = $l.Trim()
    if ($t -match '^(ShapedRecipeBuilder\.(shaped|shapeless)|ringCornerless|staffRecipe|new BrainsweepRecipeBuilder|stoneSet|stoneCutterFromTag)') {
        Flush
        $cur = $t
        continue
    }
    if ($cur) {
        if ($t -match '^\.(define|requires|pattern|save|input|output|withMedia)') { $buf.Add($t) }
        if ($t -match '^\.save\(' -or $t -match '\.save\(recipes') { $buf.Add($t); Flush }
    }
}
Flush
$results | ForEach-Object { $_ }
