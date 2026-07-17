/**
 * Lightbox Keyboard Shortcuts Manager
 * Comprehensive keyboard support for power users
 */

export class LightboxKeyboard {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.enabled = false;
    this.handlers = new Map();
    this.helpOverlayOpen = false;
    this.helpOverlay = null;
    this.registerShortcuts();
  }

  registerShortcuts() {
    // Navigation
    this.register('Escape', () => this.handleEscape());
    this.register('ArrowLeft', () => this.handleLeftArrow());
    this.register('ArrowRight', () => this.handleRightArrow());
    this.register('ArrowUp', () => this.handleUpArrow());
    this.register('ArrowDown', () => this.handleDownArrow());

    // Panel
    this.register('i', () => this.lightbox.panel?.toggle());
    this.register('I', () => this.lightbox.panel?.toggle());

    // Zoom (simplified 3-mode system: Fit → Fill → Original)
    this.register('z', () => this.lightbox.zoomSystem?.cycle());
    this.register('Z', () => this.lightbox.zoomSystem?.cycle());
    this.register('0', () => this.lightbox.zoomSystem?.setMode('fit'));
    this.register('1', () => this.lightbox.zoomSystem?.setMode('fit'));
    this.register('2', () => this.lightbox.zoomSystem?.setMode('fill'));
    this.register('3', () => this.lightbox.zoomSystem?.setMode('original'));

    // Actions
    this.register('s', () => this.lightbox.actions?.toggleFavorite());
    this.register('S', () => this.lightbox.actions?.toggleFavorite());
    this.register('d', () => this.lightbox.actions?.download());
    this.register('D', () => this.lightbox.actions?.download());
    this.register('Delete', () => this.lightbox.actions?.deletePhoto());

    // AI Analysis (fact lock shortcuts)
    this.register('r', () => this.handleRegenerateAI());
    this.register('R', () => this.handleRegenerateAI());
    this.register('l', () => this.handleLockAllFacts());
    this.register('L', () => this.handleLockAllFacts());
    this.register('u', () => this.handleUnlockAllFacts());
    this.register('U', () => this.handleUnlockAllFacts());

    // Rating (1-5 already handled in lightbox.js)
    // Help
    this.register('?', () => this.toggleHelpOverlay());
  }

  register(key, handler) {
    this.handlers.set(key, handler);
  }

  enable() {
    if (this.enabled) return;
    this.enabled = true;
    console.log('[LightboxKeyboard] Keyboard shortcuts enabled');
    // Use capture phase to run before main app's keyboard handlers
    document.addEventListener('keydown', this.handleKeyDown, { capture: true });
  }

  disable() {
    if (!this.enabled) return;
    this.enabled = false;
    console.log('[LightboxKeyboard] Keyboard shortcuts disabled');
    document.removeEventListener('keydown', this.handleKeyDown, { capture: true });
    this.hideHelpOverlay();
  }

  handleKeyDown = (event) => {
    if (!this.enabled) {
      console.log('[LightboxKeyboard] Handler not enabled');
      return;
    }

    // Ignore if typing in input/textarea/select
    if (event.target.matches('input, textarea, select, [contenteditable="true"]')) {
      return;
    }

    const handler = this.handlers.get(event.key);

    if (handler) {
      console.log('[LightboxKeyboard] Handling key:', event.key);
      event.preventDefault();
      event.stopPropagation(); // Prevent main app shortcuts from firing
      handler();
    }
  };

  handleEscape() {
    if (this.helpOverlayOpen) {
      // Priority 1: Close help overlay
      this.toggleHelpOverlay();
    } else if (this.lightbox.panel?.isOpen) {
      // Priority 2: Close panel
      this.lightbox.panel.close();
    } else {
      // Priority 3: Close lightbox
      this.lightbox.close();
    }
  }

  handleLeftArrow() {
    console.log('[LightboxKeyboard] Left arrow pressed, isZoomed:', this.isZoomed());
    // If zoomed, pan left
    if (this.isZoomed()) {
      this.pan(-50, 0);
    } else {
      // Navigate to previous photo
      console.log('[LightboxKeyboard] Calling previous()');
      this.lightbox.previous();
    }
  }

  handleRightArrow() {
    console.log('[LightboxKeyboard] Right arrow pressed, isZoomed:', this.isZoomed());
    // If zoomed, pan right
    if (this.isZoomed()) {
      this.pan(50, 0);
    } else {
      // Navigate to next photo
      console.log('[LightboxKeyboard] Calling next()');
      this.lightbox.next();
    }
  }

  handleUpArrow() {
    // Pan up when zoomed
    if (this.isZoomed()) {
      this.pan(0, -50);
    }
  }

  handleDownArrow() {
    // Pan down when zoomed
    if (this.isZoomed()) {
      this.pan(0, 50);
    }
  }

  isZoomed() {
    const zoom = this.lightbox.zoomSystem;
    if (!zoom) return false;
    // In simplified system, fill and original modes are considered "zoomed" for pan purposes
    return zoom.mode === 'fill' || zoom.mode === 'original';
  }

  pan(deltaX, deltaY) {
    const zoom = this.lightbox.zoomSystem;
    if (!zoom || zoom.currentScale <= 1.0) return; // Can't pan when not zoomed

    zoom.panOffset.x += deltaX;
    zoom.panOffset.y += deltaY;

    // Constrain to bounds
    const photo = this.lightbox.photoElement;
    if (!photo) return;

    const container = this.lightbox.container.querySelector('.lightbox-stage');
    const photoWidth = photo.naturalWidth * zoom.currentScale;
    const photoHeight = photo.naturalHeight * zoom.currentScale;
    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

    const maxX = Math.max(0, (photoWidth - containerWidth) / 2);
    const maxY = Math.max(0, (photoHeight - containerHeight) / 2);

    zoom.panOffset.x = Math.max(-maxX, Math.min(maxX, zoom.panOffset.x));
    zoom.panOffset.y = Math.max(-maxY, Math.min(maxY, zoom.panOffset.y));

    zoom.apply();
  }

  async handleRegenerateAI() {
    if (this.lightbox.panel && this.lightbox.panel.isOpen) {
      await this.lightbox.panel.regenerateAIAnalysis();
    }
  }

  async handleLockAllFacts() {
    if (!this.lightbox.currentPhoto || !this.lightbox.currentPhoto.id) return;
    if (!this.lightbox.currentPhoto.aiAnalysis) return;

    try {
      const api = this.lightbox.app.api;
      await api.post(`/api/photos/${this.lightbox.currentPhoto.id}/facts/lock-all`);

      // Update local state
      const allFactKeys = Object.keys(this.lightbox.currentPhoto.aiAnalysis.facts);
      this.lightbox.currentPhoto.aiAnalysis.lockedFactKeys = allFactKeys;

      // Re-render panel if open
      if (this.lightbox.panel && this.lightbox.panel.isOpen) {
        this.lightbox.panel.render(this.lightbox.currentPhoto);
      }

      this.lightbox.app.components.toast.show(`Locked ${allFactKeys.length} facts`, { icon: '🔒', type: 'success' });
    } catch (error) {
      console.error('Failed to lock all facts:', error);
      this.lightbox.app.components.toast.show('Failed to lock all facts', { icon: '⚠️', type: 'error' });
    }
  }

  async handleUnlockAllFacts() {
    if (!this.lightbox.currentPhoto || !this.lightbox.currentPhoto.id) return;
    if (!this.lightbox.currentPhoto.aiAnalysis) return;

    try {
      const api = this.lightbox.app.api;
      await api.post(`/api/photos/${this.lightbox.currentPhoto.id}/facts/unlock-all`);

      // Update local state
      this.lightbox.currentPhoto.aiAnalysis.lockedFactKeys = [];

      // Re-render panel if open
      if (this.lightbox.panel && this.lightbox.panel.isOpen) {
        this.lightbox.panel.render(this.lightbox.currentPhoto);
      }

      this.lightbox.app.components.toast.show('Unlocked all facts', { icon: '🔓', type: 'success' });
    } catch (error) {
      console.error('Failed to unlock all facts:', error);
      this.lightbox.app.components.toast.show('Failed to unlock all facts', { icon: '⚠️', type: 'error' });
    }
  }

  toggleHelpOverlay() {
    this.helpOverlayOpen = !this.helpOverlayOpen;

    if (this.helpOverlayOpen) {
      this.showHelpOverlay();
    } else {
      this.hideHelpOverlay();
    }
  }

  showHelpOverlay() {
    const overlay = document.createElement('div');
    overlay.className = 'keyboard-help-overlay';
    overlay.innerHTML = `
      <div class="help-card">
        <div class="help-header">
          <h2>Keyboard Shortcuts</h2>
          <button class="btn-close-help" aria-label="Close help">×</button>
        </div>
        <div class="help-content">
          <div class="help-section">
            <h3>Navigation</h3>
            <div class="help-shortcuts">
              <div><kbd>ESC</kbd> Close lightbox</div>
              <div><kbd>←</kbd> Previous photo</div>
              <div><kbd>→</kbd> Next photo</div>
              <div><kbd>I</kbd> Toggle info panel</div>
            </div>
          </div>

          <div class="help-section">
            <h3>Zoom</h3>
            <div class="help-shortcuts">
              <div><kbd>Z</kbd> Cycle: Fit → Fill → Original</div>
              <div><kbd>0</kbd> or <kbd>1</kbd> Fit to screen</div>
              <div><kbd>2</kbd> Fill viewport</div>
              <div><kbd>3</kbd> Original (100%)</div>
              <div>or click photo to cycle</div>
            </div>
          </div>

          <div class="help-section">
            <h3>Pan (when zoomed)</h3>
            <div class="help-shortcuts">
              <div><kbd>↑</kbd> <kbd>↓</kbd> <kbd>←</kbd> <kbd>→</kbd> Pan photo</div>
              <div>or drag with mouse</div>
            </div>
          </div>

          <div class="help-section">
            <h3>Actions</h3>
            <div class="help-shortcuts">
              <div><kbd>S</kbd> Toggle favorite</div>
              <div><kbd>D</kbd> Download photo</div>
              <div><kbd>Delete</kbd> Delete photo</div>
            </div>
          </div>

          <div class="help-section">
            <h3>AI Analysis</h3>
            <div class="help-shortcuts">
              <div><kbd>R</kbd> Regenerate (reroll unlocked facts)</div>
              <div><kbd>L</kbd> Lock all facts</div>
              <div><kbd>U</kbd> Unlock all facts</div>
              <div>or click fact cards to lock/unlock</div>
            </div>
          </div>

          <div class="help-section">
            <h3>Help</h3>
            <div class="help-shortcuts">
              <div><kbd>?</kbd> Show/hide this help</div>
            </div>
          </div>
        </div>
      </div>
    `;

    document.body.appendChild(overlay);
    this.helpOverlay = overlay;

    // Close button
    const closeBtn = overlay.querySelector('.btn-close-help');
    closeBtn.addEventListener('click', () => this.toggleHelpOverlay());

    // Click outside to close
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) {
        this.toggleHelpOverlay();
      }
    });
  }

  hideHelpOverlay() {
    if (this.helpOverlay) {
      this.helpOverlay.remove();
      this.helpOverlay = null;
    }
  }
}
