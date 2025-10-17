/**
 * Lightbox Zoom System
 * Zero-UI zoom with click-to-cycle, scroll-wheel, and pinch gestures
 */

export class LightboxZoom {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.mode = 'fit'; // 'fit' | 'fill' | '100%' | 'custom'
    this.currentScale = 1.0;
    this.minScale = 0.5;
    this.maxScale = 4.0;
    this.panOffset = { x: 0, y: 0 };
    this.panController = new PanController(this);
    this.badge = this.createBadge();
    this.pinchStartDistance = null;
    this.pinchStartScale = 1.0;
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
    // Click-to-cycle: Fit → Fill → 100% → Fit
    if (this.mode === 'fit') {
      this.setMode('fill');
    } else if (this.mode === 'fill') {
      this.setMode('100%');
    } else {
      this.setMode('fit');
    }
  }

  setMode(mode) {
    this.mode = mode;

    switch (mode) {
      case 'fit':
        this.currentScale = this.calculateFitScale();
        this.panOffset = { x: 0, y: 0 };
        break;
      case 'fill':
        this.currentScale = this.calculateFillScale();
        this.panOffset = { x: 0, y: 0 };
        break;
      case '100%':
        this.currentScale = this.calculate100Scale();
        this.panOffset = { x: 0, y: 0 };
        break;
    }

    this.apply();
    this.updateBadge();
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

  calculate100Scale() {
    // 1:1 pixel ratio
    return 1.0;
  }

  apply() {
    const photo = this.lightbox.photoElement;
    if (!photo) return;

    photo.style.transition = 'transform 300ms cubic-bezier(0.4, 0, 0.2, 1)';
    photo.style.transform = `
      translate(${this.panOffset.x}px, ${this.panOffset.y}px)
      scale(${this.currentScale})
    `;

    // Update cursor
    if (this.currentScale > this.calculateFitScale() + 0.01) {
      photo.style.cursor = 'grab';
      photo.classList.add('zoomed');
    } else {
      photo.style.cursor = 'zoom-in';
      photo.classList.remove('zoomed');
    }

    // Enable/disable pan
    this.panController.setEnabled(this.currentScale > this.calculateFitScale() + 0.01);
  }

  handleWheelZoom(event) {
    event.preventDefault();

    const delta = event.deltaY > 0 ? -0.1 : 0.1; // 10% per notch
    const newScale = Math.max(this.minScale, Math.min(this.maxScale, this.currentScale + delta));

    if (Math.abs(newScale - this.currentScale) > 0.001) {
      // Zoom toward cursor position
      const rect = this.lightbox.photoElement.getBoundingClientRect();
      const cursorX = (event.clientX - rect.left - rect.width / 2) / this.currentScale;
      const cursorY = (event.clientY - rect.top - rect.height / 2) / this.currentScale;

      const scaleDiff = newScale - this.currentScale;
      this.panOffset.x -= cursorX * scaleDiff;
      this.panOffset.y -= cursorY * scaleDiff;

      this.currentScale = newScale;
      this.mode = 'custom';
      this.apply();
      this.updateBadge();
    }
  }

  handlePinchZoom(event) {
    // Get pinch center and scale
    const touch1 = event.touches[0];
    const touch2 = event.touches[1];

    const centerX = (touch1.clientX + touch2.clientX) / 2;
    const centerY = (touch1.clientY + touch2.clientY) / 2;

    const distance = Math.hypot(
      touch2.clientX - touch1.clientX,
      touch2.clientY - touch1.clientY
    );

    if (!this.pinchStartDistance) {
      this.pinchStartDistance = distance;
      this.pinchStartScale = this.currentScale;
      return;
    }

    const scaleChange = distance / this.pinchStartDistance;
    let newScale = this.pinchStartScale * scaleChange;

    // Clamp (mobile: min 100%, max 400%)
    newScale = Math.max(1.0, Math.min(this.maxScale, newScale));

    if (Math.abs(newScale - this.currentScale) > 0.001) {
      // Zoom toward pinch center
      const rect = this.lightbox.photoElement.getBoundingClientRect();
      const pinchX = (centerX - rect.left - rect.width / 2) / this.currentScale;
      const pinchY = (centerY - rect.top - rect.height / 2) / this.currentScale;

      const scaleDiff = newScale - this.currentScale;
      this.panOffset.x -= pinchX * scaleDiff;
      this.panOffset.y -= pinchY * scaleDiff;

      this.currentScale = newScale;
      this.mode = 'custom';
      this.apply();
      this.updateBadge();
    }
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
      case '100%':
        text = '100%';
        break;
      case 'custom':
        text = `${Math.round(this.currentScale * 100)}%`;
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
    this.startPos = { x: 0, y: 0 };
    this.enabled = false;
  }

  setEnabled(enabled) {
    this.enabled = enabled;
  }

  handlePointerDown(event) {
    if (!this.enabled) return;

    this.isDragging = true;
    this.startPos = {
      x: event.clientX - this.zoom.panOffset.x,
      y: event.clientY - this.zoom.panOffset.y
    };

    this.zoom.lightbox.photoElement.style.cursor = 'grabbing';
    this.zoom.lightbox.photoElement.style.transition = 'none';
  }

  handlePointerMove(event) {
    if (!this.isDragging) return;

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
}
