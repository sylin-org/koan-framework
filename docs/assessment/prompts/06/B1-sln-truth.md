# B1 · Solution truth — all test projects into Koan.sln

> **Source**: docs/assessment/06-prompt-stash.md · Track B — enforcement substrate · **Tier**: T2 · **Depends on**: — (run first)
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
TASK: Add every test project on disk to Koan.sln. Today 39 of ~87 test .csproj files are not in
the solution, so `dotnet test Koan.sln` silently skips them (evidence:
docs/assessment/01-cartography.md §2.4).
RECIPE:
1. Inventory: list all **/*.csproj under tests/ (exclude bin/obj). Diff against `dotnet sln
   Koan.sln list`.
2. For each missing project: `dotnet sln Koan.sln add <path> --solution-folder tests/<suite>`
   (mirror the existing solution-folder layout; scripts/regenerate-sln.ps1 exists — read it
   first and prefer it if it covers this).
3. EXCLUDE (do not add — they are husks/orphans pending deletion):
   tests/Suites/Canon/Koan.Canon.Core.Tests, tests/Suites/Data/Vector/ (orphan Weaviate spec),
   tests/Suites/AI/Core/Koan.AI.Tests (no csproj), tests/Suites/AI/Koan.AI.Core.Tests (net8.0,
   references nonexistent packages), tests/Suites/Cache/Unit (no csproj).
4. VERIFY: `dotnet build Koan.sln` green; `dotnet test Koan.sln --list-tests` enumerates without
   container infra (container-gated specs must skip cleanly, not fail).
DONE WHEN: sln contains every live test project; build green; summary lists added projects.
STOP IF: adding a project breaks the build — report which, do not "fix" the project.
```
