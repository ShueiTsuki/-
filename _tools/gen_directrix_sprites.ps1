# 三根导线贴图（16x16）。开发期占位，见 TODO_PLAN.md 美术欠账。
Add-Type -AssemblyName System.Drawing
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16

function Px($g, $x, $y, $c) {
    if ($x -lt 0 -or $x -ge $S -or $y -lt 0 -or $y -ge $S) { return }
    $b = New-Object System.Drawing.SolidBrush($c); $g.FillRectangle($b, $x, $y, 1, 1); $b.Dispose()
}
function C($r, $g2, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g2, $b) }

function New-Directrix($name, $accent, $markKind) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)

    $rng = New-Object System.Random(3131)
    # 石底
    for ($y = 0; $y -lt $S; $y++) {
        for ($x = 0; $x -lt $S; $x++) {
            $n = $rng.Next(14)
            Px $g $x $y (C (84 + $n) (76 + $n) (100 + $n))
        }
    }
    # 横向的传导槽（导线是沿轴传导的，画一条贯穿的槽示意）
    for ($x = 1; $x -le 14; $x++) {
        Px $g $x 7 (C 46 42 58)
        Px $g $x 8 (C 46 42 58)
    }
    # 中央的标记：三种导线不同
    switch ($markKind) {
        "empty" {
            # 一个问号状的两点
            Px $g 6 7 $accent; Px $g 9 7 $accent
        }
        "bool" {
            # 上真下假的两道
            Px $g 5 7 $accent; Px $g 6 7 $accent; Px $g 7 7 $accent
            Px $g 9 8 $accent; Px $g 10 8 $accent; Px $g 11 8 $accent
        }
        "redstone" {
            # 一个实心块
            for ($dx = -2; $dx -le 2; $dx++) { Px $g (8 + $dx) 7 $accent }
        }
    }
    # 上缘高光
    for ($x = 1; $x -le 14; $x++) { Px $g $x 1 (C 118 108 142) }

    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "生成 $name"
}

New-Directrix "HexDirectrixEmpty.png"    (C 200 200 210) "empty"
New-Directrix "HexDirectrixBoolean.png"  (C 150 230 160) "bool"
New-Directrix "HexDirectrixRedstone.png" (C 240 120 110) "redstone"
"完成"
