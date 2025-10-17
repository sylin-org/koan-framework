# Phase 1: Foundation - Unified Panel Structure

**Duration:** 16-20 hours
**Dependencies:** None (Start here)
**Goal:** Create unified info panel structure and basic functionality

---

## Context

Replace two separate toggle panels (Info/AI) with a single unified panel containing 4 sections:
1. **Metadata** - EXIF data (camera, lens, settings)
2. **AI Insights** - AI-generated description and tags
3. **Actions** - Photo actions (favorite, rate, download, delete, regenerate AI)
4. **Keyboard Shortcuts** - Collapsible help section

### Current State
- Two separate panels: `.lightbox-metadata-panel` and `.lightbox-ai-panel`
- Two toggle buttons: Info (ⓘ) and AI (🤖)
- Panels slide from right side (desktop only)
- No responsive behavior

### Target State
- Single unified `.info-panel` with 4 sections
- Single toggle button (I key or button)
- Responsive: Side panel (desktop), Overlay (tablet), Bottom sheet (mobile)

---

## Tasks

### 1. Create `lightboxPanel.js` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/js/components/lightboxPanel.js`

**Class Structure:**
```javascript
export class LightboxPanel {
  constructor(lightbox, app) {
    this.lightbox = lightbox;
    this.app = app;
    this.isOpen = false;
    this.currentPhotoData = null;
    this.createDOM();
  }

  createDOM() {
    // Create panel structure (see HTML structure below)
  }

  render(photoData) {
    // Update panel with photo data
    this.currentPhotoData = photoData;
    this.renderMetadata(photoData);
    this.renderAIInsights(photoData);
    this.renderActions(photoData);
  }

  open() {
    this.isOpen = true;
    this.container.classList.add('open');
  }

  close() {
    this.isOpen = false;
    this.container.classList.remove('open');
  }

  toggle() {
    if (this.isOpen) {
      this.close();
    } else {
      this.open();
    }
  }

  renderMetadata(photo) {
    // Display EXIF: camera, lens, aperture, shutter, ISO, date
  }

  renderAIInsights(photo) {
    // Display: detailedDescription, autoTags, detectedObjects, moodDescription
    // Handle state: loading, empty, error
  }

  renderActions(photo) {
    // Display: favorite button, star rating, download, delete, regenerate AI
  }
}
```

**HTML Structure to Create:**
```html
<div class="info-panel">
  <!-- Header -->
  <div class="panel-header">
    <h2>Photo Information</h2>
    <button class="btn-close-panel" aria-label="Close panel">×</button>
  </div>

  <!-- Metadata Section -->
  <section class="panel-section" id="metadata-section">
    <h3>Details</h3>
    <div class="metadata-grid">
      <div class="metadata-item">
        <span class="label">Camera</span>
        <span class="value" id="meta-camera">Canon EOS R5</span>
      </div>
      <div class="metadata-item">
        <span class="label">Lens</span>
        <span class="value" id="meta-lens">RF 85mm F1.2 L USM</span>
      </div>
      <div class="metadata-item">
        <span class="label">Settings</span>
        <span class="value" id="meta-settings">f/2.8 • 1/500 • ISO 400</span>
      </div>
      <div class="metadata-item">
        <span class="label">Captured</span>
        <span class="value" id="meta-date">Oct 17, 2025</span>
      </div>
    </div>
  </section>

  <!-- AI Insights Section -->
  <section class="panel-section" id="ai-section">
    <h3>AI Insights</h3>
    <div class="ai-content" data-state="loaded">
      <!-- State: loading / empty / error / loaded -->
      <p class="ai-description" id="ai-description"></p>
      <div class="ai-tags" id="ai-tags"></div>
      <button class="btn-regenerate-ai" id="btn-regenerate">
        <svg><!-- Regenerate icon --></svg>
        Regenerate Description
      </button>
    </div>
  </section>

  <!-- Actions Section -->
  <section class="panel-section" id="actions-section">
    <h3>Actions</h3>
    <div class="actions-grid">
      <!-- Placeholder buttons for now, Phase 4 will implement -->
      <button class="btn-action" id="btn-favorite">
        <svg><!-- Star icon --></svg>
        Favorite
      </button>
      <button class="btn-action" id="btn-download">
        <svg><!-- Download icon --></svg>
        Download
      </button>
      <button class="btn-action btn-destructive" id="btn-delete">
        <svg><!-- Trash icon --></svg>
        Delete
      </button>
    </div>
  </section>

  <!-- Keyboard Shortcuts Section (Collapsed) -->
  <section class="panel-section" id="shortcuts-section">
    <details>
      <summary><h3>Keyboard Shortcuts</h3></summary>
      <div class="shortcuts-grid">
        <div><kbd>I</kbd> Toggle this panel</div>
        <div><kbd>ESC</kbd> Close lightbox</div>
        <div><kbd>←/→</kbd> Navigate photos</div>
        <!-- More shortcuts added in Phase 5 -->
      </div>
    </details>
  </section>
</div>
```

---

### 2. Create `lightbox-panel.css` (NEW FILE)

**Location:** `samples/S6.SnapVault/wwwroot/css/lightbox-panel.css`

**Key Styles:**
```css
/* Panel Base */
.info-panel {
  position: fixed;
  background: rgba(20, 20, 30, 0.95);
  backdrop-filter: blur(10px);
  color: rgba(255, 255, 255, 0.95);
  display: flex;
  flex-direction: column;
  transition: transform 300ms cubic-bezier(0.4, 0, 0.2, 1);
  z-index: 1003;
}

/* Mobile (<768px): Bottom sheet */
@media (max-width: 767px) {
  .info-panel {
    bottom: 0;
    left: 0;
    right: 0;
    height: 70vh;
    transform: translateY(100%);
    border-radius: 16px 16px 0 0;
  }

  .info-panel.open {
    transform: translateY(0);
  }

  .info-panel::before {
    content: '';
    display: block;
    width: 48px;
    height: 4px;
    background: rgba(255, 255, 255, 0.3);
    border-radius: 2px;
    margin: 12px auto;
    /* Drag handle */
  }
}

/* Tablet (768-1200px): Overlay */
@media (min-width: 768px) and (max-width: 1199px) {
  .info-panel {
    top: 0;
    right: 0;
    width: 320px;
    height: 100vh;
    transform: translateX(100%);
  }

  .info-panel.open {
    transform: translateX(0);
  }

  /* Backdrop dim */
  .lightbox-overlay::after {
    content: '';
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.1);
    opacity: 0;
    transition: opacity 300ms;
    pointer-events: none;
  }

  .lightbox-overlay.panel-open::after {
    opacity: 1;
  }
}

/* Desktop (>1200px): Side panel */
@media (min-width: 1200px) {
  .info-panel {
    top: 0;
    right: 0;
    width: 380px;
    height: 100vh;
    transform: translateX(100%);
  }

  .info-panel.open {
    transform: translateX(0);
  }

  /* Photo will shift left - handled in Phase 2 */
}

/* Panel Header */
.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.panel-header h2 {
  font-size: 18px;
  font-weight: 600;
  margin: 0;
}

.btn-close-panel {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.7);
  font-size: 24px;
  cursor: pointer;
  padding: 4px 8px;
}

/* Panel Sections */
.panel-section {
  padding: 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.panel-section h3 {
  font-size: 14px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: rgba(255, 255, 255, 0.5);
  margin: 0 0 12px 0;
}

/* Metadata Grid */
.metadata-grid {
  display: grid;
  gap: 12px;
}

.metadata-item {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.metadata-item .label {
  font-size: 13px;
  color: rgba(255, 255, 255, 0.5);
}

.metadata-item .value {
  font-size: 14px;
  color: rgba(255, 255, 255, 0.95);
  font-weight: 500;
}

/* AI Content States */
.ai-content[data-state="loading"]::before {
  content: 'Analyzing image...';
  display: block;
  color: rgba(255, 255, 255, 0.5);
  font-style: italic;
}

.ai-content[data-state="empty"]::before {
  content: 'No AI description generated yet';
  display: block;
  color: rgba(255, 255, 255, 0.5);
}

.ai-content[data-state="error"]::before {
  content: 'Failed to load AI insights';
  display: block;
  color: rgba(239, 68, 68, 0.9);
}

.ai-description {
  font-size: 14px;
  line-height: 1.6;
  color: rgba(255, 255, 255, 0.8);
  margin: 0 0 12px 0;
}

.ai-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 12px;
}

.ai-tag {
  padding: 4px 10px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  font-size: 12px;
  color: rgba(255, 255, 255, 0.8);
}

.btn-regenerate-ai {
  /* Button styles */
}

/* Actions Grid */
.actions-grid {
  display: grid;
  gap: 8px;
}

.btn-action {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: rgba(255, 255, 255, 0.9);
  font-size: 14px;
  cursor: pointer;
  transition: background 150ms, border-color 150ms;
}

.btn-action:hover {
  background: rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.2);
}

.btn-action.btn-destructive {
  color: rgba(239, 68, 68, 0.9);
  border-color: rgba(239, 68, 68, 0.3);
}

/* Keyboard Shortcuts */
.shortcuts-grid {
  display: grid;
  gap: 8px;
  font-size: 13px;
}

kbd {
  padding: 2px 6px;
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 4px;
  font-family: monospace;
  font-size: 11px;
}
```

---

### 3. Refactor `lightbox.js`

**Changes:**
```javascript
// At top of file
import { LightboxPanel } from './lightboxPanel.js';

export class Lightbox {
  constructor(app) {
    this.app = app;
    this.isOpen = false;
    this.currentPhotoId = null;
    this.currentIndex = 0;

    // NEW: Unified panel (replaces metadataPanelOpen, aiPanelOpen)
    this.panel = new LightboxPanel(this, app);

    this.render();
    this.setupEventListeners();
  }

  setupEventListeners() {
    // ... existing listeners ...

    // NEW: I key toggles panel
    document.addEventListener('keydown', (e) => {
      if (!this.isOpen) return;
      if (e.key === 'i' || e.key === 'I') {
        this.panel.toggle();
      }
    });
  }

  async open(photoId) {
    // ... existing open logic ...

    // Fetch photo data
    const response = await fetch(`/api/photos/${photoId}`);
    const photo = await response.json();

    // NEW: Render panel with photo data
    this.panel.render(photo);

    // ... rest of open logic ...
  }

  close() {
    // NEW: Close panel first
    if (this.panel.isOpen) {
      this.panel.close();
    }

    // ... existing close logic ...
  }

  // REMOVE: toggleMetadataPanel()
  // REMOVE: toggleAIPanel()
}
```

---

### 4. Extract lightbox styles from `app.css` to `lightbox.css`

**Location:** `samples/S6.SnapVault/wwwroot/css/lightbox.css`

**Action:**
1. Find all `.lightbox*` classes in `app.css` (around lines 800-1200)
2. Copy to new `lightbox.css` file
3. Remove from `app.css`
4. Delete old `.lightbox-metadata-panel` and `.lightbox-ai-panel` styles

---

### 5. Add CSS imports

**Recommended Option: Link in `index.html`**

Add to `<head>` section after `app.css`:
```html
<link rel="stylesheet" href="/css/lightbox.css">
<link rel="stylesheet" href="/css/lightbox-panel.css">
```

**Alternative: Import in `app.css`**
```css
@import 'lightbox.css';
@import 'lightbox-panel.css';
```

---

## Verification Steps

Open browser console and run:

```javascript
// Open lightbox
app.components.lightbox.open(app.photos[0].id);
// - Lightbox opens ✓
// - Photo displays ✓

// Panel toggle button visible
// - Look for toggle button in UI ✓

// Toggle panel with I key
// Press: I
// - Panel slides in from right (desktop) or bottom (mobile) ✓

// Check panel content
// - Metadata section shows EXIF data ✓
// - AI Insights section shows description ✓
// - Actions section shows placeholder buttons ✓
// - Keyboard shortcuts section present ✓

// Test responsive behavior
// - Desktop (>1200px): Side panel, 380px width ✓
// - Tablet (768-1200px): Overlay, 320px, backdrop dim ✓
// - Mobile (<768px): Bottom sheet, 70vh height, drag handle ✓
// Resize browser window to test each breakpoint

// Close panel
// Press: ESC
// - Panel closes ✓
// Press: ESC again
// - Lightbox closes ✓

// Console check
// - No console errors ✓
```

---

## Success Criteria

- [ ] Panel opens/closes smoothly on all screen sizes (desktop/tablet/mobile)
- [ ] All EXIF metadata visible in Metadata section
- [ ] AI description section displays correctly (handles loading/empty/error states)
- [ ] Actions section shows placeholder buttons (functionality in Phase 4)
- [ ] Keyboard shortcuts section present (collapsed)
- [ ] I key toggles panel
- [ ] ESC closes panel, then lightbox on second press
- [ ] No console errors
- [ ] No visual regressions in photo display
- [ ] Responsive breakpoints work correctly:
  - Desktop: Side panel (380px)
  - Tablet: Overlay (320px) with backdrop
  - Mobile: Bottom sheet (70vh) with drag handle

---

## Rollback Strategy

If Phase 1 fails, revert to original:

```bash
git checkout HEAD -- samples/S6.SnapVault/wwwroot/js/components/lightbox.js
git clean -f samples/S6.SnapVault/wwwroot/js/components/lightboxPanel.js
git clean -f samples/S6.SnapVault/wwwroot/css/lightbox*.css
```

---

## Next Steps

After Phase 1 is complete:
- **Phase 2:** Implement photo reflow when panel opens (desktop shift + scale)
- **Phase 3:** Add smart zoom functionality (click-to-cycle, pinch, scroll)

See: [Phase 2 Documentation](./LIGHTBOX_PHASE_2.md)
