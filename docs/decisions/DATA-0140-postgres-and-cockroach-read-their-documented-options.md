---
id: DATA-0140
slug: postgres-and-cockroach-read-their-documented-options
domain: Data
status: Accepted
date: 2026-10-21
title: PostgreSQL and CockroachDB read their documented provider options
related:
  - DATA-0133
---

# DATA-0140: PostgreSQL and CockroachDB read their documented provider options

## Context

The PostgreSQL TECHNICAL.md documents `Koan:Data:Postgres:NamingStyle`, `Separator`, `DdlPolicy`, `SchemaMatchingMode`
and `AllowProductionDdl`, and the CockroachDB documents the same keys under `Koan:Data:Cockroach`. Both modules
registered their options with `AddKoanOptions<T>()` and no section, and the configurators set only the connection
string and readiness, so every documented key was ignored: `DdlPolicy=NoDdl` still issued DDL, and
`AllowProductionDdl=true` could not unlock Production DDL although the refusal text tells the operator to set it. MySQL
binds its section; SQLite reads the keys explicitly. Measured before this change: none of the keys reached
`IOptions<PostgresOptions>` or `IOptions<CockroachOptions>`. Ledger NK-031.

## Decision

Both modules bind their provider section (`AddKoanOptions<T>(Constants.Configuration.Section)`), as MySQL does, so every
option property is configurable by its property name. The configurators also honour the documented
`SchemaMatchingMode` spelling, which wins over `SchemaMatching` when both are set. The factories already copy these
options into every repository.

## Compatibility

Behavioural: configuration that was silently ignored now applies. An application that set `DdlPolicy=NoDdl` or
`Validate` stops auto-provisioning; one that set `AllowProductionDdl=true` provisions in Production; `NamingStyle` and
`Separator` change storage names where set, which addresses different tables than before. Unset keys keep the
defaults. No public signature changes.

## Evidence

`PostgresOptionsBindingSpec` and `CockroachOptionsBindingSpec` (Data.Relational, no server): the documented keys and the
property spelling reach the options, and unset keys keep the safe defaults. The binding cases failed before this change.
