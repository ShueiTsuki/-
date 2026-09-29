# 把导出的原版 UI 贴图拼成一张带编号的对照图，供肉眼比对。
#
# 为什么要拼：`read_image` 一次只能看一张，而这些贴图是**一套**（面板底/边框/物品格的
# 背/面/覆盖/选中态）—— 拆开看会丢掉它们之间的关系（比如 slot 的 Front 是叠在 Back 上的）。
#
# 用法：pwsh -File _tools\ui_sheet.ps1
param(
    [string]$Dir = 'D:\DeepSeekHarness\tmod\_tools\vanilla_ui',
    [int]$Scale = 3,
    [string]$Out = 'D:\DeepSeekHarness\tmod\_tools\vanilla_ui_sheet.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 顺序即编号 1..N；挑的是做面板/物品格/按钮最相关的那一套
$names = @(
    'UI_PanelBackground', 'UI_PanelBorder', 'UI_InnerPanelBackground', 'UI_ButtonBacking',
    'UI_Bestiary_Slot_Back', 'UI_Bestiary_Slot_Front', 'UI_Bestiary_Slot_Overlay', 'UI_Bestiary_Slot_Selection',
    'UI_CharCreation_SmallPanel', 'UI_CharCreation_SmallPanelBorder', 'UI_Settings_Panel', 'UI_Settings_Panel_2',
    'UI_ChestCraft_0', 'UI_ChestCraft_1', 'UI_Craft', 'UI_Craft_Toggle_0',
    'UI_Scrollbar', 'UI_ScrollbarInner', 'UI_ButtonPlay', 'UI_ButtonDelete',
    'UI_Bestiary_Stat_Panel', 'UI_Bestiary_Button_Border', 'UI_Bestiary_Button_Back', 'UI_Bestiary_Button_Forward'
)

$cols = 4
$cellW = 96; $cellH = 72; $pad = 6
$rows = [int][math]::Ceiling($names.Count / $cols)
$W = $cols * ($cellW + $pad) + $pad
$H = $rows * ($cellH + $pad) + $pad

$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 26, 32, 44))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$font = New-Object System.Drawing.Font('Consolas', 8)
$brush = [System.Drawing.Brushes]::White
$dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 170, 200))
$cellPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 120, 170, 220), 1)

for ($i = 0; $i -lt $names.Count; $i++) {
    $cx = $pad + (($i % $cols) * ($cellW + $pad))
    $cy = $pad + ([int][math]::Floor($i / $cols) * ($cellH + $pad))
    $g.DrawRectangle($cellPen, $cx, $cy, $cellW, $cellH)
    $g.DrawString(($i + 1).ToString(), $font, $brush, $cx + 2, $cy + 1)

    $p = Join-Path $Dir ($names[$i] + '.png')
    if (-not (Test-Path $p)) { $g.DrawString('(缺)', $font, $dim, $cx + 20, $cy + 30); continue }

    $src = [System.Drawing.Image]::FromFile($p)
    try {
        $dw = $src.Width * $Scale
        $dh = $src.Height * $Scale
        # 格子放不下就按比例缩到格子里
        $maxW = $cellW - 6; $maxH = $cellH - 16
        if ($dw -gt $maxW) { $k = $maxW / $dw; $dw = [int]($dw * $k); $dh = [int]($dh * $k) }
        if ($dh -gt $maxH) { $k = $maxH / $dh; $dw = [int]($dw * $k); $dh = [int]($dh * $k) }

        $tx = $cx + [int](($cellW - $dw) / 2)
        $ty = $cy + 14 + [int](($maxH - $dh) / 2)
        $g.DrawImage($src, (New-Object System.Drawing.Rectangle($tx, $ty, $dw, $dh)))

        $g.DrawString("$($src.Width)x$($src.Height)", $font, $dim, $cx + 2, $cy + $cellH - 12)
    }
    finally { $src.Dispose() }
}

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "-> $Out  ($W x $H，$($names.Count) 张，放大 ${Scale}x)"
Write-Host '编号顺序：'
for ($i = 0; $i -lt $names.Count; $i++) { Write-Host ("  {0,2}. {1}" -f ($i + 1), $names[$i]) }
