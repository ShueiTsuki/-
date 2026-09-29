# 壁挂装饰与阿卡夏装饰的贴图（16x16）。开发期占位，见 TODO_PLAN.md 美术欠账。
Add-Type -AssemblyName System.Drawing

$tileDir = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 16

function Clamp255([int]$v) { if ($v -lt 0) { return 0 }; if ($v -gt 255) { return 255 }; return $v }

function New-Texture {
    param([string]$Name, [int]$R, [int]$G, [int]$B, [scriptblock]$Draw, [int]$Seed)

    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::Transparent)
    $rng = New-Object System.Random($Seed)

    function Px($x, $y, $c) {
        if ($x -lt 0 -or $x -ge 16 -or $y -lt 0 -or $y -ge 16) { return }
        $b = New-Object System.Drawing.SolidBrush($c)
        $gfx.FillRectangle($b, $x, $y, 1, 1)
        $b.Dispose()
    }
    function Shade([int]$delta) {
        return [System.Drawing.Color]::FromArgb(255, (Clamp255 ($R + $delta)), (Clamp255 ($G + $delta)), (Clamp255 ($B + $delta)))
    }

    # ① 形状掩码（由各贴图自己画）
    $mask = @{}
    & $Draw

    # ② 打光 + 噪点
    foreach ($key in @($mask.Keys)) {
        $x = [int]($key -split ',')[0]
        $y = [int]($key -split ',')[1]
        $d = $rng.Next(-8, 9)
        if (-not $mask.ContainsKey("$($x-1),$y")) { $d += 24 }
        elseif (-not $mask.ContainsKey("$($x+1),$y")) { $d -= 20 }
        Px $x $y (Shade $d)
    }

    # ③ 描边
    $outline = [System.Drawing.Color]::FromArgb(255, (Clamp255 ([int]($R * 0.3))), (Clamp255 ([int]($G * 0.3))), (Clamp255 ([int]($B * 0.3))))
    foreach ($key in @($mask.Keys)) {
        $x = [int]($key -split ',')[0]
        $y = [int]($key -split ',')[1]
        foreach ($dd in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
            $nx = $x + $dd[0]; $ny = $y + $dd[1]
            if (-not $mask.ContainsKey("$nx,$ny")) { Px $nx $ny $outline }
        }
    }

    $gfx.Dispose()
    $bmp.Save((Join-Path $tileDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "生成 $Name"
}

# 卷轴纸：竖直的纸条，上端有卷轴轴
New-Texture "ScrollPaper.png" 226 214 186 {
    for ($y = 3; $y -le 13; $y++) { for ($x = 5; $x -le 10; $x++) { $mask["$x,$y"] = $true } }
    for ($x = 4; $x -le 11; $x++) { $mask["$x,2"] = $true; $mask["$x,14"] = $true }
} 101

# 古卷轴纸：更窄、更旧
New-Texture "AncientScrollPaper.png" 206 196 158 {
    for ($y = 3; $y -le 13; $y++) { for ($x = 6; $x -le 9; $x++) { $mask["$x,$y"] = $true } }
    for ($x = 5; $x -le 10; $x++) { $mask["$x,2"] = $true; $mask["$x,14"] = $true }
    # 中间一道裂口
    $mask.Remove('7,8'); $mask.Remove('8,8')
} 102

# 卷轴纸灯笼：纸条 + 底部发光块
New-Texture "ScrollPaperLantern.png" 240 226 186 {
    for ($y = 2; $y -le 7; $y++) { for ($x = 6; $x -le 9; $x++) { $mask["$x,$y"] = $true } }
    for ($y = 8; $y -le 13; $y++) { for ($x = 5; $x -le 10; $x++) { $mask["$x,$y"] = $true } }
} 103

# 古卷轴纸灯笼
New-Texture "AncientScrollPaperLantern.png" 220 208 160 {
    for ($y = 2; $y -le 7; $y++) { for ($x = 6; $x -le 9; $x++) { $mask["$x,$y"] = $true } }
    for ($y = 8; $y -le 13; $y++) { for ($x = 5; $x -le 10; $x++) { $mask["$x,$y"] = $true } }
    $mask.Remove('5,8'); $mask.Remove('10,13')
} 104

# 紫晶壁灯：托架 + 上方晶体
New-Texture "AmethystSconce.png" 168 132 214 {
    for ($y = 8; $y -le 12; $y++) { for ($x = 6; $x -le 9; $x++) { $mask["$x,$y"] = $true } }
    for ($x = 4; $x -le 11; $x++) { $mask["$x,13"] = $true }
    # 晶体
    $mask['7,4'] = $true; $mask['8,4'] = $true
    $mask['6,5'] = $true; $mask['7,5'] = $true; $mask['9,5'] = $true
    $mask['7,6'] = $true; $mask['8,6'] = $true; $mask['8,7'] = $true
} 105

# 阿卡夏书架：整块 + 三层书
New-Texture "AkashicBookshelf.png" 96 74 108 {
    for ($y = 0; $y -le 15; $y++) { for ($x = 0; $x -le 15; $x++) { $mask["$x,$y"] = $true } }
} 106

# 阿卡夏系带：细长的符带
New-Texture "AkashicLigature.png" 150 120 196 {
    for ($y = 1; $y -le 14; $y++) { for ($x = 6; $x -le 9; $x++) { $mask["$x,$y"] = $true } }
} 107
