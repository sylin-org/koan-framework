# Meridian UX Visual Guide

**Purpose:** Visual mockups and interaction flows for the redesigned Meridian interface  
**Related:** `MERIDIAN-UX-REALIGNMENT.md` (full proposal)

---

## 🎨 Visual System at a Glance

### Color Palette (SnapVault-Inspired)

```
████████ #0A0A0A  Canvas (body background)
████████ #141414  Surface (cards, panels)
████████ #1A1A1A  Surface Hover
████████ #222222  Surface Active
████████ #2A2A2A  Border Subtle
████████ #3A3A3A  Border Medium
████████ #E8E8E8  Text Primary
████████ #A8A8A8  Text Secondary
████████ #5B9FFF  Accent (primary actions)
████████ #FBBF24  Gold (authoritative notes)
████████ #4ADE80  Green (high confidence)
```

### Typography Scale

```
32px / Bold    → Analysis Workspace Values
24px / Semibold → Page Titles
18px / Semibold → Card Titles
15px / Normal   → Body Text
13px / Normal   → Metadata, Labels
11px / Semibold → Section Headers (UPPERCASE)
```

---

## 📱 Layout Mockups

### 1. Dashboard with Sidebar

```
┌─────────────────────────────────────────────────────────────────────────┐
│ [M] Meridian                                    [⚙️ Settings] [Profile ▼] │
├────────────────┬────────────────────────────────────────────────────────┤
│                │                                                         │
│  LIBRARY       │  Welcome back, Sarah                                   │
│  • All         │                                                         │
│  • Favorites   │  ╭─ Quick Actions ──────────────────────────────────╮  │
│  • Recent      │  │ [+ New Analysis]  [AI Create Type]  [View All]  │  │
│                │  ╰─────────────────────────────────────────────────╯  │
│                │                                                         │
│  WORK          │  ╭─ System Overview ─────────────────────────────────╮  │
│  • Active ▶    │  │ ┌──────────┐ ┌──────────┐ ┌──────────┐           │  │
│  • Insights    │  │ │   12     │ │    8     │ │   24     │           │  │
│  • Documents   │  │ │Analysis  │ │ Source   │ │ Active   │           │  │
│                │  │ │ Types    │ │ Types    │ │Analyses  │           │  │
│                │  │ └──────────┘ └──────────┘ └──────────┘           │  │
│  CONFIG        │  ╰─────────────────────────────────────────────────╯  │
│  • Analysis    │                                                         │
│    Types       │  ╭─ Recent Activity ──────────────────────────────────╮  │
│  • Source      │  │ CloudCorp Assessment      Updated 2m ago         │  │
│    Types       │  │ Risk Analysis Q4          Updated 1h ago         │  │
│  • Integ...    │  │ Financial Review          Updated 3h ago         │  │
│                │  ╰─────────────────────────────────────────────────╯  │
│                │                                                         │
│  [32px gaps]   │  ╭─ Favorites ──────────────────────────────────────╮  │
│  [between      │  │ ⭐ Enterprise Template   ⭐ Security Audit Type   │  │
│   sections]    │  ╰─────────────────────────────────────────────────╯  │
│                │                                                         │
└────────────────┴────────────────────────────────────────────────────────┘
    240px wide       Fluid width content area
```

**Key Features:**

- Sidebar uses **UPPERCASE 11px** section headers with **rgba(255,255,255,0.4)** opacity
- Active item has **blue left border** (#5B9FFF)
- Content area uses **card-based layout** with consistent spacing
- **32px gaps** between sidebar sections (SnapVault pattern)

---

### 2. Analysis Types List View

```
┌─────────────────────────────────────────────────────────────────────────┐
│ [M] Meridian                                    [⚙️ Settings] [Profile ▼] │
├────────────────┬────────────────────────────────────────────────────────┤
│                │                                                         │
│  LIBRARY       │  Analysis Types                    [+ Create Type]     │
│  • All         │  ┌──────────────────────────────────────────────────┐  │
│  • Favorites   │  │ 🔍 Search types...  [Filter ▼] [Sort: Name ▼]   │  │
│  • Recent      │  └──────────────────────────────────────────────────┘  │
│                │                                                         │
│  WORK          │  Showing 12 types                                      │
│  • Active      │                                                         │
│  • Insights    │  ┌─────────────────┐  ┌─────────────────┐            │
│  • Documents   │  │ Financial       │  │ Risk            │  [Hover:    │
│                │  │ Report          │  │ Assessment      │   lift 2px  │
│  CONFIG        │  │                 │  │                 │   + shadow] │
│  • Analysis ▶  │  │ Extract revenue,│  │ Evaluate risks  │            │
│    Types       │  │ costs, metrics  │  │ and compliance  │            │
│  • Source      │  │                 │  │                 │            │
│    Types       │  │ 🏷️ Financial    │  │ 🏷️ Security     │            │
│  • Integ...    │  │                 │  │                 │            │
│                │  │ 12 analyses     │  │ 8 analyses      │            │
│                │  │                 │  │                 │            │
│                │  │ [👁️ View] [✏️ Edit]│  │ [👁️ View] [✏️ Edit]│            │
│                │  └─────────────────┘  └─────────────────┘            │
│                │                                                         │
│                │  ┌─────────────────┐  ┌─────────────────┐            │
│                │  │ Technical       │  │ Compliance      │            │
│                │  │ Assessment      │  │ Review          │            │
│                │  │                 │  │                 │            │
│                │  │ [Cards continue in responsive grid...]           │
│                │                                                         │
└────────────────┴────────────────────────────────────────────────────────┘
```

**Interaction Flow:**

1. **Click card body** → Opens detail panel (slide-in from right)
2. **Click [View] button** → Same as clicking card
3. **Click [Edit] button** → Detail panel opens in edit mode
4. **Hover card** → Subtle lift animation + shadow

---

### 3. Detail Panel (View Mode)

```
┌─────────────────┬──────────────────────────────────────────────────────┐
│                 │ ╔════════════════════════════════════════════════╗   │
│  Grid view      │ ║ Financial Report Type                [× Close] ║   │
│  (dimmed 50%)   │ ║ ───────────────────────────────────────────── ║   │
│                 │ ║                                                ║   │
│ ┌─────────────┐ │ ║ DESCRIPTION                                    ║   │
│ │ Financial   │ │ ║ Extract financial metrics including revenue,   ║   │
│ │ Report      │ │ ║ costs, employee count, and funding details.    ║   │
│ └─────────────┘ │ ║                                                ║   │
│                 │ ║ FIELDS (8)                                     ║   │
│ ┌─────────────┐ │ ║ • Annual Revenue        [Currency]             ║   │
│ │ Risk        │ │ ║ • Operating Costs       [Currency]             ║   │
│ │ Assessment  │ │ ║ • Employee Count        [Number]               ║   │
│ └─────────────┘ │ ║ • Founded Date          [Date]                 ║   │
│                 │ ║ • Funding Round         [Text]                 ║   │
│                 │ ║ [+ Show 3 more fields]                         ║   │
│                 │ ║                                                ║   │
│ [Click         │ ║ TAGS                                           ║   │
│  anywhere      │ ║ [Financial] [Vendor] [Enterprise]              ║   │
│  on backdrop   │ ║                                                ║   │
│  to close]     │ ║ USAGE                                          ║   │
│                 │ ║ Used in 12 analyses                            ║   │
│                 │ ║ Last used: 2 days ago                          ║   │
│                 │ ║ Created: Oct 15, 2025                          ║   │
│                 │ ║                                                ║   │
│                 │ ║                                                ║   │
│                 │ ║ [Delete]              [Cancel]  [Edit Type]   ║   │
│                 │ ╚════════════════════════════════════════════════╝   │
└─────────────────┴──────────────────────────────────────────────────────┘
                    Panel slides in from right (300ms ease-out)
                    60% viewport width on desktop
```

**Key Features:**

- Panel has **backdrop** (50% opacity black) over list
- Click backdrop or **press Escape** to close
- **[Edit Type]** button switches to edit mode (same panel)
- Panel maintains **scroll position** when switching modes

---

### 4. Detail Panel (Edit Mode)

```
╔════════════════════════════════════════════════╗
║ Edit: Financial Report Type      [× Close]    ║
║ ───────────────────────────────────────────── ║
║                                                ║
║ NAME                                           ║
║ ┌────────────────────────────────────────────┐ ║
║ │ Financial Report                           │ ║
║ └────────────────────────────────────────────┘ ║
║                                                ║
║ DESCRIPTION                                    ║
║ ┌────────────────────────────────────────────┐ ║
║ │ Extract financial metrics including...     │ ║
║ │                                            │ ║
║ │                                            │ ║
║ └────────────────────────────────────────────┘ ║
║                                                ║
║ FIELDS                                         ║
║ ┌────────────────────────────────────────────┐ ║
║ │ • Annual Revenue    [Currency]  [✏️] [🗑️]   │ ║
║ │ • Operating Costs   [Currency]  [✏️] [🗑️]   │ ║
║ │ • Employee Count    [Number]    [✏️] [🗑️]   │ ║
║ └────────────────────────────────────────────┘ ║
║ [+ Add Field]                                  ║
║                                                ║
║ TAGS                                           ║
║ [Financial ×] [Vendor ×] [Enterprise ×]        ║
║ [+ Add Tag]                                    ║
║                                                ║
║ ⚠️ DANGER ZONE                                 ║
║ [Delete Type] (12 analyses will be affected)   ║
║                                                ║
║                                                ║
║ [✕ Close]         [Cancel]  [Save Changes]    ║
╚════════════════════════════════════════════════╝
```

**Edit Mode Behaviors:**

- **[Save Changes]** becomes primary button (blue)
- **[Cancel]** returns to view mode (no close)
- **Auto-save draft** to localStorage every 5 seconds
- **Unsaved changes warning** if closing with edits
- **Cmd/Ctrl+S** keyboard shortcut to save

---

### 5. Analysis Workspace (Special Case)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ← Analyses   CloudCorp Vendor Assessment    [Export ▼] [⋮ More]        │
│ Enterprise Architecture Review • Updated 2m ago • 4 docs • 12 insights  │
├─────────────────────────────┬───────────────────────────────────────────┤
│                             │                                           │
│ 📊 INSIGHTS                 │ 📄 DOCUMENTS                              │
│                             │                                           │
│ @ Authoritative Notes       │ ╭─ Upload Documents ─────────────────╮   │
│ ┌─────────────────────────┐ │ │ Drop files here or click to browse │   │
│ │ "CFO confirmed revenue  │ │ ╰────────────────────────────────────╯   │
│ │ at $51.3M per call..."  │ │                                           │
│ │ [Edit Notes]            │ │ ┌───────────────────────────────────┐   │
│ └─────────────────────────┘ │ │ 📄 vendor-assessment.pdf          │   │
│                             │ │    ✓ 5 insights extracted          │   │
│ ╭─ Key Insights ──────────╮ │ │    [View] [Remove]                │   │
│ │                         │ │ └───────────────────────────────────┘   │
│ │ ┌─────────────────────┐ │ │                                           │
│ │ │ Annual Revenue   ⭐ │ │ │ ┌───────────────────────────────────┐   │
│ │ │ $51.3M             │ │ │ │ 📄 financial-statement.pdf        │   │
│ │ │                    │ │ │ │    ✓ 8 insights extracted          │   │
│ │ │ FROM AUTH NOTES    │ │ │ │    [View] [Remove]                │   │
│ │ │ (overrides doc)    │ │ │ └───────────────────────────────────┘   │
│ │ └─────────────────────┘ │ │                                           │
│ │                         │ │ ┌───────────────────────────────────┐   │
│ │ ┌─────────────────────┐ │ │ │ 📄 security-audit.pdf             │   │
│ │ │ Employee Count   ✓ │ │ │ │    ⟳ Processing (45%)              │   │
│ │ │ 475                │ │ │ │    Extracting insights...          │   │
│ │ │                    │ │ │ └───────────────────────────────────┘   │
│ │ │ FROM DOCUMENT      │ │ │                                           │
│ │ │ ████████ 94% conf  │ │ │                                           │
│ │ └─────────────────────┘ │ │                                           │
│ ╰─────────────────────────╯ │                                           │
│                             │                                           │
│ [Quality: 95% Citation]     │ [3 pending • 4 complete]                  │
│                             │                                           │
└─────────────────────────────┴───────────────────────────────────────────┘
   50% width                     50% width
   (Insights focus)              (Document management)
```

**Why Workspace is Different:**

- **Living session** (not just entity details)
- **Side-by-side panels** (insights + documents)
- **Real-time updates** (processing status, new insights)
- **Always-active upload** (drop zone visible)
- Users spend **extended time** here (vs quick edits)

---

## 🎬 Interaction Flows

### Flow 1: Browse and View Type

```
1. User clicks "Analysis Types" in sidebar
   ↓
2. Main area shows card grid
   ↓
3. User clicks card body (or [View] button)
   ↓
4. Detail panel slides in from right (300ms)
   Backdrop fades in behind list (50% opacity)
   ↓
5. User reviews type details
   ↓
6. User presses Escape or clicks backdrop
   ↓
7. Panel slides out, backdrop fades out
   Back to grid view
```

**Time:** ~2 seconds (vs 4 seconds with full-page navigation)

### Flow 2: Edit Type

```
1. User opens detail panel (see Flow 1)
   ↓
2. User clicks [Edit Type] button
   ↓
3. Panel content switches to edit form (200ms fade)
   Footer changes: [Delete] [Cancel] [Save Changes]
   ↓
4. User edits fields
   (Auto-save to localStorage every 5 seconds)
   ↓
5. User clicks [Save Changes] or presses Cmd/Ctrl+S
   ↓
6. Save animation (spinner on button)
   API call completes
   ↓
7. Success toast: "Type saved successfully"
   Panel returns to view mode
   Grid behind updates with new data
```

**Benefits:**

- No page reload
- Maintains scroll position in grid
- Easy to edit multiple types quickly

### Flow 3: Create New Type

```
1. User clicks [+ Create Type] button (header)
   ↓
2. Modal overlay appears (centered, 500px wide)
   "Create Analysis Type"
   ↓
3. User enters name, description
   ↓
4. User clicks [Create] or presses Enter
   ↓
5. API creates type, returns ID
   ↓
6. Modal closes
   Detail panel opens with new type (edit mode)
   Grid behind adds new card
```

**Alternative Flow (AI Create):**

```
1. User clicks [AI Create] button
   ↓
2. Large modal appears
   "Describe your analysis goal..."
   ↓
3. User enters natural language description
   "I want to extract financial metrics from vendor documents"
   ↓
4. AI generates type definition
   Shows preview in modal
   ↓
5. User reviews, clicks [Accept]
   ↓
6. Modal closes
   Detail panel opens with new type
```

---

## 🎨 Component States

### Sidebar Item States

```
Normal:
• All Analyses         [24]
  color: rgba(255,255,255,0.85)
  border-left: 2px solid transparent

Hover:
• All Analyses         [24]
  background: rgba(255,255,255,0.05)
  cursor: pointer

Active:
▌All Analyses         [24]  ← Blue left border
  border-left: 2px solid #5B9FFF
  background: rgba(91,159,255,0.08)
  color: rgba(255,255,255,1.0)

Focus (keyboard):
• All Analyses         [24]
  outline: 2px solid #5B9FFF
  outline-offset: 2px
```

### Card States

```
Default:
┌─────────────────────────┐
│ Financial Report        │
│ Extract revenue...      │
│ [View] [Edit]           │
└─────────────────────────┘
  background: #141414
  border: 1px solid #2A2A2A

Hover:
┌─────────────────────────┐ ↑ 2px lift
│ Financial Report        │
│ Extract revenue...      │
│ [View] [Edit]           │
└─────────────────────────┘
  box-shadow: 0 4px 12px rgba(0,0,0,0.4)
  border: 1px solid #3A3A3A

Active (pressed):
┌─────────────────────────┐
│ Financial Report        │ ← Back to normal position
│ Extract revenue...      │
│ [View] [Edit]           │
└─────────────────────────┘
  transform: translateY(0)

Selected:
┌─────────────────────────┐
│ ✓ Financial Report      │
│ Extract revenue...      │
│ [View] [Edit]           │
└─────────────────────────┘
  border: 2px solid #5B9FFF
  background: rgba(91,159,255,0.08)
```

### Button States

```
Primary Button ([Edit Type]):
Default:  background: #5B9FFF, color: white
Hover:    background: #7CB3FF
Active:   background: #4A8FEF
Disabled: background: #3A3A3A, color: #787878

Secondary Button ([Cancel]):
Default:  background: transparent, border: 1px solid #3A3A3A
Hover:    background: rgba(255,255,255,0.05)
Active:   background: rgba(255,255,255,0.1)

Danger Button ([Delete]):
Default:  background: transparent, color: #F87171
Hover:    background: rgba(248,113,113,0.1)
Active:   background: rgba(248,113,113,0.2)
```

---

## ♿ Accessibility Features

### Keyboard Navigation

```
Tab Order in Detail Panel:
1. [× Close] button
2. Content area (scrollable)
3. [Delete] button
4. [Cancel] button
5. [Save Changes] button
↻ Tab cycles back to [× Close]

Shortcuts:
Escape     → Close panel
E          → Switch to Edit mode (when in View mode)
Cmd/Ctrl+S → Save (when in Edit mode)
Cmd/Ctrl+K → Focus search
G+A        → Go to All Analyses
G+F        → Go to Favorites
G+C        → Go to Configuration
```

### Screen Reader Support

```html
<!-- Panel markup -->
<aside
  class="detail-panel"
  role="dialog"
  aria-label="Analysis Type Details"
  aria-modal="true"
>
  <h2 id="panel-title">Financial Report Type</h2>

  <!-- Content -->

  <button aria-label="Close detail panel" aria-controls="detail-panel">
    <svg aria-hidden="true">...</svg>
  </button>
</aside>

<!-- Sidebar item -->
<button
  class="sidebar-item active"
  aria-current="page"
  aria-label="Analysis Types, 12 items"
>
  <svg aria-hidden="true">...</svg>
  <span>Analysis Types</span>
  <span class="badge" aria-label="12 types">12</span>
</button>
```

### Focus Management

```javascript
// When panel opens
panel.addEventListener("open", () => {
  // Save previous focus
  previousFocus = document.activeElement;

  // Move focus to panel
  panel.querySelector(".detail-panel-close").focus();

  // Trap focus within panel
  trapFocus(panel);
});

// When panel closes
panel.addEventListener("close", () => {
  // Return focus to previous element
  previousFocus.focus();
});
```

---

## 📱 Responsive Behavior

### Desktop (>1200px)

```
┌────────────────────────────────────┐
│ Sidebar: 240px fixed width         │
│ Content: Fluid width               │
│ Detail Panel: 60% viewport width   │
└────────────────────────────────────┘
```

### Tablet (768px - 1200px)

```
┌────────────────────────────────────┐
│ Sidebar: 200px fixed width         │
│ Content: Fluid width               │
│ Detail Panel: 70% viewport width   │
└────────────────────────────────────┘
```

### Mobile (<768px)

```
┌────────────────────────────────────┐
│ Sidebar: Hamburger menu (hidden)   │
│ Content: Full width                │
│ Detail Panel: Full screen overlay  │
└────────────────────────────────────┘
```

**Mobile Sidebar:**

- Slides in from left when hamburger clicked
- Full height overlay
- Close button visible at top
- Same content as desktop (scrollable)

---

## 🎯 Design Tokens Reference

### Complete Token List

```css
:root {
  /* ===== Colors ===== */

  /* Surfaces */
  --color-canvas: #0a0a0a;
  --color-surface: #141414;
  --color-surface-hover: #1a1a1a;
  --color-surface-active: #222222;
  --color-surface-subtle: #18181b;

  /* Borders */
  --color-border-subtle: #2a2a2a;
  --color-border-medium: #3a3a3a;
  --color-border-strong: #4a4a4a;
  --color-border-interactive: #3f3f46;

  /* Text */
  --color-text-primary: #e8e8e8;
  --color-text-secondary: #a8a8a8;
  --color-text-tertiary: #787878;
  --color-text-disabled: #4a4a4a;
  --color-text-inverse: #0a0a0a;

  /* Accents */
  --color-accent-primary: #5b9fff;
  --color-accent-hover: #7cb3ff;
  --color-accent-active: #4a8fef;
  --color-accent-semantic: #a78bfa;
  --color-accent-success: #4ade80;
  --color-accent-warning: #fbbf24;
  --color-accent-danger: #f87171;
  --color-accent-favorite: #ffc947;

  /* State Colors */
  --color-focus-ring: rgba(91, 159, 255, 0.4);
  --color-selection: rgba(91, 159, 255, 0.15);

  /* ===== Typography ===== */

  /* Families */
  --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto,
    "Helvetica Neue", Arial, sans-serif;
  --font-mono: "SF Mono", Monaco, "Cascadia Code", "Courier New", monospace;

  /* Sizes */
  --text-xs: 0.6875rem; /* 11px */
  --text-sm: 0.8125rem; /* 13px */
  --text-base: 0.9375rem; /* 15px */
  --text-lg: 1.125rem; /* 18px */
  --text-xl: 1.5rem; /* 24px */
  --text-2xl: 2rem; /* 32px */

  /* Weights */
  --weight-normal: 400;
  --weight-medium: 500;
  --weight-semibold: 600;
  --weight-bold: 700;

  /* Line Heights */
  --leading-tight: 1.25;
  --leading-normal: 1.5;
  --leading-relaxed: 1.75;

  /* Letter Spacing */
  --tracking-tight: -0.025em;
  --tracking-normal: 0;
  --tracking-wide: 0.05em;
  --tracking-wider: 0.1em;

  /* ===== Spacing ===== */

  --space-0: 0;
  --space-1: 0.5rem; /* 8px */
  --space-2: 1rem; /* 16px */
  --space-3: 1.5rem; /* 24px */
  --space-4: 2rem; /* 32px */
  --space-5: 2.5rem; /* 40px */
  --space-6: 3rem; /* 48px */

  /* ===== Shadows ===== */

  --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.3);
  --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.4);
  --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.5);
  --shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.6);
  --shadow-focus: 0 0 0 3px var(--color-focus-ring);

  /* ===== Border Radius ===== */

  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 8px;
  --radius-xl: 12px;
  --radius-full: 9999px;

  /* ===== Transitions ===== */

  --ease-linear: cubic-bezier(0, 0, 1, 1);
  --ease-in: cubic-bezier(0.4, 0, 1, 1);
  --ease-out: cubic-bezier(0, 0, 0.2, 1);
  --ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);
  --ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);

  --duration-fast: 100ms;
  --duration-normal: 200ms;
  --duration-slow: 300ms;

  /* ===== Z-Index Layers ===== */

  --layer-base: 0;
  --layer-surface: 1;
  --layer-dropdown: 10;
  --layer-sticky: 50;
  --layer-overlay: 100;
  --layer-modal: 500;
  --layer-lightbox: 1000;
  --layer-toast: 2000;
}
```

---

## 🚀 Next Steps

1. **Review with Team**: Share this visual guide and gather feedback
2. **Create Prototypes**: Build interactive prototypes in Figma/HTML
3. **User Testing**: Test navigation patterns with 5-10 users
4. **Iterate**: Refine based on feedback
5. **Implement**: Follow the checklist in `MERIDIAN-UX-REALIGNMENT.md`

---

**Questions?** Refer to the full proposal or design system documentation.
