// S5.Recs filtering and sorting helpers (classic script)
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

  window.S5Filters = { filter, sort, search };
})();
