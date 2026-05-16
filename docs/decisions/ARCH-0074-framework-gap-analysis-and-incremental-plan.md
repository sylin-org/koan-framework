# ARCH-0074: Framework Gap Analysis and Incremental Implementation Plan

**Status**: Accepted
**Date**: 2026-03-25
**Deciders**: Enterprise Architect
**Scope**: Cross-cutting — all pillars, dead code removal, documentation, test coverage
**Related**: ARCH-0073 (code modernization sweep), AI-0021 (category-driven AI), AI-0032 (recipes),
ARCH-0043 (lightweight parity roadmap), KOAN-JOBS-PROPOSAL (jobs pillar)

---

## Context

A full-framework inventory identified **233 ADRs** across 18 domains and **177 projects** (64 src,
27 samples, 68 tests). Cross-referencing documentation against implementation revealed gaps in five
categories:

| # | Category | Scale | Risk |
|---|----------|-------|------|
| 1 | **Dead code and orphaned artifacts** — empty sample ghost directories, orphaned test suites, stale archive references | 8 directories + 2 test projects | **High** — confuses tooling, inflates repo |
| 2 | **In-flight features with missing pieces** — AI-0021 convention inference, AI-0032 boot report | 5 items | **High** — advertised API surface incomplete |
| 3 | **Test coverage holes** — AiRecipeProvider untested; Couchbase, ElasticSearch, OpenSearch have zero test suites | 4 providers + 1 pipeline class | **Medium** — silent regressions |
| 4 | **Documentation drift** — version refs stale, 3 proposals untriaged, 2 case studies unpublished, LUMEN-0001 orphaned | 10+ docs | **Medium** — onboarding friction |
| 5 | **Approved-but-unstarted work** — Jobs pillar (approved, 8-week plan), ARCH-0073 constant sweep (640 remaining) | 2 work streams | **Low** — planned, not urgent |

### Corrected Findings (Inventory vs. Reality)

The initial inventory flagged AI-0022–0031 lifecycle modules as "proposed only, no code". Validation
revealed this was **incorrect** — all eight AI lifecycle modules (`Koan.AI.Models`, `.Compute`,
`.Agents`, `.Training`, `.Eval`, `.Review`, `.Orchestration`, `.Prompt`) contain real implementations
with static facades, internal services, auto-registrars, and dedicated test projects. These ADRs
should be updated from Proposed → Accepted.

Similarly, `Engine`, `Client.Understand()`, and `Client.Context()` were never implemented — they
exist only as references in ADR AI-0021. No `[Obsolete]` markers are needed; the ADR text itself
should be updated to reflect that these were design-time alternatives, not deprecated code.

### Principles for Ordering

1. **Remove, don't deprecate** — this is a greenfield framework; zero backward compatibility. Dead
   code and empty shells are deleted, not marked `[Obsolete]`
2. **Finish before starting** — complete in-flight work before opening new fronts
3. **Low-hanging fruit first** — deletions, doc fixes, and small tests unblock confidence
4. **Test before extend** — stabilize existing providers before adding new pillars
5. **Vision stays vision** — proposed ADRs remain proposals; code starts only after explicit acceptance

---

## Decision

Address gaps incrementally across **six phases**, each independently shippable. Phases are ordered
by risk-reduction value, not by pillar affinity — cross-cutting hygiene comes before pillar expansion.

---

### Phase 0 — Dead Code Removal & Hygiene (hours)

**Goal**: Remove orphaned artifacts, fix stale documentation, triage pending proposals. Greenfield
principle: delete, don't deprecate.

#### 0A — Delete orphaned sample ghost directories

These directories contain only `bin/` and `obj/` — zero source code. Not in `Koan.sln`.

| Directory | Origin | Action |
|-----------|--------|--------|
| `samples/S2.Api/` | Moved to `samples/archive/S2/` previously | **Delete** — ghost |
| `samples/S6.Auth/` | Moved to `samples/archive/S6.Auth/` previously | **Delete** — ghost |
| `samples/S6.SocialCreator/` | Moved to `samples/archive/S6.SocialCreator/` previously | **Delete** — ghost |
| `samples/S12.MedTrials/` | Moved to `samples/archive/S12.MedTrials/` previously | **Delete** — ghost |
| `samples/S13.DocMind/` | Case study exists in docs; sample never materialized | **Delete** — ghost |
| `samples/KoanAspireIntegration/` | Moved to `samples/archive/` previously | **Delete** — ghost |
| `samples/S17.ZenGardenTest/` | Ad-hoc test harness with old logs | **Delete** — superseded by integration tests |

#### 0B — Delete orphaned test suites

These reference samples that no longer exist in the solution:

| Test Project | References | Action |
|---|---|---|
| `tests/Suites/S2/Integration/` | `samples/S2.Api/` (gone) | **Delete** |
| `tests/Suites/Samples/Koan.Samples.McpService.Tests/` | `samples/S12.MedTrials/` (gone) | **Delete** |

#### 0C — Delete empty/dead source files

A scan for files under 10 lines revealed **9 zero-or-one-line .cs files** that are empty stubs,
leftover placeholders, or duplicates of types that moved to new locations:

| File | Lines | Reason |
|------|-------|--------|
| `src/Koan.Core/ProbeReason.cs` | 0 | Duplicate — moved to `Observability/Probes/ProbeReason.cs` |
| `src/Koan.Core/ProbeRequestedEventArgs.cs` | 0 | Duplicate — moved to `Observability/Probes/` |
| `src/Koan.Data.Core/AggregateRoot.cs` | 0 | Legacy — replaced by `IEntity<TKey>` (DATA-0009) |
| `src/Koan.Orchestration.Abstractions/Infrastructure/EventIds.cs` | 0 | Empty stub |
| `src/Koan.Orchestration.Abstractions/Profiles.cs` | 0 | Empty stub |
| `src/Koan.Web/KoanWebOptions.cs` | 0 | Duplicate — moved to `Options/KoanWebOptions.cs` |
| `src/Koan.Web/ServiceCollectionExtensions.cs` | 0 | Duplicate — moved elsewhere |
| `src/Koan.Web/Controllers/AiController.cs` | 1 | Empty stub (single line) |
| `src/Koan.Web.Transformers/SwaggerTransformerOperationFilterPlaceholder.cs` | 1 | Placeholder |

Verify no consumers reference these before deleting (build after removal confirms).

#### 0D — Update AI-0021 ADR to reflect reality

`Engine`, `Client.Understand()`, and `Client.Context()` were **never implemented**. The ADR text
references them as superseded design alternatives. Update the ADR to:
- Remove "deprecated" language (nothing to deprecate — they never existed)
- Clarify they were design-time alternatives considered and rejected
- Mark the ADR sections about these as "Not Applicable — never implemented"

#### 0E — Update AI-0022–0031 ADR statuses

All eight lifecycle modules have real implementations. Update each ADR from `Proposed` → `Accepted`
with a note: "Implementation exists in `src/Koan.AI.{Module}/` with dedicated test coverage."

#### 0F — Fix stale documentation

| File | Issue | Fix |
|---|---|---|
| `docs/architecture/principles.md` | Footer says v0.2.18 | Update to v0.6.3 |
| `ASPIRE-INTEGRATION.md` | Date inconsistency in footer | Correct validation date |
| `docs/case-studies/s13-docmind/index.md` | Validated v0.6.2 | Bump to v0.6.3 |
| `docs/decisions/ADR-0054-*` | "Week 2 complete, Week 3 in progress" | Update to reflect actual status |

#### 0G — Triage pending archived proposals

Three items in `docs/archive/proposals/README.md` are "Pending triage":
- Adapter infrastructure centralization → assign disposition
- Entity endpoint service extraction → assign disposition
- Backup/restore comprehensive spec → assign disposition

#### 0H — toc.yml registration (done)

ARCH-0066 through ARCH-0074 already registered in this session.

**DDD/SoC note**: Phase 0 is pure removal and metadata. No domain logic touched.

---

### Phase 1 — Complete AI-0021 Convention Inference (days)

**Goal**: Deliver the remaining AI-0021 commitments so the advertised Client API is fully functional.

| # | Task | Location | Approach |
|---|------|----------|----------|
| 1.1 | Implement `Client.Embed(TEntity entity)` | `Koan.AI/Client.cs` | Reflection-based: scan `[Embedding]` fields first, fall back to all public string properties, final fallback to JSON serialization. Return `EmbedResult`. |
| 1.2 | Implement `Client.Chat(string message, TEntity entity)` | `Koan.AI/Client.cs` | Serialize entity as `Key: Value` context block, prepend to system prompt. |
| 1.3 | Implement `Client.Ocr(TEntity entity)` | `Koan.AI/Client.cs` | Scan for first `byte[]` property, delegate to `Client.Ocr(bytes)`. |
| 1.4 | Implement `EmbeddingMetadata.Resolve<T>()` convention fallback | `Koan.Data.AI` or `Koan.AI.Contracts` | Chain: `[Embedding]` attribute → AllStrings convention → JSON fallback → empty with diagnostic log |
| 1.5 | Add boot report categories section | `Koan.AI/Initialization/KoanAutoRegistrar.cs` | Emit per-category routing (Chat→source/model, Embed→source/model, Ocr→via) + active recipe name |
| 1.6 | Add diagnostic guidance logging | `Koan.AI/Client.cs` | When `Embed(entity)` produces empty content, log: "No embeddable content found. Add [Embedding] to auto-embed on save, or ensure entity has string properties." |

**DDD/SoC approach**:
- Convention inference logic lives in a new `Koan.AI/Conventions/EntityContentResolver.cs` — single
  responsibility: extract embeddable/contextual content from an entity instance.
- `Client` delegates to `EntityContentResolver`; no reflection logic in the facade itself.
- `EmbeddingMetadata.Resolve<T>()` uses the same resolver, ensuring one truth for convention rules.
- Boot report additions follow existing `module.AddSetting()` / `module.AddTool()` patterns.

---

### Phase 2 — Test Stabilization (days)

**Goal**: Close test coverage holes for existing, shipping code.

| # | Task | Scope | Approach |
|---|------|-------|----------|
| 2.1 | Add `AiRecipeProviderTests` | `tests/Suites/AI/Unit/` | Recipe loading, sparse bindings, missing recipe, priority in resolution chain |
| 2.2 | Add `AiCategoryRouter` recipe-specific tests | `tests/Suites/AI/Unit/` | Recipe override vs scope override vs advisor fallthrough |
| 2.3 | Add Couchbase connector test suite | `tests/Suites/Data/Connector.Couchbase/` | Mirror InMemory/Mongo test patterns; requires Couchbase container fixture |
| 2.4 | Add ElasticSearch vector adapter tests | `tests/Suites/Data/Connector.ElasticSearch/` | Vector-only scope; container fixture |
| 2.5 | Add OpenSearch vector adapter tests | `tests/Suites/Data/Connector.OpenSearch/` | Vector-only scope; container fixture |
| 2.6 | Add tests for Phase 1 convention inference | `tests/Suites/AI/Unit/` | `EntityContentResolver` with decorated, undecorated, empty, byte[] entities |

**DDD/SoC approach**:
- Each test project is self-contained with its own container fixtures (Docker-based).
- Test entity models are test-local, not shared — avoids coupling test assumptions across providers.
- AI unit tests mock `IConfiguration` and `IAiAdapterRegistry`; no live provider needed.

---

### Phase 3 — Documentation Consolidation (days)

**Goal**: Publish missing case studies, resolve orphaned ADRs, align docs with v0.6.3 reality.

| # | Task | Scope | Approach |
|---|------|-------|----------|
| 3.1 | Publish S16.PantryPal case study | `docs/case-studies/s16-pantrypal/` | Extract from S16-0001 ADR + sample README; follow S13 structure (index, data-modeling, ai-pipeline) |
| 3.2 | Publish S18.Prism case study | `docs/case-studies/s18-prism/` | Extract from sample + memory context; focus on AI lifecycle dogfooding |
| 3.3 | Resolve LUMEN-0001 status | `docs/decisions/LUMEN-0001-*` | Either promote to accepted with implementation pointers, or mark as deferred/exploratory |
| 3.4 | File formal Jobs pillar ADR | `docs/decisions/ARCH-0075-*` or `JOBS-0001-*` | Promote `docs/design/KOAN-JOBS-PROPOSAL.md` to numbered ADR with Accepted status |
| 3.5 | Update ADR-0054 status | `docs/decisions/ADR-0054-*` | Mark stale "Week 2/3" progress as superseded or update with current state |
| 3.6 | Update `.claude/skills/` for AI-0021 changes | `.claude/skills/koan-ai-integration/SKILL.md` | Add convention inference patterns, recipe configuration, category routing examples |

**DDD/SoC approach**:
- Case studies are narrative documentation, not code — they follow the existing s13-docmind structure.
- Skills updates reflect implemented patterns only, never proposed-but-unbuilt features.

---

### Phase 4 — Approved Work Streams (weeks)

**Goal**: Execute approved, planned work that has design docs and clear scope.

| # | Task | ADR/Plan | Approach |
|---|------|----------|----------|
| 4.1 | ARCH-0073 constant sweep completion | ARCH-0073 | Remaining ~640 magic strings across 114 files; follow established `ConfigurationConstants` pattern |
| 4.2 | Jobs pillar — Milestone 1 (Core contracts) | KOAN-JOBS-PROPOSAL | `Koan.Jobs.Core`: `IJob`, `IJobScheduler`, `JobDefinition`, `JobResult` — pure domain entities, no infrastructure |
| 4.3 | Jobs pillar — Milestone 2 (Scheduling) | KOAN-JOBS-PROPOSAL | `Koan.Scheduling` integration: cron expressions, recurring jobs, one-shot scheduling |
| 4.4 | Jobs pillar — Milestone 3 (Storage + Providers) | KOAN-JOBS-PROPOSAL | Job state persistence via existing data connectors; InMemory + Postgres adapters first |
| 4.5 | Jobs pillar — Milestones 4–6 (Web, Monitoring, Samples) | KOAN-JOBS-PROPOSAL | Dashboard UI, health integration, S-sample |
| 4.6 | AI-0015 Phase 5 — Health monitoring | AI-0015 | Background member-level circuit breakers, source health aggregation |

**DDD/SoC approach**:
- Jobs pillar follows entity-first pattern: `Job` is an `Entity<Job, Guid>` with static facade.
- Scheduling is a separate concern from job definition — `Koan.Scheduling` orchestrates, `Koan.Jobs.Core` defines.
- Health monitoring is a cross-cutting concern in `Koan.AI/Infrastructure/`, not in adapter code.

---

### Phase 5 — Proposed Pillar Expansion (months, requires ADR acceptance)

**Goal**: Convert accepted proposals into implementation. Each item requires explicit ADR status
change from Proposed → Accepted before code begins.

| # | Domain | ADRs | Prerequisite |
|---|--------|------|--------------|
| 5.1 | **Messaging topology** | MESS-0071 | Accept ADR, define ITopologyProvisioner SPI |
| 5.2 | **Storage profiles + ingest** | STOR-0003, STOR-0004 | Accept ADRs, design pipeline stages |
| 5.3 | **Cache control surface** | ARCH-0060 | Verify current implementation, identify delta |
| 5.4 | **Admin dashboard** | WEB-0061–0063 | Accept roadmap ADR, scope Phase 1 features |
| 5.5 | **Helm exporter** | ARCH-0047 Phase 3 | Design template structure, test with k3s |
| 5.6 | **Entity schema guard** | DATA-0075 | Accept ADR, design validation pipeline |
| 5.7 | **Entity transfer helpers** | DATA-0079 | Accept ADR, design cross-context audit |
| 5.8 | **[Timestamp] auto-update** | DATA-0080 | Accept ADR, design lifecycle hook integration |
| 5.9 | **Flow command bus** | FLOW-0070 | Accept ADR, define dispatch semantics |

**DDD/SoC approach**:
- Each pillar expansion starts with SPI definition (abstractions project), then implementation.
- No cross-pillar coupling introduced — pillars communicate via events or shared contracts only.

---

### Phase 6 — AI Lifecycle Validation & Hardening (weeks)

**Goal**: The eight AI lifecycle modules are implemented but their ADRs still say "Proposed". This
phase validates each module against its ADR, identifies delta, updates ADR status, and hardens test
coverage.

| # | Module | ADR | Implementation | Action |
|---|--------|-----|----------------|--------|
| 6.1 | `Koan.AI.Models` | AI-0023 | `Model.*` facade, `IModelService`, `ModelEntry`, `DeployOptions` | Validate ADR alignment, harden tests |
| 6.2 | `Koan.AI.Prompt` | AI-0025 | `Prompt.*` facade, `PromptBuilder` | Validate URI-based prompt refs vs ADR spec |
| 6.3 | `Koan.AI.Compute` | AI-0024 | `Compute.*` facade, `IComputeService`, `ComputeResource` | Validate hardware discovery vs ADR spec |
| 6.4 | `Koan.AI.Orchestration` | AI-0026 | `Chain.*` facade, `ChainBuilder`, `ChainExecutor`, `ChainMemory` | Validate composition primitives vs ADR spec |
| 6.5 | `Koan.AI.Training` | AI-0028 | `Training.*` facade, `Dataset`, `ITrainingService`, 20 files | Validate `Dataset.From<T>()` and entity bridge |
| 6.6 | `Koan.AI.Eval` | AI-0029 | `Eval.*` facade, `GateBuilder`, `Metric`, `DriftResult` | Validate quality gates vs ADR spec |
| 6.7 | `Koan.AI.Review` | AI-0030 | `Review.*` facade, `ReviewQueue`, `ReviewAction` | Validate HITL feedback loops |
| 6.8 | `Koan.AI.Agents` | AI-0031 | `Agent.*` facade, `AgentBuilder`, `AgentExecutor`, `EntityToolGenerator` | Validate entity-awareness vs ADR spec |
| 6.9 | AI-0027 Media Analysis | AI-0027 | `MediaAnalysisWorker`, `MediaAnalysisExecutor`, `MediaAnalysisEmbeddingBridge`, `MediaAnalysisRegistry` in `Koan.Data.AI` | **Implemented** — full processing pipeline |

**DDD/SoC approach**:
- Each module is already its own bounded context with internal services and auto-registrars.
- Validation focuses on: does the implementation match the ADR contract? Are there gaps or drift?
- Any delta found becomes a targeted fix, not a rewrite.
- ADR statuses updated to Accepted with implementation notes after validation passes.

---

## Implementation Status (as of 2026-03-26)

| Phase | Status | Commits | Highlights |
|---|---|---|---|
| **Phase 0** — Dead code removal | **Complete** | `ad0929ee` | 7 ghost dirs, 2 test suites, 9 dead files deleted |
| **Phase 1** — AI-0021 convention inference | **Complete** | `ad0929ee` | `EntityAi` static class, boot report categories+recipes |
| **Phase 2** — Test stabilization | **Complete** | `55dc2ea9` | 20 AI tests + 3 connector suites (Couchbase, ES, OpenSearch) |
| **Phase 3** — Documentation consolidation | **Complete** | `9c00128e` | S16/S18 case studies, JOBS-0001 ADR, skills update |
| **Phase 4** — Approved work streams | **Complete** | `6a979a56`, `7cccf7fd` | ~357 magic strings eliminated, Jobs M1-6, AI health monitoring |
| **Phase 5** — Pillar expansion (partial) | **Partial** | `b9c04801`, `a273f91b` | [Timestamp(OnSave)], cache restructure (layered + SQLite), DATA-0079 accepted |
| **Phase 6** — AI lifecycle validation | **Partial** | `0556bded` | 6.9 MediaAnalysis fully implemented; 6.1-6.8 pending validation |

---

## Consequences

### Positive

- **Clean repo** — orphaned ghosts and dead references removed, not decorated with `[Obsolete]`
- **Corrected inventory** — AI lifecycle modules recognized as implemented; ADR statuses updated
- **Finish-before-start discipline** — in-flight work (AI-0021) completes before new pillars open
- **Incremental shippability** — each phase is independently valuable and deployable
- **Risk-ordered execution** — highest-risk gaps (dead code, incomplete API surface, untested code)
  addressed first
- **DDD alignment** — each phase respects bounded contexts and separation of concerns

### Negative

- **Long tail** — Phase 4–6 span weeks to months; requires sustained commitment
- **Test infrastructure** — Couchbase/ES/OpenSearch tests need container fixtures that may be slow
  in CI
- **Phase 6 validation** — may reveal delta between ADR specs and implementations, generating
  unplanned fix work

### Risks

- **Jobs pillar scope** — 8-week plan may expand if entity-first job modeling surfaces new Data
  layer requirements
- **Convention inference edge cases** — Phase 1 reflection-based content extraction may need
  iteration for complex entity hierarchies
- **AI lifecycle drift** — if implementations diverged significantly from their ADRs, Phase 6
  validation may require non-trivial reconciliation

---

## References

- ARCH-0073: Code Modernization Sweep (prior art for phased sweep approach)
- ARCH-0043: Lightweight Parity Roadmap (Phase 1–3 overlap with Phase 5 here)
- AI-0021: Category-Driven AI with Convention Defaults (Phase 1 completes this)
- AI-0022: Unified AI Lifecycle Vision (Phase 6 implements this)
- AI-0032: Intent-Capability Resolution with Recipes (Phase 1.5 adds boot report)
- KOAN-JOBS-PROPOSAL: Jobs Pillar Design (Phase 4 implements this)
- docs/archive/proposals/README.md: Pending triage items (Phase 0.3)
