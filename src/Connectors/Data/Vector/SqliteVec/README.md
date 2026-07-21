# Sylin.Koan.Data.Vector.Connector.SqliteVec

Durable, in-process vector search for Koan through the sqlite-vec `vec0` extension—no vector server required.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.SqliteVec
```

The connector brings the Vector runtime it implements. Keep `AddKoan()` and the same Entity vector code used by other
providers.

## Smallest meaningful result

```csharp
using Koan.Data.Core.Model;
using Koan.Data.Vector;

public sealed class Article : Entity<Article> { }

await Vector<Article>.Save("koan", [1f, 0f, 0f]);
var nearest = await Vector<Article>.Search([0.9f, 0.1f, 0f], topK: 5);
```

With no sqlite-vec placement of its own, the provider pairs with SQLite's configured local connection. If neither is
configured, both local providers converge on `Data Source=.koan/data/Koan.sqlite`. Override only when vectors should
live elsewhere:

```json
{
  "ConnectionStrings": {
    "SqliteVec": "Data Source=.koan/vectors.db"
  },
  "Koan": {
    "Data": {
      "SqliteVec": {
        "DistanceMetric": "cosine"
      }
    }
  }
}
```

Supported metrics are `cosine`, `l2`, and `l1`; an unknown value fails correctively.

## Readiness and inspection

Package presence makes sqlite-vec available but does not open or create a database. Before selection, its health is
non-critical `Unknown`. Once an Entity selects it, the exact routed source becomes critical; readiness opens that
database, loads the embedded native extension, and reports failure without substituting another provider. Startup
facts show the effective de-identified store and whether it was explicit, paired from SQLite, or the local fallback.

## Boundaries

- Native binaries are bundled for `win-x64`, `linux-x64`, and `linux-arm64`. Other platforms fail with the supported
  RID list.
- The first use extracts and hash-validates vec0 v0.1.9 in a versioned temporary directory.
- Each entity/source repository holds one SQLite connection and serializes access. This is a durable embedded floor,
  not a distributed or high-concurrency vector service.
- sqlite-vec stores metadata but this adapter does not implement metadata-filter pushdown. Tenant/Shared searches fail
  closed; they never silently search across tenants. Partition and routed-source isolation remain supported.
- The first vector fixes a collection's dimension. Mixing dimensions is invalid.
- Hybrid search, continuation, export, and multiple vectors per entity are unsupported.

## References

- [Technical reference](./TECHNICAL.md)
- [Vector runtime](../../../../Koan.Data.Vector/README.md)

