// The host marks first launch before first paint. The splash is a one-time intro;
// later launches keep the overlay hidden and do not run its timer.
(function () {
  if (!document.documentElement.classList.contains('awyken-splash-enabled')) return;
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
  var MIN_MS = reduce ? 600 : 1700;
  var t0 = performance.now(), gone = false;

  function remove() {
    var el = document.getElementById('awyken-splash');
    if (el && el.parentNode) el.parentNode.removeChild(el);
  }
  function hide() {
    if (gone) return; gone = true;
    var el = document.getElementById('awyken-splash');
    if (!el) return;
    el.classList.add('ak-exit');
    setTimeout(remove, 380);
  }
  window.awykenSplash = { hide: hide };
  setTimeout(hide, MIN_MS);
})();
