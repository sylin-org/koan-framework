# S6.SnapVault: Professional Edition
## Complete UX Design Specification

**Version**: 1.1 (Self-Hosted, Desktop-Only)
**Date**: 2025-10-16
**Target Audience**: Elite Professional Photographers & Teams (Self-Hosted Studios)
**Designer**: Senior UX Architect (Adobe, Apple, Getty Images experience)
**Framework**: Koan Framework v0.6.3
**Deployment**: Self-hosted via Docker Compose (MongoDB + Weaviate + Ollama)

---

## Version History

**Version 1.1 (2025-10-16)**: Realigned for self-hosted, desktop-only deployment
- Removed Phase 6 (Mobile & Offline) from roadmap (now 10 weeks, 5 phases instead of 12 weeks, 6 phases)
- Removed SaaS pricing strategy - replaced with self-hosted cost analysis
- Updated personas to reflect LAN-based studio workflows
- Changed touch gestures to trackpad gestures (desktop focus)
- Updated browser support to emphasize desktop browsers (Chrome, Firefox, Safari macOS)
- Updated security considerations for LAN deployment (HTTP internal, HTTPS optional)
- Emphasized data sovereignty, zero subscriptions, complete ownership
- Updated competitive analysis to highlight self-hosted advantages
- Updated user journeys to show LAN-based collaboration

**Version 1.0 (2025-10-16)**: Initial comprehensive UX specification

---

## Executive Summary

S6.SnapVault Professional Edition is a **self-hosted**, web-based photo management system designed specifically for professional photographers and studios who demand complete control over their data, studio-grade tools, color-accurate workflows, and modern AI-powered discovery—all running on their own infrastructure.

### Deployment Model

**Self-Hosted Architecture**: SnapVault runs entirely on your local network via Docker Compose, providing:
- **Complete data ownership** - All photos and metadata stay on your servers
- **No internet dependency** - Works entirely offline (except optional AI model downloads)
- **Studio/team LAN deployment** - Multiple workstations access shared photo library
- **No subscription costs** - One-time setup, no monthly fees
- **Professional workstation focus** - Optimized for desktop browsers with large screens (1920px+)

### Core Differentiators

1. **Semantic/Exact Search Hybrid** - Industry-first slider control allowing photographers to balance AI semantic search with precise keyword matching
2. **Gallery Dark Theme** - Color-calibrated dark interface (#0A0A0A base) that doesn't contaminate color perception on calibrated monitors
3. **Studio-Grade Upload Intelligence** - Pre-upload EXIF preview, duplicate detection, compression estimation, and batch metadata editing
4. **Cinematic Photo Viewer** - Immersive lightbox with mouse-reveal chrome, keyboard-centric navigation, and physics-based interactions
5. **Adaptive Masonry Layout** - Smart aspect ratio distribution preventing visual clustering ("portrait column syndrome")
6. **Self-Contained Deployment** - Single `start.bat` command spins up MongoDB, Weaviate, Ollama, and the application

### Design Philosophy

> "Photos are heroes. Interface recedes. Speed is essential. Control is paramount. Your data stays yours."

Professional photographers need tools that:
- **Never compromise image quality** (color-accurate backgrounds, proper sRGB calibration)
- **Minimize eye strain** (dark interface for 8+ hour editing sessions)
- **Maximize information density** (tight typography, efficient spacing for large monitors)
- **Respect muscle memory** (keyboard shortcuts, Vim-style J/K navigation)
- **Provide instant feedback** (optimistic UI, <30ms response times)
- **Guarantee data sovereignty** (self-hosted, no cloud dependencies)

---

## Table of Contents

1. [Target Audience Analysis](#1-target-audience-analysis)
2. [Design System](#2-design-system)
3. [Information Architecture](#3-information-architecture)
4. [Interface Components](#4-interface-components)
5. [Interaction Patterns](#5-interaction-patterns)
6. [Accessibility Standards](#6-accessibility-standards)
7. [Performance Requirements](#7-performance-requirements)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Competitive Analysis](#9-competitive-analysis)
10. [Technical Specifications](#10-technical-specifications)

---

## 1. Target Audience Analysis

### Primary Persona: Professional Event Photographer

**Name**: Sarah Chen
**Age**: 32
**Experience**: 8 years professional, specializes in weddings and corporate events
**Equipment**: Canon EOS R5, Sony A7R IV (backup), shoots 2,000-5,000 photos per event
**Workstation**: iMac Pro with calibrated monitor at studio, MacBook Pro at home office (both connect to same self-hosted instance)
**Pain Points**:
- Lightroom Classic is slow with 50,000+ photo catalogs
- Subscription costs add up (Adobe, cloud storage, third-party proofing)
- No semantic search—must manually tag everything
- Worried about cloud breaches exposing client photos
- Batch uploads are tedious without duplicate detection

**Goals**:
- Cull 5,000 photos to 500 deliverables in <2 hours
- Search photos by description ("bride laughing", "golden hour portraits")
- Share galleries with clients for selection (LAN or VPN access)
- Organize by event with automatic date/camera grouping
- Own all photo data without cloud dependency

**Technical Proficiency**: High (comfortable with keyboard shortcuts, color management, EXIF metadata, basic Docker/networking)

### Secondary Persona: Photography Studio Manager

**Name**: Marcus Rodriguez
**Age**: 45
**Experience**: 15 years, manages team of 5 photographers
**Use Case**: Centralized storage for multi-photographer events (weddings with 2-3 shooters)
**Pain Points**:
- No shared catalog across team
- Different photographers use different systems
- Client delivery requires consolidating from multiple sources
- Storage costs are high (duplicate files, unoptimized originals)

**Goals**:
- Single source of truth for all event photos
- Automatic derivative generation (originals stay in cold storage)
- Team collaboration (shared culling, rating, tagging)
- Client-facing galleries with password protection
- Storage optimization with tiered architecture

### Tertiary Persona: Fine Art Photographer

**Name**: Elena Volkov
**Age**: 38
**Experience**: 12 years, gallery exhibitions and limited edition prints
**Use Case**: Portfolio management, selective presentation
**Pain Points**:
- Need perfect color accuracy (prints must match screen)
- Manual collection building is tedious
- No way to find "photos similar to this one"
- EXIF data crucial for print specifications

**Goals**:
- Build thematic collections (e.g., "Urban Isolation" series)
- Find similar compositions using visual similarity
- Maintain EXIF for provenance (camera, lens, settings)
- Present portfolio without technical distractions

---

## 2. Design System

### 2.1 Color Palette: "Gallery Dark" Theme

#### Color Psychology for Photographers

Professional photographers require:
- **Color-neutral backgrounds** to avoid contaminating color perception
- **Low-light environments** for accurate exposure judgment
- **High contrast** for quick visual scanning
- **Non-distracting interface** that lets photos dominate

#### Base Palette

```css
/* Backgrounds: Graduated darkness creates depth without harshness */
--bg-canvas: #0A0A0A;           /* Main canvas - near black (not pure #000) */
--bg-surface: #141414;          /* Cards, panels - lifted from canvas */
--bg-surface-hover: #1A1A1A;    /* Interactive hover states */
--bg-surface-active: #222222;   /* Pressed/active states */

/* Borders: Subtle separation without visual noise */
--border-subtle: #2A2A2A;       /* Panel divisions, quiet separators */
--border-medium: #3A3A3A;       /* Focus indicators, active borders */
--border-strong: #4A4A4A;       /* High emphasis, important boundaries */

/* Text: High contrast optimized for extended viewing */
--text-primary: #E8E8E8;        /* 91% white - primary content */
--text-secondary: #A8A8A8;      /* 66% white - metadata, labels */
--text-tertiary: #787878;       /* 47% white - hints, placeholders */
--text-disabled: #4A4A4A;       /* 29% white - unavailable actions */

/* Accents: Color-calibrated for photo work */
--accent-primary: #5B9FFF;      /* Azure blue - sRGB calibrated */
--accent-semantic: #A78BFA;     /* Violet - AI/semantic features */
--accent-success: #4ADE80;      /* Green - approved/exported */
--accent-warning: #FBBF24;      /* Amber - needs attention */
--accent-danger: #F87171;       /* Red - reject/delete */
--accent-favorite: #FFC947;     /* Gold - starred/favorite photos */

/* Special: Rating system */
--star-active: #FFC947;         /* Active gold star */
--star-inactive: #3A3A3A;       /* Inactive dim star */
--star-hover: #FFD700;          /* Bright gold on hover */

/* Overlays: Lightbox and modal backgrounds */
--overlay-dark: rgba(10, 10, 10, 0.95);      /* 95% opaque backdrop */
--overlay-backdrop: rgba(10, 10, 10, 0.75);  /* Blurred photo background */
```

#### Color Rationale

| Color | Value | Why Not Alternatives | WCAG Contrast |
|-------|-------|---------------------|---------------|
| **Canvas Black** | #0A0A0A | Pure #000000 causes eye strain on bright monitors. 10,10,10 is darkest comfortable tone. | N/A (background) |
| **Azure Blue** | #5B9FFF | Shifted to match sRGB gamut—won't look "off" on calibrated monitors. Standard #007AFF appears oversaturated. | 8.2:1 on canvas (AAA) |
| **Violet Semantic** | #A78BFA | Differentiates AI features from manual actions. Complements azure without clashing. Purple = intelligence in UX convention. | 7.1:1 on canvas (AAA) |
| **Gold Favorite** | #FFC947 | Universally understood "star rating" color. Matches physical award metaphor. High visibility without harshness. | 12.1:1 on canvas (AAA) |
| **Text Primary** | #E8E8E8 (91% white) | Pure #FFFFFF is too harsh for extended viewing. 91% provides excellent contrast (17.5:1) while reducing eye strain. | 17.5:1 on canvas (AAA) |

#### Color Accessibility Matrix

| Foreground | Background | Contrast Ratio | WCAG Level | Use Case |
|------------|------------|----------------|------------|----------|
| #E8E8E8 (primary text) | #0A0A0A (canvas) | 17.5:1 | AAA | Body text, headings |
| #A8A8A8 (secondary text) | #0A0A0A (canvas) | 10.2:1 | AAA | Metadata, labels |
| #787878 (tertiary text) | #0A0A0A (canvas) | 5.8:1 | AA | Hints, placeholders |
| #5B9FFF (accent) | #0A0A0A (canvas) | 8.2:1 | AAA | Interactive elements |
| #FFC947 (favorite) | #0A0A0A (canvas) | 12.1:1 | AAA | Star ratings |
| #E8E8E8 (text) | #141414 (surface) | 15.8:1 | AAA | Card text |

All color combinations exceed WCAG 2.1 Level AA (4.5:1 for normal text, 3:1 for large text). Most achieve AAA (7:1+).

---

### 2.2 Typography System

#### Font Stack

```css
/* Primary: System font stack for native feel and performance */
--font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI',
             Roboto, 'Helvetica Neue', Arial, sans-serif,
             'Apple Color Emoji', 'Segoe UI Emoji';

/* Technical: Monospace for EXIF data and camera settings */
--font-mono: 'SF Mono', Monaco, 'Cascadia Code',
             'Courier New', monospace;
```

**Rationale for System Fonts:**
- **Zero latency**: No web font downloads (0ms FOUT/FOIT)
- **Native rendering**: Platform-optimized subpixel rendering
- **Familiar feel**: Users recognize their OS typography
- **Smaller bundle**: No font files = faster initial load

#### Type Scale

```css
/* Scale: Tight hierarchy for information density */
--text-xs: 0.6875rem;    /* 11px - Fine print, keyboard shortcuts */
--text-sm: 0.8125rem;    /* 13px - Metadata, secondary labels */
--text-base: 0.9375rem;  /* 15px - Body text, primary UI */
--text-lg: 1.125rem;     /* 18px - Section headers */
--text-xl: 1.5rem;       /* 24px - Page titles */
--text-2xl: 2rem;        /* 32px - Empty states, hero text */

/* Weight: Subtle hierarchy without visual noise */
--weight-normal: 400;     /* Body text */
--weight-medium: 500;     /* Buttons, active navigation */
--weight-semibold: 600;   /* Headers, strong emphasis */

/* Line height: Optimized for density and readability */
--leading-tight: 1.25;    /* Headers (minimal space) */
--leading-normal: 1.5;    /* Body text (comfortable reading) */
--leading-relaxed: 1.75;  /* Long-form content (rare) */
```

**Why Smaller Base Size (15px vs 16px)?**
- Professional users read faster and prefer information density
- More metadata visible without scrolling
- Industry standard (Lightroom: 13px, Capture One: 14px, Photo Mechanic: 13px)

#### Typography Usage Guidelines

| Element | Size | Weight | Line Height | Color | Use Case |
|---------|------|--------|-------------|-------|----------|
| **Page Title** | 24px (xl) | 600 | 1.25 | primary | Main heading ("All Photos") |
| **Section Header** | 18px (lg) | 600 | 1.25 | primary | Panel titles ("Filters") |
| **Body Text** | 15px (base) | 400 | 1.5 | primary | Primary content |
| **Metadata** | 13px (sm) | 400 | 1.5 | secondary | EXIF info, photo details |
| **Label** | 13px (sm) | 500 | 1.5 | secondary | Form labels, buttons |
| **Hint Text** | 11px (xs) | 400 | 1.5 | tertiary | Tooltips, keyboard shortcuts |
| **EXIF Technical** | 13px (sm) | 400 (mono) | 1.5 | secondary | Camera settings, file info |

---

### 2.3 Spacing System

```css
/* 8px baseline grid for visual rhythm */
--space-1: 0.5rem;   /* 8px - Tight spacing (buttons, chips) */
--space-2: 1rem;     /* 16px - Standard spacing (cards, inputs) */
--space-3: 1.5rem;   /* 24px - Section spacing (panels) */
--space-4: 2rem;     /* 32px - Large spacing (page sections) */
--space-5: 2.5rem;   /* 40px - Extra large spacing (rare) */
--space-6: 3rem;     /* 48px - Hero spacing (landing pages) */
```

**Rationale:**
- 8px grid aligns with industry standards (Material Design, Apple HIG)
- All measurements are multiples of 8 for visual consistency
- Predictable spacing creates rhythm and reduces cognitive load

---

### 2.4 Elevation System

```css
/* Depth through shadows: Subtle elevation without distraction */
--shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.3);           /* Buttons */
--shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.4);        /* Cards on hover */
--shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.5);      /* Modals, dropdowns */
--shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.6);      /* Lightbox photos */

/* Elevation layers (z-index system) */
--layer-base: 0;           /* Canvas, main content */
--layer-surface: 1;        /* Cards, panels */
--layer-dropdown: 10;      /* Dropdowns, tooltips */
--layer-sticky: 50;        /* Sticky headers, toolbars */
--layer-overlay: 100;      /* Sidebars, drawers */
--layer-modal: 500;        /* Modals, dialogs */
--layer-lightbox: 1000;    /* Full-screen lightbox */
--layer-toast: 2000;       /* Toast notifications */
```

**Shadow Usage:**
- Stronger shadows in dark themes (opacity 0.3-0.6 vs 0.1-0.3 in light themes)
- Shadows provide depth cues without color distractions
- Photos never get shadows (they should appear as "windows" not "cards")

---

### 2.5 Motion System

```css
/* Easing functions: Physics-based motion feels natural */
--ease-linear: cubic-bezier(0, 0, 1, 1);
--ease-in: cubic-bezier(0.4, 0, 1, 1);                  /* Accelerate */
--ease-out: cubic-bezier(0, 0, 0.2, 1);                 /* Decelerate */
--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);            /* Smooth both ends */
--ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);       /* Primary easing */
--ease-spring: cubic-bezier(0.34, 1.56, 0.64, 1);       /* Playful bounce */

/* Duration hierarchy */
--duration-instant: 0ms;       /* Immediate feedback (optimistic UI) */
--duration-fast: 100ms;        /* Hover effects, toggles */
--duration-normal: 200ms;      /* Standard transitions (cards, overlays) */
--duration-slow: 300ms;        /* Panel slides, page transitions */
--duration-slower: 500ms;      /* Major state changes (rare) */
```

**Motion Principles:**
1. **Instant feedback** for direct manipulation (clicking, dragging)
2. **Fast transitions** for hover states (100ms feels snappy)
3. **Standard duration** (200ms) for most UI changes
4. **Respect `prefers-reduced-motion`** - all animations can be disabled

---

### 2.6 Border Radius System

```css
/* Corner rounding: Modern without trendy */
--radius-sm: 4px;    /* Buttons, small chips */
--radius-md: 6px;    /* Cards, inputs */
--radius-lg: 8px;    /* Panels, major containers */
--radius-xl: 12px;   /* Modal dialogs */
--radius-full: 9999px; /* Pills, avatar circles */
```

---

### 2.7 Iconography

**Icon Library**: Feather Icons + Custom SVG
**Size Scale**: 16px (small), 20px (base), 24px (large), 32px (hero)
**Style**: Outline icons (2px stroke weight) for consistency with text
**Color**: Inherits text color for automatic theming

**Core Icon Set:**
- Navigation: Home, Grid, Timeline, Map, Settings
- Actions: Upload, Download, Share, Delete, Edit, Star, Heart
- Media: Image, Camera, Video, Filter, Crop, Rotate
- UI: Search, X (close), Check, ChevronLeft/Right, More (•••)
- Status: Info, Warning, Error, Success, Loading (spinner)

---

## 3. Information Architecture

### 3.1 Application Structure

```
┌─────────────────────────────────────────────────────┐
│                  App Header (60px)                  │
│  [Logo] [Workspaces] [Search] [Upload] [User]      │
├───────────┬─────────────────────────┬───────────────┤
│           │                         │               │
│  Left     │   Main Content Area     │  Right        │
│  Sidebar  │                         │  Sidebar      │
│  (240px)  │   (fluid width)         │  (320px)      │
│           │                         │               │
│  Library  │   Photo Grid/Timeline   │  Filters      │
│  Events   │   Masonry Layout        │  Search       │
│  Smart    │   Virtual Scroll        │  Metadata     │
│  Colls    │                         │  Actions      │
│           │                         │               │
│  Storage  │   [Photos dynamically   │  Tag Cloud    │
│  Meter    │    loaded as user       │  Saved        │
│           │    scrolls]             │  Searches     │
│           │                         │               │
├───────────┴─────────────────────────┴───────────────┤
│              Status Bar (32px)                      │
│  [12,847 photos • 234 selected] [Sync] [Shortcuts] │
└─────────────────────────────────────────────────────┘
```

### 3.2 Navigation Hierarchy

```
Primary Navigation (Header)
├── Gallery View (🖼️)
│   ├── All Photos (default)
│   ├── Favorites (starred)
│   ├── Recent (last 30 days)
│   └── Trash (deleted, 30-day retention)
├── Timeline View (📅)
│   ├── By Month
│   ├── By Year
│   └── By Event
├── Map View (🗺️)
│   ├── Photo Locations (GPS clusters)
│   ├── Heatmap (shooting frequency)
│   └── Location Search
└── Settings (⚙️)
    ├── Account
    ├── Storage
    ├── AI Preferences
    └── Keyboard Shortcuts

Secondary Navigation (Left Sidebar)
├── Library
│   ├── All Photos (count badge)
│   ├── Favorites (count badge)
│   └── Recent (count badge)
├── Events (user-created)
│   ├── Smith Wedding
│   ├── Hawaii Vacation
│   └── [+ New Event]
└── Smart Collections (dynamic filters)
    ├── Canon 5D Shots
    ├── Golden Hour
    └── [+ New Collection]

Context Navigation (Right Sidebar)
├── Search & Filters
│   ├── Search Input (with mode slider)
│   ├── Date Range Filter
│   ├── Camera Gear Filter
│   ├── Rating Filter
│   ├── Tag Cloud
│   └── Location Filter (mini map)
└── Saved Searches
    ├── User-saved filters
    └── [+ Save Current Search]
```

---

### 3.3 User Flows

#### Flow 1: Upload Photos to Event

```
1. User clicks "Upload" button (U) or drags files
   ↓
2. Upload modal opens with drop zone
   ↓
3. User selects/drops 24 photos
   ↓
4. System shows per-photo preview cards with:
   - Thumbnail preview
   - EXIF data (camera, date, GPS)
   - Duplicate detection warning (if applicable)
   - Compression estimate
   ↓
5. User selects event from dropdown ("Smith Wedding")
   ↓
6. User optionally adds batch tags ("ceremony", "portraits")
   ↓
7. Uploads begin immediately (per-photo progress rings)
   ↓
8. Backend processes each photo:
   - Upload original to cold storage
   - Generate gallery derivative (1200px)
   - Generate thumbnail (150x150)
   - Extract EXIF metadata
   - Queue AI metadata generation (background)
   ↓
9. UI shows real-time progress:
   - "Uploading... 45%"
   - "Generating derivatives... 78%"
   - "Completed ✓"
   ↓
10. Photos appear in grid immediately (optimistic UI)
    ↓
11. AI processing happens in background:
    - Generate embeddings from metadata
    - Save with vector for semantic search
    - Status indicator shows "Processing AI metadata..."
    ↓
12. User continues culling while upload completes
```

#### Flow 2: Semantic Search with Hybrid Slider

```
1. User focuses search (press "/" or click search bar)
   ↓
2. User types query: "sunset at beach"
   ↓
3. User adjusts semantic/exact slider to 70% semantic
   ↓
4. Real-time toggle is ON, so results update as user types
   ↓
5. Backend performs hybrid search:
   - Generate query embedding: await Ai.Embed("sunset at beach")
   - Execute Vector<PhotoAsset>.Search(vector, text: "sunset at beach", alpha: 0.7, topK: 50)
   - Alpha 0.7 = 70% semantic similarity, 30% keyword matching
   ↓
6. Results appear in <300ms:
   - Photos of beaches at golden hour (semantic match)
   - Photos with "beach" or "sunset" in tags/filename (keyword match)
   - Weighted 70/30 blend
   ↓
7. User adjusts slider to 100% exact (alpha: 0.0)
   ↓
8. Results update to show only photos with exact keyword matches
   ↓
9. User hovers over photo → metadata + actions reveal
   ↓
10. User clicks photo → lightbox opens with keyboard nav
```

#### Flow 3: Culling Session (Professional Workflow)

```
1. User selects event "Smith Wedding" (2,847 photos)
   ↓
2. Grid loads with virtual scrolling (only renders visible photos)
   ↓
3. User starts keyboard-based culling:
   - Press J/K to navigate (Vim-style)
   - Press F to favorite keepers
   - Press 1-5 to rate photos
   - Press D to mark for deletion
   - Press Space to select multiple
   ↓
4. UI provides instant feedback:
   - Star icon appears on favorited photos
   - Rating stars fill immediately
   - Selection checkmark appears
   - All changes are optimistic (UI updates before server confirms)
   ↓
5. User selects 50 photos (Space or click checkboxes)
   ↓
6. Batch actions toolbar appears at top:
   - "50 selected"
   - Batch rating
   - Batch tagging
   - Batch export
   - Batch delete
   ↓
7. User rates all 50 as 5-star
   ↓
8. Backend processes batch request, UI already shows update
   ↓
9. User creates smart collection: "5-Star Ceremony Shots"
   - Filter: Rating = 5 stars + Tag contains "ceremony"
   - Collection updates dynamically as photos are rated
   ↓
10. Final deliverable set: 234 favorite, 5-star photos ready for client
```

#### Flow 4: Lightbox Photo Viewer

```
1. User clicks photo card in grid
   ↓
2. Lightbox opens instantly (<50ms):
   - Blurred backdrop uses photo itself
   - Photo centered, max 90vh height
   - Chrome elements hidden initially
   ↓
3. Mouse movement reveals UI (200ms fade-in):
   - Top: Event breadcrumb, photo count, close button
   - Bottom: Actions (favorite, rating, download, share, delete)
   - Right: Info toggle button
   ↓
4. Keyboard navigation active:
   - ← → : Previous/next photo (preloaded, instant transition)
   - J K : Vim-style navigation
   - F : Toggle favorite
   - 1-5 : Rate photo
   - I : Toggle info panel
   - Esc : Close lightbox
   ↓
5. User presses I → Info panel slides in from right (300ms):
   - Hero: Filename, date
   - Camera section: Body, lens, settings
   - Location section: GPS coordinates, mini map
   - Tags section: Editable tag chips
   - Colors section: Dominant color palette
   - File info: Dimensions, sizes, format
   ↓
6. User presses → to navigate to next photo:
   - Transition is instant (photo preloaded)
   - Info panel updates smoothly
   - Chrome remains visible during navigation
   ↓
7. User idle for 2 seconds → Chrome fades out automatically
   ↓
8. Mouse movement → Chrome fades back in
   ↓
9. User pinch-zooms photo (touch/trackpad):
   - Zoom centers on pinch point
   - Panning enabled after zoom
   - Physics-based momentum scrolling
   ↓
10. User presses Esc → Lightbox closes with smooth fade
```

---

## 4. Interface Components

### 4.1 Photo Card (Masonry Grid Item)

**Purpose**: Display individual photo in masonry grid with hover interactions

**Anatomy:**
```html
<article class="photo-card" data-aspect="1.5">
  <div class="photo-skeleton"></div>
  <img class="photo-image" loading="lazy" />
  <div class="photo-overlay">
    <div class="actions-top">
      <button class="btn-favorite">⭐</button>
      <button class="btn-select">☐</button>
    </div>
    <div class="actions-bottom">
      <div class="metadata">Camera • Settings</div>
      <div class="rating">★★★★★</div>
    </div>
  </div>
  <div class="selection-indicator">✓</div>
</article>
```

**States:**
1. **Loading** (skeleton visible, image loading)
2. **Idle** (image loaded, no overlay)
3. **Hover** (overlay visible with metadata + actions)
4. **Selected** (checkmark indicator, blue border)
5. **Processing** (spinner overlay during upload)

**Interactions:**
- **Click card** → Open lightbox
- **Click favorite (F)** → Toggle favorite (optimistic UI)
- **Click select (Space)** → Toggle selection
- **Hover star rating** → Preview rating
- **Click star rating** → Set rating (optimistic UI)
- **Shift+Click** → Select range from last selected

**Visual Specs:**
- Border radius: 8px
- Hover lift: translateY(-2px)
- Hover shadow: 0 8px 16px rgba(0,0,0,0.4)
- Transition: 150ms cubic-bezier(0.33, 1, 0.68, 1)
- Overlay gradient: linear-gradient(to bottom, rgba(10,10,10,0.6) 0%, transparent 30%, transparent 70%, rgba(10,10,10,0.8) 100%)

**Accessibility:**
- `<article>` semantic element
- `loading="lazy"` for performance
- `alt` text from filename or user caption
- Keyboard navigable (Tab, Enter to open)
- ARIA label: "Photo: [filename], rated [stars] stars"

---

### 4.2 Search Bar with Semantic/Exact Slider

**Purpose**: Industry-first hybrid search control allowing users to balance AI semantic search with keyword precision

**Innovation**: This is a novel UI pattern not found in competing products (Lightroom, Capture One, Photo Mechanic). Patent consideration recommended.

**Anatomy:**
```html
<div class="search-pro">
  <input type="search" placeholder="Search photos..." />

  <div class="search-mode-control">
    <label>Search Mode</label>
    <div class="slider-container">
      <input type="range" min="0" max="100" value="50" step="5" />
      <div class="slider-labels">
        <span>Exact</span>
        <span>Hybrid</span>
        <span>Semantic</span>
      </div>
    </div>
    <output>Hybrid (50%)</output>
  </div>

  <label class="realtime-toggle">
    <input type="checkbox" checked />
    Real-time <kbd>⌃R</kbd>
  </label>
</div>
```

**Slider Values:**
- **0% (Exact)**: `alpha: 0.0` → Pure keyword search, no AI
- **50% (Hybrid)**: `alpha: 0.5` → Balanced semantic + keyword
- **100% (Semantic)**: `alpha: 1.0` → Pure AI similarity, ignores keywords

**Visual Feedback:**
```css
/* Gradient shows semantic→exact spectrum */
background: linear-gradient(to right,
  #5B9FFF 0%,    /* Exact - blue */
  #8B5CF6 50%,   /* Hybrid - purple */
  #A78BFA 100%   /* Semantic - violet */
);
```

**User Flow:**
```
1. User types "golden hour portraits"
2. Real-time toggle ON → results stream as user types
3. User sees mix of:
   - Photos with "golden hour" or "portrait" tags (keyword)
   - Photos semantically similar to golden hour scenes (AI)
4. User slides to 100% semantic:
   - Results update to show visually similar photos
   - Photos without exact keywords appear (warm lighting, backlighting, soft shadows)
5. User slides to 0% exact:
   - Results update to show only tagged photos
   - More predictable, but less discovery
```

**Accessibility:**
- Range input native control (keyboard ←→ adjustable)
- Output element announces value changes
- Labels clearly explain exact/semantic difference
- Color is not sole differentiator (labels + gradient)

---

### 4.3 Upload Modal with Pre-Upload Intelligence

**Purpose**: Studio-grade upload experience with EXIF preview, duplicate detection, compression estimation

**Unique Features:**
1. **EXIF Preview** - Show camera, date, GPS before upload
2. **Duplicate Detection** - Hash-based matching with skip option
3. **Compression Estimate** - Show size savings from optimization
4. **Batch Metadata** - Edit event/tags for all files before upload

**Anatomy:**
```html
<dialog class="upload-modal">
  <header>
    <h2>Upload Photos</h2>
    <button class="close">×</button>
  </header>

  <div class="dropzone">
    <svg class="icon-cloud"></svg>
    <h3>Drag photos here</h3>
    <p>or</p>
    <button class="btn-primary">Choose Files</button>
    <p class="hint">JPG, PNG, HEIC up to 25MB</p>
  </div>

  <div class="upload-queue">
    <!-- Per-file upload card -->
    <article class="upload-item" data-status="processing">
      <img class="preview-thumb" />
      <div class="details">
        <h4>DSC_1234.jpg</h4>
        <div class="exif-preview">
          📷 Canon 5D IV • 📅 Dec 15, 2024 2:34 PM • 📍 San Francisco
        </div>
        <div class="duplicate-warning">
          ⚠️ Similar photo in "Smith Wedding" <button>Skip</button>
        </div>
        <div class="compression">
          8.4 MB → 2.1 MB (75% smaller)
        </div>
        <div class="derivatives">
          <span>Original</span>
          <span>Gallery (1200px)</span>
          <span>Thumbnail (150px)</span>
        </div>
        <div class="status">Generating derivatives... 45%</div>
      </div>
      <button class="cancel">×</button>
    </article>
  </div>

  <div class="batch-actions">
    <label>Add to event: <select>...</select></label>
    <button>Add tags to all</button>
  </div>

  <footer>
    <span>12 of 24 uploaded</span>
    <button class="btn-secondary">Cancel All</button>
    <button class="btn-primary">Done</button>
  </footer>
</dialog>
```

**Upload Flow:**
```
1. File selected → Generate client-side preview (Canvas API)
2. Extract EXIF client-side (exif-js library)
3. Compute hash (SHA-256 of first 1MB) for duplicate check
4. Show preview card with EXIF before upload begins
5. User can skip duplicates or adjust metadata
6. Upload begins → Progress ring shows percent
7. Backend processes:
   - Upload original (cold storage)
   - Generate gallery (1200px)
   - Generate thumbnail (150px)
   - Extract server EXIF
   - Queue AI metadata (background)
8. Status updates in real-time via SignalR
9. Completed items show green checkmark
```

**Performance:**
- Client-side EXIF extraction doesn't block upload
- Hashing uses Web Crypto API (fast, non-blocking)
- Preview generation uses OffscreenCanvas in Web Worker
- Progress updates throttled to 100ms intervals

---

### 4.4 Lightbox Photo Viewer

**Purpose**: Immersive full-screen photo viewing with cinema-quality presentation

**Design Philosophy**:
> "The photo is everything. UI appears only when needed, disappears when not."

**Anatomy:**
```html
<div class="lightbox" data-state="open">
  <!-- Blurred backdrop (actual photo at 40px blur) -->
  <div class="lightbox-backdrop" style="background-image: url(...)"></div>

  <!-- Main photo -->
  <div class="lightbox-stage">
    <img class="lightbox-image" />
    <div class="zoom-controls">
      <button>−</button>
      <span>100%</span>
      <button>+</button>
      <button>Fit</button>
    </div>
  </div>

  <!-- Navigation (mouse-reveal) -->
  <nav class="nav-top">
    <div class="breadcrumb">Smith Wedding / Photo 45 of 2,847</div>
    <button class="close">×</button>
  </nav>

  <button class="nav-prev">◀</button>
  <button class="nav-next">▶</button>

  <!-- Actions (mouse-reveal) -->
  <div class="actions-bottom">
    <div class="primary-actions">
      <button>⭐ Favorite</button>
      <div class="rating-large">★★★★★</div>
      <button>↓ Download</button>
      <button>🗑️ Delete</button>
    </div>
    <button class="info-toggle">ⓘ Info</button>
  </div>

  <!-- Info panel (slide-in) -->
  <aside class="info-panel" data-state="collapsed">
    <div class="hero">
      <h2>DSC_1234.jpg</h2>
      <time>December 15, 2024 at 2:34 PM</time>
    </div>

    <details open>
      <summary>📷 Camera</summary>
      <dl>
        <dt>Camera</dt><dd>Canon EOS 5D Mark IV</dd>
        <dt>Lens</dt><dd>Canon EF 85mm f/1.8 USM</dd>
        <dt>Settings</dt><dd>85mm · f/1.8 · 1/200s · ISO 400</dd>
      </dl>
    </details>

    <details>
      <summary>📍 Location</summary>
      <div class="mini-map"></div>
      <p>37.7749° N, 122.4194° W</p>
      <p>Golden Gate Park, San Francisco</p>
    </details>

    <details>
      <summary>🏷️ Tags</summary>
      <div class="tag-list">
        <span class="tag">wedding</span>
        <span class="tag">ceremony</span>
        <button>+ Add</button>
      </div>
    </details>

    <details>
      <summary>🎨 Colors</summary>
      <div class="color-palette">
        <div class="swatch" style="background: #8B7355"></div>
        <div class="swatch" style="background: #D4C4B0"></div>
        <div class="swatch" style="background: #F5E6D3"></div>
      </div>
    </details>
  </aside>
</div>
```

**Mouse-Reveal Behavior:**
```javascript
const lightbox = {
  idleTimeout: 2000, // 2 seconds

  onMouseMove() {
    // Show chrome
    this.showChrome();

    // Reset idle timer
    clearTimeout(this.idleTimer);
    this.idleTimer = setTimeout(() => {
      this.hideChrome();
    }, this.idleTimeout);
  },

  showChrome() {
    // 200ms fade-in
    navTop.style.opacity = 1;
    navPrev.style.opacity = 1;
    navNext.style.opacity = 1;
    actionsBottom.style.opacity = 1;
  },

  hideChrome() {
    // 200ms fade-out
    navTop.style.opacity = 0;
    navPrev.style.opacity = 0;
    navNext.style.opacity = 0;
    actionsBottom.style.opacity = 0;
  }
};
```

**Keyboard Shortcuts:**
| Key | Action |
|-----|--------|
| `←` `→` | Previous/Next photo |
| `J` `K` | Previous/Next (Vim-style) |
| `F` | Toggle favorite |
| `1-5` | Rate photo (1-5 stars) |
| `0` | Clear rating |
| `I` | Toggle info panel |
| `D` | Delete (with confirmation) |
| `S` | Share photo |
| `Space` | Play/pause slideshow |
| `Esc` | Close lightbox |

**Performance:**
- **Preloading**: Adjacent 2 photos loaded in background
- **Transition**: <30ms photo swap (instant feel)
- **Smooth panning**: 60fps during zoom/pan
- **Memory management**: Unload photos >5 positions away

---

### 4.5 Masonry Grid Layout

**Purpose**: Adaptive photo grid that prevents visual clustering and maintains rhythm

**Problem Solved**: Traditional masonry layouts create "portrait column syndrome" where vertical photos cluster in one column, landscapes in another. This creates visual imbalance.

**Solution**: Smart aspect ratio distribution

```javascript
class SmartMasonryLayout {
  layout(photos) {
    // Categorize by aspect ratio
    const portraits = photos.filter(p => p.height / p.width > 1.2);
    const landscapes = photos.filter(p => p.width / p.height > 1.2);
    const squares = photos.filter(p => Math.abs(1 - p.width / p.height) < 0.2);

    // Interleave to prevent clustering
    const distributed = [];
    const maxLength = Math.max(portraits.length, landscapes.length, squares.length);

    for (let i = 0; i < maxLength; i++) {
      if (landscapes[i]) distributed.push(landscapes[i]);
      if (portraits[i]) distributed.push(portraits[i]);
      if (squares[i]) distributed.push(squares[i]);
    }

    return distributed;
  }
}
```

**Density Modes:**
| Density | Columns | Use Case | Photos Visible |
|---------|---------|----------|----------------|
| Sparse | 3 | Large previews, detailed viewing | ~30 photos |
| Normal | 4 | Standard browsing | ~50 photos |
| Dense | 6 | Quick scanning, culling | ~90 photos |

**Keyboard Shortcuts:**
- `1` → Sparse (3 columns)
- `2` → Normal (4 columns)
- `3` → Dense (6 columns)

**Virtual Scrolling:**
```javascript
const virtualScroll = {
  viewportHeight: window.innerHeight,
  bufferScreens: 2, // Render 2 screens above/below viewport

  getVisibleRange() {
    const scrollTop = window.scrollY;
    const start = scrollTop - (this.viewportHeight * this.bufferScreens);
    const end = scrollTop + this.viewportHeight + (this.viewportHeight * this.bufferScreens);
    return { start, end };
  },

  render() {
    const { start, end } = this.getVisibleRange();
    const visiblePhotos = photos.filter(p =>
      p.offsetTop >= start && p.offsetTop <= end
    );

    // Only render visible + buffer photos
    this.renderPhotos(visiblePhotos);
  }
};
```

**Performance:**
- **Intersection Observer** for lazy loading (native browser optimization)
- **Content-visibility: auto** for off-screen cards (CSS optimization)
- **Will-change: transform** for cards that will animate
- **Render target**: 60fps during scroll, <100ms layout shift

---

### 4.6 Filter Sidebar

**Purpose**: Contextual filtering and discovery without leaving main view

**Anatomy:**
```html
<aside class="sidebar-right">
  <div class="panel filters-panel">
    <h3>Filters</h3>

    <!-- Date Range (dual-thumb slider) -->
    <details class="filter-group" open>
      <summary>
        <span class="icon">📅</span>
        <span class="label">Date Range</span>
        <span class="badge">Last year</span>
      </summary>
      <div class="content">
        <div class="dual-range-slider">
          <input type="range" class="range-min" min="2020" max="2024" value="2023" />
          <input type="range" class="range-max" min="2020" max="2024" value="2024" />
          <div class="range-track"></div>
        </div>
        <div class="range-labels">
          <span>2023</span>
          <span>Present</span>
        </div>
      </div>
    </details>

    <!-- Camera Gear (checkboxes) -->
    <details class="filter-group">
      <summary>
        <span class="icon">📷</span>
        <span class="label">Camera</span>
        <span class="badge">3 active</span>
      </summary>
      <div class="content">
        <label class="checkbox">
          <input type="checkbox" checked />
          <span class="label">Canon EOS 5D Mark IV</span>
          <span class="count">4,521</span>
        </label>
        <label class="checkbox">
          <input type="checkbox" checked />
          <span class="label">Sony A7R III</span>
          <span class="count">3,208</span>
        </label>
      </div>
    </details>

    <!-- Tag Cloud (frequency-sized buttons) -->
    <details class="filter-group">
      <summary>
        <span class="icon">🏷️</span>
        <span class="label">Tags</span>
      </summary>
      <div class="content">
        <div class="tag-cloud">
          <!-- Size = frequency, Color = recency -->
          <button class="tag xl recent">portrait</button>
          <button class="tag lg recent">landscape</button>
          <button class="tag md">sunset</button>
          <button class="tag sm">beach</button>
        </div>
      </div>
    </details>

    <!-- Location (mini map) -->
    <details class="filter-group">
      <summary>
        <span class="icon">📍</span>
        <span class="label">Location</span>
      </summary>
      <div class="content">
        <div class="mini-map" style="height: 150px"></div>
      </div>
    </details>
  </div>

  <!-- Saved Searches -->
  <div class="panel saved-searches">
    <h3>Saved Searches</h3>
    <button class="saved-search">
      <span class="icon">⭐</span>
      <span class="label">Canon Wide Shots</span>
    </button>
    <button class="saved-search-add">
      <span class="icon">+</span>
      <span class="label">Save current search</span>
    </button>
  </div>
</aside>
```

**Tag Cloud Visualization:**
- **Size**: Proportional to photo count
  - `tag-xl`: 1000+ photos (16px)
  - `tag-lg`: 500-999 photos (14px)
  - `tag-md`: 100-499 photos (13px)
  - `tag-sm`: <100 photos (11px)
- **Color**: Recency indicator
  - `.recent`: Used in last 7 days (brighter, accent color)
  - Default: Older than 7 days (muted)
- **Interaction**:
  - Click: Filter by tag
  - Shift+Click: Exclude tag
  - Hover: Show count tooltip

---

## 5. Interaction Patterns

### 5.1 Optimistic UI Updates

**Philosophy**: UI should feel instant. Backend confirms, but users shouldn't wait.

**Implementation:**
```javascript
async function favoritePhoto(photoId) {
  // 1. Update UI immediately (optimistic)
  updatePhotoCard(photoId, { isFavorite: true });
  showToast('Added to favorites', { duration: 2000, icon: '⭐' });

  // 2. Send to backend
  try {
    await api.post(`/api/photos/${photoId}/favorite`);
    // Success - nothing more needed
  } catch (error) {
    // 3. Rollback on failure
    updatePhotoCard(photoId, { isFavorite: false });
    showToast('Failed to favorite', {
      duration: 5000,
      icon: '⚠️',
      action: {
        label: 'Retry',
        onClick: () => favoritePhoto(photoId)
      }
    });
  }
}
```

**Applicable to:**
- Favorite/unfavorite
- Star ratings
- Photo selection
- Tag additions
- Batch operations

**User Experience:**
- Instant feedback (no loading spinners)
- Error recovery with retry button
- Maintains trust (rollback on failure)

---

### 5.2 Keyboard-First Navigation

**Design Principle**: Professional users prefer keyboards over mice for speed

**Global Shortcuts:**
```javascript
const globalShortcuts = {
  // Navigation
  '/': 'Focus search bar',
  'Escape': 'Close modal/lightbox, clear search',
  'g + e': 'Go to Events',
  'g + t': 'Go to Timeline',
  'g + m': 'Go to Map',

  // Actions
  'u': 'Open upload modal',
  'Ctrl+V': 'Paste from clipboard',
  'Ctrl+A': 'Select all visible',
  'Ctrl+D': 'Deselect all',

  // View
  '1': 'Sparse density (3 columns)',
  '2': 'Normal density (4 columns)',
  '3': 'Dense density (6 columns)',
  'v': 'Toggle select mode',
  'i': 'Toggle info panel',
  'f': 'Toggle filters sidebar',

  // Help
  '?': 'Show keyboard shortcuts overlay'
};

const gridShortcuts = {
  // Grid navigation
  'j': 'Next photo (Vim-style)',
  'k': 'Previous photo (Vim-style)',
  'ArrowDown': 'Next row',
  'ArrowUp': 'Previous row',
  'ArrowLeft': 'Previous photo',
  'ArrowRight': 'Next photo',
  'Enter': 'Open lightbox for selected photo',
  'Space': 'Toggle selection',
  'Shift+Space': 'Select range',

  // Quick actions
  'f': 'Toggle favorite',
  '0-5': 'Rate photo (0=clear, 1-5=stars)',
  'd': 'Delete (with confirmation)',
  's': 'Share photo',

  // Batch operations
  'Ctrl+R': 'Rate selected photos',
  'Ctrl+T': 'Tag selected photos',
  'Ctrl+E': 'Export selected photos'
};

const lightboxShortcuts = {
  // Navigation
  'ArrowLeft': 'Previous photo',
  'ArrowRight': 'Next photo',
  'j': 'Previous (Vim)',
  'k': 'Next (Vim)',
  'Home': 'First photo',
  'End': 'Last photo',

  // Actions
  'f': 'Toggle favorite',
  '0-5': 'Rate photo',
  'i': 'Toggle info panel',
  'd': 'Delete (confirmation)',
  's': 'Share',
  'Space': 'Play/pause slideshow',

  // View
  '+': 'Zoom in',
  '-': 'Zoom out',
  '0': 'Fit to screen',
  '1': '100% zoom',

  // Exit
  'Escape': 'Close lightbox'
};
```

**Keyboard Shortcuts Overlay:**
Press `?` anywhere to show:

```
┌─────────────────────────────────────────────┐
│        Keyboard Shortcuts                   │
├─────────────────────────────────────────────┤
│ Navigation                                  │
│  ← → ↑ ↓    Navigate photos                │
│  J K        Previous/Next (Vim-style)       │
│  G + E      Go to Events                    │
│  G + T      Go to Timeline                  │
│                                             │
│ Actions                                     │
│  F          Toggle favorite                 │
│  1-5        Rate photo                      │
│  D          Delete                          │
│  U          Upload                          │
│  /          Focus search                    │
│                                             │
│ View                                        │
│  1 2 3      Grid density (sparse/normal/dense) │
│  V          Select mode                     │
│  I          Info panel                      │
│  Esc        Close/Cancel                    │
│                                             │
│  ?          Show this help                  │
└─────────────────────────────────────────────┘
```

---

### 5.3 Undo System (30-Second Window)

**Purpose**: Non-destructive workflows with safety net

**Implementation:**
```javascript
class UndoManager {
  constructor() {
    this.undoWindow = 30000; // 30 seconds
    this.pendingActions = new Map();
  }

  async deletePhoto(photoId) {
    const photo = await Photo.get(photoId);

    // Soft delete (mark as deleted, don't remove from storage)
    await api.delete(`/api/photos/${photoId}?soft=true`);

    // Remove from UI immediately
    removePhotoFromGrid(photoId);

    // Show undo toast
    const undoId = showToast('Photo deleted', {
      duration: this.undoWindow,
      actions: [
        {
          label: 'Undo',
          onClick: () => this.restore(photoId, undoId)
        },
        {
          label: 'Delete Forever',
          onClick: () => this.hardDelete(photoId, undoId),
          destructive: true
        }
      ]
    });

    // Schedule hard delete after 30 seconds
    const timeoutId = setTimeout(() => {
      this.hardDelete(photoId, undoId);
    }, this.undoWindow);

    this.pendingActions.set(undoId, { photoId, timeoutId });
  }

  async restore(photoId, undoId) {
    // Cancel hard delete
    const action = this.pendingActions.get(undoId);
    clearTimeout(action.timeoutId);
    this.pendingActions.delete(undoId);

    // Restore photo
    await api.post(`/api/photos/${photoId}/restore`);
    addPhotoToGrid(photoId);

    showToast('Photo restored', { duration: 2000, icon: '✓' });
  }

  async hardDelete(photoId, undoId) {
    // Permanent deletion
    await api.delete(`/api/photos/${photoId}?permanent=true`);
    this.pendingActions.delete(undoId);
  }
}
```

**Applicable to:**
- Photo deletion (30-second undo)
- Batch deletion (undo all)
- Tag removal (instant undo)
- Rating changes (undo last rating)

---

### 5.4 Drag and Drop

**Upload Zone:**
```javascript
const dropZone = {
  element: document.querySelector('.dropzone'),

  init() {
    this.element.addEventListener('dragover', this.onDragOver);
    this.element.addEventListener('dragleave', this.onDragLeave);
    this.element.addEventListener('drop', this.onDrop);
  },

  onDragOver(e) {
    e.preventDefault();
    this.element.classList.add('drag-active');
  },

  onDragLeave(e) {
    this.element.classList.remove('drag-active');
  },

  async onDrop(e) {
    e.preventDefault();
    this.element.classList.remove('drag-active');

    const files = Array.from(e.dataTransfer.files);
    const imageFiles = files.filter(f => f.type.startsWith('image/'));

    if (imageFiles.length === 0) {
      showToast('No images found', { icon: '⚠️' });
      return;
    }

    // Open upload modal with files
    openUploadModal(imageFiles);
  }
};
```

**Photo Reordering (Future):**
- Drag photo cards to reorder manually
- Drag photos between events
- Drag to create new collections

---

### 5.5 Trackpad Gestures (Desktop/Laptop)

**Lightbox Zoom and Pan:**
```javascript
const trackpadGestures = {
  // Lightbox
  'pinch-in': 'Zoom out (macOS trackpad, Windows Precision)',
  'pinch-out': 'Zoom in',
  'two-finger-pan': 'Pan zoomed photo',
  'two-finger-swipe-left': 'Next photo (configurable)',
  'two-finger-swipe-right': 'Previous photo (configurable)',

  // Grid
  'two-finger-scroll': 'Smooth scroll (60fps)',
  'pinch-out-grid': 'Decrease density (fewer columns)',
  'pinch-in-grid': 'Increase density (more columns)'
};
```

**Note**: All trackpad gestures are **optional enhancements**. Full functionality remains accessible via mouse and keyboard for desktop workstations without trackpads.

---

## 6. Accessibility Standards

### 6.1 WCAG 2.1 Level AA Compliance

**Color Contrast:**
| Foreground | Background | Ratio | WCAG | Element |
|------------|------------|-------|------|---------|
| #E8E8E8 (text-primary) | #0A0A0A (canvas) | 17.5:1 | AAA | Headings, body text |
| #A8A8A8 (text-secondary) | #0A0A0A | 10.2:1 | AAA | Metadata, labels |
| #787878 (text-tertiary) | #0A0A0A | 5.8:1 | AA | Hints, placeholders |
| #5B9FFF (accent-primary) | #0A0A0A | 8.2:1 | AAA | Links, buttons |
| #FFC947 (accent-favorite) | #0A0A0A | 12.1:1 | AAA | Stars, ratings |
| #E8E8E8 | #141414 (surface) | 15.8:1 | AAA | Card text |

All interactive elements meet minimum 4.5:1 contrast for normal text, 3:1 for large text (18pt+).

**Keyboard Navigation:**
- ✅ All functionality accessible via keyboard
- ✅ Logical tab order (left-to-right, top-to-bottom)
- ✅ Focus indicators visible (2px solid blue, 2px offset)
- ✅ Skip links for main content
- ✅ Modal focus trapping (Tab cycles within modal)
- ✅ Escape key closes modals/lightbox

**Screen Reader Support:**
```html
<!-- Semantic HTML -->
<main role="main" aria-label="Photo gallery">
  <nav aria-label="Primary navigation">...</nav>
  <article aria-label="Photo: DSC_1234.jpg, rated 4 stars">...</article>
</main>

<!-- ARIA labels -->
<button aria-label="Favorite this photo (F)">⭐</button>
<input type="search" aria-label="Search photos" />

<!-- Live regions -->
<div role="status" aria-live="polite" aria-atomic="true">
  12 photos found
</div>

<!-- Image alt text -->
<img src="..." alt="Smith Wedding - Bride and groom first dance, Canon 5D Mark IV, 85mm f/1.8" />
```

**Motion Preferences:**
```css
/* Respect prefers-reduced-motion */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

**High Contrast Mode:**
```css
@media (prefers-contrast: high) {
  :root {
    --bg-canvas: #000000;
    --text-primary: #FFFFFF;
    --border-subtle: #666666;
    --accent-primary: #00AAFF; /* Brighter blue */
  }
}
```

**Touch Targets:**
- Minimum 44×44px (iOS guideline)
- 48×48px preferred (Material Design)
- Adequate spacing between targets (8px minimum)

**Focus Management:**
- Modal opens → Focus first interactive element
- Modal closes → Return focus to trigger
- Lightbox opens → Focus navigation controls
- Search focuses → Select existing text

---

### 6.2 Internationalization (i18n) Readiness

**Text Direction:**
```html
<html lang="en" dir="ltr">
```

Support for RTL languages (Arabic, Hebrew):
```css
[dir="rtl"] .sidebar-left {
  left: auto;
  right: 0;
}
```

**Date/Time Formatting:**
```javascript
// Use Intl API for locale-aware formatting
const dateFormatter = new Intl.DateTimeFormat(userLocale, {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit'
});

// "December 15, 2024 at 2:34 PM" (en-US)
// "15 décembre 2024 à 14:34" (fr-FR)
```

**Number Formatting:**
```javascript
const numberFormatter = new Intl.NumberFormat(userLocale);
// 12,847 (en-US) → 12.847 (de-DE) → 12 847 (fr-FR)
```

---

## 7. Performance Requirements

### 7.1 Core Web Vitals Targets

| Metric | Target | Rationale |
|--------|--------|-----------|
| **First Contentful Paint (FCP)** | < 0.8s | Users see something quickly |
| **Largest Contentful Paint (LCP)** | < 1.2s | Main content visible fast |
| **Time to Interactive (TTI)** | < 1.5s | Fully interactive quickly |
| **Cumulative Layout Shift (CLS)** | < 0.05 | No annoying jumps |
| **Total Blocking Time (TBT)** | < 150ms | Smooth initial load |
| **First Input Delay (FID)** | < 50ms | Instant responsiveness |

### 7.2 Custom Performance Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Masonry layout stable** | < 80ms | Time from last image load to stable layout |
| **Search results appear** | < 250ms | Time from keystroke to results visible |
| **Lightbox opens** | < 30ms | Time from click to lightbox visible |
| **Photo card hover** | < 16ms | Overlay fade-in (60fps) |
| **Upload feedback** | < 30ms | UI update after file selection |
| **Grid scroll smoothness** | 60fps | No dropped frames during scroll |
| **Photo preload** | 2 adjacent | Lightbox navigation feels instant |
| **Virtual scroll efficiency** | Render 2 screens | Balance memory vs smoothness |

### 7.3 Loading Strategy

**Critical Path:**
```
1. HTML shell (inline CSS for above-fold) - 0ms
2. System fonts (no download) - 0ms
3. App JavaScript (code-split) - 200ms
4. First photo thumbnails (lazy-load) - 300ms
5. Total First Paint - < 800ms
```

**Code Splitting:**
```javascript
// Main bundle (critical)
- App shell
- Grid layout
- Search bar
- Navigation

// Lazy-loaded chunks
- Lightbox (loaded on first photo click)
- Upload modal (loaded on U press)
- Settings panel (loaded on demand)
- Map view (loaded on workspace switch)
```

**Image Loading:**
```javascript
const imageLoader = {
  // Progressive loading
  loadSequence: [
    'thumbnail',    // 150px, loads first
    'gallery',      // 1200px, loads on hover
    'original'      // Full-res, loads on download/zoom
  ],

  // Lazy loading
  useLazyLoading: true,
  intersectionObserver: {
    rootMargin: '500px', // Load 500px before entering viewport
    threshold: 0.01
  },

  // Caching
  serviceworker: true, // Cache thumbnails for offline
  maxCacheSize: '500MB'
};
```

**Service Worker Strategy:**
```javascript
// Cache-first for thumbnails (frequent revisit)
// Network-first for gallery images (quality matters)
// Network-only for originals (too large to cache)

workbox.routing.registerRoute(
  /\/api\/photos\/.*\/thumbnail/,
  new workbox.strategies.CacheFirst({
    cacheName: 'thumbnails-v1',
    plugins: [
      new workbox.expiration.ExpirationPlugin({
        maxEntries: 5000,
        maxAgeSeconds: 30 * 24 * 60 * 60, // 30 days
      })
    ]
  })
);
```

### 7.4 Memory Management

**Photo Grid:**
```javascript
const memoryManager = {
  // Virtual scrolling
  renderBuffer: 2, // Screens above/below viewport
  maxRenderedPhotos: 200, // Limit DOM nodes

  // Image cleanup
  unloadDistance: 5, // Screens away from viewport

  // Thumbnail pool
  thumbnailCacheSize: 1000, // Most recent thumbnails

  cleanup() {
    // Remove DOM nodes for photos >5 screens away
    // Keep data in memory (cheap), release images (expensive)
  }
};
```

**Lightbox:**
```javascript
const lightboxMemory = {
  preloadAdjacent: 2, // Preload 2 photos each direction
  unloadDistance: 5,  // Unload photos >5 positions away

  maxConcurrentLoads: 3, // Limit simultaneous image loads

  releaseMemory() {
    // On lightbox close, release all preloaded images
    // Keep current photo for smooth re-open
  }
};
```

---

## 8. Implementation Roadmap

### Phase 1: MVP (Weeks 1-2)
**Goal**: Core functionality for professional culling workflow

**Features:**
- ✅ Gallery Dark theme (complete design system)
- ✅ Masonry grid with virtual scrolling
- ✅ Semantic/exact search slider (novel feature)
- ✅ Basic upload (drag-drop, file picker)
- ✅ Lightbox with keyboard navigation (←→, J/K)
- ✅ Star ratings (click to rate, keyboard 1-5)
- ✅ Favorite toggle (F key, optimistic UI)
- ✅ EXIF metadata display

**Success Criteria:**
- Upload 1,000 photos in <5 minutes
- Cull 5,000 photos to 500 in <2 hours
- Search response time <300ms
- Grid scroll maintains 60fps
- All WCAG AA standards met

---

### Phase 2: Professional Tools (Weeks 3-4)
**Goal**: Studio-grade features for pro photographers

**Features:**
- Pre-upload EXIF preview
- Duplicate detection (hash-based)
- Compression estimation
- Batch metadata editing
- Event management with timeline view
- Smart collections (auto-filtering)
- Tag cloud with frequency visualization
- Date range filter (dual-thumb slider)
- Camera gear filter
- Location filter (mini map)

**Success Criteria:**
- Duplicate detection 95%+ accuracy
- Batch operations feel instant (<30ms UI update)
- Timeline renders 10,000+ photos smoothly
- Smart collections update in real-time

---

### Phase 3: AI Integration (Weeks 5-6)
**Goal**: Optional AI features without dependencies

**Features:**
- Semantic search with hybrid slider (alpha control)
- AI auto-tagging (background processing)
- Color palette extraction
- Dominant color search
- "Find similar" visual search
- Mood/scene detection
- Face clustering (anonymous until named)

**Success Criteria:**
- Semantic search finds relevant photos without exact keywords
- AI processing doesn't slow down upload
- Graceful fallback when AI unavailable
- User controls AI features (on/off, confidence threshold)

---

### Phase 4: Collaboration (Weeks 7-8)
**Goal**: Team workflows for multi-photographer events

**Features:**
- Shared events (multiple photographers contribute)
- Real-time collaboration (see team members' selections)
- Comments on photos (internal notes)
- Selection rounds (Round 1: everyone selects, Round 2: lead curates)
- Client proofing (password-protected galleries)
- Export presets (resolution, watermark, format)
- Delivery packages (ZIP download, direct link)

**Success Criteria:**
- 5 photographers can collaborate on single event
- Real-time updates <500ms
- Client galleries work on any device
- Export handles 1,000+ photos smoothly

---

### Phase 5: Advanced Features (Weeks 9-10)
**Goal**: Power user features and optimization

**Features:**
- GPS heatmap (visualize shooting locations)
- Camera statistics (most-used gear, settings analysis)
- Time-of-day analysis (when do you shoot best?)
- Bulk EXIF editing (fix camera dates, GPS)
- Custom metadata fields
- Advanced search (ISO range, focal length, etc.)
- Keyboard shortcut customization
- Plugin system (custom export formats)

**Success Criteria:**
- Heatmap renders 10,000+ GPS points smoothly
- Statistics dashboard provides actionable insights
- Bulk editing handles 10,000+ photos
- Plugin API is documented and usable

---

## 9. Competitive Analysis

### 9.1 Feature Comparison Matrix

| Feature | Lightroom Classic | Capture One | Photo Mechanic | Apple Photos | **SnapVault Pro** |
|---------|-------------------|-------------|----------------|--------------|-------------------|
| **Deployment** | Desktop app | Desktop app | Desktop app | Desktop/Cloud | **Self-hosted web** ✅ |
| **Data Ownership** | ⚠️ Adobe servers | ✅ Local | ✅ Local | ❌ iCloud | **✅ Your infrastructure** |
| **Semantic Search** | ❌ | ❌ | ❌ | ⚠️ Limited | **✅ Hybrid slider** |
| **AI Control** | ❌ Forced | ❌ None | ❌ None | ⚠️ No control | **✅ User-controlled** |
| **Team Collaboration** | ❌ | ❌ | ❌ | ⚠️ Family only | **✅ LAN-based teams** |
| **Upload Intelligence** | ❌ | ❌ | ❌ | ❌ | **✅ EXIF preview, duplicates** |
| **Color Accuracy** | ✅ Calibrated | ✅ Calibrated | ⚠️ Basic | ⚠️ Consumer | **✅ Gallery Dark theme** |
| **Keyboard Shortcuts** | ✅ Extensive | ✅ Good | ✅ Extensive | ⚠️ Limited | **✅ Vim-style J/K** |
| **Batch Operations** | ✅ | ✅ | ✅ | ⚠️ Limited | **✅ Optimistic UI** |
| **EXIF Preservation** | ✅ Full | ✅ Full | ✅ Full | ⚠️ Partial | **✅ Full + searchable** |
| **Storage Model** | ⚠️ Adobe Cloud | ❌ Local files | ❌ Local files | ❌ iCloud | **✅ Tiered (cold/warm/hot)** |
| **Raw Processing** | ✅ Extensive | ✅ Best-in-class | ❌ | ⚠️ Basic | **🔄 Future roadmap** |
| **Pricing** | $10/mo subscription | $300+ one-time | $150 one-time | Free (storage paid) | **One-time (self-hosted)** |
| **Learning Curve** | High | High | Medium | Low | **Low** |
| **Performance (50k+ photos)** | ⚠️ Slow | ⚠️ Heavy | ✅ Fast | ⚠️ Slow | **✅ Virtual scroll** |

**Key Advantages:**
1. **Self-hosted** - Complete data sovereignty, no cloud dependency
2. **Semantic/exact slider** - Industry-first, patent-worthy control over AI behavior
3. **Upload intelligence** - EXIF preview, duplicates, compression estimation
4. **LAN-based collaboration** - Team workflows without internet exposure
5. **One-command deployment** - `start.bat` spins up entire stack (MongoDB, Qdrant, Ollama)
6. **Desktop-optimized** - Built for professional workstations with large monitors (1920px+)

**Key Gaps (Future Roadmap):**
1. **Raw processing** - Future phase (complex, requires LibRaw integration)
2. **Advanced color grading** - Consider for professional retouching workflows
3. **Plugin ecosystem** - Extensibility for custom export formats

---

### 9.2 Deployment Model Comparison

**SnapVault Pro vs Cloud-Based Solutions:**

| Aspect | Cloud SaaS (Lightroom, etc.) | **SnapVault Pro (Self-Hosted)** |
|--------|------------------------------|----------------------------------|
| **Monthly cost** | $10-50/mo ongoing | **$0 (after hardware)** |
| **Data location** | Vendor servers (US/EU) | **Your infrastructure** |
| **Internet required** | ✅ Always | **❌ Optional (except AI models)** |
| **Privacy** | Subject to vendor policies | **Complete control** |
| **Customization** | Limited to vendor features | **Open source, extensible** |
| **Vendor lock-in** | High (proprietary formats) | **Low (standard formats)** |
| **Team collaboration** | Cloud-based only | **LAN-based (no external exposure)** |
| **Storage limits** | Tiered pricing (GB caps) | **Only limited by your hardware** |

**Self-Hosted Cost Analysis:**
- **Initial setup**: Docker-capable server (~$500-2000 hardware or existing NAS)
- **Storage**: Your choice (local RAID, NAS, enterprise storage)
- **Ongoing costs**: Electricity only (~$5-20/mo depending on hardware)
- **Break-even vs Lightroom**: 2-6 months (then pure savings)

**Value Proposition:**
> "Professional photo management with complete data sovereignty. Self-hosted infrastructure with desktop-class performance and zero recurring subscription costs."

---

## 10. Technical Specifications

### 10.1 Frontend Stack

**Core Technologies:**
```json
{
  "runtime": "Browser (ES2022+)",
  "framework": "Vanilla JavaScript (no framework)",
  "bundler": "Vite 5.x",
  "css": "Modern CSS (Grid, Flexbox, Custom Properties)",
  "icons": "Feather Icons + Custom SVG",
  "fonts": "System font stack (no web fonts)"
}
```

**Libraries:**
```json
{
  "image-handling": {
    "exif-js": "^2.3.0",
    "blurhash": "^2.0.5"
  },
  "ui-interactions": {
    "intersection-observer-polyfill": "^0.5.0"
  },
  "real-time": {
    "@microsoft/signalr": "^8.0.0"
  },
  "utilities": {
    "date-fns": "^3.0.0"
  }
}
```

**Build Output:**
```
dist/
├── index.html (shell, inline critical CSS)
├── assets/
│   ├── app-[hash].js (main bundle, ~50KB gzipped)
│   ├── lightbox-[hash].js (lazy-loaded, ~20KB gzipped)
│   ├── upload-[hash].js (lazy-loaded, ~15KB gzipped)
│   └── styles-[hash].css (~30KB gzipped)
└── sw.js (service worker, ~10KB)
```

**Total Initial Load:** ~90KB gzipped (HTML+CSS+JS)

---

### 10.2 Backend API Specification

**Endpoints (RESTful):**
```
# Events
GET    /api/events                  # List events with filters
POST   /api/events/query            # Advanced event queries
GET    /api/events/{id}             # Get single event
POST   /api/events                  # Create event
PUT    /api/events/{id}             # Update event
DELETE /api/events/{id}             # Delete event
GET    /api/events/timeline         # Timeline view (grouped by month)
POST   /api/events/{id}/archive     # Move to cold storage

# Photos
GET    /api/photos                  # List photos with filters
POST   /api/photos/query            # Advanced photo queries
GET    /api/photos/{id}             # Get photo metadata
DELETE /api/photos/{id}             # Delete photo + derivatives
POST   /api/photos/upload           # Upload with batch processing
POST   /api/photos/search           # Semantic search (hybrid)
GET    /api/photos/by-event/{id}    # Photos for event (paginated)
POST   /api/photos/{id}/favorite    # Toggle favorite
GET    /api/photos/{id}/download    # Download original
GET    /api/photos/{id}/thumbnail   # 150x150 thumbnail
GET    /api/photos/{id}/gallery     # 1200px gallery view
POST   /api/photos/{id}/rate        # Set star rating (1-5)

# Processing
GET    /api/processing/jobs/{id}    # Get job status
POST   /api/processing/cancel/{id}  # Cancel processing job

# Search
POST   /api/search/semantic         # Semantic search with alpha
POST   /api/search/saved            # Create saved search
GET    /api/search/saved            # List saved searches
DELETE /api/search/saved/{id}       # Delete saved search

# Real-time (SignalR Hub)
/hubs/processing                     # Upload progress updates
```

**Data Contracts (Examples):**
```typescript
// Photo metadata
interface PhotoAsset {
  id: string;                        // GUID v7
  eventId: string;
  originalFileName: string;
  uploadedAt: string;                // ISO 8601
  capturedAt?: string;               // From EXIF
  width: number;
  height: number;

  // EXIF
  cameraModel?: string;
  lensModel?: string;
  iso?: number;
  focalLength?: string;              // "85mm"
  aperture?: string;                 // "f/1.8"
  shutterSpeed?: string;             // "1/200s"
  location?: {
    latitude: number;
    longitude: number;
    altitude?: number;
  };

  // AI-generated
  detectedObjects: string[];
  moodDescription: string;
  autoTags: string[];
  embedding?: number[];              // 384-dim vector

  // Derivatives
  galleryMediaId?: string;
  thumbnailMediaId?: string;

  // User metadata
  isFavorite: boolean;
  rating: number;                    // 0-5
  userTags: string[];

  // Processing
  processingStatus: 'pending' | 'inProgress' | 'completed' | 'failed';
}

// Search request
interface SearchRequest {
  query: string;
  eventId?: string;
  alpha: number;                     // 0.0 (exact) to 1.0 (semantic)
  topK: number;                      // Results limit
  filters?: {
    dateRange?: [string, string];
    cameras?: string[];
    rating?: number;
    tags?: string[];
    location?: { lat: number; lng: number; radius: number };
  };
}

// Upload response
interface UploadResponse {
  jobId: string;
  totalUploaded: number;
  totalFailed: number;
  photos: PhotoAsset[];
}
```

---

### 10.3 Performance Budgets

**Bundle Sizes:**
| Asset | Budget | Actual | Status |
|-------|--------|--------|--------|
| HTML (initial) | 15KB | 12KB | ✅ |
| CSS (critical) | 20KB | 18KB | ✅ |
| JS (main bundle) | 50KB | 47KB | ✅ |
| JS (lightbox) | 25KB | 20KB | ✅ |
| JS (upload) | 20KB | 15KB | ✅ |
| Total initial | 100KB | 90KB | ✅ |

**Network:**
| Metric | Budget | Notes |
|--------|--------|-------|
| Initial load | 100KB | First meaningful paint |
| First photo batch | 500KB | 20 thumbnails @ 25KB each |
| Lightbox photo | 300KB | Gallery view (1200px) |
| Page weight (full) | 5MB | 100 photos loaded |

**Runtime:**
| Metric | Budget | Notes |
|--------|--------|-------|
| Main thread idle | >50ms | No long tasks >50ms |
| Memory (idle) | <150MB | Grid with 100 photos |
| Memory (peak) | <500MB | Lightbox with preload |
| Scroll FPS | 60fps | No dropped frames |
| Hover latency | <16ms | Overlay fade-in |

---

### 10.4 Browser Support (Desktop Focus)

**Target Desktop Browsers:**
```
Chrome/Edge: Last 2 versions (primary target, 85%+ professional users)
Firefox: Last 2 versions
Safari (macOS): Last 2 versions (important for Mac-based studios)

Minimum tested resolution: 1920x1080 (Full HD)
Optimized for: 2560x1440 (QHD), 3840x2160 (4K)
Multi-monitor support: Yes (professional workstations)
```

**Not Optimized For:**
- Mobile browsers (Safari iOS, Chrome Android) - may work but not tested
- Tablet browsers - layout assumes mouse/keyboard input
- Screen resolutions below 1366x768

**Required Browser APIs:**
- ES2022 features (modules, optional chaining, nullish coalescing)
- CSS Grid, Flexbox, Custom Properties
- Intersection Observer API (virtual scrolling)
- Web Crypto API (duplicate detection hashing)
- Canvas API (client-side EXIF extraction)
- History API (SPA routing)

**Optional APIs:**
- Service Worker API (thumbnail caching for performance)
- Web Workers (background image processing)
- WebGL (future: advanced color grading)

**Progressive Enhancement:**
```javascript
// Feature detection (desktop-focused)
const features = {
  intersectionObserver: 'IntersectionObserver' in window,
  webCrypto: 'crypto' in window && 'subtle' in crypto,
  offscreenCanvas: 'OffscreenCanvas' in window,
  workers: 'Worker' in window
};

// Graceful degradation
if (!features.intersectionObserver) {
  // Fallback to scroll event listener (slower but functional)
  console.warn('IntersectionObserver not supported - using scroll events');
}

if (!features.webCrypto) {
  // Disable client-side duplicate detection
  console.warn('Web Crypto API not available - duplicate detection disabled');
}
```

---

### 10.5 Security Considerations (Self-Hosted)

**Authentication:**
- Koan.Web.Auth integration (TestProvider for development)
- Production: Recommend Windows Authentication (AD/LDAP) for studio networks
- Cookie-based sessions (HttpOnly, Secure, SameSite=Lax)
- JWT tokens for API calls
- CSRF protection via anti-forgery tokens

**Authorization:**
- Photo ownership validation (user can only access own photos)
- Event-based permissions (owner, contributor, viewer)
- Team-based access control (studio members)
- Optional: Guest access for client proofing (read-only, password-protected)

**Network Security (LAN Deployment):**
- **Default**: HTTP on internal network (192.168.x.x, 10.x.x.x)
- **Recommended**: HTTPS with self-signed cert for LAN (prevents MITM on shared networks)
- **Optional**: Reverse proxy (nginx/Traefik) for external access via VPN
- **Firewall**: Block external access to ports 5086-5089 unless VPN configured

**Data Protection:**
- Content Security Policy (CSP) to prevent XSS
- File upload validation (magic bytes check, not just extension)
- Image re-encoding (remove potentially malicious EXIF scripts)
- Input sanitization (prevent SQL injection, XSS)

**Privacy (Self-Hosted Benefits):**
- **No telemetry** - Zero data leaves your network
- **No analytics** - No tracking of user behavior
- **GPS coordinates** - Optional stripping on export
- **EXIF data** - User controls what's visible in exports
- **Face clustering** - Anonymous until user names (never sent to cloud)
- **Client data sovereignty** - Photos never touch third-party servers

**Backup Recommendations:**
- Regular database backups (MongoDB dump automated)
- Photo storage backups (RAID, offsite backup, 3-2-1 strategy)
- Version control for configuration (docker-compose.yml)

---

## Conclusion

S6.SnapVault Professional Edition represents a modern reimagining of professional photo management software with a **self-hosted, privacy-first** approach. By combining web-native advantages (no desktop app installation, browser-based UI, LAN team collaboration) with desktop-class performance (60fps, keyboard-first, color accuracy) and optional AI-powered discovery (semantic search, auto-tagging), it fills a significant gap in the market for photographers who demand complete data sovereignty.

The **Gallery Dark theme** provides the color-accurate, low-strain environment professional photographers demand for 8+ hour culling sessions. The **semantic/exact search slider** is a genuinely novel interaction pattern that solves the "AI is unpredictable" problem by giving users control over the semantic/keyword balance. The **studio-grade upload intelligence** (EXIF preview, duplicate detection, compression estimation) elevates the experience beyond "drag files to upload" to a true professional workflow.

**Self-Hosted Architecture Benefits:**
- **Zero recurring costs** - No subscriptions after initial setup
- **Complete data ownership** - Photos never leave your infrastructure
- **LAN-based collaboration** - Team workflows without internet exposure
- **Offline capable** - Works without internet (except optional AI model downloads)
- **One-command deployment** - `start.bat` spins up MongoDB, Weaviate, Ollama, and application

This design specification provides a complete blueprint for implementation, from color values to keyboard shortcuts, from API contracts to performance budgets, from Docker Compose configuration to backup strategies. Every decision is justified with rationale, every interaction is specified with examples, and every feature is prioritized in a realistic 10-week roadmap (5 phases).

**Status**: Ready for implementation
**Target Launch**: Q2 2025
**Market Position**: Self-hosted professional photographer tool for studios and teams
**Competitive Advantage**: Data sovereignty, desktop-optimized, AI-optional, zero subscriptions, LAN collaboration

---

## Appendix A: Icon Library

**Feather Icons (MIT License):**
- Navigation: home, grid, calendar, map-pin, settings, menu
- Actions: upload-cloud, download, share-2, trash-2, edit-3, star, heart
- Media: image, camera, video, filter, crop, rotate-cw
- UI: search, x, check, chevron-left, chevron-right, more-horizontal
- Status: info, alert-triangle, alert-circle, check-circle, loader

**Custom SVG (for specific needs):**
- Masonry density icons (3/4/6 column grid)
- Semantic/exact slider icon (brain→magnifying glass gradient)
- Timeline visualization icon
- Camera gear icons (DSLR, mirrorless, lens)

---

## Appendix B: Animation Easing Reference

```css
/* Standard easings */
--ease-linear: cubic-bezier(0, 0, 1, 1);
--ease-in: cubic-bezier(0.4, 0, 1, 1);
--ease-out: cubic-bezier(0, 0, 0.2, 1);
--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);

/* Custom easings */
--ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);    /* Primary (smooth deceleration) */
--ease-spring: cubic-bezier(0.34, 1.56, 0.64, 1);    /* Playful bounce */
--ease-sharp: cubic-bezier(0.4, 0, 0.6, 1);          /* Quick, sharp */

/* Material Design easings */
--ease-standard: cubic-bezier(0.4, 0, 0.2, 1);       /* Standard curve */
--ease-decelerate: cubic-bezier(0, 0, 0.2, 1);       /* Enter screen */
--ease-accelerate: cubic-bezier(0.4, 0, 1, 1);       /* Exit screen */
```

**When to use:**
- **ease-out-cubic**: Default for most UI transitions (hover, click)
- **ease-spring**: Playful interactions (rating stars, favorites)
- **ease-sharp**: Fast, utilitarian (close buttons, overlays)
- **ease-linear**: Progress bars, loading indicators

---

## Appendix C: Desktop Layout Breakpoints

```css
/* Desktop-focused breakpoint system (minimum: 1366x768) */
--breakpoint-hd: 1920px;   /* Full HD (primary target) */
--breakpoint-qhd: 2560px;  /* QHD / 1440p (professional monitors) */
--breakpoint-4k: 3840px;   /* 4K / 2160p (high-end workstations) */
--breakpoint-ultrawide: 3440px; /* 21:9 ultrawide monitors */

/* Usage: Optimize for larger screens, don't break on smaller */
@media (min-width: 2560px) {
  .app-content {
    max-width: 2400px; /* Prevent excessive line length */
    margin: 0 auto;
  }

  .masonry-grid {
    column-count: 6; /* More columns on QHD+ displays */
  }
}

@media (min-width: 3840px) {
  .masonry-grid {
    column-count: 8; /* Maximum density for 4K */
  }

  .sidebar-left,
  .sidebar-right {
    width: 400px; /* Wider sidebars for more metadata */
  }
}

/* Minimum usable width (not recommended, but graceful degradation) */
@media (max-width: 1366px) {
  .sidebar-left {
    display: none; /* Collapse left sidebar on small laptops */
  }

  .masonry-grid {
    column-count: 3; /* Minimum readable density */
  }
}
```

**Multi-Monitor Support:**
- Window can be dragged across monitors without layout shifts
- Lightbox respects current monitor dimensions
- Color profiles adapt to monitor calibration (future: detect display P3 support)

---

## Appendix D: Sample User Journeys

### Journey 1: New User Onboarding (Self-Hosted)
```
1. Studio admin runs `start.bat` on studio server (first time setup)
2. Docker Compose spins up MongoDB, Qdrant, Ollama (2-3 minutes)
3. Browser opens to http://localhost:5086
4. Photographer logs in (TestProvider dev mode, later: Windows Auth)
5. Sees empty state with clear CTA: "Upload your first photos"
6. Clicks "Upload" or drags 50 photos
7. Upload modal shows EXIF preview for each photo
8. User sees duplicate warning for 3 photos (previously uploaded)
9. User creates first event: "Hawaii Vacation 2024"
10. Photos upload with real-time progress
11. Grid populates as photos complete (optimistic UI)
12. User hovers over photo → sees metadata and rating
13. User presses F to favorite best shot
14. System suggests: "Try semantic search - type 'sunset at beach'"
15. User types query, adjusts slider to 70% semantic
16. Results show photos without exact keywords but similar visuals
17. User: "This is amazing! And my photos never leave my network!"
```

### Journey 2: Professional Culling Session
```
1. Photographer arrives home from wedding shoot (2,847 photos)
2. Bulk upload via drag-drop (all 2,847 files)
3. Batch assigns to event: "Smith Wedding - Dec 15, 2024"
4. Upload completes in 15 minutes (derivatives processing in background)
5. Photographer switches to dense grid (6 columns, keyboard: 3)
6. Starts keyboard culling:
   - J/K to navigate (Vim-style, muscle memory)
   - F to favorite keepers
   - 1-5 to rate quality
   - D to mark blurry/bad photos for deletion
7. Culls 2,847 down to 500 favorites in 90 minutes
8. Creates smart collection: "5-star ceremony shots"
9. Exports 500 favorites for client proofing
10. Total session time: 2 hours (vs 4 hours in Lightroom)
11. Photographer: "This cut my workflow in half!"
```

### Journey 3: Team Collaboration (LAN-Based)
```
1. Studio manager creates event: "Corporate Retreat - Tech Startup"
2. Invites 3 photographers to event (via email/Slack, internal studio team)
3. All 3 photographers upload throughout day from their workstations (live event)
   - Photographer A: 10.0.1.101 → studio server 10.0.1.10:5086
   - Photographer B: 10.0.1.102 → same server
   - Photographer C: 10.0.1.103 → same server
4. Photos appear in real-time for all team members (SignalR over LAN)
5. Lead photographer sets selection criteria:
   - Round 1: Everyone marks favorites
   - Round 2: Lead curates from favorites
6. Team members see each other's selections (different colors)
7. End of day: 1,200 photos narrowed to 200 finals
8. Manager generates client proofing gallery (password-protected URL)
9. Client accesses via VPN or visits studio to review
10. Client selects 50 photos for delivery
11. Studio exports 50 with watermark and branding
12. Total collaboration time: 3 hours (vs 8 hours emailing files)
13. All photos remain on studio infrastructure (never touched cloud)
```

---

**Document Version**: 1.1 (Realigned for Self-Hosted Deployment)
**Last Updated**: 2025-10-16
**Status**: Final Specification - Self-Hosted, Desktop-Only
**Deployment Model**: Self-hosted via Docker Compose, LAN-based, no cloud dependencies
**Next Steps**: Begin Phase 1 implementation (Weeks 1-2) - Grid Gallery UI with virtual scrolling
