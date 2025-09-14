// S5.Recs card rendering helpers (classic script)
// Exposes window.S5Cards with pure renderers
(function(){
  function __q(v){ return `'${String(v ?? '').replace(/'/g, "\\'")}'`; }
  function h(v){
    const s = String(v ?? '');
    return s
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/\"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
  function renderStarBar(animeId, currentRating){
    const stars = (window.S5Const && window.S5Const.RATING && typeof window.S5Const.RATING.STARS === 'number') ? window.S5Const.RATING.STARS : 5;
    let html = '<div class="star-bar flex items-center gap-1 bg-black/50 rounded-md px-2 py-1 border border-slate-700"'
      + ' data-id=' + __q(animeId) + (currentRating ? ' data-current-rating="' + String(currentRating) + '"' : '') + '>';
    for (let n=1; n<=stars; n++){
      const active = currentRating && n <= currentRating ? ' active' : '';
      html += '<button type="button" class="star-btn text-xs text-gray-400' + active + '"'
        + ' data-action="rate" data-id=' + __q(animeId) + ' data-rating="' + n + '"'
        + ' aria-label="Rate ' + n + ' star' + (n>1?'s':'') + '">'
        + '<i class="fas fa-star"></i>'
        + '</button>';
    }
    html += '</div>';
    return html;
  }

  function createAnimeCard(anime, state){
    const libraryByAnimeId = state && state.libraryByAnimeId || {};
    const selectedPreferredTags = state && state.selectedPreferredTags || [];
    const entry = libraryByAnimeId?.[anime.id] || null;
    const isFav = !!(entry && entry.favorite);
    const isWatched = !!(entry && entry.watched);
    const isDropped = !!(entry && entry.dropped);
    const userRating = entry && typeof entry.rating === 'number' ? entry.rating : null;
    const genres = Array.isArray(anime.genres) ? anime.genres : [];
    const matchPreferred = selectedPreferredTags.length > 0 && genres.some(g => selectedPreferredTags.includes(g));
    const highlightCls = matchPreferred ? ' ring-2 ring-purple-500/60 ring-offset-1 ring-offset-slate-900' : '';

    // Determine current status for the contextual button
    let statusButton, statusClass, statusIcon, statusText;

    if (isWatched) {
      statusClass = 'bg-green-600 hover:bg-green-700 text-white ring-1 ring-green-400/40';
      statusIcon = 'fa-check-circle';
      statusText = 'Completed';
    } else if (isDropped) {
      statusClass = 'bg-red-600 hover:bg-red-700 text-white ring-1 ring-red-400/40';
      statusIcon = 'fa-times-circle';
      statusText = 'Dropped';
    } else if (entry) {
      statusClass = 'bg-blue-600 hover:bg-blue-700 text-white ring-1 ring-blue-400/40';
      statusIcon = 'fa-play-circle';
      statusText = 'Plan to Watch';
    } else {
      statusClass = 'bg-slate-800 hover:bg-slate-700 text-gray-300 border border-slate-600';
      statusIcon = 'fa-plus';
      statusText = 'Add to List';
    }

    statusButton = `<button class="relative ${statusClass} text-xs px-3 py-1.5 rounded-lg font-medium transition-all min-w-24 touch-target-44"
                            data-action="status" data-id=${__q(anime.id)} title="Manage status">
                      <i class='fas ${statusIcon} mr-1'></i>${statusText}
                    </button>`;

    return `
  <div data-anime-id="${anime.id}" data-genres="${h(genres.join('|'))}" class="bg-slate-900 rounded-xl overflow-hidden hover:scale-105 hover:shadow-2xl transition-all duration-300 group${highlightCls}">
          <div class="aspect-[3/4] relative overflow-hidden cursor-pointer group" data-open-details=${__q(anime.id)} role="button" tabindex="0" title="View details">
            <img src="${anime.coverUrl || '/images/missing-cover.svg'}" alt="${h(anime.title)}" class="w-full h-full object-cover group-hover:scale-110 transition-transform duration-300" onerror="this.onerror=null;this.src='/images/missing-cover.svg'" loading="lazy">
            <div class="absolute inset-0 bg-gradient-to-t from-black/60 via-transparent to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-300"></div>
    <div class="absolute bottom-2 left-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity duration-300">
              <div class="text-white text-sm font-medium mb-1">${h(anime.title)}</div>
              <div class="text-gray-300 text-xs">${anime.episodes} eps • ${anime.year}</div>
  <div class="mt-2 flex justify-start">${renderStarBar(anime.id, userRating)}</div>
            </div>
          </div>
          <div class="p-4">
            <h3 class="font-semibold text-white text-sm mb-2 line-clamp-2 group-hover:text-purple-300 transition-colors">${h(anime.title)}</h3>
            <div class="flex flex-wrap gap-1 mb-2">
              ${anime.genres.slice(0, (window.S5Const && window.S5Const.TAGS && typeof window.S5Const.TAGS.CHIPS_IN_CARD === 'number' ? window.S5Const.TAGS.CHIPS_IN_CARD : 2)).map(genre => {
                const sel = selectedPreferredTags.includes(genre);
                const cls = sel ? 'bg-purple-600 text-white' : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
                return `<button type=\"button\" data-tag=\"${h(genre)}\" class=\"text-xs px-2 py-0.5 rounded-full border border-slate-700 ${cls}\">${h(genre)}</button>`;
              }).join('')}
            </div>
            <div class="mt-3 flex items-center justify-between">
              ${statusButton}
              ${isFav && entry ? '<button class="text-pink-400 hover:text-pink-300 text-sm touch-target-44 transition-colors" data-action="favorite" data-id=' + __q(anime.id) + ' title="Remove from favorites"><i class="fas fa-heart"></i></button>' :
                (entry ? '<button class="text-gray-400 hover:text-pink-400 text-sm touch-target-44 transition-colors" data-action="favorite" data-id=' + __q(anime.id) + ' title="Add to favorites"><i class="far fa-heart"></i></button>' : '')}
            </div>
            <div class="mt-2 flex items-center justify-between text-xs text-gray-400">
              <span>${anime.type}</span>
              <span>${anime.year}</span>
            </div>
          </div>
        </div>
      `;
  }

  function createAnimeListItem(anime, state){
    const libraryByAnimeId = state && state.libraryByAnimeId || {};
    const selectedPreferredTags = state && state.selectedPreferredTags || [];
    const entry = libraryByAnimeId?.[anime.id] || null;
    const isFav = !!(entry && entry.favorite);
    const isWatched = !!(entry && entry.watched);
    const isDropped = !!(entry && entry.dropped);
    const userRating = entry && typeof entry.rating === 'number' ? entry.rating : null;
    const genres = Array.isArray(anime.genres) ? anime.genres : [];
    const matchPreferred = selectedPreferredTags.length > 0 && genres.some(g => selectedPreferredTags.includes(g));
    const highlightCls = matchPreferred ? ' ring-2 ring-purple-500/60 ring-offset-1 ring-offset-slate-900' : '';

    // Determine current status for the contextual button (same logic as card)
    let statusButton, statusClass, statusIcon, statusText;

    if (isWatched) {
      statusClass = 'bg-green-600 hover:bg-green-700 text-white ring-1 ring-green-400/40';
      statusIcon = 'fa-check-circle';
      statusText = 'Completed';
    } else if (isDropped) {
      statusClass = 'bg-red-600 hover:bg-red-700 text-white ring-1 ring-red-400/40';
      statusIcon = 'fa-times-circle';
      statusText = 'Dropped';
    } else if (entry) {
      statusClass = 'bg-blue-600 hover:bg-blue-700 text-white ring-1 ring-blue-400/40';
      statusIcon = 'fa-play-circle';
      statusText = 'Plan to Watch';
    } else {
      statusClass = 'bg-slate-800 hover:bg-slate-700 text-gray-300 border border-slate-600';
      statusIcon = 'fa-plus';
      statusText = 'Add to List';
    }

    statusButton = `<button class="relative ${statusClass} text-xs px-3 py-1.5 rounded-lg font-medium transition-all min-w-28 touch-target-44"
                            data-action="status" data-id=${__q(anime.id)} title="Manage status">
                      <i class='fas ${statusIcon} mr-1'></i>${statusText}
                    </button>`;

    return `
      <div data-anime-id="${anime.id}" data-genres="${h(genres.join('|'))}" class="bg-slate-900 rounded-xl overflow-hidden hover:shadow-xl transition-all p-3 flex gap-4 items-stretch group${highlightCls}">
        <img src="${anime.coverUrl || '/images/missing-cover.svg'}" alt="${h(anime.title)}" class="w-24 h-32 object-cover rounded-md cursor-pointer" onerror="this.onerror=null;this.src='/images/missing-cover.svg'" data-open-details=${__q(anime.id)} title="View details" loading="lazy" role="button" tabindex="0">
        <div class="flex-1 grid grid-cols-12 gap-3">
          <div class="col-span-12 md:col-span-8">
            <div class="flex items-start justify-between gap-3">
              <h3 class="font-semibold text-white text-base line-clamp-2">${h(anime.title)}</h3>
              <div class="text-xs text-gray-400 whitespace-nowrap">${anime.year} · ${anime.type}</div>
            </div>
            <div class="mt-1 text-xs text-gray-300 line-clamp-2">${h(anime.synopsis || '')}</div>
            <div class="flex flex-wrap gap-1 mt-2">
              ${anime.genres.slice(0,(window.S5Const && window.S5Const.TAGS && typeof window.S5Const.TAGS.CHIPS_IN_LIST === 'number' ? window.S5Const.TAGS.CHIPS_IN_LIST : 4)).map(g=>{
                const sel = selectedPreferredTags.includes(g);
                const cls = sel ? 'bg-purple-600 text-white' : 'bg-slate-800 text-gray-300 hover:bg-slate-700';
                return `<button type=\"button\" data-tag=\"${h(g)}\" class=\"text-xs px-2 py-0.5 rounded-full border border-slate-700 ${cls}\">${h(g)}</button>`;
              }).join('')}
            </div>
            ${userRating ? `<div class="mt-1 text-xs text-yellow-300">Your rating: ${userRating}★</div>` : ''}
          </div>
           <div class="col-span-12 md:col-span-4 flex md:flex-col gap-2 md:items-end items-start justify-end">
             <div class="opacity-0 group-hover:opacity-100 transition-opacity duration-150">${renderStarBar(anime.id, userRating)}</div>
             <div class="flex gap-2 md:flex-col items-end">
               ${statusButton}
               ${isFav && entry ? '<button class="text-pink-400 hover:text-pink-300 text-sm touch-target-44 transition-colors" data-action="favorite" data-id=' + __q(anime.id) + ' title="Remove from favorites"><i class="fas fa-heart"></i></button>' :
                 (entry ? '<button class="text-gray-400 hover:text-pink-400 text-sm touch-target-44 transition-colors" data-action="favorite" data-id=' + __q(anime.id) + ' title="Add to favorites"><i class="far fa-heart"></i></button>' : '')}
             </div>
           </div>
        </div>
      </div>
    `;
  }

  window.S5Cards = { createAnimeCard, createAnimeListItem };
})();
