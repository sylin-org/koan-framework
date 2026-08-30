# Chroma adapter technical contract

## Ownership

`VectorSpacePlan` is the only owner of logical space name, dimensions, metric, model identity, source, and visibility.
`DataSourcePlan` owns access and storage lifecycle. `ChromaOptions` owns endpoint, tenant/database placement,
credential, and bounded operational budgets only.

The runtime has four execution parts:

- `ChromaVectorAdapterFactory` validates the plan, resolves one source-aware route, and creates the repository;
- `ChromaRoute` holds immutable endpoint, tenant/database, credential, source, and policy;
- `ChromaClient` owns bounded REST transport, status handling, and transient retries;
- `ChromaRepository` realizes shape, identity, metadata, lifecycle, and vector operations.

`ChromaFilter` is the single native where-clause writer. There is no generic HTTP-vector superclass or alternate
legacy execution path.

## Collections and shape

Koan's naming service composes Entity, routed source, ambient partition, and segmentation particles into a
case-preserving Chroma collection name. Only identifier-safe, injective particles are accepted. The collection lives
under the configured Chroma tenant/database (`default_tenant`/`default_database` on a standalone server).

All item-level routes address the collection **UUID**; the name is accepted only by
`GET/DELETE /collections/{name}`. The repository caches name→id per instance and refreshes it once when an item route
answers 404 (a collection that was deleted and recreated changes its id). Chroma's create API takes no dimension
(`hnsw.dimensions` is rejected in 1.5.9): the dimension pins from the first upsert, so shape validation checks the
configured `hnsw.space` against the plan metric always, and the reported `dimension` against the plan when non-null.
`metadata.koan_space`/`koan_model` are written at creation and the model re-validated on inspection. Shape
disagreement is a corrective error; the adapter does not repair or reinterpret the collection.

Managed save and ensure operations create a missing collection. Reads do not create. External and read-only source
policies are enforced before schema or mutation work. Chroma's REST v2 writes are synchronous: an awaited upsert or
delete is visible to the next read, which is the adapter's `Session` visibility realization. `Eventual` is not
simulated. `Clear` is a delete-by-where over the reserved `__koan_id $ne ""` predicate (Chroma refuses an empty
where-clause) combined with any scope identity and scope predicate.

## Identity and metadata

Chroma accepts arbitrary non-blank string ids (verified with unicode and 300+ character keys), so entity keys stay
verbatim as storage ids. Scope-compiled identities fold into a deterministic UUIDv5 of
`scope:<identity>\u001f<key>` so equal entity keys coexist across isolated row scopes; `__koan_scope` carries the
scope identity and scoped reads apply it as a where-clause alongside the requested ids.

Neutral metadata is stored under the `__koan_metadata` string with the full value algebra preserved for exact
round-trips (nested objects, arrays, typed scalars, byte[]). A flat projection of top-level scalars rides alongside it
under `__koan_index.<field>` keys with one fixed value conversion (Guid→"D", dates→"O", TimeSpan→"c", byte[]→base64,
numbers as numbers) — the same conversion runs on filter comparison values, so pushdown compares like with like.
Container values and nulls stay in the blob only: Chroma metadata matches flat scalars (str/int/float/bool) and
silently matches nothing against anything else. Reserved `__koan_*` user keys are rejected upstream by the metadata
materializer.

**Never write `null` metadata entries on the wire — always `{}`.** A Chroma 1.5.9 WAL bug (observed live,
2026-08-29) leaked a deleted collection's last metadata into another collection's null-metadata upsert; explicit
`{}` entries are clean. The delete response's `deleted` count is similarly unreliable (it counted requested ids, not
removed records), so upsert-insert/update and delete-deleted/missing outcomes are derived from a pre-fetch of
existence, never from the wire receipt.

## Search and filtering

The adapter uses `POST /collections/{id}/query` with one query embedding, `n_results` candidates, and the translated
`where` clause. Execution is reported as `Approximate`; the adapter never infers exactness from a small result set.

Chroma distance semantics and the portable higher-is-closer normalization:

| Metric | Chroma distance | Portable similarity |
|---|---|---|
| Cosine | `1 − cosine similarity ∈ [0,2]` | `1 − distance / 2` |
| l2 | squared Euclidean | `1 / (1 + distance)` |
| ip | `1 − inner product` | logistic of `1 − distance` |

Stable score ties are resolved deterministically: the search requests `Top + 1` candidates, re-ranks by (similarity
desc, stable id asc), and doubles the candidate window while the cutoff stays tied — up to the whole collection, or
`MaxSearchCandidates`, beyond which it is a corrective error rather than a silent reordering. `MinimumSimilarity` has
no server primitive, so those searches request the widest honest window and post-filter on the normalized scores;
filters likewise widen the window, because Chroma's where-clause evaluation must not silently under-return.

The provable where-language is: `Eq`, `Ne`, `Gt/Gte/Lt/Lte` (numeric comparison values only — Chroma rejects string
ranges), `In`, `Nin`, `$and`/`$or` groups (explicit only: multi-key implicit AND is rejected server-side; Chroma
matches absent keys for `$ne`/`$nin` and not for ranges, agreeing with the neutral evaluator). Declined before
provider I/O with a corrective error: `Not` (no `$nor`; complements diverge on absent keys), nested paths (dotted
keys are literal names that silently match nothing), `Exists`, array/size operators, case-insensitive comparison, and
`Eq(null)` (invalid server-side).

## Provider facts (probed 2026-08-29, chromadb/chroma:1.5.9)

- REST v2 requires the tenant/database prefix: `/api/v2/tenants/{tenant}/databases/{database}/...`; bare
  `/api/v2/collections` 400s with a CRN validation error. Readiness is `GET /api/v2/heartbeat`.
- The image exposes port 8000 and has **no HEALTHCHECK** — container fixtures must wait on the heartbeat endpoint,
  not the default Testcontainers strategy.
- `n_results` larger than the collection is accepted (returns everything); an empty collection returns empty arrays,
  not an error. `count` is a GET; `upsert`/`get`/`query`/`delete` are POSTs. Get drops missing ids silently —
  positional get-many is adapter work.
- Collection create conflicts answer 409 ("already exists"); `get_or_create: true` never updates an existing
  collection's configuration, so create-on-conflict must GET and validate the existing shape instead.
- Dotted metadata keys, unicode ids, and long (300+ char) ids are accepted verbatim.
- Server version string from `/api/v2/version` is the API version ("1.0.0"), not the product version.
