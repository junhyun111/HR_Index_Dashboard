const ORG_STORAGE_KEY = 'innodep-organization-v1';
const DEFAULT_ORGANIZATION = [
  { id:'ceo', name:'CEO', type:'ceo', parentId:null },
  { id:'vision-council', name:'비전정책협의회', type:'council', parentId:'ceo' },
  { id:'audit-office', name:'제도감사실', type:'office', parentId:'ceo' },
  { id:'cao', name:'CAO', type:'executive', parentId:'ceo' },
  { id:'cfo', name:'CFO 그룹', type:'group', parentId:'cao' },
  { id:'cfo-finance', name:'재무혁신부분', type:'division', parentId:'cfo' },
  { id:'finance-accounting', name:'재무회계팀', type:'team', parentId:'cfo-finance' },
  { id:'business-planning', name:'경영기획팀', type:'team', parentId:'cfo-finance' },
  { id:'cfo-innovation', name:'경영혁신부문', type:'division', parentId:'cfo' },
  { id:'hr', name:'HR팀', type:'team', parentId:'cfo-innovation' },
  { id:'infra', name:'Infra팀', type:'team', parentId:'cfo-innovation' },
  { id:'anyang-center', name:'안양센터', type:'center', parentId:'cfo' },
  { id:'manufacturing', name:'제조팀', type:'team', parentId:'anyang-center' },
  { id:'sc', name:'SC팀', type:'team', parentId:'anyang-center' },
  { id:'coo', name:'COO 그룹', type:'group', parentId:'cao' },
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
  { id:'cto', name:'CTO 그룹', type:'group', parentId:'cao' },
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
let savedOrganization = cloneOrganization(organization);
let editingOrgId = null;
let organizationDirty = false;
let canEditOrganization = false;
let organizationEditMode = false;
let organizationFreeLayoutMode = false;
let draggedOrgId = null;
let dragJustEnded = false;
let connectorFrame = 0;

function cloneOrganization(data) {
  return data.map(item => ({ ...item }));
}

function loadOrganization() {
  try {
    const saved = JSON.parse(localStorage.getItem(ORG_STORAGE_KEY));
    const loaded = validateOrganization(saved) ? saved : cloneOrganization(DEFAULT_ORGANIZATION);
    ['cfo','coo','cto'].forEach(id => {
      const item = loaded.find(candidate => candidate.id === id);
      if (item?.parentId === 'ceo' && loaded.some(candidate => candidate.id === 'cao')) item.parentId = 'cao';
    });
    return normalizeOrganization(loaded);
  } catch (_) {
    return normalizeOrganization(cloneOrganization(DEFAULT_ORGANIZATION));
  }
}

function normalizeOrganization(data) {
  const normalized = cloneOrganization(data);
  const parentIds = new Set(normalized.map(item => item.parentId));
  parentIds.forEach(parentId => {
    normalized.filter(item => item.parentId === parentId)
      .sort((a, b) => (Number.isFinite(a.order) ? a.order : normalized.indexOf(a)) - (Number.isFinite(b.order) ? b.order : normalized.indexOf(b)))
      .forEach((item, index) => item.order = index);
  });
  return normalized;
}

function childrenOf(parentId) {
  const parent = organization.find(item => item.id === parentId);
  return organization.filter(item => item.parentId === parentId)
    .sort((a, b) => {
      if (parent?.type === 'ceo') {
        const executiveOrder = Number(b.type === 'executive') - Number(a.type === 'executive');
        if (executiveOrder) return executiveOrder;
      }
      return (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name, 'ko');
    });
}

function normalizeSiblingOrders(parentId) {
  childrenOf(parentId).forEach((item, index) => item.order = index);
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
  const connectors = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  connectors.classList.add('org-connectors');
  connectors.setAttribute('aria-hidden', 'true');
  chart.append(connectors);
  if (root) chart.append(createOrgBranch(root, true));
  scheduleOrganizationConnectors();
}

function createOrgBranch(item, isRoot = false) {
  const branch = document.createElement('div');
  branch.className = `org-branch${isRoot ? ' org-root-branch' : ''}`;
  const card = document.createElement('button');
  card.type = 'button';
  card.className = `org-node org-node-${item.type}`;
  card.dataset.orgId = item.id;
  card.style.translate = `${Number(item.layoutX)||0}px ${Number(item.layoutY)||0}px`;

  const name = document.createElement('strong');
  name.textContent = item.name;
  const edit = document.createElement('span');
  edit.className = 'org-edit-hint';
  edit.textContent = canEditOrganization ? (organizationEditMode||organizationFreeLayoutMode ? '이동' : '수정') : '';
  card.append(name, edit);
  card.disabled = !canEditOrganization;
  card.classList.toggle('org-node-readonly', !canEditOrganization);
  if (canEditOrganization) {
    card.addEventListener('click', () => {
      if (!dragJustEnded) openOrgDialog(item.id);
    });
    bindOrganizationDrag(card, item, isRoot);
    bindOrganizationFreeDrag(card,item);
  }
  branch.append(card);

  const children = childrenOf(item.id);
  branch.style.setProperty('--org-weight', String(Math.max(1, children.length)));
  if (children.length) {
    const childArea = document.createElement('div');
    const horizontalLevel = isRoot || ['executive','group','division','center'].includes(item.type);
    childArea.className = `org-children${isRoot ? ' org-top-level' : horizontalLevel ? ' org-horizontal-level' : ''}`;
    childArea.dataset.parentType = item.type;
    childArea.style.setProperty('--org-child-count', String(children.length));
    children.forEach(child => childArea.append(createOrgBranch(child)));
    branch.append(childArea);
  }
  return branch;
}

function scheduleOrganizationConnectors() {
  cancelAnimationFrame(connectorFrame);
  connectorFrame = requestAnimationFrame(() => {
    connectorFrame = requestAnimationFrame(drawOrganizationConnectors);
  });
}

function drawOrganizationConnectors() {
  const chart = $('orgChart');
  const svg = chart.querySelector('.org-connectors');
  if (!svg) return;
  const chartRect = chart.getBoundingClientRect();
  const width = Math.max(1, chart.scrollWidth, chart.clientWidth);
  const height = Math.max(1, chart.scrollHeight, chart.clientHeight);
  svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
  svg.setAttribute('width', String(width));
  svg.setAttribute('height', String(height));
  svg.replaceChildren();

  const cardRect = card => {
    const rect = card.getBoundingClientRect();
    return {
      left:rect.left-chartRect.left,
      right:rect.right-chartRect.left,
      top:rect.top-chartRect.top,
      bottom:rect.bottom-chartRect.top,
      centerX:rect.left-chartRect.left+rect.width/2,
      centerY:rect.top-chartRect.top+rect.height/2
    };
  };
  const cardRects = new Map();
  chart.querySelectorAll('.org-node[data-org-id]').forEach(card => cardRects.set(card.dataset.orgId,cardRect(card)));
  const obstaclePadding = 7;
  const obstacles = [...cardRects.values()].map(rect => ({
    left:rect.left-obstaclePadding,
    right:rect.right+obstaclePadding,
    top:rect.top-obstaclePadding,
    bottom:rect.bottom+obstaclePadding
  }));
  const edges = [];
  const align=value=>Math.round(value*2)/2;
  organization.forEach(parent => childrenOf(parent.id).forEach(child => {
    const from=cardRects.get(parent.id),to=cardRects.get(child.id);
    if(!from||!to)return;
    const dx=to.centerX-from.centerX,dy=to.centerY-from.centerY;
    let fromPoint,startPoint,endPoint,toPoint;
    if(Math.abs(dy)>=Math.abs(dx)){
      const downward=dy>=0;
      fromPoint={x:from.centerX,y:downward?from.bottom:from.top};
      startPoint={x:from.centerX,y:(downward?from.bottom+10:from.top-10)};
      endPoint={x:to.centerX,y:(downward?to.top-10:to.bottom+10)};
      toPoint={x:to.centerX,y:downward?to.top:to.bottom};
    }else{
      const rightward=dx>=0;
      fromPoint={x:rightward?from.right:from.left,y:from.centerY};
      startPoint={x:(rightward?from.right+10:from.left-10),y:from.centerY};
      endPoint={x:(rightward?to.left-10:to.right+10),y:to.centerY};
      toPoint={x:rightward?to.left:to.right,y:to.centerY};
    }
    edges.push({
      owner:parent.id,
      from:{x:align(fromPoint.x),y:align(fromPoint.y)},
      start:{x:align(startPoint.x),y:align(startPoint.y)},
      end:{x:align(endPoint.x),y:align(endPoint.y)},
      to:{x:align(toPoint.x),y:align(toPoint.y)}
    });
  }));
  edges.sort((a,b)=>a.from.y-b.from.y||a.from.x-b.from.x||a.to.x-b.to.x);
  const grid=buildConnectorGrid(width,height,obstacles,edges);
  const occupied=[];

  edges.forEach(edge => {
    const routed=routeOrganizationConnector(edge.start,edge.end,grid,obstacles,occupied,edge.owner)
      ??fallbackOrganizationConnector(edge.start,edge.end,obstacles,occupied,edge.owner);
    const points=compressConnectorPoints([edge.from,...routed,edge.to]);
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    line.setAttribute('d',connectorPathData(points));
    line.setAttribute('class','org-connector-line');
    line.dataset.owner=edge.owner;
    svg.append(line);
    connectorSegments(points).forEach(segment=>occupied.push({...segment,owner:edge.owner}));
  });
}

function buildConnectorGrid(width,height,obstacles,edges) {
  const round=value=>Math.round(value*2)/2;
  const xs=new Set([4,round(width-4)]),ys=new Set([4,round(height-4)]);
  obstacles.forEach(rect=>{
    [rect.left-8,rect.left,rect.right,rect.right+8].forEach(value=>xs.add(round(Math.max(4,Math.min(width-4,value)))));
    [rect.top-8,rect.top,rect.bottom,rect.bottom+8].forEach(value=>ys.add(round(Math.max(4,Math.min(height-4,value)))));
  });
  edges.forEach(edge=>{
    [edge.start.x,edge.end.x].forEach(value=>xs.add(round(value)));
    [edge.start.y,edge.end.y].forEach(value=>ys.add(round(value)));
  });
  const addMidpoints=set=>{
    const values=[...set].sort((a,b)=>a-b);
    for(let index=1;index<values.length;index++)
      if(values[index]-values[index-1]>12)set.add(round((values[index]+values[index-1])/2));
  };
  addMidpoints(xs);addMidpoints(ys);
  const x=[...xs].sort((a,b)=>a-b),y=[...ys].sort((a,b)=>a-b);
  return {
    x,y,
    xIndex:new Map(x.map((value,index)=>[connectorCoordinateKey(value),index])),
    yIndex:new Map(y.map((value,index)=>[connectorCoordinateKey(value),index]))
  };
}

function routeOrganizationConnector(start,end,grid,obstacles,occupied,owner) {
  const startX=grid.xIndex.get(connectorCoordinateKey(start.x));
  const startY=grid.yIndex.get(connectorCoordinateKey(start.y));
  const endX=grid.xIndex.get(connectorCoordinateKey(end.x));
  const endY=grid.yIndex.get(connectorCoordinateKey(end.y));
  if([startX,startY,endX,endY].some(value=>value===undefined))return null;
  const stateKey=(x,y,direction)=>`${x},${y},${direction}`;
  const heap=[],costs=new Map(),previous=new Map();
  const firstKey=stateKey(startX,startY,0);
  costs.set(firstKey,0);
  connectorHeapPush(heap,{x:startX,y:startY,direction:0,g:0,f:Math.abs(start.x-end.x)+Math.abs(start.y-end.y),key:firstKey});
  let goal=null,iterations=0;
  const moves=[[1,0,1],[-1,0,1],[0,1,2],[0,-1,2]];
  while(heap.length&&iterations++<80000){
    const current=connectorHeapPop(heap);
    if(current.g!==costs.get(current.key))continue;
    if(current.x===endX&&current.y===endY){goal=current;break;}
    for(const [dx,dy,direction] of moves){
      const nx=current.x+dx,ny=current.y+dy;
      if(nx<0||ny<0||nx>=grid.x.length||ny>=grid.y.length)continue;
      const from={x:grid.x[current.x],y:grid.y[current.y]},to={x:grid.x[nx],y:grid.y[ny]};
      if(!connectorSegmentIsClear(from,to,obstacles))continue;
      const distance=Math.abs(to.x-from.x)+Math.abs(to.y-from.y);
      const bend=current.direction&&current.direction!==direction?18:0;
      const upward=to.y<from.y&&end.y>=start.y?distance*2+70:0;
      const collision=connectorSegmentPenalty(from,to,occupied,owner);
      const nextCost=current.g+distance+bend+upward+collision;
      const key=stateKey(nx,ny,direction);
      if(nextCost>=(costs.get(key)??Infinity))continue;
      costs.set(key,nextCost);previous.set(key,current.key);
      const heuristic=Math.abs(to.x-end.x)+Math.abs(to.y-end.y);
      connectorHeapPush(heap,{x:nx,y:ny,direction,g:nextCost,f:nextCost+heuristic,key});
    }
  }
  if(!goal)return null;
  const points=[];
  let key=goal.key;
  while(key){
    const [x,y]=key.split(',').map(Number);
    points.push({x:grid.x[x],y:grid.y[y]});
    key=previous.get(key);
  }
  return compressConnectorPoints(points.reverse());
}

function fallbackOrganizationConnector(start,end,obstacles,occupied,owner) {
  const middleY=start.y+(end.y-start.y)/2;
  const options=[
    [start,{x:start.x,y:middleY},{x:end.x,y:middleY},end],
    [start,{x:start.x,y:end.y},{x:end.x,y:end.y},end]
  ];
  const score=points=>connectorSegments(points).reduce((total,segment)=>
    total+(connectorSegmentIsClear(segment.a,segment.b,obstacles)?0:100000)
      +connectorSegmentPenalty(segment.a,segment.b,occupied,owner),0);
  return options.sort((a,b)=>score(a)-score(b))[0];
}

function connectorSegmentIsClear(a,b,obstacles) {
  const epsilon=.1;
  if(Math.abs(a.y-b.y)<epsilon){
    const left=Math.min(a.x,b.x),right=Math.max(a.x,b.x),y=a.y;
    return !obstacles.some(rect=>y>rect.top+epsilon&&y<rect.bottom-epsilon
      &&Math.max(left,rect.left)<Math.min(right,rect.right)-epsilon);
  }
  const top=Math.min(a.y,b.y),bottom=Math.max(a.y,b.y),x=a.x;
  return !obstacles.some(rect=>x>rect.left+epsilon&&x<rect.right-epsilon
    &&Math.max(top,rect.top)<Math.min(bottom,rect.bottom)-epsilon);
}

function connectorSegmentPenalty(a,b,occupied,owner) {
  const horizontal=Math.abs(a.y-b.y)<.1;
  const aMin=horizontal?Math.min(a.x,b.x):Math.min(a.y,b.y);
  const aMax=horizontal?Math.max(a.x,b.x):Math.max(a.y,b.y);
  let penalty=0;
  occupied.forEach(segment=>{
    const sameOwner=segment.owner===owner;
    const otherHorizontal=Math.abs(segment.a.y-segment.b.y)<.1;
    if(horizontal===otherHorizontal){
      if(sameOwner)return;
      const distance=horizontal?Math.abs(a.y-segment.a.y):Math.abs(a.x-segment.a.x);
      const bMin=horizontal?Math.min(segment.a.x,segment.b.x):Math.min(segment.a.y,segment.b.y);
      const bMax=horizontal?Math.max(segment.a.x,segment.b.x):Math.max(segment.a.y,segment.b.y);
      const overlap=Math.max(0,Math.min(aMax,bMax)-Math.max(aMin,bMin));
      if(overlap>0)penalty+=distance<.5?5000+overlap*25:distance<7?700+overlap*4:0;
      return;
    }
    const horizontalSegment=horizontal?{a,b}:segment;
    const verticalSegment=horizontal?segment:{a,b};
    const hx1=Math.min(horizontalSegment.a.x,horizontalSegment.b.x),hx2=Math.max(horizontalSegment.a.x,horizontalSegment.b.x);
    const vy1=Math.min(verticalSegment.a.y,verticalSegment.b.y),vy2=Math.max(verticalSegment.a.y,verticalSegment.b.y);
    const crossX=verticalSegment.a.x,crossY=horizontalSegment.a.y;
    if(crossX>=hx1-.1&&crossX<=hx2+.1&&crossY>=vy1-.1&&crossY<=vy2+.1)penalty+=sameOwner?260:1200;
  });
  return penalty;
}

function connectorSegments(points) {
  const segments=[];
  for(let index=1;index<points.length;index++)
    if(Math.abs(points[index-1].x-points[index].x)>.1||Math.abs(points[index-1].y-points[index].y)>.1)
      segments.push({a:points[index-1],b:points[index]});
  return segments;
}

function compressConnectorPoints(points) {
  const unique=points.filter((point,index)=>!index||Math.abs(point.x-points[index-1].x)>.1||Math.abs(point.y-points[index-1].y)>.1);
  if(unique.length<3)return unique;
  const result=[unique[0]];
  for(let index=1;index<unique.length-1;index++){
    const previous=result[result.length-1],current=unique[index],next=unique[index+1];
    const collinear=(Math.abs(previous.x-current.x)<.1&&Math.abs(current.x-next.x)<.1)
      ||(Math.abs(previous.y-current.y)<.1&&Math.abs(current.y-next.y)<.1);
    if(!collinear)result.push(current);
  }
  result.push(unique.at(-1));
  return result;
}

function connectorPathData(points) {
  if(!points.length)return '';
  return points.slice(1).reduce((data,point,index)=>{
    const previous=points[index];
    return `${data} ${Math.abs(previous.x-point.x)<.1?'V '+point.y:Math.abs(previous.y-point.y)<.1?'H '+point.x:`L ${point.x} ${point.y}`}`;
  },`M ${points[0].x} ${points[0].y}`);
}

function connectorCoordinateKey(value){return (Math.round(value*2)/2).toFixed(1);}

function connectorHeapPush(heap,item){
  heap.push(item);
  let index=heap.length-1;
  while(index>0){
    const parent=(index-1)>>1;
    if(heap[parent].f<=item.f)break;
    heap[index]=heap[parent];index=parent;
  }
  heap[index]=item;
}

function connectorHeapPop(heap){
  const first=heap[0],last=heap.pop();
  if(heap.length){
    let index=0;
    while(true){
      let child=index*2+1;
      if(child>=heap.length)break;
      if(child+1<heap.length&&heap[child+1].f<heap[child].f)child++;
      if(heap[child].f>=last.f)break;
      heap[index]=heap[child];index=child;
    }
    heap[index]=last;
  }
  return first;
}

function bindOrganizationFreeDrag(card,item){
  if(!organizationFreeLayoutMode)return;
  card.classList.add('org-node-free-move');
  card.addEventListener('pointerdown',event=>{
    if(event.button!==0)return;
    const chart=$('orgChart'),chartRect=chart.getBoundingClientRect(),cardRect=card.getBoundingClientRect();
    const originX=Number(item.layoutX)||0,originY=Number(item.layoutY)||0;
    const bounds={
      minX:originX+chartRect.left+4-cardRect.left,
      maxX:originX+chartRect.right-4-cardRect.right,
      minY:originY+chartRect.top+4-cardRect.top,
      maxY:originY+chartRect.bottom-4-cardRect.bottom
    };
    const startX=event.clientX,startY=event.clientY;
    let moved=false;
    card.setPointerCapture(event.pointerId);
    card.classList.add('org-node-free-dragging');
    const move=moveEvent=>{
      const dx=moveEvent.clientX-startX,dy=moveEvent.clientY-startY;
      if(Math.abs(dx)+Math.abs(dy)>2)moved=true;
      item.layoutX=Math.round(Math.max(bounds.minX,Math.min(bounds.maxX,originX+dx)));
      item.layoutY=Math.round(Math.max(bounds.minY,Math.min(bounds.maxY,originY+dy)));
      card.style.translate=`${item.layoutX}px ${item.layoutY}px`;
      scheduleOrganizationConnectors();
      moveEvent.preventDefault();
    };
    const finish=finishEvent=>{
      card.removeEventListener('pointermove',move);
      card.removeEventListener('pointerup',finish);
      card.removeEventListener('pointercancel',finish);
      card.classList.remove('org-node-free-dragging');
      if(card.hasPointerCapture(finishEvent.pointerId))card.releasePointerCapture(finishEvent.pointerId);
      if(moved){
        dragJustEnded=true;
        markOrganizationDirty(`‘${item.name}’ 조직의 화면 위치를 변경했습니다.`);
        setTimeout(()=>dragJustEnded=false,0);
      }
    };
    card.addEventListener('pointermove',move);
    card.addEventListener('pointerup',finish);
    card.addEventListener('pointercancel',finish);
  });
}

function bindOrganizationDrag(card, item, isRoot) {
  card.draggable = organizationEditMode && !isRoot;
  if (!card.draggable) return;
  card.addEventListener('dragstart', event => {
    draggedOrgId = item.id;
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', item.id);
    requestAnimationFrame(() => card.classList.add('org-node-dragging'));
  });
  card.addEventListener('dragover', event => {
    const mode = dropModeFor(card, event, item);
    if (!mode || !canMoveOrganization(draggedOrgId, item.id, mode)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    clearDropIndicators();
    card.classList.add(`org-drop-${mode}`);
    card.dataset.dropMode = mode;
  });
  card.addEventListener('dragleave', event => {
    if (!card.contains(event.relatedTarget)) clearDropIndicators();
  });
  card.addEventListener('drop', event => {
    event.preventDefault();
    const mode = card.dataset.dropMode || dropModeFor(card, event, item);
    const sourceId = draggedOrgId;
    clearDropIndicators();
    if (canMoveOrganization(sourceId, item.id, mode)) moveOrganization(sourceId, item.id, mode);
    draggedOrgId = null;
    dragJustEnded = true;
    setTimeout(() => dragJustEnded = false, 0);
  });
  card.addEventListener('dragend', () => {
    card.classList.remove('org-node-dragging');
    clearDropIndicators();
    draggedOrgId = null;
    dragJustEnded = true;
    setTimeout(() => dragJustEnded = false, 0);
  });
}

function dropModeFor(card, event, target) {
  if (!draggedOrgId || draggedOrgId === target.id) return null;
  const rect = card.getBoundingClientRect();
  const ratio = (event.clientX - rect.left) / Math.max(1, rect.width);
  if (target.parentId !== null && ratio < .24) return 'before';
  if (target.parentId !== null && ratio > .76) return 'after';
  return 'child';
}

function canMoveOrganization(sourceId, targetId, mode) {
  if (!sourceId || !targetId || !mode || sourceId === targetId) return false;
  const source = organization.find(item => item.id === sourceId);
  const target = organization.find(item => item.id === targetId);
  if (!source || !target || source.parentId === null) return false;
  const descendants = descendantIds(sourceId);
  const newParentId = mode === 'child' ? targetId : target.parentId;
  return newParentId !== null && newParentId !== sourceId && !descendants.has(newParentId);
}

function moveOrganization(sourceId, targetId, mode) {
  const source = organization.find(item => item.id === sourceId);
  const target = organization.find(item => item.id === targetId);
  if (!source || !target) return;
  const oldParentId = source.parentId;
  const newParentId = mode === 'child' ? target.id : target.parentId;
  if (newParentId === null) return;
  const siblings = childrenOf(newParentId).filter(item => item.id !== sourceId);
  let insertIndex = siblings.length;
  if (mode !== 'child') {
    const targetIndex = siblings.findIndex(item => item.id === targetId);
    insertIndex = Math.max(0, targetIndex + (mode === 'after' ? 1 : 0));
  }
  source.parentId = newParentId;
  source.layoutX=0;
  source.layoutY=0;
  siblings.splice(insertIndex, 0, source);
  siblings.forEach((item, index) => item.order = index);
  if (oldParentId !== newParentId) normalizeSiblingOrders(oldParentId);
  renderOrganization();
  markOrganizationDirty(`‘${source.name}’ 조직의 위치를 변경했습니다.`);
}

function clearDropIndicators() {
  document.querySelectorAll('.org-drop-before,.org-drop-after,.org-drop-child').forEach(node => {
    node.classList.remove('org-drop-before','org-drop-after','org-drop-child');
    delete node.dataset.dropMode;
  });
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
  if (!canEditOrganization) return;
  editingOrgId = id;
  const item = id ? organization.find(candidate => candidate.id === id) : null;
  $('orgDialogTitle').textContent = item ? '조직 정보 수정' : '새 조직 추가';
  $('orgName').value = item?.name || '';
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
  if (!canEditOrganization) return;
  organizationDirty = true;
  $('orgStatus').textContent = message;
  $('orgStatus').classList.add('is-dirty');
  $('orgSaveBtn').classList.add('has-changes');
  $('orgCancelBtn').disabled = false;
  $('sideOrgStatus').textContent = '저장 필요';
}

$('orgForm').addEventListener('submit', event => {
  event.preventDefault();
  if (!canEditOrganization) return;
  const name = $('orgName').value.trim();
  if (!name) {
    $('orgFormError').textContent = '조직명을 입력해 주세요.';
    return;
  }
  if (editingOrgId) {
    const item = organization.find(candidate => candidate.id === editingOrgId);
    const oldParentId = item.parentId;
    const newParentId = item.parentId === null ? null : $('orgParent').value;
    item.name = name;
    if (item.parentId !== null && oldParentId !== newParentId) {
      item.parentId = newParentId;
      item.order = childrenOf(newParentId).filter(candidate => candidate.id !== item.id).length;
      item.layoutX=0;
      item.layoutY=0;
      normalizeSiblingOrders(oldParentId);
      normalizeSiblingOrders(newParentId);
    }
  } else {
    const parentId = $('orgParent').value;
    organization.push({
      id: `org-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      name,
      type: 'team',
      parentId,
      order: childrenOf(parentId).length
    });
  }
  $('orgDialog').close();
  renderOrganization();
  markOrganizationDirty();
});

$('orgDeleteBtn').addEventListener('click', () => {
  if (!canEditOrganization) return;
  const item = organization.find(candidate => candidate.id === editingOrgId);
  if (!item || item.parentId === null) return;
  const descendants = descendantIds(item.id);
  const detail = descendants.size ? ` 하위 조직 ${descendants.size}개도 함께 삭제됩니다.` : '';
  if (!confirm(`‘${item.name}’ 조직을 삭제하시겠습니까?${detail}`)) return;
  const oldParentId = item.parentId;
  descendants.add(item.id);
  organization = organization.filter(candidate => !descendants.has(candidate.id));
  normalizeSiblingOrders(oldParentId);
  $('orgDialog').close();
  renderOrganization();
  markOrganizationDirty();
});

$('orgAddBtn').addEventListener('click', () => {
  if (canEditOrganization) openOrgDialog();
});
$('orgSaveBtn').addEventListener('click', () => {
  if (!canEditOrganization) return;
  try {
    localStorage.setItem(ORG_STORAGE_KEY, JSON.stringify(organization));
    savedOrganization = cloneOrganization(organization);
    organizationDirty = false;
    $('orgStatus').textContent = `저장 완료 · ${new Date().toLocaleString('ko-KR')}`;
    $('orgStatus').classList.remove('is-dirty');
    $('orgSaveBtn').classList.remove('has-changes');
    $('orgCancelBtn').disabled = true;
    $('sideOrgStatus').textContent = `${organization.length}개 조직 저장됨`;
  } catch (_) {
    $('orgStatus').textContent = '브라우저 저장소에 저장하지 못했습니다. 내보내기로 백업해 주세요.';
    $('orgStatus').classList.add('is-dirty');
  }
});

$('orgCancelBtn').addEventListener('click', () => {
  if (!canEditOrganization || !organizationDirty) return;
  if (!confirm('저장하지 않은 조직도 변경사항을 취소하시겠습니까?')) return;
  organization = cloneOrganization(savedOrganization);
  organizationDirty = false;
  renderOrganization();
  $('orgStatus').textContent = '저장된 조직도로 되돌렸습니다.';
  $('orgStatus').classList.remove('is-dirty');
  $('orgSaveBtn').classList.remove('has-changes');
  $('orgCancelBtn').disabled = true;
  $('sideOrgStatus').textContent = `${organization.length}개 조직 불러옴`;
});

$('orgLayoutBtn').addEventListener('click', () => {
  if (!canEditOrganization) return;
  organizationEditMode = !organizationEditMode;
  organizationFreeLayoutMode = false;
  $('orgLayoutBtn').classList.toggle('is-active', organizationEditMode);
  $('orgLayoutBtn').setAttribute('aria-pressed', String(organizationEditMode));
  $('orgLayoutBtn').textContent = organizationEditMode ? '소속·순서 편집 종료' : '소속·순서 편집';
  $('orgFreeLayoutBtn').classList.remove('is-active');
  $('orgFreeLayoutBtn').setAttribute('aria-pressed','false');
  $('orgFreeLayoutBtn').textContent='자유 배치';
  renderOrganization();
  updateOrganizationGuide();
});

$('orgFreeLayoutBtn').addEventListener('click',()=>{
  if(!canEditOrganization)return;
  organizationFreeLayoutMode=!organizationFreeLayoutMode;
  organizationEditMode=false;
  $('orgFreeLayoutBtn').classList.toggle('is-active',organizationFreeLayoutMode);
  $('orgFreeLayoutBtn').setAttribute('aria-pressed',String(organizationFreeLayoutMode));
  $('orgFreeLayoutBtn').textContent=organizationFreeLayoutMode?'자유 배치 종료':'자유 배치';
  $('orgLayoutBtn').classList.remove('is-active');
  $('orgLayoutBtn').setAttribute('aria-pressed','false');
  $('orgLayoutBtn').textContent='소속·순서 편집';
  renderOrganization();
  updateOrganizationGuide();
});

$('orgPositionResetBtn').addEventListener('click',()=>{
  if(!canEditOrganization)return;
  if(!organization.some(item=>(Number(item.layoutX)||0)!==0||(Number(item.layoutY)||0)!==0))return;
  if(!confirm('모든 조직 박스를 자동 배치 위치로 되돌리시겠습니까?'))return;
  organization.forEach(item=>{item.layoutX=0;item.layoutY=0;});
  renderOrganization();
  markOrganizationDirty('모든 조직 박스 위치를 자동 배치 상태로 되돌렸습니다.');
});

$('orgResetBtn').addEventListener('click', () => {
  if (!canEditOrganization) return;
  if (!confirm('처음 전달받은 조직 구성으로 되돌리시겠습니까? 저장 버튼을 누르기 전까지는 확정되지 않습니다.')) return;
  organization = normalizeOrganization(cloneOrganization(DEFAULT_ORGANIZATION));
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

$('orgImportBtn').addEventListener('click', () => {
  if (canEditOrganization) $('orgFileInput').click();
});
$('orgFileInput').addEventListener('change', async event => {
  if (!canEditOrganization) {
    event.target.value = '';
    return;
  }
  const file = event.target.files[0];
  if (!file) return;
  try {
    const imported = JSON.parse(await file.text());
    if (!validateOrganization(imported)) throw new Error('올바른 조직도 형식이 아닙니다.');
    organization = normalizeOrganization(imported);
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

function applyOrganizationPermissions() {
  ['orgImportBtn','orgResetBtn','orgCancelBtn','orgPositionResetBtn','orgLayoutBtn','orgFreeLayoutBtn','orgAddBtn','orgSaveBtn'].forEach(id => {
    $(id).hidden = !canEditOrganization;
  });
  document.querySelector('.topbar .subtitle').textContent = canEditOrganization
    ? '조직 구조를 확인하고 변경사항을 직접 관리합니다.'
    : '조직 구조를 확인할 수 있습니다.';
  document.querySelector('.org-toolbar strong').textContent = canEditOrganization ? '조직도 관리' : '조직도';
  updateOrganizationGuide();
  $('orgStatus').textContent = '저장된 조직도를 불러왔습니다.';
  $('orgCancelBtn').disabled = true;
  $('sideOrgStatus').textContent = `${organization.length}개 조직 불러옴`;
}

function updateOrganizationGuide() {
  document.querySelector('.org-guide > span:first-child').textContent = !canEditOrganization
    ? '조직도 변경은 HR 관리자 또는 관리자만 가능합니다.'
    : organizationFreeLayoutMode
      ? '조직 박스를 원하는 위치로 드래그하세요. 연결선은 이동 중 자동으로 다시 계산됩니다.'
      : organizationEditMode
      ? '카드 중앙에 놓으면 하위 조직으로, 좌우 가장자리에 놓으면 같은 단계 순서로 이동합니다.'
      : '조직 정보 수정, 소속·순서 편집 또는 자유 배치를 선택할 수 있습니다.';
}

async function initializeOrganization() {
  try {
    const response = await fetch('/api/session');
    if (response.status === 401) {
      location.replace('/login');
      return;
    }
    if (response.ok) {
      const session = await response.json();
      canEditOrganization = Boolean(session.canEdit);
      $('sessionUser').textContent = session.userName || '로그인 사용자';
      if (session.theme) window.setDashboardTheme(session.theme);
    }
  } catch (_) {
    canEditOrganization = false;
  }
  applyOrganizationPermissions();
  renderOrganization();
}

$('logoutBtn').onclick = async () => {
  await fetch('/api/auth/logout', { method:'POST' });
  location.replace('/login');
};

window.addEventListener('resize', scheduleOrganizationConnectors);
if ('ResizeObserver' in window) new ResizeObserver(scheduleOrganizationConnectors).observe($('orgChart'));

initializeOrganization();
