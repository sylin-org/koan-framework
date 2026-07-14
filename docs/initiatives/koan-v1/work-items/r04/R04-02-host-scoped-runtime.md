---
type: GUIDE
domain: core
title: "R04-02 - Make Runtime State Host-Scoped"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-02 — Make runtime state host-scoped

- Priority: P0
- Status: `in-progress`
- Depends on: R04-01
- Owner: Core hosting with Data.Core/AI consumers

## User-visible failure

Before R04-02, repeated integration hosts and some static Entity paths could retain a disposed service
provider. The original Data/AI lifecycle process failed with `ObjectDisposedException: IServiceProvider`;
static lifecycle registries and relationship metadata retain the same risk shape until audited.

## Personas

Developers see flaky tests or failures after restart; agents may diagnose the wrong host; operators
cannot trust lifecycle isolation in workers or reload scenarios.

## Current evidence

- Before the first repair, the self-executing Data.AI suite reported 79 tests: 48 passed and 31 failed.
  The root failure was `EmbeddingMetadata` resolving a logger from a disposed `AppHost.Current` in its
  static initializer; the poisoned type initializer caused the remaining cascade.
- `AppHost.Current`, static closed-generic registries, and cached relationship metadata are reachable
  from Entity paths.

## First increment — host binding and Data.AI capture repair

- The generic-host binder now owns a disposable `AppHost` lease from start through stop.
- A newer host replaces the process default; an older host cannot clear it; releasing the newest host
  never resurrects a predecessor.
- `AppHost.PushScope` remains the explicit provider selector for parallel execution flows.
- `EmbeddingMetadata` and `EntityAi` retain only immutable metadata statically and resolve logging at
  operation time.
- A real two-host in-memory probe reuses the same closed-generic Entity path and proves provider and
  storage isolation.
- The repaired Data.AI suite passes 80/80 as one process; the complete Core suite passes 195/195.

At that checkpoint, `VectorModelGuard`'s confirmation cache, runtime registration sets,
relationship/lifecycle metadata, `AppHost.Identity`, and the non-hosted `StartKoan()` binding path
still required owner-specific classification and proof.

## Second increment — durable vector-model confirmation

- Removed `VectorModelGuard`'s process-wide `(entity, partition, model)` confirmation cache.
- Every guarded write now reads the current host's keyed `VectorModelRegistry<TEntity>` record before
  deciding or recording.
- A red/green two-host probe proved the defect: before removal, host B started with an empty backend,
  inherited host A's confirmation, and left its own registry empty.
- The repeated-host fixture now proves host B records the same entity/model in its own backend.
- Data.AI builds with zero errors and its complete self-executing suite passes 81/81.

The durable registry is authoritative for completed writes but does not provide atomic compare-and-set
across simultaneous first writers. R04-02 removes host/process leakage; a provider-negotiated concurrent
write guarantee must be earned separately.

## Third increment — classify AI discovery registries

- `EmbeddingRegistry` and `MediaAnalysisRegistry` retain only immutable entity `Type` discovery facts;
  they do not retain services, configuration, host identity, adapters, or backend state.
- Their process-wide, additive, idempotent ownership is intentional. Moving these facts into host DI
  would add lifecycle complexity without isolating any host-owned state.
- Public registration remains available because generated consumer modules and framework assembly
  discovery need an infrastructure entry point. It is not a supported per-host runtime extension API.
- Focused embedding registry evidence proves idempotent type discovery, attribute-derived async
  filtering, and null-tolerant generated input. The complete AI unit suite passes 155/155.
- Strong process-lifetime `Type` references mean collectible plugin unloading remains unsupported.
- `MediaAnalysisRegistry.Register(Type, bool)` currently ignores its `async` argument because async
  selection comes from attribute metadata. That compatibility-shaped inconsistency is recorded for a
  later API cleanup; this ownership increment does not change its behavior.

The actual adjacent ownership hazard is activation, not discovery: `KoanAutoRegistrar` appends
handlers to static closed-generic Entity lifecycle arrays for every host. R04-02 remains active for
those lifecycle registrations, relationship metadata, `AppHost.Identity`, and the non-hosted
`StartKoan()` path.

## Fourth increment — idempotent lifecycle composition

- A focused red Core probe registered the same after-upsert delegate twice and observed two executions.
- A red two-host `AddKoan()` probe observed the same closed-generic AI hook grow from one registration
  after host A to two after host B.
- `EntityEventRegistry` now treats an equal delegate as the same process-stable behavior declaration.
  Distinct handlers remain ordered FIFO, and reset clears the complete pipeline without a parallel
  idempotence cache.
- The unchanged probes are green. The complete Data.AI suite passes 82/82, and the focused Data.Core
  lifecycle class passes 11/11.
- Lifecycle documentation now states the ownership boundary: handler definitions may be process-stable,
  but they must not capture a provider, scoped service, configuration snapshot, or other disposable
  host state. Different closure instances remain intentionally distinct.

This increment prevents repeatable framework composition from multiplying static AI behavior; it does
not claim that arbitrary application closures are host-safe. During Data.Core verification, disposed
test hosts also produced background Qdrant health-probe `ObjectDisposedException` logs against
`DefaultMeterFactory` while the focused tests still exited green. That is a separate host-disposal and
observability owner and must not be hidden by this repair.

## Fifth increment — host-owned startup health probing

- A focused red Core probe proved that `StartupProbeService.StopAsync` returned while its active
  contributor remained blocked and the next contributor could run after shutdown.
- A second red probe proved that the cancellation token already accepted by `RequestProbe` was not
  delivered to targeted health contributors.
- `StartupProbeService` is now a tracked one-shot `BackgroundService`; host shutdown cancels and awaits
  the active contributor before service-provider disposal.
- Probe request arguments carry the caller's cancellation token. Targeted and broadcast bridge paths
  use it, and cancellation does not create an Unhealthy sample.
- Qdrant health checks rethrow intentional host cancellation instead of logging a warning and returning
  an Unhealthy report.
- The focused Core lifecycle probes pass 2/2, Core Unit passes 74/74, main Core passes 195/195, Data.AI
  passes 82/82, and Data.Core passes 285/285. A full Data.Core output gate found zero
  `ObjectDisposedException`, `DefaultMeterFactory`, disposed-object, or cancellation-stack matches.

The same full-host logs show `HealthProbeScheduler` starting and stopping twice: it is registered as a
direct hosted service and also activated by the Koan background-service orchestrator. That duplicate
execution is the next bounded background owner; this increment does not conceal it inside the startup
probe repair.

## Sixth increment — one scheduler lifecycle owner

- Two focused red probes used real `AddKoan()` composition: one found the scheduler registered directly
  as an `IHostedService`, and the other observed two scheduler starts and two stops in one host lifecycle.
- Removed only the direct hosted-service registration. The generated background-service descriptor and
  `KoanBackgroundServiceOrchestrator` are now the scheduler's sole execution path.
- The concrete scheduler and its `IKoanBackgroundService`, `IKoanPokableService`, and
  `IHealthContributor` aliases still resolve to the same singleton, retaining manual probe actions,
  health reporting, and service inspectability.
- The unchanged focused probes pass 2/2. Core Unit passes 76/76, Core passes 195/195, Data.AI passes
  82/82, and Data.Core passes 285/285.
- Full-process output shows balanced single-owner scheduler lifecycles across repeated hosts: Data.AI
  reports 8 starts/8 stops and Data.Core 87/87. Both output scans contain zero
  `ObjectDisposedException`, `DefaultMeterFactory`, disposed-object, or `TaskCanceledException` matches.

The orchestrator cancels each child through a linked token, but its own stop path does not explicitly
await every tracked child task before returning. That adjacent shutdown contract is the next bounded
background owner; this increment proves one scheduler loop without overstating child completion.

## Seventh increment — bounded child-task shutdown

- A focused red probe proved that `KoanBackgroundServiceOrchestrator.StopAsync` returned while a
  cancellation-aware child was still blocked in cleanup. A second red probe proved that a child fault
  completed during shutdown was never reported; the host-deadline probe was already green.
- The orchestrator now awaits its tracked child tasks after canceling its own loop. The host-provided
  shutdown token remains the bound, so a non-cooperative child cannot extend graceful shutdown.
- Expected child cancellation remains non-error behavior. Faulted children completed within the
  graceful window are observed and logged once with the service name and exception rather than
  turning host shutdown into a second failure.
- Synthetic child services use closed instances of generic test types, so direct-DI lifecycle probes
  cannot be misclassified as application services by real `AddKoan()` registry discovery.
- The focused orchestrator probes pass 3/3 and the combined scheduler/orchestrator lifecycle surface
  passes 5/5. Core Unit passes 79/79, Core passes 195/195, Data.AI passes 82/82, and Data.Core passes
  285/285.
- Full-process output remains balanced across repeated hosts: Data.AI reports 8 scheduler starts/8
  stops and Data.Core 86/86. Both output scans contain zero `ObjectDisposedException`,
  `DefaultMeterFactory`, disposed-object, or `TaskCanceledException` matches.

A child that ignores cancellation may still run after the host's shutdown deadline; the framework
does not claim otherwise. Faults that occur only after that deadline are outside synchronous shutdown
reporting because the provider may already be disposing.

## Eighth increment — classify captured lifecycle dependencies

A source inventory found seven production Entity lifecycle registration statements under `src/`:
two reflection-installed AI hooks, three Identity hook declarations, and two OpenGraph hook
declarations. None retains a framework-owned service, provider, adapter, configuration snapshot,
host identity, logger, or disposable.

| Owner | Registration shape | Retained state | Runtime dependency path | Disposition |
|---|---|---|---|---|
| Data.AI embedding | targetless closed static `EmbeddingHookAsync<TEntity>` delegate | none | metadata is immutable; data/AI operations resolve against the active Entity/AI runtime when invoked | keep process-stable; the existing two-host probe proves one handler after both hosts |
| Data.AI media analysis | targetless closed static `MediaAnalysisHookAsync<TEntity>` delegate | none | metadata is immutable; data/AI operations resolve when invoked | keep process-stable; same delegate-construction path as embedding |
| Identity before-upsert | noncapturing generic async delegate, instantiated for eight Identity entity types | none beyond the closed entity type | prior state comes from the operation context | keep process-stable |
| Identity after-upsert/remove | two closure instances per Identity entity type | immutable entity/action strings plus the static subject selector delegate | `AppHost.Current` is read inside `EmitAsync` at execution time and follows the ambient host binding | keep process-stable; do not replace immutable declaration metadata with host DI |
| OpenGraph after-upsert/remove | two noncapturing generic delegates per carded entity type | none in the lifecycle delegates | each invocation looks up the current `CardRegistration` | keep process-stable; registration reset/replacement remains test-owned |
| Data.Core convenience overloads | wrapper closure around an application-supplied `Action` or synchronous `Func` | exactly the supplied delegate and its target | application-defined | supported declaration mechanism, but not automatically host-safe |

OpenGraph is the only indirect capture-capable boundary found. Its process-static `CardRegistration`
retains the application resolver, builder, and projection delegates so request-time cold fill and
lifecycle warming use one declaration. The canonical resolver (`id => T.Get(id)`) and pure entity
selectors retain no runtime owner. An application resolver or selector that closes over a service
provider, scope, service instance, options snapshot, or disposable would make that application
declaration process-owned and is unsupported. The lifecycle delegates themselves do not retain the
registration; they look it up at execution time, so `SocialCards.Reset()` releases the application
delegates for test isolation.

The public builder cannot reliably inspect an arbitrary delegate graph and determine whether it is
host-owned. A host-scoped registry redesign without a concrete supported per-host declaration use case
would add ownership machinery while weakening the intended startup declaration model. The bounded
repair is therefore an explicit ownership contract in the lifecycle and OpenGraph guidance, not a
production migration.

Focused verification passes: Data.AI repeated-host lifecycle 3/3, Data.Core Entity lifecycle 11/11,
Identity 113/113, and OpenGraph 38/38. Documentation lint reports zero errors with the unchanged 1,518
repository warnings.

## Ninth increment — resolve relationship metadata from the active host

- A focused two-host red probe used one closed Entity type and a disposable metadata singleton per
  real `AddKoan()` host. Host A disposed its singleton, but host B's Entity returned that same stale
  object instead of host B's registration; the probe failed 0/1.
- `Entity<TEntity,TKey>` no longer caches `IRelationshipMetadata` in a closed-generic static. Each
  hosted call resolves the current ambient provider through the existing targetless accessor, so the
  host owns both the service and its lifetime.
- `RelationshipMetadataService` remains a per-host singleton. Its four internal dictionaries retain
  only entity `Type`, property name, and related `Type` reflection facts; they retain no provider,
  adapter, options, logger, configuration, or host identity.
- `AssemblyCache` remains process-wide and additive because child discovery needs assembly reflection
  facts. It retains no host runtime service; collectible assembly unloading remains unsupported.
- Hostless `GetRelationshipService()` metadata inspection keeps one process-static fallback service.
  That fallback has no DI/runtime dependencies and caches only the same immutable reflection facts.
- The unchanged ownership probe passes 1/1 after the repair. The complete Data.Core process passes
  286/286 with zero `ObjectDisposedException`, `DefaultMeterFactory`, or `TaskCanceledException`
  signatures. Public relationship syntax, `IRelationshipMetadata`, and the targetless accessor remain
  compatible.

The unused global-namespace `EntityMetadataProvider` duplicate at the end of `Entity.cs` remains
vestigial; removing a public type is outside this lifetime repair. Relationship child loading still
contains load-all-and-filter paths; their cost and capability behavior remain explicitly owned by
R04-06 rather than being hidden inside host ownership work.

## Tenth increment — bind application identity to the active host

- The identity inventory found one write (`AppHostBinderHostedService.SetIdentity`) and three direct
  production reads: OpenAPI document metadata, S3 bucket-prefix fallback, and ZenGarden content-change
  filtering. The stored snapshot retained no provider, service, logger, adapter, or disposable, but it
  did retain configuration after its owning lease and ignored `AppHost.PushScope` selection.
- Two focused red probes made the split-brain concrete. Both sequential binders and parallel flow
  scopes selected distinct providers while `AppHost.Identity` continued returning the process-global
  `koan-core-tests` identity; the focused surface passed 3/5 before the repair.
- `ApplicationIdentitySnapshot` is now a host-owned singleton derived from the existing configured
  `ApplicationIdentityOptions`. `AppHost.Identity` resolves it from the current leased or flow-scoped
  provider on every access; with no registered host identity it falls back to the frozen process
  snapshot in `KoanEnv`.
- Removed the binder's independent global identity assignment. S3 and ZenGarden keep their public call
  shapes and now inherit active-host selection automatically.
- OpenAPI already receives its host's application services, so its transformer now resolves that
  provider's identity directly. A real two-TestServer probe starts Alpha and Beta together and proves
  that both documents retain their own title and application code.
- The unchanged focused binder surface passes 5/5. Core passes 197/197, Core Unit passes 79/79, and
  OpenAPI passes 10/10.

`KoanEnv.CurrentSnapshot.Application` remains an init-once process identity by design and is the
hostless fallback. MCP and composition surfaces that read `KoanEnv` directly were inventoried but not
silently reclassified as host-aware by this increment; any change to that frozen-infrastructure
contract requires its own bounded decision and evidence.

R04-02 remains active for the non-hosted `StartKoan()` path.

## Eleventh increment — make non-hosted startup own its lease

- A source inventory found one supported non-hosted entry point: Data.Core's synchronous
  `IServiceCollection.StartKoan()`. It built a provider and assigned `AppHost.Current` directly, so
  neither provider disposal nor startup failure released that process-default binding.
- Four focused probes made the defect concrete: provider disposal, overlapping sequential owners,
  parallel flow scopes followed by concurrent disposal, and runtime-discovery failure all left a
  stale provider selected. The unchanged surface passed 0/4 before the repair.
- `StartKoan()` now uses the same atomic `AppHost.Attach` ownership lease as the generic-host binder.
  Its returned provider owner releases the lease before tearing down container services;
  compare-and-release prevents an older provider from clearing a newer owner.
- Startup is transactional at the ownership boundary. If runtime resolution, discovery, or startup
  throws, the pending lease and newly built provider are disposed before the startup failure
  propagates.
- The focused ownership surface passes 4/4. The public `IServiceProvider` return shape remains
  compatible, and documentation now makes caller disposal explicit. The complete Data.Core process
  passes 290/290; Core passes 197/197, Core Unit passes 79/79, and Data.AI passes 82/82.

`StartKoan()` remains intentionally narrower than the generic host: it does not start `IHostedService`
implementations, coordinate graceful shutdown, or expose a runtime stop phase. Those are hosting-mode
limits, not hidden guarantees of this ownership repair.

## Twelfth increment — closure audit and residual ledger

The closure audit does **not** pass R04-02 yet. Canonical generic-host and non-hosted leases are green,
but a current-tree source inventory found host-owned state and ambient writes outside those owners.
Passing now would contradict the card's repeated-host objective and its no-provider-residue gate.

| Residual owner | Current evidence | Why closure is unsafe | Bounded disposition |
|---|---|---|---|
| Data.Core aggregate configuration | [`AggregateConfigs.Cache`](../../../../../src/Koan.Data.Core/AggregateConfigs.cs) stores one [`AggregateConfig<TEntity,TKey>`](../../../../../src/Koan.Data.Core/AggregateConfig.cs) per type pair; that object owns a lazy delegate closing over the first `IServiceProvider` and the repository it creates. Fourteen source/test call sites still invoke `AggregateConfigs.Reset()` as isolation machinery. | Host B can inherit host A's provider, adapter factory, guards, contributors, or repository after host A is disposed. The public reset is evidence of the leak, not lifecycle ownership. | **Next increment:** red/green two-host proof using one Entity type and no reset; replace provider retention with immutable metadata plus active-host resolution. |
| Alternate ambient writers | Seven tracked `src/` assignments remain across [Web startup](../../../../../src/Koan.Web/Hosting/KoanWebStartupFilter.cs), [Identity startup fallback](../../../../../src/Koan.Identity/Initialization/SecIdentityModule.cs), and shipped [container](../../../../../src/Koan.Testing.Containers/KoanDataSpec.cs) / [conformance](../../../../../src/Koan.Testing/EntityConformanceSpecs.cs) helpers. They write or clear `AppHost.Current` directly instead of owning `Attach` leases or flow scopes. | An unleased continuation can overwrite the last attached host, and a failed/parallel path has no owner-checked release. Sequential test conventions reduce frequency but do not establish the parallel contract. | Inventory and reduce only after the aggregate cache lands; separate framework-host wiring from testing-flow selection. |
| Static logging scopes | Thirteen production `static readonly KoanLogScope` fields use a scope implementation that [caches the first `ILogger`](../../../../../src/Koan.Core/Logging/KoanLog.cs) it resolves. `KoanLogFactoryBridge` owner-checks the global factory, but those cached scopes do not follow later hosts. | A process-static scope can retain host A's logger/provider graph and continue using it under host B. Existing tests observe a static test sink, not repeated-host logger ownership. | Add a focused two-factory probe, then make scopes resolve against the active factory without pinning a host logger. |
| Background-service locator | [`ServiceLocator.SetProvider`](../../../../../src/Koan.Core/BackgroundServices/ServiceRegistry.Contracts.cs) stores the orchestrator's provider in a process static, never clears it, and its getter currently has no production caller. | The most recently started orchestrator remains retained after stop even though the locator supplies no current behavior. | Prove the getter is dead and remove the locator/write in a narrow Core cleanup rather than adding another lease. |
| Missing-host failure contract | Current [Data](../../../../../src/Koan.Data.Core/Data.cs) and [AI](../../../../../src/Koan.AI/Client.cs) paths throw several raw `InvalidOperationException` messages, while some optional paths return `null` or catch `ObjectDisposedException`. | The card's promised single corrective host-context error is not yet true or covered by one negative-path contract. | After ownership is clean, define one Core host-context failure and project it through the affected common paths; R04-05 may reuse its facts but cannot retroactively supply R04-02 evidence. |

The same audit classified three adjacent shapes as safe for this card:
[`AdapterNaming`](../../../../../src/Koan.Data.Core/Configuration/AdapterNaming.cs) keys factories by
provider through a `ConditionalWeakTable`; the [AI client](../../../../../src/Koan.AI/Client.cs) caches
a targetless resolver and supplies the current provider on every call; discovery/metadata registries
retain only immutable type or reflection facts. `KoanEnv` remains the explicitly documented process
snapshot.

The next change is intentionally only the aggregate-configuration owner. It must begin with the
repository `explore` workflow and a failing repeated-host probe that demonstrates host B resolving its
own provider and repository without calling `AggregateConfigs.Reset()`. The other residuals remain
visible here so completing one cannot be mistaken for closing the card.

## Thirteenth increment — host-owned aggregate configuration

`AggregateConfigs` no longer stores runtime configuration in one process dictionary. Configuration
and its lazy repository are memoized inside a weak-keyed cache partitioned by the supplied
`IServiceProvider`; the value can close over that provider without extending its lifetime, and two
providers using the same closed Entity type cannot observe one another's adapter factory, guards,
read contributors, configuration, or repository. Per-provider insertion is atomic, preserving
same-host memoization under contention.

The only process-wide state retained by this surface is the additive `(EntityType, KeyType)` discovery
fact set. The new `AggregateConfigs.GetRegisteredTypes()` inspection method returns a snapshot of
those provider-free facts. `AggregateConfigs.Reset()` remains as compatibility cleanup for test
matrices, but no repeated-host correctness proof calls it.

Data.Backup no longer reflects the private Data.Core cache. Its registered-entity fallback accepts
the discovery service's injected provider, obtains the supported type facts, and resolves each
provider against that host. The no-argument compatibility facade uses the current ambient host when
one exists and reports `unknown` rather than recovering runtime metadata from an earlier host when
none exists.

Evidence on 2026-07-14:

- the new focused Data.Core ownership surface was red 1/3 against the process cache because later
  host tests reused an earlier cached configuration; unchanged after the repair it passes 3/3;
- sequential hosts resolve distinct configurations/repositories without reset, simultaneous hosts
  remain isolated, and one host creates its repository once;
- the supported Backup discovery seam passes its focused proof and the complete Backup suite passes
  2/2;
- the complete Data.Core process passes 293/293.

R04-02 remains `in-progress`. Alternate ambient writers, static logging scopes, the dead background
service locator, and the unified missing/disposed-host failure contract remain separate closure
residuals; this increment does not change their status or any capability maturity label.

## Smallest meaningful fix

Define one host/runtime lease and make service/configuration-backed registries resolve through it.
Immutable reflection/type metadata may remain static. First repair the failing lifecycle path, then add
repeat-create/dispose probes before broad migration.

## Failure behavior

A missing/disposed host throws one Koan host-context error naming the attempted operation and how to
establish a valid host. It never falls back to an earlier provider.

## Verification

- failing Data/AI lifecycle test passes repeatedly;
- N sequential and parallel host create/use/dispose cycles show no provider/registry residue;
- core Entity operations, lifecycle registration, relationship metadata, and AI registry have focused
  ownership tests;
- no new process-global mutable service/configuration cache is introduced.

## Compatibility and rollback

Preserve public call shapes while changing ownership underneath. If an API relied on cross-host static
registration, document and deprecate it rather than silently preserving leakage. Land migration in
small owner-specific commits behind the host lease.

## Stop condition

Split by owner if one host abstraction cannot be reviewed without changing unrelated module semantics.
