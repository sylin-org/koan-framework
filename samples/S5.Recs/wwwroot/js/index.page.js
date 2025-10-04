// Page controller for S5.Recs index.html (classic script)
// Moves inline logic out of the HTML and wires into existing modules.

(function(){
  // Hoist globals the page previously defined to keep behavior unchanged
  let currentUserId = null;
  let authState = { isAuthenticated: false, me: null };
  let currentTab = 'discover';
  let mediaData = [];
  let filteredData = [];
  let currentView = 'forYou';
  let currentLayout = 'grid';
  let libraryByMediaId = {};
  const PAGE_SIZE = (window.S5Config && window.S5Config.PAGE_SIZE) || 100;
  let currentRecsTopK = PAGE_SIZE;
  let currentLibraryPage = 1;
  let selectedPreferredTags = window.selectedPreferredTags || [];
  let currentMediaType = 'all';
  let recsSettings = {
    preferTagsWeight: (window.S5Const?.RECS?.DEFAULT_PREFER_WEIGHT) ?? 0.2,
    maxPreferredTags: (window.S5Const?.RECS?.DEFAULT_MAX_PREFERRED_TAGS) ?? 3,
    diversityWeight: (window.S5Const?.RECS?.DEFAULT_DIVERSITY_WEIGHT) ?? 0.1
  };

  // Expose for other modules relying on globals
  Object.assign(window, {
    currentUserId, currentTab, mediaData, filteredData, currentView, currentLayout,
    libraryByMediaId, PAGE_SIZE, currentRecsTopK, currentLibraryPage, selectedPreferredTags, currentMediaType, recsSettings
  });

  document.addEventListener('DOMContentLoaded', () => {
    // Try to detect auth state
    ensureAuthState();
    // Enforce slider config from constants
    const w = Dom.$('preferWeight');
    if (w && window.S5Const?.RECS) {
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_MIN === 'number') w.min = String(window.S5Const.RECS.PREFER_WEIGHT_MIN);
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_MAX === 'number') w.max = String(window.S5Const.RECS.PREFER_WEIGHT_MAX);
      if (typeof window.S5Const.RECS.PREFER_WEIGHT_STEP === 'number') w.step = String(window.S5Const.RECS.PREFER_WEIGHT_STEP);
      if (typeof window.recsSettings?.preferTagsWeight === 'number') w.value = String(window.recsSettings.preferTagsWeight);
    }
  // Legacy user selector removed
    loadRecsSettings();
    if(window.S5Tags && S5Tags.loadTags){ S5Tags.loadTags().then(()=> S5Tags.renderPreferredChips && S5Tags.renderPreferredChips()); }
  // Populate Genre filter from backend catalog
  populateGenres();
  // Populate Media Type toggles from backend catalog
  populateMediaTypes();
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
      requestAnimationFrame(highlightCardsByPreferredTags);
    });
    Dom.on('tagsSort', 'change', ()=> S5Tags.renderAllTags && S5Tags.renderAllTags());
    Dom.on('tagsSearch', 'input', ()=> S5Tags.renderAllTags && S5Tags.renderAllTags());
    const allTagsList = Dom.$('allTagsList');
    if(allTagsList){
      allTagsList.addEventListener('click', (e)=>{
        const btn = e.target.closest('[data-tag]');
        if(!btn) return;
        const tag = btn.getAttribute('data-tag');
        if(window.S5Tags && S5Tags.togglePreferredTag){ S5Tags.togglePreferredTag(tag); if(S5Tags.renderAllTags) S5Tags.renderAllTags(); requestAnimationFrame(highlightCardsByPreferredTags); }
      });
    }

  // Alpha slider for hybrid search (semantic vs keyword balance)
    const alphaSlider = Dom.$('alphaSlider');
    const alphaHint = Dom.$('alphaHint');
    if (alphaSlider && alphaHint) {
      const updateAlphaHint = () => {
        const alpha = parseFloat(alphaSlider.value);
        if (alpha <= 0.2) {
          alphaHint.textContent = 'Exact Match';
        } else if (alpha <= 0.4) {
          alphaHint.textContent = 'Mostly Exact';
        } else if (alpha <= 0.6) {
          alphaHint.textContent = 'Balanced';
        } else if (alpha <= 0.8) {
          alphaHint.textContent = 'Mostly Meaning';
        } else {
          alphaHint.textContent = 'Semantic';
        }
      };
      alphaSlider.addEventListener('input', updateAlphaHint);
      updateAlphaHint(); // Initialize on page load
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
  requestAnimationFrame(highlightCardsByPreferredTags);
    }, (window.S5Const?.RECS?.TAG_BOOST_DEBOUNCE_MS) ?? 50);
      });
    }

    // Filters
    // Debounced auto-apply across selects and dual-range sliders
    const debounceMs = (window.S5Const && window.S5Const.FILTERS && typeof window.S5Const.FILTERS.DEBOUNCE_MS === 'number') ? window.S5Const.FILTERS.DEBOUNCE_MS : 120;
    let applyT = null;
    function scheduleApply(){
      if (applyT) clearTimeout(applyT);
      applyT = setTimeout(() => { applyFilters(); }, debounceMs);
    }
    // Genre/Episode selects
    ['genreFilter', 'episodeFilter'].forEach(id => Dom.on(id, 'change', scheduleApply));
    // Dual-range sliders initialization + hook into scheduleApply from inside
    initDualRangeControls(scheduleApply);

    // Filter chips event handlers
    const activeFiltersContainer = Dom.$('activeFiltersContainer');
    if (activeFiltersContainer) {
      activeFiltersContainer.addEventListener('click', (e) => {
        // Handle individual chip removal
        const removeBtn = e.target.closest('[data-remove-filter]');
        if (removeBtn) {
          const filterType = removeBtn.getAttribute('data-remove-filter');
          if (window.S5Filters && S5Filters.clearFilter) {
            S5Filters.clearFilter(filterType);
          }
          return;
        }

        // Handle clear all button
        if (e.target.closest('#clearAllFilters')) {
          if (window.S5Filters && S5Filters.clearAllFilters) {
            S5Filters.clearAllFilters();
          }
          return;
        }
      });
    }

    // Outside clicks close menus (admin only)
    document.addEventListener('click', (e) => {
      const adminClickedInside = e.target.closest('#adminMenu') || e.target.closest('#adminButton');
      if (!adminClickedInside) Dom.$('adminMenu')?.classList.add('hidden');
    });

    Dom.on('forYouBtn', 'click', () => setViewSource('forYou'));
    Dom.on('freeBrowsingBtn', 'click', () => setViewSource('freeBrowsing'));
    Dom.on('libraryBtn', 'click', () => setViewSource('library'));

    // Login/Logout buttons if present in layout
    Dom.on('loginBtn', 'click', openLogin);
    Dom.on('logoutBtn', 'click', doLogout);

    // Media Type toggles
    const mediaTypeContainer = Dom.$('mediaTypeToggles');
    if (mediaTypeContainer) {
      mediaTypeContainer.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-media-type]');
        if (!btn) return;

        const mediaType = btn.getAttribute('data-media-type');
        setMediaType(mediaType);
      });
    }
  }

  async function ensureAuthState(){
    try{
      const r = await fetch('/me', { credentials: 'include' });
      if(r.ok){
        const me = await r.json();
    authState.isAuthenticated = true; authState.me = me; currentUserId = me?.id || null; window.currentUserId = currentUserId;
    console.log('[S5] Auth detected, userId =', window.currentUserId);
        reflectAuthUi(true, me);
        // prefer personalized view when authenticated
    await refreshLibraryState();
    await setViewSource('forYou');
        return;
      }
    }catch{}
    authState.isAuthenticated = false; authState.me = null; currentUserId = null;
  window.currentUserId = null;
  console.log('[S5] Guest mode');
    reflectAuthUi(false, null);
  // anonymous defaults to free browsing
  await setViewSource('freeBrowsing');
  }

  function reflectAuthUi(isAuth, me){
    const hide = (el)=>{ if(el) el.classList.add('hidden'); };
    const show = (el)=>{ if(el) el.classList.remove('hidden'); };
    // Tabs (source buttons act as tabs in this layout)
    const forYouBtn = document.getElementById('forYouBtn');
    const libraryBtn = document.getElementById('libraryBtn');
    if(isAuth){
      show(forYouBtn); show(libraryBtn);
      forYouBtn.disabled = false;
      libraryBtn.disabled = false;
    } else {
      hide(forYouBtn); show(libraryBtn);
      forYouBtn.disabled = true;
      libraryBtn.disabled = true;
    }
    // Profile banner
    if(isAuth){
      Dom.text('bannerProfileName', `Welcome back, ${me?.displayName || 'User'}!`);
      Dom.text('currentProfileName', me?.displayName || 'User');
      Dom.text('currentProfileInitial', (me?.displayName||'U').slice(0,1).toUpperCase());
      // Fetch and display real stats
      if(window.S5Api && window.S5Api.getUserStats && me?.id){
        window.S5Api.getUserStats(me.id).then(stats => {
          if(stats && typeof stats === 'object'){
            Dom.text('profileStats', `${stats.favorites ?? 0} favorites`);
            console.log('[S5] Banner stats updated', stats);
          } else {
            // keep defaults
          }
        }).catch(e => {
          console.warn('[S5] Failed to load user stats', e);
        });
      }
    } else {
      // guest: keep defaults
    }
    // Login/Logout buttons
    const loginBtn = document.getElementById('loginBtn');
    const logoutBtn = document.getElementById('logoutBtn');
    if(loginBtn && logoutBtn){ if(isAuth){ hide(loginBtn); show(logoutBtn);} else { show(loginBtn); hide(logoutBtn);} }
  }

  async function openLogin(){
    try{
      const r = await fetch('/.well-known/auth/providers', { credentials: 'include' });
      const providers = r.ok ? await r.json() : [];
      const p = Array.isArray(providers) ? providers.find(x=>x.enabled && (x.protocol==='oauth2'||x.protocol==='oidc')) : null;
      if(!p){ showToast('No login providers available', 'error'); return; }
  const ret = window.location.pathname + window.location.search;
  // Force interactive login to avoid silent auto-redirect when a provider left a cookie
  window.location.href = `/auth/${encodeURIComponent(p.id)}/challenge?return=${encodeURIComponent(ret||'/')}&prompt=login`;
    }catch{ showToast('Login failed to start', 'error'); }
  }

  async function doLogout(){
    try{
      const ret = window.location.pathname + window.location.search;
      // Navigate to server-side logout to clear auth cookie, then redirect back
      window.location.href = `/auth/logout?return=${encodeURIComponent(ret||'/')}`;
    }catch{
      window.location.href = `/auth/logout?return=/`;
    }
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

  async function populateMediaTypes(){
    try{
      if(!(window.S5Api && typeof window.S5Api.getMediaTypes==='function')) return;
      const items = await window.S5Api.getMediaTypes(); // [{ id, name, ... }]
      const container = document.getElementById('mediaTypeToggles'); if(!container) return;

      // Clear existing buttons
      container.innerHTML = '';

      // Add "All" button first
      const allBtn = document.createElement('button');
      allBtn.className = 'px-3 py-1.5 text-sm bg-purple-600 text-white rounded-lg font-medium transition-colors';
      allBtn.textContent = 'All';
      allBtn.setAttribute('data-media-type', 'all');
      container.appendChild(allBtn);

      // Add buttons for each media type
      (items||[]).forEach(item=>{
        if(!item || !item.name) return;
        const btn = document.createElement('button');
        btn.className = 'px-3 py-1.5 text-sm bg-slate-800 text-gray-300 hover:bg-slate-700 hover:text-white rounded-lg font-medium transition-colors';
        btn.textContent = item.name;
        btn.setAttribute('data-media-type', item.id || item.name);
        container.appendChild(btn);
      });

      // Set current selection
      window.currentMediaType = 'all';
    }catch{}
  }

  // Menus
  window.toggleAdminMenu = function(){ Dom.$('adminMenu')?.classList.toggle('hidden'); };

  // User selector removed

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

  // Media Type selection
  window.setMediaType = function(mediaType){
    window.currentMediaType = mediaType;
    console.log('[S5] setMediaType →', mediaType);

    // Update button styles
    const container = Dom.$('mediaTypeToggles');
    if (container) {
      const buttons = container.querySelectorAll('[data-media-type]');
      buttons.forEach(btn => {
        const isActive = btn.getAttribute('data-media-type') === mediaType;
        if (isActive) {
          btn.className = 'px-3 py-1.5 text-sm bg-purple-600 text-white rounded-lg font-medium transition-colors';
        } else {
          btn.className = 'px-3 py-1.5 text-sm bg-slate-800 text-gray-300 hover:bg-slate-700 hover:text-white rounded-lg font-medium transition-colors';
        }
      });
    }

    // Apply filter and reload data
    applyMediaTypeFilter();
  };

  async function applyMediaTypeFilter(){
    // For recommendation views, we need to re-fetch with the media type filter
    if(window.currentView === 'forYou'){
      window.currentRecsTopK = PAGE_SIZE;
      await window.loadAnimeData();
    } else if(window.currentView === 'freeBrowsing'){
      window.currentRecsTopK = PAGE_SIZE;
      await window.loadFreeBrowsingData();
    } else {
      // For library view, apply client-side filtering
      applyClientSideMediaTypeFilter();
    }
  }

  function applyClientSideMediaTypeFilter(){
    if(!window.mediaData || window.currentMediaType === 'all'){
      window.filteredData = [...window.mediaData];
    } else {
      window.filteredData = window.mediaData.filter(item => {
        // Assuming media objects have a mediaTypeId or similar field
        return item.mediaTypeId === window.currentMediaType || item.type === window.currentMediaType;
      });
    }

    if (typeof window.applySortAndFilters === 'function') {
      window.applySortAndFilters();
    } else {
      displayMedia(window.filteredData);
    }
  }

  // Library cache
  async function refreshLibraryState(){
    if(!window.currentUserId){ window.libraryByMediaId = {}; return; }
  console.log('[S5] refreshLibraryState: fetching for user', window.currentUserId);
    try{
  const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page:1, pageSize:(window.S5Const?.LIBRARY?.PAGE_SIZE) ?? 500 }) : null);
      if(!data){ window.libraryByMediaId = {}; return; }
      const map = {};
      for(const e of (data.items||[])) map[e.mediaId] = { favorite: !!e.favorite, watched: !!e.watched, dropped: !!e.dropped, rating: e.rating ?? null, updatedAt: e.updatedAt };
  window.libraryByMediaId = map;
  console.log('[S5] refreshLibraryState: cached entries', Object.keys(window.libraryByMediaId).length);
    }catch{ window.libraryByMediaId = {}; }
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

  // Source selection with auth gating
  window.setViewSource = async function(source){
    // Auth-only sources
    if((source === 'forYou' || source === 'library') && !authState.isAuthenticated){
      showToast('Please login to access personalized recommendations and your library.', 'info');
      source = 'freeBrowsing';
    }
    window.currentView = source;
    console.log('[S5] setViewSource →', source);
    // Toggle button styles
    const ids = ['forYouBtn','freeBrowsingBtn','libraryBtn'];
    ids.forEach(id => {
      const el = document.getElementById(id);
      if(!el) return;
      const active = (id === (source+'Btn'));
      el.classList.toggle('bg-purple-600', active);
      el.classList.toggle('text-white', active);
      el.classList.toggle('text-gray-400', !active);
    });
    // Load appropriate data
    if(source === 'forYou') await window.loadAnimeData();
    else if(source === 'freeBrowsing') await window.loadFreeBrowsingData();
    else await window.loadLibrary();
  };

  // Data loading
  window.loadAnimeData = async function(){
  if(!window.currentUserId){ console.warn('[S5] loadAnimeData: no userId'); displayMedia([]); return; }
    try{
      console.log('[S5] loadAnimeData → recs query', { userId: window.currentUserId, topK: window.currentRecsTopK });
      const recs = await fetchRecommendations({ text: '', topK: window.currentRecsTopK });
  window.mediaData = recs; window.filteredData = [...window.mediaData];
  // Apply current sort selection after loading
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); }
  else { displayMedia(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
      console.log('[S5] loadAnimeData → results', window.filteredData.length);
    }catch{ displayMedia([]); Dom.$('moreBtn')?.classList.add('hidden'); showToast('Failed to load recommendations', 'error'); }
  };

  window.loadFreeBrowsingData = async function(){
    try{
  console.log('[S5] loadFreeBrowsingData → recs query', { topK: window.currentRecsTopK, ignoreUserPreferences: true });
  const recs = await fetchRecommendations({ text: '', topK: window.currentRecsTopK, ignoreUserPreferences: true });
  window.mediaData = recs; window.filteredData = [...window.mediaData];
  // Apply current sort selection after loading
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); }
  else { displayMedia(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
  console.log('[S5] loadFreeBrowsingData → results', window.filteredData.length);
    }catch{ displayMedia([]); Dom.$('moreBtn')?.classList.add('hidden'); showToast('Failed to load content', 'error'); }
  };

  window.fetchRecommendations = async function({ text, topK = PAGE_SIZE, ignoreUserPreferences = false }){
  console.log('[S5] fetchRecommendations', { text, topK, ignoreUserPreferences });
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
    const userId = ignoreUserPreferences ? null : window.currentUserId;
    if(!ignoreUserPreferences && !userId){
      console.warn('[S5] fetchRecommendations: No userId for personalized query');
      return [];
    }
    // Include media type filter if set
    const mediaTypeFilter = window.currentMediaType && window.currentMediaType !== 'all' ? window.currentMediaType : null;

    // Get rating and year filters (returns null for values at extremes)
    const { ratingMin, ratingMax, yearMin, yearMax } = readDualRangeValues();

    // Get alpha value for hybrid search (semantic vs keyword balance)
    const alpha = parseFloat(Dom.$('alphaSlider')?.value ?? '0.5');

    const body = {
      text: text || '',
      topK,
      sort,
      alpha: text ? alpha : null, // Only include alpha when there's search text (hybrid mode)
      filters: {
        genres,
        episodesMax,
        spoilerSafe: true,
        preferTags,
        preferWeight: weight,
        mediaType: mediaTypeFilter,
        // Add rating/year filters - null values handled gracefully by backend
        ratingMin: ratingMin,
        ratingMax: ratingMax,
        yearMin: yearMin,
        yearMax: yearMax
      },
      userId
    };
    const data = await (window.S5Api && window.S5Api.recsQuery ? window.S5Api.recsQuery(body) : null) || { items: [] };
    const items = Array.isArray(data.items) ? data.items : [];
    const mapped = items.map(it => mapItemToMedia(it));
    // In free browsing mode, don't filter out library items since user preferences are ignored
    if (ignoreUserPreferences) {
      return mapped;
    }
    return mapped.filter(a => !window.libraryByMediaId[a.id]);
  };

  function mapItemToMedia(item){
  const a = item.media || item;

  // Use backend's computed rating (80% score + 20% popularity)
  // Fallback to client-side computation for legacy data
  let rating;
  if (a.rating != null) {
    // Backend provides blended rating
    rating = a.rating;
  } else if (a.averageScore != null && a.popularity != null) {
    // Fallback: compute blend client-side (80/20 formula)
    rating = (a.averageScore * 0.8) + a.popularity;
  } else if (a.averageScore != null) {
    // Only score available
    rating = a.averageScore;
  } else {
    // Legacy: compute from popularity/score
    const score = typeof item.score === 'number' ? item.score : (a.popularity || 0.7);
    const stars = (window.S5Const?.RATING?.STARS) ?? 5;
    rating = score * stars;
  }

  // Round to 1 decimal place for display
  const displayRating = Math.round(rating * 10) / 10;

  // Use backend's computed year, or extract from startDate
  const year = a.year ?? (a.startDate ? new Date(a.startDate).getFullYear() : '');

  // Extract media type from mediaTypeId
  const getTypeFromMediaTypeId = (mediaTypeId) => {
    if (!mediaTypeId) return 'TV';
    const typeMap = {
      'media-anime': 'Anime',
      'media-manga': 'Manga',
      'media-movie': 'Movie',
      'media-tv': 'TV',
      'media-book': 'Book',
      'media-game': 'Game',
      'media-music': 'Music'
    };
    return typeMap[mediaTypeId] || mediaTypeId.replace('media-', '').replace(/^./, c => c.toUpperCase());
  };

  const mediaType = a.type || getTypeFromMediaTypeId(a.mediaTypeId) || 'TV';

    return { id:a.id, title: a.titleEnglish || a.title || a.titleRomaji || a.titleNative || 'Untitled', englishTitle:a.titleEnglish||'', coverUrl:a.coverUrl||'', backdrop:a.bannerUrl||a.coverUrl||'', rating:displayRating, popularity:a.popularity||0, episodes:a.episodes||0, type:mediaType, mediaTypeId:a.mediaTypeId, year:year, status:a.status||'', studio:a.studio||'', source:a.source||'', synopsis:a.synopsis||'', genres: Array.isArray(a.genres)? a.genres : (Array.isArray(a.tags)? a.tags: []) };
  }

  function loadContentForTab(tab){
    switch(tab){
      case 'trending': window.filteredData = window.mediaData.sort((a,b)=>(b.popularity||0)-(a.popularity||0)).slice(0, ((window.S5Const && window.S5Const.UI && typeof window.S5Const.UI.PREVIEW_SECTION_COUNT === 'number') ? window.S5Const.UI.PREVIEW_SECTION_COUNT : 24)); break;
      case 'top-rated': window.filteredData = window.mediaData.sort((a,b)=> b.rating-a.rating).slice(0, ((window.S5Const && window.S5Const.UI && typeof window.S5Const.UI.PREVIEW_SECTION_COUNT === 'number') ? window.S5Const.UI.PREVIEW_SECTION_COUNT : 24)); break;
      case 'watchlist': loadWatchlist(); return;
      default: displayMedia(window.mediaData); return;
    }
    displayMedia(window.filteredData);
  }

  // Display
  function displayMedia(list){
    const grid = Dom.$('mediaGrid'); const loading = Dom.$('loadingState'); const empty = Dom.$('emptyState');
    loading.style.display = 'none';
    if(!list || list.length===0){ grid.classList.add('hidden'); empty.classList.remove('hidden'); Dom.text('resultCount', (window.S5Const?.TEXT?.NO_RESULTS) || 'No results'); return; }
    grid.classList.remove('hidden'); empty.classList.add('hidden');
    const tp = (window.S5Const?.TEXT?.RESULTS_PREFIX) || 'Showing ';
    const ts = (window.S5Const?.TEXT?.RESULTS_SUFFIX) || ' results';
    Dom.text('resultCount', `${tp}${list.length}${ts}`);
    const state = { libraryByMediaId: window.libraryByMediaId, selectedPreferredTags: window.selectedPreferredTags };
  if(window.currentLayout==='list'){ grid.className = 'space-y-4'; grid.innerHTML = list.map(a=>window.S5Cards.createMediaListItem(a, state)).join(''); }
  else { grid.className = 'grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-6'; grid.innerHTML = list.map(a=>window.S5Cards.createMediaCard(a, state)).join(''); }
  // After rendering, apply highlight based on selected tags
  requestAnimationFrame(highlightCardsByPreferredTags);
  }
  window.displayMedia = displayMedia;
  // Highlight cards/list rows that match any selected preferred tags
  function highlightCardsByPreferredTags(){
    try{
      const tags = Array.isArray(window.selectedPreferredTags) ? window.selectedPreferredTags : [];
      const on = tags.length > 0;
      const ringCls = 'ring-2 ring-purple-500/60 ring-offset-1 ring-offset-slate-900';
      const cards = document.querySelectorAll('#mediaGrid [data-media-id]');
      cards.forEach(el => {
        const gs = (el.getAttribute('data-genres') || '').split('|').filter(Boolean);
        const match = on && gs.some(g => tags.includes(g));
        if (match) { el.classList.add(...ringCls.split(' ')); }
        else { el.classList.remove(...ringCls.split(' ')); }
      });
    }catch{}
  }

  // Search & filters
  window.handleGlobalSearch = async function(e){
    const query = e.target.value;
    if(!query || query.trim().length===0){
      if(window.currentView==='forYou' || window.currentView==='freeBrowsing'){
        window.currentRecsTopK = PAGE_SIZE;
      }
      Dom.$('moreBtn')?.classList.add('hidden');

      // Update filter chips to remove search
      if (window.S5Filters && S5Filters.updateFilterChips) {
        const currentFilters = S5Filters.getActiveFilters();
        delete currentFilters.search; // Remove search from active filters
        S5Filters.updateFilterChips(currentFilters);
      }

      loadContentForTab(window.currentTab);
      return;
    }
    if(window.currentView==='library'){
      window.filteredData = (window.S5Filters && S5Filters.search) ? S5Filters.search(window.mediaData, query) : window.mediaData;

      // Update filter chips to include search query
      if (window.S5Filters && S5Filters.updateFilterChips) {
        const currentFilters = S5Filters.getActiveFilters();
        S5Filters.updateFilterChips({ ...currentFilters, search: query });
      }

      // Respect current sort on search within library
      if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayMedia(window.filteredData); }
      return;
    }
    try{
      window.currentRecsTopK = PAGE_SIZE;
      const isFreeBrowsing = window.currentView === 'freeBrowsing';
      const recs = await window.fetchRecommendations({ text: query, topK: window.currentRecsTopK, ignoreUserPreferences: isFreeBrowsing });
  window.mediaData = recs; window.filteredData = [...window.mediaData];
  if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayMedia(window.filteredData); }
      const btn = Dom.$('moreBtn'); if(btn){ if(window.filteredData.length >= window.currentRecsTopK) btn.classList.remove('hidden'); else btn.classList.add('hidden'); }
    }catch{ Dom.$('moreBtn')?.classList.add('hidden'); showToast('Search failed', 'error'); }
  }
  window.applyFilters = async function(){
    // Note: For recommendation views (forYou/freeBrowsing), filters are applied on the BACKEND
    // For Library view, filters are applied client-side on already-fetched data
    const genre = Dom.val('genreFilter');
    const episode = Dom.val('episodeFilter');
    const { ratingMin, ratingMax, yearMin, yearMax } = readDualRangeValues();
    const search = Dom.val('globalSearch');

    // Update filter chips
    if (window.S5Filters && S5Filters.updateFilterChips) {
      S5Filters.updateFilterChips({ genre, episode, ratingMin, ratingMax, yearMin, yearMax, search });
    }

    // For recommendation views, re-fetch from backend with new filters
    if (window.currentView === 'forYou') {
      window.currentRecsTopK = PAGE_SIZE;
      await window.loadAnimeData();
      return;
    } else if (window.currentView === 'freeBrowsing') {
      window.currentRecsTopK = PAGE_SIZE;
      await window.loadFreeBrowsingData();
      return;
    }

    // For Library view, apply client-side filters
    if (window.currentView === 'library') {
      window.filteredData = (window.S5Filters && S5Filters.filter)
        ? S5Filters.filter(window.mediaData, { genre, episode, ratingMin, ratingMax, yearMin, yearMax })
        : [...window.mediaData];
    }

    applySortAndFilters();
  };
  window.applySortAndFilters = function(){
    const sortBy = Dom.val('sortBy');
    // Only perform client-side sort for Library. For recommendations, server already ordered.
    if (window.currentView === 'library') {
      window.filteredData = (window.S5Filters && S5Filters.sort) ? S5Filters.sort(window.filteredData, sortBy) : [...window.filteredData];
    }
    displayMedia(window.filteredData);
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
  // Filters panel is always visible now; no toggle.

  // View state
  window.setViewMode = function(mode){ const gridBtn=Dom.$('gridViewBtn'); const listBtn=Dom.$('listViewBtn'); if(mode==='grid'){ gridBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; listBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } else { listBtn.className='px-3 py-1.5 text-sm text-white bg-purple-600 rounded transition-colors'; gridBtn.className='px-3 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors'; } window.currentLayout = mode; displayMedia(window.filteredData); };
  // (removed duplicate setViewSource; gated async version above is authoritative)

  // Navigation
  window.openMediaDetails = function(mediaId){
    const d = (window.S5Const && window.S5Const.PATHS && window.S5Const.PATHS.DETAILS) || 'details.html';
    window.location.href = `${d}?id=${encodeURIComponent(mediaId)}`;
  };

  // Actions
  window.openQuickRate = async function(mediaId, rating){ if(!window.currentUserId) { showToast('Please log in to rate media', 'error'); return; } try{ await (window.S5Api && window.S5Api.postRate ? window.S5Api.postRate(window.currentUserId, mediaId, rating) : null); await refreshLibraryState(); if(typeof loadUserStats === 'function'){ try{ await loadUserStats(); }catch{} } if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(mediaId); } showToast(`Rated ${rating}★`, 'success'); }catch{ showToast('Failed to rate', 'error'); } };
  window.toggleFavorite = async function(mediaId){
    if(!window.currentUserId) { showToast('Please log in to manage favorites', 'error'); return; }
    const entry = window.libraryByMediaId[mediaId];
    const newFavState = !(entry && entry.favorite);
    try{
      await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, mediaId, { favorite: newFavState }) : null);
      await refreshLibraryState();
      if(typeof loadUserStats === 'function'){ try{ await loadUserStats(); }catch{} }
      if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(mediaId); }
      showToast(newFavState ? 'Added to favorites' : 'Removed from favorites', 'success');
    }catch{ showToast('Failed to update favorites', 'error'); }
  };

  window.markWatched = async function(mediaId){ if(!window.currentUserId) { showToast('Please log in to manage your list', 'error'); return; } try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, mediaId, { watched:true }) : null); await refreshLibraryState(); if(typeof loadUserStats === 'function'){ try{ await loadUserStats(); }catch{} } if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(mediaId); } showToast('Marked as watched', 'success'); }catch{ showToast('Failed to mark watched', 'error'); } };
  window.markDropped = async function(mediaId){ if(!window.currentUserId) { showToast('Please log in to manage your list', 'error'); return; } try{ await (window.S5Api && window.S5Api.putLibrary ? window.S5Api.putLibrary(window.currentUserId, mediaId, { dropped:true }) : null); await refreshLibraryState(); if(typeof loadUserStats === 'function'){ try{ await loadUserStats(); }catch{} } if(window.currentView==='library'){ await loadLibrary(); } else { removeCardFromForYou(mediaId); } showToast('Dropped from list', 'success'); }catch{ showToast('Failed to update entry', 'error'); } };

  // Status dropdown management
  let currentStatusDropdown = null;

  window.toggleStatusDropdown = function(mediaId, buttonElement) {
    if (!window.currentUserId) {
      showToast('Please log in to manage your list', 'error');
      return;
    }

    // Close existing dropdown
    if (currentStatusDropdown) {
      // Clean up any existing rating panels first
      document.querySelectorAll('.status-rating-panel').forEach(panel => {
        panel.classList.remove('show');
        if (panel.parentNode === document.body) {
          panel.remove();
        }
      });
      currentStatusDropdown.remove();
      currentStatusDropdown = null;
    }

    // Get current entry state
    const entry = window.libraryByMediaId[mediaId];
    const isWatched = !!(entry && entry.watched);
    const isDropped = !!(entry && entry.dropped);
    const inList = !!entry;

    // Create dropdown
    const dropdown = document.createElement('div');
    dropdown.className = 'status-dropdown';

    const statusOptions = [
      { key: 'add', label: 'Add to List', icon: 'fa-plus', active: !inList },
      { key: 'plan', label: 'Plan to Watch', icon: 'fa-play-circle', active: inList && !isWatched && !isDropped },
      { key: 'watching', label: 'Watching', icon: 'fa-play-circle', active: false, hasRating: true },
      { key: 'completed', label: 'Completed', icon: 'fa-check-circle', active: isWatched, hasRating: true },
      { key: 'dropped', label: 'Dropped', icon: 'fa-times-circle', active: isDropped, hasRating: true },
      { key: 'remove', label: 'Remove from List', icon: 'fa-trash', active: false }
    ];

    // Get current user rating for this media
    const currentRating = entry?.rating || 0;

    dropdown.innerHTML = statusOptions.map(option => {
      let itemHtml = `<button class="status-dropdown-item ${option.active ? 'active' : ''}" data-status="${option.key}" data-media-id="${mediaId}">
        <i class="fas ${option.icon}"></i>
        ${option.label}`;

      // Add rating panel for watch status items
      if (option.hasRating) {
        itemHtml += `
          <div class="status-rating-panel" data-rating-panel="${option.key}">
            <div class="status-rating-header">Rate & ${option.label}</div>
            <div class="status-rating-stars">
              ${[1, 2, 3, 4, 5].map(rating =>
                `<i class="fas fa-star status-rating-star ${rating <= currentRating ? 'active' : ''}"
                   data-rating="${rating}"
                   data-status="${option.key}"
                   data-media-id="${mediaId}"
                   title="${rating} star${rating !== 1 ? 's' : ''}"></i>`
              ).join('')}
            </div>
          </div>`;
      }

      itemHtml += `</button>`;
      return itemHtml;
    }).join('');

    // Position dropdown relative to button but append to body to avoid clipping
    const buttonRect = buttonElement.getBoundingClientRect();
    dropdown.style.position = 'fixed';
    dropdown.style.zIndex = '9999';

    // Smart positioning to avoid going off-screen
    let top = buttonRect.bottom + 4;
    let left = buttonRect.left;

    // Check if dropdown would go off the right edge
    const dropdownWidth = 160; // min-width from CSS
    if (left + dropdownWidth > window.innerWidth) {
      left = buttonRect.right - dropdownWidth;
    }

    // Check if dropdown would go off the bottom edge
    const dropdownHeight = statusOptions.length * 40; // approximate height
    if (top + dropdownHeight > window.innerHeight) {
      top = buttonRect.top - dropdownHeight - 4; // Show above button instead
    }

    dropdown.style.top = `${top}px`;
    dropdown.style.left = `${left}px`;

    // Append to body to avoid container clipping
    document.body.appendChild(dropdown);

    // Show dropdown with animation
    setTimeout(() => dropdown.classList.add('show'), 10);

    // Helper function to clean up rating panels
    const cleanupRatingPanels = () => {
      document.querySelectorAll('.status-rating-panel').forEach(panel => {
        panel.classList.remove('show');
        if (panel.parentNode === document.body) {
          panel.remove();
        }
      });
    };

    // Add hover logic for rating panels
    let currentRatingPanel = null;
    let hideTimeout = null;

    // Comprehensive cleanup function for all event handlers
    const cleanupDropdown = () => {
      cleanupRatingPanels();
      if (currentStatusDropdown) {
        currentStatusDropdown.remove();
        currentStatusDropdown = null;
      }
    };

    // Store handlers for cleanup
    let closeHandler, clickHandler, scrollHandler;

    // Close dropdown on scroll to avoid positioning issues
    scrollHandler = () => {
      cleanupDropdown();
      document.removeEventListener('click', closeHandler);
      document.removeEventListener('click', clickHandler);
      document.removeEventListener('scroll', scrollHandler, true);
    };

    dropdown.addEventListener('mouseover', (e) => {
      const item = e.target.closest('.status-dropdown-item');
      if (item) {
        const panel = item.querySelector('.status-rating-panel');
        if (panel) {
          // Clear hide timeout
          if (hideTimeout) {
            clearTimeout(hideTimeout);
            hideTimeout = null;
          }

          // Always clean up all existing rating panels first
          cleanupRatingPanels();
          currentRatingPanel = null;

          // Clone the panel to avoid DOM issues
          const clonedPanel = panel.cloneNode(true);

          // Position cloned panel next to the item using fixed positioning
          const itemRect = item.getBoundingClientRect();
          clonedPanel.style.position = 'fixed';
          clonedPanel.style.top = `${itemRect.top}px`;
          clonedPanel.style.left = `${itemRect.right + 4}px`;
          clonedPanel.style.zIndex = '10001';

          // Append to body to ensure proper layering
          document.body.appendChild(clonedPanel);

          // Show this panel
          currentRatingPanel = clonedPanel;
          setTimeout(() => clonedPanel.classList.add('show'), 50);
        }
      }
    });

    dropdown.addEventListener('mouseleave', (e) => {
      // Only hide if we're truly leaving both dropdown and any visible rating panels
      const relatedTarget = e.relatedTarget;
      const isGoingToPanel = relatedTarget && (
        dropdown.contains(relatedTarget) ||
        relatedTarget.closest('.status-rating-panel')
      );

      if (!isGoingToPanel) {
        hideTimeout = setTimeout(() => {
          cleanupRatingPanels();
          currentRatingPanel = null;
        }, 150);
      }
    });

    // Handle rating panel hover to keep them visible
    document.addEventListener('mouseover', (e) => {
      const panel = e.target.closest('.status-rating-panel');
      if (panel && panel.classList.contains('show')) {
        // Clear any pending hide timeout when hovering over a rating panel
        if (hideTimeout) {
          clearTimeout(hideTimeout);
          hideTimeout = null;
        }
      }
    });

    document.addEventListener('mouseleave', (e) => {
      const panel = e.target.closest('.status-rating-panel');
      if (panel && panel.classList.contains('show')) {
        // Hide panel when leaving it and not going back to dropdown
        const relatedTarget = e.relatedTarget;
        const isGoingToDropdown = relatedTarget && dropdown.contains(relatedTarget);

        if (!isGoingToDropdown) {
          hideTimeout = setTimeout(() => {
            panel.classList.remove('show');
            currentRatingPanel = null;
          }, 150);
        }
      }
    });

    // Star hover effects within rating panels
    dropdown.addEventListener('mouseover', (e) => {
      const star = e.target.closest('.status-rating-star');
      if (!star) return;

      const panel = star.closest('.status-rating-panel');
      if (!panel) return;

      const rating = parseInt(star.getAttribute('data-rating'), 10);
      const stars = panel.querySelectorAll('.status-rating-star');

      stars.forEach(s => {
        const starRating = parseInt(s.getAttribute('data-rating'), 10);
        s.classList.toggle('hover', starRating <= rating);
      });
    });

    dropdown.addEventListener('mouseout', (e) => {
      const panel = e.target.closest('.status-rating-panel');
      if (panel && !panel.contains(e.relatedTarget)) {
        // Clear hover effects when leaving the panel
        panel.querySelectorAll('.status-rating-star.hover').forEach(s => s.classList.remove('hover'));
      }
    });

    // Handle clicks on both dropdown and rating panels (using event delegation on document)
    clickHandler = async (e) => {
      // Check if clicking on a rating star first
      const star = e.target.closest('.status-rating-star');
      if (star) {
        const rating = parseInt(star.getAttribute('data-rating'), 10);
        const status = star.getAttribute('data-status');
        const mediaId = star.getAttribute('data-media-id');

        try {
          // Set both status and rating
          let statusUpdate = {};

          switch (status) {
            case 'watching':
              statusUpdate = { watched: false, dropped: false };
              showToast(`Added to Watching with ${rating}★ rating`, 'success');
              break;
            case 'completed':
              statusUpdate = { watched: true, dropped: false };
              showToast(`Marked as Completed with ${rating}★ rating`, 'success');
              break;
            case 'dropped':
              statusUpdate = { watched: false, dropped: true };
              showToast(`Marked as Dropped with ${rating}★ rating`, 'success');
              break;
          }

          // Update library entry with status
          await window.S5Api.putLibrary(window.currentUserId, mediaId, statusUpdate);
          // Set rating
          await window.S5Api.postRate(window.currentUserId, mediaId, rating);

          await refreshLibraryState();
          if (typeof loadUserStats === 'function') {
            try { await loadUserStats(); } catch {}
          }
          if (window.currentView === 'library') {
            await loadLibrary();
          } else {
            if (status === 'completed' || status === 'dropped') {
              removeCardFromForYou(mediaId);
            }
          }

        } catch {
          showToast('Failed to update status and rating', 'error');
        }

        // Close dropdown
        cleanupDropdown();
        document.removeEventListener('click', clickHandler);
        document.removeEventListener('scroll', scrollHandler, true);
        return;
      }

      // Handle regular status item clicks (not on rating stars) - only if it's in our dropdown
      const item = e.target.closest('.status-dropdown-item');
      if (!item || !dropdown.contains(item)) return;

      const status = item.getAttribute('data-status');
      const mediaId = item.getAttribute('data-media-id');

      try {
        switch (status) {
          case 'add':
          case 'plan':
            await window.S5Api.putLibrary(window.currentUserId, mediaId, { watched: false, dropped: false });
            showToast('Added to Plan to Watch', 'success');
            break;
          case 'watching':
            await window.S5Api.putLibrary(window.currentUserId, mediaId, { watched: false, dropped: false });
            showToast('Added to Watching', 'success');
            break;
          case 'completed':
            await window.S5Api.putLibrary(window.currentUserId, mediaId, { watched: true, dropped: false });
            showToast('Marked as Completed', 'success');
            break;
          case 'dropped':
            await window.S5Api.putLibrary(window.currentUserId, mediaId, { watched: false, dropped: true });
            showToast('Marked as Dropped', 'success');
            break;
          case 'remove':
            await window.S5Api.putLibrary(window.currentUserId, mediaId, { watched: false, dropped: false, favorite: false });
            showToast('Removed from list', 'success');
            break;
        }

        await refreshLibraryState();
        if (typeof loadUserStats === 'function') {
          try { await loadUserStats(); } catch {}
        }
        if (window.currentView === 'library') {
          await loadLibrary();
        } else {
          if (status === 'completed' || status === 'dropped') {
            removeCardFromForYou(mediaId);
          }
        }

      } catch {
        showToast('Failed to update status', 'error');
      }

      // Close dropdown
      cleanupDropdown();
      document.removeEventListener('click', clickHandler);
      document.removeEventListener('scroll', scrollHandler, true);
    };

    // Add click handler to document to catch both dropdown and rating panel clicks
    document.addEventListener('click', clickHandler);

    currentStatusDropdown = dropdown;

    // Close dropdown when clicking outside
    closeHandler = (e) => {
      if (!dropdown.contains(e.target) && !buttonElement.contains(e.target)) {
        cleanupDropdown();
        document.removeEventListener('click', closeHandler);
        document.removeEventListener('click', clickHandler);
        document.removeEventListener('scroll', scrollHandler, true);
      }
    };

    // Add scroll listener (using capture to catch all scroll events)
    document.addEventListener('scroll', scrollHandler, true);

    // Delay adding the close handler to prevent immediate closing
    setTimeout(() => {
      document.addEventListener('click', closeHandler);
    }, 10);
  };

  async function loadWatchlist(){
    if(!window.currentUserId){ displayMedia([]); return; }
    try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { status:'watched', sort:'updatedAt', page:1, pageSize:(window.S5Const?.LIBRARY?.WATCHLIST_PAGE_SIZE) ?? 100 }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.mediaId).filter(Boolean); if(ids.length===0){ displayMedia([]); return; } const arr = await (window.S5Api && window.S5Api.getMediaByIds ? window.S5Api.getMediaByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToMedia({ media: a, score: a.popularity || ((window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7) })); displayMedia(mapped); }catch{ displayMedia([]); showToast('Failed to load watchlist', 'error'); }
  }
  async function loadLibrary(){
    if(!window.currentUserId){ displayMedia([]); return; }
  console.log('[S5] loadLibrary: page', window.currentLibraryPage);
  try{ const data = await (window.S5Api && window.S5Api.getLibrary ? window.S5Api.getLibrary(window.currentUserId, { sort:'updatedAt', page: window.currentLibraryPage, pageSize: PAGE_SIZE }) : null) || { items: [] }; const ids = (data.items||[]).map(e=>e.mediaId).filter(Boolean); if(window.currentLibraryPage===1 && ids.length===0){ displayMedia([]); Dom.$('moreBtn')?.classList.add('hidden'); return; } const arr = await (window.S5Api && window.S5Api.getMediaByIds ? window.S5Api.getMediaByIds(ids) : null) || []; const mapped = arr.map(a => mapItemToMedia({ media: a, score: a.popularity || ((window.S5Const?.RATING?.DEFAULT_POPULARITY_SCORE) ?? 0.7) })); if(window.currentLibraryPage===1){ window.mediaData = mapped; } else { const seen = new Set(window.mediaData.map(x=>x.id)); for(const m of mapped){ if(!seen.has(m.id)) window.mediaData.push(m); } } window.filteredData = [...window.mediaData]; for(const e of (data.items||[])){ window.libraryByMediaId[e.mediaId] = { favorite: !!e.favorite, watched: !!e.watched, dropped: !!e.dropped, rating: e.rating ?? null, updatedAt: e.updatedAt }; } if (typeof window.applySortAndFilters === 'function') { window.applySortAndFilters(); } else { displayMedia(window.filteredData); } const btn = Dom.$('moreBtn'); if(btn){ if((data.items||[]).length === PAGE_SIZE) btn.classList.remove('hidden'); else btn.classList.add('hidden'); } }catch{ displayMedia([]); showToast('Failed to load library', 'error'); }
  console.log('[S5] loadLibrary: done, items', (window.filteredData||[]).length);
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

  window.removeCardFromForYou = function(mediaId){ 
    // Only remove cards from "For You" mode, not from "Free Browsing" or "Library"
    if(window.currentView!=='forYou') return; 
    window.mediaData = window.mediaData.filter(a=>a.id!==mediaId); 
    window.filteredData = window.filteredData.filter(a=>a.id!==mediaId); 
    const grid = Dom.$('mediaGrid'); 
    const card = grid?.querySelector(`[data-media-id="${CSS.escape(String(mediaId))}"]`); 
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
      displayMedia(window.filteredData); 
    } 
  };
})();

// Dual-range helpers (module-private)
function initDualRangeControls(onChange){
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
  const onRating = (e)=> { clampPair(ratingMin, ratingMax, rMin, rMax, ratingStep, updateRatingUi, e?.target); if (typeof onChange === 'function') onChange(); };
    ratingMin.addEventListener('input', onRating);
    ratingMax.addEventListener('input', onRating);
    updateRatingUi();
  }
  const now = new Date(); const yMaxAbs = now.getFullYear(); const yMinAbs = yMaxAbs - (Y.WINDOW_YEARS || 30);
  if (yearMin && yearMax){
    yearMin.min = String(yMinAbs); yearMin.max = String(yMaxAbs); yearMin.step = '1'; yearMin.value = String(yMinAbs);
    yearMax.min = String(yMinAbs); yearMax.max = String(yMaxAbs); yearMax.step = '1'; yearMax.value = String(yMaxAbs);
  const onYear = (e)=> { clampPair(yearMin, yearMax, yMinAbs, yMaxAbs, 1, updateYearUi, e?.target); if (typeof onChange === 'function') onChange(); };
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
  // No overlay needed in stacked layout.
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
  // No overlay needed in stacked layout.
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
