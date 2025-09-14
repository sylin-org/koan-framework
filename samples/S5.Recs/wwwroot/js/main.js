// S5.Recs UI: delegated interactions for browse grid/list
// Relies on global functions defined in index.html (openDetails, toggleFavorite, markWatched, markDropped, openQuickRate, togglePreferredTag)

document.addEventListener('DOMContentLoaded', () => {
  const grid = document.getElementById('animeGrid');
  if (!grid) return;

  grid.addEventListener('click', (e) => {
    // Action buttons (rate/favorite/watched/dropped/status) take precedence
    const act = e.target.closest('[data-action]');
    if (act) {
      const action = act.getAttribute('data-action');
      const id = act.getAttribute('data-id');
      if (!id) return;
      e.preventDefault();
      if (action === 'status' && window.toggleStatusDropdown) {
        window.toggleStatusDropdown(id, act);
      } else if (action === 'favorite' && window.toggleFavorite) {
        window.toggleFavorite(id);
      } else if (action === 'watched' && window.markWatched) {
        window.markWatched(id);
      } else if (action === 'dropped' && window.markDropped) {
        window.markDropped(id);
      } else if (action === 'rate' && window.openQuickRate) {
        const rating = parseInt(act.getAttribute('data-rating') || '0', 10);
        if (rating > 0) window.openQuickRate(id, rating);
      }
      return;
    }

    // Open details on image container or image
    const openEl = e.target.closest('[data-open-details]');
    if (openEl) {
      const id = openEl.getAttribute('data-open-details');
      if (id && window.openDetails) {
        e.preventDefault();
        window.openDetails(id);
      }
      return;
    }
    // Tag toggles in cards/list
    const tagEl = e.target.closest('[data-tag]');
    if (tagEl) {
      const tag = tagEl.getAttribute('data-tag');
      if (tag && window.togglePreferredTag) {
        e.preventDefault();
        window.togglePreferredTag(tag);
      }
      return;
    }
  // (no-op here; actions handled above)
  });

  // Hover effect for star bars: light stars up to hovered one
  grid.addEventListener('mouseover', (e) => {
    const star = e.target.closest('.star-btn');
    if (!star) return;
    const bar = star.closest('.star-bar');
    if (!bar) return;
    const rating = parseInt(star.getAttribute('data-rating') || '0', 10);
    const stars = Array.from(bar.querySelectorAll('.star-btn'));
    stars.forEach(btn => {
      const r = parseInt(btn.getAttribute('data-rating') || '0', 10);
      if (r <= rating) btn.classList.add('hover'); else btn.classList.remove('hover');
    });
  });
  grid.addEventListener('mouseout', (e) => {
    const bar = e.target.closest('.star-bar');
    // If leaving the entire bar, clear hover
    if (bar && !bar.contains(e.relatedTarget)) {
      bar.querySelectorAll('.star-btn.hover').forEach(el => el.classList.remove('hover'));
    }
  });

  // Keyboard support (Enter/Space) on image container or image with data-open-details
  grid.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return;
    const openEl = e.target.closest('[data-open-details]');
    if (openEl) {
      e.preventDefault();
      const id = openEl.getAttribute('data-open-details');
      if (id && window.openDetails) window.openDetails(id);
    }
  });
});
