# C2 · Migrate Koan.WebSockets → agyo-tools (Agyo.WebSockets)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: DONE — migrated to agyo this session (Sylin.Agyo.WebSockets.0.1.2, build+pack green); Koan-side already cut (ffef0899) — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set your row `in-progress` when you start; `done`/`blocked` when you finish.

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

_Reclassified MIGRATE/SPLIT (08-agyo-reorganization) · row C2 — was CUT-TEMPLATE._

```text
STATUS: complete (see PROGRESS.md / commit ffef0899) — this card is now the CANONICAL RECORD
and the REFERENCE PATTERN for every other migrate/split row. Nothing to execute; read it to
replay the proven WebSockets migration on the next capability.

TASK: migrate Koan.WebSockets from the Koan framework to agyo-tools as Sylin.Agyo.WebSockets.
JUSTIFICATION (08-agyo-reorganization): a useful opt-in helper, not framework core — a thin shim
  over the .NET 10 BCL WebSocketStream that exposes bidirectional duplex streaming as a Stream
  (distinct from SSE, which is server-to-client only). Zero src consumers and SSE won every
  realtime use inside Koan (the original cut justification), so it does not belong in core — but
  it is genuinely valuable to Koan-built apps and touches only Koan PUBLIC packages, so it is
  migrated (kept as an opt-in package), not deleted.
SOURCE: Koan git ref ffef0899~1 (the commit-before-cut) — already recovered into agyo-tools.
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages (Sylin.Koan.Core +
  the AspNetCore framework reference); no Koan internals, no InternalsVisibleTo.
STEPS (the proven WebSockets/C2 pattern — already executed this session):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recovered source into
     src/WebSockets/; rebranded ONLY the token Koan.WebSockets -> Agyo.WebSockets (namespaces +
     assembly); Koan.Core stays (consumed via package); dropped the per-project version.json.
  2. Wrote src/WebSockets/Agyo.WebSockets.csproj: ProjectReference -> PackageReference
     Sylin.Koan.Core (Version 0.17.3, from local-feed); kept the third-party / framework refs
     (<FrameworkReference Include="Microsoft.AspNetCore.App" />). net10.0, no version.json.
  3. PER-CAPABILITY WORK: none — it is a thin shim over the .NET 10 BCL WebSocketStream; the
     rebrand + reference-swap is the whole job, nothing to decouple or re-express.
  4. dotnet build + dotnet pack -> Sylin.Agyo.WebSockets.0.1.2.nupkg
     (artifacts/nuget/Sylin.Agyo.WebSockets.0.1.2.nupkg, build+pack green); added to Agyo.sln;
     updated agyo docs/SURFACES.md row.
  5. TEST-CANON (AGYO-0001): NOT YET satisfied — it builds and packs but its 5 unit tests still
     need porting from the Koan-side suite. Ships untested today; port them to satisfy AGYO-0001.
KOAN-SIDE: already removed (consumer-facing? NO — zero src consumers, so it was cut immediately
  with no transition window). Koan-side cut landed in ffef0899; sweep of modules-overview.md /
  module-ledger.md / capability-map.md done with the cut.
VERIFY: agyo build+pack green (Sylin.Agyo.WebSockets.0.1.2); Koan build green after the removal.
DONE WHEN: capability lives in agyo as Sylin.Agyo.WebSockets, layering clean (only Sylin.Koan.*
  PackageReferences), both repos green. (Remaining: port the 5 unit tests for AGYO-0001.)
```
