# SnapVault Pro Design System

**Version:** 1.0
**Last Updated:** October 2025
**Status:** Production

---

## Philosophy

SnapVault Pro's design language prioritizes **clarity, professionalism, and efficiency**. Every interface element serves the photographer's workflow—from rapid photo sorting to detailed metadata review.

### Core Principles

1. **Consistency Over Novelty** - Predictable patterns reduce cognitive load
2. **Data Density With Breathing Room** - Information-rich without feeling cramped
3. **Professional Dark Theme** - Optimized for long editing sessions
4. **Keyboard-First, Mouse-Friendly** - Power users need shortcuts, casual users need discoverability
5. **Progressive Disclosure** - Show essentials first, details on demand

---

## Design Tokens

### Color System

#### Semantic Colors

```css
/* Surface Colors - Dark Theme Optimized */
--color-canvas: #0A0A0A;           /* Body background, deepest black */
--color-surface: #141414;          /* Panels, cards, elevated surfaces */
--color-surface-hover: #1A1A1A;    /* Interactive surface hover state */
--color-surface-active: #222222;   /* Pressed/active state */
--color-surface-subtle: #18181B;   /* Zinc-900, for nested containers */

/* Border Colors - Subtle Hierarchy */
--color-border-subtle: #2A2A2A;    /* Dividers, container outlines */
--color-border-medium: #3A3A3A;    /* Focused borders */
--color-border-strong: #4A4A4A;    /* Emphasized borders */
--color-border-interactive: #3F3F46; /* Zinc-700, interactive elements */

/* Text Colors - WCAG AAA Compliant */
--color-text-primary: #E8E8E8;     /* High contrast, body text */
--color-text-secondary: #A8A8A8;   /* Medium contrast, labels */
--color-text-tertiary: #787878;    /* Low contrast, hints */
--color-text-disabled: #4A4A4A;    /* Disabled state */
--color-text-inverse: #0A0A0A;     /* Text on light backgrounds */

/* Accent Colors - Purpose-Driven */
--color-accent-primary: #5B9FFF;   /* Blue - Primary actions, links */
--color-accent-semantic: #A78BFA;  /* Purple - AI/semantic features */
--color-accent-success: #4ADE80;   /* Green - Success states */
--color-accent-warning: #FBBF24;   /* Amber - Warnings */
--color-accent-danger: #F87171;    /* Red - Destructive actions */
--color-accent-favorite: #FFC947;  /* Gold - Stars, favorites */

/* State Colors */
--color-focus-ring: rgba(91, 159, 255, 0.4);  /* Focus indicator */
--color-selection: rgba(91, 159, 255, 0.15);  /* Selected items */
```

#### Component-Specific Colors

```css
/* Tag Pills (inspired by AI Insights) */
--color-tag-bg: rgba(59, 130, 246, 0.15);
--color-tag-border: rgba(59, 130, 246, 0.3);
--color-tag-text: #60A5FA;
--color-tag-hover-bg: rgba(59, 130, 246, 0.25);
--color-tag-active-bg: #3B82F6;
--color-tag-active-text: #FFFFFF;

/* Active Filter Pills */
--color-filter-pill-bg: #3B82F6;
--color-filter-pill-text: #FFFFFF;
--color-filter-pill-close-bg: rgba(255, 255, 255, 0.2);
--color-filter-pill-close-hover: rgba(255, 255, 255, 0.3);

/* Section Headers */
--color-section-header: #71717A;   /* Zinc-500, muted uppercase labels */
--color-section-border: rgba(63, 63, 70, 0.5); /* Zinc-700 with transparency */
```

---

### Typography

#### Font Families

```css
--font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto,
             'Helvetica Neue', Arial, sans-serif,
             'Apple Color Emoji', 'Segoe UI Emoji';
--font-mono: 'SF Mono', Monaco, 'Cascadia Code', 'Courier New', monospace;
```

#### Type Scale (Modular Scale: 1.2 ratio)

```css
/* Hierarchy */
--text-xs: 0.6875rem;    /* 11px - Uppercase labels, metadata */
--text-sm: 0.8125rem;    /* 13px - Body text, form inputs */
--text-base: 0.9375rem;  /* 15px - Primary content */
--text-lg: 1.125rem;     /* 18px - Headings */
--text-xl: 1.5rem;       /* 24px - Page titles */
--text-2xl: 2rem;        /* 32px - Hero text */

/* Weights */
--weight-normal: 400;
--weight-medium: 500;
--weight-semibold: 600;
--weight-bold: 700;

/* Line Heights */
--leading-tight: 1.25;    /* Headings */
--leading-normal: 1.5;    /* Body text */
--leading-relaxed: 1.75;  /* Long-form content */

/* Letter Spacing */
--tracking-tight: -0.025em;  /* Large headings */
--tracking-normal: 0;        /* Body text */
--tracking-wide: 0.05em;     /* Uppercase labels */
--tracking-wider: 0.1em;     /* Section headers */
```

---

### Spacing System (8px Grid)

```css
/* Base unit: 8px */
--space-0: 0;
--space-1: 0.5rem;   /* 8px - Tight spacing */
--space-2: 1rem;     /* 16px - Standard gap */
--space-3: 1.5rem;   /* 24px - Section spacing */
--space-4: 2rem;     /* 32px - Large gaps */
--space-5: 2.5rem;   /* 40px - Extra large */
--space-6: 3rem;     /* 48px - Major sections */

/* Component-Specific */
--space-section-header: 24px 0 12px 0;  /* Standard section header margin */
--space-panel-padding: 20px;            /* Panel/card internal padding */
--space-input-padding: 8px 16px;        /* Form input padding */
```

---

### Elevation & Shadows

```css
/* Shadows - Subtle depth in dark theme */
--shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.3);
--shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.4);
--shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.5);
--shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.6);

/* Focus Shadow */
--shadow-focus: 0 0 0 3px var(--color-focus-ring);

/* Z-Index Layers */
--layer-base: 0;
--layer-surface: 1;
--layer-dropdown: 10;
--layer-sticky: 50;
--layer-overlay: 100;
--layer-modal: 500;
--layer-lightbox: 1000;
--layer-toast: 2000;
```

---

### Border Radius

```css
--radius-sm: 4px;    /* Checkboxes, small buttons */
--radius-md: 6px;    /* Standard inputs, cards */
--radius-lg: 8px;    /* Panels, sections */
--radius-xl: 12px;   /* Modals, large containers */
--radius-full: 9999px; /* Pills, circular buttons */
```

---

### Motion & Transitions

```css
/* Easing Functions */
--ease-linear: cubic-bezier(0, 0, 1, 1);
--ease-in: cubic-bezier(0.4, 0, 1, 1);
--ease-out: cubic-bezier(0, 0, 0.2, 1);
--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);
--ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);
--ease-spring: cubic-bezier(0.34, 1.56, 0.64, 1);

/* Durations */
--duration-instant: 0ms;
--duration-fast: 100ms;
--duration-normal: 200ms;
--duration-slow: 300ms;
--duration-slower: 500ms;

/* Standard Transition */
--transition-default: all 200ms cubic-bezier(0, 0, 0.2, 1);
--transition-color: color 150ms ease, background-color 150ms ease;
--transition-transform: transform 200ms cubic-bezier(0.34, 1.56, 0.64, 1);
```

---

## Component Patterns

### Section Header

**Purpose:** Divide content into logical groups with clear hierarchy.

**Anatomy:**
```
SECTION TITLE       ← Uppercase, muted color, tracking-wider
─────────────────  ← Optional divider
Content...
```

**Usage:**
```html
<div class="section">
  <h4 class="section-header">DETAILS</h4>
  <div class="section-content">
    <!-- Content -->
  </div>
</div>
```

**Specs:**
- Font size: `--text-xs` (11px)
- Font weight: `--weight-semibold` (600)
- Color: `--color-section-header` (#71717A)
- Letter spacing: `--tracking-wider` (0.1em)
- Text transform: `uppercase`
- Margin: `24px 0 12px 0`

---

### Card/Panel Container

**Purpose:** Group related content with visual separation from canvas.

**Anatomy:**
```
┌─────────────────┐
│ Content         │  ← Background, border, padding
└─────────────────┘
```

**Usage:**
```html
<div class="panel">
  <!-- Panel content -->
</div>
```

**Specs:**
- Background: `rgba(39, 39, 42, 0.4)` (Zinc-800 with opacity)
- Border: `1px solid rgba(63, 63, 70, 0.5)` (Zinc-700)
- Border radius: `--radius-lg` (8px)
- Padding: `--space-panel-padding` (20px)
- Margin bottom: `--space-2` (16px)

**Hover State:**
- Border color: `rgba(82, 82, 91, 0.7)` (Zinc-600)
- Transition: `border-color 200ms ease`

---

### Tag Pill (Dual Mode)

**Purpose:** Display tags, filters, or categories with two states: selectable and removable.

**Anatomy:**
```
[Tag Name]          ← Selectable mode (toggles active state)
[Tag Name ×]        ← Removable mode (shows close button)
```

**Usage:**
```html
<!-- Selectable -->
<button class="tag-pill" data-tag="portrait">portrait</button>

<!-- Removable -->
<span class="tag-pill active">
  portrait
  <button class="tag-pill-close">×</button>
</span>
```

**Specs - Default State:**
- Background: `rgba(59, 130, 246, 0.15)` (Blue with 15% opacity)
- Border: `1px solid rgba(59, 130, 246, 0.3)`
- Color: `#60A5FA` (Blue-400)
- Padding: `6px 12px`
- Border radius: `--radius-md` (6px)
- Font size: `--text-sm` (13px)
- Font weight: `--weight-medium` (500)

**Specs - Hover State:**
- Background: `rgba(59, 130, 246, 0.25)`
- Border: `1px solid rgba(59, 130, 246, 0.5)`

**Specs - Active State:**
- Background: `#3B82F6` (Blue-500 solid)
- Border: `1px solid #3B82F6`
- Color: `#FFFFFF`

---

### Filter Pill (Active Filters)

**Purpose:** Display currently active filters with remove functionality.

**Anatomy:**
```
[Filter Name ×]  ← Always shows close button, prominent color
```

**Usage:**
```html
<div class="active-filters">
  <span class="filter-pill">
    Canon EOS R5
    <button class="pill-remove">×</button>
  </span>
</div>
```

**Specs:**
- Background: `#3B82F6` (Blue-500)
- Color: `#FFFFFF`
- Padding: `4px 10px`
- Border radius: `--radius-full` (9999px - pill shape)
- Font size: `--text-xs` (11px)
- Font weight: `--weight-medium` (500)
- Gap between text and button: `6px`

**Close Button:**
- Size: `16px × 16px`
- Background: `rgba(255, 255, 255, 0.2)`
- Color: `#FFFFFF`
- Border radius: `50%` (circular)
- Hover background: `rgba(255, 255, 255, 0.3)`

---

### Form Input (Text, Date, Search)

**Purpose:** Standard text input with consistent styling.

**Usage:**
```html
<input type="text" class="form-input" placeholder="Search cameras..." />
<input type="date" class="form-input" />
<input type="search" class="form-input" />
```

**Specs - Default:**
- Background: `--color-canvas` (#0A0A0A)
- Border: `1px solid --color-border-subtle` (#2A2A2A)
- Border radius: `--radius-md` (6px)
- Padding: `--space-input-padding` (8px 16px)
- Font size: `--text-sm` (13px)
- Color: `--color-text-primary` (#E8E8E8)
- Transition: `all 200ms ease`

**Specs - Focus:**
- Border color: `--color-accent-primary` (#5B9FFF)
- Background: `--color-surface-hover` (#1A1A1A)
- Box shadow: `--shadow-focus`

**Specs - Placeholder:**
- Color: `--color-text-tertiary` (#787878)

---

### Checkbox

**Purpose:** Binary selection control.

**Usage:**
```html
<label class="checkbox-label">
  <input type="checkbox" class="checkbox" />
  <span class="checkbox-text">Include unrated</span>
</label>
```

**Specs:**
- Size: `16px × 16px`
- Border radius: `--radius-sm` (4px)
- Accent color: `--color-accent-primary` (#5B9FFF)
- Cursor: `pointer`

**Label Container:**
- Display: `flex`
- Align items: `center`
- Gap: `--space-1` (8px)
- Padding: `--space-1` (8px)
- Border radius: `--radius-sm` (4px)
- Cursor: `pointer`

**Hover State:**
- Background: `--color-surface-hover` (#1A1A1A)

**Checked State:**
- Font weight: `--weight-medium` (500)
- Text color: `--color-text-primary` (#E8E8E8)

---

### Radio Button Group

**Purpose:** Mutually exclusive selection (e.g., AND/OR toggle).

**Usage:**
```html
<div class="radio-group">
  <label class="radio-label">
    <input type="radio" name="mode" value="all" class="radio" />
    <span>All tags</span>
  </label>
  <label class="radio-label">
    <input type="radio" name="mode" value="any" class="radio" />
    <span>Any tag</span>
  </label>
</div>
```

**Specs - Container:**
- Display: `flex`
- Gap: `--space-2` (16px)
- Padding: `--space-1` (8px)
- Background: `--color-surface` (#141414)
- Border radius: `--radius-md` (6px)

**Specs - Radio Input:**
- Size: `14px × 14px`
- Accent color: `--color-accent-primary` (#5B9FFF)

**Specs - Label:**
- Font size: `--text-xs` (11px)
- Color: `--color-text-secondary` (#A8A8A8)
- Display: `flex`
- Align items: `center`
- Gap: `6px`

**Checked State:**
- Color: `--color-text-primary` (#E8E8E8)
- Font weight: `--weight-semibold` (600)

---

### Button (Primary, Secondary, Ghost)

**Purpose:** Actions and navigation.

**Types:**

**Primary:**
```html
<button class="btn btn-primary">Apply Filters</button>
```
- Background: `--color-accent-primary` (#5B9FFF)
- Color: `#FFFFFF`
- Padding: `10px 20px`
- Border radius: `--radius-md` (6px)
- Font weight: `--weight-medium` (500)
- Hover: Lighter background, slight scale

**Secondary:**
```html
<button class="btn btn-secondary">Cancel</button>
```
- Background: `--color-surface-hover` (#1A1A1A)
- Color: `--color-text-primary` (#E8E8E8)
- Border: `1px solid --color-border-medium` (#3A3A3A)

**Ghost (Icon + Text):**
```html
<button class="btn btn-ghost">
  <svg>...</svg>
  Reset Filters
</button>
```
- Background: `transparent`
- Color: `--color-text-secondary` (#A8A8A8)
- Hover: Background: `--color-surface-hover`

---

### Slider (Range Input)

**Purpose:** Continuous value selection (e.g., rating threshold).

**Usage:**
```html
<div class="slider-container">
  <label class="slider-label">Minimum Rating</label>
  <input type="range" min="0" max="5" class="slider" />
  <div class="slider-value">★★★★☆ (4 stars)</div>
</div>
```

**Specs - Track:**
- Height: `6px`
- Background: `linear-gradient(to right, #2A2A2A 0%, #FFC947 100%)`
- Border radius: `--radius-full` (9999px)

**Specs - Thumb:**
- Size: `18px × 18px`
- Background: `--color-accent-favorite` (#FFC947)
- Border: `2px solid --color-canvas` (#0A0A0A)
- Border radius: `50%` (circular)
- Box shadow: `--shadow-md`
- Cursor: `pointer`

**Hover State:**
- Transform: `scale(1.1)`
- Box shadow: `--shadow-lg`

**Value Display:**
- Font size: `--text-sm` (13px)
- Color: `--color-text-secondary` (#A8A8A8)
- Margin top: `--space-1` (8px)

---

### Preset Button Group (Date Presets)

**Purpose:** Quick selection from common options.

**Usage:**
```html
<div class="preset-group">
  <button class="preset-btn">Today</button>
  <button class="preset-btn">Week</button>
  <button class="preset-btn active">Month</button>
  <button class="preset-btn">Year</button>
</div>
```

**Specs - Container:**
- Display: `flex`
- Gap: `--space-1` (8px)
- Flex wrap: `wrap`

**Specs - Button:**
- Flex: `1`
- Min width: `60px`
- Padding: `6px 10px`
- Background: `--color-surface-hover` (#1A1A1A)
- Border: `1px solid --color-border-subtle` (#2A2A2A)
- Border radius: `--radius-md` (6px)
- Font size: `--text-xs` (11px)
- Font weight: `--weight-medium` (500)
- Color: `--color-text-secondary` (#A8A8A8)

**Hover State:**
- Background: `--color-surface-active` (#222222)
- Border color: `--color-border-medium` (#3A3A3A)
- Color: `--color-text-primary` (#E8E8E8)

**Active State:**
- Background: `--color-accent-primary` (#5B9FFF)
- Border color: `--color-accent-primary`
- Color: `#FFFFFF`

---

### Active Filters Summary

**Purpose:** Show all active filters with quick removal.

**Usage:**
```html
<div class="active-filters-summary">
  <span class="filter-count">ACTIVE (3)</span>
  <span class="filter-pill">Canon ×</span>
  <span class="filter-pill">4+ stars ×</span>
  <span class="filter-pill">October ×</span>
  <button class="clear-all">Clear all</button>
</div>
```

**Specs - Container:**
- Background: `rgba(59, 130, 246, 0.1)`
- Border: `1px solid rgba(59, 130, 246, 0.3)`
- Border radius: `--radius-lg` (8px)
- Padding: `12px`
- Display: `flex`
- Flex wrap: `wrap`
- Gap: `8px`
- Align items: `center`
- Margin bottom: `--space-3` (24px)

**Specs - Filter Count:**
- Font size: `--text-xs` (11px)
- Font weight: `--weight-semibold` (600)
- Color: `#60A5FA` (Blue-400)
- Letter spacing: `--tracking-wide` (0.05em)

**Specs - Clear All Button:**
- Font size: `--text-xs` (11px)
- Color: `--color-text-tertiary` (#787878)
- Text decoration: `underline`
- Background: `transparent`
- Hover color: `--color-text-primary` (#E8E8E8)

---

### Result Count Footer

**Purpose:** Show filtering results in real-time.

**Usage:**
```html
<div class="result-count">
  Showing <strong>234</strong> of <strong>1,520</strong> photos
</div>
```

**Specs:**
- Text align: `center`
- Font size: `--text-sm` (13px)
- Color: `--color-text-secondary` (#A8A8A8)
- Padding top: `--space-2` (16px)
- Border top: `1px solid rgba(63, 63, 70, 0.5)`
- Margin top: `--space-2` (16px)

**Strong Numbers:**
- Color: `--color-text-primary` (#E8E8E8)
- Font weight: `--weight-semibold` (600)

---

## Layout Patterns

### Sidebar Panel Structure

```
┌─────────────────────────────┐
│ Panel Header            [×] │  ← Close button (modals only)
├─────────────────────────────┤
│                             │
│ [Active Filters Summary]    │  ← Optional, only if filters active
│                             │
│ SECTION HEADER              │  ← Uppercase label
│ ┌─────────────────────────┐ │
│ │ Section content         │ │  ← Container with padding
│ └─────────────────────────┘ │
│                             │
│ SECTION HEADER              │
│ ┌─────────────────────────┐ │
│ │ Section content         │ │
│ └─────────────────────────┘ │
│                             │
│ [Action buttons]            │  ← Reset, Apply, etc.
│                             │
│ Result count                │  ← Feedback
└─────────────────────────────┘
```

**Sidebar Width:**
- Min: `280px`
- Max: `360px`
- Preferred: `320px`

**Padding:**
- Panel: `20px`
- Sections: `16px`

**Gaps:**
- Between sections: `16px`
- Within sections: `12px`

---

### Modal Panel Structure

```
┌─────────────────────────────┐
│ Modal Title             [×] │  ← Header with close
├─────────────────────────────┤
│                             │
│ Content sections...         │
│                             │
│                             │
├─────────────────────────────┤
│ [Cancel]         [Confirm]  │  ← Footer actions
└─────────────────────────────┘
```

**Max Width:** `600px`
**Max Height:** `90vh`
**Overflow:** `auto` (for content)

---

### Two-Column Data Grid (Photo Info Pattern)

```html
<div class="data-grid">
  <div class="data-row">
    <span class="data-label">CAPTURED</span>
    <span class="data-value">Oct 17, 2025</span>
  </div>
  <div class="data-row">
    <span class="data-label">DIMENSIONS</span>
    <span class="data-value">2160 × 3840</span>
  </div>
</div>
```

**Specs:**
- Display: `grid`
- Grid template columns: `140px 1fr`
- Gap: `12px`
- Row gap: `16px`

**Label:**
- Font size: `--text-xs` (11px)
- Font weight: `--weight-semibold` (600)
- Color: `--color-section-header` (#71717A)
- Letter spacing: `--tracking-wide` (0.05em)
- Text transform: `uppercase`

**Value:**
- Font size: `--text-base` (15px)
- Color: `--color-text-primary` (#E8E8E8)

---

## Accessibility Standards

### Focus Indicators

All interactive elements MUST have visible focus indicators:

```css
*:focus-visible {
  outline: 2px solid var(--color-accent-primary);
  outline-offset: 2px;
}
```

### Color Contrast

All text must meet WCAG AAA standards:
- **Large text (18px+):** 4.5:1 minimum
- **Body text:** 7:1 minimum
- **Interactive elements:** 3:1 minimum

### Keyboard Navigation

**Required shortcuts:**
- `Tab` / `Shift+Tab` - Navigate between controls
- `Enter` / `Space` - Activate buttons/checkboxes
- `Escape` - Close modals, clear focus
- `Arrow keys` - Navigate radio groups, sliders

### Screen Reader Support

All components MUST have:
- Semantic HTML (`<button>`, `<label>`, `<section>`)
- ARIA labels for icon-only buttons
- ARIA live regions for dynamic content
- Role attributes when semantic HTML isn't sufficient

---

## Usage Guidelines

### When to Use Tag Pills vs Filter Pills

**Tag Pills:**
- ✅ Selectable options (toggle on/off)
- ✅ Browsing available values
- ✅ Non-destructive actions
- Example: Tag browser, AI insights

**Filter Pills:**
- ✅ Active selections being applied
- ✅ Quick removal needed
- ✅ Summary/status display
- Example: Active filters summary, search filters

### When to Use Sections

**Use section containers when:**
- ✅ Grouping 3+ related items
- ✅ Creating visual hierarchy
- ✅ Content can be collapsed/expanded
- ✅ Separating distinct concepts

**Don't use sections for:**
- ❌ Single standalone controls
- ❌ Purely cosmetic grouping
- ❌ Over-nesting (max 2 levels)

### When to Use Preset Buttons

**Use preset button groups when:**
- ✅ 2-5 common options exist
- ✅ Custom input is also available
- ✅ Users want quick selection
- Example: Date ranges (Today, Week, Month, Year)

**Don't use when:**
- ❌ More than 6 options (use dropdown)
- ❌ Options are complex (use radio group)
- ❌ Only one standard option exists

---

## Component Checklist

Before creating a new component, verify:

- [ ] Uses design tokens (no hardcoded colors/sizes)
- [ ] Follows spacing system (8px grid)
- [ ] Has hover/active/focus states
- [ ] Meets WCAG AAA contrast requirements
- [ ] Works with keyboard navigation
- [ ] Has ARIA labels where needed
- [ ] Responsive to different panel widths
- [ ] Consistent with similar existing components
- [ ] Documented with usage examples
- [ ] Tested in dark theme

---

## Migration Plan

### Phase 1: Establish Foundation
1. Create `design-tokens.css` with all CSS custom properties
2. Update `app.css` to import design tokens
3. Document all existing components

### Phase 2: Component Library
4. Create `/components` directory structure
5. Build reusable component classes
6. Create component showcase/documentation

### Phase 3: Rebuild Filters
7. Rebuild filters.js using design system
8. Update filter CSS to use components
9. Add missing patterns (active filters summary, etc.)

### Phase 4: Consistency Audit
10. Audit all panels against design system
11. Update inconsistent components
12. Document any new patterns discovered

---

## Resources

### Inspiration Sources
- **Apple Photos** - Section headers, data grid pattern
- **Google Photos** - Tag pills, filter chips
- **Adobe Lightroom** - Dark theme colors, metadata display
- **Figma** - Panel structure, input styling

### Design Tools
- **Coolors.co** - Color palette generation
- **Type Scale** - Typography hierarchy
- **WebAIM Contrast Checker** - WCAG compliance

### Code References
- **Photo Information Panel** (`lightbox-panel.css`) - Reference implementation
- **Tailwind CSS** - Component patterns and naming conventions
- **Radix UI** - Accessibility patterns

---

**This is a living document.** All changes to the design system must be documented here before implementation.
