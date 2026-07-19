const viewerId = 'demo';
const state = { catalog: [], ratings: new Map() };
const cardTemplate = document.querySelector('#anime-card');
const catalogGrid = document.querySelector('#catalog-grid');
const recommendationGrid = document.querySelector('#recommendations');
const status = document.querySelector('#status');
const taste = document.querySelector('#taste');

async function api(path, options) {
  const response = await fetch(path, options);
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || `Request failed (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}

function symbolFor(title) {
  return title.split(/\s+/).filter(word => /[A-Za-z0-9]/.test(word)).slice(0, 2).map(word => word[0]).join('').toUpperCase();
}

function cardFor(anime, reason = '') {
  const card = cardTemplate.content.firstElementChild.cloneNode(true);
  const poster = card.querySelector('.poster');
  poster.style.setProperty('--accent', anime.accent);
  poster.dataset.symbol = symbolFor(anime.title);
  card.querySelector('.poster-mark').textContent = anime.title;
  card.querySelector('.poster-year').textContent = anime.year;
  card.querySelector('.format').textContent = `${anime.format} · ${anime.episodes ?? '—'} eps`;
  card.querySelector('.score').textContent = `★ ${anime.communityScore.toFixed(1)}`;
  card.querySelector('h3').textContent = anime.title;
  card.querySelector('.synopsis').textContent = anime.synopsis;
  card.querySelector('.reason').textContent = reason;
  const tags = card.querySelector('.tags');
  [...anime.genres, ...anime.themes].slice(0, 3).forEach(value => {
    const tag = document.createElement('span');
    tag.className = 'tag';
    tag.textContent = value;
    tags.append(tag);
  });
  const rating = card.querySelector('.rating');
  for (let value = 1; value <= 5; value++) {
    const star = document.createElement('button');
    star.className = `star ${value <= (state.ratings.get(anime.id) || 0) ? 'active' : ''}`;
    star.type = 'button';
    star.textContent = '★';
    star.title = `${value} star${value === 1 ? '' : 's'}`;
    star.setAttribute('aria-pressed', String(value === (state.ratings.get(anime.id) || 0)));
    star.addEventListener('click', () => rate(anime, value));
    rating.append(star);
  }
  return card;
}

function renderCatalog() {
  catalogGrid.replaceChildren(...state.catalog.map(anime => cardFor(anime)));
}

function updateTaste() {
  const loved = state.catalog.filter(anime => (state.ratings.get(anime.id) || 0) >= 4).map(anime => anime.title);
  if (!loved.length) {
    taste.textContent = 'Rate a title 4 or 5 stars to shape your taste.';
    return;
  }
  const anchors = document.createElement('strong');
  anchors.textContent = loved.join(', ');
  taste.replaceChildren('Taste anchors: ', anchors);
}

async function rate(anime, rating) {
  status.className = 'status';
  status.textContent = `Remembering your ${rating}-star rating for ${anime.title}…`;
  try {
    await api(`/api/anime/viewers/${viewerId}/ratings/${anime.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rating })
    });
    state.ratings.set(anime.id, rating);
    renderCatalog();
    updateTaste();
    status.textContent = `Taste updated. Ask again to see what changed.`;
  } catch (error) {
    status.className = 'status error';
    status.textContent = error.message;
  }
}

async function recommend() {
  const mood = document.querySelector('#mood').value.trim();
  status.className = 'status';
  status.textContent = 'Reading the mood against your taste…';
  recommendationGrid.replaceChildren();
  try {
    const feed = await api(`/api/anime/recommendations?viewerId=${viewerId}&mood=${encodeURIComponent(mood)}&take=9`);
    recommendationGrid.replaceChildren(...feed.items.map(item => cardFor(item.anime, item.reason)));
    status.textContent = `${feed.items.length} picks · shaped by ${feed.tasteAnchors.join(', ') || 'your mood'}`;
  } catch (error) {
    status.className = 'status error';
    status.textContent = error.message;
  }
}

document.querySelector('#recommend-form').addEventListener('submit', event => {
  event.preventDefault();
  recommend();
});

async function start() {
  try {
    const [catalog, library] = await Promise.all([
      api('/api/anime/catalog'),
      api('/api/anime/library')
    ]);
    state.catalog = catalog.sort((left, right) => right.communityScore - left.communityScore);
    library.filter(entry => entry.viewerId === viewerId).forEach(entry => state.ratings.set(entry.animeId, entry.rating));
    renderCatalog();
    updateTaste();
    await recommend();
  } catch (error) {
    status.className = 'status error';
    status.textContent = error.message;
  }
}

start();
