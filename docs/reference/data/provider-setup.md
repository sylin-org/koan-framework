---
type: REFERENCE
domain: data
title: "Discover and test installed data providers"
audience: [developers, architects]
status: current
last_updated: 2026-09-26
framework_version: v1.0.0
validation:
  date_last_tested: 2026-09-26
  status: verified
  scope: provider discovery and candidate setup probing
---

# Discover and test installed data providers

Use the host-owned provider setup surface when an application must offer a database choice before it
persists or activates that choice.

## Application intent

> List every record-store connector composed into this host, render the fields each connector needs,
> and test submitted settings without changing the application's active database.

## Complete expression

The connector package references and the existing `AddKoan()` call are the complete composition
surface. Runtime code reads the compiled candidates and probes one submitted candidate:

```csharp
var candidates = Data.Providers.Candidates;

var result = await Data.Providers.Probe(
    "mongo",
    new Dictionary<string, string?>
    {
        ["ConnectionString"] = "mongodb://localhost:27017",
        ["Database"] = "app"
    },
    ct);
```

Each candidate carries its canonical provider ID, display name, aliases, setup fields, and whether it
implements a live probe. Field keys are the source-setting keys accepted beneath
`Koan:Data:Sources:{name}`. Secret fields are marked so a caller can use a protected input and avoid
persisting them in clear text.

No extra registration, temporary named source, Entity type, or change to `Default` is required.

## Guarantee and correction

`Candidates` is the same immutable provider set used by Data provider election. `Probe` resolves only
within that set, validates required fields, applies the configured Data diagnostic timeout, and calls
the selected adapter's candidate probe without registering a source or changing the active route.
The probe does not create provider databases, schemas, buckets, tables, collections, or Entity data.

The result distinguishes:

- `Ready` — the intended target was opened and answered a non-writing operation;
- `Reachable` — the submitted placement is valid and reachable/provisionable, but no existing target
  was opened (for example a new managed file path or an endpoint whose Entity containers are deferred);
- `InvalidConfiguration` — required or provider-specific settings are invalid;
- `Unavailable` — the configured endpoint or target did not answer;
- `TimedOut` — the framework diagnostic deadline elapsed;
- `Unsupported` — a third-party adapter did not implement candidate probing.

Probe results and corrections never echo submitted values. Caller cancellation still throws rather
than being converted into a provider finding.

## Ownership and coalescence

`DataProviderCatalog` remains the sole owner of installed provider identity and aliases. The public
setup facade projects that catalog; it does not create a second discovery registry. Each adapter owns
its field description and the smallest truthful native probe because only that adapter can distinguish
endpoint reachability from target readiness. The existing configured-source `Data.Source(name).Doctor()`
remains unchanged: it diagnoses an already frozen source and is not reused for hypothetical settings.

This gives people, IntelliSense, and coding agents one readable branch: discover through
`Data.Providers.Candidates`, then test through `Data.Providers.Probe(...)`.

## Shipped provider coverage

| Provider | Setup fields | Probe meaning |
| --- | --- | --- |
| `inmemory` | none | ready in-process |
| `json` | `DirectoryPath` | directory read/write readiness; a new usable path is reachable |
| `sqlite` | `ConnectionString` | `SELECT 1`, or reachable when a managed file awaits creation |
| `duckdb` | `ConnectionString` | `SELECT 1`, or reachable when a managed file awaits creation |
| `mongo` | `ConnectionString`, `Database` | database-scoped ping |
| `postgres` | `ConnectionString` | open plus `SELECT 1` |
| `cockroach` | `ConnectionString` | open plus `SELECT 1` |
| `sqlserver` | `ConnectionString` | open plus `SELECT 1` |
| `mysql` | `ConnectionString` | open plus `SELECT 1`; applies equally to MariaDB endpoints |
| `firebird` | `ConnectionString` | open plus `SELECT 1` |
| `redis` | `ConnectionString`, optional `Database` | selected database ping |
| `couchbase` | `ConnectionString`, `Bucket`, `Username`, `Password` | cluster and bucket readiness |
| `couchdb` | `ConnectionString`, optional `Database`, `UserId`, `Password` | authenticated endpoint reachability |

Provider presence remains reference-driven. This API does not claim that an unreferenced connector is
available, install packages, persist a choice, or perform default-route cutover.
