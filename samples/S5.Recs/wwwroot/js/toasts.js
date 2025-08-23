// Toast utility (classic script) — exposes window.showToast
(function(){
  function showToast(message, type){
    const cont = document.getElementById('toastContainer');
    if(!cont) return;
    const el = document.createElement('div');
    const color = type==='success' ? 'bg-green-600' : type==='error' ? 'bg-red-600' : 'bg-slate-700';
    el.className = `${color} text-white px-4 py-2 rounded-lg shadow-lg flex items-center gap-2 animate-fade-in`;
    const span = document.createElement('span');
    span.textContent = String(message ?? '');
    el.appendChild(span);
    cont.appendChild(el);
    setTimeout(()=>{ el.classList.add('opacity-0'); el.classList.add('transition-opacity'); el.classList.add('duration-300'); }, 2200);
    setTimeout(()=>{ el.remove(); }, 2600);
  }
  window.showToast = showToast;
})();
