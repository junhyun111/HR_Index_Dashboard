const $=id=>document.getElementById(id),number=new Intl.NumberFormat('ko-KR');
const count=v=>v==null?'-':`${number.format(v)}명`;
const money=v=>v==null?'-':`${number.format(Math.round(v/10000000)/10)}억원`;
const per=v=>v==null?'-':`${number.format(Math.round(v/10000))}만원`;
const pct=v=>v==null||!Number.isFinite(v)?'-':`${number.format(Math.round(v*10)/10)}%`;
let dashboard=null,mode='annual',canViewSalary=false;

async function load(){
  try{
    const r=await fetch('/api/management');
    if(r.status===401||r.status===403){location.replace('/login');return;}
    if(!r.ok)throw Error(`서버 오류(HTTP ${r.status})`);
    dashboard=await r.json();const session=await fetch('/api/session').then(x=>x.ok?x.json():null);if(session){canViewSalary=Boolean(session.canViewSalary);$('sessionUser').textContent=session.userName||'로그인 사용자';if(session.theme)window.setDashboardTheme(session.theme);}$('managementLoad').hidden=true;initializePeriod();render();
  }catch(e){$('managementLoad').querySelector('h2').textContent='경영지표를 불러오지 못했습니다';$('managementMessage').textContent=e.message;}
}

function initializePeriod(){
  const reports=dashboard.reports||[];
  if(!reports.length){$('dartStatus').textContent='동기화 필요';return;}
  populateYears();
}

function populateYears(){
  const source=mode==='annual'?dashboard.reports.filter(x=>x.reportCode==='11011'):dashboard.reports;
  const years=[...new Set(source.map(x=>x.businessYear))].sort((a,b)=>b-a);
  const previous=Number($('businessYearSelect').value);
  $('businessYearSelect').innerHTML=years.map(y=>`<option value="${y}">${y}년</option>`).join('');
  $('businessYearSelect').value=years.includes(previous)?String(previous):String(years[0]||'');
  populatePeriods();
}

function populatePeriods(){
  const select=$('reportPeriodSelect'),year=Number($('businessYearSelect').value);
  select.hidden=mode!=='quarterly';if(mode!=='quarterly')return;
  const rows=dashboard.reports.filter(x=>x.businessYear===year),previous=select.value;
  select.innerHTML=rows.map(x=>`<option value="${x.reportCode}">${x.reportName}</option>`).join('');
  select.value=rows.some(x=>x.reportCode===previous)?previous:(rows.at(-1)?.reportCode||'');
}

function render(){
  if(!dashboard?.reports?.length){renderEmpty();return;}
  renderTable(dashboard.reports);
  const year=Number($('businessYearSelect').value);
  const periodReports=mode==='annual'
    ?dashboard.reports.filter(x=>x.reportCode==='11011')
    :dashboard.reports.filter(x=>x.businessYear===year);
  const selected=mode==='annual'
    ?periodReports.find(x=>x.businessYear===year)
    :periodReports.find(x=>x.reportCode===$('reportPeriodSelect').value)||periodReports[periodReports.length-1];
  if(!selected){renderEmpty();return;}
  renderKpis(selected);renderSummary(selected);renderChart(periodReports);renderHrCharts(periodReports);
}

function renderKpis(x){
  const operatingMargin=ratio(x.operatingIncome,x.revenue);
  $('revenue').textContent=money(x.revenue);$('operatingIncome').textContent=money(x.operatingIncome);$('netIncome').textContent=money(x.netIncome);
  $('operatingMargin').textContent=pct(operatingMargin);$('reportPeriod').textContent=`${x.businessYear}년 ${x.reportName}`;
  $('dartStatus').textContent=`${x.businessYear} ${x.reportName} · ${x.fsDiv==='CFS'?'연결':'별도'}`;
}

function renderSummary(x){
  $('headcount').textContent=count(dashboard.headcount);$('salaryCount').textContent=canViewSalary?count(dashboard.salaryCount):'권한 필요';
  $('annualPayroll').textContent=canViewSalary?money(dashboard.annualPayroll):'권한 필요';$('debtRatio').textContent=pct(ratio(x.liabilities,x.equity));
  $('fsDiv').textContent=x.fsDiv==='CFS'?'연결재무제표':'별도재무제표';$('syncedAt').textContent=new Date(x.syncedAtUtc).toLocaleString('ko-KR');
  $('dartLink').href=x.receiptNumber?`https://dart.fss.or.kr/dsaf001/main.do?rcpNo=${encodeURIComponent(x.receiptNumber)}`:'https://dart.fss.or.kr/';
}

function renderHrCharts(rows){
  const productivity=rows.map(x=>({...x,revenuePerEmployee:x.dartEmployeeCount&&x.revenue!=null?x.revenue/x.dartEmployeeCount/10000:null,averageSalary:x.dartAverageSalary==null?null:x.dartAverageSalary/10000}));
  const efficiency=rows.map(x=>({...x,laborCostRatio:x.dartSalaryTotal?ratio(x.dartSalaryTotal,x.revenue):null,laborRoi:x.dartSalaryTotal&&x.operatingIncome!=null?(x.operatingIncome+x.dartSalaryTotal)/x.dartSalaryTotal*100:null}));
  metricChart('productivityChart',productivity,[{key:'revenuePerEmployee',name:'인당 매출',color:'#3978f6'},{key:'averageSalary',name:'평균 급여',color:'#8b6fd6'}],'만원');
  metricChart('laborEfficiencyChart',efficiency,[{key:'laborCostRatio',name:'인건비 비율',color:'#f0a43c'},{key:'laborRoi',name:'인건비 투자수익률',color:'#38a47b'}],'%');
}

function renderChart(rows){
  $('financeChartTitle').textContent=mode==='annual'?'연도별 매출·영업이익 추이':`${$('businessYearSelect').value}년 분기별 누적 추이`;
  groupedBarChart('financeChart',rows,[
    {name:'매출액',color:'#3978f6',value:r=>r.revenue==null?null:r.revenue/100000000,format:v=>`${number.format(Math.round(v*10)/10)}억원`},
    {name:'영업이익',color:'#35b7ca',value:r=>r.operatingIncome==null?null:r.operatingIncome/100000000,format:v=>`${number.format(Math.round(v*10)/10)}억원`}
  ],'억원',840);
}

function metricChart(id,rows,series,unit){
  groupedBarChart(id,rows,series.map(s=>({name:s.name,color:s.color,value:r=>r[s.key],format:v=>`${number.format(Math.round(v*10)/10)}${unit}`})),unit,620);
}

function groupedBarChart(id,rows,series,unit,width){
  if(!rows.length){$(id).innerHTML='<div class="finance-empty">표시할 데이터가 없습니다.</div>';return;}
  const height=id==='financeChart'?285:245,left=58,right=18,top=14,bottom=34,plotW=width-left-right,plotH=height-top-bottom;
  const values=rows.flatMap(row=>series.map(s=>s.value(row))).filter(v=>v!=null&&Number.isFinite(v));
  if(!values.length){$(id).innerHTML='<div class="finance-empty">공시 데이터가 없습니다.</div>';return;}
  let min=Math.min(0,...values),max=Math.max(0,...values);if(min===max)max=min+1;
  const y=v=>top+(max-v)/(max-min)*plotH,yZero=y(0),groupW=plotW/rows.length,barW=Math.min(30,groupW/(series.length+1));
  const center=i=>left+groupW*(i+.5),ticks=Array.from({length:5},(_,i)=>max-(max-min)*i/4);
  const bars=rows.map((row,rowIndex)=>series.map((s,seriesIndex)=>{
    const value=s.value(row);if(value==null||!Number.isFinite(value))return'';
    const barX=center(rowIndex)+(seriesIndex-(series.length-1)/2)*(barW+4)-barW/2,barY=Math.min(y(value),yZero),barH=Math.max(2,Math.abs(y(value)-yZero));
    return `<rect class="chart-bar ${value<0?'negative':'positive'}" x="${barX}" y="${barY}" width="${barW}" height="${barH}" rx="4" fill="${s.color}" style="animation-delay:${rowIndex*90+seriesIndex*55}ms"><title>${label(row)} ${s.name} ${s.format(value)}</title></rect>`;
  }).join('')).join('');
  $(id).innerHTML=`<svg viewBox="0 0 ${width} ${height}" role="img">
    ${ticks.map(v=>`<line x1="${left}" y1="${y(v)}" x2="${width-right}" y2="${y(v)}" stroke="#e8eef5"/><text x="${left-9}" y="${y(v)+4}" text-anchor="end">${number.format(Math.round(v))}</text>`).join('')}
    <line x1="${left}" y1="${yZero}" x2="${width-right}" y2="${yZero}" stroke="#b8c5d4"/><line class="chart-hover-line" x1="${left}" y1="${top}" x2="${left}" y2="${height-bottom}" visibility="hidden"/>
    ${bars}${rows.map((r,i)=>`<text x="${center(i)}" y="${height-8}" text-anchor="middle">${label(r)}</text>`).join('')}
  </svg>`;
  bindChartTooltip(id,rows,series,width,left,right);
}

function bindChartTooltip(id,rows,series,viewWidth,left,right){
  const container=$(id),svg=container.querySelector('svg'),tooltip=$('chartTooltip'),hoverLine=svg?.querySelector('.chart-hover-line');if(!svg||!rows.length)return;
  svg.onpointermove=event=>{
    const rect=svg.getBoundingClientRect(),plotLeft=rect.left+left/viewWidth*rect.width,plotRight=rect.right-right/viewWidth*rect.width;
    const ratio=Math.max(0,Math.min(1,(event.clientX-plotLeft)/(plotRight-plotLeft)));
    const index=Math.min(rows.length-1,Math.floor(ratio*rows.length)),row=rows[index];
    const lineX=left+(index+.5)*(viewWidth-left-right)/rows.length;
    if(hoverLine){hoverLine.setAttribute('x1',lineX);hoverLine.setAttribute('x2',lineX);hoverLine.setAttribute('visibility','visible');}
    const details=series.map(s=>({s,value:s.value(row)})).filter(x=>x.value!=null&&Number.isFinite(x.value));
    if(!details.length){tooltip.hidden=true;return;}
    tooltip.innerHTML=`<strong>${label(row)}</strong>${details.map(x=>`<div class="tooltip-row"><span class="tooltip-label"><i class="tooltip-dot" style="background:${x.s.color}"></i>${x.s.name}</span><span class="tooltip-value">${x.s.format(x.value)}</span></div>`).join('')}`;
    tooltip.hidden=false;const gap=14,tw=tooltip.offsetWidth,th=tooltip.offsetHeight;
    tooltip.style.left=`${Math.min(innerWidth-tw-8,event.clientX+gap)}px`;tooltip.style.top=`${Math.min(innerHeight-th-8,event.clientY+gap)}px`;
  };
  svg.onpointerleave=()=>{tooltip.hidden=true;if(hoverLine)hoverLine.setAttribute('visibility','hidden');};
}

function renderTable(rows){$('reportRows').innerHTML=[...rows].reverse().map(r=>`<tr><td>${r.businessYear} ${r.reportName}</td><td>${money(r.revenue)}</td><td>${money(r.operatingIncome)}</td><td>${money(r.netIncome)}</td><td>${money(r.assets)}</td><td>${money(r.liabilities)}</td><td>${money(r.equity)}</td></tr>`).join('');}
function renderEmpty(){$('financeChart').innerHTML='<div class="finance-empty">설정에서 DART 데이터를 불러와 주세요.</div>';$('reportRows').innerHTML='<tr><td class="table-empty" colspan="7">저장된 재무 데이터가 없습니다.</td></tr>';}
function ratio(a,b){return a!=null&&b? a/b*100:null;}
function label(r){return mode==='annual'?String(r.businessYear):r.reportName;}
function formatAxis(v){return Math.abs(v)>=1000?`${number.format(Math.round(v/100)/10)}천`:number.format(Math.round(v));}

document.querySelectorAll('.period-toggle button').forEach(button=>button.onclick=()=>{mode=button.dataset.mode;document.querySelectorAll('.period-toggle button').forEach(x=>x.classList.toggle('active',x===button));populateYears();render();});
$('businessYearSelect').onchange=()=>{populatePeriods();render();};
$('reportPeriodSelect').onchange=render;
document.querySelectorAll('[data-salary-access]').forEach(element=>element.onclick=()=>{if(!canViewSalary)alert('사용자 권한이 없습니다.');});
$('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST'});location.replace('/login');};
load();
