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
    async getUsers(){
      const U = (window.S5Const?.ENDPOINTS?.USERS) || '/api/users';
      return (await get(U)) || [];
    },
    async createUser(name){
      const U = (window.S5Const?.ENDPOINTS?.USERS) || '/api/users';
      return await post(U, { name });
    },
    async getUserStats(userId){
      const U = (window.S5Const?.ENDPOINTS?.USERS) || '/api/users';
      return await get(`${U}/${encodeURIComponent(userId)}/stats`) || {};
    },
    async getRecsSettings(){
      const E = (window.S5Const?.ENDPOINTS?.RECS_SETTINGS) || '/admin/recs-settings';
      return await get(E) || {};
    },
    async getLibrary(userId, { sort, page, pageSize, status } = {}){
      const B = (window.S5Const?.ENDPOINTS?.LIBRARY_BASE) || '/api/library';
      const qs = qsp({ sort, page, pageSize, status });
      return await get(`${B}/${encodeURIComponent(userId)}${qs}`) || { items: [] };
    },
    async putLibrary(userId, animeId, body){
      const B = (window.S5Const?.ENDPOINTS?.LIBRARY_BASE) || '/api/library';
      return await put(`${B}/${encodeURIComponent(userId)}/${encodeURIComponent(animeId)}`, body||{});
    },
    async postRate(userId, animeId, rating){
      const R = (window.S5Const?.ENDPOINTS?.RATE) || '/api/recs/rate';
      return await post(R, { userId, animeId, rating });
    },
    async recsQuery(body){
      const RQ = (window.S5Const?.ENDPOINTS?.RECS_QUERY) || '/api/recs/query';
      return await post(RQ, body||{}) || { items: [] };
    },
    async getAnimeByIds(ids){
      const arr = Array.isArray(ids) ? ids : [];
      if(arr.length === 0) return [];
      const AB = (window.S5Const?.ENDPOINTS?.ANIME_BY_IDS) || '/api/anime/by-ids';
      const qs = `?ids=${encodeURIComponent(arr.join(','))}`;
      return (await get(`${AB}${qs}`)) || [];
    },
    async getTags(sort){
      const T = (window.S5Const?.ENDPOINTS?.TAGS) || '/api/tags';
      return (await get(`${T}${qsp({ sort })}`)) || [];
    }
  };

  window.S5Api = api;
})();
