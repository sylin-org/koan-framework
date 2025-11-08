# Transaction Support Usage Guide

Koan Framework provides ambient transaction support for coordinating entity operations across multiple adapters. This guide demonstrates practical usage patterns.

## Table of Contents

1. [Basic Concepts](#basic-concepts)
2. [Simple Transactions](#simple-transactions)
3. [Cross-Adapter Transactions](#cross-adapter-transactions)
4. [Error Handling](#error-handling)
5. [Advanced Patterns](#advanced-patterns)

---

## Basic Concepts

### Transaction Lifecycle

Transactions in Koan Framework follow this lifecycle:

1. **Start**: Begin transaction using `EntityContext.Transaction(name)`
2. **Track**: Entity operations (`Save()`, `Remove()`) are tracked, not executed immediately
3. **Commit/Rollback**: Execute or discard all tracked operations
4. **Auto-Commit**: Transactions auto-commit on dispose unless explicitly rolled back

### Key Properties

```csharp
// Check if currently in transaction
bool inTransaction = EntityContext.InTransaction;

// Get current transaction name
string? transactionName = EntityContext.Current?.Transaction;

// Get transaction capabilities
TransactionCapabilities? capabilities = EntityContext.Capabilities;
```

---

## Simple Transactions

### Pattern 1: Auto-Commit (Recommended)

The simplest pattern - transaction commits automatically on dispose:

```csharp
using (EntityContext.Transaction("save-project"))
{
    var project = new Project { Name = "My Project", Description = "..." };
    await project.Save(ct);

    var job = new Job { ProjectId = project.Id, Name = "Job 1" };
    await job.Save(ct);

    // Auto-commit on dispose
}
```

### Pattern 2: Explicit Commit

For more control, explicitly commit:

```csharp
using (EntityContext.Transaction("explicit-commit"))
{
    var todo = new Todo { Title = "Task 1", Status = "Pending" };
    await todo.Save(ct);

    // Explicit commit
    await EntityContext.CommitAsync(ct);
}
```

### Pattern 3: Conditional Rollback

Rollback if conditions aren't met:

```csharp
using (EntityContext.Transaction("conditional-save"))
{
    var order = new Order { Amount = 1000, Status = "Pending" };
    await order.Save(ct);

    if (!await ValidateOrder(order))
    {
        await EntityContext.RollbackAsync();
        throw new ValidationException("Order validation failed");
    }

    await EntityContext.CommitAsync(ct);
}
```

---

## Cross-Adapter Transactions

### Pattern 4: Coordinating Multiple Adapters

Save entities across SQLite and SQL Server:

```csharp
using (EntityContext.Transaction("cross-adapter"))
{
    // Save to default adapter (SQLite)
    var localCache = new CacheEntry { Key = "user:123", Value = userData };
    await localCache.Save(ct);

    // Save to SQL Server
    using (EntityContext.Adapter("sqlserver"))
    {
        var userRecord = new User { Id = "123", Data = userData };
        await userRecord.Save(ct);
    }

    // Commit both atomically (best-effort)
    await EntityContext.CommitAsync(ct);
}
```

### Pattern 5: Cross-Adapter with Partitions

Combine adapter and partition routing:

```csharp
using (EntityContext.Transaction("multi-dimension"))
{
    // SQLite with partition
    using (EntityContext.Partition("production"))
    {
        await entity1.Save(ct);
    }

    // JSON adapter with different partition
    using (EntityContext.Adapter("json"))
    using (EntityContext.Partition("backup"))
    {
        await entity2.Save(ct);
    }

    await EntityContext.CommitAsync(ct);
}
```

---

## Error Handling

### Pattern 6: Transaction with Try-Catch

Handle errors gracefully:

```csharp
try
{
    using (EntityContext.Transaction("safe-operation"))
    {
        await entity1.Save(ct);
        await entity2.Save(ct);

        if (someConditionFailed)
        {
            await EntityContext.RollbackAsync();
            return;
        }

        await EntityContext.CommitAsync(ct);
    }
}
catch (TransactionException ex)
{
    logger.LogError(ex, "Transaction failed: {TransactionName}", ex.TransactionName);
    // Handle partial commit if needed
}
```

### Pattern 7: Nested Transaction Prevention

Nested transactions throw `InvalidOperationException`:

```csharp
using (EntityContext.Transaction("outer"))
{
    await entity.Save(ct);

    // This will throw InvalidOperationException
    using (EntityContext.Transaction("inner"))
    {
        // Never reached
    }
}
```

Instead, use a single transaction:

```csharp
using (EntityContext.Transaction("combined"))
{
    await Operation1Async(ct);  // Tracks operations
    await Operation2Async(ct);  // Tracks more operations

    await EntityContext.CommitAsync(ct);  // Commits all
}
```

---

## Advanced Patterns

### Pattern 8: Batch Operations in Transaction

Process large batches efficiently:

```csharp
const int batchSize = 100;
var items = GetLargeDataset();

using (EntityContext.Transaction("batch-import"))
{
    foreach (var batch in items.Chunk(batchSize))
    {
        foreach (var item in batch)
        {
            await item.Save(ct);
        }
    }

    await EntityContext.CommitAsync(ct);
}
```

### Pattern 9: Transaction with Delete Operations

Mix saves and deletes:

```csharp
using (EntityContext.Transaction("cleanup-and-recreate"))
{
    // Delete old entities
    var oldEntities = await Todo.Query(x => x.Status == "Completed", ct);
    foreach (var entity in oldEntities)
    {
        await entity.Remove(ct);
    }

    // Create new entities
    var newEntity = new Todo { Title = "Fresh Start", Status = "Pending" };
    await newEntity.Save(ct);

    await EntityContext.CommitAsync(ct);
}
```

### Pattern 10: Transaction Capabilities Inspection

Check transaction capabilities at runtime:

```csharp
using (EntityContext.Transaction("capability-aware"))
{
    await entity1.Save(ct);

    using (EntityContext.Adapter("postgres"))
    {
        await entity2.Save(ct);
    }

    var capabilities = EntityContext.Capabilities;
    if (capabilities != null)
    {
        logger.LogInformation(
            "Transaction tracking {OperationCount} operations across {AdapterCount} adapter(s)",
            capabilities.TrackedOperationCount,
            capabilities.Adapters.Length);

        if (!capabilities.SupportsDistributedTransactions)
        {
            logger.LogWarning("Best-effort atomicity only - not a true distributed transaction");
        }
    }

    await EntityContext.CommitAsync(ct);
}
```

### Pattern 11: Update Existing Entity Multiple Times

Last write wins:

```csharp
using (EntityContext.Transaction("progressive-update"))
{
    var document = await Document.Get(docId, ct);

    document!.Status = "Processing";
    await document.Save(ct);

    // Process document...
    await ProcessDocument(document);

    document.Status = "Completed";
    await document.Save(ct);  // Last save wins

    await EntityContext.CommitAsync(ct);
}
```

### Pattern 12: Configuration-Based Transaction Behavior

Configure transaction options at startup:

```csharp
// In Program.cs or startup configuration
builder.Services.AddKoanTransactions(options =>
{
    options.AutoCommitOnDispose = true;          // Default: true
    options.DefaultTimeout = TimeSpan.FromMinutes(5);
    options.MaxTrackedOperations = 50_000;       // Prevent memory issues
    options.EnableTelemetry = true;              // Activity spans + logging
});
```

---

## Best Practices

### ✅ DO

- **Use auto-commit for simple scenarios** - Less cognitive load
- **Name transactions descriptively** - Helps with debugging and telemetry
- **Keep transactions short** - Avoid long-running operations inside transactions
- **Check `EntityContext.InTransaction`** - When conditional logic depends on transaction state
- **Use explicit commit for critical operations** - When you need certainty before commit

### ❌ DON'T

- **Don't nest transactions** - Framework throws `InvalidOperationException`
- **Don't mix infrastructure and entity operations** - `Truncate`, `RemoveAll` bypass transactions
- **Don't assume true distributed transactions** - Framework provides best-effort atomicity
- **Don't track thousands of operations** - Use batching or multiple transactions
- **Don't ignore `TransactionException`** - Contains critical failure information

---

## Common Use Cases

### Use Case 1: Multi-Step Entity Creation

Creating related entities atomically:

```csharp
using (EntityContext.Transaction("create-project-with-jobs"))
{
    var project = new Project
    {
        Name = "My Project",
        StartDate = DateTime.UtcNow
    };
    await project.Save(ct);

    var jobs = Enumerable.Range(1, 5).Select(i => new Job
    {
        ProjectId = project.Id,
        Name = $"Job {i}",
        Status = "Pending"
    });

    foreach (var job in jobs)
    {
        await job.Save(ct);
    }

    // All or nothing
    await EntityContext.CommitAsync(ct);
}
```

### Use Case 2: Data Migration with Rollback

Migrate data safely with automatic rollback on failure:

```csharp
try
{
    using (EntityContext.Transaction("data-migration"))
    {
        var sourceEntities = await SourceEntity.All(ct);

        foreach (var source in sourceEntities)
        {
            var target = MapToTarget(source);
            await target.Save(ct);
        }

        await EntityContext.CommitAsync(ct);
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Migration failed - all changes rolled back");
    throw;
}
```

### Use Case 3: Backup Coordination

Coordinate primary and backup storage:

```csharp
using (EntityContext.Transaction("backup-sync"))
{
    // Primary storage
    await document.Save(ct);

    // Backup storage
    using (EntityContext.Adapter("backup"))
    {
        var backupCopy = document.Clone();
        await backupCopy.Save(ct);
    }

    await EntityContext.CommitAsync(ct);
}
```

---

## Troubleshooting

### Transaction Not Tracking Operations

**Symptom**: Entity saves execute immediately instead of being deferred.

**Solution**: Ensure `AddKoanTransactions()` is called in DI registration:

```csharp
builder.Services.AddKoan();
builder.Services.AddKoanTransactions();  // Required!
```

### Nested Transaction Error

**Symptom**: `InvalidOperationException: Cannot start transaction 'inner' inside existing transaction 'outer'`

**Solution**: Use a single transaction or move inner operations outside transaction scope.

### Partial Commit Warning

**Symptom**: Log message about partial commit failure.

**Solution**: This is expected behavior for best-effort atomicity. Consider:
- Using compensation logic
- Implementing idempotent operations
- Accepting eventual consistency

---

## Performance Considerations

### Memory Usage

Transactions track all operations in memory. For large batches:

```csharp
// DON'T: Track 100k operations in single transaction
using (EntityContext.Transaction("huge"))
{
    foreach (var item in millionItems)
    {
        await item.Save(ct);  // Memory grows unbounded
    }
}

// DO: Break into smaller transactions
foreach (var batch in millionItems.Chunk(1000))
{
    using (EntityContext.Transaction($"batch-{batchNumber}"))
    {
        foreach (var item in batch)
        {
            await item.Save(ct);
        }
    }
}
```

### Telemetry Overhead

If performance is critical, disable telemetry:

```csharp
builder.Services.AddKoanTransactions(options =>
{
    options.EnableTelemetry = false;  // Skip Activity spans
});
```

---

## Related Documentation

- [Architecture Decision Record (ADR-0078)](../decisions/DATA-0078-ambient-transaction-coordination.md)
- [EntityContext Documentation](../api/entity-context.md)
- [Multi-Provider Guide](multi-provider-guide.md)
- [Entity-First Development](entity-first-guide.md)

---

## Summary

Koan Framework transactions provide:

- ✅ **Ambient context** - No explicit coordinator objects
- ✅ **Auto-commit** - Minimal cognitive load
- ✅ **Cross-adapter** - Coordinate SQLite, SQL Server, JSON, etc.
- ✅ **Best-effort atomicity** - Not true distributed transactions
- ✅ **Telemetry** - Activity spans and structured logging
- ✅ **Simple API** - `EntityContext.Transaction(name)` with using pattern

Use transactions when you need to coordinate multiple entity operations and ensure they succeed or fail together.
