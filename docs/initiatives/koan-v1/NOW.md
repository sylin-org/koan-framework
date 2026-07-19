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
  scope: R11-05 Jobs graduation and focused owner/consumer/package proof
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 pass. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, Web request context/Data Access, and now Data
  Backup/SoftDelete and Jobs have completed their current R11 family work.
- The evaluated graph remains 101 packages and 26 claims. Generated package quality is 4 repair-required, 15
  review-required, and 82 structurally ready.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Most recent completed slice: Jobs

`Sylin.Koan.Jobs` remains one functional capability and one Entity-first application promise: implement
`IKoanJob<T>`, provide static `Execute`, submit or inspect through `.Job`/`.Jobs`, and let `AddKoan()` compose the
coordinator, ledger, context restoration, worker, scheduler, and Communication wake. No Contracts or transport
package was introduced because the only cross-module consumer intentionally activates Jobs through
`IJobCoordinator`; Communication owns only the neutral wake signal.

Built-in orchestrator, scheduler, registry, selector, compiled binding/policy, and ledger implementations are now
internal host mechanics. Durable claims automatically use Data conditional replace when the elected adapter declares
it and otherwise retain the explicit optimistic at-least-once fallback. The unproved probabilistic Ticket path,
claim-window option, and fourth claim-ticket Entity are removed.

`[JobPersistence(DataStore)]` now fails host composition correctively when no durable Data adapter is available;
`Auto` remains explicitly ephemeral in that topology. The unused provider pin, never-produced `Blocked` status,
redundant pool registrar, stale runtime-constructor compatibility requirement, and nonexistent handler-class promise
are removed. Live resource pools use standard DI through the retained `IJobPoolResolver` seam.

## Focused proof

- Jobs core: 83/83.
- Jobs SQLite: 80/80, including durable routing/schema/CAS and the corrected 100,000-row buried-lane guard.
- Jobs × Tenancy: 16/16; the standalone Tenancy suite was not run.
- Entity-language consumer: 25/25; MCP Operations: 5/5; OrderIntake: 1/1; SnapVault: 28/28.
- GoldenJourney builds Release cleanly.
- The focused Bootstrap Jobs spec compiles but its host stops before Jobs assertions on the unrelated existing required
  `LocalStorageOptions.BasePath` fixture configuration.
- The Jobs Release build/pack is warning/error-free. The artifact contains owned README, canonical icon, DLL/XML,
  build-transitive props, symbols package, and the expected dependency graph.
- Jobs has zero generated structural findings. Package quality is 4 repair / 15 review / 82 structurally ready.
- The known PMC-032 stale `Koan.Core.Adapters` warning remains limited to focused test projects.
- No full release ratchet ran; that remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; the Jobs graduation is the current local HEAD after handoff commit. The branch is expected to be 150
  commits ahead of `origin/dev` and 0 behind; verify exact HEAD.
- `tmp/` remains untracked scratch/evaluator/artifact material and must never be staged.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, HEAD, and the focused evidence recorded in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Continue R11-05 with fresh exploration of the next unresolved semantic/operational capability, currently
   `Sylin.Koan.Observability`; do not presume its disposition or repeat completed package-family work.
3. Preserve the contributor mandate for context-aware Web behavior, but do not force persistent Entity semantics or
   operational work into Web contributors merely because contributors are the correct request-context chokepoint.

## Remaining temporary dispositions

Observability; the remaining Orchestration CLI, Aspire, generator, container-provider, and Compose renderer family;
Security Trust; Testing, Containers, and Hosting; Web Admin; and ZenGarden still require terminal R11-02 decisions.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not recreate Data Access, ambient subjects, `[AccessScoped]`, or durable arbitrary-filter carriage.
- Do not recreate Backup discovery/attributes/dashboard/inline HTTP or Web Extensions' partition-backed soft delete.
- Do not rebuild Jobs or reintroduce public runtime implementations, the claim-ticket branch, provider pin, Blocked
  state, pool registrar alias, or a Contracts package without new independent-consumer evidence.
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
