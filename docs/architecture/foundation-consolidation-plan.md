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

## 1. The design philosophy

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
  concept count is ≤ a small N.
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
| **0** | **Green ratchet** | merge gate: full solution + every dogfood sample + doc-API lint, all green | — | none (tooling) |
| **1** | **Unified capability model** | `Capability`/`CapabilitySet`/`ICapabilities` + `Caps.*` + `*Support`; prove by collapsing the vector+query+write cluster | 0 | `ARCH-008x` |
| **2** | **`KoanModule`** | one self-describing unit; folds in registrars + 7 interfaces + ~30 `Add*` + bootstrap + self-report | 1 | `ARCH-008x` |
| **3** | **`Ambient` context** | one per-operation context; collapses the ~5 ambient globals (trickiest) | 1, 2 | `ARCH-008x` |
| **4** | **Assembly/pillar consolidation** | merge thin sub-projects now that the module is the unit | 2 | `ARCH-008x` / ledger |

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

- **Done:** design agreed; this plan written.
- **Next:** Facet 0 (stand up the ratchet) **or** Facet 1 / stage 1 (capability-usage deep research).
- Minor cleanup available anytime: `src/Koan.Data.Lucene/` is a stale `obj`-only leftover from the
  `Koan.Data.SearchEngine` rename (0 tracked, 0 in `Koan.sln`) — delete the directory.
