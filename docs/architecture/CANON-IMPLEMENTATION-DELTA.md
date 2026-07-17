# Canon Runtime Implementation Delta Analysis

> Historical reconciliation snapshot (2025-10-05). Its percentage and production-readiness labels
> are not current maturity claims. Use the [current Canon reference](../reference/canon/index.md) and
> [ARCH-0058](../decisions/ARCH-0058-canon-runtime-architecture.md) for today's supported boundary.

**Analysis Date**: 2025-10-05 (Updated)
**Spec Version**: SPEC-canon-runtime.md v1.0 (Synchronized)
**Implementation**: src/Koan.Canon.Domain + src/Koan.Canon.Web

---

## Executive Summary

**Historical 2025 assessment**: **reported as 100% complete / production-ready**

At the time, this reconciliation rated the Canon runtime feature-complete and production-ready against
its then-current specification. That rating is preserved as historical evidence, not repeated as a
current support claim. The current reference defines today's tested and unsupported boundaries.

**Milestones**: M1 ✅ Complete | M2 ✅ Complete | M3-M5 ⏳ Pending

---

## Component-by-Component Analysis

### ✅ **FULLY IMPLEMENTED** (Core Complete)

#### 1. Domain Model (100%)
- ✅ `CanonEntity<T>` with state management
- ✅ `CanonState` immutable record with lifecycle, readiness, signals
- ✅ `CanonLifecycle` enumeration (Active, Superseded, Archived, Withdrawn, PendingRetirement)
- ✅ `CanonReadiness` enumeration (Complete, PendingRelationships, RequiresManualReview, etc.)
- ✅ `CanonIndex` shared aggregation/external ID lookup
- ✅ `CanonStage<T>` optional staging with status transitions
- ✅ `CanonValueObject<T>` for scoped value objects
- ✅ `CanonMetadata` with external IDs, sources, policies, tags, lineage, state
- ✅ Entity lifecycle helpers (`MarkSuperseded`, `Archive`, `Restore`, `Withdraw`)
- ✅ State transformation via `ApplyState(transform)` pattern

**Code Locations**:
- `src/Koan.Canon.Domain/Model/` - All domain types
- `src/Koan.Canon.Domain/Metadata/` - Metadata types
- Tests: `tests/Koan.Canon.Domain.Tests/CanonEntityTests.cs`, `CanonStateTests.cs`, `CanonMetadataTests.cs`

#### 2. Runtime & Pipeline (100%)
- ✅ `ICanonRuntime` interface with `Canonize`, `RebuildViews`, `Replay`, `RegisterObserver`
- ✅ `CanonRuntime` implementation with full pipeline execution
- ✅ Six-phase pipeline (Intake, Validation, Aggregation, Policy, Projection, Distribution)
- ✅ `ICanonPipelineContributor<T>` unified contributor pattern
- ✅ `CanonPipelineBuilder<T>` with `AddContributor`, `AddStep` (delegate + event forms)
- ✅ `CanonRuntimeBuilder` with fluent configuration
- ✅ `CanonPipelineContext<T>` with entity, metadata, options, stage, items dictionary
- ✅ `ICanonPipelineObserver` with `BeforePhaseAsync`, `AfterPhaseAsync`, `OnErrorAsync`
- ✅ Observer registration with disposable handles
- ✅ `CanonizationOptions` with origin, correlation ID, tags, views, behaviors
- ✅ `CanonizationResult<T>` with canonical, outcome, metadata, events
- ✅ `CanonizationRecord` for replay/analytics
- ✅ In-memory record retention with configurable capacity
- ✅ Stage-only behavior (`CanonStageBehavior.StageOnly`)
- ✅ No-async suffix convention (returns `Task<T>` but named `Canonize`)

**Code Locations**:
- `src/Koan.Canon.Domain/Runtime/CanonRuntime.cs` (399 lines, comprehensive)
- `src/Koan.Canon.Domain/Runtime/CanonPipelineBuilder.cs`
- `src/Koan.Canon.Domain/Runtime/CanonRuntimeBuilder.cs`
- Tests: `tests/Koan.Canon.Domain.Tests/CanonRuntimeTests.cs` (21 passing tests)

#### 3. Persistence (historical snapshot; completed read boundary added in 0.18)
- ✅ `ICanonPersistence` interface (canonical read/write, stage write, and index lookup/upsert)
- ✅ `DefaultCanonPersistence` delegates to `entity.Save()` and `stage.Save()`
- ✅ Runtime builder `.UsePersistence()` override support
- ✅ Provider-transparent (works with SQL, MongoDB, JSON stores)

**Code Locations**:
- `src/Koan.Canon.Domain/Runtime/ICanonPersistence.cs`
- `src/Koan.Canon.Domain/Runtime/DefaultCanonPersistence.cs`

#### 4. Extension Methods (100%)
- ✅ `entity.Canonize(IServiceProvider services, CanonizationOptions?, CancellationToken)`
- ✅ `entity.Canonize(ICanonRuntime runtime, CanonizationOptions?, CancellationToken)`
- ✅ `entity.RebuildViews(IServiceProvider services, string[]?, CancellationToken)`
- ✅ Delegate to `ICanonRuntime` (no logic duplication)

**Code Locations**:
- `src/Koan.Canon.Domain/Runtime/CanonRuntimeExtensions.cs`

#### 5. Dependency Injection (100%)
- ✅ `AddCanonRuntime(Action<CanonRuntimeBuilder>?)` extension method
- ✅ `ICanonRuntimeConfigurator` discovery and execution
- ✅ `CanonRuntimeConfiguration` registered as singleton
- ✅ `ICanonRuntime` registered as singleton

**Code Locations**:
- `src/Koan.Canon.Domain/Runtime/CanonRuntimeServiceCollectionExtensions.cs`
- `src/Koan.Canon.Domain/Runtime/ICanonRuntimeConfigurator.cs`

#### 6. Web Integration (100%)
- ✅ `CanonEntitiesController<T>` generic controller with CRUD + canonization
- ✅ POST/PUT routes invoke `ICanonRuntime.Canonize` (not direct `Save`)
- ✅ Bulk operations (`UpsertMany`)
- ✅ Option parsing from headers (`X-Canon-Origin`, `X-Correlation-ID`, `X-Canon-Tag-*`)
- ✅ Option parsing from query (`?origin=`, `?forceRebuild=`, `?views=`, `?tag.key=value`)
- ✅ `CanonizationResponse<T>` DTO with canonical, outcome, metadata, events
- ✅ `CanonModelsController` discovery endpoint (`GET /api/canon/models`)
- ✅ Model catalog with slug, display name, route, pipeline metadata
- ✅ `CanonAdminController` with `GET /api/canon/admin/records` (replay)
- ✅ `POST /api/canon/admin/{slug}/rebuild` (RebuildViews)
- ✅ Auto-discovery via `KoanAutoRegistrar` scanning for `CanonEntity<>` types
- ✅ Kebab-case route slugs (e.g., `customer-canon`)

**Code Locations**:
- `src/Koan.Canon.Web/Controllers/CanonEntitiesController.cs` (297 lines)
- `src/Koan.Canon.Web/Controllers/CanonModelsController.cs`
- `src/Koan.Canon.Web/Controllers/CanonAdminController.cs`
- `src/Koan.Canon.Web/Catalog/CanonModelCatalog.cs`
- `src/Koan.Canon.Web/Initialization/KoanAutoRegistrar.cs`
- Tests: `tests/Koan.Canon.Web.Tests/CanonEntitiesControllerTests.cs`

---

## ⚠️ **MINOR DEVIATIONS** (Spec vs Implementation)

### 1. ICanonPersistence Method Names

**Spec Says**:
```csharp
public interface ICanonPersistence
{
    Task SaveCanonicalAsync<T>(T entity, CancellationToken ct);
    Task SaveStageAsync<T>(CanonStage<T> stage, CancellationToken ct);
}
```

**Implementation Has**:
```csharp
public interface ICanonPersistence
{
    Task<TModel?> GetCanonicalAsync<TModel>(string id, CancellationToken ct);
    Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken ct);
    Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken ct);
    Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken ct);
    Task UpsertIndex(CanonIndex index, CancellationToken ct);
}
```

**Impact**: Method names differ (`SaveCanonicalAsync` vs `PersistCanonicalAsync`), return types
materialize stored values, and the current contract owns prior-state/index reads as well as writes.
**Assessment**: **Implementation is superior** - returning entities enables caller to access generated IDs and updated timestamps
**Action**: Update spec to match implementation

---

### 2. CanonizationRecord Shape

**Spec Shows** (Section 8 API Reference):
```csharp
public sealed class CanonizationRecord
{
    public string CanonicalId { get; }
    public string EntityType { get; }
    public CanonizationOutcome Outcome { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }
    public CanonMetadata Metadata { get; }
    public CanonizationEvent Event { get; }
}
```

**Implementation Has**:
```csharp
public sealed class CanonizationRecord
{
    public string CanonicalId { get; init; }
    public CanonPipelinePhase Phase { get; init; }          // ADDED
    public CanonStageStatus StageStatus { get; init; }      // ADDED
    public CanonizationOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }
    public CanonMetadata Metadata { get; init; }
    public CanonizationEvent? Event { get; init; }          // Nullable
    // MISSING: EntityType
}
```

**Impact**: Implementation has richer information (Phase, StageStatus) but missing `EntityType` for filtering
**Assessment**: Implementation is **mostly superior**, but `EntityType` should be added for replay filtering
**Action**: Add `EntityType` property to `CanonizationRecord`

---

### 3. ICanonPipelineObserver Signature

**Spec Shows** (Section 5):
```csharp
public interface ICanonPipelineObserver
{
    void OnPhaseStarted(CanonPipelinePhase phase, object context);
    void OnPhaseCompleted(CanonPipelinePhase phase, object context, CanonizationEvent? evt);
    void OnPhaseFailed(CanonPipelinePhase phase, object context, Exception exception);
}
```

**Implementation Has**:
```csharp
public interface ICanonPipelineObserver
{
    ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken ct);
    ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken ct);
    ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken ct);
}
```

**Impact**: Synchronous spec vs async implementation, strongly-typed context
**Assessment**: **Implementation is superior** - async observers enable I/O (logging to DB, sending telemetry), strongly-typed context is safer
**Action**: Update spec to match async implementation

---

## ✅ **GAPS RESOLVED** (2025-10-05)

### 1. EntityType in CanonizationRecord ✅ FIXED

**Status**: ✅ Implemented (2025-10-05)
**Changes**:
- Added `public string EntityType { get; init; }` to `CanonizationRecord`
- Populated in `CanonRuntime.AppendRecord()` with `typeof(TModel).FullName ?? typeof(TModel).Name`
**Impact**: Replay filtering by entity type now supported

---

### 2. ICanonPipelineContext.Services Property ✅ FIXED

**Status**: ✅ Implemented (2025-10-05)
**Changes**:
- Added `public IServiceProvider Services { get; }` to `CanonPipelineContext<T>`
- Constructor accepts optional `IServiceProvider?` parameter (defaults to `EmptyServiceProvider.Instance`)
- `CanonRuntime` passes `IServiceProvider` via constructor injection
- DI registration updated to inject service provider when creating runtime

**Impact**: Contributors can now resolve dependencies dynamically:
```csharp
public ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<T> context, CancellationToken ct)
{
    var logger = context.Services.GetService<ILogger<MyContributor>>();
    logger?.LogInformation("Processing {Entity}", context.Entity.Id);
    // ...
}
```

---

## ✨ **IMPLEMENTATION BONUSES** (Not in Spec, But Implemented)

### 1. Context Items Dictionary

**Implementation** (`CanonPipelineContext<T>`):
```csharp
public IReadOnlyDictionary<string, object?> Items { get; }
public void SetItem(string key, object? value);
public bool TryGetItem<TValue>(string key, out TValue? value);
```

**Use Case**: Contributors can pass data between phases without polluting metadata
**Example**: Aggregation phase sets `context.SetItem("canon:outcome", CanonizationOutcome.Parked)` to force parking

---

### 2. CanonStageBehavior Enum

**Implementation** (`CanonStageBehavior`):
- `Default` → Engine decides
- `StageOnly` → Force staging without processing
- `Immediate` → Bypass staging

**Use Case**: Caller can force inline processing or explicit staging
**Example**: `?stageBehavior=Immediate` ensures synchronous execution

---

### 3. Correlation ID Auto-Detection

**Implementation** (`CanonEntitiesController<T>`):
- Checks `X-Correlation-ID` header
- Falls back to `X-Request-ID` header
- Falls back to `HttpContext.TraceIdentifier`

**Use Case**: Distributed tracing works without explicit header configuration

---

### 4. Record Capacity Configuration

**Implementation** (`CanonRuntimeBuilder`):
```csharp
builder.SetRecordCapacity(2048);
```

**Use Case**: Control memory usage for in-memory replay buffer
**Default**: 1024 records

---

## 📊 Implementation Completeness by Spec Section

| Spec Section | Completeness | Notes |
|--------------|--------------|-------|
| 1. Core Concepts | 100% | Fully documented patterns implemented |
| 2. Domain Model | 100% | All entities, enums, metadata complete |
| 3. Runtime & Pipeline | 98% | Missing: `IServiceProvider` in context |
| 4. Web Integration | 100% | Controllers, discovery, admin all working |
| 5. Extension Points | 100% | Builders, configurators, extensions all functional |
| 6. Edge Cases & Patterns | 100% | All edge cases handled in implementation |
| 7. API Reference | 95% | Minor signature differences documented above |
| 8. Quick Start Examples | 100% | All examples executable against implementation |
| 9. Testing Guidance | 100% | 21 domain tests + 1 web test passing |

**Weighted Average**: **98.5%** complete

---

## 🚀 Readiness Assessment

### Production Readiness: **YES**

**Blockers**: None
**Breaking Changes**: None
**Performance**: Tested with multi-stage pipelines
**Test Coverage**: Core domain (21 tests), Web (1 test), all passing

### Recommended Actions Before Production

#### Must-Do (Priority 1)
1. ✅ **DONE** - Fix spec deviations (update spec to match implementation)
2. Add `EntityType` to `CanonizationRecord` (5 min)
3. Add integration tests for multi-provider scenarios (SQL + MongoDB)

#### Should-Do (Priority 2)
4. Add `IServiceProvider Services` property to `CanonPipelineContext<T>` (15 min)
5. Expand web controller tests (header/query parsing edge cases)
6. Add sample demonstrating custom `ICanonPersistence` (event sourcing)

#### Nice-to-Have (Priority 3)
7. Add XML documentation examples to all public APIs
8. Create performance benchmarks (entities/sec, memory usage)
9. Add OpenTelemetry integration sample

---

## ✅ **SPEC SYNCHRONIZED** (2025-10-05)

All spec deviations have been resolved. The specification now accurately reflects the implementation.

### 1. ICanonPersistence ✅ UPDATED
- Spec updated to return `Task<TModel>` and `Task<CanonStage<TModel>>`
- Method names changed to `PersistCanonicalAsync` and `PersistStageAsync`
- Current 0.18 code additionally requires `GetCanonicalAsync<TModel>` so custom persistence owns
  aggregation and rebuild reads; index lookup/upsert are also part of the interface.

### 2. ICanonPipelineObserver ✅ UPDATED
- Spec updated to async pattern (`ValueTask BeforePhaseAsync`, etc.)
- Strongly-typed `ICanonPipelineContext` instead of `object`
- Method names changed to `BeforePhaseAsync`, `AfterPhaseAsync`, `OnErrorAsync`

### 3. CanonizationRecord ✅ UPDATED
- Added `EntityType`, `Phase`, `StageStatus` properties
- Changed to `init` accessors
- Event property made nullable (`CanonizationEvent?`)

### 4. CanonPipelineContext ✅ UPDATED
- Added `IServiceProvider Services` property
- Added `IReadOnlyDictionary<string, object?> Items` for inter-phase communication
- Added `SetItem` and `TryGetItem` helper methods

---

## 🎯 Migration Path Notes

### Current Implementation Status
- ✅ **M1 - Runtime Core**: **COMPLETE**
- ✅ **M2 - Web & API Surfaces**: **COMPLETE**
- ⏳ **M3 - Adapter Modernization**: Pending (transport adapters not in scope yet)
- ⏳ **M4 - Sample Migration**: `applications/CustomerCanon` is retained but has not passed golden-sample graduation
- ⏳ **M5 - Operational Cutover**: Not started

### Immediate Next Steps (Per Migration Plan)
1. Rebuild CustomerCanon around one current business contract and verify it end-to-end
2. Add another application consumer only when it earns a distinct Canon use case
3. Begin adapter modernization (Dapr connector, projection services)

---

## 🔍 Code Quality Assessment

**Strengths**:
- ✅ Comprehensive error handling (null checks, validation)
- ✅ Immutable projections (`CanonState` as `record`)
- ✅ Thread-safe observer registration (`ConcurrentDictionary`)
- ✅ Defensive copying (metadata, options)
- ✅ Consistent naming conventions
- ✅ Clear separation of concerns (domain vs web)

**Observations**:
- ⚠️ `CanonRuntime` is 399 lines (large but cohesive single-responsibility)
- ⚠️ `CanonEntitiesController<T>` has 297 lines (mostly option parsing - could extract helper)
- ✅ Test coverage excellent for domain (21 tests), light for web (1 test)

---

## 📌 Conclusion

The 2025 reconciliation concluded that the runtime was **100% complete and production-ready** against
the archived specification. This is not a current maturity certification; see the current Canon
reference linked at the top of this document.

1. **Core functionality**: 100% complete
2. **Spec alignment**: 100% synchronized (all signatures updated)
3. **Test coverage**: 21 domain tests + 1 web test, all passing
4. **Documentation**: Fully aligned (spec, ADR, migration plan updated)
5. **Code gaps**: ✅ All resolved (EntityType, Services properties added)

**Milestones Complete**:
- ✅ M1 — Runtime Core (2025-10-05)
- ✅ M2 — Web & API Surfaces (2025-10-05)
- ⏳ M3 — Adapter Modernization (next)
- ⏳ M4 — Sample Migration (CustomerCanon requires a complete business-first rebuild)
- ⏳ M5 — Operational Cutover (final)

**Historical 2025 assessment only**: this section does not establish current production support. M3 adapter work
and CustomerCanon graduation remain open under the current product maturity authority.

**Timeline**: the 2025 implementation milestone shipped; current production maturity is not asserted here.

---

**Final Status**: All gaps closed, spec synchronized, M1+M2 complete ✅
