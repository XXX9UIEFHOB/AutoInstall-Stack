# AutoInstall 装机系统 —— 自部署后端

原后端是 `http://203.0.113.10:3809`（**ASP.NET Core / Kestrel**，`GET /` 与 `/Home/Index` 返回同一个
224 字节的占位页，说明是 MVC 模板项目 + `UserController`）。线上没有暴露 Swagger / `.git` /
`appsettings.json` / 备份包，所以**拿不到它的源码或编译产物**。但它的接口契约已经被完整实测出来了，
这里给的是**行为等价、报文逐字节对齐**的可部署实现（`/api/user/getAppInfos` 响应长度与线上**同为 97,258 字节**）。

## 一、实测到的后端事实

| 项目 | 实测结果 |
|---|---|
| Web 服务器 | `Server: Kestrel`（ASP.NET Core） |
| 路由 | `/`, `/Home/Index` → 200 占位页；`/api/user/{getAppInfos,validate,register}` → 仅 POST，GET 一律 405 `Allow: POST` |
| `key` 参数 | **完全不校验**：`key=hq`、`key=`、`key=wrong` 都返回全部 49 个任务 |
| `validate` | 就是个**用户名存在性 oracle**，明文 JSON `{"userName":"..."}`，无密码 |
| 用户表（探到的） | `hq` → userId **4**、`name`=HQ；`admin` → userId **5**、`name`=admin；其它常见名不存在 |
| `register` | 校验 `userId` 必须存在且启用，否则 `{"success":false,"message":"用户不存在或已被停用","recordId":0}` |
| 反序列化 | 非法 JSON → `400`（ASP.NET Core 模型绑定默认行为） |

## 二、目录结构

```
backend/
├── server.py                 单文件服务（纯标准库，Python 3.8+，零依赖）
├── data/
│   ├── tasks.json            ★ 线上全量 49 个任务（原样导出，可直接用）
│   ├── users.json            授权用户（hq=4 / admin=5）
│   ├── records.jsonl         客户端 register 上报（每行一条，带完整执行日志）
│   └── access.log            请求流水
└── static/favicon.ico        原站图标（保持指纹一致）
    aspnet-kestrel/           可选：C# / Kestrel 版源码（需 dotnet 8 SDK 自行编译）
```

## 三、启动

```bash
# 最简（默认 0.0.0.0:3809，管理口令随机生成并打印在控制台）
python server.py

# 指定端口与口令
python server.py --port 3809 --token 你的口令

# Windows 常驻（nssm）
nssm install AutoInstallBackend "C:\Python311\python.exe" "C:\backend\server.py --port 3809 --token 你的口令"
nssm start AutoInstallBackend
```

Linux systemd：

```ini
[Unit]
Description=AutoInstall backend
After=network.target
[Service]
WorkingDirectory=/opt/backend
ExecStart=/usr/bin/python3 /opt/backend/server.py --port 3809 --token 你的口令
Restart=always
[Install]
WantedBy=multi-user.target
```

Docker：

```dockerfile
FROM python:3.12-slim
WORKDIR /app
COPY server.py ./
COPY data ./data
COPY static ./static
EXPOSE 3809
CMD ["python","server.py","--port","3809","--token","你的口令"]
```

## 四、管理台

浏览器打开 `http://<你的IP>:3809/admin?token=你的口令`（无口令会跳到登录页）：

- **安装任务**：增删改 `appName / appVer / appFirm / appPackageName / appScript`，脚本编辑器带行数与
  字符数统计；保存即生效（客户端下次拉取就是新版）。
- **授权用户**：这就是客户端要输入的"授权码"（= `userName`）；可停用（`enabled=false` 后 validate 返回"用户已被停用"）。
- **安装上报**：每条记录含 `userId / userIP / computerName / computerCode(CPU+硬盘指纹) / product /
  installPath / receivedAt / other(完整逐条执行日志)`。

管理 API（`X-Token` 头鉴权）：

```
GET  /admin/api/tasks|users|records
GET  /admin/api/export                     -> 任务包 zip（tasks.json + users.json + meta.json）
POST /admin/api/import?mode=merge|replace  -> body 直接放 zip（或 tasks 数组 JSON）
POST /admin/api/task            {id|null, appName, appVer, appFirm, appPackageName, appScript}
POST /admin/api/task/delete     {id}
POST /admin/api/user            {id|null, userName, name, enabled, note}
POST /admin/api/user/delete     {id}
```

管理台右上"安装任务"页还有两个按钮：**↓ 导出任务包(zip)** / **↑ 导入任务包**（合并 or 覆盖），
换机器、备份、批量搬数据都用它，不用手抄脚本。

配套工具（`tools/`）：

- `new_task.py` —— 扫安装包目录、自动算 MD5/指纹、生成脚本、语法校验、一键推送
- `WinSnap/WinSnap.exe` —— 控件抓取器：拖到安装窗口上松开，列出所有控件 ID 并生成 CLICK/SET_TEXT 片段
- `新增软件指南.md` —— 全流程说明（模板、变量、校验头、抓控件、检查清单）

## 五、让客户端连到你的后端

**方式 A：改字符串（不改代码）**

`AutoInstall.exe` 里 `WebSevice.ApiUrl` 是 `#US` 堆里的 UTF-16 字符串：

- 文件偏移 **`0x15DB9`**，原文 `http://203.0.113.10:3809`（23 字符，**必须等长替换**）
- 例：`http://127.0.0.1:38090/`（多一个 `/`，后端会把 `//api/...` 正常路由）
- 现成脚本：`../patch_local.py`

**方式 B：改源码重编译（推荐，见 `../AutoFix.Src/`）**

把 `Net.cs` 里的 `public static readonly string ApiUrl = "http://203.0.113.10:3809";` 改成你的地址，
用 `csc.exe` 重新编译即可，长度不受限制。

**客户端目录布局（必须）**

`FixTool.GetFolderPath()` 是在 **exe 所在目录**下找与 `appPackageName` 同名的子目录：

```
D:\装机包\
├── AutoInstall.exe
├── DemoApp_1.0\        ← appPackageName
│   ├── 1_Setup\Setup.exe
│   ├── 2_双击导入.reg
│   └── 4_授权文件\...
└── CAD_2025\                    ← 另一个 appPackageName
    └── 1_Setup\...
```

脚本里的 `%RUN_PATH%` 就是这个子目录，`%PATH%` 是安装目标目录，`%DISK%` 是所选磁盘（如 `C:\`）。

## 六、数据迁移

- `data/tasks.json` 就是线上 49 个任务的**原始导出**（97,258 字节响应反序列化后原样保存），
  你可以直接在管理台里改，也可以整包替换。
- 换用户表：编辑 `data/users.json`（`id` 不要重复；`userName` 即授权码）。

## 七、加固建议（很重要）

原站是**明文 HTTP + 无鉴权**，等于谁都能拉任务库、谁都能试用户名。自部署时至少：

1. 上 HTTPS（Caddy/Nginx 反代 + 证书），并把客户端 `ApiUrl` 改成 `https://`；
   注意客户端 `IgnoreInvalidCertificates=true`，自签证书也能用。
2. 给 `getAppInfos` 加真正的 `key` 校验（现版本保持"忽略 key"是为了和线上行为一致）。
3. 管理台口令用长随机串，绑定内网或加 IP 白名单。
4. 这个系统本身是"远程下发脚本以管理员权限执行"，**不要给它公网可写的入口**，
   任务库一旦被改，所有客户端就是现成的提权执行器。
