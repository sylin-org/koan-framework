# Phase 2: Photo Reflow - Intelligent Resizing

**Duration:** 12-16 hours
**Dependencies:** Phase 1 (Foundation) complete
**Goal:** Implement intelligent photo resizing when panel opens

---

## Context

When the info panel opens, the photo should gracefully reflow to fit the remaining space. This creates a better user experience than having the panel overlay the photo.

### Responsive Behavior

| Screen Size | Behavior |
|-------------|----------|
| **Desktop (>1200px)** | Photo shifts left by 190px (half panel width) and scales down to fit remaining space |
| **Tablet (768-1200px)** | Photo stays full-size, panel overlays with 10% backdrop dim |
| **Mobile (<768px)** | Photo dims to 80% brightness, bottom sheet slides up from bottom |

### Special Cases
- **Panoramas** (aspect ratio < 0.5): Force bottom sheet on all screen sizes
- **Portraits** (aspect ratio > 1.5): Use generous height scaling (0.85 vs 0.75)

---

## Tasks

### 1. Add photo layout calculation method to `Lightbox`

**Location:** Add to `lightbox.js`

**Method:**
```javascript
calculatePhotoLayout(photo, viewport, panelState) {
  const aspectRatio = photo.width / photo.height;
  const viewportRatio = viewport.width / viewport.height;

  // Special case: Panorama (ultra-wide)
  if (aspectRatio < 0.5) {
    return {
      mode: 'bottom-sheet',
      reason: 'panorama'
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
```

---

### 2. Implement desktop shift + scale behavior

**Update `lightbox.js` open/close methods:**

```javascript
class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.photoElement = null;
    this.currentLayout = null;
  }

  async open(photoId) {
    // ... existing open logic ...

    // Get photo element reference
    this.photoElement = this.container.querySelector('.lightbox-photo');

    // Calculate initial layout (panel closed)
    this.applyPhotoLayout({ open: false });

    // ... rest of open logic ...
  }

  applyPhotoLayout(panelState) {
    const viewport = {
      width: window.innerWidth,
      height: window.innerHeight
    };

    const photo = {
      width: this.currentPhotoData.width,
      height: this.currentPhotoData.height
    };

    const layout = this.calculatePhotoLayout(photo, viewport, panelState);
    this.currentLayout = layout;

    if (layout.mode === 'shift-scale') {
      // Desktop: Shift + scale animation
      this.photoElement.style.transition = 'transform 300ms cubic-bezier(0.4, 0, 0.2, 1), opacity 300ms';
      this.photoElement.style.transform = `
        translateX(${layout.offsetX}px)
        scale(${layout.scale})
      `;
      this.photoElement.style.opacity = panelState.open ? '0.95' : '1.0';
    } else if (layout.mode === 'overlay') {
      // Tablet: No photo reflow, just backdrop
      this.container.classList.toggle('panel-open', panelState.open);
    } else if (layout.mode === 'bottom-sheet') {
      // Mobile: Dim photo
      this.photoElement.style.opacity = panelState.open ? '0.8' : '1.0';
    }
  }
}
```

**Update panel open/close to trigger reflow:**

```javascript
class LightboxPanel {
  open() {
    this.isOpen = true;
    this.container.classList.add('open');

    // Trigger photo reflow
    this.lightbox.applyPhotoLayout({ open: true });
  }

  close() {
    this.isOpen = false;
    this.container.classList.remove('open');

    // Restore photo to original position
    this.lightbox.applyPhotoLayout({ open: false });
  }
}
```

---

### 3. Implement tablet overlay behavior

**Add to `lightbox.css`:**

```css
/* Tablet backdrop dim */
@media (min-width: 768px) and (max-width: 1199px) {
  .lightbox-overlay::after {
    content: '';
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.1);
    opacity: 0;
    transition: opacity 300ms;
    pointer-events: none;
    z-index: 1000;
  }

  .lightbox-overlay.panel-open::after {
    opacity: 1;
  }
}
```

---

### 4. Implement mobile bottom sheet behavior

**Add swipe-to-dismiss:**

```javascript
class LightboxPanel {
  constructor(lightbox, app) {
    // ... existing code ...
    this.setupMobileGestures();
  }

  setupMobileGestures() {
    if (window.innerWidth >= 768) return;

    let startY = 0;
    let currentY = 0;
    let isDragging = false;

    const handle = this.container.querySelector('.drag-handle');
    if (!handle) return;

    handle.addEventListener('touchstart', (e) => {
      startY = e.touches[0].clientY;
      isDragging = true;
      this.container.style.transition = 'none';
    });

    document.addEventListener('touchmove', (e) => {
      if (!isDragging) return;
      currentY = e.touches[0].clientY;
      const deltaY = currentY - startY;

      // Only allow dragging down
      if (deltaY > 0) {
        this.container.style.transform = `translateY(${deltaY}px)`;
      }
    });

    document.addEventListener('touchend', () => {
      if (!isDragging) return;
      isDragging = false;

      const deltaY = currentY - startY;
      const panelHeight = this.container.offsetHeight;
      const dismissThreshold = panelHeight * 0.5;

      this.container.style.transition = '';

      if (deltaY > dismissThreshold) {
        // Dismiss
        this.close();
      } else {
        // Snap back
        this.container.style.transform = '';
      }
    });
  }
}
```

---

### 5. Add drag handle to panel (Mobile)

**Update `lightboxPanel.js` createDOM:**

```javascript
createDOM() {
  const panel = document.createElement('div');
  panel.className = 'info-panel';
  panel.innerHTML = `
    <!-- Mobile drag handle -->
    <div class="drag-handle" aria-hidden="true"></div>

    <!-- Rest of panel structure ... -->
  `;

  this.container = panel;
  document.body.appendChild(panel);
}
```

**Add drag handle styles to `lightbox-panel.css`:**

```css
.drag-handle {
  display: none;
}

@media (max-width: 767px) {
  .drag-handle {
    display: block;
    width: 48px;
    height: 4px;
    background: rgba(255, 255, 255, 0.3);
    border-radius: 2px;
    margin: 12px auto;
    cursor: grab;
  }

  .drag-handle:active {
    cursor: grabbing;
  }
}
```

---

### 6. Handle window resize events

**Add resize handler to `Lightbox`:**

```javascript
class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.setupResizeHandler();
  }

  setupResizeHandler() {
    let resizeTimeout;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimeout);
      resizeTimeout = setTimeout(() => {
        if (this.isOpen && this.panel.isOpen) {
          this.applyPhotoLayout({ open: true });
        }
      }, 150); // Debounce
    });
  }
}
```

---

## Verification Steps

```javascript
// Desktop test (>1200px width):
app.components.lightbox.open(app.photos[0].id);
app.components.lightbox.panel.toggle();
// - Photo shifts left by ~190px ✓
// - Photo scales to fit remaining space ✓
// - Animation smooth (300ms) ✓
// - Aspect ratio preserved ✓
// - Photo dims slightly (opacity 0.95) ✓

// Close panel:
app.components.lightbox.panel.toggle();
// - Photo shifts back to center ✓
// - Photo scales back to original size ✓
// - Animation smooth ✓

// Tablet test (resize browser to 1000px width):
app.components.lightbox.open(app.photos[0].id);
app.components.lightbox.panel.toggle();
// - Photo stays full-size ✓
// - Backdrop dims 10% ✓
// - Panel overlays on right side ✓
// - No photo reflow ✓

// Mobile test (resize to 375px width):
app.components.lightbox.open(app.photos[0].id);
app.components.lightbox.panel.toggle();
// - Bottom sheet slides up from bottom ✓
// - Photo dims to 80% brightness ✓
// - Drag handle visible ✓
// - Can drag handle down to dismiss ✓
// - Swipe >50% dismisses sheet ✓
// - Swipe <50% snaps back ✓

// Panorama test:
// Find ultra-wide photo (width >> height)
app.components.lightbox.open(panoramaPhotoId);
app.components.lightbox.panel.toggle();
// - Forces bottom sheet even on desktop ✓
// - Photo stays horizontal (not rotated) ✓

// Portrait test:
// Find tall photo (height >> width)
app.components.lightbox.open(portraitPhotoId);
app.components.lightbox.panel.toggle();
// - Uses generous height scaling (0.85) ✓
// - Photo doesn't feel cramped ✓

// Performance test:
// Open Chrome DevTools → Performance
// Toggle panel open/close 5 times
// - All animations 60fps ✓
// - No layout thrashing ✓
// - Smooth transitions ✓
```

---

## Success Criteria

- [ ] Desktop (>1200px):
  - [ ] Photo reflows gracefully (shifts left + scales down)
  - [ ] Animation is smooth (60fps, 300ms duration)
  - [ ] Aspect ratio preserved
  - [ ] Photo dims slightly during transition
- [ ] Tablet (768-1200px):
  - [ ] Photo stays full-size
  - [ ] 10% backdrop dim creates visual hierarchy
  - [ ] Panel overlays photo
- [ ] Mobile (<768px):
  - [ ] Bottom sheet slides up from bottom
  - [ ] Photo dims to 80% brightness
  - [ ] Drag handle visible and functional
  - [ ] Swipe-to-dismiss works (>50% threshold)
  - [ ] Snap-back works (<50% threshold)
- [ ] Special cases:
  - [ ] Panoramas force bottom sheet on all screen sizes
  - [ ] Portraits get generous height scaling
- [ ] No layout jumps or flickers
- [ ] Window resize recalculates layout correctly

---

## Rollback Strategy

If Phase 2 fails:

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightboxPanel.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/css/lightbox.css
```

---

## Next Steps

After Phase 2 is complete:
- **Phase 3:** Add smart zoom functionality (click-to-cycle, pinch, scroll)

See: [Phase 3 Documentation](./LIGHTBOX_PHASE_3.md)
