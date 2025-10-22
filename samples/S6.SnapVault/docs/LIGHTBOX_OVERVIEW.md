# Lightbox Redesign - Overview

**Status:** Implementation Ready
**Target:** S6.SnapVault Sample Application
**Purpose:** Agent-friendly documentation for autonomous implementation

---

## Quick Start

**Current Phase:** Choose a phase to begin implementation:
- 📍 [Phase 1: Foundation](./LIGHTBOX_PHASE_1.md) - Start here
- [Phase 2: Photo Reflow](./LIGHTBOX_PHASE_2.md)
- [Phase 3: Smart Zoom](./LIGHTBOX_PHASE_3.md)
- [Phase 4: Actions Migration](./LIGHTBOX_PHASE_4.md)
- [Phase 5: Keyboard Shortcuts](./LIGHTBOX_PHASE_5.md)
- [Phase 6: Accessibility](./LIGHTBOX_PHASE_6.md)
- [Phase 7: Polish & Edge Cases](./LIGHTBOX_PHASE_7.md)

**Supporting Documents:**
- [Technical Reference](./LIGHTBOX_TECHNICAL_REFERENCE.md) - API, data structures, current architecture
- [Testing Guide](./LIGHTBOX_TESTING.md) - Verification, troubleshooting, common issues

---

## Design Vision

### Problem Statement

**Current Lightbox Issues:**
- Two separate toggle buttons (Info/AI) - confusing UX
- Panels slide over photo content - obscures image
- No zoom functionality - can't examine details
- Limited keyboard support - accessibility concerns
- Information scattered - poor progressive disclosure

### Solution: Unified Info Panel

**Consolidate all non-navigational functionality into a single panel:**
- ✅ Single "Info" toggle (I key or button)
- ✅ Contains: Metadata, AI Insights, Actions, Shortcuts
- ✅ Responsive: Side panel (desktop), Overlay (tablet), Bottom sheet (mobile)
- ✅ Photo reflows: Image scales smoothly when panel opens
- ✅ Zoom system: Pinch, scroll, click to zoom on photo
- ✅ Full keyboard: Navigate, zoom, actions via keyboard
- ✅ WCAG AAA: Screen reader support, focus management, ARIA

---

## Design Principles

### 1. **Progressive Disclosure**
- Show photo first, controls on hover/tap
- Info panel closed by default
- Expand sections only when needed

### 2. **Non-Destructive Reflow**
- Photo scales gracefully when panel opens
- Maintains aspect ratio
- Smooth animation (300ms ease-out)

### 3. **Gesture Parity**
- Desktop: Click, wheel, keyboard
- Touch: Pinch, pan, swipe, tap
- Consistent behavior across devices

### 4. **Accessibility First**
- WCAG AAA compliance
- Full keyboard navigation
- Screen reader optimization
- Reduced motion support

### 5. **Performance**
- GPU-accelerated transforms
- 60fps animations
- Lazy load panel content
- <100ms interaction response

---

## Component Architecture

### Current State (Before Redesign)

```
samples/S6.SnapVault/wwwroot/
├── js/components/
│   └── lightbox.js          ← Single file, ~500 lines
└── css/
    └── app.css              ← Lightbox styles mixed in
```

**Current Features:**
- Modal photo viewer
- Left/right navigation
- Two toggle panels (Info/AI) - slides from right
- ESC to close
- No zoom, minimal keyboard support

### Target State (After Redesign)

```
samples/S6.SnapVault/wwwroot/
├── js/components/
│   ├── lightbox.js          ← Core lightbox (refactored)
│   ├── lightboxPanel.js     ← NEW: Unified info panel
│   ├── lightboxZoom.js      ← NEW: Zoom functionality
│   ├── lightboxActions.js   ← NEW: Photo actions
│   └── lightboxKeyboard.js  ← NEW: Keyboard shortcuts
└── css/
    ├── lightbox.css         ← NEW: Core lightbox styles
    ├── lightbox-panel.css   ← NEW: Panel styles
    ├── lightbox-zoom.css    ← NEW: Zoom UI styles
    └── lightbox-responsive.css ← NEW: Breakpoints
```

### Module Responsibilities

#### `lightbox.js` (Core)
- Photo display and navigation
- Open/close modal
- Integration with other modules
- Event coordination

#### `lightboxPanel.js` (Panel System)
- Unified info panel UI
- 4 sections: Metadata, AI Insights, Actions, Shortcuts
- Responsive modes: Side panel → Overlay → Bottom sheet
- Keyboard toggle (I key)

#### `lightboxZoom.js` (Zoom System)
- Pinch zoom (touch)
- Scroll zoom (mouse)
- Click to zoom (3 levels: Fit → 100% → 200%)
- Pan when zoomed
- Zoom UI overlay

#### `lightboxActions.js` (Actions)
- Favorite toggle
- Star rating
- Download
- Delete
- Regenerate AI
- Bulk selection (future)

#### `lightboxKeyboard.js` (Keyboard)
- Shortcut manager
- Help overlay
- Key binding system
- Accessibility shortcuts

---

## Responsive Behavior

### Desktop (>1200px)
- **Panel:** Side panel, 380px width, pushes photo left
- **Photo:** Reflows to remaining space
- **Zoom:** Scroll to zoom, drag to pan
- **Navigation:** Arrow keys, mouse click

### Tablet (768-1200px)
- **Panel:** Overlay, 320px width, backdrop dim
- **Photo:** Stays full viewport
- **Zoom:** Pinch to zoom, two-finger pan
- **Navigation:** Swipe left/right

### Mobile (<768px)
- **Panel:** Bottom sheet, 70vh height, drag handle
- **Photo:** Above bottom sheet
- **Zoom:** Pinch to zoom, drag to pan
- **Navigation:** Swipe left/right, tap edges

---

## Implementation Timeline

### Estimated Total: 90-112 hours (11-14 days)

| Phase | Duration | Dependencies | Deliverables |
|-------|----------|--------------|--------------|
| **1. Foundation** | 16-20h | None | Unified panel, basic UI |
| **2. Photo Reflow** | 12-16h | Phase 1 | Dynamic photo resizing |
| **3. Smart Zoom** | 20-24h | Phase 2 | Zoom system complete |
| **4. Actions** | 8-12h | Phase 1 | All photo actions |
| **5. Keyboard** | 10-14h | Phase 1, 3, 4 | Full keyboard support |
| **6. Accessibility** | 12-16h | Phase 5 | WCAG AAA compliance |
| **7. Polish** | 12-16h | Phase 6 | Edge cases, animations |

### Parallelization Options
- **Phase 3 & 4** can run in parallel (both depend on Phase 1)
- **Phase 5** requires Phase 3 & 4 complete
- **Phase 6 & 7** must be sequential

---

## Success Metrics

### Functional Requirements
- [ ] Single unified info panel
- [ ] Photo reflows when panel opens
- [ ] 3-level zoom (Fit → 100% → 200%)
- [ ] Pinch/scroll zoom works smoothly
- [ ] All photo actions accessible from panel
- [ ] Full keyboard navigation
- [ ] Responsive on desktop/tablet/mobile

### Performance Requirements
- [ ] Panel open/close: <300ms
- [ ] Photo reflow: <300ms (smooth)
- [ ] Zoom interaction: <100ms response
- [ ] 60fps during all animations
- [ ] No layout thrashing

### Accessibility Requirements
- [ ] WCAG AAA compliance
- [ ] Screen reader announces all actions
- [ ] Focus trap in lightbox
- [ ] All controls keyboard accessible
- [ ] Reduced motion support
- [ ] Color contrast: 7:1 minimum

---

## Risk Mitigation

### High-Risk Areas
1. **Photo Reflow Complexity** - Solution: Phase 2 focuses solely on this
2. **Touch Gesture Conflicts** - Solution: Comprehensive gesture testing in Phase 3
3. **Browser Compatibility** - Solution: Progressive enhancement, fallbacks
4. **Performance on Low-End Devices** - Solution: GPU acceleration, debouncing

### Rollback Strategy
Each phase includes git rollback commands. If any phase fails:
```bash
# Revert to previous phase
git checkout HEAD -- samples/S6.SnapVault/wwwroot/
```

### Testing Strategy
- Browser testing: Chrome, Firefox, Safari, Edge
- Device testing: Desktop, tablet, mobile (iOS/Android)
- Screen reader testing: NVDA (Windows), VoiceOver (macOS/iOS)
- Performance profiling: Chrome DevTools Performance panel

---

## Next Steps

1. **Read:** [Technical Reference](./LIGHTBOX_TECHNICAL_REFERENCE.md) to understand current architecture
2. **Start:** [Phase 1 Implementation](./LIGHTBOX_PHASE_1.md) to create unified panel
3. **Test:** Use [Testing Guide](./LIGHTBOX_TESTING.md) for verification

---

**Questions or Issues?**
- Check [Testing Guide](./LIGHTBOX_TESTING.md) for common issues
- Review [Technical Reference](./LIGHTBOX_TECHNICAL_REFERENCE.md) for API details
- Each phase document includes rollback strategies
