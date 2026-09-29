# 原动力贴图（16x16）。开发期占位，见 TODO_PLAN.md 美术欠账。
Add-Type -AssemblyName System.Drawing
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16
$bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.ImageFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)

function Px($x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c); $g.FillRectangle($b, $x, $y, 1, 1); $b.Dispose()
}
function C($r, $g2, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g2, $b) }

$rng = New-Object System.Random(9921)
# 比石板更亮的紫石底 —— 原动力是环的核心，要显眼
for ($y = 0; $y -lt $S; $y++) {
    for ($x = 0; $x -lt $S; $x++) {
        $n = $rng.Next(18)
        Px $x $y (C (104 + $n) (62 + $n) (128 + $n))
    }
}
# 中央的「核心」（菱形，带高光）
for ($dy = -5; $dy -le 5; $dy++) {
    $w = 5 - [Math]::Abs($dy)
    for ($dx = -$w; $dx -le $w; $dx++) {
        $c = if ($dx -le 0 -and $dy -le 0) { C (232 200 255) } else { C (176 118 226) }
        Px (8 + $dx) (8 + $dy) $c
    }
}
# 四角的小点，暗示四个控制流方向
foreach ($p in @(@(2,2), @(13,2), @(2,13), @(13,13))) {
    Px $p[0] $p[1] (C 210 170 250)
}
# 上缘高光
for ($x = 3; $x -le 12; $x++) { Px $x 1 (C 150 106 186) }

$g.Dispose()
$bmp.Save((Join-Path $out "HexImpetus.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"生成 HexImpetus.png"
