<#
.SYNOPSIS
    探测一个媒体播放器的窗口结构（给 Capsyn 写专用适配器用）。

.DESCRIPTION
    输出三样东西：
      1. 所有可见顶层窗口（hwnd / 类名 / 进程 / 标题）—— 先看汽水音乐长什么样；
      2. 目标窗口的标题（播放时会不会变成「歌名 - 汽水音乐」）；
      3. 目标窗口的 UI Automation 树（每个控件的类型 / AutomationId / 名称 / 是否可用 / 位置）
         —— 有这些才能判断能不能读歌名、能不能点“播放/暂停/上一首/下一首”。

    结果写到 UTF-8 文件，方便直接发回来。

.PARAMETER Filter
    窗口标题或进程名里包含的关键字，比如 -Filter 汽水。不填就导出所有可见窗口。

.PARAMETER OutputPath
    输出文件路径，默认：<仓库>\.tools\probe-media-output.txt

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\probe-media-app.ps1 -Filter 汽水
#>
param(
    [string]$Filter = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class MediaProbeNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string TitleOf(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0) { return string.Empty; }
        var sb = new StringBuilder(len + 2);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
"@

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot '.tools\probe-media-output.txt'
}

$lines = New-Object System.Collections.Generic.List[string]
function Add-Line([string]$text) {
    $lines.Add($text)
    Write-Host $text
}

Add-Line ('探测时间: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Add-Line ''

# ---------- 1) 列出可见顶层窗口 ----------
Add-Line '================ 可见顶层窗口 ================'
$windows = New-Object System.Collections.Generic.List[object]

$callback = [MediaProbeNative+EnumWindowsProc]{
    param($hwnd, $lParam)
    if ([MediaProbeNative]::IsWindowVisible($hwnd)) {
        $title = [MediaProbeNative]::TitleOf($hwnd)
        if ($title) {
            $procId = 0
            [MediaProbeNative]::GetWindowThreadProcessId($hwnd, [ref]$procId) | Out-Null
            $procName = ''
            try { $procName = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName } catch { }
            $windows.Add([pscustomobject]@{
                Hwnd  = $hwnd
                Pid   = $procId
                Proc  = $procName
                Class = [MediaProbeNative]::ClassOf($hwnd)
                Title = $title
            })
        }
    }
    return $true
}

[MediaProbeNative]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

foreach ($w in $windows) {
    Add-Line ('  hwnd=0x{0:X}  pid={1,6}  {2,-22} [{3}]  "{4}"' -f $w.Hwnd.ToInt64(), $w.Pid, $w.Proc, $w.Class, $w.Title)
}
Add-Line ''

# ---------- 2) 选目标窗口 ----------
$targets = $windows
if ($Filter) {
    $targets = $windows | Where-Object { $_.Title -like "*$Filter*" -or $_.Proc -like "*$Filter*" }
}

Add-Line ('================ 目标窗口（过滤: "' + $Filter + '"）================')
if (-not $targets -or $targets.Count -eq 0) {
    Add-Line '没有匹配的窗口。请确认播放器已经打开、并且标题里有关键字；或改用 -Filter 指定别的关键字。'
    Add-Line ''
} else {
    foreach ($w in $targets) {
        Add-Line ('  hwnd=0x{0:X}  {1}  "{2}"' -f $w.Hwnd.ToInt64(), $w.Proc, $w.Title)
    }
}

# ---------- 3) 每个目标窗口的 UIA 树 ----------
$maxDepth = 6
foreach ($w in $targets) {
    Add-Line ''
    Add-Line ('================ UIA 树: ' + $w.Proc + ' / "' + $w.Title + '" ================')

    $titleBefore = $w.Title
    Start-Sleep -Milliseconds 1200
    $titleAfter = [MediaProbeNative]::TitleOf($w.Hwnd)
    Add-Line ('标题快照: 之前="' + $titleBefore + '"  1.2s 后="' + $titleAfter + '"')

    try {
        $element = [System.Windows.Automation.AutomationElement]::FromHandle($w.Hwnd)
        if (-not $element) {
            Add-Line '  无法从窗口句柄拿到 UIA 元素。'
            continue
        }

        # 广度优先遍历，限制深度与数量
        $queue = New-Object System.Collections.Generic.Queue[object]
        $queue.Enqueue([pscustomobject]@{ Element = $element; Depth = 0 })

        $count = 0
        while ($queue.Count -gt 0 -and $count -lt 600) {
            $item = $queue.Dequeue()
            $el = $item.Element
            $depth = $item.Depth
            $count++

            try {
                $ci = $el.Current
                $rect = $ci.BoundingRectangle
                $indent = ('  ' * ($depth + 1))

                # 有些控件的 rect 是无穷大（虚拟化容器），直接转 int 会溢出，这里给个占位。
                $rectText = 'rect=<unset>'
                if (-not [double]::IsInfinity($rect.X) -and -not [double]::IsInfinity($rect.Y) -and
                    -not [double]::IsInfinity($rect.Width) -and -not [double]::IsInfinity($rect.Height)) {
                    $rectText = ('rect={0},{1} {2}x{3}' -f [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height)
                }

                $line = '{0}d{1} {2,-22} id="{3}" name="{4}" enabled={5} {6}' -f `
                    $indent, $depth, $ci.ControlType.ProgrammaticName, $ci.AutomationId, $ci.Name, $ci.IsEnabled, $rectText
                Add-Line $line
            } catch {
                Add-Line ('  ' * ($depth + 1) + '<读取失败: ' + $_.Exception.Message + '>')
            }

            if ($depth -lt $maxDepth) {
                try {
                    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
                    $child = $walker.GetFirstChild($el)
                    while ($child) {
                        $queue.Enqueue([pscustomobject]@{ Element = $child; Depth = $depth + 1 })
                        $child = $walker.GetNextSibling($child)
                    }
                } catch { }
            }
        }

        Add-Line ('  （共 ' + $count + ' 个 UIA 元素）')
    } catch {
        Add-Line ('  UIA 遍历失败: ' + $_.Exception.Message)
    }
}

Add-Line ''
Add-Line '================ 探测结束 ================'

# 写文件（UTF-8 with BOM，Windows 记事本也能正常打开）
$dir = Split-Path $OutputPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllLines($OutputPath, $lines, (New-Object System.Text.UTF8Encoding($true)))
Write-Host ''
Write-Host ('已写入: ' + $OutputPath) -ForegroundColor Green
