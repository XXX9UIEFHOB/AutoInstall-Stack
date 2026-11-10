#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AutoInstall 装机系统 —— 可自部署后端（单文件、纯标准库、零依赖）

复刻线上 http://203.0.113.10:3809 的三个接口（已按实测报文对齐 JSON 键名与大小写）：

  POST /api/user/getAppInfos   form: key=hq           -> {"success":[AppInfo...],"message":"添加成功"}
  POST /api/user/validate      json: {"userName":..}  -> {"name","userId","isValid","message"}
  POST /api/user/register      json: 安装登记八字段     -> {"success","message","recordId"}
  GET  /  | /Home/Index                               -> 与线上一致的 224 字节占位页
  GET  /favicon.ico                                   -> 原站图标（可选）

额外自带一个管理台（默认 /admin），可以增删改任务、用户，查看客户端上报记录。

启动：
    python server.py --port 3809 --token 你的管理口令
    python server.py --port 3809 --token abc --host 0.0.0.0
数据目录默认 ./data（tasks.json / users.json / records.jsonl / access.log）。
"""
import argparse, io, json, os, re, secrets, sys, threading, time, urllib.parse, zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOCK = threading.Lock()
CFG = {}
INDEX_HTML = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Index</title>
</head>
<body>
    error
</body>
</html>"""


# ----------------------------------------------------------------- 存储
def _path(name):
    return os.path.join(CFG["data"], name)


def load_json(name, default):
    try:
        with open(_path(name), "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def save_json(name, obj):
    tmp = _path(name) + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)
    os.replace(tmp, _path(name))


def next_id(items):
    return max([int(i.get("id", 0)) for i in items] + [0]) + 1


def append_log(line):
    try:
        with open(_path("access.log"), "a", encoding="utf-8") as f:
            f.write("[%s] %s\n" % (time.strftime("%Y-%m-%d %H:%M:%S"), line))
    except Exception:
        pass


def append_record(rec):
    with open(_path("records.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(rec, ensure_ascii=False) + "\n")


# ----------------------------------------------------------------- 业务
def api_get_app_infos(payload):
    """线上实测：key 不参与任何校验/过滤，传什么都返回全表。"""
    with LOCK:
        tasks = load_json("tasks.json", [])
    return {"success": tasks, "message": "添加成功"}


def api_validate(payload):
    name = (payload or {}).get("userName") or ""
    with LOCK:
        users = load_json("users.json", [])
    for u in users:
        if u.get("userName") == name:
            if not u.get("enabled", True):
                return {"name": "", "userId": 0, "isValid": False, "message": "用户已被停用"}
            return {"name": u.get("name") or name, "userId": u.get("id", 0),
                    "isValid": True, "message": "验证成功"}
    return {"name": "", "userId": 0, "isValid": False, "message": "用户不存在"}


def api_register(payload):
    payload = payload or {}
    uid = int(payload.get("userId") or 0)
    with LOCK:
        users = load_json("users.json", [])
        found = next((u for u in users if int(u.get("id", 0)) == uid), None)
        if not found or not found.get("enabled", True):
            return {"success": False, "message": "用户不存在或已被停用", "recordId": 0}
        records = None  # 计数走 records.jsonl 行数
        try:
            with open(_path("records.jsonl"), "rb") as f:
                rid = sum(1 for _ in f) + 1
        except FileNotFoundError:
            rid = 1
        rec = dict(payload)
        rec["recordId"] = rid
        rec["receivedAt"] = time.strftime("%Y-%m-%d %H:%M:%S")
        append_record(rec)
    return {"success": True, "message": "登记成功", "recordId": rid}


# ----------------------------------------------------------------- 管理台
ADMIN_HTML = r"""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8"><title>装机任务管理台</title>
<style>
 body{font:14px/1.6 "Segoe UI",system-ui,"Microsoft YaHei";margin:0;background:#f5f6f8;color:#222}
 header{background:#1f2937;color:#fff;padding:12px 18px;display:flex;justify-content:space-between;align-items:center}
 header h1{font-size:16px;margin:0;font-weight:600}
 .wrap{padding:16px 18px;max-width:1400px}
 .tabs{display:flex;gap:8px;margin-bottom:12px}
 .tabs button{padding:6px 14px;border:1px solid #d0d5dd;background:#fff;border-radius:6px;cursor:pointer}
 .tabs button.on{background:#2563eb;color:#fff;border-color:#2563eb}
 table{width:100%;border-collapse:collapse;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.08)}
 th,td{padding:8px 10px;border-bottom:1px solid #eef1f4;text-align:left;vertical-align:top;font-size:13px}
 th{background:#f8fafc;font-weight:600}
 tr:hover td{background:#fafcff}
 textarea{width:100%;min-height:90px;font:12px/1.5 Consolas,monospace}
 input[type=text],input[type=number]{padding:5px 8px;border:1px solid #d0d5dd;border-radius:5px;width:100%;box-sizing:border-box}
 .btn{padding:5px 12px;border:1px solid #2563eb;background:#2563eb;color:#fff;border-radius:5px;cursor:pointer}
 .btn.g{background:#fff;color:#2563eb}
 .btn.r{background:#fff;color:#dc2626;border-color:#dc2626}
 .muted{color:#6b7280;font-size:12px}
 #toast{position:fixed;right:18px;bottom:18px;background:#111827;color:#fff;padding:10px 14px;border-radius:8px;display:none}
 dialog{border:none;border-radius:10px;padding:0;width:min(900px,92vw)}
 dialog .hd{padding:12px 16px;border-bottom:1px solid #eee;font-weight:600}
 dialog .bd{padding:14px 16px;max-height:70vh;overflow:auto}
 dialog .ft{padding:12px 16px;border-top:1px solid #eee;text-align:right;display:flex;gap:8px;justify-content:flex-end}
 label{display:block;margin:8px 0 4px;font-size:12px;color:#374151}
</style></head>
<body>
<header><h1>装机任务管理台 <span class="muted" style="color:#9ca3af">(AutoInstall backend)</span></h1>
<div><span id="who" class="muted" style="color:#cbd5e1"></span> <button class="btn g" onclick="logout()">退出</button></div></header>
<div class="wrap">
 <div class="tabs">
   <button id="tb-tasks" class="on" onclick="tab('tasks')">安装任务</button>
   <button id="tb-users" onclick="tab('users')">授权用户</button>
   <button id="tb-records" onclick="tab('records')">安装上报</button>
 </div>
 <div id="view"></div>
</div>
<dialog id="dlg"><div class="hd" id="dlgTitle"></div><div class="bd" id="dlgBody"></div>
 <div class="ft"><button class="btn g" onclick="dlg.close()">取消</button><button class="btn" id="dlgOk">保存</button></div></dialog>
<div id="toast"></div>
<script>
const TOKEN = new URLSearchParams(location.search).get('token') || localStorage.getItem('tk') || '';
if (TOKEN) localStorage.setItem('tk', TOKEN);
const api = async (p, opt={}) => {
  const r = await fetch(p, {headers:{'X-Token':TOKEN,'Content-Type':'application/json'}, ...opt});
  if (r.status === 401) { location.href='/admin/login'; throw new Error('unauth'); }
  return r.json();
};
const esc = s => (s==null?'':String(s)).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const toast = m => { const t=document.getElementById('toast'); t.textContent=m; t.style.display='block'; setTimeout(()=>t.style.display='none',2200); };
let CUR='tasks', TASKS=[], USERS=[], RECORDS=[];
function tab(n){ CUR=n; ['tasks','users','records'].forEach(x=>document.getElementById('tb-'+x).className = x===n?'on':''); render(); }
async function reload(){
  TASKS = (await api('/admin/api/tasks')).tasks || [];
  USERS = (await api('/admin/api/users')).users || [];
  RECORDS = (await api('/admin/api/records')).records || [];
  document.getElementById('who').textContent = '任务 '+TASKS.length+' / 用户 '+USERS.length+' / 上报 '+RECORDS.length;
  render();
}
function render(){
  const v=document.getElementById('view');
  if(CUR==='tasks'){
    v.innerHTML = `<p>
      <button class="btn" onclick="editTask(null)">+ 新建任务</button>
      <a class="btn g" style="text-decoration:none;display:inline-block" href="/admin/api/export?token=${TOKEN}">↓ 导出任务包(zip)</a>
      <select id="impMode" style="padding:5px;border:1px solid #d0d5dd;border-radius:5px">
        <option value="merge">合并（按 软件名+版本 覆盖同名）</option>
        <option value="replace">覆盖（清空后导入）</option>
      </select>
      <button class="btn g" onclick="document.getElementById('impFile').click()">↑ 导入任务包</button>
      <input type="file" id="impFile" accept=".zip,.json" style="display:none" onchange="doImport(this)">
      <a class="btn g" style="text-decoration:none;display:inline-block" href="/admin/api/records?token=${TOKEN}" onclick="return false" title="用下面的上报页查看">·</a>
      <span class="muted">客户端按 <b>appPackageName</b> 在 exe 同目录找同名子目录；脚本第一行是 <code>#filePath=..;md5=..;appName=..</code> 校验头</span></p>
      <table><tr><th style="width:60px">ID</th><th style="width:230px">软件</th><th>版本/厂商</th><th>包名</th><th>脚本</th><th style="width:130px">操作</th></tr>` +
      TASKS.map(t=>`<tr><td>${t.id}</td><td><b>${esc(t.appName)}</b></td>
        <td>${esc(t.appVer)}<div class="muted">${esc(t.appFirm)}</div></td>
        <td><code>${esc(t.appPackageName)}</code></td>
        <td class="muted">${(t.appScript||'').split('\n').length} 行 / ${(t.appScript||'').length} 字符</td>
        <td><button class="btn g" onclick="editTask(${t.id})">编辑</button>
            <button class="btn r" onclick="delTask(${t.id})">删除</button></td></tr>`).join('') + '</table>';
  }
  if(CUR==='users'){
    v.innerHTML = `<p><button class="btn" onclick="editUser(null)">+ 新建授权码</button>
      <span class="muted">客户端输入的"授权码"= 这里的 userName（线上原站也是明文用户名，无密码）</span></p>
      <table><tr><th style="width:60px">ID</th><th>userName（授权码）</th><th>显示名</th><th>启用</th><th>备注</th><th style="width:130px">操作</th></tr>` +
      USERS.map(u=>`<tr><td>${u.id}</td><td><code>${esc(u.userName)}</code></td><td>${esc(u.name)}</td>
        <td>${u.enabled?'✅':'⛔'}</td><td class="muted">${esc(u.note||'')}</td>
        <td><button class="btn g" onclick="editUser(${u.id})">编辑</button>
            <button class="btn r" onclick="delUser(${u.id})">删除</button></td></tr>`).join('') + '</table>';
  }
  if(CUR==='records'){
    v.innerHTML = `<p class="muted">客户端安装完成后 POST /api/user/register 的全部记录（含机器指纹与完整执行日志）</p>
      <table><tr><th style="width:50px">#</th><th>时间</th><th>授权码ID</th><th>主机</th><th>指纹</th><th>产品</th><th>路径</th><th>日志</th></tr>` +
      RECORDS.slice().reverse().map((r,i)=>`<tr><td>${r.recordId||''}</td><td class="muted">${esc(r.receivedAt)}</td>
        <td>${r.userId}</td><td>${esc(r.computerName)}<div class="muted">${esc(r.userIP)}</div></td>
        <td><code style="font-size:11px">${esc(r.computerCode)}</code></td>
        <td>${esc(r.product)}</td><td class="muted">${esc(r.installPath)}</td>
        <td><details><summary class="muted">展开</summary><pre style="white-space:pre-wrap;font-size:11px">${esc(r.other)}</pre></details></td></tr>`).join('') + '</table>';
  }
}
async function doImport(inp){
  const f = inp.files[0]; if(!f) return;
  const mode = document.getElementById('impMode').value;
  if(mode === 'replace' && !confirm('覆盖模式会先清空现有任务，确定继续？')){ inp.value=''; return; }
  toast('导入中…');
  try{
    const r = await fetch('/admin/api/import?mode='+mode, {method:'POST', headers:{'X-Token':TOKEN}, body:f});
    const j = await r.json();
    toast(j.message || '导入完成');
  }catch(e){ toast('导入失败: '+e); }
  inp.value=''; reload();
}
const dlg=document.getElementById('dlg');
function editTask(id){
  const t = id==null ? {id:'',appName:'',appVer:'',appFirm:'',appPackageName:'',appDirectory:'',appScript:'#filePath=1_Setup\\Setup.exe;appName=;computer=strict\nPRINT 开始安装\nSET_PATH \'Program Files\\你的软件\'\nRUN_INFO \'%RUN_PATH%\\1_Setup\\Setup.exe\' \'/S\'\nSLEEP 3000\nPRINT 安装完成'} : TASKS.find(x=>x.id===id);
  document.getElementById('dlgTitle').textContent = id==null ? '新建任务' : '编辑任务 #'+id;
  document.getElementById('dlgBody').innerHTML = `
    <label>软件名 appName</label><input type="text" id="f_appName" value="${esc(t.appName)}">
    <label>版本 appVer</label><input type="text" id="f_appVer" value="${esc(t.appVer)}">
    <label>厂商 appFirm</label><input type="text" id="f_appFirm" value="${esc(t.appFirm)}">
    <label>包名 appPackageName（逗号可写多个候选目录名）</label><input type="text" id="f_appPackageName" value="${esc(t.appPackageName)}">
    <label>安装脚本 appScript（第一行必须是 #key=value;... 校验头）</label>
    <textarea id="f_appScript" style="min-height:340px">${esc(t.appScript)}</textarea>`;
  document.getElementById('dlgOk').onclick = async () => {
    const body = {id: id==null? null : id};
    ['appName','appVer','appFirm','appPackageName','appScript'].forEach(k=>body[k]=document.getElementById('f_'+k).value);
    const r = await api('/admin/api/task', {method:'POST', body: JSON.stringify(body)});
    dlg.close(); toast(r.message||'已保存'); reload();
  };
  dlg.showModal();
}
async function delTask(id){ if(!confirm('删除任务 #'+id+' ?')) return; await api('/admin/api/task/delete',{method:'POST',body:JSON.stringify({id})}); reload(); toast('已删除'); }
function editUser(id){
  const u = id==null ? {id:'',userName:'',name:'',enabled:true,note:''} : USERS.find(x=>x.id===id);
  document.getElementById('dlgTitle').textContent = id==null ? '新建授权码' : '编辑用户 #'+id;
  document.getElementById('dlgBody').innerHTML = `
    <label>userName（客户端输入的授权码）</label><input type="text" id="u_userName" value="${esc(u.userName)}">
    <label>显示名 name</label><input type="text" id="u_name" value="${esc(u.name)}">
    <label>备注</label><input type="text" id="u_note" value="${esc(u.note||'')}">
    <label><input type="checkbox" id="u_enabled" ${u.enabled?'checked':''}> 启用</label>`;
  document.getElementById('dlgOk').onclick = async () => {
    const body = {id: id==null? null : id,
      userName: document.getElementById('u_userName').value,
      name: document.getElementById('u_name').value,
      note: document.getElementById('u_note').value,
      enabled: document.getElementById('u_enabled').checked};
    const r = await api('/admin/api/user', {method:'POST', body: JSON.stringify(body)});
    dlg.close(); toast(r.message||'已保存'); reload();
  };
  dlg.showModal();
}
async function delUser(id){ if(!confirm('删除用户 #'+id+' ?')) return; await api('/admin/api/user/delete',{method:'POST',body:JSON.stringify({id})}); reload(); toast('已删除'); }
function logout(){ localStorage.removeItem('tk'); location.href='/admin/login'; }
reload();
</script>
</body></html>"""

LOGIN_HTML = """<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>登录 - 管理台</title>
<style>body{font:14px system-ui,"Microsoft YaHei";display:flex;height:100vh;align-items:center;justify-content:center;background:#f5f6f8}
.box{background:#fff;padding:26px 28px;border-radius:12px;box-shadow:0 6px 24px rgba(0,0,0,.08);width:320px}
h1{font-size:16px;margin:0 0 16px}input{width:100%;padding:9px 10px;border:1px solid #d0d5dd;border-radius:6px;box-sizing:border-box}
button{margin-top:14px;width:100%;padding:9px;border:0;border-radius:6px;background:#2563eb;color:#fff;cursor:pointer}
.err{color:#dc2626;font-size:12px;margin-top:10px;min-height:16px}</style></head>
<body><form class="box" method="post" action="/admin/login">
<h1>装机任务管理台</h1><input type="password" name="token" placeholder="管理口令" autofocus>
<button type="submit">进入</button><div class="err">%ERR%</div></form></body></html>"""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "Kestrel"
    sys_version = ""

    # ---------------- 基础 ----------------
    def _send(self, code, body=b"", ctype="application/json; charset=utf-8", extra=None):
        if isinstance(body, (dict, list)):
            body = json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        elif isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _read_body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def _payload(self, raw):
        ct = (self.headers.get("Content-Type") or "").lower()
        txt = raw.decode("utf-8", "replace")
        if "json" in ct:
            try:
                return json.loads(txt) if txt.strip() else {}
            except Exception:
                return None
        q = urllib.parse.parse_qs(txt)
        return {k: v[0] for k, v in q.items()}

    def _cip(self):
        xff = self.headers.get("X-Forwarded-For") or ""
        if xff:
            return xff.split(",")[0].strip()
        return self.headers.get("X-Real-IP") or self.client_address[0]

    def _ua(self):
        return (self.headers.get("User-Agent") or "-")[:60]

    def _token(self):
        if self.headers.get("X-Token"):
            return self.headers.get("X-Token")
        c = self.headers.get("Cookie") or ""
        m = re.search(r"tk=([^;]+)", c)
        return m.group(1) if m else urllib.parse.parse_qs(
            urllib.parse.urlparse(self.path).query).get("token", [""])[0]

    def _web(self, *parts):
        """返回前端目录里的文件路径；没有就返回 None（调用方回退到内置页面）"""
        if not CFG.get("web"):
            return None
        p = os.path.join(CFG["web"], *parts)
        return p if os.path.isfile(p) else None

    def _serve_web(self, path, ctype):
        with open(path, "rb") as f:
            return self._send(200, f.read(), ctype)

    def _authed(self):
        return bool(CFG.get("token")) and self._token() == CFG["token"]

    def log_message(self, fmt, *args):
        pass

    # ---------------- GET ----------------
    def do_GET(self):
        p = self.path.split("?")[0].rstrip("/") or "/"
        if p in ("/", "/Home/Index", "/home/index"):
            return self._send(200, INDEX_HTML, "text/html; charset=utf-8")
        if p == "/favicon.ico":
            fp = os.path.join(CFG["static"], "favicon.ico")
            if os.path.exists(fp):
                with open(fp, "rb") as f:
                    return self._send(200, f.read(), "image/x-icon")
            return self._send(404, b"", "text/plain")
        if p == "/admin":
            if not self._authed():
                return self._send(302, b"", "text/plain", {"Location": "/admin/login"})
            fp = self._web("index.html")
            if fp:
                return self._serve_web(fp, "text/html; charset=utf-8")
            return self._send(200, ADMIN_HTML, "text/html; charset=utf-8")
        if p == "/admin/login":
            fp = self._web("login.html")
            if fp:
                return self._serve_web(fp, "text/html; charset=utf-8")
            return self._send(200, LOGIN_HTML.replace("%ERR%", ""), "text/html; charset=utf-8")
        if p.startswith("/admin/static/"):
            name = p[len("/admin/static/"):]
            if "/" in name or ".." in name:
                return self._send(404, b"", "text/plain")
            fp = self._web("static", name)
            if not fp:
                return self._send(404, b"", "text/plain")
            ct = "text/css; charset=utf-8" if name.endswith(".css") else \
                 "application/javascript; charset=utf-8" if name.endswith(".js") else "application/octet-stream"
            return self._serve_web(fp, ct)
        if p.startswith("/admin/api/"):
            if not self._authed():
                return self._send(401, {"message": "unauthorized"})
            return self._admin_get(p)
        if p in ("/api/user/getAppInfos", "/api/user/validate", "/api/user/register"):
            return self._send(405, b"", "text/plain", {"Allow": "POST"})
        # 其它一律 404，与线上一致
        return self._send(404, b"", "text/plain; charset=utf-8")

    def _admin_get(self, p):
        if p == "/admin/api/tasks":
            with LOCK:
                return self._send(200, {"tasks": load_json("tasks.json", [])})
        if p == "/admin/api/users":
            with LOCK:
                return self._send(200, {"users": load_json("users.json", [])})
        if p == "/admin/api/export":
            with LOCK:
                tasks = load_json("tasks.json", [])
                users = load_json("users.json", [])
            buf = io.BytesIO()
            with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
                z.writestr("tasks.json", json.dumps(tasks, ensure_ascii=False, indent=1))
                z.writestr("users.json", json.dumps(users, ensure_ascii=False, indent=1))
                z.writestr("meta.json", json.dumps({
                    "exportedAt": time.strftime("%Y-%m-%d %H:%M:%S"),
                    "taskCount": len(tasks), "userCount": len(users),
                    "format": "autoinstall-taskpack/1"
                }, ensure_ascii=False, indent=1))
            data = buf.getvalue()
            name = "autoinstall-taskpack-%s.zip" % time.strftime("%Y%m%d-%H%M%S")
            return self._send(200, data, "application/zip",
                              {"Content-Disposition": 'attachment; filename="%s"' % name})
        if p == "/admin/api/records":
            recs = []
            try:
                with open(_path("records.jsonl"), "r", encoding="utf-8") as f:
                    for line in f:
                        line = line.strip()
                        if line:
                            try:
                                recs.append(json.loads(line))
                            except Exception:
                                pass
            except FileNotFoundError:
                pass
            return self._send(200, {"records": recs[-500:]})
        return self._send(404, {"message": "not found"})

    # ---------------- POST ----------------
    def do_POST(self):
        p = self.path.split("?")[0].rstrip("/")
        raw = self._read_body()
        self._raw = raw

        if p == "/admin/login":
            form = self._payload(raw) or {}
            if form.get("token") == CFG.get("token"):
                body = b""
                self.send_response(302)
                self.send_header("Location", "/admin?token=" + urllib.parse.quote(CFG["token"]))
                self.send_header("Set-Cookie", "tk=%s; Path=/; HttpOnly" % CFG["token"])
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            return self._send(200, LOGIN_HTML.replace("%ERR%", "口令不正确"), "text/html; charset=utf-8")

        if p.startswith("/admin/api/"):
            if not self._authed():
                return self._send(401, {"message": "unauthorized"})
            return self._admin_post(p, self._payload(raw) or {})

        # ---- 客户端接口 ----
        if p == "/api/user/getAppInfos":
            append_log("getAppInfos from %s ua=%s body=%r" % (self._cip(), self._ua(), raw[:200]))
            return self._send(200, api_get_app_infos(self._payload(raw)))
        if p == "/api/user/validate":
            body = self._payload(raw)
            if body is None:
                return self._send(400, b"", "text/plain")
            append_log("validate from %s ua=%s body=%r" % (self._cip(), self._ua(), raw[:200]))
            return self._send(200, api_validate(body))
        if p == "/api/user/register":
            body = self._payload(raw)
            if body is None:
                return self._send(400, b"", "text/plain")
            r = api_register(body)
            append_log("register from %s ua=%s -> %s" % (self._cip(), self._ua(), r["message"]))
            return self._send(200, r)
        # 已知路径的其它方法 -> 405（与线上一致）
        if p in ("/api/user/getAppInfos", "/api/user/validate", "/api/user/register"):
            return self._send(405, b"", "text/plain", {"Allow": "POST"})
        return self._send(404, b"", "text/plain; charset=utf-8")

    def _admin_post(self, p, body):
        if p == "/admin/api/task":
            with LOCK:
                tasks = load_json("tasks.json", [])
                tid = body.get("id")
                if tid in (None, "", 0):
                    tid = next_id(tasks)
                    tasks.append({"id": tid, "appName": body.get("appName", ""), "appVer": body.get("appVer", ""),
                                  "appFirm": body.get("appFirm", ""), "appScript": body.get("appScript", ""),
                                  "appPath": "", "appDirectory": "", "appPackageName": body.get("appPackageName", "")})
                else:
                    t = next((x for x in tasks if int(x["id"]) == int(tid)), None)
                    if not t:
                        return self._send(404, {"message": "任务不存在"})
                    for k in ("appName", "appVer", "appFirm", "appPackageName", "appScript"):
                        t[k] = body.get(k, t.get(k, ""))
                save_json("tasks.json", tasks)
            return self._send(200, {"message": "任务已保存"})
        if p == "/admin/api/task/delete":
            with LOCK:
                tasks = load_json("tasks.json", [])
                tasks = [x for x in tasks if int(x["id"]) != int(body.get("id") or 0)]
                save_json("tasks.json", tasks)
            return self._send(200, {"message": "已删除"})
        if p == "/admin/api/user":
            with LOCK:
                users = load_json("users.json", [])
                uid = body.get("id")
                rec = {"userName": (body.get("userName") or "").strip(),
                       "name": body.get("name") or body.get("userName") or "",
                       "enabled": bool(body.get("enabled", True)),
                       "note": body.get("note", "")}
                if not rec["userName"]:
                    return self._send(400, {"message": "userName 不能为空"})
                if uid in (None, "", 0):
                    rec["id"] = next_id(users)
                    users.append(rec)
                else:
                    u = next((x for x in users if int(x["id"]) == int(uid)), None)
                    if not u:
                        return self._send(404, {"message": "用户不存在"})
                    u.update(rec)
                save_json("users.json", users)
            return self._send(200, {"message": "用户已保存"})
        if p == "/admin/api/import":
            mode = "merge"
            q = urllib.parse.urlparse(self.path).query
            m = urllib.parse.parse_qs(q).get("mode", ["merge"])[0]
            if m in ("merge", "replace"):
                mode = m
            raw = self._raw
            tasks_in, users_in = [], []
            try:
                with zipfile.ZipFile(io.BytesIO(raw)) as z:
                    names = z.namelist()
                    if "tasks.json" in names:
                        tasks_in = json.loads(z.read("tasks.json").decode("utf-8-sig"))
                    if "users.json" in names:
                        users_in = json.loads(z.read("users.json").decode("utf-8-sig"))
            except Exception:
                try:
                    obj = json.loads(raw.decode("utf-8-sig"))
                    tasks_in = obj if isinstance(obj, list) else (obj.get("tasks") or [])
                except Exception:
                    return self._send(400, {"message": "不是合法的任务包（zip 或 tasks 数组）"})
            added = updated = 0
            with LOCK:
                tasks = load_json("tasks.json", [])
                if mode == "replace":
                    for t in tasks_in:
                        t["id"] = int(t.get("id") or 0)
                    tasks = tasks_in
                    added = len(tasks_in)
                else:
                    index = {}
                    for t in tasks:
                        index[(t.get("appName", ""), t.get("appVer", ""))] = t
                    for t in tasks_in:
                        key = (t.get("appName", ""), t.get("appVer", ""))
                        if key in index:
                            index[key].update({k: t.get(k, index[key].get(k)) for k in
                                               ("appVer", "appFirm", "appScript", "appPackageName")})
                            updated += 1
                        else:
                            t["id"] = next_id(tasks)
                            tasks.append(t)
                            added += 1
                save_json("tasks.json", tasks)
                if users_in:
                    users = load_json("users.json", [])
                    if mode == "replace":
                        users = users_in
                    else:
                        have = set(u.get("userName") for u in users)
                        for u in users_in:
                            if u.get("userName") not in have:
                                u["id"] = next_id(users)
                                users.append(u)
                    save_json("users.json", users)
            append_log("import mode=%s added=%d updated=%d from %s" % (mode, added, updated, self._cip()))
            return self._send(200, {"message": "导入完成（%s）：新增 %d 条，更新 %d 条" % (mode, added, updated),
                                    "added": added, "updated": updated,
                                    "total": len(load_json("tasks.json", []))})
        if p == "/admin/api/user/delete":
            with LOCK:
                users = load_json("users.json", [])
                users = [x for x in users if int(x["id"]) != int(body.get("id") or 0)]
                save_json("users.json", users)
            return self._send(200, {"message": "已删除"})
        return self._send(404, {"message": "not found"})

    # 其它方法：已知 API 一律 405，与线上一致
    def _other(self):
        p = self.path.split("?")[0].rstrip("/")
        if p.startswith("/api/user/"):
            return self._send(405, b"", "text/plain", {"Allow": "POST"})
        return self._send(404, b"", "text/plain; charset=utf-8")

    do_PUT = do_DELETE = do_HEAD = do_OPTIONS = do_TRACE = do_PATCH = _other


def main():
    ap = argparse.ArgumentParser(description="AutoInstall 装机系统后端（可自部署）")
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=3809)
    ap.add_argument("--data", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "data"))
    ap.add_argument("--static", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "static"))
    ap.add_argument("--token", default=None, help="管理台口令；不填则随机生成并打印")
    ap.add_argument("--web", default=None,
                    help="管理台前端目录（含 index.html / login.html / static/）；不填则用内置页面")
    a = ap.parse_args()

    CFG.update(host=a.host, port=a.port, data=a.data, static=a.static, web=a.web or "",
               token=a.token or secrets.token_urlsafe(9))
    os.makedirs(CFG["data"], exist_ok=True)
    os.makedirs(CFG["static"], exist_ok=True)
    for f, d in (("tasks.json", []), ("users.json", [])):
        if not os.path.exists(_path(f)):
            save_json(f, d)

    srv = ThreadingHTTPServer((CFG["host"], CFG["port"]), Handler)
    print("=" * 66)
    print(" AutoInstall 后端已启动")
    print("   客户端接口 : http://%s:%d/api/user/{getAppInfos,validate,register}" % (CFG["host"], CFG["port"]))
    print("   管理台     : http://%s:%d/admin?token=%s" % (CFG["host"], CFG["port"], CFG["token"]))
    print("   数据目录   : %s" % CFG["data"])
    print("=" * 66, flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nbye")


if __name__ == "__main__":
    main()
