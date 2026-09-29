# 石板贴图（16x16）。开发期占位，见 TODO_PLAN.md 美术欠账。
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

$rng = New-Object System.Random(7311)
# 深灰紫石底（低饱和：这个方块不参与油漆，但保持与其它石制品一致）
for ($y = 0; $y -lt $S; $y++) {
    for ($x = 0; $x -lt $S; $x++) {
        $n = $rng.Next(16)
        Px $x $y (C (78 + $n) (74 + $n) (92 + $n))
    }
}
# 内嵌一块更亮的板面，表示这是「刻了东西的石板」
for ($y = 2; $y -le 13; $y++) {
    for ($x = 2; $x -le 13; $x++) {
        $edge = ($x -eq 2 -or $x -eq 13 -or $y -eq 2 -or $y -eq 13)
        Px $x $y $(if ($edge) { C (52, 48, 64) } else { C (108, 100, 130) })
    }
}
# 板面上一个简化的符印（三道刻痕）
Px 5 6 (C 190 172 230); Px 6 6 (C 190 172 230); Px 7 6 (C 190 172 230)
Px 8 8 (C 190 172 230); Px 9 8 (C 190 172 230)
Px 5 10 (C 190 172 230); Px 6 10 (C 190 172 230)
# 上缘高光
for ($x = 2; $x -le 13; $x++) { Px $x 2 (C 132, 122, 158) }

$g.Dispose()
$bmp.Save((Join-Path $out "HexSlate.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"生成 HexSlate.png"
