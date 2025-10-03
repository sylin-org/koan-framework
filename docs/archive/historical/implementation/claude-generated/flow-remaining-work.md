# Flow Framework - Remaining Work

## Status as of 2025-01-07

### ✅ COMPLETED Components

#### External ID Correlation (100% Complete)
- ✅ Source entity ID extraction from [Key] property
- ✅ Auto-population of `identifier.external.{source}` with source IDs (NOT aggregation keys)
- ✅ Source ID stripping from canonical models
- ✅ FlowPolicyAttribute with ExternalIdPolicy enum
- ✅ Enhanced FlowRegistry with policy-driven detection
- ✅ IdentityLink auto-creation and indexing

#### ParentKey Resolution (90% Complete)
- ✅ TryResolveParentViaExternalId() method for cross-system parent lookup
- ❌ ParentKey values resolved to canonical ULIDs in canonical models (TODO: canonical projection must use resolved ULIDs)
- ✅ Entity parking with PARENT_NOT_FOUND when parent not available
- ✅ Cross-system parent lookup and validation working

#### Flow Messaging Core (100% Complete)
- ✅ MessagingInterceptors with type-safe registration
- ✅ FlowContext with AsyncLocal and stack trace fallback
- ✅ TransportEnvelope<T> and DynamicTransportEnvelope<T>
- ✅ FlowEntityExtensions with entity.Send() pattern
- ✅ Direct MongoDB integration bypassing FlowActions

### ❌ REMAINING Work

## 1. Messaging Infrastructure Gaps

### 1.1 IQueuedMessage Interface (CRITICAL)
**Location**: `src/Koan.Messaging.Core/Contracts/IQueuedMessage.cs`
**Status**: Missing - FlowQueuedMessage references non-existent interface
**Required Implementation**:
```csharp
namespace Koan.Messaging.Contracts;

public interface IQueuedMessage
{
    string QueueName { get; }
    object Payload { get; }
}
```

### 1.2 MessagingExtensions Queue Routing (CRITICAL)
**Location**: `src/Koan.Messaging.Core/MessagingExtensions.cs`
**Status**: Missing - Messages still route to default queue
**Required Changes**:
```csharp
public static async Task Send<T>(this T message, ...) where T : class
{
    var intercepted = MessagingInterceptors.Intercept(transformed);
    
    // NEW: Check for queue-specific routing
    if (intercepted is IQueuedMessage queuedMessage)
    {
        await proxy.SendToQueueAsync(queuedMessage.QueueName, queuedMessage.Payload, ct);
    }
    else
    {
        // Existing behavior
        await proxy.SendAsync(intercepted, ct);
    }
}
```

### 1.3 RabbitMQ Provider Enhancement (CRITICAL)
**Location**: `src/Koan.Messaging.Connector.RabbitMq/RabbitMqProvider.cs`
**Status**: Missing SendToQueueAsync method
**Required Implementation**:
```csharp
public async Task SendToQueueAsync<T>(string queueName, T message, CancellationToken ct)
{
    // Declare queue if not exists
    await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
    
    // Send message to specific queue
    var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
    await channel.BasicPublishAsync("", queueName, body);
}
```

## 2. Flow Orchestrator Pattern

### 2.1 FlowOrchestratorBase Class
**Location**: `src/Koan.Canon.Core/Orchestration/FlowOrchestratorBase.cs`
**Status**: Not implemented
**Purpose**: Provide base class for Flow entity orchestration
**Required Features**:
- Auto-subscribe to "Koan.Canon.FlowEntity" queue
- Type-safe deserialization based on envelope type
- Direct intake writing with metadata separation
- Support for FlowEntity, DynamicFlowEntity, and FlowValueObject

### 2.2 FlowOrchestrator Attribute
**Location**: `src/Koan.Canon.Core/Attributes/FlowOrchestratorAttribute.cs`
**Status**: Not implemented
**Purpose**: Mark classes as Flow orchestrators for auto-discovery

### 2.3 Auto-Discovery in KoanAutoRegistrar
**Location**: `src/Koan.Canon.Core/Initialization/KoanAutoRegistrar.cs`
**Status**: Not implemented
**Purpose**: Automatically discover and register [FlowOrchestrator] classes

### 2.4 DefaultFlowOrchestrator
**Location**: `src/Koan.Canon.Core/Orchestration/DefaultFlowOrchestrator.cs`
**Status**: Not implemented
**Purpose**: Provide zero-config orchestration for simple scenarios

## 3. Queue Provisioning

### 3.1 Flow Queue Provider
**Location**: `src/Koan.Canon.Core/Infrastructure/FlowQueueProvider.cs`
**Status**: Not implemented
**Purpose**: Define Flow-specific queues
**Implementation Approach**:
```csharp
public interface IFlowQueueProvider
{
    IEnumerable<string> GetRequiredQueues();
}

public class FlowQueueProvider : IFlowQueueProvider
{
    public IEnumerable<string> GetRequiredQueues()
    {
        return new[]
        {
            "Koan.Canon.FlowEntity",
            "Koan.Canon.FlowValueObject",
            "Koan.Canon.FlowCommand"
        };
    }
}
```

### 3.2 Queue Provisioner Hosted Service
**Location**: `src/Koan.Canon.Core/Infrastructure/FlowQueueProvisioner.cs`
**Status**: Not implemented
**Purpose**: Auto-provision Flow queues at startup
**Registration**: Add to AddKoanCanon() in ServiceCollectionExtensions

## Implementation Priority

### HIGH PRIORITY (Blocking)
1. **IQueuedMessage Interface** - 1 hour
2. **MessagingExtensions Queue Routing** - 2 hours
3. **RabbitMQ SendToQueueAsync** - 2 hours

### MEDIUM PRIORITY (Architecture)
4. **FlowOrchestrator Pattern** - 4 hours
5. **Queue Provisioning** - 2 hours

### LOW PRIORITY (Polish)
6. **Performance Metrics** - 1 hour
7. **Documentation Updates** - 1 hour

## Total Estimated Effort
- **Critical Path**: 5 hours (items 1-3)
- **Full Implementation**: 13 hours (all items)

## Testing Requirements

### Integration Tests Needed
1. Verify messages route to "Koan.Canon.FlowEntity" queue
2. Test cross-system parent-child resolution
3. Confirm external IDs contain source IDs (e.g., "D1") not aggregation keys
4. Validate canonical models have no 'id' field
5. Test entity parking and unparking when parents arrive

### Performance Tests Needed
1. Queue throughput benchmarks
2. External ID lookup performance
3. Parent resolution latency
4. Canonical projection speed with external ID population

## Architecture Notes

### Why These Gaps Exist
The Flow framework implementation focused on the complex business logic (external ID correlation, parent resolution) first. The remaining gaps are in the messaging infrastructure layer, which is simpler but requires careful integration with existing Koan.Messaging components.

### Design Decisions
1. **IQueuedMessage at Koan.Messaging level**: Enables any Koan component to use queue routing, not just Flow
2. **Queue provisioning in Flow module**: Flow owns its queue definitions and lifecycle
3. **Orchestrator pattern**: Provides clean separation between message handling and Flow processing
4. **Direct MongoDB integration**: Reduces latency by eliminating extra messaging hops

## Success Criteria
- [ ] Flow entities route to dedicated "Koan.Canon.FlowEntity" queue
- [ ] External IDs correctly populated with source entity IDs
- [ ] ParentKey relationships resolved across systems
- [ ] No source 'id' fields in canonical models
- [ ] Zero-config developer experience maintained
- [ ] Performance meets or exceeds current implementation

## Next Steps
1. Implement HIGH PRIORITY items immediately
2. Test external ID and ParentKey resolution with sample data
3. Complete orchestrator pattern for production readiness
4. Document new patterns for other teams
