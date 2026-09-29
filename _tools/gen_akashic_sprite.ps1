# 阿卡夏记录方块贴图（16x16）。占位风格，见 TODO_PLAN.md 美术欠账一节。
Add-Type -AssemblyName System.Drawing
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16
$bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)

function Px($x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c); $g.FillRectangle($b, $x, $y, 1, 1); $b.Dispose()
}
function C($r, $g2, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g2, $b) }

$rng = New-Object System.Random(2024)
# 深紫色石底
for ($y = 0; $y -lt $S; $y++) {
    for ($x = 0; $x -lt $S; $x++) {
        $n = $rng.Next(18)
        Px $x $y (C (78 + $n) (58 + $n) (108 + $n))
    }
}
# 中央一块「石板」，表示这是记录载体
for ($y = 3; $y -le 12; $y++) {
    for ($x = 3; $x -le 12; $x++) {
        $edge = ($x -eq 3 -or $x -eq 12 -or $y -eq 3 -or $y -eq 12)
        Px $x $y $(if ($edge) { C (52, 38, 76) } else { C (118, 92, 158) })
    }
}
# 石板上的符印（一个简化的六边形点阵）
$glyph = @(
    @(8,5), @(6,7), @(10,7), @(7,10), @(9,10), @(8,8)
)
foreach ($p in $glyph) { Px $p[0] $p[1] (C (206, 168, 250)) }
# 高光
Px 5 4 (C (170, 140, 215))
Px 6 4 (C (150, 122, 195))

$g.Dispose()
$bmp.Save((Join-Path $out "AkashicRecord.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"生成 AkashicRecord.png"
