# ParentKey Direct Migration: Break-and-Rebuild Plan
**Clean Migration from Sora.Flow to Sora.Data**

## Executive Summary

This document outlines a direct break-and-rebuild migration approach for moving ParentKey functionality from `Sora.Flow.Core` to `Sora.Data.Core`. Since this is a greenfield implementation, we'll make breaking changes to achieve a clean, unified architecture.

## Target Architecture: Clean Data-Layer Design

### Core Relationship System in Sora.Data.Core

```
src/Sora.Data.Core/
├── Relationships/
│   ├── ParentAttribute.cs        # Replaces ParentKeyAttribute
│   ├── IRelationshipMetadata.cs           # Replaces FlowRegistry
│   ├── RelationshipMetadataService.cs     # Implementation
│   └── IRelationshipResolver.cs           # Parent resolution logic
├── Identity/
│   ├── EntityIdentityLink.cs              # Replaces IdentityLink<T>
│   ├── IIdentityResolutionService.cs      # External ID resolution
│   └── IdentityResolutionService.cs       # Implementation
├── Resolution/
│   ├── DeferredEntity.cs                  # Replaces ParkedRecord<T>
│   ├── IDeferredResolutionService.cs      # Background healing
│   └── DeferredResolutionService.cs       # Implementation
└── Extensions/
    └── EntityRelationshipExtensions.cs    # Helper methods
```

### Enhanced Entity Base Class

```csharp
// src/Sora.Data.Core/Model/Entity.cs (Enhanced)
public abstract class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // Existing properties...
    public TKey Id { get; set; } = default!;
    
    // New relationship methods (clean API without Async suffix)
    public static async Task<TParent?> GetParent<TParent>(TKey childId, CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
    {
        var resolver = ServiceLocator.GetService<IRelationshipResolver>();
        return await resolver.GetParentAsync<TEntity, TParent, TKey>(childId, ct);
    }
    
    public static async Task<IReadOnlyList<TChild>> GetChildren<TChild>(TKey parentId, CancellationToken ct = default)  
        where TChild : class, IEntity<TKey>
    {
        var resolver = ServiceLocator.GetService<IRelationshipResolver>();
        return await resolver.GetChildrenAsync<TEntity, TChild, TKey>(parentId, ct);
    }
    
    public async Task<TParent?> GetParentAsync<TParent>(CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
    {
        return await GetParentAsync<TParent>(Id, ct);
    }
}
```

## Step 1: Create Core Sora.Data Relationship System

### 1.1 ParentAttribute (Direct Replacement)

**File**: `src/Sora.Data.Core/Relationships/ParentAttribute.cs`
```csharp
using System;

namespace Sora.Data.Core.Relationships;

/// <summary>
/// Marks a property as a reference to a parent entity.
/// Direct replacement for Flow's ParentKeyAttribute.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParentAttribute : Attribute
{
    public Type EntityType { get; }
    public string? Role { get; }
    public string? PayloadPath { get; }
    
    // Primary constructor for simple usage
    public ParentAttribute(Type entityType)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }
    
    // Full constructor for advanced scenarios
    public ParentAttribute(Type entityType, string? role = null, string? payloadPath = null) 
        : this(entityType)
    {
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        PayloadPath = string.IsNullOrWhiteSpace(payloadPath) ? null : payloadPath;
    }
}
```

### 1.2 Relationship Metadata Service (Replaces FlowRegistry)

**File**: `src/Sora.Data.Core/Relationships/IRelationshipMetadata.cs`
```csharp
using System;
using System.Collections.Generic;

namespace Sora.Data.Core.Relationships;

public interface IRelationshipMetadata
{
    (Type Parent, string ParentKeyPath)? GetParentRelationship(Type entityType);
    IEnumerable<RelationshipInfo> GetChildRelationships(Type parentType);
    void RegisterRelationship<TEntity, TParent, TKey>(string foreignKeyProperty, string? role = null)
        where TEntity : class, IEntity<TKey>
        where TParent : class, IEntity<TKey>;
}

public record RelationshipInfo(
    Type EntityType,
    Type ParentType, 
    string ForeignKeyProperty,
    string? Role);
```

**File**: `src/Sora.Data.Core/Relationships/RelationshipMetadataService.cs`
```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace Sora.Data.Core.Relationships;

public class RelationshipMetadataService : IRelationshipMetadata
{
    private readonly ConcurrentDictionary<Type, (Type Parent, string ParentKeyPath)?> _parentCache = new();
    private readonly ConcurrentDictionary<Type, List<RelationshipInfo>> _childCache = new();
    
    public (Type Parent, string ParentKeyPath)? GetParentRelationship(Type entityType)
    {
        return _parentCache.GetOrAdd(entityType, type =>
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<ParentAttribute>(inherit: true);
                if (attr != null)
                {
                    // Validate parent has [Key] property
                    var parentKeyProp = attr.EntityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null);
                    
                    if (parentKeyProp == null)
                        throw new InvalidOperationException(
                            $"Parent type {attr.EntityType.Name} has no [Key] property for EntityReference resolution");
                    
                    return (attr.EntityType, prop.Name);
                }
            }
            return null;
        });
    }
    
    public IEnumerable<RelationshipInfo> GetChildRelationships(Type parentType)
    {
        return _childCache.GetOrAdd(parentType, parent =>
        {
            var relationships = new List<RelationshipInfo>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                
                foreach (var type in types)
                {
                    if (type is null || !type.IsClass || type.IsAbstract) continue;
                    
                    var parentRelation = GetParentRelationship(type);
                    if (parentRelation?.Parent == parent)
                    {
                        relationships.Add(new RelationshipInfo(
                            type,
                            parent,
                            parentRelation.Value.ParentKeyPath,
                            null));
                    }
                }
            }
            
            return relationships;
        });
    }
    
    public void RegisterRelationship<TEntity, TParent, TKey>(string foreignKeyProperty, string? role = null)
        where TEntity : class, IEntity<TKey>
        where TParent : class, IEntity<TKey>
    {
        // Manual registration for custom relationships
        _parentCache.TryAdd(typeof(TEntity), (typeof(TParent), foreignKeyProperty));
        
        var childRelations = _childCache.GetOrAdd(typeof(TParent), _ => new List<RelationshipInfo>());
        childRelations.Add(new RelationshipInfo(typeof(TEntity), typeof(TParent), foreignKeyProperty, role));
    }
}
```

### 1.3 Entity Identity System (Replaces IdentityLink<T>)

**File**: `src/Sora.Data.Core/Identity/EntityIdentityLink.cs`
```csharp
using Sora.Data.Core.Model;

namespace Sora.Data.Core.Identity;

/// <summary>
/// Links external identifiers to canonical entity ULIDs.
/// Replaces Flow's IdentityLink<TModel> with provider-agnostic implementation.
/// </summary>
public sealed class EntityIdentityLink : Entity<EntityIdentityLink>
{
    // Composite key: "{system}|{adapter}|{externalId}|{entityType}"
    public string System { get; set; } = default!;
    public string Adapter { get; set; } = default!;  
    public string ExternalId { get; set; } = default!;
    public string EntityType { get; set; } = default!; // Fully qualified type name
    public string CanonicalId { get; set; } = default!;
    
    public bool Provisional { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    
    public static string CreateCompositeKey(string system, string adapter, string externalId, string entityType)
        => $"{system}|{adapter}|{externalId}|{entityType}";
}
```

**File**: `src/Sora.Data.Core/Identity/IIdentityResolutionService.cs`
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Identity;

public interface IIdentityResolutionService
{
    Task<string?> ResolveExternalIdAsync<TEntity, TKey>(
        string system,
        string adapter,
        string externalId,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    Task<Dictionary<string, string>> BatchResolveAsync<TEntity, TKey>(
        string system,
        string adapter,
        IEnumerable<string> externalIds,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    Task CreateLinkAsync<TEntity, TKey>(
        string system,
        string adapter,
        string externalId,
        TKey canonicalId,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
}
```

### 1.4 Deferred Resolution System (Replaces ParkedRecord<T>)

**File**: `src/Sora.Data.Core/Resolution/DeferredEntity.cs`
```csharp
using Sora.Data.Core.Model;
using System;
using System.Collections.Generic;

namespace Sora.Data.Core.Resolution;

/// <summary>
/// Entity waiting for deferred resolution.
/// Replaces Flow's ParkedRecord<TModel> with provider-agnostic implementation.
/// </summary>
public sealed class DeferredEntity<TEntity, TKey> : Entity<DeferredEntity<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
{
    public string EntityType { get; set; } = typeof(TEntity).AssemblyQualifiedName!;
    public string? SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ReasonCode { get; set; } = default!; // "PARENT_NOT_FOUND", etc.
    public TEntity EntityData { get; set; } = default!;
    public Dictionary<string, object?>? SourceMetadata { get; set; }
    public ResolutionEvidence? Evidence { get; set; }
    public string? CorrelationId { get; set; }
}

public record ResolutionEvidence(
    string? ParentKey,
    string? SourceSystem,
    string? AdapterName);
```

**File**: `src/Sora.Data.Core/Resolution/IDeferredResolutionService.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Resolution;

public interface IDeferredResolutionService
{
    Task DeferAsync<TEntity, TKey>(
        TEntity entity,
        string reasonCode,
        ResolutionEvidence? evidence = null,
        string? sourceId = null,
        string? correlationId = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    Task<IReadOnlyList<DeferredEntity<TEntity, TKey>>> GetDeferredAsync<TEntity, TKey>(
        string? reasonCode = null,
        int limit = 500,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    Task ResolveAsync<TEntity, TKey>(
        DeferredEntity<TEntity, TKey> deferredEntity,
        TEntity resolvedEntity,
        string? resolutionReason = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    Task<int> ProcessDeferredEntitiesAsync(CancellationToken ct = default);
}
```

## Step 2: Direct Migration - Remove Flow Components

### 2.1 Delete Flow-Specific Files

**Remove these files entirely:**
- `src/Sora.Flow.Core/Attributes/FlowAttributes.cs` (lines 39-57: ParentKeyAttribute)
- `src/Sora.Flow.Core/Infrastructure/FlowRegistry.cs` (lines 87-151: Parent methods)
- `src/Sora.Flow.Core/Services/ParentKeyResolutionService.cs` (entire file)
- `src/Sora.Flow.Core/Extensions/ParkedRecordExtensions.cs` (entire file)
- `src/Sora.Flow.Core/Model/Identity.cs` (entire file - IdentityLink<T>)

### 2.2 Update Flow ServiceCollectionExtensions

**File**: `src/Sora.Flow.Core/ServiceCollectionExtensions.cs` (Breaking Changes)
```csharp
public static IServiceCollection AddSoraFlow(this IServiceCollection services, Action<FlowOptions>? configure = null)
{
    // Remove these lines (ParentKey-specific registrations):
    // services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ParentKeyResolutionService>());
    // services.TryAddSingleton<ParentKeyResolutionService>();
    
    // Replace with Data-layer services:
    services.TryAddSingleton<IRelationshipMetadata, RelationshipMetadataService>();
    services.TryAddSingleton<IIdentityResolutionService, IdentityResolutionService>();
    services.TryAddSingleton<IDeferredResolutionService, DeferredResolutionService>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DeferredResolutionService>());
    
    // ... rest of existing Flow setup
}
```

**Remove ParentKey-specific orchestration logic** (lines 336-706 in current file):
```csharp
// DELETE: All ParentKey resolution logic in WriteToIntakeDefault pipeline
// DELETE: ParentKeyResolutionService trigger calls
// DELETE: FlowRegistry.GetEntityParent/GetValueObjectParent calls
// DELETE: IdentityLink resolution logic
// DELETE: ParkedRecord creation and healing
```

**Replace with clean Data-layer calls:**
```csharp
private async Task<CanonicalProjection> WriteToIntakeDefault(/* params */)
{
    // ... existing logic ...
    
    // Replace ParentKey resolution with Data-layer relationship validation
    var relationshipMetadata = _sp.GetRequiredService<IRelationshipMetadata>();
    var parentRelation = relationshipMetadata.GetParentRelationship(modelType);
    
    if (parentRelation.HasValue)
    {
        var deferredService = _sp.GetRequiredService<IDeferredResolutionService>();
        var identityService = _sp.GetRequiredService<IIdentityResolutionService>();
        
        // Extract parent key from payload
        var parentKeyValue = ExtractParentKeyFromPayload(dict, parentRelation.Value.ParentKeyPath);
        
        if (parentKeyValue != null)
        {
            // Try to resolve parent using Data-layer services
            var resolvedParentId = await TryResolveParentKey(parentRelation.Value.Parent, parentKeyValue, source, identityService);
            
            if (resolvedParentId == null)
            {
                // Defer for background resolution
                await deferredService.DeferAsync(
                    canonicalModel,
                    "PARENT_NOT_FOUND", 
                    new ResolutionEvidence(parentKeyValue, source, adapterName),
                    sourceId,
                    correlationId,
                    ct);
                    
                return new CanonicalProjection(/* deferred status */);
            }
            
            // Update model with resolved parent ID
            SetParentKeyInModel(canonicalModel, parentRelation.Value.ParentKeyPath, resolvedParentId);
        }
    }
    
    // ... continue with normal processing
}
```

## Step 3: Update Sample Projects (Breaking Changes)

### 3.1 Update Sample Entity Definitions

**File**: `samples/S8.Flow/S8.Flow.Shared/Reading.cs`
```csharp
using Sora.Flow.Model;
using Sora.Data.Core.Relationships; // NEW
using Sora.Core.Utilities.Ids;
using System.ComponentModel.DataAnnotations;

namespace S8.Flow.Shared;

public sealed class Reading : FlowValueObject<Reading>
{
    // BREAKING CHANGE: Replace ParentKey with EntityReference
    [Parent(typeof(Sensor))] // Was: [ParentKey(parent: typeof(Sensor))]
    public string SensorId { get; set; } = string.Empty;
    
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    [Timestamp]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
```

**File**: `samples/S8.Flow/S8.Flow.Shared/Sensor.cs`
```csharp
public sealed class Sensor : FlowEntity<Sensor>
{
    [Parent(typeof(Device))] // Was: [ParentKey(typeof(Device))]
    public string DeviceId { get; set; } = string.Empty;
    // ... rest unchanged
}
```

**File**: `samples/S8.Flow/S8.Flow.Shared/ControlCommand.cs`
```csharp
public sealed class ControlCommand : FlowEntity<ControlCommand>
{
    [Parent(typeof(Sensor), payloadPath: "SensorId")] // Was: [ParentKey(...)]
    public string SensorId { get; set; } = string.Empty;
    // ... rest unchanged
}
```

## Step 4: Implement Sora.Web Relationship Loading with _parent/_children Structure

### 4.1 Enhanced Response Format

The relationship response uses reserved `_parent` and `_children` keys to separate entity data from relationship data:

```json
{
  "id": "123",
  "total": 299.99,
  "customerId": "456",
  "categoryId": "c01",
  "_parent": {
    "customer": {
      "id": "456", 
      "name": "John Doe",
      "email": "john@example.com"
    },
    "category": {
      "id": "c01",
      "name": "Category 01"
    }
  },
  "_children": {
    "items": [
      {"id": "789", "name": "Product A", "quantity": 2}
    ],
    "payments": [
      {"id": "p001", "amount": 299.99, "method": "credit_card"}
    ]
  }
}
```

**Benefits of this structure:**
- ✅ **Clean Separation**: Entity data vs relationship data
- ✅ **Multiple Parents**: Natural support for multiple parent types  
- ✅ **Multiple Children**: Support for different child entity types
- ✅ **No Conflicts**: No naming conflicts with entity properties
- ✅ **Optional Loading**: Relationships only included when requested

### 4.2 Enhanced QueryOptions

**File**: `src/Sora.Web/Hooks/QueryOptions.cs` (Enhanced)
```csharp
public class QueryOptions
{
    // ... existing properties ...
    
    // NEW: Relationship loading support
    public List<string> IncludeRelationships { get; set; } = new();
    public Dictionary<string, object> RelationshipFilters { get; set; } = new();
    public bool EagerLoadRelationships { get; set; } = false;
}
```

### 4.2 Enhanced EntityController

**File**: `src/Sora.Web/Controllers/EntityController.cs` (Enhanced)
```csharp
protected virtual QueryOptions BuildOptions()
{
    var q = Request.Query;
    var opts = new QueryOptions
    {
        // ... existing options ...
    };
    
    // Parse ?with= parameter for relationship loading
    if (q.TryGetValue("with", out var withValue))
    {
        var withSpec = withValue.ToString();
        if (withSpec.StartsWith('[') && withSpec.EndsWith(']'))
        {
            opts.IncludeRelationships = JsonSerializer.Deserialize<List<string>>(withSpec) ?? new();
        }
        else
        {
            opts.IncludeRelationships = withSpec.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                              .Select(s => s.Trim()).ToList();
        }
    }
    
    return opts;
}

[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
    var opts = BuildOptions();
    var model = await Data<TEntity, TKey>.GetAsync(id!, ct);
    
    if (model == null) return NotFound();
    
    // Load relationships if requested
    if (opts.IncludeRelationships.Any())
    {
        model = await LoadRelationshipsAsync(model, opts.IncludeRelationships, ct);
    }
    
    // ... rest of method
}

private async Task<object> LoadRelationshipsAsync(TEntity entity, List<string> relationships, CancellationToken ct)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentData = new Dictionary<string, object>();
    var childrenData = new Dictionary<string, object>();
    
    foreach (var relationshipName in relationships)
    {
        try
        {
            if (await IsParentRelationship(relationshipName))
            {
                var parentEntity = await LoadParentByName(entity.Id, relationshipName, ct);
                if (parentEntity != null)
                {
                    parentData[relationshipName] = parentEntity;
                }
            }
            else if (await IsChildRelationship(relationshipName))
            {
                var childEntities = await LoadChildrenByName(entity.Id, relationshipName, ct);
                if (childEntities?.Any() == true)
                {
                    childrenData[relationshipName] = childEntities;
                }
            }
        }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetService<ILogger<EntityController<TEntity, TKey>>>();
            logger?.LogWarning(ex, "Failed to load relationship {Relationship} for entity {EntityId}", 
                relationshipName, entity.Id);
        }
    }
    
    // Create response with _parent/_children structure
    var response = new Dictionary<string, object>();
    
    // Add all entity properties  
    var entityJson = JsonSerializer.SerializeToElement(entity);
    foreach (var property in entityJson.EnumerateObject())
    {
        response[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText())!;
    }
    
    // Add relationship data using reserved keys
    if (parentData.Any())
    {
        response["_parent"] = parentData;
    }
    
    if (childrenData.Any())
    {
        response["_children"] = childrenData;
    }
    
    return response;
}

private async Task<bool> IsParentRelationship(string relationshipName)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentRelations = relationshipMetadata.GetParentRelationships(typeof(TEntity));
    
    return parentRelations.Any(r => 
        string.Equals(GetRelationshipName(r.ParentType), relationshipName, StringComparison.OrdinalIgnoreCase));
}

private async Task<bool> IsChildRelationship(string relationshipName)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var childRelations = relationshipMetadata.GetChildRelationships(typeof(TEntity));
    
    return childRelations.Any(r => 
        string.Equals(GetRelationshipName(r.EntityType), relationshipName, StringComparison.OrdinalIgnoreCase));
}

private async Task<object?> LoadParentByName(TKey entityId, string parentName, CancellationToken ct)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var parentRelations = relationshipMetadata.GetParentRelationships(typeof(TEntity));
    
    var parentRelation = parentRelations.FirstOrDefault(r => 
        string.Equals(GetRelationshipName(r.ParentType), parentName, StringComparison.OrdinalIgnoreCase));
    
    if (parentRelation == null) return null;
    
    // Get entity to extract parent ID
    var entity = await Data<TEntity, TKey>.GetAsync(entityId, ct);
    if (entity == null) return null;
    
    // Extract parent ID using reflection
    var parentIdProperty = typeof(TEntity).GetProperty(parentRelation.ForeignKeyProperty, 
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    
    if (parentIdProperty?.GetValue(entity) is not TKey parentId) return null;
    
    // Load parent entity
    var parentDataMethod = typeof(Data<,>).MakeGenericType(parentRelation.ParentType, typeof(TKey))
                                           .GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static);
    
    if (parentDataMethod == null) return null;
    
    var parentTask = (Task)parentDataMethod.Invoke(null, new object[] { parentId, ct })!;
    await parentTask;
    
    return GetTaskResult(parentTask);
}

private static string GetRelationshipName(Type type)
{
    // Convert type name to relationship name (e.g., "Customer" -> "customer")
    return type.Name.ToLowerInvariant();
}

private async Task<object?> LoadParentEntityAsync(TKey childId, Type parentType, CancellationToken ct)
{
    // Use reflection to call generic GetParentAsync method
    var method = typeof(Entity<,>).MakeGenericType(typeof(TEntity), typeof(TKey))
                                  .GetMethod("GetParentAsync")!
                                  .MakeGenericMethod(parentType);
    
    var task = (Task)method.Invoke(null, new object[] { childId, ct })!;
    await task;
    
    return GetTaskResult(task);
}

private async Task<IEnumerable<object>> LoadChildEntitiesAsync(TKey parentId, Type parentType, CancellationToken ct)
{
    var relationshipMetadata = HttpContext.RequestServices.GetRequiredService<IRelationshipMetadata>();
    var childRelationships = relationshipMetadata.GetChildRelationships(parentType);
    var allChildren = new List<object>();
    
    foreach (var childRelation in childRelationships)
    {
        var children = await LoadChildEntitiesOfTypeAsync(parentId, childRelation.EntityType, ct);
        allChildren.AddRange(children);
    }
    
    return allChildren;
}

private TEntity CreateCompositeResponse(TEntity entity, Dictionary<string, object> relationships)
{
    // For now, we'll use a simple approach of adding relationship data as metadata
    // In a production implementation, you might want to create a wrapper type or use JSON merging
    
    if (entity is Entity<TEntity, TKey> entityBase)
    {
        // Store relationships in entity metadata for serialization
        foreach (var kvp in relationships)
        {
            // This would require extending Entity<> to support relationship metadata
            // For now, we'll return the entity as-is and handle relationships at the API level
        }
    }
    
    return entity;
}
```

## Step 5: Update Service Registration

### 5.1 Sora.Data Service Registration

**File**: `src/Sora.Data.Core/Extensions/ServiceCollectionExtensions.cs`
```csharp
public static class DataRelationshipServiceCollectionExtensions
{
    public static IServiceCollection AddSoraDataRelationships(this IServiceCollection services)
    {
        services.TryAddSingleton<IRelationshipMetadata, RelationshipMetadataService>();
        services.TryAddSingleton<IIdentityResolutionService, IdentityResolutionService>();
        services.TryAddSingleton<IDeferredResolutionService, DeferredResolutionService>();
        
        // Register background service for deferred resolution
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(provider =>
            provider.GetRequiredService<IDeferredResolutionService>()));
        
        return services;
    }
}
```

### 5.2 Update Flow Registration

**File**: `src/Sora.Flow.Core/ServiceCollectionExtensions.cs` (Breaking Changes)
```csharp
public static IServiceCollection AddSoraFlow(this IServiceCollection services, Action<FlowOptions>? configure = null)
{
    // Ensure Data-layer relationship services are registered
    services.AddSoraDataRelationships();
    
    // Remove all ParentKey-specific registrations
    // Add Flow-specific services that depend on Data-layer relationships
    
    return services;
}
```

### 5.3 Update Web Registration

**File**: `src/Sora.Web/Extensions/ServiceCollectionExtensions.cs` (Enhanced)
```csharp
public static IServiceCollection AddSoraWeb(this IServiceCollection services, Action<SoraWebOptions>? configure = null)
{
    // Ensure Data-layer relationship services are available for Web controllers
    services.AddSoraDataRelationships();
    
    // ... existing Web registrations
    
    return services;
}
```

## Breaking Changes Summary

### Removed Components
- ❌ `ParentKeyAttribute` → Use `ParentAttribute`
- ❌ `FlowRegistry.GetEntityParent/GetValueObjectParent` → Use `IRelationshipMetadata`
- ❌ `ParentKeyResolutionService` → Use `IDeferredResolutionService`
- ❌ `IdentityLink<T>` → Use `EntityIdentityLink`
- ❌ `ParkedRecord<T>` → Use `DeferredEntity<T,TKey>`
- ❌ `ParkedRecordExtensions` → Use `IDeferredResolutionService.ResolveAsync`

### Updated Components
- 🔄 Sample projects must update attribute usage
- 🔄 Flow orchestration pipeline simplified (remove ParentKey-specific logic)
- 🔄 Service registrations use Data-layer services
- 🔄 Background resolution uses generic service instead of Flow-specific

### New Capabilities
- ✅ Cross-module relationship support (any Entity<> can have relationships)
- ✅ Sora.Web REST endpoints with `?with=` relationship loading
- ✅ Provider-agnostic identity resolution
- ✅ Unified deferred resolution across all modules
- ✅ Type-safe relationship navigation methods on Entity<>

## Implementation Timeline

**Week 1-2: Core Data Layer**
- Create relationship system in Sora.Data.Core
- Implement identity resolution service
- Create deferred resolution service

**Week 3: Breaking Changes**
- Remove Flow-specific ParentKey components
- Update Flow orchestration pipeline
- Update service registrations

**Week 4: Sample Updates**
- Update all sample projects with new attributes
- Test Flow functionality with Data-layer services
- Verify background resolution works

**Week 5: Web Integration**
- Enhance EntityController with relationship loading
- Add `?with=` parameter support
- Test GraphQL-like REST queries

**Week 6: Testing & Documentation**
- Comprehensive testing of new system
- Update documentation
- Performance benchmarking

This direct migration approach achieves a clean, unified architecture without technical debt from compatibility layers. The breaking changes are justified by the significant architectural improvements and new cross-module capabilities.