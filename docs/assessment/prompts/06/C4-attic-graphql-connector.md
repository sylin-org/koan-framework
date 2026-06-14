# C4 · Migrate Koan.Web.Connector.GraphQl → agyo-tools (Agyo.Web.GraphQl)

> **Source**: docs/assessment/06-prompt-stash.md · Track C — the cut waves · **Tier**: T2 · **Depends on**: B1
> **Reorg (2026-06-14)**: migrate (was attic-tag/cut) — GraphQL-over-entities has real value; its HotChocolate CVE cadence moves off the Koan release train — see docs/assessment/08-agyo-reorganization.md + agyo-tools docs/decisions/AGYO-0001.
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

_Re-instantiated from MIGRATE/SPLIT-TEMPLATE (08-agyo-reorganization) · row C4. Supersedes the original CUT-TEMPLATE instance._

```text
TASK: migrate Koan.Web.Connector.GraphQl (~1300 LOC) from the Koan framework to agyo-tools as Sylin.Agyo.Web.GraphQl.
JUSTIFICATION (08-agyo-reorganization): GraphQL-over-entities is a useful opt-in helper with real value, not core
  framework surface — but it does NOT belong on the Koan release train: its HotChocolate dependency carries a CVE
  cadence (sole consumer was already archived; "HotChocolate CVE treadmill for nobody", WEB-0041/0042 note) that
  should move off Koan's cadence. It touches only Koan public packages (it reuses the WEB-0068 hook pipeline, which
  is public in Koan.Web), so it ports cleanly to agyo as an opt-in package rather than being deleted.
SOURCE: Koan tag attic/koan-web-graphql (path src/Connectors/Web/GraphQl).
ENTRY CRITERION (already verified in 08): touches only Koan PUBLIC packages — reuses the public WEB-0068 hook
  pipeline in Koan.Web; no Koan internals / InternalsVisibleTo.
STEPS (the proven WebSockets/C2 pattern):
  1. In agyo-tools (F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools): recover source from tag
     attic/koan-web-graphql (path src/Connectors/Web/GraphQl) into src/Web/GraphQl/; rebrand the project's OWN
     tokens -> Agyo.Web.GraphQl: the code namespace (`Koan.Web.GraphQl`), the assembly/project name
     (`Koan.Web.Connector.GraphQl`), and the config section (`Koan:Web:GraphQl` -> `Agyo:Web:GraphQl`) — grep first
     to confirm the exact tokens. Koan.Core / Koan.Web / Koan.Data.* stay (consumed via package). Drop the
     per-project version.json.
  2. Write Agyo.Web.GraphQl.csproj: swap ProjectReference -> PackageReference for Sylin.Koan.Web,
     Sylin.Koan.Data.Abstractions, Sylin.Koan.Data.Core, Sylin.Koan.Core (versions from
     F:/Replica/NAS/Files/repo/github/sylin-org/agyo-tools/local-feed); keep third-party refs:
     HotChocolate.AspNetCore 13.9.16 + HotChocolate.Execution 13.9.16 (pinned for CVE GHSA-qr3m-xw4c-jqw3),
     Newtonsoft.Json.
  3. PER-CAPABILITY WORK: none structural — the WEB-0068 hook pipeline it reuses is public in Koan.Web, so it
     ports as-is. NOTE the known rough spots for a later pass (do NOT block this migration on them): all-StringType
     field mapping and string-only upsert. File them against Agyo.Web.GraphQl in agyo-tools.
  4. dotnet build + dotnet pack -> Sylin.Agyo.Web.GraphQl; dotnet sln Agyo.sln add src/Web/GraphQl/Agyo.Web.GraphQl.csproj;
     update agyo docs/SURFACES.md row.
  5. TEST-CANON (AGYO-0001): port or author at least one spec exercising the GraphQL endpoint over a real entity
     through AddKoan() discovery. NOTE: it ships untested today (sole Koan consumer was the archived sample) — this
     is the first real spec, so author it rather than expecting a port.
KOAN-SIDE (immediately — NOT consumer-facing; the sole consumer is the archived sample, so no downstream re-point
  is required and no transition-safety hold applies): remove Koan.Web.Connector.GraphQl from Koan.sln; delete the
  source directory (it already lives on tag attic/koan-web-graphql); sweep the project's lines out of docs
  (modules-overview.md, module-ledger.md, capability-map.md — for big docs add a "migrated to agyo 2026-06"
  strike-through note instead of rewriting); annotate the WEB-0041/0042 note that the CVE cadence now lives in agyo.
VERIFY: agyo build+pack green (Sylin.Agyo.Web.GraphQl); Koan build green after removal
  (dotnet build Koan.sln; dotnet test Koan.sln non-container).
DONE WHEN: capability lives in agyo as Sylin.Agyo.Web.GraphQl, layering clean (only Sylin.Koan.* PackageReferences,
  names never flow down per STACK-0001), both repos green, Koan docs swept.
```
