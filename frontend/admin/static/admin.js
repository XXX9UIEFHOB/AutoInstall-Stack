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
