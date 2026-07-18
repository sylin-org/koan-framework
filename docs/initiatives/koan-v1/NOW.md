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
  scope: R11-05 through Cache, Redis, and shared-backend provider-family graduation; remaining providers next
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
  One compiler now derives 109 package shapes/platforms/dependencies from standard project facts and joins
  them to 15 conservative claims. Thirty-six missing owned READMEs and 86 unassessed packages remain
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
- [R10 — Graduate the golden sample portfolio](work-items/R10-golden-samples.md) is passed by architect mandate.
  Every maintained sample must become an executable golden example of current Koan semantics and .NET practice;
  solution compilation alone is insufficient. [R10-01](work-items/r10/R10-01-gardencoop.md) now passes and the
  [golden-sample standard](work-items/r10/GOLDEN-SAMPLE-GRADUATION.md) records its reusable evidence bar.
- GardenCoop is a four-line host with one earned application module and one cumulative business/HTTP/facts proof.
  Its win-x64 NativeAOT executable serves the same result. Dogfood also repaired Lifecycle predecessor timing and
  AOT facts serialization centrally; native output is documented as a deployment directory, not a false single file.
- [R10-02](work-items/r10/R10-02-portfolio-inventory.md) passes with an exact 27-project physical inventory and
  explicit graduation/incubation/archive-or-delete queues. [R10-03](work-items/r10/R10-03-s1-web.md) passes:
  TaskGraph is now the minimal deterministic AddKoan/Entity/EntityController relationship/cache rung with one
  cumulative dashboard/business/HTTP/facts proof.
- [R10-04](work-items/r10/R10-04-s0-console-json.md) passes. LocalChecklist is now one foundation reference, one owned
  standard host, one Entity/business method, and one deterministic materialized JSON result. The real process,
  JSON file, composition report, and shutdown are contract-backed; the public template shares ordinary `using var`.
- The architect strengthened R10 into an all-active-samples mandate. `Assess` and `incubate` are temporary migration
  states only; every project still presented as V1 curriculum must graduate or leave the active portfolio.
- [R10-05](work-items/r10/R10-05-s10-devportal.md) passes. DevPortal is now one Article Entity and a four-line host:
  approved local content publishes idempotently through typed named channels backed by SQLite, Mongo, or Postgres.
  Local and real-container evidence agree with readiness/facts; demo services, switching, benchmarking, random graph,
  AngularJS, helper/container scaffolds, and false claims are gone.
- [R10-06](work-items/r10/R10-06-g1c2-gardencoop-embedded.md) passes. GardenCoop Chapter 2 adds one local semantic-search story:
  the four-line host seeds five Produce Entities after AI composition, `[Embedding]` indexes them through referenced
  ONNX/sqlite-vec providers, and `ripe red tomato` ranks Heirloom Tomatoes first. Strict source and self-contained
  folder runs agree with readiness/facts. Dogfood also coalesced AI contribution startup, made invalid configured
  AI intent reject host startup, closed vector repository lifetime, and removed a single-file-unsafe Core diagnostic.
- [R10-08](work-items/r10/R10-08-public-documentation.md) passes. The public product surface is now one
  greenfield curriculum: root/agent front doors, 36 product-only navigation targets, graduated samples,
  current architecture/references, and 174 package/current companion files agree. A merge-gated truth
  check rejects removed activation/Messaging vocabulary, stale routes/versions/samples, non-awaited hosts,
  invalid package recipes, non-product navigation, and ADR edits. Product-surface proof passes 7/7 and
  the real FirstUse source contract passes 1/1; ADRs remain untouched.
- [R10-09](work-items/r10/R10-09-semantic-sample-portfolio.md) passes. Samples now live under semantic
  `fundamentals`, `journeys`, and `applications` identities; unrelated global numbers and non-earning projects are
  gone. GardenCoop Chapter 2 is a strict runnable superset of Chapter 1, while OpenGraph's automatic web-pipeline
  contribution lets DevPortal own the Article social-card story without changing its four-line host.
- Focused retained evidence is green: OpenGraph 39/39, DevPortal 1/1, GardenCoop C1 1/1, GardenCoop C2 1/1,
  LocalChecklist 1/1, TaskGraph 5 pass plus 2 intentional skips, OrderIntake 1/1, SnapVault 34/34, and CustomerCanon
  1/1. The public truth gate passes across 178 current files and 36 navigation targets.
- [R10-10](work-items/r10/R10-10-snapvault.md) passes. SnapVault is a local-first photo studio with SQLite/local
  storage, durable HTTP ingest, media serving, scoped client sharing, optional AI/vector enrichment, and
  participation-owned readiness. Its strict build, 34/34 focused suite, manual startup, facts, docs, and clean stop
  agree.
- [R10-11](work-items/r10/R10-11-customer-canon.md) passes. Canon is split into inert Contracts, one functional
  module, and optional Web projection; generated registry discovery compiles host-owned pipelines automatically.
  Failed/parked phases stop before canonical/index commit. CustomerCanon is a four-line local-first host with one
  canonical Entity, one policy owner, two thin phase adapters, generated HTTP/inspection, and a cumulative host proof.
- The final R10 boundary builds all ten published applications with zero warnings/errors. Eight sample-owned suites
  pass 45 tests with 2 intentional TaskGraph skips and no failures. Public truth passes across 178 current files and
  36 navigation targets; the product surface contains 15 claims and 109 packages.
- [R11 — Graduate the NuGet product surface](work-items/R11-package-product-quality.md) is active by architect
  approval. A package is now treated as a product promise: every survivor must earn a distinct reference intent,
  explain its smallest meaningful result, and pass role-proportional artifact, documentation, consumer, operator,
  and agent evidence before the exact R08-05 candidate.
- [R11-01](work-items/r11/R11-01-quality-contract-and-compiler.md) is passed. `Koan.Packaging` remains the one
  evaluated package owner; the new read-only quality projection derives roles and objective repair signals without
  project attributes, a maintained package list, release mutation, or a support promotion. R11-02 will hold only
  irreducible keep/merge/split/rename/retire judgments.
- The real baseline contains all 109 packages exactly once: 37 require objective repair, 72 require review, none is
  inferred graduated, 73 own a README, and 63 have technical companions. JSON/Markdown regenerate byte-for-byte;
  11 focused compiler cells and the warning-clean packaging-tool Release build pass. R11-02's opening matrix contains
  the same 109 identities exactly once in temporary `assess` state.
- R11-01 also corrected one shared evaluated-facts defect: empty `TargetFrameworks` previously hid every normal
  project's single `TargetFramework`. Both generated references now report the actual target for all 109 packages.
- R11-03 and R11-04 pass: the canonical mascot and package identity substrate are compiled once, the two entry
  bundles plus Templates are structurally ready, and the zero-configuration package-first golden journey proves the
  AddKoan/Entity/EntityController path.
- R11-05's foundation and contract-isolation families pass. Core Adapters and CLI Core were merged into their real
  owners; ZenGarden was renamed to an inert Contracts boundary; AI and Vector contracts shed functional leakage;
  Storage gained one necessary inert Abstractions package; and functional `MediaEntity<TEntity>` moved to Media Core
  under the application namespace `Koan.Media`.
- The Storage provider subfamily now passes. One compiled plan owns profile validation, exact pins, placement
  election, replication, capability truth, and receipts; adapters own physical IO and `StorageService` executes the
  chosen route. Unsupported registration/fallback/capability shapes and the obsolete Local suite are deleted.
- The Cache family now passes. One immutable topology owns Local/Remote provider election, pins, capabilities,
  receipts, and executable tier semantics. Memory and SQLite remain thin stores; SQLite proves exact tags, sliding
  expiry, schema migration, and restart persistence. Unsupported registration, per-policy provider pins, duplicate
  registries, false stale-revalidation language, and the Cache-specific analyzer/helper are gone.
- The Redis family now passes. `Sylin.Koan.Redis` is the single backend owner for endpoint discovery,
  orchestration, connection pooling, and disposal; Data Redis and Cache Redis own only their pillar semantics.
  Cache no longer activates Data, both consumers share the standard default `IConnectionMultiplexer`, and
  `IRedisConnectionProvider` lives in an inert contract package. `IKoanAspireResources` likewise moved to isolated
  Aspire contracts so contributors do not activate the functional runtime.
- The relational provider family now passes. One functional relational owner executes immutable route-local schema
  policy; contracts and Npgsql mechanics are module-free; Cockroach no longer activates PostgreSQL; dead Dapper and
  storage-shape surfaces are retired. Focused owner and real SQLite/PostgreSQL/Cockroach/SQL Server paths pass.
- Current generated truth contains 111 packages: 26 repair-required, 47 review-required, 38 structurally ready.
  Redis Cache 6/6 and Redis Data 12/12 pass; seven affected packages and their dependency boundaries were inspected,
  and the three new packages have no known vulnerable direct or transitive packages. Earlier Storage evidence remains
  green. Seven relational packages also inspect cleanly, with clear current vulnerability checks for the two new
  packages and Cockroach. S3 engine conformance remains an honestly stated evidence gap rather than an inferred
  compatibility claim.
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

Continue [R11-05](work-items/r11/R11-05-package-family-graduation.md) with the next terminally unassessed provider
pillar. Start with focused topology/coalescence discovery, choose the smallest family whose boundary can become
executable in one slice, and graduate package prose and artifacts only after its responsibility chokepoints and
guarantees are proved.

Keep [R08-05](work-items/r08/R08-05-initial-public-observation.md) prepared. Do not mutate remote settings, push,
tag, release, or publish until package polish is accepted and separate remote-operation authorization is renewed.

Do not call the proven local feed a public upgrade. It is exact candidate evidence only.

## Validation economy

- Start every R11 family slice with focused exploration and coalescence assessment.
- Run only the named owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at an explicit release-certification boundary.

## Repository boundary

- Branch `dev`; R08 kickoff baseline `546817ee0d3a`.
- Preserve intentional and unrelated working-tree changes.
- Never stage scratch/evaluator material under `tmp/`.
- Do not inspect or name private downstream applications.
- Do not publish, push, tag, release, or mutate remote configuration without a separate request.
