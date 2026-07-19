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
  scope: R11-05 Web request-context coalescence and Data Access retirement focused-proof green
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 pass. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-04 pass. R11-05 is active and graduates package families in dependency order before the single
  R11-07 release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, and now the Web request-context/Data Access
  slice have completed their current R11 family work.
- The current evaluated graph contains 101 packages and 26 claims. Generated package quality reports 5
  repair-required, 17 review-required, and 79 structurally ready packages.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Most recent completed slice: ordered Web request context

The initial Data Access `keep` proposal was explicitly rejected before production edits because model decoration,
token-prefix metadata, an ambient subject/carrier, a Data axis, middleware, and application services spread one link
decision across too many places. The accepted checkpoint generalized context-aware Web behavior as contributors.

Implemented result:

- `Koan.Web` owns public `IWebContextContributor` plus `WebContext` and one automatic post-authentication runner;
- contributors execute by order, and each resolved principal/capability/filter scope is synchronously entered before
  the next contributor runs, then all scopes unwind after downstream execution;
- `Where<TEntity>` AND-composes typed predicates and one Web-owned bridge supplies them to Data's existing neutral
  `IReadFilterContributor`/`ReadScopeFold` chokepoint;
- Dev Identity contributes the principal at order 0 and Identity Tenancy contributes authorized roles plus tenant at
  order 100, without their former middleware wrappers;
- SnapVault contributes its validated gallery context at order 200: `_as` identifies the person; `event` selects one
  exact active durable grant and is never authorization by itself; the contributor supplies that grant's tenant and
  `PhotoAsset.EventId` predicate or rejects before endpoints;
- raw Entity/key/Vector/Entity-backed Media reads inherit the filter automatically; proofing writes retain explicit
  grant-permission authorization;
- active request predicates bypass global Entity cache reads and cannot seed global entries; and
- `Sylin.Koan.Data.Access`, `[AccessScoped]`, `Subject`, its carrier/options/axis/test project, SnapVault subject
  middleware, and `GuestScopeService` are retired with no replacement package or compatibility shim.

Request predicates are intentionally Web-lifetime read visibility. They do not authorize writes, secure raw storage
or SQL, or serialize arbitrary filters into jobs. Durable work establishes or re-resolves its own business context.

## Focused proof

- Web context lifecycle: 4/4.
- Security Trust development identity: 30/30.
- Identity and Identity Tenancy: 85/85.
- Cache topology including dynamic read context: 63/63.
- Media Web context inheritance: 7/7.
- SnapVault real link/grant/proof/media/revocation/operator journey: 28/28.
- `Sylin.Koan.Web` Release pack is warning/error-free and contains DLL/XML, owned README, icon,
  build-transitive props, and a symbol package.
- Generated truth: 101 packages — 5 repair, 17 review, 79 structurally ready — and 26 claims.
- The known PMC-032 stale `Koan.Core.Adapters` test-project reference warning remains in Web WellKnown; execution is
  green and this slice did not affect it.
- No full solution/release ratchet ran; that remains R11-07 work.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; the slice is committed locally as `4b0acd7a2` (implementation/tests) plus its following docs/truth
  commit. At handoff the branch is 146 commits ahead of `origin/dev` and 0 behind; verify exact HEAD before the next
  action.
- `tmp/` remains untracked scratch/evaluator material and must never be staged.
- No push, publication, tag, release, deployment, remote mutation, private downstream inspection, or full release
  certification occurred.

## Resume here

1. Verify `git status`, HEAD, and the focused evidence recorded in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Continue R11-05 with fresh exploration of the next dependency-ordered unresolved Data semantic leaf. The active
   rows are `Sylin.Koan.Data.Backup` and `Sylin.Koan.Data.SoftDelete`; do not presume either disposition.
3. Preserve the contributor mandate when assessing future request-context behavior: seek the problem-space chokepoint
   and reusable lifecycle before adding model decoration, per-surface hooks, or new package ceremony.

## Remaining temporary dispositions

Data Backup and SoftDelete; Jobs; Observability; the remaining Orchestration CLI, Aspire, generator,
container-provider, and Compose renderer family; Security Trust; Testing, Containers, and Hosting; Web Admin; and
ZenGarden still require terminal R11-02 decisions.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not recreate Data Access, ambient subjects, `[AccessScoped]`, or durable arbitrary-filter carriage.
- Do not rerun Classification, standalone Tenancy, or earlier family suites without an affected dependency.
- Do not run the full release ratchet before R11-07.
- Do not stage `tmp/`, inspect private dogfood applications, or use private identities in public docs.
- Do not push, tag, publish, release, deploy, or mutate remote configuration without separate authorization.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- Assess context-aware Web behavior first as ordered contributors. Evidence is not authority; validate once at the
  edge and project the resulting context through the existing centralized enforcement seam.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and policy;
  adapters own mechanics; applications own business intent.
- Standard .NET hosting, DI, options, assembly, MSBuild, and NuGet concepts come before Koan-specific parts.
- Startup, facts, health, errors, HTTP/MCP, tests, and future tooling project canonical decisions; no projection becomes
  a second authority.
- Break-and-rebuild is preferred when compatibility preserves duplicate owners or crutches, but every rebuild is
  justified against current code rather than remembered history.

## Validation economy

- Start each new R11 family slice with focused exploration and coalescence assessment.
- Run only affected owner/consumer, package, documentation, and security cells during implementation.
- Run the complete public-release ratchet only at the explicit R11-07 certification boundary.
