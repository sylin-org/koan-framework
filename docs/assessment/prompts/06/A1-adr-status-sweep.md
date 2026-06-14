# A1 · ADR status sweep

> **Source**: docs/assessment/06-prompt-stash.md · Track A — truth restoration · **Tier**: T1 · **Depends on**: —
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
TASK: Mark superseded ADRs in docs/decisions/ so no discarded decision still reads "Accepted".
RECIPE — for each pair below: open the OLD file, add directly under the title:
  > **Status: Superseded by <NEW>.** <one-line reason>
and change any Status field to Superseded. Do not edit the NEW files except to verify they exist.
PAIRS:
- JOBS-0001, JOBS-0002, JOBS-0003 → superseded by JOBS-0005
- OPS-0050 → superseded by JOBS-0005 ("Phases 2–3 — cron, locks, windows, bootstrap runner —
  were never implemented")
- ARCH-0046 (Recipe) → superseded by ARCH-0086 (KoanModule)
- MESS-0021..0029, MESS-0070, MESS-0071 → mark "Describes a prior messaging generation; the
  inbox/alias/provisioning features no longer exist in code" (Superseded/Retired)
- FLOW-0070, FLOW-0101..0106, FLOW-0110, ARCH-0053, WEB-0050, WEB-0060 → "Flow pillar removed
  from the codebase" (Retired)
- DATA-0019 (Cqrs) → mark Superseded when/if the C-CQRS cut lands (check src/Koan.Data.Cqrs
  exists; if already deleted, mark now)
- ARCH-0060 → add "Reaffirmed by ARCH-0075" note (its control surface survived the rebuild)
- DATA-0060 + DATA-0085 → cross-reference each other (two ADRs, one decision domain)
VERIFY: scripts/docs-lint.ps1 passes.
DONE WHEN: every listed file carries the banner; summary lists files touched.
```
