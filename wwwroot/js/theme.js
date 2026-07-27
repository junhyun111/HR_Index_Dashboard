(() => {
  const key='hr-dashboard-theme';
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
})();
