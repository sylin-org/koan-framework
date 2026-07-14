---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: reviewed
  scope: R04-02 scoped Web startup and remaining closure residuals
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R04 — Harden the framework foundation](work-items/R04-foundation-hardening.md)
- State: `in-progress`
- Active child: [R04-02 — Establish host-scoped runtime ownership](work-items/r04/R04-02-host-scoped-runtime.md)
- Objective: make repeated hosts and tests unable to inherit disposed providers, registrations, or
  other host-owned runtime state.
- Foundation: R01 passed through [ARCH-0105](../../decisions/ARCH-0105-product-constitution.md) and the
  canonical [product constitution](../../architecture/product-constitution.md).
- Current state: R02 and R03 passed. All 13 surfaces are classified in
  [`CAPABILITIES.md`](CAPABILITIES.md); [ARCH-0106](../../decisions/ARCH-0106-entity-semantics-contract.md)
  and the canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md)
  now bound implementation choices. The dependency-ordered [`R04-BACKLOG.md`](R04-BACKLOG.md) is
  established, R04-01 passed, and R04-02's host lease, durable vector-model confirmation, AI
  discovery and lifecycle-capture classification, active-host relationship metadata, idempotent
  lifecycle composition, and host-owned startup-health increments are green. `HealthProbeScheduler`
  has one orchestrator-owned execution path while retaining its background, pokeable, and health
  aliases, and the orchestrator awaits owned child cleanup within the host shutdown deadline.
  Application identity now follows the active host/provider scope instead of a separate process
  static; OpenAPI resolves its explicitly supplied host provider. `KoanEnv` remains the frozen
  hostless process snapshot. The synchronous non-hosted `StartKoan()` path now owns the same atomic
  host lease as the generic-host binder: disposal releases its binding, overlapping owners cannot
  clear one another, and failed startup disposes the new provider. No capability maturity label
  changed. Aggregate configuration and its repository are now memoized per weak provider identity;
  Backup consumes provider-free type facts through a supported seam and resolves provider metadata
  against its injected host. Identity module startup now scopes Entity operations to the provider it
  was handed and restores the prior ambient host under simultaneous starts. Web pipeline startup now
  flow-scopes its application provider across Koan and downstream startup filters without replacing
  a newer attached owner. A closure audit still prevents R04-02 from passing: five testing-helper
  `AppHost.Current` assignment statements remain, static logging scopes cache first-host loggers, an
  unused background-service locator retains its provider, and the unified missing-host failure is
  absent. A parallel design-only
  [`R04 Entity Facet Candidate Slate`](R04-ENTITY-FACET-CANDIDATES.md) elects the eventual R04-07
  language without changing the active production card or implementing public syntax.

## Next safe actions

1. Treat the leased host binding, late Data.AI logger resolution, uncached durable vector-model
   registry, immutable AI type discovery, clean lifecycle-capture classification, active-host
   relationship metadata, equal-delegate lifecycle idempotence, tracked startup health probing,
   single-owner health scheduling, bounded orchestrator child shutdown, and host-owned application
   identity, provider-owned aggregate configuration, and flow-owned Identity/Web startup as the stable
   R04-02 base.
2. Run the `explore` skill before the next production increment.
3. Reduce only the next writer family: the three direct assignments in `KoanDataSpec`; first classify
   the helper's host, fixture, and disposal ownership through the repository `explore` workflow.
4. Prove repeated and overlapping data specs cannot overwrite or clear another host before choosing
   between an attached lease and a bounded flow scope.
5. Keep direct `KoanEnv.CurrentSnapshot.Application` consumers classified as process-snapshot users;
   do not imply they became host-aware through the `AppHost.Identity` repair.
6. Do not fold the two Entity-conformance assignments, static logging scopes, the dead service
   locator, or the unified missing-host error into the `KoanDataSpec` repair. They remain explicit
   later R04-02 residuals.
7. Do not mark R04-02 passed until sequential and parallel ownership probes cover every named owner and
   missing/disposed host behavior is corrective.

## Expected working tree

R04-01 and R04-02's host lease, vector-model confirmation, AI discovery and lifecycle-capture
classification, active-host relationship metadata, lifecycle idempotence, startup-health ownership,
scheduler single-owner, orchestrator shutdown, application-identity, and non-hosted startup repairs
should be committed.
The closure-audit ledger, provider-owned aggregate-configuration repair, and scoped Identity/Web
startup repairs should also be committed.
Treat every unrelated pre-existing change as user-owned.

## Verification at handoff

- Core host-binding, OpenAPI, and Data.AI projects build with zero errors;
- Core Unit passes 79/79, the Core self-executing suite passes 197/197, Data.AI passes 82/82, and
  Data.Core passes 293/293;
- the AI unit project builds with zero errors and its self-executing suite passes 155/155;
- the focused Data.Core lifecycle class passes 11/11;
- repeated-host probes prove different DI markers, Entity storage, and vector-model registry state;
- focused startup-health ownership probes pass 2/2, the Qdrant cancellation probe passes 1/1, and the
  full Data.Core output contains zero disposed-service or cancellation-stack signatures;
- focused scheduler ownership probes pass 2/2; repeated-host output records balanced scheduler
  lifecycles (Data.AI 8/8 and Data.Core 86/86 starts/stops) with zero disposal/cancellation signatures;
- focused orchestrator shutdown probes pass 3/3 and the combined scheduler/orchestrator surface 5/5;
- all seven production lifecycle registration statements are classified; none retains framework-owned
  host runtime state, while OpenGraph's application-supplied resolver/selector boundary is explicit;
- focused lifecycle consumers pass: Data.AI repeated-host 3/3, Data.Core 11/11, Identity 114/114, and
  OpenGraph 38/38;
- simultaneous Identity module startup is red 0/1 before repair and green 1/1 after it; each of two
  reconcilers observes only its supplied provider, the prior host is restored, and Identity passes
  114/114;
- Web pipeline startup is red 0/1 before repair and green 1/1 after it; a newer attached owner is
  restored after Koan and downstream startup filters run, while WellKnown passes 2/2, Web Extensions
  110/110, and OpenAPI 10/10;
- the relationship metadata two-host probe is red 0/1 before the repair and green 1/1 after it; host B
  resolves its own singleton after host A disposes its registration;
- the application-identity binder surface is red 3/5 before the repair and green 5/5 after it;
  sequential binders and parallel flow scopes resolve their own immutable snapshots;
- a real two-host OpenAPI probe passes 1/1 and the full OpenAPI suite passes 10/10; Alpha and Beta
  retain distinct document titles and application codes while both hosts are running;
- the non-hosted `StartKoan()` ownership surface is red 0/4 before the repair and green 4/4 after it;
  provider disposal, overlapping owners, concurrent scoped flows, and failed startup all release the
  correct binding and owned services;
- aggregate configuration ownership is red 1/3 before repair and green 3/3 after it without reset;
  the complete Data.Core process passes 293/293 and Backup passes 2/2;
- the closure audit still counts 14 compatibility `AggregateConfigs.Reset()` call sites, five tracked
  `src/` ambient assignments in shipped testing helpers, thirteen static logging scopes, and one
  set-only static background provider; therefore R04-02 remains `in-progress`;
- the complete Data.Core process passes 293/293 with zero disposed-service, meter-factory, or
  cancellation-exception signatures;
- runtime and consumer tests for R04-02 must cover repeat hosts and disposed-state negative paths;
- documentation metadata, links, TOC, privacy scan, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not broaden R04-02 into package, bootstrap, explanation, or Entity-facet work.
- Do not migrate public Entity vocabulary before false-success and host-lifetime hazards are bounded.
- Do not treat the R04 facet slate's target grammar as current supported syntax.
- Do not claim that a contributor which ignores cancellation is guaranteed to finish within host
  shutdown; the repaired contract propagates and awaits cancellation-aware work.
- Do not use a passing source build to claim package installation is fixed.
- Do not accept a zero test-command exit code without a discovered/executed test count.
- Do not promote maturity until external clean-room and failure evidence earns it.
