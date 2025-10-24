# Meridian UX Realignment - Quick Reference

**Status:** 🎨 Ready for Implementation  
**Full Proposal:** See `MERIDIAN-UX-REALIGNMENT.md`

---

## 🎯 Core Problem

Meridian's navigation is **disorganized and inconsistent**:

- ❌ Analyses, Analysis Types, and Source Types use different patterns
- ❌ Top nav treats all items equally (no hierarchy)
- ❌ Full-page navigation jumps break context
- ❌ No clear distinction between "work" and "configuration"

## 💡 Solution Overview

### Three-Part System

```
┌─────────────────────────────────────────────┐
│ [Logo]              [Settings] [Profile]    │ Header
├──────────┬──────────────────────────────────┤
│          │                                   │
│ LIBRARY  │  Main Content Area               │
│ • All    │  • Dashboard / Lists / Workspace │
│ • Fav    │                                   │
│          │                                   │
│ WORK     │                                   │
│ • Active │                                   │
│          │                                   │
│ CONFIG   │                                   │
│ • ATypes │                                   │
│ • STypes │                                   │
│          │                                   │
└──────────┴──────────────────────────────────┘
  Sidebar    Content (Consistent Patterns)
```

### Key Changes

1. **Borderless Sidebar** (SnapVault pattern)

   - Clean, unboxed sections
   - Blue left-border for active items
   - 11px uppercase section headers
   - 32px gaps between sections

2. **Detail Panels** (not full-page navigation)

   - Slide in from right (60% width)
   - Maintains list context behind
   - Edit mode in same panel
   - Keyboard-friendly (Escape to close)

3. **Consistent Patterns**
   - All entity types use: List → Panel → Edit
   - Exception: Analyses use workspace (justified)

---

## 🎨 Visual Design (from SnapVault)

### Colors

```css
--color-canvas: #0a0a0a; /* Background */
--color-surface: #141414; /* Cards, panels */
--color-accent-primary: #5b9fff; /* Blue for actions */
--color-text-primary: #e8e8e8; /* High contrast */
```

### Typography

```css
--text-xs: 11px; /* Section headers (uppercase) */
--text-sm: 13px; /* Metadata */
--text-base: 15px; /* Body text */
--text-lg: 18px; /* Card titles */
```

### Spacing (8px grid)

```css
--space-2: 16px; /* Standard gaps */
--space-3: 24px; /* Section padding */
--space-4: 32px; /* Sidebar section gaps */
```

---

## ✅ Quick Implementation Guide

### Phase 1: Foundation (Week 1)

- [ ] Copy SnapVault design tokens
- [ ] Build borderless sidebar component
- [ ] Create detail panel component

### Phase 2: Lists (Week 2)

- [ ] Standardize Analysis Types (card grid → panel)
- [ ] Standardize Source Types (same pattern)
- [ ] Update Analyses list (keep workspace pattern)

### Phase 3: Integration (Week 3)

- [ ] Update routing (`/configuration/*` paths)
- [ ] Add breadcrumbs
- [ ] Implement keyboard shortcuts

### Phase 4: Polish (Week 4)

- [ ] Accessibility audit
- [ ] Responsive design
- [ ] Animation polish

---

## 📐 Component Specs

### Sidebar Item

```css
.sidebar-item {
  padding: 10px 12px;
  border-left: 2px solid transparent;
  font-size: 14px;
  transition: all 0.15s ease;
}

.sidebar-item.active {
  border-left-color: #5b9fff;
  background: rgba(91, 159, 255, 0.08);
}
```

### Detail Panel

```css
.detail-panel {
  position: fixed;
  right: 0;
  width: 60%;
  height: 100vh;
  background: #141414;
  transform: translateX(100%);
  transition: transform 0.3s ease;
}

.detail-panel.open {
  transform: translateX(0);
}
```

### Entity Card

```css
.entity-card {
  background: #141414;
  border: 1px solid #2a2a2a;
  border-radius: 8px;
  padding: 20px;
  transition: all 0.15s ease;
}

.entity-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}
```

---

## 🎯 Expected Outcomes

### User Experience

- ✅ 30% faster task completion
- ✅ 50% fewer navigation errors
- ✅ >4.5/5 user satisfaction

### Technical

- ✅ All entity types use same components
- ✅ No bundle size increase
- ✅ WCAG 2.1 AA compliant
- ✅ 60fps animations

---

## 📚 Key Files to Reference

### From SnapVault

- `S6.SnapVault/DESIGN_SYSTEM.md` - Complete design documentation
- `wwwroot/css/design-tokens.css` - Color/typography system
- `wwwroot/css/sidebar-redesign.css` - Borderless sidebar pattern
- `wwwroot/css/lightbox-panel.css` - Panel slide-in animation

### Meridian Docs

- `docs/UX-SPECIFICATION.md` - Evidence-first workspace paradigm
- `docs/MERIDIAN-UX-REALIGNMENT.md` - Full proposal (this implementation)

---

## 🚀 Getting Started

1. **Read Full Proposal**: `MERIDIAN-UX-REALIGNMENT.md`
2. **Review SnapVault Design System**: `../../S6.SnapVault/DESIGN_SYSTEM.md`
3. **Start with Sidebar**: Build the navigation foundation first
4. **Then Detail Panels**: Implement the consistent list→panel pattern
5. **Test & Iterate**: Gather feedback at each phase

---

**Questions?** Review the full proposal or reach out to the design team.
