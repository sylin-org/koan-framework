// S5.Recs API client (classic script)
// Exposes window.S5Api with small, resilient wrappers
(function(){
  const JSON_HDR = { 'Content-Type': 'application/json' };
  function qsp(obj){
    const p = new URLSearchParams();
    for(const [k,v] of Object.entries(obj||{})){
      if(v === undefined || v === null || v === '') continue;
      p.append(k, String(v));
    }
    const s = p.toString();
    return s ? `?${s}` : '';
  }
  async function get(url){
    try{ const r = await fetch(url); if(!r.ok) throw new Error('HTTP '+r.status); return await r.json(); }
    catch{ return null; }
  }
  async function post(url, body){
    try{ const r = await fetch(url, { method:'POST', headers: JSON_HDR, body: JSON.stringify(body||{}) }); if(!r.ok) throw new Error('HTTP '+r.status); return await r.json(); }
    catch{ return null; }
  }
  async function put(url, body){
    try{ const r = await fetch(url, { method:'PUT', headers: JSON_HDR, body: JSON.stringify(body||{}) }); if(!r.ok) throw new Error('HTTP '+r.status); return await r.json(); }
    catch{ return null; }
  }

  const api = {
    async getUsers(){ return (await get('/api/users')) || []; },
    async createUser(name){ return await post('/api/users', { name }); },
    async getUserStats(userId){ return await get(`/api/users/${encodeURIComponent(userId)}/stats`) || {}; },
    async getRecsSettings(){ return await get('/admin/recs-settings') || {}; },
    async getLibrary(userId, { sort, page, pageSize, status } = {}){
      const qs = qsp({ sort, page, pageSize, status });
      return await get(`/api/library/${encodeURIComponent(userId)}${qs}`) || { items: [] };
    },
    async putLibrary(userId, animeId, body){ return await put(`/api/library/${encodeURIComponent(userId)}/${encodeURIComponent(animeId)}`, body||{}); },
    async postRate(userId, animeId, rating){ return await post('/api/recs/rate', { userId, animeId, rating }); },
    async recsQuery(body){ return await post('/api/recs/query', body||{}) || { items: [] }; },
    async getAnimeByIds(ids){
      const arr = Array.isArray(ids) ? ids : [];
      if(arr.length === 0) return [];
      const qs = `?ids=${encodeURIComponent(arr.join(','))}`;
      return (await get(`/api/anime/by-ids${qs}`)) || [];
    },
    async getTags(sort){ return (await get(`/api/tags${qsp({ sort })}`)) || []; }
  };

  window.S5Api = api;
})();
