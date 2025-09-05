# Sora Framework Reference Documentation

## Overview
Sora is a backend framework for .NET developers built on the principle of **reference = intent** with minimal configuration. The framework uses auto-registration patterns through `SoraAutoRegistrar` classes to provide zero-config experiences while maintaining escape hatches for customization.

## Core Principles
- **Reference = Intent**: Including a package should auto-register its capabilities
- **Minimal Configuration**: Default behaviors should work out-of-the-box
- **DRY/KISS**: Leverage existing patterns, don't reinvent
- **Auto-Registration**: Use `SoraAutoRegistrar` pattern consistently
- **Escape Hatches**: Always provide ways to override default behaviors

## Architecture Pillars

### Sora.Core
- **Purpose**: Unified runtime, secure defaults, health checks, observability
- **Key Components**: `AppHost.Current`, `SoraEnv`, bootstrap system
- **Pattern**: Foundation layer that other modules build upon

### Sora.Messaging
- **Purpose**: Agnostic communication layer for service-to-service messaging
- **Core Pattern**: Typed messaging with `entity.Send()` and `services.On<T>(handler)`
- **Message Types**:
  - **Typed**: `Device.Send()` (broadcast), `Device.SendTo("target")` (targeted)
  - **Named**: `Send("action", payload)`, handled by `On("action", handler)`
- **Semantics**: Fire-and-forget, no response expectations
- **Integration**: Clean DI sugar methods (`services.On<T>()`, `services.OnCommand<T>()`)
- **Auto-Registration**: Via `SoraAutoRegistrar` in `Sora.Messaging.Core`

#### Key APIs:
```csharp
// Sending
await entity.Send(); // Broadcast
await entity.SendTo("target"); // Targeted
await messageBus.SendAsync(message);

// Receiving
services.On<Device>(device => ProcessDevice(device))
        .OnCommand<ControlCommand>(cmd => ExecuteCommand(cmd))
        .On("seed", payload => HandleSeed(payload));
```

### Sora.Flow
- **Purpose**: Model-typed coordination/orchestration pipeline
- **Pipeline**: ingest → standardize → key → associate → project
- **Core Types**: 
  - `FlowEntity<T>`: Canonical model shapes
  - `FlowValueObject<T>`: Value objects within entities
  - `StageRecord<T>`: Pipeline stage records
  - `DynamicFlowEntity<T>`: Runtime transport format
- **Processing**: Background workers handle intake, association, projection
- **Auto-Discovery**: Scans for `FlowEntity<>` and `FlowValueObject<>` types
- **Current Issue**: `entity.Send()` bypasses messaging and goes direct to intake (WRONG)

#### Key Components:
- **Flow Intake System**: Sophisticated processing pipeline
- **Background Workers**: Model association and projection workers
- **Identity Stamping**: Server-side adapter identity management
- **Materialization**: Policy-driven canonical model building

## Integration Architecture (Target State)

### Messaging-First Flow
```
Adapters → Sora.Messaging → [FlowOrchestrator] → Sora.Flow.Intake → Business Logic
```

### Key Integration Points:
1. **Fixed `entity.Send()`**: Must use messaging, not direct Flow intake
2. **Orchestrator Pattern**: Services marked `[FlowOrchestrator]` bridge messaging → Flow
3. **Auto-Channel Provisioning**: Framework creates channels for each Flow type
4. **Bidirectional Communication**: Orchestrators can send to specific adapters or broadcast

## Auto-Registration Pattern

### Structure:
```csharp
public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Module.Name";
    public string? ModuleVersion => GetVersion();
    
    public void Initialize(IServiceCollection services)
    {
        // Register core services
        // Perform auto-discovery
        // Configure default behaviors
    }
    
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        // Report module capabilities and settings
    }
}
```

### Discovery Patterns:
- Assembly scanning with `AppDomain.CurrentDomain.GetAssemblies()`
- Attribute-based marking (`[FlowAdapter]`, `[FlowOrchestrator]`)
- Base type detection (`FlowEntity<>`, `FlowValueObject<>`)
- Configuration-gated registration

## Configuration Philosophy
- **Sane Defaults**: Containers auto-start, development requires opt-in
- **Convention-Based**: `Sora:Module:Setting` hierarchy
- **Environment Aware**: Different behaviors in containers vs development
- **Override Friendly**: All defaults can be explicitly configured