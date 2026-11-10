# 一键把 AutoInstall-Stack 推到 GitHub
#
# 用法（在仓库根目录执行）：
#   .\push-to-github.ps1 -Repo https://github.com/YOUR_NAME/AutoInstall-Stack.git -Token ghp_xxxxxxxx
#   .\push-to-github.ps1 -Repo https://github.com/YOUR_NAME/AutoInstall-Stack.git -Token ghp_xxx -Create
#   $env:GH_TOKEN="ghp_xxx"; .\push-to-github.ps1 -Repo https://github.com/YOUR_NAME/AutoInstall-Stack.git -Create
#
# -Create  会在 GitHub 上自动把仓库建出来（默认私有），需要 token 有 repo 权限
# 不传 -Token 时读环境变量 GH_TOKEN / GITHUB_TOKEN
param(
    [Parameter(Mandatory=$true)][string]$Repo,
    [string]$Token = $(if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }),
    [string]$Branch = "main",
    [switch]$Create,
    [switch]$Public
)

$ErrorActionPreference = "Stop"
function Say($m) { Write-Host "[push] $m" -ForegroundColor Cyan }
function Die($m) { Write-Host "[error] $m" -ForegroundColor Red; exit 1 }

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Die "本机没有 git" }
if (-not $Token) { Die "缺少 token：用 -Token ghp_xxx，或先 `$env:GH_TOKEN='ghp_xxx'" }

# https://github.com/USER/REPO.git -> USER/REPO
$m = [regex]::Match($Repo, 'github\.com[:/]+([^/]+)/([^/]+?)(?:\.git)?/?$')
if (-not $m.Success) { Die "看不懂这个仓库地址：$Repo（应形如 https://github.com/USER/REPO.git）" }
$owner = $m.Groups[1].Value
$name = $m.Groups[2].Value
Say "目标仓库：$owner/$name（分支 $Branch）"

if ($Create) {
    Say "调用 GitHub API 创建仓库…"
    $headers = @{ Authorization = "token $Token"; "User-Agent" = "autoinstall-stack"; Accept = "application/vnd.github+json" }
    $body = @{ name = $name; private = (-not $Public); description = "AutoInstall Stack: remote task-driven installer (backend + admin UI + client + control tools)" } | ConvertTo-Json
    try {
        $r = Invoke-RestMethod -Uri "https://api.github.com/user/repos" -Method Post -Headers $headers -Body $body -ContentType "application/json"
        Say "已创建：$($r.html_url)（$($r.visibility)）"
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match "already exists") { Say "仓库已存在，直接推送" }
        else { Die "创建失败：$msg" }
    }
}

Push-Location $PSScriptRoot
$bare = "https://github.com/$owner/$name.git"
$auth = "https://x-access-token:$Token@github.com/$owner/$name.git"

Say "设置 remote origin"
$existing = (git remote 2>$null) -split "`n" | ForEach-Object { $_.Trim() }
if ($existing -contains "origin") { git remote set-url origin $auth } else { git remote add origin $auth }

Say "推送 $Branch"
git push -u origin $Branch
if ($LASTEXITCODE -ne 0) { Pop-Location; Die "推送失败（检查 token 权限 / 仓库名）" }

# 推完把 remote 换回不带 token 的地址，避免 token 留在 .git/config
git remote set-url origin $bare
Pop-Location

Write-Host ""
Say "完成：$bare"
Say "分支：$Branch（remote 已改回不含 token 的 URL）"
