# 生成/补齐 Localization/zh-Hans.hjson 里**缺失**的物品与方块词条。
#
# 为什么需要它：tModLoader 找不到词条时**直接显示英文类名** ——
# 玩家在物品栏里看到的是 `JewelerHammer`、`MediaFlask`、`AmethystTilesItem`。
# 这不是"没翻译"，是"没词条"，而且**不报错、不警告**，只能靠人对。
#
# 名字来源（按权威性）：
#   1. 原版官方 zh_cn 语言文件（`_tools/hexlang_zh.json`，由 extract_lang_flat.mjs 从
#      官方 zh_cn.flatten.json5 抽出）—— 与图案名用的是同一套来源；
#   2. 我们自己的建材生成器表（`gen_deco_blocks.ps1` 的 `$blocks`）—— 那是这批方块的
#      **唯一真源**，中文名本来就写在那里；
#   3. 少数泰拉侧特有的替代物（媒质瓶、法阵方块）用我们自己定的名字。
#
# 本脚本**只补缺失项**，已有词条一律不动 —— 免得把已经校对过的名字覆盖掉。
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$tools = $PSScriptRoot
$mod   = Join-Path (Split-Path -Parent $tools) 'HexCastingTerraria'
$hjson = Join-Path $mod 'Localization\zh-Hans.hjson'
$encBom = New-Object System.Text.UTF8Encoding($true)

$text = [System.IO.File]::ReadAllText($hjson, [System.Text.Encoding]::UTF8)

# ── 1. 建材方块的中文名（来自生成器表，唯一真源）────────────────────
$decoNames = [ordered]@{}
$gen = Get-Content -LiteralPath (Join-Path $tools 'gen_deco_blocks.ps1') -Raw -Encoding UTF8
foreach ($m in [regex]::Matches($gen, "@\('(\w+)',\s*'([^']+)'")) {
    $decoNames[$m.Groups[1].Value] = $m.Groups[2].Value
}
Write-Host "建材表：$($decoNames.Count) 个方块名"

# ── 2. 手工表（其余物品/方块）────────────────────────────────────────
# 键 = 我们类名 → 值 = 中文名。原版有对应物的用官方译名。
$manualTiles = [ordered]@{
    'AmethystDustBlock'          = '紫水晶粉块'
    # 下面这 4 个第一版漏了：只给它们的**物品形态**写了词条，方块本身的 MapEntry 忘了 ——
    # 表现就是"物品栏里是中文、地图上/放置后是英文类名"。是 check_arch 的 ⑦ 断言抓出来的。
    'AmethystSconce'             = '紫晶烛台'
    'AkashicRecord'              = '阿卡夏记录'
    'AkashicBookshelf'           = '阿卡夏书架'
    'AkashicLigature'            = '阿卡夏桥接块'
    'ScrollPaper'                = '卷轴纸'
    'ScrollPaperLantern'         = '卷轴纸灯笼'
    'AncientScrollPaper'         = '远古卷轴纸'
    'AncientScrollPaperLantern'  = '远古卷轴纸灯笼'
    'WallScrollSmall'            = '小型挂轴'
    'WallScrollMedium'           = '中型挂轴'
    'WallScrollLarge'            = '大型挂轴'
}
$manualItems = [ordered]@{
    # ── 工具（源项目有的用官方译名）──
    'Abacus'                 = '算盘'
    'JewelerHammer'          = '珠宝匠锤'
    'ScryingLens'            = '探知透镜'
    'Spellbook'              = '法术书'
    'HexBookItem'            = '咒法学之书'
    'AkashicRecordItem'      = '阿卡夏记录'
    'MediaFlask'             = '媒质瓶'          # 泰拉侧替代物（源项目是 battery）
    # ── 三件封装法术的物品：官方译名 ──
    'Cypher'                 = '杂件'
    'Trinket'                = '缀品'
    'Artifact'               = '造物'
    # ── 法杖 ──
    'CherryStaff'            = '樱花木法杖'
    # ── 挂轴框 ──
    'WallScrollFrameSmall'   = '小型挂轴框'
    'WallScrollFrameMedium'  = '中型挂轴框'
    'WallScrollFrameLarge'   = '大型挂轴框'
    # ── 纸与灯笼 ──
    'ScrollPaperItem'                 = '卷轴纸'
    'ScrollPaperLanternItem'          = '卷轴纸灯笼'
    'AncientScrollPaperItem'          = '远古卷轴纸'
    'AncientScrollPaperLanternItem'   = '远古卷轴纸灯笼'
    # ── 阿卡夏家具 ──
    'AkashicBookshelfItem'   = '阿卡夏书架'
    'AkashicLigatureItem'    = '阿卡夏桥接块'
    'AmethystSconceItem'     = '紫晶烛台'
}

# 建材方块：**方块本身**（MapEntry）与**物品形态**（DisplayName）都要。
# 第一版只加了物品形态，结果 31 个方块的 MapEntry 全漏 —— 地图上会显示英文类名。
foreach ($k in $decoNames.Keys) {
    if (-not $manualTiles.Contains($k)) { $manualTiles[$k] = $decoNames[$k] }
    if (-not $manualItems.Contains($k + 'Item')) {
        $manualItems[$k + 'Item'] = $decoNames[$k]
    }
}

# ── 3. 已有哪些词条 ─────────────────────────────────────────────────
$have = @{}
foreach ($m in [regex]::Matches($text, '(?m)^\t{3}(\w+):\s*\{')) { $have[$m.Groups[1].Value] = 1 }

function New-Entries($pairs, $field) {
    $sb = New-Object System.Text.StringBuilder
    $n = 0
    foreach ($k in $pairs.Keys) {
        if ($have.ContainsKey($k)) { continue }
        [void]$sb.AppendLine("`t`t`t$k`: {")
        [void]$sb.AppendLine("`t`t`t`t${field}: $($pairs[$k])")
        [void]$sb.AppendLine("`t`t`t}")
        $n++
    }
    return @{ Text = $sb.ToString(); Count = $n }
}

$tileNew = New-Entries $manualTiles 'MapEntry'
$itemNew = New-Entries $manualItems 'DisplayName'
Write-Host "待补：方块 $($tileNew.Count) 条，物品 $($itemNew.Count) 条"

if ($DryRun) {
    Write-Host '--- 方块 ---'; Write-Host $tileNew.Text
    Write-Host '--- 物品 ---'; Write-Host $itemNew.Text
    exit 0
}

# ── 4. 插入到对应分区开头 ───────────────────────────────────────────
function Insert-After($src, $anchor, $block) {
    $i = $src.IndexOf($anchor)
    if ($i -lt 0) { throw "找不到锚点：$anchor" }
    $nl = $src.IndexOf("`n", $i)
    if ($nl -lt 0) { throw "锚点后没有换行：$anchor" }
    return $src.Substring(0, $nl + 1) + $block + $src.Substring($nl + 1)
}

if ($tileNew.Count -gt 0) { $text = Insert-After $text 'Tiles: {' $tileNew.Text }
if ($itemNew.Count -gt 0) { $text = Insert-After $text 'Items: {' $itemNew.Text }

# ── 5. 补 Keybinds（GiveDevKit 漏了）────────────────────────────────
if ($text -notmatch '(?m)^\t{3}GiveDevKit:\s*\{') {
    $text = Insert-After $text 'Keybinds: {' "`t`t`tGiveDevKit: {`n`t`t`t`tDisplayName: 发放开发者套件（开发者模式）`n`t`t`t}`n"
    Write-Host '补上 Keybinds.GiveDevKit'
}

[System.IO.File]::WriteAllText($hjson, $text, $encBom)
Write-Host "已写回 $hjson"
