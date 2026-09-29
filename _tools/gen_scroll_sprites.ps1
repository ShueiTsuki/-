# 生成三种卷轴贴图。32x32，差别在卷起的宽度。
Add-Type -AssemblyName System.Drawing
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 32

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}
function Px($g, $x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c); $g.FillRectangle($b, $x, $y, 1, 1); $b.Dispose()
}
function Save-Bmp($bmp, $name) { $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
function C($r,$g,$b) { [System.Drawing.Color]::FromArgb(255,$r,$g,$b) }

# 卷轴：中间一块纸（带紫色纹路），上下两端是卷轴木轴
function New-Scroll($name, $halfWidth, $paperTone, $rodTone) {
    $r = New-Canvas; $bmp = $r[0]; $g = $r[1]

    $top = 7; $bot = 24
    # 纸面
    for ($y = $top; $y -le $bot; $y++) {
        for ($x = 16 - $halfWidth; $x -le 16 + $halfWidth; $x++) {
            # 左亮右暗做出卷曲感
            $c = if ($x -le 16) { $paperTone } else { C ([int]($paperTone.R*0.82)) ([int]($paperTone.G*0.82)) ([int]($paperTone.B*0.86)) }
            Px $g $x $y $c
        }
    }
    # 纸上的紫色纹路（两道横线 + 一个符印点）
    for ($x = 17 - $halfWidth; $x -le 15 + $halfWidth; $x++) {
        Px $g $x 11 (C 168 122 220)
        Px $g $x 20 (C 168 122 220)
    }
    Px $g 16 15 (C 190 148 240)
    Px $g 16 16 (C 190 148 240)
    Px $g 15 16 (C 190 148 240)
    Px $g 17 16 (C 190 148 240)

    # 上下木轴：比纸略宽
    $rodW = $halfWidth + 2
    for ($x = 16 - $rodW; $x -le 16 + $rodW; $x++) {
        Px $g $x ($top - 2) $rodTone
        Px $g $x ($top - 1) (C ([int]($rodTone.R*1.25)) ([int]($rodTone.G*1.25)) ([int]($rodTone.B*1.25)))
        Px $g $x ($bot + 1) (C ([int]($rodTone.R*1.25)) ([int]($rodTone.G*1.25)) ([int]($rodTone.B*1.25)))
        Px $g $x ($bot + 2) $rodTone
    }

    $g.Dispose(); Save-Bmp $bmp $name
}

$paper = C 232 222 200
$rod   = C 122 96 72
New-Scroll "ScrollSmall.png"  3 $paper $rod
New-Scroll "ScrollMedium.png" 5 $paper $rod
New-Scroll "ScrollLarge.png"  8 $paper $rod
"完成"
