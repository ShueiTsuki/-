# 物品图标重刷（第二批：材料 / 数据载体 / 卷轴 / 打包法术 / 媒介瓶）
# 与第一批（法杖）同一条流水线，见 pixelforge.ps1 的说明。
. "$PSScriptRoot\pixelforge.ps1"

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 32

function Mask-Powder() {
    $m = New-Canvas $S $S
    # 一小撮粉：底部宽、顶部散
    $rows = @{
        18 = '......####......'
        19 = '....########....'
        20 = '...##########...'
        21 = '..############..'
        22 = '..############..'
        23 = '.##############.'
        24 = '.##############.'
        25 = '..############..'
    }
    foreach ($y in $rows.Keys) {
        $pad = [int](($S - $rows[$y].Length) / 2)
        $m[$y] = $m[$y].Remove($pad, $rows[$y].Length).Insert($pad, $rows[$y])
    }
    return $m
}

function Mask-Shard() {
    $m = New-Canvas $S $S
    # 碎片：菱形
    $w = 1
    for ($y = 8; $y -le 24; $y++) {
        $half = [Math]::Min(7, [Math]::Min($y - 7, 25 - $y) + 1)
        $len = $half * 2
        $pad = 16 - $half
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('#' * $len))
    }
    return $m
}

function Mask-Charged() {
    $m = New-Canvas $S $S
    # 充能晶体：菱形 + 内部高光核心（'o'）
    for ($y = 7; $y -le 25; $y++) {
        $half = [Math]::Min(8, [Math]::Min($y - 6, 26 - $y) + 1)
        $len = $half * 2
        $pad = 16 - $half
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('#' * $len))
    }
    foreach ($y in 12..17) {
        $m[$y] = $m[$y].Remove(14, 4).Insert(14, 'oooo')
    }
    return $m
}

function Mask-Orb() {
    $m = New-Canvas $S $S
    for ($y = 8; $y -le 24; $y++) {
        $dy = $y - 16
        $half = [int]([Math]::Sqrt([Math]::Max(0, 64 - $dy * $dy)))
        if ($half -le 0) { continue }
        $len = $half * 2
        $pad = 16 - $half
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('#' * $len))
    }
    # 内部的核心
    foreach ($y in 14..18) { $m[$y] = $m[$y].Remove(14, 4).Insert(14, 'oooo') }
    return $m
}

function Mask-Knot() {
    $m = New-Canvas $S $S
    # 念珠：一串珠子
    foreach ($c in @(@(11, 11), @(16, 10), @(21, 12), @(13, 18), @(19, 19))) {
        $cx = $c[0]; $cy = $c[1]
        for ($y = $cy - 2; $y -le $cy + 2; $y++) {
            for ($x = $cx - 2; $x -le $cx + 2; $x++) {
                $dx = $x - $cx; $dy = $y - $cy
                if ($dx * $dx + $dy * $dy -le 5) {
                    $m[$y] = $m[$y].Remove($x, 1).Insert($x, '#')
                }
            }
        }
    }
    return $m
}

function Mask-Scroll([int]$w) {
    $m = New-Canvas $S $S
    if ($w -le 0) { $w = 1 }
    # 卷轴：中间纸卷 + 两端木轴
    $half = 4 + $w
    $len = $half * 2
    $pad = 16 - $half
    for ($y = 10; $y -le 22; $y++) {
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('#' * $len))
    }
    foreach ($y in @(9, 23)) {
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('+' * $len))
    }
    return $m
}

function Mask-Flask() {
    $m = New-Canvas $S $S
    # 瓶身
    for ($y = 12; $y -le 26; $y++) {
        $half = [Math]::Min(7, 3 + ($y - 12))
        $len = $half * 2
        $pad = 16 - $half
        $m[$y] = $m[$y].Remove($pad, $len).Insert($pad, ('#' * $len))
    }
    # 瓶颈 + 木塞
    foreach ($y in 7..11) { $m[$y] = $m[$y].Remove(14, 4).Insert(14, '####') }
    foreach ($y in 5..6) { $m[$y] = $m[$y].Remove(13, 6).Insert(13, '++++++') }
    return $m
}

function Mask-Cypher() {
    $m = New-Canvas $S $S
    # 符纸：窄长条 + 折角
    for ($y = 8; $y -le 25; $y++) {
        $x = 12; $len = 8
        if ($y -le 11) { $len = 8 - (12 - $y) }
        $m[$y] = $m[$y].Remove($x, $len).Insert($x, ('#' * $len))
    }
    return $m
}

function Mask-Trinket() {
    $m = New-Canvas $S $S
    # 环
    for ($y = 6; $y -le 26; $y++) {
        for ($x = 6; $x -le 26; $x++) {
            $dx = $x - 16; $dy = $y - 16
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($d -ge 7 -and $d -le 9.5) { $m[$y] = $m[$y].Remove($x, 1).Insert($x, '+') }
        }
    }
    foreach ($y in 14..17) { $m[$y] = $m[$y].Remove(14, 4).Insert(14, 'oooo') }
    return $m
}

function Mask-Artifact() {
    $m = New-Canvas $S $S
    # 宽底座 + 柱身 + 顶端尖（比另外两件"更重"）
    for ($y = 22; $y -le 26; $y++) {
        $half = 4 + ($y - 22)
        $len = $half * 2
        $m[$y] = $m[$y].Remove(16 - $half, $len).Insert(16 - $half, ('#' * $len))
    }
    foreach ($y in 13..21) { $m[$y] = $m[$y].Remove(11, 10).Insert(11, ('#' * 10)) }
    foreach ($y in 10..12) { $m[$y] = $m[$y].Remove(13, 6).Insert(13, 'oooooo') }
    foreach ($y in 7..9) { $m[$y] = $m[$y].Remove(14, 4).Insert(14, 'oooo') }
    return $m
}

$amethyst = @(168, 132, 214)
$metal = @(188, 182, 196)
$paper = @(226, 214, 186)

# 媒质材料
New-PixelArt -Name "AmethystDust.png"      -OutDir $out -Mask (Mask-Powder)  -Body @(178, 140, 210) -Seed 11
New-PixelArt -Name "AmethystShard.png"     -OutDir $out -Mask (Mask-Shard)   -Body @(160, 126, 200) -Seed 12
New-PixelArt -Name "ChargedAmethyst.png"   -OutDir $out -Mask (Mask-Charged) -Body @(150, 112, 208) -Gem @(236, 214, 255) -Seed 13
New-PixelArt -Name "QuenchedAllayShard.png" -OutDir $out -Mask (Mask-Charged) -Body @(96, 150, 160) -Gem @(190, 246, 250) -Seed 14

# 数据载体
New-PixelArt -Name "Focus.png"       -OutDir $out -Mask (Mask-Orb)  -Body @(126, 104, 160) -Gem @(214, 190, 255) -Seed 21
New-PixelArt -Name "ThoughtKnot.png" -OutDir $out -Mask (Mask-Knot) -Body @(196, 178, 224) -Seed 22

# 卷轴 3 档
New-PixelArt -Name "ScrollSmall.png"  -OutDir $out -Mask (Mask-Scroll 0) -Body $paper -Accent @(140, 108, 70) -Seed 31
New-PixelArt -Name "ScrollMedium.png" -OutDir $out -Mask (Mask-Scroll 2) -Body $paper -Accent @(140, 108, 70) -Seed 32
New-PixelArt -Name "ScrollLarge.png"  -OutDir $out -Mask (Mask-Scroll 4) -Body $paper -Accent @(140, 108, 70) -Seed 33

# 媒质瓶
New-PixelArt -Name "MediaFlask.png" -OutDir $out -Mask (Mask-Flask) -Body @(150, 190, 214) -Accent @(140, 108, 70) -Gem $amethyst -Seed 41

# 打包法术三件套
New-PixelArt -Name "Cypher.png"   -OutDir $out -Mask (Mask-Cypher)   -Body $paper -Seed 51
New-PixelArt -Name "Trinket.png"  -OutDir $out -Mask (Mask-Trinket)  -Body $metal -Gem $amethyst -Seed 52
New-PixelArt -Name "Artifact.png" -OutDir $out -Mask (Mask-Artifact) -Body @(150, 120, 200) -Gem @(240, 226, 255) -Seed 53

"生成材料 4 + 载体 2 + 卷轴 3 + 媒介瓶 1 + 打包法术 3"
