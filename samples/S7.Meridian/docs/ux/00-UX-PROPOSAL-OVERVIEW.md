# Meridian UX Proposal Overview

**Version:** 1.0
**Date:** October 2025
**Design Lead:** Enterprise UX Team
**Status:** Proposed

---

## Executive Summary

This UX proposal outlines a comprehensive user experience design for **Meridian**, an evidence-backed document intelligence system built on the Koan Framework. The design centers on the **Enterprise Architecture Review** scenario, where technical leaders transform hundreds of pages of vendor documentation into trusted, citation-backed deliverables in under 15 minutes.

### Design Philosophy

**Trust Through Transparency + Effortless Power**

Meridian's UX balances two critical tensions:
1. **Automation vs. Control**: AI handles the heavy lifting while humans remain in command
2. **Simplicity vs. Power**: 80% of workflows require 3 clicks, advanced features are one layer deep
3. **Speed vs. Accuracy**: Fast processing with mandatory evidence verification
4. **Complexity vs. Clarity**: Handle 100+ page documents with crystal-clear presentation

### Core Value Proposition

> "Transform 8 hours of manual document synthesis into 15 minutes of guided AI extraction + human review, with every claim traceable to source passages."

---

## Design Principles

### 1. Evidence-First Design (Critical)

**Principle**: Never show a value without showing its source.

Every piece of extracted data includes:
- **Visual confidence indicator** (████ high, ▓▓░ medium, ▓░░ low)
- **Source document reference** (hover tooltip)
- **One-click evidence drawer** (view exact passage with highlighting)
- **Alternative values** (when conflicts exist)

**Why this matters**: Enterprise decisions require defensible data. A CIO presenting to the board needs to answer "where did this number come from?" instantly.

### 2. Progressive Disclosure (Cognitive Load)

**Principle**: Show 20% of features that solve 80% of tasks.

**Three-tier information architecture**:
- **Tier 1** (Always visible): Upload → Process → Review → Finalize
- **Tier 2** (Conditional display): Conflicts panel, Evidence drawer, Quality metrics
- **Tier 3** (Advanced settings): Merge rules, Schema editing, Custom analysis types

**Example**: New users see "Upload Documents" and "Process" buttons. Power users who need custom merge rules find them in Settings, not cluttering the primary workflow.

### 3. Intelligent Defaults (Reduce Decisions)

**Principle**: 90% of users never change default settings.

**Smart defaults include**:
- Auto-classification with 90%+ confidence
- Merge strategy: HighestConfidence (works for most scenarios)
- Field extraction schema based on analysis type
- Evidence quality thresholds (flag if <70% confidence)

**Result**: First-time users can create deliverables without understanding vector search, merge strategies, or confidence scoring.

### 4. Error Prevention > Error Handling

**Principle**: The best error message is the one never shown.

**Guardrails include**:
- File size warnings (before upload fails)
- Real-time schema validation (during typing)
- Conflict detection with guided resolution
- Undo/redo for all destructive actions

**Example**: Instead of failing with "File too large", show warning at 48MB: "⚠ Approaching 50MB limit. Processing may be slow. [Upload Anyway] [Choose Smaller File]"

### 5. Accessible by Default (WCAG AAA)

**Principle**: Accessibility is not a feature, it's a requirement.

**Commitments**:
- All text meets 7:1 contrast ratio
- Full keyboard navigation (no mouse required)
- Screen reader optimized (semantic HTML + ARIA)
- Motion sensitivity support (respects prefers-reduced-motion)
- Touch targets ≥48px (mobile-friendly)

---

## Target User Personas

### Primary: Enterprise Architect (Dana)

**Profile**:
- Title: VP of Enterprise Architecture, Fortune 500 company
- Context: Evaluates 20+ vendor proposals monthly
- Pain: Spends 8 hours reading repetitive documents, manually copying data to spreadsheets
- Goal: Create 2-page executive summaries with defensible sources in <30 minutes

**Success Criteria**:
- 85-95% extraction accuracy without manual intervention
- 100% citation coverage (every claim has evidence)
- Zero questions from stakeholders about data sources
- Deliverable ready for CIO steering committee in one session

**Typical Workflow**:
1. Upload 4 vendor documents (meeting notes, technical questionnaire, security assessment, financial data)
2. Add context: "Prioritize Q3 2024 data, focus on cloud-native capabilities"
3. Process → 8 minutes automated extraction
4. Review 3 flagged conflicts → resolve with evidence drawer
5. Download PDF with citations → present to leadership

**UX Priorities**:
- **Trust**: Every number must be verifiable
- **Speed**: Complete workflow in <15 minutes
- **Professional output**: Board-ready formatting
- **Audit trail**: Full evidence chain for compliance

### Secondary: Procurement Analyst (Jordan)

**Profile**:
- Title: Senior Procurement Analyst
- Context: Manages vendor due diligence for 10-15 vendors simultaneously
- Pain: Vendors report financials in different formats, hard to compare apples-to-apples
- Goal: Create comparison matrices with normalized data

**Success Criteria**:
- Batch processing (5+ vendors at once)
- Automated data normalization (currency, dates, formats)
- Export to CSV for spreadsheet analysis
- Side-by-side vendor comparison view

**UX Priorities**:
- **Efficiency**: Batch operations, templates
- **Consistency**: Normalized data formats
- **Comparison**: Side-by-side vendor views
- **Export flexibility**: CSV, JSON, PDF

### Tertiary: Compliance Officer (Priya)

**Profile**:
- Title: Information Security Manager
- Context: Conducts cybersecurity risk assessments for third-party vendors
- Pain: Must prove to auditors that every risk rating has documented evidence
- Goal: Generate gap analysis reports with complete audit trails

**Success Criteria**:
- Full audit trail export (CSV with all evidence links)
- Quality metrics (citation coverage, confidence distribution)
- Compliance-ready PDF formatting
- Evidence preservation for 7-year retention

**UX Priorities**:
- **Traceability**: Complete audit trail
- **Quality reporting**: Metrics dashboard
- **Evidence preservation**: Archival-ready exports
- **Regulatory compliance**: SOC 2, ISO 27001 alignment

---

## Primary User Journey: Enterprise Architecture Review

**Scenario**: Dana (VP Enterprise Architecture) evaluates a cloud platform vendor for migration.

**Input**: 4 documents totaling 127 pages
- Vendor_Prescreen_Questionnaire.pdf (23 pages)
- Technical_Architecture_Bulletin.pdf (45 pages)
- Q3_2024_Financial_Statement.pdf (12 pages)
- SOC2_Security_Assessment.pdf (47 pages)

**Expected Output**: 2-page executive summary with:
- Vendor overview (company size, financial health, key contacts)
- Technical capabilities (cloud-native maturity, integration options, scalability)
- Security posture (certifications, audit findings, compliance)
- Recommendation (Go/No-Go with evidence-backed rationale)

### Journey Map

**Phase 1: Setup (2 minutes)**
```
Landing Page
    ↓
Choose "Enterprise Architecture Review" analysis type
    ↓
Enter pipeline name: "CloudCorp Platform Evaluation"
    ↓
Add context notes: "Prioritize Q3 2024 data. Focus on Kubernetes maturity."
    ↓
Continue to Upload
```

**Phase 2: Document Upload (1 minute)**
```
Upload Screen (Drag & Drop)
    ↓
Drop 4 PDF files
    ↓
Auto-classification runs
    ✓ Vendor_Prescreen → Questionnaire Response (94% confidence)
    ✓ Technical_Architecture → Technical Bulletin (91% confidence)
    ✓ Q3_2024_Financial → Financial Statement (97% confidence)
    ⚠ SOC2_Security → Security Assessment (78% confidence)
    ↓
Review low-confidence classification (SOC2)
    ↓
Confirm classification → 100% confidence
    ↓
Click "Process All Documents"
```

**Phase 3: Processing (8 minutes, automated)**
```
Processing Status Screen
    ↓
Stage 1: Extracting vendor overview data... ████░░░░░░ 40%
    ✓ Vendor_Prescreen.pdf (complete, 45s)
    ⏳ Technical_Architecture.pdf (extracting, 2m 15s elapsed)
    ⏸ Q3_2024_Financial.pdf (queued)
    ⏸ SOC2_Security.pdf (queued)
    ↓
Stage 2: Extracting financial data... ████████░░ 80%
    ✓ All documents processed
    ↓
Stage 3: Merging conflicting data... ██████████ 100%
    ⚠ 3 conflicts detected
    ↓
Processing Complete → Navigate to Review
```

**Phase 4: Review & Conflict Resolution (4 minutes)**
```
Review Screen (Split view: Field Tree | Document Preview)
    ↓
Conflicts Panel opens automatically
    ⚠ 3 fields need attention
    ↓
Conflict 1 (RED): "Primary Contact Email - No evidence found"
    → Click [Enter Manually]
    → Type: dana.martinez@cloudcorp.com
    → Reason: "From introductory call on 2024-10-15"
    → Save
    ↓
Conflict 2 (AMBER): "Employee Count - Two values with equal confidence"
    → 450 (Vendor Prescreen, p2) - 89% confidence
    → 470 (LinkedIn Company Page) - 87% confidence
    → Click evidence drawer for first option
    → See highlighted text: "Current headcount stands at 450 full-time employees as of Q3 2024"
    → Click [Choose 450]
    ↓
Conflict 3 (YELLOW): "ISO 27001 Expiry Date - Low confidence (62%)"
    → Click [View Evidence]
    → See vague passage: "ISO certification valid through Q1 2025"
    → Click [Regenerate]
    → New extraction: "2025-03-31" with 94% confidence
    → Click [Accept]
    ↓
All conflicts resolved → Green checkmark
    ↓
Review quality metrics:
    ✓ Citation coverage: 95% (38 of 40 fields)
    ✓ High confidence: 88% (35 of 40 fields)
    ✓ Conflicts: 3 (all resolved)
    ↓
Click "Finalize Deliverable"
```

**Phase 5: Deliverable Generation (1 minute)**
```
Deliverable Preview Screen
    ↓
See rendered markdown with citations:

    ## Vendor Overview
    **Company Name**: CloudCorp Technologies Inc.[^1]
    **Employee Count**: 450 full-time employees[^2]
    **Annual Revenue**: $47.2M (FY 2023)[^3]
    ...

    [^1]: Vendor_Prescreen.pdf, page 1
    [^2]: Vendor_Prescreen.pdf, page 2
    [^3]: Q3_2024_Financial.pdf, page 3
    ↓
Click [Download PDF]
    ↓
Success message: "Executive summary ready with 38 citations"
    ↓
[Open in PDF viewer] [Create Another] [Share Link]
```

**Total Time**: 16 minutes (2 setup + 1 upload + 8 processing + 4 review + 1 finalize)

**Outcome**: Board-ready 2-page summary with defensible sources, ready for CIO presentation.

---

## Information Architecture

### Site Map (Depth of Access)

```
Level 0: Landing / Dashboard
├─ Recent Pipelines (cards)
├─ Quick Start: Analysis Type Selector
└─ Global Actions: [New Pipeline] [Templates] [Settings]

Level 1: Pipeline Workflow
├─ Setup
│   ├─ Analysis Type Selection
│   ├─ Pipeline Name & Description
│   └─ Context Notes (optional)
│
├─ Upload
│   ├─ Drag & Drop Zone
│   ├─ File List with Classification
│   └─ [Process All Documents]
│
├─ Processing (Automated)
│   ├─ Progress Indicator
│   ├─ Stage-by-Stage Status
│   └─ [Cancel Processing]
│
├─ Review
│   ├─ Field Tree (left pane)
│   ├─ Document Preview (right pane)
│   ├─ Conflicts Panel (conditional)
│   └─ Quality Metrics (expandable)
│
└─ Deliverable
    ├─ Preview (markdown/PDF)
    ├─ Quality Report
    └─ Export Options

Level 2: Contextual Tools (slide-in panels)
├─ Evidence Drawer
│   ├─ Selected Value + Source
│   ├─ Highlighted Passage
│   ├─ Alternative Values
│   └─ Actions: [Regenerate] [Edit] [Override]
│
├─ Conflicts Panel
│   ├─ Triage by Severity (Red > Amber > Yellow)
│   ├─ Conflict Details
│   └─ Resolution Actions
│
└─ Quality Metrics
    ├─ Citation Coverage Chart
    ├─ Confidence Distribution
    └─ Source Diversity

Level 3: Advanced Settings (modal dialogs)
├─ Merge Rules Configuration
├─ Schema Editor
├─ Custom Analysis Types
└─ Template Management
```

### Navigation Patterns

**Primary Navigation** (always accessible):
- Global header: [Logo] [New Pipeline] [Templates] [Help] [Account]
- Breadcrumbs: Dashboard > CloudCorp Evaluation > Review
- Progress stepper: Setup → Upload → Process → Review → Deliverable

**Contextual Navigation** (appears when relevant):
- Conflicts badge (floating action button when conflicts exist)
- Evidence drawer trigger (click any field value)
- Quality metrics panel (expandable from Review screen)

**Keyboard Shortcuts** (power users):
- `Cmd/Ctrl + K`: Quick search (fields, documents, values)
- `Cmd/Ctrl + Enter`: Finalize deliverable
- `Cmd/Ctrl + E`: Open evidence drawer for selected field
- `Esc`: Close drawer/modal
- `Tab`: Navigate between fields
- `Arrow Keys`: Navigate field tree

---

## Page Inventory

| Page Name | Purpose | Access Depth | User Flow Position |
|-----------|---------|--------------|-------------------|
| **Landing Dashboard** | Entry point, recent work, quick start | Level 0 | Start |
| **Analysis Type Selector** | Choose pipeline template | Level 1 | Step 1 |
| **Pipeline Setup** | Name, description, context | Level 1 | Step 2 |
| **Document Upload** | Add files, review classification | Level 1 | Step 3 |
| **Processing Status** | Real-time progress, stage tracking | Level 1 | Step 4 (automated) |
| **Review & Resolve** | Field inspection, conflict resolution | Level 1 | Step 5 |
| **Deliverable Preview** | Final output, quality metrics | Level 1 | Step 6 |
| **Evidence Drawer** | Source passage, alternatives | Level 2 | Contextual (from Review) |
| **Conflicts Panel** | Triage, guided resolution | Level 2 | Contextual (from Review) |
| **Quality Metrics** | Citation coverage, confidence stats | Level 2 | Contextual (from Review) |
| **Settings/Templates** | Advanced configuration | Level 3 | Via global nav |

---

## Key Design Patterns

### Pattern 1: Confidence-Based Visual Hierarchy

**Problem**: Users need to quickly identify which extracted values need attention.

**Solution**: Color-coded, iconographic confidence indicators

```
████ High (90-100%)    → Green, solid bars, low visual noise
▓▓░  Medium (70-89%)   → Amber, outlined, moderate attention
▓░░  Low (0-69%)       → Red, faded, high attention
⚠    No Evidence       → Gray with warning icon
```

**Usage**:
- Appears next to every extracted field
- Hover → tooltip with explanation
- Click → evidence drawer

### Pattern 2: Evidence Drawer (Slide-In Panel)

**Problem**: Users need to verify AI decisions without losing context.

**Solution**: 480px right-side panel with:
- Source document + page number
- Highlighted passage (exact text span)
- Confidence score with reasoning
- Alternative values (if conflicts exist)
- Actions: Regenerate, Edit, Override

**Behavior**:
- Triggered by clicking any field value or confidence indicator
- Semi-transparent backdrop (click to close)
- Slide animation (300ms ease-out)
- Keyboard: Esc to close, Tab to navigate

### Pattern 3: Conflict Triage Panel

**Problem**: Multiple conflicts can overwhelm users without prioritization.

**Solution**: Auto-sorted conflict list:

```
🔴 RED (Blocker) - No evidence found → Manual entry required
🟡 AMBER (Conflict) - Multiple values → Choose one
🟠 YELLOW (Low confidence) - Weak evidence → Regenerate or override
```

**Behavior**:
- Auto-opens if conflicts detected after processing
- Sorts by severity (blockers first)
- One-click resolution for each conflict type
- Persistent until all resolved

### Pattern 4: Inline Contextual Help

**Problem**: First-time users need guidance without overwhelming power users.

**Solution**: Three-tier help system:
- **Tooltips**: Hover for 1-2 second explanation
- **Inline tips**: Collapsible "💡 Tip" boxes for first-time workflows
- **Help drawer**: Cmd+? for comprehensive guides

**Example**:
```
First upload:
┌─────────────────────────────────────────────┐
│ 💡 Tip: Meridian works best with 3-10      │
│    source documents. We'll auto-classify   │
│    each file to extract the right data.    │
│    [Got it] [Don't show again]             │
└─────────────────────────────────────────────┘
```

### Pattern 5: Progressive Quality Feedback

**Problem**: Users need confidence that AI extraction is working before committing time.

**Solution**: Real-time quality indicators:
- **During processing**: "Found 23 high-confidence citations so far"
- **After extraction**: Quality metrics dashboard (citation %, confidence distribution)
- **Before finalize**: Final checklist (all conflicts resolved, 95% citation coverage)

---

## Mobile & Responsive Strategy

### Breakpoint Philosophy

**Desktop-First Design** (Meridian is a power tool):
- Primary usage: Desktop/laptop (1024px+)
- Secondary: Tablet (640-1023px)
- Mobile: Read-only/monitoring (320-639px)

### Responsive Adaptations

**Mobile (320-639px)**: Read-only mode
- View deliverables
- Monitor processing status
- Browse recent pipelines
- No editing or conflict resolution (too complex for small screens)

**Tablet (640-1023px)**: Simplified editing
- Single-column layouts (no split panes)
- Evidence drawer becomes full-screen modal
- Conflict resolution via bottom sheet
- Simplified field tree (accordion, one section open)

**Desktop (1024px+)**: Full power
- Split-pane layouts (field tree | document preview)
- Side-by-side evidence comparison
- Keyboard shortcuts enabled
- Batch operations

### Touch Targets
- Minimum 48×48px (Material Design standard)
- Spacing: 8px minimum between interactive elements
- Swipe gestures: Drawer close, conflict dismiss

---

## Accessibility Commitment (WCAG AAA)

### Visual Accessibility

**Color Contrast**:
- All text: 7:1 minimum (AAA standard)
- Interactive elements: 4.5:1 minimum
- No color-only indicators (always paired with icons/text)

**Typography**:
- Minimum 16px body text
- Maximum 75 characters per line
- 1.5 line height for readability
- System font stack (optimized for screen readers)

### Keyboard Navigation

**Full keyboard support**:
- Tab order: Primary actions → Field tree → Evidence drawer → Conflicts
- Shortcuts: Cmd+K (search), Cmd+E (evidence), Esc (close)
- Focus indicators: 3px solid blue outline with 2px offset
- No keyboard traps (can exit all modals/drawers)

### Screen Reader Optimization

**Semantic HTML**:
- `<main>`, `<nav>`, `<article>`, `<section>` landmarks
- `<h1>`-`<h6>` hierarchy (no skipped levels)
- `<button>` for actions, `<a>` for navigation

**ARIA Labels**:
- All interactive elements have descriptive labels
- Live regions for status updates (`aria-live="polite"`)
- Progress indicators announce percentage complete

### Motion Sensitivity

**Respects `prefers-reduced-motion`**:
- Disable drawer slide animations
- Replace spinners with static loading text
- No auto-playing animations
- Instant state transitions

---

## Performance & Technical Considerations

### Page Load Performance

**Target metrics**:
- Time to Interactive (TTI): <3 seconds
- Largest Contentful Paint (LCP): <2.5 seconds
- Cumulative Layout Shift (CLS): <0.1

**Optimization strategies**:
- Lazy load document previews
- Virtualized field tree (render only visible items)
- Debounced search (300ms)
- Progressive JPEG for document thumbnails

### Real-Time Updates

**WebSocket connection for**:
- Processing progress (stage updates every 5s)
- Conflict detection (as soon as identified)
- Quality metrics (cumulative during extraction)

**Fallback**: Long polling if WebSocket unavailable

### Offline Capability

**Progressive Web App (PWA)**:
- Cache deliverables for offline viewing
- Queue uploads for when connection restored
- Service worker for asset caching

---

## Success Metrics

### Product Metrics

**Adoption**:
- 80% of users complete first deliverable within 20 minutes
- 60% return within 7 days to create second pipeline
- 40% create 5+ pipelines within first month

**Efficiency**:
- Average time from upload to finalized deliverable: <15 minutes
- 85-95% extraction accuracy without manual intervention
- 90% of conflicts resolved with one-click actions

**Trust**:
- 95%+ citation coverage across all deliverables
- 88%+ high-confidence extractions
- Zero reported cases of "unknown data source"

### UX Metrics

**Usability**:
- System Usability Scale (SUS): >80 (excellent)
- Task success rate: >95% for primary workflows
- Time on task: <30 seconds to resolve typical conflict

**Satisfaction**:
- Net Promoter Score (NPS): >50
- Customer Satisfaction (CSAT): >4.5/5
- Feature discovery: 70% find evidence drawer within first session

**Accessibility**:
- 100% WCAG AAA compliance
- Keyboard-only task completion: 100%
- Screen reader task success: >90%

---

## Implementation Roadmap

### Phase 1: Core Workflows (MVP)
**Timeline**: 8 weeks

**Deliverables**:
1. Landing Dashboard + Analysis Type Selector
2. Document Upload + Classification
3. Processing Status Screen
4. Review Screen with Field Tree
5. Evidence Drawer (basic)
6. Deliverable Preview + PDF Export

**Success criteria**: Users can complete happy-path workflow (no conflicts) end-to-end

---

### Phase 2: Conflict Resolution
**Timeline**: 4 weeks

**Deliverables**:
1. Conflicts Panel with triage
2. Enhanced Evidence Drawer (alternatives, regenerate)
3. Manual override with justification
4. Quality Metrics Dashboard

**Success criteria**: Users can resolve 90% of conflicts with one-click actions

---

### Phase 3: Advanced Features
**Timeline**: 6 weeks

**Deliverables**:
1. Batch processing (multiple pipelines)
2. Template management
3. Incremental refresh
4. Comparison mode (side-by-side vendors)

**Success criteria**: Power users adopt batch workflows, reduce time per pipeline by 40%

---

### Phase 4: Mobile & Accessibility
**Timeline**: 4 weeks

**Deliverables**:
1. Responsive tablet layouts
2. Mobile read-only mode
3. WCAG AAA compliance audit
4. Keyboard navigation polish

**Success criteria**: 100% accessibility compliance, 50% tablet adoption among mobile users

---

## Design System Alignment

### Koan Framework Ethos

**"Reference = Intent" in UX**:
- Adding a document automatically enables extraction (no manual schema setup)
- Choosing an analysis type auto-configures merge rules
- One-click actions reflect framework's auto-registration philosophy

**Entity-First Thinking**:
- Documents, Fields, Evidence are first-class UI objects
- Every entity has inspect/edit/delete actions
- Relationships visualized (Document → Passage → Field)

**Multi-Provider Transparency**:
- Users never think about MongoDB vs. Weaviate
- Same UI works regardless of storage backend
- Performance transparency (show when query is slow)

### Visual Language

**Inspired by**:
- Linear (clean, fast, keyboard-first)
- Notion (progressive disclosure, inline editing)
- Superhuman (confidence through speed, keyboard shortcuts)

**NOT inspired by**:
- Adobe Creative Suite (too complex, too many options)
- Traditional enterprise software (cluttered, overwhelming)

---

## Next Steps

### Immediate Actions

1. **Stakeholder Review** (This document)
   - Present to Koan Framework team
   - Gather feedback on alignment with technical architecture
   - Validate persona assumptions with real users

2. **Create Detailed Wireframes** (See `02-PAGE-LAYOUTS.md`)
   - High-fidelity mockups for each screen
   - Interactive prototypes in Figma
   - Component library documentation

3. **User Testing Plan**
   - Recruit 5 enterprise architects for moderated testing
   - Test critical workflows (conflict resolution, evidence verification)
   - Iterate based on feedback

4. **Design System Documentation** (See `04-DESIGN-SYSTEM.md`)
   - Component specifications
   - Typography, color, spacing scales
   - Accessibility guidelines

### Long-Term Vision

**Year 1**: Establish Meridian as the trusted document intelligence tool for enterprise teams
**Year 2**: Expand to conversational interface ("Why did you choose this value?")
**Year 3**: Multi-modal understanding (extract from charts, graphs, images)

---

## Document Index

This UX proposal is organized into the following files:

1. **00-UX-PROPOSAL-OVERVIEW.md** (This document)
   - Design philosophy, principles, personas
   - Primary user journey
   - Information architecture
   - Success metrics

2. **01-USER-JOURNEYS.md**
   - Detailed workflow diagrams
   - Decision trees
   - Edge cases and error states

3. **02-PAGE-LAYOUTS.md**
   - Wireframes for each screen
   - Component placement
   - Responsive breakpoints

4. **03-COMPONENT-LIBRARY.md**
   - Reusable component specifications
   - Interaction patterns
   - Component API

5. **04-DESIGN-SYSTEM.md**
   - Typography, color, spacing
   - Iconography
   - Illustration style

6. **05-INTERACTIONS-ANIMATIONS.md**
   - Micro-interactions
   - Transition specifications
   - Loading states

7. **06-ACCESSIBILITY-RESPONSIVE.md**
   - WCAG AAA compliance checklist
   - Keyboard navigation maps
   - Screen reader guidelines
   - Responsive design patterns

---

## Appendix: Design Decisions

### Why Evidence Drawer (vs. Inline Expansion)?

**Decision**: Use slide-in panel instead of inline expansion for evidence.

**Rationale**:
- Preserves document context (user doesn't lose place in field tree)
- Accommodates variable evidence length (some passages are 500+ words)
- Enables comparison (show multiple alternative values side-by-side)
- Familiar pattern (Gmail, Slack use similar drawers)

**Trade-off**: Requires extra click vs. hover-to-expand, but maintains cleaner visual hierarchy.

---

### Why Auto-Open Conflicts Panel?

**Decision**: Automatically open conflicts panel if conflicts detected after processing.

**Rationale**:
- Prevents "silent failures" (user downloads deliverable without realizing conflicts exist)
- Teaches new users about conflict resolution workflow
- Can be dismissed (one-time education, not persistent annoyance)

**Trade-off**: Slightly intrusive for power users who want to review fields first, but critical for data quality.

---

### Why No Manual Document Classification Toggle During Upload?

**Decision**: Auto-classification runs immediately on upload, with manual override via dropdown.

**Rationale**:
- 90%+ accuracy means most users don't need to intervene
- Showing confidence scores builds trust (user sees "94% confident this is a Questionnaire")
- Manual toggle would add decision point to critical path

**Trade-off**: Users can't pre-classify before AI runs, but saves 30 seconds for 90% of uploads.

---

**End of Overview Document**

For detailed page layouts, wireframes, and component specifications, see the accompanying documents in this UX proposal series.
