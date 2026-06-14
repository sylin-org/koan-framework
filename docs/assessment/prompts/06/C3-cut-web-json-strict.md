# C3 · Cut Koan.Web.Json.Strict

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.
> **Reorg (2026-06-14)**: confirmed DELETE — not an agyo candidate (superseded by Newtonsoft as the canonical serializer (05 section 6.1) — reviving an app-facing STJ surface even in agyo reopens the two-serializer-worlds ambiguity; the real latent need (strict bulk-import validation) is a NEW capability, not this Minimal-API binding hardener) — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.

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

> **Reorg disposition: confirmed DELETE (08-agyo-reorganization).** Not an agyo candidate — it is a strictly-inferior predecessor of a live core capability (Newtonsoft as the canonical serializer (05 section 6.1) — reviving an app-facing STJ surface even in agyo reopens the two-serializer-worlds ambiguity; the real latent need (strict bulk-import validation) is a NEW capability, not this Minimal-API binding hardener), with no distinct app-building value. Nothing to preserve. Proceed with the cut below.

_Instantiated from CUT-TEMPLATE · row C3._

```text
TASK: cut the project(s) Koan.Web.Json.Strict.
JUSTIFICATION (verify, then cite in your summary): Decision recorded (05 §6.1): Newtonsoft is canonical; the STJ island goes.
PRECHECKS (all must hold, else STOP):
1. grep each project name across **/*.csproj — inbound ProjectReferences must match none (own tests only).
2. grep the key public type names across src/ samples/ tests/ — no live consumers beyond none (own tests only).
3. Confirm packaging/Koan.nuspec + Koan.App.nuspec do NOT list the package (verified clean for
   all rows below as of 2026-06-10).
STEPS:
1. <MODE=cut>: remove project(s) + their test projects; remove from Koan.sln; delete the source
   directories. <MODE=park>: git mv to /attic (create if absent), remove from Koan.sln, add an
   attic/README.md line explaining why. <MODE=attic-tag>: create branch attic/<name> containing
   the project, then cut from dev.
2. Remove the project's lines from docs: modules-overview.md, module-ledger.md, capability-map.md
   (grep the project name under docs/ and clean each hit; for big docs add a "removed/parked
   2026-06" strike-through note instead of rewriting).
3. delete tests/Koan.Web.Json.Strict.Tests.
4. Mark/annotate the ADR named in the JUSTIFICATION above if one is listed.
VERIFY: dotnet build Koan.sln green; dotnet test Koan.sln (non-container) green.
DONE WHEN: project gone/parked, docs swept, build+tests green, summary cites all precheck greps.
```
