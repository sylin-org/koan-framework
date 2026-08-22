---
type: GUIDE
domain: framework
title: "Relational consolidation batch — ledger"
audience: [ai-agents, maintainers]
status: current
last_updated: 2026-08-21
---

# Ledger — relational consolidation batch

Durable progress. One task, one commit, one row. Update this file as part of the task's commit, not afterwards.

## RESUME HERE

> **Next task:** none — wave 2 has not been delivered.
> **Tree state expected on start:** `dev` clean, in sync with `origin/dev`, build 0 warnings / 0 errors.
> **Batch wave:** 1 of 2. Wave 1 is scaffolding only. Every backlog item needs a decision recorded before it
> can become a card, and those decisions are not the executor's to make — see the task sequence in
> `BOOTSTRAP.md`. If no cards are present, report that and stop.

Update this block when you finish a task: set the next card, and note anything the following task must know
that its card does not already say.

## Tasks

| # | Card | Status | Commit | Notes |
|---|---|---|---|---|
| — | *(wave 2 pending)* | — | — | |

Status is one of: `not started`, `in progress`, `done`, `BLOCKED`.

## Log

One entry per task attempt, newest last. Record what you did, both results of the load-bearing check (the test
failing with the change disabled, and passing with it restored), and every deviation as a numbered item.

A **deviation** is anything you did that the card did not say to do, or anything the card said that turned out
not to match the tree. Deviations are the feedback channel for improving the next batch — an unrecorded one is
a defect in the process, not a tidy result. Record them even when the outcome was fine.

### Template

```
### T-NN · <card> · <status>
Commit: <sha>
Load-bearing check: disabled -> <FAIL/PASS>, restored -> <FAIL/PASS>
Verification: <the commands you ran, and their results>
Deviations:
  1. <what differed from the card, and what you did about it>
```

<!-- entries below -->
