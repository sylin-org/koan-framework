# Phase 7: Polish & Edge Cases - Production Readiness

**Duration:** 12-16 hours
**Dependencies:** All previous phases complete
**Goal:** Handle edge cases, optimize performance, ensure production readiness

---

## Context

Final polish to ensure the lightbox works flawlessly in all scenarios:
- Slow network handling (AI regeneration timeout)
- Large photos (>20MP) performance
- Corrupted/missing metadata graceful fallbacks
- Animation performance optimization
- Loading skeletons
- Error boundaries
- Browser compatibility testing
- Device testing

---

## Tasks

### 1. Test with Slow Network (AI Regeneration Timeout)

**Update `lightboxActions.js` regenerateAI:**

```javascript
async regenerateAI() {
  if (!this.currentPhoto) return;

  const btn = document.getElementById('btn-regenerate');
  if (!btn) return;

  // Show loading state
  btn.disabled = true;
  btn.classList.add('loading');
  btn.querySelector('.label').textContent = 'Regenerating...';

  try {
    const response = await fetch(`/api/photos/${this.currentPhoto.id}/regenerate-ai`, {
      method: 'POST'
    });

    if (!response.ok) throw new Error('Regenerate failed');

    // Poll for completion (max 60s)
    const startTime = Date.now();
    const pollInterval = 1000;
    const timeout = 60000; // 60 seconds

    let cancelled = false;

    // Add cancel button
    const cancelBtn = document.createElement('button');
    cancelBtn.textContent = 'Cancel';
    cancelBtn.className = 'btn-cancel-regenerate';
    cancelBtn.onclick = () => {
      cancelled = true;
      btn.disabled = false;
      btn.classList.remove('loading');
      btn.querySelector('.label').textContent = 'Regenerate Description';
      cancelBtn.remove();
    };
    btn.parentElement.appendChild(cancelBtn);

    const poll = async () => {
      if (cancelled) return;

      if (Date.now() - startTime > timeout) {
        // Timeout
        throw new Error('Regeneration timed out after 60 seconds. The server may be busy. Please try again later.');
      }

      // Fetch updated photo data
      const photoResponse = await fetch(`/api/photos/${this.currentPhoto.id}`);
      const updatedPhoto = await photoResponse.json();

      // Check if AI description is updated
      if (updatedPhoto.detailedDescription &&
          updatedPhoto.detailedDescription !== this.currentPhoto.detailedDescription) {
        // Success
        this.currentPhoto = updatedPhoto;
        this.lightbox.panel.renderAIInsights(updatedPhoto);

        btn.disabled = false;
        btn.classList.remove('loading');
        btn.querySelector('.label').textContent = 'Regenerate Description';
        cancelBtn.remove();

        this.app.components.toast.show('AI description regenerated', {
          icon: '✓',
          duration: 3000
        });
      } else {
        // Still processing, poll again
        setTimeout(poll, pollInterval);
      }
    };

    poll();
  } catch (error) {
    console.error('Failed to regenerate AI:', error);

    btn.disabled = false;
    btn.classList.remove('loading');
    btn.querySelector('.label').textContent = 'Try Again';

    this.app.components.toast.show(
      error.message || 'Failed to regenerate AI description',
      { icon: '❌', duration: 5000 }
    );
  }
}
```

---

### 2. Test with Large Photos (>20MP) - Performance Optimization

**Add to `lightbox.js`:**

```javascript
async open(photoId) {
  // ... existing open logic ...

  // Use gallery image (1920px) instead of full-res for display
  const imageUrl = `/storage/${photo.galleryMediaKey}`;
  this.photoElement.src = imageUrl;

  // Show loading skeleton while image loads
  this.showLoadingSkeleton();

  this.photoElement.onload = () => {
    this.hideLoadingSkeleton();
    // ... rest of open logic ...
  };

  this.photoElement.onerror = () => {
    this.hideLoadingSkeleton();
    this.showPhotoError();
  };
}

showLoadingSkeleton() {
  const skeleton = document.createElement('div');
  skeleton.className = 'photo-loading-skeleton';
  skeleton.innerHTML = `
    <div class="skeleton-shimmer"></div>
  `;
  this.container.querySelector('.lightbox-content').appendChild(skeleton);
}

hideLoadingSkeleton() {
  const skeleton = this.container.querySelector('.photo-loading-skeleton');
  skeleton?.remove();
}

showPhotoError() {
  const error = document.createElement('div');
  error.className = 'photo-error';
  error.innerHTML = `
    <svg><!-- Error icon --></svg>
    <p>Failed to load photo</p>
    <button class="btn-retry">Retry</button>
  `;
  this.container.querySelector('.lightbox-content').appendChild(error);

  error.querySelector('.btn-retry').addEventListener('click', () => {
    this.open(this.currentPhotoId);
  });
}
```

**Add skeleton CSS:**

```css
/* Loading Skeleton */
.photo-loading-skeleton {
  position: absolute;
  inset: 0;
  background: rgba(255, 255, 255, 0.05);
  display: flex;
  align-items: center;
  justify-content: center;
}

.skeleton-shimmer {
  width: 200px;
  height: 200px;
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.0) 0%,
    rgba(255, 255, 255, 0.1) 50%,
    rgba(255, 255, 255, 0.0) 100%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
}

@keyframes shimmer {
  0% { background-position: -200% 0; }
  100% { background-position: 200% 0; }
}

/* Photo Error */
.photo-error {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  color: rgba(255, 255, 255, 0.7);
}

.photo-error svg {
  width: 64px;
  height: 64px;
  opacity: 0.5;
}

.photo-error p {
  font-size: 16px;
  margin: 0;
}
```

---

### 3. Handle Missing/Corrupted Metadata

**Update `lightboxPanel.js`:**

```javascript
renderMetadata(photo) {
  const metadataSection = this.container.querySelector('#metadata-section');
  if (!metadataSection) return;

  // Graceful fallbacks for missing data
  const camera = photo.cameraModel || 'Unknown camera';
  const lens = photo.lensModel || 'Unknown lens';
  const aperture = photo.aperture || '?';
  const shutter = photo.shutterSpeed || '?';
  const iso = photo.iso || '?';
  const settings = `${aperture} • ${shutter} • ISO ${iso}`;
  const date = photo.capturedAt
    ? new Date(photo.capturedAt).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric'
      })
    : 'Unknown date';

  metadataSection.innerHTML = `
    <h3>Details</h3>
    <div class="metadata-grid">
      <div class="metadata-item">
        <span class="label">Camera</span>
        <span class="value">${this.escapeHtml(camera)}</span>
      </div>
      <div class="metadata-item">
        <span class="label">Lens</span>
        <span class="value">${this.escapeHtml(lens)}</span>
      </div>
      <div class="metadata-item">
        <span class="label">Settings</span>
        <span class="value">${settings}</span>
      </div>
      <div class="metadata-item">
        <span class="label">Captured</span>
        <span class="value">${date}</span>
      </div>
    </div>
  `;
}

renderAIInsights(photo) {
  const aiSection = this.container.querySelector('#ai-section');
  if (!aiSection) return;

  const hasDescription = photo.detailedDescription && photo.detailedDescription.trim().length > 0;
  const hasTags = photo.autoTags && photo.autoTags.length > 0;

  if (!hasDescription && !hasTags) {
    // No AI data
    aiSection.innerHTML = `
      <h3>AI Insights</h3>
      <div class="ai-content" data-state="empty">
        <p class="ai-empty-message">No AI description generated yet.</p>
        <button class="btn-regenerate-ai" id="btn-regenerate">
          <svg><!-- Regenerate icon --></svg>
          <span class="label">Generate Description</span>
        </button>
      </div>
    `;
  } else {
    // Has AI data
    aiSection.innerHTML = `
      <h3>AI Insights</h3>
      <div class="ai-content" data-state="loaded">
        ${hasDescription ? `
          <p class="ai-description">${this.escapeHtml(photo.detailedDescription)}</p>
        ` : ''}
        ${hasTags ? `
          <div class="ai-tags">
            ${photo.autoTags.map(tag => `
              <span class="ai-tag">${this.escapeHtml(tag)}</span>
            `).join('')}
          </div>
        ` : ''}
        <button class="btn-regenerate-ai" id="btn-regenerate">
          <svg><!-- Regenerate icon --></svg>
          <span class="label">Regenerate Description</span>
        </button>
      </div>
    `;
  }

  // Re-attach event listener
  const btnRegenerate = aiSection.querySelector('#btn-regenerate');
  btnRegenerate?.addEventListener('click', () => {
    this.lightbox.actions.regenerateAI();
  });
}

escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}
```

---

### 4. Optimize Animation Performance

**Update CSS with performance hints:**

```css
/* Performance Optimizations */
.info-panel,
.lightbox-photo,
.zoom-badge {
  /* Force GPU acceleration */
  will-change: transform;
  transform: translateZ(0);
}

/* Remove will-change after animation completes */
.info-panel.animation-complete,
.lightbox-photo.animation-complete {
  will-change: auto;
}
```

**Add animation completion handlers:**

```javascript
class LightboxPanel {
  open() {
    this.isOpen = true;
    this.container.classList.add('open');
    this.container.classList.remove('animation-complete');

    // Remove will-change after animation completes
    setTimeout(() => {
      this.container.classList.add('animation-complete');
    }, 300);
  }
}
```

**Debounce resize events:**

```javascript
class Lightbox {
  setupResizeHandler() {
    let resizeTimeout;
    let resizeFrame;

    window.addEventListener('resize', () => {
      // Debounce
      clearTimeout(resizeTimeout);

      // Use requestAnimationFrame for smooth updates
      if (resizeFrame) {
        cancelAnimationFrame(resizeFrame);
      }

      resizeTimeout = setTimeout(() => {
        resizeFrame = requestAnimationFrame(() => {
          if (this.isOpen && this.panel.isOpen) {
            this.applyPhotoLayout({ open: true });
          }
        });
      }, 150);
    });
  }
}
```

---

### 5. Add Error Boundaries

**Wrap critical operations:**

```javascript
class Lightbox {
  async open(photoId) {
    try {
      // ... existing open logic ...
    } catch (error) {
      console.error('Failed to open lightbox:', error);
      this.app.components.toast.show(
        'Failed to open photo. Please try again.',
        { icon: '❌', duration: 3000 }
      );
      this.close();
    }
  }
}

class LightboxPanel {
  render(photoData) {
    try {
      this.currentPhotoData = photoData;
      this.renderMetadata(photoData);
      this.renderAIInsights(photoData);
      this.renderActions(photoData);
    } catch (error) {
      console.error('Failed to render panel:', error);
      // Show error message in panel
      this.container.querySelector('.panel-content').innerHTML = `
        <div class="panel-error">
          <p>Failed to load photo information.</p>
          <button class="btn-retry-panel">Retry</button>
        </div>
      `;
    }
  }
}
```

---

### 6. Browser Compatibility Testing

**Test in all major browsers:**

```javascript
// Chrome (latest) - Primary target
// - Test all features ✓
// - Verify performance (60fps) ✓

// Firefox (latest)
// - Test especially keyboard shortcuts ✓
// - Verify focus management ✓

// Safari (latest)
// - Test animations (webkit prefixes) ✓
// - Verify backdrop-filter support ✓

// Edge (latest)
// - Test focus management ✓
// - Verify accessibility features ✓
```

**Add vendor prefixes if needed:**

```css
.info-panel {
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px); /* Safari */
}
```

---

### 7. Device Testing

**iOS Safari (iPhone):**
- Pinch zoom works ✓
- Bottom sheet drag works ✓
- Touch targets comfortable (56px) ✓
- No rubber-band scrolling issues ✓

**Android Chrome (Pixel/Samsung):**
- Pinch zoom works ✓
- Bottom sheet drag works ✓
- Touch targets comfortable ✓
- No overscroll issues ✓

**iPad Safari (Tablet):**
- Overlay mode works ✓
- Hover states work ✓
- Keyboard shortcuts (with external keyboard) ✓

**Android Tablet:**
- Overlay mode works ✓
- Touch interactions work ✓

---

### 8. Memory Management

**Add cleanup on close:**

```javascript
class Lightbox {
  close() {
    // Clean up event listeners
    this.cleanup();

    // ... existing close logic ...
  }

  cleanup() {
    // Remove zoom listeners
    this.photoElement?.removeEventListener('wheel', this.zoom.handleWheelZoom);
    this.photoElement?.removeEventListener('touchstart', this.handleTouchStart);

    // Clear references
    this.currentPhotoData = null;
    this.photoElement = null;

    // Dispose of sub-components
    this.zoom?.cleanup();
    this.panel?.cleanup();
  }
}

class LightboxZoom {
  cleanup() {
    // Clear pinch state
    this.pinchStartDistance = null;

    // Remove badge
    this.badge?.remove();
  }
}
```

---

## Verification Steps

```javascript
// Slow network test:
// 1. Open Chrome DevTools → Network → Throttling → Slow 3G
app.components.lightbox.open(app.photos[0].id);
app.components.lightbox.panel.open();
// 2. Click "Regenerate Description"
// - Spinner shows ✓
// - Cancel button appears ✓
// - Wait 60s
// - Timeout message shows ✓
// - Can try again ✓

// Large photo test:
// 1. Upload 6000×4000 photo (>20MP)
app.components.lightbox.open(largePhotoId);
// - Loading skeleton shows ✓
// - Image loads smoothly ✓
// - Zoom to 200%
// - Pan around
// - Chrome DevTools Performance shows 60fps ✓
// - Memory usage <50MB ✓

// Missing metadata test:
// 1. Mock photo data with null fields
const mockPhoto = {
  id: '123',
  cameraModel: null,
  lensModel: null,
  aperture: null,
  detailedDescription: null,
  autoTags: []
};
// 2. Open lightbox with mock photo
// - Panel shows "Unknown camera" ✓
// - Panel shows "No AI description yet" ✓
// - No console errors ✓

// Performance test:
// 1. Chrome DevTools → Performance
// 2. Record while:
//    - Opening lightbox
//    - Toggling panel
//    - Zooming
//    - Panning
// 3. Check results:
//    - All animations 60fps ✓
//    - No layout thrashing ✓
//    - Memory usage stable ✓

// Cross-browser test:
// Firefox: All features work ✓
// Safari: Animations smooth, webkit prefixes applied ✓
// Edge: Focus management works ✓

// Mobile device test:
// iPhone: Bottom sheet, pinch zoom, touch targets ✓
// Android: Same as iPhone ✓
// iPad: Overlay mode, hover states ✓
```

---

## Success Criteria

- [ ] No console errors under any scenario
- [ ] Graceful degradation for missing/corrupted data
- [ ] Smooth animations on mid-range devices (60fps)
- [ ] Works correctly on all major browsers:
  - [ ] Chrome (latest)
  - [ ] Firefox (latest)
  - [ ] Safari (latest)
  - [ ] Edge (latest)
- [ ] Works correctly on mobile devices:
  - [ ] iOS Safari (iPhone)
  - [ ] Android Chrome (Pixel/Samsung)
  - [ ] iPad Safari
  - [ ] Android Tablet
- [ ] Large photos (>20MP):
  - [ ] Load smoothly
  - [ ] Zoom smoothly
  - [ ] Memory usage <50MB
- [ ] Network timeout handling:
  - [ ] 60s timeout for AI regeneration
  - [ ] Cancel button works
  - [ ] Helpful error messages
- [ ] Error boundaries:
  - [ ] Panel errors don't crash lightbox
  - [ ] Photo load errors show retry option
  - [ ] API errors show toast notifications
- [ ] Memory management:
  - [ ] Event listeners cleaned up on close
  - [ ] References cleared
  - [ ] No memory leaks

---

## Performance Budget

| Metric | Target | Actual |
|--------|--------|--------|
| Lightbox open | <300ms | ___ |
| Panel toggle | <300ms | ___ |
| Zoom transition | <300ms | ___ |
| Photo reflow | <300ms | ___ |
| All animations | 60fps | ___ |
| Memory usage | <50MB | ___ |

**Verify with Chrome DevTools Performance panel.**

---

## Rollback Strategy

```bash
# If Phase 7 fails, revert all changes:
git checkout HEAD -- samples/S6.SnapVault/wwwroot/
```

---

## Final Checklist

Before marking complete:

- [ ] All 7 phases implemented
- [ ] All verification steps passed
- [ ] All success criteria met
- [ ] Tested in all major browsers
- [ ] Tested on mobile devices
- [ ] Performance budget met
- [ ] No console errors
- [ ] Accessibility tests passed (axe, WAVE)
- [ ] Code reviewed
- [ ] Documentation updated

---

## Deployment

After all phases complete:

```bash
# Run tests
npm test

# Build production assets
npm run build

# Deploy
./start.bat
```

---

**Congratulations! The lightbox redesign is complete. 🎉**

See: [Testing Guide](./LIGHTBOX_TESTING.md) for ongoing maintenance and troubleshooting
