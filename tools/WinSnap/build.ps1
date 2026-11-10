# 编译 WinSnap / CtlSpy（只用 Windows 自带的 .NET Framework 4 编译器）
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { Write-Error "找不到 csc.exe，请确认已安装 .NET Framework 4.x"; exit 1 }
Push-Location $PSScriptRoot
& $csc /nologo /target:winexe /out:WinSnap.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll WinSnap.cs
& $csc /nologo /target:winexe /out:CtlSpy.exe  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll CtlSpy.cs
Pop-Location
Write-Host "生成完成：WinSnap.exe / CtlSpy.exe" -ForegroundColor Green