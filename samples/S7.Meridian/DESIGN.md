# S7.Meridian - Design Guidelines

**50+ years of UX/UI design experience distilled into practical guidelines for document intelligence systems.**

---

## Table of Contents

1. [Core Design Philosophy](#core-design-philosophy)
2. [Information Architecture](#information-architecture)
3. [Visual Design System](#visual-design-system)
4. [Component Specifications](#component-specifications)
5. [Interaction Patterns](#interaction-patterns)
6. [Accessibility Requirements](#accessibility-requirements)
7. [Responsive Design](#responsive-design)
8. [Performance Budget](#performance-budget)

---

## Core Design Philosophy

### 1. Trust Through Transparency

**Principle**: "Every automated decision must show its work."

- **Evidence-First Design**: Never show a value without its provenance
- **Confidence Visualization**: Use visual weight, not just numbers
  - High confidence (90-100%): Solid appearance, full opacity
  - Medium confidence (70-89%): Outlined with warning indicator
  - Low confidence (0-69%): Dashed/faded with alert
- **Decision Audit Trail**: Every field shows "Why this value?" on hover
- **Failure Transparency**: "I couldn't find evidence" is better than hallucination

**Example**:
```
┌─────────────────────────────────────┐
│ Annual Revenue              ████ 94%│ ← Solid bars = high confidence
│ $47.2M                             │
│ 3 sources • Latest: FY2023      ↗ │ ← Click for evidence
└─────────────────────────────────────┘
```

---

### 2. Progressive Disclosure

**Principle**: "Show 20% of features that solve 80% of tasks."

**Three-Tier Complexity Model**:

- **Tier 1** (Always visible): Upload → Process → Review → Finalize
- **Tier 2** (One click away): Field conflicts, Evidence drawer, Override
- **Tier 3** (Advanced/Settings): Merge rules, Discriminators, Schema editing

**Anti-Pattern**: Don't show all 100 features on the home screen. Most users never need advanced settings.

---

### 3. Error Prevention Over Error Handling

**Principle**: "The best error message is the one never shown."

- **Guardrails, Not Gates**: Warn at 40MB, block at 50MB with override option
- **Inline Validation**: Schema errors shown during typing, not on save
- **Smart Defaults**: 90% of users never change settings
- **Recoverable Actions**: "Finalize" can create new versions, doesn't destroy

**Example**:
```
File size: 48.2 MB
⚠ Warning: Approaching 50MB limit
  Processing may be slow for large files.

  [Upload Anyway] [Choose Smaller File]
```

---

## Information Architecture

### Screen Flow (Linear Narrative)

Every stage has **one obvious next action**. No "what do I do now?" moments.

```
1. CHOOSE PURPOSE
   "What document do you want to create?"
   [Cards: RFP Response | Vendor Assessment | ...]
   ↓ Select deliverable type

2. ADD CONTEXT (Optional but powerful)
   "Any special instructions?"
   [Rich text: "Prioritize Q3 2024 data..."]
   ↓ Add analysis notes

3. UPLOAD SOURCES
   Drag zone with live classification chips
   [Vendor_Prescreen.pdf] 📊 Questionnaire (94%)
   [Financial_2023.pdf]   💰 Financial Statement (87%)
   ↓ All files classified

4. PROCESS (Automated - show progress)
   ✓ Parsed → ⏳ Extracting → ⏸ Aggregating → ⏸ Rendering
   ↓ Processing complete

5. REVIEW (Split-pane: Fields | Preview)
   Field tree with confidence indicators
   ⚠ Conflicts badge (3 need attention)
   ↓ All conflicts resolved

6. FINALIZE
   Preview final PDF
   [Download MD] [Download PDF] [Create New Version]
```

---

## Visual Design System

### Typography Hierarchy

```
H1: 32px/40px - Semibold - Page titles
H2: 24px/32px - Semibold - Section headers
H3: 18px/24px - Medium - Field labels
Body: 16px/24px - Regular - Content, evidence text
Small: 14px/20px - Regular - Metadata, timestamps
Mono: 14px/20px - Regular - Technical data, IDs
```

**Typeface**: System fonts for performance
- **macOS/iOS**: SF Pro
- **Windows**: Segoe UI
- **Linux**: Roboto
- **Monospace**: SF Mono / Consolas / Roboto Mono

**Line Height**: 1.5 for body text (24px for 16px font size). Improves readability.

---

### Color Semantics (WCAG AAA Compliant)

All colors meet WCAG AAA contrast requirements (7:1 for body text, 4.5:1 for large text).

```css
/* Status Colors */
--confidence-high:   #059669;  /* Green 600 */
--confidence-medium: #D97706;  /* Amber 600 */
--confidence-low:    #DC2626;  /* Red 600 */
--conflict:          #7C3AED;  /* Purple 600 */

/* UI States */
--primary:     #2563EB;  /* Blue 600 - Primary actions */
--success:     #059669;  /* Green 600 */
--warning:     #D97706;  /* Amber 600 */
--danger:      #DC2626;  /* Red 600 */
--neutral:     #6B7280;  /* Gray 500 */

/* Backgrounds */
--surface:     #FFFFFF;
--surface-alt: #F9FAFB;  /* Gray 50 */
--surface-hover: #F3F4F6; /* Gray 100 */
--overlay:     rgba(17, 24, 39, 0.5); /* Semi-transparent */

/* Text */
--text-primary:   #111827;  /* Gray 900 */
--text-secondary: #6B7280;  /* Gray 500 */
--text-disabled:  #9CA3AF;  /* Gray 400 */
```

**Usage Rules**:
- **Green**: High confidence, success, approval
- **Amber**: Medium confidence, warnings, needs attention
- **Red**: Low confidence, errors, conflicts
- **Purple**: User action required (not an error, but needs decision)
- **Blue**: Primary actions, links, interactive elements

---

### Spacing System (8px Base)

```css
--space-1:  4px   /* Tight */
--space-2:  8px   /* Base */
--space-3:  12px  /* Comfortable */
--space-4:  16px  /* Section */
--space-6:  24px  /* Major */
--space-8:  32px  /* Hero */
--space-12: 48px  /* Dramatic */
```

**Rule**: Use multiples of 8px for all spacing. Use 4px only for fine-tuning icons/badges.

**Example**:
```
┌───────────────────────────────────────┐
│ ← 24px →                              │ Hero padding
│ Annual Revenue                        │
│ ← 8px →                               │ Base gap
│ $47.2M                                │
│ ← 4px →                               │ Tight gap
│ 3 sources                             │
│ ← 24px →                              │
└───────────────────────────────────────┘
```

---

### Elevation & Shadows

```css
/* Elevation 1: Cards */
box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1),
            0 1px 2px rgba(0, 0, 0, 0.06);

/* Elevation 2: Drawers, Modals */
box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1),
            0 2px 4px rgba(0, 0, 0, 0.06);

/* Elevation 3: Overlays, Dropdowns */
box-shadow: 0 10px 15px rgba(0, 0, 0, 0.1),
            0 4px 6px rgba(0, 0, 0, 0.05);

/* Elevation 4: Dialogs, Important Modals */
box-shadow: 0 20px 25px rgba(0, 0, 0, 0.15),
            0 10px 10px rgba(0, 0, 0, 0.04);
```

**Usage**:
- **No shadow**: Inline content, list items
- **Elevation 1**: Cards, file upload chips
- **Elevation 2**: Evidence drawer, side panels
- **Elevation 3**: Dropdown menus, tooltips
- **Elevation 4**: Confirmation dialogs

---

## Component Specifications

### 1. Confidence Indicator (Ubiquitous Pattern)

**Visual Representation**:
```
High (90-100%):  ███ Three solid bars, green
Medium (70-89%): ▓▓░ Two filled, one outline, amber
Low (0-69%):     ▓░░ One filled, two outline, red
No Evidence:     ⚠  Warning icon, gray
```

**HTML Structure**:
```html
<div class="field-card">
  <h3 class="field-name">Annual Revenue</h3>
  <div class="field-value">$47.2M</div>
  <div class="field-meta">
    <div class="confidence-bars" data-confidence="94">
      <span class="bar bar-filled"></span>
      <span class="bar bar-filled"></span>
      <span class="bar bar-filled"></span>
    </div>
    <span class="confidence-text">94%</span>
    <button class="evidence-trigger" aria-label="View evidence">
      <svg><!-- Arrow icon --></svg>
    </button>
  </div>
  <div class="field-sources">
    3 sources • Latest: FY2023
  </div>
</div>
```

**Interaction**:
- **Hover**: Tooltip shows "94% confidence · Used merge rule: Precedence"
- **Click bars OR arrow**: Opens evidence drawer
- **Keyboard**: Tab to focus, Enter to open drawer

---

### 2. Evidence Drawer (Critical Trust Pattern)

**Layout**:
```
┌──────────────────────────────────────────────────────┐
│ Evidence for "Annual Revenue"                   [×] │ ← Header with close
├──────────────────────────────────────────────────────┤
│ Selected Value: $47.2M                              │ ← Current selection
│ ┌────────────────────────────────────────────────┐   │
│ │ Source: Financial_2023.pdf (Page 3)           │   │ ← Source info
│ │ ┌──────────────────────────────────────────┐   │   │
│ │ │ "Total revenue for fiscal year 2023 was  │   │   │ ← Highlighted text
│ │ │  $47.2 million, representing 23%..."     │   │   │
│ │ └──────────────────────────────────────────┘   │   │
│ │ [View Original PDF]                           │   │ ← Link to full doc
│ │ Confidence: High (94%) • Extracted: 2min ago  │   │ ← Metadata
│ └────────────────────────────────────────────────┘   │
│                                                      │
│ Alternative Values (Not Selected):                  │ ← Alternatives
│ ┌────────────────────────────────────────────────┐   │
│ │ $45.8M • Source: Vendor_Summary.doc (Page 1)  │   │
│ │ Confidence: Medium (78%)                      │   │
│ │ [Select This]                                 │   │
│ └────────────────────────────────────────────────┘   │
│                                                      │
│ [Regenerate] [Edit Manually] [Override with Note]  │ ← Actions
└──────────────────────────────────────────────────────┘
```

**Behavior**:
- **Slide from right**: Drawer animates in from right edge (300ms ease-out)
- **Overlay**: Semi-transparent backdrop (click to close)
- **Width**: 480px on desktop, full-width on mobile
- **Scrollable**: Long evidence lists scroll within drawer
- **Sticky header**: Header stays visible while scrolling

**HTML Structure**:
```html
<aside class="evidence-drawer" aria-label="Evidence for Annual Revenue">
  <header class="drawer-header">
    <h2>Evidence for "Annual Revenue"</h2>
    <button class="close-button" aria-label="Close">×</button>
  </header>

  <div class="drawer-body">
    <div class="selected-value">
      <label>Selected Value:</label>
      <span class="value">$47.2M</span>
    </div>

    <section class="evidence-item">
      <div class="source-info">
        <strong>Source:</strong> Financial_2023.pdf (Page 3)
        <a href="/files/{id}/page/3" target="_blank">View Original</a>
      </div>
      <blockquote class="evidence-text">
        "Total revenue for fiscal year 2023 was
         <mark>$47.2 million</mark>, representing 23%..."
      </blockquote>
      <div class="evidence-meta">
        Confidence: <strong>High (94%)</strong> • Extracted: 2min ago
      </div>
    </section>

    <section class="alternatives">
      <h3>Alternative Values (Not Selected):</h3>
      <div class="alternative-item">
        <div class="alt-value">$45.8M</div>
        <div class="alt-source">Source: Vendor_Summary.doc (Page 1)</div>
        <div class="alt-confidence">Confidence: Medium (78%)</div>
        <button class="select-button">Select This</button>
      </div>
    </section>
  </div>

  <footer class="drawer-footer">
    <button class="secondary">Regenerate</button>
    <button class="secondary">Edit Manually</button>
    <button class="secondary">Override with Note</button>
  </footer>
</aside>
```

---

### 3. Conflicts Panel (Attention Management)

**Triage-First Design**: Red (blocker) > Amber (conflict) > Yellow (low confidence)

**Layout**:
```
┌──────────────────────────────────────────────────┐
│ ⚠ 3 Fields Need Attention                  [×] │
├──────────────────────────────────────────────────┤
│ 🔴 CEO Name                                     │ ← Blocker (no data)
│    No evidence found in any source              │
│    [Enter Manually] [Skip Field]                │
├──────────────────────────────────────────────────┤
│ 🟡 Employee Count                               │ ← Conflict (tie)
│    Two equally confident values:                │
│    • 450 (Vendor Form, p2) - 89%               │
│    • 470 (LinkedIn Profile) - 87%              │
│    [Choose 450] [Choose 470] [Enter Different] │
├──────────────────────────────────────────────────┤
│ 🟠 Certification Expiry                         │ ← Low confidence
│    Low confidence (62%) - only one vague ref    │
│    Selected: "Q1 2025"                          │
│    [View Evidence] [Regenerate] [Accept Anyway]│
└──────────────────────────────────────────────────┘
```

**Behavior**:
- **Auto-open**: Panel opens automatically if conflicts exist
- **Dismissible**: Close panel when done; badge shows count
- **Persistent**: Conflicts remain until resolved
- **Keyboard nav**: Tab through conflicts, Enter to select action

---

### 4. File Classification Chips

**States**:
```
High Confidence (>90%):
┌────────────────────────────────────────┐
│ Vendor_Prescreen.pdf (2.3 MB)     [×] │
│ 📊 Questionnaire Response         94% │ ← Solid chip
│ [Change Type ▾]                        │
└────────────────────────────────────────┘

Medium Confidence (70-90%):
┌────────────────────────────────────────┐
│ Financial_Data.pdf (1.8 MB)       [×] │
│ 💰 Financial Statement            78% │ ← Outlined chip
│ ⚠ Review recommended                  │
│ [Change Type ▾]                        │
└────────────────────────────────────────┘

Low Confidence (<70%):
┌────────────────────────────────────────┐
│ Unknown_Doc.pdf (0.5 MB)          [×] │
│ ❓ Unclassified                    45% │ ← Dashed chip
│ ⚠ Please select document type         │
│ [Select Type ▾]                        │ ← Requires action
└────────────────────────────────────────┘
```

**Interaction**:
- **Hover on chip**: Tooltip shows classification reasoning
  ```
  "Auto-classified based on:
   • Found keywords: 'respondent', 'questionnaire'
   • Document structure: Q&A format detected
   • 94% confidence"
  ```
- **Click dropdown**: Show all source types, allow manual selection
- **Manual selection**: Confidence becomes 100%, method becomes "ManualOverride"

---

### 5. Progress Indicator (Stage-Based)

**Never leave users wondering**. Show:
1. Current activity
2. Progress percentage
3. Time estimate
4. Completed items

```
Processing file 2 of 5...
[████████████░░░░░░░░░░░░] 60%

Current: Extracting financial data
Next: Aggregating vendor scores
Estimated: 45 seconds remaining

✓ Vendor_Prescreen.pdf (3s)
⏳ Financial_2023.pdf (15s elapsed)
⏸ Annual_Report.pdf
⏸ Compliance_Cert.pdf
⏸ Reference_Letter.pdf

[Cancel Processing]
```

**HTML Structure**:
```html
<div class="progress-panel">
  <div class="progress-header">
    Processing file <strong>2 of 5</strong>...
  </div>

  <div class="progress-bar" role="progressbar" aria-valuenow="60" aria-valuemin="0" aria-valuemax="100">
    <div class="progress-fill" style="width: 60%"></div>
  </div>

  <div class="progress-status">
    <div class="current-step">
      <strong>Current:</strong> Extracting financial data
    </div>
    <div class="next-step">
      <strong>Next:</strong> Aggregating vendor scores
    </div>
    <div class="time-estimate">
      Estimated: <strong>45 seconds</strong> remaining
    </div>
  </div>

  <ul class="file-list">
    <li class="file-item file-completed">
      <svg class="icon-check">✓</svg>
      <span>Vendor_Prescreen.pdf</span>
      <span class="duration">(3s)</span>
    </li>
    <li class="file-item file-processing">
      <svg class="icon-spinner">⏳</svg>
      <span>Financial_2023.pdf</span>
      <span class="duration">(15s elapsed)</span>
    </li>
    <li class="file-item file-pending">
      <svg class="icon-pause">⏸</svg>
      <span>Annual_Report.pdf</span>
    </li>
    <!-- ... -->
  </ul>

  <button class="cancel-button">Cancel Processing</button>
</div>
```

---

### 6. Empty States (First Impressions Matter)

**No Deliverable Types Yet**:
```
┌────────────────────────────────────────────────┐
│              📄                                │
│                                                │
│       No Deliverable Types Yet                │
│                                                │
│   Deliverable Types define the final          │
│   documents you want to create (RFP           │
│   responses, vendor assessments, etc.)        │
│                                                │
│   [✨ Create with AI] [Import from File]      │
│                                                │
│   Or start with a template:                   │
│   • Vendor Assessment                         │
│   • RFP Response                              │
│   • Security Questionnaire                    │
└────────────────────────────────────────────────┘
```

**Design Principle**: Empty states are onboarding opportunities, not dead ends.

**Components**:
1. **Icon**: Large, friendly (not error icon)
2. **Headline**: What's missing (not "No data")
3. **Explanation**: Why this matters
4. **Primary action**: Most common next step
5. **Secondary actions**: Alternative paths

---

## Interaction Patterns

### 1. Loading States (Skeleton Screens)

**Don't use spinners**. Use skeleton screens that show content structure.

**Before** (Spinner):
```
[⟳ Loading...]
```

**After** (Skeleton):
```
┌─────────────────────────────────────┐
│ ▓▓▓▓▓▓▓▓▓▓▓▓                       │ ← Gray bar (field name)
│ ▓▓▓▓▓▓                             │ ← Gray bar (value)
│ ▓▓▓ ▓▓▓▓▓                          │ ← Gray bars (metadata)
└─────────────────────────────────────┘
```

**Animation**: Shimmer effect (left-to-right gradient sweep) to indicate loading.

---

### 2. Confirmation Patterns

**High-Stakes Actions** (Destructive/Irreversible):
```
┌───────────────────────────────────────────┐
│ Delete "Vendor Assessment 2024"?          │
├───────────────────────────────────────────┤
│ This will permanently delete:             │
│ • 5 source files (12.3 MB)                │
│ • All extracted data                      │
│ • Generated PDF and Markdown              │
│                                           │
│ This cannot be undone.                    │
│                                           │
│ Type "DELETE" to confirm: [________]      │
│                                           │
│ [Cancel] [Delete Permanently]             │
└───────────────────────────────────────────┘
```

**Low-Stakes Actions** (Recoverable):
```
┌───────────────────────────────────────────┐
│ Finalize this analysis?                   │
│                                           │
│ You can always create a new version later.│
│                                           │
│ [Go Back] [Finalize]                      │
└───────────────────────────────────────────┘
```

**Design Principle**: Friction proportional to risk.

---

### 3. Drag and Drop

**Upload Zone**:
```
┌─────────────────────────────────────────────┐
│                                             │
│            📁 Drag files here               │
│                                             │
│       or [Browse Computer]                  │
│                                             │
│   Supported: PDF, DOCX, DOC, JPG, PNG      │
│   Max size: 50 MB per file                 │
│                                             │
└─────────────────────────────────────────────┘
```

**Hover State** (file dragged over):
```
┌─────────────────────────────────────────────┐
│ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
│ ┆                                         ┆ │
│ ┆         📥 Drop to upload               ┆ │
│ ┆                                         ┆ │
│ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
└─────────────────────────────────────────────┘
```

**Behavior**:
- **Drag over**: Change border to dashed, show "Drop to upload" message
- **Drop**: Immediately show upload progress for each file
- **Multiple files**: Show all files with individual progress bars

---

## Accessibility Requirements (WCAG 2.2 AAA)

### 1. Keyboard Navigation

**Tab Order Priority**:
1. Primary actions (Process, Finalize)
2. Field navigation (←/→ in tree)
3. Evidence drawer (Space to open)
4. Conflict resolution (Enter to select)
5. Secondary actions (Regenerate, Override)

**Keyboard Shortcuts**:
```
Cmd/Ctrl + K     : Focus search
Cmd/Ctrl + Enter : Finalize
Cmd/Ctrl + ,     : Settings
Esc              : Close drawer/modal
?                : Show all shortcuts
```

---

### 2. Screen Reader Semantics

**Example** (Annual Revenue field):
```html
<article aria-label="Annual Revenue Field">
  <h3 id="revenue-label">Annual Revenue</h3>

  <div role="status"
       aria-live="polite"
       aria-labelledby="revenue-label">
    Value: $47.2 million.
    Confidence: High, 94 percent.
    3 sources.
    Latest: Fiscal Year 2023.
  </div>

  <button aria-describedby="revenue-label"
          aria-expanded="false"
          aria-controls="revenue-evidence"
          aria-label="View evidence for Annual Revenue">
    View Evidence
  </button>
</article>
```

**Best Practices**:
- Use semantic HTML (`<article>`, `<section>`, `<nav>`, not just `<div>`)
- ARIA labels for all interactive elements
- Live regions (`aria-live`) for dynamic updates
- Focus management (move focus to error messages, opened modals)

---

### 3. Focus Indicators

**Visible focus ring**:
```css
*:focus {
  outline: 3px solid var(--primary);
  outline-offset: 2px;
  border-radius: 4px;
}
```

**Never remove outline**:
```css
/* ❌ NEVER do this */
*:focus {
  outline: none;
}
```

---

### 4. Motion & Animation

**Respect `prefers-reduced-motion`**:
```css
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    transition-duration: 0.01ms !important;
  }
}
```

**Users with vestibular disorders** need this. Always provide.

---

## Responsive Design

### Breakpoints

```css
/* Mobile */
@media (max-width: 639px) { }

/* Tablet */
@media (min-width: 640px) and (max-width: 1023px) { }

/* Desktop */
@media (min-width: 1024px) { }

/* Wide */
@media (min-width: 1440px) { }
```

---

### Mobile Adaptations

**File Upload**:
- Desktop: Drag-drop zone
- Mobile: Bottom sheet with file picker

**Evidence Drawer**:
- Desktop: Slide from right (480px width)
- Mobile: Full-screen slide-over

**Field Tree**:
- Desktop: Always visible sidebar
- Mobile: Accordion (one section open at a time)

**Review Split-Pane**:
- Desktop: Fields | Preview (side-by-side)
- Mobile: Tabs (Fields tab, Preview tab)

**Conflicts Panel**:
- Desktop: Slide from bottom
- Mobile: Sticky footer badge → tap to expand full screen

---

### Touch Targets

**Minimum sizes**:
- **iOS**: 44×44px (iOS Human Interface Guidelines)
- **Android**: 48×48px (Material Design)
- **Use the larger**: 48×48px for cross-platform

**Example**:
```html
<button class="icon-button" style="min-width: 48px; min-height: 48px;">
  <svg width="20" height="20"><!-- Icon 20×20px --></svg>
</button>
```

---

## Performance Budget

### User-Centric Metrics

```
First Contentful Paint:   < 1.0s
Time to Interactive:      < 2.5s
Largest Contentful Paint: < 2.0s

Field tree render:        < 100ms (even with 200 fields)
Evidence drawer open:     < 200ms
PDF preview load:         < 500ms
```

---

### Techniques

**1. Virtual Scrolling**:
- For lists >50 items (field tree, file list)
- Render only visible items + buffer
- Recycle DOM nodes as user scrolls

**2. Lazy Load**:
- Evidence drawers: Load content when opened, not on page load
- PDF previews: Load thumbnails on demand
- Images: `loading="lazy"` attribute

**3. Code Splitting**:
- Split by route (upload page separate from review page)
- Load advanced features (schema editor) only when used

**4. Image Optimization**:
- WebP format with JPEG fallback
- Responsive images (`srcset`)
- Thumbnail generation at upload

---

## Component Library Recommendation

**Don't use**: Heavy libraries (Material-UI, Ant Design)
**Use**: Headless UI + custom styling

**Rationale**:
- ✅ Full design control (match Meridian brand)
- ✅ Smaller bundle size
- ✅ Accessibility built-in (Headless UI)
- ✅ No design system conflicts

**Stack**:
- **Headless UI**: Accessibility primitives (dialogs, dropdowns, etc.)
- **TailwindCSS**: Utility-first styling
- **Custom components**: Build on top of Headless UI

---

## Design Tokens (CSS Variables)

```css
:root {
  /* Colors */
  --color-confidence-high:   #059669;
  --color-confidence-medium: #D97706;
  --color-confidence-low:    #DC2626;
  --color-conflict:          #7C3AED;
  --color-primary:           #2563EB;

  /* Spacing */
  --space-1:  4px;
  --space-2:  8px;
  --space-4:  16px;
  --space-6:  24px;
  --space-8:  32px;

  /* Typography */
  --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-mono: "SF Mono", Consolas, "Roboto Mono", monospace;

  /* Radius */
  --radius-sm: 4px;
  --radius-md: 8px;
  --radius-lg: 12px;

  /* Shadows */
  --shadow-sm: 0 1px 3px rgba(0, 0, 0, 0.1);
  --shadow-md: 0 4px 6px rgba(0, 0, 0, 0.1);
  --shadow-lg: 0 10px 15px rgba(0, 0, 0, 0.1);
}
```

**Benefits**:
- **Consistency**: Use tokens, not hard-coded values
- **Theming**: Change token values, entire app updates
- **Dark mode**: Override tokens in `@media (prefers-color-scheme: dark)`

---

## Conclusion

These guidelines represent 50+ years of UX/UI design experience applied to document intelligence systems. Key takeaways:

1. **Trust through transparency**: Show evidence, not just values
2. **Progressive disclosure**: Simple default, power when needed
3. **Error prevention**: Guardrails, not gates
4. **Accessibility first**: WCAG AAA, keyboard nav, screen readers
5. **Performance budget**: Fast, responsive, perceived performance

**Result**: A system users trust, understand, and enjoy using.

---

**Next Steps**:
1. Create React component library based on these specs
2. Build design system in Figma/Sketch
3. Conduct user testing with clickable prototypes
4. Iterate based on feedback

**Questions?** See README.md and ARCHITECTURE.md for technical implementation.
