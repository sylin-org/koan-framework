# Sylin.Koan.Data.Connector.CouchDb

Apache CouchDB document storage for Koan. Reference the package, call `AddKoan()`, and `Entity<T>`
persistence runs against CouchDB over plain HTTP — no driver package, no vendor client, just
`System.Net.Http`.

> **Supported provider** (`couchdb-data-provider`): the record-plane conformance suite passes against a
> real CouchDB 3.5 server — conformance, the full filter-convergence corpus plus its pushdown guard,
> paging windows, capability truth — and the connector is machine-checked under NativeAOT by
> `scripts/aot-verify.ps1`. The Limits below are part of the claim.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.CouchDb
```

```json
{ "Koan": { "Data": { "Sources": { "Default": {
  "Adapter": "couchdb",
  "ConnectionString": "http://localhost:5984",
  "CouchDb": { "UserId": "admin", "Password": "..." } } } } } }
```


## Zero configuration

Start CouchDB with its documented development credentials and use the app unchanged — the adapter
resolves `auto` to the conventional local endpoint and the credential layering fills the rest:

```powershell
docker run -d -p 5984:5984 -e COUCHDB_USER=admin -e COUCHDB_PASSWORD=password couchdb:3.5
```

Credentials resolve most-specific-first: configuration keys (`Koan:Data:CouchDb:UserId` /
`Password`), then the official image's own environment convention (`COUCHDB_USER` /
`COUCHDB_PASSWORD` — the ones `docker run` already received), then the development default
`admin`/`password` (the same defaults the Testcontainers CouchDB modules and the image
documentation use; CouchDB 3.x refuses to start without an admin user, so an empty default is not
viable). The endpoint and database default to `localhost:5984` and `koan`; the database is created
on first use under managed lifecycle.
A `couchdb://user:password@host:port` URI is also accepted. Under managed lifecycle policy the
adapter creates each entity's database on first use; under `StorageLifecycle: External` it never
creates anything and an absent database reports as absent.

## Shape

CouchDB has no collections — one entity container resolves to one **database** under the source's
prefix (`Database` defaults to `koan`; `Todo` lives in `koan_todo_<namesuffix>`). Container-scoped
partitions resolve to distinct physical databases; database-scoped routing selects the prefix per
source.

## What it adds

| Capability | State |
|---|---|
| Full scalar filter set + collection operators (`$all`/`$in`/`$nin`/`$size`, bare element equality) | declared and store-executed (Mango `_find`) |
| Element-LIKE inside arrays (`$like`) | **not declared** — Mango `$regex` does not cross array elements; the floor answers it and the query records a runtime fallback fact |
| Sort lowering | **not declared** — CouchDB gates `_find` sort on a matching index; only the identity (`_id`) order is store-side |
| Atomic batch | **not declared** — `_bulk_docs` commits per document; item outcomes are complete |
| Provider-bounded paging (streams) | **not declared** — Mango has no server-side cursor; streaming rejects |

## Limits and store notes (learned the hard way)

- Any top-level document member starting with `_` is rejected ("Bad special document member"). The
  framework-managed isolation discriminators (underscore-prefixed by convention) therefore ride in a
  legal `koan` subdocument on write, hoist back on read, and selectors target `koan.<name>` — the
  row-isolation predicate stays store-enforced.
- Writes are MVCC: an update or delete carries the current `_rev` (409 without). Upserts read the
  revision first; a race surfaces as a named conflict, never as a silent overwrite.
- JSON `null` fields are stored as nulls, so "exists" compiles to present-and-non-null.
- The admin party is disabled in CouchDB 3.x defaults; credentials are required.

The container fixture in the test project wires a working single-node instance; it is the reference.
