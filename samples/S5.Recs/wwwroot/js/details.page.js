// S5.Recs Details page controller (classic script)
(function(){
  // Safely quote string values for templates (kept local)
  function __q(v){ return "'" + String(v ?? '').replace(/'/g, "\\'") + "'"; }

  // State
  let currentUserId = null;
  let currentAnimeId = null;
  let currentEntry = null;

  function goHome(){ location.href = 'index.html'; }
  function toggleProfileMenu(){ const el=document.getElementById('profileMenu'); if(el) el.classList.toggle('hidden'); }

  async function initUsers(){ try{ const r = await fetch('/api/users'); if(!r.ok) return; const users = await r.json(); renderUsers(users); const def = users.find(u=>u.isDefault) || users[0]; if(def) selectUser(def.id, def.name); }catch{} }
  function renderUsers(users){ const list = document.getElementById('userList'); if(!list) return; list.innerHTML = users.map(u=>`<div class="flex items-center space-x-3 p-3 hover:bg-slate-700 rounded-lg cursor-pointer" onclick="window.__details.selectUser(${JSON.stringify(u.id)}, ${JSON.stringify(u.name||'User')})"><div class="w-8 h-8 bg-gradient-to-r from-purple-500 to-pink-500 rounded-full flex items-center justify-center">${(u.name||'U').slice(0,1).toUpperCase()}</div><div class="flex-1"><div class="text-white">${u.name||'User'}</div>${u.isDefault?'<div class="text-xs text-gray-400">Default<\/div>':''}</div></div>`).join(''); }
  async function createNewUser(){ const input = document.getElementById('newUserName'); const name = input?.value.trim(); if(!name) return; const r = await fetch('/api/users', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ name }) }); if(r.ok){ await initUsers(); if(input) input.value=''; } }
  function selectUser(id, name){ currentUserId = id; const ini=document.getElementById('profileInitial'); const nm=document.getElementById('profileName'); if(ini) ini.textContent = (name||'U').slice(0,1).toUpperCase(); if(nm) nm.textContent = name || 'User'; const pm=document.getElementById('profileMenu'); if(pm) pm.classList.add('hidden'); if(currentAnimeId){ loadEntryState(); loadSimilar(currentAnimeId); } }

  async function loadAnime(id){ if(!id) return; const r = await fetch(`/api/anime/${encodeURIComponent(id)}`); if(!r.ok) return; const a = await r.json(); renderAnime(a); const st=document.getElementById('similarTitle'); if(st) st.textContent = `Similar to ${a.title || 'this'}`; }
  function renderAnime(a){ const poster=document.getElementById('poster'); if(poster) poster.src = a.coverUrl || a.image || a.poster || '/images/missing-cover.svg'; const mainTitle = a.title || a.titleEnglish || a.titleRomaji || a.titleNative || a.name || 'Untitled'; const t=document.getElementById('title'); if(t) t.textContent = mainTitle; const dot=document.getElementById('colorDot'); if(dot){ if(a.coverColorHex){ dot.style.backgroundColor = a.coverColorHex; dot.classList.remove('hidden'); } else { dot.classList.add('hidden'); } } const altsList = [a.titleEnglish, a.titleRomaji, a.titleNative].map(x=>x||'').filter(x=>x && x.toLowerCase() !== (mainTitle||'').toLowerCase()); const alts=document.getElementById('alts'); if(alts) alts.textContent = altsList.length ? `Also known as: ${altsList.join(' • ')}` : ''; const meta=[]; if(a.year) meta.push(a.year); if(a.episodes) meta.push(`${a.episodes} eps`); if(typeof a.popularity === 'number') meta.push(`${Math.round(a.popularity*100)}% pop`); if(a.rating) meta.push(`Score ${a.rating}`); const m=document.getElementById('meta'); if(m) m.textContent = meta.join(' • '); const tags = Array.from(new Set([...(a.genres||[]), ...(a.tags||[])])); const tagsEl=document.getElementById('tags'); if(tagsEl) tagsEl.innerHTML = tags.slice(0,12).map(t=>`<span class="px-2 py-1 rounded bg-slate-800 text-gray-300 text-xs">${t}<\/span>`).join(''); const syns = Array.isArray(a.synonyms) ? a.synonyms.filter(Boolean) : []; const synEl=document.getElementById('synonyms'); if(synEl) synEl.innerHTML = syns.slice(0,12).map(s=>`<span class="px-2 py-1 rounded bg-slate-800 text-gray-400 text-xs">${s}<\/span>`).join(''); const syn=document.getElementById('synopsis'); if(syn) syn.textContent = a.synopsis || a.description || '—'; renderEntryState(); }

  async function loadSimilar(id){ if(!currentUserId){ const c=document.getElementById('similar'); if(c) c.innerHTML=''; return; } const body = { userId: currentUserId, anchorAnimeId: id, topK: 12, filters: { spoilerSafe: true } }; const r = await fetch('/api/recs/query', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) }); if(!r.ok){ const c=document.getElementById('similar'); if(c) c.innerHTML=''; return; } const { items } = await r.json(); const list = (items||[]).map(it => it?.anime || it).filter(Boolean).filter(a => a && a.id !== id); renderSimilar(list); }
  function renderSimilar(items){ const c = document.getElementById('similar'); if(!c) return; c.innerHTML = (items||[]).map(anime => `\n        <div class=\"min-w-[180px] bg-slate-900 rounded-xl overflow-hidden hover:scale-[1.02] hover:shadow-xl transition-all cursor-pointer\" onclick=\"window.__details.gotoDetails(${__q(anime.id)})\">\n          <img src=\"${anime.coverUrl || anime.image || anime.poster || '/images/missing-cover.svg'}\" class=\"h-48 w-full object-cover\" />\n          <div class=\"p-3\">\n            <div class=\"text-sm font-semibold line-clamp-2\">${anime.titleEnglish || anime.title || 'Untitled'}<\/div>\n            <div class=\"text-xs text-gray-400 mt-1\">${(anime.genres||[]).slice(0,2).join(' • ')}<\/div>\n          </div>\n        <\/div>\n      `).join(''); }
  function gotoDetails(id){ location.href = `details.html?id=${encodeURIComponent(id)}`; }

  // Actions
  async function markFavorite(){ if(!currentUserId || !currentAnimeId) return; await fetch(`/api/library/${currentUserId}/${currentAnimeId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ favorite:true }) }); await loadEntryState(); window.showToast && showToast('Added to favorites','success'); }
  async function markWatched(){ if(!currentUserId || !currentAnimeId) return; await fetch(`/api/library/${currentUserId}/${currentAnimeId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ watched:true }) }); await loadEntryState(); window.showToast && showToast('Marked as watched','success'); }
  async function rate(stars){ if(!currentUserId || !currentAnimeId) return; await fetch('/api/recs/rate', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ userId: currentUserId, animeId: currentAnimeId, rating: stars }) }); await loadEntryState(); window.showToast && showToast(`Rated ${stars}★`,'success'); }

  async function loadEntryState(){ if(!currentUserId || !currentAnimeId){ currentEntry = null; renderEntryState(); return; } try{ const r = await fetch(`/api/library/${currentUserId}?sort=updatedAt&page=1&pageSize=500`); if(!r.ok){ currentEntry = null; renderEntryState(); return; } const data = await r.json(); currentEntry = (data.items||[]).find(e=>e.animeId===currentAnimeId) || null; }catch{ currentEntry = null; } renderEntryState(); }
  function renderEntryState(){ const favBtn = document.querySelector('button[data-action="favorite"]'); const watchBtn = document.querySelector('button[data-action="watched"]'); if(!favBtn || !watchBtn) return; const fav = !!(currentEntry && currentEntry.favorite); const w = !!(currentEntry && currentEntry.watched); favBtn.className = `bg-slate-800 hover:bg-slate-700 px-4 py-2 rounded-lg ${fav ? 'ring-1 ring-pink-400/40 bg-pink-900/30' : ''}`; watchBtn.className = `bg-slate-800 hover:bg-slate-700 px-4 py-2 rounded-lg ${w ? 'ring-1 ring-green-400/40 bg-green-900/30' : ''}`; }

  // Bootstrap
  document.addEventListener('DOMContentLoaded', async () => {
    const qs = new URLSearchParams(location.search); currentAnimeId = qs.get('id');
    // Bind top menu buttons
    const backBtn = document.querySelector('button[data-action="go-home"]'); if(backBtn) backBtn.addEventListener('click', goHome);
    const profileBtn = document.getElementById('profileBtn'); if(profileBtn) profileBtn.addEventListener('click', toggleProfileMenu);
  const addUserBtn = document.querySelector('button[data-action="create-user"]'); if(addUserBtn) addUserBtn.addEventListener('click', createNewUser);
    // Bind actions
    const favBtn = document.querySelector('button[data-action="favorite"]'); if(favBtn) favBtn.addEventListener('click', markFavorite);
    const watchBtn = document.querySelector('button[data-action="watched"]'); if(watchBtn) watchBtn.addEventListener('click', markWatched);
    // Rating dropdown buttons (1..5)
    const rateBtns = document.querySelectorAll('[data-rate]'); rateBtns.forEach(b => b.addEventListener('click', () => rate(parseInt(b.getAttribute('data-rate')||'0',10))));

    // Expose callbacks referenced from generated HTML and profile list
    window.__details = { gotoDetails, selectUser };
    window.__details.createNewUser = createNewUser;

    await initUsers();
    if(currentAnimeId){ await loadAnime(currentAnimeId); await loadEntryState(); await loadSimilar(currentAnimeId); }
  });
})();
