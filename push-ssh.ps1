# 通过 SSH 推送（先把 ~/.ssh/id_ed25519_github.pub 加到 GitHub，并建好空仓库）
param(
    [string]$Owner = "XXX9UIEFHOB",
    [string]$Name  = "AutoInstall-Stack",
    [string]$Branch = "main"
)
$ErrorActionPreference = "Continue"
function Say($m){ Write-Host "[push] $m" -ForegroundColor Cyan }
function Die($m){ Write-Host "[error] $m" -ForegroundColor Red; exit 1 }

Say "1/4 测试 GitHub SSH 认证（端口 443）"
$t = & ssh -T -o BatchMode=yes -o ConnectTimeout=10 git@github.com 2>&1
$t | ForEach-Object { "    $_" }
if ($t -match "successfully authenticated") {
    Say "    认证 OK"
} else {
    Die "认证失败：公钥还没加到 GitHub。把下面这行加到 https://github.com/settings/ssh/new`n`n$((Get-Content "$env:USERPROFILE\.ssh\id_ed25519_github.pub" -Raw).Trim())`n"
}

Say "2/4 检查仓库是否存在"
try {
    Invoke-RestMethod "https://api.github.com/repos/$Owner/$Name" -Headers @{ "User-Agent" = "ai" } -TimeoutSec 10 | Out-Null
    Say "    仓库已存在"
} catch {
    Die "仓库 $Owner/$Name 不存在。先去 https://github.com/new?name=$Name&visibility=private 建一个空仓库（不要勾 README）"
}

Say "3/4 配置 remote"
Push-Location $PSScriptRoot
$url = "git@github.com:$Owner/$Name.git"
$remotes = (git remote) -split "`n" | ForEach-Object { $_.Trim() }
if ($remotes -contains "origin") { git remote set-url origin $url } else { git remote add origin $url }
Say "    origin = $url"

Say "4/4 推送 $Branch"
git push -u origin $Branch
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Die "推送失败（把上面的报错发我）" }
Write-Host ""
Say "完成：https://github.com/$Owner/$Name"