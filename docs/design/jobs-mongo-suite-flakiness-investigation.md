---
type: DESIGN
domain: jobs / testing
title: "Jobs: Mongo cross-tier suite flakiness — root cause & fix"
status: resolved
last_updated: 2026-06-10
related:
  - decisions/JOBS-0005-job-orchestrator-rebuild.md (§7, §19)
  - src/Koan.Jobs/JobOrchestrator.cs (DrainAsync)
  - tests/Suites/Jobs/Koan.Jobs.TestKit/JobBehaviorSuite.cs
---

# Jobs: Mongo cross-tier suite flakiness — root cause & fix

## RESOLVED — it WAS a code defect (a `DrainAsync` race), not a test artifact

> **Correction.** An earlier pass of this note concluded the flake was an "emergent test-harness artifact, not a code
> defect." **That conclusion was wrong.** Instrumenting an actual full-suite run (dumping the ledger the instant a
> drain gives up) found a real race in `JobOrchestrator.DrainAsync`. The earlier isolated diagnostics missed it
> because they ran the settle→append→claim sequence *synchronously*, whereas the orchestrator runs the predecessor's
> settle in a concurrent inflight task that races the claim loop. Lesson: instrument the real loop, not a hand-rolled
> proxy of it.

**Root cause.** A chain successor is appended *inside* the predecessor's inflight task (`SettleSuccessAsync` /
`SettleFailureAsync` → `_ledger.Append`). `DrainAsync`'s loop did `RemoveAll(completed); if (inflight.Count == 0)
break;` **before** waiting on the inflight tasks. If `ClaimNext` returned null (successor not yet appended) and the
predecessor task then completed — appending the successor — in the window before that `RemoveAll`, the loop saw
`inflight` empty and **broke without re-claiming the just-appended successor**. The drain ended with a Queued,
visible, ungated successor and **nothing running** (`running=0` in every captured stall) — i.e. claimable work left
unclaimed. This violates `DrainAsync`'s own contract ("process every currently-ready job to completion, including
chain follow-ons that become ready").

**Why Mongo-only / intermittent / single-Drain-only:**
- The race window is the gap between the claim and the inflight check. On **in-memory/SQLite** operations are
  effectively synchronous, so the window is ~0 → never hit. On **Mongo** every claim/settle is a network round-trip,
  widening the window → hit intermittently.
- In **production** the worker runs `DrainAsync` on a continuous poll loop, so a missed successor is claimed on the
  next tick — the bug is invisible there. A **single `host.Drain()`** in a test asserts immediately, so the early
  break leaves the chain incomplete and the assertion fails.

**The fix** (`JobOrchestrator.DrainAsync`): don't conclude the drain is done the moment `inflight` empties — if a task
just settled (it may have appended a follow-on), re-claim before breaking:

```csharp
var settled = inflight.RemoveAll(t => t.IsCompleted);
if (inflight.Count == 0)
{
    if (settled > 0) continue;   // a task finished and may have enqueued a follow-on — re-claim before giving up
    break;                       // nothing claimable, nothing in flight → genuinely done
}
await Task.WhenAny(inflight);
inflight.RemoveAll(t => t.IsCompleted);
```

Because a task only completes *after* its `Append` returns (acknowledged write), the re-claim is guaranteed to see
the successor. This is correctness for `DrainAsync`'s contract on every store — not a test band-aid.

**Validation:** Mongo suite ×8 green (was ~4/5 flaky); in-memory 44, SQLite 50, Postgres 40, SqlServer 40 — all green
(the change is in the shared loop, so all tiers re-validated). Diagnostics reverted.

---

## Original investigation (kept for the trail — note the wrong turns)

## Symptom

- The full Mongo suite (`Koan.Jobs.Adapter.Mongo.Tests`, ~40 specs sharing one `mongo:7` Testcontainer via
  `IClassFixture<MongoJobsFixture>`) **intermittently fails ~1 spec per run (~1/3 of runs)**. The failing spec
  **varies** (`on_failure_continue_advances_the_chain`, `linear_chain_advances_and_carries_saga_state`,
  `continue_with_branches_to_an_off_chain_action`, conveyor) — always a **same-WorkId succession** spec (a chain
  stage / conveyor window / continue-with), where the **successor is not claimed within a single `host.Drain()`**.
- **Every spec passes when run alone.** In-memory (44), SQLite (50), and **Postgres (40)** are **deterministically
  green**.

## What was ruled OUT (with evidence — all on real Mongo)

| Hypothesis | Test | Result |
|---|---|---|
| Data-layer read-your-writes (insert) | `ReadYourWritesProbe.immediate_query_sees_the_just_saved_row` (200×) | **0 miss** — consistent |
| Data-layer read-your-writes (update) | `ReadYourWritesProbe.immediate_query_reflects_the_just_updated_value` (200×) | **0 miss** — consistent |
| Ledger chain-claim (settle predecessor → append same-WorkId successor → claim), single host | `ChainClaimDiagnostic.chain_successor_is_claimable_immediately_after_predecessor_settles` (100×) | **0 miss** |
| Same, with a **fresh host (new MongoClient+DI) per cycle** | `ChainClaimDiagnostic.chain_hop_under_per_spec_host_churn` (25×) | **0 miss** |

⇒ The orchestrator's claim/exclusivity/ledger logic and Mongo insert+update visibility are **correct**. The flake
is **not** in the code path the chain spec exercises.

## What was FIXED along the way (real bugs the cross-tier suite caught)

- **Mongo enum lexicographic ordering** (commit `8245dec9`). Mongo stores the `JobStatus` enum **by name**, so
  `Status < Completed` / `Status >= Completed` were *lexicographic*, not numeric — `CountActive` returned 0,
  `NonTerminal()` was wrong. Replaced ordering with **equality sets** (`NonTerminal`/`Terminal`/`ActiveOf`/`TerminalOf`
  in `JobLedgerPredicates`). **Equality is the portable form across stores; never use enum ordering in a pushed predicate.**
- **Drop-recreate clear churn** (commit `c02a0a27`). The harness cleared between specs via `RemoveAll(Optimized)`,
  which on Mongo resolves to `Fast` = **drop-and-recreate the collection**; repeating that DDL across the suite's
  rapid host churn flaked. Now clears via `RemoveStrategy.Safe` (DeleteMany — no DDL). Reduced the flake (≈ 2/3 → cleaner)
  but did **not** eliminate it.
- **Postgres readiness gating** (commit `dcc6c6e6`). `PostgresJobsFixture` (unlike its Mongo/SqlServer siblings)
  never disabled readiness gating; under the larger suite's churn it timed out and failed all 40 specs at ~17s each.
  Disabled it → 40/40 in 7s.

## What was TRIED and REVERTED (a wrong turn — do not repeat)

- A **§17.2 orchestrator change**: enforce same-node per-entity exclusivity from an in-memory authoritative
  `_exclusiveRunning` set instead of a ledger `Query(Status==Running)` probe, with the ledger query restricted to
  *other* owners (`Owner != owner`). Rationale at the time: the ledger probe could read a just-settled predecessor
  back as Running and block its successor. **Result: it regressed Mongo chains *deterministically*** (traded
  "successor blocked by stale-Running predecessor" for "predecessor re-picked as stale-Queued") and passed on
  SQLite/in-memory. **Reverted.** Lesson: the chain-claim is correct (diagnostics prove it); this was treating a
  phantom and introduced a worse symptom.

## The KEY clue for root-causing

**Postgres and SqlServer use the *same* harness pattern** — a fresh full Koan host (new DB client + DI container)
**per spec**, all sharing **one container + one database** across the class — **and they do not flake.** Only Mongo
does. So the trigger is **Mongo-adapter/driver-specific behavior under per-spec client churn on a shared server**,
not the per-spec-host pattern generically.

`MongoClientProvider` creates `new MongoClient(connectionString)` with **defaults** per host (no causally-consistent
session, no read/write concern set). Candidate Mongo-specific mechanisms to investigate:
- **Connection-pool / client churn**: ~40 `MongoClient`s created/disposed rapidly against one container — pool or
  connection-state pressure surfacing in a later spec. (The single-spec and 25-cycle diagnostics didn't reach the
  diversity/volume of the full 40-spec run.)
- **Cross-client cluster-time gossip**: a *fresh* client's first reads may execute at a cluster time behind a
  prior client's recent writes (the diagnostics wrote+read within one client, so cluster time gossiped internally —
  the cross-client first-read case was not isolated).
- **Interaction with concurrency specs** (lanes/`MaxConcurrency`, `ExclusiveJob`/`ParallelJob`) that run multiple
  inflight tasks — not covered by the sequential chain diagnostics.

## Architectural fix candidates (test infrastructure — NOT runtime code)

1. **Shared host per test-class + data-clear between specs** (instead of a fresh host per spec). Collapses ~40
   `MongoClient`s to **one per class** — directly attacks client churn. Larger harness change (`JobsHarness` /
   `JobBehaviorSuite` lifecycle), affects all tiers; must preserve per-spec isolation via data-clear. **Recommended
   primary direction.**
2. **Per-spec database isolation** (a clean DB per spec on the shared container). Eliminates cross-spec *data*
   interference; smaller, Mongo-scoped. Won't help if the trigger is connection churn rather than shared state.
3. **MongoClient lifecycle / session configuration** in the adapter (causally-consistent session; explicit
   read/write concern; pooled-client reuse) — if root-cause points at the driver. This *would* be a runtime change,
   justified only if the mechanism is shown to affect production (not just the test churn).

## Reproduction & assets

- Diagnostics live in `tests/Suites/Jobs/Adapter.Mongo/Koan.Jobs.Adapter.Mongo.Tests/`:
  `ReadYourWritesProbe.cs`, `ChainClaimDiagnostic.cs` (committed `c02a0a27`, `8373b681`).
- To observe the flake: run the full Mongo suite several times:
  `dotnet test tests/Suites/Jobs/Adapter.Mongo/Koan.Jobs.Adapter.Mongo.Tests` ×5.
- Harness: `tests/Suites/Jobs/Koan.Jobs.TestKit/JobsHarness.cs` (`CreateHostAsync` → new host per spec;
  `clearOnStart` = `EnsureJobSchema` + `RemoveAll(Safe)`). Suite: `JobBehaviorSuite.cs`. Mongo fixture:
  `Adapter.Mongo/.../MongoTier.cs` (`MongoJobsFixture`, readiness gating disabled).

## Status of the §19 deliverable (independent of this flake)

§19 (Tier 0 index+push-down, Tier 1 retention, SQLite bulk-write fix, cursor-conveyor, job-per-row guardrail, docs)
is **complete and deterministically green on in-memory, SQLite, and Postgres**, all committed on `dev` (unpushed).
The Mongo *code* is proven correct; only the Mongo *test suite* has this emergent flake.

## Next task

Root-cause the Mongo-specific emergent trigger (start from the KEY clue + candidate mechanisms above; instrument a
full-suite run rather than isolated hops), then implement the proper test-infrastructure architecture (likely #1,
shared-host-per-class). Treat a runtime adapter change (#3) as in-scope only if the mechanism is shown to affect
production, not just rapid test churn.
