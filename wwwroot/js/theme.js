(() => {
  const key='hr-dashboard-theme';
  const sidebarKey='hr-dashboard-sidebar-collapsed';
  const systemDark=window.matchMedia('(prefers-color-scheme: dark)');
  const apply=theme=>{
    const normalized=theme==='dark'?'dark':'light';
    document.documentElement.dataset.theme=normalized;
    document.documentElement.style.colorScheme=normalized;
    document.querySelectorAll('[data-theme-choice]').forEach(button=>{
      const active=button.dataset.themeChoice===normalized;
      button.classList.toggle('active',active);
      button.setAttribute('aria-pressed',String(active));
    });
  };
  window.setDashboardTheme=theme=>{
    localStorage.setItem(key,theme);
    apply(theme);
  };
  apply(localStorage.getItem(key)||(systemDark.matches?'dark':'light'));
  systemDark.addEventListener('change',event=>{
    if(!localStorage.getItem(key))apply(event.matches?'dark':'light');
  });

  const applySidebar=collapsed=>{
    document.documentElement.classList.toggle('sidebar-is-collapsed',collapsed);
    const button=document.querySelector('.sidebar-toggle');
    if(button){
      button.setAttribute('aria-expanded',String(!collapsed));
      button.setAttribute('aria-label',collapsed?'Workspace 사이드바 열기':'Workspace 사이드바 닫기');
      button.title=collapsed?'사이드바 열기':'사이드바 닫기';
    }
  };
  applySidebar(localStorage.getItem(sidebarKey)==='1');
  const setupSidebar=()=>{
    const sidebar=document.querySelector('.sidebar');
    if(!sidebar||document.querySelector('.sidebar-toggle'))return;
    const button=document.createElement('button');
    button.className='sidebar-toggle';
    button.type='button';
    button.innerHTML='<span></span><span></span><span></span>';
    sidebar.append(button);
    sidebar.querySelectorAll('.nav-item').forEach(item=>item.title=item.textContent.trim());
    button.onclick=()=>{
      const collapsed=!document.documentElement.classList.contains('sidebar-is-collapsed');
      localStorage.setItem(sidebarKey,collapsed?'1':'0');
      applySidebar(collapsed);
    };
    applySidebar(document.documentElement.classList.contains('sidebar-is-collapsed'));
  };
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',setupSidebar);
  else setupSidebar();
})();
