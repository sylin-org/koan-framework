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
  let recsSettings = { 
    preferTagsWeight: (window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2, 
    maxPreferredTags: (window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3, 
    diversityWeight: (window.S5Const?.RECS?.DEFAULT_DIVERSITY_WEIGHT) ?? 0.1 
  };

  // Expose for other modules relying on globals
  Object.assign(window, {
    currentUserId, currentTab, animeData, filteredData, currentView, currentLayout,
    libraryByAnimeId, PAGE_SIZE, currentRecsTopK, currentLibraryPage, selectedPreferredTags, recsSettings
  });

  document.addEventListener('DOMContentLoaded', () => {
    // Enforce slider config from constants
    const w = Dom.$('preferWeight');
    if (w && window.S5Const?.RECS) {
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_MIN === 'number') w.min = String(window.S5Const.RECS.PREFER_WEIGHT_MIN);
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_MAX === 'number') w.max = String(window.S5Const.RECS.PREFER_WEIGHT_MAX);
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_STEP === 'number') w.step = String(window.S5Const.RECS.PREFER_WEIGHT_STEP);
      if (typeof window.recsSettings?.preferTagsWeight === 'number') w.value = String(window.recsSettings.preferTagsWeight);
    }
    initUsers();
    loadRecsSettings();
    if(window.S5Tags && S5Tags.loadTags){ S5Tags.loadTags().then(()=> S5Tags.renderPreferredChips && S5Tags.renderPreferredChips()); }
  // Populate Genre filter from backend catalog
  populateGenres();
  setupEventListeners();
  });

  function setupEventListeners(){
    Dom.on('globalSearch', 'input', handleGlobalSearch);
  Dom.on('sortBy', 'change', async () => {
      // For recommendation-backed views, re-fetch so server applies ordering
      if (window.currentView === 'forYou') {
        window.currentRecsTopK = PAGE_SIZE;
        await window.loadAnimeData();
      } else if (window.currentView === 'freeBrowsing') {
        window.currentRecsTopK = PAGE_SIZE;
        await window.loadFreeBrowsingData();
      } else {
        // Library is client-side; just re-apply
        applySortAndFilters();
      }
    });
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
    const t = window.S5Const?.TEXT;
    Dom.$('expandTagsBtn').textContent = open ? (t?.EXPAND || 'Expand') : (t?.COLLAPSE || 'Collapse');
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

  // Debounced Tag Boost slider: re-render chips immediately for visual cue; reload data after debounce
    const weightSlider = Dom.$('preferWeight');
  if (weightSlider) {
      let tId = null;
      weightSlider.addEventListener('input', () => {
        // Update chip visuals immediately
    if (window.S5Tags && typeof S5Tags.renderPreferredChips === 'function') S5Tags.renderPreferredChips();
    if (window.S5Tags && typeof S5Tags.renderAllTags === 'function') S5Tags.renderAllTags();
        // Debounce data reloads
        if (tId) clearTimeout(tId);
    tId = setTimeout(() => {
          if (window.S5Tags && typeof S5Tags.onPreferredChanged === 'function') S5Tags.onPreferredChanged();
    }, (window.S5Const?.RECS?.TAG_BOOST_DEBOUNCE_MS) ?? 50);
      });
    }

    // Filters
  // Genre/Episode remain selects; rating/year are dual-range sliders
  ;['genreFilter', 'episodeFilter'].forEach(id => Dom.on(id, 'change', applyFilters));
  initDualRangeControls();

    // Outside clicks close menus
    document.addEventListener('click', (e) => {
      const profileClickedInside = e.target.closest('#profileMenu') || e.target.closest('#profileButton');
      if (!profileClickedInside) Dom.$('profileMenu').classList.add('hidden');
      const adminClickedInside = e.target.closest('#adminMenu') || e.target.closest('#adminButton');
      if (!adminClickedInside) Dom.$('adminMenu').classList.add('hidden');
    });

    Dom.on('forYouBtn', 'click', () => setViewSource('forYou'));
    Dom.on('freeBrowsingBtn', 'click', () => setViewSource('freeBrowsing'));
    Dom.on('libraryBtn', 'click', () => setViewSource('library'));
  }

  async function populateGenres(){
    try{
      if(!(window.S5Api && typeof window.S5Api.getGenres==='function')) return;
      const items = await window.S5Api.getGenres('alpha'); // [{ genre, count }]
      const sel = document.getElementById('genreFilter'); if(!sel) return;
      const cur = sel.value || '';
      // Reset to default option
      sel.innerHTML = '<option value="">All Genres</option>';
      (items||[]).forEach(it=>{
        if(!it || !it.genre) return;
        const opt = document.createElement('option');
        opt.value = it.genre; opt.textContent = it.genre;
        sel.appendChild(opt);
      });
      // restore selection if still present
      if(cur && Array.from(sel.options).some(o=>o.value===cur)) sel.value = cur;
    }catch{}
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
      if(attempt < 5){ 
        const base = (window.S5Const?.INIT?.RETRY_BASE_MS) ?? 1000;
        const maxMs = (window.S5Const?.INIT?.RETRY_MAX_MS) ?? 8000;
        setTimeout(()=>initUsers(attempt+1), Math.min(maxMs, (attempt+1)*base)); 
      }
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
      window.recsSettings = { 
        preferTagsWeight: s.preferTagsWeight ?? ((window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2), 
        maxPreferredTags: s.maxPreferredTags ?? ((window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3), 
        diversityWeight: s.diversityWeight ?? ((window.S5Const?.RECS?.DEFAULT_DIVERSITY_WEIGHT) ?? 0.1) 
      };
  const w = Dom.$('preferWeight'); if(w) w.value = window.recsSettings.preferTagsWeight;
  window.S5Tags && window.S5Tags.renderPreferredChips();
    }}catch{}
  }

  // Small helper to read current weight for other modules
  window.getCurrentPreferWeight = function(){
  return parseFloat(Dom.$('preferWeight')?.value || String(window.recsSettings?.preferTagsWeight ?? ((window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2)));
  };

  // Library cache
  async function refreshLibraryState(){
    if(!window.currentUserId){ window.libraryByAnimeId = {}; return; }
    try{
  const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page:1, pageSize:(window.S5Const?.LIBRARY?.PAGE_SIZE) ?? 500 }) : null);
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
  window.animeData = recs; window.filteredData = [...window.animeData];
  // Apply current sort selection after loading
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); }
  else { displayAnime(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ displayAnime([]); Dom.$('moreBtn')?.classList.add('hidden'); showToast('Failed to load recommendations', 'error'); }
  };

  window.loadFreeBrowsingData = async function(){
    try{
      const recs = await fetchRecommendations({ text: '', topK: window.currentRecsTopK, ignoreUserPreferences: true });
  window.animeData = recs; window.filteredData = [...window.animeData];
  // Apply current sort selection after loading
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); }
  else { displayAnime(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ displayAnime([]); Dom.$('moreBtn')?.classList.add('hidden'); showToast('Failed to load content', 'error'); }
  };

  window.fetchRecommendations = async function({ text, topK = PAGE_SIZE, ignoreUserPreferences = false }){
    const weight = parseFloat(Dom.$('preferWeight')?.value || String((window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2));
    const genre = Dom.val('genreFilter') || '';
    const episodeSel = Dom.val('episodeFilter') || '';
    const episodesMax = episodeSel === 'short' 
      ? ((window.S5Const?.EPISODES?.SHORT_MAX) ?? 12)
      : (episodeSel === 'medium' 
        ? ((window.S5Const?.EPISODES?.MEDIUM_MAX) ?? 25)
        : (episodeSel === 'long' 
          ? ((window.S5Const?.EPISODES?.LONG_MAX) ?? 9999)
          : null));
    const genres = []; if(genre) genres.push(genre);
    let preferTags = Array.isArray(window.selectedPreferredTags) ? [...window.selectedPreferredTags] : [];
    const max = window.recsSettings?.maxPreferredTags ?? ((window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3); if(preferTags.length > max) preferTags = preferTags.slice(0, max);
    const sort = Dom.val('sortBy') || null;
    
    const body = { 
      text: text || '', 
      topK, 
      sort,
      filters: { 
        genres, 
        episodesMax, 
        spoilerSafe: true, 
        preferTags, 
        preferWeight: weight 
      }, 
      userId: ignoreUserPreferences ? null : window.currentUserId 
    };
    
    const data = await (window.S5Api && window.S5Api.recsQuery ? window.S5Api.recsQuery(body) : null) || { items: [] };
    const items = Array.isArray(data.items) ? data.items : [];
    const mapped = items.map(it => mapItemToAnime(it));
    
    // In free browsing mode, don't filter out library items since user preferences are ignored
    if (ignoreUserPreferences) {
      return mapped;
    }
    
    return mapped.filter(a => !window.libraryByAnimeId[a.id]);
  };

  function mapItemToAnime(item){
  const a = item.anime || item;
  const score = typeof item.score === 'number' ? item.score : (typeof a.popularity === 'number' ? a.popularity : ((window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7));
  const stars = (window.S5Const?.RATING?.STARS) ?? 5;
  const minR = (window.S5Const?.RATING?.MIN) ?? 0;
  const maxR = (window.S5Const?.RATING?.MAX) ?? 5;
  const roundTo = (window.S5Const?.RATING?.ROUND_TO) ?? 10;
  const computedRating = Math.max(minR, Math.min(maxR, Math.round(score * stars * roundTo) / roundTo));
    return { id:a.id, title: a.titleEnglish || a.title || a.titleRomaji || a.titleNative || 'Untitled', englishTitle:a.titleEnglish||'', coverUrl:a.coverUrl||'', backdrop:a.bannerUrl||a.coverUrl||'', rating:computedRating, popularity:a.popularity||0, episodes:a.episodes||0, type:a.type||'TV', year:a.year||'', status:a.status||'', studio:a.studio||'', source:a.source||'', synopsis:a.synopsis||'', genres: Array.isArray(a.genres)? a.genres : (Array.isArray(a.tags)? a.tags: []) };
  }

  function loadContentForTab(tab){
    switch(tab){
      case 'trending': window.filteredData = window.animeData.sort((a,b)=>(b.popularity||0)-(a.popularity||0)).slice(0, ((window.S5Const && window.S5Const.UI && typeof window.S5Const.UI.PREVIEW_SECTION_COUNT === 'number') ? window.S5Const.UI.PREVIEW_SECTION_COUNT : 24)); break;
      case 'top-rated': window.filteredData = window.animeData.sort((a,b)=> b.rating-a.rating).slice(0, ((window.S5Const && window.S5Const.UI && typeof window.S5Const.UI.PREVIEW_SECTION_COUNT === 'number') ? window.S5Const.UI.PREVIEW_SECTION_COUNT : 24)); break;
      case 'watchlist': loadWatchlist(); return;
      default: displayAnime(window.animeData); return;
    }
    displayAnime(window.filteredData);
  }

  // Display
  function displayAnime(list){
    const grid = Dom.$('animeGrid'); const loading = Dom.$('loadingState'); const empty = Dom.$('emptyState');
    loading.style.display = 'none';
    if(!list || list.length===0){ grid.classList.add('hidden'); empty.classList.remove('hidden'); Dom.text('resultCount', (window.S5Const?.TEXT?.NO_RESULTS) || 'No results'); return; }
    grid.classList.remove('hidden'); empty.classList.add('hidden');
    const tp = (window.S5Const?.TEXT?.RESULTS_PREFIX) || 'Showing ';
    const ts = (window.S5Const?.TEXT?.RESULTS_SUFFIX) || ' results';
    Dom.text('resultCount', `${tp}${list.length}${ts}`);
    const state = { libraryByAnimeId: window.libraryByAnimeId, selectedPreferredTags: window.selectedPreferredTags };
    if(window.currentLayout==='list'){ grid.className = 'space-y-4'; grid.innerHTML = list.map(a=>window.S5Cards.createAnimeListItem(a, state)).join(''); }
    else { grid.className = 'grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-6'; grid.innerHTML = list.map(a=>window.S5Cards.createAnimeCard(a, state)).join(''); }
  }
  window.displayAnime = displayAnime;

  // Search & filters
  async function handleGlobalSearch(e){
    const query = e.target.value;
    if(!query || query.trim().length===0){ 
      if(window.currentView==='forYou' || window.currentView==='freeBrowsing'){ 
        window.currentRecsTopK = PAGE_SIZE; 
      } 
      Dom.$('moreBtn')?.classList.add('hidden'); 
      loadContentForTab(window.currentTab); 
      return; 
    }
    if(window.currentView==='library'){
      window.filteredData = (window.S5Filters && S5Filters.search) ? S5Filters.search(window.animeData, query) : window.animeData;
  // Respect current sort on search within library
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayAnime(window.filteredData); }
  return;
    }
    try{
      window.currentRecsTopK = PAGE_SIZE;
      const isFreeBrowsing = window.currentView === 'freeBrowsing';
      const recs = await window.fetchRecommendations({ text: query, topK: window.currentRecsTopK, ignoreUserPreferences: isFreeBrowsing });
  window.animeData = recs; window.filteredData = [...window.animeData];
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayAnime(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ Dom.$('moreBtn')?.classList.add('hidden'); showToast('Search failed', 'error'); }
  }
  window.applyFilters = function(){
    const genre = Dom.val('genreFilter');
    const episode = Dom.val('episodeFilter');
    const { ratingMin, ratingMax, yearMin, yearMax } = readDualRangeValues();
    window.filteredData = (window.S5Filters && S5Filters.filter)
      ? S5Filters.filter(window.animeData, { genre, episode, ratingMin, ratingMax, yearMin, yearMax })
      : [...window.animeData];
    applySortAndFilters();
  };
  window.applySortAndFilters = function(){
    const sortBy = Dom.val('sortBy');
    // Only perform client-side sort for Library. For recommendations, server already ordered.
    if (window.currentView === 'library') {
      window.filteredData = (window.S5Filters && S5Filters.sort) ? S5Filters.sort(window.filteredData, sortBy) : [...window.filteredData];
    }
    displayAnime(window.filteredData);
  };
  window.clearFilters = function(){ Dom.clearValues(['genreFilter','episodeFilter']); resetDualRangeValues(); window.applyFilters(); };
  // Override for dual-range: reset to extremes
  const _origClear = window.clearFilters;
  window.clearFilters = function(){
    Dom.clearValues(['genreFilter','episodeFilter']);
    resetDualRangeValues();
    window.applyFilters();
  };

  // Filters panel toggle
  window.toggleFilters = function(){
    const panel = Dom.$('filtersPanel');
    if(!panel) return;
    panel.classList.toggle('hidden');
    if(!panel.classList.contains('hidden')){
      // Focus first control when opening
      const first = panel.querySelector('select, input, button');
      if(first) first.focus();
    }
  };

  // View state
  window.setViewMode = function(mode){ const gridBtn=Dom.$('gridViewBtn'); const listBtn=Dom.$('listViewBtn'); if(mode==='grid'){ gridBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; listBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } else { listBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; gridBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } window.currentLayout = mode; displayAnime(window.filteredData); };
  window.setViewSource = function(mode){ 
    if(mode===window.currentView) return; 
    window.currentView = mode; 
    const fy=Dom.$('forYouBtn'); 
    const fb=Dom.$('freeBrowsingBtn'); 
    const lb=Dom.$('libraryBtn'); 
    
    // Reset all buttons to inactive state
    fy.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; 
    fb.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; 
    lb.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; 
    
    // Set active button and load appropriate data
    if(window.currentView==='forYou'){ 
      fy.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; 
      window.loadAnimeData(); 
    } else if(window.currentView==='freeBrowsing'){ 
      fb.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; 
      window.loadFreeBrowsingData(); 
    } else { 
      lb.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; 
      loadLibrary(); 
    } 
  };

  // Navigation
  window.openDetails = function(animeId){
    const d = (window.S5Const && window.S5Const.PATHS && window.S5Const.PATHS.DETAILS) || 'details.html';
    window.location.href = `${d}?id=${encodeURIComponent(animeId)}`;
  };

  // Actions
  window.openQuickRate = async function(animeId, rating){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.postRate ? window.S5Api.postRate(window.currentUserId, animeId, rating) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast(`Rated ${rating}★`, 'success'); }catch{ showToast('Failed to rate', 'error'); } };
  window.toggleFavorite = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { favorite:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Added to favorites', 'success'); }catch{ showToast('Failed to update favorites', 'error'); } };
  window.markWatched = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { watched:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Marked as watched', 'success'); }catch{ showToast('Failed to mark watched', 'error'); } };
  window.markDropped = async function(animeId){ if(!window.currentUserId) return; try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, animeId, { dropped:true }) : null); await refreshLibraryState(); loadUserStats(); if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(animeId); } showToast('Dropped from list', 'success'); }catch{ showToast('Failed to update entry', 'error'); } };

  async function loadWatchlist(){
    if(!window.currentUserId){ displayAnime([]); return; }
    try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { status:'watched', sort:'updatedAt', page:1, pageSize:(window.S5Const?.LIBRARY?.WATCHLIST_PAGE_SIZE) ?? 100 }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.animeId).filter(Boolean); if(ids.length===0){ displayAnime([]); return; } const arr = await (window.S5Api && window.S5Api.getAnimeByIds ? window.S5Api.getAnimeByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToAnime({ anime: a, score: a.popularity || ((window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7) })); displayAnime(mapped); }catch{ displayAnime([]); showToast('Failed to load watchlist', 'error'); }
  }
  async function loadLibrary(){
    if(!window.currentUserId){ displayAnime([]); return; }
  try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page: window.currentLibraryPage, pageSize: PAGE_SIZE }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.animeId).filter(Boolean); if(window.currentLibraryPage===1 && ids.length===0){ displayAnime([]); Dom.$('moreBtn')?.classList.add('hidden'); return; } const arr = await (window.S5Api && window.S5Api.getAnimeByIds ? window.S5Api.getAnimeByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToAnime({ anime: a, score: a.popularity || ((window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7) })); if(window.currentLibraryPage===1){ window.animeData = mapped; } else { const seen = new Set(window.animeData.map(x=>x.id)); for(const m of mapped){ if(!seen.has(m.id)) window.animeData.push(m); } } window.filteredData = [...window.animeData]; for(const e of (data.items||[])){ window.libraryByAnimeId[e.animeId] = { favorite: !!e.favorite, watched: !!e.watched, dropped: !!e.dropped, rating: e.rating ?? null, updatedAt: e.updatedAt }; } if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayAnime(window.filteredData); } const btn = Dom.$('moreBtn'); if(btn){ if((data.items||[]).length === PAGE_SIZE) btn.classList.remove('hidden'); else btn.classList.add('hidden'); } }catch{ displayAnime([]); showToast('Failed to load library', 'error'); }
    const tp = (window.S5Const?.TEXT?.RESULTS_PREFIX) || 'Showing ';
    const ts = (window.S5Const?.TEXT?.RESULTS_SUFFIX) || ' results';
    Dom.text('resultCount', `${tp}${window.filteredData.length}${ts}`);
  }
  window.loadLibrary = loadLibrary;

  window.loadMore = async function(){ 
    if(window.currentView==='forYou'){ 
      const inc = (window.S5Config && window.S5Config.MORE_INCREMENT) || PAGE_SIZE; 
      window.currentRecsTopK += inc; 
      await window.loadAnimeData(); 
    } else if(window.currentView==='freeBrowsing'){ 
      const inc = (window.S5Config && window.S5Config.MORE_INCREMENT) || PAGE_SIZE; 
      window.currentRecsTopK += inc; 
      await window.loadFreeBrowsingData(); 
    } else { 
      window.currentLibraryPage += 1; 
      await loadLibrary(); 
    } 
  };

  window.removeCardFromForYou = function(animeId){ 
    // Only remove cards from "For You" mode, not from "Free Browsing" or "Library"
    if(window.currentView!=='forYou') return; 
    window.animeData = window.animeData.filter(a=>a.id!==animeId); 
    window.filteredData = window.filteredData.filter(a=>a.id!==animeId); 
    const grid = Dom.$('animeGrid'); 
    const card = grid?.querySelector(`[data-anime-id="${CSS.escape(String(animeId))}"]`); 
    if(card){ 
      card.style.transition=`opacity ${(window.S5Const?.UI?.REMOVE_CARD_TRANSITION_MS) ?? 250}ms ease-out, transform ${(window.S5Const?.UI?.REMOVE_CARD_TRANSITION_MS) ?? 250}ms ease-out`; 
      card.style.opacity='0'; 
      card.style.transform='scale(0.96)'; 
      setTimeout(()=>{ 
        card.remove(); 
        const tp = (window.S5Const && window.S5Const.TEXT && window.S5Const.TEXT.RESULTS_PREFIX) || 'Showing ';
        const ts = (window.S5Const && window.S5Const.TEXT && window.S5Const.TEXT.RESULTS_SUFFIX) || ' results';
        Dom.text('resultCount', `${tp}${window.filteredData.length}${ts}`); 
        const btn = Dom.$('moreBtn'); 
        if(btn){ 
          if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); 
          else btn.classList.add('hidden'); 
        } 
      }, (window.S5Const?.UI?.REMOVE_CARD_TIMEOUT_MS) ?? 260); 
    } else { 
      displayAnime(window.filteredData); 
    } 
  };
})();

// Dual-range helpers (module-private)
function initDualRangeControls(){
  const R = (window.S5Const && window.S5Const.RATING) || {}; const Y = (window.S5Const && window.S5Const.YEAR) || {};
  const ratingMin = document.getElementById('ratingMin');
  const ratingMax = document.getElementById('ratingMax');
  const yearMin = document.getElementById('yearMin');
  const yearMax = document.getElementById('yearMax');
  const ratingStep = typeof R.STEP === 'number' ? R.STEP : 0.5;
  const rMin = typeof R.MIN === 'number' ? R.MIN : 0;
  const rMax = typeof R.MAX === 'number' ? R.MAX : 5;
  if (ratingMin && ratingMax){
    ratingMin.min = String(rMin); ratingMin.max = String(rMax); ratingMin.step = String(ratingStep); ratingMin.value = String(rMin);
    ratingMax.min = String(rMin); ratingMax.max = String(rMax); ratingMax.step = String(ratingStep); ratingMax.value = String(rMax);
    const onRating = (e)=> clampPair(ratingMin, ratingMax, rMin, rMax, ratingStep, updateRatingUi, e?.target);
    ratingMin.addEventListener('input', onRating);
    ratingMax.addEventListener('input', onRating);
    updateRatingUi();
  }
  const now = new Date(); const yMaxAbs = now.getFullYear(); const yMinAbs = yMaxAbs - (Y.WINDOW_YEARS || 30);
  if (yearMin && yearMax){
    yearMin.min = String(yMinAbs); yearMin.max = String(yMaxAbs); yearMin.step = '1'; yearMin.value = String(yMinAbs);
    yearMax.min = String(yMinAbs); yearMax.max = String(yMaxAbs); yearMax.step = '1'; yearMax.value = String(yMaxAbs);
    const onYear = (e)=> clampPair(yearMin, yearMax, yMinAbs, yMaxAbs, 1, updateYearUi, e?.target);
    yearMin.addEventListener('input', onYear);
    yearMax.addEventListener('input', onYear);
    updateYearUi();
  }
}

function clampPair(minEl, maxEl, absMin, absMax, step, after, activeEl){
  const minV = parseFloat(minEl.value);
  const maxV = parseFloat(maxEl.value);
  if (minV > maxV){
    // Push the other thumb to preserve ordering based on which one moved
    if (activeEl === minEl){
      // User moved min beyond max → push max to min
      const snap = v => Math.round(v / step) * step;
      maxEl.value = String(snap(Math.min(minV, absMax)));
    } else if (activeEl === maxEl){
      // User moved max below min → push min to max
      const snap = v => Math.round(v / step) * step;
      minEl.value = String(snap(Math.max(maxV, absMin)));
    } else {
      // Fallback: align at the crossed value
      const mid = (minV + maxV) / 2;
      const snap = v => Math.round(v / step) * step;
      minEl.value = String(snap(Math.min(mid, absMax)));
      maxEl.value = String(snap(Math.max(mid, absMin)));
    }
  }
  if (after) after();
}

function updateRatingUi(){
  const ratingMin = document.getElementById('ratingMin');
  const ratingMax = document.getElementById('ratingMax');
  const prog = document.getElementById('ratingProgress');
  const label = document.getElementById('ratingLabel');
  const R = (window.S5Const && window.S5Const.RATING) || {};
  const r0 = parseFloat(ratingMin.value), r1 = parseFloat(ratingMax.value);
  const rMin = typeof R.MIN === 'number' ? R.MIN : 0; const rMax = typeof R.MAX === 'number' ? R.MAX : 5;
  const pct = v => ((v - rMin) / (rMax - rMin)) * 100;
  if (prog){ prog.style.left = pct(Math.min(r0,r1)) + '%'; prog.style.width = (pct(Math.max(r0,r1)) - pct(Math.min(r0,r1))) + '%'; }
  // Prevent the overlay (max) slider from intercepting clicks on the left side.
  // Clip its interactive region to the right of the min thumb, keeping a small margin so the max thumb stays grabbable when equal.
  if (ratingMax){
    const leftClip = Math.max(0, pct(Math.min(r0, r1)) - 2); // keep ~2% margin for the max knob
    const clip = `inset(0 0 0 ${leftClip}%)`;
    ratingMax.style.clipPath = clip;
    ratingMax.style.webkitClipPath = clip;
  }
  if (label){ label.textContent = (r0 <= rMin && r1 >= rMax) ? 'Any' : `Rating: ${r0}–${r1}★`; }
}

function updateYearUi(){
  const yearMin = document.getElementById('yearMin');
  const yearMax = document.getElementById('yearMax');
  const prog = document.getElementById('yearProgress');
  const label = document.getElementById('yearLabel');
  const ymin = parseInt(yearMin.value, 10), ymax = parseInt(yearMax.value, 10);
  const absMin = parseInt(yearMin.min, 10), absMax = parseInt(yearMax.max, 10);
  const pct = v => ((v - absMin) / (absMax - absMin)) * 100;
  if (prog){ prog.style.left = pct(Math.min(ymin,ymax)) + '%'; prog.style.width = (pct(Math.max(ymin,ymax)) - pct(Math.min(ymin,ymax))) + '%'; }
  // Clip max slider hit area to the right of the min thumb to avoid blocking min interactions
  if (yearMax){
    const leftClip = Math.max(0, pct(Math.min(ymin, ymax)) - 1); // ~1% margin for year knob (finer granularity)
    const clip = `inset(0 0 0 ${leftClip}%)`;
    yearMax.style.clipPath = clip;
    yearMax.style.webkitClipPath = clip;
  }
  if (label){
    const any = (ymin <= absMin && ymax >= absMax);
    const present = ymax >= absMax ? 'present' : String(ymax);
    label.textContent = any ? 'Any' : `Year: ${ymin}–${present}`;
  }
}

function readDualRangeValues(){
  const R = (window.S5Const && window.S5Const.RATING) || {}; const Y = (window.S5Const && window.S5Const.YEAR) || {};
  const rmin = parseFloat(document.getElementById('ratingMin')?.value ?? 'NaN');
  const rmax = parseFloat(document.getElementById('ratingMax')?.value ?? 'NaN');
  const rAbsMin = typeof R.MIN === 'number' ? R.MIN : 0; const rAbsMax = typeof R.MAX === 'number' ? R.MAX : 5;
  const ratingMin = isNaN(rmin) || rmin <= rAbsMin ? null : rmin;
  const ratingMax = isNaN(rmax) || rmax >= rAbsMax ? null : rmax;
  const ymin = parseInt(document.getElementById('yearMin')?.value ?? 'NaN', 10);
  const ymax = parseInt(document.getElementById('yearMax')?.value ?? 'NaN', 10);
  const absNow = new Date().getFullYear(); const absMin = absNow - (Y.WINDOW_YEARS || 30);
  const yearMin = isNaN(ymin) || ymin <= absMin ? null : ymin;
  const yearMax = isNaN(ymax) || ymax >= absNow ? null : ymax;
  return { ratingMin, ratingMax, yearMin, yearMax };
}

function resetDualRangeValues(){
  const R = (window.S5Const && window.S5Const.RATING) || {}; const Y = (window.S5Const && window.S5Const.YEAR) || {};
  const rmin = document.getElementById('ratingMin'); const rmax = document.getElementById('ratingMax');
  const rAbsMin = typeof R.MIN === 'number' ? R.MIN : 0; const rAbsMax = typeof R.MAX === 'number' ? R.MAX : 5;
  if (rmin && rmax){ rmin.value = String(rAbsMin); rmax.value = String(rAbsMax); updateRatingUi(); }
  const ymin = document.getElementById('yearMin'); const ymax = document.getElementById('yearMax');
  const now = new Date().getFullYear(); const absMin = now - (Y.WINDOW_YEARS || 30);
  if (ymin && ymax){ ymin.value = String(absMin); ymax.value = String(now); updateYearUi(); }
}
