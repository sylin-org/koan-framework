# Orchestrator Bidirectional Communication Pattern

## Executive Summary

This document outlines the implementation of a bidirectional orchestration pattern for Sora.Flow that enables orchestrators to intelligently process updates from adapters and broadcast changes back to relevant systems. The pattern introduces a holistic update decision model with in-place modification capabilities and automatic change notification to affected adapters.

## Purpose & Business Value

### Current State Limitations
- Adapters operate in isolation without awareness of canonical changes
- No mechanism for systems to receive updates when other systems modify shared entities
- Rigid field ownership models that don't reflect real-world data authority patterns
- Manual synchronization required between systems

### Proposed Solution Value
- **Multi-Master Synchronization**: Multiple systems can contribute to the same entity with intelligent conflict resolution
- **Automatic Change Propagation**: When System A updates an entity, Systems B and C automatically receive the canonical changes
- **Flexible Authority**: No rigid field ownership - orchestrators make intelligent decisions based on data quality, source, and business rules
- **Reduced Integration Complexity**: Adapters only need to handle their native models, not cross-system translation

## Core Architecture

### Message Flow Pattern
```
Adapter A ──┐                              ┌──> Adapter A Queue (native model)
            ├──> Orchestrator ──> MongoDB ─┤
Adapter B ──┘    (decisions)              └──> Adapter B Queue (native model)
```

### Key Principles
1. **Adapters Are Simple Synchronizers**: Receive native models, send native models - no cross-system awareness
2. **Orchestrators Are Intelligent Mediators**: Make holistic decisions about updates, detect changes, route notifications
3. **Framework Handles Complexity**: ID translation, parent resolution, change detection all at framework level
4. **Native Models Only**: Each adapter only sees its own IDs and relationships

## Implementation Design

### 1. Orchestrator Pattern with Reference Modification

```csharp
// Enable bidirectional updates with PushUpdates flag
[FlowOrchestrator(PushUpdates = true)]
public class MainOrchestrator : FlowOrchestratorBase
{
    protected override void Configure()
    {
        Flow.OnUpdate<Person>((ref Person proposed, Person? current, UpdateMetadata metadata) =>
        {
            // Short-circuit complete rejection
            if (metadata.Source.System == "deprecated_system")
                return Skip("System is deprecated");
            
            // In-place modification for intelligent merging
            if (metadata.Source.System == "systemA" && current != null)
            {
                // A has abbreviated names - keep current if better
                if (current.FullName.Length > proposed.FullName.Length)
                {
                    proposed.FullName = current.FullName;  // Direct modification
                }
            }
            
            return Continue("Merged with existing data");
        });
    }
}

// Traditional orchestrator without push updates (default)
[FlowOrchestrator]  // PushUpdates = false by default
public class LegacyOrchestrator : FlowOrchestratorBase
{
    protected override void Configure()
    {
        // Only processes updates, doesn't push changes to adapters
        Flow.OnUpdate<Device>((ref Device proposed, Device? current, UpdateMetadata metadata) =>
        {
            // Business logic here
            return Continue();
        });
    }
}
```

### 2. Adapter Pattern - Simplified

```csharp
[FlowAdapter(system: "bms", adapter: "bms")]
public class BmsAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Receive native BMS models with BMS IDs
        Flow.Updates.On<Device>(async device =>
        {
            // device.Id = "bmsD1" (native BMS ID)
            // device.ParentId = "bmsP1" (native BMS parent ID)
            await _bmsDb.UpdateDevice(device);
        });
        
        // Send changes with native IDs
        var device = new Device { Id = "bmsD1", Serial = "SN-001" };
        await device.Send();
    }
}
```

### 3. Framework Intelligence Layer

```csharp
// FlowOrchestrator attribute definition
[AttributeUsage(AttributeTargets.Class)]
public sealed class FlowOrchestratorAttribute : Attribute
{
    /// <summary>
    /// When true, orchestrator pushes canonical changes to affected adapters.
    /// When false (default), orchestrator only processes updates without propagation.
    /// </summary>
    public bool PushUpdates { get; set; } = false;
}

internal class FlowOrchestrationEngine
{
    private readonly bool _pushUpdatesEnabled;
    
    public FlowOrchestrationEngine(Type orchestratorType)
    {
        // Check if PushUpdates is enabled via attribute
        var attr = orchestratorType.GetCustomAttribute<FlowOrchestratorAttribute>();
        _pushUpdatesEnabled = attr?.PushUpdates ?? false;
    }
    
    private async Task ProcessEntityUpdate<T>(T proposed, string sourceSystem) where T : IFlowEntity
    {
        // 1. Get current canonical
        var current = await GetCurrentCanonical<T>(proposed);
        
        // 2. Let orchestrator make decision with modification
        var working = proposed.DeepClone();
        var result = await InvokeOrchestrator(ref working, current, metadata);
        
        if (result == UpdateResult.Skip)
            return; // Rejected
        
        // 3. Update canonical with (possibly modified) model
        await UpdateCanonical(working);
        
        // 4. CONDITIONAL: Only push updates if enabled
        if (_pushUpdatesEnabled)
        {
            // Determine affected adapters
            var affected = await GetAffectedAdapters(working, sourceSystem);
            
            // Send native models to each adapter
            foreach (var targetSystem in affected)
            {
                var nativeModel = await CreateNativeModel(working, targetSystem);
                await SendToAdapter(targetSystem, nativeModel);
            }
        }
        // else: Traditional mode - just update canonical, no propagation
    }
    
    private async Task<T> CreateNativeModel<T>(T canonical, string targetSystem) where T : IFlowEntity
    {
        var native = canonical.DeepClone();
        
        // Replace canonical IDs with native IDs
        native.Id = await GetNativeId(canonical, targetSystem);
        
        // Replace parent IDs with native versions
        await ReplaceParentIds(native, targetSystem);
        
        // Remove fields this system doesn't need
        FilterFieldsForSystem(native, targetSystem);
        
        return native;
    }
}
```

### 4. Static Helpers for Clean DX

```csharp
public static class Flow
{
    // For Orchestrators
    public static void OnUpdate<T>(UpdateHandler<T> handler) where T : IFlowEntity;
    
    // For Adapters
    public static class Updates
    {
        public static IDisposable On<T>(Func<T, Task> handler) where T : IFlowEntity;
        public static IDisposable OnNew<T>(Func<T, Task> handler) where T : IFlowEntity;
    }
}

// Return types for orchestrator decisions
public static class Update
{
    public static UpdateResult Continue(string? reason = null);
    public static UpdateResult Skip(string reason);
    public static UpdateResult Defer(string reason, TimeSpan? retryAfter = null);
}
```

### 5. Decision Logging

```csharp
public class UpdateDecisionLog : Entity<UpdateDecisionLog>
{
    public DateTimeOffset Timestamp { get; set; }
    public string EntityType { get; set; }
    public string SourceSystem { get; set; }
    public DecisionType Decision { get; set; }  // Accepted, PartiallyAccepted, Dropped
    public string Reason { get; set; }
    public string ProposedSnapshot { get; set; }
    public string AcceptedSnapshot { get; set; }
    public List<string> ModifiedFields { get; set; }
}
```

## RabbitMQ Topology Changes

### Current Topology
```
Adapters → "System.String" → API Container → MongoDB
```

### New Topology (Only When PushUpdates = true)
```yaml
Exchanges:
  sora.flow.inbound:
    type: direct
    purpose: All updates from adapters to orchestrator
    
  sora.flow.outbound:  # Only created when PushUpdates enabled
    type: topic
    purpose: Native models from orchestrator to adapters

Queues:
  sora.flow.orchestrator:
    binding: sora.flow.inbound
    purpose: All inbound updates for processing
    
  sora.flow.updates.{system}:  # Only created when PushUpdates enabled
    binding: sora.flow.outbound with routing key "{system}"
    purpose: Native models for specific adapter
    content: Models with native IDs for that system
```

### Traditional Topology (PushUpdates = false, default)
```
Adapters → sora.flow.orchestrator → Orchestrator → MongoDB
                                         ↓
                                     (no outbound queues)
```

### Message Types
```csharp
// Inbound (Adapter → Orchestrator)
public class InboundUpdate<T> where T : IFlowEntity
{
    public T Entity { get; set; }          // Entity with source system's IDs
    public string SourceSystem { get; set; }
}

// Outbound (Orchestrator → Adapter)  
public class OutboundUpdate<T> where T : IFlowEntity
{
    public T Entity { get; set; }          // Entity with target system's native IDs
    // No source info - adapter doesn't need to know
}
```

## Impact Analysis

### Positive Impacts
1. **Simplified Adapters**: No ID translation, no cross-system awareness needed
2. **Intelligent Synchronization**: Orchestrators can merge data from multiple sources
3. **Automatic Propagation**: Changes flow automatically to interested systems
4. **Audit Trail**: Every decision is logged with reasoning
5. **Flexible Authority**: No rigid ownership, decisions based on context
6. **Native Operations**: Each system works with its own IDs and relationships

### Complexity Impacts
1. **Queue Management**: N+1 queues instead of single queue
2. **ID Translation**: Framework must maintain ID mappings for all systems
3. **Parent Resolution**: Must translate parent IDs for each target system
4. **State Management**: Need to track current canonical for comparison
5. **Circular Update Prevention**: Must prevent update loops

### Performance Considerations
1. **Throughput**: Additional processing for ID translation per adapter
2. **Latency**: Change detection and routing adds processing time
3. **Storage**: Decision logs and ID mappings require storage
4. **Memory**: Orchestrator needs to cache canonical models

## Implementation TODO

### Phase 1: Core Infrastructure (Week 1)
- [ ] Create `FlowOrchestratorAttribute` with `PushUpdates` property (default false)
- [ ] Create `FlowOrchestratorBase` with reference modification pattern
- [ ] Implement `Flow.OnUpdate<T>()` static helper
- [ ] Add `UpdateResult` types (Continue, Skip, Defer)
- [ ] Create `UpdateMetadata` with change detection
- [ ] Implement decision logging infrastructure
- [ ] Add conditional logic for PushUpdates in framework

### Phase 2: Native Model Generation (Week 1)
- [ ] Implement `CreateNativeModel()` with ID translation
- [ ] Add `ReplaceParentIds()` for relationship mapping
- [ ] Create `GetNativeId()` using `identifier.external.{system}`
- [ ] Implement `FilterFieldsForSystem()` (basic, no ownership)
- [ ] Add caching for ID lookups

### Phase 3: Queue Infrastructure (Week 2)
- [ ] Modify RabbitMQ topology for per-adapter queues (conditional on PushUpdates)
- [ ] Create `sora.flow.orchestrator` intake queue (always)
- [ ] Create `sora.flow.updates.{system}` pattern (only when PushUpdates = true)
- [ ] Implement conditional queue auto-provisioning at startup
- [ ] Add dead letter handling for bidirectional queues

### Phase 4: Adapter Simplification (Week 2)
- [ ] Implement `Flow.Updates.On<T>()` for adapters (conditional on PushUpdates)
- [ ] Add `Flow.Updates.OnNew<T>()` for new entities (conditional on PushUpdates)
- [ ] Create adapter context auto-detection
- [ ] Update sample adapters to new pattern (when PushUpdates enabled)
- [ ] Provide migration examples for both modes

### Phase 5: Change Detection & Routing (Week 3)
- [ ] Implement change detection between current/proposed
- [ ] Create affected adapter determination logic
- [ ] Add circular update prevention
- [ ] Implement batching for performance
- [ ] Add retry logic for failed notifications

### Phase 6: Testing & Documentation (Week 3)
- [ ] Unit tests for orchestrator decisions
- [ ] Integration tests for full flow
- [ ] Performance benchmarks
- [ ] Developer documentation
- [ ] Migration guide from current pattern

## Configuration Requirements

### Minimal Configuration (Traditional Mode)
```json
{
  "Flow": {
    "Orchestration": {
      "DecisionLogging": true,
      "BatchSize": 100
    }
  }
}
```

### Configuration with PushUpdates Enabled
```json
{
  "Flow": {
    "Orchestration": {
      // These only apply when PushUpdates = true via attribute
      "EnableChangeNotifications": true,  
      "NotificationMode": "Selective",    // All, Selective, None
      "BatchSize": 100,
      "BatchTimeout": "00:00:05",
      "DecisionLogging": true,
      "CircularUpdatePrevention": {
        "Enabled": true,
        "WindowSeconds": 5
      },
      "RetryPolicy": {
        "MaxAttempts": 3,
        "BackoffMultiplier": 2
      }
    }
  }
}
```

### Zero Configuration Example
```csharp
// Traditional mode - no configuration needed
[FlowOrchestrator]
public class SimpleOrchestrator : FlowOrchestratorBase
{
    // Processes updates, no push notifications
}

// Bidirectional mode - still no configuration needed
[FlowOrchestrator(PushUpdates = true)]
public class BidirectionalOrchestrator : FlowOrchestratorBase
{
    // Processes updates AND pushes to adapters
}
```

## Success Metrics

### Functional Metrics
- ✅ Orchestrators can modify proposed updates in-place
- ✅ Adapters receive native models with their own IDs
- ✅ Parent relationships correctly translated per system
- ✅ Changes propagate to affected systems automatically
- ✅ Decision logs capture all accept/reject reasoning

### Performance Metrics
- ✅ < 100ms latency for update decisions
- ✅ > 1000 updates/second throughput
- ✅ < 10MB memory per orchestrator instance
- ✅ ID translation cache hit rate > 95%

### Developer Experience Metrics
- ✅ Adapter code reduced by 70%
- ✅ No ID translation code in adapters
- ✅ Orchestrator logic readable and testable
- ✅ Clear audit trail for debugging

## Risk Mitigation

### Risk: Circular Updates
**Mitigation**: Track recent updates with timestamp window, skip if same entity updated within 5 seconds from same source

### Risk: ID Translation Failures
**Mitigation**: Cache ID mappings aggressively, fail gracefully with detailed logging

### Risk: Queue Overflow
**Mitigation**: Implement backpressure, batching, and dead letter queues

### Risk: Orchestrator Bottleneck
**Mitigation**: Horizontal scaling with partition by entity type

## Migration Strategy

### Phase 1: Parallel Run
- Deploy new orchestrator alongside existing system
- Mirror updates to both systems
- Compare outputs for validation

### Phase 2: Gradual Cutover
- Move one adapter at a time to new pattern
- Maintain backwards compatibility
- Monitor decision logs

### Phase 3: Full Migration
- Remove old auto-handler infrastructure
- Clean up FlowTargetedMessage usage
- Archive old code

## Conclusion

The bidirectional orchestrator pattern with reference modification provides a powerful, flexible foundation for multi-system synchronization. By handling all complexity at the framework level and providing clean abstractions, it enables sophisticated data orchestration while keeping adapter code simple and maintainable.

Key innovations:
- **Reference modification** for intuitive in-place updates
- **Native model generation** eliminates adapter-side ID translation
- **Holistic decisions** with full context instead of field-by-field
- **Automatic propagation** of changes to affected systems
- **Complete audit trail** of all orchestration decisions

This pattern transforms Sora.Flow from a simple aggregation pipeline to an intelligent multi-master synchronization platform.