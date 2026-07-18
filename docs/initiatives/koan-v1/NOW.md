---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: source-first
validation:
  date_last_tested: 2026-07-18
  status: tested
  scope: R11-05 Canon package-family graduation implemented and focused-proof green
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 are passed. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, and now Canon have completed their current R11 family slices.
- The current evaluated product graph contains 102 packages and 26 claims. Generated package quality reports 6
  repair-required, 18 review-required, and 78 structurally ready packages.

The accepted architecture remains business intent first: Entity-first application language, references express
capability intent, `AddKoan()` compiles one host-owned semantic constitution, pillars own meaning and runtime
chokepoints, adapters own mechanics, and applications own business rules. Cross-module contract packages survive only
when they are genuinely inert and independently consumed.

## Most recent completed slice: Canon

R10-11 was not restarted. Its four-line CustomerCanon host, automatic contributor discovery, functional/Web split,
same-id convergence, and failed/parked non-persistence remain the application contract.

R11-05 implemented the accepted topology:

- `Sylin.Koan.Canon` — `keep`;
- `Sylin.Koan.Canon.Contracts` — `merge` into Canon, implemented without a compatibility package;
- `Sylin.Koan.Canon.Web` — `keep`.

One immutable `CanonCompositionPlan` now owns every discovered `CanonEntity<T>`, including contributor-free models.
Runtime, startup/facts, pipeline inspection, and Web project that same decision. Built-in aggregation/policy always
participates; the first failed or parked contributor is terminal; duplicate HTTP slugs reject composition.

The default commit is explicit and tested: canonical Entity → aggregation indexes → audit. It is fail-loud and not
atomic across all Data providers. A failed checkpoint may leave earlier checkpoints durable; Canon does not claim
rollback, blind-retry safety, durable replay, distributed locking/delivery, or automatic recovery.

The public manual builder/configuration, observer/replay records, record capacity, disconnected optimization subsystem,
`CanonValueObject<T>` auto-CRUD, generated admin/rebuild routes, and `FlowPillarManifest` were retired. Headless
`ICanonRuntime.RebuildViews<T>`, standard-DI `ICanonPersistence`/`ICanonAuditSink` replacement, contributors, options,
results, and read-only pipeline metadata remain.

## Focused Canon proof

- Canon unit: 35/35.
- Canon integration: 7/7.
- CustomerCanon real-host golden path: 1/1.
- Both surviving packages built and packed in Release with one DLL/XML pair, build-transitive props, package-owned
  README, canonical icon, symbol package, exact dependencies, and no Contracts dependency.
- Current direct/transitive vulnerability audit: no known vulnerable packages for either survivor.
- Generated package-quality and product-surface truth regenerated; both Canon packages are structurally ready with no
  findings, and the verified claim names exactly Canon and Canon Web.
- No full solution/release ratchet ran; that remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev` at `1e91e2c10`; local is 140 commits ahead of `origin/dev` and 0 behind.
- The Canon implementation, R11 discovery/closure, topology, package truth, and docs are intentionally uncommitted.
- `tmp/` remains untracked scratch/evaluator material and must never be staged.
- The CustomerCanon lockfile was refreshed by the focused real-host build and no longer contains Canon Contracts.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, `HEAD`, and this Canon evidence before committing or starting another family.
2. Review the complete Canon discovery, accepted checkpoint, implementation closure, and exact focused evidence in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
3. Review Canon's implemented dispositions and the 102-row active matrix in
   [R11-02](work-items/r11/R11-02-package-topology-inventory.md).
4. If the Canon slice is accepted for commit, keep architecture and public/package truth logically reviewable and do
   not stage `tmp/`.
5. Continue R11-05 with the next dependency-ordered `assess` family. The first unresolved active row is
   `Sylin.Koan.Communication.Connector.RabbitMq`; begin with fresh exploration rather than presuming its disposition.

## Remaining temporary dispositions

Communication RabbitMQ; Data Access, Backup, and SoftDelete; Jobs; Observability; the remaining Orchestration CLI,
Aspire, generator, container-provider, and Compose renderer family; Security Trust; Testing, Containers, and Hosting;
Web Admin; and ZenGarden still require terminal R11-02 decisions.

## Do not redo

- Do not reopen R10-11's historical Canon assessment or rebuild CustomerCanon.
- Do not rerun Classification, Tenancy, or earlier family suites without an affected dependency.
- Do not run the full release ratchet before R11-07.
- Do not stage `tmp/`, inspect private dogfood applications, or use private identities in public docs.
- Do not push, tag, publish, release, deploy, or mutate remote configuration without separate authorization.
- Do not preserve a legacy surface solely for compatibility, but do not remove a current public concept without proving
  that it lacks an earned V1 intent.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and policy;
  adapters own mechanics; applications own business intent.
- Standard .NET hosting, DI, options, assembly, MSBuild, and NuGet concepts come before Koan-specific parts.
- Structural composition runs once per host/shape. Runtime operations consume immutable plans without contributor
  discovery, reflection, or provider negotiation.
- Startup, facts, health, errors, HTTP/MCP, tests, and future tooling project canonical decisions; no projection becomes
  a second authority.
- Break-and-rebuild is preferred when compatibility would preserve duplicate owners or crutches, but every rebuild is
  justified against current code, not remembered history.
- The neutral operation model remains a V1.1 target outside this release-readiness slice.

## Validation economy

- Start every new R11 family slice with focused exploration and coalescence assessment.
- Run only affected owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at the explicit R11-07 certification boundary.
