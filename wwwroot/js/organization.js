const ORG_STORAGE_KEY = 'innodep-organization-v1';
const DEFAULT_ORGANIZATION = [
  { id:'ceo', name:'CEO', type:'ceo', parentId:null },
  { id:'vision-council', name:'비전정책협의회', type:'council', parentId:'ceo' },
  { id:'audit-office', name:'제도감사실', type:'office', parentId:'ceo' },
  { id:'cao', name:'CAO', type:'executive', parentId:'ceo' },
  { id:'cfo', name:'CFO 그룹', type:'group', parentId:'ceo' },
  { id:'cfo-finance', name:'재무혁식부분', type:'division', parentId:'cfo' },
  { id:'finance-accounting', name:'재무회계팀', type:'team', parentId:'cfo-finance' },
  { id:'business-planning', name:'경영기획팀', type:'team', parentId:'cfo-finance' },
  { id:'cfo-innovation', name:'경영혁신부문', type:'division', parentId:'cfo' },
  { id:'hr', name:'HR팀', type:'team', parentId:'cfo-innovation' },
  { id:'infra', name:'Infra팀', type:'team', parentId:'cfo-innovation' },
  { id:'anyang-center', name:'안양센터', type:'center', parentId:'cfo' },
  { id:'manufacturing', name:'제조팀', type:'team', parentId:'anyang-center' },
  { id:'sc', name:'SC팀', type:'team', parentId:'anyang-center' },
  { id:'coo', name:'COO 그룹', type:'group', parentId:'ceo' },
  { id:'sales-1-div', name:'영업1부문', type:'division', parentId:'coo' },
  { id:'sales-1', name:'영업 1팀', type:'team', parentId:'sales-1-div' },
  { id:'sales-2-div', name:'영업2부문', type:'division', parentId:'coo' },
  { id:'sales-2', name:'영업 2팀', type:'team', parentId:'sales-2-div' },
  { id:'ilab-div', name:'I-Lab부문', type:'division', parentId:'coo' },
  { id:'ilab-business', name:'I-Lab사업팀', type:'team', parentId:'ilab-div' },
  { id:'sales-planning-div', name:'영업기획부문', type:'division', parentId:'coo' },
  { id:'procurement', name:'구매조달팀', type:'team', parentId:'sales-planning-div' },
  { id:'design', name:'설계팀', type:'team', parentId:'sales-planning-div' },
  { id:'certification', name:'인증팀', type:'team', parentId:'sales-planning-div' },
  { id:'customer-support', name:'고객지원팀', type:'team', parentId:'sales-planning-div' },
  { id:'sales-admin', name:'영업관리팀', type:'team', parentId:'sales-planning-div' },
  { id:'smart-div', name:'스마트사업부문', type:'division', parentId:'coo' },
  { id:'smart-business', name:'스마트사업팀', type:'team', parentId:'smart-div' },
  { id:'cto', name:'CTO 그룹', type:'group', parentId:'ceo' },
  { id:'vurix-div', name:'VURIX부문', type:'division', parentId:'cto' },
  { id:'server', name:'서버팀', type:'team', parentId:'vurix-div' },
  { id:'client', name:'클라이언트팀', type:'team', parentId:'vurix-div' },
  { id:'ai-tf', name:'AI사업T/F', type:'taskforce', parentId:'cto' },
  { id:'vunex-div', name:'VUNex사업부문', type:'division', parentId:'cto' },
  { id:'ai', name:'AI팀', type:'team', parentId:'vunex-div' },
  { id:'platform', name:'플랫폼팀', type:'team', parentId:'vunex-div' },
  { id:'pp', name:'PP팀', type:'team', parentId:'vunex-div' },
  { id:'vunex-sales', name:'영업팀', type:'team', parentId:'vunex-div' },
  { id:'qa', name:'QA팀', type:'team', parentId:'cto' },
  { id:'project', name:'과제팀', type:'team', parentId:'cto' },
  { id:'strategy-office', name:'전략기획실', type:'office', parentId:'ceo' },
  { id:'future-strategy', name:'미래전략부분', type:'division', parentId:'strategy-office' },
  { id:'business-development', name:'사업개발팀', type:'team', parentId:'future-strategy' },
  { id:'external-office', name:'대외협력실', type:'office', parentId:'ceo' }
];

const ORG_TYPE_LABELS = { ceo:'대표', executive:'임원', group:'그룹', office:'실', division:'부문', center:'센터', team:'팀', taskforce:'T/F', council:'협의회' };
const $ = id => document.getElementById(id);
let organization = loadOrganization();
let editingOrgId = null;
let organizationDirty = false;

function cloneOrganization(data) {
  return data.map(item => ({ ...item }));
}

function loadOrganization() {
  try {
    const saved = JSON.parse(localStorage.getItem(ORG_STORAGE_KEY));
    return validateOrganization(saved) ? saved : cloneOrganization(DEFAULT_ORGANIZATION);
  } catch (_) {
    return cloneOrganization(DEFAULT_ORGANIZATION);
  }
}

function validateOrganization(data) {
  if (!Array.isArray(data) || !data.length) return false;
  const ids = new Set(data.map(item => item && item.id));
  if (ids.size !== data.length || ids.has(undefined)) return false;
  if (!data.every(item => typeof item.name === 'string' && item.name.trim() && ORG_TYPE_LABELS[item.type] && (item.parentId === null || ids.has(item.parentId)))) return false;
  const roots = data.filter(item => item.parentId === null);
  if (roots.length !== 1) return false;
  return data.every(item => {
    const seen = new Set([item.id]);
    let current = item;
    while (current.parentId !== null) {
      if (seen.has(current.parentId)) return false;
      seen.add(current.parentId);
      current = data.find(candidate => candidate.id === current.parentId);
      if (!current) return false;
    }
    return true;
  });
}

function renderOrganization() {
  const root = organization.find(item => item.parentId === null);
  const chart = $('orgChart');
  chart.replaceChildren();
  if (root) chart.append(createOrgBranch(root, true));
}

function createOrgBranch(item, isRoot = false) {
  const branch = document.createElement('div');
  branch.className = `org-branch${isRoot ? ' org-root-branch' : ''}`;
  const card = document.createElement('button');
  card.type = 'button';
  card.className = `org-node org-node-${item.type}`;

  const type = document.createElement('span');
  type.className = 'org-type';
  type.textContent = ORG_TYPE_LABELS[item.type];
  const name = document.createElement('strong');
  name.textContent = item.name;
  const edit = document.createElement('span');
  edit.className = 'org-edit-hint';
  edit.textContent = '수정';
  card.append(type, name, edit);
  card.addEventListener('click', () => openOrgDialog(item.id));
  branch.append(card);

  const children = organization.filter(candidate => candidate.parentId === item.id);
  if (children.length) {
    const childArea = document.createElement('div');
    childArea.className = `org-children${isRoot ? ' org-top-level' : ''}`;
    if (isRoot) {
      const lanes = [
        { label:'CEO 직속 조직', items:children.filter(child => child.type !== 'group') },
        { label:'사업 그룹', items:children.filter(child => child.type === 'group') }
      ];
      lanes.filter(lane => lane.items.length).forEach(lane => {
        const section = document.createElement('section');
        section.className = `org-lane${lane.label === '사업 그룹' ? ' org-group-lane' : ''}`;
        const label = document.createElement('h3');
        label.textContent = lane.label;
        const nodes = document.createElement('div');
        nodes.className = 'org-lane-nodes';
        lane.items.forEach(child => nodes.append(createOrgBranch(child)));
        section.append(label, nodes);
        childArea.append(section);
      });
    } else {
      children.forEach(child => childArea.append(createOrgBranch(child)));
    }
    branch.append(childArea);
  }
  return branch;
}

function descendantIds(id) {
  const descendants = new Set();
  const visit = parentId => organization.filter(item => item.parentId === parentId).forEach(item => {
    descendants.add(item.id);
    visit(item.id);
  });
  visit(id);
  return descendants;
}

function openOrgDialog(id = null) {
  editingOrgId = id;
  const item = id ? organization.find(candidate => candidate.id === id) : null;
  $('orgDialogTitle').textContent = item ? '조직 정보 수정' : '새 조직 추가';
  $('orgName').value = item?.name || '';
  $('orgType').value = item?.type || 'team';
  $('orgFormError').textContent = '';
  $('orgDeleteBtn').hidden = !item || item.parentId === null;

  const excluded = item ? descendantIds(item.id) : new Set();
  if (item) excluded.add(item.id);
  const parentSelect = $('orgParent');
  parentSelect.replaceChildren();
  if (item?.parentId === null) {
    parentSelect.append(new Option('최상위 조직', ''));
    parentSelect.disabled = true;
  } else {
    parentSelect.disabled = false;
    organization.filter(candidate => !excluded.has(candidate.id)).forEach(candidate => {
      const option = new Option(candidate.name, candidate.id);
      option.selected = candidate.id === (item?.parentId || 'ceo');
      parentSelect.append(option);
    });
  }
  $('orgDialog').showModal();
  requestAnimationFrame(() => $('orgName').focus());
}

function markOrganizationDirty(message = '저장되지 않은 변경사항이 있습니다.') {
  organizationDirty = true;
  $('orgStatus').textContent = message;
  $('orgStatus').classList.add('is-dirty');
  $('orgSaveBtn').classList.add('has-changes');
  $('sideOrgStatus').textContent = '저장 필요';
}

$('orgForm').addEventListener('submit', event => {
  event.preventDefault();
  const name = $('orgName').value.trim();
  if (!name) {
    $('orgFormError').textContent = '조직명을 입력해 주세요.';
    return;
  }
  if (editingOrgId) {
    const item = organization.find(candidate => candidate.id === editingOrgId);
    item.name = name;
    item.type = $('orgType').value;
    if (item.parentId !== null) item.parentId = $('orgParent').value;
  } else {
    organization.push({
      id: `org-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      name,
      type: $('orgType').value,
      parentId: $('orgParent').value
    });
  }
  $('orgDialog').close();
  renderOrganization();
  markOrganizationDirty();
});

$('orgDeleteBtn').addEventListener('click', () => {
  const item = organization.find(candidate => candidate.id === editingOrgId);
  if (!item || item.parentId === null) return;
  const descendants = descendantIds(item.id);
  const detail = descendants.size ? ` 하위 조직 ${descendants.size}개도 함께 삭제됩니다.` : '';
  if (!confirm(`‘${item.name}’ 조직을 삭제하시겠습니까?${detail}`)) return;
  descendants.add(item.id);
  organization = organization.filter(candidate => !descendants.has(candidate.id));
  $('orgDialog').close();
  renderOrganization();
  markOrganizationDirty();
});

$('orgAddBtn').addEventListener('click', () => openOrgDialog());
$('orgSaveBtn').addEventListener('click', () => {
  try {
    localStorage.setItem(ORG_STORAGE_KEY, JSON.stringify(organization));
    organizationDirty = false;
    $('orgStatus').textContent = `저장 완료 · ${new Date().toLocaleString('ko-KR')}`;
    $('orgStatus').classList.remove('is-dirty');
    $('orgSaveBtn').classList.remove('has-changes');
    $('sideOrgStatus').textContent = `${organization.length}개 조직 저장됨`;
  } catch (_) {
    $('orgStatus').textContent = '브라우저 저장소에 저장하지 못했습니다. 내보내기로 백업해 주세요.';
    $('orgStatus').classList.add('is-dirty');
  }
});

$('orgResetBtn').addEventListener('click', () => {
  if (!confirm('처음 전달받은 조직 구성으로 되돌리시겠습니까? 저장 버튼을 누르기 전까지는 확정되지 않습니다.')) return;
  organization = cloneOrganization(DEFAULT_ORGANIZATION);
  renderOrganization();
  markOrganizationDirty('초기 조직도를 불러왔습니다. 저장하면 확정됩니다.');
});

$('orgExportBtn').addEventListener('click', () => {
  const blob = new Blob([JSON.stringify(organization, null, 2)], { type:'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `innodep-organization-${new Date().toISOString().slice(0, 10)}.json`;
  link.click();
  URL.revokeObjectURL(url);
});

$('orgImportBtn').addEventListener('click', () => $('orgFileInput').click());
$('orgFileInput').addEventListener('change', async event => {
  const file = event.target.files[0];
  if (!file) return;
  try {
    const imported = JSON.parse(await file.text());
    if (!validateOrganization(imported)) throw new Error('올바른 조직도 형식이 아닙니다.');
    organization = cloneOrganization(imported);
    renderOrganization();
    markOrganizationDirty('조직도를 가져왔습니다. 저장하면 확정됩니다.');
  } catch (error) {
    alert(`가져오기 실패: ${error.message}`);
  } finally {
    event.target.value = '';
  }
});

window.addEventListener('beforeunload', event => {
  if (!organizationDirty) return;
  event.preventDefault();
  event.returnValue = '';
});

$('sideOrgStatus').textContent = `${organization.length}개 조직 불러옴`;
renderOrganization();
