# Meridian UX Specification

**Status:** ✅ CANONICAL - Approved User Experience North Star
**Version:** 1.0
**Date:** October 2025
**Authority:** This document defines the official UX vision for Meridian. All implementation and design decisions should align with this specification.

---

## Vision Statement

**Meridian is an evidence-driven living intelligence platform** that transforms chaotic vendor documents into transparent, trustworthy, continuously-evolving insights. Built on Koan Framework principles, it embodies:

- **Transparency over Magic**: Every AI decision is traceable and explainable
- **Flow over Process**: Continuous workspace evolution vs. rigid pipeline constraints
- **Evidence over Assertions**: Source-linked insights with visible confidence
- **Simplicity over Scaffolding**: Minimal UI complexity, maximum capability
- **Entity-First Thinking**: Analysis as a living object, not a workflow

---

## I. Core UX Paradigm: Living Intelligence Workspace

### Mental Model Shift

| Traditional Pipeline UX | Meridian Living Intelligence |
|------------------------|------------------------------|
| Step 1 → 2 → 3 → Done | Continuous evolution, never "done" |
| Upload once, process once | Add/remove documents anytime |
| Black-box AI extraction | Transparent provenance for every value |
| Single analysis view | Multi-perspective cloning (Security/Financial/Technical) |
| Generic confidence % | Evidence-linked, source-aware insights |
| User fixes errors manually | Authoritative Notes override with AI understanding |

### The Workspace Metaphor

Think **Notion workspace** meets **document intelligence**, not a traditional ETL pipeline.

```
Users interact with a LIVING ANALYSIS that:
- Accepts new documents at any time
- Continuously updates insights as documents process
- Shows real-time evidence for every extracted value
- Allows natural language overrides via Authoritative Notes
- Can be cloned to view through different analytical lenses
- Never reaches "done" - remains editable and evolving
```

---

## II. Koan Framework Alignment

### 1. "Reference = Intent" → Visible Affordances

**Principle**: Adding a package reference automatically enables functionality
**UX Translation**: Every capability is immediately apparent; no hidden features

```
✅ Good: Prominent "Clone to [Security | Financial | Technical]" always visible
❌ Bad: Hidden "Clone" option buried in menu
```

### 2. "Entity-First" → Analysis-Centric Interface

**Principle**: `Todo.Get(id)`, `todo.Save()` patterns
**UX Translation**: Analysis is the hero object; UI orbits around its current state

```
Interface Structure:
┌─────────────────────────────────────┐
│ Analysis: CloudCorp Assessment      │ ← The Entity
│ ├─ Documents (supporting)           │
│ ├─ Insights (derived state)         │
│ └─ Actions (operations on entity)   │
└─────────────────────────────────────┘
```

### 3. "Evidence over Magic" → Transparent AI Decisions

**Principle**: Show capabilities, provider elections, boot reports
**UX Translation**: Every insight shows source, confidence, alternatives

**Implementation**: Inline evidence preview with expand-in-place (no modals)

### 4. "Minimal Scaffolding" → Zero-Wizard Philosophy

**Principle**: No elaborate setup; start coding immediately
**UX Translation**: Create analysis → Start adding documents. No 7-step wizard.

---

## III. Primary Interface: Evidence-First Workspace

### Layout Architecture

The interface prioritizes **insights with inline evidence** using hierarchical disclosure.

```
┌────────────────────────────────────────────────────────────────────┐
│ ◄ Analyses    CloudCorp Vendor Assessment              Share ▼    │
│                Enterprise Architecture Review                       │
│                Updated 2 min ago • 4 documents • 12 insights        │
├────────────────────────────────────────────────────────────────────┤
│                                                                     │
│ ╭─ Quick Actions ─────────────────────────────────────────────╮   │
│ │ [+ Add Document]  [Clone to ▼]  [Export Report]  [@ Notes] │   │
│ ╰──────────────────────────────────────────────────────────────╯   │
│                                                                     │
│ ╔═════════════════════════════════════════════════════════════╗   │
│ ║ 🔆 Authoritative Notes                                      ║   │
│ ║ "Revenue confirmed at $51.3M per CFO call Nov 15..."        ║   │
│ ║ 3 fields overridden • Last edited by Sarah, 10 min ago     ║   │
│ ╚═════════════════════════════════════════════════════════════╝   │
│                                                                     │
│ ╭─ Key Insights ──────────────────────────────────────────────╮   │
│ │                                                              │   │
│ │ ┌──────────────────────────────────────────────────────┐    │   │
│ │ │ Annual Revenue                                  ⭐   │    │   │
│ │ │ $51.3M                                               │    │   │
│ │ │                                                      │    │   │
│ │ │ ⎯⎯⎯⎯⎯ FROM AUTHORITATIVE NOTES ⎯⎯⎯⎯⎯             │    │   │
│ │ │ "Revenue confirmed at $51.3M per CFO call..."        │    │   │
│ │ │                                                      │    │   │
│ │ │ ▼ Document said $47.2M (overridden)                  │    │   │
│ │ │   Q3_Financial.pdf, Page 3 • 97% confidence          │    │   │
│ │ │   [Use this instead]                                 │    │   │
│ │ └──────────────────────────────────────────────────────┘    │   │
│ │                                                              │   │
│ │ ┌──────────────────────────────────────────────────────┐    │   │
│ │ │ Employee Count                                  ✓    │    │   │
│ │ │ 475                                                  │    │   │
│ │ │                                                      │    │   │
│ │ │ ⎯⎯⎯⎯⎯ FROM DOCUMENT ⎯⎯⎯⎯⎯                         │    │   │
│ │ │ ████████ 94% confident                               │    │   │
│ │ │ Vendor_Prescreen.pdf, Page 2                         │    │   │
│ │ │ "Current headcount stands at 475..."                 │    │   │
│ │ │                                                      │    │   │
│ │ │ ▼ 2 other mentions (all agree)                       │    │   │
│ │ └──────────────────────────────────────────────────────┘    │   │
│ ╰──────────────────────────────────────────────────────────────╯   │
│                                                                     │
│ ╭─ Documents (4) ──────────────────────────────────────────────╮   │
│ │ Drop documents here or click to browse (always active)       │   │
│ │ 📄 vendor-assessment.pdf    ✓ 5 insights                    │   │
│ │ 📄 financial-statement.pdf  ✓ 8 insights                    │   │
│ │ 📄 security-audit.pdf       ⟳ Processing (45%)              │   │
│ ╰──────────────────────────────────────────────────────────────╯   │
│                                                                     │
│ [Quality Dashboard ▼] Citation: 95% • Confidence: 88% high          │
└────────────────────────────────────────────────────────────────────┘
```

### Key UX Innovations

1. **Evidence-Inline Pattern**: No modals/drawers - evidence expands in place
2. **Hierarchical Disclosure**: Value → Source → Alternatives (progressive complexity)
3. **Visual Hierarchy**: Notes (gold) > Conflicts (amber) > Standard (white/gray)
4. **Provenance Always Visible**: Document name + page always shown
5. **Action Proximity**: [Override] [Use different] buttons at context
6. **Progressive Complexity**: Simple cases show minimal UI; conflicts auto-expand

---

## IV. Core Features

### A. Authoritative Notes (Premium Override)

**Purpose**: Allow users to provide information that unconditionally overrides all document extractions

**UX Pattern**: Natural language input with AI-powered field matching

```
┌────────────────────────────────────────────────────────────┐
│ @ Authoritative Notes                            [Edit]   │
├────────────────────────────────────────────────────────────┤
│ "We spoke with the CFO on November 15th. Revenue is now   │
│  $51.3M (up from Q3 report). They've grown to 475         │
│  employees. Support is 24/7 per contract."                │
│                                                            │
│ ⭐ 3 fields overridden:                                   │
│ • Annual Revenue: $51.3M (was $47.2M from docs)           │
│ • Employee Count: 475 (matches docs ✓)                    │
│ • Support Hours: 24/7 (was "Business hours")              │
│                                                            │
│ Last edited by Sarah Chen, 10 minutes ago                  │
└────────────────────────────────────────────────────────────┘
```

**Key Behaviors**:
- Natural language - no syntax to learn
- Live field matching feedback (shows what matched as you type)
- Gold visual treatment (#FFFBEB background, #F59E0B border)
- Star icon (⭐) indicates override status
- Auto-saves continuously
- Explicit confirmation required for reprocessing after edits

**Reference**: See `AUTHORITATIVE-NOTES-PROPOSAL.md` for complete technical specification

### B. Clone to Multi-Perspective Analysis

**Purpose**: Instantly view same vendor through different analytical lenses

**Concept**: Same documents, different extraction schemas (Security, Financial, Technical, etc.)

```
User clicks "Clone to ▼" dropdown:

┌────────────────────────────────────────────────────┐
│ Clone to Different Analysis Type                   │
├────────────────────────────────────────────────────┤
│ Current: Enterprise Architecture Review            │
│ 4 documents • 12 fields                            │
│                                                    │
│ ◉ Security Risk Assessment                         │
│   Focus: Certifications, vulnerabilities           │
│   Estimated: 18 fields, 2-3 min                   │
│                                                    │
│ ○ Financial Due Diligence                          │
│   Focus: Revenue, growth, margins                  │
│   Estimated: 24 fields, 3-4 min                   │
│                                                    │
│ ○ Technical Capability Matrix                      │
│   Focus: Tech stack, scalability                  │
│   Estimated: 15 fields, 2 min                     │
│                                                    │
│ [Cancel]              [Create Clone]               │
└────────────────────────────────────────────────────┘
```

**Value Proposition**:
- Same vendor, 3+ perspectives in minutes
- Zero re-upload (documents linked, not copied)
- Parallel analysis (different teams work simultaneously)
- Consistent notes (option to inherit or start fresh)

### C. Quality Dashboard

**Purpose**: Self-reporting analysis health (Koan's "boot report" pattern)

**Metrics Displayed**:
- **Citation Coverage**: % of fields with source evidence
- **Confidence Distribution**: High (>90%), Medium (70-90%), Low (<70%)
- **Source Diversity**: Which documents contributed insights
- **Conflict Resolution**: Auto-resolved vs. manual review needed
- **Processing Performance**: Time breakdown, cache hit rates

```
┌───────────────────────────────────────────────────────────┐
│ Analysis Quality Report                                   │
├───────────────────────────────────────────────────────────┤
│ Overall Score: 92% (Excellent)                            │
│ ████████████████████████░░                                │
│                                                           │
│ Citation Coverage:        95%  ████████████████░░         │
│ Confidence Distribution:  88% high, 10% medium, 2% low    │
│ Source Diversity:         4 documents used                │
│ Conflict Resolution:      2 conflicts, 2 auto-resolved    │
│                                                           │
│ [Export Report]  [Improve Low-Confidence Fields]          │
└───────────────────────────────────────────────────────────┘
```

**Enterprise Value**: Audit trails, quality gates, trust building

### D. Evidence-Inline Expansion

**Pattern**: Hierarchical disclosure without modal disruption

**Interaction Flow**:
1. **Collapsed**: Value + one-line source reference visible
2. **Expanded**: Full passage shown, alternatives revealed
3. **Action**: User can override or select different source

```
Collapsed State:
┌────────────────────────────────────┐
│ Revenue: $51.3M                    │
│ From: Financial_2023.pdf, Page 3   │
│ [▼ Expand evidence]                │
└────────────────────────────────────┘

Expanded State:
┌────────────────────────────────────┐
│ Revenue: $51.3M                    │
│                                    │
│ FROM DOCUMENT ─────────────        │
│ Financial_2023.pdf, Page 3         │
│ ████████ 94% confident             │
│                                    │
│ "Total revenue for fiscal year     │
│  2023 was $51.3 million,           │
│  representing..."                  │
│                                    │
│ ▼ 2 other sources ─────────        │
│ Questionnaire.pdf: "~$45-50M"      │
│   ███████░ 72% (less specific)     │
│                                    │
│ Meeting_Notes.txt: "$51M approx"   │
│   ████░░░░ 68% (informal)          │
│                                    │
│ [Override with Notes] [▲ Collapse] │
└────────────────────────────────────┘
```

**Key Principle**: Context is never lost - user sees value AND evidence together

---

## V. Interaction Patterns

### Pattern 1: Zero-Wizard Onboarding

```
Traditional (7 steps):          Meridian (continuous):
─────────────────────          ───────────────────────
Step 1: Name analysis           Start typing name →
Step 2: Choose type            → Auto-suggests type from keywords
Step 3: Upload docs            → Drag documents directly
Step 4: Configure              → Processing starts immediately
Step 5: Process                → Insights appear as ready
Step 6: Review                 → Edit/override inline
Step 7: Done                   → Analysis is living, never "done"

5 minutes, 7 clicks             30 seconds to first insight
```

### Pattern 2: Confidence as Visual Weight

```
❌ Numbers alone:               ✅ Visual hierarchy:
─────────────                  ──────────────────

"Revenue: $51.3M                Revenue:  $51.3M
 Confidence: 94%"                ████████████████ 94%

"CEO: Jane Smith                CEO:  Jane Smith
 Confidence: 67%"                ████████░░░░░░░░ 67%
                                 ⚠ Consider verifying

"Employees: 475                 Employees:  475
 Confidence: 41%"                ████░░░░░░░░░░░░ 41%
                                 ⚠️ Low confidence
```

**Principle**: Visual weight = instant signal quality (no cognitive load)

### Pattern 3: Smart Defaults + Easy Override

**System Auto-Resolves When Possible**:
- 2 sources agree → Use majority (show in UI)
- Sources conflict → Use highest confidence + newest (show reasoning)
- No source → Mark as "Not found" (suggest adding document)

**User Overrides When Needed**:
- Click value → Edit inline → Creates Authoritative Note entry
- Or use @ Notes field for batch overrides

### Pattern 4: Continuous Processing

**Traditional**: Upload all → Click "Process" → Wait → Review → Done

**Meridian**:
- Drop document → Processing starts immediately in background
- Insights update live as extraction completes
- User can add notes, clone, or review while processing continues
- No "locked" states - analysis always editable

---

## VI. Information Architecture

### Navigation Model

```
┌─────────────────────────────────────────────────────────┐
│ [≡] MERIDIAN    [Search analyses...]        [+ New]  👤 │
├─────────────────────────────────────────────────────────┤
│ Left Sidebar (collapsible):                            │
│                                                         │
│ 📊 Active Analyses (4)                                  │
│   ● CloudCorp Architecture                              │
│   ● AWS Migration                                       │
│   ○ Security Vendor Comparison                          │
│                                                         │
│ 🔗 Related Clones                                       │
│   CloudCorp Architecture                                │
│     ├─ Security (cloned 2 days ago)                     │
│     └─ Financial (cloned 1 week ago)                    │
│                                                         │
│ 📁 All Analyses                                         │
│ ⭐ Starred                                              │
│ 🗂 By Type                                              │
│ 🕐 Recent                                               │
│                                                         │
│ [Main workspace shows selected analysis →]              │
└─────────────────────────────────────────────────────────┘
```

**Hierarchy**: Flat navigation (2 levels max)
- Home: List of analyses
- Analysis: Single-page workspace (no sub-pages)

**Principle**: Minimal scaffolding = shallow navigation

---

## VII. Visual Design System

### Color Semantics

| Color | Hex | Meaning | Usage |
|-------|-----|---------|-------|
| **Gold** | #F59E0B | Authoritative | Notes, user overrides, star ratings |
| **Blue** | #2563EB | Primary actions | Create, Process, buttons |
| **Green** | #059669 | Verified/High confidence | >90% confidence, checkmarks |
| **Amber** | #D97706 | Attention needed | Conflicts, medium confidence |
| **Red** | #DC2626 | Error/Critical | Low confidence, errors |
| **Gray** | #6B7280 | Secondary | Document sources, metadata |

**Philosophy**: Color conveys meaning, not decoration

### Typography Hierarchy

```
Analysis Title:   32px/40px Semibold  (Entity name)
Section Header:   20px/28px Semibold  (Grouping)
Field Value:      28px/36px Bold      (The data)
Field Label:      16px/24px Medium    (Context)
Evidence Text:    14px/20px Italic    (Source)
Metadata:         12px/16px Regular   (Timestamps)
```

**Principle**: Value is hero, everything else supports

### Spacing System

- **Card padding**: 20px
- **Section gaps**: 24px
- **Label-to-value**: 16px
- **Evidence spacing**: 12px before, 8px within
- **Action button spacing**: 8-12px horizontal gaps

**Principle**: Generous whitespace for clarity

---

## VIII. Responsive Strategy

### Breakpoints

| Device | Width | Strategy |
|--------|-------|----------|
| **Desktop** | >1200px | Full workspace with inline evidence |
| **Tablet** | 768-1200px | Stacked sections, evidence in accordions |
| **Mobile** | <768px | Tab-based navigation, one section at a time |

### Mobile View

```
┌───────────────────────┐
│ CloudCorp Assessment  │
│ ⭐ Notes  Insights  Docs│ ← Tabs
├───────────────────────┤
│ [Currently: Insights] │
│                       │
│ Revenue               │
│ $51.3M  ⭐           │
│ From Notes            │
│ [Tap to expand]       │
│                       │
│ Employee Count        │
│ 475  ✓                │
│ 94% confident         │
│ [Tap to expand]       │
│                       │
│ ↓ Swipe for more      │
└───────────────────────┘
```

**Principle**: Feature parity, not pixel parity

---

## IX. Success Metrics

### User Experience Metrics

| Metric | Target | Rationale |
|--------|--------|-----------|
| **Time to First Insight** | <30 seconds | Zero-wizard effectiveness |
| **Evidence Discovery** | <2 clicks | Inline pattern adoption |
| **Notes Adoption** | >40% of analyses | Override feature value |
| **Clone Usage** | >30% create clones | Multi-perspective value |
| **Return Rate** | >60% weekly active | "Living" vs. "one-time" |

### Koan Alignment Metrics

| Principle | UX Metric | Target |
|-----------|-----------|--------|
| **Transparency** | % users who expand evidence | >70% |
| **Minimal Scaffolding** | Avg. clicks to create analysis | <5 |
| **Entity-First** | % understanding "living" model | >80% |
| **Evidence over Magic** | Trust score (survey) | >8/10 |

---

## X. Implementation Roadmap

### Phase 1: Foundation (Weeks 1-4)
- Core workspace layout
- Evidence-inline insight cards
- Document manager (add/remove anytime)
- Quality dashboard v1
- Zero-wizard creation

**Success**: Create analysis, see insights with sources

### Phase 2: Living Features (Weeks 5-8)
- Real-time document processing
- Inline evidence expansion/collapse
- Conflict resolution UI
- Version history

**Success**: Analysis updates without reprocessing

### Phase 3: Clone & Notes (Weeks 9-12)
- Clone to multi-perspective
- Authoritative Notes with live matching
- Note override visualization
- Collaboration features

**Success**: >30% clone, >40% use notes

### Phase 4: Polish & Scale (Weeks 13-16)
- Mobile responsive optimization
- Export templates
- Integration hooks (Slack, webhooks)
- Performance (<2s insight render)
- Accessibility (WCAG AAA)

**Success**: Production-ready enterprise deployment

---

## XI. Related Documentation

### Technical Specifications
- **`AUTHORITATIVE-NOTES-PROPOSAL.md`**: Complete technical spec for notes override feature
- **`MERIDIAN_EXPLAINED.md`**: Narrative explanation of RAG-based architecture
- **`PROPOSAL.md`**: Technical proposal and system architecture
- **`ARCHITECTURE.md`**: Detailed system design

### Implementation Guides
- **`GETTING_STARTED.md`**: Developer onboarding
- **`TESTING.md`**: Testing strategies and scenarios

### Project Management
- **`PROJECT_STATUS_REPORT.md`**: Implementation status

---

## XII. Design Principles Summary

### The Meridian Promise

1. **Transparency**: Every insight traces to source with visible confidence
2. **Simplicity**: Zero wizards, minimal clicks, natural language
3. **Entity-Centric**: Analysis is living object, continuously evolving
4. **Evidence-First**: Citations aren't hidden - they're the foundation
5. **Progressive Disclosure**: Simple by default, powerful when needed

### Experience Guarantees

**For Analysts**: Time analyzing, not wrestling with tools
**For Executives**: Trust backed by transparent provenance
**For Teams**: Real-time collaboration on living intelligence
**For Enterprises**: Scale evaluation without scaling headcount

---

**This is Meridian: Where evidence meets intelligence, and simplicity meets power.**

---

**Document Status**: ✅ CANONICAL
**Last Updated**: October 2025
**Next Review**: Upon completion of Phase 1 implementation
**Maintained By**: Product & UX Leadership
