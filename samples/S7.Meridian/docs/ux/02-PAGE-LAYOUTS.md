# Page Layouts & Wireframe Specifications

**Version:** 1.0  
**Date:** October 2025

This document contains detailed wireframes and layout specifications for all Meridian screens. For complete specifications including remaining layouts (Quality Metrics, Settings, etc.), component states, and interaction patterns, refer to the accompanying design files.

## Core Layouts Included

1. **Landing Dashboard** - Entry point with recent pipelines and analysis type selection
2. **Pipeline Setup** - Configure name, description, and context for new pipeline
3. **Document Upload** - Drag-drop interface with auto-classification
4. **Processing Status** - Real-time progress with stage-based updates
5. **Review & Resolve** - Split-pane interface with field tree and document preview
6. **Evidence Drawer** - Slide-in panel showing source passages and alternatives
7. **Conflicts Panel** - Bottom sheet for conflict triage and resolution
8. **Deliverable Preview** - Final output with markdown rendering and export options

## Layout Principles

### 8px Grid System
All spacing, dimensions, and positioning use multiples of 8px for consistency:
- **Tight**: 4px (icon gaps)
- **Base**: 8px (standard spacing)
- **Comfortable**: 16px (section spacing)
- **Generous**: 24px (major sections)
- **Dramatic**: 48px (page-level spacing)

### Responsive Breakpoints
- **Mobile**: 320-767px
- **Tablet**: 768-1023px
- **Desktop**: 1024-1439px
- **Wide**: 1440px+

### Component Sizing
- **Touch targets**: Minimum 48×48px (WCAG AAA)
- **Form inputs**: 48px height (desktop), 56px (mobile)
- **Buttons**: 48px height, variable width with 24px horizontal padding
- **Cards**: Minimum 120px height, 16px padding

## Design Files

For complete wireframes with pixel-perfect specifications, annotations, and interactive prototypes, see:
- `diagrams/wireframes-desktop.fig` (Figma source)
- `diagrams/wireframes-mobile.fig` (Figma source)
- `diagrams/component-specs.pdf` (Annotated specifications)

## Key Screen Specifications

### Landing Dashboard (Route: `/`)
**Purpose**: Entry point, recent work, quick start
**Layout**: Centered content (1200px max-width)

**Components**:
- Global header (64px height, fixed)
- Recent pipelines grid (3-up, 380px cards)
- Analysis type selector (2x2 grid, 580px cards)
- Footer (80px height)

### Pipeline Setup (Route: `/pipelines/new`)
**Purpose**: Configure pipeline name, description, and authoritative notes
**Layout**: Centered content (800px max-width)

**Components**:
- Pipeline name input (800px width, 56px height)
- Description textarea (800px width, 120px height)
- **Authoritative Notes field** (800px width, 160px height)
  - Gold background: rgba(251, 191, 36, 0.08)
  - Gold border-left: 4px solid #FBBF24
  - Icon: ⭐ 24×24px, gold
  - Placeholder: "Optional: Enter data that should override document extractions..."
  - Helper text: "Any information you enter here will take priority over extracted values"
- Action bar (bottom, 72px height)

### Document Upload (Route: `/pipelines/:id/upload`)
**Purpose**: File upload with auto-classification  
**Layout**: Centered content (800px max-width)

**Components**:
- Drag-drop zone (800×320px)
- File cards (800×120px each)
- Classification dropdowns (280px width)
- Action bar (bottom, 72px height)

### Review & Resolve (Route: `/pipelines/:id/review`)
**Purpose**: Data review, conflict resolution
**Layout**: Split-pane (40% field tree | 60% document preview)

**Components**:
- Field tree (left pane, scrollable)
  - **Gold star indicator (⭐)** for fields sourced from Authoritative Notes
  - Confidence indicators for all other fields
- PDF viewer (right pane, canvas-based)
- Evidence drawer (480px slide-in from right)
  - Shows "Authoritative Notes (User Override)" as source when applicable
  - "Edit Notes" button available for Notes-sourced fields
- Conflicts panel (slide-up from bottom)

## State Variations

Each screen includes specifications for:
- **Default state**: Standard layout
- **Loading state**: Skeleton screens, progress indicators
- **Empty state**: No data illustrations with CTAs
- **Error state**: Error messages with recovery actions
- **Success state**: Confirmation messages and next steps

## Component Interaction Zones

All interactive elements include hover, focus, active, and disabled states with clear visual feedback:
- **Hover**: Subtle background color shift, cursor change
- **Focus**: 3px blue outline with 2px offset (keyboard navigation)
- **Active**: Pressed appearance (transform or shadow change)
- **Disabled**: 50% opacity, no pointer events

---

**For detailed wireframe mockups**, see accompanying design files in `/docs/ux/diagrams/`
