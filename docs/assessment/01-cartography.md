# Stage 1 — Cartography: what is actually here

**Date**: 2026-06-10 · **Method**: 15 parallel code audits (12 pillar groups + docs corpus +
samples + test/build infra), grounded in the Stage 0 census and git-churn data. Raw structured
findings: [evidence/](evidence/). Claims below cite concrete projects/files; spot-verify before
acting on any single item.

---

## 1. The map at a glance

113 csproj across `src/` (~150k LOC), but the *real* framework is much smaller. Classified by
evidence (consumers, tests, churn, code signals):

| Class | Projects (approx.) | LOC share | Examples |
|---|---|---|---|
| **Settled core** (post-redesign, consumed, tested) | ~25 | ~45% | Data inner ring, Cache, Jobs, Web nucleus, Vector core, Security.Trust, data connectors |
| **Pre-renovation legacy** (live but old idiom) | ~15 | ~15% | Messaging.Core, Web.Auth flows, Data.Backup, Scheduling, Koan.Core outer strata |
| **Experimental / aspirational** (real code, ~0 consumers) | ~25 | ~25% | AI verticals (8 projects), Rag (+Abstractions), ServiceMesh, Cqrs, Secrets vertical, Recipe |
| **Condemned by own ADR** | 7 | ~5% | Orchestration CLI/Generators stack (ARCH-0077) |
| **Satellite products** in framework repo | ~6 | ~10% | Koan.Service.KoanContext (14.8k LOC, *not in sln*), ZenGarden, Translation |
| **Debris** (tombstone dirs, orphan files) | ~7 dirs | ~0% | See §5 ledger |

Two numbers tell the story:

- **~25–30 projects have zero consumers** in src/samples/tests — they fail the repo's own
  redesign discipline (dogfood-driven, ≥2 usages).
- The **largest codebase in the repo is an application** (`Koan.Service.KoanContext`), which is
  not even in `Koan.sln` — physically present, organizationally already evicted.

## 2. Systemic patterns (the findings that repeat in every area)

These are not per-pillar bugs; they are the shape of a feasibility-phase codebase mid-renovation.

### 2.1 Generational strata — the renovation reached ~⅓ of the surface
The 2026 redesign wave (ARCH-0084 capabilities, ARCH-0086 KoanModule, DATA-0096–0100 filter
pipeline, JOBS-0005) produced the best code in the repo, but everything it hasn't reached still
carries the previous idiom, and the generations coexist *in the same assemblies*:

- **Three module primitives**: `IKoanInitializer` / `IKoanAutoRegistrar` (82 hand-written) /
  `KoanModule` (7 adopters).
- **Two service-discovery pipelines** in Koan.Core (V1 + V2; V2 registered in DI, yet RabbitMq
  and Vault connectors still `new` V1 directly).
- **Two health aggregators** in Koan.Core (root copy never registered), **two singleflight
  primitives**, **two boot-report surfaces** (Core.Adapters vs Provenance).
- **Two auth flow engines** in Web.Auth (live hand-rolled controller — OAuth2-only, no PKCE,
  OIDC returns 501 — vs a dead-but-more-correct ASP.NET-handler path with zero callers).
- **Two schema generations** in Data.Relational (dead `Schema/` directory still shipped).
- **Gen-1 cache registrar chain** (~350 LOC, obsolete, self-serving) inside the otherwise
  exemplary Gen-2 Cache pillar.
- **Recipe pillar** = an entire superseded bootstrap idiom (assembly-attribute + AppDomain scan)
  still shipping as 2 packages with 1 stub consumer.

### 2.2 The dogfood deficit — surface grew faster than consumers
Zero-consumer (or archived-sample-only) projects, by area: `Data.Direct`, `Data.Cqrs`
(+Mongo outbox), `Web.Auth.Roles`, `Koan.WebSockets`, `Web.Json.Strict`, `Web.Transformers`,
`Web.Connector.GraphQl`, `Koan.Tagging`, the entire **Secrets** vertical (3 projects, reflection-
wired into boot, zero tests, zero consumers), **Recipe** (2), **Rag + Rag.Abstractions** (8k LOC,
`InternalsVisibleTo` a test project that doesn't exist), most of the **AI vertical family**
(Agents/Compute/Eval/Training facades — Training and Eval can only throw: no in-repo provider
implements their capabilities), `AI.Connector.HuggingFace` (functional, unconsumed),
`AI.Connector.ZenGarden` (not even in the sln, yet sole implementor of the multimodal surface),
`ServiceMesh` (+Abstractions, no ADR), `Service.Inbox.Connector.Redis` (its client API no longer
exists anywhere in src).

### 2.3 Promise/reality drift — docs hallucinate, status fields lie
- READMEs documenting **APIs that don't exist**: Media.Abstractions/Core (MediaAsset/MediaVariant/
  AddMediaCore…), Koan.Core.Adapters (BaseKoanAdapter/AdapterCapabilities DSL — its entire
  *raison d'être* was deleted), Data.Backup (BackupPlan/BackupSession), Data.Relational (dead
  schema gen), Web.Backup (removed SignalR), Messaging ADRs (inbox/aliases/provisioning).
- Root **README** showcases the deleted Flow pillar (`Flow.OnUpdate<T>`) and badges v0.6.3
  against a live 0.17 (NBGV).
- **ADR status hygiene**: JOBS-0001/0002 still read "Accepted" despite explicit supersession by
  JOBS-0005; only ~4–6 of 279 ADRs carry a Superseded status; 16 prefixes vs 6 sanctioned;
  `index.md` covers 14/279.
- **Samples catalog** (CATALOG.md, frozen 2025-10) advertises four never-built samples as "In
  Development" and omits four real ones.

### 2.4 The enforcement gap — mature test platform, voluntary execution
The test *design* is genuinely advanced (FilterConvergence differential oracle, VectorAdapterSurface
matrix, Jobs 5-tier TestKit, Web AdapterSurface ×8 backends, KoanIntegrationHost canon per
ARCH-0079). But:

- **39 of 87 test projects are not in `Koan.sln`** — `dotnet test Koan.sln` (green-ratchet leg A′)
  silently skips ~45% of the fleet, including most FilterConvergence connector suites, both
  Storage suites, Media.Core.Tests, and the entire Web AdapterSurface matrix.
- **CI is disabled** (5 of 7 workflows are noop placeholders). The only active workflow releases
  to nuget.org on push to main, gated by **build only — zero tests**.
- Whole pillars have nothing: Messaging/RabbitMq (zero broker-backed tests — acknowledged
  ARCH-0079 violation), Scheduling, Secrets, Cqrs, Direct, Backup (5k LOC), Storage replication
  (~2.2k LOC of distributed-correctness code), S3 connector, OpenApi/Swagger/GraphQl, Orchestration
  (tombstone `InternalsVisibleTo` proves its tests were deleted).

### 2.5 Cross-pillar gravity leaks
- **Kernel inversion**: Koan.Core (and Core.Adapters) take project references on
  Koan.Orchestration.Abstractions — the kernel depends on a package its own ADR (ARCH-0077)
  condemns; Core.Adapters patches missing types into foreign namespaces (`MissingTypes.cs`).
- **Product-in-the-data-plane**: mainline connectors (Mongo, Weaviate, S3, Ollama) hard-reference
  `Koan.ZenGarden.Core`; the S3 provider is functionally a ZenGarden/Moss client (presign throws
  without a Moss endpoint).
- Koan.Web hard-references Koan.Scheduling for one well-known endpoint; Cache packages reference
  Data.Abstractions solely for `ProviderPriorityAttribute`; AI contracts are split across three
  homes (Koan.Core/AI, AI.Contracts, AI.Contracts.Shared); Orchestration service discovery is the
  *largest directory inside Koan.Core* (2,460 LOC).
- **Three `KoanServiceAttribute` types** (Orchestration / ServiceMesh / Web.Auth.Services) and
  **three service-discovery stacks** (ServiceMesh UDP, ZenGarden Koi, Core.Orchestration).

### 2.6 Same concept, rebuilt per pillar
- **Cross-node signal**: Jobs' `JobReadySignal`, Cache's coherence channels, Redis pub/sub —
  the same origin-echo-filtered fire-and-forget wake, built three times.
  (`ICoherenceChannel<T>` even documents its own intended promotion to Koan.Core.)
- **"Copy data elsewhere"**: Data Transfers builders, Backup restore, Cqrs outbox mirroring.
- **Two outboxes**: Cqrs IOutboxStore (untested, unconsumed) vs Jobs durable ledger (tested).
- **Client-side query fallback**: Redis/Json/InMemory each hand-roll the same
  materialize-all + InMemoryFilterEvaluator loop.
- **~80% twin repositories**: ElasticSearch vs OpenSearch connectors (the DATA-0097 extraction
  captured only the filter translator).

## 3. Pillar-by-pillar verdicts

Condensed; full detail per area in [evidence/](evidence/).

### Core & bootstrap (`Koan.Core`, `.Adapters`, `.Registry.Generators` — ~13.5k LOC)
**Verdict: cohesive kernel inside a grab-bag shell.** The post-redesign kernel (KoanModule,
KoanRegistry + source-gen, Capabilities, Provenance, KoanEnv, Guard, Options) is tight, tested,
ADR-cited. Around it: orchestration discovery (largest directory), OTel wiring forcing 5 OpenTelemetry
packages on every app, a dead fluent background-services control plane (the project's only TODOs),
other pillars' vocabulary in constant catalogs, and `Koan.Core.Adapters` — a package whose defining
abstraction was deleted, with README "validated" for a type system that no longer exists, and a
layering-inverting dependency on Data.Abstractions.

### Data core (`Abstractions`, `Core`, `Relational`, `Direct`, `Cqrs`, `Backup`)
**Verdict: the strongest pillar, with three decaying satellites.** One capability vocabulary, one
filter AST with a single pushdown owner, one encoding contract (DATA-0100), fresh ADR trail the
code visibly cites; canon-grade tests. Seams: `Entity<T>` is a 948-line / 77-member overload matrix
(partition-string variants duplicate `EntityContext.Partition()` scoping); the naming subsystem is
~16 types across two generations; duplicate `EntityMetadataProvider` (one in global namespace);
Direct/Cqrs/Backup are unconsumed or single-consumer, untested, pre-renovation.

### Data connectors (10 under `Connectors/Data`)
**Verdict: best-covered area; uniform anatomy; drift at the edges.** All follow one canonical
shape through one capability model. Gaps: Postgres ships `UpsertMany` but doesn't declare
BulkUpsert; Couchbase missed the CAS/FastRemove/TTL waves; Redis declares `FilterSupport.Full`
honored by full-keyspace SCAN + client-side filter (dangerous at scale); ES/OS are misfiled vector
adapters and ~80% twins; byte-identical `SchemaDdlPolicy`/`SchemaMatchingMode` enums copy-pasted
across the relational trio; every adapter wires services twice (manual `XRegistration` +
`KoanAutoRegistrar`); SqliteRepository.cs is a 1,732-line monolith.

### Vector & search (`Data.Vector` + Abstractions, `SearchEngine`, 6 connectors)
**Verdict: recently hardened core, one experimental sub-surface, one broken connector.**
Residual-is-error invariant enforced at a single choke point; uniform ~25-spec matrix per adapter.
Warts: three naming/placement conventions for six connectors; Abstractions depends on Data.Core
(contradicting its own packaging claim); the VectorWorkflow/Profiles parallel API has zero adopters;
`Vector<T>` is a pure delegation wrapper over `VectorData<T>`; PGVector doesn't compile (parked on
a branch, csproj says so); `IVectorFilterTranslator`'s only implementor is that broken connector.

### Web core (12 packages, ~18k LOC)
**Verdict: strong nucleus, halo of splinters.** EntityController → IEntityEndpointService → hook
pipeline is one well-ADR'd spine, load-bearing beyond HTTP (MCP executes through it). The halo:
OpenApi+Swagger split (one consumer between them), Admin pair (headless half exists for a console
UI never built), four near-orphans (Transformers, Json.Strict, WebSockets, GraphQl) whose combined
real consumption is one archived sample. Koan.Web integrates with Transformers via three
`Type.GetType` reflection shims — deliberate decoupling signaling a wrong package boundary.

### Auth & security (Web.Auth + Roles + Services + 5 connectors + Security.Trust)
**Verdict: mid-rotation around a new center.** Security.Trust (SEC-0001) is the best-engineered
small project in the repo — every file cites its ADR section, tested unit+integration. Around it:
the live auth controller can't complete OIDC (501) while Google/Microsoft connectors *only* make
sense for OIDC — config and capability drifted apart with no integration test to catch it; SAML is
stub surface smeared across options/merge/health; Roles is an 861-LOC orphan; provider connectors
are ~25 LOC of defaults in ~50 LOC of identical scaffolding, with contradictory duplicate
assembly-level descriptors.

### Cache (7 projects + tombstone)
**Verdict: the reference pillar — this is what "settled" looks like.** Clean three-band layering,
storage strictly separated from coherence, invariants enforced in one place and self-checked in the
boot report, ADR supersession chains explicit, CrossEngine behavior oracle, an analyzer guarding
its own registration pitfall (KOAN0001). Residue: Gen-1 registrar chain (~350 LOC), tombstone dirs,
dead `InternalsVisibleTo`, Analyzers + 2 test projects missing from the sln.

### Jobs / Messaging / Scheduling
**Verdict: three generations stitched together.** Jobs (JOBS-0005) is the freshest, most disciplined
code: 5-tier shared test suite, KoanModule, CAS claim, TTL retention. Messaging.Core is
pre-redesign (per-send reflection dispatch, AppHost.Current locator, static registries, dead surface,
11 ADRs describing features that no longer exist; **zero broker-backed tests**). Scheduling is an
OPS-0050 fragment frozen at "Phase 1" — cron never came; 4 of 10 contracts referenced nowhere;
zero tests; strictly subsumed by JOBS-0005's Schedule; kept alive by two trivial consumers and a
Koan.Web project reference.

### Media / Storage / Tagging
**Verdict: clean layering, untested availability machinery, one orphan facet.** Media↔Storage
separation is genuinely right (derivations are storage entities with lineage — MEDIA-0007). But:
~2.2k LOC of replication/WAL resilience code has zero tests; the scheduled MEDIA-0008 deletion of
the obsolete output-cache family never executed; Media.Core carries a dead Koan.Web reference and
archived-sample demo entities; S3 connector is ZenGarden-entangled, buffers entire objects into
memory, untested; raw-blob HTTP serving lives in Media.Web forcing ImageSharp/Skia on blob-only
apps. Tagging is a competent 494-LOC Data-pillar facet with zero in-repo consumers and XML docs
citing a foreign app's conventions and a nonexistent ADR.

### AI (19 projects)
**Verdict: three strata wearing one pillar's name.** (1) Settled core: Contracts → AI → Data.AI +
Ollama/LMStudio + AI.Web — consumed by 5 samples and 2 services, well-tested. (2) A big-bang
vertical family (Agents/Compute/Eval/Models/Orchestration/Prompt/Review/Training + 2 connectors),
**all born 2026-05-16** implementing the AI-0022..0031 vision: coherent design language, near-zero
consumption, and Training/Eval are facades whose only runtime behavior is to throw (their providers
live in the external ZenGarden orchestrator, whose connector isn't in the sln). (3) Rag incubator
(8k LOC, zero consumers, no tests). Plus: AI.Review has *no AI dependency at all* (generic entity
HITL wearing the AI namespace); contracts in three homes; dead `Pipelines/` surface in Koan.AI.

### Orchestration (7 projects, ~5.7k LOC)
**Verdict: well-groomed code on a condemned foundation.** ARCH-0077 (2026-05) already decided:
retire the bespoke layer (generator, planner, CLI, attributes) for Aspire. Since then maintenance
has been cosmetic; tests were deleted (tombstone `InternalsVisibleTo` remains); live samples bypass
the CLI entirely with hand-written compose files; the Aspire package bundles an unrelated
docker-shelling "SelfOrchestration" subsystem; nothing implements the SPI markers the generator's
own diagnostic enforces. Carrying the decision unexecuted is pure rot risk.

### Periphery & services
**Verdict: the attic.** Canon (Domain+Web) is real, tested, ADR-backed — but drags 3 fossil
directories and an orphan test project from its Flow→Canon history. MCP is real and consumed
(6.4k LOC; minor: duplicate ICodeExecutor interfaces, dual registrars). ZenGarden is a satellite
product bridge whose contracts are load-bearing in mainline adapters, with a **119,907-byte
single-file client class**. Secrets: complete, documented, dormant — zero tests, zero consumers,
reflection-wired into boot from Data.Core. ServiceMesh/Translation/Inbox: an experiment with no
ADR, no tests, one demo, one dead service. KoanContext: the repo's largest project, outside the
sln, with 4 unit specs Compile-Removed due to drift and self-contradictory status docs
("Production-Ready" vs "74%, C+").

## 4. The supporting corpora

### Docs (27 subdirs, ~620 files)
The living core is **6 dirs**: `decisions`, `guides`, `architecture`, `reference`,
`getting-started`, `support`. Ten dirs hold ≤2 files; at least six serve the same pre-decision
role (`proposals`, `design`, `implementation`, `specifications`, `patterns`, `research`);
`prior-art/` is actually a defensive-publication portfolio. ADR quality is *inverted* relative to
hygiene: Gen-2 ADRs (ARCH-0086, DATA-0100, JOBS-0005) are exceptional — empirical probes, staged
ledgers, named deciders — but abandoned the repo's own frontmatter template, so machine-readable
status is degrading exactly as content quality peaks. 38 AI ADRs are almost all "Proposed" — a
large speculative vision layer in the decision corpus. Six large working files (104KB
`_inventory.md`, 44KB `ARCHAEOLOGY.md`, …) sit loose at docs/ root.

### Samples (~20 entries; 8 are ghost dirs)
Two-tier reality, verified by building: the 12 projects inside `Koan.sln` ride the green-ratchet
and stay current; the 4 outside rot on schedule (S8.PolyglotShop: CS1061 on a removed API;
S6.SnapVault: NU1605; root S8.Canon: 77 duplicate-attribute errors; S7.Meridian: builds only
because hand-restored days ago). The learning ladder S0→S1→S10→S14 is genuinely good and
CI-protected; then it jumps straight to large dogfood apps with stale or missing READMEs
(S18.Prism — the "Expert ✅ Active" flagship — has *no README*). Messaging's only sample was
lobotomized (literal `[REMOVED obsolete OnMessage/OnBatch handlers]` comments in Program.cs).
Eight top-level directories contain only untracked bin/obj. No sample uses KoanModule yet.
Bootstrap idiom varies across samples (StartKoan / AddKoan+AsWebApi / manual AppHost.Current) —
a newcomer cannot infer the canonical Program.cs.

### Tests & build
87 test csproj on disk; 48 in sln. ~2,097 `[Fact]/[Theory]` across 352 files. Four reusable
TestKits + KoanIntegrationHost canon. Quality gates: nullable everywhere, warnings-as-errors in
tests only, **no analyzers beyond in-repo generators, toothless .editorconfig (all suggestions),
no central package management, no coverage gate**. Versioning has three coexisting generations:
deprecated `versions.props` (ARCH-0082), the *active GitHub release workflow still driving it*,
and NBGV (ARCH-0085) declared canonical in-repo — the release path and the canon disagree.
Local muscle is unusually strong (green-ratchet additionally compiles C# blocks in docs); remote
enforcement is absent.

## 5. Debris ledger (safe deletions, verified)

| Item | Evidence |
|---|---|
| `src/Koan.Data.Lucene/` | empty (untracked obj/ only), no git history |
| `src/Koan.Cache.Adapter.Memory/` | empty; code folded into Koan.Cache |
| `src/Koan.Jobs.Core/` | one `.lscache` file; superseded by JOBS-0005 |
| `src/Koan.Flow.Core/` | one orphaned TECHNICAL.md; pillar deleted |
| `src/Koan.Context/` | one stray 31KB `IndexingService.cs` (tracked!), compiled by nothing |
| `src/Koan.Canon.Core/`, `src/Connectors/Canon/` | untracked bin/obj only |
| 8 ghost sample dirs | S4.Web, S7.TechDocs, S8.Location, S9.Location, S12.MedTrials.*, S13.DocMind.Tools, KoanAspireIntegration.AppHost — 0 tracked files each |
| Orphan test trees | `tests/Suites/Canon/Koan.Canon.Core.Tests` (tests a deleted engine), `tests/Suites/Data/Vector/` (tests a type that no longer exists), `tests/Suites/AI/Core/Koan.AI.Tests` (no csproj), `tests/Suites/Cache/Unit` (only a stale .trx), `tests/Suites/AI/Koan.AI.Core.Tests` (net8.0, references nonexistent packages) |
| `.csproj.lscache` files | ~9 scattered through src/ |
| Repo-root litter | stray `nul` files (root + tests/), `malicious-project.json`, `test-project.json`, `query-*.csx`, `PHASE1/PHASE2_IMPLEMENTATION_RESULTS.md`, `samples/S5.Recs/inspect.json` (committed CLI output) |
| Dead `InternalsVisibleTo` | Koan.Cache (×2), Koan.Web.Backup, Koan.Orchestration.Cli (×2), Koan.Rag — all naming nonexistent test assemblies |

---

*Next: [02-philosophy-dx.md](02-philosophy-dx.md) — what the framework promises, and whether the
developer experience delivers it.*
