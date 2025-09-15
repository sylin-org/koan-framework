---
id: DATA-0046
slug: DATA-0046-sqlite-schema-governance-ddl-policy
domain: DATA
status: Proposed
date: 2025-08-19
title: SQLite schema governance (DDL policy, matching mode, and magic gating)
---

# ADR 0046: SQLite schema governance (DDL policy, matching mode, and magic gating)

## Context

We introduced a projection-by-default storage model for SQLite: base table `[Id, Json]` with generated columns for projected scalar properties and optional indexes. We also need explicit governance for schema creation/changes to avoid accidental DDL in production or on read-only aggregates.

## Decision

- Add `DdlPolicy` with values:
  - `AutoCreate` (default): create base table and generated columns/indexes on demand.
  - `Validate`: do not create or alter. Only check existence; callers will fail on missing tables.
  - `NoDdl`: same as Validate; no create/alter attempts.
- Add `SchemaMatchingMode` with values:
  - `Relaxed` (default): tolerate model/table drift and report Degraded health (future work).
  - `Strict`: treat drift as Unhealthy (future work).
- Introduce `ReadOnlyAttribute` on aggregates. If present, adapters must not attempt DDL regardless of policy.
- Production safety: only allow DDL in Production when the global `Koan:AllowMagicInProduction` flag is set (or adapter option `AllowProductionDdl` is true).

## Implementation

- `SqliteOptions` now includes `DdlPolicy`, `SchemaMatching`, and `AllowProductionDdl`.
- `SqliteOptionsConfigurator` reads `Koan:Data:Sqlite:DdlPolicy` and `:SchemaMatchingMode` (alt keys supported) and the magic flag.
- `SqliteRepository.EnsureTable(SqliteConnection)` gates all CREATE/ALTER operations according to:
  - Policy must be `AutoCreate`.
  - Entity must not be annotated with `[ReadOnly]`.
  - If `KoanEnv.IsProduction` then either `KoanEnv.AllowMagicInProduction` or `AllowProductionDdl` must be true.
  - Otherwise, if table exists, return; if missing, no-op (callers may receive runtime errors until provisioned out-of-band).
- Instruction helpers (`data.ensureCreated`, `relational.schema.ensureCreated`, and SQL helpers) call `TryEnsureTableWithGovernance` rather than unconditional `EnsureTable`.

## Consequences

- Safer defaults: development stays ergonomic; production requires an explicit flag to permit DDL.
- Read-only aggregates never mutate schema.
- Existing tests remain green; future health contributor work will surface drift per `SchemaMatchingMode`.

## Follow-ups

- Extend the SQLite health contributor to detect missing tables/columns and report Degraded vs Unhealthy based on `SchemaMatchingMode` and policy.
- Document configuration keys in the adapter reference and engineering guardrails.
