# 从源项目的 Patchouli 手册生成书本**内容骨架**（Core/Ui/BookContent.Generated.cs）。
#
# 源：D:\DeepSeekHarness\hexsrc\Common\src\main\resources\assets\hexcasting\patchouli_books\thehexbook\en_us\
#     7 个分类 + 82 个条目（含 patterns/great_spells、patterns/spells 两个子目录）
#
# ⚠️ **正文拿不到**：源文件里 `"text"` 存的是**本地化键**（如 hexcasting.page.jeweler_hammer.1），
#    真正的英文正文在 mod 的 lang 文件里，而 hexsrc 里没有 lang 目录（已确认，见 BOOK_UI_DESIGN §10）。
#    所以这里只生成**结构与排版信息**（分类、条目、页面类型、图案 id、配方 id），
#    正文留空、把原来的键放进注释里 —— 将来补正文时照着键填。
#
# 为什么不写运行时 json 解析器：本项目的既定做法是「脚本生成 C#，生而勿手改」
#（见 gen_deco_blocks.ps1 / gen_progression.ps1）。这样 Core/ 不需要引入任何解析库，
#  加载零成本，而且生成期就能发现源数据的问题。
param(
    [string]$SrcRoot = 'D:\DeepSeekHarness\hexsrc\Common\src\main\resources\assets\hexcasting\patchouli_books\thehexbook\en_us',
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'

$tools = $PSScriptRoot
$root  = Split-Path -Parent $tools
if (-not $OutFile) { $OutFile = Join-Path $root 'HexCastingTerraria\Core\Ui\BookContent.Generated.cs' }
if (-not (Test-Path $SrcRoot)) { throw "找不到源手册目录：$SrcRoot" }

# PowerShell 5.1 的 ConvertFrom-Json 够用（这些文件都是规规矩矩的 JSON）
function Read-Json([string]$path) {
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

# ── 分类 ──────────────────────────────────────────────────────────────
$cats = New-Object System.Collections.ArrayList
foreach ($f in (Get-ChildItem (Join-Path $SrcRoot 'categories') -File -Filter *.json | Sort-Object Name)) {
    $j = Read-Json $f.FullName
    [void]$cats.Add([pscustomobject]@{
        Id    = $f.BaseName
        Name  = [string]$j.name
        Icon  = [string]$j.icon
        Desc  = [string]$j.description
        Sort  = [int]$j.sortnum
    })
}

# ── 条目 ──────────────────────────────────────────────────────────────
# 页面类型映射：只保留泰拉侧真会用的（PageMultiblock / PageEntity / PageQuest 已剔除）
function Map-PageKind([string]$type) {
    switch -Regex ($type) {
        'patchouli:text'                  { return 'Text' }
        'patchouli:crafting$'             { return 'Crafting' }
        'patchouli:spotlight'             { return 'Spotlight' }
        'patchouli:header'                { return 'Header' }
        'patchouli:empty'                 { return 'Separator' }
        # 源项目注册的自定页面类型（Patchouli 允许 mod 自己注册）
        'hexcasting:pattern$'             { return 'Pattern' }
        'hexcasting:manual_pattern'       { return 'Pattern' }
        'hexcasting:brainsweep'           { return 'Crafting' }
        'hexcasting:crafting_multi'       { return 'Crafting' }
        # 泰拉侧暂不需要：图片页与超链接页（我们有自己的跳转方式）
        'patchouli:image'                 { return '' }
        'patchouli:link'                  { return '' }
        default                           { return '' }
    }
}

$entries = New-Object System.Collections.ArrayList
$skippedPages = @{}
$files = Get-ChildItem (Join-Path $SrcRoot 'entries') -Recurse -File -Filter *.json | Sort-Object FullName

foreach ($f in $files) {
    $j = Read-Json $f.FullName
    if (-not $j.pages) { continue }

    $pages = New-Object System.Collections.ArrayList
    foreach ($p in $j.pages) {
        # 裸字符串页 = Patchouli 的简写，等价于 {"type":"patchouli:text","text":…}
        # （源项目里 81 页是这种写法，最初被当成"未知类型"整页丢掉了）
        if ($p -is [string]) {
            [void]$pages.Add([pscustomobject]@{
                Kind = 'Text'; Title = ''; Text = [string]$p
                Recipe = ''; Pattern = ''; Icon = ''; Template = ''
            })
            continue
        }

        # 模板页：`{"template": "hexcasting:pattern", "patterns": [...]}`
        $tmpl = [string]$p.template
        if ($tmpl) {
            # 只留下泰拉侧要的模板；多方块/实体预览那几支在泰拉没有对应物
            if ($tmpl -match 'pattern|crafting_multi') {
                [void]$pages.Add([pscustomobject]@{
                    Kind     = 'Pattern'
                    Title    = [string]$p.title
                    Text     = [string]$p.text
                    Recipe   = ''
                    Pattern  = ([string]$p.patterns)
                    Icon     = ''
                    Template = $tmpl
                })
            }
            continue
        }

        $kind = Map-PageKind ([string]$p.type)
        if (-not $kind) {
            $key = [string]$p.type
            if (-not $key) { $key = '(无 type 也无 template)' }
            if ($skippedPages.ContainsKey($key)) { $skippedPages[$key]++ } else { $skippedPages[$key] = 1 }
            continue
        }
        [void]$pages.Add([pscustomobject]@{
            Kind     = $kind
            Title    = [string]$p.title
            Text     = [string]$p.text
            Recipe   = [string]$p.recipe
            Pattern  = ([string]$p.patterns)          # 模板里的 `"patterns": "#patterns"` 是变量引用
            Icon     = [string]$p.item
            Template = ''
        })
    }
    if ($pages.Count -eq 0) { continue }

    # 分类 id：源项目是 `hexcasting:items` 形式；`patterns/great_spells` 这类**子分类**
    # 在 Patchouli 里挂在父分类下，我们的模型是平的，所以归到父分类。
    $cat = ([string]$j.category) -replace '^.*:', ''
    $cat = ($cat -split '/')[0]
    [void]$entries.Add([pscustomobject]@{
        Id     = ([string]$j.name)
        Cat    = $cat
        Name   = [string]$j.name
        Icon   = [string]$j.icon
        Sort   = if ($null -ne $j.sortnum) { [int]$j.sortnum } else { 0 }
        Adv    = [string]$j.advancement
        Pages  = $pages
    })
}

Write-Host "分类 $($cats.Count)，条目 $($entries.Count)，页面 $(($entries | ForEach-Object { $_.Pages.Count } | Measure-Object -Sum).Sum)"
if ($skippedPages.Count -gt 0) {
    Write-Host '跳过的页面类型（泰拉侧不需要）：'
    $skippedPages.GetEnumerator() | Sort-Object Name | ForEach-Object { "  $($_.Key)  ×$($_.Value)" }
}

# ── 生成 C# ───────────────────────────────────────────────────────────
function CsStr([string]$s) {
    if (-not $s) { return 'string.Empty' }
    return '"' + ($s -replace '\\', '\\\\' -replace '"', '\"') + '"'
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// <auto-generated>')
[void]$sb.AppendLine('//     本文件由 _tools/gen_book_content.ps1 生成，**不要手改**：')
[void]$sb.AppendLine('//     改生成器或改源手册，然后重新跑脚本。手改会在下次生成时被覆盖。')
[void]$sb.AppendLine('// </auto-generated>')
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('namespace HexCastingTerraria.Core.Ui;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('/// <summary>')
[void]$sb.AppendLine('/// 书本内容骨架：从源项目的 Patchouli 手册（7 分类 / ' + $entries.Count + ' 条目）生成。')
[void]$sb.AppendLine('///')
[void]$sb.AppendLine('/// ⚠️ **正文是空的**：源文件里 <c>text</c> 存的是本地化键，而真正的英文正文在 mod 的')
[void]$sb.AppendLine('/// lang 文件里，参考源码里没有那个目录。所以这里只有结构与排版信息，')
[void]$sb.AppendLine('/// 每个页面的原键保留在紧随其后的注释里，补正文时照着填。')
[void]$sb.AppendLine('/// </summary>')
[void]$sb.AppendLine('public static class BookContent')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    public static BookDocument Create()')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        var doc = new BookDocument { Id = "thehexbook", TitleKey = "hexcasting.book.thehexbook" };')
[void]$sb.AppendLine()

$catVar = @{}
foreach ($c in $cats) {
    $v = 'cat_' + ($c.Id -replace '[^A-Za-z0-9]', '_')
    $catVar[$c.Id] = $v
    [void]$sb.AppendLine("        var $v = new BookCategory")
    [void]$sb.AppendLine('        {')
    [void]$sb.AppendLine("            Id = $(CsStr $c.Id),")
    [void]$sb.AppendLine("            NameKey = $(CsStr $c.Name),")
    [void]$sb.AppendLine("            IconItem = $(CsStr $c.Icon),")
    [void]$sb.AppendLine("            DescriptionKey = $(CsStr $c.Desc),")
    [void]$sb.AppendLine("            SortNum = $($c.Sort),")
    [void]$sb.AppendLine('        };')
    [void]$sb.AppendLine("        doc.Categories.Add($v);")
    [void]$sb.AppendLine()
}

$unknownCat = @{}
foreach ($e in ($entries | Sort-Object Cat, Sort, Id)) {
    if (-not $catVar.ContainsKey($e.Cat)) {
        if ($unknownCat.ContainsKey($e.Cat)) { $unknownCat[$e.Cat]++ } else { $unknownCat[$e.Cat] = 1 }
        continue
    }
    $v = 'e_' + ($e.Cat + '_' + (($e.Id -replace '^.*:', '') -replace '[^A-Za-z0-9]', '_'))
    [void]$sb.AppendLine("        var $v = new BookEntry")
    [void]$sb.AppendLine('        {')
    [void]$sb.AppendLine("            Id = $(CsStr $e.Id),")
    [void]$sb.AppendLine("            CategoryId = $(CsStr $e.Cat),")
    [void]$sb.AppendLine("            NameKey = $(CsStr $e.Name),")
    [void]$sb.AppendLine("            IconItem = $(CsStr $e.Icon),")
    [void]$sb.AppendLine("            SortNum = $($e.Sort),")
    [void]$sb.AppendLine("            Advancement = $(CsStr $e.Adv),")
    [void]$sb.AppendLine('        };')
    foreach ($p in $e.Pages) {
        $src = if ($p.Text) { $p.Text } elseif ($p.Recipe) { "recipe:$($p.Recipe)" } elseif ($p.Pattern) { "patterns:$($p.Pattern)" } else { '' }
        if ($src) { [void]$sb.AppendLine("        // 源键：$src") }
        [void]$sb.AppendLine("        $v.Pages.Add(new BookPage")
        [void]$sb.AppendLine('        {')
        [void]$sb.AppendLine("            Kind = BookPageKind.$($p.Kind),")
        if ($p.Title) { [void]$sb.AppendLine("            Title = $(CsStr $p.Title),") }
        if ($p.Recipe) { [void]$sb.AppendLine("            RecipeItem = $(CsStr ($p.Recipe -replace '^.*:', '')),") }
        if ($p.Template) { [void]$sb.AppendLine("            TemplateId = $(CsStr $p.Template),") }
        [void]$sb.AppendLine('        });')
    }
    [void]$sb.AppendLine("        $($catVar[$e.Cat]).Entries.Add($v);")
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine('        doc.RebuildIndex();')
[void]$sb.AppendLine('        return doc;')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')

[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "-> $OutFile  ($([math]::Round((Get-Item $OutFile).Length / 1KB, 1)) KB)"

if ($unknownCat.Count -gt 0) {
    Write-Host '⚠️ 分类对不上的条目（已跳过）：' -ForegroundColor Yellow
    $unknownCat.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key) ×$($_.Value)" }
}
