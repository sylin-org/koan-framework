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
  scope: R11-05 Data Backup/SoftDelete graduation and affected-consumer proof green
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 pass. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, Web request context/Data Access, and now Data
  Backup/SoftDelete have completed their current R11 family work.
- The evaluated graph remains 101 packages and 26 claims. Generated package quality is 4 repair-required, 16
  review-required, and 81 structurally ready.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Most recent completed slice: Data semantic leaves

`Sylin.Koan.Data.SoftDelete` remains one opt-in persistent Entity semantic expressed by a single Data axis.
`T.WithDeleted()` now carries a type-targeted immutable ambient stack, so opening one recycle bin cannot reveal deleted
rows of another Entity. Nested scopes unwind independently; restore, target-scoped purge, tenancy, and other read
filters retain their prior guarantees.

`Koan.Web.Extensions` no longer exposes a second partition-moving “soft delete” meaning. Its generic controller,
registration helper, contracts, capability actions/policy, and role policy are retired. Ordinary Entity HTTP deletion
inherits `Koan.Data.SoftDelete` automatically when the application selects that Data law; a recycle-bin HTTP workflow
must be an explicit authorized product controller.

`Sylin.Koan.Data.Backup` was rebuilt from more than forty public concepts into one scoped `IBackupService`, create and
restore operations, immutable requests, and immutable receipts. Create writes a disk-bounded, provider-paged,
single-Entity ZIP with a versioned manifest, stable type/key identity, original partition, collision-proof ID, count,
and logical SHA-256 before publication through host-scoped Koan Storage. Restore validates the entire archive and
every record before its first batched upsert.

Removed Backup surface has no compatibility shim: model/assembly decoration, reflection/global discovery, static and
fake-instance facades, mutable manifest/performance models, catalog/query, validation dashboard, retention, hosted
maintenance, health, progress/cancel simulation, adapter optimization SPI, manual registrars, inline HTTP endpoints,
ASP.NET Core, and Newtonsoft.Json. DATA-0108 records the resulting recovery contract and honest non-guarantees.

## Focused proof

- Data Backup: 8/8 — SQLite + Local round-trip, bounded paging, partition preservation, corrupt/type mismatch
  fail-before-mutation, cancellation/no publication, resident-provider rejection, and repeated-name isolation.
- Data SoftDelete: 10/10 — ordinary semantics plus cross-type/nested type targeting.
- Web Extensions: 113/113 after duplicate-surface retirement.
- Entity-language SoftDelete consumer: 21/21.
- Tenancy × SoftDelete focused consumer: 4/4; the standalone Tenancy suite was not run.
- Backup, SoftDelete, and Web Extensions Release builds/packs are warning/error-free. Artifacts contain owned README,
  icon, DLL/XML, build-transitive props, symbols, and expected dependencies; Backup has no ASP.NET/Newtonsoft edge.
- Backup, SoftDelete, and Web Extensions have zero generated structural findings.
- The known PMC-032 stale `Koan.Core.Adapters` warning remains only in the SoftDelete test project.
- No full release ratchet ran; that remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; implementation/tests are committed locally as `a39edffa4`, followed by the documentation/truth
  commit. At handoff the branch is expected to be 148 commits ahead of `origin/dev` and 0 behind; verify exact HEAD.
- `tmp/` remains untracked scratch/evaluator/artifact material and must never be staged.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, HEAD, and the focused evidence recorded in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Continue R11-05 with fresh exploration of the next unresolved semantic/operational capability, currently
   `Sylin.Koan.Jobs`; do not presume its disposition or repeat completed package-family work.
3. Preserve the contributor mandate for context-aware Web behavior, but do not force persistent Entity semantics or
   operational work into Web contributors merely because contributors are the correct request-context chokepoint.

## Remaining temporary dispositions

Jobs; Observability; the remaining Orchestration CLI, Aspire, generator, container-provider, and Compose renderer
family; Security Trust; Testing, Containers, and Hosting; Web Admin; and ZenGarden still require terminal R11-02
decisions.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not recreate Data Access, ambient subjects, `[AccessScoped]`, or durable arbitrary-filter carriage.
- Do not recreate Backup discovery/attributes/dashboard/inline HTTP or Web Extensions' partition-backed soft delete.
- Do not rerun Classification, standalone Tenancy, or earlier family suites without an affected dependency.
- Do not run the full release ratchet before R11-07.
- Do not stage `tmp/`, inspect private dogfood applications, or use private identities in public docs.
- Do not push, tag, publish, release, deploy, or mutate remote configuration without separate authorization.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- Assess context-aware Web behavior first as ordered contributors. Evidence is not authority; validate once at the
  edge and project the resulting context through the existing centralized enforcement seam.
- Persistent Entity semantics belong in Data axes when decoration truthfully selects durable model behavior; they are
  not request-context contributors.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and policy;
  adapters own mechanics; applications own business intent.
- Standard .NET hosting, DI, options, assembly, MSBuild, and NuGet concepts come before Koan-specific parts.
- Startup, facts, health, errors, HTTP/MCP, tests, and future tooling project canonical decisions; no projection
  becomes a second authority.
- Break-and-rebuild is preferred when compatibility preserves duplicate owners or crutches, but every rebuild is
  justified against current code rather than remembered history.

## Validation economy

- Start each new R11 family slice with focused exploration and coalescence assessment.
- Run only affected owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at the explicit R11-07 certification boundary.
