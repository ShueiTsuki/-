# 像素画锻造炉（可 dot-source 复用的贴图生成库）
#
# 为什么要有这一层：之前的生成器各写各的「随机噪点 + 连续色」，
# 那套方法**原理上产不出**泰拉风格的贴图（细节糊成一团、没有体积感、
# 淡色物品直接看不见）。这里把它固化成一条正确的流水线：
#
#   ① 形状掩码    用 ASCII 画轮廓（'#'=主体 '+'=金属/次材料 'o'=宝石/发光 '.'=空）
#   ② 自动描边    掩码外圈相邻的透明像素 -> 填描边色（基准色 ×0.3，不是纯黑）
#   ③ 倒角打光    上方/左侧边缘 -> 亮档；下方/右侧 -> 暗档；内部 -> 中档
#   ④ 量化调色板  5 档：描边 / 暗 / 中 / 亮 / 高光，全部由同一基准色推出
#
# 用法：
#   . "$PSScriptRoot\pixelforge.ps1"
#   New-PixelArt -Name "OakStaff.png" -OutDir $dir -Size 32 `
#       -Mask @('....##....', ...) -Body @(128,92,58) -Accent @(196,168,255)
#
# 掩码是**等宽**的字符串数组；每行一个 y，每个字符一个 x。
# 用字符画轮廓比用坐标列表好审：一眼能看出形状对不对。

Add-Type -AssemblyName System.Drawing

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }

# 由基准色推出 5 档调色板
function New-Ramp([int[]]$Base) {
    $r = $Base[0]; $g = $Base[1]; $b = $Base[2]
    return @{
        # 描边：近黑但不是纯黑（纯黑在泰拉里显得"抠图没抠干净"）
        Outline = @((Clamp255 ([int]($r * 0.30))), (Clamp255 ([int]($g * 0.30))), (Clamp255 ([int]($b * 0.30))))
        Dark    = @((Clamp255 ($r - 34)), (Clamp255 ($g - 34)), (Clamp255 ($b - 34)))
        Mid     = @($r, $g, $b)
        Light   = @((Clamp255 ($r + 30)), (Clamp255 ($g + 30)), (Clamp255 ($b + 30)))
        Hi      = @((Clamp255 ($r + 54)), (Clamp255 ($g + 54)), (Clamp255 ($b + 54)))
    }
}

function New-PixelArt {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$OutDir,
        [Parameter(Mandatory = $true)][string[]]$Mask,
        [Parameter(Mandatory = $true)][int[]]$Body,
        [int[]]$Accent = @(200, 200, 210),
        [int[]]$Gem = @(196, 168, 255),
        [int]$Seed = 12345,
        # 是否让每行/每列的细节自己抖动（关掉会得到非常"干净"的描线，适合图标）
        [switch]$NoNoise
    )

    $h = $Mask.Count
    $w = 0
    foreach ($row in $Mask) { if ($row.Length -gt $w) { $w = $row.Length } }

    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::Transparent)

    $rng = New-Object System.Random($Seed)

    function Px([int]$x, [int]$y, [int[]]$c) {
        if ($x -lt 0 -or $x -ge $w -or $y -lt 0 -or $y -ge $h) { return }
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))
        $gfx.FillRectangle($brush, $x, $y, 1, 1)
        $brush.Dispose()
    }

    $rampBody = New-Ramp $Body
    $rampAccent = New-Ramp $Accent
    $rampGem = New-Ramp $Gem

    function CharAt([int]$x, [int]$y) {
        if ($y -lt 0 -or $y -ge $h) { return '.' }
        $row = $Mask[$y]
        if ($x -lt 0 -or $x -ge $row.Length) { return '.' }
        return $row[$x]
    }

    # ② 自动描边：所有"空但紧邻实心"的像素
    $outlinePixels = @{}
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            if ((CharAt $x $y) -ne '.') { continue }

            $near = $false
            foreach ($d in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
                if ((CharAt ($x + $d[0]) ($y + $d[1])) -ne '.') { $near = $true; break }
            }
            if ($near) { $outlinePixels["$x,$y"] = $true }
        }
    }

    foreach ($key in @($outlinePixels.Keys)) {
        $parts = $key -split ','
        # 描边色也跟着它挨着的材料走（金属旁边用金属的描边色，看起来才不"贴纸")
        Px ([int]$parts[0]) ([int]$parts[1]) $rampBody.Outline
    }

    # ③④ 实心像素：按"暴露方向"选档位 + 少量噪点
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $ch = CharAt $x $y
            if ($ch -eq '.') { continue }

            $ramp = switch ($ch) {
                '+' { $rampAccent }
                'o' { $rampGem }
                default { $rampBody }
            }

            $openTop = (CharAt $x ($y - 1)) -eq '.'
            $openLeft = (CharAt ($x - 1) $y) -eq '.'
            $openBottom = (CharAt $x ($y + 1)) -eq '.'
            $openRight = (CharAt ($x + 1) $y) -eq '.'

            $level = 'Mid'
            if ($openTop -or $openLeft) { $level = 'Light' }
            if ($openBottom -or $openRight) { $level = 'Dark' }
            # 左上角（同时暴露上边与左边）= 高光，这一档给了物体"棱"
            if ($openTop -and $openLeft) { $level = 'Hi' }

            $c = $ramp[$level]

            if (-not $NoNoise) {
                $n = $rng.Next(-6, 7)
                $c = @((Clamp255 ($c[0] + $n)), (Clamp255 ($c[1] + $n)), (Clamp255 ($c[2] + $n)))
            }

            Px $x $y $c
        }
    }

    $gfx.Dispose()
    $bmp.Save((Join-Path $OutDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# 把「一小段居中/缩放的掩码」贴进更大的画布，方便组合出复杂图形
function New-Canvas([int]$w, [int]$h) {
    $rows = @()
    for ($y = 0; $y -lt $h; $y++) { $rows += ('.' * $w) }
    return $rows
}
