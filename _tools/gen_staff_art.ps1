# 物品图标重刷：法杖 15 把（14 把对应原版 + 开发法杖）
#
# 走 pixelforge 的流水线：掩码 → 描边 → 倒角打光 → 量化调色板。
#
# ⚠️ 掩码字符的含义（见 pixelforge.ps1）：
#     '#' = 主体（-Body，这里是**木材色**）
#     '+' = 次材料（-Accent，这里是杖头的金属托/箍）
#     'o' = 宝石/发光（-Gem，紫水晶）
#
# 【曾经的 bug】杖身当初写的是 `++`，也就是**全部用次材料色**画 ——
# 而 13 把杖共用一个银灰色 Accent，-Body 的木材色从头到尾没被用到。
# 结果 13 把法杖在物品栏里一模一样，玩家根本分不出谁是谁。
# 现在杖身一律用 '#'，木材色才真正生效；'+' 只留给金属箍与杖头托。
#
# 另一条经验：14 把只差颜色的话在小图标里仍然难认，所以杖头有 3 种造型轮换
# （A 顶端晶体 / B 横向能量环 / C 弯钩），靠**轮廓**再区分一层。
. "$PSScriptRoot\pixelforge.ps1"

$out = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Content\Items"
$S = 32

# 杖身：3px 宽竖直杖（含两端描边共 5px），比 2px 更像"实物"
function Add-Shaft($m, [int]$top, [int]$bottom) {
    for ($y = $top; $y -le $bottom; $y++) {
        $m[$y] = $m[$y].Remove(14, 3).Insert(14, '###')
    }
    return $m
}

# 金属箍：握把处两道，用次材料色 —— 有箍才像"做出来的"，不是根树枝
function Add-Bands($m, [int[]]$rows) {
    foreach ($y in $rows) {
        $m[$y] = $m[$y].Remove(14, 3).Insert(14, '+++')
    }
    return $m
}

# 杖型 A：顶端晶体（最"标准"的法杖）
function Mask-StaffA([string]$gemChar) {
    $m = New-Canvas $S $S
    $m = Add-Shaft $m 10 29
    $m = Add-Bands $m @(20, 25)
    # 杖头：金属托 + 菱形晶体
    $m[6]  = $m[6].Remove(13, 6).Insert(13, "..++..")
    $m[7]  = $m[7].Remove(12, 8).Insert(12, ".+${gemChar}${gemChar}${gemChar}${gemChar}+.")
    $m[8]  = $m[8].Remove(13, 6).Insert(13, "+${gemChar}${gemChar}${gemChar}${gemChar}+")
    $m[9]  = $m[9].Remove(14, 4).Insert(14, ".${gemChar}${gemChar}.")
    $m[10] = $m[10].Remove(14, 4).Insert(14, ".##.")
    return $m
}

# 杖型 B：横向能量环（环里透空，轮廓完全不同）
function Mask-StaffB([string]$gemChar) {
    $m = New-Canvas $S $S
    $m = Add-Shaft $m 11 29
    $m = Add-Bands $m @(21, 26)
    $m[5]  = $m[5].Remove(12, 8).Insert(12, "+......+")
    $m[6]  = $m[6].Remove(11, 10).Insert(11, ".${gemChar}....${gemChar}.")
    $m[7]  = $m[7].Remove(11, 10).Insert(11, "+........+")
    $m[8]  = $m[8].Remove(11, 10).Insert(11, ".${gemChar}....${gemChar}.")
    $m[9]  = $m[9].Remove(12, 8).Insert(12, "+......+")
    $m[10] = $m[10].Remove(13, 6).Insert(13, ".####.")
    $m[11] = $m[11].Remove(14, 4).Insert(14, ".##.")
    return $m
}

# 杖型 C：顶端弯钩（钩子里挂一颗晶体）
function Mask-StaffC([string]$gemChar) {
    $m = New-Canvas $S $S
    $m = Add-Shaft $m 12 29
    $m = Add-Bands $m @(22, 27)
    $m[4]  = $m[4].Remove(13, 6).Insert(13, "..${gemChar}${gemChar}..")
    $m[5]  = $m[5].Remove(12, 8).Insert(12, "+${gemChar}..${gemChar}+")
    $m[6]  = $m[6].Remove(12, 4).Insert(12, ".${gemChar}${gemChar}..")
    $m[7]  = $m[7].Remove(12, 4).Insert(12, "${gemChar}${gemChar}...")
    $m[8]  = $m[8].Remove(12, 4).Insert(12, ".${gemChar}...")
    $m[9]  = $m[9].Remove(12, 4).Insert(12, "+${gemChar}..")
    $m[10] = $m[10].Remove(12, 4).Insert(12, ".##.")
    $m[11] = $m[11].Remove(13, 5).Insert(13, ".###.")
    $m[12] = $m[12].Remove(13, 5).Insert(13, "..##.")
    return $m
}

# 木材色：低饱和，与泰拉各木材的观感一致。
# 顺序刻意让相邻两种木色差得开一些，扫一眼就能分辨。
$woods = @(
    @('OakStaff',        @(150, 110,  66), 'A'),
    @('BorealStaff',     @(116, 152, 138), 'B'),
    @('PalmStaff',       @(186, 152, 104), 'C'),
    @('MahoganyStaff',   @(156,  92,  72), 'A'),
    @('EbonwoodStaff',   @( 96,  80, 112), 'B'),
    @('ShadewoodStaff',  @(124,  74,  78), 'C'),
    @('PearlwoodStaff',  @(206, 186, 206), 'A'),
    @('DynastyStaff',    @(140, 150, 108), 'B'),
    @('SpookyStaff',     @(174, 106,  54), 'C'),
    @('AshStaff',        @(104, 100, 104), 'A'),
    @('QuenchedStaff',   @( 92, 138, 148), 'B'),
    @('EdifiedStaff',    @(196, 186, 210), 'C'),
    @('MindspliceStaff', @(112,  88, 120), 'A'),
    @('CherryStaff',     @(206, 152, 176), 'B')
)

$gem = @(196, 168, 255)
$i = 0
foreach ($w in $woods) {
    $name = $w[0]; $base = $w[1]; $kind = $w[2]
    $mask = switch ($kind) {
        'A' { Mask-StaffA 'o' }
        'B' { Mask-StaffB 'o' }
        default { Mask-StaffC 'o' }
    }
    New-PixelArt -Name "$name.png" -OutDir $out -Mask $mask -Body $base -Accent @(186, 186, 198) -Gem $gem -Seed (1000 + $i)
    "生成 $name.png  [$kind]  木色 RGB($($base -join ','))"
    $i++
}

# 开发法杖：更亮、宝石是金色，一眼能认出是"调试用的"
$dev = Mask-StaffA 'o'
New-PixelArt -Name "DevStaff.png" -OutDir $out -Mask $dev -Body @(120, 190, 220) -Accent @(232, 240, 250) -Gem @(255, 240, 160) -Seed 77
"生成 DevStaff.png [A] 调试用"

"完成：15 把法杖"
