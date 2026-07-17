---
type: SPEC
domain: framework
title: "R09 - Compile the Semantic Composition Kernel"
audience: [architects, maintainers, developers, ai-agents]
status: passed
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: semantic application model, typed contribution compiler, capability overlays, compiled evidence, and one module lifecycle
---

# R09 — Compile the Semantic Composition Kernel

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R07; preserves the completed R08-01 release-wave baseline
- Unlocks: resumption of R08 public release readiness on a sustainable V1 runtime architecture
- Owner: Core composition mechanics, pillar-owned plan compilers, capability-overlay contracts, and
  evidence projections

## Meaningful outcome

An application references a capability and writes only the corresponding business intent. Koan
compiles every relevant pillar consequence once, elects providers that can honor it, refuses silent
semantic weakening, and explains the complete result to developers, coding agents, operators, and
reviewers.

The developer does not configure Data, Cache, Communication, Jobs, and Storage independently to make
Tenancy coherent. Tenancy contributes its segmentation dimension once; each active segmentable pillar
compiles a typed realization at its own chokepoint. Each pillar owns enforcement, adapters own
mechanics, and one host-owned model proves coverage.

The runtime result contains fewer decision owners, not artificially less intrinsic complexity.

## Product thesis

> Rails compressed configuration. Koan should compress uncertainty.

The saga makes Koan's internal motif executable:

> Intent over plumbing. Evidence over magic.

Application intent compiles into runtime behavior, available projections, operational truth, and
proof. The concise Entity language remains the common path; the framework machinery stays beneath it.

Every capability slice begins with its business sentence and smallest honest application expression.
The `AddKoan` / `Entity<T>` / `EntityController<T>` path is the golden comparison: visible code should
map as nearly 1:1 as possible to user intent. Extra syntax is admitted only when it expresses a real
business decision, guarantee, or deliberate override. Contributor, election, plan, and projection
machinery is never application ceremony.

## Why now

R07 proved the semantic capability ring and exposed several strong local patterns: host context,
Data axes, pointwise Entity operations, Communication route plans, layered providers, Cache plans,
runtime facts, and direct-reference intent. The next release work would otherwise harden those
patterns as separate long-lived composition systems.

The architect explicitly authorizes greenfield break-and-rebuild before V1. R08-01's release-wave
mechanism is complete locally and remains protected; the remainder of R08 is blocked until this saga
establishes the stable runtime/product foundation it would publish.

## Read first

1. [ARCH-0115](../../../decisions/ARCH-0115-semantic-contribution-compilation.md) — accepted ownership
   and compilation law.
2. [R09 coalescence inventory](../R09-COALESCENCE-INVENTORY.md) — durable current-pattern and decision map.
3. [R09 backlog](../R09-BACKLOG.md) — dependency order without duplicated live status.
4. [R09-03](r09/R09-03-optional-contribution-lifecycle.md) — passed optional-layer and first earned
   typed-contribution implementation slice.
5. [R09-04](r09/R09-04-shared-provider-selection.md) — passed shared deterministic provider-catalog
   slice with typed Data, Communication, and Cache policy.
6. [R09-05](r09/R09-05-hard-segmentation-data-cache-storage.md) — passed hard segmentation-family,
   Data, Cache, Storage, and shared guarantee-evidence slice.
7. [R09-06](r09/R09-06-tenancy-communication-jobs.md) — passed hard context-carriage, provider-trust,
   Communication, Jobs, and async guarantee-evidence slice.
8. [R09-07](r09/R09-07-explanation-correction-semantic-change.md) — passed schema-2 guarantee,
   deterministic startup explanation, correction parity, and bounded model-change slice.
9. [R09-08](r09/R09-08-module-conformance-execution-economy.md) — passed retained-module evidence,
   reporter deletion, multi-host, trimming, and execution-economy slice.
10. [R09-09](r09/R09-09-one-bootstrap-language-and-release-handoff.md) — passed repository-wide
    convergence on one module language, legacy-kernel deletion, and release handoff.
11. [R09-02](r09/R09-02-host-constitution-and-semantic-activation.md) — passed host-constitution and
   semantic-activation implementation slice.
12. [R09-01](r09/R09-01-coalescence-and-decision-inventory.md) — passed focused assessment and exact
   implementation/deletion placement.
13. [Initiative acceptance](../ACCEPTANCE.md) — mandatory coalescence, evidence, and deletion gates.

## Accepted architecture

### One semantic model, distinct information products

One host-owned semantic catalog supplies stable identities to immutable execution plans, runtime
facts, health, bounded operation receipts, telemetry correlation, and semantic diffs. These remain
distinct products with distinct lifetime, cardinality, and security.

### One generic compiler, typed pillar languages

Core owns activation filtering, deterministic dispatch, dependency qualification, collision handling,
decision reasons, host ownership, and plan freezing. Data, Cache, Communication, Jobs, Storage, Web,
and other pillars retain typed builders, combination policy, validation, and failure semantics.

### Capability overlays

A capability contributes either to a typed capability-family model or directly to an earned
pillar-specific target. Tenancy is the flagship hard family overlay: it declares one dimension and
active pillars prove their realizations. ZenGarden is the optional layered-provider archetype.
Compatibility declaration is inert. Direct activation is explicit. Hard obligations fail closed.
Optional candidates follow their pillar's fallback policy.

### Compile once, run plans

Contributor objects run only during host composition. Structural plans are compiled eagerly where
known or lazily once per legitimate host-owned shape. Runtime operations execute immutable delegates
and read ambient values; they do not enumerate contributors, negotiate providers, or mutate global
registries.

### Specificity cascade

Framework law is shared only while meaning is identical. Capability-family mechanics sit above
pillar policy; adapters remain thin physical realizations; business policy stays in the application.
Every slice must explicitly decide what coalesces and what remains specific before production code.

## Information-plane guardrail

| Plane | Canonical responsibility |
|---|---|
| Semantic catalog | entities, capabilities, bounded-context ownership, exposures, requirements, stable identities |
| Host plan | selected providers, routes, contribution folds, direct execution paths |
| Runtime facts | safe current explanation of decisions and unsupported obligations |
| Health | present availability of selected mechanisms |
| Receipts | bounded outcome of one semantic action |
| Telemetry | causal and aggregate history through ecosystem standards |
| Semantic diff | changed application meaning between exact models |

No slice may turn facts into telemetry history, health into capability proof, or a projection into a
second decision owner.

## Decisions

### DECIDED

- `Entity<T>` remains the application semantic spine; this saga changes internal ownership, not the
  business-first product grammar.
- Public semantics are designed before internal machinery. Every child records the business sentence,
  smallest honest C# expression, runtime guarantee, and corrective failure before choosing types.
- Human readability, IntelliSense discovery, and coding-model legibility are acceptance dimensions;
  a technically generic API that leaks framework mechanics fails the common-path test.
- Break-and-rebuild is mandatory when current structures produce duplicate decision owners,
  cross-pillar leakage, mutable process state, or hot-path composition.
- There is one generic contribution compiler over typed targets, not one universal contribution DSL.
- Pillar runtimes compose the compiler; they do not inherit from a framework pipeline base class.
- Capability-owned requirements and provider-owned realization claims remain independently provable.
- Direct application intent is distinct from transitive contract availability.
- A hard active capability cannot silently disappear on an unsupported pillar/provider path.
- Context integrity, logical isolation, route segmentation, physical isolation, and confidentiality
  are separate guarantees.
- Plans are host-owned, immutable, memoized by structural shape, and AOT-friendly.
- Startup, MCP, HTTP, CLI/workbench, tests, and future semantic diffs project canonical decisions.
- Replaced paths are deleted without compatibility aliases that preserve two canonical owners.
- The neutral operation model is recorded for V1.1 and is not smuggled into this V1 kernel.

### DEFAULT

- Compile genuinely cross-pillar meaning into a typed capability-family model. Publish a
  non-activating pillar target only for a real pillar-specific contributor; do not create one contracts
  package per pillar speculatively.
- Compile all statically known plans during host composition and retain a bounded host-owned memoized
  fallback only for legitimate dynamic shapes.
- Preserve current public Entity grammar unless a slice proves that the grammar itself obscures
  business intent or a guarantee.

### CLOSED BY R09-01

- Core owns `IContributeTo<TTarget>`, immutable generated descriptors, host activation, deterministic
  dependency/collision mechanics, decisions/problems, and the structural plan cache.
- Cross-pillar segmentation compiles once as a capability-family model; Data, Cache, Communication,
  Jobs, and Storage own typed realizations. Tenancy does not reference every pillar implementation.
- Existing inert abstractions host an earned public pillar target; new contracts projects require a
  real direct consumer.
- The semantic catalog owns declared/active/satisfied meaning; immutable pillar plans own execution;
  facts/health/lock/diff are projections with distinct lifetimes.
- Process-static mutable registries are not host semantic authorities. Only immutable generated/type
  facts may remain process-cached.
- Data and Communication share candidate identity, activation, qualification outcomes, deterministic
  ordering/collision, and decision receipts—not a fixed selector or failure policy.
- Cache and Storage own their segmentation identities/plans and stop reading Data registries.
- Plans are eager for finite known shapes and host-memoized single-flight for legitimate structural
  dynamic shapes; ambient values are never plan keys.
- V1 needs stable decision/problem/correction identities and bounded model diff, not a universal
  workbench or operation model.

### OPEN FOR THE FIRST DYNAMIC-PLAN CARD

- Set the absolute allocation/reflection baseline and budget when the first real dynamic plan supplies
  an honest workload. R09-02 proves structural idempotence without inventing a speculative hot-path
  budget.

### CLOSED BY R09-02 EXPLORATION

- Bundle activation follows ordinary project/package dependency edges. One shared build tool returns the
  source graph and generates deterministic `buildTransitive` package evidence; the
  existing embedded reference resource becomes a versioned roots/edges manifest. No runtime NuGet
  scanner or authored duplicate bundle props is admitted.
- Host module activation, typed contribution compilation, and structural plan caching are distinct
  lifecycle phases. R09-02 ships the constitution plus one real Communication module lifecycle;
  ZenGarden earns the first typed contribution surface; a legitimate dynamic Data shape earns the
  plan cache.

### CLOSED BY R09-03 EXPLORATION

- The exact retained generated `KoanModule` is the structural contributor. It implements an
  invariant hidden `IContributeTo<TTarget>` contract and reuses the module's one activation owner,
  identity, factory, instance, constitution order, host lifetime, startup, and reporting lifecycle.
  There is no parallel contributor class, registry, dependency graph, or memoization system.
- Generated module descriptors carry only closed target bindings and direct apply delegates. This is
  construction-free dispatch metadata; reflection supplies the equivalent only in degraded fallback.
- Target compilers drain once after application declarations and before the outer composition freezes.
  Every typed builder validates/freezes before any target plan is published. Runtime sources remain
  dynamic DI services and never rediscover or reapply structural contributors.
- ZenGarden's complete common path is its direct package reference plus `AddKoan()`. Adapter service
  names/aliases replace engine-specific offering bindings; automatic unavailability may fall through,
  while explicit `zen-garden://...` intent must resolve or fail correctively.

### CLOSED BY R09-03 IMPLEMENTATION

- The exact retained active module now supplies generated typed contribution bindings. Concern-owned
  builders compile and freeze before publication; runtime plans contain direct source types and never
  rediscover structural contributors.
- Core owns one immutable service-discovery source plan while adapters retain precedence, protocol
  normalization, health qualification, automatic fallback, and final election facts.
- ZenGarden now activates only through implementation-package intent plus `AddKoan()`. Its manual
  activation extension, offering bindings/map, and per-request contributor chain are deleted.
- Explicit ZenGarden intent fails closed in Mongo, Weaviate, and Ollama; automatic unavailability still
  follows each adapter's documented fallback policy.
- R09-04 may share deterministic selection mechanics only after its focused Data/Communication
  counterexample assessment proves identical meaning. Typed requirements and outcomes remain outside
  a shared engine.

### CLOSED BY R09-05 IMPLEMENTATION

- Tenancy contributes one hard `tenant` dimension. Core owns stable identity, applicability, tri-state
  binding, correction, and host/type memoization; it knows no pillar encoding or tenant value.
- Data, Cache, and Storage own direct executable realizations at their existing chokepoints. Ambient
  values bind once per operation and unsupported paths reject before physical I/O.
- A Data-local managed field is not presumed cross-pillar. Cache excludes it until the owning
  capability explicitly joins Core segmentation; unknown scope is never omitted from identity.
- One hidden `ISegmentationRealization` receipt seam projects stable `enforced-or-rejected` coverage.
  Startup, HTTP, and MCP use the same fact envelope without tenant values.
- `TenantAxis`, `TenantStorageGuard`, `ScopedEntityCacheKey`, and `StorageKeyScoper` are deleted without
  aliases. Native container/database placement remains unsupported rather than overstated.
- Communication and Jobs remain unclaimed by this closure. R09-06 must distinguish logical context,
  route/binding segmentation, physical topology, confidentiality, ledger, and handler guarantees.

### CLOSED BY R09-06 IMPLEMENTATION

- Core joins hard segmentation to opaque context carriage once per subject. It validates required
  coverage and capture, restores and re-binds, and remains ignorant of tenant and physical mechanisms.
- Communication and Jobs own thin typed plans at their existing terminal/ingress chokepoints. No new
  application grammar, envelope, tenant-specific ledger, or pillar dependency fanout was introduced.
- Provider ingress trust is immutable descriptor evidence and participates in Communication election.
  The local floor is host-trusted; RabbitMQ is authenticated only after HMAC verification.
- Facts distinguish enforced logical context from typed routing, shared topology/ledger, Data-owned
  work isolation, confidentiality non-claims, remote settlement, and at-least-once execution.
- Tenancy's canonical module identity now matches source/package intent. Focused Core, Tenancy,
  Communication, RabbitMQ, and Jobs cells pass without release certification.

### CLOSED BY R09-07 IMPLEMENTATION

- Schema 2 makes guarantee a first-class fact meaning. Segmentation, Communication, and Jobs project
  concern-owned guarantees without Core parsing their codes or content.
- Startup compiles one deterministic view from the canonical host envelope. HTTP and MCP serialize that
  same envelope exactly; rejected reason/correction fields remain stable across audiences.
- The resolved lock model remains the bounded semantic-change artifact and comparer. No fact history,
  prose diff, or second scanner was added.
- The public composition guide no longer invites third-party use of the reporting-only contributor
  scanner. Its six sources plus registry/Activator construction become R09-08's explicit deletion and
  generated conformance target.

## Scope

### In

- Mass inventory of current decision, registry, contributor, election, context, facts, cache, storage,
  and plan owners.
- One canonical semantic catalog and immutable host-plan boundary.
- One reusable typed contribution compiler with deterministic activation and evidence.
- Shared election mechanics where Data/Communication proof supports them, with typed policies retained.
- Tenancy coverage across active Data, Cache, Communication, Jobs, and Storage paths.
- Honest distinction among logical, routing, physical, and confidential segmentation.
- Structured decision reasons and corrective failures projected through existing facts surfaces.
- Stable semantic identities sufficient for upgrade comparison and later operation evolution.
- Third-party contributor/module conformance and direct/transitive activation rules.
- Multi-host, trimming/AOT, hot-path, privacy, and meaningful-step proof.
- Deletion of superseded registries, scanners, decision paths, and cross-pillar shortcuts.

### Out

- The V1.1 neutral operation model, mediator, workflow engine, or universal transaction coordinator.
- Automatic destructive schema or contract migration.
- Building an observability backend, agent orchestration platform, or Aspire replacement.
- Inventing Region, Residency, Classification, or Retention features merely to populate the abstraction.
- Public NuGet publication, remote mutation, tag, release, or R08 support-window decision.
- Preserving pre-1.0 APIs solely because they exist.

## Meaningful application proof

The golden comparison remains one visible concept per application decision:

```csharp
builder.Services.AddKoan();
public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

Cross-cutting capability activation remains similarly business-centered:

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order> { }

using (Tenant.Use("acme"))
{
    await order.Save();
    await order.Transport.Send();
}
```

The application does not compose cache keys, storage prefixes, context envelopes, queue partitions,
provider capabilities, or diagnostic records. Koan proves which of those consequences are active and
refuses a path that cannot satisfy the required isolation.

Removing Tenancy removes its contribution bundle and restores the baseline without leftover behavior.

## Provisional slice order

The dependency-ordered outcomes live in [R09-BACKLOG.md](../R09-BACKLOG.md). Only the current slice
receives an implementation card. The initial order is:

1. focused coalescence and decision inventory;
2. host constitution and immutable module-activation boundary with one useful Communication/facts
   vertical;
3. optional contribution lifecycle proved by ZenGarden activation, introducing the first earned typed
   contribution target;
4. shared deterministic selection mechanics with typed Data/Communication policies;
5. hard Tenancy overlay across Data, Cache, and Storage;
6. Tenancy context/segmentation across Communication and Jobs;
7. unified explanation, corrective problems, and semantic-change evidence;
8. module/contributor conformance, AOT, multi-host, and meaningful-step ratchet;
9. final deletion, clean-room local-to-production proof, and R08 resume decision.

Slices may coalesce when discovery proves one owner, or split when one card cannot produce an
independently meaningful result. They may not become preparatory scaffolding.

## Verification strategy

- Ordinary work runs only the focused owner and consumer cells named by the active child.
- Architectural claims add a bounded cross-pillar matrix, not recurring release certification.
- ZenGarden proves optional declaration/activation/fallback/removal.
- Tenancy proves hard activation, cross-pillar coverage, trusted ingress, and fail-closed gaps.
- Multi-host tests prove no mutable plan or contributor state leaks between application hosts.
- Hot-path probes prove no contributor enumeration, reflection, provider negotiation, or plan mutation.
- Direct/transitive package fixtures prove contract presence cannot hijack activation.
- Startup/facts/MCP/operator projections are checked against the same plan identity.
- FirstUse and GoldenJourney remain unchanged unless a meaningful application outcome requires change.
- The full public-release ratchet runs only when R08 resumes or risk explicitly requires it.

## Acceptance additions

- Every logical block begins with the focused coalescence assessment in `ACCEPTANCE.md`.
- Every child states the ideal business sentence, smallest honest C# expression, and exact guarantee
  before internal design; each additional public concept has a semantic justification.
- Contributor, provider-election, compiled-plan, and evidence-projection mechanics remain absent from
  the common application path and application-authoring agent instructions. Framework-maintenance agents
  receive that vocabulary only in architecture/extension-author evidence.
- Every compiled decision has one owner, stable identity, reason, and truthful unsupported state.
- The implementation contains no central pillar switch, universal contribution payload, or runtime
  contributor chain.
- A hard capability's coverage matrix names every applicable active pillar and refuses gaps.
- Optional and hard archetypes use the same mechanics without sharing fallback policy.
- Entity, generic Cache, Storage, Communication, and Jobs tenancy behavior have exact, separately named
  guarantees rather than one unqualified `tenant-safe` claim.
- Known plans are immutable and host-owned; runtime binding of ambient values does not recompile them.
- Replaced paths and stale documentation are deleted before each child passes.
- A coding agent can identify the extension contract, active decision, correction, and focused proof
  from repository/runtime evidence without private context.
- An operator can explain active capabilities, provider realization, degraded or rejected guarantees,
  and safe correction without inspecting application internals.

## Closure

R09-01 through R09-09 pass. The saga leaves one host-owned semantic constitution, one generic typed
contribution compiler, one shared provider catalog, pillar-owned immutable plans, hard Tenancy coverage,
canonical guarantee/explanation projections, and one retained `KoanModule` lifecycle per functional
implementation assembly.

The closing break-and-rebuild removed the parallel initializer/auto-registrar kernel rather than adapting
it. Standard package and assembly identity drive activation; cross-module contracts live in isolated
contract assemblies; ordinary references need no activation metadata. [ARCH-0116](../../../decisions/ARCH-0116-one-module-lifecycle.md)
records that final bootstrap constitution.

Focused owner/consumer, source/staged-package, current-sample, and FirstUse/GoldenJourney proofs are recorded
in [R09-09](r09/R09-09-one-bootstrap-language-and-release-handoff.md). Release certification and publication
remain R08 responsibilities and did not run during this closure.

## Stop conditions

- Stop a generic design that requires Core to name pillar semantics or applications to know contributors.
- Stop a design that builds per request, tenant, Entity instance, message, or cache operation.
- Stop any path that treats context carriage as routing, authorization, or confidentiality proof.
- Stop if a contributor can relax a capability owner's hard invariant or self-certify an adapter.
- Stop if a projection recomputes composition or becomes a second source of truth.
- Stop if compatibility leaves two canonical decision owners.
- Stop before publication, push, tag, release, external mutation, or private downstream inspection.

## Session protocol

The initiative's [PROGRESS.md](../PROGRESS.md) is the only live ledger and [NOW.md](../NOW.md) is the
only handoff. The R09 inventory records architecture findings, not status. Close one child completely
before claiming the next.
