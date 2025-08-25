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
  // Prefer chips: top N by popularity, but make sure selected are included even if not in top
  const preferSize = (window.S5Const?.TAGS?.PREFER_CATALOG_SIZE) ?? 16;
  const top = (window.allTags || []).slice(0, preferSize).map(x => x.tag).filter(Boolean);
  const selected = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
  const merged = [...new Set([...(selected||[]), ...top])];
  window.preferTagsCatalog = merged;
      renderPreferredChips();
      // If panel is open, render it too
      const panel = document.getElementById('allTagsPanel');
      if(panel && !panel.classList.contains('hidden')){ renderAllTags(); }
    }catch{}
  }
  function renderPreferredChips(){
    const host = document.getElementById('preferChips'); if(!host) return;
  const max = (window.recsSettings && window.recsSettings.maxPreferredTags) ?? ((window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3);
    const maxEl = document.getElementById('preferMaxTags'); if(maxEl) maxEl.textContent = String(max);
    const panel = document.getElementById('allTagsPanel');
    const catalog0 = Array.isArray(window.preferTagsCatalog) ? window.preferTagsCatalog : [];
    const selected = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
    // When collapsed, bubble selected to the front; when expanded, keep popular order
    const catalog = (panel && panel.classList.contains('hidden'))
      ? [...selected, ...catalog0.filter(t => !selected.includes(t))]
      : catalog0;
    const styleForSelected = computeSelectedTagClass();
    host.innerHTML = catalog.map(tag => {
      const sel = selected.includes(tag);
      const cls = sel ? styleForSelected : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
      return `<button type="button" data-tag="${tag}" class="px-2 py-1 text-xs rounded-full border border-slate-700 ${cls}">${tag}</button>`;
    }).join('');
  }

  function renderAllTags(){
    const list = document.getElementById('allTagsList'); if(!list) return;
    const q = (document.getElementById('tagsSearch')?.value || '').toLowerCase();
  const sort = (window.tagsSortMode || 'popularity');
    let items = Array.isArray(window.allTags) ? [...window.allTags] : [];
    if(q){ items = items.filter(x => (x.tag||'').toLowerCase().includes(q)); }
    if(sort === 'alpha'){ items.sort((a,b)=> (a.tag||'').localeCompare(b.tag||'')); }
    else { items.sort((a,b)=> (b.count||0)-(a.count||0) || (a.tag||'').localeCompare(b.tag||'')); }
    const selected = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
    const styleForSelected = computeSelectedTagClass();
    list.innerHTML = items.map(x=>{
      const sel = selected.includes(x.tag);
      const cls = sel ? styleForSelected : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
      return `<button type="button" data-tag="${x.tag}" class="px-2 py-1 text-xs rounded-full border border-slate-700 ${cls}">${x.tag} <span class="text-gray-400">(${x.count})</span></button>`;
    }).join('');
  }

  function togglePreferredTag(tag){
    const max = (window.recsSettings && window.recsSettings.maxPreferredTags) ?? ((window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3);
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
  const PAGE_SIZE = (window.S5Config && window.S5Config.PAGE_SIZE) || 100;
    window.currentRecsTopK = PAGE_SIZE;
    const btn = document.getElementById('moreBtn'); if(btn) btn.classList.add('hidden');
    if(window.currentView === 'forYou'){
      window.loadAnimeData && window.loadAnimeData();
      return;
    }
    if(window.currentView === 'freeBrowsing'){
      window.loadFreeBrowsingData && window.loadFreeBrowsingData();
      return;
    }
    // In Library view, tags don’t affect results; no-op
  }

  window.S5Tags = { loadTags, renderPreferredChips, renderAllTags, togglePreferredTag, clearPreferred, onPreferredChanged };
  // Back-compat for inline calls and main.js expectations
  window.loadTags = loadTags;
  window.renderPreferredChips = renderPreferredChips;
  window.renderAllTags = renderAllTags;
  window.togglePreferredTag = togglePreferredTag;
  window.clearPreferred = clearPreferred;
  window.onPreferredChanged = onPreferredChanged;

  // Wire sort mode toggle once DOM ready
  document.addEventListener('DOMContentLoaded', () => {
    const popBtn = document.getElementById('tagsSortPopularityBtn');
    const alphaBtn = document.getElementById('tagsSortAlphaBtn');
    window.tagsSortMode = 'popularity';
    function setMode(mode){
      window.tagsSortMode = mode;
      if(popBtn && alphaBtn){
        if(mode === 'popularity'){
          popBtn.className = 'px-2 py-1 text-xs text-white bg-purple-600 rounded transition-colors';
          alphaBtn.className = 'px-2 py-1 text-xs text-gray-400 hover:text-white rounded transition-colors';
        } else {
          alphaBtn.className = 'px-2 py-1 text-xs text-white bg-purple-600 rounded transition-colors';
          popBtn.className = 'px-2 py-1 text-xs text-gray-400 hover:text-white rounded transition-colors';
        }
      }
      renderAllTags();
    }
    if(popBtn) popBtn.addEventListener('click', () => setMode('popularity'));
    if(alphaBtn) alphaBtn.addEventListener('click', () => setMode('alpha'));
  });

  // Determine selected-tag visual style based on current Tag Boost weight
  function computeSelectedTagClass(){
    const w = (typeof window.getCurrentPreferWeight === 'function')
      ? window.getCurrentPreferWeight()
    : parseFloat(document.getElementById('preferWeight')?.value || String(window.recsSettings?.preferTagsWeight ?? ((window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2)));
  if (isNaN(w)) return 'chip--sel-mid';
  if (w <= ((window.S5Const?.RECS?.TAG_WEIGHT_LOW_MAX) ?? 0.2)) return 'chip--sel-low';
  if (w >= ((window.S5Const?.RECS?.TAG_WEIGHT_HIGH_MIN) ?? 0.6)) return 'chip--sel-high';
  return 'chip--sel-mid';
  }
})();
