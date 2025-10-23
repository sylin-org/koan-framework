# Meridian: Living Analysis Platform

**Version:** 3.0  
**Paradigm Shift:** From Linear Pipeline to Living Document  
**Date:** December 2024

---

## Executive Summary

Meridian reimagined as a **living analysis platform** where users maintain evolving vendor assessments that grow richer over time. No more step-by-step wizards - instead, a fluid workspace where documents, notes, and extractions coexist and update continuously.

### Core Mental Model Shift

**❌ OLD**: Pipeline (linear, one-time, rigid)  
**✅ NEW**: Living Analysis (evolving, collaborative, flexible)

Think Notion meets document intelligence - analyses are workspaces that teams return to, update, clone, and reference over months or years.

---

## The Living Analysis Model

```
                    ANALYSIS HUB
                         │
        ┌────────────────┼────────────────┐
        │                │                │
    DOCUMENTS        INSIGHTS         ACTIONS
    (Add anytime)    (Always fresh)   (Clone, Share)
        │                │                │
    ├─ Upload new    ├─ Live extraction ├─ Clone to new type
    ├─ Remove old    ├─ Confidence view ├─ Export snapshot
    └─ Reprocess     └─ Conflict alerts └─ Share workspace
```

### Key Principles

1. **Always Editable**: Every aspect can be modified at any time
2. **Instant Gratification**: Changes reflect immediately, processing happens in background
3. **Clone & Diverge**: One click to spawn variations with different analysis types
4. **Version Awareness**: System tracks changes but doesn't burden user with versioning
5. **Collaborative by Default**: Multiple users can contribute documents and notes

---

## Primary Interface: Analysis Workspace

```
┌────────────────────────────────────────────────────────────────┐
│ CloudCorp Vendor Assessment                          [Share] 🔗 │
│ Enterprise Architecture Review • Last updated 2 min ago        │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│ ┌─────────────┬──────────────────┬─────────────────┐          │
│ │ DOCUMENTS   │ EXTRACTED DATA   │ QUICK ACTIONS  │          │
│ │ 4 files     │ 12 fields        │                │          │
│ └─────────────┴──────────────────┴─────────────────┘          │
│                                                                │
│ ╭─ Documents & Sources ─────────────────────────────╮          │
│ │                                                   │          │
│ │ 📄 vendor-assessment.pdf          ✓ Processed    │          │
│ │ 📄 financial-statement.pdf        ✓ Processed    │          │
│ │ 📄 security-audit.pdf             ⟳ Processing   │          │
│ │ 📄 meeting-notes.txt              ✓ Processed    │          │
│ │                                                   │          │
│ │ [+ Add Documents]  [+ Add Link]  [+ Paste Text]  │          │
│ ╰───────────────────────────────────────────────────╯          │
│                                                                │
│ ╭─ Key Insights ────────────────────────────────────╮          │
│ │                                                   │          │
│ │ Company Overview                                  │          │
│ │ ├─ CloudCorp Technologies         ███ 94%        │          │
│ │ ├─ CEO: Jane Smith               ⭐ Notes        │          │
│ │ ├─ Revenue: $52.3M               ███ 89%        │          │
│ │ └─ Employees: 475                ⚠️  Conflict    │          │
│ │                                                   │          │
│ │ Technical Readiness              [Expand ▼]      │          │
│ │ Security & Compliance            [Expand ▼]      │          │
│ ╰───────────────────────────────────────────────────╯          │
│                                                                │
│ ╭─ Analysis Notes ──────────────────────────────────╮          │
│ │ ⭐ Living notes that override extractions         │          │
│ │ ┌─────────────────────────────────────────────┐  │          │
│ │ │ CEO confirmed as Jane Smith in call today   │  │          │
│ │ │ Revenue closer to $55M per Q3 update       │  │          │
│ │ │ Watch for EU expansion announcement        │  │          │
│ │ └─────────────────────────────────────────────┘  │          │
│ │ Last edited by Sarah Chen, 10 min ago           │          │
│ ╰───────────────────────────────────────────────────╯          │
│                                                                │
│ [View Report] [Export PDF] [Clone to Different Analysis Type] │
└────────────────────────────────────────────────────────────────┘
```

---

## Core Workflows

### Starting Fresh
1. Click "New Analysis"
2. Choose type (or start blank)
3. Drop documents or paste text
4. System immediately begins extraction
5. User can start adding notes while processing

### Living Updates
- Drag new document onto workspace → Auto-processes and merges
- Edit notes → Instantly reflects in insights
- Remove outdated document → Extractions update
- Change analysis type → Reprocesses with new schema

### The Clone Revolution
User clicks "Clone to Different Analysis Type":
```
┌─────────────────────────────────────────────────┐
│ Clone Analysis                                  │
├─────────────────────────────────────────────────┤
│                                                 │
│ Current: Enterprise Architecture Review         │
│ Documents: 4 files (will be reused)            │
│                                                 │
│ Select New Analysis Type:                      │
│                                                 │
│ ○ Security Risk Assessment                     │
│   Focus on vulnerabilities and compliance      │
│                                                 │
│ ● Financial Due Diligence                      │
│   Extract financial metrics and risks          │
│                                                 │
│ ○ Technical Capability Matrix                  │
│   Map technical competencies                   │
│                                                 │
│ New Analysis Name:                             │
│ [CloudCorp Financial Deep Dive     ]           │
│                                                 │
│ [Cancel]              [Create Clone]            │
└─────────────────────────────────────────────────┘
```

This creates a NEW analysis with:
- Same documents (linked, not copied)
- Different extraction schema
- Fresh insights based on new type
- Shared notes (optionally)

---

## Information Architecture (Simplified)

```
MERIDIAN
│
├─ Home Dashboard
│  ├─ Active Analyses (cards with live status)
│  ├─ Quick Start (template gallery)
│  └─ Recent Activity (team-wide feed)
│
├─ Analysis Workspace (The Core Experience)
│  ├─ Document Manager (add/remove/status)
│  ├─ Insights Panel (live extractions)
│  ├─ Notes Section (team notes)
│  └─ Action Bar (export/clone/share)
│
└─ Analysis Types (Floating Menu)
   ├─ View Current Schema
   ├─ Switch Type (reprocess)
   └─ Create Custom Type
```

No more buried settings or multi-step flows. Everything happens in the workspace.

---

## Component Innovations

### Document Drop Zone (Always Active)
```
┌─────────────────────────────────────────────┐
│ Documents (4 active, 2 processing)          │
│                                             │
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
│ ░  Drop new documents here anytime       ░ │
│ ░  or paste text directly                ░ │
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
│                                             │
│ 📄 vendor-rfp.pdf (2.1 MB)                 │
│    Added 2 min ago • Processing...         │
│                                             │
│ 📄 Updated: financial-q3.pdf (1.8 MB)      │
│    Replaced older version • Done           │
└─────────────────────────────────────────────┘
```

### Insights with Inline Editing
```
Revenue: $52.3M [89% confidence]
         └─ Click to override ─┘

[When clicked, becomes:]

Revenue: [$52.3M                    ]
         Type new value or ESC to cancel
         ⭐ This will create a note override
```

### Smart Conflict Resolution (Non-Modal)
```
Employees: ⚠️ Multiple values found
           ├─ 450 (vendor-assessment.pdf)
           ├─ 475 (financial-statement.pdf)
           └─ 500 (your notes from Sept)
           
           [Use Notes Value] [Pick Different] [Ignore]
```

---

## Why This Works Better

### Matches Real Workflow
- Teams revisit vendor assessments quarterly
- New documents arrive continuously
- Information changes (CEO replaced, revenue updated)
- Multiple stakeholders contribute insights

### Reduces Friction
- No wizard to complete
- No "restart" when adding documents
- No "locked" states during processing
- No distinction between "creation" and "editing"

### Enables New Patterns
- **Comparison Shopping**: Clone same vendor to 5 analysis types
- **Temporal Analysis**: Clone monthly to track changes
- **Team Collaboration**: Everyone adds documents/notes
- **Living Documentation**: Becomes single source of truth

---

## Mobile Experience

On mobile, the workspace adapts to tabs:

```
┌─────────────────────────────┐
│ CloudCorp Assessment        │
├─────────────────────────────┤
│ Docs | Insights | Notes |   │
├─────────────────────────────┤
│ [Currently showing Insights]│
│                             │
│ Company: CloudCorp ███      │
│ CEO: Jane Smith ⭐          │
│ Revenue: $52.3M ███         │
│ Employees: 475 ⚠️           │
│                             │
│ [Swipe for more →]          │
└─────────────────────────────┘
```

---

## Implementation Strategy

### Phase 1: Core Workspace (2 weeks)
- Analysis workspace shell
- Document manager with drag-drop
- Basic insights display
- Notes field

### Phase 2: Living Features (2 weeks)  
- Real-time document processing
- Inline editing of values
- Auto-save everything
- WebSocket updates

### Phase 3: Clone & Share (1 week)
- Clone to different type
- Public/private sharing
- Activity feed
- Collaboration features

### Phase 4: Intelligence (1 week)
- Smart conflict resolution
- Change detection
- Version diffing
- Suggested updates

---

## Success Metrics

### Engagement
- **Return Rate**: Users revisit analyses weekly (vs one-time)
- **Document Additions**: Average 3+ documents per analysis over time
- **Clone Usage**: 40% of analyses are clones
- **Team Contributions**: 2+ users per analysis

### Efficiency  
- **Time to First Insight**: <30 seconds (start adding docs immediately)
- **Clone to New Type**: <10 seconds
- **Document Update**: Instant (process in background)

---

## Core Differentiator

**Meridian is not a pipeline tool - it's a living knowledge system.**

While competitors force users through wizards to generate static reports, Meridian provides an evolving workspace where vendor intelligence grows richer over time. The "Clone to Different Analysis Type" feature is a game-changer - users can instantly see the same vendor through different lenses (security, financial, technical) without re-uploading documents.

This isn't just a UX improvement - it's a fundamental reimagining of how enterprises manage vendor intelligence.

---

**The Bottom Line**: Stop thinking pipelines. Start thinking living documents that breathe with your business.
