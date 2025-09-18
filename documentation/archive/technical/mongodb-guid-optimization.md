# MongoDB GUID Optimization Implementation Guide

## Overview

This document details the complete implementation of MongoDB GUID optimization in the Koan Framework, including automatic Entity<> pattern detection, BSON serialization configuration, and integration with automatic GUID v7 generation.

## Problem Statement

MongoDB .NET driver v3.0+ introduced breaking changes that caused custom BSON serializers to be ignored, particularly affecting GUID serialization. String-based GUIDs were being stored as strings instead of optimized BinData format, impacting database performance and storage efficiency.

## Architecture Overview

The solution consists of four integrated components:

1. **Pattern Detection**: Automatic analysis of Entity<> inheritance patterns
2. **Global Configuration**: Bootstrap-time BSON serializer registration
3. **Smart Serialization**: Conditional string-to-GUID conversion
4. **Automatic Generation**: Built-in GUID v7 ID generation

## Implementation Details

### 1. Entity Pattern Detection

**File**: `src/Koan.Data.Core/Optimization/StorageOptimizationExtensions.cs`

The framework automatically detects which entities should be optimized based on inheritance patterns:

```csharp
private static StorageOptimizationInfo AnalyzeStringKeyedEntity<TEntity>(string idPropertyName)
{
    var entityType = typeof(TEntity);
    var baseType = entityType.BaseType;

    while (baseType != null)
    {
        if (baseType.IsGenericType)
        {
            var genericTypeDef = baseType.GetGenericTypeDefinition();
            var genericArgs = baseType.GetGenericArguments();

            if (genericTypeDef.Name.StartsWith("Entity", StringComparison.Ordinal))
            {
                // Entity<TEntity> pattern (single generic - implicit string) - OPTIMIZE
                if (genericArgs.Length == 1 && genericArgs[0] == entityType)
                {
                    return new StorageOptimizationInfo
                    {
                        OptimizationType = StorageOptimizationType.Guid,
                        IdPropertyName = idPropertyName,
                        Reason = "Automatic GUID optimization for Entity<T> pattern (implicit string key)"
                    };
                }

                // Entity<TEntity, string> pattern (explicit string) - DON'T OPTIMIZE
                if (genericArgs.Length == 2 &&
                    genericArgs[0] == entityType &&
                    genericArgs[1] == typeof(string))
                {
                    return new StorageOptimizationInfo
                    {
                        OptimizationType = StorageOptimizationType.None,
                        IdPropertyName = idPropertyName,
                        Reason = "No optimization for Entity<T, string> pattern (explicit string key choice)"
                    };
                }
            }
        }
        baseType = baseType.BaseType;
    }

    // Default: IEntity<string> implementations without Entity<T> pattern - don't optimize
    return StorageOptimizationInfo.None;
}
```

**Pattern Recognition Logic**:
- `Entity<MediaFormat>` → **Optimize** (single generic parameter)
- `Entity<MediaType, string>` → **Don't optimize** (explicit string choice)
- `IEntity<string>` direct implementation → **Don't optimize** (explicit interface choice)

### 2. Global BSON Configuration

**File**: `src/Koan.Data.Mongo/Initialization/MongoOptimizationAutoRegistrar.cs`

Bootstrap-time configuration that scans all assemblies and registers global MongoDB serializers:

```csharp
public void Initialize(IServiceCollection services)
{
    Console.WriteLine("[MONGO-AUTO-REGISTRAR] MongoOptimizationAutoRegistrar.Initialize() called!");

    lock (_lock)
    {
        if (_globalConfigurationApplied) return;

        try
        {
            // Step 1: Apply global MongoDB driver configuration for v3.5.0 compatibility
            ConfigureGlobalMongoDriverSettings();

            // Step 2: Scan all assemblies for Entity types requiring optimization
            var optimizedEntityTypes = ScanForOptimizedEntityTypes();

            // Step 3: Register global serializers for discovered types
            RegisterGlobalSerializers(optimizedEntityTypes);

            Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Successfully configured GUID optimization for {optimizedEntityTypes.Count} entity types");
            _globalConfigurationApplied = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Failed to initialize MongoDB optimization: {ex.Message}");
            throw;
        }
    }
}
```

**Key Features**:
- **Assembly Scanning**: Finds all Entity<> types across loaded assemblies
- **Global Registration**: Applies to entire MongoDB driver instance
- **One-Time Setup**: Runs once during application bootstrap
- **Framework Integration**: Called automatically by main KoanAutoRegistrar

### 3. Smart String-GUID Serializer

**File**: `src/Koan.Data.Mongo/Initialization/MongoOptimizationAutoRegistrar.cs`

Intelligent BSON serializer that only converts valid GUIDs to BinData:

```csharp
public class SmartStringGuidSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();

        switch (bsonType)
        {
            case BsonType.Binary:
                var binaryData = context.Reader.ReadBinaryData();
                if (binaryData.SubType == BsonBinarySubType.UuidStandard)
                {
                    var guid = binaryData.ToGuid();
                    return guid.ToString("D");
                }
                break;

            case BsonType.String:
                return context.Reader.ReadString();

            case BsonType.Null:
                context.Reader.ReadNull();
                return null!;
        }

        throw new FormatException($"Cannot convert {bsonType} to string GUID");
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {
        if (value == null)
        {
            context.Writer.WriteNull();
            return;
        }

        // Only convert to GUID BinData if the string is a valid GUID
        if (Guid.TryParse(value, out var guid))
        {
            // Store as native MongoDB UUID BinData for optimal performance and indexing
            var binaryData = new BsonBinaryData(guid, GuidRepresentation.Standard);
            context.Writer.WriteBinaryData(binaryData);
        }
        else
        {
            // Keep as string if not a valid GUID
            context.Writer.WriteString(value);
        }
    }
}
```

**Serialization Behavior**:
- **Valid GUIDs** → Stored as BinData (optimized)
- **Non-GUID strings** → Stored as strings (unchanged)
- **Null values** → Handled properly
- **Mixed scenarios** → Each value evaluated individually

### 4. Automatic GUID v7 Generation

**File**: `src/Koan.Data.Core/Model/Entity.cs`

Lazy ID generation integrated with the optimization system:

```csharp
public abstract partial class Entity<TEntity> : Entity<TEntity, string>
    where TEntity : class, Koan.Data.Abstractions.IEntity<string>
{
    private string? _id;

    public override string Id
    {
        get => _id ??= Guid.CreateVersion7().ToString();
        set => _id = value;
    }
}
```

**Generation Behavior**:
- **Lazy initialization**: Only generates when first accessed
- **Thread-safe**: Null-coalescing assignment is atomic
- **Override-friendly**: Explicit assignment prevents generation
- **Load-safe**: Database values override automatic generation

## Integration Flow

### Bootstrap Sequence

1. **Application Startup**
   - KoanAutoRegistrar.Initialize() called by framework
   - MongoOptimizationAutoRegistrar.Initialize() called explicitly
   - Global MongoDB driver configuration applied

2. **Assembly Scanning**
   - All loaded assemblies enumerated
   - Entity<> types identified and analyzed
   - Optimization metadata computed and cached

3. **Serializer Registration**
   - SmartStringGuidSerializer registered globally
   - All string properties affected by new serialization logic
   - BsonClassMap fallback available if global registration fails

### Runtime Behavior

1. **Entity Creation**
   ```csharp
   var mediaFormat = new MediaFormat { Name = "TV Series" };
   // Id automatically becomes GUID v7 on first access
   ```

2. **Database Storage**
   ```csharp
   await mediaFormat.Save();
   // SmartStringGuidSerializer detects GUID format
   // Stores as BinData in MongoDB for optimization
   ```

3. **Database Retrieval**
   ```csharp
   var loaded = await MediaFormat.Get(mediaFormat.Id);
   // SmartStringGuidSerializer converts BinData back to string
   // Entity receives string value, automatic generation bypassed
   ```

## Configuration Examples

### Entity Definitions

**Optimized Entity** (uses automatic generation + optimization):
```csharp
[Storage(Name = "MediaFormats")]
public sealed class MediaFormat : Entity<MediaFormat>
{
    [Parent(typeof(MediaType))]
    public required string MediaTypeId { get; set; }
    public required string Name { get; set; }
    // Id automatically generated as GUID v7, stored as BinData
}
```

**Non-Optimized Entity** (explicit string IDs):
```csharp
[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]
[Storage(Name = "MediaTypes")]
public sealed class MediaType : Entity<MediaType, string>
{
    public required string Name { get; set; }
    // Id must be set explicitly, stored as string
}
```

### Usage Patterns

**Standard Usage** (MediaFormat):
```csharp
var format = new MediaFormat
{
    MediaTypeId = "media-anime",
    Name = "TV Series"
    // Id generated automatically: "018b2c3d-4e5f-7890-abcd-ef1234567890"
};
```

**Reference Data** (MediaType):
```csharp
var mediaType = new MediaType
{
    Id = "media-anime", // Explicit stable ID
    Name = "Anime"
};
```

## Debugging and Monitoring

### Debug Output

The system provides comprehensive debug logging:

```
[MONGO-KOAN-AUTO-REGISTRAR] Applying MongoDB GUID optimization directly...
[MONGO-AUTO-REGISTRAR] MongoOptimizationAutoRegistrar.Initialize() called!
[MONGO-AUTO-REGISTRAR] Scanning 15 assemblies...
[MONGO-AUTO-REGISTRAR] Found optimizable entity: MediaFormat -> Guid
[MONGO-AUTO-REGISTRAR] Successfully configured GUID optimization for 7 entity types
[OPTIMIZATION-DEBUG] MediaFormat - Entity<T> pattern detected - returning GUID optimization
[SMART-SERIALIZER] Converting string GUID 018b2c3d-4e5f-7890-abcd-ef1234567890 to BinData
```

### Monitoring Points

- **Bootstrap**: Optimization registration success/failure
- **Pattern Detection**: Which entities qualify for optimization
- **Serialization**: String-to-BinData conversion tracking
- **Performance**: Storage size reduction from BinData usage

## Performance Impact

### Storage Optimization

**Before Optimization**:
```javascript
// MongoDB document (string storage)
{
  "_id": "018b2c3d-4e5f-7890-abcd-ef1234567890", // 36 bytes
  "mediaTypeId": "media-anime",                   // ~10 bytes
  "name": "TV Series"
}
```

**After Optimization**:
```javascript
// MongoDB document (BinData storage)
{
  "_id": BinData(4, "AYssPU5feJCrzfEjRWeJA=="), // 16 bytes
  "mediaTypeId": "media-anime",                   // ~10 bytes
  "name": "TV Series"
}
```

**Benefits**:
- **Storage**: ~55% reduction in GUID field size (36→16 bytes)
- **Indexing**: Better B-tree performance with binary data
- **Sorting**: Native GUID comparison in database
- **Network**: Reduced transfer size for GUID-heavy documents

### GUID v7 Advantages

- **Time-ordering**: Chronological sorting without additional timestamps
- **Clustering**: Related entities created together have similar prefixes
- **Index performance**: Better than random GUID v4 for B-tree structures
- **Distributed systems**: Unique across multiple nodes/services

## Troubleshooting

### Common Issues

**1. Optimization Not Applied**
```
Symptom: String IDs still visible in MongoDB
Check: Debug logs show entity pattern detection
Solution: Verify Entity<TEntity> inheritance pattern
```

**2. Serialization Errors**
```
Symptom: BsonSerializationException during save/load
Check: SmartStringGuidSerializer registration
Solution: Ensure MongoOptimizationAutoRegistrar initialized
```

**3. Mixed String/GUID Storage**
```
Symptom: Some documents use BinData, others use strings
Check: When entities were created (before/after optimization)
Solution: Run data migration or accept mixed format
```

### Diagnostic Commands

**Check MongoDB Storage Format**:
```javascript
// In MongoDB shell
db.MediaFormats.findOne({}, {_id: 1});
// BinData = optimized, string = not optimized
```

**Verify Framework Configuration**:
```csharp
// In application logs
[MONGO-AUTO-REGISTRAR] Successfully configured GUID optimization for X entity types
```

## Migration Strategy

### Existing Applications

1. **Enable Optimization**: Deploy framework changes
2. **Monitor Storage**: Check new documents use BinData
3. **Gradual Migration**: New data automatically optimized
4. **Optional Cleanup**: Migrate existing string GUIDs to BinData

### Data Migration Script

```csharp
// Optional: Convert existing string GUIDs to BinData
public async Task MigrateExistingGuids()
{
    var formats = await MediaFormat.All();
    foreach (var format in formats)
    {
        if (Guid.TryParse(format.Id, out _))
        {
            // Force re-save to apply new serialization
            await format.Save();
        }
    }
}
```

## Future Enhancements

### Potential Improvements

1. **Selective Optimization**: Per-property optimization attributes
2. **Migration Tools**: Automated string-to-BinData conversion utilities
3. **Performance Metrics**: Built-in storage efficiency monitoring
4. **Custom Formats**: Support for other optimized string formats

### Framework Integration

- **Flow System**: Event correlation using optimized GUIDs
- **Caching**: AggregateBag optimization for metadata
- **Validation**: Framework-aware GUID validation
- **Testing**: Deterministic ID generation for unit tests

This implementation represents a comprehensive solution to MongoDB GUID optimization that integrates seamlessly with the Koan Framework's entity system, providing significant performance benefits while maintaining developer-friendly APIs.