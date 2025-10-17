# Phase 5: Keyboard Shortcuts - Power User Features

**Duration:** 10-14 hours
**Dependencies:** Phase 3 (Zoom) and Phase 4 (Actions) complete
**Goal:** Comprehensive keyboard shortcuts for power users

---

## Context

Add full keyboard support for all lightbox functionality:
- Navigation shortcuts (ESC, ←, →)
- Panel toggle (I)
- Zoom shortcuts (Z, 0, F, 1-4, +, -)
- Pan shortcuts (arrow keys, Space+drag)
- Action shortcuts (S, D, Delete)
- Help overlay (?)

### Keyboard Shortcuts Reference

| Category | Shortcut | Action |
|----------|----------|--------|
| **Navigation** | ESC | Close lightbox (or panel if open) |
| | ← | Previous photo |
| | → | Next photo |
| **Panel** | I | Toggle info panel |
| **Zoom** | Z | Cycle zoom (Fit → Fill → 100%) |
| | 0 | Reset to Fit |
| | F | Toggle Fit/Fill |
| | 1 | Zoom to 100% |
| | 2 | Zoom to 200% |
| | 3 | Zoom to 300% |
| | 4 | Zoom to 400% |
| | + / = | Zoom in 10% |
| | - | Zoom out 10% |
| **Pan** | ↑↓←→ | Pan 50px (when zoomed) |
| | Space+drag | Pan with spacebar |
| **Actions** | S | Toggle favorite |
| | D | Download |
| | Delete | Delete photo |
| **Help** | ? | Show/hide help overlay |

---

## Tasks

### 1. Create `lightboxKeyboard.js` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/js/components/lightboxKeyboard.js`

```javascript
export class LightboxKeyboard {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.enabled = false;
    this.handlers = new Map();
    this.helpOverlayOpen = false;
    this.registerShortcuts();
  }

  registerShortcuts() {
    // Navigation
    this.register('Escape', () => this.handleEscape());
    this.register('ArrowLeft', () => this.lightbox.previous());
    this.register('ArrowRight', () => this.lightbox.next());

    // Panel
    this.register('i', () => this.lightbox.panel.toggle());
    this.register('I', () => this.lightbox.panel.toggle());

    // Zoom
    this.register('z', () => this.lightbox.zoom.cycle());
    this.register('Z', () => this.lightbox.zoom.cycle());
    this.register('0', () => this.lightbox.zoom.setMode('fit'));
    this.register('f', () => this.toggleFitFill());
    this.register('F', () => this.toggleFitFill());
    this.register('1', () => this.lightbox.zoom.setMode('100%'));
    this.register('2', () => this.zoomToPercent(2.0));
    this.register('3', () => this.zoomToPercent(3.0));
    this.register('4', () => this.zoomToPercent(4.0));
    this.register('+', () => this.zoomIn());
    this.register('=', () => this.zoomIn()); // + without shift
    this.register('-', () => this.zoomOut());

    // Pan (when zoomed)
    this.register('ArrowUp', () => this.pan(0, -50));
    this.register('ArrowDown', () => this.pan(0, 50));
    // Left/Right handled by navigation, but work for pan when zoomed

    // Actions
    this.register('s', () => this.lightbox.actions.toggleFavorite());
    this.register('S', () => this.lightbox.actions.toggleFavorite());
    this.register('d', () => this.lightbox.actions.download());
    this.register('D', () => this.lightbox.actions.download());
    this.register('Delete', () => this.lightbox.actions.delete());

    // Help
    this.register('?', () => this.toggleHelpOverlay());
  }

  register(key, handler) {
    this.handlers.set(this.normalizeKey(key), handler);
  }

  normalizeKey(key) {
    // Normalize key names
    return key.replace('Arrow', '').toLowerCase();
  }

  enable() {
    if (this.enabled) return;
    this.enabled = true;
    document.addEventListener('keydown', this.handleKeyDown);
  }

  disable() {
    if (!this.enabled) return;
    this.enabled = false;
    document.removeEventListener('keydown', this.handleKeyDown);
  }

  handleKeyDown = (event) => {
    if (!this.enabled) return;

    // Ignore if typing in input/textarea
    if (event.target.matches('input, textarea, select')) {
      return;
    }

    const key = this.normalizeKey(event.key);
    const handler = this.handlers.get(key) || this.handlers.get(event.key);

    if (handler) {
      event.preventDefault();
      handler();
    }
  };

  handleEscape() {
    if (this.helpOverlayOpen) {
      // Close help overlay
      this.toggleHelpOverlay();
    } else if (this.lightbox.panel.isOpen) {
      // Close panel
      this.lightbox.panel.close();
    } else {
      // Close lightbox
      this.lightbox.close();
    }
  }

  toggleFitFill() {
    const zoom = this.lightbox.zoom;
    if (zoom.mode === 'fit') {
      zoom.setMode('fill');
    } else {
      zoom.setMode('fit');
    }
  }

  zoomToPercent(scale) {
    const zoom = this.lightbox.zoom;
    zoom.currentScale = scale;
    zoom.mode = 'custom';
    zoom.panOffset = { x: 0, y: 0 };
    zoom.apply();
    zoom.updateBadge();
  }

  zoomIn() {
    const zoom = this.lightbox.zoom;
    zoom.currentScale = Math.min(zoom.maxScale, zoom.currentScale + 0.1);
    zoom.mode = 'custom';
    zoom.apply();
    zoom.updateBadge();
  }

  zoomOut() {
    const zoom = this.lightbox.zoom;
    zoom.currentScale = Math.max(zoom.minScale, zoom.currentScale - 0.1);
    zoom.mode = 'custom';
    zoom.apply();
    zoom.updateBadge();
  }

  pan(deltaX, deltaY) {
    const zoom = this.lightbox.zoom;
    if (zoom.currentScale <= 1.0) return; // Can't pan when not zoomed

    zoom.panOffset.x += deltaX;
    zoom.panOffset.y += deltaY;

    // Constrain to bounds
    const photo = this.lightbox.photoElement;
    const rect = photo.getBoundingClientRect();
    const photoWidth = photo.naturalWidth * zoom.currentScale;
    const photoHeight = photo.naturalHeight * zoom.currentScale;

    const maxX = Math.max(0, (photoWidth - rect.width) / 2);
    const maxY = Math.max(0, (photoHeight - rect.height) / 2);

    zoom.panOffset.x = Math.max(-maxX, Math.min(maxX, zoom.panOffset.x));
    zoom.panOffset.y = Math.max(-maxY, Math.min(maxY, zoom.panOffset.y));

    zoom.apply();
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
          <button class="btn-close-help" aria-label="Close">×</button>
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
              <div><kbd>Z</kbd> Cycle zoom</div>
              <div><kbd>0</kbd> Reset to fit</div>
              <div><kbd>F</kbd> Toggle Fit/Fill</div>
              <div><kbd>1</kbd>-<kbd>4</kbd> Zoom to 100%-400%</div>
              <div><kbd>+</kbd> / <kbd>-</kbd> Zoom in/out 10%</div>
            </div>
          </div>

          <div class="help-section">
            <h3>Pan (when zoomed)</h3>
            <div class="help-shortcuts">
              <div><kbd>↑</kbd> <kbd>↓</kbd> <kbd>←</kbd> <kbd>→</kbd> Pan photo</div>
              <div><kbd>Space</kbd>+drag Pan with spacebar</div>
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
    overlay.querySelector('.btn-close-help').addEventListener('click', () => {
      this.toggleHelpOverlay();
    });

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
```

---

### 2. Add Keyboard Shortcuts CSS

**Add to `lightbox.css`:**

```css
/* Keyboard Help Overlay */
.keyboard-help-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.8);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1005;
  animation: fadeIn 200ms;
}

.help-card {
  background: rgba(30, 30, 40, 0.95);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  max-width: 800px;
  max-height: 80vh;
  overflow-y: auto;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
}

.help-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 24px 32px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.help-header h2 {
  font-size: 24px;
  font-weight: 600;
  margin: 0;
  color: rgba(255, 255, 255, 0.95);
}

.btn-close-help {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.7);
  font-size: 32px;
  cursor: pointer;
  padding: 0;
  line-height: 1;
}

.help-content {
  padding: 24px 32px;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 24px;
}

.help-section h3 {
  font-size: 14px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: rgba(255, 255, 255, 0.5);
  margin: 0 0 12px 0;
}

.help-shortcuts {
  display: grid;
  gap: 8px;
}

.help-shortcuts div {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 14px;
  color: rgba(255, 255, 255, 0.8);
}

kbd {
  padding: 4px 8px;
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 4px;
  font-family: monospace;
  font-size: 12px;
  color: rgba(255, 255, 255, 0.95);
  min-width: 28px;
  text-align: center;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

/* First-use Tooltip */
.zoom-hint-tooltip {
  position: fixed;
  bottom: 80px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(30, 30, 40, 0.95);
  backdrop-filter: blur(10px);
  padding: 12px 20px;
  border-radius: 8px;
  color: rgba(255, 255, 255, 0.95);
  font-size: 14px;
  z-index: 1004;
  animation: slideUp 300ms ease-out;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.3);
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateX(-50%) translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
  }
}
```

---

### 3. Show First-Use Tooltip

**Add to `lightbox.js` open method:**

```javascript
async open(photoId) {
  // ... existing open logic ...

  // Show first-use hint
  this.showFirstUseHint();

  // ... rest of open logic ...
}

showFirstUseHint() {
  // Check if hint has been shown
  if (localStorage.getItem('zoom-hint-seen')) return;

  const tooltip = document.createElement('div');
  tooltip.className = 'zoom-hint-tooltip';
  tooltip.innerHTML = '💡 Click photo to zoom • Press <kbd>?</kbd> for shortcuts';

  document.body.appendChild(tooltip);

  // Auto-dismiss after 5s
  setTimeout(() => {
    tooltip.remove();
  }, 5000);

  // Mark as seen
  localStorage.setItem('zoom-hint-seen', 'true');
}
```

---

### 4. Update Panel Keyboard Shortcuts Section

**Update `lightboxPanel.js`:**

```javascript
createDOM() {
  // ... existing panel HTML ...

  // Add keyboard shortcuts section (4th section)
  const shortcutsSection = document.createElement('section');
  shortcutsSection.className = 'panel-section';
  shortcutsSection.id = 'shortcuts-section';
  shortcutsSection.innerHTML = `
    <details>
      <summary><h3>Keyboard Shortcuts</h3></summary>
      <div class="shortcuts-grid">
        <div><kbd>I</kbd> Toggle this panel</div>
        <div><kbd>ESC</kbd> Close lightbox</div>
        <div><kbd>←/→</kbd> Navigate photos</div>
        <div><kbd>Z</kbd> Cycle zoom</div>
        <div><kbd>S</kbd> Favorite</div>
        <div><kbd>D</kbd> Download</div>
        <div><kbd>?</kbd> Show all shortcuts</div>
      </div>
    </details>
  `;

  panel.appendChild(shortcutsSection);
}
```

---

## Verification Steps

```javascript
// Basic shortcuts test:
app.components.lightbox.open(app.photos[0].id);
// Press I: Panel toggles ✓
// Press ESC: Panel closes (or lightbox if panel closed) ✓
// Press →: Next photo ✓
// Press ←: Previous photo ✓

// Zoom shortcuts test:
// Press Z: Cycles zoom (Fit → Fill → 100% → Fit) ✓
// Press 0: Resets to fit ✓
// Press 2: Zooms to 200% ✓
// Press +: Zooms in 10% ✓
// Press -: Zooms out 10% ✓
// Press F: Toggles Fit/Fill ✓

// Pan shortcuts test (after zooming in):
// Press ↑: Pans up 50px ✓
// Press ↓: Pans down 50px ✓
// Press ←: Pans left (when zoomed) ✓
// Press →: Pans right (when zoomed) ✓

// Action shortcuts test:
// Press S: Toggles favorite ✓
// Press D: Downloads photo ✓
// Press Delete: Shows delete confirmation ✓

// Help overlay test:
// Press ?: Shows help overlay ✓
// - Help card displays all shortcuts ✓
// - Organized by category ✓
// Press ESC or ?: Closes help overlay ✓
// Click outside: Closes help overlay ✓

// First-use tooltip test:
// Clear localStorage: localStorage.removeItem('zoom-hint-seen')
// Open lightbox for first time:
// - Tooltip shows at bottom ✓
// - Message: "💡 Click photo to zoom • Press ? for shortcuts" ✓
// - Auto-dismisses after 5s ✓
// Close and reopen lightbox:
// - Tooltip doesn't show again ✓

// Conflict prevention test:
// Focus on an <input> element (if any exist)
// Press shortcuts:
// - No shortcut actions triggered ✓
// - Characters typed into input ✓
```

---

## Success Criteria

- [ ] All shortcuts work correctly
- [ ] No conflicts with browser shortcuts
- [ ] Typing in inputs doesn't trigger shortcuts
- [ ] Help overlay is comprehensive and clear
- [ ] First-use tooltip appears once
- [ ] ESC key precedence: Help → Panel → Lightbox
- [ ] Keyboard shortcuts section in panel is accessible
- [ ] All shortcuts documented in help overlay

---

## Rollback Strategy

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git clean -f samples/S6.SnapVault/wwwroot/js/components/lightboxKeyboard.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/css/lightbox.css
```

---

## Next Steps

- **Phase 6:** Implement WCAG AAA accessibility compliance

See: [Phase 6 Documentation](./LIGHTBOX_PHASE_6.md)
