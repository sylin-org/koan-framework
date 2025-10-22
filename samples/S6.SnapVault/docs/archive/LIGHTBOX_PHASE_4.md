# Phase 4: Actions Migration - Unified Photo Actions

**Duration:** 8-12 hours
**Dependencies:** Phase 1 (Foundation) complete (Phase 3 optional)
**Goal:** Move all photo actions into unified panel

---

## Context

Consolidate all photo actions into the Actions section of the unified panel:
- Favorite toggle (⭐)
- Star rating (1-5 stars)
- Download button
- Delete button (with confirmation)
- Regenerate AI description button

All actions use existing API endpoints (no backend changes needed).

---

## Tasks

### 1. Create `lightboxActions.js` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/js/components/lightboxActions.js`

**Class Structure:**
```javascript
export class LightboxActions {
  constructor(lightbox, app) {
    this.lightbox = lightbox;
    this.app = app;
    this.currentPhoto = null;
  }

  setPhoto(photo) {
    this.currentPhoto = photo;
  }

  async toggleFavorite() {
    if (!this.currentPhoto) return;

    try {
      const response = await fetch(`/api/photos/${this.currentPhoto.id}/favorite`, {
        method: 'POST'
      });

      const data = await response.json();
      this.currentPhoto.isFavorite = data.isFavorite;

      // Update UI
      this.updateFavoriteButton(data.isFavorite);

      // Show toast
      this.app.components.toast.show(
        data.isFavorite ? 'Added to favorites' : 'Removed from favorites',
        { icon: data.isFavorite ? '⭐' : '☆', duration: 2000 }
      );
    } catch (error) {
      console.error('Failed to toggle favorite:', error);
      this.app.components.toast.show('Failed to update favorite', {
        icon: '❌',
        duration: 3000
      });
    }
  }

  updateFavoriteButton(isFavorite) {
    const btn = document.getElementById('btn-favorite');
    if (!btn) return;

    btn.classList.toggle('active', isFavorite);
    btn.querySelector('.icon').textContent = isFavorite ? '⭐' : '☆';

    // Heart beat animation
    if (isFavorite) {
      btn.classList.add('animate-heartbeat');
      setTimeout(() => btn.classList.remove('animate-heartbeat'), 600);
    }
  }

  async setRating(rating) {
    if (!this.currentPhoto || rating < 0 || rating > 5) return;

    try {
      const response = await fetch(`/api/photos/${this.currentPhoto.id}/rate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rating })
      });

      const data = await response.json();
      this.currentPhoto.rating = data.rating;

      // Update UI
      this.updateRatingStars(data.rating);

      // Show toast
      this.app.components.toast.show(
        rating === 0 ? 'Rating removed' : `Rated ${rating} star${rating > 1 ? 's' : ''}`,
        { icon: '⭐', duration: 2000 }
      );
    } catch (error) {
      console.error('Failed to set rating:', error);
      this.app.components.toast.show('Failed to update rating', {
        icon: '❌',
        duration: 3000
      });
    }
  }

  updateRatingStars(rating) {
    const stars = document.querySelectorAll('.rating-star');
    stars.forEach((star, index) => {
      star.classList.toggle('filled', index < rating);
    });
  }

  async download() {
    if (!this.currentPhoto) return;

    // Trigger browser download
    window.location.href = `/api/photos/${this.currentPhoto.id}/download`;

    this.app.components.toast.show('Download started', {
      icon: '⬇️',
      duration: 2000
    });
  }

  async delete() {
    if (!this.currentPhoto) return;

    // Confirmation dialog
    const confirmed = confirm(
      `Delete "${this.currentPhoto.originalFileName}"? This cannot be undone.`
    );

    if (!confirmed) return;

    try {
      const response = await fetch('/api/photos/bulk/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ photoIds: [this.currentPhoto.id] })
      });

      const data = await response.json();

      if (data.deleted > 0) {
        // Success: Close lightbox, remove from gallery
        this.lightbox.close();
        this.app.loadPhotos(); // Reload gallery

        this.app.components.toast.show('Photo deleted', {
          icon: '✓',
          duration: 3000
        });
      } else {
        throw new Error(data.errors[0] || 'Delete failed');
      }
    } catch (error) {
      console.error('Failed to delete photo:', error);
      this.app.components.toast.show('Failed to delete photo', {
        icon: '❌',
        duration: 3000
      });
    }
  }

  async regenerateAI() {
    if (!this.currentPhoto) return;

    const btn = document.getElementById('btn-regenerate');
    if (!btn) return;

    // Show loading state
    btn.disabled = true;
    btn.classList.add('loading');
    btn.querySelector('.label').textContent = 'Regenerating...';

    try {
      const response = await fetch(`/api/photos/${this.currentPhoto.id}/regenerate-ai`, {
        method: 'POST'
      });

      if (!response.ok) throw new Error('Regenerate failed');

      // Poll for completion (max 60s)
      const startTime = Date.now();
      const pollInterval = 1000;
      const timeout = 60000;

      const poll = async () => {
        if (Date.now() - startTime > timeout) {
          throw new Error('Regeneration timed out');
        }

        // Fetch updated photo data
        const photoResponse = await fetch(`/api/photos/${this.currentPhoto.id}`);
        const updatedPhoto = await photoResponse.json();

        // Check if AI description is updated
        if (updatedPhoto.detailedDescription && updatedPhoto.detailedDescription !== this.currentPhoto.detailedDescription) {
          // Success
          this.currentPhoto = updatedPhoto;
          this.lightbox.panel.renderAIInsights(updatedPhoto);

          btn.disabled = false;
          btn.classList.remove('loading');
          btn.querySelector('.label').textContent = 'Regenerate Description';

          this.app.components.toast.show('AI description regenerated', {
            icon: '✓',
            duration: 3000
          });
        } else {
          // Still processing, poll again
          setTimeout(poll, pollInterval);
        }
      };

      poll();
    } catch (error) {
      console.error('Failed to regenerate AI:', error);

      btn.disabled = false;
      btn.classList.remove('loading');
      btn.querySelector('.label').textContent = 'Regenerate Description';

      this.app.components.toast.show('Failed to regenerate AI description', {
        icon: '❌',
        duration: 3000
      });
    }
  }
}
```

---

### 2. Update `lightboxPanel.js` Actions Section

**Add action buttons HTML:**

```javascript
renderActions(photo) {
  const actionsSection = this.container.querySelector('#actions-section');
  if (!actionsSection) return;

  actionsSection.innerHTML = `
    <h3>Actions</h3>
    <div class="actions-grid">
      <!-- Favorite -->
      <button class="btn-action ${photo.isFavorite ? 'active' : ''}" id="btn-favorite" aria-label="Toggle favorite">
        <span class="icon">${photo.isFavorite ? '⭐' : '☆'}</span>
        <span class="label">Favorite</span>
      </button>

      <!-- Download -->
      <button class="btn-action" id="btn-download" aria-label="Download photo">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
        <span class="label">Download</span>
      </button>

      <!-- Delete -->
      <button class="btn-action btn-destructive" id="btn-delete" aria-label="Delete photo">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="3 6 5 6 21 6"></polyline>
          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
        </svg>
        <span class="label">Delete</span>
      </button>
    </div>

    <!-- Star Rating -->
    <div class="rating-section">
      <label>Rating</label>
      <div class="rating-stars">
        ${[1, 2, 3, 4, 5].map(star => `
          <button class="rating-star ${photo.rating >= star ? 'filled' : ''}" data-rating="${star}" aria-label="${star} star${star > 1 ? 's' : ''}">
            ⭐
          </button>
        `).join('')}
      </div>
    </div>
  `;

  // Attach event listeners
  this.attachActionListeners();
}

attachActionListeners() {
  const actions = this.lightbox.actions;

  // Favorite
  const btnFavorite = document.getElementById('btn-favorite');
  btnFavorite?.addEventListener('click', () => actions.toggleFavorite());

  // Download
  const btnDownload = document.getElementById('btn-download');
  btnDownload?.addEventListener('click', () => actions.download());

  // Delete
  const btnDelete = document.getElementById('btn-delete');
  btnDelete?.addEventListener('click', () => actions.delete());

  // Rating stars
  const ratingStars = document.querySelectorAll('.rating-star');
  ratingStars.forEach(star => {
    star.addEventListener('click', () => {
      const rating = parseInt(star.dataset.rating);
      actions.setRating(rating);
    });
  });
}
```

---

### 3. Add Action Styles to `lightbox-panel.css`

```css
/* Actions Section */
.actions-grid {
  display: grid;
  gap: 8px;
  margin-bottom: 16px;
}

.btn-action {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: rgba(255, 255, 255, 0.9);
  font-size: 14px;
  cursor: pointer;
  transition: all 150ms;
}

.btn-action:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.2);
}

.btn-action:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-action.active {
  background: rgba(59, 130, 246, 0.15);
  border-color: rgba(59, 130, 246, 0.3);
  color: rgba(96, 165, 250, 1);
}

.btn-action.btn-destructive {
  color: rgba(239, 68, 68, 0.9);
  border-color: rgba(239, 68, 68, 0.3);
}

.btn-action.btn-destructive:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.1);
  border-color: rgba(239, 68, 68, 0.5);
}

/* Favorite Animation */
@keyframes heartbeat {
  0%, 100% { transform: scale(1); }
  25% { transform: scale(1.3); }
  50% { transform: scale(1.1); }
  75% { transform: scale(1.2); }
}

.btn-action.animate-heartbeat {
  animation: heartbeat 600ms ease-out;
}

/* Loading State */
.btn-action.loading .icon {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Star Rating */
.rating-section {
  margin-top: 16px;
}

.rating-section label {
  display: block;
  font-size: 13px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: rgba(255, 255, 255, 0.5);
  margin-bottom: 8px;
}

.rating-stars {
  display: flex;
  gap: 4px;
}

.rating-star {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  padding: 4px;
  opacity: 0.3;
  transition: opacity 150ms, transform 150ms;
}

.rating-star:hover {
  opacity: 0.7;
  transform: scale(1.1);
}

.rating-star.filled {
  opacity: 1;
}
```

---

### 4. Integrate `LightboxActions` into `Lightbox`

```javascript
import { LightboxActions } from './lightboxActions.js';

export class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.actions = new LightboxActions(this, app);
  }

  async open(photoId) {
    // ... existing open logic ...

    // Set current photo for actions
    this.actions.setPhoto(photo);

    // ... rest of open logic ...
  }
}
```

---

## Verification Steps

```javascript
// Favorite toggle test:
app.components.lightbox.open(app.photos[0].id);
app.components.lightbox.panel.open();
// Click favorite button:
// - Button animates (heartbeat) ✓
// - Icon changes: ☆ → ⭐ ✓
// - API called ✓
// - Toast shows "Added to favorites" ✓
// Click again:
// - Icon changes: ⭐ → ☆ ✓
// - Toast shows "Removed from favorites" ✓

// Rating test:
// Click 3rd star:
// - Stars 1-3 filled (opacity 1) ✓
// - Stars 4-5 unfilled (opacity 0.3) ✓
// - API called with rating: 3 ✓
// - Toast shows "Rated 3 stars" ✓

// Download test:
// Click download button:
// - Browser download triggered ✓
// - Toast shows "Download started" ✓

// Delete test:
// Click delete button:
// - Confirmation dialog shows ✓
// - Confirm: API called, lightbox closes, photo removed from gallery ✓
// - Cancel: No action, lightbox stays open ✓

// AI regenerate test (if AI available):
// Click "Regenerate Description" button:
// - Button disables ✓
// - Button shows "Regenerating..." ✓
// - Loading spinner appears ✓
// - After ~15-30s, new description appears in panel ✓
// - Button re-enables ✓
// - Toast shows "AI description regenerated" ✓

// Error handling test:
// Disconnect network, click favorite:
// - Toast shows error message ✓
// - Button state reverts ✓
// - No console errors cause crashes ✓
```

---

## Success Criteria

- [ ] All actions accessible from panel Actions section
- [ ] Favorite toggle:
  - [ ] Works correctly (API updates photo)
  - [ ] Heart beat animation plays
  - [ ] Toast notification shows
- [ ] Star rating:
  - [ ] Updates immediately with visual feedback
  - [ ] API call successful
  - [ ] Toast notification shows
- [ ] Download:
  - [ ] Triggers browser download
  - [ ] Toast notification shows
- [ ] Delete:
  - [ ] Shows confirmation dialog
  - [ ] On confirm: Deletes photo, closes lightbox, removes from gallery
  - [ ] On cancel: No action
- [ ] AI regenerate:
  - [ ] Shows loading state
  - [ ] Polls for completion (max 60s)
  - [ ] Updates panel with new description
  - [ ] Handles timeout gracefully
- [ ] All actions show success/error feedback via toast
- [ ] No console errors during API calls
- [ ] Loading states prevent double-clicks

---

## Rollback Strategy

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightboxPanel.js
git clean -f samples/S6.SnapVault/wwwroot/js/components/lightboxActions.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/css/lightbox-panel.css
```

---

## Next Steps

- **Phase 5:** Add comprehensive keyboard shortcuts

See: [Phase 5 Documentation](./LIGHTBOX_PHASE_5.md)
