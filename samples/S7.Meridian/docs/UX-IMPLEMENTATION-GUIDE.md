# Meridian UX Implementation Guide

**Purpose:** Step-by-step code implementation guide for the UX realignment  
**Prerequisites:** Read `MERIDIAN-UX-REALIGNMENT.md` and review `UX-VISUAL-MOCKUPS.md`

---

## 📦 Phase 1: Foundation Setup

### Step 1.1: Copy SnapVault Design Tokens

**File:** `wwwroot/css/design-tokens.css`

Replace Meridian's current design tokens with SnapVault's refined palette:

```css
/**
 * Meridian Design Tokens
 * Based on SnapVault Pro Design System
 * Source: S6.SnapVault/wwwroot/css/design-tokens.css
 */

:root {
  /* ===== Surface Colors ===== */
  --color-canvas: #0a0a0a;
  --color-surface: #141414;
  --color-surface-hover: #1a1a1a;
  --color-surface-active: #222222;
  --color-surface-subtle: #18181b;

  /* ===== Borders ===== */
  --color-border-subtle: #2a2a2a;
  --color-border-medium: #3a3a3a;
  --color-border-strong: #4a4a4a;
  --color-border-interactive: #3f3f46;

  /* ===== Text ===== */
  --color-text-primary: #e8e8e8;
  --color-text-secondary: #a8a8a8;
  --color-text-tertiary: #787878;
  --color-text-disabled: #4a4a4a;
  --color-text-inverse: #0a0a0a;

  /* ===== Accents ===== */
  --color-accent-primary: #5b9fff;
  --color-accent-hover: #7cb3ff;
  --color-accent-success: #4ade80;
  --color-accent-warning: #fbbf24;
  --color-accent-danger: #f87171;

  /* ===== Focus & Selection ===== */
  --color-focus-ring: rgba(91, 159, 255, 0.4);
  --color-selection: rgba(91, 159, 255, 0.15);

  /* ===== Typography ===== */
  --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto,
    "Helvetica Neue", Arial, sans-serif;

  --text-xs: 0.6875rem; /* 11px - Section headers */
  --text-sm: 0.8125rem; /* 13px - Metadata */
  --text-base: 0.9375rem; /* 15px - Body */
  --text-lg: 1.125rem; /* 18px - Card titles */
  --text-xl: 1.5rem; /* 24px - Page titles */
  --text-2xl: 2rem; /* 32px - Hero values */

  --weight-normal: 400;
  --weight-medium: 500;
  --weight-semibold: 600;
  --weight-bold: 700;

  /* ===== Spacing (8px grid) ===== */
  --space-1: 0.5rem; /* 8px */
  --space-2: 1rem; /* 16px */
  --space-3: 1.5rem; /* 24px */
  --space-4: 2rem; /* 32px */
  --space-5: 2.5rem; /* 40px */
  --space-6: 3rem; /* 48px */

  /* ===== Shadows ===== */
  --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.4);
  --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.5);
  --shadow-focus: 0 0 0 3px var(--color-focus-ring);

  /* ===== Border Radius ===== */
  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 8px;
  --radius-xl: 12px;

  /* ===== Transitions ===== */
  --ease-out-cubic: cubic-bezier(0.33, 1, 0.68, 1);
  --ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);

  --duration-fast: 100ms;
  --duration-normal: 200ms;
  --duration-slow: 300ms;

  /* ===== Z-Index ===== */
  --layer-base: 0;
  --layer-dropdown: 10;
  --layer-overlay: 100;
  --layer-modal: 500;
  --layer-toast: 2000;
}
```

### Step 1.2: Create Sidebar CSS

**File:** `wwwroot/css/sidebar.css`

```css
/**
 * Borderless Sidebar Navigation
 * Inspired by SnapVault's sidebar-redesign.css
 */

.app-layout {
  display: flex;
  height: 100vh;
  background: var(--color-canvas);
}

/* ===== Sidebar Container ===== */

.app-sidebar {
  width: 240px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-4); /* 32px between sections */
  padding: var(--space-3) 0; /* 24px top/bottom */
  background: transparent; /* No background! */
  overflow-y: auto;
  overflow-x: hidden;
  border-right: 1px solid var(--color-border-subtle);
}

/* ===== Section Structure ===== */

.sidebar-section {
  display: flex;
  flex-direction: column;
  gap: 10px; /* Item spacing */
  padding: 0 var(--space-2); /* 16px horizontal padding */
}

/* ===== Section Header ===== */

.section-header {
  font-size: var(--text-xs); /* 11px */
  font-weight: var(--weight-semibold);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.4);
  margin: 0;
  padding: 0;
  user-select: none;
}

/* ===== Sidebar Items ===== */

.sidebar-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  background: transparent;
  border: none;
  border-left: 2px solid transparent;
  margin-left: -2px; /* Compensate for border */
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all var(--duration-fast) var(--ease-in-out);
  text-align: left;
  width: 100%;
  color: var(--color-text-primary);
  font-size: var(--text-sm);
  font-weight: var(--weight-normal);
}

.sidebar-item:hover {
  background: rgba(255, 255, 255, 0.05);
}

.sidebar-item.active {
  border-left-color: var(--color-accent-primary);
  background: rgba(91, 159, 255, 0.08);
  color: rgba(255, 255, 255, 1);
}

.sidebar-item:focus-visible {
  outline: 2px solid var(--color-accent-primary);
  outline-offset: 2px;
}

/* ===== Item Components ===== */

.item-icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  opacity: 0.8;
}

.sidebar-item.active .item-icon {
  opacity: 1;
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
  margin-left: auto;
}

.item-badge {
  padding: 2px 6px;
  background: rgba(255, 255, 255, 0.08);
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  font-size: 11px;
  font-weight: var(--weight-semibold);
  color: var(--color-text-tertiary);
  min-width: 20px;
  text-align: center;
}

.sidebar-item.active .item-badge {
  background: rgba(91, 159, 255, 0.2);
  border-color: rgba(91, 159, 255, 0.3);
  color: var(--color-accent-primary);
}

/* ===== Keyboard Shortcuts ===== */

.shortcut {
  display: inline-block;
  padding: 2px 4px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid var(--color-border-subtle);
  border-radius: 3px;
  font-size: 10px;
  font-weight: var(--weight-medium);
  font-family: var(--font-mono);
  color: var(--color-text-tertiary);
  line-height: 1;
}

/* ===== Responsive ===== */

@media (max-width: 768px) {
  .app-sidebar {
    position: fixed;
    left: 0;
    top: 0;
    height: 100vh;
    z-index: var(--layer-overlay);
    transform: translateX(-100%);
    transition: transform var(--duration-slow) var(--ease-out-cubic);
    background: var(--color-surface);
  }

  .app-sidebar.open {
    transform: translateX(0);
  }
}
```

### Step 1.3: Create Sidebar JavaScript

**File:** `wwwroot/js/components/Sidebar.js`

```javascript
/**
 * Sidebar Navigation Component
 * Manages navigation state, active items, and keyboard shortcuts
 */

export class Sidebar {
  constructor(router, eventBus) {
    this.router = router;
    this.eventBus = eventBus;
    this.sections = this.buildSections();
  }

  /**
   * Build sidebar sections configuration
   */
  buildSections() {
    return [
      {
        header: "LIBRARY",
        items: [
          {
            id: "all-analyses",
            label: "All Analyses",
            icon: this.icons.grid,
            path: "/analyses",
            badge: null, // Will be populated dynamically
            shortcuts: ["G", "A"],
          },
          {
            id: "favorites",
            label: "Favorites",
            icon: this.icons.star,
            path: "/analyses/favorites",
            badge: null,
            shortcuts: ["G", "F"],
          },
          {
            id: "recent",
            label: "Recent",
            icon: this.icons.clock,
            path: "/analyses/recent",
            badge: null,
            shortcuts: ["G", "R"],
          },
        ],
      },
      {
        header: "WORK",
        items: [
          {
            id: "active-analyses",
            label: "Active Analyses",
            icon: this.icons.activity,
            path: "/analyses",
            badge: null,
          },
          {
            id: "insights-dashboard",
            label: "Insights Dashboard",
            icon: this.icons.trending,
            path: "/insights",
            badge: null,
          },
          {
            id: "document-library",
            label: "Document Library",
            icon: this.icons.fileText,
            path: "/documents",
            badge: null,
          },
        ],
      },
      {
        header: "CONFIGURATION",
        items: [
          {
            id: "analysis-types",
            label: "Analysis Types",
            icon: this.icons.layers,
            path: "/configuration/analysis-types",
            badge: null,
            shortcuts: ["G", "C"],
          },
          {
            id: "source-types",
            label: "Source Types",
            icon: this.icons.fileCode,
            path: "/configuration/source-types",
            badge: null,
          },
          {
            id: "integrations",
            label: "Integrations",
            icon: this.icons.zap,
            path: "/configuration/integrations",
            badge: null,
          },
        ],
      },
    ];
  }

  /**
   * Render sidebar HTML
   */
  render() {
    return `
      <aside class="app-sidebar" role="navigation" aria-label="Main navigation">
        ${this.sections.map((section) => this.renderSection(section)).join("")}
      </aside>
    `;
  }

  /**
   * Render a section
   */
  renderSection(section) {
    return `
      <nav class="sidebar-section">
        <h2 class="section-header">${section.header}</h2>
        ${section.items.map((item) => this.renderItem(item)).join("")}
      </nav>
    `;
  }

  /**
   * Render a sidebar item
   */
  renderItem(item) {
    const isActive = this.router.currentPath === item.path;
    const activeClass = isActive ? "active" : "";

    return `
      <button
        class="sidebar-item ${activeClass}"
        data-nav-id="${item.id}"
        data-nav-path="${item.path}"
        ${isActive ? 'aria-current="page"' : ""}
        aria-label="${item.label}${
      item.badge ? ", " + item.badge + " items" : ""
    }">
        
        <svg class="item-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          ${item.icon}
        </svg>
        
        <span class="item-label">${item.label}</span>
        
        <div class="item-meta">
          ${
            item.shortcuts
              ? item.shortcuts
                  .map((key) => `<kbd class="shortcut">${key}</kbd>`)
                  .join("")
              : ""
          }
          ${
            item.badge !== null
              ? `<span class="item-badge" aria-label="${item.badge} items">${item.badge}</span>`
              : ""
          }
        </div>
      </button>
    `;
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    const items = container.querySelectorAll(".sidebar-item");

    items.forEach((item) => {
      item.addEventListener("click", (e) => {
        const path = item.dataset.navPath;
        this.router.navigate(path);
      });
    });

    // Keyboard navigation
    this.setupKeyboardNav(items);
  }

  /**
   * Setup keyboard navigation
   */
  setupKeyboardNav(items) {
    const itemsArray = Array.from(items);
    let currentIndex = itemsArray.findIndex((item) =>
      item.classList.contains("active")
    );

    document.addEventListener("keydown", (e) => {
      // Arrow key navigation (when sidebar item focused)
      if (document.activeElement.classList.contains("sidebar-item")) {
        if (e.key === "ArrowDown") {
          e.preventDefault();
          currentIndex = (currentIndex + 1) % itemsArray.length;
          itemsArray[currentIndex].focus();
        } else if (e.key === "ArrowUp") {
          e.preventDefault();
          currentIndex =
            (currentIndex - 1 + itemsArray.length) % itemsArray.length;
          itemsArray[currentIndex].focus();
        } else if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          document.activeElement.click();
        }
      }

      // Global shortcuts (G+X pattern)
      if (this.lastKey === "g" || this.lastKey === "G") {
        const shortcutItem = itemsArray.find((item) => {
          const shortcuts = item.querySelector(
            ".shortcut:last-child"
          )?.textContent;
          return shortcuts?.toLowerCase() === e.key.toLowerCase();
        });

        if (shortcutItem) {
          e.preventDefault();
          shortcutItem.click();
          this.lastKey = null;
          return;
        }
      }

      this.lastKey = e.key;
      setTimeout(() => {
        this.lastKey = null;
      }, 1000);
    });
  }

  /**
   * Update badge counts
   */
  updateBadges(counts) {
    Object.entries(counts).forEach(([itemId, count]) => {
      const item = this.sections
        .flatMap((s) => s.items)
        .find((i) => i.id === itemId);

      if (item) {
        item.badge = count;
      }
    });

    // Re-render if needed
    this.eventBus.emit("sidebar:badges-updated");
  }

  /**
   * Update active state based on current route
   */
  updateActiveState() {
    const currentPath = this.router.currentPath;

    document.querySelectorAll(".sidebar-item").forEach((item) => {
      const itemPath = item.dataset.navPath;

      if (currentPath === itemPath || currentPath.startsWith(itemPath + "/")) {
        item.classList.add("active");
        item.setAttribute("aria-current", "page");
      } else {
        item.classList.remove("active");
        item.removeAttribute("aria-current");
      }
    });
  }

  /**
   * Icon definitions
   */
  get icons() {
    return {
      grid: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>',
      star: '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>',
      clock:
        '<circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline>',
      activity:
        '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>',
      trending:
        '<polyline points="23 6 13.5 15.5 8.5 10.5 1 18"></polyline><polyline points="17 6 23 6 23 12"></polyline>',
      fileText:
        '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline>',
      layers:
        '<polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline>',
      fileCode:
        '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><polyline points="10 13 6 17 10 21"></polyline><polyline points="14 13 18 17 14 21"></polyline>',
      zap: '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>',
    };
  }
}
```

---

## 📦 Phase 2: Detail Panel Component

### Step 2.1: Create Detail Panel CSS

**File:** `wwwroot/css/detail-panel.css`

```css
/**
 * Detail Panel Component
 * Slide-in panel for entity details (view/edit mode)
 * Inspired by SnapVault's lightbox-panel.css
 */

/* ===== Backdrop ===== */

.detail-panel-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  opacity: 0;
  transition: opacity var(--duration-slow) var(--ease-in-out);
  z-index: var(--layer-overlay);
  pointer-events: none;
}

.detail-panel-backdrop.visible {
  opacity: 1;
  pointer-events: auto;
}

/* ===== Panel Container ===== */

.detail-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 60%;
  max-width: 800px;
  height: 100vh;
  background: var(--color-surface);
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.5);
  transform: translateX(100%);
  transition: transform var(--duration-slow) var(--ease-out-cubic);
  z-index: calc(var(--layer-overlay) + 1);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.detail-panel.open {
  transform: translateX(0);
}

/* ===== Header ===== */

.detail-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--color-border-subtle);
  flex-shrink: 0;
}

.detail-panel-title {
  font-size: var(--text-xl);
  font-weight: var(--weight-semibold);
  color: var(--color-text-primary);
  margin: 0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-panel-close {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  border-radius: var(--radius-md);
  color: var(--color-text-secondary);
  cursor: pointer;
  transition: all var(--duration-fast) var(--ease-in-out);
  flex-shrink: 0;
  margin-left: var(--space-2);
}

.detail-panel-close:hover {
  background: var(--color-surface-hover);
  color: var(--color-text-primary);
}

.detail-panel-close:focus-visible {
  outline: 2px solid var(--color-accent-primary);
  outline-offset: 2px;
}

/* ===== Body ===== */

.detail-panel-body {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: var(--space-4);
}

/* Smooth scroll */
.detail-panel-body {
  scroll-behavior: smooth;
}

/* Custom scrollbar */
.detail-panel-body::-webkit-scrollbar {
  width: 8px;
}

.detail-panel-body::-webkit-scrollbar-track {
  background: var(--color-surface);
}

.detail-panel-body::-webkit-scrollbar-thumb {
  background: var(--color-border-medium);
  border-radius: 4px;
}

.detail-panel-body::-webkit-scrollbar-thumb:hover {
  background: var(--color-border-strong);
}

/* ===== Footer ===== */

.detail-panel-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--color-border-subtle);
  flex-shrink: 0;
  gap: var(--space-2);
}

.footer-actions-right {
  display: flex;
  gap: var(--space-2);
  margin-left: auto;
}

/* ===== Content Sections ===== */

.panel-section {
  margin-bottom: var(--space-4);
}

.panel-section:last-child {
  margin-bottom: 0;
}

.panel-section-header {
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--color-text-tertiary);
  margin: 0 0 var(--space-2) 0;
}

.panel-section-content {
  font-size: var(--text-base);
  color: var(--color-text-primary);
  line-height: 1.6;
}

/* ===== Field Display ===== */

.panel-field {
  margin-bottom: var(--space-3);
}

.panel-field-label {
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-text-tertiary);
  margin-bottom: var(--space-1);
}

.panel-field-value {
  font-size: var(--text-base);
  color: var(--color-text-primary);
}

/* ===== Tags Display ===== */

.panel-tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}

.panel-tag {
  display: inline-flex;
  align-items: center;
  padding: 4px 12px;
  background: rgba(91, 159, 255, 0.1);
  border: 1px solid rgba(91, 159, 255, 0.3);
  border-radius: var(--radius-full);
  font-size: var(--text-sm);
  color: var(--color-accent-primary);
  font-weight: var(--weight-medium);
}

/* ===== Edit Mode ===== */

.detail-panel.edit-mode .panel-field-value {
  display: none;
}

.detail-panel.edit-mode .panel-field-input {
  display: block;
}

.panel-field-input {
  display: none;
}

.panel-field-input input,
.panel-field-input textarea,
.panel-field-input select {
  width: 100%;
  padding: 10px 12px;
  background: var(--color-surface-hover);
  border: 1px solid var(--color-border-medium);
  border-radius: var(--radius-md);
  color: var(--color-text-primary);
  font-size: var(--text-base);
  font-family: var(--font-sans);
  transition: all var(--duration-fast) var(--ease-in-out);
}

.panel-field-input input:focus,
.panel-field-input textarea:focus,
.panel-field-input select:focus {
  outline: none;
  border-color: var(--color-accent-primary);
  box-shadow: var(--shadow-focus);
}

.panel-field-input textarea {
  min-height: 100px;
  resize: vertical;
}

/* ===== Responsive ===== */

@media (max-width: 1200px) {
  .detail-panel {
    width: 70%;
  }
}

@media (max-width: 768px) {
  .detail-panel {
    width: 100%;
    max-width: none;
  }

  .detail-panel-body {
    padding: var(--space-3);
  }

  .detail-panel-footer {
    padding: var(--space-2) var(--space-3);
  }
}

/* ===== Loading State ===== */

.detail-panel.loading .detail-panel-body {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 300px;
}

.detail-panel-spinner {
  width: 48px;
  height: 48px;
  border: 3px solid var(--color-border-subtle);
  border-top-color: var(--color-accent-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
```

### Step 2.2: Create Detail Panel JavaScript

**File:** `wwwroot/js/components/DetailPanel.js`

```javascript
/**
 * DetailPanel Component
 * Reusable slide-in panel for entity details (view/edit modes)
 */

export class DetailPanel {
  constructor(eventBus) {
    this.eventBus = eventBus;
    this.isOpen = false;
    this.mode = "view"; // 'view' or 'edit'
    this.data = null;
    this.onSave = null;
    this.onDelete = null;
    this.previousFocus = null;
  }

  /**
   * Open panel with data
   */
  open(data, mode = "view") {
    this.data = data;
    this.mode = mode;
    this.isOpen = true;

    // Save current focus
    this.previousFocus = document.activeElement;

    // Render panel
    this.render();

    // Show panel
    requestAnimationFrame(() => {
      const panel = document.querySelector(".detail-panel");
      const backdrop = document.querySelector(".detail-panel-backdrop");

      backdrop?.classList.add("visible");
      panel?.classList.add("open");

      // Focus close button
      panel?.querySelector(".detail-panel-close")?.focus();

      // Trap focus
      this.trapFocus(panel);
    });

    // Emit event
    this.eventBus.emit("detail-panel:opened", { data, mode });
  }

  /**
   * Close panel
   */
  close() {
    const panel = document.querySelector(".detail-panel");
    const backdrop = document.querySelector(".detail-panel-backdrop");

    backdrop?.classList.remove("visible");
    panel?.classList.remove("open");

    // Wait for animation
    setTimeout(() => {
      panel?.remove();
      backdrop?.remove();
      this.isOpen = false;

      // Restore focus
      this.previousFocus?.focus();

      // Emit event
      this.eventBus.emit("detail-panel:closed");
    }, 300); // Match CSS transition duration
  }

  /**
   * Switch mode (view <-> edit)
   */
  switchMode(newMode) {
    this.mode = newMode;

    const panel = document.querySelector(".detail-panel");
    if (newMode === "edit") {
      panel?.classList.add("edit-mode");
    } else {
      panel?.classList.remove("edit-mode");
    }

    // Re-render body
    const body = panel?.querySelector(".detail-panel-body");
    if (body) {
      body.innerHTML = this.renderBody();
      this.attachBodyEventHandlers();
    }

    // Update footer
    const footer = panel?.querySelector(".detail-panel-footer");
    if (footer) {
      footer.innerHTML = this.renderFooter();
      this.attachFooterEventHandlers();
    }
  }

  /**
   * Render panel HTML
   */
  render() {
    // Remove existing panel
    document.querySelector(".detail-panel")?.remove();
    document.querySelector(".detail-panel-backdrop")?.remove();

    // Create new panel
    const html = `
      <div class="detail-panel-backdrop"></div>
      <aside
        class="detail-panel ${this.mode === "edit" ? "edit-mode" : ""}"
        role="dialog"
        aria-modal="true"
        aria-labelledby="panel-title">
        
        <header class="detail-panel-header">
          <h2 id="panel-title" class="detail-panel-title">
            ${this.mode === "edit" ? "Edit: " : ""}${this.escapeHtml(
      this.data.name || "Details"
    )}
          </h2>
          <button class="detail-panel-close" aria-label="Close detail panel">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </header>
        
        <div class="detail-panel-body">
          ${this.renderBody()}
        </div>
        
        <footer class="detail-panel-footer">
          ${this.renderFooter()}
        </footer>
      </aside>
    `;

    document.body.insertAdjacentHTML("beforeend", html);

    // Attach event handlers
    this.attachEventHandlers();
  }

  /**
   * Render body content (override in subclasses)
   */
  renderBody() {
    return `
      <div class="panel-section">
        <h3 class="panel-section-header">DESCRIPTION</h3>
        <div class="panel-section-content">
          ${
            this.mode === "view"
              ? `
            <p>${this.escapeHtml(
              this.data.description || "No description provided"
            )}</p>
          `
              : `
            <div class="panel-field-input">
              <textarea name="description" rows="4">${this.escapeHtml(
                this.data.description || ""
              )}</textarea>
            </div>
          `
          }
        </div>
      </div>
      
      ${
        this.data.tags && this.data.tags.length > 0
          ? `
        <div class="panel-section">
          <h3 class="panel-section-header">TAGS</h3>
          <div class="panel-tags">
            ${this.data.tags
              .map(
                (tag) => `
              <span class="panel-tag">${this.escapeHtml(tag)}</span>
            `
              )
              .join("")}
          </div>
        </div>
      `
          : ""
      }
    `;
  }

  /**
   * Render footer buttons
   */
  renderFooter() {
    if (this.mode === "edit") {
      return `
        <button class="btn btn-danger" data-action="delete">Delete</button>
        <div class="footer-actions-right">
          <button class="btn btn-secondary" data-action="cancel">Cancel</button>
          <button class="btn btn-primary" data-action="save">Save Changes</button>
        </div>
      `;
    } else {
      return `
        <button class="btn btn-danger" data-action="delete">Delete</button>
        <div class="footer-actions-right">
          <button class="btn btn-secondary" data-action="close">Close</button>
          <button class="btn btn-primary" data-action="edit">Edit</button>
        </div>
      `;
    }
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers() {
    // Close button
    document
      .querySelector(".detail-panel-close")
      ?.addEventListener("click", () => {
        this.close();
      });

    // Backdrop click
    document
      .querySelector(".detail-panel-backdrop")
      ?.addEventListener("click", () => {
        this.close();
      });

    // Escape key
    document.addEventListener("keydown", this.handleKeydown.bind(this));

    // Footer buttons
    this.attachFooterEventHandlers();

    // Body event handlers
    this.attachBodyEventHandlers();
  }

  /**
   * Attach footer event handlers
   */
  attachFooterEventHandlers() {
    const footer = document.querySelector(".detail-panel-footer");

    footer?.addEventListener("click", (e) => {
      const action = e.target.closest("[data-action]")?.dataset.action;

      switch (action) {
        case "close":
          this.close();
          break;
        case "edit":
          this.switchMode("edit");
          break;
        case "cancel":
          this.switchMode("view");
          break;
        case "save":
          this.handleSave();
          break;
        case "delete":
          this.handleDelete();
          break;
      }
    });
  }

  /**
   * Attach body event handlers
   */
  attachBodyEventHandlers() {
    // Override in subclasses if needed
  }

  /**
   * Handle keyboard events
   */
  handleKeydown(e) {
    if (!this.isOpen) return;

    if (e.key === "Escape") {
      e.preventDefault();
      this.close();
    } else if (
      e.key === "e" &&
      this.mode === "view" &&
      e.target.tagName !== "INPUT" &&
      e.target.tagName !== "TEXTAREA"
    ) {
      e.preventDefault();
      this.switchMode("edit");
    } else if (
      (e.metaKey || e.ctrlKey) &&
      e.key === "s" &&
      this.mode === "edit"
    ) {
      e.preventDefault();
      this.handleSave();
    }
  }

  /**
   * Handle save
   */
  async handleSave() {
    if (!this.onSave) return;

    const button = document.querySelector('[data-action="save"]');
    button.disabled = true;
    button.textContent = "Saving...";

    try {
      // Collect form data
      const formData = this.collectFormData();

      // Call save handler
      await this.onSave(formData);

      // Update local data
      this.data = { ...this.data, ...formData };

      // Switch to view mode
      this.switchMode("view");

      // Emit event
      this.eventBus.emit("detail-panel:saved", formData);
    } catch (error) {
      console.error("Save failed:", error);
      this.eventBus.emit("detail-panel:save-error", error);
    } finally {
      button.disabled = false;
      button.textContent = "Save Changes";
    }
  }

  /**
   * Handle delete
   */
  async handleDelete() {
    if (!this.onDelete) return;

    const confirmed = confirm(
      `Are you sure you want to delete "${this.data.name}"? This cannot be undone.`
    );
    if (!confirmed) return;

    try {
      await this.onDelete(this.data.id);
      this.close();
      this.eventBus.emit("detail-panel:deleted", this.data.id);
    } catch (error) {
      console.error("Delete failed:", error);
      this.eventBus.emit("detail-panel:delete-error", error);
    }
  }

  /**
   * Collect form data from panel inputs
   */
  collectFormData() {
    const body = document.querySelector(".detail-panel-body");
    const inputs = body.querySelectorAll("input, textarea, select");

    const data = {};
    inputs.forEach((input) => {
      if (input.name) {
        data[input.name] = input.value;
      }
    });

    return data;
  }

  /**
   * Trap focus within panel
   */
  trapFocus(panel) {
    const focusableElements = panel.querySelectorAll(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );

    const firstElement = focusableElements[0];
    const lastElement = focusableElements[focusableElements.length - 1];

    panel.addEventListener("keydown", (e) => {
      if (e.key !== "Tab") return;

      if (e.shiftKey) {
        if (document.activeElement === firstElement) {
          e.preventDefault();
          lastElement.focus();
        }
      } else {
        if (document.activeElement === lastElement) {
          e.preventDefault();
          firstElement.focus();
        }
      }
    });
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  }
}
```

---

## 📦 Phase 3: Integration

### Step 3.1: Update App Layout

**File:** `wwwroot/index.html`

```html
<!DOCTYPE html>
<html lang="en" dir="ltr">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Meridian</title>

    <!-- Design Tokens -->
    <link rel="stylesheet" href="/css/design-tokens.css" />

    <!-- New Components -->
    <link rel="stylesheet" href="/css/sidebar.css" />
    <link rel="stylesheet" href="/css/detail-panel.css" />

    <!-- Existing Components -->
    <link rel="stylesheet" href="/css/components.css" />
    <link rel="stylesheet" href="/css/app.css" />
  </head>
  <body>
    <div class="app-layout">
      <!-- Sidebar will be injected here -->
      <div id="app-sidebar"></div>

      <!-- Main content area -->
      <main id="app-content" class="app-content">
        <div class="app-loading">
          <svg class="spinner" width="48" height="48" viewBox="0 0 24 24">
            <circle cx="12" cy="12" r="10"></circle>
          </svg>
          <p>Loading Meridian...</p>
        </div>
      </main>
    </div>

    <script type="module" src="/js/app.js"></script>
  </body>
</html>
```

### Step 3.2: Update Main App

**File:** `wwwroot/js/app.js`

```javascript
import { Sidebar } from "./components/Sidebar.js";
import { DetailPanel } from "./components/DetailPanel.js";
import { Router } from "./utils/Router.js";
import { EventBus } from "./utils/EventBus.js";
import { API } from "./api.js";

class MeridianApp {
  constructor() {
    this.eventBus = new EventBus();
    this.router = new Router();
    this.api = new API();

    // Initialize components
    this.sidebar = new Sidebar(this.router, this.eventBus);
    this.detailPanel = new DetailPanel(this.eventBus);

    // Setup routes
    this.setupRoutes();

    // Listen to events
    this.setupEventListeners();
  }

  async init() {
    console.log("[Meridian] Initializing...");

    // Render sidebar
    const sidebarContainer = document.getElementById("app-sidebar");
    sidebarContainer.innerHTML = this.sidebar.render();
    this.sidebar.attachEventHandlers(sidebarContainer);

    // Update badge counts
    await this.updateBadgeCounts();

    // Start router
    this.router.start();

    console.log("[Meridian] Ready");
  }

  setupRoutes() {
    this.router.route("", () => this.renderDashboard());
    this.router.route("analyses", () => this.renderAnalysesList());
    this.router.route("analyses/:id", (params) =>
      this.renderAnalysisWorkspace(params.id)
    );
    this.router.route("configuration/analysis-types", () =>
      this.renderAnalysisTypesList()
    );
    this.router.route("configuration/source-types", () =>
      this.renderSourceTypesList()
    );
  }

  setupEventListeners() {
    // Listen for detail panel events
    this.eventBus.on("detail-panel:saved", () => {
      // Refresh current view
      this.router.refresh();
    });

    this.eventBus.on("detail-panel:deleted", () => {
      // Refresh current view
      this.router.refresh();
    });

    // Listen for sidebar badge updates
    this.eventBus.on("sidebar:badges-updated", () => {
      // Re-render sidebar
      const sidebarContainer = document.getElementById("app-sidebar");
      sidebarContainer.innerHTML = this.sidebar.render();
      this.sidebar.attachEventHandlers(sidebarContainer);
    });
  }

  async updateBadgeCounts() {
    try {
      const [types, sourceTypes, pipelines] = await Promise.all([
        this.api.getAnalysisTypes(),
        this.api.getSourceTypes(),
        this.api.getPipelines(),
      ]);

      this.sidebar.updateBadges({
        "all-analyses": pipelines.length,
        "analysis-types": types.length,
        "source-types": sourceTypes.length,
      });
    } catch (error) {
      console.error("Failed to update badge counts:", error);
    }
  }

  async renderAnalysisTypesList() {
    const container = document.getElementById("app-content");

    try {
      const types = await this.api.getAnalysisTypes();

      container.innerHTML = `
        <div class="entity-list-view">
          <header class="list-header">
            <h1>Analysis Types</h1>
            <button class="btn btn-primary" data-action="create">+ Create Type</button>
          </header>
          
          <div class="entity-grid">
            ${types.map((type) => this.renderTypeCard(type)).join("")}
          </div>
        </div>
      `;

      // Attach card click handlers
      container.querySelectorAll(".entity-card").forEach((card) => {
        card.addEventListener("click", (e) => {
          if (e.target.closest("[data-action]")) return;

          const id = card.dataset.id;
          const type = types.find((t) => t.id === id);
          this.openTypeDetailPanel(type);
        });
      });
    } catch (error) {
      console.error("Failed to render analysis types:", error);
    }
  }

  renderTypeCard(type) {
    return `
      <div class="entity-card" data-id="${type.id}">
        <h3>${this.escapeHtml(type.name)}</h3>
        <p>${this.escapeHtml(type.description || "")}</p>
        <div class="card-actions">
          <button class="btn btn-sm" data-action="view">View</button>
          <button class="btn btn-sm" data-action="edit">Edit</button>
        </div>
      </div>
    `;
  }

  openTypeDetailPanel(type) {
    this.detailPanel.data = type;
    this.detailPanel.onSave = async (data) => {
      await this.api.updateAnalysisType(type.id, data);
    };
    this.detailPanel.onDelete = async (id) => {
      await this.api.deleteAnalysisType(id);
    };
    this.detailPanel.open(type, "view");
  }

  escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  }
}

// Initialize app
const app = new MeridianApp();
app.init();
```

---

## ✅ Testing Checklist

### Phase 1 Testing

- [ ] Sidebar renders correctly
- [ ] Active state highlights properly
- [ ] Badge counts display
- [ ] Keyboard shortcuts work (G+A, G+F, etc.)
- [ ] Arrow key navigation works
- [ ] Responsive behavior on mobile

### Phase 2 Testing

- [ ] Detail panel slides in smoothly
- [ ] Backdrop dims background
- [ ] Click backdrop closes panel
- [ ] Escape key closes panel
- [ ] Edit mode switches correctly
- [ ] Save/Cancel work as expected
- [ ] Focus trap works

### Phase 3 Testing

- [ ] Card click opens detail panel
- [ ] Panel maintains scroll position
- [ ] Multiple panels can be opened/closed
- [ ] Browser back button works correctly
- [ ] Keyboard navigation throughout

---

## 🚀 Next Steps

1. **Complete Phase 1** - Get sidebar working
2. **Complete Phase 2** - Get detail panels working
3. **Complete Phase 3** - Integrate with existing code
4. **Phase 4** - Polish and accessibility audit

**Questions?** Refer to the main proposal document: `MERIDIAN-UX-REALIGNMENT.md`
