/**
 * SnapVault — invited-guest proofing gallery (step 5f).
 *
 * The flow: a guest opens their link (guest.html?token=…) while signed in → the invite is bound to their
 * identity (POST /api/gallery/accept, verified-email ownership enforced server-side) → their event's photos load
 * (access-scoped by the SEC-0008 subject the middleware set from their grant) → they select / rate, each mark
 * recorded via the guest-write floor (POST /api/proofing/{photoId}). The guest can only ever see and mark photos
 * in the event they were invited to — that isolation is the framework's, not this page's.
 */
import { API } from './api.js';

const api = new API();
const statusEl = document.getElementById('status');
const galleryEl = document.getElementById('gallery');

async function main() {
  const token = new URLSearchParams(location.search).get('token');
  if (!token) {
    statusEl.textContent = 'This gallery link is missing its invitation token.';
    return;
  }

  let eventId;
  try {
    const res = await api.post('/api/gallery/accept', { token });
    eventId = res.eventId;
  } catch (error) {
    const message = String(error?.message ?? '');
    if (message.toLowerCase().includes('sign in')) {
      statusEl.innerHTML = 'Please sign in to view your gallery, then reload this page.';
    } else {
      statusEl.textContent = `This invitation could not be opened (${message}).`;
    }
    return;
  }

  await loadGallery(eventId);
}

async function loadGallery(eventId) {
  let photos;
  try {
    const res = await api.post('/api/photosets/query', {
      startIndex: 0,
      count: 500,
      definition: { context: 'event', eventId }
    });
    photos = res.photos || [];
  } catch (error) {
    statusEl.textContent = `Could not load your photos (${error?.message ?? 'error'}).`;
    return;
  }

  if (photos.length === 0) {
    statusEl.textContent = 'Your studio has not added any photos to this gallery yet.';
    return;
  }

  statusEl.hidden = true;
  galleryEl.hidden = false;
  galleryEl.innerHTML = photos.map(renderCard).join('');
  galleryEl.querySelectorAll('.pick').forEach(btn => btn.addEventListener('click', () => toggleSelect(btn)));
  galleryEl.querySelectorAll('.stars button').forEach(btn => btn.addEventListener('click', () => rate(btn)));
}

function renderCard(photo) {
  const id = escapeAttr(photo.id);
  return `
    <figure class="guest-card" data-photo="${id}">
      <img loading="lazy" src="/media/${id}/gallery" alt="${escapeAttr(photo.fileName)}">
      <figcaption>
        <button class="pick" type="button" aria-pressed="false" data-photo="${id}">☆ Select</button>
        <span class="stars" role="group" aria-label="Rate this photo">
          ${[1, 2, 3, 4, 5].map(n => `<button type="button" data-photo="${id}" data-value="${n}" aria-label="${n} star">★</button>`).join('')}
        </span>
      </figcaption>
    </figure>`;
}

async function toggleSelect(btn) {
  const photoId = btn.dataset.photo;
  const next = btn.getAttribute('aria-pressed') !== 'true';
  try {
    await api.post(`/api/proofing/${photoId}`, { selected: next });
    btn.setAttribute('aria-pressed', String(next));
    btn.textContent = next ? '★ Selected' : '☆ Select';
    btn.closest('.guest-card')?.classList.toggle('selected', next);
  } catch (error) {
    console.error('[guest] select failed', error);
  }
}

async function rate(btn) {
  const photoId = btn.dataset.photo;
  const value = parseInt(btn.dataset.value, 10);
  try {
    await api.post(`/api/proofing/${photoId}`, { rating: value });
    const stars = [...btn.parentElement.querySelectorAll('button')];
    stars.forEach((star, index) => star.classList.toggle('on', index < value));
  } catch (error) {
    console.error('[guest] rate failed', error);
  }
}

function escapeAttr(value) {
  const div = document.createElement('div');
  div.textContent = value ?? '';
  return div.innerHTML.replace(/"/g, '&quot;');
}

main();
