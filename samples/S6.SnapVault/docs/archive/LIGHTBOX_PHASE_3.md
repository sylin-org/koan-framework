# Phase 3: Smart Zoom - Zero-UI Zoom System

**Duration:** 20-24 hours
**Dependencies:** Phase 2 (Photo Reflow) complete
**Goal:** Implement intuitive zoom with click-to-cycle, scroll-wheel, and pinch gestures

---

## Context

Create a zero-UI zoom system that "just works":
- **Click photo:** Cycles through Fit → Fill → 100% → back to Fit
- **Scroll wheel (desktop):** Zoom in/out toward cursor position
- **Pinch (mobile):** Natural pinch-to-zoom
- **Pan when zoomed:** Drag photo to view different areas
- **Zoom badge:** Shows current zoom level, auto-hides in Fit mode

### Zoom Modes

| Mode | Description | Scale |
|------|-------------|-------|
| **Fit** | Smart auto-fit (default) | Calculated to fit viewport |
| **Fill** | Maximize size, may crop edges | Calculated to fill viewport |
| **100%** | Actual size (1:1 pixel ratio) | photo.width / viewport.width |
| **Custom** | Scroll-wheel zoom | 50% - 400% |

---

## Tasks

### 1. Create `lightboxZoom.js` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/js/components/lightboxZoom.js`

**Class Structure:**
```javascript
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
    const container = this.lightbox.container.querySelector('.lightbox-content');

    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

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
    const container = this.lightbox.container.querySelector('.lightbox-content');

    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

    const photoWidth = photo.naturalWidth;
    const photoHeight = photo.naturalHeight;

    // Fill: Maximize size, may crop edges
    return Math.max(
      containerWidth / photoWidth,
      containerHeight / photoHeight
    );
  }

  calculate100Scale() {
    const photo = this.lightbox.photoElement;
    const container = this.lightbox.container.querySelector('.lightbox-content');

    const photoWidth = photo.naturalWidth;
    const containerWidth = container.clientWidth;

    // 1:1 pixel ratio
    return containerWidth / photoWidth;
  }

  apply() {
    const photo = this.lightbox.photoElement;

    photo.style.transition = 'transform 300ms cubic-bezier(0.4, 0, 0.2, 1)';
    photo.style.transform = `
      translate(${this.panOffset.x}px, ${this.panOffset.y}px)
      scale(${this.currentScale})
    `;

    // Update cursor
    if (this.currentScale > 1.0) {
      photo.style.cursor = 'grab';
    } else {
      photo.style.cursor = 'zoom-in';
    }

    // Enable/disable pan
    this.panController.setEnabled(this.currentScale > 1.0);
  }

  handleWheelZoom(event) {
    event.preventDefault();

    const delta = event.deltaY > 0 ? -0.1 : 0.1; // 10% per notch
    const newScale = Math.max(this.minScale, Math.min(this.maxScale, this.currentScale + delta));

    if (newScale !== this.currentScale) {
      // Zoom toward cursor position
      const rect = this.lightbox.photoElement.getBoundingClientRect();
      const cursorX = event.clientX - rect.left;
      const cursorY = event.clientY - rect.top;

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

    if (newScale !== this.currentScale) {
      // Zoom toward pinch center
      const rect = this.lightbox.photoElement.getBoundingClientRect();
      const pinchX = centerX - rect.left;
      const pinchY = centerY - rect.top;

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
    const rect = photo.getBoundingClientRect();
    const photoWidth = photo.naturalWidth * this.zoom.currentScale;
    const photoHeight = photo.naturalHeight * this.zoom.currentScale;

    const maxX = Math.max(0, (photoWidth - rect.width) / 2);
    const maxY = Math.max(0, (photoHeight - rect.height) / 2);

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
```

---

### 2. Create `lightbox-zoom.css` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/css/lightbox-zoom.css`

```css
/* Zoom Badge */
.zoom-badge {
  position: fixed;
  bottom: 24px;
  left: 24px;
  padding: 8px 16px;
  background: rgba(20, 20, 30, 0.9);
  backdrop-filter: blur(10px);
  border-radius: 20px;
  color: rgba(255, 255, 255, 0.95);
  font-size: 13px;
  font-weight: 600;
  z-index: 1004;
  opacity: 0;
  transform: translateY(20px);
  transition: opacity 200ms, transform 200ms;
  pointer-events: none;
}

.zoom-badge.visible {
  opacity: 1;
  transform: translateY(0);
}

/* Cursor States */
.lightbox-photo {
  cursor: zoom-in;
}

.lightbox-photo.zoomed {
  cursor: grab;
}

.lightbox-photo.zoomed:active {
  cursor: grabbing;
}

/* Mobile Zoom (Touch) */
@media (max-width: 767px) {
  .zoom-badge {
    bottom: 16px;
    left: 16px;
  }
}
```

---

### 3. Integrate `LightboxZoom` into `Lightbox`

**Update `lightbox.js`:**

```javascript
import { LightboxZoom } from './lightboxZoom.js';

export class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.zoom = new LightboxZoom(this);
    this.setupZoomListeners();
  }

  setupZoomListeners() {
    // Click-to-cycle
    this.photoElement.addEventListener('click', () => {
      this.zoom.cycle();
    });

    // Scroll-wheel zoom (desktop)
    this.photoElement.addEventListener('wheel', (e) => {
      this.zoom.handleWheelZoom(e);
    });

    // Pinch zoom (mobile)
    this.photoElement.addEventListener('touchstart', (e) => {
      if (e.touches.length === 2) {
        this.zoom.handlePinchZoom(e);
      }
    });

    this.photoElement.addEventListener('touchmove', (e) => {
      if (e.touches.length === 2) {
        e.preventDefault();
        this.zoom.handlePinchZoom(e);
      }
    });

    this.photoElement.addEventListener('touchend', () => {
      this.zoom.pinchStartDistance = null;
    });

    // Pan when zoomed
    this.photoElement.addEventListener('pointerdown', (e) => {
      this.zoom.panController.handlePointerDown(e);
    });

    document.addEventListener('pointermove', (e) => {
      this.zoom.panController.handlePointerMove(e);
    });

    document.addEventListener('pointerup', () => {
      this.zoom.panController.handlePointerUp();
    });
  }

  async open(photoId) {
    // ... existing open logic ...

    // Reset zoom when opening new photo
    this.zoom.reset();

    // ... rest of open logic ...
  }
}
```

---

### 4. Add zoom badge to DOM

**Update `lightbox.js` render:**

```javascript
render() {
  // ... existing render logic ...

  // Append zoom badge
  this.container.appendChild(this.zoom.badge);
}
```

---

## Verification Steps

```javascript
// Click-to-cycle test:
app.components.lightbox.open(app.photos[0].id);
// Click photo once:
// - Zooms from Fit → Fill ✓
// - Badge shows "Fill" ✓
// Click photo again:
// - Zooms from Fill → 100% ✓
// - Badge shows "100%" ✓
// Click photo again:
// - Zooms from 100% → Fit ✓
// - Badge shows "Fit", auto-hides after 2s ✓

// Scroll-wheel test (desktop):
// Hover over photo, scroll up:
// - Zooms in 10% per scroll ✓
// - Badge shows percentage (e.g., "110%", "120%") ✓
// - Cursor position stays stable ✓
// Scroll down:
// - Zooms out 10% per scroll ✓
// - Stops at 50% minimum ✓

// Pinch zoom test (mobile or DevTools mobile emulation):
// Two-finger pinch out:
// - Zooms in ✓
// - Pinch center stays stable ✓
// Two-finger pinch in:
// - Zooms out (but not below 100%) ✓

// Pan test:
// Zoom in (any method) until scale > 1.0
// Drag photo:
// - Pans smoothly ✓
// - Can't pan beyond image edges (no white space) ✓
// - Cursor changes to grabbing while dragging ✓

// Badge test:
// Fit mode: Badge auto-hides after 2s ✓
// Fill/100%/custom: Badge persists ✓
```

---

## Success Criteria

- [ ] Smart auto-fit handles 95% of photos perfectly
- [ ] Click-to-cycle is discoverable and smooth (300ms animation)
- [ ] Zoom badge appears/disappears appropriately:
  - [ ] Shows on mode change
  - [ ] Auto-hides in Fit mode after 2s
  - [ ] Persists in other modes
- [ ] Scroll-wheel zoom:
  - [ ] 10% per scroll notch
  - [ ] Zooms toward cursor position
  - [ ] Clamps between 50% and 400%
  - [ ] Feels precise and responsive
- [ ] Pinch zoom (mobile):
  - [ ] Feels natural (no lag)
  - [ ] Zooms toward pinch center
  - [ ] Clamps between 100% and 400%
- [ ] Pan functionality:
  - [ ] Only enabled when scale > 1.0
  - [ ] Respects image boundaries (no white space)
  - [ ] Cursor changes: grab → grabbing
  - [ ] Smooth (60fps)
- [ ] All interactions feel 60fps smooth
- [ ] No console errors

---

## Rollback Strategy

If Phase 3 fails:

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git clean -f samples/S6.SnapVault/wwwroot/js/components/lightboxZoom.js
git clean -f samples/S6.SnapVault/wwwroot/css/lightbox-zoom.css
```

---

## Next Steps

After Phase 3 is complete:
- **Phase 4:** Migrate all photo actions into unified panel

See: [Phase 4 Documentation](./LIGHTBOX_PHASE_4.md)
