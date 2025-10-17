/**
 * Lightbox Photo Viewer
 * Full-screen immersive photo viewing with EXIF, zoom, navigation
 */

export class Lightbox {
  constructor(app) {
    this.app = app;
    this.isOpen = false;
    this.currentPhotoId = null;
    this.currentPhoto = null;
    this.currentIndex = -1;
    this.zoom = 1;
    this.panX = 0;
    this.panY = 0;
    this.isDragging = false;
    this.dragStartX = 0;
    this.dragStartY = 0;
    this.showChrome = true;
    this.chromeTimeout = null;
    this.container = null;
    this.render();
  }

  render() {
    const container = document.createElement('div');
    container.className = 'lightbox';
    container.innerHTML = `
      <div class="lightbox-overlay"></div>

      <!-- Main image container -->
      <div class="lightbox-stage">
        <img class="lightbox-image" alt="Photo" />
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
    `;

    document.body.appendChild(container);
    this.container = container;
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

    // Close
    overlay.addEventListener('click', () => this.close());
    closeBtn.addEventListener('click', () => this.close());

    // Navigation
    prevBtn.addEventListener('click', () => this.previous());
    nextBtn.addEventListener('click', () => this.next());

    // Actions
    downloadBtn.addEventListener('click', () => this.download());
    favoriteBtn.addEventListener('click', () => this.toggleFavorite());

    // Zoom
    zoomInBtn.addEventListener('click', () => this.zoomIn());
    zoomOutBtn.addEventListener('click', () => this.zoomOut());
    zoomResetBtn.addEventListener('click', () => this.resetZoom());

    // Mouse wheel zoom
    image.addEventListener('wheel', (e) => {
      e.preventDefault();
      if (e.deltaY < 0) {
        this.zoomIn();
      } else {
        this.zoomOut();
      }
    }, { passive: false });

    // Pan with drag
    image.addEventListener('mousedown', (e) => {
      if (this.zoom > 1) {
        this.isDragging = true;
        this.dragStartX = e.clientX - this.panX;
        this.dragStartY = e.clientY - this.panY;
        image.style.cursor = 'grabbing';
      }
    });

    document.addEventListener('mousemove', (e) => {
      if (this.isDragging) {
        this.panX = e.clientX - this.dragStartX;
        this.panY = e.clientY - this.dragStartY;
        this.updateImageTransform();
      }
    });

    document.addEventListener('mouseup', () => {
      if (this.isDragging) {
        this.isDragging = false;
        image.style.cursor = this.zoom > 1 ? 'grab' : 'default';
      }
    });

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

    // Keyboard shortcuts
    this.keyboardHandler = (e) => {
      if (!this.isOpen) return;

      switch (e.key) {
        case 'Escape':
          this.close();
          break;
        case 'ArrowLeft':
        case 'k':
        case 'K':
          this.previous();
          break;
        case 'ArrowRight':
        case 'j':
        case 'J':
          this.next();
          break;
        case 'd':
        case 'D':
          this.download();
          break;
        case 'f':
        case 'F':
          this.toggleFavorite();
          break;
        case '+':
        case '=':
          this.zoomIn();
          break;
        case '-':
        case '_':
          this.zoomOut();
          break;
        case '0':
          this.resetZoom();
          break;
        case '1':
        case '2':
        case '3':
        case '4':
        case '5':
          this.rate(parseInt(e.key));
          break;
      }
    };

    document.addEventListener('keydown', this.keyboardHandler);
  }

  async open(photoId) {
    this.currentPhotoId = photoId;
    this.currentPhoto = this.app.photos.find(p => p.id === photoId);
    this.currentIndex = this.app.photos.findIndex(p => p.id === photoId);

    if (!this.currentPhoto) {
      console.error('Photo not found:', photoId);
      return;
    }

    this.isOpen = true;
    this.container.classList.add('show');
    this.resetZoom();

    // Load photo and metadata
    await this.loadPhoto();
    this.updateMetadata();
    this.updateNavigation();

    // Show chrome initially
    const chrome = this.container.querySelector('.lightbox-chrome');
    chrome.classList.add('visible');
    clearTimeout(this.chromeTimeout);
    this.chromeTimeout = setTimeout(() => {
      chrome.classList.remove('visible');
    }, 3000);
  }

  close() {
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
      await this.open(nextPhoto.id);
    }
  }

  async previous() {
    if (this.currentIndex > 0) {
      const prevPhoto = this.app.photos[this.currentIndex - 1];
      await this.open(prevPhoto.id);
    }
  }

  zoomIn() {
    this.zoom = Math.min(this.zoom + 0.25, 4);
    this.updateImageTransform();
    this.updateZoomUI();
  }

  zoomOut() {
    this.zoom = Math.max(this.zoom - 0.25, 1);
    if (this.zoom === 1) {
      this.panX = 0;
      this.panY = 0;
    }
    this.updateImageTransform();
    this.updateZoomUI();
  }

  resetZoom() {
    this.zoom = 1;
    this.panX = 0;
    this.panY = 0;
    this.updateImageTransform();
    this.updateZoomUI();
  }

  updateImageTransform() {
    const image = this.container.querySelector('.lightbox-image');
    image.style.transform = `scale(${this.zoom}) translate(${this.panX / this.zoom}px, ${this.panY / this.zoom}px)`;
    image.style.cursor = this.zoom > 1 ? 'grab' : 'default';
  }

  updateZoomUI() {
    const zoomLevel = this.container.querySelector('.zoom-level');
    zoomLevel.textContent = `${Math.round(this.zoom * 100)}%`;
  }

  async toggleFavorite() {
    try {
      const response = await this.app.api.post(`/api/photos/${this.currentPhotoId}/favorite`);

      // Update local state
      this.currentPhoto.isFavorite = response.isFavorite;

      // Update photo in app.photos array
      const photoInList = this.app.photos.find(p => p.id === this.currentPhotoId);
      if (photoInList) {
        photoInList.isFavorite = response.isFavorite;
      }

      // Update UI
      this.updateMetadata();

      this.app.components.toast.show(
        response.isFavorite ? 'Added to favorites' : 'Removed from favorites',
        { icon: response.isFavorite ? '❤️' : '🤍', duration: 1500 }
      );
    } catch (error) {
      console.error('Failed to toggle favorite:', error);
      this.app.components.toast.show('Failed to update favorite', { icon: '⚠️', duration: 2000 });
    }
  }

  async rate(rating) {
    try {
      const response = await this.app.api.post(`/api/photos/${this.currentPhotoId}/rate`, { rating });

      // Update local state
      this.currentPhoto.rating = response.rating;

      // Update photo in app.photos array
      const photoInList = this.app.photos.find(p => p.id === this.currentPhotoId);
      if (photoInList) {
        photoInList.rating = response.rating;
      }

      // Update UI
      this.updateRatingStars(response.rating);

      this.app.components.toast.show(
        `Rated ${rating} star${rating !== 1 ? 's' : ''}`,
        { icon: '⭐', duration: 1500 }
      );
    } catch (error) {
      console.error('Failed to rate photo:', error);
      this.app.components.toast.show('Failed to update rating', { icon: '⚠️', duration: 2000 });
    }
  }

  download() {
    // Open download endpoint in new window
    window.open(`/api/photos/${this.currentPhotoId}/download`, '_blank');
    this.app.components.toast.show('Download started', { icon: '⬇️', duration: 2000 });
  }

  destroy() {
    document.removeEventListener('keydown', this.keyboardHandler);
    clearTimeout(this.chromeTimeout);
  }
}
