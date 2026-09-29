<#
.SYNOPSIS
    生成媒质瓶（MediaFlask）贴图。
.DESCRIPTION
    泰拉瑞亚物品贴图用 32x32（内部 2x 放大）。本图按原项目紫水晶配色绘制：
    玻璃瓶 + 紫色媒质液 + 深色瓶塞。配色取自 hexcasting 的 charged_amethyst.png（#b38ef3 系）。
#>

Add-Type -AssemblyName System.Drawing

$size = 32
$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items\MediaFlask.png"

$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D]::SmoothingMode.None
$g.Clear([System.Drawing.Color]::Transparent)

# ---- 配色（取自原作紫水晶系）----
$glassLight = [System.Drawing.Color]::FromArgb(255, 226, 210, 246)
$glassDark  = [System.Drawing.Color]::FromArgb(255, 120, 92, 168)
$liquidTop  = [System.Drawing.Color]::FromArgb(255, 200, 160, 245)
$liquidBot  = [System.Drawing.Color]::FromArgb(255, 124, 72, 190)
$cork       = [System.Drawing.Color]::FromArgb(255, 132, 96, 62)
$corkDark   = [System.Drawing.Color]::FromArgb(255, 92, 64, 40)
$shine      = [System.Drawing.Color]::FromArgb(200, 255, 250, 255)

$brushGlassLight = New-Object System.Drawing.SolidBrush($glassLight)
$brushGlassDark  = New-Object System.Drawing.SolidBrush($glassDark)
$brushCork       = New-Object System.Drawing.SolidBrush($cork)
$brushCorkDark   = New-Object System.Drawing.SolidBrush($corkDark)
$brushShine      = New-Object System.Drawing.SolidBrush($shine)
$brushLiquidTop  = New-Object System.Drawing.SolidBrush($liquidTop)

# ---- 瓶塞（顶部）----
$g.FillRectangle($brushCorkDark, 13, 2, 6, 5)
$g.FillRectangle($brushCork, 14, 3, 4, 4)

# ---- 瓶颈 ----
$g.FillRectangle($brushGlassDark, 13, 7, 6, 3)
$g.FillRectangle($brushGlassLight, 14, 7, 2, 3)

# ---- 瓶身 ----
$bodyTop = 10
$bodyH = 18
$g.FillRectangle($brushGlassDark, 10, $bodyTop, 12, $bodyH)

# ---- 媒质液体（下半部分，带渐变感）----
for ($y = $bodyTop + 6; $y -lt $bodyTop + $bodyH - 1; $y++) {
    $t = ($y - ($bodyTop + 6)) / [double]($bodyH - 7)
    $r = [int](200 + (124 - 200) * $t)
    $gg = [int](160 + (72 - 160) * $t)
    $b = [int](245 + (190 - 245) * $t)
    $pen = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, $r, $gg, $b))
    $g.FillRectangle($pen, 11, $y, 10, 1)
    $pen.Dispose()
}

# ---- 液面高光 ----
$g.FillRectangle($brushLiquidTop, 11, $bodyTop + 5, 10, 1)

# ---- 玻璃高光（左侧竖条 + 顶部反光）----
$g.FillRectangle($brushGlassLight, 11, $bodyTop + 1, 2, $bodyH - 3)
$g.FillRectangle($brushShine, 12, $bodyTop + 2, 1, 5)

# ---- 瓶底阴影 ----
$g.FillRectangle($brushGlassDark, 10, $bodyTop + $bodyH - 1, 12, 1)

# ---- 清理 ----
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

foreach ($b in @($brushGlassLight, $brushGlassDark, $brushCork, $brushCorkDark, $brushShine, $brushLiquidTop)) { $b.Dispose() }

$img = [System.Drawing.Image]::FromFile($out)
"已生成: $out"
"尺寸: $($img.Width)x$($img.Height)   大小: $((Get-Item $out).Length) 字节"
$img.Dispose()
