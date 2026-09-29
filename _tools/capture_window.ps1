# 抓取指定窗口的截图 —— 渲染层验证的地基。
#
# 为什么需要它：四层自动化全是无头逻辑验证，`Client/UI/*` 的绘制部分零覆盖。
# 而"改了贴图/布局却没有测试报警"这件事，只能靠**像素**兜住。
#
# 用法：
#   .\capture_window.ps1 -TitleMatch "tModLoader" -Out "D:\shots\book.png"
#   .\capture_window.ps1 -TitleMatch "tModLoader" -OutDir "D:\shots" -Count 3 -IntervalMs 800
#   .\capture_window.ps1 -List          # 列出当前可见窗口（找标题用）
#
# 注意：抓的是**窗口内容**（PrintWindow），不是屏幕区域 ——
# 所以窗口被遮挡、最小化都不影响；但独占全屏的 DirectX 画面可能抓到黑图，
# 泰拉建议用窗口模式。
param(
    [string]$TitleMatch = 'tModLoader',
    [string]$Out = '',
    [string]$OutDir = '',
    [int]$Count = 1,
    [int]$IntervalMs = 800,
    [switch]$List
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class WinCap
{
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string TitleOf(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }
}
'@

function Get-VisibleWindows {
    $found = New-Object System.Collections.Generic.List[object]
    $cb = [WinCap+EnumWindowsProc]{
        param($h, $l)
        if ([WinCap]::IsWindowVisible($h)) {
            $t = [WinCap]::TitleOf($h)
            if ($t -ne '') { $script:_found.Add([pscustomobject]@{ Handle = $h; Title = $t }) }
        }
        return $true
    }
    $script:_found = $found
    [void][WinCap]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:_found
}

if ($List) {
    Get-VisibleWindows | Format-Table -AutoSize | Out-String | Write-Host
    exit 0
}

$targets = @(Get-VisibleWindows | Where-Object { $_.Title -like "*$TitleMatch*" })
if ($targets.Count -eq 0) {
    Write-Host "找不到标题含「$TitleMatch」的窗口。用 -List 看当前有哪些窗口。" -ForegroundColor Red
    exit 1
}

$win = $targets[0]
Write-Host "目标窗口: $($win.Title)  (hwnd=$($win.Handle))"

if ($OutDir -ne '') {
    if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
} else {
    $dir = Split-Path -Parent $Out
    if ($dir -ne '' -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

for ($i = 0; $i -lt $Count; $i++) {
    $r = New-Object WinCap+RECT
    if (-not [WinCap]::GetWindowRect($win.Handle, [ref]$r)) { throw "GetWindowRect 失败" }
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { throw "窗口尺寸非法: ${w}x${h}" }

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    # flags=2 (PW_RENDERFULLCONTENT)：不加这个，DWM 合成的窗口内容会抓成空白
    $ok = [WinCap]::PrintWindow($win.Handle, $hdc, 2)
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()

    $path = if ($OutDir -ne '') {
        Join-Path $OutDir ("shot_{0:yyyyMMdd_HHmmss}_{1}.png" -f (Get-Date), $i)
    } else {
        $Out
    }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Host ("  [{0}] {1}x{2}  PrintWindow={3}  → {4}" -f $i, $w, $h, $ok, $path)
    if ($i -lt $Count - 1) { Start-Sleep -Milliseconds $IntervalMs }
}
