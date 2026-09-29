# 卷轴挂板三件套的贴图（16x16，物品图标）。开发期占位，见 TODO_PLAN.md 美术欠账。
#
# 三块板的区别靠**边框粗细 + 内部留白**表达：
#   小 = 细边、留白少
#   中 = 中边
#   大 = 粗边、留白多（尺寸大，画成物品图标时要靠留白暗示"能挂更大的卷轴"）
Add-Type -AssemblyName System.Drawing

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 16

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }

function New-FrameIcon {
    param([string]$Name, [int]$Border)

    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)

    function Px($x, $y, $c) {
        if ($x -lt 0 -or $x -ge 16 -or $y -lt 0 -or $y -ge 16) { return }
        $b = New-Object System.Drawing.SolidBrush($c)
        $g.FillRectangle($b, $x, $y, 1, 1)
        $b.Dispose()
    }

    $wood = @(122, 92, 58)
    $paper = @(226, 214, 186)

    # 木框
    for ($y = 1; $y -le 14; $y++) {
        for ($x = 1; $x -le 14; $x++) {
            $edge = ($x -lt 1 + $Border -or $x -gt 14 - $Border -or $y -lt 1 + $Border -or $y -gt 14 - $Border)
            if ($edge) {
                # 左上亮、右下暗
                $d = 0
                if ($x -lt 1 + $Border -or $y -lt 1 + $Border) { $d = 26 } else { $d = -22 }
                Px $x $y ([System.Drawing.Color]::FromArgb(255, (Clamp255 ($wood[0] + $d)), (Clamp255 ($wood[1] + $d)), (Clamp255 ($wood[2] + $d))))
            }
            else {
                Px $x $y ([System.Drawing.Color]::FromArgb(255, $paper[0], $paper[1], $paper[2]))
            }
        }
    }

    # 描边
    $outline = [System.Drawing.Color]::FromArgb(255, 40, 30, 20)
    foreach ($y in 0..15) { Px $y 0 $outline; Px $y 15 $outline }
    foreach ($x in 0..15) { Px 0 $x $outline; Px 15 $x $outline }

    # 中间一道简化的符印刻痕（暗示"这里能挂图案"）
    Px 6 7 ([System.Drawing.Color]::FromArgb(255, 150, 130, 190))
    Px 7 7 ([System.Drawing.Color]::FromArgb(255, 150, 130, 190))
    Px 8 8 ([System.Drawing.Color]::FromArgb(255, 150, 130, 190))
    Px 7 9 ([System.Drawing.Color]::FromArgb(255, 150, 130, 190))
    Px 6 10 ([System.Drawing.Color]::FromArgb(255, 150, 130, 190))

    $g.Dispose()
    $bmp.Save((Join-Path $out $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "生成 $Name"
}

New-FrameIcon "WallScrollFrameSmall.png" 2
New-FrameIcon "WallScrollFrameMedium.png" 3
New-FrameIcon "WallScrollFrameLarge.png" 4
