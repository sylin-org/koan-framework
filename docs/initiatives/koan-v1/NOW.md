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
  and objective package-quality finding count zero. Evaluated product truth is 93 packages and 26 claims.
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

## Active slice: R12-01 preview contract and version band

Before any version file changes, R12-01 must:

- inventory every active owner's current version intent, exact projected package identity, maturity
  evidence, claim ownership, and public dependency boundary;
- decide the exact NuGet/SemVer meaning of “0.20 preview”;
- select which capability contracts become `supported-foundation` or `supported-extension`;
- map those guarantees to exact package owners without mechanically promoting their entire dependency graph;
- prove that any promoted package has no lower-maturity dependency that invalidates its guarantee;
- define compatibility, support, platform, security, and feedback expectations;
- present the resulting architecture checkpoint before selective `version.json` edits.

The product currently has 26 assessed capability claims: 15 verified, 9 demonstrated, one
experimental, and one specified. `verified` is evidence strength, not yet a compatibility guarantee.
The first read-only R12-01 inventory found 31 packages named by verified claims, six additional public
dependencies, and three entry/template packages: a 40-package ceiling. The proposed initial slate is
35 packages; Media/Storage's five-package closure is conditional on an earned Storage guarantee and
PMC-033 correction. The proposed version grammar is ordinary `0.20.x` with project intent `0.20`, not
a NuGet prerelease suffix and never a hand-authored patch.

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
  `f1a816b02`, and the current R12 opening. Verify exact HEAD and ahead/behind counts before continuing.
- `tmp/` is untracked certification/evaluator/artifact material and must never be staged.
- The tracked worktree is expected clean after the R12 opening commit.

## Resume here

1. Verify `git status`, HEAD, and the R12/R12-01 decisions before work.
2. Complete R12-01's read-only version, claim, guarantee, and dependency-boundary inventory.
3. Present the exact 0.20 channel, promoted package set, compatibility contract, and version-expression
   checkpoint before editing any `version.json`.
4. Do not begin the R12-04 mass narrative rewrite until R12-01 through R12-03 settle what the public
   product guarantees; inventory and contradiction collection are safe earlier.
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
