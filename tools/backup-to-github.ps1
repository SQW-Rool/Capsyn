<#
.SYNOPSIS
    把 Capsyn 源码提交并推送到 GitHub 仓库。

.DESCRIPTION
    仓库：https://github.com/SQW-Rool/Capsyn
    （原来叫 Capsyn-backup，改名后旧地址会自动跳转；本地 clone 想换过来就执行一次
      git remote set-url origin https://github.com/SQW-Rool/Capsyn.git）

    只提交源码与配置：bin / obj / .vs / .tools 都已在 .gitignore 里忽略。
    每次改动验证通过后执行一次，GitHub 上的提交历史就是回滚参考；
    建议同时打 tag（例如 v0.3.0-ProjectChange），回滚时直接 checkout 对应 tag。

.EXAMPLE
    cd G:\Capsyn
    powershell -ExecutionPolicy Bypass -File .\tools\backup-to-github.ps1 -Message "加入通知队列" -Tag v0.3.1-Notifications

.EXAMPLE
    # 只提交推送，不打 tag
    powershell -ExecutionPolicy Bypass -File .\tools\backup-to-github.ps1 -Message "微调胶囊圆角"
#>
[CmdletBinding()]
param(
    # 提交说明
    [Parameter(Mandatory = $true)]
    [string]$Message,

    # 可选：打完 tag 一起推送（回滚锚点）
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
Set-Location 'G:\Capsyn'

Write-Host '== git status ==' -ForegroundColor Cyan
git status --short

Write-Host '== git add -A ==' -ForegroundColor Cyan
git add -A

Write-Host '== git commit ==' -ForegroundColor Cyan
git commit -m $Message
if ($LASTEXITCODE -ne 0) {
    Write-Host '没有新的改动需要提交（继续推送）。' -ForegroundColor Yellow
}

if ($Tag) {
    Write-Host "== git tag $Tag ==" -ForegroundColor Cyan
    git tag -a $Tag -m $Tag
}

Write-Host '== git push origin main ==' -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host '推送失败：请检查网络/凭据（git config credential.helper=manager）。' -ForegroundColor Red
    exit 1
}

if ($Tag) {
    Write-Host "== git push origin $Tag ==" -ForegroundColor Cyan
    git push origin $Tag
}

Write-Host '最近提交：' -ForegroundColor Cyan
git log --oneline -n 5
Write-Host '提交推送完成。' -ForegroundColor Green
