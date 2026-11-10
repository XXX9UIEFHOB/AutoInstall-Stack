set +e
echo "=== service ==="
systemctl is-enabled autoinstall-backend
systemctl is-active autoinstall-backend
echo "=== listen ==="
ss -lntp | grep 3809
echo "=== local api ==="
python3 - <<'PY'
import json, urllib.request, urllib.parse
def post(path, data, ct):
    r = urllib.request.Request("http://127.0.0.1:3809" + path, data=data.encode(),
                               headers={"Content-Type": ct, "User-Agent": "auto_fix/HttpRequesterDemo"})
    return urllib.request.urlopen(r, timeout=5).read().decode()
b = post("/api/user/getAppInfos", urllib.parse.urlencode({"key": "hq"}), "application/x-www-form-urlencoded")
print("getAppInfos -> %d bytes, apps=%d" % (len(b), len(json.loads(b)["success"])))
print("validate(hq) ->", post("/api/user/validate", json.dumps({"userName": "hq"}), "application/json"))
print("validate(x)  ->", post("/api/user/validate", json.dumps({"userName": "nobody"}), "application/json"))
PY
echo "=== register 自测 ==="
python3 - <<'PY'
import json, urllib.request
body = json.dumps({"userIP":"1.2.3.4","computerName":"deploy-selftest","computerCode":"TEST-FINGERPRINT",
                   "product":"selftest","installPath":"C:\\selftest","installDate":"2026-11-10T11:30:00.000Z",
                   "userId":4,"other":"deploy selftest"}).encode()
r = urllib.request.Request("http://127.0.0.1:3809/api/user/register", data=body,
                           headers={"Content-Type": "application/json"})
print("register ->", urllib.request.urlopen(r, timeout=5).read().decode())
PY
echo "=== records ==="
wc -l /opt/autoinstall-backend/data/records.jsonl
tail -1 /opt/autoinstall-backend/data/records.jsonl | cut -c1-180
echo "=== admin api ==="
TOKEN=$(grep -oP '(?<=--token ).*' /etc/systemd/system/autoinstall-backend.service)
curl -s -H "X-Token: $TOKEN" http://127.0.0.1:3809/admin/api/tasks | head -c 120; echo
echo "=== nginx 现有站点 ==="
nginx -T 2>/dev/null | grep -E "^\s*(server_name|listen|root|proxy_pass)" | sed 's/^\s*//' | sort | uniq -c | sort -rn | head -25
echo "=== 是否有 default_server ==="
nginx -T 2>/dev/null | grep -n "default_server" | head -5
echo "=== 公网监听 ==="
ss -lnt | awk 'NR>1{print $4}' | grep -v '^127\.' | sort -u
