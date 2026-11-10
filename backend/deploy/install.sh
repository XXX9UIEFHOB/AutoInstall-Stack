#!/usr/bin/env bash
# AutoInstall 后端 —— Linux 一键安装（systemd）
# 用法（在解包后的目录里执行）：
#   sudo bash install.sh --port 3809 --token 你的口令 [--dir /opt/autoinstall-backend] [--open-firewall]
set -euo pipefail

DIR=/opt/autoinstall-backend
PORT=3809
TOKEN=""
OPEN_FW=0
HERE="$(cd "$(dirname "$0")" && pwd)"

while [ $# -gt 0 ]; do
  case "$1" in
    --dir) DIR="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --token) TOKEN="$2"; shift 2 ;;
    --open-firewall) OPEN_FW=1; shift ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) echo "未知参数: $1"; exit 2 ;;
  esac
done

say() { printf '\033[36m[deploy]\033[0m %s\n' "$*"; }
err() { printf '\033[31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

# ---------- 依赖 ----------
command -v python3 >/dev/null 2>&1 || err "缺少 python3，请先 apt/yum 安装"
PY="$(command -v python3)"
say "python3 = $PY ($($PY -V 2>&1))"

if [ -z "$TOKEN" ]; then
  TOKEN="$($PY -c 'import secrets;print(secrets.token_urlsafe(12))')"
  say "未指定 --token，已随机生成"
fi

# ---------- 落盘 ----------
say "安装目录: $DIR"
mkdir -p "$DIR/data" "$DIR/static"
[ -f "$HERE/server.py" ] || err "找不到 server.py（请把 server.py/data/static 与安装脚本放在同一目录）"
install -m 755 "$HERE/server.py" "$DIR/server.py"

if [ -f "$HERE/data/tasks.json" ]; then
  cp -f "$HERE/data/tasks.json" "$DIR/data/tasks.json"
  say "任务库: $(grep -c '\"appName\"' "$DIR/data/tasks.json" || true) 条"
fi
if [ -f "$HERE/data/users.json" ]; then cp -f "$HERE/data/users.json" "$DIR/data/users.json"; fi
if [ -f "$HERE/static/favicon.ico" ]; then cp -f "$HERE/static/favicon.ico" "$DIR/static/favicon.ico"; fi
touch "$DIR/data/records.jsonl" "$DIR/data/access.log"

# 端口占用检查
if command -v ss >/dev/null 2>&1 && ss -lnt 2>/dev/null | grep -q ":$PORT "; then
  err "端口 $PORT 已被占用，请用 --port 换一个"
fi

# ---------- 服务 ----------
if command -v systemctl >/dev/null 2>&1; then
  say "写入 systemd 服务"
  cat >/etc/systemd/system/autoinstall-backend.service <<EOF
[Unit]
Description=AutoInstall backend (auto_fix compatible)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$DIR
ExecStart=$PY $DIR/server.py --host 0.0.0.0 --port $PORT --token $TOKEN
Restart=always
RestartSec=3
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
  systemctl daemon-reload
  systemctl enable autoinstall-backend >/dev/null 2>&1 || true
  systemctl restart autoinstall-backend
  say "服务已启动: systemctl status autoinstall-backend"
else
  say "没有 systemd，改用 nohup 后台运行"
  cd "$DIR"
  nohup "$PY" "$DIR/server.py" --host 0.0.0.0 --port "$PORT" --token "$TOKEN" >>"$DIR/data/nohup.log" 2>&1 &
  echo $! >"$DIR/autoinstall-backend.pid"
fi

# ---------- 防火墙 ----------
if [ "$OPEN_FW" = "1" ]; then
  if command -v ufw >/dev/null 2>&1; then ufw allow "$PORT"/tcp || true; say "ufw 放行 $PORT/tcp"; fi
  if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --permanent --add-port="$PORT"/tcp >/dev/null 2>&1 || true
    firewall-cmd --reload >/dev/null 2>&1 || true
    say "firewalld 放行 $PORT/tcp"
  fi
fi

# ---------- 自检 ----------
say "等待服务就绪并自检…"
"$PY" - "$PORT" <<'PY'
import json, sys, time, urllib.request, urllib.parse
port = sys.argv[1]
base = "http://127.0.0.1:%s" % port
def post(path, data, ct):
    req = urllib.request.Request(base + path, data=data,
        headers={"Content-Type": ct, "User-Agent": "auto_fix/HttpRequesterDemo"})
    return urllib.request.urlopen(req, timeout=10).read()

for i in range(20):
    try:
        r = json.loads(post("/api/user/getAppInfos", urllib.parse.urlencode({"key": "hq"}).encode(),
                            "application/x-www-form-urlencoded").decode())
        n = len(r.get("success") or [])
        print("[deploy] /api/user/getAppInfos  OK   任务数 = %d" % n)
        v = json.loads(post("/api/user/validate", json.dumps({"userName": "hq"}).encode(),
                            "application/json; charset=utf-8").decode())
        print("[deploy] /api/user/validate       OK   %s" % v)
        sys.exit(0)
    except Exception as e:
        if i == 19:
            print("[deploy] 自检失败: %s" % e); sys.exit(1)
        time.sleep(0.5)
PY

IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
[ -n "$IP" ] || IP="<服务器IP>"
echo
echo "=============================================================="
echo " 客户端接口 : http://$IP:$PORT/api/user/{getAppInfos,validate,register}"
echo " 管理台     : http://$IP:$PORT/admin?token=$TOKEN"
echo " 管理口令   : $TOKEN"
echo " 数据目录   : $DIR/data   (tasks.json / users.json / records.jsonl)"
echo "--------------------------------------------------------------"
echo " 客户端切过来："
echo "   方式A 改字符串 → ApiUrl 文件偏移 0x15DB9，UTF-16 等长替换"
echo "   方式B 改源码   → AutoFix.Src/Net.cs 的 ApiUrl 常量，csc 重编译"
echo "=============================================================="
