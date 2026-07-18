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
  scope: R11-05 RabbitMQ Communication provider graduation implemented and focused-proof green
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 are passed. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, and now the RabbitMQ Communication provider have
  completed their current R11 family slices.
- The current evaluated product graph contains 102 packages and 26 claims. Generated package quality reports 6
  repair-required, 17 review-required, and 79 structurally ready packages.

The accepted architecture remains business intent first: Entity-first application language, references express
capability intent, `AddKoan()` compiles one host-owned semantic constitution, pillars own meaning and runtime
chokepoints, adapters own mechanics, and applications own business rules. Cross-module contract packages survive only
when they are genuinely inert and independently consumed.

## Most recent completed slice: RabbitMQ Communication provider

R07-10 was not rebuilt. Its direct-reference provider election, named-channel topology, broker-confirmed acceptance,
authenticated context, typed group/node fan-out, selection-aware health, internal framework routes, no remote
settlement, and fail-closed external reach remain the product contract.

R11-05 records and implements `keep` for `Sylin.Koan.Communication.Connector.RabbitMq`:

- standard `ConnectionStrings:RabbitMq` is the single explicit endpoint;
- `RABBITMQ_URL`, Aspire discovery, and Koan orchestration remain;
- the duplicate public endpoint option and legacy Koan endpoint aliases are gone;
- discovery/orchestration evaluators are internal mechanics;
- stable broker/protocol values have one existing constants owner;
- module reporting names candidate lanes rather than fixed default routes; and
- package/current reference/product claim evidence states the exact meaningful result and limits.

The application path remains one package reference, ordinary `AddKoan()`, the existing receiver, and
`Entity.Transport.Send`. No new registration surface, package, abstraction, or broker grammar was added.

## Focused RabbitMQ proof

- Release connector build: zero warnings and zero errors.
- Real RabbitMQ connector suite: 9/9.
- Provider-neutral provider-election facts: 8/8.
- The known PMC-032 stale `Koan.Core.Adapters` test-project reference warning remains; execution is green.
- The Release nupkg contains the package README, icon, DLL/XML pair, build-transitive props, exact dependencies, and a
  symbol package; current direct/transitive vulnerability audit reports no known vulnerable package.
- Generated quality/product truth is 102 packages — 6 repair, 17 review, 79 structurally ready — and 26 claims.
  RabbitMQ is structurally ready with no findings and current claim links.
- No full solution/release ratchet ran; that remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; verify the exact HEAD and ahead/behind count before the next action.
- Canon is already committed locally in `64bf83d69` and `ef037a479`; RabbitMQ implementation/docs are the current
  uncommitted slice until intentionally committed.
- `tmp/` remains untracked scratch/evaluator material and must never be staged.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, `HEAD`, and the focused RabbitMQ evidence in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Review the implemented RabbitMQ disposition and exact 102-row matrix in
   [R11-02](work-items/r11/R11-02-package-topology-inventory.md).
3. If the slice is not yet committed, commit code/tests separately from docs/generated truth and never stage `tmp/`.
4. Continue R11-05 with the next dependency-ordered `assess` family. The first unresolved active row is
   `Sylin.Koan.Data.Access`; begin with fresh exploration rather than presuming its disposition.

## Remaining temporary dispositions

Data Access, Backup, and SoftDelete; Jobs; Observability; the remaining Orchestration CLI, Aspire, generator,
container-provider, and Compose renderer family; Security Trust; Testing, Containers, and Hosting; Web Admin; and
ZenGarden still require terminal R11-02 decisions.

## Do not redo

- Do not reopen R07-10's RabbitMQ transport/provider architecture or rerun its historical assessment.
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
