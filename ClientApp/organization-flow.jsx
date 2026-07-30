import React, { memo, useMemo } from 'react';
import { createRoot } from 'react-dom/client';
import dagre from '@dagrejs/dagre';
import {
  Background,
  Controls,
  Handle,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState
} from '@xyflow/react';

const ORG_STORAGE_KEY='innodep-organization-v1';
const DEFAULT_ORGANIZATION=[
  {id:'ceo',name:'CEO',type:'ceo',parentId:null},
  {id:'vision-council',name:'비전정책협의회',type:'council',parentId:'ceo'},
  {id:'audit-office',name:'제도감사실',type:'office',parentId:'ceo'},
  {id:'cao',name:'CAO',type:'executive',parentId:'ceo'},
  {id:'cfo',name:'CFO 그룹',type:'group',parentId:'cao'},
  {id:'cfo-finance',name:'재무혁신부분',type:'division',parentId:'cfo'},
  {id:'finance-accounting',name:'재무회계팀',type:'team',parentId:'cfo-finance'},
  {id:'business-planning',name:'경영기획팀',type:'team',parentId:'cfo-finance'},
  {id:'cfo-innovation',name:'경영혁신부문',type:'division',parentId:'cfo'},
  {id:'hr',name:'HR팀',type:'team',parentId:'cfo-innovation'},
  {id:'infra',name:'Infra팀',type:'team',parentId:'cfo-innovation'},
  {id:'anyang-center',name:'안양센터',type:'center',parentId:'cfo'},
  {id:'manufacturing',name:'제조팀',type:'team',parentId:'anyang-center'},
  {id:'sc',name:'SC팀',type:'team',parentId:'anyang-center'},
  {id:'coo',name:'COO 그룹',type:'group',parentId:'cao'},
  {id:'sales-1-div',name:'영업1부문',type:'division',parentId:'coo'},
  {id:'sales-1',name:'영업 1팀',type:'team',parentId:'sales-1-div'},
  {id:'sales-2-div',name:'영업2부문',type:'division',parentId:'coo'},
  {id:'sales-2',name:'영업 2팀',type:'team',parentId:'sales-2-div'},
  {id:'ilab-div',name:'I-Lab부문',type:'division',parentId:'coo'},
  {id:'ilab-business',name:'I-Lab사업팀',type:'team',parentId:'ilab-div'},
  {id:'sales-planning-div',name:'영업기획부문',type:'division',parentId:'coo'},
  {id:'procurement',name:'구매조달팀',type:'team',parentId:'sales-planning-div'},
  {id:'design',name:'설계팀',type:'team',parentId:'sales-planning-div'},
  {id:'certification',name:'인증팀',type:'team',parentId:'sales-planning-div'},
  {id:'customer-support',name:'고객지원팀',type:'team',parentId:'sales-planning-div'},
  {id:'sales-admin',name:'영업관리팀',type:'team',parentId:'sales-planning-div'},
  {id:'smart-div',name:'스마트사업부문',type:'division',parentId:'coo'},
  {id:'smart-business',name:'스마트사업팀',type:'team',parentId:'smart-div'},
  {id:'cto',name:'CTO 그룹',type:'group',parentId:'cao'},
  {id:'vurix-div',name:'VURIX부문',type:'division',parentId:'cto'},
  {id:'server',name:'서버팀',type:'team',parentId:'vurix-div'},
  {id:'client',name:'클라이언트팀',type:'team',parentId:'vurix-div'},
  {id:'ai-tf',name:'AI사업T/F',type:'taskforce',parentId:'cto'},
  {id:'vunex-div',name:'VUNex사업부문',type:'division',parentId:'cto'},
  {id:'ai',name:'AI팀',type:'team',parentId:'vunex-div'},
  {id:'platform',name:'플랫폼팀',type:'team',parentId:'vunex-div'},
  {id:'pp',name:'PP팀',type:'team',parentId:'vunex-div'},
  {id:'vunex-sales',name:'영업팀',type:'team',parentId:'vunex-div'},
  {id:'qa',name:'QA팀',type:'team',parentId:'cto'},
  {id:'project',name:'과제팀',type:'team',parentId:'cto'},
  {id:'strategy-office',name:'전략기획실',type:'office',parentId:'ceo'},
  {id:'future-strategy',name:'미래전략부분',type:'division',parentId:'strategy-office'},
  {id:'business-development',name:'사업개발팀',type:'team',parentId:'future-strategy'},
  {id:'external-office',name:'대외협력실',type:'office',parentId:'ceo'}
];
const ORG_TYPE_LABELS={ceo:'대표',executive:'임원',group:'그룹',office:'실',division:'부문',center:'센터',team:'팀',taskforce:'T/F',council:'협의회'};
const $=id=>document.getElementById(id);
const clone=data=>data.map(item=>({...item}));

let organization=loadOrganization();
let savedOrganization=clone(organization);
let editingOrgId=null;
let organizationDirty=false;
let canEditOrganization=false;
let organizationEditMode=false;
let organizationFreeLayoutMode=false;
let renderVersion=0;
let flowRoot;
let organizationUpdatedAt=null;

function normalizeOrganization(data){
  const normalized=clone(data);
  new Set(normalized.map(item=>item.parentId)).forEach(parentId=>{
    normalized.filter(item=>item.parentId===parentId)
      .sort((a,b)=>(Number.isFinite(a.order)?a.order:normalized.indexOf(a))-(Number.isFinite(b.order)?b.order:normalized.indexOf(b)))
      .forEach((item,index)=>item.order=index);
  });
  return normalized;
}

function validateOrganization(data){
  if(!Array.isArray(data)||!data.length)return false;
  const ids=new Set(data.map(item=>item?.id));
  if(ids.size!==data.length||ids.has(undefined))return false;
  if(!data.every(item=>typeof item.name==='string'&&item.name.trim()&&ORG_TYPE_LABELS[item.type]&&(item.parentId===null||ids.has(item.parentId))))return false;
  if(data.filter(item=>item.parentId===null).length!==1)return false;
  return data.every(item=>{
    const seen=new Set([item.id]);let current=item;
    while(current.parentId!==null){
      if(seen.has(current.parentId))return false;
      seen.add(current.parentId);current=data.find(candidate=>candidate.id===current.parentId);
      if(!current)return false;
    }
    return true;
  });
}

function loadOrganization(){
  try{
    const saved=JSON.parse(localStorage.getItem(ORG_STORAGE_KEY));
    const loaded=validateOrganization(saved)?saved:clone(DEFAULT_ORGANIZATION);
    ['cfo','coo','cto'].forEach(id=>{
      const item=loaded.find(candidate=>candidate.id===id);
      if(item?.parentId==='ceo'&&loaded.some(candidate=>candidate.id==='cao'))item.parentId='cao';
    });
    return normalizeOrganization(loaded);
  }catch{return normalizeOrganization(clone(DEFAULT_ORGANIZATION));}
}

function childrenOf(parentId){
  const parent=organization.find(item=>item.id===parentId);
  return organization.filter(item=>item.parentId===parentId).sort((a,b)=>{
    if(parent?.type==='ceo'){
      const executiveOrder=Number(b.type==='executive')-Number(a.type==='executive');
      if(executiveOrder)return executiveOrder;
    }
    return (a.order??0)-(b.order??0)||a.name.localeCompare(b.name,'ko');
  });
}

function normalizeSiblingOrders(parentId){childrenOf(parentId).forEach((item,index)=>item.order=index);}

function orderedItems(){
  const result=[];
  const visit=parentId=>childrenOf(parentId).forEach(item=>{result.push(item);visit(item.id);});
  const root=organization.find(item=>item.parentId===null);
  if(root){result.push(root);visit(root.id);}
  return result;
}

function nodeSize(item){
  const width=Math.max(item.type==='ceo'?96:item.type==='executive'||item.type==='group'?104:76,Math.min(154,item.name.length*11+28));
  return {width,height:item.type==='ceo'?48:item.type==='executive'||item.type==='group'?44:40};
}

function buildFlowModel(){
  const graph=new dagre.graphlib.Graph();
  graph.setDefaultEdgeLabel(()=>({}));
  graph.setGraph({rankdir:'TB',ranker:'tight-tree',nodesep:22,ranksep:66,edgesep:12,marginx:34,marginy:28});
  const items=orderedItems();
  items.forEach(item=>graph.setNode(item.id,nodeSize(item)));
  items.filter(item=>item.parentId!==null).forEach(item=>graph.setEdge(item.parentId,item.id,{weight:2}));
  dagre.layout(graph);
  const autoPositions=new Map();
  const nodes=items.map(item=>{
    const size=nodeSize(item),point=graph.node(item.id);
    const auto={x:point.x-size.width/2,y:point.y-size.height/2};
    autoPositions.set(item.id,auto);
    return {
      id:item.id,
      type:'organization',
      position:{x:auto.x+(Number(item.layoutX)||0),y:auto.y+(Number(item.layoutY)||0)},
      data:{item,canEdit:canEditOrganization,editMode:organizationEditMode,freeMode:organizationFreeLayoutMode},
      draggable:canEditOrganization&&(organizationFreeLayoutMode||(organizationEditMode&&item.parentId!==null)),
      selectable:false,
      connectable:false,
      style:{width:size.width,height:size.height}
    };
  });
  const edges=items.filter(item=>item.parentId!==null).map(item=>({
    id:`${item.parentId}-${item.id}`,
    source:item.parentId,
    target:item.id,
    type:'smoothstep',
    pathOptions:{borderRadius:8,offset:22},
    className:'org-flow-edge',
    focusable:false,
    selectable:false
  }));
  return {nodes,edges,autoPositions};
}

const OrganizationNode=memo(({data})=>{
  const {item,canEdit,editMode,freeMode}=data;
  return <div className={`org-flow-node org-flow-node-${item.type}${canEdit?'':' is-readonly'}${editMode||freeMode?' is-movable':''}`}>
    {item.parentId!==null&&<Handle type="target" position={Position.Top} className="org-flow-handle"/>}
    <strong>{item.name}</strong>
    {canEdit&&<span>{editMode||freeMode?'이동':'수정'}</span>}
    <Handle type="source" position={Position.Bottom} className="org-flow-handle"/>
  </div>;
});
const nodeTypes={organization:OrganizationNode};

function FlowCanvas({version}){
  const model=useMemo(buildFlowModel,[version]);
  const [nodes,setNodes,onNodesChange]=useNodesState(model.nodes);
  const [edges,,onEdgesChange]=useEdgesState(model.edges);
  const onNodeClick=(_,node)=>{
    if(!canEditOrganization||organizationEditMode||organizationFreeLayoutMode)return;
    openOrgDialog(node.id);
  };
  const onNodeDragStop=(_,node)=>{
    const item=organization.find(candidate=>candidate.id===node.id);
    if(!item)return;
    if(organizationFreeLayoutMode){
      const auto=model.autoPositions.get(node.id);
      item.layoutX=Math.round(node.position.x-auto.x);
      item.layoutY=Math.round(node.position.y-auto.y);
      renderOrganization(false);
      markOrganizationDirty(`‘${item.name}’ 조직의 화면 위치를 변경했습니다.`);
      return;
    }
    if(!organizationEditMode){renderOrganization(false);return;}
    const width=node.measured?.width||node.width||100,height=node.measured?.height||node.height||40;
    const center={x:node.position.x+width/2,y:node.position.y+height/2};
    const target=nodes.find(candidate=>{
      if(candidate.id===node.id)return false;
      const targetWidth=candidate.measured?.width||candidate.width||100,targetHeight=candidate.measured?.height||candidate.height||40;
      return center.x>=candidate.position.x&&center.x<=candidate.position.x+targetWidth&&center.y>=candidate.position.y&&center.y<=candidate.position.y+targetHeight;
    });
    if(!target){renderOrganization(false);return;}
    const targetWidth=target.measured?.width||target.width||100;
    const ratio=(center.x-target.position.x)/targetWidth;
    const mode=target.data.item.parentId!==null&&ratio<.24?'before':target.data.item.parentId!==null&&ratio>.76?'after':'child';
    if(canMoveOrganization(node.id,target.id,mode))moveOrganization(node.id,target.id,mode);
    else renderOrganization(false);
  };
  return <ReactFlow
    nodes={nodes}
    edges={edges}
    nodeTypes={nodeTypes}
    onNodesChange={onNodesChange}
    onEdgesChange={onEdgesChange}
    onNodeClick={onNodeClick}
    onNodeDragStop={onNodeDragStop}
    nodesConnectable={false}
    nodesFocusable={false}
    edgesFocusable={false}
    elementsSelectable={false}
    deleteKeyCode={null}
    panOnDrag
    zoomOnDoubleClick={false}
    minZoom={0.2}
    maxZoom={1.8}
    fitView
    fitViewOptions={{padding:.08,minZoom:.2,maxZoom:1.15,duration:fitOnRender?220:0}}
    colorMode={document.documentElement.dataset.theme==='dark'?'dark':'light'}
  >
    <Background gap={24} size={1} color="var(--org-flow-grid)"/>
    <Controls showInteractive={false}/>
  </ReactFlow>;
}

let fitOnRender=true;
function renderOrganization(fit=true){
  fitOnRender=fit;renderVersion++;
  if(!flowRoot)flowRoot=createRoot($('orgChart'));
  flowRoot.render(<ReactFlowProvider><FlowCanvas key={renderVersion} version={renderVersion}/></ReactFlowProvider>);
}

function descendantIds(id){
  const descendants=new Set();
  const visit=parentId=>organization.filter(item=>item.parentId===parentId).forEach(item=>{descendants.add(item.id);visit(item.id);});
  visit(id);return descendants;
}

function canMoveOrganization(sourceId,targetId,mode){
  if(!sourceId||!targetId||!mode||sourceId===targetId)return false;
  const source=organization.find(item=>item.id===sourceId),target=organization.find(item=>item.id===targetId);
  if(!source||!target||source.parentId===null)return false;
  const newParentId=mode==='child'?targetId:target.parentId;
  return newParentId!==null&&newParentId!==sourceId&&!descendantIds(sourceId).has(newParentId);
}

function moveOrganization(sourceId,targetId,mode){
  const source=organization.find(item=>item.id===sourceId),target=organization.find(item=>item.id===targetId);
  if(!source||!target)return;
  const oldParentId=source.parentId,newParentId=mode==='child'?target.id:target.parentId;
  if(newParentId===null)return;
  const siblings=childrenOf(newParentId).filter(item=>item.id!==sourceId);
  let insertIndex=siblings.length;
  if(mode!=='child'){
    const targetIndex=siblings.findIndex(item=>item.id===targetId);
    insertIndex=Math.max(0,targetIndex+(mode==='after'?1:0));
  }
  source.parentId=newParentId;source.layoutX=0;source.layoutY=0;
  siblings.splice(insertIndex,0,source);siblings.forEach((item,index)=>item.order=index);
  if(oldParentId!==newParentId)normalizeSiblingOrders(oldParentId);
  renderOrganization();
  markOrganizationDirty(`‘${source.name}’ 조직의 소속 또는 순서를 변경했습니다.`);
}

function openOrgDialog(id=null){
  if(!canEditOrganization)return;
  editingOrgId=id;
  const item=id?organization.find(candidate=>candidate.id===id):null;
  $('orgDialogTitle').textContent=item?'조직 정보 수정':'새 조직 추가';
  $('orgName').value=item?.name||'';
  $('orgFormError').textContent='';
  $('orgDeleteBtn').hidden=!item||item.parentId===null;
  const excluded=item?descendantIds(item.id):new Set();
  if(item)excluded.add(item.id);
  const parentSelect=$('orgParent');parentSelect.replaceChildren();
  if(item?.parentId===null){
    parentSelect.append(new Option('최상위 조직',''));parentSelect.disabled=true;
  }else{
    parentSelect.disabled=false;
    orderedItems().filter(candidate=>!excluded.has(candidate.id)).forEach(candidate=>{
      const option=new Option(candidate.name,candidate.id);
      option.selected=candidate.id===(item?.parentId||'ceo');parentSelect.append(option);
    });
  }
  $('orgDialog').showModal();
  requestAnimationFrame(()=>$('orgName').focus());
}

function markOrganizationDirty(message='저장되지 않은 변경사항이 있습니다.'){
  if(!canEditOrganization)return;
  organizationDirty=true;$('orgStatus').textContent=message;$('orgStatus').classList.add('is-dirty');
  $('orgSaveBtn').classList.add('has-changes');$('orgCancelBtn').disabled=false;$('sideOrgStatus').textContent='저장 필요';
}

async function requestOrganization(method='GET'){
  const response=await fetch('/api/organization',method==='GET'?undefined:{
    method:'PUT',
    headers:{'Content-Type':'application/json'},
    body:JSON.stringify({items:organization})
  });
  const data=await response.json().catch(()=>({}));
  if(!response.ok)throw new Error(data.message||`조직도 서버 요청 실패(HTTP ${response.status})`);
  return data;
}

function acceptServerOrganization(data){
  if(!validateOrganization(data.items))return false;
  organization=normalizeOrganization(data.items);
  savedOrganization=clone(organization);
  organizationUpdatedAt=data.updatedAtUtc||null;
  try{localStorage.setItem(ORG_STORAGE_KEY,JSON.stringify(organization));}catch{}
  return true;
}

$('orgForm').addEventListener('submit',event=>{
  event.preventDefault();
  if(!canEditOrganization)return;
  const name=$('orgName').value.trim();
  if(!name){$('orgFormError').textContent='조직명을 입력해 주세요.';return;}
  if(editingOrgId){
    const item=organization.find(candidate=>candidate.id===editingOrgId);
    const oldParentId=item.parentId,newParentId=item.parentId===null?null:$('orgParent').value;
    item.name=name;
    if(item.parentId!==null&&oldParentId!==newParentId){
      item.parentId=newParentId;item.order=childrenOf(newParentId).filter(candidate=>candidate.id!==item.id).length;
      item.layoutX=0;item.layoutY=0;normalizeSiblingOrders(oldParentId);normalizeSiblingOrders(newParentId);
    }
  }else{
    const parentId=$('orgParent').value;
    organization.push({id:`org-${Date.now()}-${Math.random().toString(36).slice(2,7)}`,name,type:'team',parentId,order:childrenOf(parentId).length});
  }
  $('orgDialog').close();renderOrganization();markOrganizationDirty();
});

$('orgDeleteBtn').addEventListener('click',()=>{
  if(!canEditOrganization)return;
  const item=organization.find(candidate=>candidate.id===editingOrgId);
  if(!item||item.parentId===null)return;
  const descendants=descendantIds(item.id),detail=descendants.size?` 하위 조직 ${descendants.size}개도 함께 삭제됩니다.`:'';
  if(!confirm(`‘${item.name}’ 조직을 삭제하시겠습니까?${detail}`))return;
  const oldParentId=item.parentId;descendants.add(item.id);
  organization=organization.filter(candidate=>!descendants.has(candidate.id));normalizeSiblingOrders(oldParentId);
  $('orgDialog').close();renderOrganization();markOrganizationDirty();
});
['orgDialogCloseBtn','orgDialogCancelBtn'].forEach(id=>$(id).addEventListener('click',()=>$('orgDialog').close()));

$('orgAddBtn').addEventListener('click',()=>{if(canEditOrganization)openOrgDialog();});
$('orgSaveBtn').addEventListener('click',async()=>{
  if(!canEditOrganization)return;
  const button=$('orgSaveBtn');button.disabled=true;
  try{
    $('orgStatus').textContent='서버 DB에 저장하고 있습니다.';
    const data=await requestOrganization('PUT');
    acceptServerOrganization(data);organizationDirty=false;
    const savedAt=organizationUpdatedAt?new Date(organizationUpdatedAt):new Date();
    $('orgStatus').textContent=`서버 DB 저장 완료 · ${savedAt.toLocaleString('ko-KR')}`;$('orgStatus').classList.remove('is-dirty');
    $('orgSaveBtn').classList.remove('has-changes');$('orgCancelBtn').disabled=true;$('sideOrgStatus').textContent=`${organization.length}개 조직 저장됨`;
  }catch(error){
    $('orgStatus').textContent=`서버 DB 저장 실패: ${error.message}`;$('orgStatus').classList.add('is-dirty');
  }finally{button.disabled=false;}
});

$('orgCancelBtn').addEventListener('click',()=>{
  if(!canEditOrganization||!organizationDirty||!confirm('저장하지 않은 조직도 변경사항을 취소하시겠습니까?'))return;
  organization=clone(savedOrganization);organizationDirty=false;renderOrganization();
  $('orgStatus').textContent='저장된 조직도로 되돌렸습니다.';$('orgStatus').classList.remove('is-dirty');
  $('orgSaveBtn').classList.remove('has-changes');$('orgCancelBtn').disabled=true;$('sideOrgStatus').textContent=`${organization.length}개 조직 불러옴`;
});

function setEditMode(mode){
  organizationEditMode=mode==='hierarchy'?!organizationEditMode:false;
  organizationFreeLayoutMode=mode==='free'?!organizationFreeLayoutMode:false;
  $('orgLayoutBtn').classList.toggle('is-active',organizationEditMode);
  $('orgLayoutBtn').setAttribute('aria-pressed',String(organizationEditMode));
  $('orgLayoutBtn').textContent=organizationEditMode?'소속·순서 편집 종료':'소속·순서 편집';
  $('orgFreeLayoutBtn').classList.toggle('is-active',organizationFreeLayoutMode);
  $('orgFreeLayoutBtn').setAttribute('aria-pressed',String(organizationFreeLayoutMode));
  $('orgFreeLayoutBtn').textContent=organizationFreeLayoutMode?'자유 배치 종료':'자유 배치';
  renderOrganization(false);updateOrganizationGuide();
}
$('orgLayoutBtn').addEventListener('click',()=>{if(canEditOrganization)setEditMode('hierarchy');});
$('orgFreeLayoutBtn').addEventListener('click',()=>{if(canEditOrganization)setEditMode('free');});

$('orgPositionResetBtn').addEventListener('click',()=>{
  if(!canEditOrganization||!organization.some(item=>(Number(item.layoutX)||0)!==0||(Number(item.layoutY)||0)!==0))return;
  if(!confirm('모든 조직 박스를 자동 배치 위치로 되돌리시겠습니까?'))return;
  organization.forEach(item=>{item.layoutX=0;item.layoutY=0;});renderOrganization();
  markOrganizationDirty('모든 조직 박스 위치를 자동 배치 상태로 되돌렸습니다.');
});

$('orgResetBtn').addEventListener('click',()=>{
  if(!canEditOrganization||!confirm('처음 전달받은 조직 구성으로 되돌리시겠습니까? 저장 버튼을 누르기 전까지는 확정되지 않습니다.'))return;
  organization=normalizeOrganization(clone(DEFAULT_ORGANIZATION));renderOrganization();
  markOrganizationDirty('초기 조직도를 불러왔습니다. 저장하면 확정됩니다.');
});

window.addEventListener('beforeunload',event=>{if(organizationDirty){event.preventDefault();event.returnValue='';}});

function updateOrganizationGuide(){
  document.querySelector('.org-guide > span:first-child').textContent=!canEditOrganization
    ?'조직도 변경은 HR 관리자 또는 관리자만 가능합니다.'
    :organizationFreeLayoutMode
      ?'조직 박스를 원하는 좌표로 드래그하세요. SmoothStep 연결선은 이동 즉시 따라갑니다.'
      :organizationEditMode
        ?'조직을 다른 카드 중앙에 놓으면 하위로, 좌우 가장자리에 놓으면 같은 단계 순서로 이동합니다.'
        :'조직 정보 수정, 소속·순서 편집 또는 자유 배치를 선택할 수 있습니다.';
}

function applyOrganizationPermissions(){
  ['orgResetBtn','orgCancelBtn','orgPositionResetBtn','orgLayoutBtn','orgFreeLayoutBtn','orgAddBtn','orgSaveBtn'].forEach(id=>$(id).hidden=!canEditOrganization);
  document.querySelector('.topbar .subtitle').textContent=canEditOrganization?'조직 구조를 확인하고 변경사항을 직접 관리합니다.':'조직 구조를 확인할 수 있습니다.';
  document.querySelector('.org-toolbar strong').textContent=canEditOrganization?'조직도 관리':'조직도';
  updateOrganizationGuide();$('orgStatus').textContent='서버 조직도를 불러오는 중입니다.';$('orgCancelBtn').disabled=true;
  $('sideOrgStatus').textContent=`${organization.length}개 조직 불러옴`;
}

async function initializeOrganization(){
  try{
    const response=await fetch('/api/session');
    if(response.status===401){location.replace('/login');return;}
    if(response.ok){
      const session=await response.json();canEditOrganization=Boolean(session.canEdit);$('sessionUser').textContent=session.userName||'로그인 사용자';
      if(session.theme)window.setDashboardTheme(session.theme);
    }
  }catch{canEditOrganization=false;}
  applyOrganizationPermissions();
  try{
    const data=await requestOrganization();
    if(acceptServerOrganization(data)){
      const updated=data.updatedAtUtc?new Date(data.updatedAtUtc).toLocaleString('ko-KR'):'';
      $('orgStatus').textContent=updated?`서버 DB 불러옴 · ${updated}`:'서버 DB에서 조직도를 불러왔습니다.';
      $('sideOrgStatus').textContent=`${organization.length}개 조직 · 서버 DB`;
    }else if(canEditOrganization){
      $('orgStatus').textContent='기존 브라우저 조직도를 서버 DB로 옮기고 있습니다.';
      const migrated=await requestOrganization('PUT');
      acceptServerOrganization(migrated);
      $('orgStatus').textContent='기존 조직도를 서버 DB로 옮겼습니다.';
      $('sideOrgStatus').textContent=`${organization.length}개 조직 · 서버 DB`;
    }else{
      $('orgStatus').textContent='서버 조직도가 비어 있습니다. 편집 권한 사용자의 최초 접속이 필요합니다.';
      $('sideOrgStatus').textContent='서버 DB 초기화 필요';
    }
  }catch(error){
    $('orgStatus').textContent=`서버 DB 불러오기 실패 · 브라우저 백업 표시: ${error.message}`;
    $('orgStatus').classList.add('is-dirty');$('sideOrgStatus').textContent='브라우저 백업 표시 중';
  }
  renderOrganization();
}

$('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST'});location.replace('/login');};
initializeOrganization();
