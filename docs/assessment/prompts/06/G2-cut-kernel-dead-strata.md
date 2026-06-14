# G2 · Cut the kernel's dead strata

> **Source**: docs/assessment/06-prompt-stash.md · Track G — Koan.Core diet · **Tier**: T1/T2 · **Depends on**: —
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

```text
TASK: Delete from src/Koan.Core (verify each with grep across src/samples/tests first;
expect zero consumers):
1. BackgroundServices/FluentApi/** (KoanServices.Do/On/Query + ServiceBuilder — the kernel's
   only TODOs live here) + Actions/KoanServiceActions.cs + Events/KoanServiceEvents.cs IF after
   the FluentApi cut their only remaining consumers are the 3 internal services — if real
   consumers remain, trim to the constants actually used and report.
2. Extensions/EntityTransferExtensions.cs (zero consumers).
3. Json/JsonPathMapper.cs (zero external consumers — verify).
4. Rewrite src/Koan.Core.Adapters/README.md + TECHNICAL.md to describe ONLY what exists
   (readiness pipeline, options binding, boot reporting) — the current docs describe a deleted
   type system (BaseKoanAdapter etc.). Do not restructure the project itself (that is a
   separate frontier task).
VERIFY: build + Core suites green.
```
