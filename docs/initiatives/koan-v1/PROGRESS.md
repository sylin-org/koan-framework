---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Progress"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: R04-02 closure audit and aggregate-cache residual
---

# Koan V1 Reorganization Progress

This is the initiative's only live status ledger. Update it in the same change that starts, blocks,
or completes a work item. The roadmap describes order; it does not report progress.

## Initiative state

- Overall: `active`
- Current tranche: `T4 — foundation hardening`
- Active work item: `R04`
- Next decision: remove `AggregateConfigs` first-provider retention with a no-reset two-host proof
- V1 readiness: `not assessed`

## Work items

| ID | Work item | Tranche | Status | Depends on | Claim | Evidence / note |
|---|---|---|---|---|---|---|
| R00 | [Establish the privacy boundary](work-items/R00-privacy-boundary.md) | T0 | passed | — | Codex · 2026-07-13 | Published branch tips are clean; operator accepted retained historical residue and declined a disruptive rewrite. |
| R01 | [Ratify the product constitution](work-items/R01-product-constitution.md) | T1 | passed | R00 | Codex · 2026-07-13 | ARCH-0105 and the canonical product constitution separate durable rules, tactical mechanisms, and maturity claims. |
| R02 | [Build the capability truth baseline](work-items/R02-capability-baseline.md) | T2 | passed | R01 | Codex · 2026-07-13 | All 13 surfaces are classified with reproducible evidence; no capability is mislabeled as supported while packaging is incoherent. |
| R03 | [Define the Entity Semantics Contract](work-items/R03-entity-semantics-contract.md) | T3 | passed | R02 | Codex · 2026-07-13 | ARCH-0106 ratifies five semantic locations, strict Entity admission, C# 14 module facets, and host/context/event boundaries. |
| R04 | [Harden the framework foundation](work-items/R04-foundation-hardening.md) | T4 | in-progress | R03 | Codex · 2026-07-13 | R04-01 passed. R04-02's canonical hosted/non-hosted leases and named repairs are green, but closure found first-provider aggregate configuration, alternate ambient writers, cached static loggers, and a dead static service locator. |
| R05 | [Prove the golden V0-to-V1 journey](work-items/R05-golden-v0-v1-journey.md) | T5 | pending | R04 | — | Anonymous business domain only. |

Allowed status values are `pending`, `in-progress`, `blocked`, `passed`, and `stopped`. Only one work
item should normally be `in-progress`.

## Readiness queue

| Work item | Ready? | Gate |
|---|---|---|
| R00 | passed | Forward-only sanitization and residual-risk acceptance recorded on 2026-07-13. |
| R01 | passed | ARCH-0105 accepted; canonical constitution and public alignment are complete. |
| R02 | passed | Capability ledger, focused execution record, public-claim audit, and ranked dispositions accepted. |
| R03 | passed | Entity inventory, ecosystem dispositions, canonical contract, and ARCH-0106 accepted. |
| R04 | active | R04-01 passed; R04-02 remains in progress. Its next bounded owner is the first-provider `AggregateConfigs` cache; the closure ledger preserves later ambient/logging residuals. |
| R05 | no | The foundation path must be stable enough to measure honestly. |

## Divergence and risk log

| Date | Item | Observation | Disposition |
|---|---|---|---|
| 2026-07-13 | R00 | A current architecture ledger contained an identifying downstream token. | Remove the token and retain only the generic privacy rule. |
| 2026-07-13 | R00 | Current tracked content and paths are clean; 53,203 historical objects contain no identifying paths. | Current-tree and historical-path checks pass. |
| 2026-07-13 | R00 | The predecessor of the sanitized line proves identifying content remains reachable in history. Two bounded, non-emitting full-content traversals did not complete. | Treat exposure as confirmed but extent as inconclusive; stop before history mutation and request an operator disposition. |
| 2026-07-13 | R00 | The operator explicitly authorized rewriting affected published history and force-pushing affected refs. | Create an offline backup, rewrite in an isolated mirror, verify, and push only refs whose object IDs changed. |
| 2026-07-13 | R00 | Precise auditing showed a rewrite would alter all published branches, release tags, and GitHub-managed pull refs; historical object counts also amplified repeated content and generic matches. | Operator chose the proportionate forward-only path: keep all live branch tips clean, accept historical residue, and spend no further initiative energy on rewriting. |
| 2026-07-13 | Initiative | Existing architecture prose contains claims stronger than currently collated evidence. | R02 must classify code, tests, docs, and unsupported scenarios before publication changes. |
| 2026-07-13 | R02 | A clean application cannot restore the public 0.17.0 package set: Data.Abstractions requires an unpublished Core 0.17.3 patch; the SQLite graph also reports a high-severity transitive advisory. | Correct the front door immediately; make atomic, advisory-reviewed clean-room packaging R04 priority zero. |
| 2026-07-13 | R02 | The focused bootstrap suite produced no test result in 304 seconds. | Keep bootstrap at `demonstrated`; diagnose bounded execution before support promotion. |
| 2026-07-13 | R02 | AI unit and in-memory vector suites pass, but one Data/AI lifecycle integration test fails with a disposed host service provider. | Keep combined AI/vector semantics `experimental`; make repeatable host lifecycle a P0 foundation repair. |
| 2026-07-13 | R02 | The June assessment contains now-obsolete OIDC and discovery wording, while front-door docs overstated exact startup reporting and package availability. | Prefer the dated R02 ledger; correct material front-door wording now and retire stale secondary prose in R04. |
| 2026-07-13 | R03 | C# 14 can contribute constrained static and instance members to an Entity subtype; a disposable probe compiled `Todo.Semantic` without changing Data.Core. | Adopt module-owned facets in the Entity language namespace; require checked-in absence/presence/collision consumer probes in R04. |
| 2026-07-13 | R03 | Current Entity language includes `this object` persistence, `where T : class` messaging, module-absent cache members, type-wide operations on arbitrary instances, static host state, and hidden relationship full scans. | ARCH-0106 rejects these shapes; stage compatibility-aware repairs after immediate false-success/lifetime hazards. |
| 2026-07-13 | R03 | Data.Backup exposes `DeleteBackup` as a successful placeholder returning `true`. | Treat as P0 false-success behavior: disable or implement with focused proof before broader API reshaping. |
| 2026-07-13 | R03 | ABP's strategic value is boundary discipline—aggregate invariants, UoW, deferred events, module dependencies, and optional repositories—not generated layering. | Adapt the boundaries while declining mandatory scaffolding and dynamic property bags for app-owned entities. |
| 2026-07-13 | R04-01 | `DeleteBackup` had no management service or unambiguous name-to-archive deletion contract; raw storage deletion would invent unsafe semantics. | Return one actionable faulted task, prove that success is impossible, and defer deletion receipts/control-plane design. |
| 2026-07-13 | R04-03 | `dotnet test` returned zero without building or reporting tests for the new xUnit v3 suite; direct build plus self-execution proved one passing test. | Require discovered/executed counts for test-lane evidence; diagnose command/tooling coherence in the bounded-lanes card. |
| 2026-07-13 | R04-08 | Data.Backup public prose names absent APIs, and the test-authoring guide mandates a removed harness/path. | Keep R04-01 wording truthful; repair and executable-gate the broader documentation in R04-08. |
| 2026-07-13 | R04-02 | The isolated VectorModel guard spec passed, but the 79-test Data.AI process produced 31 failures after `EmbeddingMetadata` captured a logger from a disposed first-host provider. | Treat the static initializer as the root failure; lease generic-host binding and resolve host services only when an operation runs. |
| 2026-07-13 | R04-02 | Owner-checked host leases, late Data.AI logger resolution, and a two-host Entity/storage probe are green; Core passes 195/195 and Data.AI passes 80/80. Backend-dependent caches and alternate binding paths remain unaudited. | Keep R04-02 `in-progress` and migrate one runtime owner at a time rather than claiming host scope from the first repair. |
| 2026-07-13 | R04-02 | `VectorModelGuard._confirmed` let host B skip its durable registry because host A had confirmed the same entity/partition/model process-wide; the red two-host probe left B's empty backend unrecorded. | Remove the cache rather than invent another scope: every guard reads the current host's O(1) durable record. Data.AI passes 81/81. |
| 2026-07-13 | AI-0036 | The durable registry prevents leakage across completed host-routed writes, but its read-modify-write sequence is not atomic between simultaneous first writers. | Correct “never stale” wording; require provider CAS/transaction evidence before claiming concurrent different-model exclusion. |
| 2026-07-13 | R04-02 | AI discovery registries store only additive `Type` facts and immutable attribute metadata; their public methods exist for generated/framework discovery, not supported per-host extension. Static Entity lifecycle arrays populated from those facts are the actual adjacent leak shape. | Keep discovery process-wide, document collectible plugin unloading as unsupported, prove the registry contract (AI unit 155/155), and explore lifecycle activation as the next owner. |
| 2026-07-13 | R04-02 | Repeated `AddKoan()` hosts appended the same static AI lifecycle delegate; the red host probe grew one after-upsert handler to two, and a Core probe executed one delegate twice. | Make equal-delegate registration idempotent centrally while retaining FIFO for distinct handlers. Data.AI passes 82/82; the focused Data.Core lifecycle class passes 11/11. |
| 2026-07-13 | R04-02 | Focused Data.Core verification exited green but background Qdrant health work logged `ObjectDisposedException` against a disposed `DefaultMeterFactory` after test-host shutdown. | Do not hide the log or broaden the lifecycle patch. Reduce and repair health/startup task shutdown ownership as the next bounded increment. |
| 2026-07-13 | R04-02 | `StartupProbeService` detached a `Task.Run`, used the host-start token, ignored `StopAsync`, and failed to pass request cancellation through the health bridge; Qdrant then converted shutdown cancellation into an Unhealthy warning. | Make the one-shot probe a tracked `BackgroundService`, propagate request cancellation, and rethrow Qdrant host cancellation. Core Unit passes 74/74, Core 195/195, Data.AI 82/82, and Data.Core 285/285 with zero disposal/cancellation-stack log matches. Full-host logs now isolate duplicate `HealthProbeScheduler` activation as the next background owner. |
| 2026-07-13 | R04-02 | `HealthProbeScheduler` was both a direct `IHostedService` and a generated `IKoanBackgroundService`; focused real-composition probes observed two starts and two stops for one host. | Remove only the direct registration and retain the generated singleton aliases. Focused ownership passes 2/2; Core Unit 76/76, Core 195/195, Data.AI 82/82, and Data.Core 285/285 pass. Repeated-host output is balanced at 8/8 and 87/87 scheduler starts/stops with zero disposal/cancellation signatures. The orchestrator's child-task await boundary is next. |
| 2026-07-13 | R04-02 | The orchestrator canceled linked child tokens but `StopAsync` awaited only its own loop, allowing provider disposal while cancellation-aware child cleanup was still active; completed shutdown faults were also unreported. | Await tracked child tasks within the host shutdown token, ignore expected cancellation, and log completed faults once. Focused probes pass 3/3, combined lifecycle 5/5, Core Unit 79/79, Core 195/195, Data.AI 82/82, and Data.Core 285/285; repeated-host scheduler output is balanced at 8/8 and 86/86 with zero disposal/cancellation signatures. |
| 2026-07-13 | R04-02 | All seven production Entity lifecycle registration statements are runtime-capture clean: AI uses targetless static methods; Identity closures retain immutable declaration metadata and resolve the active host during execution; OpenGraph lifecycle delegates are noncapturing but its separate static registry can retain application-supplied resolver/selector closures. | Keep the framework handlers process-stable, document indirect registry and convenience-wrapper capture boundaries, and do not invent host-scoped storage without a concrete supported use case. Existing Data.AI repeated-host, Data.Core lifecycle, Identity, and OpenGraph suites remain the executable evidence; relationship metadata is next. |
| 2026-07-13 | R04-02 | A closed `Entity<TEntity,TKey>` retained host A's `IRelationshipMetadata` singleton after host A disposed it; the two-host probe failed 0/1 when host B received the stale object. | Remove the closed-generic DI-service cache and resolve through the active ambient host on each call. Keep only a hostless fallback containing service-independent reflection facts. The unchanged probe passes 1/1 and Data.Core passes 286/286 with zero lifecycle-disposal signatures; `AppHost.Identity` is next. |
| 2026-07-13 | R04-02 | Closure audit found `AggregateConfigs` still caching a lazy first-host provider/repository, seven tracked `src/` ambient assignments outside canonical leases/scopes, thirteen static logging scopes that cache a first-host logger, and a set-only static background provider. The promised unified missing-host failure is also not implemented. | Do not pass R04-02. Repair `AggregateConfigs` first with a no-reset two-host probe, then reduce each remaining owner separately; retain current maturity labels. |
| 2026-07-13 | R04-07 | A pillar-first review could have placed every Koan capability on Entity, recreating the IntelliSense clutter the contract rejects; the first slate then underweighted the delight of discovering events from the Entity itself. | Elect intrinsic `Events` plus module-grown `Cache`, `AI`, and narrowly constrained `Media`; retain direct Data and constrained Canon/Storage verbs; keep control-plane, projection, messaging, and job surfaces off generic Entity. Static `Todo.Events` owns lifecycle composition, instance `todo.Events` raises domain facts, and neither implies broker delivery. Cache remains the one-facet pilot after R04-02/R04-05. |

## Operator gates

The following actions require a recorded maintainer decision and are not implied by initiative approval:

- rewriting published Git history;
- deleting or renaming public packages or APIs;
- changing compatibility guarantees;
- publishing a V1 date or support claim;
- disclosing any private downstream identity or artifact.

## Session close protocol

Before ending a session:

1. update the active row and link durable evidence;
2. add unresolved disagreement or failure to the divergence log;
3. replace [`NOW.md`](NOW.md) with the exact next safe action;
4. run the verification required by the active card;
5. use `passed` only after [`ACCEPTANCE.md`](ACCEPTANCE.md) is satisfied.
