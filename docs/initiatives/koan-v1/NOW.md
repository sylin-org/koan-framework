---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: GardenCoop/inventory/S1/S0/S10/g1c2 passed; S14 workload-lab rebuild assessed and active; R08-05 paused
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Current state

- R00 through R07 are passed.
- [R09 — Compile the Semantic Composition Kernel](work-items/R09-semantic-composition-kernel.md) is passed.
  All nine children pass; [ARCH-0115](../../decisions/ARCH-0115-semantic-contribution-compilation.md)
  and [ARCH-0116](../../decisions/ARCH-0116-one-module-lifecycle.md) record the resulting constitution.
- R09 leaves one host-owned semantic model, one typed contribution compiler, one shared provider catalog,
  pillar-owned immutable plans, hard Tenancy coverage, canonical guarantee/explanation evidence, and one
  retained `KoanModule` lifecycle per functional implementation assembly.
- The initializer/auto-registrar compatibility kernel is deleted. Cross-module contracts live in isolated
  contract assemblies. Ordinary references carry activation; there is no `Inert`, `Required`, or equivalent
  project-reference activation metadata.
- Focused Core/bootstrap/package/journey/current-sample evidence is green. The unrelated SQLite discovery
  test-double compile drift is preserved as PMC-028 in [POST-CYCLE-TODO.md](POST-CYCLE-TODO.md).
- [R08 — Make Koan V1 responsibly releasable](work-items/R08-v1-release-readiness.md) is locally prepared and pending.
  R08-01's Git-owned package intent, exact release-wave escrow, resumable prior-wave promotion, and six
  least-privilege workflow boundaries remain the protected baseline. No package, tag, or GitHub Release was
  published or observed.
- [R08-02 — Safe connector telemetry](work-items/r08/R08-02-safe-connector-telemetry.md) is passed.
  `Redaction` is the single credential grammar; `KoanLog` is the single structured safety boundary;
  shared adapter/discovery/orchestration chokepoints own repetitive narration; PMC-019 is resolved.
- Focused evidence is green: 28 Core redaction/discovery cells, one repository bypass-policy cell, and
  17 affected connector builds. Release certification was intentionally not run.
- [R08-03 — Canonical product surface](work-items/r08/R08-03-canonical-product-surface.md) is passed.
  One compiler derives 108 package shapes/platforms/dependencies from standard project facts and joins
  them to 14 conservative claims. Thirty-seven missing owned READMEs and 88 unassessed packages remain
  visible; no support promotion is implied. PMC-010 is resolved.
- The obsolete `KoanPackageKind` taxonomy and release-manifest field are deleted. Checked-in JSON and
  Markdown projections byte-match regeneration; focused compiler/graph/planner/release-bundle proof passes 43/43.
- [R08-04 — Package-first templates](work-items/r08/R08-04-package-first-templates.md) is passed.
  One exact 108-package candidate proved release-derived template bands, direct-pack refusal, both generated
  template shapes, and package-only FirstUse/GoldenJourney. The first clean checkout also removed two hidden
  ambient-state assumptions from template tests and repository package discovery.
- PMC-029's responsible repair is implemented through
  [ARCH-0119](../../decisions/ARCH-0119-one-console-host-lifecycle.md). `StartKoan()` is now a one-line facade over
  the standard Generic Host instead of a partial raw provider: standard environment/lifetime services, hosted
  Communication/health startup, graceful disposal, and truthful startup evidence share one lifecycle. Focused
  owner/consumer proof passes 8/8 and a source-equivalent console completes Entity work without either false
  collection failure. Exact package repetition remains a next-candidate assertion, not a second 108-package run.
- [R10 — Graduate the golden sample portfolio](work-items/R10-golden-samples.md) is active by architect mandate.
  Every maintained sample must become an executable golden example of current Koan semantics and .NET practice;
  solution compilation alone is insufficient. [R10-01](work-items/r10/R10-01-gardencoop.md) now passes and the
  [golden-sample standard](work-items/r10/GOLDEN-SAMPLE-GRADUATION.md) records its reusable evidence bar.
- GardenCoop is a four-line host with one earned application module and one cumulative business/HTTP/facts proof.
  Its win-x64 NativeAOT executable serves the same result. Dogfood also repaired Lifecycle predecessor timing and
  AOT facts serialization centrally; native output is documented as a deployment directory, not a false single file.
- [R10-02](work-items/r10/R10-02-portfolio-inventory.md) passes with an exact 27-project physical inventory and
  explicit graduation/incubation/archive-or-delete queues. [R10-03](work-items/r10/R10-03-s1-web.md) passes:
  S1.Web is now the minimal deterministic AddKoan/Entity/EntityController relationship/cache rung with one
  cumulative dashboard/business/HTTP/facts proof.
- [R10-04](work-items/r10/R10-04-s0-console-json.md) passes. S0 is now one foundation reference, one owned
  standard host, one Entity/business method, and one deterministic materialized JSON result. The real process,
  JSON file, composition report, and shutdown are contract-backed; the public template shares ordinary `using var`.
- The architect strengthened R10 into an all-active-samples mandate. `Assess` and `incubate` are temporary migration
  states only; every project still presented as V1 curriculum must graduate or leave the active portfolio.
- [R10-05](work-items/r10/R10-05-s10-devportal.md) passes. S10 is now one Article Entity and a four-line host:
  approved local content publishes idempotently through typed named channels backed by SQLite, Mongo, or Postgres.
  Local and real-container evidence agree with readiness/facts; demo services, switching, benchmarking, random graph,
  AngularJS, helper/container scaffolds, and false claims are gone.
- [R10-06](work-items/r10/R10-06-g1c2-gardencoop-embedded.md) passes. g1c2 is now one local semantic-search story:
  the four-line host seeds five Produce Entities after AI composition, `[Embedding]` indexes them through referenced
  ONNX/sqlite-vec providers, and `ripe red tomato` ranks Heirloom Tomatoes first. Strict source and self-contained
  folder runs agree with readiness/facts. Dogfood also coalesced AI contribution startup, made invalid configured
  AI intent reject host startup, closed vector repository lifetime, and removed a single-file-unsafe Core diagnostic.
- No package, tag, GitHub Release, branch, or remote configuration was published or mutated. Initial coherent
  public observation and a later real public-to-candidate upgrade/rollback remain separate gates.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee,
  corrective failure, then internal types.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` remains the golden business-to-code comparison. Extra
  public concepts must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning
  and policy; adapters own mechanics; applications own business intent.
- Standard .NET/package structure is the first language. A functional assembly derives identity and exposes
  one domain-named `KoanModule`; shared vocabulary belongs in `*.Core`, `*.Abstractions`, or `*.Contracts`.
- Structural composition runs once per host/shape. Runtime operations execute immutable plans and bind ambient
  values without contributor discovery, reflection, or provider negotiation.
- Startup, facts, health, errors, HTTP/MCP, tests, and future tooling project canonical decisions; no
  projection becomes a second authority.
- Break-and-rebuild remains preferred where compatibility would preserve duplicate owners or crutches.
- The neutral operation model remains a V1.1 target and is outside the current release-readiness slice.

## Next safe action

Implement [R10-07](work-items/r10/R10-07-s14-workload-lab.md) as the assessed S14 replacement:

1. rename `S14.AdapterBench` to `S14.WorkloadLab` and replace the synthetic benchmark engine with one bounded,
   verified order-intake job and durable receipt;
2. keep the local SQLite target zero-infrastructure and express optional MongoDB, PostgreSQL, and Redis paths as
   typed named sources with corrective failures;
3. remove SignalR, provider rankings, direct database tuning, raw adapter switching, benchmark tiers/migrations,
   application-container scaffolding, and unsupported performance advice;
4. prove the dashboard, job lifecycle, exact cleanup, receipt, readiness/facts, unavailable-provider correction,
   and clean host shutdown in one focused cumulative contract;
5. keep [R08-05](work-items/r08/R08-05-initial-public-observation.md) prepared but do not mutate remote settings,
   push, tag, release, or publish until R10 reaches its portfolio boundary and authorization is renewed.

Do not call the proven local feed a public upgrade. It is exact candidate evidence only.

## Validation economy

- Start every R08 slice with focused exploration and coalescence assessment.
- Run only the named owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at an explicit release-certification boundary.

## Repository boundary

- Branch `dev`; R08 kickoff baseline `546817ee0d3a`.
- Preserve intentional and unrelated working-tree changes.
- Never stage scratch/evaluator material under `tmp/`.
- Do not inspect or name private downstream applications.
- Do not publish, push, tag, release, or mutate remote configuration without a separate request.
