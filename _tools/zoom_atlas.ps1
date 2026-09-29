# 把 Patchouli 图集放大并叠上坐标网格，用来**读出**精确的切片坐标。
#
# 为什么需要它：切片坐标不能凭缩略图估。上一轮我按缩放后的图估了 290 / 286 / 140
# 这些数字，结果把隔壁那撮描金构件切进来一条、又把书签飘带量错了 30px。
# 误差几像素到几十像素，而**看图一眼就能看出来、算却算不出来**。
#
# 有了这个工具，坐标是照着网格**读**出来的，不是估的。
#
# 用法：
#   .\zoom_atlas.ps1                                  # 整张 2 倍 + 每 16 源像素一格
#   .\zoom_atlas.ps1 -Region 0,170,300,60 -Scale 6     # 只看底部那一条，放大 6 倍
param(
    [string]$Src = 'D:\DeepSeekHarness\patchouli\Xplat\src\main\resources\assets\patchouli\textures\gui\book_brown.png',
    [int[]]$Region = @(),          # x, y, w, h（源图像素）。留空 = 整张
    [int]$Scale = 2,
    [int]$Grid = 16,               # 网格间距（源图像素）
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Out) {
    if ($Region.Count -eq 4) { $Out = Join-Path $PSScriptRoot "atlas_zoom_$($Region[0])_$($Region[1]).png" }
    else { $Out = Join-Path $PSScriptRoot 'atlas_zoom_full.png' }
}

# ⚠️ 局部变量**不能**叫 $src：PowerShell 变量名大小写不敏感，它会和参数 $Src 是同一个变量。
# 一旦 FromFile 抛错，finally 里就会对一个字符串调 Dispose()，把真正的错误盖掉
# （报的是"String 没有 Dispose 方法"，看起来完全不相干）。
try { $bmpSrc = [System.Drawing.Bitmap]::FromFile($Src) }
catch { throw "打不开图集：$Src`n$($_.Exception.Message)" }
try {
    if ($Region.Count -eq 4) { $rx = $Region[0]; $ry = $Region[1]; $rw = $Region[2]; $rh = $Region[3] }
    else { $rx = 0; $ry = 0; $rw = $bmpSrc.Width; $rh = $bmpSrc.Height }

    $dw = $rw * $Scale
    $dh = $rh * $Scale
    $bmp = New-Object System.Drawing.Bitmap($dw, $dh, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($bmpSrc, (New-Object System.Drawing.Rectangle(0, 0, $dw, $dh)),
                 (New-Object System.Drawing.Rectangle($rx, $ry, $rw, $rh)),
                 [System.Drawing.GraphicsUnit]::Pixel)

    # 网格：细线每 $Grid 源像素，粗线每 64 源像素（当锚点）
    $fine = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 0, 200, 255), 1)
    $bold = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 0, 200), 1)
    $font = New-Object System.Drawing.Font('Consolas', 9)
    $brush = [System.Drawing.Brushes]::Magenta

    for ($sx = 0; $sx -le $rw; $sx += $Grid) {
        $abs = $rx + $sx
        $pen = if ($abs % 64 -eq 0) { $bold } else { $fine }
        $x = $sx * $Scale
        $g.DrawLine($pen, $x, 0, $x, $dh)
        if ($abs % 64 -eq 0) { $g.DrawString("$abs", $font, $brush, $x + 2, 1) }
    }
    for ($sy = 0; $sy -le $rh; $sy += $Grid) {
        $abs = $ry + $sy
        $pen = if ($abs % 64 -eq 0) { $bold } else { $fine }
        $y = $sy * $Scale
        $g.DrawLine($pen, 0, $y, $dw, $y)
        if ($abs % 64 -eq 0) { $g.DrawString("$abs", $font, $brush, 1, $y + 1) }
    }

    $g.Dispose()
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "-> $Out   ($dw x $dh，源区域 $rx,$ry ${rw}x${rh}，放大 ${Scale}x，网格 ${Grid}px)"
}
finally { if ($bmpSrc -is [System.Drawing.Bitmap]) { $bmpSrc.Dispose() } }
