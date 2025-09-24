﻿// S5.Recs Details page controller (classic script)
(function(){
  // Safely quote string values for templates (kept local)
  function __q(v){ return "'" + String(v ?? '').replace(/'/g, "\\'") + "'"; }

  // State
  let currentUserId = null;
  const authState = { isAuthenticated: false, me: null };
  let currentMediaId = null;
  let currentEntry = null;

  function goHome(){ const h=(window.S5Const?.PATHS?.HOME)||'index.html'; location.href = h; }
  // No dropdown on details page; profile is read-only
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
    // Toggle Login/Logout buttons
    const loginBtn = document.getElementById('loginBtn');
    const logoutBtn = document.getElementById('logoutBtn');
    if(loginBtn && logoutBtn){ if(isAuth){ hide(loginBtn); show(logoutBtn);} else { show(loginBtn); hide(logoutBtn);} }
    // Update profile badge
    const ini=document.getElementById('profileInitial');
    const nm=document.getElementById('profileName');
    const display = isAuth ? (me?.displayName || me?.name || 'User') : 'Guest';
    if(ini) ini.textContent = (display||'U').slice(0,1).toUpperCase();
    if(nm) nm.textContent = display;
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

  // No user list on details page; always use authenticated user from /me

  async function loadMedia(id){ if(!id) return; const b=(window.S5Const?.ENDPOINTS?.MEDIA_BASE)||'/api/media'; const r = await fetch(`${b}/${encodeURIComponent(id)}`); if(!r.ok) return; const a = await r.json(); renderMedia(a); const st=document.getElementById('similarTitle'); if(st) st.textContent = `Similar to ${a.title || 'this'}`; }
  function renderMedia(a){
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

    const syn=document.getElementById('synopsis'); if(syn) syn.textContent = a.synopsis || a.description || '-';
    renderEntryState();
  }

  async function loadSimilar(id){
    console.log('[S5 Details] loadSimilar called with id:', id, 'currentUserId:', currentUserId);
    if(!id){
      console.log('[S5 Details] No media id provided, clearing similar section');
      const c=document.getElementById('similar');
      if(c) c.innerHTML='';
      return;
    }
    const topK = (window.S5Const?.DETAILS?.SIMILAR_TOPK) ?? 12;
    // Build request body - UserId is optional for content-based similar recommendations
    const body = {
      AnchorMediaId: id,
      TopK: topK,
      Filters: { SpoilerSafe: true }
    };
    // Include UserId if available for personalized filtering
    if(currentUserId) {
      body.UserId = currentUserId;
    }
    console.log('[S5 Details] Making similar recommendations request:', body);
    const rq=(window.S5Const?.ENDPOINTS?.RECS_QUERY)||'/api/recs/query';
    const r = await fetch(rq, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
    console.log('[S5 Details] Similar recommendations response status:', r.status);
    if(!r.ok){
      console.warn('[S5 Details] Similar recommendations request failed:', r.status, r.statusText);
      const c=document.getElementById('similar');
      if(c) c.innerHTML='';
      return;
    }
    const { items } = await r.json();
    console.log('[S5 Details] Similar recommendations received:', items?.length || 0, 'items');
    const list = (items||[]).map(it => it?.media || it).filter(Boolean).filter(a => a && a.id !== id);
    console.log('[S5 Details] Filtered similar list:', list.length, 'items');
    renderSimilar(list);
  }
  function renderSimilar(items){
    const c = document.getElementById('similar');
    if(!c) return;
    const maxChips = (window.S5Const?.TAGS?.CHIPS_IN_CARD) ?? 2;
    c.innerHTML = (items||[]).map(media => `
        <div class="min-w-[180px] bg-slate-900 rounded-xl overflow-hidden hover:scale-[1.02] hover:shadow-xl transition-all cursor-pointer" onclick="window.__details.gotoDetails(${__q(media.id)})">
          <img src="${media.coverUrl || media.image || media.poster || '/images/missing-cover.svg'}" class="h-48 w-full object-cover" />
          <div class="p-3">
            <div class="text-sm font-semibold line-clamp-2">${media.titleEnglish || media.title || 'Untitled'}<\/div>
            <div class="text-xs text-gray-400 mt-1">${(media.genres||[]).slice(0,maxChips).join(' • ')}<\/div>
          </div>
        <\/div>
      `).join('');
  }
  function gotoDetails(id){ const d=(window.S5Const?.PATHS?.DETAILS)||'details.html'; location.href = `${d}?id=${encodeURIComponent(id)}`; }

  // Actions
  async function markFavorite(){ if(!authState.isAuthenticated){ window.showToast && showToast('Please login first','warning'); return;} if(!currentUserId || !currentMediaId) return; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; await fetch(`${b}/${currentUserId}/${currentMediaId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ favorite:true }) }); await loadEntryState(); window.showToast && showToast('Added to favorites','success'); }
  async function markWatched(){ if(!authState.isAuthenticated){ window.showToast && showToast('Please login first','warning'); return;} if(!currentUserId || !currentMediaId) return; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; await fetch(`${b}/${currentUserId}/${currentMediaId}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ watched:true }) }); await loadEntryState(); window.showToast && showToast('Marked as watched','success'); }
  async function rate(stars){ if(!authState.isAuthenticated){ window.showToast && showToast('Please login first','warning'); return;} if(!currentUserId || !currentMediaId) return; const r=(window.S5Const?.ENDPOINTS?.RATE)||'/api/recs/rate'; await fetch(r, { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ userId: currentUserId, mediaId: currentMediaId, rating: stars }) }); await loadEntryState(); window.showToast && showToast(`Rated ${stars}★`,'success'); }

  async function loadEntryState(){ if(!currentUserId || !currentMediaId){ currentEntry = null; renderEntryState(); return; } try{ const ps = (window.S5Const?.LIBRARY?.PAGE_SIZE) ?? 500; const b=(window.S5Const?.ENDPOINTS?.LIBRARY_BASE)||'/api/library'; const r = await fetch(`${b}/${currentUserId}?sort=updatedAt&page=1&pageSize=${encodeURIComponent(ps)}`); if(!r.ok){ currentEntry = null; renderEntryState(); return; } const data = await r.json(); currentEntry = (data.items||[]).find(e=>e.mediaId===currentMediaId) || null; }catch{ currentEntry = null; } renderEntryState(); }
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
    const qs = new URLSearchParams(location.search); currentMediaId = qs.get('id');
    // Bind top menu buttons
    const backBtn = document.querySelector('button[data-action="go-home"]'); if(backBtn) backBtn.addEventListener('click', goHome);
    // Login/Logout button hooks if present in this page's layout
    const loginBtn = document.getElementById('loginBtn'); if(loginBtn) loginBtn.addEventListener('click', openLogin);
    const logoutBtn = document.getElementById('logoutBtn'); if(logoutBtn) logoutBtn.addEventListener('click', doLogout);
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
      bar.setAttribute('data-id', currentMediaId ? String(currentMediaId) : '');
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

  // Expose minimal callbacks referenced from generated HTML
  window.__details = { gotoDetails };

  await ensureAuthState();
    if(currentMediaId){ await loadMedia(currentMediaId); await loadEntryState(); await loadSimilar(currentMediaId); }
  });
})();
