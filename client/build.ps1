# 编译客户端（改 ApiUrl 后重新编译）
param(
    [string]$ApiUrl = "",                 # 例如 http://YOUR_HOST:3809
    [string]$Out    = "AutoInstall.exe"
)
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { Write-Error "找不到 csc.exe"; exit 1 }
Push-Location $PSScriptRoot
if ($ApiUrl) {
    $net = Join-Path $PSScriptRoot "Net.cs"
    $t = Get-Content $net -Raw -Encoding UTF8
    $t = [regex]::Replace($t, 'public static readonly string ApiUrl = "[^"]*";',
                          "public static readonly string ApiUrl = `"$ApiUrl`";")
    [IO.File]::WriteAllText($net, $t, (New-Object Text.UTF8Encoding($false)))
    Write-Host "ApiUrl -> $ApiUrl" -ForegroundColor Cyan
}
& $csc /nologo /target:winexe /out:$Out /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll `
      /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll `
      Core.cs Net.cs Tools.cs Native.cs Engine.cs Forms.cs
Pop-Location
Write-Host "生成完成：$Out" -ForegroundColor Green