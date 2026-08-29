# Sylin.Koan.Data.Connector.CouchDb

Apache CouchDB document storage for Koan. Reference the package, call `AddKoan()`, and `Entity<T>`
persistence runs against CouchDB over plain HTTP — no driver package, no vendor client, just
`System.Net.Http`.

> **Status: not assessed.** The package is installable and its behavioral suite is green (record-plane
> conformance, the full filter-convergence corpus plus its pushdown guard, paging windows, capability
> truth), and nothing has been promised about it. Claims are decided by the product claim ledger
> (ARCH-0120), not by this README.

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
