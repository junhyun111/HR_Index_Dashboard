const $=id=>document.getElementById(id),number=new Intl.NumberFormat('ko-KR');
const money=v=>v==null?'-':`${number.format(Math.round(v/10000000)/10)}억원`;
const per=v=>v==null?'-':`${number.format(Math.round(v/10000))}만원`;
const pct=v=>v==null||!Number.isFinite(v)?'-':`${number.format(Math.round(v*10)/10)}%`;
let dashboard=null,mode='annual';

async function load(){
  try{
    const r=await fetch('/api/management');
    if(r.status===401||r.status===403){location.replace('/login');return;}
    if(!r.ok)throw Error(`서버 오류(HTTP ${r.status})`);
    dashboard=await r.json();$('managementLoad').hidden=true;initializePeriod();render();
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
}

function render(){
  if(!dashboard?.reports?.length){renderEmpty();return;}
  const year=Number($('businessYearSelect').value);
  const periodReports=mode==='annual'
    ?dashboard.reports.filter(x=>x.reportCode==='11011')
    :dashboard.reports.filter(x=>x.businessYear===year);
  const selected=mode==='annual'
    ?periodReports.find(x=>x.businessYear===year)
    :periodReports[periodReports.length-1];
  if(!selected){renderEmpty();return;}
  renderKpis(selected);renderSummary(selected);renderChart(periodReports);renderTable(mode==='annual'?[selected]:periodReports);
}

function renderKpis(x){
  const periodPayroll=x.dartSalaryTotal;
  const operatingMargin=ratio(x.operatingIncome,x.revenue);
  const laborCostRatio=periodPayroll?ratio(periodPayroll,x.revenue):null;
  const laborRoi=periodPayroll&&x.operatingIncome!=null?(x.operatingIncome+periodPayroll)/periodPayroll*100:null;
  $('revenue').textContent=money(x.revenue);$('operatingIncome').textContent=money(x.operatingIncome);$('netIncome').textContent=money(x.netIncome);
  $('operatingMargin').textContent=pct(operatingMargin);$('revenuePerEmployee').textContent=dashboard.headcount&&x.revenue!=null?per(x.revenue/dashboard.headcount):'-';
  $('dartAverageSalary').textContent=x.dartAverageSalary==null?'-':per(x.dartAverageSalary);
  $('laborCostRatio').textContent=pct(laborCostRatio);$('laborRoi').textContent=pct(laborRoi);$('reportPeriod').textContent=`${x.businessYear}년 ${x.reportName}`;
  $('dartStatus').textContent=`${x.businessYear} ${x.reportName} · ${x.fsDiv==='CFS'?'연결':'별도'}`;
}

function renderSummary(x){
  $('headcount').textContent=`${number.format(dashboard.headcount)}명`;$('wageCount').textContent=`${number.format(dashboard.wageCount)}명`;
  $('monthlyPayroll').textContent=money(dashboard.monthlyPayroll);$('debtRatio').textContent=pct(ratio(x.liabilities,x.equity));
  $('fsDiv').textContent=x.fsDiv==='CFS'?'연결재무제표':'별도재무제표';$('syncedAt').textContent=new Date(x.syncedAtUtc).toLocaleString('ko-KR');
  $('dartLink').href=x.receiptNumber?`https://dart.fss.or.kr/dsaf001/main.do?rcpNo=${encodeURIComponent(x.receiptNumber)}`:'https://dart.fss.or.kr/';
}

function renderChart(rows){
  $('financeChartTitle').textContent=mode==='annual'?'연도별 매출·영업이익 추이':`${$('businessYearSelect').value}년 분기별 누적 추이`;
  if(!rows.length){$('financeChart').innerHTML='<div class="finance-empty">표시할 데이터가 없습니다.</div>';return;}
  const width=840,height=285,left=62,right=22,top=18,bottom=38,plotW=width-left-right,plotH=height-top-bottom;
  const values=rows.flatMap(x=>[(x.revenue||0)/100000000,(x.operatingIncome||0)/100000000]);
  let min=Math.min(0,...values),max=Math.max(0,...values);if(min===max)max=min+1;
  const y=v=>top+(max-v)/(max-min)*plotH,x=i=>left+(rows.length===1?plotW/2:i*plotW/(rows.length-1));
  const ticks=Array.from({length:5},(_,i)=>max-(max-min)*i/4);
  const path=key=>rows.map((r,i)=>`${i?'L':'M'} ${x(i)} ${y((r[key]||0)/100000000)}`).join(' ');
  const points=(key,color)=>rows.map((r,i)=>`<circle cx="${x(i)}" cy="${y((r[key]||0)/100000000)}" r="4.5" fill="${color}"><title>${label(r)} ${key==='revenue'?'매출액':'영업이익'} ${money(r[key])}</title></circle>`).join('');
  $('financeChart').innerHTML=`<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="매출액과 영업이익 추이 그래프">
    ${ticks.map(v=>`<line x1="${left}" y1="${y(v)}" x2="${width-right}" y2="${y(v)}" stroke="#e8eef5"/><text x="${left-10}" y="${y(v)+4}" text-anchor="end">${formatAxis(v)}</text>`).join('')}
    <line x1="${left}" y1="${y(0)}" x2="${width-right}" y2="${y(0)}" stroke="#b8c5d4"/>
    <path d="${path('revenue')}" fill="none" stroke="#3978f6" stroke-width="3"/><path d="${path('operatingIncome')}" fill="none" stroke="#35b7ca" stroke-width="3"/>
    ${points('revenue','#3978f6')}${points('operatingIncome','#35b7ca')}
    ${rows.map((r,i)=>`<text x="${x(i)}" y="${height-10}" text-anchor="middle">${label(r)}</text>`).join('')}
  </svg>`;
}

function renderTable(rows){$('reportRows').innerHTML=[...rows].reverse().map(r=>`<tr><td>${r.businessYear} ${r.reportName}</td><td>${r.fsDiv==='CFS'?'연결':'별도'}</td><td>${money(r.revenue)}</td><td>${money(r.operatingIncome)}</td><td>${money(r.netIncome)}</td><td>${money(r.assets)}</td><td>${money(r.liabilities)}</td><td>${money(r.equity)}</td></tr>`).join('');}
function renderEmpty(){$('financeChart').innerHTML='<div class="finance-empty">DART 새로고침을 눌러 데이터를 가져오세요.</div>';$('reportRows').innerHTML='<tr><td class="table-empty" colspan="8">저장된 재무 데이터가 없습니다.</td></tr>';}
function ratio(a,b){return a!=null&&b? a/b*100:null;}
function label(r){return mode==='annual'?String(r.businessYear):r.reportName;}
function formatAxis(v){return Math.abs(v)>=1000?`${number.format(Math.round(v/100)/10)}천`:number.format(Math.round(v));}

document.querySelectorAll('.period-toggle button').forEach(button=>button.onclick=()=>{mode=button.dataset.mode;document.querySelectorAll('.period-toggle button').forEach(x=>x.classList.toggle('active',x===button));populateYears();render();});
$('businessYearSelect').onchange=render;
$('syncBtn').onclick=async()=>{if(!confirm('DART에서 이노뎁 최신 재무 데이터를 가져올까요?'))return;$('syncBtn').disabled=true;$('syncBtn').textContent='동기화 중...';try{const r=await fetch('/api/management/sync',{method:'POST'}),x=await r.json();if(!r.ok)throw Error(x.message||'동기화 실패');await load();}catch(e){alert(e.message);}finally{$('syncBtn').disabled=false;$('syncBtn').textContent='DART 새로고침';}};
load();
