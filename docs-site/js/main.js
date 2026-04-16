/* ll-lang docs — main.js */

// ── Mobile nav toggle ────────────────────────────────────────────────────────
(function () {
  const hamburger = document.getElementById("nav-hamburger");
  const navLinks = document.getElementById("nav-links");
  if (!hamburger || !navLinks) return;

  hamburger.addEventListener("click", () => {
    const open = navLinks.classList.toggle("open");
    hamburger.setAttribute("aria-expanded", open);
  });

  // Close on outside click
  document.addEventListener("click", (e) => {
    if (!hamburger.contains(e.target) && !navLinks.contains(e.target)) {
      navLinks.classList.remove("open");
      hamburger.setAttribute("aria-expanded", false);
    }
  });
})();

// ── Copy buttons ─────────────────────────────────────────────────────────────
(function () {
  document.querySelectorAll(".copy-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      const block = btn.closest(".code-block");
      const code = block ? block.querySelector("code") : null;
      if (!code) return;

      navigator.clipboard
        .writeText(code.innerText)
        .then(() => {
          const orig = btn.textContent;
          btn.textContent = "copied!";
          setTimeout(() => (btn.textContent = orig), 1800);
        })
        .catch(() => {
          // clipboard API unavailable — select text as fallback
          const range = document.createRange();
          range.selectNodeContents(code);
          const sel = window.getSelection();
          sel.removeAllRanges();
          sel.addRange(range);
        });
    });
  });
})();

// ── Active nav link ──────────────────────────────────────────────────────────
(function () {
  const page = location.pathname.split("/").pop() || "index.html";
  document.querySelectorAll(".nav-links a").forEach((a) => {
    const href = a.getAttribute("href");
    if (href === page || (page === "" && href === "index.html")) {
      a.classList.add("active");
    }
  });
})();

// ── Scroll-based nav shadow ──────────────────────────────────────────────────
(function () {
  const nav = document.querySelector("nav");
  if (!nav) return;
  const onScroll = () => {
    nav.style.boxShadow =
      window.scrollY > 10 ? "0 1px 24px rgba(0,0,0,.5)" : "";
  };
  window.addEventListener("scroll", onScroll, { passive: true });
})();
