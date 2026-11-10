# 接口文档

Base URL：`http://<host>:3809`（可经 nginx 反代到 80/443）

所有接口返回 `application/json; charset=utf-8`，键名 **camelCase**（与客户端 `JavaScriptSerializer` 对齐）。

---

## 1. 拉取任务

```
POST /api/user/getAppInfos
Content-Type: application/x-www-form-urlencoded
User-Agent: auto_fix/HttpRequesterDemo
Body: key=hq
```

> `key` 不参与任何校验或过滤（与原后端一致），传什么都返回全表。

响应：

```json
{
  "success": [
    {
      "id": 25,
      "appName": "DemoApp-静默安装",
      "appVer": "2021",
      "appFirm": "Auto 3d Max",
      "appScript": "#filePath=1_Setup\\Setup.exe;...\nSET_PATH ...",
      "appPath": "",
      "appDirectory": "",
      "appPackageName": "DemoApp_MSI"
    }
  ],
  "message": "添加成功"
}
```

| 字段 | 说明 |
|---|---|
| `appPackageName` | 客户端在 **exe 同目录**下找的同名子目录；可写多个候选，逗号分隔（`CAD_2025,CAD2025`） |
| `appScript` | 安装脚本正文（见 README 的 DSL 一节） |

---

## 2. 授权码校验

```
POST /api/user/validate
Content-Type: application/json; charset=utf-8
Body: {"userName":"demo"}
```

响应（成功 / 失败 / 停用）：

```json
{"name":"demo","userId":1,"isValid":true,"message":"验证成功"}
{"name":"","userId":0,"isValid":false,"message":"用户不存在"}
{"name":"","userId":0,"isValid":false,"message":"用户已被停用"}
```

- 客户端界面上那个输入框就是"授权码"，实际是 `users.json` 里的 `userName`（无密码）。
- 返回 `name == "admin"` 时客户端会额外弹出调试控制台（F12 同款）。
- 非法 JSON → `400`（模型绑定失败）。

---

## 3. 安装上报

```
POST /api/user/register
Content-Type: application/json; charset=utf-8
Body:
{
  "userIP": "192.168.1.100",
  "computerName": "PC-TEST",
  "computerCode": "BFEBFBFF00000000-00000000",
  "product": "MyApp;OtherTool",
  "installPath": "C:\\Program Files\\MyApp;D:\\Other",
  "installDate": "2026-11-10T10:52:06.366Z",
  "userId": 1,
  "other": "【安装完成】\r\n开始执行脚本: ...\r\n[2/9] 执行: ..."
}
```

响应：

```json
{"success":true,"message":"登记成功","recordId":1}
{"success":false,"message":"用户不存在或已被停用","recordId":0}
```

- `computerCode` = `UPPER(CPU ProcessorId + "-" + C: 卷序列号)`，去空格。
- `userId` 必须存在于 `users.json` 且 `enabled=true`。
- `product` / `installPath` 是 **分号分隔的多值**（一次装多个软件时）。
- `other` 是客户端完整执行日志（逐条命令 + 结果）。

---

## 4. 占位页

```
GET /            -> 200 text/html（224 字节占位页，与原后端一致）
GET /Home/Index  -> 同上
GET /api/user/*  -> 405 Allow: POST
```

---

## 5. 管理台 API（需 `X-Token` 头或 `?token=`）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/admin` | 管理台页面（无 token → 302 `/admin/login`） |
| GET | `/admin/api/tasks` | `{"tasks":[...]}` |
| GET | `/admin/api/users` | `{"users":[...]}` |
| GET | `/admin/api/records` | `{"records":[...]}`（最多最近 500 条） |
| GET | `/admin/api/export` | 任务包 zip（`tasks.json` + `users.json` + `meta.json`） |
| POST | `/admin/api/import?mode=merge\|replace` | body 直接放 zip（或 tasks 数组 JSON）；merge 按 `appName+appVer` 覆盖 |
| POST | `/admin/api/task` | `{id|null, appName, appVer, appFirm, appPackageName, appScript}` |
| POST | `/admin/api/task/delete` | `{id}` |
| POST | `/admin/api/user` | `{id|null, userName, name, enabled, note}` |
| POST | `/admin/api/user/delete` | `{id}` |

导入返回示例：

```json
{"message":"导入完成（merge）：新增 0 条，更新 49 条","added":0,"updated":49,"total":49}
```
