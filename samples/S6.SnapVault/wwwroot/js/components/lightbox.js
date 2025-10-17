/**
 * Lightbox Photo Viewer
 * Full-screen immersive photo viewing with EXIF, zoom, navigation
 */

import { LightboxPanel } from './lightboxPanel.js';
import { LightboxZoom } from './lightboxZoom.js';
import { LightboxActions } from './lightboxActions.js';
import { LightboxKeyboard } from './lightboxKeyboard.js';
import { FocusManager } from './lightboxFocus.js';
import { AnnouncementManager } from './lightboxAnnouncer.js';

export class Lightbox {
  constructor(app) {
    this.app = app;
    this.isOpen = false;
    this.currentPhotoId = null;
    this.currentPhoto = null;
    this.currentIndex = -1;
    this.showChrome = true;
    this.chromeTimeout = null;
    this.container = null;
    this.photoElement = null;
    this.currentLayout = null;

    // Progressive loading state
    this.imageLoadState = 'idle'; // 'loading-gallery', 'gallery-loaded', 'loading-original', 'original-loaded'
    this.originalLoadTimer = null;
    this.originalLoadAbort = null;

    this.render();

    // Note: this.panel is initialized in render() method

    // Initialize zoom system (Phase 3)
    this.zoomSystem = new LightboxZoom(this);

    // Initialize actions system (Phase 4)
    this.actions = new LightboxActions(this, this.app);

    // Initialize keyboard shortcuts system (Phase 5)
    this.keyboard = new LightboxKeyboard(this);

    // Initialize accessibility managers (Phase 6)
    this.focusManager = new FocusManager(this);
    this.announcer = new AnnouncementManager();

    // Setup resize handler for responsive photo reflow
    this.setupResizeHandler();
  }

  render() {
    const container = document.createElement('div');
    container.className = 'lightbox';

    // ARIA: Dialog role (Phase 6)
    container.setAttribute('role', 'dialog');
    container.setAttribute('aria-modal', 'true');
    container.setAttribute('aria-labelledby', 'lightbox-title');
    container.setAttribute('aria-describedby', 'lightbox-description');

    container.innerHTML = `
      <!-- Hidden title and description for screen readers -->
      <h1 id="lightbox-title" class="sr-only">Photo Viewer</h1>
      <p id="lightbox-description" class="sr-only">
        Use arrow keys to navigate photos, I to toggle info panel, ESC to close.
      </p>

      <div class="lightbox-overlay"></div>

      <!-- Viewer area (shrinks when panel opens) -->
      <div class="lightbox-viewer">
        <!-- Main image container -->
        <div class="lightbox-stage" role="document">
        <img class="lightbox-image" alt="Current photo" />
      </div>

      <!-- Chrome (mouse-reveal UI) -->
      <div class="lightbox-chrome">
        <!-- Top bar -->
        <div class="lightbox-top-bar">
          <div class="lightbox-photo-info">
            <span class="photo-filename"></span>
            <span class="photo-dimensions"></span>
          </div>
          <div class="lightbox-actions">
            <button class="btn-icon btn-info" title="Photo Information (I)" aria-label="Toggle photo information panel" aria-expanded="false" aria-controls="info-panel">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <line x1="12" y1="16" x2="12" y2="12"></line>
                <line x1="12" y1="8" x2="12.01" y2="8"></line>
              </svg>
            </button>
            <button class="btn-icon btn-download" title="Download original (D)" aria-label="Download">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
            </button>
            <button class="btn-icon btn-favorite" title="Favorite (F)" aria-label="Toggle favorite">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"></path>
              </svg>
            </button>
            <button class="btn-icon btn-close" title="Close (Esc)" aria-label="Close">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>
        </div>

        <!-- Navigation arrows -->
        <button class="lightbox-nav lightbox-prev" title="Previous (←, K)" aria-label="Previous photo">
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="15 18 9 12 15 6"></polyline>
          </svg>
        </button>
        <button class="lightbox-nav lightbox-next" title="Next (→, J)" aria-label="Next photo">
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="9 18 15 12 9 6"></polyline>
          </svg>
        </button>
      </div>
      </div>
    `;

    document.body.appendChild(container);
    this.container = container;

    // Create and append unified panel
    this.panel = new LightboxPanel(this, this.app);
    this.container.appendChild(this.panel.getElement());

    // Append zoom badge (Phase 3)
    if (this.zoomSystem && this.zoomSystem.badge) {
      this.container.appendChild(this.zoomSystem.badge);
    }

    this.setupEventListeners();
  }

  setupEventListeners() {
    const overlay = this.container.querySelector('.lightbox-overlay');
    const image = this.container.querySelector('.lightbox-image');
    const closeBtn = this.container.querySelector('.btn-close');
    const prevBtn = this.container.querySelector('.lightbox-prev');
    const nextBtn = this.container.querySelector('.lightbox-next');
    const downloadBtn = this.container.querySelector('.btn-download');
    const favoriteBtn = this.container.querySelector('.btn-favorite');
    const infoBtn = this.container.querySelector('.btn-info');
    const chrome = this.container.querySelector('.lightbox-chrome');

    // Close
    overlay.addEventListener('click', () => this.close());
    closeBtn.addEventListener('click', () => this.close());

    // Info panel toggle
    infoBtn.addEventListener('click', () => {
      if (this.panel) this.panel.toggle();
    });

    // Navigation
    prevBtn.addEventListener('click', () => this.previous());
    nextBtn.addEventListener('click', () => this.next());

    // Actions
    downloadBtn.addEventListener('click', () => this.download());
    favoriteBtn.addEventListener('click', () => this.toggleFavorite());

    // Setup simplified zoom system event listeners (click-to-cycle)
    this.setupZoomListeners();

    // Mouse reveal chrome
    this.container.addEventListener('mousemove', () => {
      this.showChrome = true;
      chrome.classList.add('visible');

      clearTimeout(this.chromeTimeout);
      this.chromeTimeout = setTimeout(() => {
        this.showChrome = false;
        chrome.classList.remove('visible');
      }, 2000);
    });

    // NOTE: Keyboard shortcuts are now handled by LightboxKeyboard (Phase 5)
    // The old keyboard handler has been removed to prevent conflicts
  }

  setupZoomListeners() {
    const image = this.container.querySelector('.lightbox-image');
    if (!image) return;

    // Click-to-cycle: Fit → Fill → Original → Fit (simplified 3-mode system)
    image.addEventListener('click', (e) => {
      // Only cycle if user didn't drag
      if (!this.zoomSystem.panController.wasRecentDrag()) {
        this.zoomSystem.cycle();
      }
      // Reset the drag flag after checking
      this.zoomSystem.panController.resetDragFlag();
    });

    // Pan when in Fill or Original modes (works for both mouse and touch)
    image.addEventListener('pointerdown', (e) => {
      this.zoomSystem.panController.handlePointerDown(e);
    });

    document.addEventListener('pointermove', (e) => {
      this.zoomSystem.panController.handlePointerMove(e);
    });

    document.addEventListener('pointerup', () => {
      this.zoomSystem.panController.handlePointerUp();
    });
  }

  async open(photoId) {
    this.currentPhotoId = photoId;
    this.currentIndex = this.app.photos.findIndex(p => p.id === photoId);

    this.isOpen = true;
    this.container.classList.add('show');

    // Fetch fresh photo data from backend (includes latest AI description)
    await this.fetchPhotoData();

    if (!this.currentPhoto) {
      console.error('Photo not found:', photoId);
      this.close();
      return;
    }

    // Load photo and metadata
    await this.loadPhoto();
    this.updateMetadata();
    this.updateNavigation();

    // Update unified panel with photo data
    if (this.panel && this.currentPhoto) {
      this.panel.render(this.currentPhoto);
    }

    // Set current photo for actions system (Phase 4)
    if (this.actions && this.currentPhoto) {
      this.actions.setPhoto(this.currentPhoto);
    }

    // Open the panel by default
    if (this.panel) {
      this.panel.open();
    }

    // Get photo element reference and apply initial layout (panel open)
    this.photoElement = this.container.querySelector('.lightbox-image');
    this.applyPhotoLayout({ open: true });

    // Reset zoom to fit mode (Phase 3)
    if (this.zoomSystem) {
      this.zoomSystem.reset();
    }

    // Enable keyboard shortcuts (Phase 5)
    if (this.keyboard) {
      this.keyboard.enable();
      this.showFirstUseHint();
    }

    // Capture focus and announce photo (Phase 6)
    if (this.focusManager) {
      this.focusManager.captureFocus();
    }
    if (this.announcer && this.currentPhoto) {
      this.announcer.announcePhotoChange(
        this.currentIndex,
        this.app.photos.length,
        this.currentPhoto.originalFileName
      );
    }

    // Show chrome initially
    const chrome = this.container.querySelector('.lightbox-chrome');
    chrome.classList.add('visible');
    clearTimeout(this.chromeTimeout);
    this.chromeTimeout = setTimeout(() => {
      chrome.classList.remove('visible');
    }, 3000);
  }

  async fetchPhotoData() {
    try {
      // Fetch fresh photo data from API
      this.currentPhoto = await this.app.api.get(`/api/photos/${this.currentPhotoId}`);
    } catch (error) {
      console.error('Failed to fetch photo data:', error);
      this.app.components.toast.show('Failed to load photo details', { icon: '⚠️', duration: 3000 });
      // Fallback to cached data
      this.currentPhoto = this.app.photos.find(p => p.id === this.currentPhotoId);
    }
  }

  close() {
    // Close panel first if open
    if (this.panel && this.panel.isOpen) {
      this.panel.close();
    }

    // Disable keyboard shortcuts (Phase 5)
    if (this.keyboard) {
      this.keyboard.disable();
    }

    // Restore focus (Phase 6)
    if (this.focusManager) {
      this.focusManager.restoreFocus();
    }

    this.isOpen = false;
    this.container.classList.remove('show');
    this.currentPhotoId = null;
    this.currentPhoto = null;
    this.currentIndex = -1;
    clearTimeout(this.chromeTimeout);
  }

  async loadPhoto() {
    // Progressive loading: Gallery (fast) → Original (high quality)
    // Optimized for future web deployment with slow networks

    const image = this.container.querySelector('.lightbox-image');
    const galleryUrl = `/api/media/photos/${this.currentPhotoId}/gallery`;
    const originalUrl = `/api/media/photos/${this.currentPhotoId}/original`;

    // Cancel any pending original loads from previous photo
    if (this.originalLoadAbort) {
      this.originalLoadAbort.abort();
      this.originalLoadAbort = null;
    }
    clearTimeout(this.originalLoadTimer);

    // Reset state
    this.imageLoadState = 'loading-gallery';
    image.dataset.quality = 'gallery';

    // Show loading state
    image.style.opacity = '0.5';

    // PHASE 1: Load gallery version (fast, ~200KB)
    image.src = galleryUrl;
    image.alt = this.currentPhoto.originalFileName;

    try {
      await new Promise((resolve, reject) => {
        image.onload = resolve;
        image.onerror = reject;
      });

      image.style.opacity = '1';
      this.imageLoadState = 'gallery-loaded';

      // PHASE 2: Schedule original load after user engagement
      // Only load original if:
      // - User stays on photo >2s (engaged)
      // - User zooms (quality needed)
      this.scheduleOriginalLoad(image, originalUrl);

    } catch (error) {
      console.error('Failed to load gallery image:', error);
      image.style.opacity = '1';
      // Fallback: try original directly
      await this.loadOriginalImage(image, originalUrl);
    }
  }

  scheduleOriginalLoad(image, originalUrl) {
    // Check if original is already cached - if so, load immediately
    // Otherwise delay by 2 seconds to avoid wasting bandwidth for quick navigation
    this.checkIfCached(originalUrl).then(isCached => {
      if (isCached) {
        // Cache hit - load immediately with no delay
        console.log('Original cached - loading immediately');
        this.loadOriginalImage(image, originalUrl);
      } else {
        // Not cached - delay load to avoid wasting bandwidth on quick browsing
        this.originalLoadTimer = setTimeout(() => {
          this.loadOriginalImage(image, originalUrl);
        }, 2000);
      }
    });
  }

  async checkIfCached(url) {
    // Use a HEAD request or image preload to check if resource is cached
    // If it completes very quickly (<50ms), it's likely cached
    const startTime = performance.now();

    try {
      // Create a test image to check cache
      const testImage = new Image();

      const loadPromise = new Promise((resolve, reject) => {
        testImage.onload = () => resolve(true);
        testImage.onerror = () => reject(false);
      });

      testImage.src = url;

      // If image is cached, onload fires synchronously or very quickly
      await loadPromise;

      const loadTime = performance.now() - startTime;

      // If it loaded in less than 50ms, it's almost certainly from cache
      return loadTime < 50;

    } catch {
      return false;
    }
  }

  async loadOriginalImage(image, originalUrl) {
    if (this.imageLoadState === 'original-loaded') return; // Already loaded

    this.imageLoadState = 'loading-original';

    // Create AbortController for cancellation support
    this.originalLoadAbort = new AbortController();

    try {
      // Capture current visual state before swap
      const galleryNaturalWidth = image.naturalWidth;
      const galleryNaturalHeight = image.naturalHeight;
      const currentScale = this.zoomSystem ? this.zoomSystem.currentScale : 1.0;
      const currentPanOffset = this.zoomSystem ? { ...this.zoomSystem.panOffset } : { x: 0, y: 0 };
      const currentMode = this.zoomSystem ? this.zoomSystem.mode : 'fit';

      // Preload original in background
      const tempImage = new Image();
      tempImage.src = originalUrl;

      await new Promise((resolve, reject) => {
        tempImage.onload = resolve;
        tempImage.onerror = reject;

        // Support cancellation
        this.originalLoadAbort.signal.addEventListener('abort', () => {
          tempImage.src = ''; // Cancel load
          reject(new Error('Load cancelled'));
        });
      });

      // Calculate scale adjustment to maintain same displayed size
      // If gallery was 1200px scaled to 960px (scale 0.8),
      // and original is 4000px, we need scale 0.24 to still show 960px
      const dimensionRatio = galleryNaturalWidth / tempImage.naturalWidth;
      const adjustedScale = currentScale * dimensionRatio;

      // Disable transitions for seamless swap
      if (image) {
        image.style.transition = 'none';
      }

      // Seamless swap: update src and adjust scale simultaneously
      image.src = originalUrl;
      image.dataset.quality = 'original';
      this.imageLoadState = 'original-loaded';

      // Wait for browser to update naturalWidth/Height
      await new Promise(resolve => {
        if (image.complete && image.naturalWidth > 0) {
          resolve();
        } else {
          image.onload = resolve;
        }
      });

      // Recalculate zoom based on current mode with new dimensions
      if (this.zoomSystem) {
        // Preserve the mode and recalculate scale for new dimensions
        // In simplified 3-mode system:
        // - 'fit' always fits to screen
        // - 'fill' always fills viewport
        // - 'original' always shows 1:1
        this.zoomSystem.mode = currentMode;

        switch (currentMode) {
          case 'fit':
            this.zoomSystem.currentScale = this.zoomSystem.calculateFitScale();
            this.zoomSystem.panOffset = { x: 0, y: 0 };
            break;
          case 'fill':
            this.zoomSystem.currentScale = this.zoomSystem.calculateFillScale();
            this.zoomSystem.panOffset = { x: 0, y: 0 };
            break;
          case 'original':
            this.zoomSystem.currentScale = 1.0;
            this.zoomSystem.panOffset = { x: 0, y: 0 };
            break;
        }

        // Apply without transition (instant)
        const photo = this.lightbox.photoElement;
        if (photo) {
          photo.style.transition = 'none';
          photo.style.transform = `
            translate(${this.zoomSystem.panOffset.x}px, ${this.zoomSystem.panOffset.y}px)
            scale(${this.zoomSystem.currentScale})
          `;

          // Update zoom badge to reflect state
          this.zoomSystem.updateBadge();

          // Re-enable transitions after a frame
          requestAnimationFrame(() => {
            photo.style.transition = '';
          });
        }
      }

      // Show subtle quality indicator
      this.showQualityIndicator();

    } catch (error) {
      if (error.message !== 'Load cancelled') {
        console.warn('Failed to load original image:', error);
      }
      // Keep gallery version - it's good enough
      this.imageLoadState = 'gallery-loaded';
    }
  }

  showQualityIndicator() {
    // Subtle toast: "Full Resolution"
    const indicator = document.createElement('div');
    indicator.className = 'quality-indicator';
    indicator.innerHTML = '✨ Full Resolution';
    indicator.style.cssText = `
      position: fixed;
      bottom: 80px;
      left: 50%;
      transform: translateX(-50%);
      background: rgba(0, 0, 0, 0.75);
      color: white;
      padding: 8px 16px;
      border-radius: 20px;
      font-size: 13px;
      font-weight: 500;
      pointer-events: none;
      opacity: 0;
      transition: opacity 200ms ease;
      z-index: 10001;
    `;

    document.body.appendChild(indicator);

    // Fade in
    requestAnimationFrame(() => {
      indicator.style.opacity = '1';
    });

    // Auto-remove after 2 seconds
    setTimeout(() => {
      indicator.style.opacity = '0';
      setTimeout(() => indicator.remove(), 200);
    }, 2000);
  }

  // Trigger immediate original load when user zooms
  onUserZoom() {
    if (this.imageLoadState === 'gallery-loaded') {
      const image = this.container.querySelector('.lightbox-image');
      const originalUrl = `/api/media/photos/${this.currentPhotoId}/original`;

      // Cancel timer and load immediately
      clearTimeout(this.originalLoadTimer);
      this.loadOriginalImage(image, originalUrl);
    }
  }

  updateMetadata() {
    const photo = this.currentPhoto;

    // Filename and dimensions
    this.container.querySelector('.photo-filename').textContent = photo.originalFileName;
    this.container.querySelector('.photo-dimensions').textContent = `${photo.width} × ${photo.height}`;

    // Favorite button state
    const favoriteBtn = this.container.querySelector('.btn-favorite');
    if (photo.isFavorite) {
      favoriteBtn.classList.add('active');
      favoriteBtn.querySelector('svg').setAttribute('fill', 'currentColor');
    } else {
      favoriteBtn.classList.remove('active');
      favoriteBtn.querySelector('svg').setAttribute('fill', 'none');
    }
  }


  updateNavigation() {
    const prevBtn = this.container.querySelector('.lightbox-prev');
    const nextBtn = this.container.querySelector('.lightbox-next');

    prevBtn.style.display = this.currentIndex > 0 ? 'flex' : 'none';
    nextBtn.style.display = this.currentIndex < this.app.photos.length - 1 ? 'flex' : 'none';
  }

  async next() {
    if (this.currentIndex < this.app.photos.length - 1) {
      const nextPhoto = this.app.photos[this.currentIndex + 1];
      this.currentPhotoId = nextPhoto.id;
      this.currentIndex++;

      // Preserve current zoom mode
      const preservedMode = this.zoomSystem ? this.zoomSystem.mode : 'fit';

      // Fetch fresh data and update display
      await this.fetchPhotoData();
      await this.loadPhoto();
      this.updateMetadata();
      this.updateNavigation();

      // Restore zoom mode (Phase 3)
      if (this.zoomSystem) {
        this.zoomSystem.setMode(preservedMode);
      }

      // Update unified panel
      if (this.panel && this.currentPhoto) {
        this.panel.render(this.currentPhoto);
      }

      // Update actions with new photo (Phase 4)
      if (this.actions && this.currentPhoto) {
        this.actions.setPhoto(this.currentPhoto);
      }

      // Announce photo change (Phase 6)
      if (this.announcer && this.currentPhoto) {
        this.announcer.announcePhotoChange(
          this.currentIndex,
          this.app.photos.length,
          this.currentPhoto.originalFileName
        );
      }
    }
  }

  async previous() {
    if (this.currentIndex > 0) {
      const prevPhoto = this.app.photos[this.currentIndex - 1];
      this.currentPhotoId = prevPhoto.id;
      this.currentIndex--;

      // Preserve current zoom mode
      const preservedMode = this.zoomSystem ? this.zoomSystem.mode : 'fit';

      // Fetch fresh data and update display
      await this.fetchPhotoData();
      await this.loadPhoto();
      this.updateMetadata();
      this.updateNavigation();

      // Restore zoom mode (Phase 3)
      if (this.zoomSystem) {
        this.zoomSystem.setMode(preservedMode);
      }

      // Update unified panel
      if (this.panel && this.currentPhoto) {
        this.panel.render(this.currentPhoto);
      }

      // Update actions with new photo (Phase 4)
      if (this.actions && this.currentPhoto) {
        this.actions.setPhoto(this.currentPhoto);
      }

      // Announce photo change (Phase 6)
      if (this.announcer && this.currentPhoto) {
        this.announcer.announcePhotoChange(
          this.currentIndex,
          this.app.photos.length,
          this.currentPhoto.originalFileName
        );
      }
    }
  }

  // Old zoom methods removed - replaced by LightboxZoom system (Phase 3)

  // Old action methods - redirect to LightboxActions (Phase 4, backward compatibility)
  async toggleFavorite() {
    if (this.actions) {
      return this.actions.toggleFavorite();
    }
  }

  async rate(rating) {
    if (this.actions) {
      return this.actions.setRating(rating);
    }
  }

  download() {
    if (this.actions) {
      return this.actions.download();
    }
  }

  async deletePhoto() {
    if (this.actions) {
      return this.actions.deletePhoto();
    }
  }

  async regenerateAIDescription() {
    if (this.actions) {
      return this.actions.regenerateAI();
    }
  }

  // Deprecated: kept for backward compatibility
  toggleAIPanel() {
    if (this.panel) {
      this.panel.toggle();
    }
  }

  showFirstUseHint() {
    // Check if user has seen the hint before
    if (localStorage.getItem('lightbox-hint-seen')) return;

    const tooltip = document.createElement('div');
    tooltip.className = 'zoom-hint-tooltip';
    tooltip.innerHTML = '💡 Click photo to zoom • Press <kbd>?</kbd> for shortcuts';
    document.body.appendChild(tooltip);

    // Auto-dismiss after 5 seconds
    setTimeout(() => {
      tooltip.remove();
    }, 5000);

    // Mark hint as seen
    localStorage.setItem('lightbox-hint-seen', 'true');
  }

  formatMarkdown(text) {
    // Simple markdown-to-HTML converter for the AI description
    let html = text;

    // Headers (## Header)
    html = html.replace(/^## (.+)$/gm, '<h4>$1</h4>');
    html = html.replace(/^# (.+)$/gm, '<h3>$1</h3>');

    // Bold (**text**)
    html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

    // Italic (*text*)
    html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');

    // Line breaks (preserve double newlines as paragraphs)
    const paragraphs = html.split('\n\n').filter(p => p.trim());
    html = paragraphs.map(p => {
      // Don't wrap headers in paragraphs
      if (p.trim().startsWith('<h')) {
        return p;
      }
      // Replace single newlines within paragraphs with <br>
      const withBreaks = p.replace(/\n/g, '<br>');
      return `<p>${withBreaks}</p>`;
    }).join('');

    return html;
  }

  calculatePhotoLayout(photo, viewport, panelState) {
    const aspectRatio = photo.width / photo.height;

    // Special case: Panorama (ultra-wide)
    if (aspectRatio < 0.5) {
      return {
        mode: 'bottom-sheet',
        reason: 'panorama',
        scale: 1.0,
        offsetX: 0
      };
    }

    // Special case: Portrait (ultra-tall)
    const isPortrait = aspectRatio > 1.5;
    const heightScale = isPortrait ? 0.85 : 0.75;

    // Desktop: Shift + scale
    if (viewport.width >= 1200) {
      const panelWidth = panelState.open ? 380 : 0;
      const availableWidth = viewport.width - panelWidth - 80; // 80px margins
      const availableHeight = viewport.height * heightScale;

      const scale = Math.min(
        availableWidth / photo.width,
        availableHeight / photo.height,
        1.0 // Never scale up
      );

      return {
        mode: 'shift-scale',
        scale,
        offsetX: panelState.open ? -190 : 0, // Half panel width
        width: photo.width * scale,
        height: photo.height * scale
      };
    }

    // Tablet: Overlay (no reflow)
    if (viewport.width >= 768) {
      return {
        mode: 'overlay',
        scale: 1.0,
        offsetX: 0
      };
    }

    // Mobile: Bottom sheet
    return {
      mode: 'bottom-sheet',
      scale: 1.0,
      offsetX: 0
    };
  }

  applyPhotoLayout(panelState) {
    if (!this.currentPhoto) return;

    const viewport = {
      width: window.innerWidth,
      height: window.innerHeight
    };

    const photo = {
      width: this.currentPhoto.width,
      height: this.currentPhoto.height
    };

    const layout = this.calculatePhotoLayout(photo, viewport, panelState);
    this.currentLayout = layout;

    const viewer = this.container.querySelector('.lightbox-viewer');
    const overlay = this.container.querySelector('.lightbox-overlay');

    if (layout.mode === 'shift-scale') {
      // Desktop: Shrink viewer width to make room for panel
      viewer.style.transition = 'width 300ms cubic-bezier(0.4, 0, 0.2, 1)';
      viewer.style.width = panelState.open ? 'calc(100% - 380px)' : '100%';

      // Remove panel-open class from overlay (desktop doesn't use backdrop)
      overlay.classList.remove('panel-open');

      // Recalculate zoom after viewer size changes
      if (this.zoomSystem && this.zoomSystem.mode === 'fit') {
        // Small delay to let the transition start, then recalculate
        setTimeout(() => {
          this.zoomSystem.reset();
        }, 10);
      }
    } else if (layout.mode === 'overlay') {
      // Tablet: No viewer reflow, just backdrop
      viewer.style.width = '100%';
      overlay.classList.toggle('panel-open', panelState.open);
    } else if (layout.mode === 'bottom-sheet') {
      // Mobile: No viewer changes
      viewer.style.width = '100%';
      overlay.classList.remove('panel-open');
    }
  }

  setupResizeHandler() {
    let resizeTimeout;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimeout);
      resizeTimeout = setTimeout(() => {
        if (this.isOpen && this.panel && this.panel.isOpen) {
          this.applyPhotoLayout({ open: true });
        }
      }, 150); // Debounce
    });
  }

  destroy() {
    // Cleanup
    clearTimeout(this.chromeTimeout);
    if (this.announcer) {
      this.announcer.destroy();
    }
  }
}
