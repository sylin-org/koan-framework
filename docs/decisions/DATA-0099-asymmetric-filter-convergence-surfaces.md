---
id: DATA-0099
slug: DATA-0099-asymmetric-filter-convergence-surfaces
domain: DATA
status: Accepted
date: 2026-06-02
relates-to: [DATA-0096, DATA-0097, ARCH-0079]
---

# DATA-0099: Asymmetric filter-convergence test surfaces (data floors, vector hard-errors)

## Context

Two adapter families verify filter correctness with a convergence oracle — run every filter in a
fixed corpus through the real adapter and through an in-memory reference, and assert identical
results. They are **deliberately asymmetric**, and a periodic instinct to "make them consistent"
keeps surfacing. This DDR records why the asymmetry is correct so it is not unified away.

The asymmetry in the test surfaces is not incidental — it mirrors a deliberate asymmetry in the two
runtime **filter contracts**:

- **Entity/relational data path (DATA-0096).** `FilterPushdownCoordinator` splits a filter into the
  part the adapter can push to the backend and a **residual** evaluated client-side by
  `InMemoryFilterEvaluator` (the "floor"). The adapter has all candidate rows, so flooring the
  residual is correct. **Every** filter therefore works: push what you can, floor the rest.

- **Vector path (DATA-0097 §3).** A kNN search returns only the top-K nearest. Applying a residual
  predicate *after* that truncation would silently drop matches that ranked K+1 only because
  unfiltered neighbours crowded them out (recall loss). So vectors have **no floor**:
  `VectorFilterCoordinator` (`src/Koan.Data.Vector/.../VectorFilterCoordinator.cs`) splits the
  filter and, finding any residual, throws `VectorFilterUnsupportedException` **before** the search
  runs. The vector contract is "fully pushable, or fail loud" — never silent partial results.

Because the contracts differ, the obligations the two test surfaces verify differ:

| | Data convergence (`Koan.Data.AdapterSurface.TestKit/FilterConvergence.cs`) | Vector convergence (`Koan.Data.VectorAdapterSurface.TestKit`, `VectorFilterConvergenceSpecsBase`) |
|---|---|---|
| Reference oracle | `InMemoryFilterEvaluator` over the corpus | InMemory vector adapter (`VectorFilterCapabilities.Full`, `DictionaryFilterEvaluator`) |
| Pushed operator | run via adapter, assert == oracle | run via adapter, assert == oracle |
| Non-pushed operator | floored client-side → asserted == oracle (converges trivially) | rejected by the coordinator → asserted to **hard-error** (`VectorFilterUnsupportedException`), recorded as unsupported |
| Spec intent | "every operator converges with the oracle" | "every operator converges with the oracle **or** hard-errors" |

## Decision

**Keep the two convergence surfaces asymmetric. Do not add floor-convergence coverage to the vector
surface, and do not give the vector path a client-side residual floor to enable it.**

- The data surface asserts floor-convergence for **all** operators (pushed ones are verified against
  the oracle; non-pushed ones converge through the shared floor). This is correct for a contract
  that floors.
- The vector surface asserts **converge-or-hard-error**: each operator a provider declares pushable
  converges with the InMemory-vector oracle, and each operator it does not declare is verified to
  fail loud at the coordinator gate. This is the complete obligation for a contract that has no
  floor — both legal outcomes are tested.

Forcing symmetry would mean building a vector floor (post-filtering the kNN top-K), which is exactly
the recall-loss bug DATA-0097 eliminated. A test that demanded floor-convergence for vectors would
be asserting incorrect behaviour.

## Consequences

- The vector TestKit treating an undeclared operator as "hard-error, record as unsupported" (rather
  than exercising a floor) is **correct coverage**, not a gap. Per-adapter skip counts equal each
  provider's declared capability gaps (e.g. Milvus's 5 unsupported operators = its 5 omitted
  `FilterOperator`s).
- Reviewers must not "align" the two TestKits for cosmetic consistency. The shared corpus and the
  shared "compare to the in-memory oracle" mechanism are the parity; the floor-vs-hard-error
  divergence is intentional.
- `VectorFilterCapabilities` honesty is load-bearing: a declared operator is verified by the
  convergence assertion (a mistranslation diverges and fails); an undeclared operator is verified to
  hard-error.

## Optional tightening (not required)

The one genuine — and small — gap is that the vector convergence spec records the hard-errored
operators and only asserts `supported >= 1`; it does not assert that the hard-errored set **equals**
the provider's declared non-pushable `VectorFilterCapabilities`. In practice this is near-tautological
(the `FilterSplitter` gates on the same declaration), so it is recorded as an optional capability-
honesty tightening, **not** as a reason to add a floor:

> Assert that, for every operator in the corpus, the result either converges (operator is declared
> pushable) or hard-errors (operator is not declared), and that this partition exactly matches the
> adapter's declared `VectorFilterCapabilities`.

## Alternatives considered

- **Add a vector residual floor + symmetric floor-convergence coverage.** Rejected: a kNN residual
  cannot be floored without silently under-returning (DATA-0097 §3). This re-introduces the exact
  fail-silent class DATA-0097 removed and would make the test demand wrong behaviour.
- **Make the data surface skip non-pushed operators too (symmetry the other direction).** Rejected:
  the data floor is real and cheap to exercise; skipping would lose verification that the coordinator
  correctly routes residuals to the floor end-to-end.
