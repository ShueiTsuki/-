# 打包法术三件套的贴图（20x20，物品图标）。开发期占位，见 TODO_PLAN.md 美术欠账。
#
# 三件东西的**轮廓必须能区分**（缩小到 50% 也要能认出）：
#   符纸   = 窄长条 + 一道折角
#   饰品   = 圆环（挂着的东西）
#   法器   = 宽底座 + 顶部尖（更"重"的形体）
Add-Type -AssemblyName System.Drawing

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 20

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }

function New-Icon {
    param([string]$Name, [scriptblock]$Draw, [int]$BaseR, [int]$BaseG, [int]$BaseB)

    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)

    $script:g = $g
    $script:rng = New-Object System.Random(4242)

    function Px($x, $y, $c) {
        if ($x -lt 0 -or $x -ge 20 -or $y -lt 0 -or $y -ge 20) { return }
        $b = New-Object System.Drawing.SolidBrush($c)
        $script:g.FillRectangle($b, $x, $y, 1, 1)
        $b.Dispose()
    }

    # ① 形状掩码：先用块状形体画出剪影，再加细节
    $mask = @{}
    & $Draw

    # ② 自动描边 + ③ 倒角打光（左上亮、右下暗）
    foreach ($key in @($mask.Keys)) {
        $x = [int]($key -split ',')[0]
        $y = [int]($key -split ',')[1]
        $n = $script:rng.Next(-8, 9)

        $isTopLeft = (-not $mask.ContainsKey("$($x-1),$y")) -or (-not $mask.ContainsKey("$x,$($y-1)"))
        $isBottomRight = (-not $mask.ContainsKey("$($x+1),$y")) -or (-not $mask.ContainsKey("$x,$($y+1)"))

        $r = $BaseR; $gg = $BaseG; $b = $BaseB
        if ($isTopLeft) { $r += 38; $gg += 38; $b += 38 }
        elseif ($isBottomRight) { $r -= 30; $gg -= 30; $b -= 30 }

        Px $x $y ([System.Drawing.Color]::FromArgb(255, (Clamp255 ($r + $n)), (Clamp255 ($gg + $n)), (Clamp255 ($b + $n))))
    }

    # 描边：掩码外侧一圈填深色（泰拉的约定：近黑但非纯黑）
    $outline = [System.Drawing.Color]::FromArgb(255, [int]($BaseR * 0.3), [int]($BaseG * 0.3), [int]($BaseB * 0.3))
    foreach ($key in @($mask.Keys)) {
        $x = [int]($key -split ',')[0]
        $y = [int]($key -split ',')[1]
        foreach ($d in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
            $nx = $x + $d[0]; $ny = $y + $d[1]
            if (-not $mask.ContainsKey("$nx,$ny")) { Px $nx $ny $outline }
        }
    }

    $g.Dispose()
    $bmp.Save((Join-Path $out $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "生成 $Name"
}

# 符纸：窄长条，右上角折起
New-Icon "Cypher.png" {
    for ($y = 2; $y -le 17; $y++) {
        for ($x = 6; $x -le 13; $x++) { $mask["$x,$y"] = $true }
    }
    # 折角：右上角削掉
    foreach ($p in @('13,2', '13,3', '12,2')) { $mask.Remove($p) }
    # 中间一道笔画的缺口，看起来像写了字
    $mask.Remove('9,7'); $mask.Remove('10,8'); $mask.Remove('9,11')
} 232 220 190

# 饰品：圆环 + 一颗芯
New-Icon "Trinket.png" {
    for ($y = 3; $y -le 16; $y++) {
        for ($x = 3; $x -le 16; $x++) {
            $dx = $x - 9.5; $dy = $y - 9.5
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($d -ge 4.5 -and $d -le 6.5) { $mask["$x,$y"] = $true }
        }
    }
    # 环内悬浮的核
    foreach ($p in @('9,9', '10,9', '9,10', '10,10')) { $mask[$p] = $true }
} 214 186 120

# 法器：宽底座 + 顶部尖
New-Icon "Artifact.png" {
    for ($y = 12; $y -le 17; $y++) {
        $half = 6 - ($y - 12)
        for ($x = (9 - $half); $x -le (10 + $half); $x++) { $mask["$x,$y"] = $true }
    }
    # 中段柱身
    for ($y = 7; $y -le 11; $y++) {
        for ($x = 7; $x -le 12; $x++) { $mask["$x,$y"] = $true }
    }
    # 顶部尖
    foreach ($p in @('9,5', '10,5', '9,6', '10,6', '9,4', '10,4', '9,3', '10,3')) { $mask[$p] = $true }
} 168 132 220
