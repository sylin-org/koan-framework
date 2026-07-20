---
type: GUIDE
domain: framework
title: "Koan 0.20 Preview Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: tested
  scope: R12-06 first-wave pre-staging recovery and bounded certification-wave redesign
---

# Koan 0.20 preview current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome

- R00 through R07, R09 through R11, and R12-01 through R12-04 pass. R08's local candidate evidence is
  retained; its public tail is owned by R12.
- The public narrative, selective 0.20 guarantee boundary, package compiler, API-key release coordinator,
  templates, FirstUse, and GoldenJourney are implemented. Exactly the 38 supported package owners carry
  0.20 intent; repository membership or transitive dependency does not promote another package.
- The CLI and Aspire families remain shelved outside `Koan.sln`. Usagi Picks is a standalone product at
  `lbotinelly/usagipicks`, not a bundled Koan sample.
- [R12-06](work-items/r12/R12-06-publish-and-observe-first-wave.md) is active under the maintainer's explicit
  authorization. The established `NUGET_API_KEY` remains step-scoped to prepared promotion; OIDC is deferred.
- No package has been staged or published. There is no durable `automation/package-lineage-dev` branch,
  `release/dev/*` tag, GitHub Release, or public-wave escrow.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive
only when genuinely inert and independently consumed.

## Active slice: R12-06 first public wave

Five ordinary `dev` release events reached read-only exact-version proof and exposed independent runner defects.
Each was corrected at its existing owner with focused Windows/Linux evidence:

- `f6531198f`: cross-platform project-reference and docs-proof portability;
- `7b176a8cb`: rooted Web Auth provider paths resolve as HTTP application-relative URIs on Linux;
- `ea86be4b1`: deferred embedding fixture uses test-owned worker timing;
- `3c6988d6c`: Local storage enforces one portable object-key language;
- `d7d673719`: Jobs SQLite's 100,000-row claim sentinel has honest runner headroom.

Run `29766528071` was cancelled while still inside read-only proof after the maintainer rejected the accidental
106-project serial queue. Pack, escrow, lineage persistence, staging, and promotion did not run. Terminal cancellation
and empty lineage/tag/Release state were verified.

The certification topology is now redesigned at the existing `scripts/green-ratchet.ps1` chokepoint:

- the full solution still builds once at the exact version commit;
- every runnable test project still receives its own `dotnet test` process and five-minute hang detector;
- a processor-derived wave runs at most four projects concurrently, with an explicit bounded override;
- complete project logs are grouped, every project result is reported, and failures join once;
- package pack/clean-room/escrow remains unreachable until the joined ratchet is green;
- the same optimization applies to current proof and exceptional prior-wave reconstruction without adding a
  workflow job, manifest, artifact handoff, scheduler, or credential boundary.

Focused redesign evidence is green: release workflow contract 10/10, Core 339/339, Cache topology 63/63, a live
two-project concurrency probe, PowerShell parsing, broad docs with zero errors, and the public-docs truth gate.
The full release ratchet is intentionally not rerun locally; the protected workflow owns that exact boundary.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; before the pending redesign commit, local and `origin/dev` both resolve to `d7d67371997c13baebfbab1add500c70704e0e14`.
- Tracked pending changes are limited to the ratchet, its Packaging contract, NuGet publishing guidance, R12-06,
  and this handoff.
- `tmp/` is untracked local certification/evaluator material and must never be staged.
- No process from the cancelled workflow is local. The deliberately stopped broad Packaging verification left no
  owned test process; the exact contract class replaced it as the focused check.

## Resume here

1. Verify status and HEAD; never stage `tmp/`.
2. Commit the bounded certification-wave change and advance `dev` once through the normal path.
3. Observe all six release authority jobs. The exact proof should now run up to four isolated project processes at
   once and report all independent failures from that wave.
4. On red, do not restart the complete local ratchet. Isolate only the reported owners, apply one bounded correction,
   and let the next authorized `dev` event use coordinator recovery.
5. On terminal green, require NuGet visibility, exact lineage/tag/completion agreement, and an immutable GitHub
   Release before beginning public-feed template, FirstUse, GoldenJourney, provider-swap, rejection, facts, health,
   lockfile, and maintainer-comprehension observation.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not mass-promote all active packages to 0.20 or restore the shelved CLI/Aspire families.
- Do not recreate Data Access ambient subjects, `[AccessScoped]`, arbitrary durable filter carriage, or model
  decoration for request context; Web context belongs at ordered contributor chokepoints.
- Do not rerun Tenancy, Classification, completed package-family suites, or the complete local ratchet without an
  affected dependency and explicit need.
- Do not create a GitHub test job per project, split version-lineage authority across artifacts, weaken one-process
  isolation, skip suites, or permit staging before the joined proof succeeds.
- Do not stage `tmp/`, inspect private dogfood applications, hand-pack, manually choose packages, replace escrow,
  move tags, publish outside the coordinator, or mutate remote configuration.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- Prefer contributors at capability chokepoints over decorations and spread.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and
  policy; adapters own mechanics; applications own business intent.
- Standard .NET concepts are preferred over Koan ceremony. Break-and-rebuild is justified only by current code and
  consumer evidence.
