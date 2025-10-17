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

        <!-- Bottom bar with EXIF and rating -->
        <div class="lightbox-bottom-bar">
          <div class="lightbox-exif">
            <div class="exif-section exif-camera">
              <svg class="exif-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path>
                <circle cx="12" cy="13" r="4"></circle>
              </svg>
              <span class="exif-camera-text"></span>
            </div>
            <div class="exif-section exif-settings">
              <svg class="exif-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="3"></circle>
                <path d="M12 1v6m0 6v6M1 12h6m6 0h6"></path>
              </svg>
              <span class="exif-settings-text"></span>
            </div>
            <div class="exif-section exif-date">
              <svg class="exif-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>
                <line x1="16" y1="2" x2="16" y2="6"></line>
                <line x1="8" y1="2" x2="8" y2="6"></line>
                <line x1="3" y1="10" x2="21" y2="10"></line>
              </svg>
              <span class="exif-date-text"></span>
            </div>
            <div class="exif-section exif-tags">
              <svg class="exif-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"></path>
                <line x1="7" y1="7" x2="7.01" y2="7"></line>
              </svg>
              <span class="exif-tags-text"></span>
            </div>
            <button class="exif-section exif-ai-toggle btn-ai-description"
                    title="Toggle AI description (I)"
                    aria-label="Toggle photo information panel"
                    aria-expanded="false"
                    aria-controls="info-panel">
              <svg class="exif-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"></path>
                <line x1="12" y1="17" x2="12.01" y2="17"></line>
              </svg>
              <span class="exif-ai-toggle-text">AI Description</span>
            </button>
          </div>
          <div class="lightbox-rating">
            <div class="rating-stars">
              ${[1, 2, 3, 4, 5].map(star => `
                <button class="star-btn" data-rating="${star}" aria-label="Rate ${star} stars">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
                  </svg>
                </button>
              `).join('')}
            </div>
          </div>
        </div>

        <!-- AI Description Panel -->
        <div class="lightbox-ai-panel">
          <div class="ai-panel-header">
            <h3>AI-Generated Description</h3>
            <div class="ai-panel-actions">
              <button class="btn-icon btn-regenerate-ai" title="Regenerate description" aria-label="Regenerate AI description">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="23 4 23 10 17 10"></polyline>
                  <polyline points="1 20 1 14 7 14"></polyline>
                  <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
                </svg>
              </button>
              <button class="btn-icon btn-close-ai" title="Close (I)" aria-label="Close AI description">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"></line>
                  <line x1="6" y1="6" x2="18" y2="18"></line>
                </svg>
              </button>
            </div>
          </div>
          <div class="ai-panel-content"></div>
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

        <!-- Zoom controls -->
        <div class="lightbox-zoom-controls">
          <button class="btn-icon btn-zoom-out" title="Zoom out (-)" aria-label="Zoom out">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"></circle>
              <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              <line x1="8" y1="11" x2="14" y2="11"></line>
            </svg>
          </button>
          <span class="zoom-level">100%</span>
          <button class="btn-icon btn-zoom-in" title="Zoom in (+)" aria-label="Zoom in">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"></circle>
              <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              <line x1="11" y1="8" x2="11" y2="14"></line>
              <line x1="8" y1="11" x2="14" y2="11"></line>
            </svg>
          </button>
          <button class="btn-icon btn-zoom-reset" title="Reset zoom (0)" aria-label="Reset zoom">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"></circle>
              <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
            </svg>
          </button>
        </div>
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
    const zoomInBtn = this.container.querySelector('.btn-zoom-in');
    const zoomOutBtn = this.container.querySelector('.btn-zoom-out');
    const zoomResetBtn = this.container.querySelector('.btn-zoom-reset');
    const starBtns = this.container.querySelectorAll('.star-btn');
    const chrome = this.container.querySelector('.lightbox-chrome');
    const aiToggleBtn = this.container.querySelector('.btn-ai-description');
    const aiCloseBtn = this.container.querySelector('.btn-close-ai');
    const aiRegenerateBtn = this.container.querySelector('.btn-regenerate-ai');

    // Close
    overlay.addEventListener('click', () => this.close());
    closeBtn.addEventListener('click', () => this.close());

    // AI Description toggle - now toggles unified panel
    aiToggleBtn.addEventListener('click', () => {
      if (this.panel) this.panel.toggle();
    });
    aiCloseBtn.addEventListener('click', () => {
      if (this.panel) this.panel.toggle();
    });
    aiRegenerateBtn.addEventListener('click', () => this.regenerateAIDescription());

    // Navigation
    prevBtn.addEventListener('click', () => this.previous());
    nextBtn.addEventListener('click', () => this.next());

    // Actions
    downloadBtn.addEventListener('click', () => this.download());
    favoriteBtn.addEventListener('click', () => this.toggleFavorite());

    // Zoom (deprecated - kept for backward compatibility, redirects to new zoom system)
    zoomInBtn.addEventListener('click', () => {
      if (this.zoomSystem) {
        const newScale = Math.min(this.zoomSystem.currentScale + 0.25, this.zoomSystem.maxScale);
        this.zoomSystem.currentScale = newScale;
        this.zoomSystem.mode = 'custom';
        this.zoomSystem.apply();
        this.zoomSystem.updateBadge();
      }
    });
    zoomOutBtn.addEventListener('click', () => {
      if (this.zoomSystem) {
        const newScale = Math.max(this.zoomSystem.currentScale - 0.25, this.zoomSystem.minScale);
        this.zoomSystem.currentScale = newScale;
        if (newScale <= this.zoomSystem.calculateFitScale()) {
          this.zoomSystem.mode = 'fit';
          this.zoomSystem.panOffset = { x: 0, y: 0 };
        } else {
          this.zoomSystem.mode = 'custom';
        }
        this.zoomSystem.apply();
        this.zoomSystem.updateBadge();
      }
    });
    zoomResetBtn.addEventListener('click', () => {
      if (this.zoomSystem) {
        this.zoomSystem.reset();
      }
    });

    // Setup new zoom system event listeners (Phase 3)
    this.setupZoomListeners();

    // Rating
    starBtns.forEach(btn => {
      btn.addEventListener('click', async () => {
        const rating = parseInt(btn.dataset.rating);
        await this.rate(rating);
      });
    });

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

    // Click-to-cycle: Fit → Fill → 100% → Fit
    image.addEventListener('click', (e) => {
      // Only cycle if not panning
      if (!this.zoomSystem.panController.isDragging) {
        this.zoomSystem.cycle();
      }
    });

    // Scroll-wheel zoom (desktop)
    image.addEventListener('wheel', (e) => {
      this.zoomSystem.handleWheelZoom(e);
    }, { passive: false });

    // Pinch zoom (mobile/touchpad)
    image.addEventListener('touchstart', (e) => {
      if (e.touches.length === 2) {
        e.preventDefault();
        this.zoomSystem.handlePinchZoom(e);
      }
    }, { passive: false });

    image.addEventListener('touchmove', (e) => {
      if (e.touches.length === 2) {
        e.preventDefault();
        this.zoomSystem.handlePinchZoom(e);
      }
    }, { passive: false });

    image.addEventListener('touchend', () => {
      this.zoomSystem.pinchStartDistance = null;
    });

    // Pan when zoomed (works for both mouse and touch)
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

    // Get photo element reference and apply initial layout (panel closed)
    this.photoElement = this.container.querySelector('.lightbox-image');
    this.applyPhotoLayout({ open: false });

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
    const image = this.container.querySelector('.lightbox-image');
    const galleryUrl = `/api/media/photos/${this.currentPhotoId}/gallery`;

    // Show loading state
    image.style.opacity = '0.5';

    // Load gallery resolution image
    image.src = galleryUrl;
    image.alt = this.currentPhoto.originalFileName;

    await new Promise((resolve, reject) => {
      image.onload = resolve;
      image.onerror = reject;
    });

    image.style.opacity = '1';
  }

  updateMetadata() {
    const photo = this.currentPhoto;

    // Filename and dimensions
    this.container.querySelector('.photo-filename').textContent = photo.originalFileName;
    this.container.querySelector('.photo-dimensions').textContent = `${photo.width} × ${photo.height}`;

    // Camera info
    const cameraText = [];
    if (photo.cameraModel) cameraText.push(photo.cameraModel);
    if (photo.lensModel) cameraText.push(photo.lensModel);
    this.container.querySelector('.exif-camera-text').textContent = cameraText.join(' · ') || 'No camera data';

    // Settings
    const settingsText = [];
    if (photo.focalLength) settingsText.push(photo.focalLength);
    if (photo.aperture) settingsText.push(photo.aperture);
    if (photo.shutterSpeed) settingsText.push(photo.shutterSpeed);
    if (photo.iso) settingsText.push(`ISO ${photo.iso}`);
    this.container.querySelector('.exif-settings-text').textContent = settingsText.join(' · ') || 'No settings data';

    // Date
    const dateText = photo.capturedAt
      ? new Date(photo.capturedAt).toLocaleString('en-US', {
          year: 'numeric', month: 'short', day: 'numeric',
          hour: '2-digit', minute: '2-digit'
        })
      : new Date(photo.uploadedAt).toLocaleString('en-US', {
          year: 'numeric', month: 'short', day: 'numeric'
        });
    this.container.querySelector('.exif-date-text').textContent = dateText;

    // Tags
    const tags = [...photo.autoTags, ...photo.detectedObjects].slice(0, 5);
    this.container.querySelector('.exif-tags-text').textContent = tags.length > 0
      ? tags.join(', ')
      : 'No tags';

    // AI Description
    const aiToggleBtn = this.container.querySelector('.btn-ai-description');
    const aiPanelContent = this.container.querySelector('.ai-panel-content');

    if (photo.detailedDescription && photo.detailedDescription.trim()) {
      aiToggleBtn.style.display = 'flex';
      // Convert markdown-like formatting to HTML
      const formattedDescription = this.formatMarkdown(photo.detailedDescription);
      aiPanelContent.innerHTML = formattedDescription;
    } else {
      aiToggleBtn.style.display = 'none';
      aiPanelContent.innerHTML = '<p class="no-description">No AI description available yet. The description is being generated in the background.</p>';
    }

    // Favorite button state
    const favoriteBtn = this.container.querySelector('.btn-favorite');
    if (photo.isFavorite) {
      favoriteBtn.classList.add('active');
      favoriteBtn.querySelector('svg').setAttribute('fill', 'currentColor');
    } else {
      favoriteBtn.classList.remove('active');
      favoriteBtn.querySelector('svg').setAttribute('fill', 'none');
    }

    // Rating stars
    this.updateRatingStars(photo.rating);
  }

  updateRatingStars(rating) {
    const starBtns = this.container.querySelectorAll('.star-btn');
    starBtns.forEach((btn, index) => {
      const starRating = index + 1;
      const svg = btn.querySelector('svg');
      if (starRating <= rating) {
        btn.classList.add('active');
        svg.setAttribute('fill', 'currentColor');
      } else {
        btn.classList.remove('active');
        svg.setAttribute('fill', 'none');
      }
    });
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

      // Fetch fresh data and update display
      await this.fetchPhotoData();
      await this.loadPhoto();
      this.updateMetadata();
      this.updateNavigation();

      // Reset zoom to fit mode (Phase 3)
      if (this.zoomSystem) {
        this.zoomSystem.reset();
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

      // Fetch fresh data and update display
      await this.fetchPhotoData();
      await this.loadPhoto();
      this.updateMetadata();
      this.updateNavigation();

      // Reset zoom to fit mode (Phase 3)
      if (this.zoomSystem) {
        this.zoomSystem.reset();
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
