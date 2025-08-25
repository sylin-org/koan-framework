// Small DOM helpers for S5.Recs (classic script)
(function(){
  function $(id){ return document.getElementById(id); }
  function val(id){ const el = $(id); return el ? el.value : undefined; }
  function setVal(id, v){ const el = $(id); if(el) el.value = v; }
  function text(id, t){ const el = $(id); if(el) el.textContent = t; }
  function on(id, ev, fn){ const el = $(id); if(el) el.addEventListener(ev, fn); }
  function show(el){ if(el) el.classList.remove('hidden'); }
  function hide(el){ if(el) el.classList.add('hidden'); }
  function toggleHidden(el){ if(el) el.classList.toggle('hidden'); }
  function clearValues(ids){ (ids||[]).forEach(i => setVal(i, '')); }
  window.Dom = { $, val, setVal, text, on, show, hide, toggleHidden, clearValues };
})();
