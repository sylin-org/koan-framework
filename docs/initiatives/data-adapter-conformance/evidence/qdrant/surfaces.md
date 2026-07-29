---
type: REFERENCE
domain: data
title: "Qdrant Surface Inventory"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green
  scope: Qdrant public and provider surface
---
# Qdrant surfaces

| Surface | Public entry | Claim | Source posture | Effect / result | Semantic owner | Native owner | Failure path | Cells |
|---|---|---|---|---|---|---|---|---|
| Space | `Source(...).Vector<TEntity>(...)` | immutable name/dimensions/metric/visibility | all | create or validate named-vector shape | `VectorSpacePlan` | Qdrant collection | mismatch fails; no repair | V-01, V-02, V-09, V-20 |
| Point | `Vector<TEntity>.Save/Get/Delete` | complete point and positional reads | policy-bound | awaited upsert/retrieve/delete | Vector contract | points REST API | policy, cancellation, status, shape | V-03–V-06, V-11, V-23 |
| Search | `Vector<TEntity>.Search` | bounded deterministic KNN | read | normalized higher-is-closer result | Vector request/result | Query API | unstable bound or unsupported request fails | V-07–V-10, V-24 |
| Filter | `query.Where(Filter...)` | native pre-filtering | read | declared neutral operators only | `FilterSupport` | payload filter | unsupported fails closed | V-13 |
| Batch | repository bulk seam | ordered outcomes, non-atomic truth | write | one bounded native request | `BatchResult` | points mutation | pre-validation or provider failure | V-17, V-18 |
| Lifecycle | `EnsureCreated/Clear/Sync` | policy-correct shape and scoped clear | Managed/ReadOnly/External | validate/create/delete-by-filter/no-op barrier | `DataSourcePlan` | collections/points API | forbidden effects fail before mutation | V-20 |
| Isolation | ambient Entity context | row/container/database separation | closed posture | scoped ID/predicate/name/source | Koan segmentation and naming | point ID, payload, collection, route | missing or lossy scope fails closed | V-21, G-09 |
| Declines | ordinary query/export/batch calls | no fabricated portability | all | no provider call where unsupported | capability set | none | corrective exception | V-12, V-14–V-16, V-18, V-19 |

Public Qdrant configuration is limited to endpoint, API key, readiness, and bounded operator budgets. Vector shape,
lifecycle, access, collection identity, and visibility are not duplicated as provider options.
