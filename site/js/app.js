/* GamepadEnhancer - site script. Plain JS, no dependencies: page chrome only (theme toggle, nav, copy buttons). */
(function () {
  'use strict';

  function ready(fn) { if (document.readyState !== 'loading') fn(); else document.addEventListener('DOMContentLoaded', fn); }

  ready(function () {
    var root = document.documentElement;

    var toggle = document.getElementById('theme-toggle');
    if (toggle) toggle.addEventListener('click', function () {
      var cur = root.getAttribute('data-theme') || (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
      var next = cur === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      try { localStorage.setItem('vigem-theme', next); } catch (e) { /* storage may be blocked */ }
      document.dispatchEvent(new Event('themechange'));
    });

    var navBtn = document.getElementById('nav-toggle'), navList = document.getElementById('nav-links');
    if (navBtn && navList) navBtn.addEventListener('click', function () {
      var open = navList.classList.toggle('open'); navBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    });

    document.querySelectorAll('pre').forEach(function (pre) {
      if (pre.dataset.nocopy !== undefined) return;
      var b = document.createElement('button');
      b.type = 'button'; b.className = 'btn-small'; b.textContent = 'Copy'; b.setAttribute('aria-label', 'Copy code');
      b.style.cssText = 'position:absolute;top:var(--space-2);right:var(--space-2)';
      b.addEventListener('click', function () {
        var txt = pre.innerText.replace(/\nCopy$/, '');
        var done = function () { b.textContent = 'Copied'; setTimeout(function () { b.textContent = 'Copy'; }, 1400); };
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(txt).then(done, function () {});
      });
      pre.appendChild(b);
    });

    var year = document.getElementById('year'); if (year) year.textContent = new Date().getFullYear();
  });
})();
