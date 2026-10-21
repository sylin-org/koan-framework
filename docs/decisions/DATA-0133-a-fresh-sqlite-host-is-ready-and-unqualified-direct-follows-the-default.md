---
id: DATA-0133
slug: a-fresh-sqlite-host-is-ready-and-unqualified-direct-follows-the-default
domain: Data
status: Accepted
date: 2026-10-21
title: A fresh zero-config SQLite host is ready, and an unqualified Direct session follows the active default
related:
  - DATA-0111
  - DATA-0126
---

# DATA-0133: A fresh zero-config SQLite host is ready, and an unqualified Direct session follows the active default

## Context

- **Readiness (ledger NK-008).** The SQLite health probe treats a managed database that has not been written yet as
  awaiting provisioning, but only when the file's parent directory exists. The zero-config default is
  `.koan/data/Koan.sqlite` under the content root, and nothing but the first DDL creates `.koan/data`, so a fresh
  container reported the critical `data:sqlite` contributor Unhealthy until some write ran. If readiness gates traffic,
  no write arrives. Measured before this change.
- **Direct (NK-009).** DATA-0111 promises that an unqualified `Data.Direct()` follows the active logical default. An
  unqualified session bound the horizon with the plan resolved from the provider's physical connection, whose route
  identity hashes that connection; the default route authority issued its plan without one. Every Execute, Scalar,
  Query and Begin then threw `StaleDataRouteException` on hosts whose default has no
  `Koan:Data:Sources:{active}:ConnectionString`: zero-config SQLite and DuckDB, and hosts configured through
  `ConnectionStrings:Default` or a provider key. Measured before this change with `ConnectionStrings:Default`.

## Decision

1. **Readiness looks at the nearest existing ancestor.** A missing managed database file whose nearest existing
   ancestor is a directory is awaiting provisioning (provisioning creates every missing directory). An ancestor that is
   a file is still a fault and still fails. Observing never creates a directory.
2. **An unqualified Direct session binds the authority's current plan**, the plan Entity repositories bind, for its
   horizon lease. The plan resolved from the physical connection still answers source-policy `Demand`. Qualified
   sessions (`source:`, `adapter:`, `WithConnectionString`) are unchanged.

## Compatibility

- A fresh host is ready before its first write; a misconfigured path whose ancestor is a file still fails readiness.
- Unqualified `Direct()` works where it threw; it follows a committed cutover through the authority's plan.
- No public signature or stored format changes.

## Evidence

`SqliteHealthContributorSpec` (Koan.Data.Connector.Sqlite.Tests): a host whose database lives under a missing
`.koan/data` is Healthy before and after its first write and the probe creates no directory; the existing
directory-is-a-file spec still reports Unhealthy. An unqualified Direct scalar on a host configured only by
`ConnectionStrings:Default` returns 1. Both new specs failed before this change.
