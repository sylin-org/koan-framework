---
id: DATA-0006
slug: DATA-0006-instruction-execution-api
domain: DATA
status: Accepted
date: 2025-08-16
---

# 0006: Instruction Execution API (Entity.Execute / Adapter passthrough)

 

## Context

- Developers sometimes need provider-specific capabilities not covered by the high-level repository API (e.g., raw SQL, stored procedures, custom indexes, maintenance jobs).
- The request is to enable `Entity.Execute("instructions")`, passing operations down to the data adapter in a controlled, typed way, without breaking provider-agnostic ergonomics.
- We must keep core POCO entities DI-free and avoid tight coupling while offering a safe, discoverable escape hatch.

## Decision (proposed)

Introduce a small “instruction” abstraction and an optional capability interface that adapters can implement. Provide an execution shim on the DataService for convenience. Entity-level “Execute” is offered as an extension that simply forwards to the DataService.

- Abstractions (agnostic):
  - Instruction envelope
    - Name: string (namespaced, e.g. "relational.sql.scalar", "relational.schema.ensureCreated", "document.index.create", "vector.search")
    - Payload: object? (provider-specific or model-specific DTO)
    - Parameters: IReadOnlyDictionary<string, object?>? (for parameterized execution)
    - Options: IReadOnlyDictionary<string, object?>? (timeouts, consistency, transaction hints)
  - Optional capability on repositories (duck-typed):
    - IInstructionExecutor<TEntity>
      - Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
- Service shim:
  - IDataService extension: Execute<TEntity,TResult>(Instruction, CancellationToken)
    - Resolves repository for TEntity and forwards to IInstructionExecutor if implemented; otherwise NotSupportedException.
- Entity-level convenience:
  - Static/extension helper to call through the DataService (no DI on the entity itself). Example: `await data.Execute<Todo,int>(Instruction.Sql.NonQuery("DELETE FROM ... WHERE ...", params));`

This preserves SoC: the entity stays a POCO; the execution capability is optional and discoverable at runtime.

## Example usage

- Relational (SQLite shown, similar for others):
  - NonQuery
    - Name: "relational.sql.nonquery"
    - Payload: { Sql: string }
    - Parameters: { ... } → provider uses parameterization (Dapper) to avoid injection
    - Result: int (affected rows)
  - Scalar
    - Name: "relational.sql.scalar"
    - Payload: { Sql: string }
    - Result: T (scalar)
  - Query (into JSON rows)
    - Name: "relational.sql.query"
    - Payload: { Sql: string }
    - Result: string (JSON array) or IEnumerable<dynamic>
  - Schema ensure
    - Name: "relational.schema.ensureCreated"
    - Payload: null (adapter infers from TEntity via relational toolkit)
    - Result: bool (true if changed)

### Sugar APIs

- Data service helper:
  - `await data.Execute<Todo,int>(InstructionSql.NonQuery("INSERT ...", new { ... }));`
  - `var count = await data.Execute<Todo,long>(InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));`

- Static facades (require AppHost.Current in app context):
  - `await Data<Todo>.Execute<int>("INSERT ..."); // NonQuery by default for int`
  - `var count = await Data<Todo>.Execute<long>("SELECT COUNT(*) FROM Todo"); // Scalar`

- Test-local minimal sugar (optional):
  - `await Todo.Execute("INSERT ...", data, new { id = "1" }); // fire-and-forget NonQuery`

- Document stores (future adapters):
  - Name: "document.index.create" with payload describing index keys/options
  - Name: "document.aggregate" with payload holding pipeline stages

- Vector databases (future adapters):
  - Name: "vector.index.create" with payload (dimensions, metric, lists)
  - Name: "vector.search" with payload (query vector, k, filters) → returns top-k with scores

## Contract sketch

- Abstractions (Sora.Data.Abstractions)
  - record Instruction(string Name, object? Payload = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    IReadOnlyDictionary<string, object?>? Options = null);
  - interface IInstructionExecutor<TEntity>
    where TEntity : class
    {
      Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default);
    }
  - static class DataServiceExecuteExtensions
    {
      public static Task<TResult> Execute<TEntity, TResult>(this IDataService data, Instruction instruction, CancellationToken ct = default) where TEntity : class;
    }

- Provider opt-in (e.g., Sora.Data.Sqlite)
  - SqliteRepository<TEntity,TKey> : IInstructionExecutor<TEntity>
    - Supports relational.* instruction names; throws for unknown names.

## Safety and ergonomics

- Parameterization: Instruction exposes a Parameters dictionary that adapters must bind to provider parameters (e.g., Dapper). No string interpolation by default.
- Capability detection: Use `as IInstructionExecutor<TEntity>` or a `Supports(Instruction)` helper to check support.
- Testability: Treat an Instruction as a value object; unit test per provider/operation. Keep names stable and versioned.
- Backward compatibility: Optional interface; no breaking changes to existing adapters unless they opt in.

## Alternatives considered

- Adding Execute to IDataRepository directly: Simple but forces all adapters to implement or throw; rejected in favor of optional capability.
- Entity instance method with hidden static service locator: Convenience, but introduces hidden DI; rejected. We keep entity methods as extensions that require explicit `IDataService`.
- "Scripting" strings for all: Too untyped and error-prone; rejected in favor of a structured Instruction envelope with namespaced intents and parameters.

## Migration and rollout

- Phase 1 (relational/SQLite):
  - Add abstractions to Sora.Data.Abstractions and extension in Sora.Data.Core.
  - Implement minimal handlers in Sqlite: nonquery, scalar, schema.ensureCreated.
  - Add tests.
- Phase 2 (document/vector):
  - Define canonical instruction names and DTO payloads for common operations.
  - Implement in respective providers when they land.

## Open questions

- Return typing: We allow TResult generics; consider a `ResultEnvelope` for richer status/metrics.
- Transactions: add optional instruction-scoped transaction hints vs separate transaction API.
- Authorization: surface policy hooks in higher layers to gate dangerous instructions.

## Summary

A minimal, optional instruction execution capability provides a safe, typed escape hatch to provider-specific features. It maintains POCO purity, keeps adapters in control, and scales across relational, document, and vector models via namespaced instruction contracts.
