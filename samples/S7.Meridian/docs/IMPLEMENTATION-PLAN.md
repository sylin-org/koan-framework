# Meridian Premium UX Implementation Plan

**Status:** 🔴 In Progress
**Version:** 1.0
**Date:** October 2025
**Authority:** This document defines the implementation plan for Meridian's premium user experience based on approved design decisions.

---

## Executive Summary

### Project Goal
Transform Meridian from a basic analysis workspace into a **premium, enterprise-grade living intelligence platform** with:
- Strategic dashboard for holistic view
- Compact, information-dense insights (borrowed from SnapVault Photo Information panel)
- Type management interfaces (borrowed from GDoc Assistant)
- Two-column workspace layout (Documents 40% | Insights 60%)

### Current State (What Exists)
✅ **Completed** (Initial Implementation):
- Basic HTML structure (`wwwroot/index.html`)
- Complete CSS design system:
  - `design-tokens.css` - UX Spec color semantics
  - `components.css` - Shared components
  - `app.css` - Main layout
  - `workspace.css` - Analysis workspace
  - `evidence-panel.css` - Evidence detail panel
  - `notes.css` - Authoritative Notes styling
- JavaScript architecture:
  - `utils/EventBus.js`, `utils/StateManager.js`
  - `api.js` - Complete API client
  - `components/Toast.js`
  - `app.js` - Main application entry point
- Single-view analysis workspace with:
  - Large insight cards (400px width, 28px value font)
  - Documents section (afterthought)
  - Authoritative Notes (gold treatment)
  - Quality Dashboard

### Target State (What Needs to Change)

🔴 **Missing/Needs Redesign**:
1. **Dashboard View** - Strategic entry point with metrics, recent activity, favorites
2. **Type Management Views** - CRUD interfaces for Analysis Types and Source Types
3. **Compact Insights Panel** - SnapVault-inspired dark panel with fact-row layout
4. **Two-Column Workspace** - Documents (40%) and Insights (60%) side-by-side
5. **Navigation System** - Home → Analyses/Types → Workspace hierarchy

### Key Design Decisions

| Decision | Rationale | Pattern Source |
|----------|-----------|----------------|
| **Dashboard-first navigation** | Users need holistic view before diving into analyses | GDoc Assistant |
| **Card-based type management** | Scannable, information-dense, enterprise-grade | GDoc Assistant |
| **Compact fact-row insights** | 3x information density, professional focus | SnapVault Photo Info |
| **Dark insights panel** | Reduces eye strain, makes data the hero | SnapVault Photo Info |
| **Two-column workspace** | Documents and insights have equal visual weight | Original requirement |
| **Tag-based semantics** | Source ([notes]/[doc]) and confidence ([⭐ 100%]) as tags | SnapVault AI Insights |

---

## Current Implementation Status

### ✅ Phase 0: Foundation (COMPLETED)
- [x] HTML structure with workspace layout
- [x] CSS design system (6 files, 2000+ lines)
- [x] JavaScript utilities (EventBus, StateManager)
- [x] API client with all Meridian endpoints
- [x] Toast notification system
- [x] Main app.js with basic routing

### 🔴 Phase 1: Dashboard & Navigation (IN PROGRESS - START HERE)
- [ ] Create Dashboard view (Home)
- [ ] Hero section with value proposition
- [ ] Quick Actions sidebar
- [ ] System Overview metrics (6 metric cards)
- [ ] Recent Activity stream with status badges
- [ ] Favorites system for types
- [ ] Navigation between views (Home ↔ Analyses ↔ Types)

### 🔴 Phase 2: Type Management Views (PENDING)
- [ ] Analysis Types grid view
- [ ] Source Types grid view
- [ ] Card-based layout with metadata
- [ ] Search and filter functionality
- [ ] AI Create modals
- [ ] CRUD operations
- [ ] Tag system for categorization

### 🔴 Phase 3: Compact Insights Panel (PENDING)
- [ ] Replace card grid with SnapVault-inspired panel
- [ ] Dark panel aesthetic (rgba(26, 29, 36))
- [ ] Fact-row layout (label + value + tags)
- [ ] Grouped sections (Company Info, Certifications, etc.)
- [ ] Tag-based UI ([notes], [doc], [⭐ 100%], [✓ 94%])
- [ ] Override notices inline
- [ ] Document references inline

### 🔴 Phase 4: Two-Column Workspace (PENDING)
- [ ] 40/60 split layout (Documents | Insights)
- [ ] Document list with metadata and status
- [ ] Type selector in header
- [ ] Drag-and-drop refinement
- [ ] Processing indicators

### 🔴 Phase 5: Polish & Testing (PENDING)
- [ ] Keyboard shortcuts
- [ ] Accessibility (WCAG AA)
- [ ] Responsive behavior (Desktop → Tablet → Mobile)
- [ ] Performance optimization
- [ ] Error states and edge cases

---

## Design System Reference

### Color Semantics (From UX-SPECIFICATION.md)

**Per UX Spec Section VII - "Color conveys meaning, not decoration"**

| Color | Hex | Meaning | Usage |
|-------|-----|---------|-------|
| **Gold** | #F59E0B | Authoritative | Notes, user overrides, star ratings, favorites |
| **Blue** | #2563EB | Primary actions | Create, Process, navigation, links |
| **Green** | #059669 | Verified/High confidence | >90% confidence, checkmarks, success |
| **Amber** | #D97706 | Attention needed | Conflicts, medium confidence (70-90%) |
| **Red** | #DC2626 | Error/Critical | Low confidence (<70%), errors, delete |
| **Gray** | #6B7280 | Secondary | Document sources, metadata, disabled |

### Typography Hierarchy

**Per UX Spec - "Value is hero, everything else supports"**

```css
/* Analysis Title - The Entity */
--font-size-analysis-title: 32px;
--line-height-analysis-title: 40px;
--font-weight-analysis-title: 600;

/* Section Headers - Grouping */
--font-size-section-header: 20px;
--line-height-section-header: 28px;
--font-weight-section-header: 600;

/* Field Value - The Data (Hero) - CHANGED FOR COMPACT VIEW */
/* OLD: --font-size-field-value: 28px; (TOO BIG) */
/* NEW: --font-size-field-value: 14px; (SnapVault pattern) */
--font-size-field-value: 14px;
--line-height-field-value: 20px;
--font-weight-field-value: 500;

/* Field Label - Context */
--font-size-field-label: 12px;
--line-height-field-label: 16px;
--font-weight-field-label: 500;

/* Evidence Text - Source passages */
--font-size-evidence: 11px;
--line-height-evidence: 16px;
--font-weight-evidence: 400;

/* Metadata - Timestamps, document refs */
--font-size-metadata: 11px;
--line-height-metadata: 14px;
--font-weight-metadata: 400;
```

### Spacing System

**Per UX Spec - "Generous whitespace for clarity"**

```css
/* SnapVault-inspired compact spacing */
--spacing-panel-section: 20px;  /* Section padding */
--spacing-label-value: 6px;     /* Label to value gap */
--spacing-insight-row: 16px;    /* Between insight rows */
--spacing-tags: 6px;            /* Between tags */
```

---

## Target Architecture

### Navigation Hierarchy

```
Meridian Application
│
├─ 🏠 Dashboard (Home) ← DEFAULT VIEW
│  ├─ Hero Section
│  │  ├─ Value proposition
│  │  ├─ Feature highlights (6 feature cards)
│  │  └─ Primary CTAs ([New Analysis] [Analysis Types] [Source Types])
│  │
│  ├─ Quick Actions Sidebar (Always visible)
│  │  ├─ [➕ Create New Analysis]
│  │  ├─ [📊 View All Analyses]
│  │  ├─ [📋 Manage Analysis Types] + [🤖 AI Create]
│  │  └─ [📄 Manage Source Types] + [🤖 AI Create]
│  │
│  ├─ System Overview (6 metric cards in 2 rows)
│  │  ├─ Active Analyses
│  │  ├─ Processing Jobs
│  │  ├─ Analysis Types
│  │  ├─ Documents Processed
│  │  ├─ Source Types
│  │  └─ Average Quality
│  │
│  ├─ Recent Activity Stream
│  │  ├─ Analysis cards with status badges
│  │  ├─ [Active] [Processing] [Error] states
│  │  └─ [Open Analysis] / [View Progress] / [Retry] actions
│  │
│  └─ Favorites
│     ├─ Favorite Analysis Types (top 3)
│     └─ Favorite Source Types (top 2)
│
├─ 📊 Analyses List View
│  ├─ Search and filter
│  ├─ Card grid (like Dashboard recent activity)
│  └─ [Click] → Analysis Workspace
│
├─ 📋 Analysis Types Management
│  ├─ Card grid view (GDoc pattern)
│  ├─ Search and tag filter
│  ├─ Usage stats ("Used in 8 analyses")
│  ├─ [AI Create] and [➕ New] buttons
│  └─ [Click] → Type Detail/Edit
│
├─ 📄 Source Types Management
│  ├─ Card grid view (GDoc pattern)
│  ├─ Search and tag filter
│  ├─ [AI Create] and [➕ New] buttons
│  └─ [Click] → Type Detail/Edit
│
└─ 🔬 Analysis Workspace (Living Intelligence)
   ├─ Header
   │  ├─ [◄ Home] back button
   │  ├─ Analysis title
   │  ├─ [Change Type ▼] selector
   │  └─ [×] close
   │
   ├─ Quick Actions Bar
   │  ├─ [➕ Add Document]
   │  ├─ [📋 Clone to ▼]
   │  ├─ [📥 Export]
   │  └─ [🔆 @ Notes] toggle
   │
   ├─ Authoritative Notes Section (Collapsible, Gold)
   │  ├─ Natural language textarea
   │  ├─ Auto-save indicator
   │  ├─ Override count
   │  └─ Live field matching feedback
   │
   ├─ Two-Column Layout (40% | 60%)
   │  │
   │  ├─ LEFT: Documents (40%)
   │  │  ├─ Search documents
   │  │  ├─ Document cards with metadata
   │  │  │  ├─ Status (✓ Processed, ⟳ Processing, ⚠ Error)
   │  │  │  ├─ Insights count
   │  │  │  ├─ Avg confidence
   │  │  │  ├─ File size, pages
   │  │  │  ├─ Timestamp
   │  │  │  └─ [View] [Remove] actions
   │  │  └─ Drop zone (always active)
   │  │
   │  └─ RIGHT: Insights Panel (60%) ← SNAPVAULT PATTERN
   │     ├─ Dark panel (rgba(26, 29, 36))
   │     ├─ Scrollable content
   │     │
   │     ├─ OVERVIEW Section
   │     │  ├─ Created timestamp
   │     │  ├─ Updated timestamp
   │     │  ├─ Document count
   │     │  ├─ Insight count
   │     │  └─ Quality score
   │     │
   │     ├─ AI EXTRACTION SUMMARY
   │     │  └─ Natural language summary (AI-generated)
   │     │
   │     ├─ Grouped Insight Sections (e.g., COMPANY INFORMATION)
   │     │  └─ Fact rows:
   │     │     ├─ Label (12px, muted)
   │     │     ├─ Value (14px, prominent)
   │     │     ├─ Tags ([notes] [⭐ 100%] or [doc] [✓ 94%])
   │     │     ├─ Override notice ("⚠ Doc said $47.2M")
   │     │     └─ Document reference ("vendor.pdf, Page 1")
   │     │
   │     ├─ ACTIONS Section
   │     │  ├─ [♡ Favorite] [F]
   │     │  ├─ [📥 Export Report] [E]
   │     │  ├─ [🔄 Refresh Analysis] [R]
   │     │  ├─ [🗑 Delete] [Del]
   │     │  └─ Rating stars (☆ ☆ ☆ ☆ ☆)
   │     │
   │     └─ KEYBOARD SHORTCUTS (Collapsible)
   │
   └─ Quality Dashboard Footer (Collapsible)
      ├─ Summary: "Citation: 95% • Confidence: 88% high"
      └─ Detailed metrics grid
```

---

## Component Specifications

### 1. Dashboard View Component

**File:** `wwwroot/js/components/Dashboard.js`

**Responsibilities:**
- Render hero section with value prop
- Display system metrics (call `/api/pipelines` and aggregate)
- Show recent activity (last 10 analyses with status)
- Manage favorites (localStorage-based)
- Navigate to other views

**HTML Structure:**
```html
<div class="workspace workspace-dashboard active" data-workspace="dashboard">
  <!-- Hero Section -->
  <div class="dashboard-hero">
    <div class="hero-content">
      <h1>📊 Meridian Intelligence Platform</h1>
      <p>Evidence-driven document intelligence...</p>
      <div class="hero-actions">
        <button class="btn-primary">➕ New Analysis</button>
        <button class="btn-secondary">📋 Analysis Types</button>
        <button class="btn-secondary">📄 Source Types</button>
      </div>
    </div>
    <div class="hero-features">
      <!-- 6 feature cards: Evidence-First, Authoritative Notes, etc. -->
    </div>
  </div>

  <!-- Quick Actions Sidebar -->
  <aside class="quick-actions-sidebar">
    <!-- Always visible actions -->
  </aside>

  <!-- System Overview -->
  <section class="system-overview">
    <h2>📈 System Overview</h2>
    <div class="metrics-grid">
      <!-- 6 metric cards -->
      <div class="metric-card">
        <div class="metric-value">18</div>
        <div class="metric-label">Analyses Active</div>
      </div>
      <!-- ... -->
    </div>
  </section>

  <!-- Recent Activity -->
  <section class="recent-activity">
    <h2>⚡ Recent Activity</h2>
    <div class="activity-stream">
      <!-- Analysis cards with status badges -->
    </div>
  </section>

  <!-- Favorites -->
  <section class="favorites">
    <h2>⭐ Favorites</h2>
    <!-- Favorite types lists -->
  </section>
</div>
```

**CSS Classes:** (Add to `app.css`)
```css
.dashboard-hero {
  background: linear-gradient(135deg, #2563EB 0%, #1D4ED8 100%);
  color: white;
  padding: 60px 40px;
  border-radius: 16px;
  margin: 24px;
}

.hero-features {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 20px;
  margin-top: 40px;
}

.feature-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 12px;
  padding: 24px;
  text-align: center;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 20px;
  margin-top: 16px;
}

.metric-card {
  background: var(--color-bg-elevated);
  border: 1px solid var(--color-border-light);
  border-radius: 12px;
  padding: 24px;
  text-align: center;
}

.metric-value {
  font-size: 36px;
  font-weight: 700;
  color: var(--color-blue);
  margin-bottom: 8px;
}

.metric-label {
  font-size: 14px;
  color: var(--color-text-secondary);
}

.activity-stream {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.activity-card {
  background: var(--color-bg-elevated);
  border: 1px solid var(--color-border-light);
  border-radius: 12px;
  padding: 20px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.activity-card-info {
  flex: 1;
}

.activity-card-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--color-text-primary);
  margin-bottom: 8px;
}

.activity-card-meta {
  font-size: 12px;
  color: var(--color-text-secondary);
}

.status-badge {
  padding: 4px 12px;
  border-radius: 12px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
}

.status-badge[data-status="active"] {
  background: rgba(5, 150, 105, 0.15);
  color: var(--color-green);
}

.status-badge[data-status="processing"] {
  background: rgba(37, 99, 235, 0.15);
  color: var(--color-blue);
}

.status-badge[data-status="error"] {
  background: rgba(220, 38, 38, 0.15);
  color: var(--color-red);
}
```

**API Calls:**
- `GET /api/pipelines` - Get all analyses
- `GET /api/analysistypes` - Get analysis types count
- `GET /api/sourcetypes` - Get source types count (if endpoint exists)

**State Management:**
```javascript
// In app.js state
{
  currentView: 'dashboard',
  systemMetrics: {
    activeAnalyses: 0,
    processingJobs: 0,
    analysisTypes: 0,
    documentsProcessed: 0,
    sourceTypes: 0,
    avgQuality: 0
  },
  recentActivity: [],
  favorites: {
    analysisTypes: [],
    sourceTypes: []
  }
}
```

---

### 2. Analysis Types Management Component

**File:** `wwwroot/js/components/AnalysisTypesManager.js`

**Responsibilities:**
- Display card grid of analysis types (GDoc pattern)
- Search and filter by tags
- Navigate to type detail/edit
- Handle AI creation modal
- Handle CRUD operations
- Manage favorites

**HTML Structure:**
```html
<div class="workspace workspace-types" data-workspace="analysis-types">
  <!-- Header -->
  <div class="types-header">
    <div class="header-left">
      <button class="btn-back">◄ Home</button>
      <h1>📋 Analysis Types</h1>
    </div>
    <div class="header-right">
      <button class="btn-ai-create">🤖 AI Create</button>
      <button class="btn-new-type">➕ New</button>
    </div>
  </div>

  <!-- Filters -->
  <div class="types-filters">
    <input type="text" class="search-input" placeholder="Search analysis types...">
    <select class="tag-filter">
      <option value="">All Tags</option>
      <!-- Populated dynamically -->
    </select>
    <div class="types-count">24 of 24 types</div>
  </div>

  <!-- Card Grid -->
  <div class="types-grid">
    <!-- Type cards injected here -->
    <div class="type-card" data-type-id="...">
      <div class="type-card-header">
        <div class="type-badge">EAR</div>
        <button class="btn-favorite">⭐</button>
      </div>
      <div class="type-card-title">Enterprise Architecture Review</div>
      <div class="type-card-description">
        CIO steering committee readiness assessment
      </div>
      <div class="type-card-tags">
        <span class="tag">enterprise</span>
        <span class="tag">architecture</span>
        <span class="tag">review</span>
      </div>
      <div class="type-card-meta">
        <div>📅 Created: 9/26/25</div>
        <div>✏️ Updated: 9/26/25</div>
      </div>
      <div class="type-card-stats">
        <div>Used in 8 analyses</div>
        <div>12 fields • 4 source types</div>
      </div>
      <div class="type-card-actions">
        <button class="btn-secondary">👁 View</button>
        <button class="btn-secondary">✏️ Edit</button>
        <button class="btn-destructive">🗑 Delete</button>
      </div>
    </div>
  </div>
</div>
```

**CSS Classes:** (Add to new file `wwwroot/css/type-management.css`)
```css
.types-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 24px;
  border-bottom: 1px solid var(--color-border-light);
}

.types-filters {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  background: var(--color-bg-secondary);
  border-bottom: 1px solid var(--color-border-light);
}

.search-input {
  flex: 1;
  padding: 10px 16px;
  border: 1px solid var(--color-border-medium);
  border-radius: 8px;
  font-size: 14px;
}

.types-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 20px;
  padding: 24px;
}

.type-card {
  background: var(--color-bg-elevated);
  border: 1px solid var(--color-border-light);
  border-radius: 12px;
  padding: 20px;
  transition: all 0.2s;
}

.type-card:hover {
  border-color: var(--color-border-medium);
  box-shadow: var(--shadow-md);
  transform: translateY(-2px);
}

.type-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.type-badge {
  padding: 4px 12px;
  background: var(--color-gray-200);
  color: var(--color-gray-700);
  font-size: 11px;
  font-weight: 700;
  border-radius: 6px;
  text-transform: uppercase;
}

.btn-favorite {
  font-size: 18px;
  color: var(--color-gray-400);
  transition: color 0.2s;
}

.btn-favorite:hover,
.btn-favorite.active {
  color: var(--color-gold);
}

.type-card-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--color-text-primary);
  margin-bottom: 8px;
}

.type-card-description {
  font-size: 14px;
  color: var(--color-text-secondary);
  margin-bottom: 12px;
  line-height: 1.5;
}

.type-card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 12px;
}

.type-card-tags .tag {
  padding: 4px 10px;
  background: rgba(37, 99, 235, 0.1);
  border: 1px solid rgba(37, 99, 235, 0.2);
  color: var(--color-blue);
  font-size: 11px;
  border-radius: 12px;
}

.type-card-meta {
  font-size: 12px;
  color: var(--color-text-tertiary);
  margin-bottom: 12px;
}

.type-card-stats {
  font-size: 12px;
  color: var(--color-text-secondary);
  margin-bottom: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--color-border-light);
}

.type-card-actions {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 8px;
}
```

**API Calls:**
- `GET /api/analysistypes` - List all types
- `POST /api/analysistypes` - Create new type
- `PATCH /api/analysistypes/{id}` - Update type
- `DELETE /api/analysistypes/{id}` - Delete type
- `POST /api/analysistypes/ai-suggest` - AI-generate type

---

### 3. Compact Insights Panel Component (SnapVault Pattern)

**File:** `wwwroot/js/components/InsightsPanel.js`

**Responsibilities:**
- Render dark panel with scrollable sections
- Display overview metadata
- Show AI summary
- Render grouped fact rows
- Handle tag-based UI for source/confidence
- Display override notices and document references
- Manage actions and keyboard shortcuts

**HTML Structure:**
```html
<div class="insights-panel">
  <!-- Panel Header -->
  <div class="panel-header">
    <h2>Analysis Overview</h2>
    <button class="btn-close-panel">×</button>
  </div>

  <!-- Scrollable Content -->
  <div class="panel-content">

    <!-- Overview Section -->
    <section class="panel-section">
      <h3 class="section-title">OVERVIEW</h3>
      <div class="overview-grid">
        <div class="overview-item">
          <span class="overview-label">Created</span>
          <span class="overview-value">Oct 20, 2025 2:14 PM</span>
        </div>
        <div class="overview-item">
          <span class="overview-label">Updated</span>
          <span class="overview-value">Oct 21, 2025 12:46 PM</span>
        </div>
        <div class="overview-item">
          <span class="overview-label">Documents</span>
          <span class="overview-value">4</span>
        </div>
        <div class="overview-item">
          <span class="overview-label">Insights</span>
          <span class="overview-value">12</span>
        </div>
        <div class="overview-item">
          <span class="overview-label">Quality Score</span>
          <span class="overview-value">94%</span>
        </div>
      </div>
    </section>

    <!-- AI Summary Section -->
    <section class="panel-section">
      <h3 class="section-title">AI EXTRACTION SUMMARY</h3>
      <p class="ai-summary-text">
        Enterprise architecture readiness assessment for CloudCorp,
        a vendor with $52.3M revenue and 175 employees...
      </p>
    </section>

    <!-- Grouped Insights Sections -->
    <section class="panel-section">
      <h3 class="section-title">COMPANY INFORMATION</h3>

      <!-- Fact Row - Notes Sourced -->
      <div class="insight-row" data-source="notes">
        <div class="insight-label">Annual Revenue</div>
        <div class="insight-value-container">
          <span class="insight-value">$52.3M</span>
          <div class="insight-tags">
            <span class="tag" data-source="notes">notes</span>
            <span class="tag" data-confidence="high">⭐ 100%</span>
          </div>
        </div>
        <div class="insight-override-notice">
          ⚠ Doc said $47.2M (overridden)
        </div>
      </div>

      <!-- Fact Row - Document Sourced -->
      <div class="insight-row" data-source="doc">
        <div class="insight-label">Primary Contact</div>
        <div class="insight-value-container">
          <span class="insight-value">Jordan Kim</span>
          <div class="insight-tags">
            <span class="tag" data-source="doc">doc</span>
            <span class="tag" data-confidence="high">✓ 94%</span>
          </div>
        </div>
        <div class="insight-document-ref">
          vendor-prescreen.pdf, Page 1
        </div>
      </div>

      <!-- More fact rows... -->
    </section>

    <!-- More grouped sections... -->

    <!-- Actions Section -->
    <section class="panel-section">
      <h3 class="section-title">ACTIONS</h3>
      <div class="actions-grid">
        <button class="action-btn">
          <span>♡ Favorite</span>
          <kbd class="action-shortcut">F</kbd>
        </button>
        <button class="action-btn">
          <span>📥 Export Report</span>
          <kbd class="action-shortcut">E</kbd>
        </button>
        <button class="action-btn">
          <span>🔄 Refresh Analysis</span>
          <kbd class="action-shortcut">R</kbd>
        </button>
        <button class="action-btn destructive">
          <span>🗑 Delete</span>
          <kbd class="action-shortcut">Del</kbd>
        </button>
      </div>
      <div class="rating-section">
        <div class="rating-label">Rating</div>
        <div class="rating-stars">
          <button class="star-btn">☆</button>
          <button class="star-btn">☆</button>
          <button class="star-btn">☆</button>
          <button class="star-btn">☆</button>
          <button class="star-btn">☆</button>
        </div>
      </div>
    </section>

  </div>
</div>
```

**CSS Classes:** (Create new file `wwwroot/css/insights-panel.css`)
```css
/**
 * SnapVault-Inspired Insights Panel
 * Dark, focused, information-dense display
 */

.insights-panel {
  background: #1a1d24;
  color: rgba(255, 255, 255, 0.95);
  border-radius: 12px;
  overflow-y: auto;
  max-height: calc(100vh - 200px);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
}

/* Custom scrollbar */
.insights-panel::-webkit-scrollbar {
  width: 6px;
}

.insights-panel::-webkit-scrollbar-track {
  background: rgba(255, 255, 255, 0.05);
}

.insights-panel::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.2);
  border-radius: 3px;
}

.insights-panel::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.3);
}

/* Panel Header */
.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  position: sticky;
  top: 0;
  background: #1a1d24;
  z-index: 10;
}

.panel-header h2 {
  font-size: 18px;
  font-weight: 600;
  margin: 0;
}

.btn-close-panel {
  color: rgba(255, 255, 255, 0.6);
  font-size: 24px;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 150ms;
}

.btn-close-panel:hover {
  background: rgba(255, 255, 255, 0.1);
  color: rgba(255, 255, 255, 0.95);
}

/* Panel Sections */
.panel-section {
  padding: 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.panel-section:last-child {
  border-bottom: none;
}

.section-title {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: rgba(255, 255, 255, 0.5);
  margin-bottom: 12px;
}

/* Overview Grid */
.overview-grid {
  display: grid;
  gap: 10px;
}

.overview-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 0;
}

.overview-label {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.6);
}

.overview-value {
  font-size: 12px;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.95);
}

/* AI Summary */
.ai-summary-text {
  font-size: 14px;
  line-height: 1.6;
  color: rgba(255, 255, 255, 0.85);
}

/* Insight Row (Fact Row Pattern) */
.insight-row {
  margin-bottom: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}

.insight-row:last-child {
  border-bottom: none;
  margin-bottom: 0;
  padding-bottom: 0;
}

/* Gold highlight for notes-sourced */
.insight-row[data-source="notes"] {
  background: rgba(245, 158, 11, 0.08);
  padding: 12px;
  border-radius: 8px;
  border: 1px solid rgba(245, 158, 11, 0.2);
  margin-bottom: 16px;
}

.insight-label {
  font-size: 12px;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.6);
  margin-bottom: 6px;
  display: block;
}

.insight-row[data-source="notes"] .insight-label {
  color: rgba(251, 191, 36, 0.9);
}

.insight-value-container {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.insight-value {
  font-size: 14px;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.95);
}

/* Tags (Source and Confidence) */
.insight-tags {
  display: flex;
  gap: 6px;
  align-items: center;
}

.tag {
  padding: 4px 10px;
  background: rgba(59, 130, 246, 0.15);
  border: 1px solid rgba(59, 130, 246, 0.3);
  border-radius: 12px;
  font-size: 11px;
  font-weight: 500;
  color: rgba(96, 165, 250, 1);
  white-space: nowrap;
}

/* Notes source tag - Gold */
.tag[data-source="notes"] {
  background: rgba(245, 158, 11, 0.15);
  border-color: rgba(245, 158, 11, 0.4);
  color: rgba(251, 191, 36, 1);
}

/* Document source tag - Blue */
.tag[data-source="doc"] {
  background: rgba(59, 130, 246, 0.15);
  border-color: rgba(59, 130, 246, 0.3);
  color: rgba(96, 165, 250, 1);
}

/* High confidence tag - Green */
.tag[data-confidence="high"] {
  background: rgba(5, 150, 105, 0.15);
  border-color: rgba(5, 150, 105, 0.3);
  color: rgba(16, 185, 129, 1);
}

/* Override Notice */
.insight-override-notice {
  font-size: 11px;
  color: rgba(251, 191, 36, 0.8);
  margin-top: 4px;
  font-style: italic;
}

.insight-override-notice::before {
  content: "⚠ ";
}

/* Document Reference */
.insight-document-ref {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.4);
  margin-top: 4px;
}

/* Actions Grid */
.actions-grid {
  display: grid;
  gap: 8px;
  margin-bottom: 16px;
}

.action-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: rgba(255, 255, 255, 0.9);
  font-size: 14px;
  cursor: pointer;
  transition: all 150ms;
  text-align: left;
}

.action-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.2);
}

.action-btn.destructive {
  color: rgba(239, 68, 68, 0.9);
  border-color: rgba(239, 68, 68, 0.3);
}

.action-btn.destructive:hover {
  background: rgba(239, 68, 68, 0.1);
  border-color: rgba(239, 68, 68, 0.5);
}

.action-shortcut {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.4);
  padding: 2px 6px;
  background: rgba(255, 255, 255, 0.08);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 4px;
  font-family: 'SF Mono', monospace;
}

/* Rating Section */
.rating-section {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
}

.rating-label {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.6);
  margin-bottom: 8px;
}

.rating-stars {
  display: flex;
  gap: 4px;
}

.star-btn {
  color: rgba(255, 255, 255, 0.3);
  font-size: 20px;
  cursor: pointer;
  transition: color 150ms;
  padding: 4px;
}

.star-btn:hover,
.star-btn.active {
  color: rgba(251, 191, 36, 1);
}
```

**JavaScript Rendering Logic:**
```javascript
class InsightsPanel {
  constructor(app) {
    this.app = app;
  }

  render(deliverable) {
    const container = document.querySelector('.insights-panel');
    if (!container) return;

    // Parse deliverable data
    const dataJson = this.parseDeliverableData(deliverable);
    const fields = dataJson?.fields || {};

    // Group fields by category (AI-based or manual)
    const grouped = this.groupFields(fields);

    let html = `
      <div class="panel-header">
        <h2>Analysis Overview</h2>
      </div>
      <div class="panel-content">
        ${this.renderOverviewSection(deliverable)}
        ${this.renderSummarySection(dataJson)}
        ${this.renderGroupedSections(grouped, deliverable)}
        ${this.renderActionsSection()}
      </div>
    `;

    container.innerHTML = html;
    this.attachEventListeners();
  }

  groupFields(fields) {
    // Group by category (could be from schema or AI-inferred)
    // Example grouping logic:
    const groups = {
      'COMPANY INFORMATION': ['annualRevenue', 'employeeCount', 'primaryContact'],
      'CERTIFICATIONS': ['isoCertification', 'socCompliance', 'dataResidency'],
      'TIMELINE': ['pilotDeadline', 'infrastructurePrep'],
      // ... more groups
    };

    const result = {};
    Object.entries(groups).forEach(([groupName, fieldKeys]) => {
      result[groupName] = {};
      fieldKeys.forEach(key => {
        if (fields[key]) {
          result[groupName][key] = fields[key];
        }
      });
    });

    return result;
  }

  renderFactRow(key, value, metadata = {}) {
    const isNotesSourced = metadata.source === 'notes';
    const confidence = metadata.confidence || 94;
    const documentRef = metadata.documentRef || '';
    const overrideNotice = metadata.overrideNotice || '';

    return `
      <div class="insight-row" data-source="${metadata.source || 'doc'}">
        <div class="insight-label">${this.formatFieldName(key)}</div>
        <div class="insight-value-container">
          <span class="insight-value">${this.escapeHtml(value)}</span>
          <div class="insight-tags">
            <span class="tag" data-source="${isNotesSourced ? 'notes' : 'doc'}">
              ${isNotesSourced ? 'notes' : 'doc'}
            </span>
            <span class="tag" data-confidence="${confidence >= 90 ? 'high' : 'medium'}">
              ${isNotesSourced ? '⭐' : '✓'} ${confidence}%
            </span>
          </div>
        </div>
        ${overrideNotice ? `
          <div class="insight-override-notice">
            ⚠ ${overrideNotice}
          </div>
        ` : ''}
        ${documentRef && !isNotesSourced ? `
          <div class="insight-document-ref">
            ${documentRef}
          </div>
        ` : ''}
      </div>
    `;
  }

  // ... more helper methods
}
```

---

### 4. Two-Column Workspace Layout

**File:** Update `wwwroot/css/workspace.css`

**Add Two-Column Split:**
```css
/* Two-Column Workspace Layout */
.workspace-analysis-content {
  display: grid;
  grid-template-columns: 40% 60%; /* Documents 40% | Insights 60% */
  gap: 24px;
  padding: 24px;
  overflow: hidden;
  height: calc(100vh - 200px); /* Account for header/footer */
}

.documents-column {
  overflow-y: auto;
  padding-right: 12px;
}

.insights-column {
  overflow-y: auto;
  padding-left: 12px;
  border-left: 1px solid var(--color-border-light);
}

/* Responsive: Stack on small screens */
@media (max-width: 1200px) {
  .workspace-analysis-content {
    grid-template-columns: 1fr;
    grid-template-rows: auto 1fr;
  }

  .insights-column {
    border-left: none;
    border-top: 1px solid var(--color-border-light);
    padding-left: 0;
    padding-top: 24px;
  }
}
```

**Update HTML Structure:**
```html
<div class="workspace workspace-analysis" data-workspace="analysis">
  <!-- Quick Actions Bar -->
  <div class="quick-actions-bar">...</div>

  <!-- Authoritative Notes Section -->
  <div class="authoritative-notes-section">...</div>

  <!-- Two-Column Content -->
  <div class="workspace-analysis-content">

    <!-- LEFT: Documents Column (40%) -->
    <div class="documents-column">
      <h3 class="section-title">
        📄 DOCUMENTS (<span class="doc-count">0</span>)
      </h3>
      <input type="text" class="search-input" placeholder="Search documents...">
      <div class="documents-list">
        <!-- Document cards -->
      </div>
    </div>

    <!-- RIGHT: Insights Column (60%) -->
    <div class="insights-column">
      <div class="insights-header">
        <h3 class="section-title">💡 KEY INSIGHTS</h3>
        <input type="text" class="search-input" placeholder="Search insights...">
        <select class="group-selector">
          <option value="none">Group: None</option>
          <option value="category">By Category</option>
          <option value="source">By Source</option>
        </select>
      </div>
      <div class="insights-panel">
        <!-- SnapVault-style panel content -->
      </div>
    </div>

  </div>

  <!-- Quality Dashboard Footer -->
  <div class="quality-dashboard-section">...</div>
</div>
```

---

## Implementation Checklist

### Phase 1: Dashboard & Navigation ⬅️ **START HERE**

**Step 1.1: Create Dashboard Component** (2 hours)
- [ ] Create `wwwroot/js/components/Dashboard.js`
- [ ] Implement hero section HTML/CSS
- [ ] Add feature cards (6 features from UX spec)
- [ ] Implement Quick Actions sidebar
- [ ] Wire up navigation buttons

**Step 1.2: System Metrics** (1 hour)
- [ ] Create metric cards component
- [ ] Fetch data from API (`/api/pipelines`, `/api/analysistypes`)
- [ ] Aggregate metrics (active analyses, processing jobs, etc.)
- [ ] Update metric display
- [ ] Add CSS animations for loading states

**Step 1.3: Recent Activity Stream** (1 hour)
- [ ] Create activity card component
- [ ] Implement status badges (Active, Processing, Error)
- [ ] Add click handlers to open analyses
- [ ] Add "View Progress" for processing jobs
- [ ] Add "Retry" for failed jobs

**Step 1.4: Favorites System** (30 min)
- [ ] Implement localStorage-based favorites
- [ ] Add favorite toggle buttons (⭐)
- [ ] Display favorite types on dashboard
- [ ] Sync favorites across views

**Step 1.5: Navigation** (30 min)
- [ ] Update `app.js` with view routing
- [ ] Add dashboard as default view
- [ ] Implement `showDashboard()`, `showAnalyses()`, `showAnalysisTypes()`, `showSourceTypes()`
- [ ] Test navigation flow

**Testing:**
- [ ] Dashboard loads on app start
- [ ] Metrics display correctly
- [ ] Navigation works between all views
- [ ] Favorites persist across sessions

---

### Phase 2: Type Management Views

**Step 2.1: Analysis Types View** (2 hours)
- [ ] Create `wwwroot/js/components/AnalysisTypesManager.js`
- [ ] Create `wwwroot/css/type-management.css`
- [ ] Implement card grid layout
- [ ] Fetch types from API
- [ ] Render type cards with metadata
- [ ] Add search and filter functionality

**Step 2.2: Source Types View** (1 hour)
- [ ] Create `wwwroot/js/components/SourceTypesManager.js`
- [ ] Reuse type-management.css styles
- [ ] Implement card grid layout
- [ ] Fetch source types from API (check if endpoint exists)
- [ ] Render source type cards

**Step 2.3: AI Create Modal** (1 hour)
- [ ] Create AI creation modal component
- [ ] Form fields: Goal, Audience, Additional Context
- [ ] Call `/api/analysistypes/ai-suggest`
- [ ] Display generated type for review
- [ ] Save generated type

**Step 2.4: CRUD Operations** (1 hour)
- [ ] Implement create new type
- [ ] Implement edit type (modal or inline)
- [ ] Implement delete type (with confirmation)
- [ ] Update UI after operations
- [ ] Handle errors gracefully

**Testing:**
- [ ] Types display correctly
- [ ] Search and filter work
- [ ] AI creation generates valid types
- [ ] CRUD operations persist to API
- [ ] Favorites toggle works

---

### Phase 3: Compact Insights Panel

**Step 3.1: Panel Structure** (1 hour)
- [ ] Create `wwwroot/js/components/InsightsPanel.js`
- [ ] Create `wwwroot/css/insights-panel.css`
- [ ] Implement dark panel container
- [ ] Add scrollable content area
- [ ] Style panel header

**Step 3.2: Overview & Summary** (30 min)
- [ ] Render OVERVIEW section with metadata
- [ ] Render AI EXTRACTION SUMMARY
- [ ] Parse deliverable data

**Step 3.3: Fact Rows** (2 hours)
- [ ] Implement `renderFactRow()` method
- [ ] Create insight-row HTML structure
- [ ] Add tag system (source and confidence)
- [ ] Style notes-sourced rows (gold treatment)
- [ ] Add override notices
- [ ] Add document references

**Step 3.4: Grouped Sections** (1 hour)
- [ ] Implement field grouping logic
- [ ] Render section headers (COMPANY INFORMATION, etc.)
- [ ] Group related insights
- [ ] Add collapsible sections (optional enhancement)

**Step 3.5: Actions Footer** (30 min)
- [ ] Render actions grid
- [ ] Add keyboard shortcut badges
- [ ] Wire up action buttons
- [ ] Implement rating stars

**Testing:**
- [ ] Panel renders with correct data
- [ ] Tags display with correct colors
- [ ] Override notices show for notes-sourced fields
- [ ] Document references appear for doc-sourced fields
- [ ] Actions work correctly
- [ ] Dark theme looks professional

---

### Phase 4: Two-Column Workspace

**Step 4.1: Layout CSS** (30 min)
- [ ] Update `workspace.css` with grid layout
- [ ] Add 40/60 split (documents | insights)
- [ ] Style document column
- [ ] Style insights column
- [ ] Test responsive behavior

**Step 4.2: Documents Column** (1 hour)
- [ ] Enhance document cards with metadata
- [ ] Add status indicators (✓ Processed, ⟳ Processing, ⚠ Error)
- [ ] Display insights count
- [ ] Display avg confidence
- [ ] Add file size, pages, timestamp

**Step 4.3: Type Selector** (30 min)
- [ ] Add [Change Type ▼] dropdown to header
- [ ] Fetch available analysis types
- [ ] Implement type change handler
- [ ] Re-process analysis with new type

**Step 4.4: Integration** (1 hour)
- [ ] Update `renderAnalysisWorkspace()` in app.js
- [ ] Integrate InsightsPanel component
- [ ] Wire up document upload
- [ ] Test end-to-end flow

**Testing:**
- [ ] Two columns display side-by-side
- [ ] Columns scroll independently
- [ ] Document metadata displays correctly
- [ ] Type selector works
- [ ] Responsive layout stacks on small screens

---

### Phase 5: Polish & Testing

**Step 5.1: Keyboard Shortcuts** (1 hour)
- [ ] Document all shortcuts
- [ ] Implement keyboard handler
- [ ] Add shortcuts to actions
- [ ] Display shortcuts in panel

**Step 5.2: Accessibility** (1 hour)
- [ ] Add ARIA labels
- [ ] Test keyboard navigation
- [ ] Test screen reader compatibility
- [ ] Add focus indicators
- [ ] Test color contrast (WCAG AA)

**Step 5.3: Error States** (1 hour)
- [ ] Empty states for all views
- [ ] Loading states with skeletons
- [ ] Error states with retry
- [ ] Network error handling
- [ ] Validation errors

**Step 5.4: Final Testing** (2 hours)
- [ ] Test all user flows
- [ ] Test on different screen sizes
- [ ] Test with real data
- [ ] Performance optimization
- [ ] Cross-browser testing

---

## File Structure

```
samples/S7.Meridian/
├── wwwroot/
│   ├── index.html                      ✅ EXISTS (needs updates)
│   │
│   ├── css/
│   │   ├── design-tokens.css           ✅ EXISTS
│   │   ├── components.css              ✅ EXISTS
│   │   ├── app.css                     ✅ EXISTS (needs dashboard styles)
│   │   ├── workspace.css               ✅ EXISTS (needs two-column layout)
│   │   ├── evidence-panel.css          ✅ EXISTS
│   │   ├── notes.css                   ✅ EXISTS
│   │   ├── insights-panel.css          🔴 CREATE (SnapVault pattern)
│   │   └── type-management.css         🔴 CREATE (GDoc pattern)
│   │
│   └── js/
│       ├── app.js                      ✅ EXISTS (needs dashboard routing)
│       ├── api.js                      ✅ EXISTS
│       │
│       ├── components/
│       │   ├── Toast.js                ✅ EXISTS
│       │   ├── Dashboard.js            🔴 CREATE
│       │   ├── AnalysisTypesManager.js 🔴 CREATE
│       │   ├── SourceTypesManager.js   🔴 CREATE
│       │   └── InsightsPanel.js        🔴 CREATE
│       │
│       └── utils/
│           ├── EventBus.js             ✅ EXISTS
│           └── StateManager.js         ✅ EXISTS
│
└── docs/
    ├── UX-SPECIFICATION.md             ✅ CANONICAL UX REFERENCE
    ├── IMPLEMENTATION-PLAN.md          ✅ THIS DOCUMENT
    └── ... (other docs)
```

---

## API Endpoints Reference

### Pipelines (Analyses)
- `GET /api/pipelines` - List all pipelines
- `GET /api/pipelines/{id}` - Get pipeline by ID
- `POST /api/pipelines` - Create new pipeline
- `DELETE /api/pipelines/{id}` - Delete pipeline

### Analysis Types
- `GET /api/analysistypes` - List all analysis types
- `GET /api/analysistypes/{id}` - Get analysis type by ID
- `POST /api/analysistypes` - Create analysis type
- `PATCH /api/analysistypes/{id}` - Update analysis type
- `DELETE /api/analysistypes/{id}` - Delete analysis type
- `POST /api/analysistypes/ai-suggest` - AI-generate type from goal/audience

### Source Types
- `GET /api/sourcetypes` - List all source types (check if exists)
- (Assume similar CRUD endpoints as analysis types)

### Documents
- `POST /api/pipelines/{id}/documents` - Upload document (multipart/form-data)
- `POST /api/pipelines/{id}/documents/content` - Upload text content
- `GET /api/pipelines/{id}/documents` - List documents

### Authoritative Notes
- `GET /api/pipelines/{id}/notes` - Get notes
- `PUT /api/pipelines/{id}/notes` - Set notes (body: `{ authoritativeNotes, reProcess }`)

### Deliverables
- `GET /api/pipelines/{id}/deliverables/latest` - Get latest deliverable
- `GET /api/pipelines/{id}/deliverables/markdown` - Get markdown output
- `GET /api/pipelines/{id}/deliverables/json` - Get JSON output

### Jobs
- `GET /api/pipelines/{id}/jobs/{jobId}` - Get job status

---

## Design Pattern References

### SnapVault Photo Information Panel
**Borrowed Patterns:**
- Dark panel aesthetic (`rgba(26, 29, 36)`)
- Hierarchical sections (DETAILS → AI INSIGHTS → SUMMARY → Actions)
- Compact fact-row layout (label + value + tags)
- Tag-based UI for metadata
- Smart spacing (20px sections, 6px label-value gap, 16px row gap)
- Inline document references (no modals)
- Actions with keyboard shortcuts

**CSS Patterns to Reuse:**
```css
/* From lightbox-panel.css */
.panel-section {
  padding: 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.fact-row {
  display: grid;
  grid-template-columns: 110px 1fr;
  gap: 12px;
  padding: 8px 0;
  align-items: baseline;
}

.fact-label {
  font-size: 11px;
  font-weight: 600;
  text-transform: capitalize;
  color: rgba(255, 255, 255, 0.45);
}

.fact-value {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.85);
}

.ai-tag {
  padding: 3px 8px;
  background: rgba(59, 130, 246, 0.12);
  border: 1px solid rgba(59, 130, 246, 0.25);
  border-radius: 10px;
  font-size: 10.5px;
  color: rgba(96, 165, 250, 1);
}
```

### GDoc Assistant Dashboard
**Borrowed Patterns:**
- Hero section with value prop + feature highlights
- Workflow visualization (optional - could adapt for Meridian)
- System Overview metrics (6 cards in grid)
- Recent Activity stream with status badges
- Card-based type management
- Dual create buttons (Regular + AI)

**CSS Patterns to Reuse:**
```css
/* Hero section pattern */
.dashboard-hero {
  background: linear-gradient(135deg, #2563EB 0%, #1D4ED8 100%);
  color: white;
  padding: 60px 40px;
  border-radius: 16px;
}

/* Metric cards */
.metric-card {
  background: white;
  border: 1px solid #E5E7EB;
  border-radius: 12px;
  padding: 24px;
  text-align: center;
}

/* Status badges */
.status-badge[data-status="active"] {
  background: rgba(5, 150, 105, 0.15);
  color: #059669;
}

.status-badge[data-status="processing"] {
  background: rgba(37, 99, 235, 0.15);
  color: #2563EB;
}

.status-badge[data-status="error"] {
  background: rgba(220, 38, 38, 0.15);
  color: #DC2626;
}

/* Type cards */
.type-card {
  background: white;
  border: 1px solid #E5E7EB;
  border-radius: 12px;
  padding: 20px;
  transition: all 0.2s;
}

.type-card:hover {
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
  transform: translateY(-2px);
}
```

---

## Testing Criteria

### Dashboard View
- [ ] Hero section displays with correct value prop
- [ ] Feature cards render (6 features)
- [ ] System metrics calculate correctly
- [ ] Recent activity shows latest 10 analyses
- [ ] Status badges display correct colors
- [ ] Navigation buttons work
- [ ] Favorites load from localStorage

### Type Management
- [ ] Analysis types load and display
- [ ] Source types load and display
- [ ] Search filters results
- [ ] Tag filter works
- [ ] AI Create modal opens
- [ ] AI generation works
- [ ] CRUD operations succeed
- [ ] Favorites toggle persists

### Insights Panel
- [ ] Dark panel renders
- [ ] Overview section shows metadata
- [ ] AI summary displays
- [ ] Fact rows render correctly
- [ ] Notes-sourced rows have gold treatment
- [ ] Tags show correct colors
- [ ] Override notices appear
- [ ] Document references show
- [ ] Actions work
- [ ] Rating stars interactive

### Two-Column Workspace
- [ ] Documents column shows metadata
- [ ] Insights column displays panel
- [ ] Columns scroll independently
- [ ] Type selector changes type
- [ ] Drag-and-drop uploads files
- [ ] Processing indicators update
- [ ] Responsive layout stacks

### Accessibility
- [ ] Keyboard navigation works
- [ ] ARIA labels present
- [ ] Focus indicators visible
- [ ] Color contrast passes WCAG AA
- [ ] Screen reader compatible

### Performance
- [ ] Initial load < 2s
- [ ] Dashboard metrics load < 1s
- [ ] Type cards render < 500ms
- [ ] Panel scroll is smooth
- [ ] No layout shifts

---

## Next Session Instructions

**To continue this work in a future session, provide this prompt:**

```
Continue the work defined in docs/IMPLEMENTATION-PLAN.md.

Current Status: [Specify which phase/step you're on]

Focus on: [Specify which component or section to work on next]
```

**Example:**
```
Continue the work defined in docs/IMPLEMENTATION-PLAN.md.

Current Status: Phase 1 - Dashboard & Navigation (Step 1.1)

Focus on: Creating the Dashboard component with hero section and system metrics.
```

**What You'll Get:**
- I will read this document
- Understand the current state and target architecture
- Pick up exactly where you left off
- Follow the design patterns and code styles defined here
- Update the checklist as work progresses

---

## Appendix: Code Snippets

### A. Dashboard Hero Section HTML
```html
<div class="dashboard-hero">
  <div class="hero-content">
    <h1>📊 Meridian Intelligence Platform</h1>
    <p class="hero-subtitle">
      Evidence-driven document intelligence with AI-powered extraction,
      authoritative override, and continuous workspace evolution.
    </p>
    <div class="hero-actions">
      <button class="btn-primary btn-new-analysis">➕ New Analysis</button>
      <button class="btn-secondary btn-manage-analysis-types">📋 Analysis Types</button>
      <button class="btn-secondary btn-manage-source-types">📄 Source Types</button>
    </div>
  </div>

  <div class="hero-features">
    <div class="feature-card">
      <div class="feature-icon">🔍</div>
      <div class="feature-title">EVIDENCE-FIRST</div>
      <div class="feature-desc">Transparent AI with visible provenance</div>
    </div>
    <div class="feature-card">
      <div class="feature-icon">🔆</div>
      <div class="feature-title">AUTHORITATIVE NOTES</div>
      <div class="feature-desc">Natural language override system</div>
    </div>
    <div class="feature-card">
      <div class="feature-icon">📊</div>
      <div class="feature-title">MULTI-PERSPECTIVE</div>
      <div class="feature-desc">Clone to different analysis types</div>
    </div>
    <div class="feature-card">
      <div class="feature-icon">♻️</div>
      <div class="feature-title">LIVING ANALYSIS</div>
      <div class="feature-desc">Continuous evolution, never "done"</div>
    </div>
    <div class="feature-card">
      <div class="feature-icon">📈</div>
      <div class="feature-title">QUALITY DASHBOARD</div>
      <div class="feature-desc">Self-reporting analysis health</div>
    </div>
    <div class="feature-card">
      <div class="feature-icon">⚡</div>
      <div class="feature-title">CONTINUOUS PROCESSING</div>
      <div class="feature-desc">Add documents anytime</div>
    </div>
  </div>
</div>
```

### B. Fact Row Rendering Function
```javascript
renderFactRow(key, value, metadata = {}) {
  const isNotesSourced = metadata.source === 'notes';
  const confidence = metadata.confidence || 94;
  const documentRef = metadata.documentRef || '';
  const overrideNotice = metadata.overrideNotice || '';
  const docValue = metadata.documentValue || '';

  return `
    <div class="insight-row" data-source="${metadata.source || 'doc'}">
      <div class="insight-label">${this.formatFieldName(key)}</div>
      <div class="insight-value-container">
        <span class="insight-value">${this.escapeHtml(String(value))}</span>
        <div class="insight-tags">
          <span class="tag" data-source="${isNotesSourced ? 'notes' : 'doc'}">
            ${isNotesSourced ? 'notes' : 'doc'}
          </span>
          <span class="tag" data-confidence="${confidence >= 90 ? 'high' : confidence >= 70 ? 'medium' : 'low'}">
            ${isNotesSourced ? '⭐' : '✓'} ${confidence}%
          </span>
        </div>
      </div>
      ${overrideNotice ? `
        <div class="insight-override-notice">
          Doc said ${this.escapeHtml(docValue)} (overridden)
        </div>
      ` : ''}
      ${documentRef && !isNotesSourced ? `
        <div class="insight-document-ref">
          ${this.escapeHtml(documentRef)}
        </div>
      ` : ''}
    </div>
  `;
}
```

### C. Type Card Rendering
```javascript
renderTypeCard(type) {
  const name = type.name || type.Name;
  const id = type.id || type.Id;
  const description = type.description || type.Description || '';
  const tags = type.tags || type.Tags || [];
  const usageCount = type.usageCount || 0;
  const fieldCount = type.fieldCount || 0;
  const sourceTypeCount = type.sourceTypeCount || 0;
  const isFavorite = this.favorites.includes(id);

  // Generate badge acronym (e.g., "Enterprise Architecture Review" → "EAR")
  const badge = name.split(' ').map(word => word[0]).join('').toUpperCase().substring(0, 3);

  return `
    <div class="type-card" data-type-id="${id}">
      <div class="type-card-header">
        <div class="type-badge">${badge}</div>
        <button class="btn-favorite ${isFavorite ? 'active' : ''}" data-type-id="${id}">
          ${isFavorite ? '⭐' : '☆'}
        </button>
      </div>
      <div class="type-card-title">${this.escapeHtml(name)}</div>
      <div class="type-card-description">${this.escapeHtml(description)}</div>
      <div class="type-card-tags">
        ${tags.map(tag => `<span class="tag">${this.escapeHtml(tag)}</span>`).join('')}
      </div>
      <div class="type-card-meta">
        <div>📅 Created: ${this.formatDate(type.created)}</div>
        <div>✏️ Updated: ${this.formatDate(type.updated)}</div>
      </div>
      <div class="type-card-stats">
        <div>Used in ${usageCount} analyses ${isFavorite ? '⭐' : ''}</div>
        <div>${fieldCount} fields • ${sourceTypeCount} source types</div>
      </div>
      <div class="type-card-actions">
        <button class="btn-secondary btn-view-type">👁 View</button>
        <button class="btn-secondary btn-edit-type">✏️ Edit</button>
        <button class="btn-destructive btn-delete-type">🗑 Delete</button>
      </div>
    </div>
  `;
}
```

---

**END OF IMPLEMENTATION PLAN**

---

**Document Maintenance:**
- Update checklist items as completed
- Add new decisions to "Design Decisions" section
- Update file structure as new files are created
- Document any deviations from the plan with rationale
- Keep this document in sync with actual implementation

**Version History:**
- v1.0 (Oct 2025) - Initial implementation plan based on UX evaluation and design decisions
