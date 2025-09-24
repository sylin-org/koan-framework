# Entity ID Storage Optimization - Technical Architecture

## Architecture Overview

The Entity ID Storage Optimization system provides transparent storage-level optimization while maintaining string-based developer APIs through a three-layer architecture:

1. **Entity Layer**: String-based IDs for developer consistency
2. **Optimization Layer**: Extension-based analysis and per-entity caching using `StorageOptimizationExtensions` and `AggregateBag`
3. **Storage Layer**: Native database types with transparent conversion

## Core Components

### 1. Extension-Based Analysis & AggregateBag Integration

**Purpose**: AggregateBag-integrated optimization analysis using `StorageOptimizationExtensions`.

```csharp
// Usage: Retrieve optimization info for an entity type
var optimizationInfo = serviceProvider.GetStorageOptimization<MyEntity, string>();
if (optimizationInfo.IsOptimized) {
    // Use optimized storage type for DDL, queries, etc.
}
```

**Automatic Optimization Logic:**

- **`Entity<Model>`** → Implicit string key → **OPTIMIZE** (assumes GUID-like behavior)
- **`Entity<Model, string>`** → Explicit string key → **DO NOT optimize** (respects developer intent)
- **Direct `IEntity<string>`** → Explicit choice → **DO NOT optimize**

**Manual Control:**

- Use `[OptimizeStorage(OptimizationType = StorageOptimizationType.Guid)]` to force optimization
- Use `[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]` to disable optimization

**Diagnostics:**

- Optimization reason and type available via `StorageOptimizationInfo`
- Logs inheritance pattern detection for troubleshooting

### 2. StorageOptimization & StorageOptimizationInfo

**Purpose**: Encapsulates conversion strategies and optimization metadata.

```csharp
namespace Koan.Data.Core.Optimization;

// Actual implementation - enum-based optimization types
public enum StorageOptimizationType
{
    None,
    Guid
    // Future: Int32, Int64, Binary, etc.
}

// Metadata about optimization decision
public sealed class StorageOptimizationInfo
{
    public StorageOptimizationType OptimizationType { get; init; }
    public string IdPropertyName { get; init; } = "";
    public string Reason { get; init; } = "";
    public bool IsOptimized => OptimizationType != StorageOptimizationType.None;
}

// Factory methods for conversion strategies
public sealed class StorageOptimization
{
    public Type StorageType { get; init; }
    public Func<string, object> ToStorage { get; init; }
    public Func<object, string> FromStorage { get; init; }
    public string OptimizationType { get; init; }

    // Factory methods for different optimization types
    public static StorageOptimization CreateGuidOptimization(string reason) => new()
    {
        StorageType = typeof(Guid),
        ToStorage = id => Guid.Parse(id),
        FromStorage = obj => ((Guid)obj).ToString(),
        OptimizationType = "GUID",
        Reason = reason
    };

    // Providers handle their own native type mappings
}
```

### 3. Repository Pattern with Transparent Optimization

**Purpose**: Transparent conversion in CRUD operations using `IOptimizedDataRepository`.

```csharp
namespace Koan.Data.Postgres;

internal sealed class PostgresRepository<TEntity, TKey> :
    IOptimizedDataRepository<TEntity, TKey>,
    IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly StorageOptimizationInfo _optimizationInfo;

    public PostgresRepository(IServiceProvider serviceProvider, /* other deps */)
    {
        _optimizationInfo = serviceProvider.GetStorageOptimization<TEntity, TKey>();
    }

    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        // Simple pre-write optimization transformation
        if (_optimizationInfo.IsOptimized && typeof(TKey) == typeof(string))
        {
            OptimizeEntityForStorage(entity, _optimizationInfo);
        }

        // Standard repository operations...
    }

    private static void OptimizeEntityForStorage(TEntity entity, StorageOptimizationInfo optimizationInfo)
    {
        if (!optimizationInfo.IsOptimized || typeof(TKey) != typeof(string))
            return;

        var idProperty = typeof(TEntity).GetProperty(optimizationInfo.IdPropertyName);
        if (idProperty?.GetValue(entity) is string stringId && !string.IsNullOrEmpty(stringId))
        {
            switch (optimizationInfo.OptimizationType)
            {
                case StorageOptimizationType.Guid:
                    if (Guid.TryParse(stringId, out var guid))
                        idProperty.SetValue(entity, guid.ToString("D"));
                    break;
            }
        }
    }
}
```

## Key Implementation Highlights

### AggregateBag Integration

The optimization system integrates seamlessly with Koan's existing `AggregateBag` caching system:

```csharp
// Extension method leverages AggregateBag for caching optimization metadata
public static StorageOptimizationInfo GetStorageOptimization<TEntity, TKey>(this IServiceProvider serviceProvider)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    return AggregateBags.GetOrAdd<TEntity, TKey, StorageOptimizationInfo>(
        serviceProvider,
        OptimizationBagKey,
        () => AnalyzeEntityOptimization<TEntity, TKey>());
}
```

### Simple Pre-Write Transformation

Rather than complex serialization logic, the system uses simple entity transformation before database operations:

```csharp
// Applied just before database operations
private static void OptimizeEntityForStorage(TEntity entity, StorageOptimizationInfo optimizationInfo)
{
    // Simple GUID normalization for storage
    if (optimizationInfo.OptimizationType == StorageOptimizationType.Guid)
    {
        if (Guid.TryParse(stringId, out var guid))
            idProperty.SetValue(entity, guid.ToString("D"));
    }
}
```

## Performance Optimization Details

### Bootstrap Analysis Performance

```csharp
// One-time cost per entity type during application startup
// Uses AggregateBag for caching - analysis happens once per entity type
public static StorageOptimizationInfo GetStorageOptimization<TEntity, TKey>(this IServiceProvider serviceProvider)
{
    return AggregateBags.GetOrAdd<TEntity, TKey, StorageOptimizationInfo>(
        serviceProvider,
        OptimizationBagKey,
        () => {
            // Analysis cost: ~1-2ms per entity type
            // Executed once per application lifetime
            return AnalyzeEntityOptimization<TEntity, TKey>();
        });
}
```

### Runtime Conversion Performance

```csharp
// Optimized conversion delegates (pre-compiled)
public class StorageOptimization
{
    // Pre-compiled conversion functions for optimal performance
    private static readonly Func<string, Guid> StringToGuid = id => Guid.Parse(id);
    private static readonly Func<Guid, string> GuidToString = guid => guid.ToString();

    // Conversion cost: ~100ns per operation
    // Database I/O: ~1ms per operation
    // Net overhead: <0.01%
}
```

## Error Handling and Diagnostics

### Conversion Error Recovery

```csharp
public class SafeStorageOptimization : StorageOptimization
{
    public object ToStorageWithFallback(string id)
    {
        try
        {
            return ToStorage(id);
        }
        catch (Exception ex)
        {
            // Log conversion failure and fallback to string
            _logger.LogWarning("ID conversion failed for {Id}: {Error}. Using string storage.", id, ex.Message);
            return id; // Fallback to string storage
        }
    }
}
```

### Diagnostic Logging

```csharp
// Bootstrap diagnostics
_logger.LogInformation("Entity {Type}: Storage optimization enabled. Type={StorageType}, Reason={Reason}",
    typeof(TEntity).Name, optimization.StorageType.Name, optimization.Reason);

// Runtime diagnostics (debug level)
_logger.LogDebug("Converting ID {StringId} to {StorageType} for {EntityType}",
    stringId, storageType.Name, typeof(TEntity).Name);
```

## Database Provider Compatibility

### Provider-Specific Mappings

```csharp
internal static class ProviderTypeMapping
{
    private static readonly Dictionary<string, Dictionary<Type, string>> Mappings = new()
    {
        ["postgresql"] = new()
        {
            [typeof(Guid)] = "uuid",
            [typeof(int)] = "integer",
            [typeof(long)] = "bigint",
            [typeof(string)] = "text"
        },
        ["sqlserver"] = new()
        {
            [typeof(Guid)] = "uniqueidentifier",
            [typeof(int)] = "int",
            [typeof(long)] = "bigint",
            [typeof(string)] = "nvarchar(256)"
        },
        ["mysql"] = new()
        {
            [typeof(Guid)] = "binary(16)",
            [typeof(int)] = "int",
            [typeof(long)] = "bigint",
            [typeof(string)] = "varchar(255)"
        }
    };

    public static string GetOptimalType(string provider, Type clrType) =>
        Mappings.TryGetValue(provider, out var mapping) && mapping.TryGetValue(clrType, out var dbType)
            ? dbType
            : "text"; // Safe fallback
}
```

This technical architecture provides the foundation for transparent, high-performance ID storage optimization while maintaining full compatibility with existing Koan patterns.
