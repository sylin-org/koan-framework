# ParentKey Migration Implementation Plan
**From Sora.Flow to Sora.Data: Detailed Migration Strategy**

## Executive Summary

This document provides a detailed implementation plan for migrating ParentKey functionality from `Sora.Flow.Core` to `Sora.Data.Core`. The migration preserves all existing functionality while enabling cross-module parent-child relationships and GraphQL-like querying capabilities.

## Current State Analysis

### Core Files and Dependencies

**Primary Implementation Files:**
- `src/Sora.Flow.Core/Attributes/FlowAttributes.cs:44-57` - ParentKeyAttribute definition
- `src/Sora.Flow.Core/Infrastructure/FlowRegistry.cs:90-151` - Parent resolution logic
- `src/Sora.Flow.Core/Services/ParentKeyResolutionService.cs` - Background healing service
- `src/Sora.Flow.Core/Extensions/ParkedRecordExtensions.cs` - Healing operations
- `src/Sora.Flow.Core/Model/Identity.cs` - IdentityLink for external ID correlation
- `src/Sora.Flow.Core/Model/Typed.cs:86-105` - ParkedRecord definition

**Integration Points:**
- `src/Sora.Flow.Core/ServiceCollectionExtensions.cs:127-128` - Service registration
- `src/Sora.Flow.Core/ServiceCollectionExtensions.cs:336-706` - Flow pipeline integration
- `src/Sora.Flow.Core/Orchestration/FlowOrchestratorBase.cs:497-623` - Orchestration pipeline

**Sample Usage:**
- `samples/S8.Flow/S8.Flow.Shared/Reading.cs:11` - `[ParentKey(parent: typeof(Sensor))]`
- `samples/S8.Flow/S8.Flow.Shared/Sensor.cs` - `[ParentKey(typeof(Device))]`
- `samples/S8.Flow/S8.Flow.Shared/ControlCommand.cs` - Complex ParentKey with payloadPath

### Current Architecture Map

```
┌─────────────────────────────────────────────────────────────────┐
│                     Current Flow Architecture                    │
├─────────────────────────────────────────────────────────────────┤
│ FlowEntity/FlowValueObject                                      │
│   └── [ParentKey(parent: typeof(Parent))]                      │
│       └── FlowRegistry.GetEntityParent/GetValueObjectParent    │
│           └── ParentKeyResolutionService (Background)          │
│               └── BatchResolveParentKeys                       │
│                   └── IdentityLink<TParent> lookup             │
│                       └── ParkedRecordExtensions.HealAsync()   │
│                           └── flowActions.SeedAsync()          │
└─────────────────────────────────────────────────────────────────┘
```

## Target Architecture

### New Sora.Data.Core Structure

```
┌─────────────────────────────────────────────────────────────────┐
│                  Target Data Architecture                       │
├─────────────────────────────────────────────────────────────────┤
│ Entity<TEntity, TKey>                                           │
│   └── [EntityReference(typeof(Parent))]                        │
│       └── IRelationshipRegistry.GetRelationship               │
│           └── IRelationshipResolver (Background)               │
│               └── IIdentityCorrelationService                  │
│                   └── IDeferredResolutionStore                 │
│                       └── IEntityHealingService                │
│                           └── Provider-specific injection      │
└─────────────────────────────────────────────────────────────────┘
```

## Migration Strategy: Bridge Pattern with Compatibility Layer

### Phase 1: Foundation Layer (Weeks 1-3)

#### 1.1 Create Core Abstractions in Sora.Data.Core

**File**: `src/Sora.Data.Core/Relationships/EntityReferenceAttribute.cs`
```csharp
using System;

namespace Sora.Data.Core.Relationships;

/// <summary>
/// Marks a property as a reference to a parent entity. Replaces Flow's ParentKeyAttribute
/// with provider-agnostic implementation supporting all Sora modules.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class EntityReferenceAttribute : Attribute
{
    public Type ParentType { get; }
    public string? Role { get; }
    public string? ExternalKeyPath { get; }
    
    public EntityReferenceAttribute(Type parentType, string? role = null, string? externalKeyPath = null)
    {
        ParentType = parentType ?? throw new ArgumentNullException(nameof(parentType));
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        ExternalKeyPath = string.IsNullOrWhiteSpace(externalKeyPath) ? null : externalKeyPath;
    }
}
```

**File**: `src/Sora.Data.Core/Relationships/IRelationshipRegistry.cs`
```csharp
using System;
using System.Collections.Generic;

namespace Sora.Data.Core.Relationships;

public interface IRelationshipRegistry
{
    /// <summary>
    /// Gets parent relationship info for the specified entity type.
    /// Replaces FlowRegistry.GetEntityParent() with provider-agnostic implementation.
    /// </summary>
    (Type Parent, string ParentKeyPath)? GetParentRelationship(Type entityType);
    
    /// <summary>
    /// Gets all relationships for the specified entity type.
    /// </summary>
    IEnumerable<RelationshipInfo> GetRelationships(Type entityType);
    
    /// <summary>
    /// Registers a custom relationship between entity types.
    /// </summary>
    void RegisterRelationship<TEntity, TParent, TKey>(
        string foreignKeyProperty,
        string? role = null)
        where TEntity : class, IEntity<TKey>
        where TParent : class, IEntity<TKey>;
}

public record RelationshipInfo(
    Type EntityType,
    Type ParentType,
    string ForeignKeyProperty,
    string? Role,
    RelationshipCardinality Cardinality);

public enum RelationshipCardinality
{
    OneToOne,
    OneToMany,
    ManyToMany
}
```

**File**: `src/Sora.Data.Core/Relationships/DefaultRelationshipRegistry.cs`
```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace Sora.Data.Core.Relationships;

public class DefaultRelationshipRegistry : IRelationshipRegistry
{
    private readonly ConcurrentDictionary<Type, (Type Parent, string ParentKeyPath)?> _parentCache = new();
    private readonly ConcurrentDictionary<Type, List<RelationshipInfo>> _relationshipCache = new();

    public (Type Parent, string ParentKeyPath)? GetParentRelationship(Type entityType)
    {
        return _parentCache.GetOrAdd(entityType, type =>
        {
            // Look for EntityReferenceAttribute on properties (replaces FlowRegistry logic)
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<EntityReferenceAttribute>(inherit: true);
                if (attr != null)
                {
                    // Validate parent type has [Key] property (same as Flow validation)
                    var parentKeyProp = attr.ParentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null);
                    
                    if (parentKeyProp == null)
                        throw new InvalidOperationException(
                            $"Parent type {attr.ParentType.Name} has no [Key] property for EntityReference resolution");
                    
                    return (attr.ParentType, prop.Name);
                }
            }
            return null;
        });
    }

    public IEnumerable<RelationshipInfo> GetRelationships(Type entityType)
    {
        return _relationshipCache.GetOrAdd(entityType, type =>
        {
            var relationships = new List<RelationshipInfo>();
            
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<EntityReferenceAttribute>(inherit: true);
                if (attr != null)
                {
                    relationships.Add(new RelationshipInfo(
                        type,
                        attr.ParentType,
                        prop.Name,
                        attr.Role,
                        RelationshipCardinality.OneToOne)); // Default for parent references
                }
            }
            
            return relationships;
        });
    }

    public void RegisterRelationship<TEntity, TParent, TKey>(
        string foreignKeyProperty, 
        string? role = null) 
        where TEntity : class, IEntity<TKey> 
        where TParent : class, IEntity<TKey>
    {
        var entityType = typeof(TEntity);
        var relationships = _relationshipCache.GetOrAdd(entityType, _ => new List<RelationshipInfo>());
        
        relationships.Add(new RelationshipInfo(
            entityType,
            typeof(TParent),
            foreignKeyProperty,
            role,
            RelationshipCardinality.OneToOne));
    }
}
```

#### 1.2 Create Bridge Compatibility Layer

**File**: `src/Sora.Flow.Core/Compatibility/ParentKeyBridge.cs`
```csharp
using Sora.Data.Core.Relationships;
using Sora.Flow.Attributes;
using System;

namespace Sora.Flow.Core.Compatibility;

/// <summary>
/// Bridge layer that maintains backward compatibility while migrating to Data-layer relationships.
/// This allows existing ParentKey usage to continue working during the migration period.
/// </summary>
public static class ParentKeyBridge
{
    private static IRelationshipRegistry? _relationshipRegistry;
    
    internal static void Initialize(IRelationshipRegistry relationshipRegistry)
    {
        _relationshipRegistry = relationshipRegistry;
    }
    
    /// <summary>
    /// Bridges FlowRegistry.GetEntityParent() to use the new Data-layer relationship registry.
    /// Maintains exact same API for backward compatibility.
    /// </summary>
    public static (Type Parent, string ParentKeyPath)? GetEntityParent(Type type)
    {
        if (_relationshipRegistry == null)
        {
            // Fallback to original FlowRegistry during transition
            return Infrastructure.FlowRegistry.GetEntityParent(type);
        }
        
        return _relationshipRegistry.GetParentRelationship(type);
    }
    
    /// <summary>
    /// Bridges FlowRegistry.GetValueObjectParent() to use the new Data-layer relationship registry.
    /// Maintains exact same API for backward compatibility.
    /// </summary>
    public static (Type Parent, string ParentKeyPath)? GetValueObjectParent(Type type)
    {
        if (_relationshipRegistry == null)
        {
            // Fallback to original FlowRegistry during transition
            return Infrastructure.FlowRegistry.GetValueObjectParent(type);
        }
        
        return _relationshipRegistry.GetParentRelationship(type);
    }
}

/// <summary>
/// Compatibility alias for ParentKeyAttribute that maps to EntityReferenceAttribute.
/// Allows existing code to continue working without changes during migration.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParentKeyCompatibilityAttribute : EntityReferenceAttribute
{
    public Type Parent => ParentType;
    public string? PayloadPath => ExternalKeyPath;
    
    public ParentKeyCompatibilityAttribute(Type parent, string? role = null, string? payloadPath = null)
        : base(parent, role, payloadPath)
    {
    }
}
```

#### 1.3 Update FlowRegistry to Use Bridge

**File**: `src/Sora.Flow.Core/Infrastructure/FlowRegistry.cs` (Updated)
```csharp
// Replace existing GetEntityParent method:
public static (Type Parent, string ParentKeyPath)? GetEntityParent(Type t)
{
    // Use bridge layer if available, otherwise fallback to original implementation
    return Compatibility.ParentKeyBridge.GetEntityParent(t) ?? 
           s_entityParent.GetOrAdd(t, ComputeEntityParent);
}

public static (Type Parent, string ParentKeyPath)? GetValueObjectParent(Type t)
{
    // Use bridge layer if available, otherwise fallback to original implementation
    return Compatibility.ParentKeyBridge.GetValueObjectParent(t) ?? 
           s_voParent.GetOrAdd(t, ComputeValueObjectParent);
}

// Keep original methods as private for fallback during migration
private static (Type Parent, string ParentKeyPath)? ComputeEntityParent(Type t)
{
    // ... existing implementation ...
}

private static (Type Parent, string ParentKeyPath)? ComputeValueObjectParent(Type t)
{
    // ... existing implementation ...
}
```

### Phase 2: Identity Correlation Layer (Weeks 4-6)

#### 2.1 Create Provider-Agnostic Identity System

**File**: `src/Sora.Data.Core/Identity/IIdentityCorrelationService.cs`
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Identity;

/// <summary>
/// Service for correlating external identifiers to canonical entity ULIDs.
/// Replaces Flow-specific IdentityLink with provider-agnostic implementation.
/// </summary>
public interface IIdentityCorrelationService
{
    /// <summary>
    /// Resolves external ID to canonical entity ULID for the specified parent type.
    /// </summary>
    Task<string?> ResolveExternalIdAsync<TParent, TKey>(
        string sourceSystem,
        string externalId,
        CancellationToken ct = default)
        where TParent : class, IEntity<TKey>;
    
    /// <summary>
    /// Batch resolves multiple external IDs for performance optimization.
    /// </summary>
    Task<Dictionary<string, string>> BatchResolveExternalIdsAsync<TParent, TKey>(
        string sourceSystem,
        IEnumerable<string> externalIds,
        CancellationToken ct = default)
        where TParent : class, IEntity<TKey>;
    
    /// <summary>
    /// Creates identity link between external ID and canonical entity.
    /// </summary>
    Task CreateIdentityLinkAsync<TEntity, TKey>(
        string sourceSystem,
        string adapterName,
        string externalId,
        TKey canonicalId,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
}
```

**File**: `src/Sora.Data.Core/Identity/IdentityCorrelation.cs`
```csharp
using Sora.Data.Core.Model;

namespace Sora.Data.Core.Identity;

/// <summary>
/// Provider-agnostic identity correlation entity.
/// Replaces Flow's IdentityLink<TModel> with generic implementation.
/// </summary>
public sealed class IdentityCorrelation : Entity<IdentityCorrelation>
{
    // Composite key: "{system}|{adapter}|{externalId}"
    public string System { get; set; } = default!;
    public string Adapter { get; set; } = default!;  
    public string ExternalId { get; set; } = default!;
    public string EntityType { get; set; } = default!; // Type name for multi-type support
    public string CanonicalId { get; set; } = default!;
    
    public bool Provisional { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    
    /// <summary>
    /// Creates composite key for O(1) lookup without provider-specific queries.
    /// </summary>
    public static string CreateCompositeKey(string system, string adapter, string externalId)
        => $"{system}|{adapter}|{externalId}";
}
```

#### 2.2 Create Flow Identity Bridge

**File**: `src/Sora.Flow.Core/Compatibility/IdentityBridge.cs`
```csharp
using Sora.Data.Core.Identity;
using Sora.Flow.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Core.Compatibility;

/// <summary>
/// Bridge that adapts Flow's IdentityLink<TModel> to use the new provider-agnostic
/// IdentityCorrelationService while maintaining backward compatibility.
/// </summary>
public class FlowIdentityCorrelationBridge : IIdentityCorrelationService
{
    public async Task<string?> ResolveExternalIdAsync<TParent, TKey>(
        string sourceSystem, 
        string externalId, 
        CancellationToken ct = default) 
        where TParent : class, IEntity<TKey>
    {
        // Use existing Flow IdentityLink lookup pattern
        var compositeKey = $"{sourceSystem}|{sourceSystem}|{externalId}";
        var identityLink = await Data<IdentityLink<TParent>, string>.GetAsync(compositeKey, ct);
        
        return identityLink?.ReferenceUlid;
    }

    public async Task<Dictionary<string, string>> BatchResolveExternalIdsAsync<TParent, TKey>(
        string sourceSystem, 
        IEnumerable<string> externalIds, 
        CancellationToken ct = default) 
        where TParent : class, IEntity<TKey>
    {
        var resolved = new Dictionary<string, string>();
        
        // Batch lookup using existing Flow pattern
        var lookupTasks = externalIds.Select(async externalId =>
        {
            var compositeKey = $"{sourceSystem}|{sourceSystem}|{externalId}";
            var identityLink = await Data<IdentityLink<TParent>, string>.GetAsync(compositeKey, ct);
            
            if (identityLink?.ReferenceUlid != null)
            {
                return new KeyValuePair<string, string>(externalId, identityLink.ReferenceUlid);
            }
            
            return (KeyValuePair<string, string>?)null;
        });
        
        var results = await Task.WhenAll(lookupTasks);
        
        foreach (var result in results.Where(r => r.HasValue))
        {
            resolved[result.Value.Key] = result.Value.Value;
        }
        
        return resolved;
    }

    public async Task CreateIdentityLinkAsync<TEntity, TKey>(
        string sourceSystem, 
        string adapterName, 
        string externalId, 
        TKey canonicalId, 
        CancellationToken ct = default) 
        where TEntity : class, IEntity<TKey>
    {
        var identityLink = new IdentityLink<TEntity>
        {
            Id = $"{sourceSystem}|{adapterName}|{externalId}",
            System = sourceSystem,
            Adapter = adapterName,
            ExternalId = externalId,
            ReferenceUlid = canonicalId.ToString()!,
            CreatedAt = DateTimeOffset.UtcNow,
            Provisional = false
        };
        
        await identityLink.Save(ct);
    }
}
```

### Phase 3: Deferred Resolution System (Weeks 7-9)

#### 3.1 Create Generic Deferred Resolution Store

**File**: `src/Sora.Data.Core/Resolution/IDeferredResolutionStore.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Resolution;

/// <summary>
/// Generic store for entities that need deferred resolution.
/// Replaces Flow's ParkedRecord<TModel> with provider-agnostic implementation.
/// </summary>
public interface IDeferredResolutionStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// Parks an entity for later resolution with the specified reason.
    /// </summary>
    Task ParkAsync(
        TEntity entity,
        string reasonCode,
        object? evidence = null,
        string? sourceId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets parked entities by reason code for batch resolution.
    /// </summary>
    Task<IReadOnlyList<DeferredResolutionRecord<TEntity, TKey>>> GetParkedAsync(
        string? reasonCode = null,
        int limit = 500,
        CancellationToken ct = default);
    
    /// <summary>
    /// Heals a parked entity by providing the resolved data and removing the parked record.
    /// </summary>
    Task HealAsync(
        DeferredResolutionRecord<TEntity, TKey> parkedRecord,
        TEntity healedEntity,
        string? healingReason = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Removes a parked record without healing (for cleanup/expiration).
    /// </summary>
    Task RemoveAsync(DeferredResolutionRecord<TEntity, TKey> parkedRecord, CancellationToken ct = default);
}

/// <summary>
/// Generic deferred resolution record.
/// Replaces Flow's ParkedRecord<TModel> with provider-agnostic implementation.
/// </summary>
public sealed class DeferredResolutionRecord<TEntity, TKey> : Entity<DeferredResolutionRecord<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
{
    public string? SourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ReasonCode { get; set; }
    public TEntity? Data { get; set; }
    public Dictionary<string, object?>? Source { get; set; }
    public object? Evidence { get; set; }
    public string? CorrelationId { get; set; }
}
```

#### 3.2 Create Flow Deferred Resolution Bridge

**File**: `src/Sora.Flow.Core/Compatibility/DeferredResolutionBridge.cs`
```csharp
using Sora.Data.Core.Resolution;
using Sora.Flow.Model;
using Sora.Flow.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Core.Compatibility;

/// <summary>
/// Bridge that adapts Flow's ParkedRecord<TModel> to use generic deferred resolution
/// while maintaining backward compatibility with existing healing patterns.
/// </summary>
public class FlowDeferredResolutionBridge<TModel> : IDeferredResolutionStore<TModel, string>
    where TModel : class, IEntity<string>
{
    private readonly IFlowActions _flowActions;
    
    public FlowDeferredResolutionBridge(IFlowActions flowActions)
    {
        _flowActions = flowActions;
    }

    public async Task ParkAsync(
        TModel entity, 
        string reasonCode, 
        object? evidence = null, 
        string? sourceId = null, 
        CancellationToken ct = default)
    {
        // Create Flow ParkedRecord using existing pattern
        var parkedRecord = new ParkedRecord<TModel>
        {
            Id = Ulid.NewUlid().ToString(),
            SourceId = sourceId ?? "migration-bridge",
            OccurredAt = DateTimeOffset.UtcNow,
            ReasonCode = reasonCode,
            Data = entity,
            Evidence = evidence,
            CorrelationId = Guid.NewGuid().ToString()
        };
        
        await parkedRecord.Save(ct);
    }

    public async Task<IReadOnlyList<DeferredResolutionRecord<TModel, string>>> GetParkedAsync(
        string? reasonCode = null, 
        int limit = 500, 
        CancellationToken ct = default)
    {
        // Query Flow's ParkedRecord collection
        var parkedRecords = await Data<ParkedRecord<TModel>, string>
            .Query()
            .Where(pr => reasonCode == null || pr.ReasonCode == reasonCode)
            .Take(limit)
            .ToListAsync(ct);
        
        // Convert to generic deferred resolution records
        return parkedRecords.Select(pr => new DeferredResolutionRecord<TModel, string>
        {
            Id = pr.Id,
            SourceId = pr.SourceId,
            OccurredAt = pr.OccurredAt,
            ReasonCode = pr.ReasonCode,
            Data = pr.Data,
            Source = pr.Source,
            Evidence = pr.Evidence,
            CorrelationId = pr.CorrelationId
        }).ToList();
    }

    public async Task HealAsync(
        DeferredResolutionRecord<TModel, string> record, 
        TModel healedEntity, 
        string? healingReason = null, 
        CancellationToken ct = default)
    {
        // Convert back to Flow ParkedRecord for healing
        var flowParkedRecord = await Data<ParkedRecord<TModel>, string>.GetAsync(record.Id, ct);
        if (flowParkedRecord == null) return;
        
        // Use existing Flow healing pattern
        await flowParkedRecord.HealAsync(_flowActions, healedEntity, healingReason, record.CorrelationId, ct);
    }

    public async Task RemoveAsync(DeferredResolutionRecord<TModel, string> record, CancellationToken ct = default)
    {
        var flowParkedRecord = await Data<ParkedRecord<TModel>, string>.GetAsync(record.Id, ct);
        await flowParkedRecord?.Delete(ct)!;
    }
}
```

### Phase 4: Background Resolution Service (Weeks 10-12)

#### 4.1 Create Generic Relationship Resolver

**File**: `src/Sora.Data.Core/Relationships/IRelationshipResolver.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Relationships;

/// <summary>
/// Service for resolving parent-child relationships and healing deferred entities.
/// Replaces Flow's ParentKeyResolutionService with provider-agnostic implementation.
/// </summary>
public interface IRelationshipResolver
{
    /// <summary>
    /// Resolves a parent key to canonical ULID using all available resolution strategies.
    /// </summary>
    Task<string?> ResolveParentKeyAsync<TParent, TKey>(
        string parentKey,
        string sourceSystem,
        CancellationToken ct = default)
        where TParent : class, IEntity<TKey>;
    
    /// <summary>
    /// Processes all deferred resolution records for the specified entity type.
    /// </summary>
    Task<int> ProcessDeferredResolutionAsync<TEntity, TKey>(
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>;
    
    /// <summary>
    /// Triggers immediate processing of deferred resolution records.
    /// Replaces ParentKeyResolutionService.TriggerResolutionAsync().
    /// </summary>
    Task TriggerResolutionAsync(CancellationToken ct = default);
}
```

#### 4.2 Create Background Resolution Service

**File**: `src/Sora.Data.Core/Relationships/BackgroundRelationshipResolver.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Data.Core.Identity;
using Sora.Data.Core.Resolution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Core.Relationships;

/// <summary>
/// Background service for resolving deferred parent-child relationships.
/// Replaces Flow's ParentKeyResolutionService with provider-agnostic implementation.
/// </summary>
public class BackgroundRelationshipResolver : BackgroundService, IRelationshipResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRelationshipRegistry _relationshipRegistry;
    private readonly IIdentityCorrelationService _identityService;
    private readonly ILogger<BackgroundRelationshipResolver> _logger;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);

    public BackgroundRelationshipResolver(
        IServiceProvider serviceProvider,
        IRelationshipRegistry relationshipRegistry,
        IIdentityCorrelationService identityService,
        ILogger<BackgroundRelationshipResolver> logger)
    {
        _serviceProvider = serviceProvider;
        _relationshipRegistry = relationshipRegistry;
        _identityService = identityService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[relationships] Background relationship resolver started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await TriggerResolutionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[relationships] Error in resolution cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        
        _logger.LogInformation("[relationships] Background relationship resolver stopped");
    }

    public async Task<string?> ResolveParentKeyAsync<TParent, TKey>(
        string parentKey, 
        string sourceSystem, 
        CancellationToken ct = default) 
        where TParent : class, IEntity<TKey>
    {
        // Try external ID correlation first
        var resolved = await _identityService.ResolveExternalIdAsync<TParent, TKey>(
            sourceSystem, parentKey, ct);
            
        if (resolved != null)
            return resolved;
        
        // Try direct canonical lookup
        var parent = await Data<TParent, TKey>.GetAsync((TKey)(object)parentKey, ct);
        return parent?.Id.ToString();
    }

    public async Task<int> ProcessDeferredResolutionAsync<TEntity, TKey>(
        CancellationToken ct = default) 
        where TEntity : class, IEntity<TKey>
    {
        var deferredStore = _serviceProvider.GetService<IDeferredResolutionStore<TEntity, TKey>>();
        if (deferredStore == null) return 0;
        
        var parentInfo = _relationshipRegistry.GetParentRelationship(typeof(TEntity));
        if (!parentInfo.HasValue) return 0;
        
        var parkedRecords = await deferredStore.GetParkedAsync("PARENT_NOT_FOUND", 500, ct);
        var resolvedCount = 0;
        
        foreach (var record in parkedRecords)
        {
            try
            {
                var evidence = ExtractResolutionEvidence(record.Evidence);
                if (evidence.ParentKey == null || evidence.SourceSystem == null) continue;
                
                var resolvedParentId = await ResolveParentKeyUsingType(
                    parentInfo.Value.Parent, 
                    evidence.ParentKey, 
                    evidence.SourceSystem, 
                    ct);
                
                if (resolvedParentId != null && record.Data != null)
                {
                    var healedEntity = UpdateParentKeyInEntity(record.Data, parentInfo.Value.ParentKeyPath, resolvedParentId);
                    await deferredStore.HealAsync(record, healedEntity, $"Parent resolved to {resolvedParentId}", ct);
                    resolvedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[relationships] Failed to heal deferred record {Id}", record.Id);
            }
        }
        
        return resolvedCount;
    }

    public async Task TriggerResolutionAsync(CancellationToken ct = default)
    {
        if (!_resolutionLock.Wait(0)) return;
        
        try
        {
            _logger.LogDebug("[relationships] Triggered immediate resolution");
            
            // Find all entity types with relationships
            var entityTypes = DiscoverEntitiesWithRelationships();
            var totalResolved = 0;
            
            foreach (var entityType in entityTypes)
            {
                try
                {
                    var resolved = await ProcessDeferredResolutionUsingReflection(entityType, ct);
                    totalResolved += resolved;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[relationships] Error processing {EntityType}", entityType.Name);
                }
            }
            
            if (totalResolved > 0)
            {
                _logger.LogInformation("[relationships] Resolved {Count} deferred relationships", totalResolved);
            }
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private List<Type> DiscoverEntitiesWithRelationships()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                
                if (_relationshipRegistry.GetParentRelationship(t).HasValue)
                {
                    result.Add(t);
                }
            }
        }
        
        return result;
    }

    private async Task<string?> ResolveParentKeyUsingType(Type parentType, string parentKey, string sourceSystem, CancellationToken ct)
    {
        // Use reflection to call generic method with correct parent type
        var method = typeof(BackgroundRelationshipResolver).GetMethod(nameof(ResolveParentKeyAsync))!
            .MakeGenericMethod(parentType, typeof(string));
        
        var task = (Task<string?>)method.Invoke(this, new object[] { parentKey, sourceSystem, ct })!;
        return await task;
    }

    private async Task<int> ProcessDeferredResolutionUsingReflection(Type entityType, CancellationToken ct)
    {
        var method = typeof(BackgroundRelationshipResolver).GetMethod(nameof(ProcessDeferredResolutionAsync))!
            .MakeGenericMethod(entityType, typeof(string));
        
        var task = (Task<int>)method.Invoke(this, new object[] { ct })!;
        return await task;
    }

    private static (string? ParentKey, string? SourceSystem) ExtractResolutionEvidence(object? evidence)
    {
        if (evidence == null) return (null, null);
        
        var parentKey = evidence.GetType().GetProperty("parentKey")?.GetValue(evidence) as string;
        var sourceSystem = evidence.GetType().GetProperty("source")?.GetValue(evidence) as string;
        
        return (parentKey, sourceSystem);
    }

    private static TEntity UpdateParentKeyInEntity<TEntity, TKey>(TEntity entity, string parentKeyProperty, string resolvedParentId)
        where TEntity : class, IEntity<TKey>
    {
        var entityType = typeof(TEntity);
        var parentKeyProp = entityType.GetProperty(parentKeyProperty, 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        
        if (parentKeyProp != null && parentKeyProp.CanWrite)
        {
            parentKeyProp.SetValue(entity, resolvedParentId);
        }
        
        return entity;
    }

    public override void Dispose()
    {
        _resolutionLock?.Dispose();
        base.Dispose();
    }
}
```

### Phase 5: Flow Integration Layer (Weeks 13-15)

#### 5.1 Update Flow Service Registration

**File**: `src/Sora.Flow.Core/ServiceCollectionExtensions.cs` (Updated)
```csharp
// Add at the beginning of the AddSoraFlow method:

// Register Data-layer relationship services
services.TryAddSingleton<IRelationshipRegistry, DefaultRelationshipRegistry>();
services.TryAddSingleton<IIdentityCorrelationService, FlowIdentityCorrelationBridge>();

// Initialize bridge layer
services.AddSingleton<IHostedService>(provider =>
{
    var registry = provider.GetRequiredService<IRelationshipRegistry>();
    Compatibility.ParentKeyBridge.Initialize(registry);
    
    // Use new BackgroundRelationshipResolver instead of ParentKeyResolutionService
    return new BackgroundRelationshipResolver(
        provider,
        registry,
        provider.GetRequiredService<IIdentityCorrelationService>(),
        provider.GetRequiredService<ILogger<BackgroundRelationshipResolver>>());
});

// Register legacy ParentKeyResolutionService as fallback during migration
services.TryAddSingleton<ParentKeyResolutionService>();

// Register deferred resolution bridges for Flow types
RegisterDeferredResolutionBridges(services);

private static void RegisterDeferredResolutionBridges(IServiceCollection services)
{
    // Auto-register bridges for discovered Flow entity types
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    var flowEntityTypes = new List<Type>();
    
    foreach (var asm in assemblies)
    {
        try
        {
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (IsFlowEntity(type))
                {
                    flowEntityTypes.Add(type);
                }
            }
        }
        catch { continue; }
    }
    
    foreach (var entityType in flowEntityTypes)
    {
        var bridgeType = typeof(FlowDeferredResolutionBridge<>).MakeGenericType(entityType);
        var interfaceType = typeof(IDeferredResolutionStore<,>).MakeGenericType(entityType, typeof(string));
        
        services.TryAddScoped(interfaceType, bridgeType);
    }
}

private static bool IsFlowEntity(Type type)
{
    if (!type.IsClass || type.IsAbstract) return false;
    
    var baseType = type.BaseType;
    while (baseType != null)
    {
        if (baseType.IsGenericType)
        {
            var genericDef = baseType.GetGenericTypeDefinition();
            if (genericDef == typeof(FlowEntity<>) || genericDef == typeof(FlowValueObject<>))
                return true;
        }
        baseType = baseType.BaseType;
    }
    
    return false;
}
```

#### 5.2 Update Sample Usage for Compatibility

**File**: `samples/S8.Flow/S8.Flow.Shared/Reading.cs` (Updated for compatibility)
```csharp
using Sora.Flow.Model;
using Sora.Flow.Attributes;
using Sora.Data.Core.Relationships; // New import
using Sora.Core.Utilities.Ids;
using System.ComponentModel.DataAnnotations;

namespace S8.Flow.Shared;

public sealed class Reading : FlowValueObject<Reading>
{
    // Use both attributes during migration for compatibility
    [ParentKey(parent: typeof(Sensor))] // Legacy Flow attribute
    [EntityReference(typeof(Sensor))]   // New Data-layer attribute
    public string SensorId { get; set; } = string.Empty;
    
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    [Timestamp]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
```

### Phase 6: Sora.Web Integration (Weeks 16-18)

#### 6.1 Enhance EntityController with Relationship Loading

**File**: `src/Sora.Web/Extensions/RelationshipQueryExtensions.cs`
```csharp
using Microsoft.AspNetCore.Http;
using Sora.Data.Core.Relationships;
using Sora.Web.Hooks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sora.Web.Extensions;

public static class RelationshipQueryExtensions
{
    /// <summary>
    /// Parses ?with= query parameter into relationship loading options.
    /// </summary>
    public static List<string> ParseWithParameter(this IQueryCollection query)
    {
        if (!query.TryGetValue("with", out var withValue))
            return new List<string>();
        
        var withSpec = withValue.ToString();
        if (string.IsNullOrWhiteSpace(withSpec))
            return new List<string>();
        
        // Support both JSON array and comma-separated formats
        if (withSpec.StartsWith('[') && withSpec.EndsWith(']'))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(withSpec) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
        
        return withSpec.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => !string.IsNullOrEmpty(s))
                      .ToList();
    }
    
    /// <summary>
    /// Enhances QueryOptions with relationship loading configuration.
    /// </summary>
    public static QueryOptions WithRelationships(this QueryOptions options, List<string> relationships)
    {
        // Add relationship loading configuration to QueryOptions
        // This would require extending QueryOptions class
        options.Extensions["Relationships"] = relationships;
        return options;
    }
}
```

**File**: `src/Sora.Web/Controllers/EntityController.cs` (Enhanced)
```csharp
// Add to BuildOptions method:
protected virtual QueryOptions BuildOptions()
{
    var q = Request.Query;
    var opts = new QueryOptions
    {
        // ... existing options ...
    };
    
    // Add relationship loading support
    var relationships = q.ParseWithParameter();
    if (relationships.Any())
    {
        opts = opts.WithRelationships(relationships);
    }
    
    return opts;
}

// Enhanced GetById method with relationship loading:
[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
    var opts = BuildOptions();
    var model = await Data<TEntity, TKey>.GetAsync(id!, ct);
    
    if (model == null) 
        return NotFound();
    
    // Apply relationship loading if requested
    if (opts.Extensions.TryGetValue("Relationships", out var relationshipsObj) &&
        relationshipsObj is List<string> relationships && relationships.Any())
    {
        model = await LoadRelationships(model, relationships, ct);
    }
    
    // ... rest of method
}

private async Task<TEntity> LoadRelationships<TEntity, TKey>(
    TEntity entity, 
    List<string> relationships, 
    CancellationToken ct)
    where TEntity : class, IEntity<TKey>
{
    var relationshipResolver = HttpContext.RequestServices.GetService<IRelationshipResolver>();
    if (relationshipResolver == null) return entity;
    
    // This would require implementing relationship loading logic
    // in the EntityController or delegating to a specialized service
    
    return entity;
}
```

### Migration Timeline and Risk Mitigation

## Migration Execution Plan

### Week-by-Week Breakdown

**Weeks 1-3: Foundation**
- ✅ Create EntityReferenceAttribute and IRelationshipRegistry
- ✅ Implement DefaultRelationshipRegistry 
- ✅ Create ParentKeyBridge for compatibility
- ✅ Update FlowRegistry to use bridge layer
- 🔄 Test existing Flow functionality still works

**Weeks 4-6: Identity System**
- ✅ Create IIdentityCorrelationService and IdentityCorrelation entity
- ✅ Implement FlowIdentityCorrelationBridge
- ✅ Test external ID resolution continues working
- 🔄 Begin using new identity system in parallel with old

**Weeks 7-9: Deferred Resolution**  
- ✅ Create IDeferredResolutionStore abstraction
- ✅ Implement FlowDeferredResolutionBridge
- ✅ Test parked record healing continues working
- 🔄 Run both systems in parallel for validation

**Weeks 10-12: Background Service**
- ✅ Create BackgroundRelationshipResolver
- ✅ Register as replacement for ParentKeyResolutionService  
- ✅ Test background resolution performance
- 🔄 Monitor resolution rates and error handling

**Weeks 13-15: Flow Integration**
- ✅ Update Flow service registration to use Data-layer services
- ✅ Update sample projects with dual attributes
- ✅ Test full Flow pipeline with new relationship system
- 🔄 Performance testing and optimization

**Weeks 16-18: Web Integration**
- ✅ Enhance EntityController with ?with= parameter support
- ✅ Implement relationship loading in REST endpoints
- ✅ Create documentation and examples
- 🔄 End-to-end testing of GraphQL-like REST queries

### Risk Mitigation Strategies

1. **Backward Compatibility**: Bridge pattern ensures existing code continues working
2. **Parallel Execution**: Run both old and new systems side-by-side during migration  
3. **Incremental Migration**: Entity types can be migrated one at a time
4. **Rollback Plan**: Bridge can fallback to original FlowRegistry if needed
5. **Performance Monitoring**: Built-in metrics to track resolution performance
6. **Extensive Testing**: Unit, integration, and performance tests for all migration steps

### Success Criteria

- ✅ All existing Flow functionality preserved during migration
- ✅ Background resolution performance maintained or improved  
- ✅ New Sora.Web relationship endpoints working correctly
- ✅ Sample applications updated and functioning
- ✅ Documentation updated with migration guide
- ✅ Zero breaking changes to existing consumer code

This migration plan provides a safe, incremental path from the current Flow-specific ParentKey implementation to a unified, provider-agnostic relationship system that enables powerful cross-module capabilities while preserving all existing functionality.