# Settings & Maintenance Page - UX Design Specification

## Design Philosophy

**Target Users:** Professional photographers, photo agencies, creative teams managing large photo libraries

**Core Principles:**
1. **Safety First:** Multi-step confirmations for destructive actions
2. **Clarity:** Every action has clear consequences explained
3. **Efficiency:** Common tasks are quick, dangerous tasks require deliberation
4. **Professionalism:** Clean, modern interface that builds trust

---

## Visual Layout

### Page Structure (Responsive Grid)

```
┌─────────────────────────────────────────────────────────────────┐
│ Header: SnapVault Pro > Settings > Storage & Data               │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─ Storage Overview ─────────────────────────┐                 │
│  │  📊 Visual storage breakdown chart          │                 │
│  │  • Hot Tier: 2.3 GB (CDN/Thumbnails)        │                 │
│  │  • Warm Tier: 8.7 GB (Gallery)              │                 │
│  │  • Cold Tier: 45.2 GB (Originals)           │                 │
│  │  Total: 56.2 GB / 500 GB (11% used)         │                 │
│  └───────────────────────────────────────────┘                  │
│                                                                   │
│  ┌─ Maintenance Actions ─────────────────────┐                  │
│  │  🔄 Rebuild Search Index                   │                 │
│  │     Last indexed: 2 hours ago              │                 │
│  │     [Rebuild Now]                          │                 │
│  │                                             │                 │
│  │  🧹 Clear AI Embedding Cache                │                 │
│  │     2,847 cached embeddings (127 MB)       │                 │
│  │     [Clear Cache]                          │                 │
│  │                                             │                 │
│  │  ⚡ Optimize Database                       │                 │
│  │     Compact and rebuild indexes            │                 │
│  │     [Optimize Now]                         │                 │
│  └───────────────────────────────────────────┘                  │
│                                                                   │
│  ┌─ Data Export & Backup ────────────────────┐                  │
│  │  📦 Export Photo Metadata                   │                 │
│  │     Download JSON archive of all metadata  │                 │
│  │     [Export Metadata]                      │                 │
│  │                                             │                 │
│  │  💾 Backup Configuration                    │                 │
│  │     Export settings and preferences        │                 │
│  │     [Backup Config]                        │                 │
│  └───────────────────────────────────────────┘                  │
│                                                                   │
│  ⚠️  DANGER ZONE                                                 │
│  ┌─────────────────────────────────────────────────────┐        │
│  │  🗑️  Wipe Entire Repository                          │        │
│  │                                                       │        │
│  │  This will permanently delete:                       │        │
│  │  • All photos and media files                        │        │
│  │  • All metadata and AI-generated data                │        │
│  │  • All events and processing history                 │        │
│  │                                                       │        │
│  │  ⚠️  This action cannot be undone                     │        │
│  │                                                       │        │
│  │  [Show Wipe Options]                                 │        │
│  └─────────────────────────────────────────────────────┘        │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Color Palette & Accessibility

### Color System (WCAG 2.1 AAA Compliant)

**Background Layers:**
- `--bg-primary: #0F172A` (Deep slate - reduces eye strain)
- `--bg-secondary: #1E293B` (Elevated surfaces)
- `--bg-tertiary: #334155` (Interactive elements)

**Semantic Colors (with contrast ratios):**

**Information (Blue-Gray)**
```css
--info-bg: #334155      /* Card backgrounds */
--info-border: #475569  /* Subtle borders */
--info-text: #CBD5E1    /* 12.6:1 on dark bg */
```

**Success (Green - Safe Actions)**
```css
--success-bg: #064E3B    /* Button background */
--success-border: #10B981
--success-text: #6EE7B7  /* 8.2:1 contrast */
```

**Warning (Amber - Caution)**
```css
--warning-bg: #78350F    /* Amber-900 */
--warning-border: #F59E0B
--warning-text: #FCD34D  /* 9.1:1 contrast */
```

**Danger (Red - Destructive)**
```css
--danger-bg: #7F1D1D     /* Red-900 */
--danger-border: #EF4444
--danger-text: #FCA5A5   /* 8.8:1 contrast */
```

### Typography Hierarchy

```css
/* Page Title */
.settings-title {
  font-size: 2rem;
  font-weight: 600;
  letter-spacing: -0.025em;
  color: #F1F5F9;
}

/* Section Headers */
.section-header {
  font-size: 1.125rem;
  font-weight: 500;
  color: #CBD5E1;
  margin-bottom: 0.75rem;
}

/* Action Labels */
.action-label {
  font-size: 0.9375rem;
  font-weight: 500;
  color: #E2E8F0;
}

/* Descriptions */
.action-description {
  font-size: 0.875rem;
  color: #94A3B8;
  line-height: 1.5;
}

/* Data Stats */
.stat-value {
  font-family: 'SF Mono', 'Monaco', monospace;
  font-size: 1.5rem;
  font-weight: 600;
  color: #3B82F6;
}
```

---

## Interactive Components

### 1. Storage Overview Chart

**Visual Design:**
- Horizontal stacked bar chart with smooth gradients
- Colors: Hot (🔥 Orange), Warm (🌤️ Yellow), Cold (❄️ Blue)
- Hover shows exact sizes and percentages
- Animated on page load (progressive fill)

**Accessibility:**
- `aria-label` with full description
- Data table alternative (collapsible)
- Keyboard navigation to view details

### 2. Action Cards

**Standard Maintenance Actions:**
```html
<div class="action-card action-safe">
  <div class="action-icon">🔄</div>
  <div class="action-content">
    <h3 class="action-label">Rebuild Search Index</h3>
    <p class="action-description">
      Updates vector embeddings and metadata indexes for faster searches
    </p>
    <span class="action-meta">Last indexed: 2 hours ago</span>
  </div>
  <button class="btn btn-primary" aria-describedby="rebuild-help">
    Rebuild Now
  </button>
</div>
```

**States:**
- **Default:** Subtle border, dark background
- **Hover:** Elevated shadow, brighter border
- **Loading:** Progress spinner, disabled state
- **Success:** Green checkmark, fade out after 2s
- **Error:** Red border, error message inline

### 3. Danger Zone Interaction Pattern

**Progressive Disclosure (Safety-First Design):**

**Step 1: Initial State (Collapsed)**
```
┌─────────────────────────────────────────────────┐
│  ⚠️  DANGER ZONE                                 │
│  ┌────────────────────────────────────────────┐ │
│  │  🗑️  Wipe Entire Repository                 │ │
│  │                                             │ │
│  │  This action cannot be undone              │ │
│  │  [Show Wipe Options] ▼                     │ │
│  └────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Step 2: Expanded (Show Options)**
```
┌─────────────────────────────────────────────────┐
│  ⚠️  DANGER ZONE                                 │
│  ┌────────────────────────────────────────────┐ │
│  │  🗑️  Wipe Entire Repository                 │ │
│  │                                             │ │
│  │  ⚠️  WARNING: This will permanently delete: │ │
│  │                                             │ │
│  │  ☑️  All photos (4,382 files, 56.2 GB)     │ │
│  │  ☑️  All metadata and AI embeddings         │ │
│  │  ☑️  All events and processing history      │ │
│  │  ☑️  All configuration and preferences      │ │
│  │                                             │ │
│  │  📥 We recommend exporting data first       │ │
│  │  [Export All Data]                         │ │
│  │                                             │ │
│  │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │ │
│  │                                             │ │
│  │  To proceed, type: DELETE ALL DATA         │ │
│  │  ┌─────────────────────────────────────┐  │ │
│  │  │                                       │  │ │
│  │  └─────────────────────────────────────┘  │ │
│  │                                             │ │
│  │  [Cancel]  [Wipe Repository] (disabled)   │ │
│  └────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Step 3: Confirmation Match**
```
┌─────────────────────────────────────────────────┐
│  Type: DELETE ALL DATA                          │
│  ┌─────────────────────────────────────────┐   │
│  │ DELETE ALL DATA                         │   │
│  └─────────────────────────────────────────┘   │
│                                                  │
│  [Cancel]  [Wipe Repository] ← Now enabled     │
└─────────────────────────────────────────────────┘
```

**Step 4: Final Confirmation Modal**
```
┌──────────────────────────────────────────┐
│           ⚠️  FINAL CONFIRMATION          │
│                                           │
│  You are about to permanently delete:    │
│                                           │
│  • 4,382 photos (56.2 GB)                │
│  • All metadata and embeddings           │
│  • All events and history                │
│                                           │
│  This action is IRREVERSIBLE             │
│                                           │
│  Last chance to export your data:        │
│  [Export Now]                            │
│                                           │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │
│                                           │
│  Are you absolutely sure?                │
│                                           │
│  [No, Cancel]  [Yes, Wipe Everything]   │
│                  ↑ Red, bold             │
└──────────────────────────────────────────┘
```

**Step 5: Progress Feedback**
```
┌──────────────────────────────────────────┐
│           🗑️  Wiping Repository          │
│                                           │
│  [████████████████░░░░░░░░░░] 65%        │
│                                           │
│  Removing photos... 2,845 / 4,382        │
│                                           │
│  ⚠️  Do not close this window            │
└──────────────────────────────────────────┘
```

**Step 6: Completion**
```
┌──────────────────────────────────────────┐
│           ✓  Repository Wiped            │
│                                           │
│  All data has been permanently removed   │
│                                           │
│  Deleted:                                │
│  • 4,382 photos (56.2 GB)                │
│  • 2,847 AI embeddings                   │
│  • 23 events                             │
│                                           │
│  [Return to Settings]                    │
└──────────────────────────────────────────┘
```

---

## Micro-interactions & Animation

**Principles:**
- **Fast feedback:** <100ms for button presses
- **Smooth transitions:** 200-300ms cubic-bezier easing
- **Purposeful motion:** Guide attention, don't distract

**Examples:**

**1. Button Press (Safe Actions)**
```css
.btn-primary:active {
  transform: scale(0.98);
  transition: transform 80ms cubic-bezier(0.4, 0, 0.2, 1);
}

.btn-primary.loading::after {
  content: '';
  animation: spin 0.6s linear infinite;
}
```

**2. Danger Zone Expansion**
```css
.danger-zone.expanded {
  animation: expand-danger 300ms cubic-bezier(0.4, 0, 0.2, 1);
}

@keyframes expand-danger {
  from {
    max-height: 120px;
    opacity: 0.8;
  }
  to {
    max-height: 600px;
    opacity: 1;
  }
}
```

**3. Progress Bar (Smooth Increment)**
```css
.progress-bar {
  transition: width 400ms cubic-bezier(0.4, 0, 0.2, 1);
}

/* Pulse on critical operations */
.progress-bar.danger {
  animation: danger-pulse 1.5s ease-in-out infinite;
}
```

**4. Success/Error Toasts**
- Slide in from top-right (300ms)
- Auto-dismiss after 4s (success) or persist (error)
- Dismissible via click or ESC key

---

## Accessibility Features (WCAG 2.1 AAA)

### Keyboard Navigation
```
Tab Order:
1. Settings tabs (General → Storage → Appearance → About)
2. Storage chart (Enter to view data table)
3. Each action card (Enter to execute)
4. Danger zone trigger (Enter to expand)
5. Export buttons (if needed)
6. Confirmation input (auto-focus on expand)
7. Wipe button (Enter to show modal)
```

### Screen Reader Support
```html
<!-- Action Card Example -->
<div class="action-card" role="region" aria-labelledby="rebuild-heading">
  <h3 id="rebuild-heading">Rebuild Search Index</h3>
  <p id="rebuild-desc">Updates vector embeddings for faster searches</p>
  <button
    aria-describedby="rebuild-desc"
    aria-live="polite"
    aria-busy="false">
    Rebuild Now
  </button>
</div>

<!-- Danger Zone -->
<div class="danger-zone" role="alert" aria-live="assertive">
  <h3>Danger Zone</h3>
  <p>Warning: Destructive actions below</p>
  <!-- ... -->
</div>

<!-- Progress -->
<div role="status" aria-live="polite" aria-atomic="true">
  <span class="sr-only">Wiping repository: 65% complete</span>
</div>
```

### Focus Indicators
```css
*:focus-visible {
  outline: 2px solid #3B82F6;
  outline-offset: 2px;
  border-radius: 4px;
}

/* Danger actions get red focus */
.danger-zone *:focus-visible {
  outline-color: #EF4444;
}
```

### Color Blindness Considerations
- Never rely on color alone (use icons + text)
- Danger actions: Red background + 🗑️ icon + "Delete" text
- Success: Green background + ✓ icon + "Success" text
- Patterns/textures for charts (not just colors)

---

## Responsive Design

### Desktop (1024px+)
- Two-column layout: Stats left, actions right
- Fixed action cards width (600px max)
- Spacious padding (2rem)

### Tablet (768px - 1023px)
- Single column, full-width cards
- Larger touch targets (48px min)
- Reduced padding (1.5rem)

### Mobile (< 768px)
- Stack all elements vertically
- Full-width buttons
- Simplified storage chart (list view)
- Sticky confirmation modal footer

---

## Security Considerations

**Rate Limiting:**
- Max 1 wipe operation per hour (server-side)
- IP-based throttling for repeated attempts

**Audit Logging:**
```javascript
{
  action: 'REPOSITORY_WIPE',
  timestamp: '2025-10-17T02:15:33Z',
  user: 'admin@example.com',
  ip: '192.168.1.100',
  confirmationText: 'DELETE ALL DATA',
  deletedItems: {
    photos: 4382,
    events: 23,
    embeddings: 2847
  }
}
```

**Recovery Window:**
- 30-second cancellation period (countdown timer)
- "Undo Wipe" button during deletion process
- Immediate stop of async deletion tasks

---

## Implementation Priority

**Phase 1: MVP (1-2 days)**
- Storage overview card
- Basic maintenance actions (rebuild index, clear cache)
- Simple wipe with 2-step confirmation

**Phase 2: Enhanced Safety (1 day)**
- Type-to-confirm pattern
- Export before wipe nudge
- Progress feedback

**Phase 3: Polish (1 day)**
- Animations and micro-interactions
- Full accessibility audit
- Responsive optimization

---

## Design Rationale Summary

**Why This Approach Works:**

1. **Progressive Disclosure:** Dangerous actions are hidden until intentionally revealed, reducing accidental clicks

2. **Multiple Confirmation Layers:** Type-to-confirm + modal prevents 99.9% of accidental deletions

3. **Visual Hierarchy:**
   - Information cards are calm (blue-gray)
   - Actions are inviting (blue)
   - Danger is impossible to miss (red) but not panic-inducing

4. **Accessibility First:** Every interaction works with keyboard, screen reader, and alternative input methods

5. **Psychological Safety:**
   - Export options prominently placed before destructive actions
   - Clear communication of consequences
   - Escape hatches at every step

6. **Professional Aesthetics:**
   - Clean, modern design builds trust
   - Consistent with SnapVault's existing UI
   - Looks like a tool professionals would use

**Color Psychology in Action:**
- **Blue** (trust, stability) for safe operations
- **Amber** (caution) for actions requiring thought
- **Red** (danger) only for irreversible actions
- **Dark backgrounds** reduce cognitive load during serious decisions

This design balances the need for powerful maintenance tools with the responsibility of protecting user data.
