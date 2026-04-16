/* ll-lang docs — main.js */

// Mobile nav toggle
(function () {
  var hamburger = document.getElementById('nav-hamburger');
  var navLinks  = document.getElementById('nav-links');
  if (!hamburger || !navLinks) return;
  hamburger.addEventListener('click', function () {
    var open = navLinks.classList.toggle('open');
    hamburger.setAttribute('aria-expanded', open);
  });
  document.addEventListener('click', function (e) {
    if (!hamburger.contains(e.target) && !navLinks.contains(e.target)) {
      navLinks.classList.remove('open');
      hamburger.setAttribute('aria-expanded', false);
    }
  });
})();

// Copy buttons
(function () {
  document.querySelectorAll('.copy-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var block = btn.closest('.code-block');
      var code  = block ? block.querySelector('code') : null;
      if (!code) return;
      navigator.clipboard.writeText(code.innerText).then(function () {
        var orig = btn.textContent;
        btn.textContent = 'copied!';
        setTimeout(function () { btn.textContent = orig; }, 1800);
      }).catch(function () {
        var range = document.createRange();
        range.selectNodeContents(code);
        var sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      });
    });
  });
})();

// Active nav link
(function () {
  var page = location.pathname.split('/').pop() || 'index.html';
  document.querySelectorAll('.nav-links a').forEach(function (a) {
    var href = a.getAttribute('href');
    if (href === page || (page === '' && href === 'index.html')) {
      a.classList.add('active');
    }
  });
})();

// Scroll nav shadow
(function () {
  var nav = document.querySelector('nav');
  if (!nav) return;
  window.addEventListener('scroll', function () {
    nav.style.boxShadow = window.scrollY > 10 ? '0 1px 24px rgba(0,0,0,.5)' : '';
  }, { passive: true });
})();
