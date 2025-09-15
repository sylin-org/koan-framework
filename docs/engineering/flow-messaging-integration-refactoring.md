# Koan.Flow + Koan.Messaging Integration Refactoring Proposal

## Current State Analysis
- ❌ **BROKEN**: `entity.Send()` bypasses messaging system entirely
- ❌ **MISSING**: No bridge between Koan.Messaging and Koan.Flow  
- ❌ **INCOMPLETE**: No orchestrator pattern implementation
- ✅ **WORKING**: Flow intake system and processing pipeline
- ✅ **WORKING**: Auto-discovery of Flow types
- ✅ **WORKING**: Koan.Messaging typed and named message patterns

## Target Architecture
```
Clients/Adapters → Koan.Messaging → [FlowOrchestrator] → Koan.Flow.Intake → Processing Pipeline
                                ↕
                            Bidirectional messaging with adapters
```

## Implementation Checklist

### Phase 1: Core Foundation
- [ ] **1.1** Create `Flow` static API class with `Outbound`/`Inbound` properties
- [ ] **1.2** Implement `FlowSendBuilder` for fluent sending syntax
- [ ] **1.3** Implement `FlowHandlerBuilder` for fluent handler registration
- [ ] **1.4** Create `IFlowIntakeSender` interface for routing to Flow intake
- [ ] **1.5** Update `[OrchestratorAttribute]` to be assembly-level (already exists but may need adjustment)

### Phase 2: Fix Broken `entity.Send()` 
- [ ] **2.1** Identify all `entity.Send()` extension methods in `FlowEntitySendExtensions.cs`
- [ ] **2.2** Replace direct `IFlowSender` usage with `IMessageBus.SendAsync()`
- [ ] **2.3** Ensure `entity.Send()` routes through messaging system
- [ ] **2.4** Add `entity.SendTo(target)` for targeted messaging
- [ ] **2.5** Create `entity.SendToFlowIntake()` for direct Flow routing (internal use)

### Phase 3: Orchestrator Auto-Registration
- [ ] **3.1** Enhance `Koan.Flow.Core.KoanAutoRegistrar` to detect `[FlowOrchestrator]` assemblies
- [ ] **3.2** Auto-discover `FlowEntity<>` and `FlowValueObject<>` types in orchestrator assemblies
- [ ] **3.3** Auto-register message handlers: `services.On<T>(entity => entity.SendToFlowIntake())`
- [ ] **3.4** Handle both typed messages and named messages ("seed", etc.)
- [ ] **3.5** Add configuration options for orchestrator behavior

### Phase 4: Flow Static API Implementation
- [ ] **4.1** Implement `Flow.Send("command").To("target")` syntax
- [ ] **4.2** Implement `Flow.Send("command").Broadcast()` syntax  
- [ ] **4.3** Implement `Flow.Send<T>(entity).To("target")` syntax
- [ ] **4.4** Implement `Flow.On<T>(handler)` registration syntax
- [ ] **4.5** Implement `Flow.On("command", handler)` registration syntax
- [ ] **4.6** Integrate with existing `services.On<T>()` patterns

### Phase 5: Message Routing & Targeting
- [ ] **5.1** Design message envelope format for targeting (headers/metadata)
- [ ] **5.2** Implement topic-based routing for broadcast scenarios
- [ ] **5.3** Implement targeted routing for specific adapter communication
- [ ] **5.4** Handle no-orchestrator scenarios (peer-to-peer)
- [ ] **5.5** Add message filtering/routing logic

### Phase 6: Channel Provisioning
- [ ] **6.1** Auto-create messaging channels for discovered Flow types
- [ ] **6.2** Implement naming strategy: `flow.{model}` (e.g., `flow.device`)
- [ ] **6.3** Handle MQ topology provisioning (if using RabbitMQ/Redis)
- [ ] **6.4** Add configuration for channel naming and routing
- [ ] **6.5** Support multiple orchestrators with proper message routing

### Phase 7: Integration & Testing
- [ ] **7.1** Update sample projects to use new patterns
- [ ] **7.2** Verify backward compatibility for existing direct Flow usage
- [ ] **7.3** Add integration tests for messaging → Flow pipeline
- [ ] **7.4** Test orchestrator discovery and handler registration
- [ ] **7.5** Test both broadcast and targeted messaging scenarios

### Phase 8: Documentation & Examples
- [ ] **8.1** Update Flow reference documentation
- [ ] **8.2** Create orchestrator pattern guide
- [ ] **8.3** Add code examples for common scenarios
- [ ] **8.4** Document migration path from direct Flow usage
- [ ] **8.5** Create troubleshooting guide

### Phase 9: Advanced Features
- [ ] **9.1** Add support for message transformation/middleware
- [ ] **9.2** Implement message retry and error handling
- [ ] **9.3** Add monitoring and diagnostics integration
- [ ] **9.4** Support for multiple Flow orchestrators
- [ ] **9.5** Advanced routing patterns and message filtering

### Phase 10: Performance & Polish
- [ ] **10.1** Optimize auto-discovery performance
- [ ] **10.2** Add caching for discovered types and handlers
- [ ] **10.3** Memory usage optimization for message routing
- [ ] **10.4** Add performance benchmarks
- [ ] **10.5** Final API polish and consistency review

## Success Criteria
- ✅ `entity.Send()` uses messaging system, not direct Flow intake
- ✅ `[FlowOrchestrator]` assemblies auto-register message handlers
- ✅ Fluent API: `Flow.Send("cmd").To("target")` and `Flow.On<T>(handler)`
- ✅ Both broadcast and targeted messaging work correctly
- ✅ No breaking changes to existing Flow functionality
- ✅ Zero-config experience with escape hatches for customization
- ✅ Complete integration between Koan.Messaging and Koan.Flow

## Implementation Priority
**Start with Phase 1-2** to establish foundation and fix the broken `entity.Send()` implementation. This unblocks the core messaging-first architecture before building orchestrator features.

## API Design Examples

### Target Sending Syntax
```csharp
// Current broken implementation (bypasses messaging):
await device.Send(); // ❌ Goes direct to Flow intake

// Fixed implementation (uses messaging):
await device.Send(); // ✅ Goes through Koan.Messaging
await device.SendTo("bms:simulator"); // ✅ Targeted messaging

// Flow-specific command syntax:
await Flow.Send("seed", payload).To("bms:simulator");
await Flow.Send("seed").Broadcast();
await Flow.Send<Device>(device).To("target");
```

### Target Handler Registration
```csharp
// In [FlowOrchestrator] assemblies - auto-registered:
Flow.On<Device>(device => device.SendToFlowIntake())
    .On<Reading>(reading => reading.SendToFlowIntake())
    .On("seed", payload => HandleSeedCommand(payload));

// Or in services registration:
services.OnFlow<Device>(device => ProcessDevice(device))
        .OnFlow("seed", payload => HandleSeed(payload));
```

### Implementation Notes
- **Backward Compatibility**: Existing `entity.Send()` calls should work but route through messaging
- **Configuration**: All auto-registration should be configurable with sane defaults
- **Error Handling**: Failed messages should integrate with existing Flow rejection/DLQ system
- **Performance**: Auto-discovery should be cached and optimized for startup performance