/**
 * Photo Grid Component with Virtual Scrolling
 * Masonry layout with hover interactions
 */

import { VIEW_PRESETS, selectOptimalImageTier, getResponsiveColumns } from '../viewPresets.js';

export class PhotoGrid {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.photo-grid');
    this.photoCards = new Map();
    this.observer = null;
    this.infiniteScrollObserver = null;
    this.viewportWidth = window.innerWidth;
    this.devicePixelRatio = window.devicePixelRatio || 1;
    this.setupIntersectionObserver();
    this.setupViewportDetection();
    this.detectMasonrySupport();
  }

  detectMasonrySupport() {
    // Detect CSS Grid Masonry support (grid-template-rows: masonry)
    this.supportsMasonry = CSS.supports('grid-template-rows', 'masonry');
    console.log(`[Grid] CSS Masonry support: ${this.supportsMasonry ? 'Yes (native)' : 'No (using CSS columns fallback)'}`);
  }

  setupViewportDetection() {
    // Update viewport dimensions on resize with debouncing
    let resizeTimer;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => {
        this.viewportWidth = window.innerWidth;
        this.devicePixelRatio = window.devicePixelRatio || 1;
        console.log(`[Grid] Viewport updated: ${this.viewportWidth}px @ ${this.devicePixelRatio}x DPI`);
      }, 250);
    });
  }

  setupIntersectionObserver() {
    this.observer = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        const img = entry.target;
        if (entry.isIntersecting) {
          // Load image when it enters viewport
          if (img.dataset.src && !img.src) {
            img.src = img.dataset.src;
            img.removeAttribute('data-src');

            // No recalculation needed - aspect-ratio pre-allocation prevents layout shift
          }
        }
      });
    }, {
      rootMargin: '500px' // Load 500px before entering viewport
    });
  }

  render() {
    if (!this.container) return;

    const photos = this.app.state.photos;

    if (photos.length === 0) {
      this.renderEmpty();
      return;
    }

    // Hide empty state
    const emptyState = this.container.querySelector('.empty-state-hero');
    if (emptyState) {
      emptyState.style.display = 'none';
    }

    // Reset tier logging flag for new render
    this._tierLogged = false;

    // Clear existing photos
    const existingCards = this.container.querySelectorAll('.photo-card');
    existingCards.forEach(card => card.remove());

    // Apply CSS class based on masonry support
    this.container.classList.toggle('masonry-native', this.supportsMasonry);
    this.container.classList.toggle('masonry-fallback', !this.supportsMasonry);

    // Render all photos in current state
    photos.forEach(photo => {
      this.addPhotoCard(photo);
    });
  }

  renderEmpty() {
    const emptyState = this.container.querySelector('.empty-state-hero');
    if (emptyState) {
      emptyState.style.display = 'flex';
    }
  }

  /**
   * Get optimal image URL based on smart tier selection
   * @param {Object} photo - Photo object with media IDs
   * @returns {string} - Optimal image URL for current display
   */
  getOptimalImageUrl(photo) {
    const presetId = this.app.state.viewPreset || 'comfortable';
    const preset = VIEW_PRESETS[presetId];

    if (!preset) {
      console.warn(`Unknown preset: ${presetId}, falling back to masonry`);
      return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;
    }

    // Select optimal tier based on display characteristics
    const tier = selectOptimalImageTier(preset, this.viewportWidth, this.devicePixelRatio);

    // Log tier selection (first photo only to avoid spam)
    if (!this._tierLogged) {
      const columns = getResponsiveColumns(preset, this.viewportWidth);
      const tileWidth = Math.floor(this.viewportWidth / columns);
      const effectivePixels = Math.floor(tileWidth * this.devicePixelRatio);
      console.log(`🎯 Smart Resolution: ${tier} tier (${presetId} preset, ${columns} cols, ${tileWidth}px/tile × ${this.devicePixelRatio}x = ${effectivePixels}px effective)`);
      this._tierLogged = true;
    }

    // Map tier to appropriate endpoint
    switch (tier) {
      case 'gallery':
        // Gallery tier (1200px) - use gallery thumbnail if available
        if (photo.galleryThumbnailMediaId) {
          return `/api/media/gallery-thumbnails/${photo.galleryThumbnailMediaId}`;
        }
        // Fallback to masonry if gallery not available
        return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;

      case 'retina':
        // Retina tier (600px) - will be implemented in Phase 3
        // For now, fallback to masonry
        if (photo.retinaThumbnailMediaId) {
          return `/api/media/retina-thumbnails/${photo.retinaThumbnailMediaId}`;
        }
        return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;

      case 'masonry':
      default:
        // Masonry tier (300px) - aspect-ratio preserved
        return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;
    }
  }

  addPhotoCard(photo) {
    const card = this.createPhotoCard(photo);
    this.container.appendChild(card);
    this.photoCards.set(photo.id, card);

    // Observe image for lazy loading
    const img = card.querySelector('.photo-image');
    if (img && this.observer) {
      this.observer.observe(img);
    }
  }

  createPhotoCard(photo) {
    const article = document.createElement('article');
    article.className = 'photo-card';
    article.dataset.photoId = photo.id;

    // Pre-allocate space using aspect-ratio to eliminate layout shift
    const aspectRatio = photo.width / photo.height;
    article.dataset.aspect = aspectRatio.toFixed(2);
    article.style.aspectRatio = `${photo.width} / ${photo.height}`;

    // Smart tier selection based on view preset and display characteristics
    const thumbnailUrl = this.getOptimalImageUrl(photo);

    const isFavorite = photo.isFavorite || false;
    const rating = photo.rating || 0;

    article.innerHTML = `
      <div class="photo-skeleton"></div>
      <img class="photo-image" data-src="${thumbnailUrl}" alt="${this.escapeHtml(photo.originalFileName)}" loading="lazy" />
      <div class="photo-overlay">
        <div class="actions-top">
          <button class="btn-favorite ${isFavorite ? 'active' : ''}" aria-label="Favorite (F)" data-photo-id="${photo.id}">
            <svg class="icon" width="20" height="20" viewBox="0 0 24 24" fill="${isFavorite ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
          </button>
          <button class="btn-select" aria-label="Select (Space)" data-photo-id="${photo.id}">
            <svg class="icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            </svg>
          </button>
        </div>
        <div class="actions-bottom">
          <div class="metadata">
            ${photo.cameraModel || 'Unknown camera'}
            ${photo.capturedAt ? '• ' + this.formatDate(photo.capturedAt) : ''}
          </div>
          <div class="rating" data-photo-id="${photo.id}">
            ${this.renderStars(rating, photo.id)}
          </div>
        </div>
      </div>
      <div class="selection-indicator" style="display: none;">
        <svg class="icon" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
          <polyline points="20 6 9 17 4 12"></polyline>
        </svg>
      </div>
    `;

    // Event listeners
    const img = article.querySelector('.photo-image');
    img.addEventListener('load', () => {
      article.querySelector('.photo-skeleton')?.remove();
    });

    // Click card to open lightbox
    article.addEventListener('click', (e) => {
      // Don't open lightbox if clicking action buttons
      if (e.target.closest('.btn-favorite, .btn-select, .rating')) {
        return;
      }
      this.app.components.lightbox.open(photo.id);
    });

    // Favorite button
    const favoriteBtn = article.querySelector('.btn-favorite');
    favoriteBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      this.app.favoritePhoto(photo.id);
    });

    // Select button
    const selectBtn = article.querySelector('.btn-select');
    selectBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      this.toggleSelection(photo.id);
    });

    // Rating stars
    const stars = article.querySelectorAll('.star');
    stars.forEach((star, index) => {
      star.addEventListener('click', (e) => {
        e.stopPropagation();
        const newRating = index + 1;
        this.app.ratePhoto(photo.id, newRating);
      });
    });

    return article;
  }

  renderStars(rating, photoId) {
    let html = '';
    for (let i = 1; i <= 5; i++) {
      const filled = i <= rating;
      html += `
        <svg class="star ${filled ? 'filled' : ''}" width="16" height="16" viewBox="0 0 24 24" fill="${filled ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="2" data-rating="${i}">
          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
        </svg>
      `;
    }
    return html;
  }

  updatePhotoCard(photoId, photo) {
    const card = this.photoCards.get(photoId);
    if (!card) return;

    // Update favorite button
    const favoriteBtn = card.querySelector('.btn-favorite');
    const favoriteSvg = favoriteBtn.querySelector('svg');
    if (photo.isFavorite) {
      favoriteBtn.classList.add('active');
      favoriteSvg.setAttribute('fill', 'currentColor');
    } else {
      favoriteBtn.classList.remove('active');
      favoriteSvg.setAttribute('fill', 'none');
    }

    // Update rating
    const ratingContainer = card.querySelector('.rating');
    ratingContainer.innerHTML = this.renderStars(photo.rating || 0, photoId);

    // Re-attach star click handlers
    const stars = ratingContainer.querySelectorAll('.star');
    stars.forEach((star, index) => {
      star.addEventListener('click', (e) => {
        e.stopPropagation();
        this.app.ratePhoto(photoId, index + 1);
      });
    });
  }

  toggleSelection(photoId) {
    const card = this.photoCards.get(photoId);
    if (!card) return;

    if (this.app.state.selectedPhotos.has(photoId)) {
      this.app.state.selectedPhotos.delete(photoId);
      card.classList.remove('selected');
      card.querySelector('.selection-indicator').style.display = 'none';
    } else {
      this.app.state.selectedPhotos.add(photoId);
      card.classList.add('selected');
      card.querySelector('.selection-indicator').style.display = 'flex';
    }

    // Update bulk actions toolbar
    this.app.components.bulkActions.update(this.app.state.selectedPhotos.size);
  }

  formatDate(isoString) {
    const date = new Date(isoString);
    const formatter = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric'
    });
    return formatter.format(date);
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  /**
   * Append new photos to the grid (for pagination/infinite scroll)
   * @param {Array} photos - Array of new photos to append
   */
  appendPhotos(photos) {
    if (!photos || photos.length === 0) return;

    console.log(`[Grid] Appending ${photos.length} photos to DOM...`);

    // Add new photo cards to DOM
    photos.forEach(photo => {
      this.addPhotoCard(photo);
    });

    // CSS handles layout automatically - no recalculation needed
    console.log(`[Grid] Layout automatically updated by CSS`);
  }

  /**
   * Enable infinite scroll to load more photos when near bottom
   */
  enableInfiniteScroll() {
    // Disconnect existing observer if present
    if (this.infiniteScrollObserver) {
      this.infiniteScrollObserver.disconnect();
    }

    // Create sentinel element at the end of the grid
    let sentinel = this.container.querySelector('.infinite-scroll-sentinel');
    if (!sentinel) {
      sentinel = document.createElement('div');
      sentinel.className = 'infinite-scroll-sentinel';
      sentinel.style.height = '1px';
      sentinel.style.width = '100%';
      this.container.appendChild(sentinel);
    }

    // Observe when sentinel enters viewport
    this.infiniteScrollObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting && this.app.state.hasMorePages && !this.app.state.loadingMore) {
            console.log(`[Infinite Scroll] Sentinel visible - triggering load (400px margin)`);
            this.app.loadMorePhotos();
          }
        });
      },
      {
        rootMargin: '400px' // Trigger 400px before sentinel visible
      }
    );

    this.infiniteScrollObserver.observe(sentinel);
    console.log('[Infinite Scroll] Enabled with 400px preload margin');
  }

  /**
   * Disable infinite scroll
   */
  disableInfiniteScroll() {
    if (this.infiniteScrollObserver) {
      this.infiniteScrollObserver.disconnect();
      this.infiniteScrollObserver = null;
    }
  }
}
