---
type: EVIDENCE
domain: data
title: "DAC-09R-04 No replay and bounded transfer verification"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: DAC-09R-04 focused verification
---

# DAC-09R-04 verification

## Result

PASS. `Entity<T>` and generic `Data<T,TKey>` now enter one `Copy`, `Move`, and `Mirror` execution owner. `Batch(int)`
is the maximum provider candidate page, destination mutation batch, deferred identity-delete batch, and retained
conflict result. Query-shaped transfer delegates, delete-timing strategies, unbounded result audit retention, the
parallel partition APIs, and `PartitionMoveBuilder` are removed.

Source selection uses `QueryStreamCoordinator`, which qualifies provider-bounded paging before the first provider
query, validates filter/pagination/order receipts before admitting a page, compiles any residual predicate once, and
propagates caller cancellation. Destination `UpsertMany` and source `DeleteMany` pass through the facade's exact-count
receipt validation. An ambiguous exception is surfaced after one dispatch and is never used as a fallback signal.

Move streams and shapes each source candidate once. Confirmed destination identities are written to a private,
delete-on-close, length-prefixed journal with a finite per-value bound; deletion begins only after source streaming
finishes. This preserves copy-before-delete without retaining the dataset in memory or mutating an offset-paged source
between pages. Mirror uses positional, receipt-validated `GetMany` batches for reconciliation and defers mutations of
the side currently being paged. `MirrorConflict` states `Latest`, `Source`, `Destination`, or `Report` once.

`RepositoryFacade.DeleteAll` no longer probes a clear instruction and catches `NotSupportedException`. It selects the
semantic repository `DeleteAll` contract before dispatch for unrestricted managed sources, while external, scoped,
Lifecycle, and override cases retain the provider-bounded semantic delete path.

## Reproduced checks

| Check | Result |
|---|---|
| Focused transfer and clear selection matrix | 19/19 PASS |
| Broader transfer, streaming, and source-policy regression | 107/107 PASS |
| Explicit `.Batch(2)` observation | provider pages and destination writes never exceed 2 |
| Missing bounded-paging capability | rejects with zero provider queries and zero destination writes |
| Invalid provider page receipt | one read dispatch, zero destination writes |
| Cancellation before candidate materialization | one cancelled read dispatch, zero destination writes |
| Ambiguous post-commit destination fault | exactly one destination dispatch; no replay |
| One-way mirror replica deletion | bounded read first, exact deferred delete receipt afterward |
| Same-context movement | no-op before provider work |
| Duplicate-surface negative search | no partition copy/move/replace builder, query-shaped transfer, `BatchSize`, or delete strategy remains in source/tests |
| Downstream compilation | moderation controller and Web adapter TestKit compile against the compact builder |
| Solution build | PASS, 0 compiler errors |
| Dynamic surface map | 52 surfaces, 3,153 public entries, 422 critic matches |
| Framework scorecard projection | 105/105 rows |
| Initiative protocol | 41 cards, 41 progress rows, 105 primer IDs, 22 packets |
| Mutation protocol | 16/16 PASS |
| `git diff --check` | PASS; line-ending notices only |

The legacy SQLite adapter advertises provider-bounded paging but currently returns an invalid handled-filter receipt.
The Framework proof therefore uses a truthful focused repository and keeps the SQLite provider cell RED for its
empty-root gold card; R04 does not weaken receipt validation or patch the legacy connector.

Restore-free builds emitted NU1900 warnings because the sandbox could not reach NuGet vulnerability metadata. The
complete solution compiled with zero errors.
