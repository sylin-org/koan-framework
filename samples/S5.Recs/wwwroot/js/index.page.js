// Page controller for S5.Recs index.html (classic script)
// Moves inline logic out of the HTML and wires into existing modules.

(function(){
  // Hoist globals the page previously defined to keep behavior unchanged
  let currentUserId = null;
  let currentTab = 'discover';
  let animeData = [];
  let filteredData = [];
  let currentView = 'forYou';
  let currentLayout = 'grid';
  let libraryByAnimeId = {};
  const PAGE_SIZE = (window.S5Config && window.S5Config.PAGE_SIZE) || 100;
  let currentRecsTopK = PAGE_SIZE;
  let currentLibraryPage = 1;
  let selectedPreferredTags = window.selectedPreferredTags || [];
  let recsSettings = { preferTagsWeight: 0.2, maxPreferredTags: 3, diversityWeight: 0.1 };

  // Expose for other modules relying on globals
  Object.assign(window, {
    currentUserId, currentTab, animeData, filteredData, currentView, currentLayout,
    libraryByAnimeId, PAGE_SIZE, currentRecsTopK, currentLibraryPage, selectedPreferredTags, recsSettings
  });

  document.addEventListener('DOMContentLoaded', () => {
    initUsers();
    loadRecsSettings();
    if(window.S5Tags && S5Tags.loadTags){ S5Tags.loadTags().then(()=> S5Tags.renderPreferredChips && S5Tags.renderPreferredChips()); }
    setupEventListeners();
  });

  function setupEventListeners(){
    Dom.on('globalSearch', 'input', handleGlobalSearch);
    Dom.on('sortBy', 'change', applySortAndFilters);
    Dom.on('gridViewBtn', 'click', () => setViewMode('grid'));
    Dom.on('listViewBtn', 'click', () => setViewMode('list'));
    // Tags UI
    const chips = Dom.$('preferChips');
    if (chips){
      chips.addEventListener('click', (e)=>{
        const btn = e.target.closest('[data-tag]');
        if(!btn) return;
        const tag = btn.getAttribute('data-tag');
        if(window.S5Tags && S5Tags.togglePreferredTag) S5Tags.togglePreferredTag(tag);
      });
    }
    Dom.on('expandTagsBtn', 'click', ()=>{
      const p = Dom.$('allTagsPanel');
      const open = p.classList.toggle('hidden');
      Dom.$('expandTagsBtn').textContent = open ? 'Expand' : 'Collapse';
      if(!open && window.S5Tags && S5Tags.renderAllTags){ S5Tags.renderAllTags(); }
    });
    Dom.on('tagsSort', 'change', ()=> S5Tags.renderAllTags && S5Tags.renderAllTags());
    Dom.on('tagsSearch', 'input', ()=> S5Tags.renderAllTags && S5Tags.renderAllTags());
    const allTagsList = Dom.$('allTagsList');
    if(allTagsList){
      allTagsList.addEventListener('click', (e)=>{
        const btn = e.target.closest('[data-tag]');
        if(!btn) return;
        const tag = btn.getAttribute('data-tag');
        if(window.S5Tags && S5Tags.togglePreferredTag){ S5Tags.togglePreferredTag(tag); if(S5Tags.renderAllTags) S5Tags.renderAllTags(); }
      });
    }

    // Filters
    ;['genreFilter', 'ratingFilter', 'yearFilter', 'episodeFilter'].forEach(id => Dom.on(id, 'change', applyFilters));

    // Outside clicks close menus
    document.addEventListener('click', (e) => {
      const profileClickedInside = e.target.closest('#profileMenu') || e.target.closest('#profileButton');
      if (!profileClickedInside) Dom.$('profileMenu').classList.add('hidden');
      const adminClickedInside = e.target.closest('#adminMenu') || e.target.closest('#adminButton');
      if (!adminClickedInside) Dom.$('adminMenu').classList.add('hidden');
    });

    Dom.on('forYouBtn', 'click', () => setViewSource('forYou'));
    Dom.on('libraryBtn', 'click', () => setViewSource('library'));
  }

  // Menus
  window.toggleProfileMenu = function(){ Dom.$('profileMenu').classList.toggle('hidden'); };
  window.toggleAdminMenu = function(){ Dom.$('adminMenu').classList.toggle('hidden'); };

  // Users
  async function initUsers(attempt=0){
    try{
      const users = await (window.S5Api && window.S5Api.getUsers ? window.S5Api.getUsers() : []);
      renderUsers(users);
      const def = users.find(u=>u.isDefault) || users[0];
      if(def){ selectUser(def.id, def.name); }
    }catch(e){
      if(attempt < 5){ setTimeout(()=>initUsers(attempt+1), Math.min(8000, (attempt+1)*1000)); }
    }
  }
  function renderUsers(users){
    const list = Dom.$('userList'); if(!list) return;
    list.innerHTML = users.map(u=>`<div class=\"flex items-center space-x-3 p-3 hover:bg-slate-700 rounded-lg cursor-pointer\" onclick=\"selectUser(${JSON.stringify(u.id)}, ${JSON.stringify(u.name||'User')})\">\n        <div class=\"w-8 h-8 bg-gradient-to-r from-purple-500 to-pink-500 rounded-full flex items-center justify-center\">${(u.name||'U').slice(0,1).toUpperCase()}</div>\n        <div class=\"flex-1\"><div class=\"text-white\">${u.name}</div>${u.isDefault?'<div class="text-xs text-gray-400">Default</div>':''}</div>\n      </div>`).join('');
  }
  window.createNewUser = async function(){
    const input = Dom.$('newUserName');
    const name = input?.value.trim();
    if(!name) return;
    try{ await (window.S5Api && window.S5Api.createUser ? window.S5Api.createUser(name) : Promise.resolve(null)); await initUsers(); input.value=''; showToast('User created', 'success'); }
    catch{ showToast('Failed to create user', 'error'); }
  };
  window.selectUser = function(id, name){
    window.currentUserId = id;
    Dom.text('currentProfileInitial', (name||'U').slice(0,1).toUpperCase());
    Dom.text('currentProfileName', name || 'User');
    Dom.text('bannerProfileInitial', (name||'U').slice(0,1).toUpperCase());
    Dom.text('bannerProfileName', `Welcome back, ${name||'User'}!`);
    loadUserStats();
    Dom.$('profileMenu').classList.add('hidden');
    window.currentRecsTopK = PAGE_SIZE; window.currentLibraryPage = 1; Dom.$('moreBtn')?.classList.add('hidden');
    refreshLibraryState().finally(() => loadAnimeData());
  };
  async function loadUserStats(){
    if(!window.currentUserId) return;
    try{ const s = await (window.S5Api && window.S5Api.getUserStats ? window.S5Api.getUserStats(window.currentUserId) : null); if(s) Dom.text('profileStats', `${s.favorites} favorites • ${s.watched} watched`); }
    catch{}
  }

  // Settings
  async function loadRecsSettings(){
    try{ const s = await (window.S5Api && window.S5Api.getRecsSettings ? window.S5Api.getRecsSettings() : null); if(s){
      window.recsSettings = { preferTagsWeight: s.preferTagsWeight ?? 0.2, maxPreferredTags: s.maxPreferredTags ?? 3, diversityWeight: s.diversityWeight ?? 0.1 };
      const w = Dom.$('preferWeight'); if(w) w.value = window.recsSettings.preferTagsWeight;
      Dom.text('preferWeightVal', String(window.recsSettings.preferTagsWeight));
      window.S5Tags && window.S5Tags.renderPreferredChips();
    }}catch{}
  }

  // Library cache
  async function refreshLibraryState(){
    if(!window.currentUserId){ window.libraryByAnimeId = {}; return; }
    try{
      const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page:1, pageSize:500 }) : null);
      if(!data){ window.libraryByAnimeId = {}; return; }
      const map = {};
      for(const e of (data.items||[])) map[e.animeId] = { favorite: !!e.favorite, watched: !!e.watched, dropped: !!e.dropped, rating: e.rating ?? null, updatedAt: e.updatedAt };
      window.libraryByAnimeId = map;
    }catch{ window.libraryByAnimeId = {}; }
  }
  window.refreshLibraryState = refreshLibraryState;

  // Tabs
  window.setActiveTab = function(tab){
    window.currentTab = tab;
    document.querySelectorAll('nav a').forEach(link => {
      link.className = link.className.replace('text-white border-b-2 border-purple-500', 'text-gray-400 border-b-2 border-transparent hover:border-gray-600');
    });
    event.target.className = 'text-white border-b-2 border-purple-500 pb-1 px-1';
    const titles = { discover: 'Discover Anime', trending: 'Trending Now', 'top-rated': 'Top Rated Anime', watchlist: 'My Watchlist' };
    Dom.text('contentTitle', titles[tab] || 'Browse Anime');
    loadContentForTab(tab);
  };

  // Data loading
  window.loadAnimeData = async function(){
    if(!window.currentUserId){ displayAnime([]); return; }
    try{
      const recs = await fetchRecommendations({ text: '', topK: window.currentRecsTopK });
      window.animeData = recs; window.filteredData = [...window.animeData]; displayAnime(window.filteredData);
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ displayAnime([]); Dom.$('moreBtn')?.classList.add('hidden'); showToast('Failed to load recommendations', 'error'); }
  };

  window.fetchRecommendations = async function({ text, topK = PAGE_SIZE }){
    const weight = parseFloat(Dom.$('preferWeight')?.value || '0.2');
    const genre = Dom.val('genreFilter') || '';
    const episodeSel = Dom.val('episodeFilter') || '';
    const episodesMax = episodeSel === 'short' ? 12 : (episodeSel === 'medium' ? 25 : (episodeSel === 'long' ? 9999 : null));
    const genres = []; if(genre) genres.push(genre);
    let preferTags = Array.isArray(window.selectedPreferredTags) ? [...window.selectedPreferredTags] : [];
    const max = window.recsSettings?.maxPreferredTags ?? 3; if(preferTags.length > max) preferTags = preferTags.slice(0, max);
    const body = { text: text || '', topK, filters: { genres, episodesMax, spoilerSafe: true, preferTags, preferWeight: weight }, userId: window.currentUserId };
    const data = await (window.S5Api && window.S5Api.recsQuery ? window.S5Api.recsQuery(body) : null) || { items: [] };
    const items = Array.isArray(data.items) ? data.items : [];
    const mapped = items.map(it => mapItemToAnime(it));
    return mapped.filter(a => !window.libraryByAnimeId[a.id]);
  };

  function mapItemToAnime(item){
    const a = item.anime || item;
    const score = typeof item.score === 'number' ? item.score : (typeof a.popularity === 'number' ? a.popularity : 0.7);
    const computedRating = Math.max(1, Math.min(5, Math.round(score * 5 * 10) / 10));
    return { id:a.id, title: a.titleEnglish || a.title || a.titleRomaji || a.titleNative || 'Untitled', englishTitle:a.titleEnglish||'', coverUrl:a.coverUrl||'', backdrop:a.bannerUrl||a.coverUrl||'', rating:computedRating, popularity:a.popularity||0, episodes:a.episodes||0, type:a.type||'TV', year:a.year||'', status:a.status||'', studio:a.studio||'', source:a.source||'', synopsis:a.synopsis||'', genres: Array.isArray(a.genres)? a.genres : (Array.isArray(a.tags)? a.tags: []) };
  }

  function loadContentForTab(tab){
    switch(tab){
      case 'trending': window.filteredData = window.animeData.sort((a,b)=>(b.popularity||0)-(a.popularity||0)).slice(0,24); break;
      case 'top-rated': window.filteredData = window.animeData.sort((a,b)=> b.rating-a.rating).slice(0,24); break;
      case 'watchlist': loadWatchlist(); return;
      default: displayAnime(window.animeData); return;
    }
    displayAnime(window.filteredData);
  }

  // Display
  function displayAnime(list){
    const grid = Dom.$('animeGrid'); const loading = Dom.$('loadingState'); const empty = Dom.$('emptyState');
    loading.style.display = 'none';
    if(!list || list.length===0){ grid.classList.add('hidden'); empty.classList.remove('hidden'); Dom.text('resultCount', 'No results'); return; }
    grid.classList.remove('hidden'); empty.classList.add('hidden'); Dom.text('resultCount', `Showing ${list.length} results`);
    const state = { libraryByAnimeId: window.libraryByAnimeId, selectedPreferredTags: window.selectedPreferredTags };
    if(window.currentLayout==='list'){ grid.className = 'space-y-4'; grid.innerHTML = list.map(a=>window.S5Cards.createAnimeListItem(a, state)).join(''); }
    else { grid.className = 'grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-6'; grid.innerHTML = list.map(a=>window.S5Cards.createAnimeCard(a, state)).join(''); }
  }
  window.displayAnime = displayAnime;

  // Search & filters
  async function handleGlobalSearch(e){
    const query = e.target.value;
    if(!query || query.trim().length===0){ if(window.currentView==='forYou'){ window.currentRecsTopK = PAGE_SIZE; } Dom.$('moreBtn')?.classList.add('hidden'); loadContentForTab(window.currentTab); return; }
    if(window.currentView==='library'){
      window.filteredData = (window.S5Filters && S5Filters.search) ? S5Filters.search(window.animeData, query) : window.animeData;
      displayAnime(window.filteredData); return;
    }
    try{
      window.currentRecsTopK = PAGE_SIZE;
      const recs = await window.fetchRecommendations({ text: query, topK: window.currentRecsTopK });
      window.animeData = recs; window.filteredData = [...window.animeData]; displayAnime(window.filteredData);
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ Dom.$('moreBtn')?.classList.add('hidden'); showToast('Search failed', 'error'); }
  }
  window.applyFilters = function(){
    const genre = Dom.val('genreFilter'); const rating = Dom.val('ratingFilter'); const year = Dom.val('yearFilter'); const episode = Dom.val('episodeFilter');
    window.filteredData = (window.S5Filters && S5Filters.filter) ? S5Filters.filter(window.animeData, { genre, rating, year, episode }) : [...window.animeData];
    applySortAndFilters();
  };
  window.applySortAndFilters = function(){ const sortBy = Dom.val('sortBy'); window.filteredData = (window.S5Filters && S5Filters.sort) ? S5Filters.sort(window.filteredData, sortBy) : [...window.filteredData]; displayAnime(window.filteredData); };
  window.clearFilters = function(){ Dom.clearValues(['genreFilter','ratingFilter','yearFilter','episodeFilter']); window.applyFilters(); };

  // View state
  window.setViewMode = function(mode){ const gridBtn=Dom.$('gridViewBtn'); const listBtn=Dom.$('listViewBtn'); if(mode==='grid'){ gridBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; listBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } else { listBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; gridBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } window.currentLayout = mode; displayAnime(window.filteredData); };
  window.setViewSource = function(mode){ if(mode===window.currentView) return; window.currentView = mode; const fy=Dom.$('forYouBtn'); const lb=Dom.$('libraryBtn'); if(window.currentView==='forYou'){ fy.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; lb.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; window.loadAnimeData(); } else { lb.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; fy.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; loadLibrary(); } };

  // Navigation
  window.openDetails = function(animeId){ window.location.href = `details.html?id=${encodeURIComponent(animeId)}`; };

  // Actions
  window.openQuickRate = async function(animeId, rating){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.postRate ? window.S5Api.postRate(window.currentUserId, animeId, rating) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast(`Rated ${rating}★`, 'success'); }catch{ showToast('Failed to rate', 'error'); } };
  window.toggleFavorite = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { favorite:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Added to favorites', 'success'); }catch{ showToast('Failed to update favorites', 'error'); } };
  window.markWatched = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { watched:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Marked as watched', 'success'); }catch{ showToast('Failed to mark watched', 'error'); } };
  window.markDropped = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { dropped:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Dropped from list', 'success'); }catch{ showToast('Failed to update entry', 'error'); } };

  async function loadWatchlist(){
    if(!window.currentUserId){ displayAnime([]); return; }
    try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { status:'watched', sort:'updatedAt', page:1, pageSize:100 }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.animeId).filter(Boolean); if(ids.length===0){ displayAnime([]); return; } const arr = await (window.S5Api && window.S5Api.getAnimeByIds ? window.S5Api.getAnimeByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToAnime({ anime: a, score: a.popularity || 0.7 })); displayAnime(mapped); }catch{ displayAnime([]); showToast('Failed to load watchlist', 'error'); }
  }
  async function loadLibrary(){
    if(!window.currentUserId){ displayAnime([]); return; }
    try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page: window.currentLibraryPage, pageSize: PAGE_SIZE }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.animeId).filter(Boolean); if(window.currentLibraryPage===1 && ids.length===0){ displayAnime([]); Dom.$('moreBtn')?.classList.add('hidden'); return; } const arr = await (window.S5Api && window.S5Api.getAnimeByIds ? window.S5Api.getAnimeByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToAnime({ anime: a, score: a.popularity || 0.7 })); if(window.currentLibraryPage===1){ window.animeData = mapped; } else { const seen = new Set(window.animeData.map(x=>x.id)); for(const m of mapped){ if(!seen.has(m.id)) window.animeData.push(m); } } window.filteredData = [...window.animeData]; for(const e of (data.items||[])){ window.libraryByAnimeId[e.animeId] = { favorite: !!e.favorite, watched: !!e.watched, dropped: !!e.dropped, rating: e.rating ?? null, updatedAt: e.updatedAt }; } displayAnime(window.filteredData); const btn = Dom.$('moreBtn'); if(btn){ if((data.items||[]).length === PAGE_SIZE) btn.classList.remove('hidden'); else btn.classList.add('hidden'); } }catch{ displayAnime([]); showToast('Failed to load library', 'error'); }
  }
  window.loadLibrary = loadLibrary;

  window.loadMore = async function(){ if(window.currentView==='forYou'){ const inc = (window.S5Config && window.S5Config.MORE_INCREMENT) || PAGE_SIZE; window.currentRecsTopK += inc; await window.loadAnimeData(); } else { window.currentLibraryPage += 1; await loadLibrary(); } };

  window.removeCardFromForYou = function(animeId){ if(window.currentView!=='forYou') return; window.animeData = window.animeData.filter(a=>a.id!==animeId); window.filteredData = window.filteredData.filter(a=>a.id!==animeId); const grid = Dom.$('animeGrid'); const card = grid?.querySelector(`[data-anime-id="${CSS.escape(String(animeId))}"]`); if(card){ card.style.transition='opacity 250ms ease-out, transform 250ms ease-out'; card.style.opacity='0'; card.style.transform='scale(0.96)'; setTimeout(()=>{ card.remove(); Dom.text('resultCount', `Showing ${window.filteredData.length} results`); const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); } }, 260); } else { displayAnime(window.filteredData); } };
})();
