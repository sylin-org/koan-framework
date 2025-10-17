# Phase 6: Accessibility - WCAG AAA Compliance

**Duration:** 12-16 hours
**Dependencies:** All previous phases complete
**Goal:** WCAG AAA compliance and comprehensive screen reader support

---

## Context

Ensure the lightbox is fully accessible to users with disabilities:
- ARIA labels and roles
- Focus management and focus trap
- Screen reader announcements
- Keyboard navigation (completed in Phase 5)
- Reduced motion support
- Color contrast (WCAG AAA: 7:1 minimum)
- Touch targets (44×44px minimum)
- High contrast mode support

---

## Tasks

### 1. Add ARIA Labels to All Interactive Elements

**Update `lightbox.js` render:**

```javascript
render() {
  const lightbox = document.createElement('div');
  lightbox.className = 'lightbox-overlay';
  // ARIA: Dialog role
  lightbox.setAttribute('role', 'dialog');
  lightbox.setAttribute('aria-modal', 'true');
  lightbox.setAttribute('aria-labelledby', 'lightbox-title');
  lightbox.setAttribute('aria-describedby', 'lightbox-description');

  lightbox.innerHTML = `
    <!-- Hidden title for screen readers -->
    <h1 id="lightbox-title" class="sr-only">Photo Viewer</h1>
    <p id="lightbox-description" class="sr-only">
      Use arrow keys to navigate photos, I to toggle info panel, ESC to close.
    </p>

    <div class="lightbox-content" role="document">
      <img class="lightbox-photo" alt="" aria-label="Current photo" />
    </div>

    <!-- Close button -->
    <button class="lightbox-close" aria-label="Close photo viewer">×</button>

    <!-- Navigation -->
    <button class="lightbox-prev" aria-label="Previous photo">
      <svg><!-- Arrow icon --></svg>
    </button>
    <button class="lightbox-next" aria-label="Next photo">
      <svg><!-- Arrow icon --></svg>
    </button>

    <!-- Info toggle -->
    <button
      class="lightbox-info-toggle"
      aria-label="Toggle photo information panel"
      aria-expanded="false"
      aria-controls="info-panel">
      <svg><!-- Info icon --></svg>
    </button>
  `;

  this.container = lightbox;
  document.body.appendChild(lightbox);
}
```

**Update `lightboxPanel.js` with ARIA:**

```javascript
createDOM() {
  const panel = document.createElement('div');
  panel.className = 'info-panel';
  panel.id = 'info-panel';
  // ARIA: Complementary role
  panel.setAttribute('role', 'complementary');
  panel.setAttribute('aria-labelledby', 'panel-title');

  panel.innerHTML = `
    <div class="panel-header">
      <h2 id="panel-title">Photo Information</h2>
      <button class="btn-close-panel" aria-label="Close panel">×</button>
    </div>
    <!-- ... rest of panel ... -->
  `;
}
```

---

### 2. Implement Focus Management

**Create FocusManager class:**

```javascript
class FocusManager {
  constructor(lightbox) {
    this.lightbox = lightbox;
    this.previousFocus = null;
    this.focusableElements = [];
  }

  captureFocus() {
    // Save currently focused element
    this.previousFocus = document.activeElement;

    // Get all focusable elements in lightbox
    this.updateFocusableElements();

    // Move focus to first element (close button)
    if (this.focusableElements.length > 0) {
      this.focusableElements[0].focus();
    }

    // Set up focus trap
    document.addEventListener('keydown', this.handleFocusTrap);
  }

  restoreFocus() {
    // Restore focus to previous element
    if (this.previousFocus && this.previousFocus.focus) {
      this.previousFocus.focus();
    }

    // Remove focus trap
    document.removeEventListener('keydown', this.handleFocusTrap);
  }

  updateFocusableElements() {
    const container = this.lightbox.container;
    const selector = 'button:not(:disabled), [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
    this.focusableElements = Array.from(container.querySelectorAll(selector));
  }

  handleFocusTrap = (event) => {
    if (event.key !== 'Tab') return;

    this.updateFocusableElements();

    const firstElement = this.focusableElements[0];
    const lastElement = this.focusableElements[this.focusableElements.length - 1];

    if (event.shiftKey) {
      // Shift+Tab: Move backwards
      if (document.activeElement === firstElement) {
        event.preventDefault();
        lastElement.focus();
      }
    } else {
      // Tab: Move forwards
      if (document.activeElement === lastElement) {
        event.preventDefault();
        firstElement.focus();
      }
    }
  };
}

// Add to Lightbox class:
export class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.focusManager = new FocusManager(this);
  }

  open(photoId) {
    // ... existing open logic ...

    // Capture focus
    this.focusManager.captureFocus();
  }

  close() {
    // Restore focus
    this.focusManager.restoreFocus();

    // ... existing close logic ...
  }
}
```

---

### 3. Add Screen Reader Announcements

**Create AnnouncementManager class:**

```javascript
class AnnouncementManager {
  constructor() {
    this.liveRegion = this.createLiveRegion();
  }

  createLiveRegion() {
    const region = document.createElement('div');
    region.className = 'sr-only';
    region.setAttribute('aria-live', 'polite');
    region.setAttribute('aria-atomic', 'true');
    document.body.appendChild(region);
    return region;
  }

  announce(message, priority = 'polite') {
    // Change priority to assertive for errors
    this.liveRegion.setAttribute('aria-live', priority);

    // Clear and set message
    this.liveRegion.textContent = '';
    setTimeout(() => {
      this.liveRegion.textContent = message;
    }, 100);
  }

  announcePhotoChange(index, total, filename) {
    this.announce(`Photo ${index + 1} of ${total}. ${filename}`);
  }

  announceZoomChange(mode) {
    this.announce(`Zoom changed to ${mode}`);
  }

  announcePanelState(isOpen) {
    this.announce(isOpen ? 'Photo information panel opened' : 'Photo information panel closed');
  }

  announceError(message) {
    this.announce(message, 'assertive');
  }
}

// Add to Lightbox class:
export class Lightbox {
  constructor(app) {
    // ... existing code ...
    this.announcer = new AnnouncementManager();
  }

  async open(photoId) {
    // ... existing open logic ...

    // Announce photo
    this.announcer.announcePhotoChange(
      this.currentIndex,
      this.app.photos.length,
      photo.originalFileName
    );
  }

  next() {
    // ... existing next logic ...
    this.announcer.announcePhotoChange(
      this.currentIndex,
      this.app.photos.length,
      this.currentPhotoData.originalFileName
    );
  }

  // Similar for previous()
}

// Add to LightboxPanel:
open() {
  this.isOpen = true;
  this.container.classList.add('open');
  this.lightbox.applyPhotoLayout({ open: true });

  // Update ARIA
  document.querySelector('.lightbox-info-toggle')?.setAttribute('aria-expanded', 'true');

  // Announce
  this.lightbox.announcer.announcePanelState(true);

  // Move focus to panel
  this.container.querySelector('.btn-close-panel')?.focus();
}
```

---

### 4. Add Reduced Motion Support

**Add to CSS:**

```css
/* Reduced Motion Support */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }

  .lightbox-overlay {
    animation: none;
  }

  .info-panel {
    transition: none;
  }

  .lightbox-photo {
    transition: none;
  }

  .zoom-badge {
    transition: none;
  }
}
```

---

### 5. Ensure Color Contrast (WCAG AAA: 7:1)

**Verify and update colors:**

```css
:root {
  /* Text colors - Ensure 7:1 contrast ratio */
  --text-primary: #f5f5f5;      /* 21:1 on #0a0a0a ✓ */
  --text-secondary: #d4d4d4;    /* 15:1 on #0a0a0a ✓ */
  --text-tertiary: #a3a3a3;     /* 9.5:1 on #0a0a0a ✓ */

  /* Accent colors - Ensure 7:1 ratio */
  --accent-primary: #60a5fa;    /* 8.2:1 on #0a0a0a ✓ */
  --accent-hover: #93c5fd;      /* 11:1 on #0a0a0a ✓ */

  /* Error/Warning colors */
  --error-text: #fca5a5;        /* 8.5:1 on #0a0a0a ✓ */
  --warning-text: #fcd34d;      /* 12:1 on #0a0a0a ✓ */
  --success-text: #86efac;      /* 11:1 on #0a0a0a ✓ */
}
```

**Test with axe DevTools:**
```bash
# Install: https://www.deque.com/axe/devtools/
# Run in browser console:
axe.run((err, results) => {
  console.log(results.violations);
});
# Should return 0 color contrast violations
```

---

### 6. Ensure Touch Targets (44×44px minimum)

**Update button styles:**

```css
/* All interactive elements */
button,
a,
.rating-star,
.btn-action {
  min-width: 44px;
  min-height: 44px;
  padding: 12px;
}

/* Navigation arrows */
.lightbox-prev,
.lightbox-next {
  width: 48px;
  height: 48px;
}

@media (max-width: 767px) {
  /* Larger touch targets on mobile */
  .lightbox-prev,
  .lightbox-next {
    width: 56px;
    height: 56px;
  }

  .lightbox-close {
    width: 48px;
    height: 48px;
  }
}
```

---

### 7. Add Screen Reader Only Class

**Add utility CSS:**

```css
/* Screen reader only (visually hidden) */
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border-width: 0;
}
```

---

### 8. High Contrast Mode Support

**Add high contrast CSS:**

```css
/* High Contrast Mode */
@media (prefers-contrast: high) {
  :root {
    --text-primary: ButtonText;
    --text-secondary: ButtonText;
    --bg-overlay: Canvas;
    --bg-panel: Canvas;
    --accent-primary: Highlight;
    --border-subtle: ButtonText;
  }

  button,
  .btn-action {
    border: 2px solid ButtonText;
  }

  .info-panel {
    border: 2px solid ButtonText;
  }
}
```

---

## Verification Steps

```javascript
// Keyboard navigation test:
app.components.lightbox.open(app.photos[0].id);
// Press Tab repeatedly:
// - Focus cycles: Close → Prev → Next → Info toggle ✓
// - Focus trapped within lightbox ✓
// - Visual focus indicator visible ✓
// Press I to open panel:
// - Focus moves to panel close button ✓
// Tab cycles through panel elements ✓
// Press ESC:
// - Panel closes, focus returns to info toggle ✓

// Screen reader test (NVDA on Windows):
// 1. Install NVDA: https://www.nvaccess.org/
// 2. Start NVDA (Ctrl+Alt+N)
// 3. Open lightbox:
//    - Announces "Photo Viewer dialog" ✓
//    - Announces "Viewing photo 1 of 10" ✓
// 4. Tab through elements:
//    - All buttons announce labels ✓
//    - Current photo announced ✓
// 5. Navigate to next photo:
//    - Announces "Photo 2 of 10. [filename]" ✓
// 6. Open panel:
//    - Announces "Photo information panel opened" ✓

// VoiceOver test (macOS):
// 1. Enable VoiceOver (Cmd+F5)
// 2. Test same steps as NVDA
// 3. Verify all announcements work

// Reduced motion test:
// 1. Windows: Settings → Accessibility → Visual effects → "Reduce animations"
// 2. macOS: System Preferences → Accessibility → Display → "Reduce motion"
// 3. Open lightbox and toggle panel:
//    - No animations, instant show/hide ✓
// 4. Zoom photo:
//    - No animations, instant zoom ✓

// Color contrast test:
// 1. Install axe DevTools extension
// 2. Open lightbox
// 3. Run axe scan
// 4. Check results:
//    - 0 color contrast issues ✓

// Touch target test (mobile):
// 1. Open Chrome DevTools, mobile emulation
// 2. Inspect all buttons:
//    - All buttons at least 44×44px ✓
//    - Navigation arrows 56×56px on mobile ✓

// High contrast mode test (Windows):
// 1. Settings → Accessibility → High contrast
// 2. Turn on high contrast
// 3. Open lightbox:
//    - All borders visible ✓
//    - Text readable ✓
//    - Buttons have clear boundaries ✓
```

---

## Success Criteria

- [ ] Passes automated accessibility tests (axe, WAVE) with 0 errors
- [ ] Screen reader users can:
  - [ ] Navigate entire lightbox without sighted help
  - [ ] Understand current photo position
  - [ ] Hear all button labels
  - [ ] Receive zoom/panel state announcements
- [ ] Focus management:
  - [ ] Focus captured when opening
  - [ ] Focus trapped within lightbox (Tab cycles)
  - [ ] Focus restored when closing
  - [ ] Visual focus indicator visible
- [ ] Reduced motion:
  - [ ] All animations respect `prefers-reduced-motion`
  - [ ] Panel shows/hides instantly
  - [ ] Zoom transitions instant
- [ ] Color contrast:
  - [ ] Meets WCAG AAA (7:1 minimum)
  - [ ] Verified with axe DevTools
- [ ] Touch targets:
  - [ ] All buttons meet 44×44px minimum
  - [ ] Navigation arrows 56×56px on mobile
- [ ] Keyboard navigation:
  - [ ] Completed in Phase 5
  - [ ] All functionality keyboard accessible
- [ ] High contrast mode:
  - [ ] Displays properly
  - [ ] All elements visible and readable

---

## Rollback Strategy

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightboxPanel.js
git checkout HEAD -- samples/S6.SnapVault/wwwroot/css/lightbox.css
git checkout HEAD -- samples/S6.SnapVault/wwwroot/css/lightbox-panel.css
```

---

## Next Steps

- **Phase 7:** Polish and edge case handling

See: [Phase 7 Documentation](./LIGHTBOX_PHASE_7.md)
