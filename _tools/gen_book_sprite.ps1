# 咒法学之书的贴图（20x20）。开发期占位，见 TODO_PLAN.md 美术欠账。
Add-Type -AssemblyName System.Drawing

$dir = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 20
$bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.Clear([System.Drawing.Color]::Transparent)

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }
function Px($x, $y, $r, $g, $b) {
    if ($x -lt 0 -or $x -ge 20 -or $y -lt 0 -or $y -ge 20) { return }
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, $r, $g, $b))
    $gfx.FillRectangle($brush, $x, $y, 1, 1)
    $brush.Dispose()
}

$rng = New-Object System.Random(20240913)

# ① 形状掩码：一本立着的厚书（封面 + 书脊 + 书页）
$mask = @{}
for ($y = 2; $y -le 17; $y++) {
    for ($x = 3; $x -le 16; $x++) { $mask["$x,$y"] = $true }
}
# 书页：右侧露出的白边
for ($y = 3; $y -le 16; $y++) { $mask["17,$y"] = $true }

# ② 打光 + 噪点：封面紫，书脊更深，书页米色
foreach ($key in @($mask.Keys)) {
    $x = [int]($key -split ',')[0]
    $y = [int]($key -split ',')[1]
    $n = $rng.Next(-7, 8)

    if ($x -ge 17) {
        # 书页
        Px $x $y (Clamp255 (222 + $n)) (Clamp255 (212 + $n)) (Clamp255 (186 + $n))
    }
    elseif ($x -le 5) {
        # 书脊：更暗
        Px $x $y (Clamp255 (72 + $n)) (Clamp255 (54 + $n)) (Clamp255 (100 + $n))
    }
    else {
        # 封面：按上下渐变（上亮下暗）
        $d = [int]((8 - $y) * 2)
        Px $x $y (Clamp255 (128 + $d + $n)) (Clamp255 (98 + $d + $n)) (Clamp255 (186 + $d + $n))
    }
}

# ③ 封面上一枚符印（三条刻痕）
foreach ($p in @(@(9, 6), @(10, 6), @(11, 6), @(10, 8), @(11, 8), @(12, 8), @(9, 10), @(10, 10), @(11, 10))) {
    Px $p[0] $p[1] 236 226 255
}

# ④ 描边
foreach ($key in @($mask.Keys)) {
    $x = [int]($key -split ',')[0]
    $y = [int]($key -split ',')[1]
    foreach ($dd in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
        $nx = $x + $dd[0]; $ny = $y + $dd[1]
        if (-not $mask.ContainsKey("$nx,$ny")) { Px $nx $ny 40 32 56 }
    }
}

$gfx.Dispose()
$bmp.Save((Join-Path $dir "HexBook.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"生成 HexBook.png"
