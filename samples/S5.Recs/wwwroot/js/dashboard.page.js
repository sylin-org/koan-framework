// S5.Recs Dashboard page controller (classic script)
(function(){
  // Utilities
  function setDot(id, ok){ const el=document.getElementById(id); if(!el) return; el.className = 'w-3 h-3 rounded-full ' + (ok?'bg-green-500':'bg-red-500'); }

  // Overview loaders
  async function loadStats(){ try{ const r = await fetch('/admin/stats'); if(r.ok){ const s = await r.json(); const anime=(s.anime||0).toLocaleString(); const vectors=(s.vectors||0).toLocaleString(); const $=document.getElementById.bind(document); $('ov-anime').textContent=anime; $('ov-vectors').textContent=vectors; }}catch{} }
  async function loadTagsCount(){ try{ const cReq = await fetch('/api/tags'); const full = cReq.ok? await cReq.json():[]; const el=document.getElementById('ov-tags'); if(el) el.textContent = (full.length||0).toLocaleString(); }catch{} }
  async function loadHealth(){ try{ const r = await fetch('/.well-known/sora/health'); const ok=r.ok; setDot('apiDot', ok); setDot('dbDot', ok); setDot('vecDot', ok); setDot('cacheDot', ok); const el=document.getElementById('ov-health'); if(el) el.textContent = ok?'OK':'N/A'; }catch{ setDot('apiDot', false); setDot('dbDot', false); setDot('vecDot', false); setDot('cacheDot', false); const el=document.getElementById('ov-health'); if(el) el.textContent='N/A'; } }
  async function loadObservability(){ try{ const r = await fetch('/.well-known/sora/observability'); const el=document.getElementById('observabilityJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('observabilityJson'); if(el) el.textContent='Unavailable'; } }
  async function loadAggregates(){ try{ const r = await fetch('/.well-known/sora/aggregates'); const el=document.getElementById('aggregatesJson'); if(!el) return; el.textContent = r.ok? JSON.stringify(await r.json(), null, 2):'Unavailable'; }catch{ const el=document.getElementById('aggregatesJson'); if(el) el.textContent='Unavailable'; } }

  // Admin actions
  async function seedData(){ await seedDataFrom('anilist', 50, false); }
  async function seedDataFromForm(){ const src=document.getElementById('importSource')?.value||'anilist'; const lim=parseInt(document.getElementById('importLimit')?.value||'200',10); const ow=!!document.getElementById('importOverwrite')?.checked; await seedDataFrom(src, lim, ow); }
  async function seedDataFrom(source, limit, overwrite){ try{ window.showToast && showToast('Seeding…'); const r = await fetch('/admin/seed/start', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ source, limit, overwrite }) }); window.showToast && showToast(r.ok?'Seed started':'Seed failed', r.ok?'success':'error'); if(r.ok){ setTimeout(()=>{ loadStats(); loadTagsCount(); }, 1500); }}catch{ window.showToast && showToast('Seed error','error'); } }
  async function vectorUpsert(){ try{ window.showToast && showToast('Vector upsert…'); const r = await fetch('/admin/seed/vectors', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ limit: 1000 }) }); window.showToast && showToast(r.ok?'Vectors job started':'Vectors job failed', r.ok?'success':'error'); }catch{ window.showToast && showToast('Vectors error','error'); } }
  async function rebuildTags(){ try{ window.showToast && showToast('Rebuilding tags…'); const r = await fetch('/admin/tags/rebuild', { method:'POST' }); window.showToast && showToast(r.ok?'Tags rebuilt':'Tags rebuild failed', r.ok?'success':'error'); if(r.ok){ loadTagsCount(); }}catch{ window.showToast && showToast('Tags error','error'); } }

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
  async function loadRecsSettings(){ try{ const r = await fetch('/admin/recs-settings'); if(!r.ok) throw 0; const s = await r.json(); const ptw = document.getElementById('ptw'); const ptwNum = document.getElementById('ptwNum'); const mpt = document.getElementById('mpt'); const mptNum = document.getElementById('mptNum'); const dw = document.getElementById('dw'); const dwNum = document.getElementById('dwNum'); const to2 = v => (v ?? 0).toFixed(2); if(ptw) ptw.value = to2(s.preferTagsWeight); if(ptwNum) ptwNum.value = to2(s.preferTagsWeight); if(mpt) mpt.value = s.maxPreferredTags ?? 3; if(mptNum) mptNum.value = s.maxPreferredTags ?? 3; if(dw) dw.value = to2(s.diversityWeight); if(dwNum) dwNum.value = to2(s.diversityWeight); }catch{} }
  async function saveRecsSettings(){ try{ const body = { preferTagsWeight: parseFloat(document.getElementById('ptwNum')?.value || '0'), maxPreferredTags: parseInt(document.getElementById('mptNum')?.value || '3', 10), diversityWeight: parseFloat(document.getElementById('dwNum')?.value || '0') }; window.showToast && showToast('Saving settings…'); const r = await fetch('/admin/recs-settings', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) }); window.showToast && showToast(r.ok? 'Settings saved' : 'Save failed', r.ok? 'success':'error'); }catch{ window.showToast && showToast('Save error','error'); } }

  // Bootstrap
  document.addEventListener('DOMContentLoaded', () => {
    loadStats(); loadHealth(); loadTagsCount(); loadAggregates(); loadObservability(); bindSettings(); loadRecsSettings();
    // Expose actions for buttons with inline handlers
    Object.assign(window, { seedData, seedDataFromForm, vectorUpsert, rebuildTags, loadAggregates, loadObservability });
  });
})();
