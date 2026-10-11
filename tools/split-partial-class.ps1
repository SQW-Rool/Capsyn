<#
    split-partial-class.ps1 —— 把一个类文件按"成员名/内容特征"拆成多个 partial class 文件。

    为什么用它：结构整理（越拆越细）需要把大文件里的成员搬到新文件，手工搬容易漏花括号。
    这个脚本是机械搬移 + 自校验：

      1. 先备份原文件；
      2. 只在**方法/属性的顶层边界**上切（缩进 4 空格的成员声明，含其上的 /// 注释与 [特性]）；
      3. 按给定的正则分组把成员分到不同 partial 文件；未命中的留在原文件；
      4. 原文件与新文件的类声明都补上 partial；
      5. 调用方负责构建；构建失败就调用 -Restore 回滚。

    用法：
      # 拆分
      ./tools/split-partial-class.ps1 -Path 'Services\IslandWindowStyler.cs' `
          -Group 'Subclass=.Subclass.cs:Subclass|WndProc|WM_|Hook' `
          -Group 'Display=.Display.cs:Dpi|Display|Monitor|WorkArea|Scale' `
          -Group 'Region=.Region.cs:Region|Rgn|Shape' `
          -Suffix 'Services\'
      # 回滚
      ./tools/split-partial-class.ps1 -Restore
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string[]]$Group = @(),
    [string]$Suffix = '',
    [switch]$Restore,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$backupDir = Join-Path $env:TEMP 'capsyn-split-backup'

if ($Restore) {
    if (-not (Test-Path $backupDir)) { '没有备份可回滚'; return }
    Get-ChildItem $backupDir -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path 'G:\Capsyn' $_.Name) -Force
        "已回滚: $($_.Name)"
    }
    Remove-Item $backupDir -Recurse -Force
    return
}

if (-not (Test-Path $Path)) { throw "找不到文件: $Path" }
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

$full = (Resolve-Path $Path).Path
$text = [System.IO.File]::ReadAllText($full)
$enc = New-Object System.Text.UTF8Encoding($false)
Copy-Item $full (Join-Path $backupDir (Split-Path $Path -Leaf)) -Force

# ---------- 1. 找出类声明与类体范围 ----------
$classMatch = [regex]::Match($text, '(?m)^\s*(?<decl>(?:internal|public|private|sealed|static|abstract|\s)*class\s+(?<name>\w+)[^\r\n{]*)')
if (-not $classMatch.Success) { throw "没找到 class 声明: $Path" }
$className = $classMatch.Groups['name'].Value
$declText = $classMatch.Groups['decl'].Value.Trim()
$bodyStart = $text.IndexOf('{', $classMatch.Index + $classMatch.Length - 1)
$bodyEnd = $text.LastIndexOf('}')
if ($bodyStart -lt 0 -or $bodyEnd -le $bodyStart) { throw "类体范围异常: $Path" }
$head = $text.Substring(0, $classMatch.Index)               # usings + namespace
$body = $text.Substring($bodyStart + 1, $bodyEnd - $bodyStart - 1)

# ---------- 2. 按"顶层成员"切片（缩进 4 个空格的声明行，连同其上注释/特性） ----------
$lines = $body -split "`r?`n"
$starts = New-Object System.Collections.Generic.List[int]
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^    (public|private|internal|protected|static|sealed|override|virtual|async|unsafe|partial|extern|~|void|Task|string|int|double|bool|record)\b') {
        $j = $i - 1
        while ($j -ge 0 -and ($lines[$j] -match '^\s*(///|\[|$)')) { $j-- }
        $starts.Add($j + 1)
    }
}
$members = @()
for ($k = 0; $k -lt $starts.Count; $k++) {
    $from = $starts[$k]
    $to = if ($k + 1 -lt $starts.Count) { $starts[$k + 1] - 1 } else { $lines.Count - 1 }
    $members += ,([pscustomobject]@{ Text = ($lines[$from..$to] -join "`r`n"); Index = $k })
}

# ---------- 3. 分组 ----------
$partialDecl = if ($declText -match '\bpartial\b') { $declText } else { $declText -replace '\bclass\b', 'partial class' }
$buckets = @{}
$kept = New-Object System.Collections.Generic.List[string]
foreach ($m in $members) {
    $hit = $null
    foreach ($g in $Group) {
        $parts = $g -split '=', 3
        if ($m.Text -match $parts[2]) { $hit = $parts[0]; break }
    }
    if ($hit) {
        if (-not $buckets.ContainsKey($hit)) { $buckets[$hit] = New-Object System.Collections.Generic.List[string] }
        $buckets[$hit].Add($m.Text)
    } else {
        $kept.Add($m.Text)
    }
}

if ($DryRun) {
    "类 $className：成员 $($members.Count) 个"
    foreach ($k in $buckets.Keys) { "  → $k : $($buckets[$k].Count) 个" }
    "  留在原文件: $($kept.Count) 个"
    return
}

# ---------- 4. 写回 ----------
$newBody = "`r`n" + ($kept -join "`r`n") + "`r`n"
[System.IO.File]::WriteAllText($full, $head + $partialDecl + "`r`n{" + $newBody + "}`r`n", $enc)
"原文件已瘦身: $(Split-Path $Path -Leaf)（留下 $($kept.Count) 个成员）"

$dir = Split-Path $full -Parent
$base = [System.IO.Path]::GetFileNameWithoutExtension($full)
foreach ($g in $Group) {
    $parts = $g -split '=', 3
    $key = $parts[0]; $fileName = $parts[1]
    if (-not $buckets.ContainsKey($key)) { continue }
    $target = Join-Path (Join-Path 'G:\Capsyn' $Suffix) ($base + $fileName)
    $out = $head + $partialDecl + "`r`n{`r`n" + ($buckets[$key] -join "`r`n") + "`r`n}`r`n"
    [System.IO.File]::WriteAllText($target, $out, $enc)
    "新文件: $(Split-Path $target -Leaf)（$($buckets[$key].Count) 个成员）"
}
