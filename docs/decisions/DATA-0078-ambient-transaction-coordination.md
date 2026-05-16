---
id: DATA-0078
slug: DATA-0078-ambient-transaction-coordination
domain: DATA
status: Accepted
date: 2025-11-07
title: Ambient transaction coordination via EntityContext
---

# ADR 0078: Ambient transaction coordination via EntityContext

## Context

Entity operations (Save, Delete, UpsertMany) currently execute immediately and independently. This creates challenges for scenarios requiring atomic coordination:

1. **Dual-store consistency**: Services like Koan.Context need to coordinate SQLite metadata with Weaviate vectors
2. **Cross-adapter business logic**: Applications may need to save to multiple databases atomically
3. **Batch operations**: Multiple entity changes should succeed or fail together

Current workarounds are manual and error-prone:

- Direct SQL transactions (adapter-specific, breaks abstraction)
- Manual compensation logic (complex, easy to get wrong)
- No framework support for cross-adapter coordination

## Requirements

1. **Minimal cognitive load**: Should be easy to use correctly, hard to use incorrectly
2. **Composable**: Work seamlessly with existing source/adapter/partition routing
3. **Observable**: Named transactions for correlation, telemetry, debugging
4. **Non-breaking**: Existing code continues to work without changes
5. **Opt-in complexity**: Simple cases simple, complex cases possible
6. **Provider-agnostic**: Work with any adapter supporting local transactions
7. **Best-effort atomicity**: Coordinate across adapters where possible, clear about limitations

## Decision

We will extend `EntityContext.With()` to support transaction coordination via an optional `transaction` parameter.

### API Design

```csharp
// Extend EntityContext
public static class EntityContext
{
    public static IDisposable With(
        string? source = null,
        string? adapter = null,
        string? partition = null,
        string? transaction = null);  // NEW

    // Convenience method
    public static IDisposable Transaction(string name);

    // Transaction control
    public static Task CommitAsync(CancellationToken ct = default);
    public static Task RollbackAsync(CancellationToken ct = default);

    // State queries
    public static bool InTransaction { get; }
    public static TransactionCapabilities? Capabilities { get; }
}
```

### Behavior

**Deferred Execution**: When `EntityContext.Transaction()` is active:

- `Entity<T>.Save()` tracks operation instead of executing immediately
- `Entity<T>.Delete()` tracks operation instead of executing immediately
- Operations grouped by adapter and executed in adapter-local transactions

**Auto-Commit**: Transaction commits automatically on successful dispose

- Zero cognitive load for happy path
- No explicit commit needed (but allowed for clarity)

**Auto-Rollback**: Transaction rolls back on:

- Explicit `EntityContext.RollbackAsync()` call
- Unhandled exception (dispose without commit)
- Dispose without commit or rollback

**Atomicity Guarantees**:

- **Within adapter**: Full ACID via local database transactions
- **Across adapters**: Best-effort coordination (not true distributed transactions)
- **Failure window**: Between adapter commits (if adapter A commits, adapter B fails)

### Usage Patterns

#### Pattern 1: Auto-commit (recommended)

```csharp
using (EntityContext.Transaction("save-project"))
{
    await project.Save(ct);
    await job.Save(ct);
    // Auto-commit on dispose
}
```

#### Pattern 2: Explicit commit (clarity)

```csharp
using (EntityContext.Transaction("save-project"))
{
    await project.Save(ct);
    await job.Save(ct);
    await EntityContext.CommitAsync(ct);  // Explicit
}
```

#### Pattern 3: Conditional rollback

```csharp
using (EntityContext.Transaction("save-project"))
{
    await project.Save(ct);

    if (!IsValid(project))
    {
        await EntityContext.RollbackAsync(ct);
        return Result.ValidationFailed();
    }
    // Auto-commit if not rolled back
}
```

#### Pattern 4: Cross-adapter coordination

```csharp
using (EntityContext.Transaction("cross-db"))
{
    // Save to SQLite
    await entity1.Save(ct);

    // Save to SQL Server
    using (EntityContext.Source("SqlServer"))
    {
        await entity2.Save(ct);
    }
    // Both adapters commit atomically (best-effort)
}
```

#### Pattern 5: Composition with partition

```csharp
using (EntityContext.Transaction("index-batch"))
using (EntityContext.Partition(projectId))
{
    foreach (var chunk in chunks)
    {
        await chunk.Save(ct);
    }
    // Auto-commit
}
```

### Transaction Coordinator

Transactions are managed by `ITransactionCoordinator`:

```csharp
public interface ITransactionCoordinator
{
    string Name { get; }
    bool IsCompleted { get; }

    void Track<TEntity, TKey>(
        TEntity entity,
        OperationType operation,
        ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);

    TransactionCapabilities GetCapabilities();
}
```

**Operation Tracking**:

- Operations grouped by adapter
- Each adapter gets a local transaction (Direct API)
- Operations executed in order within adapter
- All adapters commit if all succeed

**Compensation**: For adapters that don't support transactions (vectors):

- Execute immediately (not tracked)
- Provide cleanup delegates for compensation
- Framework logs compensation attempts

### Capabilities

Query transaction capabilities at runtime:

```csharp
using (EntityContext.Transaction("check-support"))
{
    var caps = EntityContext.Capabilities;

    // Check what's supported
    if (caps.SupportsLocalTransactions)
    {
        // All adapters support local transactions
    }

    if (caps.RequiresCompensation)
    {
        // Some operations need compensation (e.g., vectors)
    }

    // See which adapters are participating
    foreach (var adapter in caps.Adapters)
    {
        _logger.LogInformation("Adapter {Adapter} in transaction", adapter);
    }
}
```

### Constraints

**Nested transactions**: Not supported, throws clear error

```csharp
using (EntityContext.Transaction("outer"))
{
    using (EntityContext.Transaction("inner"))  // ❌ Throws InvalidOperationException
    {
        // Not allowed
    }
}
```

**Mixed immediate/deferred**: Operations outside transaction execute immediately

```csharp
await entity1.Save(ct);  // Immediate execution

using (EntityContext.Transaction("batch"))
{
    await entity2.Save(ct);  // Tracked (deferred)
    await entity3.Save(ct);  // Tracked (deferred)
}  // Both execute and commit atomically
```

**Infrastructure operations**: These operations always execute immediately (not tracked):

- `RemoveAll(RemoveStrategy.Fast)` - Bulk operations
- Schema operations (`EnsureHealthyAsync`)
- Vector operations (no transaction support in most vector DBs)

## Registration

Transaction support is opt-in:

```csharp
// Enable transactions
builder.Services.AddKoanTransactions(options =>
{
    options.DefaultTimeout = TimeSpan.FromMinutes(2);
    options.AutoCommitOnDispose = true;  // Default
    options.EnableTelemetry = true;
    options.MaxTrackedOperations = 10_000;
});
```

## Telemetry

All transaction operations emit structured logs and ActivitySource spans:

```csharp
// ActivitySource spans
Activity: Koan.Data.Transaction
  - transaction.name: "save-project"
  - transaction.adapters: ["sqlite", "sqlserver"]
  - transaction.operation_count: 5
  - transaction.outcome: "committed" | "rolled_back"
  - transaction.duration_ms: 42

// Structured logs
[INF] Transaction 'save-project' started (correlation_id: abc123)
[INF] Transaction 'save-project' tracking 3 operations on adapter 'sqlite'
[INF] Transaction 'save-project' tracking 2 operations on adapter 'sqlserver'
[INF] Transaction 'save-project' committed successfully in 42ms
```

## Implementation Strategy

### Phase 1: Core Infrastructure (Week 1)

- Update `EntityContext` with transaction parameter
- Implement `ITransactionCoordinator` interface
- Implement `TransactionCoordinator` with operation tracking
- Create `TransactionScope` disposal logic

### Phase 2: Entity Integration (Week 2)

- Update `Data<T,K>.UpsertAsync` to check for active transaction
- Update `Data<T,K>.DeleteAsync` to check for active transaction
- Implement operation tracking and grouping by adapter
- Handle partition context in tracked operations

### Phase 3: Execution & Coordination (Week 3)

- Integrate with Direct API for adapter-local transactions
- Implement commit coordination (all adapters)
- Implement rollback coordination
- Add compensation pattern for non-transactional adapters

### Phase 4: Testing & Observability (Week 4)

- Unit tests (operation tracking, commit/rollback)
- Integration tests (SQLite, SQL Server, cross-adapter)
- ActivitySource spans and structured logging
- Performance benchmarks

### Phase 5: Documentation & Samples (Week 5)

- API documentation
- Usage guides
- Migration examples
- Best practices guide

## Consequences

### Positive

- ✅ **Minimal API surface**: Extends existing `EntityContext.With()` pattern
- ✅ **Zero cognitive load**: Auto-commit on dispose for happy path
- ✅ **Great observability**: Named transactions, telemetry built-in
- ✅ **Composable**: Works with source/adapter/partition routing
- ✅ **Non-breaking**: Existing code unchanged
- ✅ **Provider-agnostic**: Works with any adapter supporting local transactions
- ✅ **Clear semantics**: Explicit about best-effort coordination

### Negative

- ⚠️ **Not true distributed transactions**: Window between adapter commits
- ⚠️ **Deferred execution**: Operations don't persist until commit (may surprise developers)
- ⚠️ **Memory overhead**: Tracked operations held in memory until commit
- ⚠️ **Complexity**: New failure modes (commit failures, partial success)
- ⚠️ **Testing burden**: More test scenarios (happy path, rollback, compensation)

### Mitigations

- **Document limitations clearly**: Not ACID across adapters
- **Provide capability detection**: Query what's supported at runtime
- **Add guardrails**: Max tracked operations, timeouts, deadlock detection
- **Comprehensive logging**: All transaction events logged for debugging
- **Clear error messages**: Helpful guidance when things go wrong

## Alternatives Considered

### Alternative 1: Separate UnitOfWork API

```csharp
using (var uow = UnitOfWork.Begin())
{
    await entity.Save(ct);
    await uow.Commit(ct);
}
```

**Rejected**: Introduces new top-level API, doesn't compose with EntityContext

### Alternative 2: Explicit transaction objects

```csharp
var tx = Transaction.Begin();
await entity.Save(tx, ct);
await tx.Commit(ct);
```

**Rejected**: Changes all Save() signatures, breaking change

### Alternative 3: Two-Phase Commit (XA/DTC)

**Rejected**: SQLite doesn't support XA, requires enterprise infrastructure

### Alternative 4: Saga Pattern

**Rejected**: Too complex for framework-level feature, eventually consistent

## References

- DATA-0049: Direct commands API (provides transaction infrastructure)
- DATA-0077: EntityContext routing (ambient context pattern)
- DATA-0007: Transactional batch semantics (related proposal)

## Decision Makers

- Framework Architect
- Data Team Lead
- Koan.Context Team

## Status History

- 2025-11-07: Proposed
- 2025-11-07: Accepted
