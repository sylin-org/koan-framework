---
type: EVIDENCE
domain: data
title: "DAC-05 verification — Entity execution, receipts, batch, and streams"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework-owned Entity, query, bulk, batch, transaction-coordination, and stream semantics
---

# DAC-05 verification — Entity execution, receipts, batch, and streams

## Result

PASS for the Framework-owned execution boundary. Data now owns one correctness order from provider candidates through
residual filter, unhandled sort, true total, pagination, projection, and final visible Entity Lifecycle. Query results
must prove each handled axis. Get-many is positional. Bulk writes prepare once, dispatch once, and require an exact
affected-count receipt. Exact mutation outcomes, conditional replace, atomic batches, and complete per-item outcomes
are capability-plus-native-seam contracts; missing seams reject before work.

Batch item results are validated in native grouped positions and returned in logical builder-call order. A false or
indeterminate post-dispatch receipt is outcome-unknown and is never replayed. The shared transaction coordinator now
publishes only sequential deferred coordination, never native/local atomicity.

Current provider implementations are not grandfathered. The existing SQLite relationship case is deliberately RED:
the adapter advertises native filter execution but does not return `FilterHandled=true`. DAC-11 must satisfy the
receipt in the empty-root SQLite replacement; Core does not infer it or add a compatibility path.

## Executable evidence

| Evidence | Result |
|---|---|
| Entity/query/pipeline/stream owned matrix | 76 passing cases: positional get-many, outcome seams, one-dispatch bulk, lifecycle timing, conditional replace, atomic preflight/receipt, logical batch order, false query receipts, projection fallback, count, cancellation, and bounded streams |
| Source/transaction/relationship framework matrix | 62 passing cases after excluding the one provider-owned SQLite receipt RED |
| Convergence suite | 19/19 passed; LINQ and JSON-filter paths share the same plan/finalization behavior |
| Existing SQLite native relationship receipt | Expected RED: `QueryReceiptRejectedException` because a pushed filter is not acknowledged |
| `Koan.sln` restore-free build | PASS; zero warnings and zero errors |
| Diff hygiene | `git diff --check` PASS; only repository line-ending notices |

The broad Data Core suite remains unsuitable as a gate because of the unrelated pre-existing runtime-fact
multiplicity, Windows EventLog permission, and missing AI fixture failures recorded under DAC-04. DAC-05 uses the
owned deterministic matrices and complete solution compilation.

## Ownership proof

- `RepositoryFacade` is the single Entity boundary for source policy, guards, isolation, transforms, write stamps,
  Lifecycle, keyed normalization, bulk preparation, batch qualification, conditional replace, and exact outcomes.
- `QueryReceiptValidator` rejects false filter, sort, page, projection, total, and count claims before public return.
- `FilterPushdownCoordinator` owns residual/filter/sort/total/page/projection order. Adapters execute only the supplied
  pushable definition and report what they actually handled.
- `IDataQueryBoundary` separates provider candidates from visible materialization. Lifecycle runs only for the final
  page or accepted stream item, with no per-item collection allocation on the stream hot path.
- `InMemoryEntityProjection` uses bounded, compiled member plans and preserves Entity identity. Unsupported nested
  sparse Entity shapes reject before provider work with the neutral records correction.
- `IMutationOutcomeRepository` and `IBatchSet.ExecutionCapabilities` are the exact native seams behind stronger
  mutation and batch guarantees. Capability strings alone cannot satisfy them.
- `TransactionCoordinator` is explicit sequential coordination. Its typed failure reports commit/retry/replay facts
  and completed-operation count without leaking provider messages.

## Primer-row disposition

| Rows | DAC-05 disposition |
|---|---|
| B-02 | PASS at the framework boundary: output cardinality/order/duplicates/null slots are normalized and unrequested identities reject. Provider identity codecs remain adapter cases. |
| B-03 | PASS for exact upsert/delete outcome vocabulary, capability/seam coupling, lost-race/missing behavior, commit validation, and no replay. Provider-native insert/update distinction remains a child proof. |
| B-04–B-05 | PASS for guards/transforms/Lifecycle placement, one native bulk dispatch, exact counts, bounded semantic mass removal, deferred mutation, missing target, atomic qualification, and logical item receipts. Native rollback/partial-failure behavior remains per adapter. |
| B-06–B-07 | PASS for Data-owned split/finalization and strict per-axis receipts. Complete operator/value corpora and provider plan evidence remain DAC-06 and adapter child proofs. |
| B-08 | PASS for unsupported atomic/idempotent/conditional/projection/bounded operations rejecting before unsafe work; post-dispatch inconsistency never replays. |
| B-09 | PASS for coordinator admission, complete-order/page receipts, bounded page size, cancellation, and final-visible Lifecycle. Native resource release and true provider bounds remain adapter child proofs. |
| C-03 | PASS: semantic mass removal is provider-bounded and scope-preserving; external `Optimized` lowers safely and structural `Fast` rejects. |
| G-05–G-06 | PASS for framework atomic/CAS qualification and honest outcomes. Native rollback, commit-unknown, and single compare-and-set evidence remain provider child proofs. |
| P-02, P-04 | PASS for cached projection plans, one bulk dispatch, no transaction/atomicity fiction, and no client-page/stream claim. Provider-relative allocation/plan budgets remain certification proofs. |
| B-01 | Child proof: every adapter must prove its key/value codecs and boundary corpus through DAC-06 and its provider card. |

These child proofs are explicit conformance work, not compatibility exceptions. The SQLite and MongoDB golds must
implement the shared seams directly from empty roots; fleet adapters follow only after both gold contracts pass.
