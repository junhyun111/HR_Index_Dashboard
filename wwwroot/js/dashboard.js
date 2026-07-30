const $=id=>document.getElementById(id),fmt=new Intl.NumberFormat('ko-KR');let page=1,pageSize=10,initialized=false,timer,detailData,canViewSalary=false,canEdit=false,movementData={hires:[],terminations:[]},movementMode='hires';
const filterSelectIds=['workplaceFilter','deptFilter','positionFilter','jobGroupFilter','genderFilter'];
const defaultColumnNames={workplace:'사업장',parentDepartment:'상위부서',department:'부서',employeeNumber:'사번',name:'성명',position:'직위',workShift:'근무조',duty:'직책',jobGroup:'직군',employmentType:'사원구분',gender:'성별',birthDate:'생년월일',hireDate:'입사일자',terminationDate:'퇴사일자',annualSalary:'책정연봉',monthlyWage:'월임금',education:'최종학력',schoolName:'학교명',major:'전공'};let columnNames={...defaultColumnNames};
const localNow=new Date(Date.now()-new Date().getTimezoneOffset()*60000),todayValue=localNow.toISOString().slice(0,10);$('employeeDateFilter').value=todayValue;
const originalFetch=window.fetch.bind(window);window.fetch=(resource,options={})=>{const url=typeof resource==='string'?resource:resource.url;if(url.startsWith('/api/dashboard')||url.startsWith('/api/employees')){const headers=new Headers(options.headers||{});headers.set('X-Employee-Date',$('employeeDateFilter').value||todayValue);options={...options,headers};}return originalFetch(resource,options);};
function selectedDbName(){return `employee${($('employeeDateFilter').value||todayValue).replaceAll('-','').slice(2)}.db`;}function updateDateStatus(lastModifiedAt,isAutomaticallyUpdated){const selected=$('employeeDateFilter').value||todayValue,modified=lastModifiedAt?new Intl.DateTimeFormat('ko-KR',{timeZone:'Asia/Seoul',year:'numeric',month:'long',day:'numeric',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(lastModifiedAt)):null;$('dateStatus').textContent=isAutomaticallyUpdated?'최종 수정: 자동업데이트 된 DB입니다':modified?`최종 수정 ${modified}`:'저장된 데이터 없음';$('dateStatus').classList.toggle('is-past',selected!==todayValue);}
async function loadColumnNames(){try{const r=await fetch('/api/settings/employee-columns');if(!r.ok)return;const rows=await r.json();rows.forEach(x=>columnNames[x.key]=x.displayName);applyColumnNames(rows);}catch{}}
function applyColumnNames(rows){document.querySelectorAll('.employee-table th[data-key]').forEach(th=>th.textContent=columnNames[th.dataset.key]||th.textContent);document.querySelectorAll('#employeeForm [name]').forEach(input=>{const label=input.closest('label'),text=[...label.childNodes].find(x=>x.nodeType===Node.TEXT_NODE);if(text)text.nodeValue=`${columnNames[input.name]||defaultColumnNames[input.name]||input.name}${input.name==='annualSalary'||input.name==='monthlyWage'?'(원)':''} `;});$('workplaceFilter').options[0].textContent=`전체 ${columnNames.workplace}`;$('deptFilter').options[0].textContent=`전체 ${columnNames.department}`;$('positionFilter').options[0].textContent=`전체 ${columnNames.position}`;$('jobGroupFilter').options[0].textContent=`전체 ${columnNames.jobGroup}`;$('genderFilter').options[0].textContent=`전체 ${columnNames.gender}`;$('searchInput').placeholder=`${columnNames.employeeNumber}, ${columnNames.name}, ${columnNames.department}, ${columnNames.position}, ${columnNames.jobGroup}, ${columnNames.gender} 검색`;$('employeeSearchInput').placeholder=`${columnNames.name} 또는 ${columnNames.employeeNumber} 검색`;$('pasteArea').placeholder=rows.sort((a,b)=>a.order-b.order).map(x=>x.displayName).join('\t');document.querySelector('.paste-guide').innerHTML=`Excel 표를 머리글과 함께 붙여넣으세요. <strong>${esc(columnNames.employeeNumber)}만 필수</strong>이며, 빈 값·-·형식이 맞지 않는 값은 NULL로 저장됩니다.`;}
function hasEmployeeNumberHeader(headers){return headers.some(x=>{const value=x.trim();return value===defaultColumnNames.employeeNumber||value===columnNames.employeeNumber;});}
async function load(){const p=new URLSearchParams({page,pageSize});for(const [k,v] of Object.entries({workplace:$('workplaceFilter').value,department:$('deptFilter').value,position:$('positionFilter').value,jobGroup:$('jobGroupFilter').value,gender:$('genderFilter').value,search:$('searchInput').value.trim()}))if(v)p.set(k,v);try{const r=await fetch(`/api/dashboard?${p}`);if(r.status===401||r.status===403){location.replace('/login');return;}if(!r.ok)throw Error(`서버 오류(HTTP ${r.status})`);const d=await r.json();await session();if(!initialized)initFilters(d.filters);render(d);updateDateStatus(d.summary.lastModifiedAt,d.summary.isAutomaticallyUpdated);$('loadState').hidden=true;$('filterArea').hidden=false;$('dashboard').hidden=false;$('sourceStatus').textContent=`${selectedDbName()} · ${fmt.format(d.summary.totalCount)}명`;}catch(e){$('loadState').querySelector('h2').textContent='데이터를 불러오지 못했습니다';$('loadMessage').textContent=e.message;}}
async function session(){if($('employeeActions').dataset.checked)return;$('employeeActions').dataset.checked='1';const r=await fetch('/api/session');if(r.ok){const x=await r.json();canViewSalary=Boolean(x.canViewSalary);canEdit=Boolean(x.canEdit);$('sessionUser').textContent=x.userName||'로그인 사용자';$('employeeActions').hidden=!canEdit;$('addScheduledHireBtn').hidden=!canEdit;if(x.theme)window.setDashboardTheme(x.theme);}}
function initFilters(f){add('workplaceFilter',f.workplaces);add('deptFilter',f.departments);add('positionFilter',f.positions);add('jobGroupFilter',f.jobGroups);add('genderFilter',f.genders);initialized=true;}function add(id,a){(a||[]).forEach(v=>$(id).append(new Option(v,v)));}
function render(d){detailData=d;const s=d.summary;$('totalPeople').innerHTML=`${fmt.format(s.filteredCount)}<span class="kpi-unit">명</span>`;$('dataAsOf').textContent=s.dataAsOf?`(${s.dataAsOf.slice(0,10).replaceAll('-','.')} 기준)`:'';$('averageAge').textContent=s.averageAge??'-';$('averageAnnualSalary').textContent=s.averageAnnualSalary==null?'-':fmt.format(s.averageAnnualSalary);$('averageAnnualSalaryValue').hidden=!canViewSalary;$('averageAnnualSalaryLocked').hidden=canViewSalary;$('averageTenure').textContent=s.averageTenure??'-';$('hiresThisYear').innerHTML=`${s.hiresThisYear}<span class="kpi-unit">명</span>`;$('terminationsThisYear').innerHTML=`${s.terminationsThisYear}<span class="kpi-unit">명</span>`;renderPrimaryPanel(d);pie('genderChart',Object.entries(d.genders).map(([label,value])=>({label,value})),label=>label.startsWith('남')?'#3978f6':label.startsWith('여')?'#e86f9e':null);pie('jobGroupChart',d.jobGroups);pie('educationChart',d.educationGroups||[]);loadPersonnelMovements();table(d.employees,d.pagination);}
const employeeProfileFields=[
  {key:'workplace',max:100},{key:'parentDepartment',max:100},{key:'department',max:100},
  {key:'employeeNumber',max:50,required:true},{key:'name',max:100},{key:'position',max:50},
  {key:'workShift',max:50},{key:'duty',max:50},{key:'jobGroup',max:50},
  {key:'employmentType',max:50},{key:'gender',max:20},
  {key:'birthDate',type:'date'},{key:'hireDate',type:'date'},{key:'terminationDate',type:'date'},
  {key:'annualSalary',type:'number'},{key:'monthlyWage',type:'number'},
  {key:'education',max:100},{key:'schoolName',max:150},{key:'major',max:100}
];
function profileValue(employee,key){
  const value=employee[key];
  if(value==null||value==='')return '-';
  if(key.endsWith('Date'))return String(value).slice(0,10).replaceAll('-','.');
  if(key==='annualSalary'||key==='monthlyWage')return won(value);
  return String(value);
}
function renderPrimaryPanel(data){
  const employee=data.summary.filteredCount===1&&data.employees?.length===1?data.employees[0]:null;
  if(employee){renderEmployeeProfile(employee);return;}
  $('primaryChartCard').classList.remove('is-employee-profile');
  $('primaryChartTitle').textContent='부서별 인원';
  $('primaryChartAction').textContent='HEADCOUNT';
  $('deptChart').className='bar-chart';
  departmentBars(data.departments||[]);
}
function renderEmployeeProfile(employee,editing=false){
  const target=$('deptChart'),action=$('primaryChartAction'),initial=(employee.name||employee.employeeNumber||'人').trim().slice(0,1);
  $('primaryChartCard').classList.add('is-employee-profile');
  $('primaryChartTitle').textContent='인적사항';
  target.className=`employee-profile${editing?' is-editing':''}`;
  action.innerHTML=canEdit?(editing?`<span class="profile-action-group"><button class="profile-action-button profile-cancel-button" id="profileCancelButton" type="button">취소</button><button class="profile-action-button" id="profileActionButton" type="submit" form="employeeProfileForm">저장하기</button></span>`:`<button class="profile-action-button" id="profileActionButton" type="button">수정하기</button>`):'';
  const identity=`<div class="employee-profile-identity"><div class="employee-profile-avatar" aria-hidden="true">${esc(initial)}</div><div><strong>${esc(employee.name||'이름 미입력')}</strong><span>${esc(employee.department||employee.parentDepartment||'부서 미입력')} · ${esc(employee.position||'직위 미입력')}</span></div></div>`;
  if(!editing){
    target.innerHTML=`${identity}<div class="employee-profile-grid">${employeeProfileFields.map(field=>`<div class="employee-profile-item"><span>${esc(columnNames[field.key]||defaultColumnNames[field.key]||field.key)}</span><strong title="${esc(profileValue(employee,field.key))}">${esc(profileValue(employee,field.key))}</strong></div>`).join('')}</div>`;
    if(canEdit)$('profileActionButton').onclick=()=>renderEmployeeProfile(employee,true);
    return;
  }
  target.innerHTML=`${identity}<form class="employee-profile-form" id="employeeProfileForm"><div class="employee-profile-grid">${employeeProfileFields.map(field=>{
    const value=field.key.endsWith('Date')&&employee[field.key]?String(employee[field.key]).slice(0,10):(employee[field.key]??'');
    const numeric=field.type==='number',label=columnNames[field.key]||defaultColumnNames[field.key]||field.key;
    return `<label class="employee-profile-field"><span>${esc(label)}${numeric?' (원)':''}</span><input name="${field.key}" type="${field.type||'text'}" value="${esc(value)}" ${field.required?'required':''} ${field.max?`maxlength="${field.max}"`:''} ${numeric?'min="0" step="1"':''}></label>`;
  }).join('')}</div><p class="employee-profile-error" id="employeeProfileError"></p></form>`;
  $('employeeProfileForm').onsubmit=event=>saveEmployeeProfile(event,employee);
  $('profileCancelButton').onclick=()=>renderEmployeeProfile(employee,false);
}
async function saveEmployeeProfile(event,employee){
  event.preventDefault();
  const form=event.currentTarget,button=$('profileActionButton'),data={};
  employeeFields.forEach(name=>{const input=form.elements[name];data[name]=input?.value.trim()||null;});
  ['annualSalary','monthlyWage'].forEach(name=>{if(data[name]!=null)data[name]=Number(data[name]);});
  button.disabled=true;button.textContent='저장 중...';$('employeeProfileError').textContent='';
  try{
    const response=await fetch(`/api/employees/${employee.id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});
    const result=await response.json().catch(()=>({}));
    if(!response.ok)throw Error(result.message||'저장하지 못했습니다.');
    $('importStatus').textContent='인적사항을 DB에 반영했습니다.';
    await load();
  }catch(error){
    $('employeeProfileError').textContent=error.message;
    button.disabled=false;button.textContent='저장하기';
  }
}
function departmentBars(a){
  if(!a.length){$('deptChart').innerHTML='<div class="chart-empty">조회 결과 없음</div>';return;}
  const segmentColor=(index,count)=>{
    const start=[83,107,142],end=[212,220,230],ratio=count<=1?0:index/(count-1);
    return `rgb(${start.map((value,i)=>Math.round(value+(end[i]-value)*ratio)).join(',')})`;
  };
  const max=Math.max(1,...a.map(x=>x.value));
  $('deptChart').innerHTML=a.map(department=>{
    const positions=department.positions||[];
    const segments=positions.map((position,index)=>{
      const tooltip=`${position.label} · ${fmt.format(position.value)}명`;
      return `<span class="position-segment" tabindex="0" role="img" aria-label="${esc(tooltip)}" data-tooltip="${esc(tooltip)}" style="width:${position.value/department.value*100}%;--segment-color:${segmentColor(index,positions.length)}"></span>`;
    }).join('');
    return `<div class="bar-row department-bar-row"><span class="bar-label" title="${esc(department.label)}">${esc(department.label)}</span><div class="bar-track department-bar-track"><div class="department-bar-fill" style="width:${department.value/max*100}%">${segments}</div></div><span class="bar-number">${department.value}</span></div>`;
  }).join('');
}
function bars(id,a){const m=Math.max(1,...a.map(x=>x.value));$(id).innerHTML=a.length?a.map(x=>`<div class="bar-row"><span class="bar-label">${esc(x.label)}</span><div class="bar-track"><div class="bar-fill" style="width:${x.value/m*100}%"></div></div><span class="bar-number">${x.value}</span></div>`).join(''):'<div class="chart-empty">조회 결과 없음</div>';}
async function loadPersonnelMovements(){
  try{
    const r=await fetch('/api/personnel-movements');
    if(!r.ok)throw Error(`HTTP ${r.status}`);
    movementData=await r.json();
    renderPersonnelMovements();
  }catch{
    $('movementList').innerHTML='<div class="movement-empty">입·퇴사자 정보를 불러오지 못했습니다.</div>';
  }
}
function renderPersonnelMovements(){
  const rows=movementMode==='hires'?(movementData.hires||[]):(movementData.terminations||[]);
  $('hireMovementCount').textContent=(movementData.hires||[]).length;
  $('terminationMovementCount').textContent=(movementData.terminations||[]).length;
  $('movementDateHeading').textContent=movementMode==='hires'?'입사일자':'퇴사일자';
  [['hireMovementTab','hires'],['terminationMovementTab','terminations']].forEach(([id,mode])=>{
    const active=movementMode===mode;
    $(id).classList.toggle('active',active);
    $(id).setAttribute('aria-selected',String(active));
  });
  $('movementList').innerHTML=rows.length?rows.map(item=>`<div class="movement-row ${item.canDelete?'is-scheduled':''}"><span>${item.date.slice(0,10).replaceAll('-','.')}</span><strong title="${esc(item.name)}">${esc(item.name)}${item.type==='입사예정자'?'<small class="movement-type">입사예정자</small>':''}</strong><span title="${esc(item.department||'-')}">${esc(item.department||'-')}</span><span title="${esc(item.position||'-')}">${esc(item.position||'-')}</span>${item.canDelete&&canEdit?`<button class="movement-delete" type="button" data-id="${item.id}" aria-label="${esc(item.name)} 입사예정 취소">×</button>`:''}</div>`).join(''):'<div class="movement-empty">해당 기간의 인원이 없습니다.</div>';
  $('movementList').querySelectorAll('.movement-delete').forEach(button=>button.onclick=()=>deleteScheduledHire(Number(button.dataset.id)));
}
let movementTransitionVersion=0,movementTransitionTarget='hires',movementLockedUntil=0,movementUnlockTimer;
function lockMovementNavigation(){
  movementLockedUntil=Date.now()+1300;
  clearTimeout(movementUnlockTimer);
  [$('hireMovementTab'),$('terminationMovementTab')].forEach(button=>button.disabled=true);
  movementUnlockTimer=setTimeout(()=>{
    if(Date.now()>=movementLockedUntil)
      [$('hireMovementTab'),$('terminationMovementTab')].forEach(button=>button.disabled=false);
  },1300);
}
async function switchMovementMode(nextMode){
  if(nextMode===movementTransitionTarget||Date.now()<movementLockedUntil)return;
  lockMovementNavigation();
  const list=$('movementList'),version=++movementTransitionVersion,direction=nextMode==='terminations'?1:-1;
  movementTransitionTarget=nextMode;
  list.getAnimations().forEach(animation=>animation.cancel());
  list.style.removeProperty('opacity');list.style.removeProperty('transform');
  if(nextMode===movementMode)return;
  const reduceMotion=false;
  try{
    if(!reduceMotion)await list.animate(
      [{opacity:1,transform:'translateX(0)'},{opacity:0,transform:`translateX(${-direction*14}px)`}],
      {duration:120,easing:'ease-in',fill:'forwards'}
    ).finished;
    if(version!==movementTransitionVersion)return;
    movementMode=nextMode;renderPersonnelMovements();
    if(!reduceMotion)await list.animate(
      [{opacity:0,transform:`translateX(${direction*14}px)`},{opacity:1,transform:'translateX(0)'}],
      {duration:180,easing:'cubic-bezier(.2,.75,.25,1)',fill:'forwards'}
    ).finished;
  }catch(error){
    if(version===movementTransitionVersion&&error?.name!=='AbortError')console.warn('입·퇴사자 전환이 중단되었습니다.',error);
  }finally{
    if(version===movementTransitionVersion){
      list.getAnimations().forEach(animation=>animation.cancel());
      list.style.removeProperty('opacity');list.style.removeProperty('transform');
      movementTransitionTarget=movementMode;
    }
  }
}
async function deleteScheduledHire(id){
  const item=(movementData.hires||[]).find(x=>x.id===id);
  if(!item||!confirm(`${item.name}님의 입사예정을 취소할까요?`))return;
  const r=await fetch(`/api/personnel-movements/hires/${id}`,{method:'DELETE'});
  if(!r.ok){const x=await r.json().catch(()=>({}));return alert(x.message||'입사예정을 취소하지 못했습니다.');}
  await loadPersonnelMovements();
}
function pie(id,a,colorForLabel=null){const colors=['#3978f6','#35b7ca','#38a47b','#f0a43c','#8b6fd6','#e66b7a'],total=a.reduce((s,x)=>s+x.value,0);if(!total){$(id).innerHTML='<div class="chart-empty">조회 결과 없음</div>';return;}const itemColors=a.map((x,i)=>colorForLabel?.(x.label)||colors[i%colors.length]);let end=0;const stops=a.map((x,i)=>{const start=end;end+=x.value/total*100;return `${itemColors[i]} ${start}% ${end}%`;});$(id).innerHTML=`<div class="pie-layout"><div class="donut" style="background:conic-gradient(${stops.join(',')})"><div class="donut-center"><strong>${total}</strong><span>조회 인원</span></div></div><div class="legend">${a.map((x,i)=>`<span><i class="dot" style="background:${itemColors[i]}"></i>${esc(x.label)} ${x.value}명</span>`).join('')}</div></div>`;}
function ageTenureScatter(points,targetId='detailChart'){
  const target=$(targetId);
  target.classList.remove('salary-band-chart');
  target.classList.add('tenure-density-detail');
  if(!points.length){target.innerHTML='<div class="chart-empty">생년월일과 입사일자 데이터가 없습니다.</div>';return;}
  const width=680,height=380,left=58,right=24,top=24,bottom=48,plotWidth=width-left-right,plotHeight=height-top-bottom;
  let minAge=Math.floor(Math.min(...points.map(x=>x.age))/5)*5,maxAge=Math.ceil(Math.max(...points.map(x=>x.age))/5)*5;
  if(maxAge<=minAge)maxAge=minAge+5;
  const minTenure=0,maxTenure=Math.max(2,Math.ceil(Math.max(...points.map(x=>x.tenure))/2)*2);
  const xScale=value=>left+(value-minAge)/(maxAge-minAge)*plotWidth;
  const yScale=value=>top+plotHeight-(value-minTenure)/(maxTenure-minTenure)*plotHeight;
  const tickCount=4,xTicks=Array.from({length:tickCount+1},(_,i)=>minAge+(maxAge-minAge)*i/tickCount),yTicks=Array.from({length:tickCount+1},(_,i)=>minTenure+(maxTenure-minTenure)*i/tickCount);
  target.innerHTML=`<svg class="tenure-density-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="직원별 연령과 근속연수 산점도">
    ${xTicks.map(value=>`<line class="density-grid" x1="${xScale(value)}" y1="${top}" x2="${xScale(value)}" y2="${top+plotHeight}"></line><text class="density-tick" x="${xScale(value)}" y="${height-16}" text-anchor="middle">${Math.round(value)}</text>`).join('')}
    ${yTicks.map(value=>`<line class="density-grid" x1="${left}" y1="${yScale(value)}" x2="${width-right}" y2="${yScale(value)}"></line><text class="density-tick" x="${left-6}" y="${yScale(value)+3}" text-anchor="end">${Math.round(value)}</text>`).join('')}
    ${points.map((point,index)=>`<circle class="density-point" style="--point-delay:${Math.min(index*8,420)}ms" cx="${xScale(point.age).toFixed(2)}" cy="${yScale(point.tenure).toFixed(2)}" r="3.2"><title>연령 ${point.age.toFixed(1)}세 · 근속 ${point.tenure.toFixed(1)}년</title></circle>`).join('')}
    <line class="density-axis" x1="${left}" y1="${top+plotHeight}" x2="${width-right}" y2="${top+plotHeight}"></line><line class="density-axis" x1="${left}" y1="${top}" x2="${left}" y2="${top+plotHeight}"></line>
    <text class="density-label" x="${left+plotWidth/2}" y="${height-2}" text-anchor="middle">연령(세)</text>
    <text class="density-label" x="9" y="${top+plotHeight/2}" text-anchor="middle" transform="rotate(-90 9 ${top+plotHeight/2})">근속연수(년)</text>
  </svg>`;
}
const val=x=>esc(x||'-'),date=x=>x?x.slice(0,10):'-',won=x=>{if(x==null)return '-';const amount=Math.max(0,Math.trunc(Number(x))),eok=Math.floor(amount/100000000),man=Math.floor(amount%100000000/10000);if(eok&&man)return `${eok}억 ${man}만원`;if(eok)return `${eok}억원`;if(man)return `${man}만원`;return `${amount}원`;};function table(a,p){$('employeeRows').innerHTML=a.length?a.map(x=>`<tr><td>${val(x.workplace)}</td><td>${val(x.parentDepartment)}</td><td>${val(x.department)}</td><td>${val(x.employeeNumber)}</td><td>${val(x.name)}</td><td>${val(x.position)}</td><td>${val(x.workShift)}</td><td>${val(x.duty)}</td><td>${val(x.jobGroup)}</td><td>${val(x.employmentType)}</td><td>${val(x.gender)}</td><td>${date(x.birthDate)}</td><td>${date(x.hireDate)}</td><td>${date(x.terminationDate)}</td><td>${won(x.annualSalary)}</td><td>${won(x.monthlyWage)}</td><td>${val(x.education)}</td><td>${val(x.schoolName)}</td><td>${val(x.major)}</td></tr>`).join(''):'<tr><td class="table-empty" colspan="19">조건에 맞는 사원이 없습니다.</td></tr>';$('resultCount').textContent=`${fmt.format(p.totalCount)}명`;page=p.page;renderPagination(p.pages);}function esc(v){const d=document.createElement('div');d.textContent=String(v??'');return d.innerHTML;}
function renderPagination(pages){const items=[],start=Math.max(2,page-2),end=Math.min(pages-1,page+2);items.push(`<button class="page-btn page-edge" data-page="1" ${page===1?'disabled':''}>처음</button><button class="page-btn" data-page="${page-1}" ${page===1?'disabled':''}>‹</button>`);items.push(pageButton(1));if(start>2)items.push('<span class="page-ellipsis">…</span>');for(let n=start;n<=end;n++)items.push(pageButton(n));if(end<pages-1)items.push('<span class="page-ellipsis">…</span>');if(pages>1)items.push(pageButton(pages));items.push(`<button class="page-btn" data-page="${page+1}" ${page===pages?'disabled':''}>›</button><button class="page-btn page-edge" data-page="${pages}" ${page===pages?'disabled':''}>마지막</button>`);$('pageControls').innerHTML=items.join('');$('pageControls').querySelectorAll('button[data-page]:not(:disabled)').forEach(button=>button.onclick=()=>{page=Number(button.dataset.page);load();});}
function pageButton(n){return `<button class="page-btn ${n===page?'active':''}" data-page="${n}" ${n===page?'disabled':''}>${n}</button>`;}
filterSelectIds.forEach(id=>$(id).onchange=()=>{page=1;load();});$('searchInput').oninput=()=>{clearTimeout(timer);timer=setTimeout(()=>{page=1;load();},250);};$('resetBtn').onclick=()=>{filterSelectIds.forEach(id=>$(id).value='');$('searchInput').value='';page=1;load();};$('pageSizeSelect').onchange=()=>{pageSize=Number($('pageSizeSelect').value);page=1;load();};$('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST'});location.replace('/login');};
const employeeFields=['workplace','parentDepartment','department','employeeNumber','name','position','workShift','duty','jobGroup','employmentType','gender','birthDate','hireDate','terminationDate','annualSalary','monthlyWage','education','schoolName','major'];let employeeSearchMode='edit',employeeSearchTimer;
$('editMenuBtn').onclick=e=>{e.stopPropagation();$('editMenu').hidden=!$('editMenu').hidden;};document.addEventListener('click',e=>{if(!$('editMenu').contains(e.target)&&e.target!==$('editMenuBtn'))$('editMenu').hidden=true;});
$('editMenu').onclick=e=>{const action=e.target.dataset.action;if(!action)return;$('editMenu').hidden=true;if(action==='add')openEmployee();if(action==='edit'||action==='delete')openEmployeeSearch(action);if(action==='paste')openPaste();if(action==='export')exportEmployees();if(action==='delete-all')deleteAllEmployees();};
function openEmployee(x=null){$('employeeForm').reset();$('employeeFormError').textContent='';$('employeeId').value=x?.id||'';$('employeeDialogTitle').textContent=x?'직원 수정':'직원 추가';if(x)employeeFields.forEach(name=>{const input=$('employeeForm').elements[name];if(input)input.value=name.endsWith('Date')&&x[name]?x[name].slice(0,10):(x[name]??'');});$('employeeDialog').showModal();}
function openEmployeeSearch(mode){employeeSearchMode=mode;$('employeeSearchTitle').textContent=mode==='delete'?'직원 삭제':'직원 수정';$('employeeSearchInput').value='';$('employeeSearchDialog').showModal();searchEmployees();}
async function searchEmployees(){const q=encodeURIComponent($('employeeSearchInput').value.trim()),r=await fetch(`/api/employees/search?q=${q}`);if(!r.ok){$('employeeSearchResults').innerHTML='<div class="employee-search-empty">검색 결과를 불러오지 못했습니다.</div>';return;}const rows=await r.json();$('employeeSearchResults').innerHTML=rows.length?rows.map(x=>`<button class="employee-result" type="button" data-id="${x.id}"><strong>${esc(x.name||'-')}</strong><span>${esc(x.employeeNumber)}</span><span>${esc(x.department||'-')}</span></button>`).join(''):'<div class="employee-search-empty">검색 결과가 없습니다.</div>';$('employeeSearchResults').querySelectorAll('button').forEach((button,i)=>button.onclick=()=>selectEmployee(rows[i]));}
async function selectEmployee(x){if(employeeSearchMode==='delete'){if(!confirm(`${x.name||'이름 없음'} (${x.employeeNumber}) 직원을 삭제할까요?`))return;const r=await fetch(`/api/employees/${x.id}`,{method:'DELETE'});if(!r.ok){const data=await r.json().catch(()=>({}));return alert(data.message||'삭제하지 못했습니다.');}$('employeeSearchDialog').close();await refreshDashboard('직원을 삭제했습니다.');return;}$('employeeSearchDialog').close();openEmployee(x);}
$('employeeSearchInput').oninput=()=>{clearTimeout(employeeSearchTimer);employeeSearchTimer=setTimeout(searchEmployees,250);};$('closeSearchBtn').onclick=()=>$('employeeSearchDialog').close();
$('hireMovementTab').onclick=()=>switchMovementMode('hires');
$('terminationMovementTab').onclick=()=>switchMovementMode('terminations');
$('addScheduledHireBtn').onclick=()=>{
  $('scheduledHireForm').reset();$('scheduledHireError').textContent='';
  const tomorrow=new Date();tomorrow.setDate(tomorrow.getDate()+1);
  const localTomorrow=new Date(tomorrow.getTime()-tomorrow.getTimezoneOffset()*60000).toISOString().slice(0,10);
  $('scheduledHireForm').elements.hireDate.min=localTomorrow;
  $('scheduledHireForm').elements.hireDate.value=localTomorrow;
  $('scheduledHireDialog').showModal();
};
['closeScheduledHireBtn','cancelScheduledHireBtn'].forEach(id=>$(id).onclick=()=>$('scheduledHireDialog').close());
$('scheduledHireForm').onsubmit=async e=>{
  e.preventDefault();$('scheduledHireError').textContent='';
  const form=new FormData(e.currentTarget),data=Object.fromEntries(form.entries());
  const r=await fetch('/api/personnel-movements/hires',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});
  if(!r.ok){const x=await r.json().catch(()=>({}));$('scheduledHireError').textContent=x.message||'입사예정자를 등록하지 못했습니다.';return;}
  $('scheduledHireDialog').close();movementMode='hires';movementTransitionTarget='hires';await loadPersonnelMovements();
};
$('employeeForm').onsubmit=async e=>{e.preventDefault();const id=$('employeeId').value,data={};employeeFields.forEach(name=>data[name]=$('employeeForm').elements[name].value||null);['annualSalary','monthlyWage'].forEach(name=>{if(data[name]!=null)data[name]=Number(data[name]);});const r=await fetch(id?`/api/employees/${id}`:'/api/employees',{method:id?'PUT':'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});if(!r.ok){const x=await r.json().catch(()=>({}));$('employeeFormError').textContent=x.message||'저장하지 못했습니다.';return;}$('employeeDialog').close();await refreshDashboard(id?'직원 정보를 수정했습니다.':'직원을 추가했습니다.');};['closeEmployeeBtn','cancelEmployeeBtn'].forEach(id=>$(id).onclick=()=>$('employeeDialog').close());
async function refreshDashboard(message){initialized=false;filterSelectIds.forEach(id=>$(id).options.length=1);page=1;await load();$('importStatus').textContent=message;}
async function exportEmployees(){const r=await fetch('/api/employees/export');if(!r.ok)return alert('내보내기 실패');const a=document.createElement('a');a.href=URL.createObjectURL(await r.blob());a.download=`hr-employees-${$('employeeDateFilter').value||todayValue}.xlsx`;a.click();URL.revokeObjectURL(a.href);}
async function deleteAllEmployees(){const selected=$('employeeDateFilter').value||todayValue,count=detailData?.summary?.totalCount||0,displayDate=selected.replaceAll('-','.');if(count===0)return alert(`${displayDate}에 삭제할 사원이 없습니다.`);if(!confirm(`${displayDate} 사원 ${fmt.format(count)}명을 모두 삭제할까요?\n삭제한 데이터는 복구할 수 없습니다.`))return;const r=await fetch('/api/employees/all',{method:'DELETE'}),x=await r.json().catch(()=>({}));if(!r.ok)return alert(x.message||'전체 삭제에 실패했습니다.');await refreshDashboard(`${displayDate} 사원 ${fmt.format(x.deleted||0)}명을 모두 삭제했습니다.`);}
function openPaste(){$('pasteArea').value='';$('deleteMissingEmployees').checked=false;summary();$('pasteDialog').showModal();}['closePasteBtn','cancelPasteBtn'].forEach(id=>$(id).onclick=()=>$('pasteDialog').close());$('pasteArea').oninput=summary;function summary(){const lines=$('pasteArea').value.trim().split(/\r?\n/).filter(x=>x.trim()),cols=lines[0]?.split('\t').length||0,hasHeader=hasEmployeeNumberHeader(lines[0]?.split('\t')||[]);$('pasteSummary').textContent=lines.length?(hasHeader?`${Math.max(0,lines.length-1)}개 데이터 행 · ${cols}열 감지`:`${lines.length}개 행 · ${cols}열 감지 · 필수 머리글인 ${columnNames.employeeNumber}을(를) 포함해 주세요.`):'붙여넣은 데이터가 없습니다.';$('applyPasteBtn').disabled=lines.length<2||!hasHeader;}
$('excelUploadBtn').onclick=()=>$('excelUploadInput').click();
$('excelUploadInput').onchange=async()=>{
  const input=$('excelUploadInput'),file=input.files?.[0];input.value='';
  if(!file)return;
  if(!/\.(xlsx|xlsm|xls|csv)$/i.test(file.name)){alert('.xlsx, .xlsm, .xls 또는 .csv 파일을 선택해 주세요.');return;}
  const selected=$('employeeDateFilter').value||todayValue,displayDate=selected.replaceAll('-','.');
  const deleteMissing=$('deleteMissingEmployees').checked;
  if(!confirm(`${file.name} 파일을 ${displayDate} 직원 현황에 반영할까요?${deleteMissing?'\n\n주의: 파일에 없는 인원은 DB에서 삭제됩니다.':''}`))return;
  const button=$('excelUploadBtn'),originalText=button.textContent;
  button.disabled=true;button.dataset.loading='true';button.textContent='업로드 중...';$('pasteSummary').textContent=`${file.name} 파일을 확인하고 있습니다.`;
  try{
    const body=new FormData();body.append('file',file);
    const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),120000);let response;
    try{response=await fetch(`/api/employees/import?deleteMissing=${deleteMissing}`,{method:'POST',body,signal:controller.signal});}finally{clearTimeout(timeout);}
    const result=await response.json().catch(()=>({}));
    if(!response.ok)throw Error(result.message||`업로드 실패(HTTP ${response.status})`);
    $('importStatus').textContent=`${selected} Excel 반영 완료 · 수정 ${result.updated}명 / 추가 ${result.added}명${result.deleted?` / 삭제 ${result.deleted}명`:''}`;
    $('pasteDialog').close();initialized=false;filterSelectIds.forEach(id=>$(id).options.length=1);page=1;await load();
  }catch(error){
    $('pasteSummary').textContent=error.name==='AbortError'?'오류: 처리 시간이 2분을 초과했습니다.':`오류: ${error.message}`;
  }finally{
    button.textContent=originalText;delete button.dataset.loading;button.disabled=false;
  }
};
$('pasteForm').onsubmit=async e=>{e.preventDefault();const firstRow=$('pasteArea').value.trim().split(/\r?\n/,1)[0]?.split('\t').map(x=>x.trim())||[];if(!hasEmployeeNumberHeader(firstRow)){$('pasteSummary').textContent=`오류: Excel에서 필수 머리글인 ${columnNames.employeeNumber}을(를) 포함해 복사해 주세요.`;return;}const selected=$('employeeDateFilter').value||todayValue,displayDate=selected.replaceAll('-','.'),deleteMissing=$('deleteMissingEmployees').checked;if(!confirm(`${displayDate} 직원 현황에 ${columnNames.employeeNumber} 기준으로 반영하시겠습니까?${deleteMissing?'\n\n주의: 표에 없는 인원은 DB에서 삭제됩니다.':''}`))return;const button=$('applyPasteBtn'),originalText=button.textContent;button.disabled=true;button.dataset.loading='true';button.textContent='DB 반영 중...';$('pasteSummary').textContent='데이터를 확인하고 저장하고 있습니다.';try{const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),120000);let r;try{r=await fetch('/api/employees/paste',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:$('pasteArea').value,deleteMissing}),signal:controller.signal});}finally{clearTimeout(timeout);}const x=await r.json().catch(()=>({}));if(!r.ok)throw Error(x.message||`반영 실패(HTTP ${r.status})`);$('importStatus').textContent=`${selected} 반영 완료 · 수정 ${x.updated}명 / 추가 ${x.added}명${x.deleted?` / 삭제 ${x.deleted}명`:''}`;$('pasteDialog').close();initialized=false;filterSelectIds.forEach(id=>$(id).options.length=1);page=1;await load();}catch(e){$('pasteSummary').textContent=e.name==='AbortError'?'오류: 처리 시간이 2분을 초과했습니다. 서버 상태를 확인해 주세요.':`오류: ${e.message}`;}finally{button.textContent=originalText;delete button.dataset.loading;button.disabled=false;}};loadColumnNames().finally(load);
function detailVerticalBars(rows){$('detailChart').classList.remove('salary-band-chart','tenure-density-detail');if(!rows.length){$('detailChart').innerHTML='<div class="chart-empty">조회 결과 없음</div>';return;}const width=680,height=330,left=18,right=18,top=35,bottom=48,plotWidth=width-left-right,plotHeight=height-top-bottom,max=Math.max(1,...rows.map(x=>x.value)),groupWidth=plotWidth/rows.length,barWidth=Math.min(46,groupWidth*.62),baseline=top+plotHeight;$('detailChart').innerHTML=`<svg class="detail-vertical-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="세로 막대 상세 그래프"><line class="detail-axis" x1="${left}" y1="${baseline}" x2="${width-right}" y2="${baseline}"></line>${rows.map((item,index)=>{const barHeight=item.value/max*plotHeight,x=left+groupWidth*index+(groupWidth-barWidth)/2,y=baseline-barHeight,center=x+barWidth/2,basis=item.basisDate?` · ${item.basisDate.slice(0,10)} 기준`:item.targetDate?' · DB 없음':'';return `<text class="detail-column-value" x="${center}" y="${Math.max(18,y-9)}" text-anchor="middle">${fmt.format(item.value)}명</text><rect class="detail-column-bar" x="${x}" y="${y}" width="${barWidth}" height="${barHeight}" rx="7"><title>${esc(item.label)} ${item.value}명${esc(basis)}</title></rect><text class="detail-column-label" x="${center}" y="${baseline+26}" text-anchor="middle">${esc(item.label)}</text>`;}).join('')}</svg>`;}
let salaryDetailIndex=0,tenureDetailIndex=0,detailSwitchMode='salary';
function renderDetailSwitchDots(label,index,count){
  $('salaryDetailDots').setAttribute('aria-label',`${label} 그래프 ${index+1}/${count}`);
  $('salaryDetailDots').innerHTML=Array.from({length:count},(_,dotIndex)=>`<i class="${dotIndex===index?'active':''}"></i>`).join('');
  $('salaryDetailPrev').setAttribute('aria-label',`이전 ${label} 그래프`);
  $('salaryDetailNext').setAttribute('aria-label',`다음 ${label} 그래프`);
}
function renderSalaryDetail(){
  const views=[
    {title:'연봉 구간별 인원',description:'단위: 만원 · 연봉이 입력된 인원의 분포입니다.'},
    {title:'직위별 연봉 밴드',description:'단위: 만원 · 박스는 연봉의 1~3사분위, 선은 최솟값·최댓값, ◆는 중앙값입니다.'},
    {title:'연령대·성별 연봉 밴드',description:'단위: 만원 · 각 연령대의 남성·여성 연봉 분포이며, 점은 중앙값입니다.'}
  ];
  const view=views[salaryDetailIndex];
  $('salaryBandTooltip').hidden=true;
  renderDetailSwitchDots('연봉',salaryDetailIndex,views.length);
  $('detailTitle').textContent=view.title;
  $('detailDescription').textContent=view.description;
  if(salaryDetailIndex===1)salaryPositionBandChart(detailData.salaryPositionBands||[]);
  else if(salaryDetailIndex===2)salaryAgeGenderBandChart(detailData.salaryAgeGenderBands||[]);
  else detailVerticalBars(detailData.annualSalaryGroups||[]);
}
function renderTenureDetail(){
  const views=[
    {title:'근속연수별 인원',description:'입사일자가 입력된 인원의 근속 구간입니다.'},
    {title:'연령·근속 분포',description:'생년월일과 입사일자가 입력된 직원의 연령과 근속연수 분포입니다.'}
  ];
  const view=views[tenureDetailIndex];
  $('salaryBandTooltip').hidden=true;
  renderDetailSwitchDots('근속연수',tenureDetailIndex,views.length);
  $('detailTitle').textContent=view.title;
  $('detailDescription').textContent=view.description;
  if(tenureDetailIndex===1)ageTenureScatter(detailData.ageTenurePoints||[]);
  else detailVerticalBars(detailData.tenureGroups||[]);
}
let salaryDetailTransitioning=false,salaryDetailTransitionVersion=0,salaryDetailLockedUntil=0,salaryDetailUnlockTimer;
function lockSalaryDetailNavigation(){
  salaryDetailLockedUntil=Date.now()+1300;
  clearTimeout(salaryDetailUnlockTimer);
  [$('salaryDetailPrev'),$('salaryDetailNext')].forEach(button=>button.disabled=true);
  salaryDetailUnlockTimer=setTimeout(()=>{
    if(Date.now()>=salaryDetailLockedUntil)
      [$('salaryDetailPrev'),$('salaryDetailNext')].forEach(button=>button.disabled=false);
  },1300);
}
function resetSalaryDetailTransition(){
  salaryDetailTransitionVersion++;
  const chart=$('detailChart');
  chart.getAnimations().forEach(animation=>animation.cancel());
  chart.classList.remove('is-transitioning');
  chart.style.removeProperty('opacity');
  chart.style.removeProperty('transform');
  salaryDetailTransitioning=false;
}
async function changeSalaryDetail(direction){
  if(salaryDetailTransitioning||Date.now()<salaryDetailLockedUntil)return;
  lockSalaryDetailNavigation();
  const chart=$('detailChart'),reduceMotion=false;
  const transitionVersion=++salaryDetailTransitionVersion;
  salaryDetailTransitioning=true;chart.classList.add('is-transitioning');
  try{
    if(!reduceMotion)await chart.animate([{opacity:1,transform:'translateX(0)'},{opacity:0,transform:`translateX(${-direction*22}px)`}],{duration:150,easing:'ease-in',fill:'forwards'}).finished;
    if(transitionVersion!==salaryDetailTransitionVersion)return;
    if(detailSwitchMode==='tenure'){
      tenureDetailIndex=(tenureDetailIndex+direction+2)%2;
      renderTenureDetail();
    }else{
      salaryDetailIndex=(salaryDetailIndex+direction+3)%3;
      renderSalaryDetail();
    }
    if(!reduceMotion)await chart.animate([{opacity:0,transform:`translateX(${direction*22}px)`},{opacity:1,transform:'translateX(0)'}],{duration:220,easing:'cubic-bezier(.2,.75,.25,1)',fill:'forwards'}).finished;
  }catch(error){
    if(transitionVersion===salaryDetailTransitionVersion&&error?.name!=='AbortError')console.warn('상세 그래프 전환이 중단되었습니다.',error);
  }finally{
    if(transitionVersion===salaryDetailTransitionVersion)resetSalaryDetailTransition();
  }
}
function salaryPositionBandChart(rows){
  $('detailChart').classList.remove('tenure-density-detail');$('detailChart').classList.add('salary-band-chart');
  if(!rows.length){$('detailChart').innerHTML='<div class="chart-empty">설정된 직위가 없습니다.</div>';return;}
  const left=62,right=18,width=Math.max(520,left+right+rows.length*78),height=350,top=30,bottom=58,plotWidth=width-left-right,plotHeight=height-top-bottom;
  const available=rows.filter(x=>x.count>0&&x.max!=null),rawMax=Math.max(0,...available.map(x=>x.max));
  const step=rawMax<=5000?1000:rawMax<=10000?2000:5000,max=Math.max(step,Math.ceil(rawMax/step)*step),baseline=top+plotHeight;
  const y=value=>top+(max-value)/max*plotHeight,ticks=Array.from({length:6},(_,i)=>max*(5-i)/5),groupWidth=plotWidth/rows.length,boxWidth=Math.min(46,groupWidth*.48);
  const groups=rows.map((item,index)=>{
    const center=left+groupWidth*(index+.5),label=`${item.label}${item.count?` (${item.count}명)`:''}`;
    if(!item.count)return `<g class="salary-band-group"><text class="salary-band-empty" x="${center}" y="${baseline-8}" text-anchor="middle">-</text><text class="salary-band-label" x="${center}" y="${baseline+28}" text-anchor="middle">${esc(item.label)}</text></g>`;
    const boxTop=y(item.q3),boxBottom=y(item.q1),boxHeight=Math.max(3,boxBottom-boxTop),medianY=y(item.median),diamond=`${center},${medianY-5} ${center+5},${medianY} ${center},${medianY+5} ${center-5},${medianY}`;
    const summary=`${item.label} ${item.count}명 · 최소 ${fmt.format(item.min)}만원 · Q1 ${fmt.format(item.q1)}만원 · 중앙 ${fmt.format(item.median)}만원 · Q3 ${fmt.format(item.q3)}만원 · 최대 ${fmt.format(item.max)}만원`;
    return `<g class="salary-band-group" style="animation-delay:${index*70}ms" role="img" aria-label="${esc(summary)}" data-label="${esc(item.label)}" data-count="${item.count}" data-min="${item.min}" data-q1="${item.q1}" data-median="${item.median}" data-q3="${item.q3}" data-max="${item.max}"><line class="salary-band-whisker" x1="${center}" y1="${y(item.max)}" x2="${center}" y2="${y(item.min)}"></line><line class="salary-band-whisker" x1="${center-boxWidth*.28}" y1="${y(item.max)}" x2="${center+boxWidth*.28}" y2="${y(item.max)}"></line><line class="salary-band-whisker" x1="${center-boxWidth*.28}" y1="${y(item.min)}" x2="${center+boxWidth*.28}" y2="${y(item.min)}"></line><rect class="salary-band-box" x="${center-boxWidth/2}" y="${boxTop}" width="${boxWidth}" height="${boxHeight}" rx="3"></rect><line class="salary-band-median" x1="${center-boxWidth/2}" y1="${y(item.median)}" x2="${center+boxWidth/2}" y2="${y(item.median)}"></line><polygon class="salary-band-median-diamond" points="${diamond}"></polygon><text class="salary-band-label" x="${center}" y="${baseline+28}" text-anchor="middle">${esc(label)}</text></g>`;
  }).join('');
  $('detailChart').innerHTML=`<svg class="salary-band-svg" style="min-width:${width}px" viewBox="0 0 ${width} ${height}" role="img" aria-label="직위별 연봉 밴드 그래프"><text class="salary-band-unit" x="${left}" y="14">연봉(만원)</text>${ticks.map(value=>`<line class="salary-band-grid" x1="${left}" y1="${y(value)}" x2="${width-right}" y2="${y(value)}"></line><text class="salary-band-tick" x="${left-9}" y="${y(value)+4}" text-anchor="end">${fmt.format(Math.round(value))}</text>`).join('')}<line class="salary-band-axis" x1="${left}" y1="${baseline}" x2="${width-right}" y2="${baseline}"></line>${groups}</svg>`;
  bindSalaryBandTooltip();
}
function salaryAgeGenderBandChart(rows){
  $('detailChart').classList.remove('tenure-density-detail');$('detailChart').classList.add('salary-band-chart');
  const available=rows.filter(x=>x.count>0&&x.max!=null),ageGroups=[...new Set(rows.map(x=>x.label))];
  if(!available.length||!ageGroups.length){$('detailChart').innerHTML='<div class="chart-empty">연령대·성별 급여 데이터가 없습니다.</div>';return;}
  const left=62,right=18,width=Math.max(520,left+right+ageGroups.length*82),height=350,top=42,bottom=58,plotWidth=width-left-right,plotHeight=height-top-bottom;
  const rawMax=Math.max(0,...available.map(x=>x.max)),step=rawMax<=5000?1000:rawMax<=10000?2000:5000,max=Math.max(step,Math.ceil(rawMax/step)*step),baseline=top+plotHeight;
  const y=value=>top+(max-value)/max*plotHeight,ticks=Array.from({length:6},(_,i)=>max*(5-i)/5),groupWidth=plotWidth/ageGroups.length,boxWidth=Math.min(24,groupWidth*.28);
  const byKey=new Map(rows.map(x=>[`${x.label}-${x.gender}`,x]));
  const groups=ageGroups.map((ageGroup,ageIndex)=>{
    const ageCenter=left+groupWidth*(ageIndex+.5);
    const boxes=['남성','여성'].map((gender,genderIndex)=>{
      const item=byKey.get(`${ageGroup}-${gender}`);
      if(!item?.count)return '';
      const center=ageCenter+(genderIndex===0?-boxWidth*.72:boxWidth*.72),boxTop=y(item.q3),boxBottom=y(item.q1),boxHeight=Math.max(3,boxBottom-boxTop);
      const type=gender==='남성'?'is-male':'is-female',summary=`${ageGroup} ${gender} ${item.count}명 · 최소 ${fmt.format(item.min)}만원 · Q1 ${fmt.format(item.q1)}만원 · 중앙 ${fmt.format(item.median)}만원 · Q3 ${fmt.format(item.q3)}만원 · 최대 ${fmt.format(item.max)}만원`;
      return `<g class="salary-band-group salary-age-gender-group ${type}" style="animation-delay:${(ageIndex*2+genderIndex)*35}ms" role="img" aria-label="${esc(summary)}" data-label="${esc(ageGroup)} · ${gender}" data-count="${item.count}" data-min="${item.min}" data-q1="${item.q1}" data-median="${item.median}" data-q3="${item.q3}" data-max="${item.max}"><line class="salary-band-whisker" x1="${center}" y1="${y(item.max)}" x2="${center}" y2="${y(item.min)}"></line><line class="salary-band-whisker" x1="${center-boxWidth*.28}" y1="${y(item.max)}" x2="${center+boxWidth*.28}" y2="${y(item.max)}"></line><line class="salary-band-whisker" x1="${center-boxWidth*.28}" y1="${y(item.min)}" x2="${center+boxWidth*.28}" y2="${y(item.min)}"></line><rect class="salary-band-box" x="${center-boxWidth/2}" y="${boxTop}" width="${boxWidth}" height="${boxHeight}" rx="3"></rect><circle class="salary-age-median-point" cx="${center}" cy="${y(item.median)}" r="4"></circle></g>`;
    }).join('');
    return `${boxes}<text class="salary-band-label" x="${ageCenter}" y="${baseline+28}" text-anchor="middle">${esc(ageGroup)}</text>`;
  }).join('');
  $('detailChart').innerHTML=`<svg class="salary-band-svg salary-age-gender-svg" style="min-width:${width}px" viewBox="0 0 ${width} ${height}" role="img" aria-label="연령대와 성별에 따른 연봉 밴드 그래프"><text class="salary-band-unit" x="${left}" y="14">연봉(만원)</text><g class="salary-age-legend" transform="translate(${Math.max(left+110,width-right-122)} 14)"><circle class="salary-age-legend-male" cx="0" cy="0" r="4"></circle><text x="8" y="4">남성</text><circle class="salary-age-legend-female" cx="50" cy="0" r="4"></circle><text x="58" y="4">여성</text></g>${ticks.map(value=>`<line class="salary-band-grid" x1="${left}" y1="${y(value)}" x2="${width-right}" y2="${y(value)}"></line><text class="salary-band-tick" x="${left-9}" y="${y(value)+4}" text-anchor="end">${fmt.format(Math.round(value))}</text>`).join('')}<line class="salary-band-axis" x1="${left}" y1="${baseline}" x2="${width-right}" y2="${baseline}"></line>${groups}</svg>`;
  bindSalaryBandTooltip();
}
function bindSalaryBandTooltip(){
  const tooltip=$('salaryBandTooltip');
  $('detailChart').querySelectorAll('.salary-band-group[data-label]').forEach(group=>{
    group.onpointerenter=group.onpointermove=event=>{
      const rows=[['최소',group.dataset.min],['Q1',group.dataset.q1],['중앙',group.dataset.median],['Q3',group.dataset.q3],['최대',group.dataset.max]];
      tooltip.innerHTML=`<strong>${esc(group.dataset.label)} <small>${group.dataset.count}명</small></strong>${rows.map(([label,value])=>`<div><span>${label}</span><b>${fmt.format(Number(value))}만원</b></div>`).join('')}`;
      tooltip.hidden=false;const gap=14,tw=tooltip.offsetWidth,th=tooltip.offsetHeight,shell=tooltip.parentElement,rect=shell.getBoundingClientRect();
      tooltip.style.left=`${Math.max(8,Math.min(rect.width-tw-8,event.clientX-rect.left+gap))}px`;
      tooltip.style.top=`${Math.max(8,Math.min(rect.height-th-8,event.clientY-rect.top+gap))}px`;
    };
    group.onpointerleave=()=>tooltip.hidden=true;
  });
}
let headcountRequestId=0;
async function loadHeadcountTrend(mode){const requestId=++headcountRequestId;document.querySelectorAll('#headcountPeriodToggle button').forEach(button=>{const active=button.dataset.mode===mode;button.classList.toggle('active',active);button.setAttribute('aria-pressed',String(active));});$('detailDescription').textContent=mode==='monthly'?'최근 12개월 · 월말 DB가 없으면 해당 월의 마지막 DB를 기준으로 표시합니다.':'최근 15일 · 해당 날짜의 DB가 없으면 0명으로 표시합니다.';$('detailChart').setAttribute('aria-busy','true');$('detailChart').innerHTML='<div class="chart-empty">인원 추이를 불러오는 중입니다.</div>';try{const r=await fetch(`/api/employees/headcount-trend?mode=${mode}`);if(!r.ok)throw Error(`서버 오류(HTTP ${r.status})`);const data=await r.json();if(requestId!==headcountRequestId)return;detailVerticalBars(data.items);}catch(e){if(requestId===headcountRequestId)$('detailChart').innerHTML=`<div class="chart-empty">${esc(e.message)}</div>`;}finally{if(requestId===headcountRequestId)$('detailChart').removeAttribute('aria-busy');}}
function openDetail(type){if(!detailData)return;if(type==='salary'&&!canViewSalary){alert('사용자 권한이 없습니다.');return;}if(type==='headcount'){$('salaryDetailSwitch').hidden=true;$('detailTitle').textContent='조회 인원 추이';$('headcountPeriodToggle').hidden=false;$('detailDialog').showModal();loadHeadcountTrend('monthly');return;}$('headcountPeriodToggle').hidden=true;if(type==='salary'){detailSwitchMode='salary';salaryDetailIndex=0;$('salaryDetailSwitch').hidden=false;renderSalaryDetail();$('detailDialog').showModal();return;}if(type==='tenure'){detailSwitchMode='tenure';tenureDetailIndex=0;$('salaryDetailSwitch').hidden=false;renderTenureDetail();$('detailDialog').showModal();return;}$('salaryDetailSwitch').hidden=true;const config={age:['연령대별 인원','생년월일이 입력된 인원의 연령대 분포입니다.',detailData.ageGroups],hires:['올해 월별 입사 인원',`${new Date().getFullYear()}년 1월부터 12월까지의 입사자 수입니다.`,detailData.monthlyHires],terminations:['올해 월별 퇴사 예정인원','오늘 이후로 예정된 퇴사자만 월별로 표시합니다.',detailData.monthlyTerminations]}[type];if(!config)return;$('detailTitle').textContent=config[0];$('detailDescription').textContent=config[1];detailVerticalBars(config[2]);$('detailDialog').showModal();}
$('salaryDetailPrev').onclick=()=>changeSalaryDetail(-1);
$('salaryDetailNext').onclick=()=>changeSalaryDetail(1);
document.querySelectorAll('#headcountPeriodToggle button').forEach(button=>button.onclick=()=>loadHeadcountTrend(button.dataset.mode));
document.querySelectorAll('.kpi-clickable').forEach(card=>{card.onclick=()=>openDetail(card.dataset.detail);card.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();openDetail(card.dataset.detail);}};});$('closeDetailBtn').onclick=()=>{$('salaryBandTooltip').hidden=true;resetSalaryDetailTransition();$('detailDialog').close();};$('detailDialog').onclick=e=>{if(e.target===$('detailDialog')){$('salaryBandTooltip').hidden=true;resetSalaryDetailTransition();$('detailDialog').close();}};
$('detailDialog').addEventListener('cancel',()=>{$('salaryBandTooltip').hidden=true;resetSalaryDetailTransition();});
$('employeeDateFilter').onchange=async()=>{initialized=false;filterSelectIds.forEach(id=>$(id).options.length=1);$('searchInput').value='';$('importStatus').textContent='';page=1;await load();};
