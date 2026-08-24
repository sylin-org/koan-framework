---
type: REFERENCE
domain: data
title: "Entity stores"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/data/entity-stores.md
---

# Entity stores

Persist business state as an `Entity<T>` and keep using the same Entity verbs as the application
moves from a local file to a service-backed store.

Persistence is only the floor. For relationships, lifecycle, contexts, HTTP, Jobs, Events, AI,
storage, and agent projections that attach to the same type, start at
[Entity capability hooks](entities.md).

## You need

| Piece | Package | Note |
|---|---|---|
| Koan Entity semantics | arrives with the application or foundation bundle | model state as `Entity<T>` |
| One Entity store | topology table below | pick exactly one default unless the domain owns several |
| HTTP projection (optional) | `Sylin.Koan.Web` | one `EntityController<T>` reuses the same model |

## The constraint box

> **The constraint:** One Entity syntax does not promise provider parity. Filtering, paging,
> ordering, streaming, transactions, counts, and isolation vary by store. Verify every operation
> the application promises against the selected provider; a working `Save` proves only that a
> store answered.

## Choose by topology

| First fit | Package | Operating boundary |
|---|---|---|
| Disposable tests and ephemeral work | `Sylin.Koan.Data.Connector.InMemory` | disappears with the process |
| Small file-backed exploration | `Sylin.Koan.Data.Connector.Json` | file ownership, concurrency, and growth are yours |
| Durable local or single-node state | `Sylin.Koan.Data.Connector.Sqlite` | the usual zero-service default |
| Networked documents | `Sylin.Koan.Data.Connector.Mongo` | operate MongoDB and settle query/consistency semantics |
| Networked relational state | `Sylin.Koan.Data.Connector.Postgres` | operate PostgreSQL and own schema/connection policy |
| Existing MySQL estate | `Sylin.Koan.Data.Connector.MySql` (not assessed) | a reachable MySQL server is required |
| Microsoft relational estate | `Sylin.Koan.Data.Connector.SqlServer` | operate SQL Server and own schema/connection policy |
| Keyed state near Redis | `Sylin.Koan.Data.Connector.Redis` | query, stream, durability, and memory limits differ |
| Distributed documents and key/value state | `Sylin.Koan.Data.Connector.Couchbase` | bucket, query, consistency, and topology matter |
| Distributed PostgreSQL-shaped SQL | `Sylin.Koan.Data.Connector.Cockroach` | verify supported SQL behavior and latency |

## Leaves

- **Pasteable build:** [store and expose](../../recipes/store-and-expose.md) - template, package,
  `Entity<T>`, `Save`, query, `EntityController<T>`, and the three-part proof
- **Tested contract:** [Data reference](../../reference/data/index.md)
- **Provider lookup:** [Data capability map](../../reference/capability-map.md#data)

Adding a second store does not move existing data. If that is the outcome, continue to
[store cutover](store-cutover.md).
