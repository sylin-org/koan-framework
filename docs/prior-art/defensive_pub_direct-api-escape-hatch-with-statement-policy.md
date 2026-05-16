# Defensive Publication: Zero-Scaffold Direct Data Commands API with Statement Policy Governance

## Header Block

- **Title:** Zero-Scaffold Direct Data Commands API with Statement Policy Governance for Multi-Provider Application Frameworks
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Application framework data access infrastructure, specifically methods for executing ad-hoc database commands within a governed, multi-provider data access layer without requiring entity scaffolding.
- **Keywords:** direct SQL, escape hatch, statement policy, data governance, parameterized queries, source routing, adapter resolution, transaction management, production safety, multi-provider, zero-scaffold, ad-hoc commands

---

## 1. Problem Statement

Modern application frameworks that provide entity-based data access (such as ORM systems or the Entity-First pattern described in a related disclosure) necessarily constrain operations to the entity model. Developers frequently encounter scenarios requiring raw database operations — ad-hoc reporting queries, bulk data corrections, schema introspection, one-off migrations, or cross-database joins — that don't fit entity boundaries.

The conventional approach forces developers to step outside the framework entirely, obtaining raw database connections via `ADO.NET`, `JDBC`, or equivalent low-level APIs. This creates several problems. First, connection strings must be obtained independently, duplicating the framework's source routing logic. Second, no governance exists — the same connection can execute DDL statements, dropping tables in production. Third, audit and observability are lost — the operation happens outside the framework's telemetry pipeline. Fourth, transaction coordination becomes manual, requiring developers to manage connections and transactions across multiple data stores.

Existing solutions occupy two extremes. Lightweight libraries like Dapper provide raw SQL execution but no governance, routing, or audit. Full ORMs like Entity Framework provide `FromSqlRaw()` but tie operations to a `DbContext` and specific entity types, offering no standalone ad-hoc capability. Neither approach provides production-aware governance that prevents dangerous operations in specific environments.

What is needed is an escape hatch that participates in the framework's routing, governance, and observability infrastructure while allowing unconstrained query execution — a "governed raw access" pattern that is neither fully constrained (like entity operations) nor fully unconstrained (like raw ADO.NET).

---

## 2. Prior Art Summary

**ADO.NET / JDBC (Raw Database APIs):** Provide full SQL execution capability but offer no routing (developers must manage connection strings), no governance (any statement can execute in any environment), no audit integration, and no transaction coordination with framework-managed operations. The developer operates entirely outside the framework.

**Dapper (.NET):** Wraps ADO.NET with convenience methods (`Query<T>`, `Execute`) but requires manual connection management. No source routing — the caller must provide the connection. No governance — all statements are permitted. No integration with framework telemetry. No transaction coordination beyond manual `IDbTransaction` passing.

**Entity Framework Core `FromSqlRaw()`:** Allows raw SQL within a `DbContext` but the result must map to an entity or keyless query type. Cannot execute arbitrary statements (INSERT, UPDATE, DELETE, DDL) outside of entity operations. Tied to a specific `DbContext`, not standalone. No standalone session concept. No statement policy governance.

**Spring Data JDBC / JdbcTemplate:** Provides template-based raw SQL execution but tied to a specific `DataSource`. No multi-source routing. No statement policy governance. No integration with entity-level data access infrastructure.

**Specific gaps not addressed by prior art:**
1. No system provides governed raw SQL execution with configurable statement policies (SelectOnly, NoDdl, All) that vary by environment.
2. No system integrates raw SQL routing with an entity framework's source/adapter resolution chain.
3. No system provides a standalone fluent session builder for raw SQL with automatic rollback on dispose.
4. No system routes through adapter instruction executors when an entity hint is provided, preserving adapter-specific rewriting and safety features.

---

## 3. Detailed Description of the Invention

### 3.1 Architecture Overview

The invention introduces a `Direct` data access API that provides zero-scaffold SQL execution within a governed framework. The API consists of three components:

1. **IDirectDataService** — Factory that creates `DirectSession` instances with source/adapter routing
2. **DirectSession** — Fluent builder for individual queries with timeout, max-rows, and parameter binding
3. **DirectTransaction** — Full transaction lifecycle with automatic rollback on dispose

### 3.2 Source/Adapter Routing

The Direct API reuses the framework's existing source/adapter resolution:

```
Connection resolution priority (first match wins):
1. WithConnectionString() — explicit inline override
2. Source definition — adapter + connection string from DataSourceRegistry
3. Adapter routing — default connection for named adapter
4. "Default" source — fallback when no routing specified
```

This ensures that `data.Direct(source: "analytics")` resolves the same connection as entity operations targeting the "analytics" source. Source and adapter are mutually exclusive (same constraint as entity operations).

### 3.3 Statement Policy Governance

```
Configuration:
  Koan:Data:AllowDirectCommands = true | false (default: false)
  Koan:Data:DirectStatementPolicy = SelectOnly | NoDdl | All

StatementPolicy enum:
  SelectOnly — Only SELECT statements permitted
  NoDdl      — SELECT, INSERT, UPDATE, DELETE permitted; CREATE, ALTER, DROP blocked
  All        — No restrictions (development/testing only)
```

When `AllowDirectCommands` is false (the default), any attempt to create a DirectSession throws `InvalidOperationException`. This prevents accidental use in production unless explicitly opted in.

Statement policy validation occurs before execution. The framework performs lightweight statement classification (leading keyword analysis) to enforce the policy. This is a safety net, not a SQL parser — it blocks obvious violations while allowing the adapter's own safety mechanisms to handle edge cases.

### 3.4 Fluent Session API

```
// Query with typed result
var reports = await data.Direct(source: "analytics")
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithMaxRows(1000)
    .Query<SalesReport>(
        "SELECT region, total FROM reports WHERE year = @year",
        new { year = 2025 });

// Scalar
var count = await data.Direct(adapter: "postgres")
    .Scalar<int>("SELECT COUNT(*) FROM products WHERE active = @active",
        new { active = true });

// Execute (returns rows affected)
var affected = await data.Direct(source: "default")
    .Execute("UPDATE products SET price = price * @factor WHERE category = @cat",
        new { factor = 1.1, cat = "electronics" });

// Raw result (Dictionary<string, object?>)
var rows = await data.Direct(source: "reporting")
    .Query("SELECT * FROM audit_log WHERE ts > @since",
        new { since = DateTime.UtcNow.AddDays(-7) });
```

### 3.5 Parameter Binding

All parameters must be passed via parameter objects — string interpolation is explicitly not supported. The framework normalizes parameter prefixes (adding "@" if missing). Parameter values are redacted in logs (only parameter names appear).

### 3.6 Entity-Hint Routing

When the source identifier starts with `"entity:"`, the Direct API delegates to the adapter's instruction executor rather than raw ADO.NET:

```
var results = await data.Direct(source: "entity:Product")
    .Query<ProductSummary>("...");
// Routes through adapter's instruction executor
// Preserves adapter rewriting, safety features, observability
```

This allows leveraging adapter-specific optimizations (query rewriting, dialect translation) while still executing arbitrary queries.

### 3.7 Transaction Support

```
await using var tx = await data.Direct(adapter: "postgres").BeginTransaction();
await tx.Execute("INSERT INTO audit_log ...", new { ... });
await tx.Execute("UPDATE accounts ...", new { ... });
await tx.Commit();
// If Commit() not called before Dispose: automatic Rollback()
```

The transaction wraps an underlying database transaction. On `Dispose()` without `Commit()`, an automatic `Rollback()` is executed. This prevents accidental partial commits.

### 3.8 Default Limits

```
DirectOptions:
  TimeoutSeconds = 30 (default, configurable per-session)
  MaxRows = 10_000 (default, configurable per-session)
```

These prevent runaway queries from consuming excessive resources.

### 3.9 Observability Integration

All Direct API operations emit:
- Structured log events with statement classification, source/adapter routing, duration, row count
- OpenTelemetry spans when telemetry is enabled
- Parameter names (but not values) in log output

---

## 4. Claims-Style Disclosure

1. A method for providing governed ad-hoc database command execution within a multi-provider application framework, wherein a `Direct` session factory resolves connection routing through the same source/adapter priority chain used for entity operations, distinct from raw database APIs in that the routing is inherited from framework configuration rather than manually specified.

2. A statement policy governance mechanism wherein ad-hoc database commands are classified by leading keyword and validated against a configurable `StatementPolicy` (SelectOnly, NoDdl, All) before execution, with the entire Direct API disabled by default in production environments, distinct from raw SQL APIs which provide no execution-time governance.

3. A fluent session builder for ad-hoc database commands that provides chainable configuration (timeout, max rows, parameter objects) with automatic parameter prefix normalization and value redaction in logs, distinct from template-based SQL APIs in that the session carries routing context from the factory.

4. A transaction management pattern for ad-hoc database commands wherein a `DirectTransaction` object wraps a database transaction and automatically executes `Rollback()` on `Dispose()` if `Commit()` was not explicitly called, distinct from manual transaction management in that the rollback is guaranteed by the `IAsyncDisposable` contract.

5. An entity-hint routing mechanism wherein Direct API calls with a source identifier prefixed with `"entity:"` delegate execution to the adapter's instruction executor rather than raw database APIs, preserving adapter-specific query rewriting, dialect translation, and safety features, distinct from raw SQL APIs which bypass adapter logic entirely.

6. A connection resolution chain for ad-hoc commands that follows four precedence levels (explicit connection string > source definition > adapter routing > default source), reusing the framework's `DataSourceRegistry` without duplication, distinct from connection factory patterns that maintain separate connection pools for raw and entity operations.

7. A default resource limiting mechanism wherein ad-hoc commands are constrained by configurable `MaxRows` and `TimeoutSeconds` defaults that apply unless explicitly overridden per-session, preventing unbounded resource consumption from ad-hoc queries.

8. An observability integration pattern wherein ad-hoc database commands emit the same structured telemetry (OpenTelemetry spans, structured log events) as entity operations, with parameter values redacted from logs while parameter names are preserved for debugging.

---

## 5. Implementation Evidence

- **Interface:** `IDirectDataService` in `src/Koan.Data.Core/Direct/IDirectDataService.cs`
- **Session:** `DirectSession` in `src/Koan.Data.Direct/DirectSession.cs`
- **Transaction:** `DirectTransaction` in `src/Koan.Data.Direct/DirectTransaction.cs`
- **Registration:** `DirectRegistration` in `src/Koan.Data.Direct/DirectRegistration.cs`
- **Options:** `DirectOptions` in `src/Koan.Data.Core/Options/DirectOptions.cs`
- **ADR:** `docs/decisions/DATA-0049-direct-commands-api.md`
- **Related ADR:** `docs/decisions/DATA-0051-direct-routing-via-adapter-instruction-executors.md`
- **Framework Version:** Koan Framework v0.6.3
- **Build Target:** net10.0

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** The statement policy governance (Claim 2) is trivially a whitelist/blacklist on SQL keywords — any production database already has this via role-based permissions (GRANT/REVOKE). The framework-level governance adds nothing over database-level security.

**Author revision:** Added clarification that framework-level governance operates at the application layer, complementing but not replacing database-level permissions. The distinction is: database permissions are per-user/role and apply uniformly; framework governance is per-environment (Development vs. Production) and per-policy (the same user can have different policies in different environments). The leading-keyword classification is explicitly described as a "safety net" rather than a security boundary. The inventive step is the integration of environment-aware governance into the data access API itself, not the keyword classification algorithm.

### Pass 2
**Antagonist:** The automatic rollback on dispose (Claim 4) is standard `IAsyncDisposable` pattern used by every database library. `SqlTransaction.Dispose()` already rolls back uncommitted transactions. This is not novel.

**Author revision:** Acknowledged that individual rollback-on-dispose is standard. The novelty is the complete `DirectTransaction` contract within the governed framework context: the transaction inherits source routing, respects statement policy, emits framework telemetry, and coordinates with entity-level operations. The rollback guarantee is a property of the complete system, not a standalone invention.

### Pass 3
**Antagonist:** The entity-hint routing (Claim 5) is just a conditional dispatch — if source starts with "entity:", use one code path, otherwise use another. This is trivial branching.

**Author revision:** Refined the description to emphasize that entity-hint routing preserves the adapter's instruction execution pipeline, including dialect-specific query rewriting, parameterization safety, connection pooling, and observability. The non-obvious aspect is that a raw SQL escape hatch can selectively opt back into adapter infrastructure — most frameworks draw a hard boundary between "framework-managed" and "raw" operations. The bidirectional permeability of this boundary is the inventive contribution.

### Pass 4
**Antagonist:** No further objections. The disclosure adequately describes the governance integration, routing reuse, and bidirectional adapter permeability as a combined system. Individual components are known; the combination within a governed escape hatch is sufficiently described to establish prior art.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
