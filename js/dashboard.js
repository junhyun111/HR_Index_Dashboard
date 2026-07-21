const DATA_URL = './js/innodep_hr_dummy_200.json';
const PAGE_SIZE = 10;
let sourceData = [], viewData = [], page = 1, sortKey = '이름', sortDirection = 1;

const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('ko-KR');
const required = ['회사명','부서명','이름','직급','직책','성별','나이','임금(월)','근속연수'];

function cleanData(data) {
  if (!Array.isArray(data)) throw new Error('JSON의 최상위 값은 배열이어야 합니다.');
  const valid = data.filter(row => row && required.every(key => key in row)).map(row => ({
    ...row,
    나이: Number(row.나이),
    '임금(월)': Number(row['임금(월)']),
    근속연수: Number(row.근속연수)
  })).filter(row => Number.isFinite(row.나이) && Number.isFinite(row['임금(월)']) && Number.isFinite(row.근속연수));
  if (!valid.length) throw new Error('필수 필드를 가진 유효한 사원 데이터가 없습니다.');
  return valid;
}

async function loadDefault() {
  try {
    const response = await fetch(DATA_URL);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    initialize(cleanData(await response.json()));
  } catch (error) {
    $('loadState').querySelector('h2').textContent = 'JSON 파일을 선택해 주세요';
    $('loadMessage').textContent = 'HTML을 더블클릭해 연 경우 브라우저 보안 정책으로 자동 로딩이 제한됩니다. 기존 JSON 파일을 선택하면 즉시 대시보드가 열립니다.';
    $('fileLabel').hidden = false;
    $('sourceStatus').textContent = '파일 선택 대기';
  }
}

function initialize(data) {
  sourceData = data;
  fillSelect('deptFilter', unique(data, '부서명'));
  fillSelect('gradeFilter', gradeOrder(unique(data, '직급')));
  fillSelect('genderFilter', unique(data, '성별'));
  $('loadState').hidden = true;
  $('filterArea').hidden = false;
  $('dashboard').hidden = false;
  $('sourceStatus').textContent = `${number.format(data.length)}명 로드 완료`;
  applyFilters();
}

function unique(data, key) { return [...new Set(data.map(d => d[key]))].sort((a,b) => String(a).localeCompare(String(b), 'ko')); }
function gradeOrder(items) {
  const order = ['사원','주임','대리','과장','차장','부장','임원'];
  return items.sort((a,b) => (order.indexOf(a) < 0 ? 99 : order.indexOf(a)) - (order.indexOf(b) < 0 ? 99 : order.indexOf(b)));
}
function fillSelect(id, values) {
  values.forEach(value => { const option = document.createElement('option'); option.value = value; option.textContent = value; $(id).append(option); });
}

function applyFilters() {
  const dept = $('deptFilter').value, grade = $('gradeFilter').value, gender = $('genderFilter').value;
  const query = $('searchInput').value.trim().toLowerCase();
  viewData = sourceData.filter(row =>
    (!dept || row.부서명 === dept) && (!grade || row.직급 === grade) && (!gender || row.성별 === gender) &&
    (!query || [row.이름, row.부서명, row.직책, row.직급].some(v => String(v).toLowerCase().includes(query)))
  );
  page = 1;
  render();
}

function render() { renderKpis(); renderDepartments(); renderGender(); renderSalary(); renderTable(); }
function average(values) { return values.length ? values.reduce((a,b) => a+b, 0) / values.length : 0; }
function median(values) { if (!values.length) return 0; const a = [...values].sort((x,y)=>x-y), m = Math.floor(a.length/2); return a.length % 2 ? a[m] : (a[m-1]+a[m])/2; }

function renderKpis() {
  const ages = viewData.map(d=>d.나이), salaries = viewData.map(d=>d['임금(월)']), tenures = viewData.map(d=>d.근속연수);
  $('totalPeople').innerHTML = `${number.format(viewData.length)}<span class="kpi-unit">명</span>`;
  $('peopleNote').textContent = `전체의 ${sourceData.length ? (viewData.length/sourceData.length*100).toFixed(1) : 0}%`;
  $('avgAge').textContent = viewData.length ? average(ages).toFixed(1) : '-';
  $('ageNote').textContent = viewData.length ? `${Math.min(...ages)}세 ~ ${Math.max(...ages)}세` : '데이터 없음';
  $('avgSalary').textContent = viewData.length ? number.format(Math.round(average(salaries)/10000)) : '-';
  $('salaryNote').textContent = viewData.length ? `중앙값 ${number.format(Math.round(median(salaries)/10000))}만원` : '데이터 없음';
  $('avgTenure').textContent = viewData.length ? average(tenures).toFixed(1) : '-';
  const longTerm = viewData.length ? viewData.filter(d=>d.근속연수>=10).length/viewData.length*100 : 0;
  $('tenureNote').textContent = `10년 이상 ${longTerm.toFixed(1)}%`;
}

function counts(key) {
  return [...viewData.reduce((map,row) => map.set(row[key], (map.get(row[key])||0)+1), new Map())].sort((a,b)=>b[1]-a[1]);
}
function renderDepartments() {
  const items = counts('부서명'), max = Math.max(1, ...items.map(i=>i[1]));
  $('deptChart').innerHTML = items.length ? items.map(([label,value]) => `<div class="bar-row"><span class="bar-label" title="${esc(label)}">${esc(label)}</span><div class="bar-track"><div class="bar-fill" style="width:${value/max*100}%"></div></div><span class="bar-number">${value}</span></div>`).join('') : empty('조회 결과 없음');
}
function renderGender() {
  const male = viewData.filter(d=>d.성별==='남').length, female = viewData.filter(d=>d.성별==='여').length, total = viewData.length || 1;
  const pct = male/total*100;
  $('genderChart').innerHTML = `<div class="donut-wrap"><div class="donut" style="--male:${pct}%"><div class="donut-center"><strong>${viewData.length}</strong><span>조회 인원</span></div></div><div class="legend"><span><i class="dot dot-male"></i>남 ${male}명</span><span><i class="dot dot-female"></i>여 ${female}명</span></div></div>`;
}
function renderSalary() {
  const bins = [300,400,500,600,700,800,900,Infinity], labels = ['~300','~400','~500','~600','~700','~800','~900','900+'];
  const values = Array(bins.length).fill(0);
  viewData.forEach(row => { const salary = row['임금(월)']/10000; const idx = bins.findIndex(limit => salary < limit); values[idx < 0 ? bins.length-1 : idx]++; });
  const max = Math.max(1,...values);
  $('salaryChart').innerHTML = values.map((value,i)=>`<div class="hist-col"><span class="hist-value">${value || ''}</span><div class="hist-bar" style="height:${Math.max(2,value/max*155)}px"></div><span class="hist-label">${labels[i]}</span></div>`).join('');
}

function renderTable() {
  const sorted = [...viewData].sort((a,b) => {
    const av=a[sortKey], bv=b[sortKey];
    return (typeof av==='number' && typeof bv==='number' ? av-bv : String(av).localeCompare(String(bv),'ko')) * sortDirection;
  });
  const pages = Math.max(1, Math.ceil(sorted.length/PAGE_SIZE)); page = Math.min(page,pages);
  const rows = sorted.slice((page-1)*PAGE_SIZE,page*PAGE_SIZE);
  $('employeeRows').innerHTML = rows.length ? rows.map(row=>`<tr>
    <td><div class="person"><span class="avatar">${esc(String(row.이름).slice(0,1))}</span>${esc(row.이름)}</div></td><td>${esc(row.부서명)}</td><td><span class="pill">${esc(row.직급)}</span></td><td>${esc(row.직책)}</td><td>${esc(row.성별)}</td><td>${row.나이}세</td><td>${number.format(row['임금(월)'])}원</td><td>${row.근속연수.toFixed(1)}년</td>
  </tr>`).join('') : `<tr><td class="table-empty" colspan="8">조건에 맞는 사원이 없습니다.</td></tr>`;
  $('resultCount').textContent = `${number.format(viewData.length)}명`;
  $('pageInfo').textContent = `${page} / ${pages}`;
  $('prevBtn').disabled = page<=1; $('nextBtn').disabled = page>=pages;
}

function empty(text) { return `<div class="chart-empty">${text}</div>`; }
function esc(value) { const div=document.createElement('div'); div.textContent=String(value ?? ''); return div.innerHTML; }

['deptFilter','gradeFilter','genderFilter'].forEach(id => $(id).addEventListener('change', applyFilters));
$('searchInput').addEventListener('input', applyFilters);
$('resetBtn').addEventListener('click', () => { ['deptFilter','gradeFilter','genderFilter'].forEach(id=>$(id).value=''); $('searchInput').value=''; applyFilters(); });
$('prevBtn').addEventListener('click',()=>{ if(page>1){page--;renderTable();} });
$('nextBtn').addEventListener('click',()=>{ if(page*PAGE_SIZE<viewData.length){page++;renderTable();} });
document.querySelectorAll('th[data-key]').forEach(th=>th.addEventListener('click',()=>{ const key=th.dataset.key; if(sortKey===key) sortDirection*=-1; else {sortKey=key;sortDirection=1;} renderTable(); }));
$('fileInput').addEventListener('change', async event => {
  const file=event.target.files[0]; if(!file)return;
  try { initialize(cleanData(JSON.parse(await file.text()))); }
  catch(error) { $('loadMessage').textContent=`파일 오류: ${error.message}`; }
});

loadDefault();
