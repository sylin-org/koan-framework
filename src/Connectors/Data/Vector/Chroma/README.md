# Sylin.Koan.Data.Vector.Connector.Chroma

Chroma vector storage for Koan. Reference the package, call `AddKoan()`, and `Vector<T>` kNN search
runs against a Chroma server over REST v2 — plan-bound spaces, metadata filter pushdown, and
session-visible mutations with no client driver dependency.

> **Status: not assessed.** The package is installable and its behavioral suite is green (vector-plane
> AODB isolation conformance and the V-01..V-24 provider annex, filter convergence, capability truth),
> and nothing has been promised about it. Claims are decided by the product claim ledger (ARCH-0120),
> not by this README.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Chroma
```

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("Default").Vector<Todo>(space => space
        .Name("todos")
        .Dimensions(384)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session));
});
```

```json
{ "Koan": { "Data": { "Chroma": { "Endpoint": "http://localhost:8000" } } } }
```

The adapter speaks the Chroma REST v2 API under the configured tenant/database
(`default_tenant`/`default_database` on a standalone server). Collections are created and
shape-validated under the framework's managed lifecycle: metric at creation, dimension pinned by the
first write, both re-validated against the declared `VectorSpacePlan` on every boot.

## What it adds

| Capability | State |
|---|---|
| kNN search with normalized [0,1] similarity (cosine, l2, inner product) | declared, proven by the vector-plane suite |
| Metadata filter pushdown (Eq/Ne/range/In/Nin, and/or groups) | declared and store-executed against the flat scalar index |
| Deterministic tie ordering, stable positional get-many, honest upsert/delete outcomes | declared |
| Isolation: row / container / database scoped | declared, proven by the AODB cells (container + database via the name-fold floor) |
| Hybrid (lexical + semantic) search, named spaces, continuation tokens, atomic batches, streaming export | **not declared** — rejected with a corrective message, not silently approximated |
| Negation (`Not`), nested metadata paths, array/size operators, `Exists`, case-insensitive comparison, range comparisons on non-numeric values | **not declared** — Chroma's `where` language cannot express them faithfully; the filter fails closed before provider I/O |

## How it composes

One entity space maps to one Chroma collection; ambient partitions and routed sources fold into the
collection name (the name-mangling floor every Koan vector store realizes). Point identity is the
entity key verbatim, with scope-compiled identities folded into a deterministic UUID so a scoped row
never collides with its unscoped twin. Metadata is dual-written: the full neutral value algebra rides
a reserved JSON string for exact round-trips, and a flat scalar projection (same conversion on write
and on filter values) makes metadata filters answerable by the store itself.

## Limits

- Range operators (`$gt`/`$gte`/`$lt`/`$lte`) push down only when the comparison value is numeric —
  Chroma rejects non-numeric range comparisons server-side, so those fail closed at translation.
- Metadata containers (objects, arrays) round-trip through the neutral blob but are not filterable;
  Chroma matches only flat scalars.
- `MinimumSimilarity` has no Chroma server primitive: the search requests the widest honest window
  (bounded by `MaxSearchCandidates`) and applies the threshold on the normalized scores it returns.
- Distance semantics are Chroma's: cosine distance = 1 − similarity, l2 = squared Euclidean, inner
  product distance = 1 − inner product; the adapter normalizes each onto the shared scale.
