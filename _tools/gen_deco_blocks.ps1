# 生成「装饰方块」家族（P2-6）的**代码 + 贴图**。
#
# 为什么要生成而不是手写：这一批是纯机械重复（每个方块 = 一个 ModTile + 一个 ModItem + 配方），
# 手写 30 个类除了制造 30 处笔误机会没有任何价值。
# 表在下面，改一行就多一个方块 —— 也方便以后和源项目的清单逐条对齐。
#
# 注意：建材贴图**必须低饱和 / 接近灰阶**：泰拉的油漆是乘算染色，
# 彩色贴图染完会变成一坨脏色。这里所有色值都压了饱和度。
Add-Type -AssemblyName System.Drawing

$tileDir = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$codePath = Join-Path $tileDir "DecoBlocks.Generated.cs"

# ── 方块表 ────────────────────────────────────────────────────────────
# 字段：类名, 中文名, R, G, B, 纹样(plain/tiles/bricks/bricksSmall/pillar/bark/planks/leaves),
#       镐力, 尘, 稀有度档位
#
# 稀有度档位（不是 ItemRarityID 的数字，见下面 $rarityNames）：
#   0 = 白（肉前常见）  1 = 蓝（肉前稀有）  2 = 粉（肉后）  3 = 青（月后）
#
# 配方**不在这里**了 —— 见下面的「配方表」。
# 之前配方塞在行里（材料/数量/产出三列），结果长歪了：同族的「瓦」写成从基底块
# 1:1，而源项目是从**砖** 4:4；「砖」又漏了源项目「砖 ← 小砖」的回炉配方；
# 切石机那一整条路径干脆没有。原因不是笔误 —— 是**行内三列表达不了「角色」**：
# 同族的基底块/砖/小砖/瓦/柱之间有固定的比例与互相关系，得按角色推，不能各写各的。
$blocks = @(
    # ── 板岩系（源项目 9 种）──
    @('SlateBlock',              '板岩块',           70, 66, 84, 'plain',       0,  'Stone', 0),
    @('SlateTiles',              '板岩瓦',           74, 70, 90, 'tiles',       0,  'Stone', 0),
    @('SlateBricks',             '板岩砖',           78, 72, 94, 'bricks',      0,  'Stone', 0),
    @('SlateBricksSmall',        '板岩小型砖',         82, 76, 98, 'bricksSmall', 0,  'Stone', 0),
    @('SlatePillar',             '板岩柱',           76, 70, 92, 'pillar',      0,  'Stone', 0),
    @('SlateAmethystTiles',      '板岩紫晶瓦',       96, 80, 110, 'tiles',      0,  'Stone', 0),
    @('SlateAmethystBricks',     '板岩紫晶砖',       100, 84, 114, 'bricks',    0,  'Stone', 0),
    @('SlateAmethystBricksSmall','板岩紫晶小型砖',     104, 88, 118, 'bricksSmall', 0, 'Stone', 0),
    @('SlateAmethystPillar',     '板岩紫晶柱',       98, 82, 112, 'pillar',     0,  'Stone', 0),

    # ── 紫晶系（源项目 4 种；基底块是 Content/Tiles/AmethystDustBlock.cs 手写的）──
    @('AmethystTiles',           '紫水晶瓦',           138, 112, 176, 'tiles',     0,  'Stone', 1),
    @('AmethystBricks',          '紫水晶砖',           144, 118, 182, 'bricks',    0,  'Stone', 1),
    @('AmethystBricksSmall',     '紫水晶小型砖',         150, 124, 188, 'bricksSmall', 0, 'Stone', 1),
    @('AmethystPillar',          '紫水晶柱',           140, 114, 178, 'pillar',    0,  'Stone', 1),

    # ── 淬灵系（源项目 4 种）──
    @('QuenchedAllay',           '淬灵晶块',           104, 168, 178, 'plain',     1,  'Stone', 2),
    @('QuenchedAllayTiles',      '淬灵晶瓦',           108, 174, 184, 'tiles',     1,  'Stone', 2),
    @('QuenchedAllayBricks',     '淬灵晶砖',           112, 180, 190, 'bricks',    1,  'Stone', 2),
    @('QuenchedAllayBricksSmall','淬灵晶小型砖',         116, 186, 196, 'bricksSmall', 1, 'Stone', 2),

    # ── 启迪木系（源项目的木材家族，14 种）──
    # 稀有度档 1（蓝）：源项目里它不是终局内容 —— 「启迪/edify」是**普通法术**
    # （HexActionTagProvider 的启蒙名单里没有它，只有雷击/飞行/脑叶切除那 15 个大战法术要启蒙），
    # 作用是**把树苗启迪成启迪树苗**，种下去长成阿卡夏树 → 产出启迪原木与彩色启迪树叶。
    # 也就是说它的真实门槛是「找到一棵树苗 + 会画那一个图案」，属于**肉前**。
    @('EdifiedLog',              '启迪原木',         96, 84, 66, 'bark',        0,  'WoodFurniture',  1),
    @('EdifiedLogAmethyst',      '晶紫启迪原木',     100, 84, 78, 'bark',        0,  'WoodFurniture',  1),
    @('EdifiedLogAventurine',    '砂蓝启迪原木',     94, 90, 68, 'bark',         0,  'WoodFurniture',  1),
    @('EdifiedLogCitrine',       '晶黄启迪原木',     104, 92, 62, 'bark',        0,  'WoodFurniture',  1),
    @('EdifiedLogPurple',        '紫色启迪原木',       92, 80, 74, 'bark',         0,  'WoodFurniture',  1),
    @('StrippedEdifiedLog',      '去皮启迪原木',     120, 108, 88, 'strippedBark', 0, 'WoodFurniture', 1),
    @('EdifiedWood',             '启迪木',           96, 84, 66, 'bark',         0,  'WoodFurniture',  1),
    @('StrippedEdifiedWood',     '去皮启迪木',       120, 108, 88, 'strippedBark', 0, 'WoodFurniture', 1),
    @('EdifiedPlanks',           '启迪木板',         132, 118, 92, 'planks',      0,  'WoodFurniture',  1),
    @('EdifiedPanel',            '启迪木块',       138, 124, 98, 'planks',      0,  'WoodFurniture',  1),
    @('EdifiedTile',             '启迪木方砖',       142, 128, 102, 'tiles',      0,  'WoodFurniture',  1),
    @('AmethystEdifiedLeaves',   '晶紫启迪树叶',     122, 100, 150, 'leaves',     0,  'Grass', 1),
    @('AventurineEdifiedLeaves', '砂蓝启迪树叶',     112, 122, 96, 'leaves',      0,  'Grass', 1),
    @('CitrineEdifiedLeaves',    '晶黄启迪树叶',     134, 120, 88, 'leaves',      0,  'Grass', 1)
)

# ══ 配方表 ═══════════════════════════════════════════════════════════
#
# 材料写法：
#   '@StoneBlock'     → 原版物品 ItemID.StoneBlock
#   '!AmethystDust'   → 手写的模组物品类（**不**补 Item 后缀）
#   'SlateBlock'      → 本脚本生成的方块类（自动补 Item 后缀）
#   '#SlateBlocks'    → 配方组（RecipeGroup，见 Content/Items/HexRecipeGroups.cs）
#
# 合成站一律写全 TileID 名；**不写 AddTile 就是徒手合成**，只能用在本该徒手的东西上。

$rarityNames = @('White', 'Blue', 'Pink', 'Cyan')

function New-Rec {
    param([int]$Out, $Ings, [string]$Tile, [string]$Comment = '')
    [pscustomobject]@{ Out = $Out; Ings = $Ings; Tile = $Tile; Comment = $Comment }
}

$recipes = [ordered]@{}
function Add-Rec([string]$cls, $rec) {
    if (-not $recipes.Contains($cls)) {
        $recipes[$cls] = New-Object System.Collections.ArrayList
    }
    [void]$recipes[$cls].Add($rec)
}

# ── 建材族：对齐源项目 HexplatRecipes.stoneSet / stoneCutterFromTag ──
#
# 源项目 605-643（工作台）：
#   bricks ×4 ← base   ×4     shaped 2×2
#   bricks ×1 ← small  ×1     shapeless 回炉
#   small  ×1 ← bricks ×1     shapeless 切细
#   tiles  ×4 ← bricks ×4     shaped 2×2 —— **是 bricks，不是 base**
#   pillar ×2 ← base   ×2     shaped 1×2
#
# 源项目 645-651（切石机）：从 **tag（任意同族方块）** 1:1 切成造型块，不损耗。
#   泰拉没有切石机 → 用**重型工作台**（TileID.HeavyWorkBench = 283），
#   输入用配方组（原版那条本来就是 tag），于是「任意同族方块 → 任意造型 1:1」也成立。
#
# 泰拉的配方**不分形状、不看摆放**，只数材料数量，所以上面每条只保留数量比。
$stoneFamilies = @(
    [pscustomobject]@{
        Cn = '板岩'; Base = 'SlateBlock'; Group = 'SlateBlocks'
        Bricks = 'SlateBricks'; Small = 'SlateBricksSmall'
        Tiles = 'SlateTiles'; Pillar = 'SlatePillar'
    }
    [pscustomobject]@{
        Cn = '紫晶'; Base = 'AmethystDustBlock'; Group = 'AmethystBlocks'
        Bricks = 'AmethystBricks'; Small = 'AmethystBricksSmall'
        Tiles = 'AmethystTiles'; Pillar = 'AmethystPillar'
    }
    [pscustomobject]@{
        Cn = '淬灵'; Base = 'QuenchedAllay'; Group = 'QuenchedAllayBlocks'
        Bricks = 'QuenchedAllayBricks'; Small = 'QuenchedAllayBricksSmall'
        Tiles = 'QuenchedAllayTiles'; Pillar = $null
    }
)

foreach ($fam in $stoneFamilies) {
    $base = $fam.Base
    $grp = '#' + $fam.Group

    Add-Rec $fam.Bricks (New-Rec 4 ([ordered]@{ $base = 4 }) 'TileID.WorkBenches' `
        "源 stoneSet：$($fam.Cn)砖 ×4 ← $($fam.Cn)基底块 ×4")
    Add-Rec $fam.Bricks (New-Rec 1 ([ordered]@{ $fam.Small = 1 }) 'TileID.WorkBenches' `
        '源 stoneSet：砖 ×1 ← 小砖 ×1（回炉，HexplatRecipes.java:615）')
    Add-Rec $fam.Bricks (New-Rec 1 ([ordered]@{ $grp = 1 }) 'TileID.HeavyWorkBench' `
        '源 stoneCutterFromTag：切石机 1:1（泰拉 = 重型工作台）')

    Add-Rec $fam.Small (New-Rec 1 ([ordered]@{ $fam.Bricks = 1 }) 'TileID.WorkBenches' `
        '源 stoneSet：小砖 ×1 ← 砖 ×1（切细）')
    Add-Rec $fam.Small (New-Rec 1 ([ordered]@{ $grp = 1 }) 'TileID.HeavyWorkBench' `
        '源 stoneCutterFromTag：切石机 1:1')

    Add-Rec $fam.Tiles (New-Rec 4 ([ordered]@{ $fam.Bricks = 4 }) 'TileID.WorkBenches' `
        '源 stoneSet：瓦 ×4 ← 砖 ×4（**不是**基底块）')
    Add-Rec $fam.Tiles (New-Rec 1 ([ordered]@{ $grp = 1 }) 'TileID.HeavyWorkBench' `
        '源 stoneCutterFromTag：切石机 1:1')

    if ($fam.Pillar) {
        Add-Rec $fam.Pillar (New-Rec 2 ([ordered]@{ $base = 2 }) 'TileID.WorkBenches' `
            "源 stoneSet：$($fam.Cn)柱 ×2 ← $($fam.Cn)基底块 ×2")
        Add-Rec $fam.Pillar (New-Rec 1 ([ordered]@{ $grp = 1 }) 'TileID.HeavyWorkBench' `
            '源 stoneCutterFromTag：切石机 1:1')
    }
}

# 基底块的成形（源项目里不在 stoneSet 里，各自单列）
Add-Rec 'SlateBlock' (New-Rec 8 ([ordered]@{ '!DeepslateItem' = 8; '!AmethystDust' = 1 }) 'TileID.WorkBenches' `
    '源 281 ringAll(深板岩, 紫水晶粉)：8 深板岩环 + 1 粉 → 8 板岩块。深板岩是移植版的物品（石块 + 粉压成，见 Content/Tiles/Deepslate.cs）')
# 淬灵晶块没有合成：照原版只能剥离意识（紫水晶块 + 悦灵）得到，挖它要「共振」镐（原版的精准采集）才掉方块本身。
# 这里曾经有「4 片碎片 → 1 块」代替精准采集，2026-10-01 有了共振前缀后去掉。

# ── 板岩 × 紫晶 混料（源项目 441-459，shapeless 1 + 1 → 2）──
$stoneMix = @(
    @{ Out = 'SlateAmethystBricks';      A = 'SlateBricks';      B = 'AmethystBricks' }
    @{ Out = 'SlateAmethystBricksSmall'; A = 'SlateBricksSmall'; B = 'AmethystBricksSmall' }
    @{ Out = 'SlateAmethystTiles';       A = 'SlateTiles';       B = 'AmethystTiles' }
    @{ Out = 'SlateAmethystPillar';      A = 'SlatePillar';      B = 'AmethystPillar' }
)
foreach ($m in $stoneMix) {
    Add-Rec $m.Out (New-Rec 2 ([ordered]@{ $m.A = 1; $m.B = 1 }) 'TileID.WorkBenches' `
        "源 441-459：$($m.A) + $($m.B) → 2（板岩与紫晶同款混料）")
}

# ── 启迪木系（源项目 312-390 + HexTags）── 阶段：**肉前**
#
# 源项目里这一族**没有合成配方**：启迪原木/彩色树叶是「启迪树苗」法术（`hexcasting:edify`）
# 长出的**阿卡夏树**产出的。那个法术是普通法术 —— `HexActionTagProvider` 的启蒙名单
# （REQUIRES_ENLIGHTENMENT）里只有雷击/飞行/造岩浆/大传送/大哨卫/祈雨/停雨/脑叶切除/
# 造电池/5 种药水这 15 个大战法术，**没有 edify**。所以它的门槛是「找得到树苗 + 会画图案」，
# 属于早期内容。
#
# 泰拉侧还没有阿卡夏树与启迪树苗（世界生成那一块没做），所以下面给的是**保底转化**：
# 站用工作台、材料用原版木材（`RecipeGroupID.Wood` = 任意木材，原版配方组）。
# 比例逐条照抄源项目，注释里标了行号。等启迪树苗做出来，这些保底配方可以原样保留
# （源项目自己也留了 Create 兼容的粉碎/切割路径）。
$WOOD = 'TileID.WorkBenches'

Add-Rec 'EdifiedLog' (New-Rec 1 ([ordered]@{ '%Wood' = 1 }) $WOOD `
    '源：Edify 把树苗变成启迪树苗 → 阿卡夏树（**无配方**）→ 这里给 1:1 保底转化')
Add-Rec 'EdifiedLogAmethyst' (New-Rec 1 ([ordered]@{ 'EdifiedLog' = 1; '@Amethyst' = 1 }) $WOOD `
    '彩色启迪原木（嵌紫晶）；源项目来自不同树种的阿卡夏树，泰拉用宝石定色')
Add-Rec 'EdifiedLogAventurine' (New-Rec 1 ([ordered]@{ 'EdifiedLog' = 1; '@Emerald' = 1 }) $WOOD `
    '彩色启迪原木（嵌翡翠 ≈ 东陵玉，同色系、同为肉前宝石）')
Add-Rec 'EdifiedLogCitrine' (New-Rec 1 ([ordered]@{ 'EdifiedLog' = 1; '@Topaz' = 1 }) $WOOD `
    '彩色启迪原木（嵌黄玉 ≈ 黄晶，同色系、同为肉前宝石）')
Add-Rec 'EdifiedLogPurple' (New-Rec 1 ([ordered]@{ 'EdifiedLog' = 1; '!AmethystDust' = 1 }) $WOOD `
    '彩色启迪原木（嵌紫水晶粉）')
Add-Rec 'StrippedEdifiedLog' (New-Rec 1 ([ordered]@{ '#EdifiedLogs' = 1 }) $WOOD `
    '源 HexStrippables：5 种启迪原木都去皮成同一种（tag 输入）')
Add-Rec 'EdifiedWood' (New-Rec 3 ([ordered]@{ 'EdifiedLog' = 4 }) $WOOD `
    '源 319-323：启迪木 ×3 ← 启迪原木 ×4')
Add-Rec 'StrippedEdifiedWood' (New-Rec 3 ([ordered]@{ 'StrippedEdifiedLog' = 4 }) $WOOD `
    '源 319-323：去皮启迪木 ×3 ← 去皮启迪原木 ×4')
Add-Rec 'EdifiedPlanks' (New-Rec 4 ([ordered]@{ '#EdifiedLogs' = 1 }) $WOOD `
    '源 312-314：启迪木板 ×4 ← 1 个任意启迪原木（tag）')
Add-Rec 'EdifiedPanel' (New-Rec 9 ([ordered]@{ 'EdifiedPlanks' = 9 }) $WOOD `
    '源 326-331：面板 ×9 ← 启迪木板 ×9')
Add-Rec 'EdifiedTile' (New-Rec 6 ([ordered]@{ 'EdifiedPlanks' = 6 }) $WOOD `
    '源 333-338：瓷砖 ×6 ← 启迪木板 ×6')
Add-Rec 'AmethystEdifiedLeaves' (New-Rec 4 ([ordered]@{ 'EdifiedPlanks' = 1; '@Amethyst' = 1 }) $WOOD `
    '源：树叶由 Edify 转化（**无配方**）→ 保底：启迪木板定形 + 宝石定色')
Add-Rec 'AventurineEdifiedLeaves' (New-Rec 4 ([ordered]@{ 'EdifiedPlanks' = 1; '@Emerald' = 1 }) $WOOD `
    '源：树叶由 Edify 转化（**无配方**）→ 保底同上')
Add-Rec 'CitrineEdifiedLeaves' (New-Rec 4 ([ordered]@{ 'EdifiedPlanks' = 1; '@Topaz' = 1 }) $WOOD `
    '源：树叶由 Edify 转化（**无配方**）→ 保底同上')

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }

function New-BlockTexture {
    param([string]$Name, [int]$R, [int]$G, [int]$B, [string]$Pattern)

    $S = 16
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::Transparent)

    $rng = New-Object System.Random(($R * 7919 + $G * 131 + $B))

    function Px($x, $y, $c) {
        if ($x -lt 0 -or $x -ge 16 -or $y -lt 0 -or $y -ge 16) { return }
        $brush = New-Object System.Drawing.SolidBrush($c)
        $gfx.FillRectangle($brush, $x, $y, 1, 1)
        $brush.Dispose()
    }
    # 只接受一个「明暗偏移」参数。
    # 注意：之前这里是 Shade($base, $delta) 而调用写成 Shade $d 0 —— 参数错位，
    # 结果所有像素都是基准色（贴图整片纯色、毫无纹样）。函数签名要跟调用对齐。
    function Shade([int]$delta) {
        return [System.Drawing.Color]::FromArgb(255, (Clamp255 ($R + $delta)), (Clamp255 ($G + $delta)), (Clamp255 ($B + $delta)))
    }

    # 噪点分档：只用 3 档，避免"连续渐变"的糊感（泰拉的材质是离散调色板）
    $noise = @{}
    for ($y = 0; $y -lt $S; $y++) {
        for ($x = 0; $x -lt $S; $x++) {
            $n = $rng.Next(3)
            $delta = switch ($n) { 0 { -10 } 1 { 0 } default { 10 } }
            $noise["$x,$y"] = $delta
        }
    }

    for ($y = 0; $y -lt $S; $y++) {
        for ($x = 0; $x -lt $S; $x++) {
            $d = $noise["$x,$y"]

            switch ($Pattern) {
                'plain' { }

                'tiles' {
                    # 8x8 两格瓦，缝在 x=0/8 与 y=0/8
                    if ($x % 8 -eq 0 -or $y % 8 -eq 0) { $d -= 16 }
                    else { $d += 4 }
                }

                'bricks' {
                    # 错缝砖：每 4 行错一次，砖宽 8
                    $row = [Math]::Floor($y / 4)
                    $offset = if ($row % 2 -eq 0) { 0 } else { 4 }
                    if ($y % 4 -eq 0 -or (($x + $offset) % 8) -eq 0) { $d -= 18 }
                    else { $d += 3 }
                }

                'bricksSmall' {
                    $row = [Math]::Floor($y / 2)
                    $offset = if ($row % 2 -eq 0) { 0 } else { 2 }
                    if ($y % 2 -eq 0 -or (($x + $offset) % 4) -eq 0) { $d -= 16 }
                    else { $d += 2 }
                }

                'pillar' {
                    # 竖纹柱：中间两道亮线，两侧压暗
                    if ($x -eq 0 -or $x -eq 15) { $d -= 20 }
                    elseif ($x -eq 5 -or $x -eq 10) { $d += 16 }
                    else { $d -= 4 }
                }

                'bark' {
                    # 树皮：竖向条纹 + 少量结节
                    if ($rng.Next(6) -eq 0) { $d -= 22 }
                    if ($x % 5 -eq 0) { $d -= 8 }
                    if ($y % 7 -eq 0 -and $x % 3 -eq 0) { $d += 12 }
                }

                'strippedBark' {
                    # 去皮：更平滑，只有细竖纹
                    if ($x % 6 -eq 0) { $d -= 6 }
                    if ($rng.Next(10) -eq 0) { $d += 10 }
                }

                'planks' {
                    # 木板：每 4 行一条横缝，缝上有短竖线
                    if ($y % 4 -eq 0) { $d -= 20 }
                    if ($y % 4 -ne 0 -and ($x % 8 -eq 3) -and ($y % 4 -eq 2)) { $d -= 12 }
                    else { $d += 4 }
                }

                'leaves' {
                    # 树叶：随机镂空（保留一点透明，看起来才像叶子）
                    if ($rng.Next(7) -eq 0) { $d -= 34 }
                }
            }

            Px $x $y (Shade $d)
        }
    }

    # 树叶：挖掉几块，让轮廓不规整
    if ($Pattern -eq 'leaves') {
        $transparent = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
        foreach ($p in @(@(0, 0), @(15, 0), @(0, 15), @(15, 15), @(7, 0), @(0, 8), @(15, 7))) {
            Px $p[0] $p[1] $transparent
        }
    }

    $gfx.Dispose()
    $bmp.Save((Join-Path $tileDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ══ 配方 -> C# 代码 ═══════════════════════════════════════════════════
function Get-IngredientCode([string]$spec, [int]$count) {
    if ($spec.StartsWith('@')) { return ".AddIngredient(ItemID.$($spec.Substring(1)), $count)" }
    # 配方组用 C# 常量名而不是字符串字面量：名字写错就编译不过，不会静默少一组
    if ($spec.StartsWith('#')) { return ".AddRecipeGroup(HexRecipeGroups.$($spec.Substring(1)), $count)" }
    # % = 原版自带的配方组（Terraria.ID.RecipeGroups），如 %Wood 收任意木材、%IronBar 收铁锭或铅锭
    if ($spec.StartsWith('%')) { return ".AddRecipeGroup(RecipeGroupID.$($spec.Substring(1)), $count)" }
    if ($spec.StartsWith('!')) { return ".AddIngredient<$($spec.Substring(1))>($count)" }
    return ".AddIngredient<${spec}Item>($count)"
}

function Get-RecipeLines($rec) {
    $lines = New-Object System.Collections.ArrayList
    if ($rec.Comment) { [void]$lines.Add("        // $($rec.Comment)") }
    if ($rec.Out -eq 1) { [void]$lines.Add('        CreateRecipe()') }
    else { [void]$lines.Add("        CreateRecipe($($rec.Out))") }
    foreach ($k in $rec.Ings.Keys) {
        [void]$lines.Add('            ' + (Get-IngredientCode $k $rec.Ings[$k]))
    }
    [void]$lines.Add("            .AddTile($($rec.Tile))")
    [void]$lines.Add('            .Register();')
    [void]$lines.Add('')
    return $lines
}

# ── 生成代码 ──────────────────────────────────────────────────────────
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("// <auto-generated>")
[void]$sb.AppendLine("//     本文件由 _tools/gen_deco_blocks.ps1 生成，**不要手改**：")
[void]$sb.AppendLine("//     改表里的那一行，然后重新跑脚本。手改会在下次生成时被覆盖。")
[void]$sb.AppendLine("// </auto-generated>")
[void]$sb.AppendLine("using HexCastingTerraria.Content.Items;")
[void]$sb.AppendLine("using Microsoft.Xna.Framework;")
[void]$sb.AppendLine("using Terraria;")
[void]$sb.AppendLine("using Terraria.ID;")
[void]$sb.AppendLine("using Terraria.ModLoader;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("namespace HexCastingTerraria.Content.Tiles;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("/// <summary>")
[void]$sb.AppendLine("/// 建材方块的公共实现（P2-6 装饰方块家族）。")
[void]$sb.AppendLine("///")
[void]$sb.AppendLine("/// 每个子类只声明「自己和别人有什么不同」（颜色 / 镐力 / 尘 / 掉落），")
[void]$sb.AppendLine("/// 其余行为完全一致。子类由脚本生成，见文件头。")
[void]$sb.AppendLine("/// </summary>")
[void]$sb.AppendLine("public abstract class HexDecoBlock : ModTile")
[void]$sb.AppendLine("{")
[void]$sb.AppendLine("    /// <summary>小地图上的颜色。</summary>")
[void]$sb.AppendLine("    protected abstract Color MapColor { get; }")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    /// <summary>挖掘所需镐力。0 = 任意镐；1 = 梦魇镐以上；2 = 精金镐以上。</summary>")
[void]$sb.AppendLine("    protected virtual int RequiredPick => 0;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    /// <summary>挖掘时的尘埃。</summary>")
[void]$sb.AppendLine("    protected virtual int BlockDust => DustID.Stone;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    /// <summary>是否阻挡光线（树叶这类不该挡）。</summary>")
[void]$sb.AppendLine("    protected virtual bool BlocksLight => true;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    /// <summary>发出的光，null = 不发光（原版淬灵晶系亮度 4）。</summary>")
[void]$sb.AppendLine("    protected virtual Vector3? LightColor => null;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    public override void SetStaticDefaults()")
[void]$sb.AppendLine("    {")
[void]$sb.AppendLine("        Main.tileSolid[Type] = true;")
[void]$sb.AppendLine("        Main.tileBlockLight[Type] = BlocksLight;")
[void]$sb.AppendLine("        Main.tileMergeDirt[Type] = false;")
[void]$sb.AppendLine("        Main.tileLighted[Type] = LightColor is not null;")
[void]$sb.AppendLine()
[void]$sb.AppendLine("        MinPick = RequiredPick;")
[void]$sb.AppendLine("        DustType = BlockDust;")
[void]$sb.AppendLine("        HitSound = SoundID.Tink;")
[void]$sb.AppendLine("        AddMapEntry(MapColor);")
[void]$sb.AppendLine("    }")
[void]$sb.AppendLine()
[void]$sb.AppendLine("    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)")
[void]$sb.AppendLine("    {")
[void]$sb.AppendLine("        if (LightColor is not { } c) return;")
[void]$sb.AppendLine("        r = c.X;")
[void]$sb.AppendLine("        g = c.Y;")
[void]$sb.AppendLine("        b = c.Z;")
[void]$sb.AppendLine("    }")
[void]$sb.AppendLine("}")
[void]$sb.AppendLine()

foreach ($b in $blocks) {
    $cls = $b[0]; $cn = $b[1]; $r = $b[2]; $g2 = $b[3]; $b2 = $b[4]
    $pick = $b[6]; $dust = $b[7]; $rarity = [int]$b[8]

    # 类名 -> 源项目的蛇形 id（SlateBricksSmall -> slate_bricks_small）
    $slug = ($cls -creplace '([a-z0-9])([A-Z])', '$1_$2').ToLower()

    if ($rarity -lt 0 -or $rarity -ge $rarityNames.Count) {
        throw "$cls 的稀有度档位 $rarity 越界（合法 0..$($rarityNames.Count - 1)）"
    }
    $rarityName = $rarityNames[$rarity]

    [void]$sb.AppendLine("/// <summary>$cn。对应源项目 ``hexcasting:$slug``。</summary>")
    [void]$sb.AppendLine("public sealed class $cls : HexDecoBlock")
    [void]$sb.AppendLine("{")
    [void]$sb.AppendLine("    protected override Color MapColor => new($r, $g2, $b2);")

    if ($pick -gt 0) { [void]$sb.AppendLine("    protected override int RequiredPick => $pick;") }
    if ($dust -ne 'Stone') { [void]$sb.AppendLine("    protected override int BlockDust => DustID.$dust;") }
    if ($b[5] -eq 'leaves') { [void]$sb.AppendLine("    protected override bool BlocksLight => false;") }
    # 原版淬灵晶系（BlockQuenchedAllay）亮度 4：和阿卡夏桥接块同一档的淡紫
    if ($cls -like 'QuenchedAllay*') { [void]$sb.AppendLine("    protected override Vector3? LightColor => new(0.16f, 0.12f, 0.24f);") }

    [void]$sb.AppendLine("}")
    [void]$sb.AppendLine()

    # 物品
    $itemCls = "${cls}Item"
    [void]$sb.AppendLine("/// <summary>$cn（物品形态）。</summary>")
    [void]$sb.AppendLine("public sealed class $itemCls : HexDecoBlockItem")
    [void]$sb.AppendLine("{")
    [void]$sb.AppendLine("    public override int TileType => ModContent.TileType<$cls>();")
    [void]$sb.AppendLine("    protected override int ItemRarity => ItemRarityID.$rarityName;")
    [void]$sb.AppendLine()

    $recs = $null
    if ($recipes.Contains($cls)) { $recs = $recipes[$cls] }

    if ($null -eq $recs -or $recs.Count -eq 0) {
        # 没配方就得说清楚为什么 —— 否则下一个人会以为是漏了
        [void]$sb.AppendLine("    // 没有合成配方：源项目这一项也不是合成的（见 _tools/gen_deco_blocks.ps1 的配方表）")
    }
    else {
        [void]$sb.AppendLine("    public override void AddRecipes()")
        [void]$sb.AppendLine("    {")
        foreach ($rec in $recs) {
            foreach ($ln in (Get-RecipeLines $rec)) { [void]$sb.AppendLine($ln) }
        }
        [void]$sb.AppendLine("    }")
    }
    [void]$sb.AppendLine("}")
    [void]$sb.AppendLine()

    New-BlockTexture "$cls.png" $r $g2 $b2 $b[5]
}

[System.IO.File]::WriteAllText($codePath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
"生成 $($blocks.Count) 个方块 -> $codePath"

# 顺带把「有配方 / 无配方」点一遍，防止表里漏写角色
$noRecipe = @()
foreach ($b in $blocks) { if (-not $recipes.Contains($b[0])) { $noRecipe += $b[0] } }
if ($noRecipe.Count -gt 0) { "注意：无配方：$($noRecipe -join ', ')" }
"配方条目：$(($recipes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) 条，覆盖 $(($recipes.Keys).Count) 个物品"
