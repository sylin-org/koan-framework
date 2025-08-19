---
id: DATA-0049
slug: DATA-0049-direct-commands-api-legacy
domain: DATA
status: Accepted
date: 2025-08-19
title: Direct data commands API (escape hatch)
---

# ADR 0049: Direct data commands API (escape hatch)

## Context

Users often have credentials to a database but no predefined schema/entities. They need a zero-scaffold, developer-friendly way to run ad-hoc queries/commands against a named source or adapter. This should be simple, safe-by-default, observable, and consistent across adapters.

## Decision

We will introduce a "Direct" data API as an escape hatch, built on each adapter's instruction executor, with the following design:

- Async-only, no Async suffix in method names (terse API).
- Mapping is relaxed: resultset → JSON → type using Newtonsoft.Json.
- Connection resolution: WithConnectionString() accepts a name or a raw connection string; names resolve from IConfiguration first, then from the internal adapter/source registry.
- Transactions use a fluent session with Begin()/Commit() (Dispose or missing Commit() rolls back).
- Parameterization is required; logs redact parameter values.
- Production guardrails and limits are enabled by default.

### API shape (conceptual)

- Entry
  - `data.Direct(sourceOrAdapter)`
  - `.WithConnectionString(value)` // value can be a name or raw string
  - `.WithTimeout(TimeSpan)`, `.WithMaxRows(int)`, `.WithTag(string)`
  - `.Begin()` → transactional session with `.Commit()` / `.Rollback()`

- Commands (all methods are async; no Async suffix in names)
  - `Query(sql, params?, ct)` → `IReadOnlyList<object>` (JToken-backed)
  - `Query<T>(sql, params?, ct)` → `IReadOnlyList<T>` (relaxed JSON mapping)
  - `Scalar<T>(sql, params?, ct)` → `T?`
  - `Execute(sql, params?, ct)` → `int`

- Stored procedures (phase 1 via Execute/Query with CommandType; richer Call() later)

### Mapping (Newtonsoft.Json)

- Per-row materialization to `JObject` (columnName → JToken); then `JsonConvert.DeserializeObject<T>()` with relaxed settings:
  - Case-insensitive, ignore unknown, include nulls.
  - `DateParseHandling = DateTimeOffset`, `FloatParseHandling = Decimal`, InvariantCulture.
  - Common converters: `StringEnumConverter`, ISO DateTime, Guid, TimeSpan, byte[].
- Columns containing JSON (text/jsonb) are parsed and included as nested tokens.
- `Query<object>` returns JSON-native shapes (JObject/JArray) to avoid double mapping.

### WithConnectionString() resolution order

1) IConfiguration (first win):
   - `ConnectionStrings:{name}`
   - `Sora:Data:Sources:{name}:ConnectionString`
2) Internal registry:
   - If `name` matches a registered source/bundle, use it.
   - If `name` matches an adapter moniker (e.g., `postgres`, `sqlserver`, `sqlite`, `mongodb`), use adapter defaults (may still require a raw connection string).
3) Otherwise treat `value` as a raw connection string.

Notes:
- Prefer named connections in production; redact raw secrets in logs and warn once per process when raw strings are used.

### Transactions

- `var tx = data.Direct("sqlserver").WithConnectionString(cs).Begin();`
- `await tx.Execute("DELETE FROM T WHERE Id=@id", new { id = 1 }, ct);`
- `await tx.Commit();` // Dispose without Commit() → rollback automatically.
- `Rollback()` available for explicit control.

### Governance and safety

- Disabled by default in production:
  - `Sora:Data:AllowDirectCommands = false` (must be set true to enable).
- Optional policy:
  - `Sora:Data:Direct:StatementPolicy = SelectOnly | NoDdl | All` (default All in dev; default NoDdl in prod unless overridden).
- Limits:
  - `Sora:Data:Direct:DefaultMaxRows` (e.g., 10_000), `DefaultTimeoutSeconds` (e.g., 30), overrideable per call.
- Parameters only; interpolation discouraged. All logs redact parameter values; statements truncated and hashed.

### Observability

- ActivitySource spans around each call with tags: `db.system`, `sora.source`, `sora.adapter`, statement kind, rows returned/affected, duration.
- Structured logs with redacted parameters and statement hashes for correlation.

### Rollout plan

- Phase 1: Relational adapters (SqlServer, Postgres, Sqlite) with Query/Scalar/Execute and transactions.
- Phase 2: MongoDB (command/aggregate wrappers), typed mapping preserved via JSON normalization.
- Phase 3: Streaming (IAsyncEnumerable<T>) and multi-result (QueryMultiple).
- Phase 4: Statement policy/limits and audit improvements.
- Phase 5: Docs and samples with security guidance and production enablement checklist.

## Consequences

- Simple, zero-scaffold path for reports/ops.
- Slight perf cost from JSON hop, offset by better tolerance and consistency.
- Clear guardrails reduce risk in production.
- Built on instruction executors, avoiding duplication per adapter.

## Alternatives considered

- System.Text.Json for mapping: lighter but less forgiving for this use-case.
- Sync/async dual API: rejected to keep the surface terse and consistent.
- Transactions hidden inside each call: rejected for lack of control; explicit Begin/Commit preferred.
