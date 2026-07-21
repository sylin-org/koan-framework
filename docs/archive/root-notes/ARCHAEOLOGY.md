# Koan Framework: An Archaeological Narrative

**How a .NET framework evolved through iterative, agentic development — told through its git history, decision records, and source code.**

*Compiled March 2026 from 1,340 commits, 160+ Architecture Decision Records, proposal documents, and source code analysis spanning August 2025 to March 2026.*

---

## Table of Contents

1. [Project Orientation](#1-project-orientation)
2. [Timeline of Major Events](#2-timeline-of-major-events)
3. [Conceptual Drift Map](#3-conceptual-drift-map)
4. [The Philosophy's Three Eras](#4-the-philosophys-three-eras)
5. [Moments of Highest Uncertainty](#5-moments-of-highest-uncertainty)
6. [The Stable Core](#6-the-stable-core)
7. [Proposal Lifecycle](#7-proposal-lifecycle)
8. [Philosophical Tensions](#8-philosophical-tensions)
9. [Raw Evidence Appendix](#9-raw-evidence-appendix)

---

## 1. Project Orientation

Koan Framework is a .NET 10 framework for building sophisticated applications with simple patterns. It provides Active Record-style ergonomics (`Entity<T>`, `todo.Save()`) with polyglot storage transparency (SQL, NoSQL, Vector, JSON), AI-native capabilities (chat, embeddings, semantic search), event-driven architecture, and automated orchestration — all unified under a "Reference = Intent" philosophy where adding a NuGet package reference is sufficient to enable functionality.

The repository contains **1,340 commits** across 7 months (August 2025 – March 2026), **60+ source modules** under `src/`, **17 samples** (some archived), and an extraordinary **160+ Architecture Decision Records** across 13 domains (AI, ARCH, DATA, DX, FLOW, MESS, WEB, OPS, STOR, MEDIA, CORE, TEST, LUMEN). Documentation directories include `docs/decisions/`, `docs/proposals/`, `docs/architecture/`, `docs/specifications/`, `docs/design/`, and `docs/archive/`.

The project was originally named **Sora** and hosted at `sylin-labs/sora-framework` before being rebranded to **Koan** at `sylin-org/koan-framework`.

The single author is Leo Botinelly, with AI assistants (GitHub Copilot, Claude Code) as co-authors on key commits. The commit naming convention uses conventional commits with scope tags (`feat(ai):`, `fix(zengarden):`, `refactor(canon):`), and commit messages are remarkably detailed — often multi-paragraph with itemized change lists, making the git history itself a rich documentary source.

### Commit Rhythm

```
Month          Commits
─────────────  ───────
2025-08        356     ████████████████████████████████████
2025-09        410     █████████████████████████████████████████
2025-10        377     █████████████████████████████████████▌
2025-11        100     ██████████
2026-01         57     █████▌
2026-02         31     ███
2026-03          9     ▌
```

Three distinct burst patterns emerge: the initial dump (Aug 18–20, 185 commits), the September sprint (Sep 23–30, 192 commits), and feature delivery sprints (Oct 17–19, Nov 3–5). Quiet periods between suggest design and thinking phases. The December 2025 gap — zero commits — may represent reflection, or work shifting to the now-separate Zen Garden repository.

---

## 2. Timeline of Major Events

### Foundation Period (August 2025)

| Date | Type | Description | Evidence |
|------|------|-------------|----------|
| **2025-08-18** | `FOUNDATION` | **The Big Bang**: 82 commits in a single day. Project pushed to GitHub as `sora-framework` at version 0.2.8 — substantial prior work existed. Core concepts arrive fully formed: `Entity<T>`, `EntityController<T>`, `ISoraAutoRegistrar`, `BootReport`, health endpoints, transformers, configuration helpers. The "Reference = Intent" pattern is established on Day 1. | `b71717b5` "Initial push", `addd6285` introduces AutoRegistrar, `DX-0038` ADR |
| **2025-08-18** | `CRYSTALLIZATION` | **Auto-Registration born**: `ISoraAutoRegistrar` + `SoraBootstrapReport` introduced, legacy `ISoraInitializer` classes removed in the same day. The concept of modules self-describing at boot time arrives essentially final. | `addd6285`, `cd0bc905` |
| **2025-08-19** | `FOUNDATION` | **AI pillar established**: 11 ADRs written in two days (AI-0001 through AI-0013). `Sora.AI.Contracts`, adapter registry, multi-service routing, MCP runtime all specified. The AI architecture arrives as specification before implementation. | `AI-0001` through `AI-0013`, `703ec57c` |
| **2025-08-19** | `CONSOLIDATION` | **IEntity unification**: `IAggregateRoot<TKey>` removed, everything unified on `IEntity<TKey>`. DDD "aggregate root" becomes a documentation concept only. An early simplification that stuck. | `DATA-0009` |
| **2025-08-21** | `CRYSTALLIZATION` | **Entity-first facade specified**: `DATA-0059` establishes `Entity<T>.Get(id)`, `todo.Save()` patterns and save semantics. This becomes the framework's signature API. | `DATA-0059`, `df93c5f4` |
| **2025-08-25–27** | `FOUNDATION` | **Media + Storage + Orchestration burst**: Storage module, media pipeline, orchestration manifest system all arrive within 3 days. The framework's pillared architecture solidifies. | `3f97db8f`, `e7fb4c11`, `eecdb05a` |
| **2025-08-30** | `FOUNDATION` | **Sora.Flow born**: Canonicalization/pipeline module introduced with entity-first patterns, auto-registrars, TTL purge. S8 sample wired. | `15ce0f97` |
| **2025-08-31** | `DELETION` | **Legacy Flow runtime removed**: "Remove legacy flags and code paths; no EnableLegacyRuntime." The greenfield typed runtime replaces whatever came before — and this is only 13 days after the initial push. | `f6892469` |

### Identity Period (September 2025)

| Date | Type | Description | Evidence |
|------|------|-------------|----------|
| **2025-09-01** | `PIVOT` | **ULID to UUID v7 begins**: Flow entities start migrating from custom ULID to standard UUID v7. First commits signal a deeper identity rethink. | `19666dca`, `FLOW-0104` |
| **2025-09-15** | `RENAME` | **The Great Rename: Sora to Koan**: Combined with complete ULID removal and UUID v7 adoption. Framework rebranding touches every file. Co-authored with Claude Code — the first documented human-AI pair programming on a framework-level decision. | `00d06394` |
| **2025-09-23–30** | `DRIFT` | **The September Sprint**: 192 commits in 8 days. .NET 10 migration, NuGet package updates, 233 to 0 build errors fixed, AI modernization (ADR-0014), canonical source-member architecture (AI-0015), Canon rename, MCP integration, and comprehensive documentation enrichment. The single densest period of evolution. | Multiple commits, `b8b42a7d`, `394f3183`, `925fe7f0` |
| **2025-09-29** | `RENAME` | **Flow to Canon**: Pipeline/canonicalization pillar renamed because "Flow" confused people with generic ETL. Supersedes ARCH-0053. The framework proves willing to do expensive renames for naming clarity. | `ARCH-0056`, `ffe5e2c7` |

### Maturation Period (October – November 2025)

| Date | Type | Description | Evidence |
|------|------|-------------|----------|
| **2025-10-01** | `PIVOT` | **AI Modernization (ADR-0014)**: Sources, capabilities, and fallback architecture. Circuit breakers, health monitoring. The first major revision of the AI architecture — just 6 weeks after the initial 11 ADRs. A terminology correction followed: "Source" and "Group" were swapped to "Member" and "Source" because the original names confused people. | `AI-0014`, `AI-0015`, `788faa99` |
| **2025-10-05** | `REALIZATION` | **Canon Implementation Delta**: Post-hoc reconciliation document reveals that implementation outran the specification. Three key deviations noted — all marked "Implementation is superior." The spec said void returns; the code returned entities. The spec used sync callbacks; the code used async. The framework learns faster than it can spec. | `docs/architecture/CANON-IMPLEMENTATION-DELTA.md` |
| **2025-10-09** | `CONSOLIDATION` | **Sample Strategic Realignment**: DX-0045 archives 7 samples (S2, S4.Web, S6.Auth, S6.SocialCreator, S12, S15, KoanAspireIntegration), creates learning path progression. Samples shift from ad-hoc showcases to intentional pedagogical sequence. | `DX-0045`, `103f4e86` |
| **2025-10-12** | `CONSOLIDATION` | **AddKoan bootstrap rehomed to Core**: The bootstrap entry point moves from Web into Core, reflecting that Koan is not just a web framework. | `d663ddb0`, `ARCH-0065` |
| **2025-10-16–19** | `SPLIT` | **S7 replaced**: `S7.ContentPlatform` and `S7.TechDocs` replaced by `S7.Meridian` — a third incarnation of the content management sample, each time with a clearer identity. | `4aa488ce` |
| **2025-11-03–05** | `REALIZATION` | **Vector search maturation sprint**: 20+ commits. Sliding window pagination, partition-based import, attribute-driven embeddings (ARCH-0070), SemanticSearch API. The vector story goes from "it works" to "it works at scale." | `fbb2390d`, `b116f1e0`, `29fb4b30`, `d052c806` |
| **2025-11-05** | `PIVOT` | **The `[Embedding]` attribute flip**: Initially the attribute *gated* on-demand operations. After implementation experience, the framework realized convention-inferred defaults work better — the attribute should only gate lifecycle behaviors (auto-embed-on-save), not on-demand calls. | `ARCH-0070`, later codified in `AI-0021` |
| **2025-11-06–09** | `FOUNDATION` | **KoanContext service born**: AI-optimized code intelligence service built in phased approach (Phase 1: foundation, Phase 2: React/TypeScript migration). The framework starts eating its own dogfood — building services *with* Koan *for* Koan. | `28aeaca2`, `30c17398`, `68bac35e` |

### Federation Period (January – March 2026)

| Date | Type | Description | Evidence |
|------|------|-------------|----------|
| **2026-01-17–28** | `SPLIT` | **Zen Garden re-homed**: The Rust-based infrastructure project (embedded in `other/zen-garden/`) extracted to its own repository. `Koan.ZenGarden` client library created as the integration bridge. What was monorepo becomes federation. 4,321 lines of `Cargo.lock` deleted in a single commit. | `0e46dc7f`, `b0bf861c`, `ab4935c5` |
| **2026-02-06–09** | `PIVOT` | **AI Architecture v3 (ADR AI-0021)**: Category-driven AI with convention defaults. `Engine` facade deprecated, replaced by `Client`. Monolithic `IAiAdapter` split into `IChatAdapter`, `IEmbedAdapter`, `IOcrAdapter` (ISP principle). Per-category configuration. The AI subsystem's third major architecture in 6 months. | `AI-0021`, `1a23e082`, `7417498f` |
| **2026-02-07–09** | `CRYSTALLIZATION` | **ZenGarden capability orchestration**: Persistent stone roster, active topology hydration, container failover. The service discovery system matures from "find a service" to "maintain a living topology." | `d6a8e4c1`, `d4fd9615`, `5cb487d0` |
| **2026-03-05** | `REALIZATION` | **Orchestrator model advisor**: AI model selection delegated to ZenGarden offering catalog. The framework learns that infrastructure decisions (which AI model to use) should come from the topology, not from application configuration. | `9790a894`, `b843545b`, `d3193d31` |

---

## 3. Conceptual Drift Map

### Entity\<T\>

- **Started**: Day 1 (`Entity<TEntity>` with `IEntity<TKey>`). Purpose: Active Record-style persistence.
- **Ended**: Same core concept, but with static convenience methods (`Todo.Get(id)`, `todo.Save()`), GUID v7 auto-generation, multi-provider transparency, embedded AI capabilities (semantic search, embeddings), entity lifecycle events, partition-aware routing, and transaction coordination.
- **Key moment**: `DATA-0059` (Aug 21) crystallized the entity-first facade. The Entity pattern proved so stable that everything else was layered onto it rather than beside it.
- **Arc**: Foundation, then expansion by accretion. Never broke, only grew.

### Auto-Registration / "Reference = Intent"

- **Started**: Day 1 as `ISoraAutoRegistrar` — modules self-register services when their assembly is referenced.
- **Ended**: `IKoanAutoRegistrar` with `Describe()` boot reports, source-generated registries (`CORE-0072`), and `AddKoan()` as a single bootstrap call in `Koan.Core`.
- **Key moment**: Never changed in concept. The name changed from Sora to Koan, and the bootstrap entry point moved from Web to Core (`ARCH-0065`), but the idea was right from the start.
- **Arc**: Crystallized immediately, refined operationally over time.

### AI Architecture (the most-revised concept)

- **Started**: 11 ADRs in 2 days (Aug 19–21). `Sora.AI` facade, adapter registry, multi-service routing. Introduced `Engine` as the terse entrypoint.
- **Went through**: Source/Group terminology confusion (corrected in AI-0014/0015). Sources/capabilities/fallback modernization (ADR-0014, Oct). Canonical source-member architecture (AI-0015, Oct). Entity-first AI with `[Embedding]` as gatekeeper (AI-0020).
- **Ended**: Category-driven architecture (AI-0021, Feb 2026). `Engine` deprecated, `Client` facade with per-category targeting (`Client.Scope()`). Monolithic adapter split into ISP interfaces (`IChatAdapter`, `IEmbedAdapter`, `IOcrAdapter`). `[Embedding]` no longer gates on-demand operations — convention-inferred defaults work for any entity.
- **Key moments**: Every 2–3 months, the AI architecture was substantially revised. The pattern: spec aggressively, implement quickly, realize the abstraction was wrong, re-spec.
- **Arc**: Aspirational overdesign, then iterative simplification, then convention-driven defaults. The AI subsystem taught the framework what it meant to evolve in the presence of a rapidly moving external domain.

### Flow to Canon

- **Started**: `Sora.Flow.Core` (Aug 30) — canonicalization/pipeline engine for heterogeneous data ingestion.
- **Went through**: Typed runtime (Aug 31), identity maps (Sep 1), ULID-to-UUID migration, legacy code removal.
- **Ended**: Renamed to `Koan.Canon` (Sep 29) because "Flow" caused confusion with generic ETL pipelines. The rename was expensive (touching many files) but considered worthwhile for naming clarity.
- **Post-rename discovery**: The Canon Implementation Delta document revealed that the implementation outran the specification in three areas — async patterns, return types, and record richness. All three were marked "Implementation is superior."
- **Arc**: Named wrong, confusion detected, renamed. Then built faster than the spec could keep up with.

### Zen Garden

- **Started**: Embedded Rust project in `other/zen-garden/` — a network service discovery and infrastructure management system with mDNS topology, offering manifests, and CLI tools (Moss registry, Rake CLI).
- **Went through**: Massive internal refactoring (moss main.rs extraction achieving 93% reduction, rake command extraction, manifest-driven discovery). Intense development September–October 2025.
- **Ended**: Extracted to own repository (Jan 2026). `Koan.ZenGarden` client library created as integration bridge. The client itself was rebuilt once (`ab4935c5` "rebuild adapter around tools-domain runtime"). Now serves as the topology source for AI model selection.
- **Arc**: Monorepo inclusion, outgrew its container, extracted, federated. A project-within-a-project that became important enough to need independence.

### Samples (S0–S17)

- **Started**: Ad-hoc collection (S2, S4, S5, S6.Auth, S6.SocialCreator, S7.ContentPlatform, S7.TechDocs, S8.Flow, S8.Location...).
- **Went through**: Strategic realignment (DX-0045, Oct 2025): 7 samples archived, learning paths defined, capability matrix created. Individual samples renamed to reflect purpose: S5 became "AnimeRadar", S6 became "SnapVault", S7 became "Meridian."
- **Ended**: Curated pedagogical sequence (S0 beginner, S1 web, S5 AI, S16 MCP, S8 Canon). A `CATALOG.md` with learning paths and capability coverage matrix.
- **Notable**: S7 had three incarnations (ContentPlatform, TechDocs, Meridian). S6 had three (Auth, SocialCreator, SnapVault). The most-touched non-infrastructure file in the entire repository was `samples/S5.Recs/Services/SeedService.cs` (45 touches) — the seed service for the flagship recommendation sample was the proving ground for framework features.
- **Arc**: Organic growth, proliferation, curation. The samples became the framework's primary narrative device.

### The Name: Sora to Koan

- **Started**: "Sora Framework" at `sylin-labs/sora-framework`.
- **Ended**: "Koan Framework" at `sylin-org/koan-framework`.
- **Key moment**: September 15, 2025 (`00d06394`). Combined with ULID-to-UUID v7 migration. Co-authored with Claude Code.
- **Completeness**: Zero "Sora" references remain in any `.cs` file. The rename was thorough.
- **Why it matters**: The rename happened just 28 days after the initial push. This suggests the original name was provisional, or the framework's identity crystallized quickly enough that the old name felt wrong.

---

## 4. The Philosophy's Three Eras

Analysis of the framework's documentation reveals three distinct philosophical eras, visible in the shifting tone and language of documents written at different times.

### Era 1: The Idealist (v0.2.18, pre-GitHub, ~October 2024)

The `principles.md` document — dated to the v0.2.18 era, before the GitHub push — reads as a manifesto:

> "Escape hatches everywhere. Framework enhances but never constrains."
> "Zero boilerplate."
> "Just add reference."

This is the voice of someone who knows what they want to build. The document is clean, code-centric, and silent about operational realities like distributed coordination, horizontal scaling, and capability detection. It assumes containerization and observability are "standard."

Notably, `principles.md` has **not been updated** since its original writing, even as the framework reached v0.6.3. It remains an aspirational time capsule.

### Era 2: The Pragmatist (v0.6.2, ~October 2025)

The `koan-elasticity-strategy.md` document, marked as **draft** and **not-yet-tested**, contains the first honest admission:

> "Koan is elastic-friendly but not elastic-native."
> "Framework lacks first-class queueing, distributed coordination."
> "This makes single-instance or manually scaled deployments straightforward, but elasticity currently depends on external infrastructure discipline."

The language shifts from prescriptive ("just add a reference") to descriptive ("currently depends on external infrastructure discipline"). This is the voice of someone who has run their framework in containers and discovered the gap between elegant single-instance design and distributed reality.

The Canon Implementation Delta document (`CANON-IMPLEMENTATION-DELTA.md`) belongs to this era. It's a post-hoc reconciliation showing that the implementation outran the spec in three areas, each marked "Implementation is superior." This is the framework learning faster than it can spec.

### Era 3: The Honest (v0.6.3, ~February 2026)

The `koan-platform-comparative-analysis.md` document positions the framework explicitly:

> "Choose Koan when: You want a cohesive .NET framework..."
> "Augment with other tooling when: You require ready-made business modules, extensive connector catalogs, low-code builders, or certified compliance/governance."

And in its gaps section:

> "Ecosystem maturity: Limited packaged connectors."
> "Operational tooling: No first-party admin or stewardship consoles."
> "Support footprint: Open-source project; teams shoulder Tier 1/2 support."

This is not typical framework marketing. It's a builder who has stopped competing with everything and started defining their niche. The tone has evolved from "Koan solves everything" to "Koan solves this; bring your own for that."

### The Documentation Posture Shift

This evolution is also encoded in `ARCH-0041-docs-posture-instructions-over-tutorials.md`, which mandates "instruction-first content; avoid tutorials." The framework has moved from "teach how to use" to "teach how it works" — a maturity shift from hand-holding to self-service documentation.

The CLAUDE.md Skills system reinforces this: different users are routed to different learning paths by use case (entity-first, bootstrap, performance), an acknowledgment that "one principles.md for all" doesn't work.

---

## 5. Moments of Highest Uncertainty

### 1. The AI Architecture Identity Crisis (Aug 2025 – Feb 2026)

**The unsettled question**: How should a framework expose AI capabilities to application developers?

**Approaches tried**:

1. **Monolithic adapter** with `IAiAdapter` covering all capabilities (Aug 2025)
2. **Engine facade** — terse, semantic entrypoint (`Engine.Prompt(...)`, `Engine.Embed(...)`) (AI-0011, Aug 21)
3. **Source-member architecture** with capability negotiation and terminology correction (AI-0014/0015, Oct 2025)
4. **Entity-first AI** with `[Embedding]` as gatekeeper for operations and transaction coordination (AI-0020, late 2025)
5. **Category-driven ISP split** — `IChatAdapter`, `IEmbedAdapter`, `IOcrAdapter` with `Client` facade replacing `Engine`; `[Embedding]` flipped from gatekeeper to lifecycle opt-in (AI-0021, Feb 2026)

**How it was resolved**: AI-0021 represents the clearest architecture yet, driven by two key insights:

- Different AI operations (chat, embed, OCR) have fundamentally different lifecycle characteristics and shouldn't share an interface (the ISP principle applied to AI adapters).
- Convention-inferred defaults are better than attribute-gated operations. You shouldn't need `[Embedding]` to call `Client.Embed(entity)` — the attribute should only control automatic lifecycle integration (embed-on-save).

**What was learned**: AI integration is a domain where the external landscape (models, APIs, capabilities) shifts faster than framework architecture can track. The solution was to stop trying to normalize everything behind one interface and instead embrace categorical distinctions. The framework wrote 21 ADRs in the AI domain — more than any other — because this was the area of greatest conceptual churn.

### 2. The Flow/Canon Identity Problem (Aug 30 – Sep 29, 2025)

**The unsettled question**: What is the data canonicalization pillar actually called, and what does it do?

**What happened**: Created as `Sora.Flow` — a name that immediately conflicted with generic ETL/pipeline concepts. Within one month, confusion was sufficient to justify a breaking rename to `Koan.Canon`. The ADR (ARCH-0056) explicitly rejected alternatives: "Koan.CanonicalFlow" (too verbose), "Koan.Reference" (understates ingestion responsibilities).

After the rename, the Canon Implementation Delta revealed that the implementation had already diverged from its spec in three beneficial ways — the team was building the right thing under the wrong name, and the spec lagged behind the code.

**What was learned**: In a framework with many pillars, naming is load-bearing. A bad name creates ongoing friction in documentation, conversation, and conceptual mapping. The cost of a breaking rename at month 1 is far lower than living with naming confusion forever. And once a name is fixed, implementation can outrun specification — as long as you honestly document the delta.

### 3. The Zen Garden Boundary Question (Sep 2025 – Jan 2026)

**The unsettled question**: Should the Rust-based infrastructure discovery system live inside the .NET framework monorepo?

**What happened**: Zen Garden started in `other/zen-garden/` — a Rust codebase (Moss service registry, Rake CLI) embedded in a .NET solution. It grew substantially: mDNS topology, offering manifests, modular extraction of 1000+ line `main.rs` files, cross-platform deployment. By January 2026, it was re-homed to its own repository, with `Koan.ZenGarden` as a .NET client library bridge. The client library itself was then rebuilt once to align with the tools-domain runtime model.

**What was learned**: A framework's ecosystem boundary is an active design decision. Including everything in a monorepo reduces coordination cost but creates conceptual bloat. The extraction happened at the right moment — after the design was stable enough to define a clean API boundary, but before the Rust codebase's development velocity further complicated the .NET build process.

### 4. The Sample Proliferation Crisis (Aug – Oct 2025)

**The unsettled question**: How many samples are too many, and what story should they tell?

**What happened**: Samples accumulated organically: S2 (unclear purpose), S4.Web (no README), S6 had three incarnations (S6.Auth, S6.SocialCreator, S6.SnapVault), S7 had three (S7.ContentPlatform, S7.TechDocs, S7.Meridian). Some had overlapping functionality, others lacked documentation.

**How it was resolved**: DX-0045 (Oct 2025) imposed strategic order. 7 samples archived with a `ARCHIVED.md` migration guide. A learning path progression defined (Beginner, Intermediate, Advanced). A capability matrix created so developers could find "which sample shows X?" Four new themed samples were planned to fill coverage gaps (S3.NotifyHub, S4.DevHub, S6.MediaHub, S9.OrderFlow).

**What was learned**: Samples in a framework serve as narrative — they tell the story of what the framework can do and how to think about it. An unstructured collection of samples tells a confusing story. The shift from "demos" to "learning paths" reflected a deeper understanding of the framework's audience.

### 5. The Entity-First Boundary (How far does Entity\<T\> extend?)

**The unsettled question**: Should the entity pattern absorb AI, vectors, events, and media, or should these be separate concerns?

**What happened**: The initial design gave entities persistence. Then entity sets and routing arrived (DATA-0030, DATA-0077). Then entity lifecycle events (DATA-0074). Then attribute-driven embeddings (ARCH-0070) made entities vector-aware. Then entity-backed search profiles (ADR-0054). The boundary kept expanding.

A tension emerged: the philosophy says entities are first-class for domain models, but orchestration and infrastructure are service-centric. `ZenGardenClient` is a singleton service, not an entity. `ICanonRuntime` is injected via DI. Background jobs use `IElasticJob` interfaces. The entity-first philosophy and the service-centric reality coexist.

**Current resolution**: `Entity<T>` is the universal substrate for domain models. AI, vector, lifecycle, and routing capabilities are layered onto it via convention and optional attributes. Infrastructure and orchestration remain service-centric. The framework bets that one abstraction (entity) is better than many parallel ones for domain modeling, while accepting that not everything is a domain model.

---

## 6. The Stable Core

These concepts appeared in the earliest commits and remain essentially unchanged:

- **`Entity<T>` as the fundamental unit**: The core pattern of `public class Todo : Entity<Todo>` with static methods and instance save is present from Day 1 and has never been architecturally questioned. Everything else in the framework rotates around it.

- **Auto-Registration / "Reference = Intent"**: `IKoanAutoRegistrar` (originally `ISoraAutoRegistrar`) with `Initialize()` and `Describe()` was specified on August 18, 2025 and has not changed in structure. The pattern of each assembly providing `/Initialization/KoanAutoRegistrar.cs` is framework canon.

- **Boot Report**: Self-describing infrastructure where modules report their configuration at startup. Created Day 1, never revisited architecturally.

- **EntityController\<T\>**: The REST API controller base class. Created Day 1, enhanced but never restructured.

- **Multi-provider transparency**: The idea that the same entity code works across SQL, NoSQL, Vector, and JSON stores. This was in the initial design and remains the framework's core differentiator.

- **Convention-over-configuration philosophy**: From the earliest commits (`fc566a7c` "Swagger: auto-register via ISoraInitializer + startup filter; no explicit Add/Use needed when referenced"), the framework prefers automatic discovery over explicit wiring.

- **Health endpoints** (`/health`, `/health/live`, `/health/ready`): Standardized on Day 1 and never changed.

These stable elements suggest that the framework's author had a clear architectural vision before the first commit. The uncertainty was not about *what* the framework should be, but about *how* its expanding surface area should be organized — naming, boundaries, and API ergonomics.

---

## 7. Proposal Lifecycle

The `docs/proposals/` directory reveals a maturation curve in how design work is done.

### Completed Proposals

Six proposals were fully delivered and moved to `docs/proposals/complete/`:

| Proposal | Problem Solved | Impact |
|----------|---------------|--------|
| Canon Complete Overhaul | Decoupled canonization from messaging | 90% reduction in cognitive overhead for basic canonization |
| Unified Adapter Framework | Standardized multi-database adapter patterns | Enabled "Reference = Intent" for data adapters |
| AI Provider Election Pipeline | Unified routing for multiple AI providers | Integrated into category routing pipeline |
| Couchbase Adapter | Extended multi-provider transparency | Proved adapter pattern works across heterogeneous storage |
| Fast Count Optimization | Query performance for large datasets | Framework-level query optimization |
| Fluent Guard Pattern | Readable validation API | Framework utility `Guard` class (`Must.NotBeNull()`, `Be.Positive()`) |

### Misaligned Proposals

Three proposals landed in `docs/proposals/misaligned/` — explicitly categorized as partially delivered:

| Proposal | What Shipped | What Didn't | Assessment |
|----------|-------------|-------------|------------|
| Koan Admin Surface | Web dashboard, LaunchKit pipeline, options validator | Console surface (50% of scope), discovery routes, advanced panels | "B- grade" — 68% delivered |
| S13 DocMind | — | AI-generated framework documentation | Abandoned: too speculative, would require embedded AI agent |
| Readiness Provisioning Strategy | — | Orchestration readiness patterns | Superseded by simpler proven approaches |

The existence of gap analysis documents (`PROP-koan-admin-surface-GAP-ANALYSIS.md`) created *after* partial implementation is notable — the team institutionalized the practice of honestly assessing how reality diverged from plans.

### In-Progress Proposals

The largest in-progress proposal is **Koan.Context** — a local-first, partition-aware vector indexing and code intelligence service. Its UX is 91% complete but backend-blocked, with 74 tasks still pending across 7 milestones. It represents the framework's ambition to build its own tooling using its own patterns — the most recursive test of a framework's principles.

The **Zen Garden Integration** proposal is comprehensive (16 sections, detailed migration path) but not yet implemented. It would eliminate manual connection string configuration by discovering infrastructure topology automatically.

### What the Lifecycle Reveals

- **Early proposals** were largely post-hoc documentation of code already shipped — retrospective crystallization.
- **Mature proposals** are detailed specifications written *before* implementation, with checklists, migration paths, and phased milestones.
- **Gap analysis documents** emerged as a new genre — post-implementation reality checks. This is the framework learning to document the distance between intention and result.

The `misaligned/` directory is perhaps the most intellectually honest artifact in the codebase. Most projects bury their partial deliveries; this one labels them.

---

## 8. Philosophical Tensions

Four tensions run through the entire codebase history. None are contradictions — they are the distance traveled between aspiration and understanding.

### Entity-First vs. Service-First Reality

| Stated Philosophy | Discovered Reality |
|------|------|
| "Entities handle their own persistence. No repository pattern needed." | `ZenGardenClient` is a singleton service. `ICanonRuntime` is DI-injected. CLI uses `IServiceProvider`. Background jobs use `IElasticJob`. |

The resolution: Entities are first-class for **domain models**, but **orchestration and infrastructure** are service-centric. The philosophy conflates the two in its early documentation but the code correctly separates them.

### Multi-Provider Transparency vs. Capability Detection

| Stated Philosophy | Discovered Reality |
|------|------|
| "Same code works across PostgreSQL, MongoDB, SQLite, JSON/Redis, and vector stores." | Weaviate supports Knn, Filters, Hybrid, PaginationToken, StreamingResults. Simpler providers support only Knn. Graceful degradation code is required. |

The resolution: Transparency holds for the common subset, but capability detection is mandatory for advanced features. The framework's capability flags system makes this explicit rather than hiding it.

### Sane Defaults vs. Explicit Opt-In

| Stated Philosophy | Discovered Reality |
|------|------|
| "Smart defaults, fail fast on misconfiguration." | `[EntityBackup]` is mandatory for backup coverage. `[Embedding]` gates auto-embed-on-save. |

The resolution: The framework learned that **implicit behavior causes silent failures**. The backup system's design document states: *"Explicit opt-in prevents silent data loss from unbounded auto-discovery."* Defaults are still sane, but the definition of "sane" shifted from "do everything automatically" to "do nothing silently."

### Escape Hatches vs. Architectural Conventions

| Stated Philosophy | Discovered Reality |
|------|------|
| "Framework enhances but never constrains. Escape hatches everywhere." | CLAUDE.md flags `IRepository<T>`, manual service registration, and custom ORM usage as anti-patterns. |

The resolution: There *are* escape hatches, but using them is flagged. The philosophy says "never constrain," but the engineering culture says "conventions matter, and violating them makes framework tooling less effective." The framework doesn't prevent anti-patterns — it makes them visible.

---

## 9. Raw Evidence Appendix

### Key Commits

| Hash | Date | Message | What It Reveals |
|------|------|---------|---------|
| `b71717b5` | 2025-08-18 | "Initial push" | Already at v0.2.8 — significant prior development |
| `addd6285` | 2025-08-18 | "Core: introduce ISoraAutoRegistrar + SoraBootstrapReport" | Auto-registration born fully formed on Day 1 |
| `cd0bc905` | 2025-08-18 | "Auto-registration sweep: add SoraAutoRegistrar to all modules" | Immediate framework-wide adoption — not gradual |
| `56098b60` | 2025-08-18 | "Runtime environment consolidation: remove IRuntimeInfo, introduce static SoraEnv" | Breaking with DI orthodoxy for static runtime |
| `f6892469` | 2025-08-31 | "feat(flow): greenfield typed runtime — Remove legacy flags and code paths" | Legacy code removed just 13 days after first push |
| `00d06394` | 2025-09-15 | "feat(core): complete ULID removal and framework modernization — Framework rebranding from Sora to Koan" | The identity pivot. Co-authored with Claude Code |
| `ffe5e2c7` | 2025-09-29 | "docs(arch): record Koan.Canon pillar rename" | Flow to Canon: naming matters enough to justify expensive rename |
| `394f3183` | 2025-09-30 | "fix: disable obsolete integration tests, build now succeeds (233 to 0 errors)" | Major .NET 10 migration pain compressed into one sprint |
| `0e46dc7f` | 2026-01-28 | "chore: remove Zen Garden (re-homed to its own repo)" | 4,321 lines of Cargo.lock deleted — ecosystem boundary drawn |
| `1a23e082` | 2026-02-09 | "ai(core): replace Engine/Ai facade with Client + AiCategoryRouter (AI-0021)" | Third major AI architecture in 6 months |
| `9790a894` | 2026-03-05 | "fix(zengarden): resolve advisor endpoint via offering catalog" | AI model selection delegated to infrastructure topology |

### Key Decision Chains

**AI Architecture Chain** (densest domain — 21 ADRs):

```
AI-0001 (baseline)
  -> AI-0008 (adapters + registry)
    -> AI-0011 (Engine facade)
      -> AI-0014 (modernization: sources/capabilities/fallback)
        -> AI-0015 (canonical source-member, terminology fix, supersedes 0014)
          -> AI-0020 (entity-first AI, [Embedding] as gatekeeper)
            -> AI-0021 (category-driven, Engine deprecated, [Embedding] flipped)
```

Each revision represents a deeper understanding of how AI integration actually works in practice vs. in theory. The chain moved from "normalize everything" to "embrace categorical distinctions."

**Entity Routing Chain**:

```
DATA-0009 (unify on IEntity)
  -> DATA-0030 (entity sets/routing)
    -> DATA-0058 (adapter role attributes)
      -> DATA-0059 (entity-first facade)
        -> DATA-0062 (instance save, set-first-class)
          -> DATA-0077 (context/source/adapter/partition routing)
            -> DATA-0088 (adapter auto-configuration resolver)
```

Progressive layering of routing sophistication onto the entity model. The critical constraint discovered in DATA-0077: Source XOR Adapter (mutually exclusive, because a source already defines its adapter).

**Flow/Canon Chain**:

```
ARCH-0053 (Flow pillar established)
  -> ARCH-0056 (renamed to Canon, supersedes ARCH-0053)
    -> CANON-IMPLEMENTATION-DELTA (spec vs. reality reconciliation)
```

A name change followed by the honest admission that code had already outrun the specification.

**Bootstrap Chain**:

```
DX-0038 (auto-registration standard, Day 1)
  -> ARCH-0065 (AddKoan rehomed from Web to Core)
    -> CORE-0072 (source-generated registries)
```

Progressive centralization and optimization of the bootstrap mechanism. The original pattern survived unchanged; only its location and implementation technique evolved.

### Superseded Decisions

| Superseded ADR | Superseded By | What Changed |
|------|------|------|
| ADR-0054 (entity-backed search profiles) | ADR-0055 (tag-centric semantic search) | Profile management was over-engineered; tags proved simpler |
| AI-0014 (terminology: Source/Group) | AI-0015 (terminology: Member/Source) | Original terms confused people; swapped for clarity |
| AI-0020 (`[Embedding]` gates on-demand ops) | AI-0021 (`[Embedding]` gates only lifecycle) | Convention-first philosophy won; attributes for customization, not gating |
| ARCH-0053 (Flow pillar) | ARCH-0056 (Canon pillar) | "Flow" confused with generic ETL; "Canon" signals canonicalization |

Each supersession is a documented moment where the framework admitted "we understood this wrong" and corrected course with a visible trail.

### Notable Source Code Artifacts

- **Zero Sora remnants in `.cs` files**: The rename was thorough — complete conceptual cleanliness.
- **`Sora.sln` in churn history** (38 touches): The old solution file was among the most-touched files before the rename.
- **`samples/S5.Recs/Services/SeedService.cs`** (45 touches): The most-touched non-infrastructure file. The seed service for the flagship recommendation sample was revised repeatedly — evidence that the sample was the proving ground for framework features.
- **Template files with placeholder TODOs** (`Koan.Core.Adapters/Templates/`): These scaffolding templates contain extensive TODOs, suggesting they were created as aspirational scaffolding rather than extracted from working code.
- **`VectorizationWorker.OBSOLETE.cs`**: An explicitly obsoleted file kept alongside its replacement — the framework preserves its archaeological layers.
- **`docs/proposals/misaligned/`**: A directory dedicated to proposals that didn't land as designed. Most projects bury their partial deliveries; this one labels them.

### Most-Touched Files (Top 10)

| Touches | File | What This Tells Us |
|---------|------|-------------------|
| 67 | `docs/decisions/toc.yml` | ADR discipline — the table of contents was updated with every new decision |
| 55 | `docs/toc.yml` | Documentation structure was continuously reorganized |
| 45 | `samples/S5.Recs/Services/SeedService.cs` | The flagship sample's seed logic was the framework's testing ground |
| 44 | `README.md` | The project's public identity was revised repeatedly |
| 41 | `src/Sora.Flow.Core/ServiceCollectionExtensions.cs` | The Flow/Canon pillar's service registration was the most volatile infrastructure code |
| 38 | `Sora.sln` | The pre-rename solution file saw intense project addition/removal |
| 35 | `Koan.sln` | The post-rename solution file continued the pattern |
| 34 | `samples/S8.Flow/S8.Flow.Adapters.Oem/Program.cs` | The Flow adapter sample's entry point was revised as the pillar evolved |
| 31 | `samples/S5.Recs/Controllers/AdminController.cs` | Admin capabilities for the flagship sample were built iteratively |
| 29 | `samples/S6.SnapVault/wwwroot/js/app.js` | The photo management sample's client code underwent significant UX evolution |

---

## Meta-Observation: The Shape of Agentic Development

This codebase tells a story about a particular mode of software development: a single architect with AI assistants, building a framework through rapid iteration, exhaustive documentation, and willingness to rename, delete, and restructure.

The evidence suggests a repeating pattern:

1. **Specify aggressively** — write 11 ADRs in two days, lay down the architectural intent before writing code
2. **Implement quickly** — 80+ commits in a day, build the thing fast enough that momentum carries past doubt
3. **Observe honestly** — the September sprint that fixes 233 build errors, the Canon Implementation Delta that admits the code outran the spec
4. **Rename without sentiment** — Sora to Koan, Flow to Canon, Engine to Client. If the name is wrong, change it, no matter the cost
5. **Curate deliberately** — archive 7 samples when they no longer serve the narrative, create a `misaligned/` directory for proposals that didn't land

The framework's most interesting characteristic may be its documentation-to-code ratio. With 160+ ADRs, extensive proposals, gap analyses, and detailed commit messages, the *thinking* is as visible as the *code*. The ADRs in particular serve a dual purpose: they record decisions for future reference, but they also appear to function as a design tool — writing the ADR is how the developer thinks through the problem. The evidence for this is the density: you don't write one ADR per day unless writing ADRs is part of how you work, not an afterthought.

The concepts that were right from the start (`Entity<T>`, auto-registration, boot reports) suggest deep domain expertise in .NET framework design. The concepts that required multiple revisions (AI architecture, naming, sample organization) cluster around two themes: **external integration** (AI models and capabilities shift faster than framework architecture can track) and **communication** (names and samples are how a framework communicates its intent to developers).

The human-AI collaboration is not incidental — it's structural. Claude Code co-authored the framework rename. GitHub Copilot co-authored earlier commits. The CLAUDE.md file is maintained as a living instruction set for AI assistants working on the codebase. This is a framework that was built *with* AI assistance and *for* AI integration — a recursive relationship that makes the AI architecture's evolution particularly significant. The framework's hardest design problem was also its most personal: how to integrate the thing helping you build.

And perhaps the most telling artifact: the original `principles.md`, written before the GitHub push, preserved in its original aspirational form while the real philosophy evolved around it in ADRs, delta documents, and comparative analyses. The aspirational voice and the honest voice coexist, and the gap between them is the story of the project.

---

*This document was compiled through automated archaeological analysis of the Koan Framework repository. Every claim is traceable to a specific commit hash, ADR identifier, or file path. Where evidence is thin, this is noted. The analysis reads history, not evaluating quality — understanding how understanding changed over time.*
