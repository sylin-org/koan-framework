# Meridian UX Realignment Proposal

**Prepared by:** Senior UI/UX Designer  
**Date:** October 23, 2025  
**Status:** 🎨 PROPOSAL  
**Objective:** Create a cohesive, intuitive experience that aligns with Koan ethos and borrows proven UI patterns from SnapVault

---

## Executive Summary

The current Meridian interface suffers from **fragmented navigation** and **inconsistent access patterns** for lists and details (analyses, analysis types, source types). This creates an uneven, disjointed experience that fails to meet the standards of modern enterprise applications.

This proposal establishes a **unified UX system** inspired by SnapVault's elegant, borderless design while maintaining Meridian's unique evidence-driven workspace paradigm.

### Key Improvements

- ✅ **Unified sidebar navigation** (SnapVault-inspired borderless design)
- ✅ **Consistent list/detail patterns** across all entity types
- ✅ **Clear information hierarchy** (Library → Work → Settings)
- ✅ **Contextual workspace panels** instead of full-page navigation jumps
- ✅ **Accessible, keyboard-driven** interaction patterns
- ✅ **Professional dark theme** optimized for extended use

---

## 🔍 Current State: Identified Problems

### Problem 1: Disconnected Navigation Patterns

**Analyses** use a Dashboard → List → Workspace flow:

```
Dashboard ➔ "View Analyses" ➔ Full-page list ➔ Two-column workspace
```

**Analysis Types** use Dashboard → Cards → Full-page form:

```
Dashboard ➔ "Manage Analysis Types" ➔ Card grid ➔ Full-page form view
```

**Source Types** use the same pattern as Analysis Types

**Result:** Users must learn 3 different mental models for fundamentally similar operations (browse, view, edit entities).

### Problem 2: Unclear Hierarchy

All navigation items appear at equal weight in the top nav:

```
[Dashboard] [Analyses] [Analysis Types] [Source Types]
```

**Issues:**

- No distinction between **primary work** (Analyses) and **configuration** (Types)
- Settings and work areas mixed together
- No scalability (what happens with 10+ nav items?)
- Cognitive overload from too many top-level choices

### Problem 3: Inconsistent Detail Access

**Analyses:** Click card → Opens in same window with two-column layout  
**Analysis Types:** Click card → Navigate to full-page view with edit mode  
**Source Types:** Same as Analysis Types

**Result:** Unpredictable behavior. Users can't build muscle memory.

### Problem 4: Missing Context

- No breadcrumbs showing location in information hierarchy
- No visible relationship between Analyses and Analysis Types
- Back button behavior unclear
- No indication of "where am I" in the application

---

## 🎨 Design Philosophy: SnapVault Aesthetic + Meridian Intelligence

### SnapVault's Visual Excellence

SnapVault demonstrates professional-grade design through:

1. **Borderless Sidebar** - Clean, unboxed sections with subtle separations
2. **Consistent Typography Hierarchy** - Uppercase 11px section headers, 14px items
3. **Muted Color Palette** - Dark surfaces (#0A0A0A, #141414, #1A1A1A) with strategic blue accents
4. **Breathing Room** - Generous whitespace (32px section gaps, 10px item gaps)
5. **Progressive Disclosure** - Information revealed as needed, not all at once
6. **Keyboard-First** - Visible shortcuts, logical tab order

### Meridian's Unique Needs

While borrowing SnapVault's UI patterns, Meridian has distinct requirements:

1. **Evidence Transparency** - Must show document sources and confidence
2. **Living Workspace** - Analyses evolve continuously, not static
3. **Type Management** - Configuration is prominent (not afterthought)
4. **Multi-Entity Relationships** - Analyses ↔ Types ↔ Documents ↔ Insights

**Solution:** Adapt SnapVault's visual language to Meridian's information architecture.

---

## 💡 Proposed Solution: Unified Navigation System

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ [Meridian Logo]                           [⚙] [Profile ▼]  │ ← Header
├────────┬────────────────────────────────────────────────────┤
│        │                                                     │
│ LIBRARY│  Main Content Area                                 │
│ • All  │  • Dashboard (default)                             │
│ • Fav  │  • Analysis Workspace (when selected)              │
│        │  • List views (filtered by sidebar selection)      │
│ WORK   │                                                     │
│ • Ana  │                                                     │
│ • Ins  │                                                     │
│        │                                                     │
│ CONFIG │                                                     │
│ • ATyp │                                                     │
│ • STyp │                                                     │
│        │                                                     │
│ (32px) │  (Consistent for all views)                        │
│  gaps  │                                                     │
└────────┴────────────────────────────────────────────────────┘
```

### Component 1: Borderless Sidebar (SnapVault Pattern)

Inspired by SnapVault's elegant `sidebar-redesign.css`, use a **clean, unboxed design** with subtle visual hierarchy.

```
┌────────────────────────┐
│                        │
│  LIBRARY               │ ← 11px uppercase, rgba(255,255,255,0.4)
│  • All Analyses        │ ← 14px, left-accent when active
│  • Favorites           │
│  • Recent              │
│                        │ (32px gap)
│  WORK                  │
│  • Active Analyses     │ ← Active has blue left border
│  • Insights Dashboard  │
│  • Document Library    │
│                        │ (32px gap)
│  CONFIGURATION         │
│  • Analysis Types      │
│  • Source Types        │
│  • Integrations        │
│                        │
└────────────────────────┘
```

**CSS Pattern (from SnapVault):**

```css
.sidebar-section {
  display: flex;
  flex-direction: column;
  gap: 10px; /* Item spacing */
  margin-bottom: 32px; /* Section spacing */
  padding: 0; /* No container padding */
  background: transparent; /* No boxes! */
  border: none; /* No borders! */
}

.section-header {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.4);
}

.sidebar-item {
  padding: 10px 12px;
  border-left: 2px solid transparent;
  transition: all 0.15s ease;
}

.sidebar-item.active {
  border-left-color: #5b9fff; /* Blue accent */
  background: rgba(91, 159, 255, 0.08);
  color: rgba(255, 255, 255, 1);
}
```

**Benefits:**

- ✅ Clean, professional aesthetic
- ✅ Scales to many items without feeling cramped
- ✅ Clear visual hierarchy without boxes
- ✅ Active state is unmistakable

### Component 2: Unified Content Area Pattern

**All entity types** (Analyses, Analysis Types, Source Types) follow **one consistent pattern**:

#### Pattern A: List View (Grid Cards)

```
┌──────────────────────────────────────────────────────────┐
│  Analysis Types                       [+ Create Type]    │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 🔍 Search types...     [Filter ▼]  [Sort: Name ▼] │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Financial       │  │ Risk Assessment │              │
│  │ Last used: 2d   │  │ Last used: 5d   │              │
│  │ 12 analyses     │  │ 8 analyses      │              │
│  │                 │  │                 │              │
│  │ [👁 View] [✏️ Edit]│  │ [👁 View] [✏️ Edit]│              │
│  └─────────────────┘  └─────────────────┘              │
└──────────────────────────────────────────────────────────┘
```

**Interaction:**

- Click card body → Opens **Detail Panel** (slide-in from right, 60% width)
- Click [View] button → Same as clicking card
- Click [Edit] → Detail Panel opens in **Edit Mode**

#### Pattern B: Detail Panel (Slide-in Overlay)

Inspired by SnapVault's **lightbox panel** pattern, use a **slide-in panel** instead of full-page navigation.

```
┌────────────────┬─────────────────────────────────────────┐
│ Grid view      │  ╔════════════════════════════════════╗ │
│ (dimmed 50%)   │  ║ Financial Report Type              ║ │
│                │  ║ ─────────────────────────────────  ║ │
│ ┌──────────┐   │  ║                                    ║ │
│ │ Card     │   │  ║ DESCRIPTION                        ║ │
│ └──────────┘   │  ║ Extract revenue, costs, headcount  ║ │
│                │  ║                                    ║ │
│ ┌──────────┐   │  ║ FIELDS (8)                         ║ │
│ │ Card     │   │  ║ • Annual Revenue    (Currency)     ║ │
│ └──────────┘   │  ║ • Employee Count    (Number)       ║ │
│                │  ║ • Founded Date      (Date)         ║ │
│                │  ║                                    ║ │
│                │  ║ TAGS                               ║ │
│                │  ║ [Financial] [Vendor] [Enterprise]  ║ │
│                │  ║                                    ║ │
│                │  ║                                    ║ │
│                │  ║ [✕ Close]    [Delete] [Edit]       ║ │
│                │  ╚════════════════════════════════════╝ │
└────────────────┴─────────────────────────────────────────┘
```

**Benefits:**

- ✅ Maintains context (can see list behind panel)
- ✅ Faster than full-page navigation
- ✅ Easy to close and return to browsing
- ✅ Consistent behavior across all entity types
- ✅ Keyboard-friendly (Escape to close)

**CSS Pattern (from SnapVault lightbox-panel.css):**

```css
.detail-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 60%;
  height: 100vh;
  background: var(--color-surface); /* #141414 */
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.5);
  transform: translateX(100%);
  transition: transform 0.3s cubic-bezier(0.33, 1, 0.68, 1);
  z-index: 100;
  overflow-y: auto;
}

.detail-panel.open {
  transform: translateX(0);
}

.detail-panel-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  opacity: 0;
  transition: opacity 0.3s ease;
  z-index: 99;
}

.detail-panel-backdrop.visible {
  opacity: 1;
}
```

#### Pattern C: Edit Mode (In-Panel)

Same panel, switches to edit mode with editable fields:

```
╔════════════════════════════════════╗
║ Edit: Financial Report Type        ║
║ ─────────────────────────────────  ║
║                                    ║
║ NAME                               ║
║ [Financial Report               ] ║
║                                    ║
║ DESCRIPTION                        ║
║ [Extract financial metrics...   ] ║
║ [                                ] ║
║                                    ║
║ FIELDS                             ║
║ • Annual Revenue    [Edit] [⌫]     ║
║ • Employee Count    [Edit] [⌫]     ║
║ [+ Add Field]                      ║
║                                    ║
║ TAGS                               ║
║ [Financial ×] [Vendor ×]           ║
║ [+ Add Tag]                        ║
║                                    ║
║                                    ║
║ [✕ Close]   [Cancel] [Save]        ║
╚════════════════════════════════════╝
```

**Benefits:**

- ✅ Same container as view mode (reduced context switching)
- ✅ Clear edit affordances
- ✅ Cancel returns to view mode without closing panel

### Component 3: Analysis Workspace (Special Case)

Analyses are **living workspaces**, not simple entities. They need the **two-column pattern** but accessed consistently.

**Access Pattern:**

1. Sidebar → "All Analyses" (shows filtered grid in main area)
2. Click analysis card → Opens **full workspace** (not panel)
3. Breadcrumb shows: Home > Analyses > [Analysis Name]

**Workspace Layout:**

```
┌──────────────────────────────────────────────────────────┐
│ ← Analyses   CloudCorp Assessment         [⚙️] [Export] │
├────────────────┬─────────────────────────────────────────┤
│ 📊 INSIGHTS    │ 📄 DOCUMENTS                            │
│                │                                         │
│ Key Insights   │ vendor-assessment.pdf  ✓               │
│ ┌────────────┐ │ financial-stmt.pdf     ⟳ 45%          │
│ │ Revenue    │ │                                         │
│ │ $51.3M     │ │ [Drop files or click to upload]        │
│ └────────────┘ │                                         │
│                │                                         │
│ ┌────────────┐ │ @ AUTHORITATIVE NOTES                  │
│ │ Employees  │ │ "CFO confirmed revenue at $51.3M..."   │
│ │ 475        │ │ [Edit Notes]                           │
│ └────────────┘ │                                         │
└────────────────┴─────────────────────────────────────────┘
```

**Why Different:**

- Analyses are **work sessions**, not configuration
- Need side-by-side panels for insights + documents
- Users spend extended time here (vs quick edits for types)

---

## 🎨 Visual Design System (SnapVault Palette)

### Color Tokens

Adopt SnapVault's refined dark theme with semantic blue accents.

```css
/* Surface Hierarchy */
--color-canvas: #0a0a0a; /* Body background */
--color-surface: #141414; /* Panels, cards */
--color-surface-hover: #1a1a1a; /* Hover states */
--color-surface-active: #222222; /* Pressed states */

/* Borders - Subtle */
--color-border-subtle: #2a2a2a;
--color-border-medium: #3a3a3a;
--color-border-strong: #4a4a4a;

/* Text - High Contrast */
--color-text-primary: #e8e8e8;
--color-text-secondary: #a8a8a8;
--color-text-tertiary: #787878;
--color-text-disabled: #4a4a4a;

/* Accent - Blue for Actions */
--color-accent-primary: #5b9fff; /* Primary buttons, links */
--color-accent-hover: #7cb3ff; /* Hover state */

/* Semantic Colors - Meridian Specific */
--color-gold: #fbbf24; /* Authoritative Notes */
--color-green: #4ade80; /* High confidence */
--color-amber: #fbbf24; /* Medium confidence */
--color-red: #f87171; /* Low confidence / errors */
```

### Typography

```css
/* Font Families */
--font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto,
  "Helvetica Neue", Arial, sans-serif;

/* Type Scale */
--text-xs: 11px; /* Section headers (uppercase) */
--text-sm: 13px; /* Metadata, labels */
--text-base: 15px; /* Body text, buttons */
--text-lg: 18px; /* Card titles */
--text-xl: 24px; /* Page titles */
--text-2xl: 32px; /* Hero text (values in workspace) */

/* Weights */
--weight-normal: 400;
--weight-medium: 500;
--weight-semibold: 600;
--weight-bold: 700;
```

### Spacing System (8px Grid)

```css
--space-1: 8px; /* Tight (button padding) */
--space-2: 16px; /* Standard gap */
--space-3: 24px; /* Section padding */
--space-4: 32px; /* Large gaps (between sidebar sections) */
--space-5: 40px; /* Extra large */
--space-6: 48px; /* Major sections */
```

### Motion & Transitions

```css
/* Easing */
--ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);
--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);

/* Durations */
--duration-fast: 100ms; /* Hovers */
--duration-normal: 200ms; /* Standard transitions */
--duration-slow: 300ms; /* Panel slides */
```

---

## 🔧 Interaction Patterns

### Pattern 1: Sidebar Navigation

**Behavior:**

- Single-click activates section
- Active section highlighted with blue left border
- Badge shows count (e.g., "12" next to "Active Analyses")
- Keyboard: Arrow keys to navigate, Enter to activate

**Example:**

```
LIBRARY
• All Analyses           [24]  ← Badge
• Favorites              [5]
• Recent                 [8]
                              (32px gap)
WORK
• Active Analyses        [12] ← Active (blue border)
• Insights Dashboard
• Document Library       [156]
```

### Pattern 2: Card Grid

**Layout:**

- Responsive grid: 1-4 columns based on viewport width
- Card hover: Subtle lift (`transform: translateY(-2px)`)
- Card click: Opens detail panel
- Buttons visible on hover (or always on touch devices)

**Interaction States:**

```css
.entity-card {
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.entity-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

.entity-card:active {
  transform: translateY(0);
}
```

### Pattern 3: Detail Panel

**Opening:**

- Slide in from right (300ms, ease-out)
- Backdrop fades in behind list (50% opacity)
- Focus trapped in panel (Tab cycles within panel)

**Closing:**

- Click backdrop
- Click [× Close] button
- Press Escape key
- Navigate via breadcrumb

**Keyboard:**

- Tab: Cycle through interactive elements
- Escape: Close panel
- E: Switch to Edit mode (when in View mode)
- Cmd/Ctrl+S: Save (when in Edit mode)

### Pattern 4: Search & Filter

**Search Bar:**

- Debounced input (300ms delay)
- Clear button appears when text entered
- Keyboard: Cmd/Ctrl+K to focus

**Filter Chips:**

```
[Tag: Financial ×] [Status: Active ×] [+ Add Filter]
```

- Click × to remove filter
- Live updates grid below
- Persist in URL for bookmarking

---

## 📊 Information Architecture

### Unified Mental Model

```
Meridian
├─ LIBRARY (Browse)
│  ├─ All Analyses
│  ├─ Favorites
│  └─ Recent
│
├─ WORK (Primary Activities)
│  ├─ Active Analyses → List → Workspace
│  ├─ Insights Dashboard
│  └─ Document Library
│
└─ CONFIGURATION (Setup)
   ├─ Analysis Types → List → Detail Panel → Edit
   ├─ Source Types → List → Detail Panel → Edit
   └─ Integrations → List → Detail Panel → Edit
```

**Hierarchy Rules:**

1. **Library** = Entry points (no creation, just browsing)
2. **Work** = Where users spend 90% of time (analyses, insights)
3. **Configuration** = Types, templates, settings (10% of time)

### Routing Strategy

**URL Pattern:**

```
/                            → Dashboard
/analyses                    → Analyses list (all)
/analyses/favorites          → Filtered by favorites
/analyses/:id                → Analysis workspace
/configuration/analysis-types → Analysis Types list
/configuration/analysis-types/:id → Detail panel (query param: ?view=true)
/configuration/analysis-types/:id/edit → Detail panel (edit mode)
```

**Benefits:**

- ✅ Bookmarkable states
- ✅ Browser back/forward work intuitively
- ✅ Clear hierarchy in URL
- ✅ Detail panels maintain list context

---

## ✅ Implementation Checklist

### Phase 1: Foundation (Week 1)

- [ ] **Adopt SnapVault design tokens** (`design-tokens.css`)

  - Copy color system, typography scale, spacing values
  - Update existing Meridian tokens to match

- [ ] **Implement borderless sidebar** (`sidebar.css`, `sidebar.js`)

  - Three sections: Library, Work, Configuration
  - Active state styling (blue left border)
  - Badge display for counts
  - Keyboard navigation

- [ ] **Create detail panel component** (`DetailPanel.js`, `detail-panel.css`)
  - Slide-in animation from right
  - Backdrop with click-to-close
  - View/Edit mode toggle
  - Keyboard shortcuts (Escape, Tab trap)

### Phase 2: Unified Lists (Week 2)

- [ ] **Standardize Analysis Types list**

  - Use consistent card grid pattern
  - Click card → Opens detail panel (not full page)
  - Edit button → Detail panel in edit mode

- [ ] **Standardize Source Types list**

  - Same pattern as Analysis Types
  - Consistent search/filter/sort UI

- [ ] **Standardize Analyses list**
  - Card grid with preview stats
  - Click → Opens workspace (full view)
  - Favorites toggle on cards

### Phase 3: Navigation Integration (Week 3)

- [ ] **Update routing**

  - Implement `/configuration/*` paths
  - Detail panel URL patterns (`?panel=:id`)
  - Browser back/forward handling

- [ ] **Breadcrumb component**

  - Show hierarchy: Home > Section > Item
  - Clickable navigation
  - Auto-generated from route

- [ ] **Keyboard shortcuts overlay** (press `?` to show)
  - G+A: Go to All Analyses
  - G+F: Go to Favorites
  - G+C: Go to Configuration
  - /: Focus search
  - N: New item (context-aware)

### Phase 4: Polish & Testing (Week 4)

- [ ] **Accessibility audit**

  - Screen reader testing
  - Keyboard-only navigation
  - Focus indicators
  - ARIA labels

- [ ] **Responsive design**

  - Sidebar collapses to hamburger on mobile
  - Detail panel becomes full-screen on small screens
  - Touch-friendly tap targets (44×44px minimum)

- [ ] **Animation polish**

  - Consistent easing curves
  - Loading skeletons for async data
  - Empty states with illustrations

- [ ] **Documentation**
  - Update `README.md` with navigation map
  - User guide for keyboard shortcuts
  - Developer guide for adding new entity types

---

## 📈 Success Metrics

### User Experience Metrics

- **Task Completion Time**: 30% reduction in time to find/edit a type
- **Error Rate**: 50% reduction in navigation errors (wrong section)
- **User Satisfaction**: >4.5/5 on post-session survey
- **Cognitive Load**: Users can describe hierarchy without training

### Technical Metrics

- **Code Consistency**: All entity types use same components
- **Bundle Size**: No increase (reuse patterns, delete old code)
- **Accessibility**: WCAG 2.1 AA compliance
- **Performance**: 60fps animations, <100ms interactions

---

## 🎯 Design Rationale: Why This Approach?

### 1. SnapVault's Visual Language Proven Effective

SnapVault demonstrates **professional-grade** design that scales:

- Used by photographers managing 10,000+ images
- Clean, borderless aesthetic reduces visual noise
- Consistent patterns enable muscle memory

**Adaptation:** We borrow the **visual language** (colors, spacing, typography) while respecting Meridian's unique **information architecture** (evidence, types, analyses).

### 2. Sidebar Navigation Scales Better Than Top Nav

**Top Nav Issues:**

```
[Dashboard] [Analyses] [Types] [Sources] [Settings] [More...] ← Cluttered!
```

**Sidebar Benefits:**

```
LIBRARY       ← Semantic grouping
• All
• Favorites

CONFIGURATION ← Clear separation
• Types
• Sources
```

- ✅ Scales to 20+ items without redesign
- ✅ Clear hierarchy (sections group related items)
- ✅ Always visible (no hidden hamburger menus)
- ✅ Easy to scan vertically (natural reading direction)

### 3. Detail Panels Faster Than Full-Page Navigation

**Traditional Flow:**

```
List → Click → Full page load → Edit → Save → Back button → List reload
```

**Panel Flow:**

```
List → Click → Panel slides in → Edit in place → Save → Panel closes
```

**Benefits:**

- ✅ 50% faster (no page reload)
- ✅ Maintains context (can see list behind)
- ✅ Keyboard-friendly (Escape to close)
- ✅ Encourages browsing (easy to open/close many items)

### 4. Consistent Patterns Reduce Cognitive Load

**Current:** Users must learn 3 different patterns (dashboard, workspace, forms)  
**Proposed:** All entity types follow **one pattern** (list → panel → edit)

**Exception:** Analyses use workspace (justified by their unique nature as living sessions)

**Result:** Reduced training time, fewer errors, faster workflows

---

## 🚀 Migration Strategy

### Step 1: Parallel Implementation (No Breaking Changes)

- Implement new sidebar alongside existing top nav
- Add detail panels as **alternative** to full-page views
- Use feature flags to toggle between old/new UX

### Step 2: Gradual Rollout

- Week 1: Internal team testing with new UX
- Week 2: Beta users opt-in to new experience
- Week 3: New UX becomes default (old UX still accessible)
- Week 4: Remove old code after monitoring metrics

### Step 3: Deprecation

- Week 5: Remove feature flags
- Week 6: Clean up old CSS/JS files
- Week 7: Update documentation and screenshots

---

## 📚 References & Inspiration

### Internal Documents

- `S6.SnapVault/DESIGN_SYSTEM.md` - Color system, typography
- `S6.SnapVault/wwwroot/css/sidebar-redesign.css` - Borderless sidebar pattern
- `S6.SnapVault/wwwroot/css/lightbox-panel.css` - Panel slide-in animation
- `S7.Meridian/docs/UX-SPECIFICATION.md` - Evidence-first workspace paradigm

### Design Patterns

- **Linear** (linear.app) - Sidebar hierarchy, keyboard shortcuts
- **Notion** - Panel-based detail views, breadcrumb navigation
- **Figma** - Contextual panels, consistent interaction patterns
- **SnapVault Pro** - Dark theme, borderless aesthetic, professional polish

---

## 🎨 Appendix: Component Specifications

### A. Sidebar Item Anatomy

```html
<button class="sidebar-item active">
  <svg class="item-icon">...</svg>
  <span class="item-label">Active Analyses</span>
  <div class="item-meta">
    <kbd class="shortcut">G</kbd>
    <kbd class="shortcut">A</kbd>
    <span class="item-badge">12</span>
  </div>
</button>
```

**CSS:**

```css
.sidebar-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-left: 2px solid transparent;
  border-radius: 6px;
  font-size: 14px;
  color: rgba(255, 255, 255, 0.85);
  transition: all 0.15s ease;
}

.sidebar-item.active {
  border-left-color: var(--color-accent-primary);
  background: rgba(91, 159, 255, 0.08);
  color: rgba(255, 255, 255, 1);
}

.item-icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}

.item-label {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.item-meta {
  display: flex;
  align-items: center;
  gap: 4px;
}

.item-badge {
  padding: 2px 6px;
  background: rgba(255, 255, 255, 0.08);
  border-radius: 12px;
  font-size: 11px;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.7);
}
```

### B. Detail Panel Anatomy

```html
<div class="detail-panel-backdrop visible"></div>
<aside
  class="detail-panel open"
  role="dialog"
  aria-label="Analysis Type Details"
>
  <header class="detail-panel-header">
    <h2 class="detail-panel-title">Financial Report Type</h2>
    <button class="detail-panel-close" aria-label="Close">
      <svg>...</svg>
    </button>
  </header>

  <div class="detail-panel-body">
    <!-- Content (view or edit mode) -->
  </div>

  <footer class="detail-panel-footer">
    <button class="btn-secondary">Delete</button>
    <div class="footer-actions-right">
      <button class="btn-secondary">Cancel</button>
      <button class="btn-primary">Save</button>
    </div>
  </footer>
</aside>
```

### C. Card Grid Layout

```css
.entity-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
  padding: 24px;
}

.entity-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border-subtle);
  border-radius: 8px;
  padding: 20px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.entity-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
  border-color: var(--color-border-medium);
}
```

---

## ✨ Closing Thoughts

This redesign transforms Meridian from a **fragmented collection of views** into a **cohesive, professional application** that respects user attention and builds muscle memory through consistent patterns.

By borrowing SnapVault's elegant visual language while maintaining Meridian's unique evidence-driven paradigm, we create an experience that:

- ✅ **Looks professional** (dark theme, consistent spacing, subtle animations)
- ✅ **Feels intuitive** (one pattern for all entities, predictable behavior)
- ✅ **Scales effortlessly** (sidebar + panels handle 10 or 10,000 items)
- ✅ **Respects users** (keyboard shortcuts, fast interactions, clear hierarchy)
- ✅ **Aligns with Koan** (minimal scaffolding, clear patterns, DX-first thinking)

**Next Step:** Review this proposal with the team, gather feedback, and begin Phase 1 implementation.

---

**Questions or Feedback?**  
Contact: [Design Team] | Version: 1.0 | Last Updated: October 23, 2025
