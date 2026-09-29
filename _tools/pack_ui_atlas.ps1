# 把离屏渲染要用的所有贴图打成一个「命名图集」：裸 RGBA + 名字索引。
#
# 为什么要打包而不是逐个读：
#   离屏工程（net10.0）**没有 PNG 解码器**，所以解码只能在 PowerShell 侧做完
#   （它自带 .NET Framework 的 System.Drawing）。既然都要解码，就顺手拼成一张 ——
#   C# 那边只需要「一张裸图 + 一张名字->矩形 的表」，DrawImage 按名字取。
#
# 打包内容分两类：
#   ① Patchouli 书图集（book_brown.png）—— 整张打包，切片坐标由 Core 侧给
#   ② 原版泰拉 UI 贴图（_tools/vanilla_ui/*.png，由 DevTextureDump 从游戏里导出来的真图）
#
# 用法：pwsh -File _tools\pack_ui_atlas.ps1
param(
    [string]$VanillaDir = 'D:\DeepSeekHarness\tmod\_tools\vanilla_ui',
    [string]$BookPng = 'D:\DeepSeekHarness\patchouli\Xplat\src\main\resources\assets\patchouli\textures\gui\book_brown.png',
    [string]$OutDir = 'D:\DeepSeekHarness\tmod\_tools',
    [int]$MaxWidth = 1024
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 逻辑名 -> 来源文件。Core/ 侧的皮肤只认左边的名字。
$map = [ordered]@{
    'book'           = $BookPng
    'bookmark'       = $BookPng      # 与 book 同一张，切片坐标不同
    'buttons'        = $BookPng
    'panel_bg'       = Join-Path $VanillaDir 'UI_PanelBackground.png'
    'panel_border'   = Join-Path $VanillaDir 'UI_PanelBorder.png'
    'panel_inner'    = Join-Path $VanillaDir 'UI_InnerPanelBackground.png'
    'button_backing' = Join-Path $VanillaDir 'UI_ButtonBacking.png'
    'slot_back'      = Join-Path $VanillaDir 'UI_Bestiary_Slot_Back.png'
    'slot_front'     = Join-Path $VanillaDir 'UI_Bestiary_Slot_Front.png'
    'slot_overlay'   = Join-Path $VanillaDir 'UI_Bestiary_Slot_Overlay.png'
    'slot_selection' = Join-Path $VanillaDir 'UI_Bestiary_Slot_Selection.png'
}

# ① 先量尺寸，决定画布多大（简单的行式排布）
$items = @()
foreach ($k in $map.Keys) {
    $p = $map[$k]
    if (-not (Test-Path $p)) { Write-Host "跳过（不存在）: $k <- $p"; continue }
    $im = [System.Drawing.Image]::FromFile($p)
    try { $items += [pscustomobject]@{ Name = $k; Path = $p; W = $im.Width; H = $im.Height } }
    finally { $im.Dispose() }
}
if ($items.Count -eq 0) { throw '没有任何可用贴图' }

$pad = 2
$x = 0; $y = 0; $rowH = 0
$placed = @()
foreach ($it in $items) {
    if ($x + $it.W -gt $MaxWidth -and $x -gt 0) { $x = 0; $y += $rowH + $pad; $rowH = 0 }
    $placed += [pscustomobject]@{ Name = $it.Name; Path = $it.Path; X = $x; Y = $y; W = $it.W; H = $it.H }
    $x += $it.W + $pad
    if ($it.H -gt $rowH) { $rowH = $it.H }
}
$W = [int][math]::Min($MaxWidth, ($placed | ForEach-Object { $_.X + $_.W } | Measure-Object -Maximum).Maximum)
$H = $y + $rowH

# ② 画进一张画布
$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
foreach ($p in $placed) {
    $im = [System.Drawing.Image]::FromFile($p.Path)
    try { $g.DrawImage($im, $p.X, $p.Y, $p.W, $p.H) } finally { $im.Dispose() }
}
$g.Dispose()

# ③ 输出裸 RGBA（直通 alpha）
$bytes = New-Object byte[] ($W * $H * 4)
$i = 0
for ($yy = 0; $yy -lt $H; $yy++) {
    for ($xx = 0; $xx -lt $W; $xx++) {
        $c = $bmp.GetPixel($xx, $yy)
        $bytes[$i] = $c.R; $bytes[$i+1] = $c.G; $bytes[$i+2] = $c.B; $bytes[$i+3] = $c.A
        $i += 4
    }
}
$bmp.Dispose()

$raw = Join-Path $OutDir 'ui_atlas.raw'
[System.IO.File]::WriteAllBytes($raw, $bytes)

# ④ 名字索引（纯文本，C# 直接 split 就能读，不引解析库）
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# ui_atlas.txt —— 由 pack_ui_atlas.ps1 生成，勿手改")
[void]$sb.AppendLine("# 格式： name x y w h")
[void]$sb.AppendLine("atlas $W $H")
foreach ($p in $placed) { [void]$sb.AppendLine("$($p.Name) $($p.X) $($p.Y) $($p.W) $($p.H)") }
$txt = Join-Path $OutDir 'ui_atlas.txt'
[System.IO.File]::WriteAllText($txt, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "-> $raw  ($W x $H, $([math]::Round($bytes.Length/1KB)) KB)"
Write-Host "-> $txt  ($($placed.Count) 张贴图)"
foreach ($p in $placed) { Write-Host ("   {0,-16} {1,4}x{2,-4} @ {3},{4}" -f $p.Name, $p.W, $p.H, $p.X, $p.Y) }
