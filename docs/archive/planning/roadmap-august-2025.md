# Roadmap — August 2025

Accepted next steps (code-first, testable):

1) String query integration tests (SQLite)
- Cases: WHERE suffix, full SELECT, parameter binding, empty/large results, cancellation.
- Acceptance: Tests passing on CI; no SQL injection via string interpolation.

2) Cancellation tokens end-to-end
- Ensure `CancellationToken` flows through repositories, batch, and instruction execution.
- Acceptance: A test cancels mid-query/batch and asserts TaskCanceledException.

3) Capability matrix endpoint (Koan.Web)
- GET `/.well-known/Koan/capabilities` returns aggregates, key type, default provider, and query/write capability flags.
- Acceptance: Unit test for shape; manual smoke via sample S1.

4) Transactional batch semantics
- Implement `RequireAtomic` in SQLite (transaction) and best-effort in JSON; surface NotSupported when required.
- Acceptance: Tests for atomic all-or-nothing vs best-effort per-item errors.

5) Optimistic concurrency (optional)
- Add `[ConcurrencyToken]` attribute; implement in SQLite and JSON.
- Acceptance: Conflict test on mismatched token; success increments/updates token.

6) Relational schema sync upgrades
- FK constraints, unique indexes, default values mapping; DDL sync.
- Acceptance: Schema sync tests create and update tables accordingly.

7) LINQ/Filter bridge (scoped)
- Minimal predicate support (equality/range/LIKE) for relational providers.
- Acceptance: Tests translate simple expressions to SQL and execute.

8) Postgres adapter scaffold (Dapper)
- Discovery, priority, string query, instructions, schema ensure.
- Acceptance: Testcontainers-backed integration tests (CI opt-in).

9) Benchmarks and telemetry
- BenchmarkDotNet micro-benchmarks; OpenTelemetry spans around repo ops.
- Acceptance: Bench results checked in; OTEL spans visible in sample when enabled.
