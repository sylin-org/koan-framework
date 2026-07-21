# E1-sqlserver · Fold SqlServer manual registration into the registrar

> **Source**: docs/assessment/06-prompt-stash.md · Track E — finish the in-flight migrations · **Tier**: T2 · **Depends on**: B1
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

_Instantiated from E1-TEMPLATE · adapter SqlServer._

```text
TASK: In src/Connectors/Data/SqlServer, fold the manual SqlServerRegistration static class into
the existing Initialization/KoanAutoRegistrar so there is exactly ONE wiring unit.
RECIPE:
1. Read both files. Diff what each registers. The auto path registers MORE (discovery adapter,
   orchestration evaluator) — the manual path's unique lines (if any) move into the registrar.
2. Find consumers of the manual AddSqlServerAdapter() extension (grep across src/samples/tests —
   expect: test fixtures only). Update those call sites to rely on AddKoan() via the fixture's
   KoanIntegrationHost, OR if the fixture genuinely needs eager registration, point it at a
   retained thin AddSqlServerAdapter() that now just delegates to the registrar's Register
   method — choose the FIRST option unless tests fail.
3. Delete the manual class.
VERIFY: build green; that adapter's test suite green (tests/Suites/Data/SqlServer).
ADAPTER (this session): SqlServer.
```
