---
type: REFERENCE
domain: data
title: "DAC-49 Proposed Vector Conformance Annex"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: exact public Vector language and V-01 through V-24 acceptance cells ratified on 2026-07-27
---

# Proposed Vector conformance annex

This is the accepted decision record. The primer contains the normative language; this file preserves the ballot that
the product owner ratified on 2026-07-27 and is not a second semantic catalog.

## User-delight surface

One source owns routing, policy, and the vector-space contract:

```csharp
koan.Data.Source("Semantic").Vector<Document>(space => space
    .Name("documents")
    .Dimensions(1536)
    .Metric(VectorMetric.Cosine)
    .Visibility(VectorVisibility.Session));
```

Ordinary writes and reads stay small:

```csharp
await Vector<Document>.Save(document.Id, embedding, new { document.Category }, ct);

VectorPoint<string>? stored = await Vector<Document>.Get(document.Id, ct);
bool deleted = await Vector<Document>.Delete(document.Id, ct);
```

Search uses one compact builder instead of a positional option list:

```csharp
VectorSearchResult<string> result = await Vector<Document>.Search(
    embedding,
    query => query
        .Top(12)
        .Where(filter)
        .Space("content")
        .AtLeast(.82),
    ct);

foreach (var match in result.Items)
    Console.WriteLine($"{match.Id}: {match.Similarity:P1}");
```

Earned features add only the words they need:

```csharp
var related = await Vector<Document>.Search(
    embedding,
    query => query.Text("adapter design").SemanticWeight(.7),
    ct);

var next = await Vector<Document>.Search(
    embedding,
    query => query.Top(20).After(related.Continuation!),
    ct);
```

`VectorQuery` has seven orthogonal clauses: `Top`, `Where`, `Space`, `AtLeast`, `Text`, `SemanticWeight`, and `After`.
The zero-clause query is pure vector search with `Top(10)`. `SemanticWeight` is valid only with `Text`, is inclusive
`0..1`, and says exactly which side the number weights. Unsupported clauses reject before provider I/O.

## Exact regular result model

```csharp
public sealed record VectorPoint<TKey>(
    TKey Id,
    ReadOnlyMemory<float> Embedding,
    DataObject? Metadata);

public sealed record VectorMatch<TKey>(
    TKey Id,
    double Similarity,
    DataObject? Metadata);

public enum VectorSearchAccuracy
{
    Exact,
    Approximate
}

public sealed record VectorSearchExecution(
    VectorMetric Metric,
    VectorSearchAccuracy Accuracy,
    int? CandidatesConsidered);

public sealed record VectorSearchResult<TKey>(
    IReadOnlyList<VectorMatch<TKey>> Items,
    string? Continuation,
    VectorSearchExecution Execution);
```

`DataObject` uses the primer's closed neutral value algebra. Input POCO/JSON/provider metadata is normalized once or
rejected; provider runtime objects never escape through the regular result.

`Similarity` is always finite, in `[0,1]`, and higher means closer. Adapters monotonically normalize their declared
metric: cosine maps its full range, non-negative distances use `1 / (1 + distance)`, and unbounded inner product uses a
stable logistic transform. The value preserves rank inside one declared space; it is not comparable across models,
spaces, metrics, or providers. Provider-native distance/score remains restricted diagnostic evidence. `AtLeast` uses
this normalized value, with native translation when possible and fail-closed rejection when bounded equivalence is not
possible.

An exact/approximate execution fact is mandatory. Approximate search is not a failure and may return a different valid
candidate set, but every returned set is unique, bounded by `Top`, ordered by descending `Similarity`, and tie-broken by
stable ID. `CandidatesConsidered` is present only when the provider reports it honestly.

There is no `TotalKind`: kNN search does not promise a meaningful global total. A continuation exists only when the
provider can resume the same source/space/query snapshot safely; otherwise the result is deliberately non-pageable.

## Persistence, visibility, and batches

- `Save` is an upsert. Reusing an ID replaces its embedding and metadata without creating a second point.
- A successful single write returns no fabricated count. `Delete` is `true` only when a visible point was removed.
- `Get(id)` returns one complete point or `null`. `Get(ids)` returns one slot per input ID, in input order, with `null`
  for missing points. It does not return a dictionary that erases duplicates or order.
- Batch save/delete returns the shared `BatchResult<TKey>` with per-item outcomes. A native bulk claim may optimize the
  operation but cannot weaken outcomes, source guards, cancellation, or isolation.
- `VectorVisibility.Session` is the default: after an awaited write/delete, subsequent operations in that source see
  it. `Eventual` is explicit; `Sync()` is then a visibility barrier for all earlier writes in that source. A skipped
  refresh, undocumented polling loop, or arbitrary delay is not a visibility contract.
- An Atomic Batch claim means all-or-nothing provider mutation. A non-atomic bulk result says so; it never borrows the
  transaction vocabulary.

`SaveWithVector` remains orchestration, not an adapter primitive. It never advertises cross-store atomicity unless one
real coordinator proves it. Partial completion throws one typed coordination failure containing entity/vector commit
facts and safe retry/compensation guidance.

## Source policy and lifecycle

The existing Source contract applies without a Vector-specific policy vocabulary.

- `ReadOnly` rejects save, delete, batch, clear, and write-side coordination before callbacks, readiness, provider
  clients, or storage creation.
- `External` rejects create, repair, rebuild, and provider auto-create. It may validate the declared vector-space shape.
- `Managed` may `Ensure()` the exact declared name/dimensions/metric/model/metadata shape and may repair only when
  explicitly authorized.
- `Clear()` is semantic delete-all and follows the same External/ReadWrite equivalence rule as Entity `DeleteAll`.
- `Sync()` is only a visibility barrier. The current destructive `Flush()` name is retired.
- Structural rebuild/statistics/optimization are registered operations with explicit effects, or provider-native
  extensions. They do not expand the ordinary Vector facade.

Partitions and routed sources use Data's existing axes. Row isolation is a managed metadata predicate; container and
database isolation alter the physical vector-space address. A pinned physical name that defeats an active axis rejects
at plan time rather than logging and continuing.

## Prior-art conclusions

Provider vocabulary cannot be Koan's public semantic contract:

| Provider fact | Design consequence |
|---|---|
| [Qdrant collections](https://qdrant.tech/documentation/manage-data/collections/) bind dimensions and a metric; named vectors may bind different shapes. | Name, dimensions, metric, and named space belong to one immutable plan. |
| [Weaviate](https://docs.weaviate.io/weaviate/concepts/search/vector-search) distinguishes lower-is-better distance from higher-is-better certainty, and certainty is not valid for every metric. | Raw `Score`/`Distance` cannot be the regular cross-provider result. |
| [Milvus](https://milvus.io/docs/range-search.md) reverses better/worse direction by metric, and its [consistency levels](https://milvus.io/docs/consistency.md) trade visibility for latency. | Similarity direction and visibility must be explicit Koan guarantees. |
| [Elasticsearch](https://www.elastic.co/guide/en/elasticsearch/reference/8.18/knn-search.html) transforms native similarity into `_score` and separates `k` from candidate count. | Normalized similarity and approximate execution/candidate facts are different concerns. |
| [OpenSearch](https://docs.opensearch.org/latest/mappings/supported-field-types/knn-spaces/) defines metric-specific distance-to-score transforms and multiple filtering strategies. | The adapter owns metric translation and must prove filter placement. |
| [sqlite-vec](https://alexgarcia.xyz/sqlite-vec/features/knn.html) returns lower-is-better distance, binds dimensions in `vec0`, and performs exact brute-force kNN. | An exact local oracle is feasible, but its raw distance is not portable API. |

The common delight is therefore: one immutable space, one higher-is-better similarity, explicit approximation, honest
visibility, and fail-closed earned features.

## Current-surface baseline

The existing Docker-free suites were used as evidence, not authority. The InMemory Vector surface passed 34/34 cases.
SqliteVec passed 29 cases and reported five explicit skips: export, statistics, empty-index export, hybrid search, and
filter convergence. The baseline confirms that shared source/isolation mechanics can execute without provider
infrastructure; the skips identify exactly where the annex must demand an earned claim, a fail-closed path, or a real
implementation rather than treating absence as green.

## Proposed profiles

| Profile | Applicability | Cells |
|---|---|---|
| Vector Core | every connector exposing `Vector<TEntity>` | Source Core plus V-01–V-11, V-20, V-23–V-24 |
| Eventual Vector Visibility | source explicitly selects `Eventual` | V-12 |
| Vector Filters | metadata `Where` is announced | V-13 |
| Vector Hybrid | `Text` search is announced | V-14 |
| Named Vector Spaces | more than one vector space per point is announced | V-15 |
| Vector Continuation | resumable search is announced | V-16 |
| Vector Bulk | native bulk save or delete is announced | V-17 |
| Vector Atomic Batch | all-or-nothing batch is announced | V-18 plus G-05 |
| Vector Export | bounded export is announced | V-19 |
| Managed Vector Lifecycle | create/repair is allowed | V-20 plus A-07, A-09, G-01 |
| Vector Isolation | each announced row/container/database mode | V-21 plus G-09, one case per mode |
| Entity/Vector Coordination | `SaveWithVector` is exposed | V-22 |

Read-only and External profiles select vector cases under C-01–C-06. Provider-native inspection selects D-09. Vector
search/persistence operations add cases to the common P-01–P-06 performance/placement cells rather than inventing a
second performance catalog.

## Proposed stable acceptance cells

- **V-01** [STATIC, BOOT, NEG] A vector-space plan binds source, safe physical name, dimensions, metric, visibility,
  optional model identity, and optional named space once; an unelected provider performs no I/O.
- **V-02** [LIVE, ORACLE, NEG] Empty, non-finite, wrong-dimension, or wrong-space embeddings reject before mutation;
  every valid boundary value round-trips without drift.
- **V-03** [LIVE, ORACLE] Saving a new ID inserts one point; saving it again atomically replaces embedding and metadata
  without a duplicate.
- **V-04** [LIVE, ORACLE, FAULT] Delete returns the correct existing/missing outcome; provider failure never reports
  success and does not widen scope.
- **V-05** [LIVE, ORACLE] Get-one returns one complete point or `null`; get-many preserves input count, order, and
  duplicates, with `null` in every missing slot.
- **V-06** [LIVE, ORACLE, NEG] Metadata survives the neutral value algebra without reserved-field collision, shape
  invention, provider objects, or missing/null confusion.
- **V-07** [LIVE, ORACLE] Search returns at most `Top`, no duplicate ID, descending similarity, and stable ID tie order;
  empty and fewer-than-requested results are normal.
- **V-08** [LIVE, ORACLE, PLAN] Similarity is finite `[0,1]`, higher is closer, monotonic for the declared metric, and
  `AtLeast` is applied equivalently or rejected before unbounded work.
- **V-09** [LIVE, ORACLE, NEG] Search uses exactly the declared source, dimensions, metric, model, and named space; a
  point/query from another space never mixes silently.
- **V-10** [LIVE, ORACLE, PLAN] Exact/Approximate and candidate facts report provider work honestly; approximation is
  never presented as an exact oracle result.
- **V-11** [LIVE, ORACLE, FAULT] Default Session visibility makes each awaited save/delete observable to subsequent get
  and search operations in the same source without arbitrary sleeps.
- **V-12** [LIVE, ORACLE, FAULT] Explicit Eventual visibility may defer search visibility; `Sync` is cancellable,
  bounded, and makes every earlier accepted write/delete in that source visible or fails.
- **V-13** [LIVE, ORACLE, NEG, PLAN] `Where` constrains the candidate set before ranking. The shared filter oracle
  agrees for every announced operator; residual/post-filter fallback rejects before provider I/O or unbounded work.
- **V-14** [LIVE, ORACLE, NEG, PLAN] Hybrid search defines `SemanticWeight` endpoints as the pure lexical/vector modes,
  preserves normalized final rank, and rejects unsupported text/weight combinations before I/O.
- **V-15** [LIVE, ORACLE, NEG] Each named space keeps its own immutable dimensions/metric/model, and unsupported or
  ambiguous space selection fails with safe available choices.
- **V-16** [LIVE, ORACLE, NEG] A continuation is opaque and bound to source, space, query, filter, and ordering; resume
  has no duplicates/gaps within its declared snapshot contract, and wrong-context reuse rejects.
- **V-17** [LIVE, ORACLE, FAULT, PLAN] Each native bulk save/delete preserves per-item outcomes, cancellation, guards,
  visibility, and isolation; dispatch count and partial failure are reported honestly.
- **V-18** [LIVE, ORACLE, FAULT] An Atomic Batch claim is all-or-nothing under injected mid-batch failure; otherwise the
  result is explicitly non-atomic and retains every item outcome.
- **V-19** [LIVE, ORACLE, FAULT] Export is provider-bounded, cancellable, and yields every visible point exactly once
  under its stated snapshot/weak-consistency contract without materializing the corpus.
- **V-20** [LIVE, NEG, PLAN] Ensure/validate/clear obey lifecycle and access policy, compare the complete space shape,
  never auto-create under External, and keep visibility sync distinct from destructive clearing.
- **V-21** [LIVE, ORACLE, NEG] Every announced vector isolation mode prevents cross-scope get/search/export/delete and
  maps to the declared metadata or physical-address mechanism; a pinned conflicting name rejects at plan time.
- **V-22** [LIVE, FAULT, NEG] Entity/vector coordination never invents cross-store atomicity; every injected stage
  failure exposes safe commit facts, retry disposition, and compensation guidance without leaking provider secrets.
- **V-23** [FAULT, NEG, LIFE] Cancellation, timeout, rate limit, schema mismatch, unavailable provider, and disposal map
  to the shared failure taxonomy; host disposal releases clients, cursors, polling, and background work.
- **V-24** [PERF, PLAN] Warm single save/get/search, native bulk, filtered search, and materialization meet pinned
  provider-relative budgets with one compiled immutable plan and no per-result reflection or metadata-shape rebuild.

## Ratification ballot

Approval means all of the following, together:

1. Source owns Vector name/dimensions/metric/visibility through `.Vector<TEntity>(...)`.
2. Search uses the seven-clause compact builder above.
3. Regular results expose normalized `Similarity`, never ambiguous provider `Score`/`Distance`.
4. Exact/Approximate is mandatory execution truth; global total is removed.
5. Session visibility is default; Eventual requires explicit configuration and `Sync` supplies the barrier.
6. `Flush` is retired as destructive vocabulary; `Clear` deletes and `Sync` waits for visibility.
7. Get-many preserves positional missing values; batch writes return per-item results.
8. Filters are native pre-filters or fail closed; no silent post-filter fallback.
9. `SaveWithVector` is explicit non-atomic orchestration unless a real coordinator proves otherwise.
10. V-01–V-24 and the profile table above become the sole Vector annex inside the existing primer.
