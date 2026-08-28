---
type: GUIDE
domain: data
title: "DuckDB Engine Guide"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: verified
  scope: install split, single-writer posture, engine options, materialization store, ATTACH pairing
related_guides:
  - entity-analytics.md
  - entity-access-and-streaming.md
  - ../recipes/entity-analytics.md
---

# DuckDB Engine Guide

DuckDB is Koan's embedded analytical engine: an in-process OLAP store that answers aggregation
questions fast and materializes projected answers. It complements the record store — SQLite (or any
elected connector) stays the system of record; DuckDB answers the questions.

Full package details live in the connector's [README](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/Data/DuckDb/README.md)
and [TECHNICAL](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/Data/DuckDb/TECHNICAL.md)
notes. This guide owns the application-facing decisions.

## Install: the split

```powershell
dotnet add package Sylin.Koan.Data.Connector.DuckDb          # managed ADO.NET only (~235 KB)
dotnet add package Sylin.Koan.Data.Connector.DuckDb.Native   # the engine binary (per-RID, ~330 MB across 5 RIDs)
```

The split is deliberate: the connector alone carries no native payload, so hosts choose their
footprint. The native rider pins the engine version — DuckDB storage is backward-compatible, not
forward-compatible, so the pin is what keeps a shipped app's files readable.

As an Entity store, DuckDB works with the ordinary bootstrap and zero configuration: the
zero-configuration target is `.koan/data/Koan.duckdb`.

## The one-writer posture

DuckDB allows exactly one process to hold a database file read-write — and on Windows, even
read-only opens are excluded while a writer holds the file. Koan treats this as composition, not a
caveat:

- **Paths anchor to the content root**, so unrelated applications never share a store by accident.
- **A second writer is classified as an ownership conflict** — non-retryable, health-degrading, with
  a message naming the file. It is never surfaced as a transient fault.
- **Scale-out uses per-host or per-tenant files.** Database-mode routing (one file per routed
  tenant) is the supported multi-tenant posture. A derived store rebuilds from the record store, so
  per-host copies are cheap by construction.

Never point two application instances at one DuckDB file read-write. The engine enforces it, the
adapter classifies it, and the docs promise it — pick the topology instead of fighting the lock.

## Engine options

| Option | Default | Meaning |
|---|---|---|
| `MemoryLimit` | 80% of system RAM | The engine competes with your heap in-process. Set it (`"2GB"`). |
| `Threads` | all cores | Engine thread count. |
| `AutoInstallExtensions` | **`false`** | Runtime extension downloads from DuckDB's CDN are off. Pre-install instead. |
| `ExtensionDirectory` | — | Preloaded extension directory for air-gapped hosts (`INSTALL` once, then `LOAD` offline). |

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.DuckDb(o =>
    {
        o.ConnectionString = "Data Source=.koan/data/Koan.duckdb";
        o.MemoryLimit = "2GB";
        o.AutoInstallExtensions = false;   // the default; shown for clarity
    });
});
```

## Extensions

Extensions (`httpfs` for S3/HTTPS reads, `sqlite` for ATTACH, `spatial`, `vss`) are packaged per
engine version. Air-gapped hosts: run `INSTALL httpfs` once on a connected machine, ship the
`.duckdb/extensions` directory (or point `ExtensionDirectory` at a shipped copy), and loads resolve
locally. With autoinstall off, a missing extension fails with a corrective naming `INSTALL` — never
a silent capability gap.

## The ATTACH pairing

The sqlite scanner is built into the bundled engine, so the engine can ATTACH the application's
existing SQLite database and aggregate over it without ingestion — the canonical pairing:

```csharp
await Data.Source("analytics").Execute("ATTACH '.koan/data/Koan.sqlite' AS app (TYPE sqlite)");
var rows = await Data.Source("analytics").Query<long>("SELECT COUNT(*) FROM app.todos");
```

Lane SQL uses DuckDB's parameter spelling (`$name`, not `@name`) and read lanes are enforced by the
engine: a lane opens `BEGIN TRANSACTION READ ONLY`, so a lane cannot write.

## As the analytics engine

The connector declares the analytics engine capability and registers its projection sink. When
`Sylin.Koan.Data.Analytics` is referenced, materialized projections land in
`.koan/analytics/Koan.duckdb` — per-host derived state that rebuilds from the record store. See the
[Entity analytics how-to](entity-analytics.md).

## Storage format compatibility

The engine's storage format is backward-compatible (newer engines read older files) but not
forward-compatible. The native rider pins the engine version per app; `STORAGE_VERSION` pinning is
available through the connection string for long-lived files. Engine upgrades are ordinary
dependency-floor bumps — the record store never needs converting, only the derived store, which
rebuilds anyway.

## Honest envelope

- One writer per file, across processes — never a farm-shared file.
- Per-entity writes are correct but not the fast path; bulk writes and aggregates are the engine's
  element.
- Constraint-level `ALTER`s are unsupported upstream: the schema policy creates and validates, with
  explicit rebuild instead of pretending to alter.
- Document-expression mapped indexes are declined until planner matching is demonstrated — the
  honest envelope beats an index the engine may ignore.
