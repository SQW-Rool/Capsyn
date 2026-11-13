<#
.SYNOPSIS
    一键打「绿色版」（自包含，解压即用，不需要安装 .NET / Windows App Runtime）。

.DESCRIPTION
    流程：清理 → dotnet publish（self-contained + WindowsAppSDKSelfContained）→ 放入「使用说明.txt」
    → 压缩成带一层顶目录的 zip（解压不会散一地）→ 算 SHA256。

    产物（默认在仓库根目录的 dist\ 下）：
      Capsyn-<Version>-win-x64-portable.zip
      Capsyn-<Version>-win-x64-portable.zip.sha256

    要求：本机有 .NET SDK；不需要 Inno Setup / NSIS 之类的安装包工具。
    注意：WinUI 3 不支持 PublishSingleFile / PublishTrimmed，所以是「一个文件夹」而不是单文件 exe。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\build-portable.ps1

.EXAMPLE
    # 换个版本号（只影响产物命名和使用说明里的版本）
    powershell -ExecutionPolicy Bypass -File .\tools\build-portable.ps1 -Version 4.0.1-beta2
#>
[CmdletBinding()]
param(
    # 版本号：只用于产物命名与说明文字
    [string]$Version = '4.0.0-beta',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    # 产物目录（相对仓库根目录）
    [string]$OutputDir = 'dist'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$project = Join-Path $root 'Capsyn.csproj'
$outDir = Join-Path $root $OutputDir
$zipName = "Capsyn-$Version-$Runtime-portable"
$zipPath = Join-Path $outDir "$zipName.zip"
$hashPath = "$zipPath.sha256"

$stageRoot = Join-Path $env:TEMP "capsyn-portable-$PID"
$publishDir = Join-Path $stageRoot 'publish'
$stageDir = Join-Path $stageRoot "Capsyn-$Version"

Write-Host '== 1/5 清理 ==' -ForegroundColor Cyan
Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDir, $stageDir, $outDir -Force | Out-Null

Write-Host '== 2/5 publish（自包含 + WinAppSDK 自包含） ==' -ForegroundColor Cyan

# 删掉上一次 publish 留在 bin 里的 publish 目录：
# CopyXamlArtifactsToPublish 的 glob 会扫 $(TargetDir)**，不清掉的话会把旧发布产物再收一遍，
# 在打包结果里长出 publish\publish\... 的嵌套副本。
Get-ChildItem (Join-Path $root "bin\$Configuration") -Directory -Recurse -Filter 'publish' -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  清理旧发布目录: $($_.FullName)" -ForegroundColor DarkGray; Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "  项目: $project" -ForegroundColor DarkGray
dotnet publish $project `
    -c $Configuration `
    -p:Platform=x64 `
    -r $Runtime `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（exit $LASTEXITCODE）"
}

Write-Host '== 3/5 组装 + 使用说明 ==' -ForegroundColor Cyan
Copy-Item (Join-Path $publishDir '*') $stageDir -Recurse -Force

$instructions = @"
Capsyn v$Version —— Windows 灵动岛（绿色版）
=================================================

【怎么用】
1. 把整个文件夹解压到任意位置（不要只拖 Capsyn.exe 出来）；
2. 双击 Capsyn.exe，屏幕顶部中央会出现时间岛；
3. 鼠标移到岛上会展开看板（指标 / 时间工具 / 设置）；
4. 退出：看板右下角红色电源按钮（唯一退出入口）。

【拖拽解压（v5.1.0 起）】
- 把压缩包拖到时间岛上 → 选目标目录 → 解压；
- 支持 .zip / .7z / .rar / .tar / .tar.gz / .xz / .bz2 等；
  .zip 用 .NET 内置解压（精确进度），其它格式走 Windows 自带的 tar.exe（libarchive，无需装任何东西）；
- 目标目录里已有同名文件时不会覆盖，会自动解到「压缩包名\」子目录。

【系统要求】
- Windows 10 1809 及以上 / Windows 11，x64；
- 什么都不用装：.NET 运行时和 Windows App Runtime 已经打进这个文件夹了。

【数据放在哪】
- 设置（铃声选择）：%LOCALAPPDATA%\Capsyn\settings.json
- 铃声缓存：%LOCALAPPDATA%\Capsyn\ringtones\
- 诊断日志（仅当设置环境变量 CAPSYN_DIAG=1）：exe 同目录 capsyn-diag.log
- 崩溃日志（永远写）：exe 同目录 capsyn-crash.log

【怎么卸载】
删掉这个文件夹；想清干净就再删 %LOCALAPPDATA%\Capsyn。

【小提示】
- 首次运行如果被 SmartScreen 拦（未知发布者），点"更多信息 → 仍要运行"；
- 想让它开机自启：自己把 Capsyn.exe 的快捷方式放进 shell:startup；
- 调试用环境变量：CAPSYN_DIAG=1（诊断日志）、CAPSYN_ALARM_TEST=秒 / CAPSYN_COUNTDOWN_TEST=秒（验证到点提醒）。
"@

$instructionsPath = Join-Path $stageDir '使用说明.txt'
[System.IO.File]::WriteAllText($instructionsPath, $instructions, (New-Object System.Text.UTF8Encoding($true)))

Write-Host '== 4/5 压缩 ==' -ForegroundColor Cyan
Remove-Item $zipPath, $hashPath -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipTimer = [Diagnostics.Stopwatch]::StartNew()
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)
$zipTimer.Stop()

Write-Host '== 5/5 校验和 ==' -ForegroundColor Cyan
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
"$hash  $(Split-Path $zipPath -Leaf)" | Set-Content -LiteralPath $hashPath -Encoding ASCII

$fileCount = (Get-ChildItem $stageDir -Recurse -File | Measure-Object).Count
$rawSize = [Math]::Round(((Get-ChildItem $stageDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
$zipSize = [Math]::Round(((Get-Item $zipPath).Length / 1MB), 1)

Write-Host ''
Write-Host '== 完成 ==' -ForegroundColor Green
Write-Host "  压缩包 : $zipPath"
Write-Host "  校验和 : $hashPath"
Write-Host "  解压后 : $fileCount 个文件 / $rawSize MB"
Write-Host "  压缩后 : $zipSize MB（耗时 $([Math]::Round($zipTimer.Elapsed.TotalSeconds, 0))s）"
Write-Host "  SHA256 : $hash"
Write-Host ''
Write-Host '  发出去之前建议：在干净目录解压一次、双击 Capsyn.exe 确认能起（脚本不做这一步，避免每次打包都启动应用）。' -ForegroundColor DarkGray

Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
