---
type: GUIDE
domain: framework
title: "Relational consolidation batch — bootstrap"
audience: [ai-agents, maintainers]
status: current
last_updated: 2026-08-21
---

# Bootstrap — relational consolidation batch

Read this file completely before opening any card. It is the ground rules; a card is only the task.

## What this batch is

A linear sequence of independently landable tasks that finish the relational consolidation arc and clear the
data-layer entries the arc left behind. Each task is one commit against a green tree.

You will not be given conversational context. Everything you need is in this file, the card you are on, and the
documents they cite. If something you need is in none of them, that is a STOP condition — see below.

## Authority order

When two sources disagree, the earlier wins:

1. **The tree.** `git ls-files` is the authority on what exists. Read the file before you believe a description
   of it. This repository contains `bin`/`obj` directories from earlier builds that make retired projects look
   live, and secondary documents that describe the tree as it was on the day they were written. Three separate
   investigations in this repo's recent history were sent down blind alleys by a confidently-worded document
   describing code that had been deleted.
2. **`docs/decisions/DATA-0119-*.md` and `docs/decisions/DATA-0120-*.md`.** These are normative for where a
   relational decision lives and what is collapsible. Cards cite them; they do not restate them. If a card and
   an ADR disagree, STOP — do not reconcile them yourself.
3. **This bootstrap.**
4. **The card.**

`docs/MEMORY.md` carries durable working conventions and learnings. It is not authority over the tree, but
where it names a hazard, that hazard is real and was paid for.

## Environment facts

- .NET 10. Build with `dotnet build Koan.sln -c Debug`. **The build must end at 0 warnings and 0 errors.**
  Warnings are errors here in practice; a task that adds one is not done.
- Docker is available. Container-backed suites (PostgreSQL, SQL Server, MySQL, CockroachDB, Couchbase, Mongo)
  start their own containers via Testcontainers. SQLite and InMemory need no container.
- **Every project builds into one shared output path outside the working tree.** Never run a build or a test
  while another build or test is in flight, in any window. A concurrent build silently replaces the assemblies
  the other run is testing, and the result is a plausible-looking failure in an unrelated suite. This has
  produced false regressions twice. Run one thing at a time, always.
- Line endings are mixed. If you edit files with a script, detect CRLF and preserve it, or the edit silently
  matches nothing. Prefer the editing tools over scripted text replacement.
- Git: commit on `dev`. Push when you commit. Never touch `main` — releasing is a fast-forward of `main` from
  `dev` and is not your concern.

## Per-task procedure

1. Open the card. Read it fully before editing anything.
2. Re-read the tree facts the card states. **They are labelled as of authoring and may have gone stale.** If a
   fact is wrong, STOP — do not adapt. A stale premise is how a task quietly does the wrong thing.
3. Make the change the card specifies. Only that change. No drive-by fixes, no adjacent tidying.
4. Add the tests the card names, with the assertions the card pins. **You transcribe the expected values; you
   never compute them.** If a pinned assertion looks wrong to you, STOP and say so — an expectation you derived
   yourself validates your own bug.
5. **Prove the test is load-bearing.** Disable the change (comment the fix, invert the flag), run the new test,
   confirm it FAILS, restore the change, confirm it PASSES. A test that passes with the fix removed proves
   nothing, and this repo has shipped two of them. Record both results in the ledger.
6. Run the card's verification commands, literally, one at a time.
7. Commit. One task, one commit. Push.
8. Update `LEDGER.md`: the task row, and a log entry with any numbered deviations.

## Failure protocol

If a task cannot be completed:

1. **Revert your changes.** `git checkout -- .` for unstaged work. Leave the tree exactly as you found it — a
   half-applied task is worse than an unstarted one because the next reader cannot tell which is which.
2. Mark the task **BLOCKED** in `LEDGER.md` with the reason, what you tried, and the precise thing that stopped
   you (a file that does not exist, a test that fails for a reason the card does not predict, two plausible
   readings of an instruction).
3. **Halt the batch.** Do not skip ahead to the next task. The sequence is ordered by coupling; a later task
   may depend on the one you could not finish. Report and stop.

## STOP conditions

Stop and report rather than choosing, if any of these occur:

- A file, type, or member the card names does not exist, or does not have the shape the card describes.
- A test fails that the card did not predict would fail.
- The build produces a warning.
- There are two plausible ways to satisfy an instruction.
- A pinned assertion disagrees with what the code plainly does.
- You are about to modify a file in the NEVER list below without the sanctioned exception.

## NEVER touch

Each entry names its single sanctioned exception, if one exists. Outside that exception, modifying these is a
STOP condition.

| Never | Sanctioned exception |
|---|---|
| Package versions, `version.json`, `Directory.Packages.props` version pins | none — NBGV owns versions |
| `main` branch | none |
| `docs/decisions/**` — ADRs are dated records | none in this batch; cards cite ADRs, they never amend them |
| `src/Koan.Data.Relational/Orchestration/RelationalSchemaOrchestrator.cs` | **T-REPAIR** only |
| `src/Koan.Data.Relational.Abstractions/Orchestration/IRelationalDdlExecutor.cs` | **T-REPAIR** only |
| `src/Koan.Data.Core/Querying/FilterPushdownCoordinator.cs` | none |
| `src/Koan.Data.Core/Mapping/**` | none |
| `src/Koan.Core/Reflection/DeclarationOrder.cs` and every caller of it | none — this is the NativeAOT path and it is guarded by `scripts/aot-lint.ps1` |
| `scripts/aot-lint.ps1`, `scripts/aot-verify.ps1`, `.github/workflows/aot-verify.yml` | none |
| Any adapter's dialect (`*Dialect.cs`) | **T-COUCHBASE-TIMESPAN** only, and only the member its card names |
| Anything under `samples/` | none |

## Completion criteria for the batch

Every task row in `LEDGER.md` reads `done`, the tree is clean, `dev` is pushed, and
`dotnet build Koan.sln -c Debug` reports 0 warnings and 0 errors.

## Task sequence

Ordered so that every prefix leaves a coherent tree. Do them in this order. Do not reorder.

**Wave 1 delivers this scaffolding only. No cards are present yet, and that is deliberate rather than an
oversight.** Every remaining item in the backlog turned out to require a decision that an executor must not
make: where the comparable-encoding contract lives and how documents written before it are read (PMC-037);
what a bulk-write spec should measure instead of a wall clock, given no adapter exposes a statement count
today (PMC-044); whether repairing a stale projection happens silently under `AutoCreate` or takes its own
consent (PMC-052); and what the shared relational core physically is, which DATA-0120 names as a core without
saying whether it is a base class, a set of helpers, or something else.

Handing those over as cards would be handing over the decisions with them, and this batch's premise is that the
executor resolves nothing by judgment. Each becomes a card once its decision is recorded normatively in
`docs/decisions/DATA-0120-one-relational-repository-four-drivers.md` or its own record.

| # | Card | Subject | State |
|---|---|---|---|
| — | — | — | wave 2 pending: decisions above |

If you are reading this and the table is still empty, there is nothing to run. Report that and stop; do not
reconstruct tasks from the register entries, which state problems rather than solutions.
