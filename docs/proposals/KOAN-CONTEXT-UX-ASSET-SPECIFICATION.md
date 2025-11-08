# Koan.Context: UX Asset Specification & Implementation Plan

---
**Type:** UX ASSET SPECIFICATION
**Domain:** koan-context, design-system, frontend-architecture
**Status:** proposed
**Created:** 2025-11-07
**Framework Version:** v0.6.3+
**Authors:** UX Engineering Team

---

## Executive Summary

This specification transforms Koan.Context from a functional MVP (single HTML file) into a **Grafana-quality premium application** with enterprise-grade design, comprehensive component library, and production-ready architecture.

**Current State:** Functional MVP (~1,056 LOC single HTML file)
**Future State:** Premium multi-page application (~8,500 LOC across design system, components, pages)

**Quality Benchmark:** Grafana dashboards + Linear project management + Vercel deploy UI

**Investment:**
- **Design Phase:** $25k-30k (4-5 weeks, 1 senior UX engineer)
- **Implementation Phase:** $40k-50k (6-8 weeks, 2 frontend engineers)
- **Total:** $65k-80k

**Key Deliverables:**
1. Complete design system (tokens, components, patterns)
2. Component library (30+ components, Storybook documented)
3. 6 reimagined pages (Dashboard, Projects, Search, Jobs, Insights, Settings)
4. Modern build system (Vite + TypeScript + Tailwind)
5. Real-time data visualization (charts, metrics, live updates)

---

## Table of Contents

1. [Current State Audit](#1-current-state-audit)
2. [Future State Vision](#2-future-state-vision)
3. [Design System Specification](#3-design-system-specification)
4. [Component Library Inventory](#4-component-library-inventory)
5. [Page Architecture](#5-page-architecture)
6. [Backend API Enhancements](#6-backend-api-enhancements)
7. [Frontend Architecture](#7-frontend-architecture)
8. [Data Visualization Strategy](#8-data-visualization-strategy)
9. [Implementation Roadmap](#9-implementation-roadmap)
10. [Asset Deliverables](#10-asset-deliverables)

---

## 1. Current State Audit

### 1.1 Existing Assets Inventory

```
Current Frontend Structure:
└── wwwroot/
    └── index.html (1,056 LOC)
        ├── Inline CSS (styles, ~300 LOC)
        ├── HTML structure (~200 LOC)
        └── Vanilla JavaScript (~556 LOC)
            ├── Project management
            ├── Search interface
            ├── Settings management
            └── API integration

Current Backend API:
├── ProjectsController.cs
│   ├── GET /api/projects
│   ├── POST /api/projects/create
│   ├── POST /api/projects/{id}/index
│   ├── GET /api/projects/{id}/status
│   ├── GET /api/projects/{id}/health
│   └── POST /api/projects/bulk-index
├── SearchController.cs
│   ├── POST /api/search
│   └── POST /api/search/suggestions
├── JobsController.cs
│   └── GET /api/jobs/active
└── McpToolsController.cs
```

### 1.2 Current Capabilities Assessment

| Feature | Status | Quality Grade | Notes |
|---------|--------|---------------|-------|
| **Project CRUD** | ✅ Working | C+ | Basic forms, minimal validation |
| **Search** | ✅ Working | B- | Functional but text-only results |
| **Settings** | ✅ Working | C | Configuration-focused, not user-friendly |
| **Progress Tracking** | ⚠️ Partial | D+ | Polls every 10s, basic progress bar |
| **Dashboard** | ❌ Missing | F | No overview/metrics page |
| **Data Visualization** | ❌ Missing | F | No charts, graphs, or metrics |
| **Real-time Updates** | ⚠️ Partial | D | Manual refresh, no SSE/WebSocket |
| **Responsive Design** | ⚠️ Partial | C- | Basic media queries only |
| **Accessibility** | ❌ Missing | F | No ARIA, no keyboard nav beyond basics |
| **Design System** | ❌ Missing | F | Inline styles, no tokens |
| **Component Library** | ❌ Missing | F | No reusable components |
| **Error States** | ⚠️ Minimal | D+ | Alert boxes only |
| **Empty States** | ⚠️ Minimal | D | Plain text messages |
| **Loading States** | ⚠️ Minimal | D | "Loading..." text only |

**Overall Grade: D+** (Functional but far from premium)

---

### 1.3 UX Debt Analysis

#### Critical UX Debts

**1. No Dashboard (Grafana-equivalent missing)**
```
Current: Users land on Projects tab
Expected: Dashboard with system health, metrics, recent activity

Impact: HIGH
- No system overview
- No at-a-glance health status
- No usage metrics visibility
- Poor executive visibility
```

**2. Text-Only Search Results**
```
Current: Code displayed as plain text in <pre> tags
Expected: Syntax-highlighted code with file tree navigation

Impact: MEDIUM-HIGH
- Hard to scan results
- No language detection
- No file context
- Poor readability
```

**3. Polling-Based Progress Updates**
```
Current: JavaScript polls /api/projects every 10 seconds
Expected: Server-Sent Events (SSE) for real-time updates

Impact: MEDIUM
- Inefficient (unnecessary requests)
- 10-second lag in updates
- No sub-second precision
- Poor scalability
```

**4. No Data Visualization**
```
Current: Numbers displayed as text (e.g., "10,234 files")
Expected: Charts showing trends, distribution, performance

Impact: MEDIUM-HIGH
- Hard to spot trends
- No historical data visibility
- No performance insights
- Poor decision-making support
```

**5. Modal-Based Configuration**
```
Current: Create project via generic modal
Expected: Multi-step wizard with validation, path browser, preview

Impact: MEDIUM
- High error rate (invalid paths)
- No guidance for new users
- No validation until submit
- Poor first-time experience
```

---

## 2. Future State Vision

### 2.1 Target User Experience (Grafana-Quality)

**Inspiration: Best-in-Class Products**

| Aspect | Inspiration Source | Key Takeaway |
|--------|-------------------|--------------|
| **Dashboard Design** | Grafana | Grid-based layout, real-time metrics, customizable panels |
| **Data Visualization** | Grafana | Time-series charts, gauges, stat panels, threshold indicators |
| **Navigation** | Linear | Sidebar with command palette (Cmd+K), breadcrumbs, context switching |
| **Search UX** | Algolia DocSearch | Instant results, keyboard navigation, result preview |
| **Project Management** | Vercel Projects | Card-based layout, status indicators, quick actions |
| **Job Tracking** | GitHub Actions | Real-time logs, expandable stages, duration tracking |
| **Settings** | Stripe Dashboard | Sectioned forms, inline help, save indicators, dangerous action warnings |

### 2.2 Future State Screenshots (Wireframes)

#### Dashboard View (New - Grafana-Inspired)

```
┌────────────────────────────────────────────────────────────────────┐
│ ☰ Koan Context    [🔍 Search...]                        Profile ▼ │
├──────┬─────────────────────────────────────────────────────────────┤
│      │                                                              │
│ 🏠 D │  DASHBOARD                                                   │
│ 📁 P │                                                              │
│ 🔍 S │  ┌─────────────┬─────────────┬─────────────┬─────────────┐ │
│ ⚙️ J │  │ Projects    │ Chunks      │ Searches    │ Avg Latency │ │
│ 📊 I │  │ 5 total     │ 127K total  │ 234 today   │ 156ms       │ │
│ ⚙️ S │  │ +1 today    │ +3.4K today │ +12 /hour   │ ↓ 12% week  │ │
│      │  └─────────────┴─────────────┴─────────────┴─────────────┘ │
│      │                                                              │
│      │  System Health: ● All Systems Operational                   │
│      │  ┌──────────────────────────────────────────────────────┐  │
│      │  │ ✓ SQLite        ✓ Vector Store    ✓ Embedding API   │  │
│      │  │ ✓ Outbox Sync   ✓ Jobs Queue      ✓ File System     │  │
│      │  └──────────────────────────────────────────────────────┘  │
│      │                                                              │
│      │  Search Performance (Last 24h)                              │
│      │  ┌──────────────────────────────────────────────────────┐  │
│      │  │        ╱╲                                             │  │
│      │  │       ╱  ╲      ╱╲                                    │  │
│      │  │  ╱╲  ╱    ╲    ╱  ╲     ╱╲                           │  │
│      │  │ ╱  ╲╱      ╲__╱    ╲___╱  ╲___                       │  │
│      │  │────────────────────────────────────                  │  │
│      │  │ 00:00   06:00   12:00   18:00   24:00                │  │
│      │  │                                                       │  │
│      │  │ Queries/hour: ↑ 8%   P95 Latency: ↓ 12%             │  │
│      │  └──────────────────────────────────────────────────────┘  │
│      │                                                              │
│      │  Active Jobs (2)                                             │
│      │  ┌──────────────────────────────────────────────────────┐  │
│      │  │ my-enterprise-app         ⏳ 62%   ETA: 23 min       │  │
│      │  │ ████████████░░░░░░░░                                 │  │
│      │  │ 6,345 / 10,234 files     25,128 chunks               │  │
│      │  └──────────────────────────────────────────────────────┘  │
│      │  ┌──────────────────────────────────────────────────────┐  │
│      │  │ docs-project              ⏳ 18%   ETA: 8 min        │  │
│      │  │ ███░░░░░░░░░░░░░░                                    │  │
│      │  │ 234 / 1,289 files        1,045 chunks                │  │
│      │  └──────────────────────────────────────────────────────┘  │
│      │                                                              │
│      │  Recent Activity                                             │
│      │  ┌──────────────────────────────────────────────────────┐  │
│      │  │ 2m ago   Search "authentication" (127 results) 156ms │  │
│      │  │ 15m ago  Indexed koan-data (3,456 chunks) 4m 32s     │  │
│      │  │ 1h ago   Search "vector provider" (89 results) 203ms │  │
│      │  │ 2h ago   Created project "my-new-app"                 │  │
│      │  └──────────────────────────────────────────────────────┘  │
└──────┴─────────────────────────────────────────────────────────────┘
```

#### Projects View (Enhanced - Vercel-Inspired)

```
┌────────────────────────────────────────────────────────────────────┐
│ Projects (5)                    [+ New Project]  [⚡ Bulk Actions] │
├────────────────────────────────────────────────────────────────────┤
│                                                                     │
│ Filters: [All Projects ▼] [Status ▼] [Sort: Recent ▼]             │
│                                                                     │
│ ┌─────────────────────────────────────────────────────────────┐   │
│ │ koan-core                                    ● Ready         │   │
│ │ /projects/koan-framework/koan-core                            │   │
│ │                                                               │   │
│ │ ┌──────────┬──────────┬──────────┬──────────────────────┐   │   │
│ │ │ 12.3K    │ 123 MB   │ 2h ago   │ ✓ Healthy            │   │
│ │ │ chunks   │ indexed  │ updated  │ 0 errors             │   │
│ │ └──────────┴──────────┴──────────┴──────────────────────┘   │   │
│ │                                                               │   │
│ │ Performance: ↑ 5% faster | Commit: a3f2b1c                   │   │
│ │                                                               │   │
│ │ [🔄 Re-index] [🔍 Search] [📊 Analytics] [⚙️ Settings] [...]│   │
│ └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│ ┌─────────────────────────────────────────────────────────────┐   │
│ │ my-app                                       ⏳ Indexing 62% │   │
│ │ /projects/my-enterprise-app                                   │   │
│ │                                                               │   │
│ │ ████████████████░░░░░░░░░░░░░░░░░                           │   │
│ │                                                               │   │
│ │ ┌──────────────────────────────────────────────────────┐    │   │
│ │ │ Phase: Generating Embeddings (3/4)                   │    │   │
│ │ │ Current: src/Services/AuthenticationService.cs       │    │   │
│ │ │ Progress: 6,345 / 10,234 files (62%)                 │    │   │
│ │ │ Rate: 6.2 files/sec, 24.8 chunks/sec                 │    │   │
│ │ │ ETA: 23 minutes (3:45 PM)                            │    │   │
│ │ └──────────────────────────────────────────────────────┘    │   │
│ │                                                               │   │
│ │ [⏸️ Pause] [❌ Cancel] [📊 View Details]                     │   │
│ └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│ ┌─────────────────────────────────────────────────────────────┐   │
│ │ legacy-codebase                              ⚠️ Failed       │   │
│ │ /projects/old-system                                          │   │
│ │                                                               │   │
│ │ ⚠️ Error: Path traversal detected in file discovery          │   │
│ │ Last attempt: 1 day ago                                       │   │
│ │                                                               │   │
│ │ Suggested fixes:                                              │   │
│ │ • Verify project path is within allowed directories          │   │
│ │ • Check for symbolic links or junctions                      │   │
│ │ • Review .gitignore for path exclusions                      │   │
│ │                                                               │   │
│ │ [🔄 Retry] [🛠️ Diagnose] [📄 View Logs] [❌ Remove]         │   │
│ └─────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

#### Search View (Enhanced - Algolia-Inspired)

```
┌────────────────────────────────────────────────────────────────────┐
│ 🔍 [authentication middleware                          ] ⌘K       │
├────────────────────────────────────────────────────────────────────┤
│                                                                     │
│ ┌──────────┬──────────────────────────────────────────────────┐   │
│ │ FILTERS  │  RESULTS (127 in 156ms)                          │   │
│ ├──────────┤                                                   │   │
│ │          │  ┌────────────────────────────────────────────┐  │   │
│ │ Projects │  │ 🎯 JwtMiddleware.cs:78-95     Score: 96%  │  │   │
│ │ ☑ All (5)│  │ src/Auth/JwtMiddleware.cs                   │  │   │
│ │          │  │                                             │  │   │
│ │ ☑ koan-  │  │  78  public class JwtMiddleware             │  │   │
│ │   core   │  │  79  {                                       │  │   │
│ │ ☐ my-app │  │  80      public async Task InvokeAsync(...) │  │   │
│ │          │  │  81      {                                   │  │   │
│ │ Types    │  │  82          var token = Request.Headers[   │  │   │
│ │ ☑ Code   │  │               "Authorization"];             │  │   │
│ │ ☑ Docs   │  │  83          if (string.IsNullOrEmpty(token)│  │   │
│ │ ☐ Config │  │  84              return Unauthorized();      │  │   │
│ │          │  │  85                                           │  │   │
│ │ Relevance│  │  86          var principal = await _validator│  │   │
│ │ ▓▓▓▓▓▓▓░ │  │                  .ValidateTokenAsync(token); │  │   │
│ │ > 0.8    │  │  87                                           │  │   │
│ │          │  │ 📁 koan-core │ 💬 C# │ 📏 234 tokens         │  │   │
│ │ Hybrid   │  │ 🕐 Indexed 2h ago                            │  │   │
│ │ ▓▓▓▓▓▓▓░ │  │                                             │  │   │
│ │ 0.8      │  │ [📁 Open] [📋 Copy] [🔗 Editor] [⭐ Save]   │  │   │
│ │          │  └────────────────────────────────────────────┘  │   │
│ │ [Clear]  │                                                   │   │
│ │          │  ┌────────────────────────────────────────────┐  │   │
│ └──────────┤  │ AuthenticationService.cs:42-58  Score: 94%│  │   │
│            │  │ src/Services/AuthenticationService.cs       │  │   │
│            │  │                                             │  │   │
│            │  │  42  public async Task<User>                │  │   │
│            │  │         AuthenticateAsync(...)              │  │   │
│            │  │  43  {                                       │  │   │
│            │  │  44      var user = await _userRepo         │  │   │
│            │  │             .FindAsync(username);            │  │   │
│            │  │  45      if (user == null)                  │  │   │
│            │  │  46          return null;                    │  │   │
│            │  │                                             │  │   │
│            │  │ [Actions...]                                │  │   │
│            │  └────────────────────────────────────────────┘  │   │
│            │                                                   │   │
│            │  Page 1 of 13   [Load More]                      │   │
│            │                                                   │   │
└────────────┴───────────────────────────────────────────────────────┘
```

---

## 3. Design System Specification

### 3.1 Design Tokens (CSS Custom Properties)

**File:** `src/design-system/tokens.css`

```css
:root {
  /* ========================================
     COLOR PALETTE (Trust & Calm)
     ======================================== */

  /* Primary - Trust Blue */
  --color-primary-50:  #EFF6FF;
  --color-primary-100: #DBEAFE;
  --color-primary-200: #BFDBFE;
  --color-primary-300: #93C5FD;
  --color-primary-400: #60A5FA;
  --color-primary-500: #3B82F6;
  --color-primary-600: #2563EB; /* PRIMARY */
  --color-primary-700: #1D4ED8;
  --color-primary-800: #1E40AF;
  --color-primary-900: #1E3A8A;

  /* Success - Green */
  --color-success-50:  #ECFDF5;
  --color-success-100: #D1FAE5;
  --color-success-200: #A7F3D0;
  --color-success-300: #6EE7B7;
  --color-success-400: #34D399;
  --color-success-500: #10B981; /* SUCCESS */
  --color-success-600: #059669;
  --color-success-700: #047857;
  --color-success-800: #065F46;
  --color-success-900: #064E3B;

  /* Warning - Amber */
  --color-warning-50:  #FFFBEB;
  --color-warning-100: #FEF3C7;
  --color-warning-200: #FDE68A;
  --color-warning-300: #FCD34D;
  --color-warning-400: #FBBF24;
  --color-warning-500: #F59E0B; /* WARNING */
  --color-warning-600: #D97706;
  --color-warning-700: #B45309;
  --color-warning-800: #92400E;
  --color-warning-900: #78350F;

  /* Error - Red */
  --color-error-50:  #FEF2F2;
  --color-error-100: #FEE2E2;
  --color-error-200: #FECACA;
  --color-error-300: #FCA5A5;
  --color-error-400: #F87171;
  --color-error-500: #EF4444; /* ERROR */
  --color-error-600: #DC2626;
  --color-error-700: #B91C1C;
  --color-error-800: #991B1B;
  --color-error-900: #7F1D1D;

  /* Neutrals - Gray */
  --color-gray-50:  #FAFAFA;
  --color-gray-100: #F5F5F5;
  --color-gray-200: #E5E5E5;
  --color-gray-300: #D4D4D4;
  --color-gray-400: #A3A3A3;
  --color-gray-500: #737373;
  --color-gray-600: #525252;
  --color-gray-700: #404040;
  --color-gray-800: #262626;
  --color-gray-900: #171717;

  /* Semantic Colors */
  --color-background:     var(--color-gray-50);
  --color-surface:        #FFFFFF;
  --color-border:         var(--color-gray-200);
  --color-text-primary:   var(--color-gray-900);
  --color-text-secondary: var(--color-gray-600);
  --color-text-tertiary:  var(--color-gray-500);

  /* Code Highlight */
  --color-code-purple: #7C3AED;
  --color-code-cyan:   #06B6D4;

  /* ========================================
     TYPOGRAPHY
     ======================================== */

  /* Font Families */
  --font-family-sans:  'Inter', system-ui, -apple-system, sans-serif;
  --font-family-mono:  'JetBrains Mono', 'Consolas', 'Monaco', monospace;

  /* Font Sizes (rem) */
  --font-size-xs:   0.75rem;   /* 12px */
  --font-size-sm:   0.875rem;  /* 14px */
  --font-size-base: 1rem;      /* 16px */
  --font-size-lg:   1.125rem;  /* 18px */
  --font-size-xl:   1.25rem;   /* 20px */
  --font-size-2xl:  1.5rem;    /* 24px */
  --font-size-3xl:  1.875rem;  /* 30px */
  --font-size-4xl:  2.25rem;   /* 36px */

  /* Font Weights */
  --font-weight-normal:   400;
  --font-weight-medium:   500;
  --font-weight-semibold: 600;
  --font-weight-bold:     700;

  /* Line Heights */
  --line-height-tight:  1.2;
  --line-height-normal: 1.5;
  --line-height-relaxed: 1.6;

  /* ========================================
     SPACING (8px grid)
     ======================================== */

  --spacing-0:  0;
  --spacing-1:  0.25rem;  /* 4px */
  --spacing-2:  0.5rem;   /* 8px */
  --spacing-3:  0.75rem;  /* 12px */
  --spacing-4:  1rem;     /* 16px */
  --spacing-5:  1.25rem;  /* 20px */
  --spacing-6:  1.5rem;   /* 24px */
  --spacing-8:  2rem;     /* 32px */
  --spacing-10: 2.5rem;   /* 40px */
  --spacing-12: 3rem;     /* 48px */
  --spacing-16: 4rem;     /* 64px */
  --spacing-20: 5rem;     /* 80px */

  /* ========================================
     BORDERS
     ======================================== */

  --border-width-1: 1px;
  --border-width-2: 2px;
  --border-width-4: 4px;

  --border-radius-sm: 0.375rem; /* 6px */
  --border-radius-md: 0.5rem;   /* 8px */
  --border-radius-lg: 0.75rem;  /* 12px */
  --border-radius-xl: 1rem;     /* 16px */
  --border-radius-full: 9999px;

  /* ========================================
     SHADOWS
     ======================================== */

  --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.05);
  --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
  --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
  --shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);

  /* ========================================
     TRANSITIONS
     ======================================== */

  --transition-fast:   150ms ease;
  --transition-base:   200ms ease;
  --transition-slow:   300ms ease;

  --ease-in:      cubic-bezier(0.4, 0, 1, 1);
  --ease-out:     cubic-bezier(0, 0, 0.2, 1);
  --ease-in-out:  cubic-bezier(0.4, 0, 0.2, 1);

  /* ========================================
     Z-INDEX SCALE
     ======================================== */

  --z-base:        0;
  --z-dropdown:    1000;
  --z-sticky:      1020;
  --z-fixed:       1030;
  --z-modal-backdrop: 1040;
  --z-modal:       1050;
  --z-popover:     1060;
  --z-tooltip:     1070;
  --z-notification: 1080;
}

/* Dark Mode (future) */
@media (prefers-color-scheme: dark) {
  :root {
    --color-background:     var(--color-gray-900);
    --color-surface:        var(--color-gray-800);
    --color-border:         var(--color-gray-700);
    --color-text-primary:   var(--color-gray-50);
    --color-text-secondary: var(--color-gray-400);
    --color-text-tertiary:  var(--color-gray-500);
  }
}
```

---

### 3.2 Typography System

**File:** `src/design-system/typography.css`

```css
/* ========================================
   TYPOGRAPHY SCALE
   ======================================== */

.text-xs {
  font-size: var(--font-size-xs);
  line-height: var(--line-height-normal);
}

.text-sm {
  font-size: var(--font-size-sm);
  line-height: var(--line-height-normal);
}

.text-base {
  font-size: var(--font-size-base);
  line-height: var(--line-height-normal);
}

.text-lg {
  font-size: var(--font-size-lg);
  line-height: var(--line-height-normal);
}

.text-xl {
  font-size: var(--font-size-xl);
  line-height: var(--line-height-tight);
}

.text-2xl {
  font-size: var(--font-size-2xl);
  line-height: var(--line-height-tight);
}

.text-3xl {
  font-size: var(--font-size-3xl);
  line-height: var(--line-height-tight);
}

.text-4xl {
  font-size: var(--font-size-4xl);
  line-height: var(--line-height-tight);
}

/* Font Weights */
.font-normal   { font-weight: var(--font-weight-normal); }
.font-medium   { font-weight: var(--font-weight-medium); }
.font-semibold { font-weight: var(--font-weight-semibold); }
.font-bold     { font-weight: var(--font-weight-bold); }

/* Font Families */
.font-sans { font-family: var(--font-family-sans); }
.font-mono { font-family: var(--font-family-mono); }

/* Text Colors */
.text-primary   { color: var(--color-text-primary); }
.text-secondary { color: var(--color-text-secondary); }
.text-tertiary  { color: var(--color-text-tertiary); }

/* Heading Styles */
h1, .heading-1 {
  font-size: var(--font-size-4xl);
  font-weight: var(--font-weight-bold);
  line-height: var(--line-height-tight);
  color: var(--color-text-primary);
  margin-bottom: var(--spacing-6);
}

h2, .heading-2 {
  font-size: var(--font-size-3xl);
  font-weight: var(--font-weight-semibold);
  line-height: var(--line-height-tight);
  color: var(--color-text-primary);
  margin-bottom: var(--spacing-4);
}

h3, .heading-3 {
  font-size: var(--font-size-2xl);
  font-weight: var(--font-weight-semibold);
  line-height: var(--line-height-tight);
  color: var(--color-text-primary);
  margin-bottom: var(--spacing-3);
}

h4, .heading-4 {
  font-size: var(--font-size-xl);
  font-weight: var(--font-weight-semibold);
  line-height: var(--line-height-normal);
  color: var(--color-text-primary);
  margin-bottom: var(--spacing-2);
}
```

---

## 4. Component Library Inventory

### 4.1 Core Components (30+ Components)

#### Foundation Components

**1. Button** (`Button.tsx`)
```typescript
interface ButtonProps {
  variant: 'primary' | 'secondary' | 'danger' | 'ghost';
  size: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  isDisabled?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  onClick?: () => void;
  children: React.ReactNode;
}

// Usage:
<Button variant="primary" size="md" leftIcon={<PlusIcon />}>
  Create Project
</Button>
```

**2. Input** (`Input.tsx`)
```typescript
interface InputProps {
  type: 'text' | 'password' | 'email' | 'number' | 'search';
  label?: string;
  placeholder?: string;
  helperText?: string;
  error?: string;
  isDisabled?: boolean;
  leftAddon?: React.ReactNode;
  rightAddon?: React.ReactNode;
  onChange?: (value: string) => void;
}
```

**3. Badge** (`Badge.tsx`)
```typescript
interface BadgeProps {
  variant: 'success' | 'warning' | 'error' | 'info' | 'neutral';
  size: 'sm' | 'md' | 'lg';
  children: React.ReactNode;
}

// Usage:
<Badge variant="success" size="md">Ready</Badge>
<Badge variant="warning" size="md">Indexing</Badge>
```

**4. Card** (`Card.tsx`)
```typescript
interface CardProps {
  variant: 'elevated' | 'outlined' | 'ghost';
  padding: 'sm' | 'md' | 'lg';
  children: React.ReactNode;
  header?: React.ReactNode;
  footer?: React.ReactNode;
  onHover?: () => void;
}
```

**5. Modal** (`Modal.tsx`)
```typescript
interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  size: 'sm' | 'md' | 'lg' | 'xl' | 'full';
  closeOnOverlayClick?: boolean;
  children: React.ReactNode;
  footer?: React.ReactNode;
}
```

---

#### Data Display Components

**6. Table** (`Table.tsx`)
```typescript
interface TableProps {
  columns: ColumnDef[];
  data: any[];
  isLoading?: boolean;
  onRowClick?: (row: any) => void;
  sortable?: boolean;
  paginated?: boolean;
  pageSize?: number;
}
```

**7. ProgressBar** (`ProgressBar.tsx`)
```typescript
interface ProgressBarProps {
  value: number; // 0-100
  max?: number;
  variant: 'default' | 'success' | 'warning' | 'error';
  size: 'sm' | 'md' | 'lg';
  showLabel?: boolean;
  isIndeterminate?: boolean;
}

// Usage:
<ProgressBar value={62} variant="default" size="md" showLabel />
```

**8. StatCard** (`StatCard.tsx` - Grafana-inspired)
```typescript
interface StatCardProps {
  label: string;
  value: string | number;
  change?: {
    value: number;
    trend: 'up' | 'down' | 'neutral';
    period: string;
  };
  icon?: React.ReactNode;
  onClick?: () => void;
}

// Usage:
<StatCard
  label="Total Projects"
  value={5}
  change={{ value: +1, trend: 'up', period: 'today' }}
  icon={<FolderIcon />}
/>
```

**9. CodeBlock** (`CodeBlock.tsx`)
```typescript
interface CodeBlockProps {
  code: string;
  language: string;
  lineNumbers?: boolean;
  highlightLines?: number[];
  startLine?: number;
  endLine?: number;
  maxHeight?: string;
  copyable?: boolean;
}

// Usage:
<CodeBlock
  code={chunk.content}
  language="csharp"
  lineNumbers
  startLine={42}
  endLine={58}
  highlightLines={[45, 46]}
  copyable
/>
```

**10. Timeline** (`Timeline.tsx`)
```typescript
interface TimelineProps {
  items: TimelineItem[];
  variant: 'vertical' | 'horizontal';
}

interface TimelineItem {
  timestamp: Date;
  title: string;
  description?: string;
  icon?: React.ReactNode;
  status?: 'pending' | 'completed' | 'failed';
}
```

---

#### Feedback Components

**11. Toast** (`Toast.tsx`)
```typescript
interface ToastProps {
  variant: 'success' | 'error' | 'warning' | 'info';
  title: string;
  description?: string;
  duration?: number; // auto-dismiss ms
  onClose?: () => void;
  action?: {
    label: string;
    onClick: () => void;
  };
}

// Usage (via hook):
const toast = useToast();
toast.success('Project indexed successfully', {
  description: '10,234 files processed in 38 minutes',
  duration: 5000
});
```

**12. Alert** (`Alert.tsx`)
```typescript
interface AlertProps {
  variant: 'info' | 'success' | 'warning' | 'error';
  title: string;
  description?: string;
  onClose?: () => void;
  action?: {
    label: string;
    onClick: () => void;
  };
}
```

**13. Spinner** (`Spinner.tsx`)
```typescript
interface SpinnerProps {
  size: 'sm' | 'md' | 'lg' | 'xl';
  variant: 'default' | 'primary';
  label?: string;
}
```

**14. Skeleton** (`Skeleton.tsx`)
```typescript
interface SkeletonProps {
  variant: 'text' | 'rectangular' | 'circular';
  width?: string;
  height?: string;
  count?: number;
}

// Usage:
<Skeleton variant="text" width="100%" count={3} />
<Skeleton variant="rectangular" width="200px" height="100px" />
```

---

#### Navigation Components

**15. Sidebar** (`Sidebar.tsx`)
```typescript
interface SidebarProps {
  items: SidebarItem[];
  collapsed?: boolean;
  onItemClick?: (item: SidebarItem) => void;
}

interface SidebarItem {
  id: string;
  label: string;
  icon: React.ReactNode;
  href?: string;
  badge?: number | string;
  children?: SidebarItem[];
}
```

**16. Breadcrumbs** (`Breadcrumbs.tsx`)
```typescript
interface BreadcrumbsProps {
  items: BreadcrumbItem[];
  separator?: React.ReactNode;
}

interface BreadcrumbItem {
  label: string;
  href?: string;
  onClick?: () => void;
}
```

**17. Tabs** (`Tabs.tsx`)
```typescript
interface TabsProps {
  tabs: TabItem[];
  activeTab: string;
  onChange: (tabId: string) => void;
  variant: 'line' | 'enclosed' | 'pills';
}

interface TabItem {
  id: string;
  label: string;
  icon?: React.ReactNode;
  content: React.ReactNode;
  disabled?: boolean;
}
```

**18. CommandPalette** (`CommandPalette.tsx` - Linear-inspired)
```typescript
interface CommandPaletteProps {
  isOpen: boolean;
  onClose: () => void;
  commands: Command[];
  onExecute: (command: Command) => void;
}

interface Command {
  id: string;
  label: string;
  icon?: React.ReactNode;
  shortcut?: string;
  group?: string;
  action: () => void;
}

// Usage: Open with Cmd+K / Ctrl+K
```

---

#### Chart Components (Grafana-Quality)

**19. LineChart** (`LineChart.tsx`)
```typescript
interface LineChartProps {
  data: ChartData[];
  xAxisKey: string;
  yAxisKey: string;
  color?: string;
  showGrid?: boolean;
  showLegend?: boolean;
  height?: number;
  isLoading?: boolean;
}

interface ChartData {
  [key: string]: any;
}
```

**20. BarChart** (`BarChart.tsx`)
```typescript
interface BarChartProps {
  data: ChartData[];
  xAxisKey: string;
  yAxisKey: string;
  color?: string;
  horizontal?: boolean;
  stacked?: boolean;
}
```

**21. PieChart** (`PieChart.tsx`)
```typescript
interface PieChartProps {
  data: {
    label: string;
    value: number;
    color?: string;
  }[];
  donut?: boolean;
  showLabels?: boolean;
  showLegend?: boolean;
}
```

**22. Gauge** (`Gauge.tsx` - Grafana-style)
```typescript
interface GaugeProps {
  value: number;
  min: number;
  max: number;
  thresholds?: {
    value: number;
    color: string;
  }[];
  label?: string;
  unit?: string;
  size: 'sm' | 'md' | 'lg';
}

// Usage:
<Gauge
  value={156}
  min={0}
  max={500}
  thresholds={[
    { value: 200, color: 'green' },
    { value: 400, color: 'yellow' },
    { value: 500, color: 'red' }
  ]}
  label="Search Latency"
  unit="ms"
  size="md"
/>
```

**23. Heatmap** (`Heatmap.tsx`)
```typescript
interface HeatmapProps {
  data: {
    x: string;
    y: string;
    value: number;
  }[];
  colorScale: string[];
  showValues?: boolean;
}
```

---

#### Form Components

**24. Select** (`Select.tsx`)
```typescript
interface SelectProps {
  options: {
    value: string;
    label: string;
    disabled?: boolean;
  }[];
  value?: string;
  onChange: (value: string) => void;
  label?: string;
  placeholder?: string;
  error?: string;
  isMulti?: boolean;
  isSearchable?: boolean;
}
```

**25. Checkbox** (`Checkbox.tsx`)
```typescript
interface CheckboxProps {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  isDisabled?: boolean;
  isIndeterminate?: boolean;
}
```

**26. Radio** (`Radio.tsx`)
```typescript
interface RadioGroupProps {
  options: {
    value: string;
    label: string;
    description?: string;
  }[];
  value: string;
  onChange: (value: string) => void;
  label?: string;
}
```

**27. Switch** (`Switch.tsx`)
```typescript
interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label?: string;
  size: 'sm' | 'md' | 'lg';
  isDisabled?: boolean;
}
```

**28. Slider** (`Slider.tsx`)
```typescript
interface SliderProps {
  value: number;
  onChange: (value: number) => void;
  min: number;
  max: number;
  step: number;
  label?: string;
  marks?: { value: number; label: string }[];
}
```

**29. FileUpload** (`FileUpload.tsx`)
```typescript
interface FileUploadProps {
  accept?: string;
  maxSize?: number;
  multiple?: boolean;
  onUpload: (files: File[]) => void;
  label?: string;
  helperText?: string;
}
```

**30. DatePicker** (`DatePicker.tsx`)
```typescript
interface DatePickerProps {
  value?: Date;
  onChange: (date: Date) => void;
  label?: string;
  minDate?: Date;
  maxDate?: Date;
  format?: string;
}
```

---

#### Domain-Specific Components

**31. ProjectCard** (`ProjectCard.tsx`)
```typescript
interface ProjectCardProps {
  project: Project;
  onIndex?: () => void;
  onSearch?: () => void;
  onDelete?: () => void;
  showProgress?: boolean;
}

// Renders project card with status badge, metrics, actions
```

**32. SearchResultCard** (`SearchResultCard.tsx`)
```typescript
interface SearchResultCardProps {
  chunk: Chunk;
  source: SourceFile;
  score: number;
  onOpen?: () => void;
  onCopy?: () => void;
  highlightQuery?: string;
}

// Renders code chunk with syntax highlighting, metadata
```

**33. JobProgressPanel** (`JobProgressPanel.tsx`)
```typescript
interface JobProgressPanelProps {
  job: Job;
  detailed?: boolean;
  onPause?: () => void;
  onCancel?: () => void;
  refreshInterval?: number;
}

// Renders job progress with stages, ETA, statistics
```

**34. HealthStatusIndicator** (`HealthStatusIndicator.tsx`)
```typescript
interface HealthStatusIndicatorProps {
  services: {
    name: string;
    healthy: boolean;
    message?: string;
  }[];
  onRefresh?: () => void;
}

// Renders system health with per-service status
```

---

### 4.2 Component Composition Patterns

#### Pattern 1: Dashboard Panels

```typescript
<DashboardGrid>
  <GridItem span={3}>
    <StatCard
      label="Total Projects"
      value={5}
      change={{ value: +1, trend: 'up', period: 'today' }}
      icon={<FolderIcon />}
    />
  </GridItem>

  <GridItem span={3}>
    <StatCard
      label="Total Chunks"
      value="127K"
      change={{ value: +3.4, trend: 'up', period: 'today' }}
      icon={<DocumentIcon />}
    />
  </GridItem>

  <GridItem span={6}>
    <Card header="Search Performance">
      <LineChart
        data={performanceData}
        xAxisKey="hour"
        yAxisKey="latency"
        height={200}
      />
    </Card>
  </GridItem>

  <GridItem span={6}>
    <Card header="Active Jobs">
      <JobProgressPanel job={activeJob} />
    </Card>
  </GridItem>
</DashboardGrid>
```

#### Pattern 2: Project Management

```typescript
<ProjectList>
  {projects.map(project => (
    <ProjectCard
      key={project.id}
      project={project}
      onIndex={() => handleIndex(project.id)}
      onSearch={() => navigate(`/search?projectId=${project.id}`)}
      onDelete={() => handleDelete(project.id)}
      showProgress={project.status === 'Indexing'}
    />
  ))}
</ProjectList>
```

#### Pattern 3: Search Interface

```typescript
<SearchLayout>
  <SearchHeader>
    <SearchInput
      value={query}
      onChange={setQuery}
      placeholder="Search your code..."
      shortcuts={['/', 'Cmd+K']}
    />
  </SearchHeader>

  <SearchBody>
    <SearchFilters>
      <FilterSection title="Projects">
        <Checkbox label="koan-core" checked />
        <Checkbox label="my-app" />
      </FilterSection>

      <FilterSection title="Relevance">
        <Slider
          value={minScore}
          onChange={setMinScore}
          min={0}
          max={1}
          step={0.1}
        />
      </FilterSection>
    </SearchFilters>

    <SearchResults>
      {results.map(result => (
        <SearchResultCard
          key={result.id}
          chunk={result.chunk}
          source={result.source}
          score={result.score}
          highlightQuery={query}
        />
      ))}
    </SearchResults>
  </SearchBody>
</SearchLayout>
```

---

## 5. Page Architecture

### 5.1 Page Inventory & Specifications

#### Page 1: Dashboard (NEW - Priority 1)

**Route:** `/` or `/dashboard`

**Purpose:** Grafana-quality overview of system health, metrics, and activity

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header (Global)                                      │
├──────┬──────────────────────────────────────────────┤
│      │ Dashboard                                     │
│ Sid  │                                               │
│ ebar │ [Stat Cards: Projects, Chunks, Searches, Lat]│
│      │                                               │
│      │ [System Health Panel]                         │
│      │                                               │
│      │ [Search Performance Chart - 24h]              │
│      │                                               │
│      │ [Active Jobs Panel]                           │
│      │                                               │
│      │ [Recent Activity Timeline]                    │
│      │                                               │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `StatCard` (4x - metrics)
- `HealthStatusIndicator` (1x - system health)
- `LineChart` (1x - performance trends)
- `JobProgressPanel` (Nx - active jobs)
- `Timeline` (1x - recent activity)

**Data Sources:**
- `GET /api/metrics/summary` (new endpoint needed)
- `GET /api/health` (new endpoint needed)
- `GET /api/metrics/performance?period=24h` (new endpoint needed)
- `GET /api/jobs/active`
- `GET /api/activity/recent` (new endpoint needed)

**Refresh Strategy:**
- Metrics: Poll every 30 seconds
- Active jobs: SSE connection for real-time updates
- Activity: Poll every 60 seconds

---

#### Page 2: Projects (ENHANCED - Priority 2)

**Route:** `/projects`

**Purpose:** Manage all indexed projects (Vercel-style cards)

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header: Projects (5)  [+ New] [Bulk Actions ▼]     │
├──────┬──────────────────────────────────────────────┤
│      │ [Filters: All, Status, Sort]                 │
│ Sid  │                                               │
│ ebar │ [Project Card 1 - koan-core - Ready]         │
│      │ [Project Card 2 - my-app - Indexing 62%]     │
│      │ [Project Card 3 - legacy - Failed]           │
│      │ ...                                           │
│      │                                               │
│      │ [Pagination]                                  │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `ProjectCard` (Nx)
- `FilterBar` (1x)
- `Button` (actions)
- `Modal` (create/edit project)
- `Pagination` (1x)

**Modals:**
- Create Project Wizard (multi-step)
- Edit Project Settings
- Bulk Actions Menu

**Data Sources:**
- `GET /api/projects?page=1&filter=...`
- `GET /api/jobs/active` (for progress enrichment)

---

#### Page 3: Search (ENHANCED - Priority 1)

**Route:** `/search`

**Purpose:** Semantic code search with filters (Algolia-quality)

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header: [Global Search Input - Cmd+K]              │
├──────┬──────────────────────────────────────────────┤
│ Fil  │ Results (127 in 156ms)                       │
│ ter  │                                               │
│ s    │ [SearchResultCard 1 - Score 96%]             │
│      │ [SearchResultCard 2 - Score 94%]             │
│ Pro  │ [SearchResultCard 3 - Score 91%]             │
│ ject │ ...                                           │
│ s    │                                               │
│ Type │ [Load More]                                   │
│ s    │                                               │
│ Rel  │                                               │
│ eva  │                                               │
│ nce  │                                               │
│      │                                               │
│ Hyb  │                                               │
│ rid  │                                               │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `SearchInput` (global, always accessible)
- `FilterPanel` (collapsible)
- `SearchResultCard` (Nx - syntax highlighted)
- `Pagination` or infinite scroll
- `CodeBlock` (syntax highlighting)

**Features:**
- Instant search (debounced 300ms)
- Keyboard navigation (j/k, Enter)
- Result preview on hover
- Copy/Open/Save actions
- Syntax highlighting via Prism.js

**Data Sources:**
- `POST /api/search` (with filters)
- `POST /api/search/suggestions` (typeahead)

---

#### Page 4: Jobs (ENHANCED - Priority 3)

**Route:** `/jobs`

**Purpose:** View all indexing jobs (GitHub Actions-style)

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header: Jobs  [Active] [Completed] [Failed]        │
├──────┬──────────────────────────────────────────────┤
│      │ Active Jobs (2)                              │
│ Sid  │                                               │
│ ebar │ [Job Card - my-app - 62% - ETA 23min]        │
│      │   └─ [Expandable: Stage details, logs]       │
│      │                                               │
│      │ [Job Card - docs - 18% - ETA 8min]           │
│      │   └─ [Expandable: Stage details, logs]       │
│      │                                               │
│      │ Recent Completed (10)                        │
│      │ [Job Card - koan-core - Completed - 4m32s]   │
│      │ [Job Card - legacy - Failed - 2m15s]         │
│      │ ...                                           │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `JobCard` (Nx - with expand/collapse)
- `ProgressBar` (per job)
- `Timeline` (stage visualization)
- `CodeBlock` (log viewer)
- `Tabs` (Active/Completed/Failed)

**Features:**
- Real-time updates via SSE
- Expandable job details
- Stage-by-stage progress
- Log streaming
- Cancel/Retry actions

**Data Sources:**
- `GET /api/jobs?status=active`
- `GET /api/jobs?status=completed&limit=10`
- `GET /api/jobs/{id}/logs` (streaming logs)
- SSE: `/api/jobs/stream` (real-time updates)

---

#### Page 5: Insights (NEW - Priority 4)

**Route:** `/insights`

**Purpose:** Analytics and trends (usage, performance, quality)

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header: Insights  [Last 7 days ▼]                  │
├──────┬──────────────────────────────────────────────┤
│      │ Usage Trends                                 │
│ Sid  │ [LineChart - Searches over time]             │
│ ebar │                                               │
│      │ Performance Metrics                           │
│      │ [Gauge - Avg Latency] [Gauge - P95] [Gauge]  │
│      │                                               │
│      │ Project Health                                │
│      │ [PieChart - Status distribution]             │
│      │                                               │
│      │ Top Searches                                  │
│      │ [BarChart - Most frequent queries]           │
│      │                                               │
│      │ Indexing Performance                          │
│      │ [LineChart - Files/sec over time]            │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `LineChart` (2x - trends)
- `BarChart` (1x - top searches)
- `PieChart` (1x - status distribution)
- `Gauge` (3x - latency metrics)
- `Select` (date range picker)

**Data Sources:**
- `GET /api/analytics/searches?period=7d`
- `GET /api/analytics/performance?period=7d`
- `GET /api/analytics/projects/health`
- `GET /api/analytics/top-queries?limit=10`

---

#### Page 6: Settings (REFACTORED - Priority 5)

**Route:** `/settings`

**Purpose:** Configure system behavior (Stripe-quality forms)

**Layout:**
```
┌─────────────────────────────────────────────────────┐
│ Header: Settings                                     │
├──────┬──────────────────────────────────────────────┤
│ Nav  │ General Settings                             │
│      │ ┌──────────────────────────────────────────┐ │
│ Gen  │ │ Auto-Resume Indexing: [Toggle]           │ │
│ eral │ │ Resume Delay: [Select: Immediate]        │ │
│      │ └──────────────────────────────────────────┘ │
│ Ind  │                                               │
│ ex   │ Indexing Performance                         │
│ ing  │ ┌──────────────────────────────────────────┐ │
│      │ │ Max Concurrent Jobs: [Input: 2]          │ │
│ Sto  │ │ Batch Size: [Input: 50]                  │ │
│ rage │ │ Chunk Size: [Slider: 1024 tokens]        │ │
│      │ └──────────────────────────────────────────┘ │
│ Log  │                                               │
│ ging │ Storage & Cleanup                            │
│      │ [Section...]                                  │
│ Adv  │                                               │
│ ance │ [Unsaved Changes] [Cancel] [Save Settings]   │
│ d    │                                               │
└──────┴──────────────────────────────────────────────┘
```

**Components Used:**
- `Tabs` or `Sidebar` (section navigation)
- `Input` (text, number)
- `Select` (dropdowns)
- `Switch` (toggles)
- `Slider` (ranges)
- `Alert` (unsaved changes warning)
- `Button` (save, cancel, reset)

**Features:**
- Auto-save draft (localStorage)
- Validation on blur
- Dangerous action confirmation
- Requires restart indicator
- Export/Import settings

**Data Sources:**
- `GET /api/configuration`
- `PUT /api/configuration`

---

### 5.2 Navigation Flow

```
┌────────────────────────────────────────────────────┐
│ Global Navigation                                  │
├────────────────────────────────────────────────────┤
│                                                     │
│ Sidebar (Persistent):                              │
│ ┌────────────────────────────────────┐            │
│ │ 🏠 Dashboard      (/)               │            │
│ │ 📁 Projects       (/projects)       │            │
│ │ 🔍 Search         (/search)         │            │
│ │ ⚙️  Jobs          (/jobs)           │            │
│ │ 📊 Insights       (/insights)       │            │
│ │ ⚙️  Settings      (/settings)       │            │
│ └────────────────────────────────────┘            │
│                                                     │
│ Global Actions:                                    │
│ • Cmd+K / Ctrl+K: Open command palette             │
│ • /: Focus search input                            │
│ • Esc: Close modals/popovers                       │
│ • ?: Show keyboard shortcuts                       │
│                                                     │
│ Breadcrumbs (Contextual):                          │
│ Dashboard > Projects > koan-core                   │
│                                                     │
└────────────────────────────────────────────────────┘
```

---

## 6. Backend API Enhancements

### 6.1 New Endpoints Required

#### Dashboard Metrics API

**1. GET /api/metrics/summary**
```json
{
  "projects": {
    "total": 5,
    "ready": 3,
    "indexing": 2,
    "failed": 0,
    "changeToday": +1
  },
  "chunks": {
    "total": 127000,
    "changeToday": +3400,
    "changeTrend": "up"
  },
  "searches": {
    "today": 234,
    "last24h": 412,
    "perHour": 17.2,
    "changeTrend": "up"
  },
  "performance": {
    "avgLatencyMs": 156,
    "p95LatencyMs": 340,
    "p99LatencyMs": 680,
    "changeWeek": -12.0
  }
}
```

**2. GET /api/health**
```json
{
  "healthy": true,
  "services": [
    {
      "name": "SQLite",
      "healthy": true,
      "message": "Connected",
      "latencyMs": 2
    },
    {
      "name": "Vector Store",
      "healthy": true,
      "message": "Online",
      "latencyMs": 45
    },
    {
      "name": "Embedding API",
      "healthy": true,
      "message": "Responding",
      "latencyMs": 120
    },
    {
      "name": "Outbox Sync",
      "healthy": true,
      "message": "Lag < 5s",
      "lagSeconds": 2.3
    }
  ]
}
```

**3. GET /api/metrics/performance?period=24h**
```json
{
  "period": "24h",
  "granularity": "1h",
  "dataPoints": [
    {
      "timestamp": "2025-11-07T00:00:00Z",
      "searches": 12,
      "avgLatencyMs": 165,
      "p95LatencyMs": 350
    },
    {
      "timestamp": "2025-11-07T01:00:00Z",
      "searches": 8,
      "avgLatencyMs": 142,
      "p95LatencyMs": 310
    }
    // ... 24 data points
  ],
  "summary": {
    "totalSearches": 412,
    "avgLatencyMs": 156,
    "p95LatencyMs": 340,
    "trends": {
      "searches": "up",
      "latency": "down"
    }
  }
}
```

**4. GET /api/activity/recent?limit=10**
```json
{
  "activities": [
    {
      "timestamp": "2025-11-07T14:32:00Z",
      "type": "search",
      "description": "Search \"authentication\" (127 results)",
      "metadata": {
        "projectId": "proj-abc",
        "latencyMs": 156,
        "resultCount": 127
      }
    },
    {
      "timestamp": "2025-11-07T14:17:00Z",
      "type": "index_completed",
      "description": "Indexed koan-data (3,456 chunks)",
      "metadata": {
        "projectId": "proj-xyz",
        "durationSeconds": 272,
        "chunksCreated": 3456
      }
    }
  ]
}
```

---

#### Analytics API (Insights Page)

**5. GET /api/analytics/searches?period=7d**
```json
{
  "period": "7d",
  "granularity": "1d",
  "dataPoints": [
    {
      "date": "2025-11-01",
      "searches": 234,
      "avgLatencyMs": 165
    },
    {
      "date": "2025-11-02",
      "searches": 198,
      "avgLatencyMs": 142
    }
    // ... 7 days
  ],
  "topQueries": [
    { "query": "authentication", "count": 45 },
    { "query": "vector provider", "count": 32 },
    { "query": "entity model", "count": 28 }
  ]
}
```

**6. GET /api/analytics/projects/health**
```json
{
  "distribution": {
    "ready": 3,
    "indexing": 2,
    "failed": 0,
    "notIndexed": 0
  },
  "projects": [
    {
      "id": "proj-abc",
      "name": "koan-core",
      "status": "Ready",
      "documentCount": 12345,
      "lastIndexed": "2025-11-07T12:00:00Z",
      "healthScore": 95
    }
  ]
}
```

---

#### Real-Time Streaming API

**7. SSE /api/jobs/stream**
```
Content-Type: text/event-stream

event: job-progress
data: {"jobId": "job-123", "progress": 62, "currentOperation": "Embedding chunk 6,345 of 10,234"}

event: job-completed
data: {"jobId": "job-123", "status": "Completed", "durationSeconds": 2292}

event: job-failed
data: {"jobId": "job-456", "status": "Failed", "errorMessage": "Path traversal detected"}
```

**8. SSE /api/metrics/stream**
```
Content-Type: text/event-stream

event: search-completed
data: {"latencyMs": 156, "resultCount": 127}

event: indexing-progress
data: {"projectId": "proj-abc", "progress": 65}
```

---

### 6.2 Enhanced Existing Endpoints

#### Projects Controller Enhancements

**GET /api/projects?page=1&pageSize=20&status=Ready&sort=lastIndexed:desc**
- Add pagination
- Add filtering by status
- Add sorting

**GET /api/projects/{id}/analytics**
```json
{
  "projectId": "proj-abc",
  "name": "koan-core",
  "indexingHistory": [
    {
      "timestamp": "2025-11-07T12:00:00Z",
      "durationSeconds": 272,
      "filesIndexed": 10234,
      "chunksCreated": 40512
    }
  ],
  "searchHistory": {
    "last7Days": 145,
    "topQueries": [...]
  },
  "performance": {
    "avgFilesPerSecond": 6.2,
    "avgLatencyMs": 142
  }
}
```

---

## 7. Frontend Architecture

### 7.1 Technology Stack

```yaml
Framework: React 18+ with TypeScript
Build Tool: Vite (fast HMR, optimized builds)
Styling: Tailwind CSS + CSS Modules (scoped styles)
State Management: Zustand (lightweight, TypeScript-friendly)
Data Fetching: TanStack Query (React Query) + Axios
Routing: React Router v6
Charts: Recharts (lightweight, composable)
Code Highlighting: Prism.js + react-syntax-highlighter
Forms: React Hook Form + Zod (validation)
Icons: Lucide React (lightweight, tree-shakeable)
Testing: Vitest + React Testing Library
Storybook: Component documentation + visual testing
Linting: ESLint + Prettier
Type Checking: TypeScript strict mode
```

### 7.2 Project Structure

```
src/
├── assets/
│   ├── fonts/
│   │   ├── Inter/
│   │   └── JetBrainsMono/
│   └── icons/
│
├── components/
│   ├── common/
│   │   ├── Button/
│   │   │   ├── Button.tsx
│   │   │   ├── Button.module.css
│   │   │   ├── Button.stories.tsx
│   │   │   └── Button.test.tsx
│   │   ├── Input/
│   │   ├── Badge/
│   │   └── ... (30+ components)
│   │
│   ├── domain/
│   │   ├── ProjectCard/
│   │   ├── SearchResultCard/
│   │   ├── JobProgressPanel/
│   │   └── ...
│   │
│   └── layout/
│       ├── Sidebar/
│       ├── Header/
│       └── PageLayout/
│
├── pages/
│   ├── Dashboard/
│   │   ├── Dashboard.tsx
│   │   ├── components/
│   │   │   ├── MetricsGrid/
│   │   │   ├── HealthPanel/
│   │   │   └── PerformanceChart/
│   │   └── hooks/
│   │       └── useDashboardData.ts
│   │
│   ├── Projects/
│   ├── Search/
│   ├── Jobs/
│   ├── Insights/
│   └── Settings/
│
├── hooks/
│   ├── useProjects.ts
│   ├── useSearch.ts
│   ├── useJobs.ts
│   ├── useRealtime.ts (SSE)
│   └── useToast.ts
│
├── api/
│   ├── client.ts (Axios instance)
│   ├── projects.ts
│   ├── search.ts
│   ├── jobs.ts
│   ├── metrics.ts
│   └── types.ts
│
├── stores/
│   ├── projectsStore.ts (Zustand)
│   ├── searchStore.ts
│   └── uiStore.ts (modals, toasts, etc.)
│
├── utils/
│   ├── formatting.ts
│   ├── validation.ts
│   └── helpers.ts
│
├── design-system/
│   ├── tokens.css
│   ├── typography.css
│   ├── utilities.css
│   └── reset.css
│
├── App.tsx
├── main.tsx
└── vite.config.ts
```

### 7.3 State Management Architecture

**Zustand Stores:**

```typescript
// stores/projectsStore.ts
interface ProjectsStore {
  projects: Project[];
  isLoading: boolean;
  error: string | null;

  fetchProjects: () => Promise<void>;
  createProject: (data: CreateProjectRequest) => Promise<void>;
  deleteProject: (id: string) => Promise<void>;
  indexProject: (id: string, force?: boolean) => Promise<void>;
}

// stores/searchStore.ts
interface SearchStore {
  query: string;
  results: SearchResult[];
  isSearching: boolean;
  filters: SearchFilters;

  setQuery: (query: string) => void;
  setFilters: (filters: Partial<SearchFilters>) => void;
  performSearch: () => Promise<void>;
  clearResults: () => void;
}

// stores/uiStore.ts
interface UIStore {
  modals: {
    createProject: boolean;
    deleteConfirm: boolean;
  };
  toasts: Toast[];

  openModal: (modal: keyof UIStore['modals']) => void;
  closeModal: (modal: keyof UIStore['modals']) => void;
  showToast: (toast: Omit<Toast, 'id'>) => void;
  dismissToast: (id: string) => void;
}
```

### 7.4 Real-Time Data Strategy

**Server-Sent Events (SSE):**

```typescript
// hooks/useRealtime.ts
export function useRealtimeJobs() {
  const [jobs, setJobs] = useState<Job[]>([]);

  useEffect(() => {
    const eventSource = new EventSource('/api/jobs/stream');

    eventSource.addEventListener('job-progress', (event) => {
      const data = JSON.parse(event.data);
      setJobs(jobs => jobs.map(job =>
        job.id === data.jobId
          ? { ...job, progress: data.progress, currentOperation: data.currentOperation }
          : job
      ));
    });

    eventSource.addEventListener('job-completed', (event) => {
      const data = JSON.parse(event.data);
      // Update job status, show toast notification
    });

    return () => eventSource.close();
  }, []);

  return jobs;
}
```

---

## 8. Data Visualization Strategy

### 8.1 Chart Library Comparison

| Library | Size | Composability | TypeScript | Verdict |
|---------|------|---------------|------------|---------|
| **Recharts** | 100KB | ✅ Excellent | ✅ Full | ✅ **SELECTED** |
| Chart.js | 150KB | ⚠️ Limited | ⚠️ Partial | ❌ Imperative API |
| Victory | 200KB | ✅ Excellent | ✅ Full | ❌ Too large |
| Nivo | 180KB | ✅ Good | ✅ Full | ❌ Heavy animations |
| D3.js | 240KB | ✅ Ultimate | ⚠️ Manual | ❌ Steep learning curve |

**Selection: Recharts** - Perfect balance of size, composability, TypeScript support

### 8.2 Chart Components Implementation

**LineChart Example:**

```typescript
// components/common/LineChart/LineChart.tsx
import { LineChart as RechartsLine, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';

interface LineChartProps {
  data: { [key: string]: any }[];
  xAxisKey: string;
  yAxisKey: string;
  color?: string;
  height?: number;
  showGrid?: boolean;
}

export function LineChart({
  data,
  xAxisKey,
  yAxisKey,
  color = 'var(--color-primary-600)',
  height = 300,
  showGrid = true
}: LineChartProps) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <RechartsLine data={data}>
        {showGrid && <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />}
        <XAxis dataKey={xAxisKey} stroke="var(--color-text-tertiary)" />
        <YAxis stroke="var(--color-text-tertiary)" />
        <Tooltip
          contentStyle={{
            backgroundColor: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--border-radius-md)'
          }}
        />
        <Line
          type="monotone"
          dataKey={yAxisKey}
          stroke={color}
          strokeWidth={2}
          dot={{ fill: color, r: 4 }}
          activeDot={{ r: 6 }}
        />
      </RechartsLine>
    </ResponsiveContainer>
  );
}
```

**Usage in Dashboard:**

```typescript
<LineChart
  data={performanceData}
  xAxisKey="hour"
  yAxisKey="latency"
  color="var(--color-primary-600)"
  height={250}
/>
```

---

## 9. Implementation Roadmap

### 9.1 Phase 1: Foundation (Weeks 1-2)

**Week 1: Design System + Core Components**

**Day 1-2: Project Setup**
- [ ] Initialize Vite + React + TypeScript project
- [ ] Configure Tailwind CSS
- [ ] Set up ESLint + Prettier
- [ ] Configure Storybook
- [ ] Create design tokens (tokens.css)
- [ ] Set up typography system

**Day 3-5: Foundation Components (10)**
- [ ] Button (all variants, loading states)
- [ ] Input (text, search, with addons)
- [ ] Badge (all semantic colors)
- [ ] Card (elevated, outlined, ghost)
- [ ] Modal (all sizes, close behaviors)
- [ ] Spinner (loading indicator)
- [ ] Skeleton (loading placeholders)
- [ ] Alert (all variants, dismissible)
- [ ] Toast (with queue, auto-dismiss)
- [ ] ProgressBar (determinate, indeterminate)

**Week 2: Data Display + Navigation**

**Day 1-3: Data Display Components (8)**
- [ ] Table (sortable, paginated)
- [ ] CodeBlock (syntax highlighting, line numbers)
- [ ] StatCard (Grafana-style metrics)
- [ ] Timeline (vertical, horizontal)
- [ ] Gauge (threshold indicators)
- [ ] Heatmap (activity visualization)
- [ ] Tabs (line, enclosed, pills)
- [ ] Breadcrumbs

**Day 4-5: Navigation Components (6)**
- [ ] Sidebar (collapsible, with badges)
- [ ] Header (global search, profile menu)
- [ ] CommandPalette (Cmd+K, fuzzy search)
- [ ] Pagination
- [ ] FilterBar
- [ ] SearchInput (with shortcuts)

---

### 9.2 Phase 2: Pages (Weeks 3-4)

**Week 3: Dashboard + Projects + Search**

**Day 1-2: Dashboard Page**
- [ ] Dashboard layout
- [ ] Metrics grid (4 StatCards)
- [ ] System health panel
- [ ] Performance chart integration
- [ ] Active jobs panel
- [ ] Recent activity timeline
- [ ] API integration (/api/metrics/*, /api/health)
- [ ] SSE connection for live updates

**Day 3-4: Projects Page**
- [ ] Projects list layout
- [ ] ProjectCard component (enhanced)
- [ ] Filter bar (status, sort)
- [ ] Create project modal (multi-step wizard)
- [ ] Bulk actions menu
- [ ] API integration (/api/projects)
- [ ] Real-time job progress updates

**Day 5: Search Page**
- [ ] Search layout (filters + results)
- [ ] SearchResultCard (syntax highlighted)
- [ ] Filter panel (collapsible)
- [ ] Infinite scroll or pagination
- [ ] Keyboard navigation (j/k)
- [ ] API integration (/api/search)

**Week 4: Jobs + Insights + Settings**

**Day 1: Jobs Page**
- [ ] Jobs list (active/completed/failed tabs)
- [ ] JobCard (expandable, with logs)
- [ ] Stage-by-stage progress
- [ ] Cancel/Retry actions
- [ ] SSE for real-time updates
- [ ] API integration (/api/jobs)

**Day 2: Insights Page**
- [ ] Insights layout
- [ ] Chart integration (Recharts)
- [ ] Usage trends (LineChart)
- [ ] Performance gauges (3x)
- [ ] Project health (PieChart)
- [ ] Top searches (BarChart)
- [ ] API integration (/api/analytics/*)

**Day 3: Settings Page**
- [ ] Settings layout (sectioned)
- [ ] Form components (Input, Select, Switch, Slider)
- [ ] Unsaved changes warning
- [ ] Validation
- [ ] Dangerous action confirmation
- [ ] API integration (/api/configuration)

**Day 4-5: Polish & Integration**
- [ ] Global navigation wiring
- [ ] Command palette integration
- [ ] Keyboard shortcuts
- [ ] Error boundaries
- [ ] Loading states
- [ ] Empty states
- [ ] Responsive design
- [ ] Cross-browser testing

---

### 9.3 Phase 3: Backend API (Weeks 5-6)

**Week 5: New Endpoints**

**Day 1-2: Metrics API**
- [ ] GET /api/metrics/summary
- [ ] GET /api/health
- [ ] GET /api/metrics/performance
- [ ] GET /api/activity/recent

**Day 3-4: Analytics API**
- [ ] GET /api/analytics/searches
- [ ] GET /api/analytics/projects/health
- [ ] GET /api/analytics/top-queries

**Day 5: Streaming API**
- [ ] SSE /api/jobs/stream
- [ ] SSE /api/metrics/stream

**Week 6: Enhancements + Testing**

**Day 1-3: API Enhancements**
- [ ] Projects pagination/filtering/sorting
- [ ] GET /api/projects/{id}/analytics
- [ ] Enhanced error responses
- [ ] Rate limiting
- [ ] Request logging

**Day 4-5: Testing**
- [ ] Unit tests for controllers
- [ ] Integration tests for endpoints
- [ ] Load testing (100 concurrent searches)
- [ ] SSE connection stability

---

### 9.4 Phase 4: Polish & Launch (Weeks 7-8)

**Week 7: Testing + Accessibility**

**Day 1-3: Frontend Testing**
- [ ] Unit tests for components (Vitest)
- [ ] Integration tests for pages
- [ ] E2E tests (Playwright)
- [ ] Storybook visual tests
- [ ] Performance testing (Lighthouse)

**Day 4-5: Accessibility**
- [ ] WCAG 2.1 AA audit
- [ ] Keyboard navigation testing
- [ ] Screen reader testing (NVDA, VoiceOver)
- [ ] Color contrast verification
- [ ] ARIA labels audit

**Week 8: Documentation + Launch**

**Day 1-2: Documentation**
- [ ] Component documentation (Storybook)
- [ ] API documentation (Swagger)
- [ ] User guide (onboarding)
- [ ] Developer guide (contribution)

**Day 3-4: Final Polish**
- [ ] Bug fixes from testing
- [ ] Performance optimization
- [ ] Bundle size optimization
- [ ] Caching strategy
- [ ] Production build testing

**Day 5: Launch**
- [ ] Production deployment
- [ ] Monitoring setup
- [ ] Analytics configuration
- [ ] Launch announcement

---

## 10. Asset Deliverables

### 10.1 Design Deliverables

**Figma Files (Design System)**

```
Koan Context Design System.fig
├── 01 - Design Tokens
│   ├── Colors (palette, semantic)
│   ├── Typography (scale, weights)
│   ├── Spacing (8px grid)
│   ├── Shadows
│   └── Border Radius
│
├── 02 - Foundation Components
│   ├── Buttons (9 variants × 3 sizes = 27 states)
│   ├── Inputs (6 types × 3 states = 18 variants)
│   ├── Badges (6 colors × 3 sizes = 18 variants)
│   └── ... (30+ components)
│
├── 03 - Data Display Components
│   ├── StatCard (4 layouts)
│   ├── CodeBlock (with/without line numbers)
│   ├── Charts (5 types)
│   └── ...
│
├── 04 - Page Layouts
│   ├── Dashboard (desktop, tablet, mobile)
│   ├── Projects (desktop, tablet, mobile)
│   ├── Search (desktop, tablet, mobile)
│   ├── Jobs (desktop, tablet, mobile)
│   ├── Insights (desktop, tablet, mobile)
│   └── Settings (desktop, tablet, mobile)
│
└── 05 - Flows & States
    ├── Create Project Wizard (5 steps)
    ├── Search Flow (empty, loading, results, error)
    ├── Job Progress (stages, completion, failure)
    └── Error States (12 scenarios)
```

**Figma Deliverable Size:** ~200 frames, ~500 components

---

### 10.2 Code Deliverables

**Frontend Codebase**

```
Frontend Repository Structure:
└── koan-context-ui/ (~8,500 LOC TypeScript + CSS)
    ├── src/
    │   ├── components/ (30+ components, ~3,000 LOC)
    │   ├── pages/ (6 pages, ~2,000 LOC)
    │   ├── hooks/ (~500 LOC)
    │   ├── api/ (~800 LOC)
    │   ├── stores/ (~400 LOC)
    │   ├── utils/ (~300 LOC)
    │   └── design-system/ (~500 LOC CSS)
    │
    ├── .storybook/ (Storybook config)
    ├── tests/ (~1,000 LOC tests)
    ├── package.json
    ├── vite.config.ts
    ├── tsconfig.json
    └── README.md

Backend Enhancements: (~1,500 LOC C#)
└── src/Koan.Context/
    ├── Controllers/
    │   ├── MetricsController.cs (new, ~300 LOC)
    │   ├── HealthController.cs (new, ~200 LOC)
    │   ├── AnalyticsController.cs (new, ~400 LOC)
    │   └── [Enhanced existing controllers, ~600 LOC]
    │
    └── Services/
        ├── MetricsService.cs (new, ~250 LOC)
        ├── AnalyticsService.cs (new, ~350 LOC)
        └── StreamingService.cs (SSE, ~200 LOC)
```

**Total Code Volume:**
- Frontend: ~8,500 LOC (TypeScript + CSS)
- Backend: ~1,500 LOC (C#)
- Tests: ~1,200 LOC
- **Total: ~11,200 LOC**

---

### 10.3 Documentation Deliverables

**1. Design System Documentation** (`DESIGN-SYSTEM.md`)
- Complete color palette reference
- Typography scale usage guide
- Component API documentation (props, variants)
- Composition patterns
- Accessibility guidelines
- Code examples for each component

**2. Component Storybook** (Interactive)
- All 30+ components documented
- Live props playground
- Accessibility tests integrated
- Visual regression tests
- Usage examples
- Do's and Don'ts

**3. Frontend Architecture Guide** (`FRONTEND-ARCHITECTURE.md`)
- Technology stack rationale
- Project structure explanation
- State management patterns
- API integration patterns
- Real-time data strategy
- Testing strategy

**4. API Documentation** (`API-REFERENCE.md`)
- All endpoint specifications
- Request/response schemas
- Authentication details
- Rate limiting
- Error codes
- Example curl commands

**5. User Guide** (`USER-GUIDE.md`)
- Getting started (5-minute setup)
- Dashboard overview
- Project management workflow
- Search tips and tricks
- Job monitoring
- Settings configuration
- Keyboard shortcuts reference

**6. Developer Guide** (`DEVELOPER-GUIDE.md`)
- Local development setup
- Running Storybook
- Running tests
- Building for production
- Contributing guidelines
- Code style guide

---

### 10.4 Asset Checklist

**Design Assets:**
- [ ] Figma design system file (~200 frames)
- [ ] Component specifications (30+ components)
- [ ] Page mockups (6 pages × 3 breakpoints = 18 layouts)
- [ ] Flow diagrams (create project, search, job monitoring)
- [ ] Icon set (50+ icons, SVG)
- [ ] Logo variations (light/dark, horizontal/vertical)

**Code Assets:**
- [ ] Design system (tokens, typography, utilities)
- [ ] Component library (30+ components)
- [ ] Page implementations (6 pages)
- [ ] Hooks library (8+ hooks)
- [ ] API client (Axios + React Query)
- [ ] State management (Zustand stores)
- [ ] Unit tests (85%+ coverage)
- [ ] Integration tests (E2E flows)
- [ ] Storybook stories (all components)

**Backend Assets:**
- [ ] MetricsController (5 endpoints)
- [ ] HealthController (1 endpoint)
- [ ] AnalyticsController (6 endpoints)
- [ ] StreamingService (SSE implementation)
- [ ] Enhanced ProjectsController
- [ ] Enhanced SearchController
- [ ] Unit tests (controllers, services)
- [ ] Integration tests (API endpoints)

**Documentation Assets:**
- [ ] Design system documentation
- [ ] Component Storybook
- [ ] Frontend architecture guide
- [ ] API reference documentation
- [ ] User guide
- [ ] Developer guide
- [ ] Keyboard shortcuts reference
- [ ] Accessibility statement

---

## 11. Budget & Resource Allocation

### 11.1 Detailed Cost Breakdown

**Design Phase (4-5 weeks)**

| Task | Hours | Rate | Cost |
|------|-------|------|------|
| Design system (tokens, components) | 80 | $150/hr | $12,000 |
| Page layouts (6 pages × 3 breakpoints) | 54 | $150/hr | $8,100 |
| Flow diagrams & specifications | 40 | $150/hr | $6,000 |
| Storybook stories setup | 20 | $150/hr | $3,000 |
| **Total Design** | **194** | | **$29,100** |

**Frontend Implementation (6-8 weeks)**

| Task | Hours | Rate | Cost |
|------|-------|------|------|
| Project setup + build system | 16 | $125/hr | $2,000 |
| Design system implementation | 40 | $125/hr | $5,000 |
| Component library (30+ components) | 120 | $125/hr | $15,000 |
| Page implementations (6 pages) | 80 | $125/hr | $10,000 |
| API integration + state management | 40 | $125/hr | $5,000 |
| Real-time features (SSE) | 20 | $125/hr | $2,500 |
| Testing (unit + integration + E2E) | 40 | $125/hr | $5,000 |
| Accessibility + responsive design | 24 | $125/hr | $3,000 |
| **Total Frontend** | **380** | | **$47,500** |

**Backend Implementation (2-3 weeks)**

| Task | Hours | Rate | Cost |
|------|-------|------|------|
| New API endpoints (MetricsController, etc.) | 40 | $150/hr | $6,000 |
| Streaming API (SSE) | 20 | $150/hr | $3,000 |
| API enhancements (pagination, filtering) | 24 | $150/hr | $3,600 |
| Testing (unit + integration) | 16 | $150/hr | $2,400 |
| **Total Backend** | **100** | | **$15,000** |

**Documentation & Polish (1 week)**

| Task | Hours | Rate | Cost |
|------|-------|------|------|
| Design system documentation | 8 | $100/hr | $800 |
| User guide | 8 | $100/hr | $800 |
| Developer guide | 8 | $100/hr | $800 |
| API documentation | 8 | $100/hr | $800 |
| Final testing + bug fixes | 16 | $125/hr | $2,000 |
| **Total Documentation** | **48** | | **$5,200** |

**Total Investment: $96,800**

---

### 11.2 Team Structure

**Option 1: External Consultancy**

- 1 Senior UX/UI Designer (5 weeks, full-time)
- 1 Senior Frontend Engineer (8 weeks, full-time)
- 1 Mid-Level Frontend Engineer (6 weeks, part-time)
- 1 Backend Engineer (3 weeks, part-time)
- 1 QA Engineer (2 weeks, part-time)

**Option 2: Internal Team**

- Reduce costs by 20-30% (no agency markup)
- Longer timeline (10-12 weeks due to context switching)
- Better domain knowledge
- Ongoing maintenance included

---

### 11.3 ROI Justification

**Quantitative Benefits:**

1. **Reduced Support Costs**
   - Current: Users email "how do I..." questions (5-10/week, 30 min each)
   - Future: Self-service UI reduces support by 70%
   - Savings: ~$15k/year in support time

2. **Faster Enterprise Sales Cycles**
   - Current: Demos require technical founder (2-3 hours per demo)
   - Future: Self-serve demo via premium UI (prospects explore independently)
   - Impact: 30% faster sales cycles, 20% higher close rate

3. **Increased User Adoption**
   - Current: 40% of trial users churn due to UX complexity
   - Future: Premium UI reduces churn to <15%
   - Impact: +25% in MRR growth

**Qualitative Benefits:**

1. **Brand Perception:** Premium UI signals enterprise-readiness
2. **Competitive Differentiation:** Grafana-quality UX is rare in dev tools
3. **Developer Satisfaction:** Better UX = better reviews = more organic growth
4. **Procurement Approval:** Professional UI eases enterprise buying decisions

**Break-Even Analysis:**

- Investment: $97k
- Avg Enterprise Deal: $50k/year
- Break-even: 2 additional enterprise customers in Year 1
- Expected: 5-8 additional customers (conservative)
- **ROI:** 250-400% in Year 1

---

## Conclusion

This specification transforms Koan.Context from a functional MVP into a **Grafana-quality premium application** suitable for enterprise adoption.

**Key Achievements:**
- **30+ reusable components** (design system foundation)
- **6 reimagined pages** (Dashboard, Projects, Search, Jobs, Insights, Settings)
- **Real-time updates** (SSE for live progress tracking)
- **Data visualization** (Recharts-powered analytics)
- **Accessibility** (WCAG 2.1 AA compliant)
- **Production-ready** (85%+ test coverage, comprehensive documentation)

**Investment:** $97k over 12-14 weeks
**Returns:** 250-400% ROI in Year 1 through increased sales and reduced churn

**Next Steps:**
1. Approve budget and timeline
2. Assemble team (internal or external)
3. Begin Phase 1: Design System (Week 1)
4. Deliver MVP Dashboard + Projects (Week 4 checkpoint)
5. Full launch (Week 12)

---

**Document Status:** PROPOSED
**Review Required:** Engineering Leadership, Product Management, Design Team
**Approval Needed:** CTO, VP Product
**Next Review:** 2025-11-14

**Related Documentation:**
- [Koan.Context UX Proposal](KOAN-CONTEXT-UX-PROPOSAL.md)
- [Koan.Context Overview](../guides/koan-context-overview.md)
- [Koan.Context Hardening Proposal](KOAN-CONTEXT-HARDENING.md)
