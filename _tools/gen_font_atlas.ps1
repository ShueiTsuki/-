# 把一套等宽像素字体烘成「位图图集 + 字符宽度表」，供离屏渲染用。
#
# 为什么必须做（而且必须**排在版面之前**）：
#   `OffscreenCanvas.DrawText` 现在画的是**等宽占位方块**，而真字形不是等宽的 ——
#   `i` 窄、`W` 宽。于是现在算出来的折行位置与分页全是错的，等真字上上去还要重调一遍。
#   而且标题与正文的**视觉重量差**用方块根本判断不了，那恰恰是"土不土"的大头。
#
# 为什么用 PowerShell 的 System.Drawing：
#   net10.0 的离屏工程没有字体栅格化能力（System.Drawing.Common 从 .NET 7 起不再随框架分发），
#   而 Windows PowerShell 5.1 自带 .NET Framework，能正常画字。烘成位图之后，
#   C# 侧只查表贴图，不需要任何字体库。这与 gen_*.ps1 的既有做法一致。
#
# ⚠️ 这套字形**不是泰拉原版字体**。它定的是版面与配色；字号字距的最后一轮微调
#    仍需在游戏里对着 FontAssets.MouseText 做一次。
#
# 用法：pwsh -File _tools\gen_font_atlas.ps1
param(
    # ⚠️ 必须是**比例**字体。上一版用 Consolas（等宽），前进宽度全是 7 ——
    # 那和占位方块是一回事，折行位置照样不会变，等于没做。
    # 泰拉的 FontAssets.MouseText 是比例字体，所以这里也要比例字体，版面才对得上。
    [string]$FontName = 'Segoe UI',
    [float]$FontSize = 14,
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = $PSScriptRoot }
Add-Type -AssemblyName System.Drawing

# 逐字栅格化，量出每个字的实际墨迹范围与前进宽度
$bmp = New-Object System.Drawing.Bitmap(64, 64, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
$g.Clear([System.Drawing.Color]::Transparent)

$font = New-Object System.Drawing.Font($FontName, $FontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = [System.Drawing.StringFormat]::GenericTypographic
$fmt.FormatFlags = $fmt.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$chars = @()
for ($c = 32; $c -le 126; $c++) {
    $ch = [char]$c
    $s = [string]$ch
    $sz = $g.MeasureString($s, $font, 1000, $fmt)
    $chars += [pscustomobject]@{
        Code    = $c
        Char    = $ch
        Advance = [int][math]::Ceiling($sz.Width)
        Height  = [int][math]::Ceiling($sz.Height)
    }
}
$g.Dispose(); $bmp.Dispose()

$lineH = ($chars | Measure-Object -Property Height -Maximum).Maximum
$cellW = ($chars | Measure-Object -Property Advance -Maximum).Maximum
Write-Host "字体 $FontName @ ${FontSize}px ：行高 $lineH，最大前进宽度 $cellW"

$cols = 16
$rows = [int][math]::Ceiling($chars.Count / $cols)

# 排成 16 列的网格图集。
# 格子宽度取**该列**的最大前进宽度而不是全局最大 —— 比例字体下全局最大（比如 'W' ）
# 会让每个格子都撑到最宽，图集白白大一圈。
$colW = New-Object int[] $cols
for ($i = 0; $i -lt $chars.Count; $i++) {
    $ci = $i % $cols
    if ($chars[$i].Advance -gt $colW[$ci]) { $colW[$ci] = $chars[$i].Advance }
}
$rowH = New-Object int[] $rows
for ($i = 0; $i -lt $chars.Count; $i++) {
    $ri = [int][math]::Floor($i / $cols)
    if ($chars[$i].Height -gt $rowH[$ri]) { $rowH[$ri] = $chars[$i].Height }
}
$colX = New-Object int[] $cols
$x = 0
for ($c = 0; $c -lt $cols; $c++) { $colX[$c] = $x; $x += $colW[$c] }
$rowY = New-Object int[] $rows
$y = 0
for ($r = 0; $r -lt $rows; $r++) { $rowY[$r] = $y; $y += $rowH[$r] }
$aw = $x
$ah = $y

$atlas = New-Object System.Drawing.Bitmap($aw, $ah, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$ag = [System.Drawing.Graphics]::FromImage($atlas)
$ag.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
$ag.Clear([System.Drawing.Color]::Transparent)
$brush = [System.Drawing.Brushes]::White

$index = @{}
for ($i = 0; $i -lt $chars.Count; $i++) {
    $ci = $i % $cols
    $ri = [int][math]::Floor($i / $cols)
    $cx = $colX[$ci]
    $cy = $rowY[$ri]
    $ag.DrawString([string]$chars[$i].Char, $font, $brush, $cx, $cy, $fmt)
    $index[[string]$chars[$i].Code] = @{
        x = $cx; y = $cy
        w = $colW[$ci]; h = $rowH[$ri]
        adv = $chars[$i].Advance
    }
}
$ag.Dispose(); $font.Dispose()

$png = Join-Path $OutDir 'book_font.png'
$atlas.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
# ⚠️ 这里**不能** Dispose：下面生成检查图还要用它。上一版在这里就释放了，
# 结果预览那步报 "Parameter is not valid." —— 看起来像画图参数错，其实是对象已释放。

# 同时导出裸 RGBA：离屏工程（net10.0）**没有 PNG 解码器**
# （System.Drawing.Common 从 .NET 7 起不再随框架分发），所以解码在 PowerShell 侧做完，
# C# 那边只读字节。书图集也是同一套办法，见 prep_book_atlas.ps1。
$rawBytes = New-Object byte[] ($aw * $ah * 4)
$ri2 = 0
for ($yy = 0; $yy -lt $ah; $yy++) {
    for ($xx = 0; $xx -lt $aw; $xx++) {
        $c = $atlas.GetPixel($xx, $yy)
        $rawBytes[$ri2] = $c.R; $rawBytes[$ri2 + 1] = $c.G; $rawBytes[$ri2 + 2] = $c.B; $rawBytes[$ri2 + 3] = $c.A
        $ri2 += 4
    }
}
$rawPath = Join-Path $OutDir 'book_font.raw'
[System.IO.File]::WriteAllBytes($rawPath, $rawBytes)

# 纯文本宽度表。比让 C# 解 JSON 更省事：一行一个字形，空格分隔，肉眼可查、改起来直观，
# 也不需要引入任何解析库（离屏工程里少一个会出错的地方）。
$tbl = New-Object System.Text.StringBuilder
[void]$tbl.AppendLine("# book_font.txt —— 由 gen_font_atlas.ps1 生成，勿手改")
[void]$tbl.AppendLine("# 格式： code x y w h advance")
[void]$tbl.AppendLine("atlas $aw $ah")
[void]$tbl.AppendLine("lineH $lineH")
foreach ($i in ($index.Keys | Sort-Object { [int]$_ })) {
    $gg = $index[$i]
    [void]$tbl.AppendLine("$i $($gg.x) $($gg.y) $($gg.w) $($gg.h) $($gg.adv)")
}
$tblPath = Join-Path $OutDir 'book_font.txt'
[System.IO.File]::WriteAllText($tblPath, $tbl.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$meta = [ordered]@{
    font     = $FontName
    size     = $FontSize
    cellW    = $cellW
    lineH    = $lineH
    cols     = $cols
    rows     = $rows
    atlas    = 'book_font.png'
    note     = '不是泰拉原版字体；只用于离屏定版面与配色。宽度表键是字符码的十进制字符串。'
    glyphs   = $index
}
$json = Join-Path $OutDir 'book_font.json'
[System.IO.File]::WriteAllText($json, ($meta | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "-> $png  ($aw x $ah)"
Write-Host "-> $json ($((Get-Item $json).Length) B，$($chars.Count) 个字形)"

# ── 深底放大检查图 ─────────────────────────────────────────────────────
# 图集本身是**白字透明底**（绘制时用 tint 乘算上色，这是对的），
# 但那样在白底看图工具里几乎看不见 —— 上一版我打开就是一片白，根本无法核对字形。
# 所以另出一张专供眼睛看的：深底 + 4 倍最近邻 + 每格描边。
$scale = 4
$pad = 2
$pw = ($aw + ($cols * $pad)) * $scale
$ph = ($ah + ($rows * $pad)) * $scale
$pre = New-Object System.Drawing.Bitmap($pw, $ph, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pg = [System.Drawing.Graphics]::FromImage($pre)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 24, 22, 30))
$pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor

$cellPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 120, 200, 255), 1)
for ($i = 0; $i -lt $chars.Count; $i++) {
    $ci = $i % $cols
    $ri = [int][math]::Floor($i / $cols)
    $gx = ($colX[$ci] + ($ci * $pad)) * $scale
    $gy = ($rowY[$ri] + ($ri * $pad)) * $scale
    $gw = $colW[$ci] * $scale
    $gh = $rowH[$ri] * $scale

    $pg.DrawImage($atlas, (New-Object System.Drawing.Rectangle($gx, $gy, $gw, $gh)),
                  (New-Object System.Drawing.Rectangle($colX[$ci], $rowY[$ri], $colW[$ci], $rowH[$ri])),
                  [System.Drawing.GraphicsUnit]::Pixel)
    $pg.DrawRectangle($cellPen, $gx, $gy, $gw - 1, $gh - 1)
}
$pg.Dispose()
$prePath = Join-Path $OutDir 'book_font_preview.png'
$pre.Save($prePath, [System.Drawing.Imaging.ImageFormat]::Png)
$pre.Dispose()
$atlas.Dispose()   # 用完了才释放（检查图已经画完）
Write-Host "-> $prePath  (${scale}x 放大、深底，供眼睛核对字形)"
Write-Host "-> $tblPath  ($((Get-Item $tblPath).Length) B，纯文本宽度表)"
Write-Host "-> $rawPath  ($($rawBytes.Length) B，裸 RGBA)"
