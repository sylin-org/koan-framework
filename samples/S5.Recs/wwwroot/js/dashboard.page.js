// S5.Recs Dashboard page controller (classic script)
(function(){
  // Overview loaders
  async function loadStats(){ try{ const u=(window.S5Const?.ENDPOINTS?.ADMIN_STATS)||'/admin/stats'; const r = await fetch(u); if(r.ok){ const s = await r.json(); const media=(s.media||0).toLocaleString(); const vectors=(s.vectors||0).toLocaleString(); const $=document.getElementById.bind(document); $('ov-media').textContent=media; $('ov-vectors').textContent=vectors; }}catch{} }
  async function loadTagsCount(){ try{ const u=(window.S5Const?.ENDPOINTS?.TAGS)||'/api/tags'; const cReq = await fetch(u); const full = cReq.ok? await cReq.json():[]; const el=document.getElementById('ov-tags'); if(el) el.textContent = (full.length||0).toLocaleString(); }catch{} }
  async function loadHealth(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_HEALTH)||'/.well-known/Koan/health'; const r = await fetch(u); const el=document.getElementById('ov-health'); if(el) el.textContent = r.ok?'OK':'N/A'; }catch{ const el=document.getElementById('ov-health'); if(el) el.textContent='N/A'; } }
  async function loadObservability(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_OBSERVABILITY)||'/.well-known/Koan/observability'; const r = await fetch(u); const el=document.getElementById('observabilityJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('observabilityJson'); if(el) el.textContent='Unavailable'; } }
  async function loadAggregates(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_AGGREGATES)||'/.well-known/Koan/aggregates'; const r = await fetch(u); const el=document.getElementById('aggregatesJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('aggregatesJson'); if(el) el.textContent='Unavailable'; } }

  // Admin actions
  async function seedDataFromForm(){ const src=document.getElementById('importSource')?.value||'anilist'; const mediaType=document.getElementById('importMediaType')?.value; const limitValue=document.getElementById('importLimit')?.value; const lim=limitValue ? parseInt(limitValue,10) : null; const ow=!!document.getElementById('importOverwrite')?.checked; if(!mediaType){ window.showToast && showToast('Please select a media type','error'); return; } await seedDataFrom(src, mediaType, lim, ow); }
  async function seedDataFrom(source, mediaType, limit, overwrite){ try{ window.showToast && showToast('Seeding…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_SEED_START)||'/admin/seed/start'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ source, mediaType, limit, overwrite }) }); window.showToast && showToast(r.ok?'Seed started':'Seed failed', r.ok?'success':'error'); if(r.ok){ const d=(window.S5Const?.ADMIN?.QUICK_ACTIONS_REFRESH_DELAY_MS)??1500; setTimeout(()=>{ loadStats(); loadTagsCount(); }, d); }}catch{ window.showToast && showToast('Seed error','error'); } }
  async function loadMediaTypes(){ try{ const u=(window.S5Const?.ENDPOINTS?.MEDIA_TYPES)||'/api/media-types'; const r = await fetch(u); if(r.ok){ const types = await r.json(); const select = document.getElementById('importMediaType'); if(select){ select.innerHTML = '<option value="">Select media type...</option><option value="all">All (import all media types)</option>'; types.forEach(type => { const option = document.createElement('option'); option.value = type.name; option.textContent = type.name; select.appendChild(option); }); }}}catch{ const select = document.getElementById('importMediaType'); if(select) select.innerHTML = '<option value="">Failed to load</option>'; } }
  async function vectorUpsert(){ try{ window.showToast && showToast('Vector upsert…'); const lim=(window.S5Const?.ADMIN?.VECTOR_UPSERT_LIMIT)??1000; const u=(window.S5Const?.ENDPOINTS?.ADMIN_SEED_VECTORS)||'/admin/seed/vectors'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ limit: lim }) }); window.showToast && showToast(r.ok?'Vectors job started':'Vectors job failed', r.ok?'success':'error'); }catch{ window.showToast && showToast('Vectors error','error'); } }
  async function rebuildTags(){ try{ window.showToast && showToast('Rebuilding tags…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_TAGS_REBUILD)||'/admin/tags/rebuild'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Tags rebuilt: ${result.updated || 0} tags`, 'success'); setTimeout(loadTagsCount, 500); }else{ window.showToast && showToast('Tags rebuild failed','error'); }}catch{ window.showToast && showToast('Tags error','error'); } }
  async function rebuildGenres(){ try{ window.showToast && showToast('Rebuilding genres…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_GENRES_REBUILD)||'/admin/genres/rebuild'; const r = await fetch(u, { method:'POST' }); window.showToast && showToast(r.ok?'Genres rebuilt':'Genres rebuild failed', r.ok?'success':'error'); }catch{ window.showToast && showToast('Genres error','error'); } }

  // Cache and Flush actions
  async function rebuildFromCache(){ try{ window.showToast && showToast('Rebuilding from cache...'); const u='/admin/rebuild-db-from-cache'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({}) }); if(r.ok){ const result = await r.json(); const total = result.totalImported || 0; window.showToast && showToast(`Rebuild complete: ${total} items imported`, 'success'); setTimeout(()=>{ loadStats(); loadTagsCount(); }, 1500); }else{ window.showToast && showToast('Rebuild failed','error'); }}catch{ window.showToast && showToast('Rebuild error','error'); } }
  async function flushCache(){ if(!confirm('Delete all cached raw import data?')) return; try{ window.showToast && showToast('Flushing cache…'); const u='/admin/flush/cache'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Cache flushed: ${result.count} jobs deleted`, 'success'); }else{ window.showToast && showToast('Flush cache failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function flushMedia(){ if(!confirm('Delete ALL media documents? This cannot be undone!')) return; try{ window.showToast && showToast('Flushing media…'); const u='/admin/flush/media'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Media flushed: ${result.count} items deleted`, 'success'); setTimeout(loadStats, 1000); }else{ window.showToast && showToast('Flush media failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function flushVectors(){ if(!confirm('Delete all vector embeddings?')) return; try{ window.showToast && showToast('Flushing vectors…'); const u='/admin/flush/vectors'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Vectors flushed: ${result.count} vectors deleted`, 'success'); setTimeout(loadStats, 1000); }else{ window.showToast && showToast('Flush vectors failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function flushTags(){ if(!confirm('Delete all tag catalog data?')) return; try{ window.showToast && showToast('Flushing tags…'); const u='/admin/flush/tags'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Tags flushed: ${result.count} tags deleted`, 'success'); setTimeout(loadTagsCount, 1000); }else{ window.showToast && showToast('Flush tags failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function flushGenres(){ if(!confirm('Delete all genre catalog data?')) return; try{ window.showToast && showToast('Flushing genres…'); const u='/admin/flush/genres'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Genres flushed: ${result.count} genres deleted`, 'success'); }else{ window.showToast && showToast('Flush genres failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function flushEmbeddingsCache(){ if(!confirm('Delete all cached embeddings? This will NOT affect vector database, only the cache.')) return; try{ window.showToast && showToast('Flushing embeddings cache…'); const u='/admin/flush/embeddings'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Embeddings cache flushed: ${result.count} cached embeddings deleted`, 'success'); }else{ window.showToast && showToast('Flush embeddings cache failed','error'); }}catch{ window.showToast && showToast('Flush error','error'); } }
  async function exportEmbeddingsToCache(){ if(!confirm('Export all current vectors to embeddings cache? This will re-embed all media items and populate the cache for adapter portability.')) return; try{ window.showToast && showToast('Exporting vectors to embeddings cache… This may take several minutes.'); const u='/admin/cache/embeddings/export'; const r = await fetch(u, { method:'POST' }); if(r.ok){ const result = await r.json(); window.showToast && showToast(`Export started: ${result.jobId}. Check logs for progress.`, 'success'); }else{ window.showToast && showToast('Export failed','error'); }}catch{ window.showToast && showToast('Export error','error'); } }

  // Settings
  function bindSettings(){
    const ptw = document.getElementById('ptw');
    const ptwNum = document.getElementById('ptwNum');
    const mpt = document.getElementById('mpt');
    const mptNum = document.getElementById('mptNum');
    const dw = document.getElementById('dw');
    const dwNum = document.getElementById('dwNum');
    if(!ptw||!ptwNum||!mpt||!mptNum||!dw||!dwNum) return;
    const link = (a,b) => { a.addEventListener('input', ()=> b.value = a.value); b.addEventListener('input', ()=> a.value = b.value); };
    link(ptw, ptwNum); link(mpt, mptNum); link(dw, dwNum);
    const saveBtn = document.getElementById('btnSaveRecs');
    const resetBtn = document.getElementById('btnResetRecs');
    if(saveBtn) saveBtn.addEventListener('click', saveRecsSettings);
    if(resetBtn) resetBtn.addEventListener('click', loadRecsSettings);
  }
  async function loadRecsSettings(){ try{ const u=(window.S5Const?.ENDPOINTS?.RECS_SETTINGS)||'/admin/recs-settings'; const r = await fetch(u); if(!r.ok) throw 0; const s = await r.json(); const ptw = document.getElementById('ptw'); const ptwNum = document.getElementById('ptwNum'); const mpt = document.getElementById('mpt'); const mptNum = document.getElementById('mptNum'); const dw = document.getElementById('dw'); const dwNum = document.getElementById('dwNum'); const to2 = v => (v ?? 0).toFixed(2); if(ptw) ptw.value = to2(s.preferTagsWeight); if(ptwNum) ptwNum.value = to2(s.preferTagsWeight); const defMpt=(window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS)??3; if(mpt) mpt.value = s.maxPreferredTags ?? defMpt; if(mptNum) mptNum.value = s.maxPreferredTags ?? defMpt; if(dw) dw.value = to2(s.diversityWeight); if(dwNum) dwNum.value = to2(s.diversityWeight); }catch{} }
  async function saveRecsSettings(){ try{ const defMpt=(window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS)??3; const body = { preferTagsWeight: parseFloat(document.getElementById('ptwNum')?.value || '0'), maxPreferredTags: parseInt(document.getElementById('mptNum')?.value || String(defMpt), 10), diversityWeight: parseFloat(document.getElementById('dwNum')?.value || '0') }; window.showToast && showToast('Saving settings…'); const u=(window.S5Const?.ENDPOINTS?.RECS_SETTINGS)||'/admin/recs-settings'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) }); window.showToast && showToast(r.ok? 'Settings saved' : 'Save failed', r.ok? 'success':'error'); }catch{ window.showToast && showToast('Save error','error'); } }

  // Censor tags admin
  async function initCenKoandmin(){
    const input = document.getElementById('censorInput');
    const btnAdd = document.getElementById('btnCenKoandd');
    const btnCopy = document.getElementById('btnCensorCopy');
    const btnClear = document.getElementById('btnCensorClear');
    const list = document.getElementById('censorList');
    const count = document.getElementById('censorCount');
    const toggle = document.getElementById('btnCensorToggle');
    const container = document.getElementById('censorListContainer');
    if(!input || !btnAdd || !btnClear || !list || !count) return;

    // Toggle expand/collapse
    if(toggle && container){
      const setExpanded = (expanded) => {
        toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        toggle.textContent = expanded ? 'Hide' : 'Show';
        if(expanded){ container.classList.remove('hidden'); }
        else { container.classList.add('hidden'); }
      };
      // default collapsed
      setExpanded(false);
      toggle.addEventListener('click', () => {
        const isExp = toggle.getAttribute('aria-expanded') === 'true';
        setExpanded(!isExp);
      });
    }

    const refresh = async () => {
      try{
  const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR)||'/admin/tags/censor');
        if(!r.ok) throw 0;
        const data = await r.json();
        count.textContent = (data.tags?.length || 0).toString();
        list.innerHTML = '';
        (data.tags||[]).forEach(t => {
          const btn = document.createElement('button');
          btn.type = 'button';
          // Use shared chip styles
          btn.className = 'chip chip--dark';
          btn.textContent = t;
          btn.title = 'Click to remove from censor list';
          btn.addEventListener('click', async () => {
            try{
              const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR_REMOVE)||'/admin/tags/censor/remove', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ tag: t }) });
              if(!r.ok) throw 0;
              window.showToast && showToast(`Removed "${t}"`, 'success');
              await refresh();
            }catch{ window.showToast && showToast('Remove failed','error'); }
          });
          list.appendChild(btn);
        });
      }catch{}
    };

    btnAdd.addEventListener('click', async () => {
      const text = input.value || '';
      if(!text.trim()) return;
      btnAdd.disabled = true;
      try{
  const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR_ADD)||'/admin/tags/censor/add', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ text }) });
        if(!r.ok) throw 0;
        input.value = '';
        window.showToast && showToast('Censor tags updated','success');
        await refresh();
      }catch{ window.showToast && showToast('Failed to update censor tags','error'); }
      finally{ btnAdd.disabled = false; }
    });

    btnClear.addEventListener('click', async () => {
      if(!confirm('Clear the entire censor list?')) return;
      btnClear.disabled = true;
      try{
  const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR_CLEAR)||'/admin/tags/censor/clear', { method:'POST' });
        if(!r.ok) throw 0;
        window.showToast && showToast('Censor list cleared','success');
        await refresh();
      }catch{ window.showToast && showToast('Failed to clear list','error'); }
      finally{ btnClear.disabled = false; }
    });

    if(btnCopy){
      btnCopy.addEventListener('click', async () => {
        try{
          const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR)||'/admin/tags/censor');
          if(!r.ok) throw 0;
          const data = await r.json();
          const text = (data.tags||[]).join(', ');
          await navigator.clipboard.writeText(text);
          window.showToast && showToast('Censor list copied','success');
        }catch{ window.showToast && showToast('Copy failed','error'); }
      });
    }

    await refresh();
  }

  // Tag Browser functionality for Censor management
  let allTagsForCensor = [];
  let censoredTags = [];
  let censorTagsSortMode = 'popularity';

  async function loadTagsForCensor(){
    try{
      // Use admin endpoint to get ALL tags including censored ones
      const url = '/admin/tags/all?sort=popularity';
      const r = await fetch(url);
      if(!r.ok) throw new Error('Failed to fetch all tags');
      const list = await r.json();
      allTagsForCensor = Array.isArray(list) ? list : [];
      renderCensorTagsBrowser();
    }catch{
      // Fallback: try to show something
      const browser = document.getElementById('censorTagsBrowser');
      if(browser) browser.innerHTML = '<div class="text-sm text-red-400">Failed to load tags</div>';
    }
  }

  function renderCensorTagsBrowser(){
    const browser = document.getElementById('censorTagsBrowser');
    if(!browser) return;

    const q = (document.getElementById('censorTagsSearch')?.value || '').toLowerCase();
    let items = [...allTagsForCensor];

    // Filter by search
    if(q){ items = items.filter(x => (x.tag||'').toLowerCase().includes(q)); }

    // Sort
    if(censorTagsSortMode === 'alpha'){
      items.sort((a,b)=> (a.tag||'').localeCompare(b.tag||''));
    } else {
      items.sort((a,b)=> (b.count||0)-(a.count||0) || (a.tag||'').localeCompare(b.tag||''));
    }

    // Create case-insensitive lookup set for censored tags
    const censoredTagsLower = new Set(censoredTags.map(t => t.toLowerCase()));

    browser.innerHTML = items.map(x=>{
      const isCensored = censoredTagsLower.has((x.tag||'').toLowerCase());
      const cls = isCensored
        ? 'px-2 py-1 text-xs rounded-full border border-red-500 bg-red-600 text-white hover:bg-red-700'
        : 'px-2 py-1 text-xs rounded-full border border-slate-700 bg-slate-800 text-gray-300 hover:bg-slate-700';
      return `<button type="button" data-tag="${x.tag}" class="${cls}">${x.tag} <span class="text-gray-400">(${x.count})</span></button>`;
    }).join('');

    // Add click handlers
    browser.querySelectorAll('button[data-tag]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const tag = btn.getAttribute('data-tag');
        await toggleCensorTag(tag);
      });
    });
  }

  async function toggleCensorTag(tag){
    // Use case-insensitive comparison to match backend behavior
    const isCurrentlyCensored = censoredTags.some(t => t.toLowerCase() === tag.toLowerCase());
    try{
      if(isCurrentlyCensored){
        // Remove from censor
        const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR_REMOVE)||'/admin/tags/censor/remove', {
          method:'POST',
          headers:{'Content-Type':'application/json'},
          body: JSON.stringify({ tag })
        });
        if(!r.ok) throw 0;
        window.showToast && showToast(`Removed "${tag}" from censor list`, 'success');
      } else {
        // Add to censor
        const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR_ADD)||'/admin/tags/censor/add', {
          method:'POST',
          headers:{'Content-Type':'application/json'},
          body: JSON.stringify({ text: tag })
        });
        if(!r.ok) throw 0;
        window.showToast && showToast(`Added "${tag}" to censor list`, 'success');
      }
      await refreshCensorData();
    }catch{
      window.showToast && showToast(`Failed to toggle "${tag}"`, 'error');
    }
  }

  async function refreshCensorData(){
    // Refresh the main censor list
    await initCenKoandmin();
    // Re-render the browser to update button states
    renderCensorTagsBrowser();
  }

  function initCensorTagsBrowser(){
    const popBtn = document.getElementById('censorTagsSortPopularityBtn');
    const alphaBtn = document.getElementById('censorTagsSortAlphaBtn');
    const searchInput = document.getElementById('censorTagsSearch');

    function setMode(mode){
      censorTagsSortMode = mode;
      if(popBtn && alphaBtn){
        if(mode === 'popularity'){
          popBtn.className = 'px-2 py-1 text-xs text-white bg-red-600 rounded transition-colors';
          alphaBtn.className = 'px-2 py-1 text-xs text-gray-400 hover:text-white rounded transition-colors';
        } else {
          alphaBtn.className = 'px-2 py-1 text-xs text-white bg-red-600 rounded transition-colors';
          popBtn.className = 'px-2 py-1 text-xs text-gray-400 hover:text-white rounded transition-colors';
        }
      }
      renderCensorTagsBrowser();
    }

    if(popBtn) popBtn.addEventListener('click', () => setMode('popularity'));
    if(alphaBtn) alphaBtn.addEventListener('click', () => setMode('alpha'));

    if(searchInput){
      searchInput.addEventListener('input', () => {
        renderCensorTagsBrowser();
      });
    }

    // Manual input toggle
    const toggleBtn = document.getElementById('btnToggleManualInput');
    const section = document.getElementById('manualInputSection');
    const chevron = document.getElementById('manualInputChevron');

    if(toggleBtn && section){
      toggleBtn.addEventListener('click', () => {
        const isHidden = section.classList.contains('hidden');
        if(isHidden){
          section.classList.remove('hidden');
          if(chevron) chevron.style.transform = 'rotate(90deg)';
        } else {
          section.classList.add('hidden');
          if(chevron) chevron.style.transform = 'rotate(0deg)';
        }
      });
    }
  }

  // Override the existing initCenKoandmin to also track censored tags
  const originalInitCenKoandmin = initCenKoandmin;
  initCenKoandmin = async function(){
    await originalInitCenKoandmin();

    // Load current censor list
    try{
      const r = await fetch((window.S5Const?.ENDPOINTS?.ADMIN_TAGS_CENSOR)||'/admin/tags/censor');
      if(r.ok){
        const data = await r.json();
        censoredTags = data.tags || [];
        renderCensorTagsBrowser();
      }
    }catch{}
  };

  // Bootstrap
  document.addEventListener('DOMContentLoaded', () => {
  loadStats(); loadHealth(); loadTagsCount(); loadAggregates(); loadObservability(); bindSettings(); loadRecsSettings(); initCenKoandmin(); loadTagsForCensor(); initCensorTagsBrowser(); loadMediaTypes();
    // Expose actions for buttons with inline handlers
  Object.assign(window, { seedDataFromForm, vectorUpsert, rebuildTags, rebuildGenres, rebuildFromCache, flushCache, flushMedia, flushVectors, flushTags, flushGenres, loadAggregates, loadObservability, loadMediaTypes });
  });
})();
