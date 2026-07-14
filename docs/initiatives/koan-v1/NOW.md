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
  scope: R04-02 passed; R04-03 next
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R04 — Harden the framework foundation](work-items/R04-foundation-hardening.md)
- State: `in-progress`
- Active child: none; [R04-02 — Establish host-scoped runtime ownership](work-items/r04/R04-02-host-scoped-runtime.md)
  passed and [R04-03 — Establish bounded bootstrap test lanes](work-items/r04/R04-03-bounded-bootstrap-lanes.md)
  is next.
- Objective: begin R04-03 from the repeatable-host foundation and make bootstrap evidence finish or
  fail with a bounded, diagnostic result.
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
  a newer attached owner. `KoanDataSpec` now delegates host attachment and release to the generic-host
  binder; both boot overloads preserve a later startup owner, older-host disposal cannot clear the
  newer host, and package companions state the sequential execution limit. `EntityConformanceSpecs`
  also delegates ownership to that binder without changing its one-class-per-Entity grammar; older
  fixture teardown cannot clear a newer owner. There are now zero direct `AppHost.Current`
  assignment statements under `src/`. `KoanLog` now resolves the active host or flow's
  `ILoggerFactory` through `AppHost`; its thirteen static scopes retain only category text, and the
  separate factory bridge and logger caches are gone. ARCH-0107 records that simplified owner. A
  background-service locator and its sole write are now deleted; its getter had no source, test, or
  sample consumer, while the public DI-owned service registry remains unchanged. ARCH-0108 now gives
  required common Data/AI paths one typed missing/disposed/missing-service contract while optional AI
  probes remain quiet. Core 204/204, Core Unit 79/79, AI Unit 157/157, and Data.Core pass; closure
  inventory is clean, so R04-02 is passed. A parallel design-only
  [`R04 Entity Facet Candidate Slate`](R04-ENTITY-FACET-CANDIDATES.md) elects the eventual R04-07
  language without changing the active production card or implementing public syntax.

## Next safe actions

1. Treat R04-02 and ARCH-0108 as the stable host/lifecycle base; do not reopen it for unrelated
   bootstrap, packaging, explanation, or Entity-language work.
2. Run the repository `explore` skill before R04-03's first production change.
3. Reproduce the non-completing bootstrap lane with an explicit executable runner and an observed test
   count; separate build time, discovery, composition, infrastructure wait, and teardown.
4. Elect the smallest deterministic lane that can finish or emit a phase-specific timeout without
   requiring external infrastructure.
5. Classify the known integration-host failed-start concern inside R04-03 instead of retroactively
   broadening ARCH-0108.
6. Keep the obsolete test-authoring guide for R04-08 and package restore truth for R04-04.
7. Preserve the current maturity labels until the later clean-room and negotiation gates pass.

## Expected working tree

R04-01 and R04-02's host lease, vector-model confirmation, AI discovery and lifecycle-capture
classification, active-host relationship metadata, lifecycle idempotence, startup-health ownership,
scheduler single-owner, orchestrator shutdown, application-identity, and non-hosted startup repairs
should be committed.
The closure-audit ledger, provider-owned aggregate-configuration repair, scoped Identity/Web startup,
and binder-owned data-spec and Entity-conformance repairs should also be committed. Host-scoped
logging and the dead-locator deletion should be committed; ARCH-0108 and its Data/AI consumer
migration may be the only active production diff.
Treat every unrelated pre-existing change as user-owned.

## Verification at handoff

- Core host-binding, OpenAPI, and Data.AI projects build with zero errors;
- Core Unit passes 79/79, the Core self-executing suite passes 204/204, Data.AI passes 82/82, and
  Data.Core passes 294/294;
- the AI unit project builds with zero errors and its self-executing suite passes 157/157;
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
- `KoanDataSpec` host ownership is red 1/3 before repair and green 3/3 after it; both boot overloads
  preserve a newer startup owner, older-host disposal leaves the newer host selected, InMemory passes
  55/55, and Data.Core passes 293/293;
- `EntityConformanceSpecs` host ownership is red 0/1 before repair and green 1/1 after it; an older
  conformance host cannot clear a newer owner, the meta-suite reports 11 passed with 3 intentional
  skips, and both focused consumer suites report 4 passed with 2 intentional skips;
- façade logging ownership is red 0/2 before repair and green 2/2 after it; a third focused guard
  fixes the binder ahead of later hosted services, Core passes 200/200, Core Unit passes 79/79, and
  the established 293-test Data.Core process exits successfully;
- background-service locator inventory finds zero getters after deletion; the focused orchestrator
  lifecycle passes 3/3, Core Unit passes 79/79, and Core passes 200/200;
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
- the closure audit still counts 14 compatibility `AggregateConfigs.Reset()` call sites, zero direct
  `src/` ambient assignments, thirteen category-only static logging scopes with zero cached loggers,
  zero static background providers, and no cached AI pipeline resolver; ARCH-0108 covers common
  required Data/AI host-context failures and R04-02 is passed;
- the complete Data.Core process passes 294/294 with zero disposed-service, meter-factory, or
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
