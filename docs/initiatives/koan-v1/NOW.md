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
  scope: R12-06 SQLite first-use correction and parallel read-only release lanes
---

# Koan 0.20 preview current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome

- R00 through R07, R09 through R11, and R12-01 through R12-04 pass. R08's local candidate evidence is
  retained; its public tail is owned by R12.
- The public narrative, selective 0.20 guarantee boundary, package compiler, API-key release coordinator,
  templates, FirstUse, and GoldenJourney are implemented. Exactly the supported package owners carry 0.20 intent;
  repository membership or transitive dependency does not promote another package.
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

Release run [`29769598076`](https://github.com/sylin-org/koan-framework/actions/runs/29769598076) established two
important facts before stopping safely:

- all 106 isolated test projects passed under bounded concurrency; the exact ratchet took 19m46s;
- the later package-only generated console failed on its first `Todo.Save()` because SQLite did not create its table
  under the Generic Host's ordinary `Production` environment.

The failure occurred before escrow, lineage persistence, staging, promotion, tags, Releases, or publication. Remote
lineage/tag/Release absence was reverified after the run.

The SQLite defect is a regression from relational schema consolidation `a2facdefc`. SQLite previously treated its
default `AutoCreate` policy as sufficient permission for its embedded application-owned file. That literal
zero-configuration behavior is restored at the provider configuration chokepoint. `Validate`, `NoDdl`, and
`[ReadOnly]` still prohibit schema mutation; network relational providers retain their production guard.

The release feedback path is also redesigned:

- `prove_current` uses two read-only matrix lanes over the same exact event/version commit;
- certification runs build, complete tests, docs, skills, lockfile, and blueprint proof;
- packages concurrently runs pack, closure, generated templates, FirstUse, GoldenJourney, and escrow assembly;
- both lanes join at `stage_current`, which remains the first durable mutation;
- the API key remains available only to the later prepared promotion step;
- inside certification, 105 ordinary test projects run in the bounded process wave and the child-process-heavy
  `Koan.Packaging.Tests` project runs alone afterward. Run `29769598076` measured that project at 639.7 seconds under
  contention; focused standalone class probes completed in seconds to about one minute.

Run [`29773568971`](https://github.com/sylin-org/koan-framework/actions/runs/29773568971) then proved the redesign's
useful boundaries: packages completed green in roughly nine minutes, including the exact generated console; the
certification lane also completed green; and staging waited for both. Its first persistence check failed closed
because the two isolated lineage compilers minted different commit IDs (`8462d4f...` vs `fb2f49e...`) from identical
trees. Git's invocation-time committer timestamp was the hidden input. No lineage push, draft, escrow upload,
promotion, tag, Release, or package publication occurred.

Lineage compilation now derives both Git author and committer timestamps from the exact source commit. This makes
`VersionCommit` a reproducible function of its declared source/tree/parent/message inputs while retaining the matrix
and its stage equality assertion. Focused evidence is green: SQLite configuration truth 5/5, full SQLite connector
38/38, a source-referenced console first save/query under explicit `Production`, release workflow contract 10/10,
lineage Git acceptance 12/12 including a wall-clock-separated reproducibility fact, PowerShell/YAML parsing, docs
with zero errors, public-docs truth, and `git diff --check`.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; committed local and `origin/dev` HEAD are
  `037625d2f4af8ee2805360735d368a0e63300c14`.
- The SQLite/release-matrix batch is committed and pushed. Pending tracked changes are limited to reproducible
  lineage commit metadata, its focused acceptance fact, R12-06, and this handoff.
- `tmp/` is untracked local evidence and must never be staged. It includes a disposable console source probe.
- No owned test, sample, packaging, or release process remains running.

## Resume here

1. Verify status and HEAD; never stage `tmp/`.
2. Inspect and commit only the reproducible-lineage batch, then advance `dev` once through the authorized normal
   path.
3. Observe both `prove_current` lanes. They must report one identical exact `VersionCommit`; no staging can start
   until both are green.
4. On red, diagnose only the named owner/lane. Do not rerun the complete local ratchet or weaken the join.
5. On terminal green, require NuGet visibility, exact lineage/tag/completion agreement, and an immutable GitHub
   Release before public-feed template, FirstUse, GoldenJourney, provider-swap, rejection, facts, health, lockfile,
   and maintainer-comprehension observation.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not mass-promote all active packages to 0.20 or restore the shelved CLI/Aspire families.
- Do not recreate Data Access ambient subjects, `[AccessScoped]`, arbitrary durable filter carriage, or model
  decoration for request context; Web context belongs at ordered contributor chokepoints.
- Do not rerun Tenancy, Classification, completed package-family suites, or the complete local ratchet without an
  affected dependency and explicit need.
- Do not skip suites, let Packaging contend inside the general project wave, serialize independent test/package
  proof, or permit staging before both read-only lanes succeed.
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
