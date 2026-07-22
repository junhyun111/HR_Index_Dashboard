const PAGE_SIZE = 10;
const $ = id => document.getElementById(id);
const number = new Intl.NumberFormat('ko-KR');
let page = 1;
let sortKey = 'name';
let sortDirection = 'asc';
let searchTimer;
let filtersInitialized = false;

async function loadDashboard() {
  setLoading(true);
  const parameters = new URLSearchParams({
    page,
    pageSize: PAGE_SIZE,
    sort: sortKey,
    direction: sortDirection
  });
  const values = {
    department: $('deptFilter').value,
    grade: $('gradeFilter').value,
    gender: $('genderFilter').value,
    search: $('searchInput').value.trim()
  };
  Object.entries(values).forEach(([key, value]) => { if (value) parameters.set(key, value); });

  try {
    const response = await fetch(`/api/dashboard?${parameters}`, { credentials: 'same-origin' });
    if (response.status === 401 || response.status === 403) {
      throw new Error('이 대시보드를 조회할 AD 권한이 없습니다.');
    }
    if (!response.ok) throw new Error(`서버 오류(HTTP ${response.status})`);
    const data = await response.json();
    if (!filtersInitialized) initializeFilters(data.filters);
    render(data);
    $('loadState').hidden = true;
    $('filterArea').hidden = false;
    $('dashboard').hidden = false;
    $('sourceStatus').textContent = `SQLite · ${number.format(data.summary.totalCount)}명`;
  } catch (error) {
    $('loadState').hidden = false;
    $('loadState').querySelector('h2').textContent = '데이터를 불러오지 못했습니다';
    $('loadMessage').textContent = error.message;
    $('sourceStatus').textContent = 'API 연결 실패';
  } finally {
    setLoading(false);
  }
}

function setLoading(isLoading) {
  $('sourceStatus').textContent = isLoading ? 'DB 조회 중...' : $('sourceStatus').textContent;
}

function initializeFilters(filters) {
  fillSelect('deptFilter', filters.departments);
  fillSelect('gradeFilter', filters.grades);
  fillSelect('genderFilter', filters.genders);
  filtersInitialized = true;
}

function fillSelect(id, values) {
  values.forEach(value => $(id).append(new Option(value, value)));
}

function render(data) {
  renderKpis(data.summary, data.permissions);
  renderDepartments(data.departments);
  renderGender(data.genders, data.summary.filteredCount);
  renderSalary(data.salaryDistribution, data.permissions);
  renderTable(data.employees, data.pagination, data.permissions);
}

function renderKpis(summary, permissions) {
  $('totalPeople').innerHTML = `${number.format(summary.filteredCount)}<span class="kpi-unit">명</span>`;
  $('peopleNote').textContent = `전체의 ${summary.totalCount ? (summary.filteredCount / summary.totalCount * 100).toFixed(1) : 0}%`;
  $('avgAge').textContent = summary.averageAge ?? '-';
  $('ageNote').textContent = summary.minimumAge == null ? '데이터 없음' : `${summary.minimumAge}세 ~ ${summary.maximumAge}세`;
  $('avgTenure').textContent = summary.averageTenure ?? '-';
  $('tenureNote').textContent = `10년 이상 ${summary.longTermPercentage.toFixed(1)}%`;

  if (permissions.canViewSalary) {
    $('avgSalary').textContent = summary.averageSalary == null ? '-' : number.format(Math.round(summary.averageSalary / 10000));
    $('salaryNote').textContent = summary.medianSalary == null ? '데이터 없음' : `중앙값 ${number.format(Math.round(summary.medianSalary / 10000))}만원`;
  } else {
    $('avgSalary').textContent = '-';
    $('salaryNote').textContent = '급여 조회 권한 필요';
  }
}

function renderDepartments(items) {
  const max = Math.max(1, ...items.map(item => item.value));
  $('deptChart').innerHTML = items.length ? items.map(item => `<div class="bar-row"><span class="bar-label" title="${esc(item.label)}">${esc(item.label)}</span><div class="bar-track"><div class="bar-fill" style="width:${item.value / max * 100}%"></div></div><span class="bar-number">${item.value}</span></div>`).join('') : empty('조회 결과 없음');
}

function renderGender(genders, total) {
  const male = genders.남 || 0;
  const female = genders.여 || 0;
  const percentage = total ? male / total * 100 : 0;
  $('genderChart').innerHTML = `<div class="donut-wrap"><div class="donut" style="--male:${percentage}%"><div class="donut-center"><strong>${total}</strong><span>조회 인원</span></div></div><div class="legend"><span><i class="dot dot-male"></i>남 ${male}명</span><span><i class="dot dot-female"></i>여 ${female}명</span></div></div>`;
}

function renderSalary(items, permissions) {
  if (!permissions.canViewSalary) {
    $('salaryChart').innerHTML = empty('급여 조회 권한이 필요합니다.');
    return;
  }
  const max = Math.max(1, ...items.map(item => item.value));
  $('salaryChart').innerHTML = items.map(item => `<div class="hist-col"><span class="hist-value">${item.value || ''}</span><div class="hist-bar" style="height:${Math.max(2, item.value / max * 155)}px"></div><span class="hist-label">${item.label}</span></div>`).join('');
}

function renderTable(rows, pagination, permissions) {
  $('employeeRows').innerHTML = rows.length ? rows.map(row => `<tr>
    <td><div class="person"><span class="avatar">${esc(row.name.slice(0, 1))}</span>${esc(row.name)}</div></td>
    <td>${esc(row.departmentName)}</td><td><span class="pill">${esc(row.grade)}</span></td>
    <td>${esc(row.position)}</td><td>${esc(row.gender)}</td><td>${row.age}세</td>
    <td>${permissions.canViewSalary && row.monthlySalary != null ? `${number.format(row.monthlySalary)}원` : '권한 없음'}</td>
    <td>${row.yearsOfService.toFixed(1)}년</td>
  </tr>`).join('') : '<tr><td class="table-empty" colspan="8">조건에 맞는 사원이 없습니다.</td></tr>';
  $('resultCount').textContent = `${number.format(pagination.totalCount)}명`;
  $('pageInfo').textContent = `${pagination.page} / ${pagination.pages}`;
  $('prevBtn').disabled = pagination.page <= 1;
  $('nextBtn').disabled = pagination.page >= pagination.pages;
  page = pagination.page;
}

function empty(text) { return `<div class="chart-empty">${text}</div>`; }
function esc(value) { const div = document.createElement('div'); div.textContent = String(value ?? ''); return div.innerHTML; }

['deptFilter', 'gradeFilter', 'genderFilter'].forEach(id => $(id).addEventListener('change', () => {
  page = 1;
  loadDashboard();
}));
$('searchInput').addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => { page = 1; loadDashboard(); }, 250);
});
$('resetBtn').addEventListener('click', () => {
  ['deptFilter', 'gradeFilter', 'genderFilter'].forEach(id => $(id).value = '');
  $('searchInput').value = '';
  page = 1;
  loadDashboard();
});
$('prevBtn').addEventListener('click', () => { if (page > 1) { page--; loadDashboard(); } });
$('nextBtn').addEventListener('click', () => { page++; loadDashboard(); });
document.querySelectorAll('th[data-key]').forEach(th => th.addEventListener('click', () => {
  const keyMap = {
    이름: 'name', 부서명: 'departmentName', 직급: 'grade', 직책: 'position', 성별: 'gender',
    나이: 'age', '임금(월)': 'monthlySalary', 근속연수: 'yearsOfService'
  };
  const key = keyMap[th.dataset.key] || 'name';
  if (sortKey === key) sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
  else { sortKey = key; sortDirection = 'asc'; }
  page = 1;
  loadDashboard();
}));

loadDashboard();
