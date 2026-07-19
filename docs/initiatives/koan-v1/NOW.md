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
  scope: R11-05 Observability graduation and focused owner/package proof
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 pass. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, Web request context/Data Access, Data
  Backup/SoftDelete, Jobs, and now Observability have completed their current R11 family work.
- The evaluated graph remains 101 packages and 26 claims. Generated package quality is 3 repair-required, 15
  review-required, and 83 structurally ready.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Most recent completed slice: Observability

`Sylin.Koan.Observability` remains one optional functional leaf: reference it and the application's existing
`AddKoan()` call composes standard OpenTelemetry traces and metrics. Core retains only the inert
`ObservabilityOptions` and health/diagnostic primitives because they are independently consumed by Core/Web; no new
Contracts, Web, exporter, contributor, or source-registry package was created.

The discovered module now compiles one immutable host plan without `BuildServiceProvider`. The public
`AddKoanObservability` callback, sentinel, fixed source list, missing meter subscription, and trace-only OTLP headers
are removed. `Koan.*` is the single trace/meter subscription boundary; ASP.NET Core, `HttpClient`, and runtime
instrumentation join the same providers. Advanced applications use OpenTelemetry's own builder.

Invalid booleans, sample rates, and endpoints reject boot with the exact correction. `Enabled=false`, both signals
disabled, or Production without a package-owned endpoint creates no providers. Startup/provenance/composition facts
explain active state, signals, wildcard boundary, and exporter kind without exposing endpoint or headers.

## Focused proof

- Observability Release suite: 8/8.
- Observability and affected Web Release builds: zero warnings/errors.
- Observability Release pack is clean. The nupkg contains its owned README, canonical icon, DLL/XML,
  build-transitive props, exact Core/OpenTelemetry dependencies, repository commit, and matching symbols package.
- Current NuGet audit reports no known vulnerable direct or transitive package.
- Observability has zero generated structural findings. Package quality is 3 repair / 15 review / 83 structurally
  ready; product truth remains 101 packages and 26 claims, with operations/diagnostics now verified.
- No live collector/container or full release ratchet ran; complete certification remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; the Observability graduation is the current local HEAD after its handoff commit. The branch is
  expected to be 151 commits ahead of `origin/dev` and 0 behind; verify exact HEAD.
- `tmp/` remains untracked scratch/evaluator/artifact material and must never be staged.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, HEAD, and the focused evidence recorded in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Continue R11-05 with fresh exploration of the next unresolved family, currently the remaining Orchestration CLI,
   Aspire, generator, Docker/Podman provider, and Compose renderer topology. Contract isolation work already completed
   for `Orchestration.Abstractions`, `Aspire.Abstractions`, and the former CLI Core; do not repeat it.
3. Preserve the contributor mandate for context-aware Web behavior, but do not force persistent Entity semantics or
   operational work into Web contributors merely because contributors are the correct request-context chokepoint.

## Remaining temporary dispositions

The remaining Orchestration CLI, Aspire, generator, container-provider, and Compose renderer family; Security Trust;
Testing, Containers, and Hosting; Web Admin; and ZenGarden still require terminal R11-02 decisions.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not recreate Data Access, ambient subjects, `[AccessScoped]`, or durable arbitrary-filter carriage.
- Do not recreate Backup discovery/attributes/dashboard/inline HTTP or Web Extensions' partition-backed soft delete.
- Do not rebuild Jobs or reintroduce public runtime implementations, the claim-ticket branch, provider pin, Blocked
  state, pool registrar alias, or a Contracts package without new independent-consumer evidence.
- Do not rebuild Observability, restore `AddKoanObservability`, enumerate source names, introduce a Koan exporter or
  contributor registry, or move the inert options/health contract out of Core.
- Do not rerun Classification, standalone Tenancy, Jobs, or earlier family suites without an affected dependency.
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
- Standard .NET hosting, DI, options, assembly, MSBuild, NuGet, and OpenTelemetry concepts come before Koan-specific
  parts.
- Startup, facts, health, errors, HTTP/MCP, tests, and future tooling project canonical decisions; no projection
  becomes a second authority.
- Break-and-rebuild is preferred when compatibility preserves duplicate owners or crutches, but every rebuild is
  justified against current code rather than remembered history.

## Validation economy

- Start each new R11 family slice with focused exploration and coalescence assessment.
- Run only affected owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at the explicit R11-07 certification boundary.
