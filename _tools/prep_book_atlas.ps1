# 把 Patchouli 的书本图集预处理成离屏渲染能直接吃的格式。
#
# 为什么需要它：离屏出图跑在 `drawtest`（net10.0 控制台），那里**没有 PNG 解码器**
# （`System.Drawing.Common` 从 .NET 7 起不再随框架分发，离线 restore 不可靠；
#   我自己只写了 PNG 编码器）。而 Windows PowerShell 5.1 自带 .NET Framework，
# `System.Drawing` 能正常解 PNG —— 所以让 PowerShell 解码、写成裸 RGBA，
# C# 侧只读字节，不需要任何解码库。
#
# 运行时（游戏内）不走这条路：tModLoader 自己会加载 PNG。
# 这条只服务离屏出图。
#
# 来源：`D:\DeepSeekHarness\patchouli`（VazkiiMods/Patchouli，CC BY-NC-SA 3.0）
#       —— 署名 / 禁商用 / 相同方式共享，见 CREDITS.md。
param(
    [string]$Src = 'D:\DeepSeekHarness\patchouli\Xplat\src\main\resources\assets\patchouli\textures\gui',
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = $PSScriptRoot }

Add-Type -AssemblyName System.Drawing

# 书体切片。**这些数字是用 `_tools/zoom_atlas.ps1` 放大叠网格读出来的**，不是估的。
#
# 上一轮凭缩略图估的 290 / 140 / 30 全都不对：290 把右侧那撮描金构件的左边切了进来，
# (140,180,110,36) 和 (0,178,140,30) 量到的是别的东西。**图集切片必须放大到像素级再读。**
#
# 读数依据（整张 2 倍 + 每 16 源像素一格，见 atlas_zoom_full.png）：
#   书体本身        x 4..276  y 4..185      —— 右边收到 276 是为了避开 277 起的描金构件
#   纸面（书内）     x 22..262 y 18..170      —— 绝对坐标；相对书体是 (18,14)-(258,166)
#   中缝            x ≈ 145
#   书签飘带        x 0..135  y 188..205
#   圆点按钮一簇     x 135..175 y 195..205
#   页角装饰        x 258..300 y 170..200
#   描金构件        x 277..345 y 0..85
#   空白页填充      x 408..512 y 150..250
$slices = [ordered]@{
    book       = @(4,   4,   272, 181)   # 只含书体像素；右边界刻意收在描金构件之前
    paper      = @(22,  18,  240, 152)   # 纸面（皮边之内）
    spine      = @(145, 18,  2,   152)   # 中缝
    bookmark   = @(0,   188, 135, 18)    # 书签飘带
    buttons    = @(135, 195, 40,  10)    # 圆点按钮
    ornaments  = @(277, 0,   68,  85)    # 描金小构件
    pagefiller = @(408, 150, 104, 100)   # 空白页填充
    bookcorner = @(258, 170, 42,  30)    # 页角装饰
}

foreach ($colour in 'brown', 'blue', 'purple') {
    $png = Join-Path $Src "book_$colour.png"
    if (-not (Test-Path $png)) { Write-Host "跳过（不存在）：$png"; continue }

    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $w = $bmp.Width; $h = $bmp.Height

        # 取成直通 alpha 的 RGBA（Bitmap 默认可能是预乘，所以自己逐像素读）
        $bytes = New-Object byte[] ($w * $h * 4)
        $i = 0
        for ($y = 0; $y -lt $h; $y++) {
            for ($x = 0; $x -lt $w; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bytes[$i]   = $c.R
                $bytes[$i+1] = $c.G
                $bytes[$i+2] = $c.B
                $bytes[$i+3] = $c.A
                $i += 4
            }
        }

        $raw = Join-Path $OutDir "book_$colour.raw"
        [System.IO.File]::WriteAllBytes($raw, $bytes)

        $meta = [ordered]@{ width = $w; height = $h; raw = "book_$colour.raw"; slices = $slices }
        $json = $meta | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText((Join-Path $OutDir "book_$colour.json"), $json,
            (New-Object System.Text.UTF8Encoding($false)))

        Write-Host ("{0,-18} {1}x{2} -> {3} ({4} KB) + json" -f "book_$colour.png", $w, $h,
            (Split-Path $raw -Leaf), [math]::Round($bytes.Length / 1KB))
    }
    finally { $bmp.Dispose() }
}

Write-Host ''
Write-Host '切片初值（照图看的，出图后按效果微调）：'
foreach ($k in $slices.Keys) {
    Write-Host ("  {0,-12} x={1,-4} y={2,-4} w={3,-4} h={4}" -f $k, $slices[$k][0], $slices[$k][1], $slices[$k][2], $slices[$k][3])
}
