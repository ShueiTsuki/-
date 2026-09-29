# 生成聚念核心 / 念珠贴图。32x32。
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
    $b = New-Object System.Drawing.SolidBrush($c)
    $g.FillRectangle($b, $x, $y, 1, 1)
    $b.Dispose()
}
function Save-Bmp($bmp, $name) { $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
function C($r,$g,$b) { [System.Drawing.Color]::FromArgb(255,$r,$g,$b) }

# ---------- 聚念核心：一枚菱形晶体嵌在金属环里 ----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
# 外圈金属环
for ($a = 0; $a -lt 360; $a += 3) {
    $rad = $a * [Math]::PI / 180.0
    foreach ($rr in @(11, 12)) {
        $x = [int][Math]::Round(16 + [Math]::Cos($rad) * $rr)
        $y = [int][Math]::Round(16 + [Math]::Sin($rad) * $rr)
        Px $g $x $y (C 150 142 168)
    }
}
# 内部晶体（菱形）
for ($dy = -7; $dy -le 7; $dy++) {
    $w = 7 - [Math]::Abs($dy)
    for ($dx = -$w; $dx -le $w; $dx++) {
        $c = if ($dx -le 0 -and $dy -le 0) { C 226 198 255 } else { C 172 122 228 }
        Px $g (16 + $dx) (16 + $dy) $c
    }
}
Px $g 15 12 (C 250 242 255)
$g.Dispose(); Save-Bmp $bmp "Focus.png"

# ---------- 念珠：一串珠子（竖直，上端打结）----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
# 绳
for ($y = 6; $y -le 26; $y++) { Px $g 16 $y (C 120 100 84) }
# 珠子
$beads = @(8, 12, 16, 20, 24)
foreach ($by in $beads) {
    for ($dy = -2; $dy -le 2; $dy++) {
        $w = 2 - [Math]::Abs($dy)
        for ($dx = -$w; $dx -le $w; $dx++) {
            $c = if ($dx -le 0 -and $dy -le 0) { C 222 190 255 } else { C 166 116 222 }
            Px $g (16 + $dx) ($by + $dy) $c
        }
    }
}
# 顶端的结
Px $g 16 4 (C 210 178 250)
Px $g 15 5 (C 190 150 240)
Px $g 17 5 (C 190 150 240)
$g.Dispose(); Save-Bmp $bmp "ThoughtKnot.png"
"完成"
