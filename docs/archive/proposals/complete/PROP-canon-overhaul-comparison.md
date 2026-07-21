# Koan.Canon Overhaul: Proposal Comparison & Synthesis

**Date**: 2025-10-05
**Purpose**: Compare two architectural proposals for Koan.Canon redesign and identify complementary insights

> **Status (2025-10):** Implemented via the shipping canon runtime under [`src/Koan.Canon.Domain`](../../../../src/Koan.Canon.Domain). The combined design landed as documented in [SPEC-canon-runtime.md](../specifications/SPEC-canon-runtime.md), delivering the local-first runtime (`CanonEntity<T>.Canonize(...)`) and transport-agnostic pipeline contracts (`ICanonRuntime`, `ICanonPipelineContributor`, etc.).

---

## Proposal Comparison Matrix

| Aspect | My Proposal (PROP-canon-overhaul.md) | Other Proposal (PROP-canon-overhaul-2.md) |
|--------|--------------------------------------|-------------------------------------------|
| **Primary Focus** | Local-first, message-optional architecture | Transport-agnostic pipeline engine with transformation hooks |
| **Main Problem** | Messaging coupling prevents simple usage | Pipeline mixing concerns + lack of extensibility hooks |
| **Data Model** | Collapse 11 entities → 3 (CanonEntity, CanonIndex, CanonStage) | Keep entities, abstract via ICanonStore<T> |
| **API Surface** | `await device.Canonize(origin: "sourceA")` | `await ICanonRuntime.Canonize<T>(entity, options)` |
| **Architecture** | Strategy pattern (Local vs Distributed) | Pipeline stages with hook contracts |
| **Extensibility** | Canon.OnUpdate handlers | Per-phase hooks (IIntakeStep, IValidationStep, etc.) |
| **Package Structure** | 2 packages (Core, Messaging) | 5 packages (Domain, Metadata, Engine, Runtime, Transport) |
| **Operations** | Basic canonization | Canonization + RebuildViews + Replay |
| **Observability** | Not explicitly addressed | ICanonPipelineObserver + ICanonEventPublisher |
| **Migration** | 12-week plan with backward compat | Feature toggle approach, gradual switch |

---

## Strengths of My Proposal

### 1. Framework Principle Analysis
✅ **Deep alignment analysis** with specific file/line references showing violations of:
- "Reference = Intent" principle
- Entity-First development patterns
- Provider transparency

### 2. Data Model Simplification
✅ **Concrete consolidation plan**:
- 11 entities per model → 3 entities total
- 11 storage containers → 1-2 containers
- Removes custom naming overrides
- **Specific migration scripts** for index consolidation

### 3. Embedded Metadata Approach
✅ **CanonMetadata property** on entities (simpler than separate entities):
```csharp
public class Device : CanonEntity<Device> {
    [JsonProperty("__canon")]
    public CanonMetadata Metadata { get; internal set; } = new();
}
```

### 4. Deployment Pattern Clarity
✅ **Three explicit patterns**:
- Local-first (simple applications)
- Distributed (centralized canonizer)
- Hybrid (local + eventual sync)

### 5. Performance Metrics
✅ **Quantified improvements**:
- Database round-trips: 7+ → 2 operations
- Storage overhead: 11 containers → 1-2
- Setup complexity: 5 steps → 1 step

### 6. Detailed Migration Path
✅ **Phase-by-phase plan** with:
- Specific files to create/modify/delete
- Migration scripts for data consolidation
- Backward compatibility shims
- 12-week timeline

---

## Strengths of Other Proposal

### 1. Pipeline Hook Architecture ⭐
✅ **Sophisticated transformation system**:

| Phase | Hook Contract | Capabilities |
|-------|--------------|--------------|
| Intake | `IIntakeStep` | BeforeIntake (mutate request), AfterIntake (augment metadata) |
| Validation | `IValidationStep` | Validate, OnValid, OnInvalid transforms |
| Aggregation | `IAggregationStep` | OnSelectAggregationKey, OnResolveCanonicalId, OnConflict |
| Policy | `IPolicyStep` | OnPolicyEvaluated, OnPolicyApplied |
| Projection | `IProjectionStep` | OnBuildCanonicalView, OnBuildLineage, OnProduceCustomView |
| Distribution | `IDistributionStep` | BeforeDistribute, AfterDistribute |

**Advantage**: Precise extension points with deterministic ordering - much more powerful than my Canon.OnUpdate approach.

### 2. Operational Capabilities ⭐
✅ **ICanonRuntime operational methods**:
```csharp
public interface ICanonRuntime {
    Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options);

    // REBUILD VIEWS - critical for data migrations
    Task RebuildViews<T>(string canonicalId, string[]? views);

    // REPLAY - audit/debugging capability
    IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from, DateTimeOffset? to);

    // OBSERVABILITY
    IDisposable RegisterObserver(ICanonPipelineObserver observer);
}
```

**Advantage**: My proposal lacks operational tooling - this is critical for production scenarios.

### 3. Observability & Events ⭐
✅ **Structured telemetry**:
- `ICanonPipelineObserver` with BeforePhase, AfterPhase, OnError callbacks
- `ICanonEventPublisher` emitting CanonizationStarted, Completed, Parked, Failed
- CLI tools consuming runtime for replay/reprojection

**Advantage**: Production-grade observability vs my basic approach.

### 4. Metadata Provider Pattern ⭐
✅ **ICanonMetadataProvider**:
- Scans assemblies once at boot
- Produces `CanonModelDescriptor<T>` with:
  - Aggregation keys
  - Parent relationships
  - External ID policies
  - Default policies
  - Custom transforms

**Advantage**: Centralized metadata discovery vs scattered attribute reading in my approach.

### 5. Package Granularity ⭐
✅ **Five focused packages**:
```
Koan.Canon.Domain     → Entities, descriptors
Koan.Canon.Metadata   → Metadata provider
Koan.Canon.Engine     → Pipeline orchestrator
Koan.Canon.Runtime    → ICanonRuntime surface
Koan.Canon.Transport  → Optional adapters
```

**Advantage**: Better separation of concerns vs my 2-package approach.

### 6. Edge Case Handling ⭐
✅ **Explicit strategies**:
- **Healing queue** for conflicting aggregation data
- **Blob storage** for massive payloads
- **Parent dependency parking** with metadata
- **Transport resilience** with retry/replay

**Advantage**: Production-ready vs my basic approach.

### 7. CLI Tooling ⭐
✅ **Command-line operations**:
- `Canonize` - manual entity canonization
- `Replay` - historical replay for debugging
- `RebuildViews` - projection rebuilding

**Advantage**: Essential for operations, missing from my proposal.

---

## Complementary Insights for Synthesis

### Critical Additions to My Proposal

#### 1. Adopt Pipeline Hook Architecture
**Integrate**: The 6-phase pipeline (Intake → Validation → Aggregation → Policy → Projection → Distribution) with per-phase hook contracts.

**Modification**: Keep my simple LocalCanonProcessor for basic scenarios, but add ICanonPipelineBuilder for advanced scenarios:

```csharp
// Simple scenario (my current proposal)
await device.Canonize(origin: "sourceA"); // Uses default pipeline

// Advanced scenario (from other proposal)
services.AddCanonEngine(builder => {
    builder.ConfigureModel<Device>(model => {
        model.AddValidationStep(new DeviceValidator());
        model.AddAggregationStep(new ConflictResolver());
        model.AddProjectionStep(new CustomViewBuilder());
    });
});
```

#### 2. Add Operational Methods to ICanonProcessor
**Extend my interface**:
```csharp
public interface ICanonProcessor {
    // Existing
    Task<T> CanonizeAsync<T>(T entity, string origin, CancellationToken ct);

    // NEW from other proposal
    Task RebuildViewsAsync<T>(string canonicalId, string[]? views, CancellationToken ct);
    IAsyncEnumerable<CanonRecord> ReplayAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    IDisposable RegisterObserver(ICanonObserver observer);
}
```

#### 3. Add Observability Layer
**Integrate**:
- `ICanonPipelineObserver` for phase-level telemetry
- `ICanonEventPublisher` for structured events
- Default implementation forwarding to Koan event bus

#### 4. Adopt Metadata Provider Pattern
**Replace** my scattered attribute reading with:
```csharp
public interface ICanonMetadataProvider {
    CanonModelDescriptor<T> GetDescriptor<T>() where T : CanonEntity<T>;
}

public class CanonModelDescriptor<T> {
    public string[] AggregationKeys { get; init; }
    public ParentRelationship? Parent { get; init; }
    public Dictionary<string, PolicyDefinition> Policies { get; init; }
    // ...
}
```

#### 5. Add CLI Tooling
**Create** `Koan.Canon.Cli` package with:
- `canon canonize <type> <json>` - manual canonization
- `canon replay --from <date> --to <date>` - replay operations
- `canon rebuild-views <canonical-id>` - projection rebuilding

#### 6. Enhance Package Structure
**Refine** my 2-package approach to 4 packages:
```
Koan.Canon.Core       → Entities, ICanonProcessor, LocalCanonProcessor
Koan.Canon.Engine     → Pipeline hooks, ICanonPipelineBuilder (optional)
Koan.Canon.Messaging  → Transport adapter (optional)
Koan.Canon.Cli        → Command-line tools (optional)
```

#### 7. Add Edge Case Strategies
**Document** explicit handling:
- Healing queue implementation
- Blob storage for large payloads
- Parent dependency resolution with parking
- Transport resilience patterns

---

## Synthesis: Enhanced Proposal

### Tiered Architecture

**Tier 1: Simple (90% of scenarios)**
```csharp
// My proposal's simplicity preserved
public class Device : CanonEntity<Device> {
    [AggregationTag] public string Serial { get; set; } = "";
}

await device.Canonize(origin: "sourceA"); // Just works
```

**Tier 2: Advanced (Complex enterprise scenarios)**
```csharp
// Other proposal's sophistication added
services.AddCanonEngine(builder => {
    builder.ConfigureModel<Device>(model => {
        model.Intake(intake => intake.BeforeIntake(ValidateSerial));
        model.Aggregation(agg => agg.OnConflict(ConflictStrategy.PreferNewer));
        model.Projection(proj => proj.AddCustomView("DeviceHealth", BuildHealthView));
    });
});
```

**Tier 3: Operational (Production tooling)**
```csharp
// Other proposal's operational capabilities
var runtime = serviceProvider.GetRequiredService<ICanonRuntime>();
await runtime.RebuildViewsAsync<Device>(canonId, views: ["canonical", "lineage"]);

// CLI equivalent
$ canon rebuild-views --type Device --id abc123 --views canonical,lineage
```

### Integrated API Design

```csharp
// Koan.Canon.Core/ICanonProcessor.cs (Enhanced from my proposal)
public interface ICanonProcessor
{
    // BASIC: Simple local canonization (my proposal)
    Task<T> CanonizeAsync<T>(T entity, string origin, CancellationToken ct = default)
        where T : CanonEntity<T>, new();

    // OPERATIONAL: View rebuilding (other proposal)
    Task RebuildViewsAsync<T>(string canonicalId, string[]? views = null, CancellationToken ct = default)
        where T : CanonEntity<T>, new();

    // OPERATIONAL: Replay capability (other proposal)
    IAsyncEnumerable<CanonRecord> ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default);

    // OBSERVABILITY: Observer registration (other proposal)
    IDisposable RegisterObserver(ICanonObserver observer);
}

// Koan.Canon.Engine/ICanonPipelineBuilder.cs (From other proposal)
public interface ICanonPipelineBuilder
{
    ICanonPipelineBuilder ConfigureModel<T>(Action<ICanonModelBuilder<T>> configure)
        where T : CanonEntity<T>, new();

    ICanonPipelineBuilder AddGlobalIntakeStep(IIntakeStep step);
    ICanonPipelineBuilder AddGlobalValidationStep(IValidationStep step);
    // ... other global steps
}

public interface ICanonModelBuilder<T> where T : CanonEntity<T>, new()
{
    ICanonModelBuilder<T> Intake(Action<IIntakeConfiguration> configure);
    ICanonModelBuilder<T> Validation(Action<IValidationConfiguration> configure);
    ICanonModelBuilder<T> Aggregation(Action<IAggregationConfiguration> configure);
    ICanonModelBuilder<T> Policy(Action<IPolicyConfiguration> configure);
    ICanonModelBuilder<T> Projection(Action<IProjectionConfiguration> configure);
    ICanonModelBuilder<T> Distribution(Action<IDistributionConfiguration> configure);
}
```

### Data Model Strategy (My approach + Storage abstraction)

**Keep my simplification** (11 entities → 3):
```csharp
// 1. Canonical entity with embedded metadata (my proposal)
public class Device : CanonEntity<Device> {
    [AggregationTag] public string Serial { get; set; } = "";

    [JsonProperty("__canon")]
    public CanonMetadata Metadata { get; internal set; } = new();
}

// 2. Shared index (my proposal)
public class CanonIndex : Entity<CanonIndex> {
    [Index] public string EntityType { get; set; } = "";
    [Index] public string ExternalSystem { get; set; } = "";
    [Index] public string ExternalId { get; set; } = "";
    [Index] public string CanonicalId { get; set; } = "";
}

// 3. Optional staging (my proposal, enhanced with status from other)
public class CanonStage<T> : Entity<CanonStage<T>> {
    public CanonStageStatus Status { get; set; } // Pending, Processing, Completed, Parked, Healing
    public string Origin { get; set; } = "";
    public T Payload { get; set; } = default!;
}
```

**Add storage abstraction** (from other proposal) for testability:
```csharp
// Koan.Canon.Core/Storage/ICanonStore.cs
public interface ICanonStore<T> where T : CanonEntity<T>, new()
{
    Task<T?> GetCanonicalAsync(string id, CancellationToken ct);
    Task SaveCanonicalAsync(T entity, CancellationToken ct);

    Task<CanonStage<T>[]> LoadStagesAsync(string canonId, CancellationToken ct);
    Task SaveStageAsync(CanonStage<T> stage, CancellationToken ct);

    Task<CanonIndex?> FindIndexAsync(string externalSystem, string externalId, CancellationToken ct);
    Task SaveIndexAsync(CanonIndex index, CancellationToken ct);
}

// Default implementation uses Data<,> (backward compatible)
public class DefaultCanonStore<T> : ICanonStore<T> where T : CanonEntity<T>, new()
{
    public async Task<T?> GetCanonicalAsync(string id, CancellationToken ct)
        => await Data<T, string>.GetAsync(id, ct);

    public async Task SaveCanonicalAsync(T entity, CancellationToken ct)
        => await entity.Save(ct);

    // ... other methods use Data<,> and EntityContext.Partition
}
```

---

## Recommended Synthesis Strategy

### Phase 1: Foundation (My Proposal) - Weeks 1-4
✅ Implement simple local-first canonization
✅ Data model consolidation (11 → 3 entities)
✅ LocalCanonProcessor with basic merge logic
✅ CanonEntity<T>.Canonize() method

**Outcome**: Basic canonization works without messaging

### Phase 2: Pipeline Architecture (Other Proposal) - Weeks 5-8
✅ Add ICanonPipelineBuilder and hook contracts
✅ Implement 6-phase pipeline (Intake → Distribution)
✅ ICanonStore<T> abstraction
✅ ICanonMetadataProvider for metadata discovery

**Outcome**: Advanced extensibility for complex scenarios

### Phase 3: Operational Tooling (Other Proposal) - Weeks 9-10
✅ Add RebuildViews, Replay methods
✅ Implement ICanonPipelineObserver
✅ Create ICanonEventPublisher
✅ Build CLI tools package

**Outcome**: Production-ready operational capabilities

### Phase 4: Transport Adapter (Both Proposals) - Weeks 11-12
✅ Optional Koan.Canon.Messaging package
✅ CanonMessagingBridge (my proposal)
✅ Transport resilience (other proposal)

**Outcome**: Optional distributed canonization

---

## Final Recommendation

**Adopt a hybrid approach** that combines:

1. **My proposal's strengths**:
   - Data model simplification (11 → 3)
   - Framework alignment analysis
   - Local-first emphasis
   - Simple developer experience for common cases

2. **Other proposal's strengths**:
   - Pipeline hook architecture for extensibility
   - Operational capabilities (RebuildViews, Replay)
   - Observability patterns
   - CLI tooling
   - Edge case handling

**Result**: A system that is:
- ✅ Simple for basic scenarios (`await device.Canonize("sourceA")`)
- ✅ Powerful for complex scenarios (pipeline hooks)
- ✅ Production-ready (observability, CLI tools)
- ✅ Data-efficient (3 entities vs 11)
- ✅ Framework-aligned (local-first, entity-scoped)

The enhanced proposal preserves the simplicity of my approach while adding the sophistication and operational maturity of the other proposal.
