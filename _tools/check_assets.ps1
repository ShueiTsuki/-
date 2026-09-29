# 资源名审计：找出「类名与贴图文件名对不上」的内容类。
#
# 为什么需要它：tModLoader 按 **类名** 去找同名 PNG，找不到就在加载时抛
# MissingResourceException 并**禁用整个模组**。
#
# 更要命的是：**专用服务器不会报这个错** —— `Mod.TransferAllAssets` 只在客户端跑
# （服务端不需要贴图）。所以「服务端能加载」永远不能证明客户端能加载。
# 这个脚本补上那一环。
#
# 做法：先把所有类与它们的基类扫出来，再**沿继承链向上找**到 tModLoader 的内容类型。
# 不能只匹配直接基类 —— 我们有自己的中间基类（HexDecoBlock / HexDirectrixItemBase …），
# 只认 tML 基类会漏掉一大半内容（第一版就漏了）。
$modRoot = "D:\DeepSeekHarness\tmod\HexCastingTerraria"

# tModLoader 会按类名找贴图的类型
$tileLike = @('ModTile', 'ModWall', 'ModProjectile', 'ModNPC', 'ModBuff', 'ModMount', 'ModItem')
# 这些内容类型不需要贴图
$noTexture = @('ModPlayer', 'ModSystem', 'ModTileEntity', 'GlobalTile', 'GlobalNPC',
               'GlobalProjectile', 'GlobalItem', 'ModPrefix', 'ModDust', 'ModCommand')

# ── 第一遍：收集所有类及其基类 ──
$classes = @{}
Get-ChildItem $modRoot -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    ForEach-Object {
        $rel = $_.FullName.Substring($modRoot.Length + 1) -replace '\\', '/'
        $dir = Split-Path $rel -Parent
        $text = Get-Content $_.FullName -Raw

        foreach ($m in [regex]::Matches($text, '(?m)^public\s+(sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(\w+)\s*:\s*([\w\.]+)')) {
            $classes[$m.Groups[2].Value] = [pscustomobject]@{
                Name       = $m.Groups[2].Value
                Base       = $m.Groups[3].Value.Split('.')[-1]
                Abstract   = ($m.Groups[1].Value -match 'abstract')
                Dir        = $dir
                HasTexture = ($text -match 'override\s+string\s+Texture')
            }
        }
    }

# ── 沿继承链解析到「需要贴图 / 不需要贴图 / 不是内容」 ──
function Resolve-Kind($cls, $depth = 0) {
    if ($depth -gt 12) { return 'unknown' }
    if (-not $classes.ContainsKey($cls)) {
        if ($tileLike -contains $cls) { return 'texture' }
        if ($noTexture -contains $cls) { return 'none' }
        return 'notcontent'
    }

    return Resolve-Kind $classes[$cls].Base ($depth + 1)
}

$needTexture = @()
$missing = @()

# 基类覆写了 Texture 的话，子类**继承**那一条，不需要自己的 PNG。
# （第一版只查了「同一个文件里有没有覆写」，于是把 31 个建材物品全报成缺失 —— 假阳性淹掉真问题。）
function Has-InheritedTexture($cls, $depth = 0) {
    if ($depth -gt 12) { return $false }
    if (-not $classes.ContainsKey($cls)) { return $false }
    if ($classes[$cls].HasTexture) { return $true }
    return Has-InheritedTexture $classes[$cls].Base ($depth + 1)
}

foreach ($c in $classes.Values) {
    if ($c.Abstract) { continue }                       # 抽象类不会被自动加载
    if ($c.HasTexture) { continue }                     # 本类显式覆写了贴图路径
    if (Has-InheritedTexture $c.Base) { continue }      # 基类覆写了，子类继承

    $kind = Resolve-Kind $c.Base
    if ($kind -ne 'texture') { continue }

    $needTexture += $c
    $png = Join-Path $modRoot (Join-Path $c.Dir "$($c.Name).png")

    if (-not (Test-Path $png)) {
        # 帮忙定位候选：去掉 Item 后缀的同名文件、或 Tiles 下的同名方块图
        $stripped = $c.Name -replace 'Item$', ''
        $candidates = @()
        foreach ($cand in @("$($c.Dir)/$stripped.png", "Content/Tiles/$stripped.png", "Content/Items/$stripped.png")) {
            if (Test-Path (Join-Path $modRoot $cand)) { $candidates += $cand }
        }

        $missing += [pscustomobject]@{
            Class      = $c.Name
            Expected   = "$($c.Dir)/$($c.Name).png"
            Candidates = ($candidates -join '  |  ')
        }
    }
}

"==== 需要贴图的具体类：$($needTexture.Count) 个 ===="
"==== 缺失：$($missing.Count) 个 ===="
if ($missing.Count -gt 0) {
    $missing | Format-Table -AutoSize | Out-String -Width 220
    exit 1
}
