# 补齐 Localization/en-US.hjson 里**缺失**的物品与方块词条。
#
# 为什么需要它：tModLoader 找不到词条时会**静默回退到类名**，所以英文环境里
# 玩家看到的是 `AmethystDustBlockItem` 而不是 "Amethyst Dust Block"。
# 中文那边早先就是这个症状（已修），英文这边一直漏着 —— 缺口是**单向**的：
# 中文有、英文没有 99 条，英文有而中文没有 0 条。
#
# 中文的来源是官方 zh_cn 展平表（_tools/hexlang_zh.json），但**英文没有对应来源**：
# hexsrc 里没有 assets/hexcasting/lang/ 目录，全盘也没有 hexcasting 的 jar。
# 只有 patchouli_books/thehexbook/en_us/ 的英文手册正文 —— 用词风格照它核对，
# 名字本体在下面的表里人工给出（Hex Casting 的官方叫法）。
#
# 与 gen_localization.ps1 同构：**只补缺失键，不动已有条目**。
#
# 用法：pwsh -File _tools\gen_localization_en.ps1
param(
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

$L         = 'D:\DeepSeekHarness\tmod\HexCastingTerraria\Localization\'
$target    = $L + 'en-US.hjson'
$zhPath    = $L + 'zh-Hans.hjson'
$enc       = New-Object System.Text.UTF8Encoding($true)   # 这两个文件带 BOM

# ── 英文名表（99 条缺失键）────────────────────────────────────────────
# 规则：优先用源项目官方叫法；`*Item` 是物品形态、去掉 `Item` 是方块本体（名字相同）；
# 自创物品（引导书 / 壁挂卷轴框 / 开发者套件）自己起名。
$names = [ordered]@{
    # 工具与存储
    'Abacus'                        = 'Abacus'
    'JewelerHammer'                 = "Jeweler's Hammer"
    'ScryingLens'                   = 'Scrying Lens'
    'Spellbook'                     = 'Spellbook'
    'HexBookItem'                   = 'The Hex Book'
    'AkashicRecordItem'             = 'Akashic Record'
    'AkashicRecord'                 = 'Akashic Record'
    'Cypher'                        = 'Cypher'
    'Trinket'                       = 'Trinket'
    'Artifact'                      = 'Artifact'
    # 法杖
    'CherryStaff'                   = 'Cherry Staff'
    # 壁挂卷轴
    'WallScrollFrameSmall'          = 'Small Scroll Frame'
    'WallScrollFrameMedium'         = 'Medium Scroll Frame'
    'WallScrollFrameLarge'          = 'Large Scroll Frame'
    'WallScrollSmall'               = 'Small Wall Scroll'
    'WallScrollMedium'              = 'Medium Wall Scroll'
    'WallScrollLarge'               = 'Large Wall Scroll'
    # 卷轴纸与装饰
    'ScrollPaperItem'               = 'Scroll Paper'
    'ScrollPaper'                   = 'Scroll Paper'
    'ScrollPaperLanternItem'        = 'Scroll Paper Lantern'
    'ScrollPaperLantern'            = 'Scroll Paper Lantern'
    'AncientScrollPaperItem'        = 'Ancient Scroll Paper'
    'AncientScrollPaper'            = 'Ancient Scroll Paper'
    'AncientScrollPaperLanternItem' = 'Ancient Scroll Paper Lantern'
    'AncientScrollPaperLantern'     = 'Ancient Scroll Paper Lantern'
    'AmethystSconceItem'            = 'Amethyst Sconce'
    'AmethystSconce'                = 'Amethyst Sconce'
    'AkashicBookshelfItem'          = 'Akashic Bookshelf'
    'AkashicBookshelf'              = 'Akashic Bookshelf'
    'AkashicLigatureItem'           = 'Akashic Ligature'
    'AkashicLigature'               = 'Akashic Ligature'
    # 板岩系
    'SlateBlockItem'                = 'Slate Block'
    'SlateBlock'                    = 'Slate Block'
    'SlateTilesItem'                = 'Slate Tiles'
    'SlateTiles'                    = 'Slate Tiles'
    'SlateBricksItem'               = 'Slate Bricks'
    'SlateBricks'                   = 'Slate Bricks'
    'SlateBricksSmallItem'          = 'Slate Bricks (Small)'
    'SlateBricksSmall'              = 'Slate Bricks (Small)'
    'SlatePillarItem'               = 'Slate Pillar'
    'SlatePillar'                   = 'Slate Pillar'
    'SlateAmethystTilesItem'        = 'Slate Amethyst Tiles'
    'SlateAmethystTiles'            = 'Slate Amethyst Tiles'
    'SlateAmethystBricksItem'       = 'Slate Amethyst Bricks'
    'SlateAmethystBricks'           = 'Slate Amethyst Bricks'
    'SlateAmethystBricksSmallItem'  = 'Slate Amethyst Bricks (Small)'
    'SlateAmethystBricksSmall'      = 'Slate Amethyst Bricks (Small)'
    'SlateAmethystPillarItem'       = 'Slate Amethyst Pillar'
    'SlateAmethystPillar'           = 'Slate Amethyst Pillar'
    # 紫晶系
    'AmethystDustBlock'             = 'Amethyst Dust Block'
    'AmethystTilesItem'             = 'Amethyst Tiles'
    'AmethystTiles'                 = 'Amethyst Tiles'
    'AmethystBricksItem'            = 'Amethyst Bricks'
    'AmethystBricks'                = 'Amethyst Bricks'
    'AmethystBricksSmallItem'       = 'Amethyst Bricks (Small)'
    'AmethystBricksSmall'           = 'Amethyst Bricks (Small)'
    'AmethystPillarItem'            = 'Amethyst Pillar'
    'AmethystPillar'                = 'Amethyst Pillar'
    # 淬灵系
    'QuenchedAllayItem'             = 'Quenched Allay'
    'QuenchedAllay'                 = 'Quenched Allay'
    'QuenchedAllayTilesItem'        = 'Quenched Allay Tiles'
    'QuenchedAllayTiles'            = 'Quenched Allay Tiles'
    'QuenchedAllayBricksItem'       = 'Quenched Allay Bricks'
    'QuenchedAllayBricks'           = 'Quenched Allay Bricks'
    'QuenchedAllayBricksSmallItem'  = 'Quenched Allay Bricks (Small)'
    'QuenchedAllayBricksSmall'      = 'Quenched Allay Bricks (Small)'
    # 启迪木系
    'EdifiedLogItem'                = 'Edified Log'
    'EdifiedLog'                    = 'Edified Log'
    'EdifiedLogAmethystItem'        = 'Amethyst Edified Log'
    'EdifiedLogAmethyst'            = 'Amethyst Edified Log'
    'EdifiedLogAventurineItem'      = 'Aventurine Edified Log'
    'EdifiedLogAventurine'          = 'Aventurine Edified Log'
    'EdifiedLogCitrineItem'         = 'Citrine Edified Log'
    'EdifiedLogCitrine'             = 'Citrine Edified Log'
    'EdifiedLogPurpleItem'          = 'Purple Edified Log'
    'EdifiedLogPurple'              = 'Purple Edified Log'
    'StrippedEdifiedLogItem'        = 'Stripped Edified Log'
    'StrippedEdifiedLog'            = 'Stripped Edified Log'
    'EdifiedWoodItem'               = 'Edified Wood'
    'EdifiedWood'                   = 'Edified Wood'
    'StrippedEdifiedWoodItem'       = 'Stripped Edified Wood'
    'StrippedEdifiedWood'           = 'Stripped Edified Wood'
    'EdifiedPlanksItem'             = 'Edified Planks'
    'EdifiedPlanks'                 = 'Edified Planks'
    'EdifiedPanelItem'              = 'Edified Panel'
    'EdifiedPanel'                  = 'Edified Panel'
    'EdifiedTileItem'               = 'Edified Tile'
    'EdifiedTile'                   = 'Edified Tile'
    'AmethystEdifiedLeavesItem'     = 'Amethyst Edified Leaves'
    'AmethystEdifiedLeaves'         = 'Amethyst Edified Leaves'
    'AventurineEdifiedLeavesItem'   = 'Aventurine Edified Leaves'
    'AventurineEdifiedLeaves'       = 'Aventurine Edified Leaves'
    'CitrineEdifiedLeavesItem'      = 'Citrine Edified Leaves'
    'CitrineEdifiedLeaves'          = 'Citrine Edified Leaves'
    # 法术环组件
    'HexDirectrixEmpty'             = 'Empty Directrix'
    'HexImpetus'                    = 'Impetus'
    'HexSlate'                      = 'Slate'
    # 世界生成与开发者工具
    'GeodeCore'                     = 'Geode Core'
    'GiveDevKit'                    = 'Developer Kit'
}

# ── 从中文文件读出每个键属于 Items 还是 Tiles ─────────────────────────
$zhText = [System.IO.File]::ReadAllText($zhPath, [System.Text.Encoding]::UTF8)
$secOf  = @{}
$section = ''
foreach ($line in ($zhText -split "`r?`n")) {
    if ($line -match '^\s*(Items|Tiles)\s*:\s*\{') { $section = $Matches[1]; continue }
    $m = [regex]::Match($line, '^\s*([A-Za-z0-9_]+)\s*:\s*\{\s*$')
    if ($m.Success -and $section) { $secOf[$m.Groups[1].Value] = $section }
}

$text = [System.IO.File]::ReadAllText($target, [System.Text.Encoding]::UTF8)

$itemLines = New-Object System.Collections.ArrayList
$tileLines = New-Object System.Collections.ArrayList
$skipped   = New-Object System.Collections.ArrayList

foreach ($k in $names.Keys) {
    # 已有条目一律不动（这个脚本只补缺口，不做覆盖）
    if ($text -match "(?m)^\s*$([regex]::Escape($k))\s*:") { $skipped.Add($k); continue }

    $sec = if ($secOf.ContainsKey($k)) { $secOf[$k] } else { 'Items' }
    $field = if ($sec -eq 'Tiles') { 'MapEntry' } else { 'DisplayName' }
    $entry = "`t`t`t$k`: {`r`n`t`t`t`t${field}: `"$($names[$k])`"`r`n`t`t`t}"

    if ($sec -eq 'Tiles') { [void]$tileLines.Add($entry) } else { [void]$itemLines.Add($entry) }
}

Write-Host "待补：物品 $($itemLines.Count) 条，方块 $($tileLines.Count) 条（已存在跳过 $($skipped.Count) 条）"

function Insert-After([string]$text, [string]$anchor, [string]$block) {
    # ⚠️ 行尾锚点必须把 `\r` 也算进去：文件是 CRLF，而 .NET 在多行模式下
    # `$` 匹配的是 `\n` **之前**的位置 —— 于是 `[ \t]*$` 会因为残留的 `\r` 而匹配失败。
    $m = [regex]::Match($text, "(?m)^([ \t]*)$([regex]::Escape($anchor))[ \t]*\{[ \t\r]*$")
    if (-not $m.Success) { throw "找不到锚点：$anchor" }
    $at = $m.Index + $m.Length
    return $text.Substring(0, $at) + "`r`n" + $block + $text.Substring($at)
}

if ($itemLines.Count -gt 0) { $text = Insert-After $text 'Items:' ($itemLines -join "`r`n") }
if ($tileLines.Count -gt 0) { $text = Insert-After $text 'Tiles:' ($tileLines -join "`r`n") }

if ($DryRun) {
    Write-Host '（DryRun，不写文件）'
    exit 0
}

[System.IO.File]::WriteAllText($target, $text, $enc)
Write-Host "已写回 $target"
