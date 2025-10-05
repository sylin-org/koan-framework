# Canon Runtime Specification

**Version**: 1.0
**Status**: Active
**Last Updated**: 2025-10-05

---

## Contract

### Audience
Framework contributors and application teams building canonical data systems.

### Inputs
- Canon domain entity definitions (`CanonEntity<T>`, `CanonValueObject<T>`)
- Runtime configuration (`CanonizationOptions`, pipeline contributors)
- Koan entity-first data access primitives

### Outputs
- Composable canonization pipelines with deterministic execution
- Metadata tracking (provenance, lineage, policies)
- HTTP API surfaces for entity CRUD + canonization
- Admin operations (replay, rebuild, diagnostics)

### Success Criteria
- Applications build canon pipelines without legacy assemblies
- Entity patterns and provider transparency remain intact
- Every pipeline phase exposes deterministic hooks
- Developers canonize entities with minimal cognitive load

### Error Modes
- Misconfigured stage behaviors → validation failures with actionable diagnostics
- Missing data providers → graceful fallback to in-memory or parking
- Incompatible adapter migrations → detected at boot with clear remediation
- Concurrency violations → stage transitions guard against double-processing

---

## 1. Core Concepts

### What is Canonization?

Canonization is the process of transforming incoming data from multiple sources into a single, authoritative representation (the "canonical" record). The Canon runtime orchestrates this transformation through a multi-stage pipeline while tracking metadata, lineage, and quality signals.

### Pipeline Architecture

```
Intake → Validation → Aggregation → Policy → Projection → Distribution
   ↓         ↓            ↓           ↓          ↓            ↓
[Stage Contributors execute in deterministic order per phase]
   ↓         ↓            ↓           ↓          ↓            ↓
[Observers receive events for telemetry and auditing]
```

**Phases**:
- **Intake**: Initial payload reception, origin assignment, metadata initialization
- **Validation**: Data quality checks, schema validation, business rules
- **Aggregation**: Identity resolution, external ID mapping, conflict detection
- **Policy**: Attribute selection, versioning, compliance enforcement
- **Projection**: Canonical view generation, lineage tracking, custom projections
- **Distribution**: Downstream notification, event publishing, integration hooks

### State Model

Canon entities carry a unified state projection combining lifecycle, readiness, and consumer signals:

**`CanonState`** - Immutable projection tracking:
- **Lifecycle**: Active, PendingRetirement, Superseded, Archived, Withdrawn
- **Readiness**: Complete, PendingRelationships, PendingEnrichment, Provisional, RequiresManualReview, Degraded
- **Signals**: Key/value dictionary for downstream hints (e.g., `remediation:reason`, `pending:dependency`)

---

## 2. Domain Model

### Canonical Entities

```csharp
public abstract class CanonEntity<TModel> : Entity<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public CanonMetadata Metadata { get; set; }
    public CanonState State { get; private set; }
    public CanonLifecycle Lifecycle => State.Lifecycle;

    // State transformation
    public void SetState(CanonState state);
    public void ApplyState(Func<CanonState, CanonState> transform);

    // Lifecycle helpers
    public void MarkSuperseded(string replacementId, string? reason = null);
    public void Archive(string? notes = null);
    public void Restore();
    public void Withdraw(string reason);
}
```

**Key Properties**:
- Inherits Koan's `Entity<T>` pattern (auto GUID v7, provider transparency)
- `Metadata` tracks provenance, external IDs, lineage, policy outcomes
- `State` exposes immutable lifecycle + readiness + signals snapshot
- `Id` ensures metadata canonical ID alignment on first access

### Value Objects

```csharp
public abstract class CanonValueObject<TValue> : Entity<TValue>
    where TValue : CanonValueObject<TValue>, new()
{
    public string? CanonicalReferenceId { get; set; }
    public CanonMetadata Metadata { get; set; }
}
```

Value objects participate in canonization but are scoped to a parent canonical entity.

### Index

```csharp
public sealed class CanonIndex : Entity<CanonIndex>
{
    public string EntityType { get; set; }        // CLR type name
    public string Key { get; set; }               // Aggregation key or external ID
    public CanonIndexKeyKind Kind { get; set; }   // Aggregation | ExternalId
    public string CanonicalId { get; private set; }
    public string? Origin { get; private set; }
    public Dictionary<string, string?> Attributes { get; set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string canonicalId, string? origin = null, IReadOnlyDictionary<string, string?>? attributes = null);
}
```

Shared lookup index across all canonical entities for aggregation and identity resolution.

### Stage

```csharp
public sealed class CanonStage<TModel> : Entity<CanonStage<TModel>>
    where TModel : CanonEntity<TModel>, new()
{
    public string EntityType { get; private set; }
    public string? CanonicalId { get; private set; }
    public string? Origin { get; private set; }
    public TModel? Payload { get; set; }
    public CanonStageStatus Status { get; private set; }  // Pending | Processing | Completed | Failed | Parked
    public string? CorrelationId { get; set; }
    public List<CanonStageTransition> Transitions { get; set; }

    public void MarkProcessing(string? actor = null, string? notes = null);
    public void MarkCompleted(string? actor = null, string? notes = null);
    public void Park(string reason, string? actor = null);
    public void MarkFailed(string errorCode, string message, string? actor = null);
    public void ResetToPending(string? actor = null, string? notes = null);
}
```

Optional staging for deferred or distributed processing.

### Metadata

```csharp
public sealed class CanonMetadata
{
    public string? CanonicalId { get; private set; }
    public string? Origin { get; private set; }
    public DateTimeOffset CanonizedAt { get; private set; }

    public Dictionary<string, CanonExternalId> ExternalIds { get; set; }
    public Dictionary<string, CanonSourceAttribution> Sources { get; set; }
    public Dictionary<string, CanonPolicySnapshot> Policies { get; set; }
    public Dictionary<string, string> Tags { get; set; }
    public CanonLineage Lineage { get; set; }
    public CanonState State { get; set; }

    // External ID management
    public CanonExternalId RecordExternalId(string scheme, string value, ...);
    public bool TryGetExternalId(string scheme, out CanonExternalId? externalId);

    // Source attribution
    public CanonSourceAttribution RecordSource(string sourceKey, Action<CanonSourceAttribution>? configure = null);

    // Tag management
    public void SetTag(string key, string value);
    public bool TryGetTag(string key, out string? value);

    // Metadata operations
    public CanonMetadata Clone();
    public void Merge(CanonMetadata incoming, bool preferIncoming = true);
}
```

Metadata tracks ingestion history, lineage, policy outcomes, and external identifiers.

---

## 3. Runtime & Pipeline

### ICanonRuntime

```csharp
public interface ICanonRuntime
{
    Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new();

    Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new();

    IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    IDisposable RegisterObserver(ICanonPipelineObserver observer);
}
```

Entry point for all canonization operations. No `Async` suffix per framework conventions.

### Pipeline Contributors

```csharp
public interface ICanonPipelineContributor<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    CanonPipelinePhase Phase { get; }
    ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken cancellationToken);
}
```

**Unified contributor pattern** replaces phase-specific interfaces for simplicity and composability.

**Example**:
```csharp
public sealed class ValidationContributor : ICanonPipelineContributor<CustomerCanon>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<CustomerCanon> context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Entity.Name))
        {
            context.Entity.ApplyState(state => state
                .WithReadiness(CanonReadiness.RequiresManualReview)
                .WithSignal("validation:field", "Name"));

            return ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
            {
                Phase = Phase,
                StageStatus = CanonStageStatus.Parked,
                Message = "Validation failed: Name required"
            });
        }

        context.Metadata.SetTag("validated", "true");
        return ValueTask.FromResult<CanonizationEvent?>(null);
    }
}
```

### Pipeline Context

```csharp
public sealed class CanonPipelineContext<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public TModel Entity { get; }
    public CanonMetadata Metadata { get; }
    public CanonizationOptions Options { get; }
    public IServiceProvider Services { get; }
    public CanonStage<TModel>? Stage { get; }

    // Context items for inter-phase communication
    public IReadOnlyDictionary<string, object?> Items { get; }
    public void SetItem(string key, object? value);
    public bool TryGetItem<TValue>(string key, out TValue? value);
}
```

Exposes entity state, metadata, options, and service provider to contributors.

### Observers

```csharp
public interface ICanonPipelineObserver
{
    ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken cancellationToken = default);
    ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken cancellationToken = default);
    ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken cancellationToken = default);
}
```

Registered observers receive callbacks for telemetry, auditing, and analytics.

### Canonization Options

```csharp
public sealed record CanonizationOptions
{
    public string? Origin { get; init; }
    public string? CorrelationId { get; init; }
    public bool ForceRebuild { get; init; }
    public bool SkipDistribution { get; init; }
    public CanonStageBehavior StageBehavior { get; init; }  // Default | StageOnly | Immediate
    public string[]? RequestedViews { get; init; }
    public Dictionary<string, string?> Tags { get; init; }

    public CanonizationOptions WithOrigin(string origin);
    public CanonizationOptions WithTag(string key, string? value);
    public CanonizationOptions WithStageBehavior(CanonStageBehavior behaviour);
    public static CanonizationOptions Merge(CanonizationOptions primary, CanonizationOptions? fallback);
}
```

### Persistence Strategy

```csharp
public interface ICanonPersistence
{
    Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken) where TModel : CanonEntity<TModel>, new();
    Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken) where TModel : CanonEntity<TModel>, new();
}
```

**Default implementation** (`DefaultCanonPersistence`):
- Delegates to `entity.Save()` for canonical entities
- Delegates to `stage.Save()` for stage records
- Relies on Koan's provider-transparent entity statics

**Custom implementations**:
- Override for specific storage strategies (e.g., event sourcing, CQRS)
- Maintain same semantic guarantees (atomic writes, consistency)

---

## 4. Web Integration

### Entity Controllers

```csharp
public class CanonEntitiesController<T> : EntityController<T>
    where T : CanonEntity<T>, new()
{
    // Inherits standard CRUD from EntityController<T>
    // POST/PUT operations invoke ICanonRuntime.Canonize instead of direct Save
}
```

Generic controllers registered per canonical entity via auto-discovery.

**Routes**: `/api/canon/{model-slug}` (e.g., `/api/canon/customer-canon`)

**Request Options** (headers/query):
- `X-Canon-Origin` or `?origin=` → Set origin
- `?forceRebuild=true` → Trigger view reprojection
- `?views=view-a,view-b` → Scope rebuilds
- `?tag.foo=bar` or `X-Canon-Tag-Foo: bar` → Attach tags
- `?stageBehavior=StageOnly` → Force staging

### Discovery Endpoint

**Route**: `/api/canon/models`

Returns canonical models with pipeline metadata:
```json
{
  "models": [
    {
      "type": "CustomerCanon",
      "slug": "customer-canon",
      "route": "/api/canon/customer-canon",
      "isValueObject": false,
      "hasPipeline": true,
      "phases": ["Intake", "Validation", "Aggregation", "Policy", "Projection", "Distribution"]
    }
  ]
}
```

### Admin Endpoints

**Route**: `/api/canon/admin`

Operations:
- `POST /api/canon/admin/replay?from={date}&to={date}` → Stream canonization records
- `POST /api/canon/admin/rebuild/{model}/{id}?views=a,b` → Rebuild views for entity

### Auto-Registration

`Koan.Canon.Web/Initialization/KoanAutoRegistrar`:
- Scans assemblies for `CanonEntity<>` and `CanonValueObject<>` types
- Registers generic controllers via `GenericControllers.AddGenericController`
- Wires `ICanonRuntime`, `ICanonModelCatalog`, configurators into DI
- Emits boot report with discovered models and routes

---

## 5. Extension Points

### Pipeline Configuration

```csharp
// Builder pattern
var runtime = new CanonRuntimeBuilder()
    .UsePersistence(new CustomCanonPersistence())
    .ConfigurePipeline<CustomerCanon>(pipeline =>
    {
        pipeline.AddContributor(new ValidationContributor());
        pipeline.AddStep(CanonPipelinePhase.Aggregation, async (context, ct) =>
        {
            // Inline step logic
            context.Metadata.SetTag("aggregated", "true");
            await ValueTask.CompletedTask;
        }, "Aggregation complete");
    })
    .Build();

// Dependency injection
services.AddCanonRuntime(builder =>
{
    builder.ConfigurePipeline<ProductCanon>(pipeline => { ... });
});
```

### Runtime Configurators

```csharp
public interface ICanonRuntimeConfigurator
{
    void Configure(CanonRuntimeBuilder builder);
}

public sealed class ProductCanonConfigurator : ICanonRuntimeConfigurator
{
    public void Configure(CanonRuntimeBuilder builder)
    {
        builder.ConfigurePipeline<ProductCanon>(pipeline =>
        {
            pipeline.AddStep(CanonPipelinePhase.Intake, async (context, ct) =>
            {
                context.Metadata.SetTag("source", "importer");
                await ValueTask.CompletedTask;
            });
        });
    }
}
```

Configurators are auto-discovered and executed during runtime initialization.

### Extension Methods

```csharp
public static class CanonRuntimeExtensions
{
    public static Task<CanonizationResult<T>> Canonize<T>(this T entity, IServiceProvider services, CanonizationOptions? options = null, CancellationToken ct = default);
    public static Task<CanonizationResult<T>> Canonize<T>(this T entity, ICanonRuntime runtime, CanonizationOptions? options = null, CancellationToken ct = default);
    public static Task RebuildViews<T>(this T entity, IServiceProvider services, string[]? views = null, CancellationToken ct = default);
}
```

**Usage**:
```csharp
var customer = new CustomerCanon { Name = "Acme Corp" };
var result = await customer.Canonize(services, new CanonizationOptions { Origin = "crm" });
```

---

## 6. Edge Cases & Patterns

### Concurrency & Idempotency

**Challenge**: Multiple workers processing the same entity must avoid double-processing.

**Mitigation**:
- Stage transitions are guarded with optimistic concurrency (via `UpdatedAt` timestamp)
- Contributors should be idempotent (re-entrant executions produce same result)
- Use deterministic transition keys when calculating aggregation outcomes

### Provider Capability Gaps

**Challenge**: Data provider lacks streaming or specific query features.

**Mitigation**:
- Runtime detects capabilities via `Data<T, K>.QueryCaps`
- Falls back to paged reads (`FirstPage`, `Page`) when streaming unavailable
- Logs degraded performance warnings for operational visibility

### Partial Stage Failures

**Challenge**: Stage mutates state then fails before completion.

**Mitigation**:
- Use compensating actions (explicit rollback of staged writes)
- Emit `CanonizationOutcome.Parked` with remediation signals
- Contributors should mutate context atomically or not at all

### State Drift & Metadata Merges

**Challenge**: Concurrent updates from different sources create conflicting metadata.

**Mitigation**:
- `CanonState.Merge(incoming, preferIncoming)` applies deterministic merge logic
- Readiness/lifecycle transitions are monotonic (can only degrade, not randomly jump)
- Signals are deduplicated (last-write-wins with case-insensitive keys)

### Missing Data Dependencies

**Challenge**: Entity references parent not yet canonized.

**Mitigation**:
```csharp
entity.ApplyState(state => state
    .WithReadiness(CanonReadiness.PendingRelationships)
    .WithSignal("pending:parent", parentId));
```
Downstream consumers defer processing until dependency resolves.

### Schema Evolution

**Challenge**: Metadata schema changes over time, old records become incompatible.

**Mitigation**:
- Version metadata aggressively (store schema version in tags)
- Use `CanonMetadata.Merge` with graceful null handling for missing fields
- Implement migration contributors that detect old schema and upgrade in-place

### Remediation Workflow

**Pattern**:
```csharp
public void FlagForRemediation(string reason)
{
    ApplyState(state => state
        .WithReadiness(CanonReadiness.RequiresManualReview)
        .WithSignal("remediation:reason", reason));
}
```

Downstream systems query for entities with `RequiresManualReview` readiness and process healing queue.

### Large Payloads

**Challenge**: Entity too large for in-memory processing.

**Mitigation**:
- Intake contributor streams payload to blob storage
- Injects storage pointer into metadata: `context.Metadata.SetTag("blob:uri", blobUri)`
- Downstream contributors load from blob as needed

### Replay Volume Management

**Challenge**: `ICanonRuntime.Replay` materializes records into memory.

**Mitigation**:
- Use streaming `IAsyncEnumerable<CanonizationRecord>`
- Callers paginate via `from`/`to` filters
- Future: Add batch size parameter and checkpoint tokens

---

## 7. API Reference

### Key Types

#### Enumerations

**`CanonLifecycle`**:
- `Active` → Canonical entity is current and authoritative
- `PendingRetirement` → Scheduled for replacement
- `Superseded` → Replaced by newer canonical ID
- `Archived` → No longer participates in projections
- `Withdrawn` → Irrecoverable validation/policy failure

**`CanonReadiness`**:
- `Unknown` → Not explicitly set
- `Complete` → Safe for downstream consumption
- `PendingRelationships` → Waiting on parent/child canonization
- `PendingEnrichment` → Awaiting external data
- `Provisional` → May be retracted
- `RequiresManualReview` → Failed quality gates
- `Degraded` → Should not be distributed further

**`CanonPipelinePhase`**:
- `Intake`, `Validation`, `Aggregation`, `Policy`, `Projection`, `Distribution`

**`CanonStageBehavior`**:
- `Default` → Engine decides (stage when pipeline configured for deferred processing)
- `StageOnly` → Force staging without immediate processing
- `Immediate` → Bypass staging, process inline

**`CanonStageStatus`**:
- `Pending`, `Processing`, `Completed`, `Failed`, `Parked`

**`CanonizationOutcome`**:
- `Canonized` → Successfully completed all phases
- `Parked` → Requires manual intervention
- `Failed` → Unrecoverable error
- `Staged` → Deferred for async processing

#### Results & Records

**`CanonizationResult<T>`**:
```csharp
public sealed class CanonizationResult<T> where T : CanonEntity<T>, new()
{
    public T Canonical { get; }
    public CanonizationOutcome Outcome { get; }
    public CanonMetadata Metadata { get; }
    public IReadOnlyList<CanonizationEvent> Events { get; }
    public bool ReprojectionTriggered { get; }
    public bool DistributionSkipped { get; }

    public CanonizationResult<T> WithOutcome(CanonizationOutcome outcome);
    public CanonizationResult<T> WithEvents(params CanonizationEvent[] additionalEvents);
}
```

**`CanonizationRecord`**:
```csharp
public sealed class CanonizationRecord
{
    public string CanonicalId { get; init; }
    public string EntityType { get; init; }
    public CanonPipelinePhase Phase { get; init; }
    public CanonStageStatus StageStatus { get; init; }
    public CanonizationOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }
    public CanonMetadata Metadata { get; init; }
    public CanonizationEvent? Event { get; init; }
}
```

**`CanonizationEvent`**:
```csharp
public sealed class CanonizationEvent
{
    public CanonPipelinePhase Phase { get; set; }
    public CanonStageStatus StageStatus { get; set; }
    public CanonState? CanonState { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Message { get; set; }
    public string? Detail { get; set; }
}
```

### Registration Helpers

**Service Collection Extensions**:
```csharp
public static class CanonRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCanonRuntime(this IServiceCollection services, Action<CanonRuntimeBuilder>? configure = null);
}
```

**Runtime Builder**:
```csharp
public sealed class CanonRuntimeBuilder
{
    public CanonRuntimeBuilder UsePersistence(ICanonPersistence persistence);
    public CanonRuntimeBuilder ConfigurePipeline<TModel>(Action<CanonPipelineBuilder<TModel>> configure) where TModel : CanonEntity<TModel>, new();
    public ICanonRuntime Build();
}
```

**Pipeline Builder**:
```csharp
public sealed class CanonPipelineBuilder<TModel> where TModel : CanonEntity<TModel>, new()
{
    public CanonPipelineBuilder<TModel> AddContributor(ICanonPipelineContributor<TModel> contributor);
    public CanonPipelineBuilder<TModel> AddStep(CanonPipelinePhase phase, Func<CanonPipelineContext<TModel>, CancellationToken, ValueTask> action, string? description = null);
    public CanonPipelineDescriptor<TModel> Build();
}
```

---

## 8. Quick Start Examples

### Minimal Inline Canonization

```csharp
// Define canonical entity
public sealed class CustomerCanon : CanonEntity<CustomerCanon>
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

// Configure runtime
var runtime = new CanonRuntimeBuilder()
    .ConfigurePipeline<CustomerCanon>(pipeline =>
    {
        pipeline.AddStep(CanonPipelinePhase.Validation, (context, ct) =>
        {
            if (string.IsNullOrWhiteSpace(context.Entity.Name))
            {
                context.Entity.ApplyState(s => s.WithReadiness(CanonReadiness.RequiresManualReview));
            }
            else
            {
                context.Metadata.SetTag("validated", "true");
            }
            return ValueTask.CompletedTask;
        });
    })
    .Build();

// Canonize entity
var customer = new CustomerCanon { Name = "Acme Corp", Email = "contact@acme.com" };
var result = await runtime.Canonize(customer, new CanonizationOptions { Origin = "crm" });

Console.WriteLine($"Outcome: {result.Outcome}, ID: {result.Canonical.Id}");
```

### Pipeline Contributor

```csharp
public sealed class EmailNormalizationContributor : ICanonPipelineContributor<CustomerCanon>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Intake;

    public ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<CustomerCanon> context, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(context.Entity.Email))
        {
            context.Entity.Email = context.Entity.Email.ToLowerInvariant().Trim();
            context.Metadata.SetTag("intake:email-normalized", "true");
        }

        return ValueTask.FromResult<CanonizationEvent?>(null);
    }
}

// Register
builder.ConfigurePipeline<CustomerCanon>(pipeline =>
{
    pipeline.AddContributor(new EmailNormalizationContributor());
});
```

### Web Controller Usage

```csharp
// Auto-discovered controller via Koan.Canon.Web
// POST /api/canon/customer-canon
// Headers: X-Canon-Origin: crm
// Body: { "name": "Acme Corp", "email": "CONTACT@ACME.COM" }
//
// Response:
// {
//   "canonical": { "id": "...", "name": "Acme Corp", "email": "contact@acme.com", ... },
//   "outcome": "Canonized",
//   "metadata": { ... },
//   "events": [ ... ]
// }
```

### Observer Registration

```csharp
public sealed class TelemetryObserver : ICanonPipelineObserver
{
    private readonly ILogger _logger;

    public ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("Phase {Phase} started for {EntityType}", phase, context.EntityType.Name);
        return ValueTask.CompletedTask;
    }

    public ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation("Phase {Phase} completed: {Message}", phase, @event.Message);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken ct = default)
    {
        _logger.LogError(exception, "Phase {Phase} failed", phase);
        return ValueTask.CompletedTask;
    }
}

// Register
var observer = new TelemetryObserver(logger);
var subscription = runtime.RegisterObserver(observer);
// Dispose subscription when done
```

---

## 9. Testing Guidance

### Unit Tests

**Location**: `tests/Koan.Canon.Domain.Tests`

**Coverage Areas**:
- `CanonStateTests` → State transitions, merges, signal management
- `CanonMetadataTests` → External ID recording, lineage tracking, tag operations
- `CanonRuntimeTests` → Pipeline execution, observer notifications, persistence delegation
- `CanonEntityTests` → Lifecycle helpers (MarkSuperseded, Archive, Withdraw)

**Pattern**:
```csharp
[Fact]
public async Task Canonize_WithValidation_ShouldParkOnFailure()
{
    var runtime = new CanonRuntimeBuilder()
        .ConfigurePipeline<TestEntity>(p => p.AddContributor(new FailingValidator()))
        .Build();

    var entity = new TestEntity();
    var result = await runtime.Canonize(entity);

    result.Outcome.Should().Be(CanonizationOutcome.Parked);
    entity.State.Readiness.Should().Be(CanonReadiness.RequiresManualReview);
}
```

### Integration Tests

**Coverage Areas**:
- Multi-stage pipelines against real providers (SQL, MongoDB, etc.)
- Controller request/response flows with header/query option parsing
- Replay and rebuild operations with seeded data

### Controller Tests

**Pattern**:
```csharp
[Fact]
public async Task Post_WithCanonOriginHeader_ShouldPassToRuntime()
{
    var controller = new CanonEntitiesController<TestEntity>(mockRuntime.Object);
    controller.ControllerContext.HttpContext = new DefaultHttpContext();
    controller.Request.Headers["X-Canon-Origin"] = "test-system";

    await controller.Post(new TestEntity { Name = "Test" });

    mockRuntime.Verify(r => r.Canonize(
        It.IsAny<TestEntity>(),
        It.Is<CanonizationOptions>(o => o.Origin == "test-system"),
        It.IsAny<CancellationToken>()));
}
```

---

## 10. References

### Related Documentation
- [Canon Runtime Architecture ADR](../decisions/ARCH-0058-canon-runtime-architecture.md) - Architectural decisions and rationale
- [Canon Runtime Migration Plan](../architecture/canon-runtime-migration.md) - Milestone tracking and cutover status
- [Data Access Pagination ADR](../decisions/DATA-0061-data-access-pagination-and-streaming.md) - Streaming patterns
- [Koan Architecture Principles](../architecture/principles.md) - Framework design philosophy

### Code Locations
- Domain: `src/Koan.Canon.Domain/`
- Web: `src/Koan.Canon.Web/`
- Tests: `tests/Koan.Canon.Domain.Tests/`, `tests/Koan.Canon.Web.Tests/`
- Samples: `samples/S8.Canon/`

### Migration Status
See [canon-runtime-migration.md](../architecture/canon-runtime-migration.md) for current milestone completion.

---

**End of Specification**
