---
id: JOBS-0011
slug: a-job-that-defers-itself-keeps-what-it-changed
domain: Jobs
status: Accepted
date: 2026-11-10
title: A job that defers itself keeps what it changed
related:
  - JOBS-0005
  - JOBS-0010
---

# JOBS-0011: A job that defers itself keeps what it changed

Amends [JOBS-0005](JOBS-0005-job-orchestrator-rebuild.md) §6.5 (reschedule and backoff) and
[JOBS-0010](JOBS-0010-jobs-never-starve-and-never-lose-an-update.md) Decision 4 (what settle writes back). The rest of
those records stands.

## Context

A handler that cannot finish now asks to run again later with `ctx.Reschedule` or `ctx.Backoff`, and returns. Settle
applied the deferral and returned before it wrote the work item back, so whatever the handler changed on its item — a
cursor it advanced, a failure count it raised, a note of what went wrong — was lost, and the next run loaded the item
as it was. Nothing in JOBS-0005, JOBS-0010 or the module's guides said so, and an application that records its state on
the item and defers meets it as a job that never moves forward: a cursor read again for good, a backoff that never
lengthens.

## Decision

1. **A handler that returned having asked to `Reschedule` or `Backoff` has what it changed written back before the
   deferral is applied**, by the same write a completed run settles with (JOBS-0010 Decision 4): only the members the
   handler changed on the reference it was given, onto the row as stored. Its next run loads the change.
2. **A conflict on that write fails the attempt**, as it does for a completed run (JOBS-0010 Decision 5): the deferral
   is not applied, the attempt counts, and the job runs again on the stored item.

## What does not change

- A deferral consumes no attempt.
- A job deferred at dispatch by a gate ran no handler and writes nothing.
- A handler that changed nothing writes nothing.
- A handler that threw `RescheduleException` did not return: its changes are not kept, as after any other throw.

## Evidence

On the in-memory and SQLite tiers:

- `a_job_that_defers_itself_keeps_what_it_changed` (by `Reschedule` and by `Backoff`): the deferred run's change is
  stored, the next run loads it, and the job completes with one reschedule and one attempt.
- `a_job_that_defers_itself_having_changed_nothing_writes_nothing`: another writer's change made while the run deferred
  stands.
