# 方块贴图与模组图标重刷（第三批）
# 同一条流水线，见 pixelforge.ps1。
. "$PSScriptRoot\pixelforge.ps1"

$tiles = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$root  = "D:\DeepSeekHarness\tmod\HexCastingTerraria"

# ── 母岩（16x16）：深紫石底 + 亮色晶脉 ─────────────────────────────
$m = New-Canvas 16 16
for ($y = 0; $y -lt 16; $y++) { $m[$y] = '#' * 16 }
# 晶脉：不规则走向的 '+'（次材料 = 更亮的紫）
foreach ($p in @(@(3, 2), @(4, 2), @(5, 3), @(5, 4), @(6, 5), @(9, 3), @(10, 4), @(10, 5), @(11, 6),
                 @(2, 9), @(3, 10), @(4, 10), @(7, 8), @(8, 9), @(12, 11), @(13, 12), @(6, 13), @(7, 14))) {
    $x = $p[0]; $y = $p[1]
    $m[$y] = $m[$y].Remove($x, 1).Insert($x, '+')
}
New-PixelArt -Name "GeodeCore.png" -OutDir $tiles -Mask $m -Body @(74, 62, 92) -Accent @(150, 116, 200) -Seed 201

# ── 紫水晶粉块（16x16）：沙质感 + 少量亮点 ─────────────────────────
$m = New-Canvas 16 16
for ($y = 0; $y -lt 16; $y++) { $m[$y] = '#' * 16 }
foreach ($p in @(@(2, 3), @(7, 2), @(12, 4), @(4, 7), @(9, 8), @(13, 9), @(3, 12), @(8, 13), @(11, 14))) {
    $x = $p[0]; $y = $p[1]
    $m[$y] = $m[$y].Remove($x, 1).Insert($x, 'o')
}
New-PixelArt -Name "AmethystDustBlock.png" -OutDir $tiles -Mask $m -Body @(122, 98, 156) -Gem @(198, 170, 240) -Seed 202

# ── 晶簇 4 阶（越大越亮、越"长"）─────────────────────────────────
function Mask-Cluster([int]$size) {
    $mm = New-Canvas 16 16
    # 底部基座
    for ($y = 13; $y -le 15; $y++) {
        for ($x = 2; $x -le 13; $x++) { $mm[$y] = $mm[$y].Remove($x, 1).Insert($x, '#') }
    }
    # 竖直晶体，数量与高度随阶数增长。
    #
    # 注意：这里用字符串 "x:top:half" 而不是数组套数组：
    # PowerShell 的 `switch` 把结果送进管道时会**展开一层**，
    # `@(@(7,11,1))` 出来变成 `7,11,1` 三个标量，
    # 于是 `$c[1]` 变成 $null，掩码悄悄画不出来（而且只在某些阶数上报错）。
    $spec = switch ($size) {
        0 { '7:11:1' }
        1 { '6:9:1,9:11:1' }
        2 { '5:7:2,8:5:2,11:9:1' }
        default { '4:5:2,7:3:2,10:6:2,12:10:1' }
    }

    foreach ($one in ($spec -split ',')) {
        $parts = $one -split ':'
        $cx = [int]$parts[0]; $top = [int]$parts[1]; $half = [int]$parts[2]
        for ($y = $top; $y -le 13; $y++) {
            $width = $half * 2 + 1
            if ($y -eq $top) { $width = 1 }
            $pad = $cx - [int]($width / 2)
            if ($pad -lt 0) { $pad = 0 }
            if ($pad + $width -gt 16) { $width = 16 - $pad }
            if ($width -le 0) { continue }
            $mm[$y] = $mm[$y].Remove($pad, $width).Insert($pad, ('o' * $width))
        }
    }
    return $mm
}

New-PixelArt -Name "AmethystBudSmall.png"  -OutDir $tiles -Mask (Mask-Cluster 0) -Body @(88, 72, 110) -Gem @(176, 146, 220) -Seed 211
New-PixelArt -Name "AmethystBudMedium.png" -OutDir $tiles -Mask (Mask-Cluster 1) -Body @(88, 72, 110) -Gem @(184, 154, 228) -Seed 212
New-PixelArt -Name "AmethystBudLarge.png"  -OutDir $tiles -Mask (Mask-Cluster 2) -Body @(88, 72, 110) -Gem @(194, 164, 238) -Seed 213
New-PixelArt -Name "AmethystCluster.png"   -OutDir $tiles -Mask (Mask-Cluster 3) -Body @(88, 72, 110) -Gem @(208, 178, 252) -Seed 214

# ── 模组图标 ──────────────────────────────────────────────────────
# tModLoader 约定：`icon.png` = 80x80（`icon_small.png` 请**不要**提供，理由见文件末尾）。
# 画一个「六边形图案符印」：这是咒法学最有辨识度的视觉元素。
function Mask-HexSigil([int]$size) {
    $mm = New-Canvas $size $size
    $c = ($size - 1) / 2.0
    $r = $size * 0.42

    # 六边形外框
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $dx = $x - $c; $dy = $y - $c
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($d -ge $r - 1.2 -and $d -le $r) {
                $mm[$y] = $mm[$y].Remove($x, 1).Insert($x, '#')
            }
        }
    }

    # 内部符印：一条折线（借用图案的"起点-转折-终点"观感）
    $pts = @(@(0.30, 0.34), @(0.50, 0.30), @(0.68, 0.44), @(0.56, 0.62), @(0.36, 0.60), @(0.46, 0.46))
    for ($i = 0; $i -lt $pts.Count - 1; $i++) {
        $ax = [int]($pts[$i][0] * $size); $ay = [int]($pts[$i][1] * $size)
        $bx = [int]($pts[$i + 1][0] * $size); $by = [int]($pts[$i + 1][1] * $size)
        $steps = [Math]::Max([Math]::Abs($bx - $ax), [Math]::Abs($by - $ay))
        if ($steps -le 0) { $steps = 1 }
        for ($s = 0; $s -le $steps; $s++) {
            $x = [int]($ax + ($bx - $ax) * $s / $steps)
            $y = [int]($ay + ($by - $ay) * $s / $steps)
            for ($t = -1; $t -le 1; $t++) {
                $nx = $x + $t; $ny = $y
                if ($nx -ge 0 -and $nx -lt $size -and $ny -ge 0 -and $ny -lt $size) {
                    $mm[$ny] = $mm[$ny].Remove($nx, 1).Insert($nx, 'o')
                }
            }
        }
    }

    return $mm
}

New-PixelArt -Name "icon.png"       -OutDir $root -Mask (Mask-HexSigil 80) -Body @(58, 46, 92) -Gem @(206, 178, 255) -Seed 301 -NoNoise

# 注意：**不要**再生成 `icon_small.png`。
#
# 它曾经在这里生成（30x30 六边形符印），而每次打包都会多一条警告：
#     Image loading failed: unknown image type
# 逐项二分定位到就是它：把这个文件移走，警告立刻消失；把它换成 tModLoader
# **内嵌模板**里的那份 icon_small.png（`Terraria/ModLoader/Templates/icon_small.png`），
# 警告也消失。而用 GDI+（System.Drawing）重新编码的 30x30 / 32x32 / 16x16 三份
# 都会复现 —— 也就是说打包期那条解码路径读不了 GDI+ 写出的 PNG。
#
# 那为什么本模组其余 90 多张贴图（同样是 GDI+ 写的）没事？因为它们是**运行期**
# 加载的，走的是另一条路；这条只发生在打包期对 icon_small.png 的那次读取上。
#
# 结论：`icon_small.png` 缺失时 tModLoader 会**回退到自带模板**（见
# `Terraria.ModLoader.Core.ModCompile::AddResource` 里那两个字符串），
# 既没有警告、也不会缺图标 —— 所以「不生成」才是干净的状态。
# 这条由 check_arch.ps1 的断言⑩ 盯着，别手滑加回来。
#
# （工具链事实：模组源码里的 icon.png / icon_small.png 是 tModLoader 约定，
#   见 `Mod::Autoload` 里的 'icon_small.png' 与 'Failed to load icon_small.png. Reason: '。）

"生成方块 6 + 模组图标 1（不含 icon_small.png，理由见上）"
