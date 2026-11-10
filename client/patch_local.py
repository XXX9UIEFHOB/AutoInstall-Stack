#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AutoInstall.exe 本地化补丁：
 1) WebSevice.ApiUrl : 原地址 -> 新地址（等长替换，多一个 '/' 由 mock 归一化）
 2) Form1::IsPassword 的返回值 ldloc.3 -> ldc.i4.1（0x999: 09 -> 17），授权校验恒真
用法: python patch_local.py <src.exe> <dst.exe>
"""
import sys, os

src, dst = sys.argv[1], sys.argv[2]
d = bytearray(open(src, 'rb').read())
log = []

# ---------- 1) ApiUrl ----------
old_url = os.environ.get('OLD_URL', 'http://127.0.0.1:3809')   # 改成目标 exe 里的原始 ApiUrl
old = old_url.encode('utf-16-le')
new_text = 'http://127.0.0.1:38090/'
new = new_text.encode('utf-16-le')
assert len(old) == len(new), 'length mismatch %d vs %d' % (len(old), len(new))
i = d.find(old)
assert i > 0, 'ApiUrl not found'
d[i:i+len(old)] = new
log.append('ApiUrl  @0x%X: %r -> %r' % (i, old_url, new_text))

# ---------- 2) IsPassword => return true ----------
# Form1::IsPassword  RVA 0x264C, fat 头 12 字节 -> 代码起点文件偏移 0x858
#   IL_013D ldc.i4.1 / IL_013E stloc.3 / IL_013F br.s 0 / IL_0141 ldloc.3 / IL_0142 ret
#   => 把 IL_0141 的 09(ldloc.3) 改成 17(ldc.i4.1)
pat = bytes([0x17, 0x0D, 0x2B, 0x00, 0x09, 0x2A])
code_off = 0x858
j = d.find(pat, code_off, code_off + 0x200)
assert j > 0, 'IsPassword tail pattern not found'
d[j + 4] = 0x17
log.append('IsPassword @0x%X: 09 (ldloc.3) -> 17 (ldc.i4.1)  => 恒返回 true' % (j + 4))

open(dst, 'wb').write(bytes(d))
print('\n'.join(log))
print('written %s (%d bytes)' % (dst, len(d)))
