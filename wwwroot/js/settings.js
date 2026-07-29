const $=id=>document.getElementById(id);
let sessionData=null,columnSettings=[],accounts=[],salaryPositions=[];

function showSettingsPanel(panelId,toggle=false){
  let target=panelId?$(panelId):null;
  if(target?.classList.contains('admin-only')&&!sessionData?.isAdministrator)target=null;
  if(target?.classList.contains('editor-only')&&!sessionData?.canEdit)target=null;
  if(toggle&&target&&!target.hidden)target=null;
  document.querySelectorAll('[data-settings-panel]').forEach(panel=>{
    const active=panel===target;
    panel.hidden=!active;
    panel.classList.toggle('active',active);
  });
  document.querySelectorAll('[data-settings-target]').forEach(button=>{
    const active=Boolean(target)&&button.dataset.settingsTarget===target.id;
    button.classList.toggle('active',active);
    button.setAttribute('aria-pressed',String(active));
  });
  document.querySelector('.settings-content')?.classList.toggle('has-selection',Boolean(target));
}

document.querySelectorAll('[data-settings-target]').forEach(button=>{
  button.onclick=()=>showSettingsPanel(button.dataset.settingsTarget,true);
});

async function request(url,options){
  const response=await fetch(url,options);
  if(response.status===401){location.replace('/login');throw Error('로그인이 필요합니다.');}
  const data=response.status===204?null:await response.json().catch(()=>({}));
  if(!response.ok)throw Error(data?.message||`요청을 처리하지 못했습니다. (HTTP ${response.status})`);
  return data;
}

async function initialize(){
  try{
    sessionData=await request('/api/session');
    $('sessionUser').textContent=sessionData.userName||'로그인 사용자';
    $('roleBadge').textContent=sessionData.isAdministrator?'관리자 권한':sessionData.isHrAdministrator?'HR 관리자 권한':'일반 사용자';
    $('profileForm').elements.newLoginId.value=sessionData.userName||'';
    if(sessionData.theme)window.setDashboardTheme(sessionData.theme);
    document.querySelectorAll('.settings-menu-card.admin-only').forEach(element=>element.hidden=!sessionData.isAdministrator);
    document.querySelectorAll('.settings-menu-card.editor-only').forEach(element=>element.hidden=!sessionData.canEdit);
    showSettingsPanel(null);
    if(sessionData.isAdministrator){
      const [accountRows,columns,history,positionRows]=await Promise.all([
        request('/api/settings/accounts'),
        request('/api/settings/employee-columns'),
        request('/api/settings/database-history'),
        request('/api/settings/salary-positions')
      ]);
      accounts=accountRows;columnSettings=columns;salaryPositions=positionRows.map(x=>x.positionName);
      renderAccounts();renderColumns();renderHistory(history);renderSalaryPositions();
    }
  }catch(error){setMessage('profileStatus',error.message,true);}
}

$('profileForm').onsubmit=async event=>{
  event.preventDefault();
  const form=event.currentTarget,button=form.querySelector('button[type=submit]');
  button.disabled=true;setMessage('profileStatus','변경하고 있습니다.');
  try{
    const data=await request('/api/settings/profile',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({
      currentPassword:form.elements.currentPassword.value,
      newLoginId:form.elements.newLoginId.value.trim(),
      newPassword:form.elements.newPassword.value
    })});
    sessionData.userName=data.loginId;$('sessionUser').textContent=data.loginId;
    form.elements.currentPassword.value='';form.elements.newPassword.value='';
    setMessage('profileStatus','로그인 정보를 변경했습니다.',false,true);
  }catch(error){setMessage('profileStatus',error.message,true);}
  finally{button.disabled=false;}
};

document.querySelectorAll('[data-theme-choice]').forEach(button=>button.onclick=async()=>{
  const theme=button.dataset.themeChoice;
  try{
    await request('/api/settings/theme',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({theme})});
    window.setDashboardTheme(theme);setMessage('themeStatus',`${theme==='dark'?'다크':'라이트'} 모드로 저장했습니다.`,false,true);
  }catch(error){setMessage('themeStatus',error.message,true);}
});

$('dartSyncBtn').onclick=async()=>{
  if(!sessionData?.canEdit)return;
  if(!confirm('DART에서 이노뎁 최신 재무 데이터를 가져올까요?'))return;
  const button=$('dartSyncBtn');button.disabled=true;button.textContent='불러오는 중...';setMessage('dartStatus','최신 재무 공시 데이터를 확인하고 있습니다.');
  try{
    await request('/api/management/sync',{method:'POST'});
    setMessage('dartStatus','DART 데이터를 새로 불러왔습니다.',false,true);
  }catch(error){setMessage('dartStatus',error.message,true);}
  finally{button.disabled=false;button.textContent='DART 새로고침';}
};

$('accountCreateForm').onsubmit=async event=>{
  event.preventDefault();const form=event.currentTarget,button=form.querySelector('button');
  button.disabled=true;setMessage('accountStatus','계정을 추가하고 있습니다.');
  try{
    await request('/api/settings/accounts',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({
      loginId:form.elements.loginId.value.trim(),password:form.elements.password.value,role:form.elements.role.value
    })});
    form.reset();accounts=await request('/api/settings/accounts');renderAccounts();setMessage('accountStatus','계정을 추가했습니다.',false,true);
  }catch(error){setMessage('accountStatus',error.message,true);}
  finally{button.disabled=false;}
};

function renderAccounts(){
  $('accountRows').innerHTML=accounts.map(account=>{
    const self=account.loginId===sessionData.userName;
    return `<tr data-id="${account.id}">
      <td><input data-field="loginId" maxlength="120" value="${escapeHtml(account.loginId)}" ${self?'disabled':''}>${self?'<span class="self-account">현재 로그인 계정</span>':''}</td>
      <td><select data-field="role" ${self?'disabled':''}><option value="User" ${account.role==='User'?'selected':''}>일반 사용자</option><option value="HrAdministrator" ${account.role==='HrAdministrator'?'selected':''}>HR 관리자</option><option value="Administrator" ${account.role==='Administrator'?'selected':''}>관리자</option></select></td>
      <td><input data-field="newPassword" type="password" minlength="4" placeholder="${self?'로그인 관리에서 변경':'변경할 때만 입력'}" ${self?'disabled':''}></td>
      <td><label class="account-state"><input data-field="isActive" type="checkbox" ${account.isActive?'checked':''} ${self?'disabled':''}>${account.isActive?'활성':'비활성'}</label></td>
      <td><div class="account-actions">${self?'<span class="self-account">본인 계정</span>':`<button class="mini-button primary" type="button" data-action="save">저장</button><button class="mini-button danger" type="button" data-action="delete">삭제</button>`}</div></td>
    </tr>`;
  }).join('');
  $('accountRows').querySelectorAll('button[data-action]').forEach(button=>button.onclick=()=>button.dataset.action==='save'?saveAccount(button.closest('tr')):deleteAccount(button.closest('tr')));
  $('accountRows').querySelectorAll('[data-field=isActive]').forEach(input=>input.onchange=()=>{const text=input.parentElement.lastChild;text.textContent=input.checked?'활성':'비활성';});
}

async function saveAccount(row){
  const id=Number(row.dataset.id),button=row.querySelector('[data-action=save]');button.disabled=true;
  try{
    await request(`/api/settings/accounts/${id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({
      loginId:row.querySelector('[data-field=loginId]').value.trim(),
      role:row.querySelector('[data-field=role]').value,
      newPassword:row.querySelector('[data-field=newPassword]').value||null,
      isActive:row.querySelector('[data-field=isActive]').checked
    })});
    accounts=await request('/api/settings/accounts');renderAccounts();setMessage('accountStatus','계정 정보를 저장했습니다.',false,true);
  }catch(error){setMessage('accountStatus',error.message,true);button.disabled=false;}
}

async function deleteAccount(row){
  const account=accounts.find(x=>x.id===Number(row.dataset.id));if(!confirm(`${account.loginId} 계정을 삭제할까요?`))return;
  try{await request(`/api/settings/accounts/${account.id}`,{method:'DELETE'});accounts=await request('/api/settings/accounts');renderAccounts();setMessage('accountStatus','계정을 삭제했습니다.',false,true);}
  catch(error){setMessage('accountStatus',error.message,true);}
}

function renderColumns(){
  $('columnSettingRows').innerHTML=columnSettings.map(item=>`<tr><td class="settings-order">${item.order}</td><td><span class="internal-key">${escapeHtml(item.key)}</span></td><td class="default-name">${escapeHtml(item.defaultName)}</td><td><input class="column-name-input" data-key="${escapeHtml(item.key)}" maxlength="50" value="${escapeHtml(item.displayName)}"></td></tr>`).join('');
  document.querySelectorAll('.column-name-input').forEach(input=>input.oninput=validateColumns);
}
function validateColumns(){
  const inputs=[...document.querySelectorAll('.column-name-input')],names=new Map();let message='';
  inputs.forEach(input=>input.classList.remove('is-invalid'));
  for(const input of inputs){const name=input.value.trim().toLocaleLowerCase('ko-KR');if(!name){input.classList.add('is-invalid');message='열 이름은 비워 둘 수 없습니다.';}else if(names.has(name)){input.classList.add('is-invalid');names.get(name).classList.add('is-invalid');message='열 이름은 중복할 수 없습니다.';}else names.set(name,input);}
  setMessage('columnStatus',message,Boolean(message));return !message;
}
$('saveColumnsBtn').onclick=async()=>{if(!validateColumns())return;const button=$('saveColumnsBtn');button.disabled=true;try{columnSettings=await request('/api/settings/employee-columns',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify([...document.querySelectorAll('.column-name-input')].map(input=>({key:input.dataset.key,displayName:input.value.trim()})))});renderColumns();setMessage('columnStatus','열 이름을 저장했습니다.',false,true);}catch(error){setMessage('columnStatus',error.message,true);}finally{button.disabled=false;}};
$('resetColumnsBtn').onclick=async()=>{if(!confirm('모든 열 이름을 기본값으로 복원할까요?'))return;try{columnSettings=await request('/api/settings/employee-columns/reset',{method:'POST'});renderColumns();setMessage('columnStatus','기본 이름으로 복원했습니다.',false,true);}catch(error){setMessage('columnStatus',error.message,true);}};

function renderSalaryPositions(){
  $('salaryPositionList').innerHTML=salaryPositions.length?salaryPositions.map((name,index)=>`<div class="salary-position-row" data-index="${index}"><span class="salary-position-order">${index+1}</span><span class="salary-position-name">${escapeHtml(name)}</span><span class="salary-position-actions"><button type="button" data-action="up" aria-label="${escapeHtml(name)} 위로 이동" ${index===0?'disabled':''}>↑</button><button type="button" data-action="down" aria-label="${escapeHtml(name)} 아래로 이동" ${index===salaryPositions.length-1?'disabled':''}>↓</button><button type="button" data-action="delete">삭제</button></span></div>`).join(''):'<div class="settings-loading">등록된 직위가 없습니다.</div>';
  $('salaryPositionList').querySelectorAll('button[data-action]').forEach(button=>button.onclick=()=>{
    const index=Number(button.closest('.salary-position-row').dataset.index),action=button.dataset.action;
    if(action==='delete')salaryPositions.splice(index,1);
    else{const target=action==='up'?index-1:index+1;[salaryPositions[index],salaryPositions[target]]=[salaryPositions[target],salaryPositions[index]];}
    renderSalaryPositions();setMessage('salaryPositionStatus','변경 내용을 저장해 주세요.');
  });
}
function addSalaryPosition(){
  const input=$('salaryPositionInput'),name=input.value.trim();
  if(!name){setMessage('salaryPositionStatus','추가할 직위 이름을 입력해 주세요.',true);return;}
  if(salaryPositions.some(x=>x.toLocaleLowerCase('ko-KR')===name.toLocaleLowerCase('ko-KR'))){setMessage('salaryPositionStatus','이미 등록된 직위입니다.',true);return;}
  salaryPositions.push(name);input.value='';renderSalaryPositions();setMessage('salaryPositionStatus','변경 내용을 저장해 주세요.');
}
$('addSalaryPositionBtn').onclick=addSalaryPosition;
$('salaryPositionInput').onkeydown=event=>{if(event.key==='Enter'){event.preventDefault();addSalaryPosition();}};
$('saveSalaryPositionsBtn').onclick=async()=>{
  const button=$('saveSalaryPositionsBtn');button.disabled=true;
  try{const rows=await request('/api/settings/salary-positions',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({positions:salaryPositions})});salaryPositions=rows.map(x=>x.positionName);renderSalaryPositions();setMessage('salaryPositionStatus','연봉 그래프 직위 설정을 저장했습니다.',false,true);}
  catch(error){setMessage('salaryPositionStatus',error.message,true);}
  finally{button.disabled=false;}
};

function renderHistory(rows){
  $('databaseHistory').innerHTML=rows.length?rows.map(item=>`<div class="history-item"><span class="history-time">${formatDateTime(item.occurredAtUtc)}</span><span class="history-action">${escapeHtml(item.action)}</span><span class="history-user">${escapeHtml(item.userName)}</span><span class="history-detail">${item.databaseDate.slice(0,10)} · ${escapeHtml(item.detail)}</span></div>`).join(''):'<div class="settings-loading">아직 저장된 DB 업데이트 이력이 없습니다.</div>';
}
function formatDateTime(value){return new Intl.DateTimeFormat('ko-KR',{timeZone:'Asia/Seoul',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(value));}
function setMessage(id,message,isError=false,isSuccess=false){const element=$(id);element.textContent=message;element.classList.toggle('is-error',isError);element.classList.toggle('is-success',isSuccess);}
function escapeHtml(value){const element=document.createElement('div');element.textContent=String(value??'');return element.innerHTML;}
$('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST'});location.replace('/login');};
initialize();
