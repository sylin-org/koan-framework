---
title: "Foundation Consolidation Plan — fewer but more meaningful parts"
type: PLAN
status: active
date: 2026-06-01
owner: leonaquitaine
theme: cross-cutting kernel consolidation
---

# Foundation Consolidation Plan — *fewer but more meaningful parts*

> Canonical, self-contained plan for the framework-wide consolidation. It is the seed for the
> per-facet ADRs (proposed `ARCH-008x`). Read this first; each facet then gets its own DDR.

## 0. Why this exists (the moment)

Koan is an opinionated **application meta-framework** (.NET 10): collapse the whole backend stack —
data, web, AI/vector, messaging, cache, media, jobs, orchestration, auth — behind an **entity-first**
model with **Reference = Intent** (a package reference auto-enables functionality) and multi-provider
transparency. Front door: `Entity<T>`, `Data<T,K>`, `Vector<T>`, `[Embedding]`, `Job<T>`.

The architect is the **sole implementor and sole consumer**, dogfooding sample apps (S5.Recs,
S6.SnapVault, S14.AdapterBench, S16.PantryPal, g1c1.GardenCoop, …) to exercise framework surfaces.
Breadth (~80 `src` projects, ~17 pillars) is the chief liability: every internal redesign ripples
across tests/samples/docs/connectors and the coherence tax compounds. With **no external consumers**,
this is the lowest-cost window to **shake the foundations so the patterns settle** — consolidating
toward fewer, more meaningful parts. The dogfood apps are both the consumer surface and the ratchet.

**Frame: the framework so far is a viability exercise — now harden it for v1.** Everything built to
date proved the thesis works (entity-first + Reference=Intent + multi-provider + AI/vector, end to
end, across diverse dogfood apps). The job now is not to add surface, and not even consolidation for
its own sake — it is to **remove the development scaffolding and leave the parts that work, ready for
v1.** Much of the accretion this plan targets — the ~40 capability types, the 7 registration
interfaces, the split-brain `Add*`-vs-auto-discovery, the aspirational AI sub-pillars — is exactly
that scaffolding: it existed to *prove* a capability, not because a real product needs it as a
distinct part. **Consolidation and deletion are the same move seen from two sides.**

## 1. The design philosophy

- **Descaffold, don't just consolidate.** For every part ask: *is this load-bearing for the proven
  product, or scaffolding from the proof-of-viability phase?* Load-bearing → keep / harden / merge.
  Scaffolding (aspirational, exploratory, only-there-to-prove-a-point) → **cut** — do not lovingly
  refactor it. The destination is v1, not a prettier prototype.
- **Count developer-facing CONCEPTS, not projects.** Drive down the ideas a developer must hold;
  internal plumbing may remain many small invisible parts.
- A part earns its place only if a dogfood app reaches for it naturally **or** removing it forces
  ceremony back into an app.
- **The proven template:** the data/query stack already did this — DATA-0095/0096/0097 collapsed N
  filter ASTs + marker-interface dispatch into **one** `Filter` AST + `QueryDefinition` +
  `IQueryRepository`/`IRawQueryRepository`, capability-negotiated, fail-loud (residual-is-error).
  JOBS-0003 did the same for jobs (CRTP `Job<T>` with Context/Result + `Do()`). **We replicate that
  move on the cross-cutting layer.**

## 2. The discipline (non-negotiable)

A sole implementor with total freedom can refactor forever; the danger is **non-convergence**.

- **Drive every collapse from dogfood usage** — collapse where ≥2 real usages prove the overlap.
  Resist the elegant abstraction no app needs (second-system effect).
- **Enforce a green ratchet:** full solution + every dogfood sample + a doc-API lint, all green, or it
  doesn't merge. This is what converts dogfooding from rot-prone into proof-of-collapse. (The
  multi-API test/sample/doc drift repaired in the 2026-06 session is what consolidation *without* a
  ratchet looks like.)
- **Define "settled" up front:** a new dogfood app of a known shape (CRUD / AI-RAG / media /
  event-driven) can be built end-to-end without editing the framework, and the developer-facing
  concept count is ≤ a small N. **This is the v1 bar:** the proven surface, scaffolding removed,
  patterns settled — not feature-complete, but coherent and stable enough to stand as v1.
- **Watch over-fitting:** sole-consumer bias means "meaningful" can quietly mean "meaningful to
  S5/S6/S14" — keep the dogfood set diverse in shape.
- **Keep the ADR/DDR trail** through the shake-up. Future-you is also a consumer who needs the *why*.

## 3. The consolidation map (surface audit, 2026-06)

Leverage is in the **cross-cutting primitives every pillar re-implements**, not the pillars. Ranked:

1. **Capability / self-report — flagship smell.** ~40 ad-hoc capability types, no unified model:
   three different types literally named `Capability` (class, enum, record), three `ZenGardenCapability`,
   three `AiCapability`, plus per-pillar `*Capabilities` enums (`Query`, `Vector`, `Write`, `Messaging`,
   `Orchestration`, `Security`, `Health`, `Configuration`, …), records (`Filter`, `VectorFilter`,
   `CacheStore`, `Repository`, `StorageProvider`, `Transaction`, `Coherence`, `Exporter`), and
   interfaces (`IQuery`, `IVector`, `IWrite`, `IMcp`, `ICapability`).
2. **Registration / discovery — split brain.** 86 `KoanAutoRegistrar` impls + 7 registration
   interfaces (`IKoanAutoRegistrar`, `IKoanInitializer`, `IKoanManifest`, `IKoanStartupService`,
   `IKoanAspireRegistrar`, `IKoanAdminManifestService`, `IKoanAuthEventContributor`) + ~30 manual
   `Add*` extension methods. Auto-discovery vs explicit `Add*` is unreconciled.
3. **Ambient context / state — ~5 globals.** `AppHost.Current` (SP + `PushScope`), `EntityContext`
   (data routing), `KoanEnv` (~65 sites), an AI AsyncLocal context, a Cache `Scope`.
4. **Bootstrap / self-report wiring** — many ad-hoc `*Bootstrap*` contributors + hosted services per
   pillar (ties to #1).
5. **Pillar / project fragmentation** — ~80 src projects; over-split pillars (Web ~11, AI ~12 incl.
   likely-aspirational Compute/Eval/Models/Training).

**Already coherent — do NOT churn:** the front-door facades (`Entity<T>`, `Data<T,K>`, `Vector<T>`,
`Job<T>`, `[Embedding]`); config (`AddKoanOptions<T>`).

### 3a. Verified audit (2026-06-02) — grounding the first-pass map

A six-axis evidence sweep (read-only, file:line) re-checked §3 against the current tree (now **108
`.csproj`**) after the data/filter/naming stack settled. Corrections and additions:

**Reframe — count distinct *intents*, not projects (sharpens #5).** In a Reference=Intent framework
each project *is* an opt-in unit, so "merge the thin projects" is wrong by default: merging
`Koan.Web.Json.Strict` into Web means you can no longer opt into strict JSON without SSE. The merge
test is **"is this a distinct intent a developer would reference alone?"** — *yes* → keep separate
even if tiny (`Json.Strict`, `Sse`, `WebSockets`, per-provider connectors, cache adapter/coherence
packages); *no, internal seam* → merge (`AI.Contracts.Shared`→`Contracts`, `ZenGarden.Core`→
`ZenGarden`, `Orchestration.Cli.Core`→`Cli`, the ceremony abstractions below). Several of the "~80
projects" are correctly invisible plumbing.

**Split-brains that *drift into bugs* (do first — correctness, not aesthetics):**
- **Capability declaration (#1) — three coexisting generations.** Gen-1 (`Koan.Core.Adapters` caps
  surface) + legacy enums/markers + the new token model, kept in sync by hand-written bridges. Same
  bug class the filter-caps triple-representation was. → finish [ARCH-0084](../decisions/ARCH-0084-unified-capability-model.md).
- **Connection-string parsing — 4 orchestration evaluators vs. the canonical `ConnectionStringParser`,
  with behavioural divergence** (Mongo drops multi-host; Redis drops DB extraction; Postgres re-does
  key-normalization; Couchbase ad-hoc host split). Self-contained ~180-line fix.
- **Auth contributors discovered off-registry.** `IKoanAuthEventContributor` uses its *own* reflection
  scan, invisible to `KoanRegistry`/provenance, fails silently. One of the 7 interfaces in #2.
- **Messaging — orphaned static `MessagingTransformers`/`MessagingInterceptors`** never wired into the
  handler lifecycle (`MessagingLifecycleService` reads only `HandlerRegistry`). *Needs confirmation.*

**Refinements to the ranked targets:**
- **#2 registration:** verified **85 `KoanAutoRegistrar`** + 7 interfaces. The "Add* + auto-registrar"
  pairs are *mostly the good pattern* (the registrar calls the Add*) — the real smell is the 7
  overlapping discovery interfaces + fragmented bootstrap, which is the `KoanModule` (Facet 2) target.
- **#3 ambient:** exactly **5 genuine ambient stacks**; `KoanEnv` is **config, not ambient** (don't
  fold it). The win is *one push/pop idiom* (`CacheScopeAccessor` is manual/non-IDisposable, the odd
  one out), not merging genuinely-different payloads.
- **#5 / Facet 4 deletion candidates (evidence-backed):** the 8 `Koan.AI.{Compute,Training,Agents,
  Eval,Review,Models,Prompt,Orchestration}` sub-pillars have a **single consumer (`samples/S18.Prism`)**
  — the sole-consumer over-fit the discipline warns about. Ceremony Abstractions/Core splits to merge:
  `Rag`, `ServiceMesh`, `Recipe` (single implementer). Jobs + Scheduling are two execution engines
  (potential unified `Lane(...).OnCron(...)` — needs the ≥2-usage proof).
- **Zero-risk now (non-project ghosts, not in the 108 csproj):** delete `Koan.Canon.Core`,
  `Koan.Cache.Adapter.Memory`, `Koan.Data.Lucene` (obj-only), `Koan.Flow.Core` (doc-only),
  `Koan.Recipe.Observability` (1-file stub); confirm/relocate the orphaned
  `Koan.Context/Services/IndexingService.cs` (no csproj, compiled by nothing).

**Explicit non-targets (leave alone — discriminating judgment):** the `Guard` migration (~671 inline
null-checks; idiomatic C#, no drift — cosmetic, *not* a split-brain); `AddKoanOptions` adoption drift
(real but low-value — opportunistic, not a facet); the event-payload `*Context` classes (correctly
distinct); the *justified* Abstractions splits (Data/Vector/Cache/Orchestration/Media — multiple
independent implementers); the front-door facades.

## 4. The design — two primitives

The insight: #1, #2, #4, #5 are facets of one question — *how does a unit of functionality describe
itself to the runtime, and how does the runtime compose, negotiate, and report it?* That is **one
descriptor**. #3 is genuinely different (per-operation state) and gets its own primitive.

### 4.1 `KoanModule` — one self-describing unit (subsumes #2, #4, drives #5)

```csharp
public abstract class KoanModule
{
    public abstract string Id { get; }                        // "data.postgres"
    public virtual IReadOnlyList<string> DependsOn => [];      // ordering — replaces [Before]/[After]
    public virtual void Describe(ICapabilities caps) { }       // capabilities — replaces ~40 types
    public virtual void Register(IServiceCollection s) { }     // DI — replaces Add* + KoanAutoRegistrar.Register
    public virtual Task Start(CancellationToken ct) => Task.CompletedTask; // bootstrap — replaces *Bootstrap services
    // self-report is FREE: the runtime renders Describe()'s output into the boot/health report.
}
```

Runtime loop (one pipeline, replacing the half-done variants today): **discover** (assembly scan —
the Reference=Intent backbone) → **topo-sort** by `DependsOn` → `Register` → `Start` → **render the
boot report from each module's capabilities**.

### 4.2 The unified capability model (flagship — lives inside `Describe`)

Generalize the proven `VectorFilterCapabilities`: a set of strongly-typed tokens, with optional typed
detail for the few structured ones. Three verbs — **declare / require / report**.

```csharp
// DECLARE (adapter author, in Describe) — strongly typed, IntelliSense-discoverable
caps.Add(Caps.Write.Bulk)
    .Add(Caps.Write.AtomicBatch)
    .Add(Caps.Query.RawProvider)
    .Add(Caps.Query.Filter, new FilterSupport(Ops.Eq | Ops.Range | Ops.In, ignoreCase: false, nestedPaths: true));

// NEGOTIATE (consumer / framework) — one ask, one fail-loud
if (module.Caps.Has(Caps.Write.Bulk)) { ... }
module.Caps.Require(Caps.Query.RawProvider);                   // -> CapabilityNotSupportedException("data.postgres lacks query.rawProvider")
var f = module.Caps.Detail<FilterSupport>(Caps.Query.Filter);  // structured detail only where a feature needs it

// REPORT — free, uniform
foreach (var c in module.Caps.All) bootReport.Line(module.Id, c);
```

**The collapse:** every `*Capabilities` enum → tokens under `Caps.*`; every `*Capabilities` record →
a `*Support` detail attached to a token; every `I*Capabilities` interface → `module.Caps`. ~40 types →
`Capability`, `CapabilitySet`, `ICapabilities`, + ~3–5 `*Support` records. Fail-loud becomes one
`CapabilityNotSupportedException` (the vector coordinator's "residual is error" promoted to a rule).

### 4.3 `Ambient` — one per-operation execution context (#3)

```csharp
using (Ambient.Push(a => a.Partition("tenant-42").Source("replica")))
{
    await Order.Query(o => o.Total > 100);   // partition/source/adapter flow automatically
}
// Ambient.Current.Services, .Partition, .Source, .Adapter, .Get<T>(slot)
```

Collapses the ~5 ambient globals onto one context with one scoping verb. **Carries scoped/overridable
state only** — truly-static boot config stays in `AddKoanOptions<T>`. Likely **layers over** the SP
rather than replacing `AppHost.Current` (different lifetimes). The many domain event/hook `*Context`
payloads (Auth*Context, HookContext, EntityEventContext, …) are a different concern and stay.

### 4.4 Pillar consolidation (#5) falls out

Once a "feature" is a `KoanModule`, not a *project*, thin sub-projects merge into a handful of
assemblies each exposing several modules. Reference=Intent still works (one package → its modules
auto-discovered); fewer packages to reference.

### 4.5 Premium-DX target (before/after)

Today a Postgres adapter author touches five conventions (`KoanAutoRegistrar`, a caps enum *and*
record, an `Add*Adapter`, a bootstrap hosted service, boot-report wiring). After:

```csharp
public sealed class PostgresModule : KoanModule
{
    public override string Id => "data.postgres";
    public override IReadOnlyList<string> DependsOn => ["data.core"];
    public override void Describe(ICapabilities c) => c
        .Add(Caps.Write.Bulk).Add(Caps.Query.RawProvider)
        .Add(Caps.Query.Filter, FilterSupport.Full);
    public override void Register(IServiceCollection s) => s.AddRepository<PostgresRepository>();
}
```

One file → discovery, ordering, negotiation, self-report, health, all free.

## 5. Execution — composable facets

Each facet is independently shippable and leaves the tree green at **every commit** (its
implementation is internally staged: additive → migrate → delete).

| # | Facet | Scope | Depends on | ADR |
|---|-------|-------|-----------|-----|
| **0** | **Green ratchet** ✅ | one gate: `scripts/green-ratchet.ps1` — build `Koan.sln` (framework **+ samples, which live in the sln**) + tests + `docs-lint.ps1` + diff-scoped `validate-code-examples.ps1` | — | tooling (no ADR) |
| **1** | **Unified capability model** | `Capability`/`CapabilitySet`/`ICapabilities` + `Caps.*` + `*Support`; prove by collapsing the vector+query+write cluster | 0 | `ARCH-008x` |
| **2** | **`KoanModule`** | one self-describing unit; folds in registrars + 7 interfaces + ~30 `Add*` + bootstrap + self-report | 1 | `ARCH-008x` |
| **3** | **`Ambient` context** | one per-operation context; collapses the ~5 ambient globals (trickiest) | 1, 2 | `ARCH-008x` |
| **4** | **Assembly/pillar consolidation + descaffold** | merge thin sub-projects now that the module is the unit; **cut** aspirational/scaffolding pillars (audit AI Compute/Eval/Models/Training etc. against real dogfood use) | 2 | `ARCH-008x` / ledger |

**Order rationale:** Facet 1 is the smallest self-contained win and a clean generalization of proven
code (`VectorFilterCapabilities`) — it sets the rhythm. Facet 2 then becomes "the thing that hosts
`Describe`." Facet 3 is last because merging `AppHost.Current`-class state is where lifetime semantics
get sharp. Facet 4 harvests the simplification Facet 2 creates.

## 6. The per-facet cycle

Each facet (1–4) runs:

1. **Deep research** — enumerate every real usage of the surfaces being collapsed (the "how many ways
   to X" inventory), map which dogfood apps exercise each, and pin **the genuine variation the unified
   model MUST express**. Adversarial check: *"what real usage would this model fail to express?"*
2. **Decision** — resolve the design and its forks; surface architect-level forks explicitly.
3. **ADR** — write the DDR (context / decision / consequences + a staged migration ledger).
4. **Implementation** — **a)** additive foundation (new primitive lands, nothing breaks, conformance
   specs pass) → **b)** migrate consumers one cluster/pillar at a time (green at each) → **c)** delete
   legacy once the last consumer is off it.
5. **Test / settle** — conformance specs pin the primitive; the green ratchet gates every step; **the
   facet's docs/guides are reconciled in the same breath** (so docs never re-drift). Facet exits when
   its "settled" check holds.

## 7. Open forks (decide in-cycle)

- **Module discovery:** build-time generator (AOT / fast-boot manifest — the two existing Roslyn
  generators exist for this) vs runtime reflective discovery.
- **Structured capabilities:** token-with-attached-detail vs separate typed profiles fetched by token.
- **`Ambient` scope:** how far it absorbs vs layers over `AppHost.Current` (lifetime semantics).
- **Naming:** `KoanModule` / `Ambient` are placeholders; note the existing `Koan.Context` pillar may
  collide; `EntityContext` already exists.

## 8. Status

- **Done:**
  - Plan written; viability→v1 framing folded in.
  - **Facet 1 research + decision → [ARCH-0084](../decisions/ARCH-0084-unified-capability-model.md) (Accepted).**
    Capability model designed; the Gen-1 cut and the `TransactionCapabilities` split approved;
    detail-mechanism fork resolved to attach-to-token.
  - **Facet 0 — green ratchet stood up:** `scripts/green-ratchet.ps1` composes build (framework +
    samples) + tests + `docs-lint.ps1` + diff-scoped `validate-code-examples.ps1`. Baseline made
    green: linter false-positive fixed (`.cs#Lnnn` source anchors no longer mis-checked), 18 stale
    cross-references repointed (docs-lint → 0 errors), and the code-example validator rescoped to
    **instructional** surfaces only (net10.0, diff-scoped, `<!-- validate:skip -->` opt-out).
  - **Facet 1 stage (a) — additive foundation landed:** `Koan.Core.Capabilities`
    (`Capability` / `CapabilitySet` / `ICapabilities` / `CapabilityNotSupportedException`) + per-pillar
    catalogs (`DataCaps`, `VectorCaps`) + `FilterSupport` (generalizing both filter records) + the
    enum↔token bridge. 14 conformance specs green; whole-solution build + ratchet green. Legacy enums
    untouched (additive only).
- **In progress:** Facet 1 **stage (b)** — migration.
  - **b1 ✓** adapter declaration contract `IDescribesCapabilities` (signature-aligned with Facet 2's
    `KoanModule.Describe`) + native-or-bridge resolvers (`DataCaps.Describe` / `VectorCaps.Describe`);
    specs green.
  - **b2 ✓ (web negotiation chain):** `EntityRequestContext.Capabilities` / `HookContext.Capabilities`
    `IQueryCapabilities`→`CapabilitySet`; `EntityEndpointService`, `WellKnownController`, and the
    GraphQl connector negotiate via `DataCaps.Describe(repo)` (native-or-bridge); `Koan-Write-Capabilities`
    header + `/.well-known` rendering preserved via the bridge; 4 wrapper records deleted
    (`DefaultQueryCapabilities`/`RepositoryCapabilities`/`RepoWriteCaps`/`RepoCaps`). `Entity.cs`'s
    `FastRemove` hot-path already reads `Data<T>.Capabilities.Has(DataCaps.Write.FastRemove)`. Solution +
    samples green; Web AdapterSurface (InMemory) 56/56. (commit `ff490212`)
  - **b3-prereq ✓ (decouple the consumers):** `RepositoryFacade` + `CqrsRepositoryDecorator` +
    `CachedRepository` now implement `IDescribesCapabilities` (delegate `Describe`→`DataCaps.Describe(_inner)`)
    and no longer read the markers; `CapabilitySet.CopyInto` added as the forward-correct passthrough;
    `Data<T>.QueryCaps`/`WriteCaps` rebased onto `Data<T>.Capabilities` (S10 + docs unchanged); 6 connector
    capability conformance specs moved to the token contract. **The only remaining marker reader is the
    `DataCaps.Describe` bridge fallback itself** — so the adapter flips are now atomic. Solution + samples
    green; Core 176, Data.Filtering 97, Data.Core 111, Cache.Topology 50, InMemory 32, Json 7, Web
    AdapterSurface (InMemory) 56. (commit `5b39cd1c`)
  - **b3 ✓ (all 14 adapters flipped):** the 8 data (commit `a3282d4d`) + 6 vector (commit `653a7a3b`)
    connectors now declare natively via `Describe(ICapabilities)` (DataCaps/VectorCaps tokens mirroring
    their former enums) and dropped the marker interfaces + enum properties; the 5 internal
    `Writes.HasFlag(FastRemove)` self-checks became constant `RemoveStrategy.Fast`; `Vector<T>.GetCapabilities`
    rebased onto `VectorCaps.Describe`. Conformance specs verify each flip end-to-end. The only remaining
    legacy-marker readers are the `DataCaps`/`VectorCaps` bridge fallbacks + the test fakes that pin them.
    Solution + samples green; InMemory data 32, Json 7, Data.Core 111, VectorAdapterSurface 26, Data.Filtering 97.
  - **stage (c) ✓ — the deletion (commits `b8b3486e` code + the docs commit):** deleted the 6 legacy
    types (`Query/Write/VectorCapabilities` enums + `IQuery/IWrite/IVectorCapabilities` markers), gutted the
    `DataCaps`/`VectorCaps` From/To/marker-fallback bridges (`Describe` is now a thin native-only resolver),
    removed the `Caps`/`WriteCapsImpl` wrappers + `Data<T>.QueryCaps`/`WriteCaps`; `Vector<T>.GetCapabilities`
    now returns `CapabilitySet`. The `/.well-known` aggregate + `Koan-Write-Capabilities` header re-render
    token ids from `caps.All`. Migrated `S10.DevPortal` + the 4 test fakes; reconciled the active capability
    guides/reference + added supersede banners to DATA-0002/0003 (historical ADRs left intact). Removed the
    vestigial `QueryCapabilities` field from Gen-1 `AdapterCapabilities`. Solution + samples green;
    Core 176, Data.Filtering 92, InMemory 32, Json 7, VectorAdapterSurface 26, Web AdapterSurface 56.

  **Facet 1 is COMPLETE** (the unified capability model is the sole surface). Follow-ons (all scoped in
  ARCH-0084, independent of the core capability migration):
  - **`TransactionCapabilities` runtime-state split ✓** (commit `87fb98d8`) — `TxCaps` tokens
    (tx.local/distributed/compensation) declared via the coordinator's `Capabilities` set; runtime state
    (`Adapters`/`TrackedOperationCount`) moved onto the coordinator; `EntityContext.Capabilities` →
    `EntityContext.CurrentTransaction`; the `TransactionCapabilities` record deleted.
  - **Gen-1 cut ✓** (commit `9f55e878`) — deleted the abandoned Gen-1 adapter-capability surface
    (`AdapterCapabilities`, `Capabilities.cs` 6 enums, `BaseKoanAdapter`, `IKoanAdapter`, `Templates/*`,
    the dead `OrchestrationRuntimeBridge`); rewrote `LMStudioAdapter` off `BaseKoanAdapter` (now lean like
    `OllamaAdapter`). Kept the load-bearing Readiness subsystem, `AdapterBootReporting`, the
    `[OrchestrationAware]` attribute, and `UnifiedServiceMetadata`. AI.Unit 151/151, AI.EndToEnd 31/31.
  - **FilterSupport sub-facet — OPEN (the one remaining; its own focused pass).** Collapse
    `FilterCapabilities` + `VectorFilterCapabilities` records into the existing `FilterSupport`, attached to
    the `DataCaps.Query.Filter` / `VectorCaps.Filters` token as detail (read via `caps.Detail<FilterSupport>`).
    **Decision: target B (token integration)** — it is what gives the capability model's `Add<TDetail>`/
    `Detail<T>` mechanism its *one intended consumer*; doing only A (type-collapse, keep property) leaves
    that mechanism as dead scaffolding. (`CapabilitySet.CopyInto` is already detail-preserving for exactly
    this, so the facade/decorators propagate the token detail for free.)

    **Executable plan (worked out 2026-06-02; ~40 files, ATOMIC — does not decompose into safe partial
    commits, so do it in one green push):**
    - `FilterSupport` is **structurally identical** to `FilterCapabilities` (same fields, same
      `CanPush(op, collectionField)`, same `None`/`Full`) → the type-collapse is behaviour-preserving. The
      vector form uses `FilterSupport.Uniform(...)` (Scalar==Collection, since vector metadata is schemaless);
      the vector splitter arm calls `caps.CanPush(op, collectionField: false)`.
    - Contracts: `IQueryRepository.FilterCapabilities` and `IVectorSearchRepository.FilterCapabilities`
      → `FilterSupport`; `FilterSplitter.Split` (both overloads) + `FilterPushdownCoordinator.Plan` +
      `VectorFilterCoordinator.Validate` take `FilterSupport`.
    - Sources: `RelationalFilterCapabilities.Default` (→ rename `RelationalFilterSupport`); each vector
      translator's `.Caps` (`SearchEngine`/`Milvus`/`Qdrant`/`Weaviate`/`PGVector`) and the Mongo/Couchbase
      translator caps → `FilterSupport`. The 8 data + 6 vector adapter `FilterCapabilities` properties → `FilterSupport`.
    - For B: each adapter's `Describe` adds `caps.Add(<Filter token>, <its FilterSupport>)`; the coordinator
      callers read `Data<T>.Capabilities.Detail<FilterSupport>(DataCaps.Query.Filter)` (no caching needed —
      same per-call `Describe` cost the FastRemove hot-path already accepts); delete the `FilterCapabilities`
      property from the contract + facade/decorators. Note: vector adapters that fully-qualify
      `Koan.Data.Abstractions.Filtering.VectorFilterCapabilities` need `using Koan.Data.Abstractions.Filtering;`
      added for `FilterSupport`.
    - Delete `FilterCapabilities.cs` + `VectorFilterCapabilities.cs` records + the `FilterSupport.From(...)`
      bridges; update the `FilterConvergence` / `VectorFilterConvergenceSpecsBase` TestKits + the
      `CapabilityBridgeTests` `FilterSupport.From` specs.
    - **Verify with the FULL cross-adapter convergence oracle** (container-backed) — the structural identity
      makes a bug unlikely but the oracle is the proof. This is why it is its own session, not a tail-end rush.

  Next major facet: **Facet 2 — `KoanModule`** (registration + bootstrap + self-report), which hosts
  `Describe` and builds directly on the capability model just landed.
  - **Gen-1 cut (independent of the enum migration):** `LMStudioAdapter` is the one consumer but it
    inherits its *entire* scaffolding from `BaseKoanAdapter` (`Logger`, `GetOptions<T>`,
    `GetConnectionString`, `InitializeAdapter`/`CheckAdapterHealth`/`GetAdapterBootstrapMetadata`), so the
    cut is a real LMStudio rewrite, not just a capability swap. The `Koan.Core.Adapters` **Readiness**
    subsystem + `AdapterBootReporting` are load-bearing and stay — only the capability surface
    (`AdapterCapabilities`, `Capabilities.cs` enums, `BaseKoanAdapter`, `Templates/*`) is cut.
- Minor cleanup available anytime: the non-project ghost dirs (`Koan.Data.Lucene`, `Koan.Canon.Core`,
  `Koan.Cache.Adapter.Memory` — obj-only; `Koan.Flow.Core` — doc-only) carry 0 tracked source and are
  absent from `Koan.sln`; delete the directories.
