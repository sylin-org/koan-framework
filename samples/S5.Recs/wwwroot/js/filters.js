﻿// S5.Recs filtering and sorting helpers (classic script)
// Provides small, pure helpers to keep index.html lean
(function(){
  function getEpisodeRange(key){
    const S = (window.S5Const && window.S5Const.EPISODES) || {};
    const SHORT_MAX = S.SHORT_MAX ?? 12;
    const MEDIUM_MAX = S.MEDIUM_MAX ?? 25;
    switch(key){
      case 'short': return [1, SHORT_MAX];
      case 'medium': return [SHORT_MAX + 1, MEDIUM_MAX];
      case 'long': return [MEDIUM_MAX + 1, Number.POSITIVE_INFINITY];
      default: return [0, Number.POSITIVE_INFINITY];
    }
  }

  function filter(list, { genre, ratingMin, ratingMax, yearMin, yearMax, rating, year, episode } = {}){
    let out = Array.isArray(list) ? [...list] : [];
    if (genre) {
      out = out.filter(a => Array.isArray(a.genres) && a.genres.includes(genre));
    }
    // Back-compat: single rating => minimum
    if (rating && (ratingMin == null)) ratingMin = parseFloat(rating);
    if (ratingMin != null || ratingMax != null) {
      const rmin = (ratingMin != null) ? parseFloat(ratingMin) : null;
      const rmax = (ratingMax != null) ? parseFloat(ratingMax) : null;
      out = out.filter(a => {
        const r = a.rating ?? 0;
        if (rmin != null && r < rmin) return false;
        if (rmax != null && r > rmax) return false;
        return true;
      });
    }
    // Back-compat: single year exact match
    if (year && (yearMin == null && yearMax == null)) {
      out = out.filter(a => String(a.year || '') === String(year));
    } else if (yearMin != null || yearMax != null) {
      const ymin = (yearMin != null) ? parseInt(yearMin, 10) : null;
      const ymax = (yearMax != null) ? parseInt(yearMax, 10) : null;
      out = out.filter(a => {
        const y = parseInt(a.year || 0, 10) || null;
        if (y == null) return (ymin == null || ymax == null); // if missing, include unless both bounds active
        if (ymin != null && y < ymin) return false;
        if (ymax != null && y > ymax) return false;
        return true;
      });
    }
    if (episode) {
      const [min, max] = getEpisodeRange(episode);
      out = out.filter(a => {
        const eps = a.episodes || 0;
        return eps >= min && eps <= max;
      });
    }
    return out;
  }

  function sort(list, sortBy){
    const arr = Array.isArray(list) ? [...list] : [];
    switch(sortBy){
      case 'rating':
        arr.sort((a,b)=> (b.rating||0) - (a.rating||0));
        break;
      case 'recent':
        arr.sort((a,b)=> (parseInt(b.year||0) || 0) - (parseInt(a.year||0) || 0));
        break;
      case 'popular':
        arr.sort((a,b)=> (b.popularity||0) - (a.popularity||0));
        break;
      case 'relevance':
      default:
        // Keep current order
        break;
    }
    return arr;
  }

  function search(list, query){
    const q = (query||'').trim().toLowerCase();
    if(!q) return Array.isArray(list) ? [...list] : [];
    return (Array.isArray(list)? list: []).filter(a => {
      const t = (a.title||'').toLowerCase();
      const g = Array.isArray(a.genres) ? a.genres.map(x=>(x||'').toLowerCase()) : [];
      return t.includes(q) || g.some(x => x.includes(q));
    });
  }

  // Track and render active filter chips
  function updateFilterChips(activeFilters = {}) {
    const container = document.getElementById('activeFiltersContainer');
    const chipsContainer = document.getElementById('activeFilterChips');

    if (!container || !chipsContainer) return;

    const chips = [];

    // Genre filter chip
    if (activeFilters.genre) {
      chips.push({
        type: 'genre',
        label: `Genre: ${activeFilters.genre}`,
        value: activeFilters.genre
      });
    }

    // Rating range chip
    if (activeFilters.ratingMin != null || activeFilters.ratingMax != null) {
      const min = activeFilters.ratingMin || 1;
      const max = activeFilters.ratingMax || 5;
      chips.push({
        type: 'rating',
        label: `Rating: ${min}–${max}`,
        value: { min, max }
      });
    }

    // Year range chip
    if (activeFilters.yearMin != null || activeFilters.yearMax != null || activeFilters.year) {
      if (activeFilters.year) {
        chips.push({
          type: 'year',
          label: `Year: ${activeFilters.year}`,
          value: activeFilters.year
        });
      } else {
        const min = activeFilters.yearMin || '';
        const max = activeFilters.yearMax || '';
        const label = min && max ? `Year: ${min}–${max}` : min ? `Year: ${min}+` : `Year: –${max}`;
        chips.push({
          type: 'year',
          label: label,
          value: { min, max }
        });
      }
    }

    // Episode length chip
    if (activeFilters.episode) {
      const labels = {
        'short': 'Short (≤12 eps)',
        'medium': 'Medium (13–25 eps)',
        'long': 'Long (26+ eps)'
      };
      chips.push({
        type: 'episode',
        label: labels[activeFilters.episode] || `Episodes: ${activeFilters.episode}`,
        value: activeFilters.episode
      });
    }

    // Search query chip
    if (activeFilters.search && activeFilters.search.trim()) {
      chips.push({
        type: 'search',
        label: `Search: "${activeFilters.search.trim()}"`,
        value: activeFilters.search.trim()
      });
    }

    // Show/hide container based on active chips
    if (chips.length === 0) {
      container.classList.add('hidden');
      return;
    }

    container.classList.remove('hidden');

    // Render chips
    chipsContainer.innerHTML = chips.map(chip => `
      <span class="inline-flex items-center gap-1.5 px-3 py-1.5 bg-purple-900/30 text-purple-200 text-xs rounded-full border border-purple-700/50">
        <span>${chip.label}</span>
        <button type="button"
                class="hover:text-white hover:bg-purple-600/50 rounded-full p-0.5 transition-colors touch-target-44"
                data-remove-filter="${chip.type}"
                data-filter-value='${JSON.stringify(chip.value)}'
                title="Remove ${chip.label}">
          <i class="fas fa-times text-xs"></i>
        </button>
      </span>
    `).join('');
  }

  // Get current active filters from form elements
  function getActiveFilters() {
    const filters = {};

    // Get genre from dropdown
    const genreSelect = document.getElementById('genreFilter');
    if (genreSelect && genreSelect.value) {
      filters.genre = genreSelect.value;
    }

    // Get rating range
    const ratingMin = document.getElementById('ratingMin');
    const ratingMax = document.getElementById('ratingMax');
    if (ratingMin && ratingMin.value) {
      filters.ratingMin = parseFloat(ratingMin.value);
    }
    if (ratingMax && ratingMax.value) {
      filters.ratingMax = parseFloat(ratingMax.value);
    }

    // Get year filters
    const yearExact = document.getElementById('yearFilter');
    const yearMin = document.getElementById('yearMin');
    const yearMax = document.getElementById('yearMax');
    if (yearExact && yearExact.value) {
      filters.year = parseInt(yearExact.value, 10);
    } else {
      if (yearMin && yearMin.value) {
        filters.yearMin = parseInt(yearMin.value, 10);
      }
      if (yearMax && yearMax.value) {
        filters.yearMax = parseInt(yearMax.value, 10);
      }
    }

    // Get episode length
    const episodeFilter = document.getElementById('episodeFilter');
    if (episodeFilter && episodeFilter.value) {
      filters.episode = episodeFilter.value;
    }

    // Get search query
    const searchInput = document.getElementById('globalSearch');
    if (searchInput && searchInput.value && searchInput.value.trim()) {
      filters.search = searchInput.value.trim();
    }

    return filters;
  }

  // Clear specific filter
  function clearFilter(filterType) {
    switch (filterType) {
      case 'genre':
        const genreSelect = document.getElementById('genreFilter');
        if (genreSelect) genreSelect.value = '';
        break;
      case 'rating':
        const ratingMin = document.getElementById('ratingMin');
        const ratingMax = document.getElementById('ratingMax');
        if (ratingMin) ratingMin.value = '';
        if (ratingMax) ratingMax.value = '';
        break;
      case 'year':
        const yearExact = document.getElementById('yearFilter');
        const yearMin = document.getElementById('yearMin');
        const yearMax = document.getElementById('yearMax');
        if (yearExact) yearExact.value = '';
        if (yearMin) yearMin.value = '';
        if (yearMax) yearMax.value = '';
        break;
      case 'episode':
        const episodeFilter = document.getElementById('episodeFilter');
        if (episodeFilter) episodeFilter.value = '';
        break;
      case 'search':
        const searchInput = document.getElementById('globalSearch');
        if (searchInput) {
          searchInput.value = '';
          // Trigger search clear event for recommendations/library views
          if (window.handleGlobalSearch) {
            window.handleGlobalSearch({ target: searchInput });
          }
        }
        break;
    }

    // Trigger filter update if global function exists (except for search which handles its own updates)
    if (filterType !== 'search' && window.applyFilters) {
      window.applyFilters();
    }
  }

  // Clear all filters
  function clearAllFilters() {
    clearFilter('genre');
    clearFilter('rating');
    clearFilter('year');
    clearFilter('episode');

    // Also clear search if it exists
    const searchInput = document.getElementById('searchInput');
    if (searchInput) searchInput.value = '';

    // Trigger filter update if global function exists
    if (window.applyFilters) {
      window.applyFilters();
    }
  }

  window.S5Filters = { filter, sort, search, updateFilterChips, getActiveFilters, clearFilter, clearAllFilters };
})();
