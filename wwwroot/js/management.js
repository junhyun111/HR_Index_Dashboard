const $=id=>document.getElementById(id),number=new Intl.NumberFormat('ko-KR');
const money=v=>v==null?'-':`${number.format(Math.round(v/100000000*10)/10)}억원`,per=v=>v==null?'-':`${number.format(Math.round(v/10000))}만원`,pct=v=>v==null?'-':`${number.format(v)}%`;
async function load(){
  try{const r=await fetch('/api/management');if(r.status===401||r.status===403){location.replace('/login');return;}if(!r.ok)throw Error(`서버 오류(HTTP ${r.status})`);const d=await r.json();render(d);}
  catch(e){$('managementLoad').querySelector('h2').textContent='경영지표를 불러오지 못했습니다';$('managementMessage').textContent=e.message;}
}
function render(d){
  const x=d.latest;$('managementLoad').hidden=true;
  if(!x){$('dartStatus').textContent='동기화 필요';$('financeBars').innerHTML='<div class="finance-empty">DART 새로고침을 눌러 데이터를 가져오세요.</div>';$('reportRows').innerHTML='<tr><td class="table-empty" colspan="8">저장된 재무 데이터가 없습니다.</td></tr>';return;}
  $('dartStatus').textContent=`${x.businessYear} ${x.reportName} · ${x.fsDiv==='CFS'?'연결':'별도'}`;$('revenue').textContent=money(x.revenue);$('operatingIncome').textContent=money(x.operatingIncome);$('netIncome').textContent=money(x.netIncome);$('operatingMargin').textContent=pct(x.operatingMargin);$('reportPeriod').textContent=`${x.businessYear}년 ${x.reportName}`;
  $('revenuePerEmployee').textContent=per(x.revenuePerEmployee);$('operatingPerEmployee').textContent=per(x.operatingIncomePerEmployee);$('laborCostRatio').textContent=pct(x.laborCostRatio);$('laborRoi').textContent=pct(x.laborRoi);
  $('headcount').textContent=`${number.format(d.headcount)}명`;$('wageCount').textContent=`${number.format(d.wageCount)}명`;$('monthlyPayroll').textContent=money(d.monthlyPayroll);$('debtRatio').textContent=pct(x.debtRatio);$('fsDiv').textContent=x.fsDiv==='CFS'?'연결재무제표':'별도재무제표';$('syncedAt').textContent=new Date(x.syncedAtUtc).toLocaleString('ko-KR');
  if(x.receiptNumber)$('dartLink').href=`https://dart.fss.or.kr/dsaf001/main.do?rcpNo=${encodeURIComponent(x.receiptNumber)}`;
  bars(d.reports);$('reportRows').innerHTML=[...d.reports].reverse().map(r=>`<tr><td>${r.businessYear} ${r.reportName}</td><td>${r.fsDiv==='CFS'?'연결':'별도'}</td><td>${money(r.revenue)}</td><td>${money(r.operatingIncome)}</td><td>${money(r.netIncome)}</td><td>${money(r.assets)}</td><td>${money(r.liabilities)}</td><td>${money(r.equity)}</td></tr>`).join('');
}
function bars(rows){const list=rows.slice(-12),max=Math.max(1,...list.flatMap(x=>[Math.abs(x.revenue||0),Math.abs(x.operatingIncome||0)]));$('financeBars').innerHTML=list.length?list.map(x=>`<div class="finance-group"><div class="finance-bar revenue" title="매출 ${money(x.revenue)}" style="height:${Math.max(2,Math.abs(x.revenue||0)/max*185)}px"></div><div class="finance-bar operating" title="영업이익 ${money(x.operatingIncome)}" style="height:${Math.max(2,Math.abs(x.operatingIncome||0)/max*185)}px"></div><span class="finance-label">${x.businessYear%100}.${x.reportName.replace('분기','Q').replace('반기','H').replace('연간','Y')}</span></div>`).join(''):'<div class="finance-empty">저장된 재무 데이터가 없습니다.</div>';}
$('syncBtn').onclick=async()=>{if(!confirm('DART에서 이노뎁 최신 재무 데이터를 가져올까요?'))return;$('syncBtn').disabled=true;$('syncBtn').textContent='동기화 중...';try{const r=await fetch('/api/management/sync',{method:'POST'}),x=await r.json();if(!r.ok)throw Error(x.message||'동기화 실패');await load();}catch(e){alert(e.message);}finally{$('syncBtn').disabled=false;$('syncBtn').textContent='DART 새로고침';}};
load();
