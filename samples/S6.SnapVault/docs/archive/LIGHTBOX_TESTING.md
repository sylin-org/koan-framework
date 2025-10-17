# Lightbox Testing & Troubleshooting Guide

**Purpose:** Comprehensive testing strategies, common issues, and troubleshooting for the lightbox redesign

---

## Quick Testing Checklist

### Per-Phase Testing

After completing each phase, run these quick tests:

```javascript
// Open browser console (F12)
// Test lightbox opens
app.components.lightbox.open(app.photos[0].id);

// Test phase-specific features
// Phase 1: Panel toggle
app.components.lightbox.panel.toggle();

// Phase 2: Photo reflow
// Resize browser window and toggle panel

// Phase 3: Zoom
// Click photo, scroll wheel, pinch (mobile)

// Phase 4: Actions
// Click favorite, rating, download, delete

// Phase 5: Keyboard
// Press shortcuts: I, Z, S, D, ?, ESC

// Phase 6: Accessibility
// Tab through elements, test with screen reader

// Phase 7: Edge cases
// Test with slow network, large photos, missing data
```

---

## Testing Strategies

### 1. Browser Testing

**Desktop Browsers:**

| Browser | Version | Priority | Test Focus |
|---------|---------|----------|------------|
| Chrome | Latest | High | Performance, DevTools profiling |
| Firefox | Latest | High | Keyboard shortcuts, focus management |
| Safari | Latest | High | Animations, webkit prefixes |
| Edge | Latest | Medium | Focus management, accessibility |

**Testing Process:**
```javascript
// 1. Open lightbox
app.components.lightbox.open(app.photos[0].id);

// 2. Test core features
// - Panel toggle ✓
// - Navigation (arrows, keyboard) ✓
// - Zoom (click, scroll, pinch) ✓
// - Actions (favorite, rate, download) ✓

// 3. Check console for errors
// - No errors ✓
// - No warnings ✓

// 4. Test responsive behavior
// - Desktop (>1200px) ✓
// - Tablet (768-1200px) ✓
// - Mobile (<768px) ✓
```

---

### 2. Device Testing

**Mobile Devices:**

| Device | OS | Browser | Test Focus |
|--------|-----|---------|------------|
| iPhone 13 | iOS 16+ | Safari | Pinch zoom, bottom sheet, touch targets |
| iPhone SE | iOS 15+ | Safari | Small screen compatibility |
| Pixel 6 | Android 12+ | Chrome | Pinch zoom, bottom sheet |
| Samsung Galaxy | Android 11+ | Chrome/Samsung Internet | Touch interactions |
| iPad Pro | iOS 16+ | Safari | Tablet overlay mode, hover states |

**Mobile Testing Checklist:**
- [ ] Bottom sheet slides up smoothly
- [ ] Drag handle dismisses sheet (>50% threshold)
- [ ] Pinch zoom feels natural
- [ ] Touch targets comfortable (56px on mobile)
- [ ] No overscroll/rubber-band issues
- [ ] Swipe navigation works
- [ ] Photo dims when panel opens

**Desktop Testing Checklist:**
- [ ] Side panel slides in from right
- [ ] Photo shifts left and scales down
- [ ] Animations smooth (60fps)
- [ ] Scroll wheel zoom works
- [ ] All keyboard shortcuts work

---

### 3. Accessibility Testing

**Screen Readers:**

| Screen Reader | OS | Browser | Priority |
|---------------|-----|---------|----------|
| NVDA | Windows | Firefox | High |
| JAWS | Windows | Chrome | Medium |
| VoiceOver | macOS | Safari | High |
| VoiceOver | iOS | Safari | High |

**NVDA Testing (Windows):**

```plaintext
1. Download NVDA: https://www.nvaccess.org/
2. Install and start (Ctrl+Alt+N)
3. Open S6.SnapVault in Firefox
4. Open lightbox

Expected Announcements:
- "Photo Viewer dialog"
- "Viewing photo 1 of 10"
- All buttons announce labels
- Photo navigation announces new photo
- Panel state changes announced
```

**VoiceOver Testing (macOS):**

```plaintext
1. Enable VoiceOver: Cmd+F5
2. Open S6.SnapVault in Safari
3. Open lightbox

Expected Behavior:
- Same as NVDA
- Verify all ARIA labels work
- Test focus management
```

**Automated Accessibility Testing:**

```javascript
// Install axe DevTools extension
// https://www.deque.com/axe/devtools/

// Run in console:
axe.run((err, results) => {
  console.log('Violations:', results.violations);
  console.log('Passes:', results.passes.length);
});

// Expected: 0 violations
```

**WCAG Compliance Checklist:**
- [ ] All interactive elements have ARIA labels
- [ ] Focus management works (trap, restore)
- [ ] Screen reader announcements clear
- [ ] Color contrast meets 7:1 (WCAG AAA)
- [ ] Touch targets ≥44px (desktop), ≥56px (mobile)
- [ ] Keyboard navigation complete
- [ ] Reduced motion supported

---

### 4. Performance Testing

**Chrome DevTools Performance Panel:**

```javascript
// 1. Open Chrome DevTools (F12) → Performance
// 2. Click Record
// 3. Perform actions:
app.components.lightbox.open(app.photos[0].id); // Open lightbox
app.components.lightbox.panel.toggle(); // Toggle panel
// Zoom in/out, pan, navigate

// 4. Stop recording
// 5. Analyze:
// - FPS should be 60 (green bars)
// - No layout thrashing (purple bars)
// - Memory usage stable (<50MB)
```

**Performance Budget:**

| Metric | Target | How to Measure |
|--------|--------|----------------|
| Lightbox open | <300ms | DevTools Performance: Time from click to photo visible |
| Panel toggle | <300ms | DevTools Performance: Time from click to panel visible |
| Zoom transition | <300ms | DevTools Performance: Time from click to zoom complete |
| Photo reflow | <300ms | DevTools Performance: Time from panel open to reflow complete |
| All animations | 60fps | DevTools Performance: FPS counter (green = good) |
| Memory usage | <50MB | DevTools Memory: Take heap snapshot |

**Memory Leak Testing:**

```javascript
// 1. Open DevTools → Memory
// 2. Take heap snapshot (Baseline)
// 3. Open/close lightbox 20 times:
for (let i = 0; i < 20; i++) {
  app.components.lightbox.open(app.photos[i % app.photos.length].id);
  await new Promise(r => setTimeout(r, 500));
  app.components.lightbox.close();
  await new Promise(r => setTimeout(r, 500));
}
// 4. Take heap snapshot (After)
// 5. Compare snapshots:
// - Memory growth should be minimal (<5MB)
// - No detached DOM nodes
```

---

## Common Issues & Solutions

### Issue 1: Panel Doesn't Slide In

**Symptoms:**
- Panel appears instantly or doesn't appear at all
- No smooth animation

**Causes & Solutions:**

1. **Missing CSS transition:**
   ```css
   /* Add to lightbox-panel.css */
   .info-panel {
     transition: transform 300ms cubic-bezier(0.4, 0, 0.2, 1);
   }
   ```

2. **Transform not applied:**
   ```javascript
   // Check in browser console
   console.log(document.querySelector('.info-panel').style.transform);
   // Should be: translateX(0) or translateY(0) when open
   ```

3. **`will-change` interfering:**
   ```css
   /* Remove will-change after animation */
   .info-panel.animation-complete {
     will-change: auto;
   }
   ```

---

### Issue 2: Photo Doesn't Reflow on Desktop

**Symptoms:**
- Photo stays centered when panel opens
- Panel overlays photo

**Causes & Solutions:**

1. **Layout calculation not called:**
   ```javascript
   // In lightboxPanel.js open()
   open() {
     this.isOpen = true;
     this.container.classList.add('open');

     // ADD THIS:
     this.lightbox.applyPhotoLayout({ open: true });
   }
   ```

2. **Viewport width check:**
   ```javascript
   // Verify desktop breakpoint
   console.log(window.innerWidth); // Should be >1200 for desktop
   ```

3. **Photo element reference missing:**
   ```javascript
   // In lightbox.js open()
   this.photoElement = this.container.querySelector('.lightbox-photo');
   ```

---

### Issue 3: Zoom Not Working

**Symptoms:**
- Clicking photo does nothing
- Scroll wheel doesn't zoom
- Pinch doesn't work

**Causes & Solutions:**

1. **Event listeners not attached:**
   ```javascript
   // In lightbox.js setupZoomListeners()
   this.photoElement.addEventListener('click', () => {
     this.zoom.cycle();
   });
   ```

2. **Zoom badge not showing:**
   ```javascript
   // Check badge exists
   console.log(document.querySelector('.zoom-badge'));
   // Verify CSS
   .zoom-badge.visible { opacity: 1; }
   ```

3. **Transform not applied:**
   ```javascript
   // In lightboxZoom.js apply()
   photo.style.transform = `scale(${this.currentScale})`;
   ```

---

### Issue 4: Keyboard Shortcuts Not Working

**Symptoms:**
- Pressing keys does nothing
- Some shortcuts work, others don't

**Causes & Solutions:**

1. **Keyboard not enabled:**
   ```javascript
   // In lightbox.js open()
   this.keyboard.enable();
   ```

2. **Event listener check:**
   ```javascript
   // Verify key normalization
   console.log(this.keyboard.normalizeKey('ArrowLeft')); // Should be 'left'
   ```

3. **Input focus stealing shortcuts:**
   ```javascript
   // In lightboxKeyboard.js handleKeyDown
   if (event.target.matches('input, textarea, select')) {
     return; // Don't trigger shortcuts
   }
   ```

---

### Issue 5: Actions Not Updating Photo

**Symptoms:**
- Clicking favorite/rating doesn't update
- API call succeeds but UI doesn't change

**Causes & Solutions:**

1. **Photo reference not set:**
   ```javascript
   // In lightbox.js open()
   this.actions.setPhoto(photo);
   ```

2. **UI update method not called:**
   ```javascript
   // In lightboxActions.js toggleFavorite()
   this.currentPhoto.isFavorite = data.isFavorite;
   this.updateFavoriteButton(data.isFavorite); // ADD THIS
   ```

3. **Event listener not attached:**
   ```javascript
   // In lightboxPanel.js attachActionListeners()
   const btnFavorite = document.getElementById('btn-favorite');
   btnFavorite?.addEventListener('click', () => actions.toggleFavorite());
   ```

---

### Issue 6: Screen Reader Not Announcing

**Symptoms:**
- Screen reader silent when opening lightbox
- Photo changes not announced

**Causes & Solutions:**

1. **Live region missing:**
   ```javascript
   // In AnnouncementManager createLiveRegion()
   const region = document.createElement('div');
   region.className = 'sr-only';
   region.setAttribute('aria-live', 'polite');
   region.setAttribute('aria-atomic', 'true');
   document.body.appendChild(region);
   ```

2. **ARIA labels missing:**
   ```html
   <!-- Add to lightbox -->
   <div role="dialog" aria-modal="true" aria-labelledby="lightbox-title">
     <h1 id="lightbox-title" class="sr-only">Photo Viewer</h1>
   </div>
   ```

3. **Announcements not called:**
   ```javascript
   // In lightbox.js open()
   this.announcer.announcePhotoChange(this.currentIndex, total, filename);
   ```

---

### Issue 7: Mobile Bottom Sheet Not Dismissing

**Symptoms:**
- Dragging handle doesn't dismiss sheet
- Sheet snaps back even after dragging >50%

**Causes & Solutions:**

1. **Touch events not attached:**
   ```javascript
   // In lightboxPanel.js setupMobileGestures()
   const handle = this.container.querySelector('.drag-handle');
   handle.addEventListener('touchstart', ...);
   ```

2. **Threshold calculation wrong:**
   ```javascript
   // Verify threshold
   const dismissThreshold = panelHeight * 0.5; // 50%
   if (deltaY > dismissThreshold) {
     this.close();
   }
   ```

3. **Transform not cleared:**
   ```javascript
   // In touchend handler
   this.container.style.transition = '';
   this.container.style.transform = ''; // Snap back
   ```

---

### Issue 8: AI Regeneration Timeout

**Symptoms:**
- AI regeneration never completes
- Button stays in loading state

**Causes & Solutions:**

1. **Polling not working:**
   ```javascript
   // Add timeout and logging
   const poll = async () => {
     console.log('Polling for AI update...');
     if (Date.now() - startTime > 60000) {
       throw new Error('Timed out after 60 seconds');
     }
     // ... polling logic
   };
   ```

2. **Comparison logic:**
   ```javascript
   // Verify description changed
   if (updatedPhoto.detailedDescription !== this.currentPhoto.detailedDescription) {
     console.log('AI description updated!');
     // Success
   }
   ```

3. **Add cancel button:**
   ```javascript
   // Allow user to cancel
   const cancelBtn = document.createElement('button');
   cancelBtn.textContent = 'Cancel';
   cancelBtn.onclick = () => { cancelled = true; };
   ```

---

## Debugging Tools

### Console Logging

```javascript
// Enable debug mode
window.LIGHTBOX_DEBUG = true;

// In lightbox.js
if (window.LIGHTBOX_DEBUG) {
  console.log('Lightbox opened:', photoId);
  console.log('Current layout:', this.currentLayout);
  console.log('Zoom state:', this.zoom.mode, this.zoom.currentScale);
}
```

### Visual Debugging

```css
/* Add to lightbox.css for debugging */
.debug-mode .info-panel {
  outline: 2px solid red;
}

.debug-mode .lightbox-photo {
  outline: 2px solid blue;
}

.debug-mode .lightbox-content {
  outline: 2px solid green;
}
```

### Performance Monitoring

```javascript
// Add to lightbox.js
performance.mark('lightbox-open-start');
// ... open logic ...
performance.mark('lightbox-open-end');
performance.measure('lightbox-open', 'lightbox-open-start', 'lightbox-open-end');

// View results
console.table(performance.getEntriesByType('measure'));
```

---

## Regression Testing

After making changes, run this comprehensive test:

```javascript
// 1. Open lightbox
app.components.lightbox.open(app.photos[0].id);
// ✓ Photo displays
// ✓ No console errors

// 2. Toggle panel
app.components.lightbox.panel.toggle();
// ✓ Panel slides in
// ✓ Photo reflows (desktop)
// ✓ Animation smooth

// 3. Test zoom
// Click photo 3 times: Fit → Fill → 100% → Fit
// ✓ Zoom cycles correctly
// ✓ Badge shows/hides appropriately

// 4. Test actions
// Click favorite
// ✓ Icon changes
// ✓ API called
// ✓ Toast shows

// 5. Test keyboard
// Press: I, Z, S, ←, →, ESC
// ✓ All shortcuts work

// 6. Test navigation
app.components.lightbox.next();
// ✓ Next photo loads
// ✓ Panel updates with new photo data

// 7. Close lightbox
app.components.lightbox.close();
// ✓ Lightbox closes
// ✓ Focus restored
// ✓ No console errors
```

---

## Continuous Integration

### Automated Tests (Future)

```javascript
// Example Playwright test
test('lightbox opens and closes', async ({ page }) => {
  await page.goto('http://localhost:5000');

  // Click first photo
  await page.click('.gallery-item:first-child');

  // Verify lightbox visible
  await expect(page.locator('.lightbox-overlay')).toBeVisible();

  // Close lightbox
  await page.keyboard.press('Escape');

  // Verify lightbox hidden
  await expect(page.locator('.lightbox-overlay')).not.toBeVisible();
});
```

---

## Getting Help

**Before asking for help:**
1. Check this document for common issues
2. Check browser console for errors
3. Verify phase completion checklist
4. Test in different browser
5. Check git status for unexpected changes

**When reporting issues:**
```markdown
## Issue Description
Brief description of the problem

## Steps to Reproduce
1. Open lightbox
2. Click X button
3. Observe Y behavior

## Expected Behavior
What should happen

## Actual Behavior
What actually happens

## Environment
- Browser: Chrome 120
- OS: Windows 11
- Screen size: 1920x1080
- Phase: Phase 3 (Smart Zoom)

## Console Errors
[Paste any console errors]

## Screenshots
[If applicable]
```

---

**Back to:** [Overview](./LIGHTBOX_OVERVIEW.md) | [Phase 1](./LIGHTBOX_PHASE_1.md)
