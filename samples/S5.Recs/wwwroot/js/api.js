// S5.Recs API client (classic script)
// Exposes window.S5Api with small, resilient wrappers
(function(){
  const JSON_HDR = { 'Content-Type': 'application/json' };
  async function parseJsonSafely(r){
    // Treat 204/205 as success with empty object
    if(r.status === 204 || r.status === 205) return {};
    const ct = (r.headers && r.headers.get && r.headers.get('content-type')) || '';
    if(ct && ct.toLowerCase().includes('application/json')){
      try{ return await r.json(); }catch{ return {}; }
    }
    // No/unknown content-type → return empty object on success
    return {};
  }
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
    console.log('[S5Api] GET', url);
    try{ const r = await fetch(url, { credentials: 'include' }); if(!r.ok) throw new Error('HTTP '+r.status); const data = await parseJsonSafely(r); console.log('[S5Api] GET response', url, data); return data; }
    catch(e){ console.warn('[S5Api] GET failed', url, e); return null; }
  }
  async function post(url, body){
    console.log('[S5Api] POST', url, body);
    try{ const r = await fetch(url, { method:'POST', headers: JSON_HDR, body: JSON.stringify(body||{}), credentials: 'include' }); if(!r.ok) throw new Error('HTTP '+r.status); const data = await parseJsonSafely(r); console.log('[S5Api] POST response', url, data); return data; }
    catch(e){ console.warn('[S5Api] POST failed', url, e); return null; }
  }
  async function put(url, body){
    console.log('[S5Api] PUT', url, body);
    try{ const r = await fetch(url, { method:'PUT', headers: JSON_HDR, body: JSON.stringify(body||{}), credentials: 'include' }); if(!r.ok) throw new Error('HTTP '+r.status); const data = await parseJsonSafely(r); console.log('[S5Api] PUT response', url, data); return data; }
    catch(e){ console.warn('[S5Api] PUT failed', url, e); return null; }
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
    async putLibrary(userId, mediaId, body){
      const B = (window.S5Const?.ENDPOINTS?.LIBRARY_BASE) || '/api/library';
      return await put(`${B}/${encodeURIComponent(userId)}/${encodeURIComponent(mediaId)}`, body||{});
    },
    async postRate(userId, mediaId, rating){
      const R = (window.S5Const?.ENDPOINTS?.RATE) || '/api/recs/rate';
      return await post(R, { userId, mediaId, rating });
    },
    async recsQuery(body){
      const RQ = (window.S5Const?.ENDPOINTS?.RECS_QUERY) || '/api/recs/query';
      return await post(RQ, body||{}) || { items: [] };
    },
    async getMediaByIds(ids){
      const arr = Array.isArray(ids) ? ids : [];
      if(arr.length === 0) return [];
      const MB = (window.S5Const?.ENDPOINTS?.MEDIA_BY_IDS) || '/api/media/by-ids';
      const qs = `?ids=${encodeURIComponent(arr.join(','))}`;
      return (await get(`${MB}${qs}`)) || [];
    },
    // Legacy compatibility
    async getAnimeByIds(ids){
      return await this.getMediaByIds(ids);
    },
    async getMediaTypes(){
      const MT = (window.S5Const?.ENDPOINTS?.MEDIA_TYPES) || '/api/media-types';
      return (await get(MT)) || [];
    },
    async getMediaFormats(mediaType){
      const MF = (window.S5Const?.ENDPOINTS?.MEDIA_FORMATS) || '/api/media-formats';
      return (await get(`${MF}${qsp({ mediaType })}`)) || [];
    },
    async getTags(sort, showCensored){
      const T = (window.S5Const?.ENDPOINTS?.TAGS) || '/api/tags';
      return (await get(`${T}${qsp({ sort, showCensored })}`)) || [];
    },
    async getGenres(sort){
      const G = (window.S5Const?.ENDPOINTS?.GENRES) || '/api/genres';
      return (await get(`${G}${qsp({ sort })}`)) || [];
    }
  };

  window.S5Api = api;
})();
