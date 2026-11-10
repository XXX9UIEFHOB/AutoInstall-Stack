#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AutoInstall 装机系统 —— 新增软件工具（生成任务脚本 + 校验 + 推送到你的后端）

本地用法（Windows 直接 python new_task.py ...）：

  # 1) 看看服务器上现在有哪些任务
  python new_task.py list --server http://YOUR_HOST --token 口令

  # 2) 按模板生成脚本并预览（不推送）
  python new_task.py make --dir "D:\\装机包\\MyApp_1.0" --name MyApp --ver 1.0 --firm 某某公司 \
                          --template setup --entry "1_Setup\\Setup.exe"

  # 3) 生成 + 校验 + 直接推到服务器
  python new_task.py make --dir "D:\\装机包\\MyApp_1.0" --name MyApp --ver 1.0 \
                          --template msi --entry "1_Setup\\MyApp.msi" \
                          --server http://YOUR_HOST --token 口令

  # 4) 校验一份已有脚本
  python new_task.py lint --script my.txt

  # 5) 删除任务
  python new_task.py delete --id 54 --server http://YOUR_HOST --token 口令

模板：msi / setup / green（绿色软件）/ reg（导入注册表破解）
"""
import argparse, hashlib, json, os, re, sys, urllib.parse, urllib.request

# Windows 控制台中文友好（失败也不影响功能）
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

COMMANDS = {
    "IF", "ELSE", "END_IF", "SET_PATH", "SLEEP", "EXIT", "HELP", "ECHO", "PRINT",
    "WIN", "WIN_INFO", "SET_WIN", "SET_PROCESS", "WAIT_WIN", "WAIT_PROCESS", "CLOSE", "KILL",
    "WHILE_WIN", "WHILE_PROCESS", "WHILE_TEXT", "WHILE_TEXT_P", "READ_PACE",
    "CLICK", "CLICK_TEXT", "CLICK_RELATIVE", "CLICK_EX", "CLICK_T", "MOVE", "MOUSE_CLICK",
    "KEY", "PASTE_TEXT", "READ_TEXT", "GET_TEXT", "SET_TEXT", "GET_CODE",
    "RUN", "RUN_A", "RUN_INFO", "EXECUTE_SCRIPT", "FILE", "COPY_FILE", "FILES", "COPY_FILES",
    "IS_FILE", "ZIP", "ADI", "APP_PATH", "LINK", "REPLACE_TXT", "LIST_CONTROLS",
}
# 引擎实际支持的变量（%TEMP% 在 ParsingCommand 构造函数里自动注入）
VARS = {"%PATH%", "%DISK%", "%TEMP%", "%RUN_PATH%", "%APP_PATH%"}


# ------------------------------------------------------------------ 基础
def md5_file(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def fnv1a64(path, limit=8192):
    h = 14695981039346656037
    with open(path, "rb") as f:
        data = f.read(limit)
    for b in data:
        h ^= b
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return "%016X" % h


def find_entry(root, entry=None):
    """在软件目录里找安装入口"""
    if entry:
        p = os.path.join(root, entry)
        if not os.path.isfile(p):
            sys.exit("找不到入口文件: %s" % p)
        return entry
    cands = [
        r"1_Setup\Setup.exe", r"1_Setup\setup.exe", r"1_Setup\Setup.msi",
        r"1_Setup\Install.exe", r"Setup.exe", r"setup.exe", r"install.exe",
    ]
    for c in cands:
        if os.path.isfile(os.path.join(root, c)):
            return c
    for base, _, files in os.walk(root):
        for fn in files:
            if fn.lower() in ("setup.exe", "install.exe") or fn.lower().endswith(".msi"):
                return os.path.relpath(os.path.join(base, fn), root)
    return None


# ------------------------------------------------------------------ 模板
def build_script(name, ver, firm, entry, template, pkg, extra):
    head = "#filePath=%s" % entry
    lines = []
    if template == "msi":
        lines = [
            "SET_PATH 'Program Files\\%s'" % name,
            "PRINT 正在安装 %s %s ..." % (name, ver),
            "RUN_INFO 'msiexec.exe' '/i \"%%RUN_PATH%%\\%s\" INSTALLDIR=\"%%PATH%%\" /qb /norestart /L*V \"%%TEMP%%install_%s.log\"'" % (entry, name),
            "SLEEP 3000",
        ]
    elif template == "setup":
        lines = [
            "SET_PATH 'Program Files\\%s'" % name,
            "PRINT 正在安装 %s %s ..." % (name, ver),
            "RUN_A 'start \"\" \"%%RUN_PATH%%\\%s\"'" % entry,
            "# 安装向导如果是标准 InstallShield/NSIS，先抓控件ID再用 CLICK：",
            "#   WHILE_WIN '安装 - %s' 60" % name,
            "#   SET_WIN '安装 - %s'" % name,
            "#   CLICK 1          # 下一步 / 我接受",
            "#   SET_TEXT 4 PATH  # 路径框填 %%PATH%%（4 是常见控件ID，务必用 GET_CODE 确认）",
            "WHILE_TEXT 完成 600",
            "SLEEP 3000",
        ]
    elif template == "green":
        lines = [
            "SET_PATH 'Program Files\\%s'" % name,
            "PRINT 正在复制 %s ..." % name,
            "COPY_FILES '%%RUN_PATH%%\\Program' '%%PATH%%'",
            "SLEEP 1000",
            "LINK '%s'" % name,
        ]
    elif template == "reg":
        lines = [
            "SET_PATH 'Program Files\\%s'" % name,
            "PRINT 安装 %s ..." % name,
            "RUN_INFO '%%RUN_PATH%%\\%s' '/S'" % entry,
            "SLEEP 3000",
            "PRINT 导入注册表授权 ...",
            "RUN '%%RUN_PATH%%\\2_双击导入.reg'",
            "SLEEP 1500",
            "SET_WIN '注册表编辑器'",
            "SLEEP 100",
            "KEY ENTER",
            "SLEEP 1000",
            "SET_WIN '注册表编辑器'",
            "SLEEP 100",
            "KEY ENTER",
            "SLEEP 1000",
        ]
    else:
        sys.exit("未知模板: %s" % template)

    tail = ["PRINT %s 安装完成" % name]
    if extra:
        tail = extra + tail
    return "\n".join([head] + lines + tail)


# ------------------------------------------------------------------ 校验
def lint(text, verbose=True):
    warns, errs = [], []
    lines = text.split("\n")
    if not lines or not lines[0].strip():
        errs.append("第一行必须是校验头 #filePath=..;md5=..;appName=..")
    else:
        h = lines[0]
        if h[0] not in "#/":
            errs.append("校验头第一个字符会被程序吃掉（Substring(1)），首字符必须是 # 或 /，否则 filePath 会变成 ilePath")
        for m in re.finditer(r"(\w+)=([^;]+)", h[1:]):
            k = m.group(1)
            if k not in ("filePath", "md5", "appName", "computer", "verify"):
                warns.append("校验头里有未知键 %r（引擎只认 filePath/md5/appName/computer/verify）" % k)
    for i, raw in enumerate(lines, 1):
        s = raw.strip()
        if not s or s.startswith("#") or s.startswith("//"):
            continue
        parts = s.split()
        cmd = parts[0].upper()
        if cmd not in COMMANDS:
            errs.append("第 %d 行: 未知命令 %r（引擎会打印『未知命令』并跳过）" % (i, parts[0]))
        for v in re.findall(r"%[A-Z_]+%", s):
            if v not in VARS:
                warns.append("第 %d 行: 变量 %s 引擎不会替换（可用: %s）" % (i, v, " ".join(sorted(VARS))))
        if cmd in ("CLICK", "CLICK_TEXT", "CLICK_T", "SET_WIN", "SLEEP", "SET_PATH") and len(parts) < 2:
            errs.append("第 %d 行: %s 缺参数" % (i, cmd))
    if verbose:
        for w in warns:
            print("  [warn] " + w)
        for e in errs:
            print("  [ERROR] " + e)
        print("  校验结果: %s (%d 错误 / %d 警告)" % ("通过" if not errs else "不通过", len(errs), len(warns)))
    return errs


# ------------------------------------------------------------------ 后端 API
def api(server, token, path, body=None):
    url = server.rstrip("/") + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method="POST" if data else "GET",
                                 headers={"Content-Type": "application/json", "X-Token": token,
                                          "User-Agent": "auto_fix/tool"})
    with urllib.request.urlopen(req, timeout=20) as r:
        return json.loads(r.read().decode("utf-8"))


def cmd_list(a):
    d = api(a.server, a.token, "/admin/api/tasks")
    print("%-5s %-26s %-10s %-14s %s" % ("ID", "软件", "版本", "厂商", "包名"))
    for t in sorted(d["tasks"], key=lambda x: x["id"]):
        print("%-5s %-26s %-10s %-14s %s" % (t["id"], t["appName"], t["appVer"], t["appFirm"], t["appPackageName"]))


def cmd_delete(a):
    print(api(a.server, a.token, "/admin/api/task/delete", {"id": a.id}))


def cmd_lint(a):
    errs = lint(open(a.script, encoding="utf-8").read())
    sys.exit(1 if errs else 0)


def cmd_make(a):
    entry = find_entry(a.dir, a.entry)
    if not entry:
        sys.exit("在 %s 里没找到安装入口，请用 --entry 指定（相对路径）" % a.dir)
    full = os.path.join(a.dir, entry)
    md5 = a.md5 or md5_file(full)
    verify = a.verify or fnv1a64(full)
    pkg = a.package or os.path.basename(os.path.abspath(a.dir))
    extra = a.extra.split("|") if a.extra else []

    text = build_script(a.name, a.ver, a.firm, entry, a.template, pkg, extra)
    # 注入校验头
    head = text.split("\n")[0]
    head += ";md5=%s" % md5
    if a.appname_check:
        head += ";appName=%s" % a.appname_check
    if a.strict:
        head += ";computer=strict"
    head += ";verify=%s" % verify
    text = "\n".join([head] + text.split("\n")[1:])

    print("入口文件 : %s" % entry)
    print("文件大小 : %d 字节" % os.path.getsize(full))
    print("MD5      : %s" % md5)
    print("FNV1a64  : %s" % verify)
    print("包名     : %s   （客户端会找 exe 同目录下这个子目录）" % pkg)
    print("-" * 70)
    print(text)
    print("-" * 70)
    errs = lint(text)
    if errs:
        sys.exit("脚本没通过校验，已中止")

    if a.out:
        open(a.out, "w", encoding="utf-8").write(text)
        print("已写入: %s" % a.out)

    if a.server and a.token:
        if a.dry_run:
            print("--dry-run：不推送")
            return
        r = api(a.server, a.token, "/admin/api/task", {
            "id": None, "appName": a.name, "appVer": a.ver, "appFirm": a.firm,
            "appPackageName": pkg, "appScript": text})
        print("推送结果: %s" % r)
    else:
        print("提示: 加 --server/--token 可直接推到后端")


def main():
    p = argparse.ArgumentParser(description="新增软件任务工具（AutoInstall backend）")
    sub = p.add_subparsers(dest="cmd", required=True)

    m = sub.add_parser("make", help="生成脚本并可选推送")
    m.add_argument("--dir", required=True, help="软件目录（里面是 1_Setup\\... 这类内容）")
    m.add_argument("--name", required=True, help="软件名 appName")
    m.add_argument("--ver", default="", help="版本 appVer")
    m.add_argument("--firm", default="", help="厂商 appFirm")
    m.add_argument("--entry", default=None, help="安装入口相对路径，默认自动探测")
    m.add_argument("--template", default="setup", choices=["msi", "setup", "green", "reg"])
    m.add_argument("--package", default=None, help="appPackageName，默认取目录名")
    m.add_argument("--md5", default=None, help="手工指定 MD5（默认自动算）")
    m.add_argument("--verify", default=None, help="手工指定 FNV1a 指纹（默认自动算）")
    m.add_argument("--appname-check", dest="appname_check", default=None,
                   help="头行 appName=（客户端会去开始菜单查同名 lnk，命中就弹『已安装』）")
    m.add_argument("--strict", action="store_true", help="头行加 computer=strict（主机名有中文会警告）")
    m.add_argument("--extra", default=None, help="追加命令，用 | 分隔，如 --extra \"COPY_FILES '%RUN_PATH%\\4_授权文件' '%PATH%'\"")
    m.add_argument("--out", default=None, help="把生成的脚本写到文件")
    m.add_argument("--server", default=None)
    m.add_argument("--token", default=None)
    m.add_argument("--dry-run", action="store_true")
    m.set_defaults(func=cmd_make)

    l = sub.add_parser("lint", help="校验脚本")
    l.add_argument("--script", required=True)
    l.set_defaults(func=cmd_lint)

    ls = sub.add_parser("list", help="列出后端任务")
    ls.add_argument("--server", required=True)
    ls.add_argument("--token", required=True)
    ls.set_defaults(func=cmd_list)

    d = sub.add_parser("delete", help="删除任务")
    d.add_argument("--id", type=int, required=True)
    d.add_argument("--server", required=True)
    d.add_argument("--token", required=True)
    d.set_defaults(func=cmd_delete)

    a = p.parse_args()
    a.func(a)


if __name__ == "__main__":
    main()
