# Koan.Jobs v1.0 Implementation Plan

**Status**: ✅ APPROVED FOR EXECUTION
**Timeline**: 8 weeks, 6 milestones
**Approach**: Break-and-rebuild greenfield (no backward compatibility required)
**Target Release**: v1.0.0

---

## Executive Summary

This plan covers the complete implementation of Koan.Jobs from current state to v1.0 GA. The execution order front-loads all breaking changes (Milestone 1), then builds features on the clean foundation.

**Critical Decisions**:
- ✅ Greenfield rebuild acceptable - no migration needed
- ✅ Job entity becomes pure domain model (infrastructure in `JobIndexCache`)
- ✅ Archival enabled by default to prevent unbounded growth
- ✅ Recipe system for reusable configuration profiles
- ✅ REST API via `Koan.Jobs.Web` package

---

## Milestone 1: Entity Refactor & Core Cleanup (Week 1)

**Goal**: Clean domain model, remove infrastructure leakage, fix enums

**Priority**: P0 - Foundation for all other work

### 1.1 Remove Infrastructure Fields from Job Entity

**Effort**: 6 hours

**Removed Fields**:
- ❌ `Source` → Move to `JobIndexCache`
- ❌ `Partition` → Move to `JobIndexCache`
- ❌ `StorageMode` → Move to `JobIndexCache`
- ❌ `AuditTrailEnabled` → Move to `JobIndexCache`
- ❌ `CancellationRequested` → Move to `JobIndexCache` (ephemeral)
- ❌ `UpdatedAt` → Replace with `[Timestamp] LastModified`

**New Fields**:
- ✅ `[Timestamp] public DateTimeOffset LastModified { get; set; }`

**Files Modified**:
- `src/Koan.Jobs.Core/Model/Job.cs`
- `src/Koan.Jobs.Core/Support/JobIndexCache.cs`
- `src/Koan.Jobs.Core/Store/InMemoryJobStore.cs`
- `src/Koan.Jobs.Core/Store/EntityJobStore.cs`
- `src/Koan.Jobs.Core/Execution/JobCoordinator.cs`
- `src/Koan.Jobs.Core/Execution/JobExecutor.cs`

### 1.2 Implement [Timestamp] Auto-Update

**Effort**: 3 hours

**Location**: `src/Koan.Data.Core/EntityExtensions.cs` (or appropriate location)

**Implementation**:
```csharp
internal static class EntityTimestampHelper
{
    internal static void UpdateTimestamps<T>(T entity) where T : class
    {
        var properties = typeof(T).GetProperties()
            .Where(p => p.GetCustomAttribute<TimestampAttribute>() != null);

        foreach (var prop in properties)
        {
            if (prop.PropertyType == typeof(DateTimeOffset))
                prop.SetValue(entity, DateTimeOffset.UtcNow);
            // Handle DateTimeOffset?, DateTime, DateTime?
        }
    }
}

// Integrate into Entity<T>.Save()
public async Task<T> Save(CancellationToken ct = default)
{
    EntityTimestampHelper.UpdateTimestamps(this);
    await Data<T, K>.Upsert((T)this, ct);
    return (T)this;
}
```

### 1.3 Enhance JobIndexCache

**Effort**: 2 hours

**Enhanced Structure**:
```csharp
internal sealed class JobIndexEntry
{
    public string JobId { get; }
    public JobStorageMode StorageMode { get; }
    public string? Source { get; }
    public string? Partition { get; }
    public bool AuditEnabled { get; }
    public Type JobType { get; }

    // Ephemeral cancellation control
    public bool CancellationRequested { get; set; }
}
```

### 1.4 Remove JobStatus.Succeeded

**Effort**: 1 hour

**Changes**:
- Remove `Succeeded = 90` from enum
- Update all usages to single `Completed` state
- Update `Job.Generic.cs:50` wait logic
- Update `JobExecutor.cs:114` completion logic

### 1.5 Update InMemory TTL

**Effort**: 30 minutes

**Changes**:
```csharp
public sealed class InMemoryStoreOptions
{
    public int CompletedRetentionMinutes { get; set; } = 60;  // Changed from 15
    public int FaultedRetentionMinutes { get; set; } = 120;   // Changed from 60
    public int SweepIntervalSeconds { get; set; } = 60;
}
```

### Acceptance Criteria

- [ ] Job entity contains only domain fields (matches proposal)
- [ ] `JobIndexCache` tracks Source, Partition, StorageMode, AuditEnabled
- [ ] Cancellation uses ephemeral flag + durable status
- [ ] `JobStatus.Succeeded` removed from codebase
- [ ] `[Timestamp]` auto-updates on every `Save()`
- [ ] InMemory TTL = 60 min (completed), 120 min (failed)
- [ ] All tests pass with refactored model
- [ ] XML docs updated

---

## Milestone 2: Critical Interfaces & Fixes (Week 2)

**Goal**: Core extensibility and standards compliance

**Priority**: P0 - Blocking for custom retry scenarios

### 2.1 Implement ICustomRetryPolicy Interface

**Effort**: 4 hours

**Files**:
- `src/Koan.Jobs.Core/Execution/ICustomRetryPolicy.cs` (new)
- `src/Koan.Jobs.Core/Execution/JobExecutor.cs` (modify)
- `tests/Koan.Jobs.Core.Tests/CustomRetryPolicyTests.cs` (new)

**Interface**:
```csharp
public interface ICustomRetryPolicy
{
    bool ShouldRetry(JobExecution lastExecution);
    TimeSpan? GetRetryDelay(JobExecution lastExecution) => null;
}
```

**Integration Points**:
- `JobExecutor.ShouldRetryJob()` - New helper method
- `JobExecutor.CalculateRetryDelay()` - New helper method
- Call before retry attempts

**Test Scenarios**:
- HTTP 4xx errors skip retry
- HTTP 5xx errors retry
- Custom delay calculation
- Backward compatibility (jobs without interface)

### 2.2 Fix Retry Policy Defaults

**Effort**: 30 minutes

**Changes**:
```csharp
public sealed class RetryPolicyAttribute : Attribute
{
    public int MaxAttempts { get; init; } = 3;  // Changed from 1
    public RetryStrategy Strategy { get; init; } = RetryStrategy.ExponentialBackoff;  // Changed from None
    public int InitialDelaySeconds { get; init; } = 5;  // Changed from 30
    // ...
}
```

### 2.3 OpenTelemetry Activity Auto-Capture

**Effort**: 2 hours

**File**: `src/Koan.Jobs.Core/Support/JobEnvironment.cs`

**Implementation**:
```csharp
public static JobRunBuilder<TJob, TContext, TResult> CreateBuilder<TJob, TContext, TResult>(
    TContext context,
    string? correlationId,
    CancellationToken cancellationToken)
{
    var effectiveCorrelationId = correlationId
        ?? Activity.Current?.TraceId.ToString()
        ?? Activity.Current?.Id;
    // ...
}
```

**Tests**:
- Activity present → auto-capture TraceId
- Explicit correlationId → override Activity
- No Activity → null correlationId

### Acceptance Criteria

- [ ] `ICustomRetryPolicy` interface compiled with XML docs
- [ ] `JobExecutor` calls `ShouldRetry()` before retry
- [ ] Tests prove HTTP status code scenarios
- [ ] Retry defaults: MaxAttempts=3, InitialDelay=5s
- [ ] Activity.Current.TraceId auto-captured
- [ ] All tests pass

---

## Milestone 3: Jobs.Recipe() System (Week 3)

**Goal**: Reusable job configuration profiles

**Priority**: P1 - DRY principle for job configuration

### 3.1 Implement Recipe Builder

**Effort**: 12 hours

**New Files**:
- `src/Koan.Jobs.Core/Recipes/JobRecipeBuilder.cs`
- `src/Koan.Jobs.Core/Recipes/JobRecipe.cs`
- `src/Koan.Jobs.Core/Jobs.cs` (static entry point)
- `tests/Koan.Jobs.Core.Tests/RecipeTests.cs`

**API Surface**:
```csharp
// Static entry point
var recipe = Jobs.Recipe()
    .Persist(source: "jobs", partition: "hot")
    .Audit()
    .WithDefaults(metadata: meta => meta["Module"] = "Backups")
    .Build<MyJob>();

// Usage
var job = await recipe.Start(context)
    .With(userId: userId)  // Per-run addition
    .Persist(partition: "cold")  // Override recipe
    .Run();
```

**Override Semantics**: Last `.Persist()` / `.Audit()` wins (full override)

### Acceptance Criteria

- [ ] `Jobs.Recipe()` entry point exists
- [ ] Recipe builder supports all fluent methods
- [ ] Recipe `.Start()` applies defaults
- [ ] Per-run overrides work (last wins)
- [ ] Tests prove reusability
- [ ] XML docs on all public APIs

---

## Milestone 4: Enterprise Archival System (Weeks 4-5)

**Goal**: Prevent unbounded job table growth

**Priority**: P0 - Production blocker

### 4.1 Archival Policy Model

**Effort**: 6 hours

**New Files**:
- `src/Koan.Jobs.Core/Archival/JobArchivalPolicy.cs`
- `src/Koan.Jobs.Core/Archival/JobArchivalOptions.cs`
- `src/Koan.Jobs.Core/Archival/DefaultArchivalPolicies.cs`

**Configuration**:
```csharp
public sealed class JobArchivalOptions
{
    public bool Enabled { get; set; } = true;  // Enabled by default
    public int CheckIntervalMinutes { get; set; } = 60;
    public bool UseDefaults { get; set; } = true;
    public List<JobArchivalPolicy> Policies { get; set; } = new();
}
```

**Default Policies**: 30-day retention for Completed/Failed/Cancelled

### 4.2 Archival Background Service

**Effort**: 10 hours

**Files**:
- `src/Koan.Jobs.Core/Archival/JobArchivalService.cs`
- `src/Koan.Jobs.Core/ServiceCollectionExtensions.cs` (registration)
- `tests/Koan.Jobs.Core.Tests/ArchivalServiceTests.cs`

**Key Features**:
- Query only entity-persisted jobs (skip InMemory)
- Batch archival (default 500 per batch)
- Verification with retry (3 attempts, 5s delay)
- Delete from source only after verification
- Logging for all operations

### 4.3 Archival Metrics (Optional)

**Effort**: 3 hours

**File**: `src/Koan.Jobs.Core/Archival/JobArchivalMetrics.cs`

**Metrics**:
- `jobs.archived` (counter)
- `jobs.archival.duration` (histogram)
- Tagged by policy name

### Acceptance Criteria

- [ ] JobArchivalService runs every 60 minutes
- [ ] Default policies apply (30-day retention)
- [ ] Jobs move to "archive" partition
- [ ] Verification prevents data loss
- [ ] Only entity-persisted jobs archived
- [ ] Tests prove archival logic
- [ ] Metrics published (if implemented)

---

## Milestone 5: REST API & Web Integration (Week 6)

**Goal**: Out-of-box HTTP API

**Priority**: P1 - Standard integration

### 5.1 Implement JobsController

**Effort**: 8 hours

**New Project**: `src/Koan.Jobs.Web/`

**Files**:
- `src/Koan.Jobs.Web/Controllers/JobsController.cs`
- `src/Koan.Jobs.Web/ServiceCollectionExtensions.cs`
- `src/Koan.Jobs.Web/Koan.Jobs.Web.csproj`
- `tests/Koan.Jobs.Web.Tests/JobsControllerTests.cs`

**Endpoints**:
- `GET /api/jobs` (inherited from EntityController)
- `GET /api/jobs/{id}` (inherited)
- `GET /api/jobs/{id}/progress`
- `POST /api/jobs/{id}/cancel`
- `GET /api/jobs/{id}/executions`

**Features**:
- OpenAPI/Swagger documentation
- Proper HTTP status codes
- Error handling
- Integration tests

### Acceptance Criteria

- [ ] JobsController inherits EntityController<Job>
- [ ] All custom endpoints implemented
- [ ] OpenAPI docs generated
- [ ] Integration tests pass
- [ ] Auto-registration works

---

## Milestone 6: Advanced Features (Weeks 7-8)

**Goal**: Parent-child navigation, FiniteJob pattern

**Priority**: P2 - Workflow enabler

### 6.1 Parent-Child Job Navigation

**Effort**: 6 hours

**File**: `src/Koan.Jobs.Core/Extensions/JobRelationshipExtensions.cs`

**Methods**:
```csharp
public static Task<IReadOnlyList<TChild>> GetChildren<TChild>(this Job parent, CancellationToken ct);
public static Task<TParent?> GetParent<TParent>(this Job child, CancellationToken ct);
public static JobRunBuilder<TChild, TChildContext, TChildResult> StartChild<...>(this Job parent, ...);
```

**Usage**:
```csharp
// Spawn child
var child = await parent.StartChild<ChildJob, ChildContext, ChildResult>(context).Run();

// Query children
var children = await parent.GetChildren<ChildJob>();

// Navigate to parent
var parent = await child.GetParent<ParentJob>();
```

### 6.2 FiniteJob Pattern

**Effort**: 8 hours

**File**: `src/Koan.Jobs.Core/Model/FiniteJob.cs`

**Base Class**:
```csharp
public abstract class FiniteJob<TJob, TContext, TResult, TItem> : Job<TJob, TContext, TResult>
{
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }
    public int SkippedItems { get; set; }

    protected abstract IAsyncEnumerable<TItem> GetItems(TContext context, CancellationToken ct);
    protected abstract Task<ItemResult> ProcessItem(TItem item, int index, IJobProgress progress, CancellationToken ct);
    protected abstract TResult BuildResult();
}
```

**Automatic Features**:
- Per-item progress reporting
- Item count tracking
- Failure/skip counting

### Acceptance Criteria

- [ ] Parent-child navigation works
- [ ] `.StartChild()` sets ParentJobId
- [ ] FiniteJob auto-tracks items
- [ ] Tests prove workflow patterns
- [ ] XML docs complete

---

## Definition of Done (All Milestones)

**Per-Milestone Checklist**:
- [ ] Implementation complete
- [ ] Unit tests written (happy + error paths)
- [ ] Integration tests written (where applicable)
- [ ] XML docs on all public APIs
- [ ] All tests passing locally
- [ ] Code review approved
- [ ] Breaking changes noted (if any)

**Repository Updates**:
- [ ] CHANGELOG.md updated with changes
- [ ] Examples added to tests
- [ ] Proposal updated (for future features)

---

## Testing Strategy

### Unit Tests (Per Milestone)

**Milestone 1**:
- Entity refactor tests (new model validation)
- Index cache tests (metadata tracking)
- Timestamp auto-update tests

**Milestone 2**:
- Custom retry policy tests (4xx vs 5xx)
- Activity correlation tests
- Retry default tests

**Milestone 3**:
- Recipe builder tests (defaults, overrides)
- Multiple jobs from same recipe

**Milestone 4**:
- Archival policy tests
- Verification retry tests
- Eventual consistency handling

**Milestone 5**:
- Controller endpoint tests
- OpenAPI schema validation

**Milestone 6**:
- Parent-child workflow tests
- FiniteJob item processing tests

### Integration Tests

- End-to-end job execution
- Progress tracking across storage modes
- Archival with real entity store
- HTTP API contracts
- Multi-provider scenarios (PostgreSQL, MongoDB)

---

## Timeline Summary

| Milestone | Duration | Deliverable |
|-----------|----------|-------------|
| **1: Entity Refactor** | Week 1 | Clean domain model, [Timestamp] support |
| **2: Critical Interfaces** | Week 2 | ICustomRetryPolicy, Activity integration |
| **3: Recipe System** | Week 3 | Jobs.Recipe() reusable profiles |
| **4: Archival** | Weeks 4-5 | Production-safe archival with defaults |
| **5: REST API** | Week 6 | JobsController HTTP endpoints |
| **6: Advanced** | Weeks 7-8 | Parent-child, FiniteJob |

**Total**: 8 weeks to v1.0 GA

---

## Success Metrics

- ✅ Job entity contains only domain fields
- ✅ All infrastructure tracked in JobIndexCache
- ✅ 80%+ test coverage on new code
- ✅ Zero unbounded growth (archival enabled)
- ✅ OpenTelemetry integration working
- ✅ Recipe pattern reduces boilerplate
- ✅ REST API fully functional
- ✅ Breaking changes documented

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| [Timestamp] breaks existing Entity<T> | Implement in Koan.Data.Core with tests |
| Archival deletes jobs prematurely | Verification step before deletion, logging |
| Recipe pattern adds complexity | Comprehensive docs, examples, optional adoption |
| Performance regression | Batch operations, configurable intervals, metrics |

---

**Plan Status**: ✅ **CEMENTED - EXECUTION APPROVED**

See also:
- `BREAKING-CHANGES.md` - All breaking changes
- `KOAN-JOBS-PROPOSAL.md` - Full architectural proposal
