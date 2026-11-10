# AutoFix.Src —— 从 AutoInstall.exe 还原的 C# 工程

把 `AutoInstall.exe`（auto_fix，.NET 4.8）按 IL 逐模块还原成 **可直接编译运行** 的等价工程。
不是"注释式伪代码"，是能编出 exe、能被同一个 mock 服务端驱动的实现。

## 文件 / 模块对应

| 文件 | 原类型 | 说明 |
|---|---|---|
| `Core.cs` | `Program` + `Data.*` + `Model.WindowInfo` | 入口、7 个 JSON 数据契约、窗口模型 |
| `Net.cs` | `WebReqst.HttpRequester`(+`ResponseResult`)、`Services.WebSevice` | HTTP 客户端（含**关闭证书校验**）、三个业务接口 |
| `Tools.cs` | `Tools.FixTool`、`FileFingerprint`、`FileStableQuickFingerprint`、`ZipHelper` | 路径/磁盘/指纹/快捷方式/复制/内嵌 zip 加载 |
| `Native.cs` | `WinApi.WinInterac` | 41 个 P/Invoke、控件枚举/点击、键鼠、剪贴板 |
| `Engine.cs` | `Engine.ParsingCommand`(`:ParsingCommandEntity`)、`UnExpected`、`EngineThread` | 49 关键字 DSL 引擎 |
| `Forms.cs` | `Form1`、`Info_Form`、`InstallPathSelect` | 界面（纯代码构建，不需要 .resx） |

关键还原点（都来自 IL 证据）：

- `ParsingCommand` **继承** `ParsingCommandEntity`（TypeDef#20 extends ParsingCommandEntity），
  所以 `Starter.SetRunPath(...)` 与 `Starter.ProcessCommand(...)` 能在同一个字段上调用。
- `Form1.IsPassword` 把授权码以明文 JSON POST 到 `/api/user/validate`，并**同时写静态和实例的 userId**。
- `WebSevice.SubmitInfo` 绕开 `HttpRequester`，直接 `new HttpWebRequest`（所以三个请求的 UA 各不相同）。
- `FixTool.GetFolderPath` 是在 **exe 所在目录**下找同名子目录，不是开始菜单。
- `FixTool.CopyFile(src, dstDir)` 第二个参数是**目标目录**，实际落盘为 `dstDir\文件名`。
- `ZipHelper` 用 `EndsWith("Ionic.Zip.dll")` 在清单资源里找 DLL → 发布版没有该资源，必然抛异常。
- `EXIT` 命令 = `Environment.Exit(0)`（直接硬退，跳过 register 与自删除）。

## 编译

只用系统自带的 .NET Framework 4 编译器，无需 dotnet SDK / NuGet：

```powershell
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /out:AutoFix_rebuilt.exe `
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:System.Management.dll /r:System.Web.Extensions.dll `
  Core.cs Net.cs Tools.cs Native.cs Engine.cs Forms.cs
```

`System.Web.Extensions.dll` 提供 `JavaScriptSerializer`（原样本的 JSON 序列化器）。
生成 `AutoFix_rebuilt.exe`（82 KB，AnyCPU）；实测窗口标题、控件、请求序列与原始样本一致。

## 与原始样本的已知差异（不影响行为）

- 界面是用代码搭的，没有还原 `.resx` 里的图标与布局细节。
- `HttpRequester` 的头部集合、Cookie 容器、代理支持做了等价简化（原版也是薄封装）。
- `WinInterac` 保留了全部对外方法名与消息常量，内部实现按功能等价重写（原版 41 个 P/Invoke 中
  少数仅被死代码使用）。
- `Info_Form` 的调试控制台改为只读展示引擎 `PrintInfo` 增量（原版可手输命令）。

## 跑起来（配合 mock 服务端）

```powershell
# 1. 起 mock（端口 38090，接口与真实服务端一致）
python mock_server.py 38090
# 2. 把 ApiUrl 指向本地（任意等长替换脚本，或改 Net.cs 重新编译）
#    原始二进制里该字符串在文件偏移 0x15DB9（UTF-16LE）
python patch_local.py AutoInstall.exe AutoInstall_local.exe
# 3. exe 必须放在"安装包目录"的父目录里，子目录名 == AppPackageName
#    <root>\AutoInstall_local.exe
#    <root>\E2E-Demo\          ← appPackageName
# 4. 启动 → 输入任意授权码 → 点"开始"
```
