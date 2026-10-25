(function () {
  const root = document.documentElement;
  const nav = document.getElementById('nav');
  const burger = document.getElementById('burger');
  const links = document.querySelector('.links');
  const themeBtn = document.getElementById('theme');
  const year = document.getElementById('year');

  // Theme: follow the OS until the user picks one.
  const stored = localStorage.getItem('wiina-theme');
  const queryTheme = new URLSearchParams(location.search).get('theme');
  const prefersLight = window.matchMedia('(prefers-color-scheme: light)');
  const initial = (queryTheme === 'light' || queryTheme === 'dark')
    ? queryTheme
    : (stored || (prefersLight.matches ? 'light' : 'dark'));
  root.dataset.theme = initial;

  themeBtn?.addEventListener('click', () => {
    const next = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next;
    localStorage.setItem('wiina-theme', next);
  });

  prefersLight.addEventListener('change', (event) => {
    if (localStorage.getItem('wiina-theme')) return;
    root.dataset.theme = event.matches ? 'light' : 'dark';
  });

  // Sticky nav border
  const onScroll = () => nav.classList.toggle('scrolled', window.scrollY > 8);
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  // Mobile menu
  burger?.addEventListener('click', () => {
    const open = links.classList.toggle('open');
    burger.setAttribute('aria-expanded', String(open));
  });
  links?.addEventListener('click', (event) => {
    if (event.target.closest('a')) {
      links.classList.remove('open');
      burger?.setAttribute('aria-expanded', 'false');
    }
  });

  // Reveal sections
  const targets = document.querySelectorAll('.section, .feature, .card, .hero-shot');
  if ('IntersectionObserver' in window) {
    const io = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add('in');
          io.unobserve(entry.target);
        }
      }
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    targets.forEach((el) => {
      el.classList.add('reveal');
      io.observe(el);
    });
  }

  if (year) year.textContent = new Date().getFullYear();
})();
