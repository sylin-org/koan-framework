# Koan.Jobs Architectural Decisions Record (ADR)

**Last Updated**: 2025-10-03
**Status**: Cemented - Ready for Implementation

---

## Decision Summary

This document records all architectural decisions made for Koan.Jobs v1.0 implementation. Decisions are organized by category and marked with their approval status.

---

## Core Design Decisions

### ADR-001: Job Entity Purity ✅ APPROVED

**Decision**: Jobs are pure domain entities with no infrastructure fields.

**Context**: Initial implementation included `Source`, `Partition`, `StorageMode`, `AuditTrailEnabled` fields in the Job entity, violating separation of concerns.

**Options Considered**:
- **A**: Keep infrastructure fields (status quo)
- **B**: Move to metadata dictionary (partial cleanup)
- **C**: Move to separate `JobIndexCache` (full separation) ✅ SELECTED

**Rationale**:
- Jobs represent domain work, not storage routing
- Infrastructure concerns leak into domain model
- Violates "pure domain entity" principle from proposal

**Implementation**:
- Remove: `Source`, `Partition`, `StorageMode`, `AuditTrailEnabled`, `CancellationRequested`
- Track in `JobIndexCache` (in-memory)
- Milestone 1 (Week 1)

**Consequences**:
- ✅ Clean domain model
- ✅ Framework principles upheld
- ❌ Breaking database schema change
- ❌ Cancellation state ephemeral (acceptable)

---

### ADR-002: Change Tracking via [Timestamp] ✅ APPROVED

**Decision**: Use `[Timestamp]` attribute instead of manual `UpdatedAt` field.

**Context**: Job entity had manual `UpdatedAt` field requiring explicit assignment.

**Options Considered**:
- **A**: Keep manual `UpdatedAt`
- **B**: Use `[Timestamp] LastModified` with auto-update ✅ SELECTED
- **C**: Remove timestamp tracking entirely

**Rationale**:
- Microsoft's `[Timestamp]` attribute is standard pattern
- Auto-update removes boilerplate
- Consistent with Entity Framework conventions
- Domain field (when was job last changed), not infrastructure

**Implementation**:
- Add `[Timestamp] public DateTimeOffset LastModified { get; set; }`
- Implement auto-update in `Koan.Data.Core` (new capability)
- Called automatically in `Entity<T>.Save()`

**Consequences**:
- ✅ No manual tracking needed
- ✅ Consistent across all entities
- ❌ Requires Koan.Data.Core enhancement

---

### ADR-003: Single Success State (Completed) ✅ APPROVED

**Decision**: Remove `JobStatus.Succeeded` enum value, use only `Completed`.

**Context**: Implementation had both `Succeeded` and `Completed` states with unclear distinction.

**Options Considered**:
- **A**: Keep both, document distinction
- **B**: Remove `Succeeded`, use single `Completed` state ✅ SELECTED

**Rationale**:
- Proposal spec defines single success state
- Industry standard (Hangfire, Kubernetes Jobs use single state)
- Reduces cognitive load
- No clear use case for distinction

**Implementation**:
- Remove `Succeeded = 90` from enum
- Update all usages to `Completed`
- Milestone 1 (Week 1)

**Consequences**:
- ✅ Simpler state machine
- ✅ Aligns with industry standards
- ❌ Breaking change

---

### ADR-004: Retry Policy Defaults ✅ APPROVED

**Decision**: Default to 3 attempts with 5s initial delay and exponential backoff.

**Context**: Initial implementation defaulted to single attempt with no retry.

**Options Considered**:
- **A**: Keep single-attempt default (conservative)
- **B**: Use 3 attempts, 5s delay, exponential backoff ✅ SELECTED (matches proposal)

**Rationale**:
- Aligns with proposal specification
- Industry best practice (transient failures are common)
- Fail-fast should be opt-in, not default
- More user-friendly for common scenarios

**Implementation**:
```csharp
public int MaxAttempts { get; init; } = 3;
public RetryStrategy Strategy { get; init; } = RetryStrategy.ExponentialBackoff;
public int InitialDelaySeconds { get; init; } = 5;
```

**Consequences**:
- ✅ Better defaults for most scenarios
- ✅ Matches proposal spec
- ❌ Breaking change (jobs retry more by default)
- ⚠️ Users wanting single-attempt must be explicit

---

## Extensibility Decisions

### ADR-005: ICustomRetryPolicy Synchronous ✅ APPROVED

**Decision**: `ICustomRetryPolicy` interface is synchronous only.

**Context**: Retry decisions might need async operations (e.g., service health checks).

**Options Considered**:
- **A**: Synchronous `bool ShouldRetry(JobExecution)` ✅ SELECTED
- **B**: Async `Task<bool> ShouldRetryAsync(JobExecution)`
- **C**: Both sync and async overloads

**Rationale**:
- Retry decisions should analyze error type/message (synchronous)
- Live health checks violate single responsibility (should be separate concern)
- Keeps executor simple and fast
- Prevents accidental I/O in hot path

**Implementation**:
```csharp
public interface ICustomRetryPolicy
{
    bool ShouldRetry(JobExecution lastExecution);
    TimeSpan? GetRetryDelay(JobExecution lastExecution) => null;
}
```

**Consequences**:
- ✅ Simple interface
- ✅ Fast execution
- ❌ No async health checks (acceptable limitation)

---

### ADR-006: Recipe Override Semantics (Last Wins) ✅ APPROVED

**Decision**: Per-run `.Persist()` / `.Audit()` calls fully override recipe defaults.

**Context**: When recipe defines defaults, how should per-run calls interact?

**Options Considered**:
- **A**: Last wins (full override) ✅ SELECTED
- **B**: Merge (per-run values override specific fields)
- **C**: Explicit `.OverridePersist()` method required

**Rationale**:
- Simplest mental model
- Consistent with builder pattern conventions
- Recipe provides weak defaults, fluent API provides strong overrides
- No ambiguity

**Example**:
```csharp
var recipe = Jobs.Recipe().Persist(source: "jobs", partition: "hot").Build<MyJob>();

var job = await recipe.Start(context)
    .Persist(source: "archive", partition: "cold")  // Fully overrides recipe
    .Run();
// Result: source="archive", partition="cold"
```

**Consequences**:
- ✅ Clear semantics
- ✅ No edge cases
- ⚠️ Recipe defaults are "weak" (can be surprising)

---

## Storage & Persistence Decisions

### ADR-007: Cancellation Ephemeral (Memory-Based) ✅ APPROVED

**Decision**: `CancellationRequested` flag is stored in memory only, lost on restart.

**Context**: Cancellation could be durable (DB) or ephemeral (memory).

**Options Considered**:
- **A**: Durable (persist to DB)
- **B**: Ephemeral (memory only) ✅ SELECTED
- **C**: Hybrid (Status field provides durability)

**Rationale**:
- Cancellation is runtime control signal, not domain data
- Memory-based operations are acceptable as ephemeral
- Job status provides durable signal for queued/created jobs
- Acceptable trade-off for "pure domain entity" principle

**Implementation**:
```csharp
// Ephemeral flag in JobIndexCache
public bool CancellationRequested { get; set; }

// Durable signal via Status
if (job.Status == JobStatus.Cancelled)
    return; // Won't resume on restart
```

**Consequences**:
- ✅ Clean entity model
- ✅ Consistent with ephemeral nature of in-flight operations
- ❌ Running jobs may resume after restart if cancelled (acceptable)

---

### ADR-008: InMemory TTL (60/120 minutes) ✅ APPROVED

**Decision**: InMemory jobs live 60 min (completed), 120 min (failed) before auto-cleanup.

**Context**: Original implementation used 15/60 minutes.

**Options Considered**:
- **A**: Keep 15/60 minutes (original)
- **B**: Increase to 60/120 minutes ✅ SELECTED
- **C**: Make configurable only (no defaults)

**Rationale**:
- 15 minutes too short for debugging
- 1 hour provides reasonable window for investigation
- Failed jobs kept longer (2 hours) for error analysis
- Still prevents unbounded growth

**Implementation**:
```csharp
public int CompletedRetentionMinutes { get; set; } = 60;
public int FaultedRetentionMinutes { get; set; } = 120;
```

**Consequences**:
- ✅ Better debugging experience
- ⚠️ Slightly higher memory usage (acceptable)

---

### ADR-009: Archival Enabled by Default ✅ APPROVED

**Decision**: Job archival is enabled by default for entity-persisted jobs.

**Context**: Production systems need unbounded growth prevention.

**Options Considered**:
- **A**: Opt-in (disabled by default)
- **B**: Enabled by default ✅ SELECTED

**Rationale**:
- Prevents unbounded table growth (production risk)
- 30-day retention is conservative enough for most use cases
- Only affects entity-persisted jobs (InMemory unaffected)
- Users can disable if needed

**Implementation**:
```csharp
public sealed class JobArchivalOptions
{
    public bool Enabled { get; set; } = true;  // Enabled
    // Default 30-day policies for Completed/Failed/Cancelled
}
```

**Consequences**:
- ✅ Production-safe by default
- ✅ Prevents data accumulation
- ⚠️ Jobs older than 30 days auto-archived (configurable)

---

### ADR-010: Archival Verification with Retry ✅ APPROVED

**Decision**: Always verify jobs exist in archive before deleting from source, with retry.

**Context**: Eventual consistency in distributed systems may cause verification failures.

**Options Considered**:
- **A**: No verification (trust write)
- **B**: Single verification check
- **C**: Verification with retry (3 attempts, 5s delay) ✅ SELECTED

**Rationale**:
- Prevents data loss on eventual consistency issues
- Replication lag is real (especially MongoDB, cross-region)
- Retry gives system time to converge
- Failed verification skips deletion (safe failure mode)

**Implementation**:
```csharp
private async Task<bool> VerifyArchival(batch, policy, ct, maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        var archived = await QueryArchived(batch, policy, ct);
        if (archived.Count == batch.Count)
            return true;

        if (attempt < maxRetries - 1)
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
    return false;
}
```

**Consequences**:
- ✅ Data loss prevention
- ✅ Handles eventual consistency
- ⚠️ Archival may delay if replication slow (acceptable)

---

## API & Integration Decisions

### ADR-011: OpenTelemetry Auto-Capture ✅ APPROVED

**Decision**: Auto-capture `Activity.Current.TraceId` for correlation ID.

**Context**: OpenTelemetry W3C Trace Context provides distributed tracing.

**Options Considered**:
- **A**: Manual correlation ID only
- **B**: Auto-capture from Activity.Current ✅ SELECTED
- **C**: Always use Activity (no manual override)

**Rationale**:
- W3C Trace Context is industry standard
- OpenTelemetry integration is expected
- Explicit `correlationId` parameter allows override
- Graceful null if no Activity present

**Implementation**:
```csharp
var effectiveCorrelationId = correlationId
    ?? Activity.Current?.TraceId.ToString()
    ?? Activity.Current?.Id;
```

**Consequences**:
- ✅ Seamless distributed tracing
- ✅ Manual override still available
- ✅ No breaking changes

---

### ADR-012: Jobs.Recipe() Static API ✅ APPROVED

**Decision**: Use static `Jobs.Recipe()` entry point (not DI-first).

**Context**: Recipe system could be static or DI-registered.

**Options Considered**:
- **A**: Static `Jobs.Recipe()` pattern ✅ SELECTED (like AI modules)
- **B**: DI-first `services.AddJobRecipe<>()`
- **C**: Both patterns supported

**Rationale**:
- Consistent with Koan AI module patterns
- Lightweight, no DI ceremony
- Clear intent (`Jobs.Recipe()` is obvious)
- DI registration can be added later if needed

**Implementation**:
```csharp
public static class Jobs
{
    public static JobRecipeBuilder Recipe() => new JobRecipeBuilder();
}
```

**Consequences**:
- ✅ Simple, discoverable API
- ✅ Consistent with framework patterns
- ✅ Can add DI support later if needed

---

## Deferred Decisions (Future)

### Post-v1.0 Features

**Distributed Job Execution**:
- Multi-instance coordination
- Leader election
- Deferred until production scale requirements identified

**Dashboard UI**:
- Real-time monitoring SPA
- Deferred until v1.0 API stabilizes
- Can leverage existing EntityController

**SignalR Real-Time Progress**:
- WebSocket-based progress streaming
- Deferred until demand validated

**Persistent Queues**:
- Redis/RabbitMQ adapters
- Deferred until high-throughput scenarios identified

**S13.DocMind Migration**:
- Next priority after v1.0 release
- Separate migration guide needed

---

## Decision Process

**Authority**: Enterprise Architect (Framework Creator)
**Advisory**: Senior Technical Advisor (Implementation Specialist)

**Process**:
1. Proposal and analysis (advisor provides options + pros/cons)
2. Decision (architect makes final call)
3. Canonization (decision documented, becomes framework law)
4. Implementation (advisor ensures consistency)

**Reopening Decisions**:
- Canonical decisions are not revisited unless architect explicitly reopens
- Post-v1.0 feedback may trigger reviews

---

## References

- `KOAN-JOBS-PROPOSAL.md` - Full architectural proposal
- `IMPLEMENTATION-PLAN.md` - Detailed implementation roadmap
- `BREAKING-CHANGES.md` - Breaking changes documentation

---

**Decisions Status**: ✅ ALL APPROVED - READY FOR IMPLEMENTATION
