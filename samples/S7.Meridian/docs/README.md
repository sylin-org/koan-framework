# Meridian Documentation Index

**Welcome to Meridian documentation.** This index guides you to the right documents based on your role and goals.

---

## � NEW: UX Realignment Proposal (Oct 2025)

### 📋 **[UX-REALIGNMENT-INDEX.md](UX-REALIGNMENT-INDEX.md)** ⭐ START HERE

**Complete UX redesign proposal** addressing navigation inconsistencies and borrowing proven patterns from SnapVault.

**5 comprehensive documents:**

1. **Executive Summary** - Overview & key decisions (10 min)
2. **Full Proposal** - Complete design specification (30 min)
3. **Quick Reference** - Fast lookup for developers (5 min)
4. **Visual Mockups** - ASCII art layouts & flows (15 min)
5. **Implementation Guide** - Step-by-step code (25 min)

**Problems solved:**

- ✅ Fragmented navigation patterns (3 different approaches)
- ✅ Unclear hierarchy (work vs configuration)
- ✅ Context-breaking full-page jumps
- ✅ Inconsistent list/detail access

**Solutions:**

- ✅ Unified sidebar navigation (SnapVault-inspired)
- ✅ Contextual detail panels (60% width slide-ins)
- ✅ Professional dark theme (#0A0A0A, #141414)
- ✅ Consistent patterns across all entity types

👉 **[READ THE INDEX](UX-REALIGNMENT-INDEX.md)** to find the right document for your role.

---

## �🎯 Quick Navigation

### I want to understand the user experience

👉 **`UX-SPECIFICATION.md`** ✅ CANONICAL (Original Vision)

This is the **authoritative UX north star** that defines:

- Core interface patterns (evidence-inline, living workspace)
- Authoritative Notes override experience
- Clone to multi-perspective analysis
- Quality dashboard and metrics
- Koan Framework alignment principles
- Visual design system and interaction patterns

**Everyone should read this first** to understand Meridian's UX vision.

👉 **`UX-REALIGNMENT-INDEX.md`** 🎨 NEW (Redesign Proposal)

**Addresses navigation issues** identified in current implementation:

- Unified sidebar navigation
- Consistent detail panels
- SnapVault-inspired visual design
- 4-phase implementation plan

**Read this** if you're implementing the UI or reviewing the redesign.

---

### I want to understand how it works technically

👉 **`../MERIDIAN_EXPLAINED.md`**

A narrative guide that explains:

- The problem Meridian solves (document chaos → structured intelligence)
- How RAG (Retrieval-Augmented Generation) works
- Step-by-step journey through the extraction pipeline
- Evidence chains and citation tracking
- Conflict resolution strategies
- Real-world examples

**Start here** if you want to understand the "why" and "how" before diving into code.

---

### I want to build features or extend the system

👉 **`../PROPOSAL.md`**

Complete technical specification including:

- System architecture and design decisions
- Data models and entity relationships
- API endpoints and contracts
- Processing pipeline stages
- Configuration options
- Extension points

**Reference this** when implementing features or making architectural decisions.

---

### I want to implement Authoritative Notes

👉 **`AUTHORITATIVE-NOTES-PROPOSAL.md`**

Comprehensive specification covering:

- Virtual document pattern architecture
- AI-powered field matching
- Precedence-based merge logic
- UX specifications (visual treatment, editing flow)
- Prompt engineering for natural language extraction
- Testing strategy and acceptance criteria
- Implementation timeline (8-week roadmap)

**The definitive guide** for implementing the notes override feature.

---

### I want to get started as a developer

👉 **`GETTING_STARTED.md`**

Developer onboarding covering:

- Environment setup
- Running the sample locally
- Database configuration
- API exploration
- First analysis walkthrough
- Common development workflows

**Start here** if you're a new developer joining the project.

---

### I want to understand design rationale

👉 **`../ARCHITECTURE.md`**

Deep dive into:

- Design decisions and trade-offs
- Why RAG vs. alternatives
- Vector database selection
- Schema validation approach
- Performance considerations
- Scalability patterns

**For architects** evaluating or extending Meridian.

---

## 📂 Document Organization

```
samples/S7.Meridian/
│
├── README.md                      → Quick start, tutorial, learning guide
├── MERIDIAN_EXPLAINED.md          → Narrative explanation of concepts
├── PROPOSAL.md                    → Technical specification
├── ARCHITECTURE.md                → Design rationale and decisions
│
└── docs/
    ├── README.md                  → This index (you are here)
    │
    ├── UX-SPECIFICATION.md        ✅ CANONICAL UX AUTHORITY
    │   → Official user experience specification
    │   → All UX decisions reference this document
    │
    ├── AUTHORITATIVE-NOTES-PROPOSAL.md
    │   → Complete technical spec for notes override feature
    │
    ├── GETTING_STARTED.md
    │   → Developer onboarding guide
    │
    └── ux-archive/
        ├── ARCHIVED.md            → Why this folder exists
        ├── prop2/                 → Original living analysis exploration
        └── 00-04-*.md             → Historical UX iterations
```

---

## 🧭 Navigation Paths by Role

### Product Manager / Designer

1. **`UX-SPECIFICATION.md`** - Understand canonical UX vision
2. **`MERIDIAN_EXPLAINED.md`** - Learn how the system works
3. **`AUTHORITATIVE-NOTES-PROPOSAL.md`** - Deep dive on notes feature

### Frontend Developer

1. **`UX-SPECIFICATION.md`** - UI patterns and interaction design
2. **`GETTING_STARTED.md`** - Environment setup
3. **`PROPOSAL.md`** - API contracts and data models

### Backend Developer

1. **`GETTING_STARTED.md`** - Environment setup
2. **`PROPOSAL.md`** - Technical architecture
3. **`ARCHITECTURE.md`** - Design decisions
4. **`AUTHORITATIVE-NOTES-PROPOSAL.md`** - Notes implementation guide

### Enterprise Architect

1. **`UX-SPECIFICATION.md`** - User experience philosophy
2. **`MERIDIAN_EXPLAINED.md`** - System narrative
3. **`ARCHITECTURE.md`** - Technical decisions and trade-offs
4. **`PROPOSAL.md`** - Complete technical specification

### QA / Tester

1. **`MERIDIAN_EXPLAINED.md`** - System behavior understanding
2. **`UX-SPECIFICATION.md`** - Expected user flows
3. **`AUTHORITATIVE-NOTES-PROPOSAL.md`** - UAT scenarios (Appendix)
4. **`../TESTING.md`** - Testing strategies

---

## 📋 Documentation Status

| Document                            | Status        | Purpose                          |
| ----------------------------------- | ------------- | -------------------------------- |
| **UX-SPECIFICATION.md**             | ✅ CANONICAL  | Authoritative UX north star      |
| **AUTHORITATIVE-NOTES-PROPOSAL.md** | ✅ Approved   | Technical spec for notes feature |
| **MERIDIAN_EXPLAINED.md**           | ✅ Current    | Narrative technical guide        |
| **PROPOSAL.md**                     | ✅ Current    | Technical specification          |
| **ARCHITECTURE.md**                 | ✅ Current    | Design rationale                 |
| **GETTING_STARTED.md**              | ✅ Current    | Developer onboarding             |
| **ux-archive/**                     | ⚠️ Historical | Superseded UX explorations       |

---

## 🔍 Finding Specific Information

### User Experience Questions

- **Interface patterns?** → `UX-SPECIFICATION.md` Section III
- **Visual design (colors, typography)?** → `UX-SPECIFICATION.md` Section VII
- **Mobile/responsive strategy?** → `UX-SPECIFICATION.md` Section VIII
- **Success metrics?** → `UX-SPECIFICATION.md` Section IX

### Technical Questions

- **How does RAG work?** → `MERIDIAN_EXPLAINED.md` Section on RAG
- **Data models?** → `PROPOSAL.md` or `../README.md` Step 1
- **API endpoints?** → `PROPOSAL.md` API section
- **Pipeline stages?** → `MERIDIAN_EXPLAINED.md` Step-by-Step Journey
- **Confidence scoring?** → `MERIDIAN_EXPLAINED.md` Evidence section

### Feature Implementation Questions

- **Authoritative Notes?** → `AUTHORITATIVE-NOTES-PROPOSAL.md`
- **Clone feature?** → `UX-SPECIFICATION.md` Section IV.B
- **Quality dashboard?** → `UX-SPECIFICATION.md` Section IV.C
- **Evidence display?** → `UX-SPECIFICATION.md` Section IV.D

---

## 🚫 Deprecated / Archived

### docs/ux-archive/

This folder contains **superseded UX documentation** from earlier iterations:

- prop2 living analysis exploration (concepts now integrated into UX-SPECIFICATION.md)
- Early UX proposal drafts (00-04 series)
- Component/layout explorations

**⚠️ DO NOT USE FOR IMPLEMENTATION**

These are preserved for historical reference only. All current UX decisions should reference **`UX-SPECIFICATION.md`**.

See `ux-archive/ARCHIVED.md` for details.

---

## ✅ Documentation Principles

1. **Single Source of Truth**: `UX-SPECIFICATION.md` is the canonical UX authority
2. **Clear Hierarchy**: Core docs in root, supporting docs in `docs/`
3. **Role-Based Navigation**: Find what you need based on your role
4. **Status Indicators**: ✅ Current, ⚠️ Historical, 🚧 In Progress
5. **Cross-References**: Documents link to each other where relevant

---

## 🤝 Contributing to Documentation

When adding or updating documentation:

1. **Update this index** if adding new documents
2. **Reference UX-SPECIFICATION.md** for UX decisions (don't create competing UX docs)
3. **Use clear status indicators** (✅ Current, ⚠️ Deprecated, etc.)
4. **Add cross-references** to related documents
5. **Follow the hierarchy**: Core docs in root, supporting docs in `docs/`

---

## 📞 Questions?

If you can't find what you're looking for:

1. Check the **Quick Navigation** section above
2. Review the **Navigation Paths by Role**
3. Use **Finding Specific Information** search guide
4. File an issue if documentation is missing or unclear

---

**Maintained By**: Meridian Product & Engineering Team
**Last Updated**: October 2025
**Next Review**: After Phase 1 implementation completion
