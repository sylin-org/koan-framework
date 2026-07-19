---
type: GUIDE
domain: framework
title: "Koan 0.20 Preview Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: tested
  scope: R12 opening, selective 0.20 guarantee promotion, and coherent-public-narrative mandate
---

# Koan 0.20 preview current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome

- R00 through R07, R09, R10, and R11 pass.
- R08 is stopped with all local release/candidate evidence retained; its unexecuted public-observation
  and upgrade tail are superseded by R12.
- [R12 — Road to the 0.20 Preview](work-items/R12-road-to-020-preview.md) is active. The preview is a
  maturity cycle, not a feature campaign.
- Only packages whose public contracts Koan explicitly guarantees earn the 0.20 version signal.
  Demonstrated, experimental, and unassessed packages do not inherit 0.20 from build success,
  repository membership, or transitive dependency.
- [R12-04](work-items/r12/R12-04-coherent-public-narrative.md) owns the maintainer-mandated realignment
  of every public-facing surface into one greenfield, present-tense narrative.
- All 93 active packages have terminal topology dispositions, meaningful reference intent, package-owned presentation,
  and objective package-quality finding count zero. Evaluated product truth is 93 packages and 29 claims; exactly
  38 owners belong to supported claims and declare 0.20 intent.
- The exact R11-07 local public-release ratchet is green from repair commit `736b82cc3`.
- The exact R08-05 local candidate from source `844449dd8c` compiled version `378f43beb54f`, packed all 93 active
  packages, proved both templates plus package-only FirstUse/GoldenJourney, and produced deterministic local escrow.
- The bespoke Orchestration CLI and Aspire families remain shelved beyond V1 under `shelved/`, outside `Koan.sln`,
  active package discovery, and the release graph. Standard .NET/Aspire/Compose/Docker/Podman/Kubernetes tooling owns
  application topology.
- No remote state was changed: no push, tag, publication, Release, deployment, or repository configuration mutation.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Active slice: R12-04 coherent public narrative

R12-01 through R12-03 pass. The generated boundary is a 14-owner supported foundation plus 24
supported-extension owners. Exactly those 38 owners declare project-local 0.20 intent, every public
Koan dependency stays inside the supported closure, and no other package carries 0.20. The existing
product-surface compiler enforces claim/version/dependency agreement in both directions; there is no
maintained allowlist or stamping layer. Focused graph/version tests pass 41/41, lineage/planner tests
pass 39/39, and the packaging tool builds warning-clean.

[R12-04](work-items/r12/R12-04-coherent-public-narrative.md) is active. Compile the exact public-content
graph now covers 667 current assets, 656 current text surfaces, 107 historical boundaries, 42 navigation
targets, and 11 graduated sample roots. Root, site, package, template, sample, tool, contributor, and
agent-skill surfaces now tell one present-tense path: package install, four-line host, first Entity result,
progressive capability references, runtime facts and correction, application responsibilities, and the
generated maturity boundary. Obsolete duplicate Web/OpenAPI/AI/transaction/CLI/Aspire/Flow/Messaging
instruction was removed or classified as history; the competing docs inventory and hand-maintained adapter
matrix were retired. Historical ADRs and initiatives remain evidence outside the ordinary learning graph.

Focused R12-04 evidence passes: public-doc truth, 16/16 product/quality compiler tests, warning-clean
packaging build, byte-identical generated reports, all ten graduated sample Release builds and their exact
selective-0.20 lock audit, 20 strict skills, 20/20 instructional examples, whitespace checks, and broad
documentation structure with zero errors / 1,377 non-gating warnings. No full ratchet or remote mutation
ran. R12-04 remains open only for the two required independent public-context-only cold reads and any
corrections they uncover; package-only external-consumer proof remains downstream.

## Recently completed preview foundation

R12-01 passed with the maintainer-accepted checkpoint: ordinary stable-format `0.20.x`, project-local
`0.20` intent, NBGV-owned patches, claim-owned selective admission, and no automatic promotion by
dependency or repository membership. Its 35 packages remain an assessment slate, not admitted truth.

[R12-02](work-items/r12/R12-02-preview-blocking-seams.md) re-evaluated the complete PMC register
against that slate. Each concern receives one current disposition: repair, close by current evidence,
transfer to the preview phase that owns its terminal proof, or explicit exclusion/nonclaim. The first
exploration checks Storage's remaining layered-activation invariant through Media. GardenCoop C2 now
passes 1/1 without Storage in its graph, so the historical sample failure is closed and must not be
repeated as the current defect.

PMC-033 is now repaired at the generalized Storage chokepoint. Availability is inert until profile/
default configuration or actual service use declares routing intent; configured routes still compile
at startup, unconfigured use still fails correctively, and facts distinguish inactive availability.
Focused Release evidence passes Media Web 8/8, Storage 20/20, bootstrap pillars 13/13, Data.AI 87/87,
and GardenCoop C2 1/1. No full ratchet ran.

PMC-025 is closed as stale current-state evidence, not repaired code. Current source and package-only
FirstUse run on Windows without any EventLog override; the fresh focused source proof passes 1/1, and
.NET 10.0.8 already disables only the EventLog sink on `SecurityException`. Koan retains standard
host logging ownership.

PMC-001 is now repaired without renaming stored data or adding an analyzer. The framework-owned
`JobMetric` row is internal; its existing CLR/storage/`Count` identity remains unchanged. Applications
see one business-facing `JobMetrics.Summary(...)` operation. Jobs passes 84/84, Tenancy 16/16, and the
Jobs Release build is warning-clean.

PMC-002 and PMC-004 are now repaired together. MCP transport choices live once at the host through
explicit STDIO, Streamable HTTP, and deprecated legacy SSE switches; one `HttpRoute` and unified
session vocabulary replace the transitional master/override and SSE-derived names. Per-Entity
transport metadata is removed because every edge projects the same governed surface. One camelCase,
`[McpIgnore]`-aware application JSON policy now covers Entity and custom tools plus Code Mode while
protocol DTOs remain spec-owned. Conformance passes 80/80, Streamable/legacy HTTP 19/19, field
exclusion 5/5, Code Mode 27/27, source FirstUse/GoldenJourney 3/3, bootstrap pillars 13/13, and the MCP build is warning-clean.

PMC-007 and PMC-015 are now closed together at the existing Data/Web boundary. The current filter
AST/coordinator already gives query-capable adapters execute-or-reject result semantics; shared HTTP
proofs now cover compound/mixed-case input and reject malformed or unknown filters without dropping
them. InMemory passes 74/74, JSON 52/52, and SQLite 52/52. Data's cached first-use Entity shape guard
now rejects public properties that differ only by case before adapter creation, with one rename
correction; exact owner tests pass 9/9 and affected builds are warning-clean. No provider flag,
mapping attribute, or new filter layer was added.

PMC-024 is repaired without production code. The direct-reference manifest fixture now stages a
synthetic `Koan.Core` project, its local-only feed/cache, intermediates, and outputs under one temporary
root while importing the real production targets read-only. It asserts that no `ProjectReference`
escapes that root, preserves package/project manifest truth, and proves a missing package fails only
inside the fixture. The exact packaging cell passes 1/1.

PMC-003, PMC-028, and PMC-032 are closed by current evidence. R11-07's exact Release build is
zero-warning; SQLite's current discovery fake compiles and its connector suite passes 36/36; a fresh
XML inventory finds zero missing references across every test project. R12-02 therefore passes with
every PMC repaired, closed, transferred, or explicitly excluded against the preview guarantee.

[R12-03](work-items/r12/R12-03-preview-product-boundary.md) passes. It replaced the 35-package
assessment slate with the exact 38-owner supported closure, promoted only those owners to 0.20, and
made the generated product-surface compiler reject missing, leaked, or stray promotion.

## Most recent completed slice: R08-05 local exact candidate

The first rehearsal exposed and repaired two release-boundary defects without reopening package architecture:

- `5aeabb2a6` deletes Git file-similarity rename guessing; evaluated package ID/path continuity remains the one owner.
- `844449dd8` makes the first protected lineage bootstrap all current owners from the coherent source event instead of
  requiring a manual seed or interpreting a legacy noncanonical tree.

Focused lineage/workflow evidence passes 43/43 and focused docs lint passes. From exact source
`844449dd8c4927881853d315cba5a569cdb817c9`, the local candidate produced:

- version commit `378f43beb54f0e8ee8ca0876013526dc97597b4f`;
- 93 bootstrap packages, 93 markers, 93 nupkgs, and 90 expected snupkgs;
- clean-room pack in 723.2 seconds;
- `Sylin.Koan.Templates` `0.17.613`, with both public shapes reaching their business result;
- FirstUse in 3.546 seconds and GoldenJourney in 6.863 seconds with truthful lock/facts/readiness/rejection evidence;
- local bundle SHA-256 `79a9305f77d63c520fcf4a41b7daf0a27a98568ca9479fad1a3ba0ea4a2999dc`.

The bundle and evidence remain under `tmp/` and are not remote authority. No candidate process remains running.

## Most recent completed slice: R11-07 certification

The exact repository-owned command passed every leg:

```powershell
pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease
```

Evidence:

- 20 minutes 22 seconds total;
- public-release solution build: zero warnings/errors;
- committed composition lockfiles: no drift;
- 103 solution test projects: 4,648 passed, 30 intentional environment skips, 0 failed (4,678 total);
- Packaging: 196/196, including source-checkout FirstUse and GoldenJourney;
- broad docs: zero errors / 1,626 pre-existing non-gating warnings;
- public docs: 233 current files / 42 navigation targets;
- instructional examples: 20/20;
- strict skills: 20, zero errors/warnings;
- strict SQL blueprint: one, zero errors/warnings.

The ratchet left no tracked drift. Its 19 retained MSBuild node-reuse workers were identified by exact parent and
command line and stopped; no application/test process from this slice remains.

## Certification repairs now committed

- Removed a stale deleted Cache analyzer-test entry from `Koan.sln`.
- Restored one shared DI-hosted transform round-trip oracle for Mongo/Postgres/SQL Server tests without restoring the
  deleted process-static production pattern.
- Reconciled the Data lockfile and GoldenJourney rejection proofs with the current fail-fast configured-default
  promise: startup stops with the adapter, configuration key, safe connector correction, and no connection-string
  detail.
- Centralized the broad pillar suite's required Local Storage test profile rather than weakening provider validation.
- Serialized source-build Packaging probes through the existing executable-probe collection and made synthetic NuGet
  activation/caches deterministic.
- Removed stale Packaging expectations for the shelved Orchestration package and the intentionally removed
  Tenancy.Web → Communication edge.
- Reconciled public skills and the SQL blueprint with current module identity, relationship, Media, Storage,
  Observability, semantic activation, relational contracts, live samples, and the shelved CLI/Aspire boundary.
- Refreshed tracked sample lockfiles for the relational-abstractions split and OrderIntake's Npgsql/Redis closure.

No production runtime implementation was changed by R11-07. The only functional tool change is the executable probe's
bounded process-exit observation so GoldenJourney can prove fail-fast startup truth.

## Package graduation outcome that must remain closed

- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, Web request context/Data Access, Data
  Backup/SoftDelete, Jobs, Observability, Security Trust, Testing, Web Admin, and ZenGarden completed R11 family work.
- Canon's R10-11 automatic contributor discovery, functional/Web split, four-line CustomerCanon host, and
  failed/parked non-persistence remain intact.
- Context-aware Web behavior uses ordered `IWebContextContributor`s at the request chokepoint. Scoped-link evidence,
  tenant, principal, capability scopes, rejection, and read predicates are centralized contributors; arbitrary durable
  filter carriage and model decoration are not restored.
- `Sylin.Koan.ZenGarden.Contracts` remains a dependency-free inert boundary; the runtime remains one functional
  package with `ZenGarden.Offering`, `.Storage`, and `.Capability` as its application surfaces.
- Observability remains one optional standard OpenTelemetry leaf; Jobs remains one capability with internal runtime
  mechanics and Communication wake hints; Security Trust remains one ES256 trust path.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; R11 completion is `3f7ca5186`, followed by R08-05 repairs `5aeabb2a6` and `844449dd8`,
  local-candidate closeout `e40cd4525`, API-key architecture amendment `5c346082c`, workflow wiring
  `f1a816b02`, and R12 selective-boundary commit `679ae4f97`. Verify exact HEAD and ahead/behind counts
  before continuing.
- `tmp/` is untracked certification/evaluator/artifact material and must never be staged.
- The tracked worktree contains the coherent R12-04 public-narrative tranche until it is committed.

## Resume here

1. Verify `git status`, HEAD, and the R12-04 implementation/evidence record before work.
2. Obtain two independent public-context-only cold reads against the compiled public learning path;
   record anonymous findings and make repository-owned corrections without exposing initiative history.
3. Re-run the focused R12-04 public-doc, structural-doc, skill, example, packaging-generation, sample-lock,
   and whitespace checks affected by those corrections; do not substitute the full release ratchet.
4. Close R12-04 only when both readers reproduce the intended story with no unresolved contradiction,
   then advance to the next R12 maturity slice.
5. Preserve the R08 release compiler and API-key promotion path unless current evidence identifies a
   defect required by the selective preview.
6. Stop before repository-secret or repository/branch/environment settings, push/merge, tag, GitHub
   Release, NuGet publication, deployment, or other remote mutation until R12-06 records and rechecks
   its exact terminal target and gates.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not mass-promote all active packages to 0.20; the version is an earned guarantee signal.
- Do not equate R11 structural/package quality or `verified` test maturity with an accepted compatibility guarantee.
- Do not rerun R11 family suites or the complete ratchet without a new affected dependency or explicit certification
  need; R11-07 has the one required green complete boundary.
- Do not recreate Data Access ambient subjects, `[AccessScoped]`, durable arbitrary-filter carriage, Backup HTTP
  control planes, Web Extensions partition-backed soft delete, or Jobs' removed public runtime/claim branches.
- Do not restore Observability's manual registration/fixed source list, Trust's shared-secret/parallel issuer stack,
  Testing's catch-all skips, Web Admin's false control plane/security bypasses, or ZenGarden's removed endpoint
  manager/storage CDC/manual Configure/generic events.
- Do not move shelved CLI or Aspire projects into `src/`/`Koan.sln` or present their source as V1 capability.
- Do not stage `tmp/`, inspect private dogfood applications, or use private identities in public docs.
- Do not push, tag, publish, release, deploy, or mutate remote configuration without separate explicit authorization.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- Assess context-aware Web behavior first as ordered contributors. Validate evidence once at the edge and project the
  resulting context through the centralized enforcement seam.
- Persistent Entity semantics belong in Data axes when decoration truthfully selects durable model behavior; they are
  not request-context contributors.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and policy;
  adapters own mechanics; applications own business intent.
- Standard .NET concepts are preferred over Koan ceremony. Break-and-rebuild is justified only by current code and
  consumer evidence.
