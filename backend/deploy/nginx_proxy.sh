set -e
CONF=/www/server/panel/vhost/nginx/0.default.conf
BAK=${CONF}.bak.$(date +%Y%m%d%H%M%S)
cp -a "$CONF" "$BAK"
echo "[nginx] 已备份 -> $BAK"

cat > "$CONF" <<'EOF'
server
{
    listen 80;
    server_name _;
    index index.html;
    root /www/server/nginx/html;

    # ===== AutoInstall 后端反代（127.0.0.1:3809） START =====
    location ^~ /api/ {
        proxy_pass http://127.0.0.1:3809;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
        proxy_buffering off;
    }

    location ^~ /admin {
        proxy_pass http://127.0.0.1:3809;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    # ===== AutoInstall 后端反代 END =====
}
EOF

if nginx -t 2>&1; then
    nginx -s reload
    echo "[nginx] reload 完成"
else
    echo "[nginx] 配置检查失败，回滚"
    cp -a "$BAK" "$CONF"
    exit 1
fi

echo "=== 本机经由 80 端口验证 ==="
curl -s -o /dev/null -w "POST /api/user/getAppInfos -> %{http_code}  %{size_download} bytes\n" \
     -X POST -d "key=hq" http://127.0.0.1/api/user/getAppInfos
curl -s -X POST -d '{"userName":"hq"}' -H "Content-Type: application/json" http://127.0.0.1/api/user/validate; echo
curl -s -o /dev/null -w "GET /admin -> %{http_code}\n" http://127.0.0.1/admin
curl -s -o /dev/null -w "GET /     -> %{http_code}\n" http://127.0.0.1/
echo "=== 清理安装临时目录 ==="
rm -rf /tmp/ai-deploy /tmp/payload.tar.gz /tmp/install.sh && echo "已清理"
