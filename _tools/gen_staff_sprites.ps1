# 生成 13 把法杖与紫水晶粉块的贴图。
Add-Type -AssemblyName System.Drawing

$itemsOut = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$tilesOut = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Tiles"
$S = 32

function New-Canvas($size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}
function Px($g, $x, $y, $c, $max) {
    if ($x -lt 0 -or $x -ge $max -or $y -lt 0 -or $y -ge $max) { return }
    $b = New-Object System.Drawing.SolidBrush($c)
    $g.FillRectangle($b, $x, $y, 1, 1)
    $b.Dispose()
}
function Save-Bmp($bmp, $dir, $name) {
    $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ---------- 法杖：斜杆 + 顶端晶体 ----------
function New-Staff($name, $wood, $woodDark, $gem, $gemLight) {
    $r = New-Canvas $S; $bmp = $r[0]; $g = $r[1]

    # 杖身：从左下到右上的一条 3px 粗斜杆
    for ($t = 0; $t -lt 20; $t++) {
        $x = 6 + $t
        $y = 26 - $t
        # 左右两条描边用暗色，中间用亮色，做出圆柱感
        Px $g $x $y $woodDark $S
        Px $g ($x + 1) $y $wood $S
        Px $g $x ($y + 1) $woodDark $S
    }

    # 顶端晶体：一个小菱形
    $cx = 24; $cy = 7; $rad = 4
    for ($dy = -$rad; $dy -le $rad; $dy++) {
        $w = $rad - [Math]::Abs($dy)
        for ($dx = -$w; $dx -le $w; $dx++) {
            $c = if ($dx -le 0 -and $dy -le 0) { $gemLight } else { $gem }
            Px $g ($cx + $dx) ($cy + $dy) $c $S
        }
    }
    # 晶体高光
    Px $g ($cx - 1) ($cy - 2) ([System.Drawing.Color]::FromArgb(255, 255, 250, 255)) $S

    $g.Dispose(); Save-Bmp $bmp $itemsOut $name
}

$gem      = [System.Drawing.Color]::FromArgb(255, 168, 118, 228)
$gemLight = [System.Drawing.Color]::FromArgb(255, 214, 180, 250)

function W($r, $g, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }
function Dark($c) { [System.Drawing.Color]::FromArgb(255, [int]($c.R * 0.65), [int]($c.G * 0.65), [int]($c.B * 0.65)) }

$woods = @(
    @("OakStaff.png",        (W 150 110  70)),
    @("BorealStaff.png",     (W 178 168 150)),
    @("PalmStaff.png",       (W 186 152 104)),
    @("MahoganyStaff.png",   (W 158  96  74)),
    @("EbonwoodStaff.png",   (W  84  70 100)),
    @("ShadewoodStaff.png",  (W 120  74  78)),
    @("PearlwoodStaff.png",  (W 210 186 196)),
    @("DynastyStaff.png",    (W 128 138 158)),
    @("SpookyStaff.png",     (W 132  92  56)),
    @("AshStaff.png",        (W 104 100 104))
)
foreach ($w in $woods) {
    New-Staff $w[0] $w[1] (Dark $w[1]) $gem $gemLight
    "生成 " + $w[0]
}

# 三把特殊法杖：晶体颜色不同
New-Staff "QuenchedStaff.png"   (W 150 150 160) (W 100 100 110) (W 178 232 232) (W 228 252 252)
New-Staff "EdifiedStaff.png"    (W 196 186 210) (W 140 132 152) (W 226 210 250) (W 250 244 255)
New-Staff "MindspliceStaff.png" (W 92  70  86)  (W  60  44  56)  (W 206  90 110) (W 255 170 180)
"生成特殊法杖 3 把"

# ---------- 紫水晶粉块：16x16 压实粉末 ----------
$r = New-Canvas 16; $bmp = $r[0]; $g = $r[1]
$rng = New-Object System.Random(97)
for ($y = 0; $y -lt 16; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
        $n = $rng.Next(30)
        Px $g $x $y (W (140 + $n) (100 + $n) (196 + $n)) 16
    }
}
# 压实的颗粒纹理：几处亮斑，表示这是「粉」
for ($k = 0; $k -lt 18; $k++) {
    Px $g ($rng.Next(16)) ($rng.Next(16)) (W 216 190 250) 16
}
$g.Dispose(); Save-Bmp $bmp $tilesOut "AmethystDustBlock.png"
"生成 AmethystDustBlock.png"
"完成"
