# 生成 M-1 四个媒质材料的贴图。32x32，与物品类同路径同名。
# 用程序生成而不是找素材：这几个是自创物品，没有现成美术资源。
Add-Type -AssemblyName System.Drawing

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 32

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Save-Bmp($bmp, $name) {
    $p = Join-Path $out $name
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "生成 $name"
}

# ---------- 1) 紫水晶粉：一堆散落的小颗粒 ----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
$rng = New-Object System.Random(1001)
for ($i = 0; $i -lt 46; $i++) {
    $x = 6 + $rng.Next(20); $y = 10 + $rng.Next(16)
    # 越靠下颗粒越密（像一堆粉）
    if ($y -lt 16 -and $rng.Next(2) -eq 0) { continue }
    $sz = 2 + $rng.Next(2)
    $c = [System.Drawing.Color]::FromArgb(255, 168 + $rng.Next(40), 120 + $rng.Next(50), 220 + $rng.Next(30))
    $b = New-Object System.Drawing.SolidBrush($c)
    $g.FillEllipse($b, $x, $y, $sz, $sz)
    $b.Dispose()
}
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 120, 70, 180), 1)
$g.DrawEllipse($pen, 5, 9, 22, 18)
$pen.Dispose(); $g.Dispose()
Save-Bmp $bmp "AmethystDust.png"

# ---------- 2) 紫水晶碎片：棱角碎片 ----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
$shard = @(
    (New-Object System.Drawing.Point(16, 3)),
    (New-Object System.Drawing.Point(24, 16)),
    (New-Object System.Drawing.Point(19, 29)),
    (New-Object System.Drawing.Point(12, 27)),
    (New-Object System.Drawing.Point(8, 15))
)
$fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 158, 100, 214))
$g.FillPolygon($fill, $shard)
$fill.Dispose()
# 高光面
$hi = @(
    (New-Object System.Drawing.Point(16, 3)),
    (New-Object System.Drawing.Point(20, 16)),
    (New-Object System.Drawing.Point(12, 27)),
    (New-Object System.Drawing.Point(8, 15))
)
$hf = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 205, 160, 245))
$g.FillPolygon($hf, $hi)
$hf.Dispose()
$op = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 96, 50, 150), 1)
$g.DrawPolygon($op, $shard)
$op.Dispose(); $g.Dispose()
Save-Bmp $bmp "AmethystShard.png"

# ---------- 3) 充能紫水晶：晶体 + 内核发光 ----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
# 外圈柔光
for ($rad = 13; $rad -ge 6; $rad -= 1) {
    $a = [int](10 + (13 - $rad) * 6)
    $glow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($a, 190, 140, 255))
    $g.FillEllipse($glow, 16 - $rad, 16 - $rad, $rad * 2, $rad * 2)
    $glow.Dispose()
}
$crystal = @(
    (New-Object System.Drawing.Point(16, 4)),
    (New-Object System.Drawing.Point(22, 12)),
    (New-Object System.Drawing.Point(20, 24)),
    (New-Object System.Drawing.Point(12, 26)),
    (New-Object System.Drawing.Point(9, 14)),
    (New-Object System.Drawing.Point(12, 8))
)
$cf = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 176, 120, 235))
$g.FillPolygon($cf, $crystal)
$cf.Dispose()
# 内核
$core = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 230, 255))
$g.FillEllipse($core, 12, 12, 8, 8)
$core.Dispose()
$op = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 110, 60, 175), 1)
$g.DrawPolygon($op, $crystal)
$op.Dispose(); $g.Dispose()
Save-Bmp $bmp "ChargedAmethyst.png"

# ---------- 4) 淬灵晶碎片：苍白偏青的碎片 + 冷光 ----------
$r = New-Canvas; $bmp = $r[0]; $g = $r[1]
for ($rad = 12; $rad -ge 6; $rad -= 1) {
    $a = [int](8 + (12 - $rad) * 5)
    $glow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($a, 150, 235, 235))
    $g.FillEllipse($glow, 16 - $rad, 16 - $rad, $rad * 2, $rad * 2)
    $glow.Dispose()
}
$q = @(
    (New-Object System.Drawing.Point(16, 3)),
    (New-Object System.Drawing.Point(25, 13)),
    (New-Object System.Drawing.Point(21, 28)),
    (New-Object System.Drawing.Point(12, 28)),
    (New-Object System.Drawing.Point(7, 13))
)
$qf = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 178, 232, 232))
$g.FillPolygon($qf, $q)
$qf.Dispose()
$qhi = @(
    (New-Object System.Drawing.Point(16, 3)),
    (New-Object System.Drawing.Point(21, 13)),
    (New-Object System.Drawing.Point(12, 28)),
    (New-Object System.Drawing.Point(7, 13))
)
$qhf = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 225, 252, 252))
$g.FillPolygon($qhf, $qhi)
$qhf.Dispose()
$qp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 90, 165, 170), 1)
$g.DrawPolygon($qp, $q)
$qp.Dispose(); $g.Dispose()
Save-Bmp $bmp "QuenchedAllayShard.png"

"完成"
