/**
 * Photo Grid Component with Virtual Scrolling
 * Masonry layout with hover interactions
 */

import { VIEW_PRESETS, selectOptimalImageTier, getResponsiveColumns } from '../viewPresets.js';
import { escapeHtml } from '../utils/html.js';

export class PhotoGrid {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.photo-grid');
    this.scrollContainer = document.querySelector('.main-content');
    this.photoCards = new Map();
    this.observer = null;
    this.scrollHandler = null;
    this.viewportWidth = window.innerWidth;
    this.devicePixelRatio = window.devicePixelRatio || 1;
    this.setupIntersectionObserver();
    this.setupViewportDetection();
    this.detectMasonrySupport();
  }

  detectMasonrySupport() {
    // CSS Masonry Browser Landscape (as of 2025-01):
    //
    // TWO COMPETING SYNTAXES EXIST:
    //
    // 1. Firefox/Safari Syntax (SHIPPED):
    //    - grid-template-rows: masonry
    //    - Firefox 87+ (stable), Safari Technology Preview
    //    - W3C CSS Grid Level 3 Draft: https://www.w3.org/TR/css-grid-3/
    //
    // 2. Chrome Syntax (EXPERIMENTAL FLAG):
    //    - display: masonry
    //    - Chrome behind #enable-experimental-web-platform-features flag
    //    - Different property model, uses grid-column/grid-row
    //    - More details: https://www.w3.org/TR/css-grid-3/#masonry-model
    //
    // CURRENT IMPLEMENTATION:
    // - Detects BOTH syntaxes (Firefox/Safari AND Chrome)
    // - Applies appropriate CSS class for detected syntax
    // - Falls back to CSS columns if neither is supported
    //
    // FALLBACK STRATEGY:
    // - CSS columns (column-count) provides graceful degradation
    // - All browsers get working masonry layout, just different ordering

    // Check for Firefox/Safari syntax first (stable implementation)
    const supportsGridMasonry = CSS.supports('grid-template-rows', 'masonry');

    // Check for Chrome syntax (experimental flag)
    const supportsDisplayMasonry = CSS.supports('display', 'masonry');

    // Determine which masonry mode to use
    if (supportsGridMasonry) {
      this.masonryMode = 'grid'; // Firefox/Safari
      this.supportsMasonry = true;
      console.log('[Grid] CSS Masonry support: Yes (Firefox/Safari syntax: grid-template-rows)');
    } else if (supportsDisplayMasonry) {
      this.masonryMode = 'display'; // Chrome
      this.supportsMasonry = true;
      console.log('[Grid] CSS Masonry support: Yes (Chrome syntax: display: masonry)');
    } else {
      this.masonryMode = null;
      this.supportsMasonry = false;
      console.log('[Grid] CSS Masonry support: No (using CSS columns fallback)');
    }
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

  /**
   * Update view preset without re-rendering (instant, no image reload)
   * Only updates the CSS data-preset attribute to trigger column changes
   */
  updatePreset() {
    if (!this.container) return;

    const currentPreset = this.app.state.viewPreset || 'comfortable';
    this.container.dataset.preset = currentPreset;
    console.log(`[Grid] Updated preset to: ${currentPreset} (no re-render)`);
  }

  render() {
    if (!this.container) return;

    const photos = this.app.state.photos;

    console.log(`[Grid] render() called - ${photos.length} photos in state`);

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
    console.log(`[Grid] Clearing ${existingCards.length} existing cards`);
    existingCards.forEach(card => card.remove());

    // Clear the photoCards map
    this.photoCards.clear();

    // Apply CSS class based on masonry support and mode
    this.container.classList.remove('masonry-grid', 'masonry-display', 'masonry-fallback');
    if (this.masonryMode === 'grid') {
      this.container.classList.add('masonry-grid');
    } else if (this.masonryMode === 'display') {
      this.container.classList.add('masonry-display');
    } else {
      this.container.classList.add('masonry-fallback');
    }

    // Apply view preset to grid container
    const currentPreset = this.app.state.viewPreset || 'comfortable';
    this.container.dataset.preset = currentPreset;
    console.log(`[Grid] Applied preset: ${currentPreset}`);

    // Render all photos in current state
    photos.forEach(photo => {
      this.addPhotoCard(photo);
    });

    console.log(`[Grid] render() complete - ${this.photoCards.size} cards in DOM`);
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
      console.log(`[Smart Resolution] ${tier} tier (${presetId} preset, ${columns} cols, ${tileWidth}px/tile × ${this.devicePixelRatio}x = ${effectivePixels}px effective)`);
      this._tierLogged = true;
    }

    // Map tier to appropriate endpoint
    switch (tier) {
      case 'gallery':
        // Gallery tier (1200px) - use photo ID (endpoint looks up gallery media)
        if (photo.galleryMediaId) {
          return `/api/media/photos/${photo.id}/gallery`;
        }
        // Fallback to masonry if gallery not available
        return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;

      case 'retina':
        // Retina tier (600px) - use media ID directly
        if (photo.retinaThumbnailMediaId) {
          return `/api/media/retina-thumbnails/${photo.retinaThumbnailMediaId}`;
        }
        return `/api/media/masonry-thumbnails/${photo.masonryThumbnailMediaId || photo.id}`;

      case 'masonry':
      default:
        // Masonry tier (300px) - use media ID directly
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
    article.draggable = false; // Card is NOT draggable (allows text selection)

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
      <img class="photo-image draggable" data-src="${thumbnailUrl}" alt="${escapeHtml(photo.originalFileName)}" loading="lazy" />
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

    // Make image draggable via JavaScript (controlled by CSS class)
    img.draggable = true;

    img.addEventListener('load', () => {
      article.querySelector('.photo-skeleton')?.remove();
    });

    // Dragstart on image - simulate two-step flow in one gesture
    img.addEventListener('dragstart', (e) => {
      const gridContainer = document.querySelector('.photo-grid');
      const isBrushSelecting = gridContainer?.classList.contains('brush-selecting');
      const brushSelection = this.app.components.photoSelection.selectedPhotoIds || [];

      console.log('[Grid] Dragstart event fired:', {
        photoId: photo.id,
        imageDraggable: img.draggable,
        imageClasses: img.className,
        gridHasBrushClass: isBrushSelecting,
        existingBrushSelection: brushSelection.length
      });

      if (brushSelection.length === 0) {
        // STEP 1: Auto-range-select this image (simulate selection stage ending)
        this.app.components.photoSelection.selectedPhotoIds = [photo.id];
        this.app.components.photoSelection.updateVisualFeedback(
          [photo.id],
          Array.from(document.querySelectorAll('.photo-card'))
        );
        this.app.components.photoSelection.setSelectedPhotoIds([photo.id]);
        console.log('[Grid] ✓ Auto-selected single image:', photo.id);
      } else {
        console.log('[Grid] ✓ Using existing brush selection:', brushSelection.length, 'photos');
      }

      // STEP 2: Start drag action (happens automatically, just set drag data)
      e.dataTransfer.effectAllowed = 'copy';
      e.dataTransfer.setData('text/plain', photo.id);
      console.log('[Grid] ✓ Drag data set, drag action initiated');
    });

    // Dragend for debugging
    img.addEventListener('dragend', (e) => {
      console.log('[Grid] Dragend event fired for photo:', photo.id);
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
   * Uses scroll position detection instead of sentinel (masonry-compatible)
   */
  enableInfiniteScroll() {
    if (!this.scrollContainer) {
      console.error('[Infinite Scroll] Could not find .main-content scroll container');
      return;
    }

    // Remove any existing scroll listener
    if (this.scrollHandler) {
      this.scrollContainer.removeEventListener('scroll', this.scrollHandler);
    }

    // Remove old sentinel if it exists (from previous implementation)
    const oldSentinel = this.container.querySelector('.infinite-scroll-sentinel');
    if (oldSentinel) {
      oldSentinel.remove();
    }

    // Create scroll handler with throttling - use arrow function to preserve 'this'
    const checkScroll = () => {
      const hasMorePages = this.app.state.hasMorePages;
      const loadingMore = this.app.state.loadingMore;

      if (!hasMorePages || loadingMore) return;

      // Use scrollContainer measurements instead of window
      const scrollTop = this.scrollContainer.scrollTop;
      const scrollHeight = this.scrollContainer.scrollHeight;
      const clientHeight = this.scrollContainer.clientHeight;

      // Calculate distance from bottom
      const distanceFromBottom = scrollHeight - (scrollTop + clientHeight);

      // Trigger when within 400px of bottom
      if (distanceFromBottom < 400) {
        this.app.loadMorePhotos();
      }
    };

    this.scrollHandler = this.throttle(checkScroll, 200); // Throttle to every 200ms

    this.scrollContainer.addEventListener('scroll', this.scrollHandler);
    console.log('[Infinite Scroll] Enabled with scroll-based detection on .main-content (400px preload)');
  }

  /**
   * Throttle function to limit execution rate
   */
  throttle(func, wait) {
    let timeout = null;
    let previous = 0;

    return (...args) => {
      const now = Date.now();
      const remaining = wait - (now - previous);

      if (remaining <= 0 || remaining > wait) {
        if (timeout) {
          clearTimeout(timeout);
          timeout = null;
        }
        previous = now;
        func(...args);
      } else if (!timeout) {
        timeout = setTimeout(() => {
          previous = Date.now();
          timeout = null;
          func(...args);
        }, remaining);
      }
    };
  }

  /**
   * Disable infinite scroll
   */
  disableInfiniteScroll() {
    if (this.scrollHandler && this.scrollContainer) {
      this.scrollContainer.removeEventListener('scroll', this.scrollHandler);
      this.scrollHandler = null;
    }
  }
}
