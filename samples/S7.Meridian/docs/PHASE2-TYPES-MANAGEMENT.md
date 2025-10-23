# Phase 2: Types Management - Full CRUD Implementation

**Project**: Meridian UX Enhancement - Types Management Module
**Status**: Planning Phase
**Date**: 2025-10-23

---

## Executive Summary

This document outlines the implementation plan for comprehensive Type Management in Meridian, addressing three critical gaps:

1. **Analysis Types CRUD**: Full-page CRUD operations using EntityController<> capabilities (View/Edit modes)
2. **Source Types Module**: Complete module with same CRUD capabilities as Analysis Types
3. **AI Create Modal**: Professional modal interface with proper UX for AI-generated type suggestions

**CRITICAL DESIGN DECISION**: CRUD operations use **full-page UIs** (NOT modals). Only the AI Create interface uses a modal.

---

## Current State Analysis

### ✅ What Exists (Phase 1)
- Dashboard with metrics and navigation
- Compact Insights Panel (SnapVault-inspired)
- Two-column workspace (40/60 split)
- Basic type management view (read-only grid)
- Simple `prompt()` dialogs for creation

### ❌ What's Missing (Phase 2 Scope)

#### 1. Analysis Types CRUD
**Problem**: Current implementation only shows a grid. No View, Edit, Create, or Delete operations.

**Current Code**:
```javascript
// wwwroot/js/app.js:241-294
async renderTypesManagement(container) {
  // Only renders read-only grid
  // No View/Edit/Delete actions
}
```

**EntityController Endpoints Available** (from meridian-swagger.json):
- `GET /api/analysistypes` - List all
- `GET /api/analysistypes/{id}` - Get single
- `GET /api/analysistypes/new` - Get template for new entity
- `POST /api/analysistypes` - Create
- `PATCH /api/analysistypes/{id}` - Update (JSON Patch format)
- `DELETE /api/analysistypes/{id}` - Delete single
- `DELETE /api/analysistypes?q={query}` - Delete by query
- `POST /api/analysistypes/bulk` - Bulk create
- `DELETE /api/analysistypes/bulk` - Bulk delete

#### 2. Source Types Module
**Problem**: Completely missing. No UI, no navigation, no management interface.

**EntityController Endpoints Available** (from meridian-swagger.json):
- `GET /api/sourcetypes` - List all
- `GET /api/sourcetypes/{id}` - Get single
- `GET /api/sourcetypes/new` - Get template
- `POST /api/sourcetypes` - Create
- `PATCH /api/sourcetypes/{id}` - Update (JSON Patch)
- `DELETE /api/sourcetypes/{id}` - Delete
- `POST /api/sourcetypes/ai-suggest` - AI generation
- `POST /api/sourcetypes/bulk` - Bulk create
- `DELETE /api/sourcetypes/bulk` - Bulk delete
- `GET /api/sourcetypes/all` - Get all (alternative endpoint)

#### 3. AI Create Interface
**Problem**: Using native `prompt()` and `confirm()` dialogs - unprofessional UX.

**Current Code**:
```javascript
// wwwroot/js/app.js:779-802
async createTypeWithAI() {
  const goal = prompt('What do you want to analyze?'); // ❌ Native dialog
  const audience = prompt('Who is the audience?');     // ❌ Native dialog
  // ...
  const confirm = window.confirm(`AI suggests...`);    // ❌ Native dialog
}
```

**Required**: Professional modal with:
- Form fields with labels and placeholders
- Help text and examples
- Loading states during AI generation
- Preview of AI suggestions before saving
- Separate modals for Analysis Types and Source Types

---

## Target Architecture

### Navigation Hierarchy

```
Dashboard
├── Analysis Types Management (full-page)
│   ├── List View (grid with actions)
│   ├── View Mode (full-page, read-only)
│   ├── Create Mode (full-page form)
│   ├── Edit Mode (full-page form)
│   └── AI Create Modal (overlay)
│
├── Source Types Management (full-page)
│   ├── List View (grid with actions)
│   ├── View Mode (full-page, read-only)
│   ├── Create Mode (full-page form)
│   ├── Edit Mode (full-page form)
│   └── AI Create Modal (overlay)
│
└── Analyses (existing)
```

### File Structure

```
wwwroot/
├── js/
│   ├── components/
│   │   ├── Dashboard.js                    [EXISTS - needs update]
│   │   ├── InsightsPanel.js                [EXISTS]
│   │   ├── Modal.js                        [CREATE - reusable modal]
│   │   ├── AnalysisTypesManager.js         [CREATE - full CRUD]
│   │   ├── SourceTypesManager.js           [CREATE - full CRUD]
│   │   ├── AICreateTypeModal.js            [CREATE - AI interface]
│   │   └── TypeFormView.js                 [CREATE - shared form component]
│   ├── api.js                              [UPDATE - add CRUD methods]
│   └── app.js                              [UPDATE - new routes]
│
├── css/
│   ├── modal.css                           [CREATE - modal styles]
│   ├── type-management.css                 [CREATE - full-page CRUD styles]
│   └── ... (existing files)
│
└── index.html                              [EXISTS - no changes needed]
```

---

## API Client Updates

### File: `wwwroot/js/api.js`

Add CRUD methods for both entity types:

```javascript
// ==================== Analysis Types CRUD ====================

/**
 * Get analysis type template for new entity
 */
async getAnalysisTypeTemplate() {
  return this.get('/api/analysistypes/new');
}

/**
 * Update analysis type (PATCH)
 * @param {string} id - Type ID
 * @param {Object} updates - Fields to update
 */
async updateAnalysisType(id, updates) {
  // Convert to JSON Patch format
  const patches = Object.entries(updates).map(([key, value]) => ({
    op: 'replace',
    path: `/${key}`,
    value: value
  }));

  return this.request(`/api/analysistypes/${id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json-patch+json' },
    body: JSON.stringify(patches)
  });
}

/**
 * Delete analysis type
 */
async deleteAnalysisType(id) {
  return this.delete(`/api/analysistypes/${id}`);
}

/**
 * Bulk delete analysis types
 */
async bulkDeleteAnalysisTypes(ids) {
  return this.request('/api/analysistypes/bulk', {
    method: 'DELETE',
    body: JSON.stringify(ids)
  });
}

// ==================== Source Types CRUD ====================

/**
 * Get all source types
 */
async getSourceTypes() {
  return this.get('/api/sourcetypes');
}

/**
 * Get source type by ID
 */
async getSourceType(id) {
  return this.get(`/api/sourcetypes/${id}`);
}

/**
 * Get source type template
 */
async getSourceTypeTemplate() {
  return this.get('/api/sourcetypes/new');
}

/**
 * Create source type
 */
async createSourceType(sourceType) {
  return this.post('/api/sourcetypes', sourceType);
}

/**
 * Update source type (PATCH)
 */
async updateSourceType(id, updates) {
  const patches = Object.entries(updates).map(([key, value]) => ({
    op: 'replace',
    path: `/${key}`,
    value: value
  }));

  return this.request(`/api/sourcetypes/${id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json-patch+json' },
    body: JSON.stringify(patches)
  });
}

/**
 * Delete source type
 */
async deleteSourceType(id) {
  return this.delete(`/api/sourcetypes/${id}`);
}

/**
 * AI-generate source type
 */
async suggestSourceType(goal, audience, additionalContext = '') {
  return this.post('/api/sourcetypes/ai-suggest', {
    goal,
    audience,
    additionalContext
  });
}

/**
 * Bulk operations for source types
 */
async bulkCreateSourceTypes(sourceTypes) {
  return this.post('/api/sourcetypes/bulk', sourceTypes);
}

async bulkDeleteSourceTypes(ids) {
  return this.request('/api/sourcetypes/bulk', {
    method: 'DELETE',
    body: JSON.stringify(ids)
  });
}
```

---

## Component Specifications

### 1. Modal Component (`Modal.js`)

**Purpose**: Reusable modal foundation for AI Create interfaces.

**Features**:
- Overlay with backdrop blur
- Centered content area
- Header with title and close button
- Footer with action buttons
- Responsive (full-screen on mobile)
- Escape key to close
- Click outside to close (optional)

**API**:
```javascript
class Modal {
  constructor(options = {}) {
    this.title = options.title || '';
    this.size = options.size || 'medium'; // 'small', 'medium', 'large'
    this.closeOnOverlay = options.closeOnOverlay !== false;
  }

  open(content, footerButtons = []) {
    // Render modal with content and buttons
    // Attach event handlers
    // Return promise that resolves with result
  }

  close(result = null) {
    // Remove modal from DOM
    // Resolve promise
  }

  setLoading(isLoading) {
    // Show/hide loading spinner in modal
  }
}
```

**HTML Structure**:
```html
<div class="modal-overlay">
  <div class="modal-dialog modal-{size}">
    <div class="modal-header">
      <h2 class="modal-title">{title}</h2>
      <button class="modal-close" aria-label="Close">×</button>
    </div>
    <div class="modal-body">
      {content}
    </div>
    <div class="modal-footer">
      {buttons}
    </div>
  </div>
</div>
```

---

### 2. AI Create Type Modal (`AICreateTypeModal.js`)

**Purpose**: Professional interface for AI-generated type suggestions.

**Two Variants**:
1. `AICreateAnalysisTypeModal` - For analysis types
2. `AICreateSourceTypeModal` - For source types (input types)

**UX Flow**:
1. **Step 1: User Input Form**
   - Goal field (textarea, 2-3 lines)
   - Audience field (text input)
   - Additional context (textarea, optional, collapsed by default)
   - Help text with examples
   - "Generate" button

2. **Step 2: Loading State**
   - Spinner with "AI is generating suggestions..."
   - Progress indicator

3. **Step 3: Preview & Confirm**
   - Display AI-generated suggestions in read-only fields
   - Name (with edit capability)
   - Description (with edit capability)
   - Generated schema/template preview
   - Actions: "Create Type" (primary) or "Cancel" / "Regenerate"

**Form Design**:

```html
<!-- Step 1: Input Form -->
<div class="ai-create-form">
  <div class="form-section">
    <label class="form-label">
      What do you want to analyze?
      <span class="required">*</span>
    </label>
    <textarea
      class="form-input"
      rows="3"
      placeholder="Example: Customer feedback from support tickets to identify common pain points"
      required
    ></textarea>
    <p class="form-help">
      Describe the analysis goal in 1-2 sentences. Be specific about what insights you need.
    </p>
  </div>

  <div class="form-section">
    <label class="form-label">
      Who is the audience?
      <span class="required">*</span>
    </label>
    <input
      type="text"
      class="form-input"
      placeholder="Example: Product managers and customer success team"
      required
    />
    <p class="form-help">
      Who will use these insights? This helps tailor the output format.
    </p>
  </div>

  <div class="form-section form-section-collapsible">
    <button class="form-section-toggle" type="button">
      <svg class="icon-chevron">...</svg>
      Additional Context (optional)
    </button>
    <div class="form-section-content" hidden>
      <textarea
        class="form-input"
        rows="4"
        placeholder="Any specific requirements, data formats, or constraints..."
      ></textarea>
    </div>
  </div>

  <div class="form-examples">
    <details>
      <summary>Show Examples</summary>
      <div class="example-list">
        <div class="example-item">
          <strong>Goal:</strong> Extract key financial metrics from quarterly earnings reports
          <br>
          <strong>Audience:</strong> Executive team and investors
        </div>
        <div class="example-item">
          <strong>Goal:</strong> Analyze contract terms to identify non-standard clauses
          <br>
          <strong>Audience:</strong> Legal team and contract managers
        </div>
      </div>
    </details>
  </div>
</div>

<!-- Step 3: Preview -->
<div class="ai-preview">
  <div class="preview-header">
    <svg class="icon-ai">...</svg>
    <h3>AI Generated Suggestion</h3>
  </div>

  <div class="form-section">
    <label class="form-label">Type Name</label>
    <input
      type="text"
      class="form-input"
      value="{ai_generated_name}"
    />
  </div>

  <div class="form-section">
    <label class="form-label">Description</label>
    <textarea
      class="form-input"
      rows="3"
    >{ai_generated_description}</textarea>
  </div>

  <div class="form-section">
    <label class="form-label">Generated Schema</label>
    <div class="schema-preview">
      <pre><code>{ai_generated_schema}</code></pre>
    </div>
  </div>

  <div class="preview-actions">
    <button class="btn btn-secondary" data-action="regenerate">
      <svg class="icon">...</svg>
      Regenerate
    </button>
    <button class="btn btn-primary" data-action="create">
      <svg class="icon">...</svg>
      Create Type
    </button>
  </div>
</div>
```

---

### 3. Type Form View (`TypeFormView.js`)

**Purpose**: Shared full-page form component for Create/Edit modes.

**Used By**: Both AnalysisTypesManager and SourceTypesManager.

**Modes**:
- `view` - Read-only display
- `create` - New entity form
- `edit` - Edit existing entity

**Features**:
- Full-page comfortable layout
- Clear visual hierarchy
- Inline validation
- Dirty state tracking
- Unsaved changes warning
- Save/Cancel actions in sticky footer

**Layout**:
```
┌─────────────────────────────────────────────────────┐
│ ← Back to Types    [View Mode]                      │ Header
├─────────────────────────────────────────────────────┤
│                                                      │
│  ┌───────────────────────────────────────────────┐ │
│  │ Basic Information                             │ │
│  ├───────────────────────────────────────────────┤ │
│  │ Name:        [Financial Analysis Type      ] │ │
│  │ Description: [Analyze quarterly earnings...] │ │
│  │ Tags:        [financial] [quarterly] [+Add] │ │
│  └───────────────────────────────────────────────┘ │
│                                                      │
│  ┌───────────────────────────────────────────────┐ │
│  │ Template Schema                               │ │
│  ├───────────────────────────────────────────────┤ │
│  │ {                                             │ │
│  │   "revenue": "currency",                      │ │
│  │   "profit_margin": "percentage"               │ │
│  │ }                                             │ │
│  └───────────────────────────────────────────────┘ │
│                                                      │
│  ┌───────────────────────────────────────────────┐ │
│  │ Analysis Instructions                         │ │
│  ├───────────────────────────────────────────────┤ │
│  │ [Guidance for AI extraction...]              │ │
│  └───────────────────────────────────────────────┘ │
│                                                      │
├─────────────────────────────────────────────────────┤
│         [Cancel]           [Save Changes]          │ Sticky Footer
└─────────────────────────────────────────────────────┘
```

**API**:
```javascript
class TypeFormView {
  constructor(mode, entityType, api, eventBus) {
    this.mode = mode; // 'view', 'create', 'edit'
    this.entityType = entityType; // 'analysis' or 'source'
    this.api = api;
    this.eventBus = eventBus;
    this.isDirty = false;
  }

  async render(id = null) {
    // Load entity if edit/view mode
    // Render full-page form
    // Return HTML string
  }

  attachEventHandlers(container) {
    // Handle form input changes
    // Track dirty state
    // Validate fields
    // Handle save/cancel
  }

  async save() {
    // Validate form
    // Call API (create or update)
    // Navigate back on success
  }
}
```

---

### 4. Analysis Types Manager (`AnalysisTypesManager.js`)

**Purpose**: Complete CRUD interface for Analysis Types.

**Routes**:
- `/types/analysis` - List view
- `/types/analysis/new` - Create mode
- `/types/analysis/{id}` - View mode
- `/types/analysis/{id}/edit` - Edit mode

**List View Features**:
- Grid layout with type cards
- Search/filter by name, tags
- Bulk selection and actions
- Actions per card: View, Edit, Delete
- "Create Type" button
- "AI Create" button (opens modal)
- Empty state with call-to-action

**Card Actions**:
```html
<div class="type-card">
  <div class="type-card-header">
    <h3>Financial Analysis</h3>
    <div class="type-card-actions">
      <button class="btn-icon" data-action="view" title="View">
        <svg>eye icon</svg>
      </button>
      <button class="btn-icon" data-action="edit" title="Edit">
        <svg>edit icon</svg>
      </button>
      <button class="btn-icon" data-action="delete" title="Delete">
        <svg>trash icon</svg>
      </button>
    </div>
  </div>
  <p class="type-card-description">...</p>
  <div class="type-card-meta">
    <span class="type-badge">Analysis</span>
    <span class="type-usage">Used in 5 analyses</span>
  </div>
</div>
```

**Bulk Actions**:
```html
<div class="bulk-actions-bar" hidden>
  <span class="bulk-selection-count">3 selected</span>
  <button class="btn btn-secondary" data-action="bulk-delete">
    <svg class="icon">trash</svg>
    Delete Selected
  </button>
  <button class="btn btn-secondary" data-action="clear-selection">
    Clear Selection
  </button>
</div>
```

---

### 5. Source Types Manager (`SourceTypesManager.js`)

**Purpose**: Complete CRUD interface for Source Types (Input Types).

**Identical structure to AnalysisTypesManager**, but for source types.

**Routes**:
- `/types/source` - List view
- `/types/source/new` - Create mode
- `/types/source/{id}` - View mode
- `/types/source/{id}/edit` - Edit mode

**Key Differences**:
- Different API endpoints (`/api/sourcetypes`)
- Different badge color/label ("Input" instead of "Analysis")
- Different AI Create modal title and examples

---

## CSS Specifications

### File: `wwwroot/css/modal.css`

```css
/* ==================== Modal Overlay ==================== */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  animation: fadeIn 0.2s ease;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.modal-dialog {
  background: var(--color-bg-elevated);
  border-radius: 16px;
  box-shadow: var(--shadow-2xl);
  max-height: 90vh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s ease;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* Modal Sizes */
.modal-small { width: 400px; }
.modal-medium { width: 600px; }
.modal-large { width: 800px; }

/* ==================== Modal Header ==================== */
.modal-header {
  padding: 24px 24px 16px;
  border-bottom: 1px solid var(--color-border-light);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.modal-title {
  font-size: 20px;
  font-weight: 600;
  margin: 0;
  color: var(--color-text-primary);
}

.modal-close {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
  color: var(--color-text-secondary);
  transition: all 0.2s ease;
}

.modal-close:hover {
  background: var(--color-bg-hover);
  color: var(--color-text-primary);
}

/* ==================== Modal Body ==================== */
.modal-body {
  padding: 24px;
  overflow-y: auto;
  flex: 1;
}

/* ==================== Modal Footer ==================== */
.modal-footer {
  padding: 16px 24px;
  border-top: 1px solid var(--color-border-light);
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  background: var(--color-bg-secondary);
}

/* ==================== AI Create Form ==================== */
.ai-create-form {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text-primary);
  display: flex;
  align-items: center;
  gap: 4px;
}

.required {
  color: var(--color-red);
}

.form-input {
  padding: 12px;
  border: 1px solid var(--color-border-medium);
  border-radius: 8px;
  font-size: 14px;
  color: var(--color-text-primary);
  background: var(--color-bg-base);
  transition: border-color 0.2s ease;
}

.form-input:focus {
  border-color: var(--color-blue);
  outline: none;
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
}

.form-help {
  font-size: 12px;
  color: var(--color-text-secondary);
  margin: 0;
  line-height: 1.4;
}

/* ==================== Collapsible Section ==================== */
.form-section-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 0;
  font-size: 14px;
  font-weight: 500;
  color: var(--color-text-secondary);
  transition: color 0.2s ease;
}

.form-section-toggle:hover {
  color: var(--color-text-primary);
}

.icon-chevron {
  transition: transform 0.2s ease;
}

.form-section-toggle[aria-expanded="true"] .icon-chevron {
  transform: rotate(180deg);
}

/* ==================== Examples ==================== */
.form-examples {
  margin-top: 16px;
}

.form-examples details {
  border: 1px solid var(--color-border-light);
  border-radius: 8px;
  padding: 12px;
  background: var(--color-bg-secondary);
}

.form-examples summary {
  font-size: 13px;
  font-weight: 500;
  color: var(--color-blue);
  cursor: pointer;
}

.example-list {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.example-item {
  padding: 12px;
  background: var(--color-bg-elevated);
  border-radius: 6px;
  font-size: 12px;
  line-height: 1.6;
}

/* ==================== AI Preview ==================== */
.ai-preview {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.preview-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--color-border-light);
}

.preview-header h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.icon-ai {
  color: var(--color-blue);
}

.schema-preview {
  background: var(--color-bg-secondary);
  border: 1px solid var(--color-border-light);
  border-radius: 8px;
  padding: 16px;
}

.schema-preview pre {
  margin: 0;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  line-height: 1.5;
}

.preview-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  padding-top: 8px;
}

/* ==================== Loading State ==================== */
.modal-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;
  gap: 16px;
}

.modal-loading .spinner {
  width: 48px;
  height: 48px;
  animation: spin 1s linear infinite;
  color: var(--color-blue);
}

.modal-loading p {
  font-size: 14px;
  color: var(--color-text-secondary);
  margin: 0;
}

/* ==================== Responsive ==================== */
@media (max-width: 768px) {
  .modal-dialog {
    width: 100%;
    height: 100%;
    max-height: 100vh;
    border-radius: 0;
  }

  .modal-medium,
  .modal-large {
    width: 100%;
  }
}
```

---

### File: `wwwroot/css/type-management.css`

```css
/* ==================== Full-Page Type Form ==================== */
.type-form-view {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: var(--color-bg-base);
}

.type-form-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  background: var(--color-bg-elevated);
  border-bottom: 1px solid var(--color-border-light);
  flex-shrink: 0;
}

.type-form-mode-badge {
  padding: 6px 12px;
  background: var(--color-blue-bg);
  color: var(--color-blue);
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  border-radius: 6px;
  letter-spacing: 0.5px;
}

.type-form-mode-badge.mode-view {
  background: var(--color-gray-200);
  color: var(--color-gray-700);
}

.type-form-mode-badge.mode-create {
  background: rgba(5, 150, 105, 0.1);
  color: var(--color-green);
}

.type-form-mode-badge.mode-edit {
  background: var(--color-blue-bg);
  color: var(--color-blue);
}

.type-form-title {
  flex: 1;
  font-size: 20px;
  font-weight: 600;
  margin: 0;
}

/* ==================== Form Content ==================== */
.type-form-content {
  flex: 1;
  overflow-y: auto;
  padding: 32px;
}

.type-form-container {
  max-width: 800px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 32px;
}

/* Form Sections */
.type-form-section {
  background: var(--color-bg-elevated);
  border: 1px solid var(--color-border-light);
  border-radius: 12px;
  padding: 24px;
}

.type-form-section-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 20px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--color-border-light);
}

.type-form-section-icon {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  background: var(--color-blue-bg);
  color: var(--color-blue);
  display: flex;
  align-items: center;
  justify-content: center;
}

.type-form-section-title {
  font-size: 16px;
  font-weight: 600;
  margin: 0;
}

.type-form-section-body {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* Form Fields */
.type-form-field {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.type-form-field-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text-primary);
  display: flex;
  align-items: center;
  gap: 4px;
}

.type-form-field-input {
  padding: 12px 16px;
  border: 2px solid var(--color-border-medium);
  border-radius: 8px;
  font-size: 14px;
  color: var(--color-text-primary);
  background: var(--color-bg-base);
  transition: all 0.2s ease;
}

.type-form-field-input:focus {
  border-color: var(--color-blue);
  outline: none;
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
}

.type-form-field-input:disabled {
  background: var(--color-bg-secondary);
  color: var(--color-text-tertiary);
  cursor: not-allowed;
}

.type-form-field-textarea {
  min-height: 120px;
  resize: vertical;
  font-family: inherit;
}

.type-form-field-help {
  font-size: 12px;
  color: var(--color-text-secondary);
  margin: 0;
  line-height: 1.4;
}

/* View Mode Styles */
.type-form-view .type-form-field-input {
  border-color: transparent;
  background: transparent;
  padding-left: 0;
}

.type-form-view .type-form-field-label {
  color: var(--color-text-secondary);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

/* ==================== Tags Input ==================== */
.type-form-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 8px;
  border: 2px solid var(--color-border-medium);
  border-radius: 8px;
  min-height: 44px;
  background: var(--color-bg-base);
}

.type-form-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 8px 4px 12px;
  background: var(--color-blue-bg);
  color: var(--color-blue);
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
}

.type-form-tag-remove {
  width: 16px;
  height: 16px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--color-blue);
  transition: background 0.2s ease;
}

.type-form-tag-remove:hover {
  background: rgba(37, 99, 235, 0.2);
}

.type-form-tag-input {
  flex: 1;
  min-width: 120px;
  border: none;
  outline: none;
  background: transparent;
  font-size: 14px;
  padding: 4px;
}

/* ==================== Schema Editor ==================== */
.type-form-schema-editor {
  position: relative;
}

.type-form-schema-textarea {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  line-height: 1.6;
  min-height: 200px;
  background: var(--color-bg-secondary);
  border-color: var(--color-border-medium);
}

/* ==================== Sticky Footer ==================== */
.type-form-footer {
  padding: 16px 32px;
  background: var(--color-bg-elevated);
  border-top: 1px solid var(--color-border-light);
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
}

.type-form-footer-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.type-form-dirty-indicator {
  display: none;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--color-text-secondary);
}

.type-form-dirty-indicator.visible {
  display: flex;
}

.type-form-dirty-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-amber);
}

.type-form-footer-right {
  display: flex;
  gap: 12px;
}

/* ==================== Bulk Actions Bar ==================== */
.bulk-actions-bar {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 24px;
  background: var(--color-text-primary);
  color: white;
  border-radius: 12px;
  box-shadow: var(--shadow-2xl);
  z-index: 100;
  animation: slideUp 0.3s ease;
}

.bulk-selection-count {
  font-size: 14px;
  font-weight: 600;
}

.bulk-actions-bar .btn {
  background: rgba(255, 255, 255, 0.15);
  color: white;
  border: 1px solid rgba(255, 255, 255, 0.3);
}

.bulk-actions-bar .btn:hover {
  background: rgba(255, 255, 255, 0.25);
}

/* ==================== Responsive ==================== */
@media (max-width: 768px) {
  .type-form-content {
    padding: 16px;
  }

  .type-form-section {
    padding: 16px;
  }

  .type-form-footer {
    flex-direction: column;
    gap: 12px;
  }

  .type-form-footer-left,
  .type-form-footer-right {
    width: 100%;
    justify-content: center;
  }
}
```

---

## Navigation Routes (app.js updates)

Add these routes to the `navigate()` function:

```javascript
async navigate(view, params = {}) {
  switch (view) {
    // ... existing cases

    // ==================== Analysis Types ====================
    case 'analysis-types-list':
      await this.renderAnalysisTypesList(appContainer);
      break;

    case 'analysis-type-view':
      await this.renderAnalysisTypeView(appContainer, params.id);
      break;

    case 'analysis-type-create':
      await this.renderAnalysisTypeForm(appContainer, 'create');
      break;

    case 'analysis-type-edit':
      await this.renderAnalysisTypeForm(appContainer, 'edit', params.id);
      break;

    case 'analysis-type-ai-create':
      await this.openAICreateModal('analysis');
      break;

    // ==================== Source Types ====================
    case 'source-types-list':
      await this.renderSourceTypesList(appContainer);
      break;

    case 'source-type-view':
      await this.renderSourceTypeView(appContainer, params.id);
      break;

    case 'source-type-create':
      await this.renderSourceTypeForm(appContainer, 'create');
      break;

    case 'source-type-edit':
      await this.renderSourceTypeForm(appContainer, 'edit', params.id);
      break;

    case 'source-type-ai-create':
      await this.openAICreateModal('source');
      break;
  }
}
```

---

## Dashboard Updates

Update the Dashboard component to include Source Types management:

```javascript
// Dashboard.js - renderQuickActions()

<button class="quick-action-item" data-action="manage-analysis-types">
  <div class="quick-action-icon">
    <svg>...</svg>
  </div>
  <div class="quick-action-content">
    <div class="quick-action-label">Analysis Types</div>
    <div class="quick-action-desc">Manage analysis templates</div>
  </div>
</button>

<button class="quick-action-item" data-action="manage-source-types">
  <div class="quick-action-icon">
    <svg>...</svg>
  </div>
  <div class="quick-action-content">
    <div class="quick-action-label">Source Types</div>
    <div class="quick-action-desc">Manage input types</div>
  </div>
</button>
```

Update event handlers:

```javascript
// Dashboard.js - attachEventHandlers()

case 'manage-analysis-types':
  this.eventBus.emit('navigate', 'analysis-types-list');
  break;

case 'manage-source-types':
  this.eventBus.emit('navigate', 'source-types-list');
  break;
```

---

## Implementation Checklist

### Phase 2.1: Foundation Components (4-6 hours)

- [ ] **Modal Component** (`Modal.js` + `modal.css`)
  - [ ] Create Modal class with open/close methods
  - [ ] Implement overlay with backdrop blur
  - [ ] Add keyboard (Escape) and click-outside handling
  - [ ] Create size variants (small, medium, large)
  - [ ] Test modal stacking (if needed)

- [ ] **Update API Client** (`api.js`)
  - [ ] Add Analysis Types CRUD methods
  - [ ] Add Source Types CRUD methods
  - [ ] Implement JSON Patch format for updates
  - [ ] Add bulk operations methods
  - [ ] Test all endpoints

### Phase 2.2: AI Create Modal (4-6 hours)

- [ ] **AICreateTypeModal Component** (`AICreateTypeModal.js`)
  - [ ] Create base class with three-step flow
  - [ ] Implement Step 1: Input form with validation
  - [ ] Implement Step 2: Loading state
  - [ ] Implement Step 3: Preview and edit
  - [ ] Add collapsible "Additional Context" section
  - [ ] Add expandable examples section
  - [ ] Create separate variants for Analysis and Source types
  - [ ] Wire up to API (ai-suggest endpoints)
  - [ ] Handle regeneration flow

### Phase 2.3: Type Form View (6-8 hours)

- [ ] **TypeFormView Component** (`TypeFormView.js` + `type-management.css`)
  - [ ] Create full-page form layout
  - [ ] Implement View mode (read-only)
  - [ ] Implement Create mode (empty form)
  - [ ] Implement Edit mode (pre-filled form)
  - [ ] Add form sections: Basic Info, Schema, Instructions
  - [ ] Implement tags input with add/remove
  - [ ] Add JSON schema editor (textarea with syntax highlighting)
  - [ ] Implement dirty state tracking
  - [ ] Add unsaved changes warning (beforeunload)
  - [ ] Create sticky footer with actions
  - [ ] Add inline validation
  - [ ] Test responsive behavior

### Phase 2.4: Analysis Types Manager (4-6 hours)

- [ ] **AnalysisTypesManager Component** (`AnalysisTypesManager.js`)
  - [ ] Create list view with type cards
  - [ ] Add search/filter functionality
  - [ ] Implement card actions (View, Edit, Delete)
  - [ ] Add bulk selection with checkboxes
  - [ ] Create bulk actions bar
  - [ ] Wire up "Create Type" button
  - [ ] Wire up "AI Create" button (opens modal)
  - [ ] Implement delete confirmation
  - [ ] Handle empty state
  - [ ] Integrate TypeFormView for Create/Edit/View

### Phase 2.5: Source Types Manager (4-6 hours)

- [ ] **SourceTypesManager Component** (`SourceTypesManager.js`)
  - [ ] Clone AnalysisTypesManager structure
  - [ ] Update API calls to source types endpoints
  - [ ] Update badge styling (Input vs Analysis)
  - [ ] Update AI Create modal variant
  - [ ] Test all CRUD operations

### Phase 2.6: Integration & Testing (3-4 hours)

- [ ] **Dashboard Updates**
  - [ ] Add "Manage Source Types" quick action
  - [ ] Add Source Types metrics card
  - [ ] Update navigation event handlers

- [ ] **App.js Navigation**
  - [ ] Add all new routes (10 routes total)
  - [ ] Implement route rendering functions
  - [ ] Test navigation flow
  - [ ] Test back button behavior

- [ ] **End-to-End Testing**
  - [ ] Test Analysis Types CRUD flow
  - [ ] Test Source Types CRUD flow
  - [ ] Test AI Create for both types
  - [ ] Test bulk operations
  - [ ] Test responsive behavior
  - [ ] Test keyboard navigation
  - [ ] Test form validation
  - [ ] Test unsaved changes warning

---

## Testing Scenarios

### Scenario 1: Create Analysis Type Manually
1. Navigate to Dashboard
2. Click "Manage Analysis Types"
3. Click "Create Type" button
4. Fill in Name: "Financial Analysis"
5. Fill in Description: "Extract financial metrics from reports"
6. Add tags: "financial", "quarterly"
7. Edit JSON schema
8. Click "Save"
9. Verify redirect to list view
10. Verify new type appears in grid

### Scenario 2: AI Create Source Type
1. Navigate to Dashboard
2. Click "Manage Source Types"
3. Click "AI Create" button
4. Modal opens with form
5. Enter Goal: "Process customer support tickets"
6. Enter Audience: "Customer success team"
7. Expand "Additional Context" (optional)
8. Click "Generate"
9. See loading spinner
10. Preview AI suggestions
11. Edit name/description if needed
12. Click "Create Type"
13. Modal closes, type appears in list

### Scenario 3: Edit Existing Type
1. Navigate to type management list
2. Find type card
3. Click "Edit" icon
4. Full-page form opens with data
5. Modify description
6. Form shows dirty indicator
7. Click "Save"
8. See success toast
9. Redirect to list view
10. Changes reflected

### Scenario 4: Bulk Delete
1. Navigate to type management list
2. Select multiple types (checkboxes)
3. Bulk actions bar appears
4. Click "Delete Selected"
5. Confirmation dialog
6. Confirm deletion
7. Types removed from list
8. Success toast appears

### Scenario 5: View Mode (Read-Only)
1. Navigate to type management list
2. Click "View" icon on a card
3. Full-page view opens
4. All fields are read-only
5. No Save button visible
6. Click "Edit" button (if in view mode)
7. Switch to edit mode

---

## Success Criteria

### Must Have (P0)
- ✅ Full CRUD operations for Analysis Types (Create, Read, Update, Delete)
- ✅ Full CRUD operations for Source Types (Create, Read, Update, Delete)
- ✅ AI Create modal with proper UX (separate for each type)
- ✅ Full-page form views (NOT modals)
- ✅ View/Edit modes clearly distinguished
- ✅ Bulk operations (select multiple, delete)
- ✅ Dirty state tracking with unsaved changes warning
- ✅ Responsive design (desktop, tablet, mobile)

### Should Have (P1)
- Search/filter in list views
- Tags input with autocomplete
- JSON schema validation
- Inline form validation
- Success/error toast notifications
- Loading states during API calls
- Empty states with call-to-action

### Nice to Have (P2)
- Duplicate type action
- Export/import types
- Type usage statistics ("Used in X analyses")
- Bulk edit
- Drag-and-drop reordering
- Keyboard shortcuts (Ctrl+S to save, Escape to cancel)

---

## Files Summary

| File | Status | Lines | Purpose |
|------|--------|-------|---------|
| `wwwroot/js/components/Modal.js` | CREATE | ~200 | Reusable modal foundation |
| `wwwroot/js/components/AICreateTypeModal.js` | CREATE | ~400 | AI Create interface |
| `wwwroot/js/components/TypeFormView.js` | CREATE | ~500 | Full-page CRUD form |
| `wwwroot/js/components/AnalysisTypesManager.js` | CREATE | ~600 | Analysis Types CRUD |
| `wwwroot/js/components/SourceTypesManager.js` | CREATE | ~600 | Source Types CRUD |
| `wwwroot/css/modal.css` | CREATE | ~350 | Modal and AI Create styles |
| `wwwroot/css/type-management.css` | CREATE | ~450 | Full-page form styles |
| `wwwroot/js/api.js` | UPDATE | +200 | Add CRUD methods |
| `wwwroot/js/app.js` | UPDATE | +300 | Add navigation routes |
| `wwwroot/js/components/Dashboard.js` | UPDATE | +50 | Add Source Types link |
| **Total** | | **~3650** | **10 files** |

---

## Next Session Instructions

To continue this work in a future session, provide this command:

```
Continue the work defined in docs/PHASE2-TYPES-MANAGEMENT.md
```

I will:
1. Read this document
2. Check current implementation status
3. Start with Phase 2.1 (Foundation Components)
4. Follow the checklist sequentially
5. Mark items as completed using TodoWrite
6. Test each component before moving to next phase

---

## Design Principles to Follow

### 1. Zero-Wizard Philosophy
- No multi-step wizards for basic CRUD
- Direct access to all form fields
- Clear, immediate actions

### 2. Evidence-First
- Show validation errors inline
- Preview AI suggestions before accepting
- Clear visual feedback for all actions

### 3. Enterprise-Grade UX
- Full-page comfortable layouts (NOT cramped modals)
- Professional form design with proper spacing
- Clear visual hierarchy and typography
- Responsive across all devices

### 4. Koan Framework Alignment
- Use EntityController<> capabilities fully
- Leverage JSON Patch for updates
- Support bulk operations
- Follow REST conventions

---

**End of Document**
