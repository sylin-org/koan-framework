# Automatic GUID v7 Generation in Entity<> Base Classes

## Overview

The Koan Framework automatically generates GUID v7 identifiers for entities that inherit from `Entity<TEntity>` (the convenience base class for string-keyed entities). This feature promotes "auto-safe ID" patterns while maintaining full flexibility for custom scenarios.

## Key Benefits

- **Zero Boilerplate**: Entities are immediately usable without manual ID assignment
- **Time-Ordered IDs**: GUID v7 provides better database performance and natural sorting
- **Developer Experience**: Eliminates common bugs from null/empty IDs
- **Flexible Override**: Explicit ID assignment still works when needed
- **Load-Safe**: Automatic generation is overridden by loaded entity values

## Implementation Details

### Core Mechanism

The `Entity<TEntity>` base class implements lazy GUID v7 generation:

```csharp
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, Koan.Data.Abstractions.IEntity<string>
{
    private string? _id;

    /// <summary>
    /// Gets or sets the entity ID. Automatically generates a GUID v7 value on first access if not already set.
    /// This promotes auto-safe ID patterns while allowing explicit override when needed.
    /// </summary>
    public override string Id
    {
        get => _id ??= Guid.CreateVersion7().ToString();
        set => _id = value;
    }
}
```

### Behavior Patterns

#### 1. Automatic Generation
```csharp
var mediaFormat = new MediaFormat
{
    MediaTypeId = "media-anime",
    Name = "TV Series"
    // Id is automatically generated on first access
};

Console.WriteLine(mediaFormat.Id); // "018b2c3d-4e5f-7890-abcd-ef1234567890"
```

#### 2. Explicit Override
```csharp
var mediaType = new MediaType
{
    Id = "media-custom", // Explicit ID assignment
    Name = "Custom Type"
};
```

#### 3. Loaded Entity Behavior
```csharp
// When loading from database, stored ID value is preserved
var loaded = await MediaFormat.Get("existing-id");
Console.WriteLine(loaded.Id); // "existing-id" (not auto-generated)
```

## Integration with Storage Optimization

The automatic ID generation works seamlessly with the Koan Framework's storage optimization system:

### Optimized Entities (Default)
Entities inheriting from `Entity<TEntity>` automatically qualify for GUID optimization:

```csharp
[Storage(Name = "MediaFormats")]
public sealed class MediaFormat : Entity<MediaFormat>
{
    // Inherits automatic GUID v7 generation
    // Qualifies for MongoDB BinData optimization
    [Parent(typeof(MediaType))]
    public required string MediaTypeId { get; set; }
    // ...
}
```

### Opt-Out Entities
Entities can explicitly disable optimization and use custom ID patterns:

```csharp
[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]
[Storage(Name = "MediaTypes")]
public sealed class MediaType : Entity<MediaType, string>
{
    // Uses explicit Entity<TEntity, string> base
    // No automatic ID generation
    // Custom ID assignment required
    public required string Name { get; set; }
    // ...
}
```

## Migration and Compatibility

### Existing Code
Existing code continues to work without changes:
- Explicit ID assignments override automatic generation
- No breaking changes to existing entity behavior
- Backward compatible with all existing patterns

### New Development
New entities benefit immediately:
- No need to assign IDs in most cases
- Consistent GUID v7 usage across the application
- Better database performance from time-ordered IDs

## Performance Characteristics

### GUID v7 Advantages
- **Time-ordered**: Natural chronological sorting
- **Index-friendly**: Better B-tree performance than GUID v4
- **Locality**: Related entities created together have similar prefixes
- **Uniqueness**: Globally unique across distributed systems

### Lazy Generation
- **On-demand**: Only generates when ID is first accessed
- **Zero overhead**: No generation cost for entities that set explicit IDs
- **Memory efficient**: Single string field per entity

## Best Practices

### When to Use Automatic Generation
✅ **Use for most domain entities**:
```csharp
public class Order : Entity<Order>
{
    // Automatic GUID v7 generation
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
}
```

### When to Use Explicit IDs
✅ **Use for stable reference data**:
```csharp
[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]
public class Currency : Entity<Currency, string>
{
    public string Id { get; set; } = null!; // "USD", "EUR", etc.
    public string Name { get; set; } = null!;
}
```

### When to Use Custom Key Types
✅ **Use Entity<TEntity, TKey> for non-string keys**:
```csharp
public class NumericEntity : Entity<NumericEntity, int>
{
    // Uses integer primary key
    // No automatic generation
}
```

## Testing Considerations

### Deterministic Testing
For unit tests requiring predictable IDs:

```csharp
[Test]
public void TestWithKnownId()
{
    var entity = new MediaFormat
    {
        Id = "test-format-id", // Override automatic generation
        Name = "Test Format"
    };

    Assert.AreEqual("test-format-id", entity.Id);
}
```

### Integration Testing
For integration tests, automatic generation works naturally:

```csharp
[Test]
public async Task TestEntityPersistence()
{
    var entity = new MediaFormat { Name = "Test" };
    // Id automatically generated

    await entity.Save();
    var loaded = await MediaFormat.Get(entity.Id);

    Assert.IsNotNull(loaded);
    Assert.AreEqual(entity.Id, loaded.Id);
}
```

## Technical Implementation Notes

### Thread Safety
- The lazy initialization (`_id ??= Guid.CreateVersion7().ToString()`) is thread-safe
- Multiple threads accessing Id simultaneously will only generate one GUID
- No locking overhead or race conditions

### Serialization
- JSON serialization includes the generated ID
- Entity Framework/MongoDB serialize the ID normally
- No special serialization attributes required

### Inheritance
- Only affects `Entity<TEntity>` (single generic parameter)
- `Entity<TEntity, TKey>` continues to use provided key type
- Inheritance chain preserved for polymorphic scenarios

## Framework Integration

This feature integrates with other Koan Framework capabilities:

- **Data Layer**: Works with all data providers (MongoDB, SQL, etc.)
- **Flow System**: Event sourcing uses generated IDs for correlation
- **Relationship Navigation**: Parent/child relationships work seamlessly
- **Caching**: AggregateBag optimization metadata includes ID generation info
- **Validation**: Framework validation works with auto-generated IDs

## Monitoring and Diagnostics

The framework provides insight into ID generation behavior:

```csharp
// Debug output shows optimization decisions
[OPTIMIZATION-DEBUG] MediaFormat - Entity<T> pattern detected - returning GUID optimization
[OPTIMIZATION-DEBUG] MediaType - OptimizeStorage.None - no automatic generation
```

This comprehensive automatic ID generation feature exemplifies the Koan Framework's philosophy of "sensible defaults with full flexibility" - developers get optimal behavior by default while retaining complete control when needed.