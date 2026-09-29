# 召唤方块 / 召唤光源贴图（16x16）。开发期占位。
Add-Type -AssemblyName System.Drawing
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16
function Px($g, $x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c); $g.FillRectangle($b, $x, $y, 1, 1); $b.Dispose()
}
function C($r, $g2, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g2, $b) }
function New-Bmp { 
    $b = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear([System.Drawing.Color]::Transparent)
    return @($b, $g)
}

# ---- 召唤方块：半透明的能量方块（亮紫 + 网格纹）----
$r = New-Bmp; $bmp = $r[0]; $g = $r[1]
$rng = New-Object System.Random(5150)
for ($y = 0; $y -lt $S; $y++) {
    for ($x = 0; $x -lt $S; $x++) {
        $n = $rng.Next(20)
        Px $g $x $y (C (112 + $n) (84 + $n) (160 + $n))
    }
}
# 能量网格纹：每隔 4 格一道亮线
for ($i = 0; $i -lt $S; $i += 4) {
    for ($k = 0; $k -lt $S; $k++) {
        Px $g $i $k (C 190 150 245)
        Px $g $k $i (C 190 150 245)
    }
}
# 四角高亮，暗示「这是凭空造出来的」
foreach ($p in @(@(0,0), @(15,0), @(0,15), @(15,15))) { Px $g $p[0] $p[1] (C 225 195 255) }
$g.Dispose(); $bmp.Save((Join-Path $out "ConjuredBlock.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"生成 ConjuredBlock.png"

# ---- 召唤光源：一个发光的晶体点 ----
$r = New-Bmp; $bmp = $r[0]; $g = $r[1]
# 由内到外的光晕
for ($rad = 7; $rad -ge 1; $rad--) {
    $a = 30 + (7 - $rad) * 28
    if ($a -gt 255) { $a = 255 }
    $c = [System.Drawing.Color]::FromArgb($a, 240, 220, 255)
    $b = New-Object System.Drawing.SolidBrush($c)
    $g.FillEllipse($b, 8 - $rad, 8 - $rad, $rad * 2, $rad * 2)
    $b.Dispose()
}
# 内核
Px $g 8 8 (C 255 252 255)
Px $g 7 8 (C 255 252 255); Px $g 9 8 (C 255 252 255)
Px $g 8 7 (C 255 252 255); Px $g 8 9 (C 255 252 255)
$g.Dispose(); $bmp.Save((Join-Path $out "ConjuredLight.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"生成 ConjuredLight.png"
