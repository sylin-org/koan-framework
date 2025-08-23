// S5.Recs UI: delegated interactions for browse grid/list
// Relies on global functions defined in index.html (openDetails, toggleFavorite, markWatched, markDropped, openQuickRate, togglePreferredTag)

document.addEventListener('DOMContentLoaded', () => {
  const grid = document.getElementById('animeGrid');
  if (!grid) return;

  grid.addEventListener('click', (e) => {
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
    // Bottom action buttons
    const act = e.target.closest('[data-action]');
    if (act) {
      const action = act.getAttribute('data-action');
      const id = act.getAttribute('data-id');
      if (!id) return;
      if (action === 'favorite' && window.toggleFavorite) {
        window.toggleFavorite(id);
      } else if (action === 'watched' && window.markWatched) {
        window.markWatched(id);
      } else if (action === 'dropped' && window.markDropped) {
        window.markDropped(id);
      } else if (action === 'rate' && window.openQuickRate) {
        const rating = parseInt(act.getAttribute('data-rating') || '0', 10);
        if (rating > 0) window.openQuickRate(id, rating);
      }
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
