# AutoInstall 后端 —— Windows 侧一键部署（SSH/scp）
#
# 用法示例：
#   .\deploy.ps1 -Server 1.2.3.4 -User root -KeyFile C:\keys\id_ed25519 -Token MyPass123
#   .\deploy.ps1 -Server 1.2.3.4 -User ubuntu -KeyFile .\id_rsa -RemotePort 8080 -OpenFirewall
#   .\deploy.ps1 -Server 1.2.3.4 -KeyFile .\id_rsa -BuildClient          # 顺便生成指向该后端的客户端
#
param(
    [Parameter(Mandatory=$true)][string]$Server,
    [int]$SshPort = 22,
    [string]$User = "root",
    [string]$KeyFile,
    [int]$RemotePort = 3809,
    [string]$Token,
    [string]$RemoteDir = "/opt/autoinstall-backend",
    [switch]$OpenFirewall,
    [switch]$BuildClient,
    [string]$BuildClientOut
)

$ErrorActionPreference = "Stop"
$here    = $PSScriptRoot                                   # ...\backend\deploy
$backend = Split-Path $here -Parent                        # ...\backend
$re      = Split-Path $backend -Parent                     # ...\re
$src     = Join-Path $re "AutoFix.Src"
$work    = Join-Path $env:TEMP ("ai-deploy-" + [guid]::NewGuid().ToString("N").Substring(0,8))

function Say($m){ Write-Host "[deploy] $m" -ForegroundColor Cyan }
function Die($m){ Write-Host "[error] $m" -ForegroundColor Red; exit 1 }

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) { Die "本机没有 ssh.exe" }
if (-not (Get-Command tar -ErrorAction SilentlyContinue)) { Die "本机没有 tar.exe" }
if (-not $Token) { $Token = -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 16 | % {[char]$_}) ; Say "未指定 -Token，随机生成：$Token" }

# ---------- 1. 打包 payload ----------
New-Item -ItemType Directory -Force -Path $work | Out-Null
Say "打包 payload -> $work\payload.tar.gz"
Copy-Item (Join-Path $here "install.sh") $work
& tar -czf "$work\payload.tar.gz" -C $backend server.py data static
if ($LASTEXITCODE -ne 0) { Die "tar 打包失败" }

# ---------- 2. SSH 参数 ----------
$sshOpts = @("-o", "StrictHostKeyChecking=accept-new", "-o", "UserKnownHostsFile=$env:USERPROFILE\.ssh\known_hosts")
if ($KeyFile) {
    if (-not (Test-Path $KeyFile)) { Die "私钥不存在: $KeyFile" }
    $sshOpts += @("-i", $KeyFile, "-o", "IdentitiesOnly=yes")
}
$sshArgs = @("-p", "$SshPort") + $sshOpts
$scpArgs = @("-P", "$SshPort") + $sshOpts
$target = "$User@$Server"

Say "连通性测试: $target (port $SshPort)"
& ssh @sshArgs $target "echo OK; uname -a; python3 -V || echo 'NO_PYTHON3'"
if ($LASTEXITCODE -ne 0) { Die "SSH 连接失败（检查 IP/端口/用户/私钥权限）" }

# ---------- 3. 上传 ----------
Say "上传 payload"
& scp @scpArgs "$work\payload.tar.gz" "$work\install.sh" "${target}:/tmp/"
if ($LASTEXITCODE -ne 0) { Die "scp 上传失败" }

# ---------- 4. 远端安装 ----------
$fw = if ($OpenFirewall) { "--open-firewall" } else { "" }
$remoteCmd = "set -e; rm -rf /tmp/ai-deploy; mkdir -p /tmp/ai-deploy; " +
             "tar xzf /tmp/payload.tar.gz -C /tmp/ai-deploy; cp /tmp/install.sh /tmp/ai-deploy/; " +
             "cd /tmp/ai-deploy; bash install.sh --dir '$RemoteDir' --port $RemotePort --token '$Token' $fw"
Say "远端安装"
if ($User -eq "root") {
    & ssh @sshArgs $target $remoteCmd
} else {
    & ssh @sshArgs $target "sudo bash -c '$remoteCmd'"
}
if ($LASTEXITCODE -ne 0) { Die "远端安装失败，看上面的输出" }

# ---------- 5. 本地验收 ----------
Say "本地验收 http://${Server}:$RemotePort"
$ok = $false
for ($i=0; $i -lt 12; $i++) {
    try {
        $r = Invoke-WebRequest "http://${Server}:$RemotePort/api/user/getAppInfos" -Method POST `
             -Body "key=hq" -ContentType "application/x-www-form-urlencoded" `
             -UserAgent "auto_fix/HttpRequesterDemo" -TimeoutSec 8 -UseBasicParsing
        $j = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray()) | ConvertFrom-Json
        "getAppInfos  OK  任务数 = {0}" -f $j.success.Count | Write-Host
        $ok = $true; break
    } catch { Start-Sleep -Seconds 2 }
}
if (-not $ok) { Write-Host "[warn] 本地连不上 http://${Server}:$RemotePort —— 可能是云安全组/防火墙没放行" -ForegroundColor Yellow }

# ---------- 6. 可选：生成指向该后端的客户端 ----------
if ($BuildClient) {
    if (-not (Test-Path $src)) { Die "找不到 $src" }
    $bc = Join-Path $work "client-src"
    Copy-Item $src $bc -Recurse
    $apiUrl = "http://${Server}:$RemotePort"
    $net = Join-Path $bc "Net.cs"
    $txt = Get-Content $net -Raw -Encoding UTF8
    $txt = [regex]::Replace($txt, 'public static readonly string ApiUrl = "[^"]*";',
        "public static readonly string ApiUrl = `"$apiUrl`";")
    Set-Content $net -Value $txt -Encoding UTF8 -NoNewline
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    Push-Location $bc
    & $csc /nologo /target:winexe /out:AutoInstall_custom.exe /r:System.dll /r:System.Core.dll `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll `
        /r:System.Web.Extensions.dll Core.cs Net.cs Tools.cs Native.cs Engine.cs Forms.cs | Out-Null
    Pop-Location
    $outExe = if ($BuildClientOut) { $BuildClientOut } else { Join-Path $re "AutoInstall_$($Server -replace '[^\w\.]','_').exe" }
    Copy-Item (Join-Path $bc "AutoInstall_custom.exe") $outExe -Force
    Say "客户端已生成: $outExe  (ApiUrl=$apiUrl)"
    Say "投放目录要求: <root>\AutoInstall_custom.exe + <root>\<appPackageName>\"
}

# ---------- 7. 收尾 ----------
Write-Host ""
Write-Host "=============================================================="
Write-Host " 客户端接口 : http://${Server}:$RemotePort/api/user/{getAppInfos,validate,register}"
Write-Host " 管理台     : http://${Server}:$RemotePort/admin?token=$Token"
Write-Host " 管理口令   : $Token"
Write-Host " 远端目录   : $RemoteDir/data"
Write-Host "=============================================================="
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
