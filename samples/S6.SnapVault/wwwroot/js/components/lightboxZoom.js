/**
 * Lightbox Zoom System (Simplified)
 * Three fixed modes: Fit → Fill → Original
 * Click to cycle, drag to pan when zoomed
 */

export class LightboxZoom {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.mode = 'fit'; // 'fit' | 'fill' | 'original'
    this.currentScale = 1.0;
    this.panOffset = { x: 0, y: 0 };
    this.panController = new PanController(this);
    this.badge = this.createBadge();
  }

  createBadge() {
    const badge = document.createElement('div');
    badge.className = 'zoom-badge';
    badge.textContent = 'Fit';
    return badge;
  }

  reset() {
    // Reset to fit mode when opening new photo
    this.mode = 'fit';
    this.currentScale = this.calculateFitScale();
    this.panOffset = { x: 0, y: 0 };
    this.apply();
    this.updateBadge();
  }

  cycle() {
    // Click-to-cycle: Fit → Fill → Original → Fit
    if (this.mode === 'fit') {
      this.setMode('fill');
    } else if (this.mode === 'fill') {
      this.setMode('original');
    } else {
      this.setMode('fit');
    }
  }

  setMode(mode) {
    this.mode = mode;

    switch (mode) {
      case 'fit':
        // Fit: Show entire photo, letterbox/pillarbox if needed
        this.currentScale = this.calculateFitScale();
        this.panOffset = { x: 0, y: 0 };
        break;
      case 'fill':
        // Fill: Fill viewport, crop edges if needed (like object-fit: cover)
        this.currentScale = this.calculateFillScale();
        this.panOffset = { x: 0, y: 0 };
        this.triggerOriginalLoad();
        break;
      case 'original':
        // Original: 1:1 pixel ratio (100%)
        this.currentScale = 1.0;
        this.panOffset = { x: 0, y: 0 };
        this.triggerOriginalLoad();
        break;
    }

    this.apply();
    this.updateBadge();
  }

  triggerOriginalLoad() {
    // Notify lightbox to load original immediately when user zooms
    if (this.lightbox && typeof this.lightbox.onUserZoom === 'function') {
      this.lightbox.onUserZoom();
    }
  }

  calculateFitScale() {
    const photo = this.lightbox.photoElement;
    if (!photo || !photo.naturalWidth) return 1.0;

    const stage = this.lightbox.container.querySelector('.lightbox-stage');
    if (!stage) return 1.0;

    const containerWidth = stage.clientWidth - 80; // 40px padding each side
    const containerHeight = stage.clientHeight - 80;

    const photoWidth = photo.naturalWidth;
    const photoHeight = photo.naturalHeight;

    const photoRatio = photoWidth / photoHeight;
    const containerRatio = containerWidth / containerHeight;

    // Smart fit: Handle similar ratios
    if (Math.abs(photoRatio - containerRatio) < 0.1) {
      // Similar ratios: Fit-contain
      return Math.min(
        containerWidth / photoWidth,
        containerHeight / photoHeight
      );
    }

    // Ultra-wide: Fit width, allow vertical scroll
    if (photoRatio > 2.0) {
      return containerWidth / photoWidth;
    }

    // Ultra-tall: Fit height, allow horizontal scroll
    if (photoRatio < 0.5) {
      return containerHeight / photoHeight;
    }

    // Default: Fit-contain
    return Math.min(
      containerWidth / photoWidth,
      containerHeight / photoHeight
    );
  }

  calculateFillScale() {
    const photo = this.lightbox.photoElement;
    if (!photo || !photo.naturalWidth) return 1.0;

    const stage = this.lightbox.container.querySelector('.lightbox-stage');
    if (!stage) return 1.0;

    const containerWidth = stage.clientWidth;
    const containerHeight = stage.clientHeight;

    const photoWidth = photo.naturalWidth;
    const photoHeight = photo.naturalHeight;

    // Fill: Maximize size, may crop edges
    return Math.max(
      containerWidth / photoWidth,
      containerHeight / photoHeight
    );
  }

  apply() {
    const photo = this.lightbox.photoElement;
    if (!photo) return;

    photo.style.transition = 'transform 300ms cubic-bezier(0.4, 0, 0.2, 1)';
    photo.style.transform = `
      translate(${this.panOffset.x}px, ${this.panOffset.y}px)
      scale(${this.currentScale})
    `;

    // Update cursor based on mode
    const isPannable = this.mode === 'fill' || this.mode === 'original';
    if (isPannable) {
      photo.style.cursor = 'grab';
      photo.classList.add('zoomed');
    } else {
      photo.style.cursor = 'zoom-in';
      photo.classList.remove('zoomed');
    }

    // Enable pan for fill and original modes
    this.panController.setEnabled(isPannable);
  }

  updateBadge() {
    let text = '';

    switch (this.mode) {
      case 'fit':
        text = 'Fit';
        break;
      case 'fill':
        text = 'Fill';
        break;
      case 'original':
        text = '100%';
        break;
    }

    this.badge.textContent = text;
    this.badge.classList.add('visible');

    // Auto-hide in Fit mode after 2s
    if (this.mode === 'fit') {
      setTimeout(() => {
        this.badge.classList.remove('visible');
      }, 2000);
    }
  }
}

// Pan Controller Helper
class PanController {
  constructor(zoom) {
    this.zoom = zoom;
    this.isDragging = false;
    this.didDrag = false; // Track if user actually moved during drag
    this.startPos = { x: 0, y: 0 };
    this.pointerDownPos = { x: 0, y: 0 }; // Track initial pointer position
    this.enabled = false;
  }

  setEnabled(enabled) {
    this.enabled = enabled;
  }

  handlePointerDown(event) {
    if (!this.enabled) return;

    this.isDragging = true;
    this.didDrag = false; // Reset drag flag
    this.pointerDownPos = { x: event.clientX, y: event.clientY };
    this.startPos = {
      x: event.clientX - this.zoom.panOffset.x,
      y: event.clientY - this.zoom.panOffset.y
    };

    this.zoom.lightbox.photoElement.style.cursor = 'grabbing';
    this.zoom.lightbox.photoElement.style.transition = 'none';
  }

  handlePointerMove(event) {
    if (!this.isDragging) return;

    // Check if user actually moved (>5px threshold to ignore tiny movements)
    const deltaX = Math.abs(event.clientX - this.pointerDownPos.x);
    const deltaY = Math.abs(event.clientY - this.pointerDownPos.y);
    if (deltaX > 5 || deltaY > 5) {
      this.didDrag = true;
    }

    const newX = event.clientX - this.startPos.x;
    const newY = event.clientY - this.startPos.y;

    // Constrain to image bounds (prevent white space)
    const photo = this.zoom.lightbox.photoElement;
    const container = this.zoom.lightbox.container.querySelector('.lightbox-stage');

    const photoWidth = photo.naturalWidth * this.zoom.currentScale;
    const photoHeight = photo.naturalHeight * this.zoom.currentScale;
    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

    const maxX = Math.max(0, (photoWidth - containerWidth) / 2);
    const maxY = Math.max(0, (photoHeight - containerHeight) / 2);

    this.zoom.panOffset.x = Math.max(-maxX, Math.min(maxX, newX));
    this.zoom.panOffset.y = Math.max(-maxY, Math.min(maxY, newY));

    photo.style.transform = `
      translate(${this.zoom.panOffset.x}px, ${this.zoom.panOffset.y}px)
      scale(${this.zoom.currentScale})
    `;
  }

  handlePointerUp() {
    if (!this.isDragging) return;

    this.isDragging = false;
    this.zoom.lightbox.photoElement.style.cursor = 'grab';
    this.zoom.lightbox.photoElement.style.transition = '';
  }

  // Check if the last interaction was a drag (for click prevention)
  wasRecentDrag() {
    return this.didDrag;
  }

  // Reset drag flag (called after click handler checks it)
  resetDragFlag() {
    this.didDrag = false;
  }
}
