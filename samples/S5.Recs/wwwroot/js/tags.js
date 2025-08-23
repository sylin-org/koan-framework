// S5.Recs preferred-tags UI helpers (classic script)
// Depends on globals: selectedPreferredTags, recsSettings, preferTagsCatalog, allTags,
// and functions: loadAnimeData, showToast, displayAnime
(function(){
  async function loadTags(){
    try{
      if(!(window.S5Api && typeof window.S5Api.getTags === 'function')) return;
      const list = await window.S5Api.getTags('popularity') || [];
      // allTags expects shape: [{ tag, count }]
      window.allTags = Array.isArray(list) ? list : [];
      // Prefer chips: top N by popularity
      const top = (window.allTags || []).slice(0, 16).map(x => x.tag).filter(Boolean);
      window.preferTagsCatalog = top;
      renderPreferredChips();
      // If panel is open, render it too
      const panel = document.getElementById('allTagsPanel');
      if(panel && !panel.classList.contains('hidden')){ renderAllTags(); }
    }catch{}
  }
  function renderPreferredChips(){
    const host = document.getElementById('preferChips'); if(!host) return;
    const max = (window.recsSettings && window.recsSettings.maxPreferredTags) ?? 3;
    const maxEl = document.getElementById('preferMaxTags'); if(maxEl) maxEl.textContent = String(max);
    const catalog = Array.isArray(window.preferTagsCatalog) ? window.preferTagsCatalog : [];
    const selected = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
    host.innerHTML = catalog.map(tag => {
      const sel = selected.includes(tag);
      const cls = sel ? 'bg-purple-600 text-white' : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
      return `<button type="button" data-tag="${tag}" class="px-2 py-1 text-xs rounded-full border border-slate-700 ${cls}">${tag}</button>`;
    }).join('');
  }

  function renderAllTags(){
    const list = document.getElementById('allTagsList'); if(!list) return;
    const q = (document.getElementById('tagsSearch')?.value || '').toLowerCase();
    const sort = document.getElementById('tagsSort')?.value || 'popularity';
    let items = Array.isArray(window.allTags) ? [...window.allTags] : [];
    if(q){ items = items.filter(x => (x.tag||'').toLowerCase().includes(q)); }
    if(sort === 'alpha'){ items.sort((a,b)=> (a.tag||'').localeCompare(b.tag||'')); }
    else { items.sort((a,b)=> (b.count||0)-(a.count||0) || (a.tag||'').localeCompare(b.tag||'')); }
    const selected = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
    list.innerHTML = items.map(x=>{
      const sel = selected.includes(x.tag);
      const cls = sel ? 'bg-purple-600 text-white' : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
      return `<button type="button" data-tag="${x.tag}" class="px-2 py-1 text-xs rounded-full border border-slate-700 ${cls}">${x.tag} <span class="text-gray-400">(${x.count})</span></button>`;
    }).join('');
  }

  function togglePreferredTag(tag){
    const max = (window.recsSettings && window.recsSettings.maxPreferredTags) ?? 3;
    const arr = window.selectedPreferredTags = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
    const idx = arr.indexOf(tag);
    if(idx >= 0){ arr.splice(idx,1); }
    else {
      if(arr.length >= max){ window.showToast && window.showToast(`Select up to ${max} tags`, 'info'); return; }
      arr.push(tag);
    }
    renderPreferredChips();
    onPreferredChanged();
  }

  function clearPreferred(){
    window.selectedPreferredTags = [];
    renderPreferredChips();
    onPreferredChanged();
  }

  function onPreferredChanged(){
    if(window.currentView !== 'forYou') return;
    const PAGE_SIZE = (window.S5Config && window.S5Config.PAGE_SIZE) || 100;
    window.currentRecsTopK = PAGE_SIZE;
    const btn = document.getElementById('moreBtn'); if(btn) btn.classList.add('hidden');
    window.loadAnimeData && window.loadAnimeData();
  }

  window.S5Tags = { loadTags, renderPreferredChips, renderAllTags, togglePreferredTag, clearPreferred, onPreferredChanged };
  // Back-compat for inline calls and main.js expectations
  window.loadTags = loadTags;
  window.renderPreferredChips = renderPreferredChips;
  window.renderAllTags = renderAllTags;
  window.togglePreferredTag = togglePreferredTag;
  window.clearPreferred = clearPreferred;
  window.onPreferredChanged = onPreferredChanged;
})();
