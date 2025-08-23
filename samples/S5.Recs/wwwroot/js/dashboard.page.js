// S5.Recs Dashboard page controller (classic script)
(function(){
  // Overview loaders
  async function loadStats(){ try{ const u=(window.S5Const?.ENDPOINTS?.ADMIN_STATS)||'/admin/stats'; const r = await fetch(u); if(r.ok){ const s = await r.json(); const anime=(s.anime||0).toLocaleString(); const vectors=(s.vectors||0).toLocaleString(); const $=document.getElementById.bind(document); $('ov-anime').textContent=anime; $('ov-vectors').textContent=vectors; }}catch{} }
  async function loadTagsCount(){ try{ const u=(window.S5Const?.ENDPOINTS?.TAGS)||'/api/tags'; const cReq = await fetch(u); const full = cReq.ok? await cReq.json():[]; const el=document.getElementById('ov-tags'); if(el) el.textContent = (full.length||0).toLocaleString(); }catch{} }
  async function loadHealth(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_HEALTH)||'/.well-known/sora/health'; const r = await fetch(u); const el=document.getElementById('ov-health'); if(el) el.textContent = r.ok?'OK':'N/A'; }catch{ const el=document.getElementById('ov-health'); if(el) el.textContent='N/A'; } }
  async function loadObservability(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_OBSERVABILITY)||'/.well-known/sora/observability'; const r = await fetch(u); const el=document.getElementById('observabilityJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('observabilityJson'); if(el) el.textContent='Unavailable'; } }
  async function loadAggregates(){ try{ const u=(window.S5Const?.ENDPOINTS?.WK_AGGREGATES)||'/.well-known/sora/aggregates'; const r = await fetch(u); const el=document.getElementById('aggregatesJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('aggregatesJson'); if(el) el.textContent='Unavailable'; } }

  // Admin actions
  async function seedDataFromForm(){ const src=document.getElementById('importSource')?.value||'anilist'; const defLim=(window.S5Const?.ADMIN?.IMPORT_DEFAULT_LIMIT)??200; const lim=parseInt(document.getElementById('importLimit')?.value||String(defLim),10); const ow=!!document.getElementById('importOverwrite')?.checked; await seedDataFrom(src, lim, ow); }
  async function seedDataFrom(source, limit, overwrite){ try{ window.showToast && showToast('Seeding…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_SEED_START)||'/admin/seed/start'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ source, limit, overwrite }) }); window.showToast && showToast(r.ok?'Seed started':'Seed failed', r.ok?'success':'error'); if(r.ok){ const d=(window.S5Const?.ADMIN?.QUICK_ACTIONS_REFRESH_DELAY_MS)??1500; setTimeout(()=>{ loadStats(); loadTagsCount(); }, d); }}catch{ window.showToast && showToast('Seed error','error'); } }
  async function vectorUpsert(){ try{ window.showToast && showToast('Vector upsert…'); const lim=(window.S5Const?.ADMIN?.VECTOR_UPSERT_LIMIT)??1000; const u=(window.S5Const?.ENDPOINTS?.ADMIN_SEED_VECTORS)||'/admin/seed/vectors'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ limit: lim }) }); window.showToast && showToast(r.ok?'Vectors job started':'Vectors job failed', r.ok?'success':'error'); }catch{ window.showToast && showToast('Vectors error','error'); } }
  async function rebuildTags(){ try{ window.showToast && showToast('Rebuilding tags…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_TAGS_REBUILD)||'/admin/tags/rebuild'; const r = await fetch(u, { method:'POST' }); window.showToast && showToast(r.ok?'Tags rebuilt':'Tags rebuild failed', r.ok?'success':'error'); if(r.ok){ loadTagsCount(); }}catch{ window.showToast && showToast('Tags error','error'); } }
  async function rebuildGenres(){ try{ window.showToast && showToast('Rebuilding genres…'); const u=(window.S5Const?.ENDPOINTS?.ADMIN_GENRES_REBUILD)||'/admin/genres/rebuild'; const r = await fetch(u, { method:'POST' }); window.showToast && showToast(r.ok?'Genres rebuilt':'Genres rebuild failed', r.ok?'success':'error'); }catch{ window.showToast && showToast('Genres error','error'); } }

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
  async function initCensorAdmin(){
    const input = document.getElementById('censorInput');
    const btnAdd = document.getElementById('btnCensorAdd');
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

  // Bootstrap
  document.addEventListener('DOMContentLoaded', () => {
  loadStats(); loadHealth(); loadTagsCount(); loadAggregates(); loadObservability(); bindSettings(); loadRecsSettings(); initCensorAdmin();
    // Initialize Import limit from constants to avoid magic number in HTML
    const defLim = (window.S5Const && window.S5Const.ADMIN && typeof window.S5Const.ADMIN.IMPORT_DEFAULT_LIMIT === 'number') ? window.S5Const.ADMIN.IMPORT_DEFAULT_LIMIT : 200;
    const limEl = document.getElementById('importLimit');
    if(limEl && !limEl.value){ limEl.value = String(defLim); }
    // Expose actions for buttons with inline handlers
  Object.assign(window, { seedDataFromForm, vectorUpsert, rebuildTags, rebuildGenres, loadAggregates, loadObservability });
  });
})();
