// S5.Recs Details page controller (classic script)
(function(){
  // Safely quote string values for templates (kept local)
  function __q(v){ return "'" + String(v ?? '').replace(/'/g, "\\'") + "'"; }

  // State
  let currentUserId = null;
  const authState = { isAuthenticated: false, me: null };
  let currentAnimeId = null;
  let currentEntry = null;

  function goHome(){ const h=(window.S5Const?.PATHS?.HOME)||'index.html'; location.href = h; }
  function toggleProfileMenu(){ const el=document.getElementById('profileMenu'); if(el) el.classList.toggle('hidden'); }
  function show(el){ if(el) el.classList.remove('hidden'); }
  function hide(el){ if(el) el.classList.add('hidden'); }

  async function ensureAuthState(){
    try{
      const r = await fetch('/me', { credentials: 'include' });
      if(r.ok){ const me = await r.json(); authState.isAuthenticated = true; authState.me = me; currentUserId = me?.id || null; window.currentUserId = currentUserId; reflectAuthUi(true, me); return; }
    }catch{}
    authState.isAuthenticated = false; authState.me = null; currentUserId = null; window.currentUserId = null; reflectAuthUi(false, null);
  }

  function reflectAuthUi(isAuth, me){
    // Toggle Login/Logout buttons if present in layout
    const loginBtn = document.getElementById('loginBtn');
    const logoutBtn = document.getElementById('logoutBtn');
    if(loginBtn && logoutBtn){ if(isAuth){ hide(loginBtn); show(logoutBtn);} else { show(loginBtn); hide(logoutBtn);} }
  }

  async function openLogin(){
    try{
      const r = await fetch('/.well-known/auth/providers', { credentials: 'include' });
      const providers = r.ok ? await r.json() : [];
      const p = Array.isArray(providers) ? providers.find(x=>x.enabled && (x.protocol==='oauth2'||x.protocol==='oidc')) : null;
      if(!p){ window.showToast && showToast('No login providers available', 'error'); return; }
  const ret = window.location.pathname + window.location.search;
  window.location.href = `/auth/${encodeURIComponent(p.id)}/challenge?return=${encodeURIComponent(ret||'/')}&prompt=login`;
    }catch{ window.showToast && showToast('Login failed to start', 'error'); }
  }

  async function doLogout(){
    try{
      const ret = window.location.pathname + window.location.search;
      window.location.href = `/auth/logout?return=${encodeURIComponent(ret||'/')}`;
    }catch{ window.location.href = `/auth/logout?return=/`; }
  }

  async function initUsers(){ try{ const u=(window.S5Const?.ENDPOINTS?.USERS)||'/api/users'; const r = await fetch(u); if(!r.ok) return; const users = await r.json(); renderUsers(users); const def = users.find(u=>u.isDefault) || users[0]; if(def) selectUser(def.id, def.name); }catch{} }
  function renderUsers(users){ const list = document.getElementById('userList'); if(!list) return; list.innerHTML = users.map(u=>`<div class="flex items-center space-x-3 p-3 hover:bg-slate-700 rounded-lg cursor-pointer" onclick="window.__details.selectUser(${JSON.stringify(u.id)}, ${JSON.stringify(u.name||'User')})"><div class="w-8 h-8 bg-gradient-to-r from-purple-500 to-pink-500 rounded-full flex items-center justify-center">${(u.name||'U').slice(0,1).toUpperCase()}</div><div class="flex-1"><div class="text-white">${u.name||'User'}</div>${u.isDefault?'<div class="text-xs text-gray-400">Default<\/div>':''}</div></div>`).join(''); }
  async function createNewUser(){ const input = document.getElementById('newUserName'); const name = input?.value.trim(); if(!name) return; const u=(window.S5Const?.ENDPOINTS?.USERS)||'/api/users'; const r = await fetch(u, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ name }) }); if(r.ok){ await initUsers(); if(input) input.value=''; } }
  function selectUser(id, name){ currentUserId = id; const ini=document.getElementById('profileInitial'); const nm=document.getElementById('profileName'); if(ini) ini.textContent = (name||'U').slice(0,1).toUpperCase(); if(nm) nm.textContent = name || 'User'; const pm=document.getElementById('profileMenu'); if(pm) pm.classList.add('hidden'); if(currentAnimeId){ loadEntryState(); loadSimilar(currentAnimeId); } }

  async function loadAnime(id){ if(!id) return; const b=(window.S5Const?.ENDPOINTS?.ANIME_BASE)||'/api/anime'; const r = await fetch(`${b}/${encodeURIComponent(id)}`); if(!r.ok) return; const a = await r.json(); renderAnime(a); const st=document.getElementById('similarTitle'); if(st) st.textContent = `Similar to ${a.title || 'this'}`; }
  function renderAnime(a){
    const poster=document.getElementById('poster');
    if(poster) poster.src = a.coverUrl || a.image || a.poster || '/images/missing-cover.svg';

    const mainTitle = a.title || a.titleEnglish || a.titleRomaji || a.titleNative || a.name || 'Untitled';
    const t=document.getElementById('title'); if(t) t.textContent = mainTitle;

    const dot=document.getElementById('colorDot');
    if(dot){
      if(a.coverColorHex){ dot.style.backgroundColor = a.coverColorHex; dot.classList.remove('hidden'); }
      else { dot.classList.add('hidden'); }
    }

    // Alt titles
    const altsList = [a.titleEnglish, a.titleRomaji, a.titleNative]
      .map(x=>x||'')
      .filter(x=>x && x.toLowerCase() !== (mainTitle||'').toLowerCase());
    const alts=document.getElementById('alts'); if(alts) alts.textContent = altsList.length ? `Also known as: ${altsList.join(' • ')}` : '';

    // Meta (year • episodes • popularity • score)
    const meta=[];
    if(a.year) meta.push(a.year);
    if(a.episodes) meta.push(`${a.episodes} eps`);
    if(typeof a.popularity === 'number') meta.push(`${Math.round(a.popularity*100)}% pop`);
    // Compute display rating consistently with browse
    (function(){
      const stars = (window.S5Const?.RATING?.STARS) ?? 5;
      const minR = (window.S5Const?.RATING?.MIN) ?? 1;
      const maxR = (window.S5Const?.RATING?.MAX) ?? 5;
      const roundTo = (window.S5Const?.RATING?.ROUND_TO) ?? 10; // 1 decimal
      const def = (window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7;
      const baseScore = (typeof a.score === 'number') ? a.score : (typeof a.popularity === 'number' ? a.popularity : def);
      const computed = Math.max(minR, Math.min(maxR, Math.round(baseScore * stars * roundTo) / roundTo));
      if(!Number.isNaN(computed)) meta.push(`Score ${computed}★`);
    })();

    const m=document.getElementById('meta'); if(m) m.textContent = meta.join(' • ');

    const maxChips = (window.S5Const?.TAGS?.CHIPS_IN_DETAILS) ?? 12;
    const tags = Array.from(new Set([...(a.genres||[]), ...(a.tags||[])]));
    const tagsEl=document.getElementById('tags');
    if(tagsEl) tagsEl.innerHTML = tags.slice(0,maxChips).map(t=>`<span class="px-2 py-1 rounded bg-slate-800 text-gray-300 text-xs">${t}<\/span>`).join('');

    const syns = Array.isArray(a.synonyms) ? a.synonyms.filter(Boolean) : [];
    const synEl=document.getElementById('synonyms'); if(synEl) synEl.innerHTML = syns.slice(0,maxChips).map(s=>`<span class="px-2 py-1 rounded bg-slate-800 text-gray-400 text-xs">${s}<\/span>`).join('');

    const syn=document.getElementById('synopsis'); if(syn) syn.textContent = a.synopsis || a.description || '—';
    renderEntryState();
  }

  async function loadSimilar(id){ if(!currentUserId){ const c=document.getElementById('similar'); if(c) c.innerHTML=''; return; } const topK = (window.S5Const?.DETAILS?.SIMILAR_TOPK) ?? 12; const body = { userId: currentUserId, anchorAnimeId: id, topK, filters: { spoilerSafe: true } }; const rq=(window.S5Const?.ENDPOINTS?.RECS_QUERY)||'/api/recs/query'; const r = await fetch(rq, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) }); if(!r.ok){ const c=document.getElementById('similar'); if(c) c.innerHTML=''; return; } const { items } = await r.json(); const list = (items||[]).map(it => it?.anime || it).filter(Boolean).filter(a => a && a.id !== id); renderSimilar(list); }
  function renderSimilar(items){
    const c = document.getElementById('similar');
    if(!c) return;
    const maxChips = (window.S5Const?.TAGS?.CHIPS_IN_CARD) ?? 2;
    c.innerHTML = (items||[]).map(anime => `
        <div class="min-w-[180px] bg-slate-900 rounded-xl overflow-hidden hover:scale-[1.02] hover:shadow-xl transition-all cursor-pointer" onclick="window.__details.gotoDetails(${__q(anime.id)})">
          <img src="${anime.coverUrl || anime.image || anime.poster || '/images/missing-cover.svg'}" class="h-48 w-full object-cover" />
          <div class="p-3">
            <div class="text-sm font-semibold line-clamp-2">${anime.titleEnglish || anime.title || 'Untitled'}<\/div>
            <div class="text-xs text-gray-400 mt-1">${(anime.genres||[]).slice(0,maxChips).join(' • ')}<\/div>
          </div>
        <\/div>
      `).join('');
  }
  function gotoDetails(id){ const d=(window.S5Const?.PATHS?.DETAILS)||'details.html'; location.href = `${d}?id=${encodeURIComponent(id)}`; }

  // Actions
  async function markFavorite(){ if(!currentUserId || !currentAnimeId) return; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; await fetch(`${b}/${currentUserId}/${currentAnimeId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ favorite:true }) }); await loadEntryState(); window.showToast && showToast('Added to favorites','success'); }
  async function markWatched(){ if(!currentUserId || !currentAnimeId) return; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; await fetch(`${b}/${currentUserId}/${currentAnimeId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ watched:true }) }); await loadEntryState(); window.showToast && showToast('Marked as watched','success'); }
  async function rate(stars){ if(!currentUserId || !currentAnimeId) return; const r=(window.S5Const?.ENDPOINTS?.RATE)||'/api/recs/rate'; await fetch(r, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ userId: currentUserId, animeId: currentAnimeId, rating: stars }) }); await loadEntryState(); window.showToast && showToast(`Rated ${stars}★`,'success'); }

  async function loadEntryState(){ if(!currentUserId || !currentAnimeId){ currentEntry = null; renderEntryState(); return; } try{ const ps = (window.S5Const?.LIBRARY?.PAGE_SIZE) ?? 500; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; const r = await fetch(`${b}/${currentUserId}?sort=updatedAt&page=1&pageSize=${encodeURIComponent(ps)}`); if(!r.ok){ currentEntry = null; renderEntryState(); return; } const data = await r.json(); currentEntry = (data.items||[]).find(e=>e.animeId===currentAnimeId) || null; }catch{ currentEntry = null; } renderEntryState(); }
  function renderEntryState(){
    const favBtn = document.querySelector('button[data-action="favorite"]');
    const watchBtn = document.querySelector('button[data-action="watched"]');
    if(!favBtn || !watchBtn) return;
    const fav = !!(currentEntry && currentEntry.favorite);
    const w = !!(currentEntry && currentEntry.watched);
    favBtn.className = `bg-slate-800 hover:bg-slate-700 px-4 py-2 rounded-lg ${fav ? 'ring-1 ring-pink-400/40 bg-pink-900/30' : ''}`;
    watchBtn.className = `bg-slate-800 hover:bg-slate-700 px-4 py-2 rounded-lg ${w ? 'ring-1 ring-green-400/40 bg-green-900/30' : ''}`;
    // Refresh star bar active state
    const host = document.getElementById('rateButtons');
    if (host) {
      const curr = (currentEntry && typeof currentEntry.rating === 'number') ? currentEntry.rating : null;
      const bar = host.querySelector('.star-bar');
      if (bar) {
        if (curr != null) bar.setAttribute('data-current-rating', String(curr)); else bar.removeAttribute('data-current-rating');
        Array.from(bar.querySelectorAll('.star-btn')).forEach(btn => {
          const n = parseInt(btn.getAttribute('data-rating')||'0',10);
          if (curr && n <= curr) btn.classList.add('active'); else btn.classList.remove('active');
        });
      }
    }
  }

  // Bootstrap
  document.addEventListener('DOMContentLoaded', async () => {
    const qs = new URLSearchParams(location.search); currentAnimeId = qs.get('id');
    // Bind top menu buttons
    const backBtn = document.querySelector('button[data-action="go-home"]'); if(backBtn) backBtn.addEventListener('click', goHome);
  const profileBtn = document.getElementById('profileBtn'); if(profileBtn) profileBtn.addEventListener('click', toggleProfileMenu);

  // Login/Logout button hooks if present in this page's layout
  const loginBtn = document.getElementById('loginBtn'); if(loginBtn) loginBtn.addEventListener('click', openLogin);
  const logoutBtn = document.getElementById('logoutBtn'); if(logoutBtn) logoutBtn.addEventListener('click', doLogout);
  const addUserBtn = document.querySelector('button[data-action="create-user"]'); if(addUserBtn) addUserBtn.addEventListener('click', createNewUser);
    // Bind actions
    const favBtn = document.querySelector('button[data-action="favorite"]'); if(favBtn) favBtn.addEventListener('click', markFavorite);
    const watchBtn = document.querySelector('button[data-action="watched"]'); if(watchBtn) watchBtn.addEventListener('click', markWatched);
    // Build rating panel using the same star-bar UI as browse cards
    (function(){
      const host = document.getElementById('rateButtons');
      if(!host) return;
      const stars = (window.S5Const && window.S5Const.RATING && typeof window.S5Const.RATING.STARS === 'number') ? window.S5Const.RATING.STARS : 5;
      // Clear any previous content
      host.innerHTML = '';
      const bar = document.createElement('div');
      bar.className = 'star-bar flex items-center gap-1 bg-black/50 rounded-md px-2 py-1 border border-slate-700';
      bar.setAttribute('data-id', currentAnimeId ? String(currentAnimeId) : '');
      const curr = (currentEntry && typeof currentEntry.rating === 'number') ? currentEntry.rating : null;
      if (curr != null) bar.setAttribute('data-current-rating', String(curr));
      for (let n=1; n<=stars; n++){
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'star-btn text-xs text-gray-400' + ((curr && n <= curr) ? ' active' : '');
        btn.setAttribute('data-rating', String(n));
        btn.setAttribute('aria-label', `Rate ${n} ${n>1?'stars':'star'}`);
        btn.innerHTML = '<i class="fas fa-star"></i>';
        btn.addEventListener('click', () => rate(n));
        btn.addEventListener('mouseover', () => {
          const starsEls = Array.from(bar.querySelectorAll('.star-btn'));
          starsEls.forEach(el => {
            const r = parseInt(el.getAttribute('data-rating')||'0',10);
            if (r <= n) el.classList.add('hover'); else el.classList.remove('hover');
          });
        });
        btn.addEventListener('mouseout', (e) => {
          // Clear hover when leaving the entire bar
          if (!bar.contains(e.relatedTarget)) {
            bar.querySelectorAll('.star-btn.hover').forEach(el => el.classList.remove('hover'));
          }
        });
        bar.appendChild(btn);
      }
      host.appendChild(bar);
    })();

    // Expose callbacks referenced from generated HTML and profile list
    window.__details = { gotoDetails, selectUser };
    window.__details.createNewUser = createNewUser;

  await ensureAuthState();
  await initUsers();
    if(currentAnimeId){ await loadAnime(currentAnimeId); await loadEntryState(); await loadSimilar(currentAnimeId); }
  });
})();
