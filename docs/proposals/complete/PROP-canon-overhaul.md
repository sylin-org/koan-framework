# PROP: Koan.Canon Complete Overhaul - Local-First, Message-Optional Architecture

**Status**: Delivered (2025-10)
**Priority**: High
**Impact**: Breaking Change (Major Version Bump)
**Author**: Enterprise Architecture Analysis
**Date**: 2025-10-05

> **Implementation verification (2025-10):**
> - `CanonEntity<T>.Canonize(...)` ships in [`src/Koan.Canon.Domain/Model/CanonEntity.cs`](../../../../src/Koan.Canon.Domain/Model/CanonEntity.cs) providing the local-first API proposed here.
> - The transport-optional runtime and pipeline live in [`src/Koan.Canon.Domain/Runtime`](../../../../src/Koan.Canon.Domain/Runtime), delivering the decoupled architecture that removes messaging coupling.
> - Simplified storage models (`CanonIndex`, `CanonStage`) replace the legacy sprawl under [`src/Koan.Canon.Domain/Model`](../../../../src/Koan.Canon.Domain/Model).

> The remainder of this document captures the original problem analysis and rationale that preceded the shipped implementation.

---

## Executive Summary

The current Koan.Canon implementation violates core framework principles through **tight coupling to messaging infrastructure**, preventing simple local-first usage and creating deployment rigidity. This proposal presents a complete architectural overhaul that:

1. **Decouples canonization from transport** - Canonization logic is independent of messaging
2. **Enables local-first development** - `await device.Canonize(origin: "sourceA")` works immediately
3. **Simplifies data model** - Reduces from 10+ entities per model to 2-3
4. **Maintains distributed capability** - Optional messaging for centralized canonization
5. **Restores framework alignment** - Follows Entity<T> patterns and "Reference = Intent" principle

**Key Metric**: 90% reduction in cognitive overhead for basic canonization scenarios while preserving 100% of distributed capabilities.

---

## Current State Analysis

### Critical Issues

#### 1. **Messaging Coupling Violation** (Framework Principle: "Reference = Intent")

**Current Behavior**:
```csharp
// Adding Koan.Canon reference does NOT enable simple usage
public class Device : CanonEntity<Device> {
    public string Serial { get; set; } = "";
}

// REQUIRES messaging infrastructure setup:
services.AddKoan();
services.AddMessaging(); // ← Extra dependency
services.AddCanonOrchestrator(); // ← Background service

// REQUIRES understanding transport envelopes:
using (CanonContext.With("sourceSystemA")) {
    await device.Send(); // ← Messaging-only API
}
```

**Problem**: `CanonEntityExtensions.cs:47-87` hardcodes `MessagingInterceptors.RegisterForInterface<IDynamicCanonEntity>()`, forcing ALL Canon entities through queue infrastructure.

**Framework Violation**: Unlike `Entity<T>.Save()` which works immediately after adding Koan.Data, Canon entities require messaging infrastructure configuration before ANY functionality works.

---

#### 2. **Entity-First Pattern Violation** (Framework Principle: Entity-Scoped Behavior)

**Current State**:
- `Entity<T>` has: `Save()`, `Get()`, `All()`, `Query()` - all entity-scoped
- `CanonEntity<T>` has: NO canonization methods - must use external `.Send()` extension

**What Users Expect** (from framework consistency):
```csharp
var device = new Device { Serial = "DEV-001" };
await device.Canonize(origin: "sourceSystemA"); // ← Does not exist!
```

**What They Get**:
```csharp
await device.Send(); // ← Messaging extension, not entity behavior
```

**Problem**: `Typed.cs:16` - `CanonEntity<T>` is just a marker base class with zero canonization behavior.

---

#### 3. **Provider Transparency Violation** (Framework Principle: Deployment-Agnostic)

**Current State**: Canonization ONLY works through messaging
- Cannot test locally without message broker
- Cannot run in-process for simple scenarios
- Deployment architecture determines API availability

**Framework Violation**: Unlike Koan.Data (works anywhere) and Koan.Jobs (has `ExecuteAsync()` AND `QueueAsync()`), Canon forces distributed architecture for all scenarios.

---

### Architectural Debt Assessment

#### Data Model Complexity (10+ Entities Per Model)

**Current Storage Explosion**:
```
Device model creates:
  - Device (canonical entity)
  - StageRecord<Device> (intake)
  - KeyedRecord<Device> (post-association, same data as StageRecord!)
  - ParkedRecord<Device> (rejection queue)
  - KeyIndex<Device> (aggregation key index)
  - ReferenceItem<Device> (version tracking)
  - ProjectionTask<Device> (work queue)
  - CanonicalProjection<Device> (view storage)
  - LineageProjection<Device> (lineage view)
  - IdentityLink<Device> (external ID mapping)
  - PolicyState<Device> (policy decisions)

= 11 storage containers per model!
```

**Problem**: `ServiceCollectionExtensions.cs:161-191` uses custom `OverrideStorageNaming()` to create model-specific containers, breaking provider transparency.

**Data Duplication**: Same payload copied across `StageRecord` → `KeyedRecord` → `Device` (3x storage overhead).

---

#### Worker Polling Overhead

**Current Approach**: Background workers poll entity storage every 500ms-5s
```csharp
// ServiceCollectionExtensions.cs:574-880 - Association Worker
while (!stoppingToken.IsCancellationRequested) {
    var intakeRecords = await StageRecord<T>.Page(1, batch);
    foreach (var rec in intakeRecords) {
        // Process, create KeyIndex, ReferenceItem, etc.
        await KeyedRecord<T>.UpsertAsync(...);
        await StageRecord<T>.DeleteAsync(...);
    }
    await Task.Delay(500ms);
}
```

**Inefficiency**: Every canonization requires:
1. Write `StageRecord<T>` to DB
2. Background worker polls and reads record
3. Worker writes `KeyIndex`, `ReferenceItem`, `ProjectionTask`
4. Worker deletes `StageRecord`, writes `KeyedRecord`
5. Projection worker polls `ProjectionTask`
6. Projection worker reads all keyed records
7. Projection worker writes canonical entity + views

**Result**: 7+ database round-trips for a single entity canonization that should be 1-2 operations.

---

## Proposed Architecture

### Design Principles

1. **Local-First, Message-Optional**: Canonization works in-process by default, messaging is an enhancement
2. **Entity-Scoped API**: Canonization behavior lives on the entity, not external infrastructure
3. **Minimal Entities**: Metadata is embedded or indexed, not separate entities per model
4. **Provider Transparent**: Works identically across SQL, NoSQL, JSON, in-memory
5. **Separation of Concerns**: Canonization logic ≠ Transport mechanism

---

### New Developer Experience

#### Simple Local Canonization (90% of scenarios)

```csharp
// Just works after AddKoan() - no messaging required
public class Device : CanonEntity<Device> {
    [AggregationTag] public string Serial { get; set; } = "";
    public string Manufacturer { get; set; } = "";
}

// Usage - CLEAN and SIMPLE
var device = new Device {
    Id = "sourceA::device-001", // Source-specific ID
    Serial = "SN-12345",
    Manufacturer = "Acme Corp"
};

// Local canonization - no messaging needed!
var canonical = await device.Canonize(origin: "sourceA");
// Returns: Device with canonical GUID v7 Id + merged data

// Access canonical entity immediately
var loaded = await Device.Get(canonical.Id);
```

#### Distributed Canonization (Centralized orchestrator scenarios)

```csharp
// Same entity, different deployment
services.AddKoan();
services.AddMessaging(); // ← Optional enhancement

// Send to centralized canonizer
await device.SendForCanonization();
// OR: await device.Canonize(distributed: true);

// Processed by dedicated canonization service
// Client gets immediate response, processing happens async
```

---

### Core API Design

#### 1. CanonEntity<T> with Canonization Behavior

```csharp
// Koan.Canon.Core/Model/CanonEntity.cs
public abstract class CanonEntity<TModel> : Entity<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    /// <summary>
    /// Canonizes this entity, merging it with existing data from the same source domain.
    /// Returns the canonical entity with assigned Canonical ID (GUID v7).
    /// </summary>
    /// <param name="origin">Source system identifier (e.g., "erp-system", "crm-prod")</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<TModel> Canonize(string origin, CancellationToken ct = default)
    {
        var processor = GetCanonProcessor(); // From service provider
        return await processor.CanonizeAsync((TModel)this, origin, ct);
    }

    /// <summary>
    /// Sends entity to a centralized canonization orchestrator via messaging.
    /// Use when you have a dedicated canonization service handling multiple sources.
    /// </summary>
    public async Task SendForCanonization(CancellationToken ct = default)
    {
        var sender = GetCanonSender(); // Only available if messaging configured
        await sender.SendAsync((TModel)this, ct);
    }

    /// <summary>
    /// Canon metadata - framework managed, not for direct manipulation.
    /// Contains source mappings, version tracking, and aggregation state.
    /// </summary>
    [JsonProperty("__canon")]
    public CanonMetadata Metadata { get; internal set; } = new();
}

public class CanonMetadata
{
    public Dictionary<string, string> ExternalIds { get; set; } = new();
    public ulong Version { get; set; }
    public Dictionary<string, HashSet<string>> Sources { get; set; } = new();
    public Dictionary<string, string> PolicyDecisions { get; set; } = new();
}
```

---

#### 2. ICanonProcessor - Core Canonization Logic (Transport-Agnostic)

```csharp
// Koan.Canon.Core/Processing/ICanonProcessor.cs
public interface ICanonProcessor
{
    /// <summary>
    /// Canonizes an entity locally (in-process).
    /// </summary>
    Task<T> CanonizeAsync<T>(T entity, string origin, CancellationToken ct = default)
        where T : CanonEntity<T>, new();

    /// <summary>
    /// Retrieves the current canonical entity for given external ID.
    /// </summary>
    Task<T?> GetByExternalIdAsync<T>(string origin, string externalId, CancellationToken ct = default)
        where T : CanonEntity<T>, new();
}

// Default implementation - local processing
public class LocalCanonProcessor : ICanonProcessor
{
    public async Task<T> CanonizeAsync<T>(T entity, string origin, CancellationToken ct)
        where T : CanonEntity<T>, new()
    {
        // 1. Resolve or create canonical reference ID
        var canonId = await ResolveCanonicalId<T>(entity, origin, ct);

        // 2. Load existing canonical entity (if exists)
        var canonical = await Data<T, string>.GetAsync(canonId, ct) ?? new T { Id = canonId };

        // 3. Merge entity data into canonical
        await MergeEntity(canonical, entity, origin, ct);

        // 4. Execute Canon.OnUpdate handlers (if registered)
        if (Canon.HasHandler<T>())
        {
            var handler = Canon.GetHandler<T>();
            var metadata = new UpdateMetadata { SourceSystem = origin, Timestamp = DateTimeOffset.UtcNow };
            var result = await handler!(ref canonical, canonical, metadata);

            if (result.Action == UpdateAction.Skip)
                return canonical; // Don't save
        }

        // 5. Save canonical entity
        await canonical.Save(ct);

        // 6. Update external ID index
        await IndexExternalId<T>(origin, entity.Id, canonId, ct);

        return canonical;
    }

    private async Task<string> ResolveCanonicalId<T>(T entity, string origin, CancellationToken ct)
        where T : CanonEntity<T>, new()
    {
        // Check aggregation keys to find existing canonical entity
        var aggregationTags = CanonRegistry.GetAggregationTags(typeof(T));

        foreach (var tag in aggregationTags)
        {
            var value = GetPropertyValue(entity, tag);
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Look up in shared index
            var existing = await CanonIndex.Query(i =>
                i.EntityType == typeof(T).Name &&
                i.AggregationKey == tag &&
                i.AggregationValue == value
            ).FirstOrDefaultAsync(ct);

            if (existing != null)
                return existing.CanonicalId; // Found existing canonical entity
        }

        // Check external ID index
        var externalIdEntry = await CanonIndex.Query(i =>
            i.EntityType == typeof(T).Name &&
            i.ExternalSystem == origin &&
            i.ExternalId == entity.Id
        ).FirstOrDefaultAsync(ct);

        if (externalIdEntry != null)
            return externalIdEntry.CanonicalId;

        // New canonical entity - mint GUID v7
        return Guid.CreateVersion7().ToString("n");
    }

    private async Task MergeEntity<T>(T canonical, T incoming, string origin, CancellationToken ct)
        where T : CanonEntity<T>, new()
    {
        // Get merge strategy (configurable per entity type)
        var strategy = GetMergeStrategy<T>();

        // Default: last-write-wins with source tracking
        foreach (var prop in typeof(T).GetProperties())
        {
            if (prop.Name == "Id" || prop.Name == "Metadata") continue;

            var incomingValue = prop.GetValue(incoming);
            if (incomingValue == null) continue;

            // Apply merge strategy
            var shouldUpdate = strategy.ShouldUpdate(canonical, prop.Name, incomingValue, origin);
            if (shouldUpdate)
            {
                prop.SetValue(canonical, incomingValue);

                // Track source
                canonical.Metadata.Sources.TryAdd(prop.Name, new HashSet<string>());
                canonical.Metadata.Sources[prop.Name].Add(origin);
            }
        }

        canonical.Metadata.Version++;
        canonical.Metadata.ExternalIds[origin] = incoming.Id;
    }
}
```

---

#### 3. Simplified Data Model (3 Entities Total)

```csharp
// 1. CanonEntity<T> - The canonical entity itself (standard Entity<T>)
public class Device : CanonEntity<Device> {
    [AggregationTag] public string Serial { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";

    // Metadata embedded (framework-managed)
    [JsonProperty("__canon")]
    public CanonMetadata Metadata { get; internal set; } = new();
}

// 2. CanonIndex - Shared index for all entity types (replaces KeyIndex, IdentityLink)
public class CanonIndex : Entity<CanonIndex> {
    // Composite key: EntityType + ExternalSystem + ExternalId
    [Index] public string EntityType { get; set; } = "";
    [Index] public string ExternalSystem { get; set; } = "";
    [Index] public string ExternalId { get; set; } = "";
    [Index] public string CanonicalId { get; set; } = "";

    // Optional: Aggregation key tracking
    [Index] public string? AggregationKey { get; set; }
    [Index] public string? AggregationValue { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// 3. CanonStage<T> - OPTIONAL staging (only for async/distributed processing)
public class CanonStage<T> : Entity<CanonStage<T>> where T : CanonEntity<T>, new() {
    public CanonStageStatus Status { get; set; } // Pending, Processing, Completed, Rejected
    public string Origin { get; set; } = "";
    public T Payload { get; set; } = default!;
    public Dictionary<string, object?> SourceEnvelope { get; set; } = new();
    public string? RejectionReason { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum CanonStageStatus { Pending, Processing, Completed, Rejected }
```

**Result**:
- **11 entities → 3 entities** (73% reduction)
- **11 storage containers per model → 1-2 containers** (82-91% reduction)
- **No custom naming overrides** (provider transparency restored)

---

#### 4. Optional Messaging Bridge (Distributed Scenarios)

```csharp
// Koan.Canon.Messaging/CanonMessagingBridge.cs
public class CanonMessagingBridge
{
    public static void RegisterCanonMessaging(IServiceCollection services)
    {
        // Only register if messaging is configured
        if (!services.HasMessaging()) return;

        // Intercept .SendForCanonization() calls
        services.On<CanonTransportEnvelope>(async envelope => {
            var processor = AppHost.Current.GetRequiredService<ICanonProcessor>();

            // Deserialize entity from envelope
            var entityType = Type.GetType(envelope.EntityType);
            var entity = envelope.Payload.Deserialize(entityType);

            // Process locally in orchestrator service
            var method = typeof(ICanonProcessor)
                .GetMethod("CanonizeAsync")!
                .MakeGenericMethod(entityType);

            await (Task)method.Invoke(processor, new[] { entity, envelope.Origin, CancellationToken.None })!;
        });
    }
}

// Simple envelope (replaces complex TransportEnvelope<T> hierarchy)
public class CanonTransportEnvelope
{
    public string EntityType { get; set; } = "";
    public string Origin { get; set; } = "";
    public object Payload { get; set; } = default!;
    public Dictionary<string, object?> Metadata { get; set; } = new();
}
```

---

### Deployment Patterns

#### Pattern 1: Local-First (Simple Applications)

```csharp
// Startup.cs
services.AddKoan();
// That's it! Canonization works immediately

// Usage
var device = new Device { Serial = "SN-001" };
var canonical = await device.Canonize(origin: "factory-floor");
// Immediately available
```

**Use Cases**:
- Single-service applications
- Testing and development
- Microservices with isolated domains

---

#### Pattern 2: Distributed with Centralized Canonizer

```csharp
// Source System A
services.AddKoan();
services.AddMessaging(); // Enable distributed mode

await device.SendForCanonization();

// Dedicated Canonization Service
services.AddKoan();
services.AddCanonOrchestrator(); // Subscribes to canon messages

// Processes all incoming entities from multiple sources
```

**Use Cases**:
- Enterprise MDM scenarios
- Multiple source systems feeding central canonical repository
- High-volume ingestion with dedicated canonization service

---

#### Pattern 3: Hybrid (Local + Eventual Sync)

```csharp
// Each service maintains local canonical entities
await device.Canonize(origin: "serviceA"); // Local

// Periodic sync to enterprise canonical repository
await CanonSyncService.SyncToEnterprise<Device>();
```

**Use Cases**:
- Distributed systems with eventual consistency
- Edge computing with cloud sync
- Offline-capable applications

---

## Implementation Strategy

### Phase 1: Core Refactor (Breaking Changes)

**Week 1-2: Foundation**
1. Create new `ICanonProcessor` interface
2. Implement `LocalCanonProcessor` (local canonization logic)
3. Add `Canonize()` method to `CanonEntity<T>`
4. Create simplified `CanonIndex` entity

**Files to Create**:
- `src/Koan.Canon.Core/Processing/ICanonProcessor.cs`
- `src/Koan.Canon.Core/Processing/LocalCanonProcessor.cs`
- `src/Koan.Canon.Core/Processing/MergeStrategies.cs`
- `src/Koan.Canon.Core/Model/CanonIndex.cs`

**Files to Modify**:
- `src/Koan.Canon.Core/Model/Typed.cs` (add Canonize() to CanonEntity<T>)

---

**Week 3-4: Messaging Decoupling**
1. Move messaging logic to optional `Koan.Canon.Messaging` package
2. Create `CanonMessagingBridge` for distributed scenarios
3. Make `RegisterCanonInterceptors()` conditional
4. Update `ServiceCollectionExtensions.AddKoanCanon()` to support both modes

**Files to Create**:
- `src/Koan.Canon.Messaging/CanonMessagingBridge.cs`
- `src/Koan.Canon.Messaging/CanonTransportEnvelope.cs`

**Files to Modify**:
- `src/Koan.Canon.Core/ServiceCollectionExtensions.cs`
- `src/Koan.Canon.Core/Extensions/CanonEntityExtensions.cs` (make messaging optional)

---

### Phase 2: Data Model Migration (Breaking Changes)

**Week 5-6: Index Consolidation**
1. Create `CanonIndex` entity
2. Migrate `KeyIndex<T>` and `IdentityLink<T>` data to `CanonIndex`
3. Update association logic to use shared index
4. Remove custom storage naming overrides

**Migration Script**:
```csharp
// Koan.Canon.Core/Migration/IndexConsolidationMigration.cs
public async Task MigrateToCanonIndex()
{
    var models = DiscoverCanonModels();

    foreach (var modelType in models)
    {
        // Migrate KeyIndex<T>
        var keyIndexType = typeof(KeyIndex<>).MakeGenericType(modelType);
        var keyIndexes = await GetAllAsync(keyIndexType);

        foreach (var ki in keyIndexes)
        {
            await new CanonIndex {
                EntityType = modelType.Name,
                AggregationKey = GetPropertyValue(ki, "AggregationKey"),
                AggregationValue = GetPropertyValue(ki, "AggregationKey"),
                CanonicalId = GetPropertyValue(ki, "ReferenceId")
            }.Save();
        }

        // Migrate IdentityLink<T>
        var identityLinkType = typeof(IdentityLink<>).MakeGenericType(modelType);
        var identityLinks = await GetAllAsync(identityLinkType);

        foreach (var il in identityLinks)
        {
            await new CanonIndex {
                EntityType = modelType.Name,
                ExternalSystem = GetPropertyValue(il, "System"),
                ExternalId = GetPropertyValue(il, "ExternalId"),
                CanonicalId = GetPropertyValue(il, "ReferenceId")
            }.Save();
        }
    }
}
```

---

**Week 7-8: Metadata Embedding**
1. Add `CanonMetadata` property to canonical entities
2. Migrate `ReferenceItem<T>` and `PolicyState<T>` data to embedded metadata
3. Update projection logic to use embedded metadata
4. Deprecate separate metadata entities

**Files to Modify**:
- `src/Koan.Canon.Core/Model/Typed.cs` (add Metadata property)
- Projection workers to read from entity.Metadata

---

### Phase 3: Worker Refactor (Non-Breaking)

**Week 9-10: Event-Driven Processing**
1. Replace polling workers with event-driven processing
2. Use `Canon.OnUpdate` handlers for custom logic
3. Optional: Integrate with Koan.Jobs for background work
4. Add `CanonStage<T>` for async scenarios

**New Pattern**:
```csharp
// Instead of polling workers, use events
services.AddKoan();
services.AddCanonOrchestrator(options => {
    options.Mode = CanonProcessingMode.Immediate; // Or Deferred
});

// Immediate: Canonize inline
await device.Canonize(origin: "source"); // Fully processed

// Deferred: Stage and process in background
await device.StageForCanonization(origin: "source");
// Background service processes staged entities
```

---

### Phase 4: Documentation & Samples

**Week 11: Documentation**
1. Update README with new API patterns
2. Create migration guide from v1.x to v2.0
3. Document local vs. distributed deployment patterns
4. Add troubleshooting guide

**Week 12: Samples**
1. Simple local canonization sample (S15.LocalCanon)
2. Distributed canonization sample (S16.DistributedCanon)
3. Hybrid pattern sample (S17.HybridCanon)
4. Migration example (S8.Canon.v2)

---

## Migration Path for Existing Code

### Breaking Changes

1. **Messaging is Optional**
   - **Before**: All Canon entities auto-routed through messaging
   - **After**: Messaging only used when explicitly configured
   - **Migration**: Add `services.AddCanonMessaging()` to enable distributed mode

2. **API Changes**
   - **Before**: `await device.Send()`
   - **After**: `await device.Canonize(origin: "sourceA")` (local) OR `await device.SendForCanonization()` (distributed)
   - **Migration**: Replace `.Send()` with `.Canonize()` for local scenarios

3. **Data Model Consolidation**
   - **Before**: 11 entities per model (KeyIndex, IdentityLink, StageRecord, etc.)
   - **After**: 2-3 entities (CanonEntity, CanonIndex, optional CanonStage)
   - **Migration**: Run index consolidation migration script

---

### Backward Compatibility Layer (Optional)

For gradual migration, provide compatibility shims:

```csharp
// Koan.Canon.Compat/CanonV1Extensions.cs
public static class CanonV1Compatibility
{
    [Obsolete("Use .Canonize(origin) for local or .SendForCanonization() for distributed")]
    public static async Task Send<T>(this T entity) where T : CanonEntity<T>, new()
    {
        // Default to local canonization
        var origin = CanonContext.Current?.System ?? "unknown";
        await entity.Canonize(origin);
    }
}
```

---

## Success Metrics

### Developer Experience
- **Setup Complexity**: 5+ configuration steps → 1 step (`services.AddKoan()`)
- **API Simplicity**: `.Send()` + context management → `.Canonize(origin: "x")`
- **Test Setup**: Messaging infrastructure required → No infrastructure needed

### Performance
- **Database Round-Trips**: 7+ operations → 2 operations (read + write)
- **Latency**: Polling delay (500ms-5s) → Immediate processing
- **Storage Overhead**: 11 containers per model → 1-2 containers per model

### Architecture
- **Deployment Flexibility**: Messaging required → Messaging optional
- **Framework Alignment**: Violates 3 principles → Aligns with all principles
- **Entity Count**: 11 per model → 3 total (shared)

---

## Risk Assessment

### High Risk: Data Migration
- **Mitigation**: Provide tested migration scripts, rollback capability
- **Testing**: Extensive integration tests with multiple providers
- **Staging**: Require migration validation in non-prod environments first

### Medium Risk: API Breaking Changes
- **Mitigation**: Provide compatibility shims for gradual migration
- **Documentation**: Clear migration guide with examples
- **Communication**: Major version bump (v2.0) with detailed changelog

### Low Risk: Performance Regressions
- **Mitigation**: Benchmark before/after, ensure improvements
- **Monitoring**: Add performance metrics to track canonization throughput
- **Rollback**: Keep v1.x maintained for 6 months

---

## Alternatives Considered

### Alternative 1: Keep Messaging, Add Local Fallback
**Pros**: Less breaking changes
**Cons**: Doesn't solve root coupling issue, dual code paths increase complexity
**Decision**: Rejected - doesn't achieve framework alignment goals

### Alternative 2: Messaging-First with Better Abstractions
**Pros**: Maintains distributed-first architecture
**Cons**: Still violates "Reference = Intent", high barrier to entry
**Decision**: Rejected - contradicts Koan Framework's local-first philosophy

### Alternative 3: Incremental Refactor (Non-Breaking)
**Pros**: No breaking changes
**Cons**: Technical debt accumulates, confusing dual APIs
**Decision**: Rejected - clean break better for long-term framework health

---

## Recommendation

**Approve complete overhaul** with v2.0 major version bump.

**Rationale**:
1. Current architecture fundamentally incompatible with framework principles
2. Incremental fixes won't resolve coupling issues
3. Clean redesign enables local-first + distributed flexibility
4. Aligns Canon with proven patterns from Koan.Data and Koan.Jobs
5. Dramatically improves developer experience and testing simplicity

**Timeline**: 12 weeks to v2.0 release with migration support
**Support**: Maintain v1.x for 6 months with critical bug fixes only
**Migration**: Provide automated migration tools + comprehensive documentation

---

## Appendix A: File Impact Analysis

### Files to Delete (No Longer Needed)
- `src/Koan.Canon.Core/Model/DynamicTransportEnvelope.cs`
- `src/Koan.Canon.Core/Model/Record.cs`
- `src/Koan.Canon.Core/Messaging/CanonQueuedMessage.cs`
- `src/Koan.Canon.Core/Sending/FlowValueObjectSendExtensions.cs` (move to messaging package)

### Files to Significantly Refactor
- `src/Koan.Canon.Core/Model/Typed.cs` - Add Canonize() method, embed metadata
- `src/Koan.Canon.Core/ServiceCollectionExtensions.cs` - Remove workers, simplify registration
- `src/Koan.Canon.Core/Extensions/CanonEntityExtensions.cs` - Make messaging conditional
- `src/Koan.Canon.Core/Orchestration/CanonOrchestratorBase.cs` - Add local processing pathway

### New Files to Create
- `src/Koan.Canon.Core/Processing/ICanonProcessor.cs`
- `src/Koan.Canon.Core/Processing/LocalCanonProcessor.cs`
- `src/Koan.Canon.Core/Model/CanonIndex.cs`
- `src/Koan.Canon.Messaging/CanonMessagingBridge.cs` (new package)
- `src/Koan.Canon.Core/Migration/IndexConsolidationMigration.cs`

---

## Appendix B: Example Canon.OnUpdate Pattern

```csharp
// Clean handler registration (preserved from current design)
public class Startup
{
    public void Configure()
    {
        Canon.OnUpdate<Device>(async (ref Device proposed, Device? current, UpdateMetadata meta) =>
        {
            // Custom merge logic
            if (current != null && proposed.LastUpdated < current.LastUpdated)
                return Update.Skip("Incoming data is stale");

            // Enrich data
            proposed.EnrichedAt = DateTimeOffset.UtcNow;
            proposed.ProcessedBy = meta.SourceSystem;

            return Update.Continue();
        });
    }
}

// Usage - handler invoked automatically during canonization
await device.Canonize(origin: "sourceA"); // Handler called inline
```

---

**END OF PROPOSAL**
