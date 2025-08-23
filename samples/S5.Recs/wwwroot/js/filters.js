// S5.Recs filtering and sorting helpers (classic script)
// Provides small, pure helpers to keep index.html lean
(function(){
  function getEpisodeRange(key){
    switch(key){
      case 'short': return [1, 12];
      case 'medium': return [13, 25];
      case 'long': return [26, Number.POSITIVE_INFINITY];
      default: return [0, Number.POSITIVE_INFINITY];
    }
  }

  function filter(list, { genre, rating, year, episode } = {}){
    let out = Array.isArray(list) ? [...list] : [];
    if (genre) {
      out = out.filter(a => Array.isArray(a.genres) && a.genres.includes(genre));
    }
    if (rating) {
      const min = parseFloat(rating);
      out = out.filter(a => (a.rating || 0) >= min);
    }
    if (year) {
      out = out.filter(a => String(a.year || '') === String(year));
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
