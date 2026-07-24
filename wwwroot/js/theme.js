(() => {
  const key = 'hr-dashboard-theme';
  const systemDark = window.matchMedia('(prefers-color-scheme: dark)');
  const saved = localStorage.getItem(key);
  const apply = theme => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;
    const button = document.getElementById('themeToggle');
    if (button) {
      const dark = theme === 'dark';
      button.textContent = dark ? '☀' : '☾';
      button.title = dark ? '라이트 모드로 전환' : '다크 모드로 전환';
      button.setAttribute('aria-label', button.title);
      button.setAttribute('aria-pressed', String(dark));
    }
  };
  apply(saved || (systemDark.matches ? 'dark' : 'light'));
  document.addEventListener('DOMContentLoaded', () => {
    const button = document.createElement('button');
    button.id = 'themeToggle';
    button.className = 'theme-toggle';
    button.type = 'button';
    button.addEventListener('click', () => {
      const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
      localStorage.setItem(key, next);
      apply(next);
    });
    const topbar = document.querySelector('.topbar');
    if (topbar) {
      const right = topbar.lastElementChild;
      if (right) {
        const group = document.createElement('div');
        group.className = 'topbar-actions';
        right.replaceWith(group);
        group.append(button, right);
      } else topbar.append(button);
    } else {
      button.classList.add('theme-toggle-floating');
      document.body.append(button);
    }
    apply(document.documentElement.dataset.theme);
  });
  systemDark.addEventListener('change', event => {
    if (!localStorage.getItem(key)) apply(event.matches ? 'dark' : 'light');
  });
})();
