# Koan.Data.Connector.InMemory

Thread-safe in-memory data adapter for Koan Framework.

## Features

- **Zero Configuration**: Auto-registers as fallback adapter (priority: -100)
- **Full LINQ Support**: Complete LINQ-to-Objects query capabilities
- **Thread-Safe**: Concurrent dictionary-based storage
- **Partition-Aware**: Respects `EntityContext.With(partition: "tenant-id")`
- **Framework Integration**: All cross-cutting concerns work automatically
  - ✅ [Timestamp] auto-update
  - ✅ Entity events (OnBeforeSave, OnAfterLoad)
  - ✅ Schema validation
  - ✅ GUID v7 auto-generation
  - ✅ Audit trails

## Usage

### Basic Usage

```csharp
// No configuration needed - just reference the package
// InMemory adapter auto-registers as fallback

public class Todo : Entity<Todo> {
    public string Title { get; set; } = "";
    [Timestamp] public DateTimeOffset LastModified { get; set; }
}

var todo = new Todo { Title = "Buy milk" };
await todo.Save();  // Uses InMemory adapter automatically

var all = await Todo.All();  // LINQ queries work
var found = await Todo.Query(t => t.Title.Contains("milk"));
```

### Multi-Tenant / Partition Support

```csharp
// Isolate data by partition
using (EntityContext.With(partition: "tenant-123")) {
    var todo = new Todo { Title = "Tenant 123 data" };
    await todo.Save();  // Stored in "tenant-123" partition
}

using (EntityContext.With(partition: "tenant-456")) {
    var todos = await Todo.All();  // Only sees "tenant-456" data
}
```

### Testing Scenarios

```csharp
[Fact]
public async Task TestEntityBehavior() {
    // InMemory adapter is perfect for unit tests
    var entity = new MyEntity { Name = "Test" };
    await entity.Save();

    // All framework features work identically to production adapters
    entity.LastModified.Should().BeAfter(DateTimeOffset.MinValue);  // [Timestamp] works

    var loaded = await MyEntity.Get(entity.Id);
    loaded.Should().NotBeNull();
}
```

## Capabilities

- **Query**: `QueryCapabilities.Linq`
- **Write**: `WriteCapabilities.BulkUpsert | BulkDelete | AtomicBatch`

## Architecture

### Thread-Safe Storage

Data is stored in `ConcurrentDictionary<TKey, TEntity>` per (entity type, partition) tuple:

```
InMemoryDataStore (Singleton)
├─ Store<Todo, "default">: ConcurrentDictionary<string, Todo>
├─ Store<Todo, "tenant-123">: ConcurrentDictionary<string, Todo>
├─ Store<User, "default">: ConcurrentDictionary<string, User>
└─ ...
```

### Lifetime

- **InMemoryDataStore**: Singleton - data persists across repository instances
- **InMemoryRepository**: Scoped per entity type + partition
- Data cleared on application restart (ephemeral by design)

## Priority

**Priority: -100 (Lowest)**

InMemory adapter acts as a fallback when no other adapter is configured. Real adapters (PostgreSQL, MongoDB, etc.) will override it.

```csharp
// Explicit InMemory usage
services.AddKoan()
    .UseInMemoryStorage();  // If extension method exists

// Or let it auto-register as fallback
services.AddKoan();  // InMemory used if no other adapter configured
```

## When to Use

✅ **Good For:**
- Unit testing (fast, no database required)
- Development/prototyping
- Ephemeral caching scenarios
- Learning Koan Framework patterns

❌ **Not Good For:**
- Production data persistence (data lost on restart)
- Cross-process data sharing
- Large datasets (all data in memory)

## Performance

- **Get**: O(1) - ConcurrentDictionary lookup
- **Query (LINQ)**: O(n) - LINQ-to-Objects
- **Upsert**: O(1) - ConcurrentDictionary update
- **Batch**: Atomic within memory (no transaction overhead)

## See Also

- [DATA-0081 ADR](../../../../docs/decisions/DATA-0081-inmemory-adapter.md) - Architecture decision record
- [Koan.Data.Core](../../../Koan.Data.Core/) - Core data abstractions
- [Entity<T> Pattern](../../../Koan.Data.Core/Model/Entity.cs) - Entity base class
