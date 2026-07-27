const $=id=>document.getElementById(id);
let settings=[];

async function loadSession(){
  const response=await fetch('/api/session');
  if(response.status===401||response.status===403){location.replace('/login');return false;}
  if(response.ok){const data=await response.json();$('sessionUser').textContent=data.userName||'로그인 사용자';}
  return true;
}

async function loadSettings(){
  try{
    const response=await fetch('/api/settings/employee-columns');
    if(response.status===401||response.status===403){location.replace('/login');return;}
    if(!response.ok)throw Error(`설정을 불러오지 못했습니다. (HTTP ${response.status})`);
    settings=await response.json();
    render();
    setStatus('');
  }catch(error){
    $('columnSettingRows').innerHTML=`<tr><td colspan="4" class="settings-loading">${escapeHtml(error.message)}</td></tr>`;
    setStatus(error.message,true);
  }
}

function render(){
  $('columnSettingRows').innerHTML=settings.map(item=>`<tr>
    <td class="settings-order">${item.order}</td>
    <td><span class="internal-key">${escapeHtml(item.key)}</span></td>
    <td class="default-name">${escapeHtml(item.defaultName)}</td>
    <td><input class="column-name-input" data-key="${escapeHtml(item.key)}" maxlength="50" value="${escapeHtml(item.displayName)}" aria-label="${escapeHtml(item.defaultName)} 표시 이름"></td>
  </tr>`).join('');
  document.querySelectorAll('.column-name-input').forEach(input=>input.oninput=validateInputs);
}

function validateInputs(){
  const inputs=[...document.querySelectorAll('.column-name-input')],names=new Map();
  inputs.forEach(input=>input.classList.remove('is-invalid'));
  let message='';
  for(const input of inputs){
    const name=input.value.trim().toLocaleLowerCase('ko-KR');
    if(!name){input.classList.add('is-invalid');message='열 이름은 비워 둘 수 없습니다.';continue;}
    if(names.has(name)){input.classList.add('is-invalid');names.get(name).classList.add('is-invalid');message='열 이름은 중복해서 사용할 수 없습니다.';}
    else names.set(name,input);
  }
  setStatus(message,Boolean(message));
  return !message;
}

async function saveSettings(){
  if(!validateInputs())return;
  const button=$('saveColumnsBtn'),request=[...document.querySelectorAll('.column-name-input')].map(input=>({key:input.dataset.key,displayName:input.value.trim()}));
  button.disabled=true;setStatus('저장하고 있습니다.');
  try{
    const response=await fetch('/api/settings/employee-columns',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)});
    const data=await response.json().catch(()=>({}));
    if(!response.ok)throw Error(data.message||'저장하지 못했습니다.');
    settings=data;render();setStatus('변경사항을 저장했습니다.',false,true);
  }catch(error){setStatus(error.message,true);}
  finally{button.disabled=false;}
}

async function resetSettings(){
  if(!confirm('모든 사원 DB 열 이름을 기본값으로 복원할까요?'))return;
  const button=$('resetColumnsBtn');button.disabled=true;setStatus('기본 이름으로 복원하고 있습니다.');
  try{
    const response=await fetch('/api/settings/employee-columns/reset',{method:'POST'});
    const data=await response.json().catch(()=>({}));
    if(!response.ok)throw Error(data.message||'복원하지 못했습니다.');
    settings=data;render();setStatus('기본 이름으로 복원했습니다.',false,true);
  }catch(error){setStatus(error.message,true);}
  finally{button.disabled=false;}
}

function setStatus(message,isError=false,isSuccess=false){const status=$('settingsStatus');status.textContent=message;status.classList.toggle('is-error',isError);status.classList.toggle('is-success',isSuccess);}
function escapeHtml(value){const element=document.createElement('div');element.textContent=String(value??'');return element.innerHTML;}

$('saveColumnsBtn').onclick=saveSettings;
$('resetColumnsBtn').onclick=resetSettings;
$('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST'});location.replace('/login');};
(async()=>{if(await loadSession())await loadSettings();})();
