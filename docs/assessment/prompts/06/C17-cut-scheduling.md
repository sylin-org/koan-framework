# C17 · Migrate Koan.Scheduling → agyo-tools (Agyo.Scheduling)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.
> **Reorg (2026-06-14)**: migrate (was BLOCKED — consumed downstream) — preserve the lightweight in-proc scheduler in agyo as Sylin.Agyo.Scheduling; de-bloat Koan.Web on the way out — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.

---

## Session preamble

```text
You are working on the Koan Framework (.NET 10 meta-framework; repo root = the working
directory). Rules for this session — they override your defaults:

1. SCOPE: do exactly the task below. One intent per session. No drive-by fixes, no
   refactoring outside the named files, no "while I'm here".
2. EVIDENCE FIRST: before editing, read the files the task names. Never reference an API you
   have not seen in this session — the repo's older docs contain APIs that do not exist; grep
   before you trust. Any API you use in code or docs must be evidenced by a file:line you read.
3. VERIFY: run the named verification (at minimum: `dotnet build Koan.sln`). A session that
   cannot get back to green REVERTS its changes and reports — never "fix forward" into new scope.
4. OUTPUT CONTRACT: your final summary lists every file touched, and for every claim cites
   evidence ("removed X — verified zero references: grep '<pattern>' = 0 hits"). No vague claims.
5. STOP CONDITIONS — stop and report instead of choosing, if you hit ANY of: a failing test you
   did not expect; an API that does not match this recipe; a second plausible way to do the task;
   a reference to the thing you're removing from a file this recipe did not predict.
6. NO-GO ZONES (do not modify, ever, in a T1/T2 session): src/Koan.Data.Core/Model/**,
   EntityContext internals, src/Koan.Core/Hosting/** (except where a recipe names exact lines),
   RegistrySourceGenerator, capability token definitions, any adapter's query-translation code,
   any public API rename.
7. CONVENTIONS: Newtonsoft.Json is the canonical serializer (do not introduce STJ surfaces).
   Canonical entity verbs: Save / Remove / Query. Canonical module primitive: KoanModule.
   Never manually register framework services. Never add a new Add*() extension where a
   registrar exists. Commit messages: conventional commits (feat/fix/refactor/docs/test/chore).
```

---

## Task

```text
TASK: migrate Koan.Scheduling (~470 LOC) from the Koan framework to agyo-tools as
Sylin.Agyo.Scheduling.

JUSTIFICATION (08-agyo-reorganization): Scheduling is a useful opt-in in-proc helper, not core
infrastructure — but it is consumer-facing (apps reference it transitively through Koan.Web, and
the original C17 cut was BLOCKED precisely because it is consumed downstream). It is NOT delete
material: it is the deliberately-minimal in-proc alternative to the durable Koan.Jobs ledger
(no queues, no data store, just a 1s poll loop), which is a legitimately valuable PowerToys-for-
Koan capability. It touches only Koan PUBLIC packages (the .csproj's sole ProjectReference is
Koan.Core), so it ports cleanly under the STACK-0001 layering law (names never flow down).

SOURCE: Koan working tree src/Koan.Scheduling (intact — orchestrator, attributes, interfaces,
README/TECHNICAL all present).

ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages, no internals / no
InternalsVisibleTo — src/Koan.Scheduling/Koan.Scheduling.csproj declares exactly one
ProjectReference: ..\Koan.Core\Koan.Core.csproj. No third-party refs.

STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover source into
     src/Agyo.Scheduling/; rebrand ONLY the token Koan.Scheduling -> Agyo.Scheduling (namespaces,
     RootNamespace, AssemblyName). Koan.Core stays Koan.Core (consumed via package — DO NOT
     rebrand it). Drop the per-project version.json.
  2. Write Agyo.Scheduling.csproj: replace the ProjectReference to ..\Koan.Core with a
     PackageReference to Sylin.Koan.Core (version from
     F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools/local-feed). No third-party refs to
     carry over.
  3. PER-CAPABILITY WORK — DECIDE: finish-or-document. Koan.Scheduling is ~60% vaporware: the
     interfaces ICronScheduled / IProvidesLock / IAllowedWindows / IHealthFacts are DECLARED but
     SchedulingOrchestrator.cs ignores them (only IFixedDelay / IOnStartup / IHasTimeout /
     IHasMaxConcurrency are honored). Pick ONE and execute it in agyo:
       (a) IMPLEMENT the declared-but-ignored contracts in Agyo.SchedulingOrchestrator (cron via
           Cronos, distributed lock via IProvidesLock, allowed-window gating, health facts), OR
       (b) DOCUMENT it as the deliberately-minimal in-proc scheduler — DELETE the unimplemented
           interface files (ICronScheduled / IProvidesLock / IAllowedWindows / IHealthFacts) so
           the surface matches the orchestrator, and state in the README that the durable /
           cron / distributed-lock story lives in Koan.Jobs (the ledger-backed tier), with
           Agyo.Scheduling positioned as the zero-dependency in-proc alternative.
     Record the decision + rationale in the agyo SURFACES row and AGYO-0001.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Scheduling; `dotnet sln Agyo.sln add
     src/Agyo.Scheduling/Agyo.Scheduling.csproj`; update agyo docs/SURFACES.md row.
  5. TEST-CANON (AGYO-0001): port/author at least one spec exercising the in-proc loop
     (IFixedDelay + IOnStartup fire; IHasTimeout cancels; IHasMaxConcurrency caps). NOTE: this
     capability ships UNTESTED in Koan today — there is no scheduling integration spec to port,
     so author one fresh in agyo.

  6. TRANSITION SAFETY (consumer-facing): do NOT remove Koan.Scheduling from Koan until agyo
     publishes Sylin.Agyo.Scheduling AND every downstream consumer that reaches it transitively
     (through Koan.Web) has re-pointed at the agyo package or had the dependency removed. Keep
     Koan publishing the old Sylin.Koan.Scheduling package until then.

DE-BLOAT KOAN (can land immediately — independent of the transition gate): src/Koan.Web/
Koan.Web.csproj line 20 hard-references ..\Koan.Scheduling\Koan.Scheduling.csproj, so EVERY web
app drags in the scheduler and runs its 1s poll loop with zero registered tasks. On the way out:
  - DROP that ProjectReference from src/Koan.Web/Koan.Web.csproj.
  - DROP the dead /.well-known/Koan/scheduling endpoint (no tasks => nothing to report).
  - MOVE the KoanServiceEvents.Scheduling / KoanServiceActions.Scheduling constant groups OUT of
    Koan.Core (define agyo's own Agyo equivalents in Agyo.Scheduling; do not leave Koan.Core
    carrying scheduling-specific constants once the project is gone).
  Verify after each removal: grep "Koan.Scheduling|IScheduledTask" across src/ samples/ tests/ —
  any remaining hit is an undocumented consumer => STOP and report (this is the downstream
  consumer the transition gate protects).

KOAN-SIDE (after the transition gate clears, or immediately for any item not consumer-facing):
remove Koan.Scheduling from Koan.sln; sweep modules-overview.md / module-ledger.md /
capability-map.md; mark OPS-0050 superseded (the original scheduling ADR). Keep no scheduling
seam behind — the in-proc loop now lives entirely in agyo.

VERIFY: agyo build+pack green (Sylin.Agyo.Scheduling resolves against local-feed Sylin.Koan.Core
only); Koan build green after the Koan.Web de-bloat (`dotnet build Koan.sln`); tests/Suites/Jobs
all green; grep "Koan.Scheduling|IScheduledTask" across src/ samples/ tests/ = 0 hits (docs hits
get the A1-style banner treatment).

DONE WHEN: scheduling lives in agyo as Sylin.Agyo.Scheduling, layering clean (only Sylin.Koan.*
PackageReferences, never a Koan ProjectReference), the finish-or-document decision is recorded,
Koan.Web no longer drags the dead poll loop into every web app, and both repos are green.
```
