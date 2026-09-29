# 生成晶洞方块贴图（16x16 物块贴图）。
Add-Type -AssemblyName System.Drawing

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16

function New-Bmp {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}
function Save-Bmp($bmp, $name) {
    $p = Join-Path $out $name
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose(); "生成 $name"
}
function Px($g, $x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c)
    $g.FillRectangle($b, $x, $y, 1, 1)
    $b.Dispose()
}

# ---------- 母岩：深紫石底 + 亮晶斑点 ----------
$r = New-Bmp; $bmp = $r[0]; $g = $r[1]
$rng = New-Object System.Random(41)
for ($y = 0; $y -lt $S; $y++) {
    for ($x = 0; $x -lt $S; $x++) {
        $n = $rng.Next(22)
        Px $g $x $y ([System.Drawing.Color]::FromArgb(255, 74 + $n, 42 + $n, 104 + $n))
    }
}
for ($k = 0; $k -lt 14; $k++) {
    $x = $rng.Next($S); $y = $rng.Next($S)
    Px $g $x $y ([System.Drawing.Color]::FromArgb(255, 196, 150, 245))
}
$g.Dispose(); Save-Bmp $bmp "GeodeCore.png"

# ---------- 晶簇：若干根高度不一的晶柱 ----------
# 每根晶柱 = 一个尖锥。底部对齐到 y=15。
function Draw-Spike($g, $cx, $height, $halfWidth, $bright) {
    $baseY = 15
    $topY = $baseY - $height + 1
    for ($y = $topY; $y -le $baseY; $y++) {
        $t = ($y - $topY) / [double][Math]::Max(1, ($baseY - $topY))
        $w = [int][Math]::Round($t * $halfWidth)
        for ($dx = -$w; $dx -le $w; $dx++) {
            $c = if ($dx -le 0) { $bright }
                 else { [System.Drawing.Color]::FromArgb(255, [int]($bright.R * 0.7), [int]($bright.G * 0.7), [int]($bright.B * 0.78)) }
            Px $g ($cx + $dx) $y $c
        }
    }
    # 尖顶高光
    Px $g $cx ($topY + 1) ([System.Drawing.Color]::FromArgb(255, 246, 234, 255))
}

$main  = [System.Drawing.Color]::FromArgb(255, 178, 128, 232)
$pale  = [System.Drawing.Color]::FromArgb(255, 205, 168, 246)

function New-Cluster($name, $spikes) {
    $r = New-Bmp; $bmp = $r[0]; $g = $r[1]
    foreach ($s in $spikes) {
        # 侧柱先用暗一点的颜色画，主柱后画（压在上面）
        Draw-Spike $g $s[0] $s[1] $s[2] $s[3]
    }
    $g.Dispose(); Save-Bmp $bmp $name
}

$dim = [System.Drawing.Color]::FromArgb(255, 140, 96, 190)

# 小芽：一根矮柱
New-Cluster "AmethystBudSmall.png"  @( ,@(8, 4, 1, $dim) )
# 中芽：一根 + 两侧小柱
New-Cluster "AmethystBudMedium.png" @( @(5, 4, 1, $dim), @(11, 5, 1, $dim), @(8, 8, 2, $main) )
# 大芽：三根，主柱更高
New-Cluster "AmethystBudLarge.png"  @( @(4, 6, 1, $dim), @(12, 7, 1, $dim), @(8, 11, 2, $main) )
# 成熟：四根，主柱到顶，加一根亮柱
New-Cluster "AmethystCluster.png"   @( @(3, 7, 1, $dim), @(13, 8, 1, $dim), @(6, 10, 1, $pale), @(10, 12, 2, $main) )

"完成"
