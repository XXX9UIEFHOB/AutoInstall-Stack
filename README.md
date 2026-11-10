# AutoInstall Stack

一套"远程任务下发 + 本地自动装机"的完整实现，含 **后端服务 / 管理台前端 / 客户端源码 / 抓控件与脚本工具**。

> 客户端是一个 **.NET 4.x WinForms 程序**：启动即 UAC 提权 → 向服务端拉任务 → 按任务里的脚本
> 在本机以管理员权限执行安装流程（支持静默参数、也支持模拟点击安装向导）→ 上报安装结果。
> 后端是一个 **零依赖单文件 Python 服务**（也可换 ASP.NET Core 版），自带管理台。

```
┌─────────────┐   ① POST /api/user/getAppInfos     ┌──────────────┐
│  客户端 exe  │ ─────────────────────────────────▶ │   后端服务    │
│ (管理员权限) │   ② POST /api/user/validate        │  server.py   │
│             │   ③ POST /api/user/register        │  + 管理台     │
└─────────────┘ ◀───────────────────────────────── └──────────────┘
       │ 执行任务脚本（msiexec / 向导点击 / 覆盖文件 / 导入注册表）
       ▼
   目标软件安装完成
```

## 目录

| 目录 | 内容 |
|---|---|
| `backend/` | 后端服务（`server.py` 单文件，零依赖）+ 种子数据 + Docker + 部署脚本（systemd / nginx） |
| `frontend/admin/` | 管理台前端（`index.html` / `static/admin.css` / `static/admin.js` / `login.html`） |
| `client/` | 客户端 C# 源码（按反汇编还原，可用系统自带 `csc.exe` 直接编译）+ 二进制补丁脚本 |
| `tools/` | `new_task.py`（生成/校验/推送任务）、`WinSnap` `CtlSpy`（抓控件 ID）、脚本模板 |
| `docs/` | 接口文档、逆向分析报告、部署指南；`docs/research/` 是任务库样本 |

## 快速开始

### 1) 起后端（30 秒）

```bash
cd backend
python3 server.py --port 3809 --token 你的口令
#   客户端接口 http://<ip>:3809/api/user/{getAppInfos,validate,register}
#   管理台     http://<ip>:3809/admin?token=你的口令
```

数据在 `backend/data/`：`tasks.json`（任务，已带 2 条演示任务）`users.json`（授权码）`records.jsonl`（上报）。

Docker 也行：

```bash
cd backend && docker compose up -d
```

### 2) 编译客户端

```powershell
cd client
& C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:AutoInstall.exe `
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:System.Management.dll /r:System.Web.Extensions.dll `
  Core.cs Net.cs Tools.cs Native.cs Engine.cs Forms.cs
```

改服务器地址：编辑 `client/Net.cs` 里的

```csharp
public static readonly string ApiUrl = "http://127.0.0.1:3809";
```

或者用 `client/patch_local.py` 直接改已编译 exe 里的字符串（偏移 `0x15DB9`，UTF-16，等长替换）。

### 3) 客户端目录布局（必须）

客户端在 **exe 所在目录**下找与任务 `appPackageName` 同名的子目录作为安装包目录：

```
D:\装机包\
├── AutoInstall.exe
├── MyApp_1.0\             ← appPackageName
│   └── 1_Setup\Setup.exe
└── CAD_2025\
    └── 1_Setup\...
```

### 4) 加一款新软件

1. `tools/WinSnap/WinSnap.exe` 拖到安装向导上，抓控件 ID（或 `CtlSpy.exe` 鼠标一指）；
2. 照 `tools/完整脚本模板.txt` 拼脚本；
3. `python tools/new_task.py make --dir "D:\装机包\MyApp_1.0" --name MyApp --template setup --server http://YOUR_HOST:3809 --token 口令`
4. 客户端跑一遍，管理台「安装上报」里看逐条日志。

## 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/user/getAppInfos` | 返回全部任务（form `key=hq`，与线上行为一致：不校验 key） |
| POST | `/api/user/validate` | `{"userName":"..."}` → `{name,userId,isValid,message}`；"授权码"就是用户名 |
| POST | `/api/user/register` | 安装上报（IP/主机名/CPU+硬盘指纹/路径/产品/完整日志） |
| GET | `/` `/Home/Index` | 占位页（与原后端指纹一致） |
| GET | `/admin` | 管理台（任务增删改 / 授权用户 / 上报记录 / 任务包导入导出） |
| GET | `/admin/api/export` | 导出任务包 zip |
| POST | `/admin/api/import?mode=merge\|replace` | 导入任务包 |

详见 [`docs/API.md`](docs/API.md)。

## 脚本 DSL（客户端内置解释器）

49 个关键字，逐行执行；`//`、`#` 开头是注释；第一行是校验头。

```
#filePath=1_Setup\Setup.exe;md5=<32位>;appName=MyApp;computer=strict;verify=<FNV1a>
SET_PATH 'Program Files\MyApp'
RUN_A 'start "" "%RUN_PATH%\1_Setup\Setup.exe"'
WHILE_WIN '安装 - MyApp' 60
SET_WIN '安装 - MyApp'
CLICK_TEXT 我接受
SET_TEXT 4 PATH
CLICK 1
WHILE_TEXT 完成 200
CLICK 1
```

常用指令：`SET_WIN / WHILE_WIN / WHILE_TEXT / CLICK / CLICK_TEXT / CLICK_RELATIVE / SET_TEXT /
RUN / RUN_A / RUN_INFO / ADI / COPY_FILE(S) / ZIP / KILL / KEY / SLEEP / READ_PACE / PRINT / EXIT`。
变量：`%RUN_PATH%` `%PATH%` `%DISK%` `%TEMP%` `%APP_PATH%`。完整说明见 `tools/新增软件指南.md`。

## 部署

`backend/deploy/` 里有一套现成脚本：

- `install.sh` —— Linux 一键（systemd / nohup 兜底 + 自检）
- `deploy.ps1` —— Windows 侧一键（打包 → scp → 远端安装 → 本地验收 → 可选重编客户端）
- `nginx_proxy.sh` —— 把 `/api/`、`/admin` 反代到 3809（云安全组没开端口时用）

详见 [`docs/部署指南.md`](docs/部署指南.md)。

## 安全注意事项

- 后台默认 **明文 HTTP + 口令放进 query**，公网部署请套 HTTPS（Caddy/Nginx）并给 `/admin` 加 IP 白名单。
- 客户端 `IgnoreInvalidCertificates = true`，自签证书可用。
- **任务库 = 所有客户端的执行权**：能改任务的人就能在所有终端上以管理员权限跑任意命令，务必备份 + 收紧写权限。
- `data/records.jsonl` 含机器指纹与完整执行日志，注意合规与保存期限。

## 研究资料

`docs/AutoInstall_逆向分析报告.md` 是对原样本的完整逆向（PE/CLR 结构、IL、协议、DSL、缺陷），
`docs/research/` 是抓取到的任务库样本（49 款 CAD/CAE 的自动化装机脚本）。
