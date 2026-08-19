---
type: GUIDE
domain: framework
title: "Connector fleet — ledger"
audience: [ai-agents, maintainers]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: durable progress record and resume point
---

# Ledger

The one authoritative record of progress. If this file and your recollection disagree, this file is
right. Update it in the same commit as the work it describes, or immediately after recording BLOCKED.

## RESUME HERE

> **Next task:** T1 — [pgvector](tasks/T1-pgvector.md)
> **State:** not started
> **Last commit touching this initiative:** none
>
> Read [BOOTSTRAP.md](BOOTSTRAP.md) before doing anything. Check T1's STOP preconditions first.

Whoever picks this up next: update the three lines above **before** you start, so an interruption
leaves an accurate resume point rather than a stale one.

## Status

| # | Task | State | Commit | Oracle exit |
|---|---|---|---|---|
| T1 | pgvector | Not started | — | — |
| T2 | Redis vector | Not started | — | — |
| T3 | MySQL / MariaDB | Not started | — | — |
| T4 | Mongo Atlas Vector | Not started | — | — |

States: `Not started` · `In progress` · `Done` · `BLOCKED`.

## Log

One entry per task attempt. Append; never rewrite history. Copy this template:

```
### T<n> — <task name> — <Done | BLOCKED> — <date>

Commit: <sha or "none">
Oracle: <literal command> -> exit <code>
Acceptance: skills-verify <pass/fail> · docs-lint <Errors: n> · build <pass/fail> · discoverability <done/not>

Deviations:
1. <what differed from the task prompt, and what you did about it>
2. ...
(or: none)

Notes: <anything the next executor needs. For BLOCKED, what is required to unblock.>
```

A deviation is anything where the tree contradicted the prompt, where you made a judgement the prompt
did not pin, or where you touched a file the prompt did not name. Recording one is neutral — it is the
feedback channel that improves the next batch, not an admission of error. An entry with `none` is
equally valid; do not invent deviations to seem thorough.

---

<!-- Entries begin below. Newest last. -->
