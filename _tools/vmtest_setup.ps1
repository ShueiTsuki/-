# 离线验证 VM：把 Core 里与 Terraria 无关的源码拷进独立控制台工程并跑测试
$ErrorActionPreference='Stop'
$src = "D:\DeepSeekHarness\tmod\HexCastingTerraria\Core"
$dst = "D:\DeepSeekHarness\vmtest"

if(Test-Path $dst){ Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dst | Out-Null

function Copy-Subset($rel, $exclude = @()) {
    $from = Join-Path $src $rel
    $to = Join-Path $dst $rel
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem $from -File -Filter *.cs | Where-Object { $exclude -notcontains $_.Name } | ForEach-Object {
        Copy-Item $_.FullName $to -Force
    }
    # 递归子目录
    Get-ChildItem $from -Directory | ForEach-Object {
        Copy-Subset (Join-Path $rel $_.Name) $exclude
    }
}

# Casting / Registry / World / Media **整目录**都能拷 —— Core 已经没有任何 XNA / tModLoader 依赖。
#
# 以前这里排除 HexGrid.cs（它用过 XNA 的 Vector2），代价是**坐标换算从来没被真正测过**：
# 验证工程里测的是手抄的一份公式副本，真代码改错一个符号测试照样全绿。
# 现在 HexGrid 改用自己的 Vec2f，排除表清空 —— 测的就是真代码。
# 这条约束由 _tools\check_arch.ps1 强制：Core/ 下再出现 XNA/tModLoader 引用就会失败。
Copy-Subset "Casting"
Copy-Subset "Registry"
Copy-Subset "World"
Copy-Subset "Media"
# Ui 目录是纯布局数学（不碰 XNA），同样要进测试网 ——
# 否则「格子画在哪」和「点在哪个格子上」这两份算法又会变成没测过的代码。
Copy-Subset "Ui"

Set-Content (Join-Path $dst "vmtest.csproj") @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>vmtest</AssemblyName>
    <RootNamespace>HexCastingTerraria</RootNamespace>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
'@

# 写入测试程序（它是本工程的入口点，必须在清空目录之后放入）
Copy-Item "D:\DeepSeekHarness\tmod\_tools\vmtest_Program.cs" (Join-Path $dst "Program.cs") -Force

"已拷贝到 $dst"
Get-ChildItem $dst -Recurse -File -Filter *.cs | Measure-Object | Select-Object -ExpandProperty Count
