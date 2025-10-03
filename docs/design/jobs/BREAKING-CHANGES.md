# Koan.Jobs v1.0 Breaking Changes

**Version**: v0.9 → v1.0
**Date**: 2025-10-03
**Status**: Greenfield Rebuild - No Migration Required

---

## Overview

Koan.Jobs v1.0 represents a **complete architectural refactor** to align with the "pure domain entity" principle. Since this is a greenfield implementation with no existing production databases, all changes are acceptable.

**Migration Strategy**: Not applicable - no existing deployments to migrate.

---

## Breaking Changes by Category

### 1. Job Entity Schema Changes

#### Removed Fields (BREAKING)

The following fields have been **permanently removed** from the `Job` entity:

```csharp
// ❌ REMOVED - No longer in Job entity
public string? Source { get; set; }
public string? Partition { get; set; }
public JobStorageMode StorageMode { get; set; }
public bool AuditTrailEnabled { get; set; }
public bool CancellationRequested { get; set; }
public DateTimeOffset UpdatedAt { get; set; }
```

**Rationale**: These are infrastructure concerns, not domain data. They violate the "pure domain entity" principle.

**New Location**: All removed fields now tracked in `JobIndexCache` (in-memory):

```csharp
internal sealed class JobIndexEntry
{
    public string JobId { get; }
    public JobStorageMode StorageMode { get; }
    public string? Source { get; }
    public string? Partition { get; }
    public bool AuditEnabled { get; }
    public Type JobType { get; }
    public bool CancellationRequested { get; set; }  // Ephemeral
}
```

**Impact**:
- Database schema must be updated (columns dropped)
- Code referencing `job.Source`, `job.StorageMode`, etc. will not compile
- Cancellation state is now ephemeral (lost on app restart)

#### Added Fields

```csharp
// ✅ NEW - Auto-updated change tracking
[Timestamp]
public DateTimeOffset LastModified { get; set; }
```

**Impact**:
- Database schema must add `LastModified` column
- Automatically updated on every `job.Save()` call
- Replaces manual `UpdatedAt` tracking

---

### 2. JobStatus Enum Changes

#### Removed Enum Value (BREAKING)

```csharp
// ❌ REMOVED
public enum JobStatus
{
    Succeeded = 90,  // NO LONGER EXISTS
}

// ✅ NEW
public enum JobStatus
{
    Created = 0,
    Queued = 10,
    Running = 20,
    Completed = 100,  // Single success state
    Failed = 110,
    Cancelled = 120
}
```

**Rationale**: Single success state (`Completed`) reduces confusion and aligns with industry standards.

**Impact**:
- Code checking `JobStatus.Succeeded` will not compile
- Replace with `JobStatus.Completed` everywhere

**Migration Pattern**:
```csharp
// BEFORE (v0.9)
if (job.Status is JobStatus.Succeeded or JobStatus.Completed)
    return job.Result;

// AFTER (v1.0)
if (job.Status == JobStatus.Completed)
    return job.Result;
```

---

### 3. Retry Policy Defaults

#### Changed Defaults (BREAKING)

```csharp
// BEFORE (v0.9)
public sealed class RetryPolicyAttribute : Attribute
{
    public int MaxAttempts { get; init; } = 1;
    public RetryStrategy Strategy { get; init; } = RetryStrategy.None;
    public int InitialDelaySeconds { get; init; } = 30;
}

// AFTER (v1.0)
public sealed class RetryPolicyAttribute : Attribute
{
    public int MaxAttempts { get; init; } = 3;
    public RetryStrategy Strategy { get; init; } = RetryStrategy.ExponentialBackoff;
    public int InitialDelaySeconds { get; init; } = 5;
}
```

**Rationale**: Align with proposal spec and industry best practices (fail-fast is opt-in, retry is default).

**Impact**:
- Jobs without `[RetryPolicy]` attribute now retry **3 times by default** (was 1)
- Initial delay changed from 30s to 5s
- Strategy changed from None to ExponentialBackoff

**Migration Pattern**:
```csharp
// If single-attempt behavior desired (old default)
[RetryPolicy(MaxAttempts = 1, Strategy = RetryStrategy.None)]
public class MyJob : Job<MyJob, MyContext, MyResult>
{
    // ...
}
```

---

### 4. InMemory Store TTL Changes

#### Changed Retention Defaults (BREAKING)

```csharp
// BEFORE (v0.9)
public sealed class InMemoryStoreOptions
{
    public int CompletedRetentionMinutes { get; set; } = 15;
    public int FaultedRetentionMinutes { get; set; } = 60;
}

// AFTER (v1.0)
public sealed class InMemoryStoreOptions
{
    public int CompletedRetentionMinutes { get; set; } = 60;
    public int FaultedRetentionMinutes { get; set; } = 120;
}
```

**Rationale**: Provide more time for debugging (1 hour vs 15 minutes for completed jobs).

**Impact**:
- In-memory jobs live 4x longer before automatic cleanup
- May increase memory usage slightly (acceptable trade-off)

**Migration**: No action required - new defaults are more generous.

---

### 5. Cancellation Semantics Changes

#### Ephemeral Cancellation State (BEHAVIORAL CHANGE)

**BEFORE (v0.9)**:
```csharp
public class Job : Entity<Job>
{
    public bool CancellationRequested { get; set; }  // Persisted to DB
}
```

**AFTER (v1.0)**:
```csharp
// CancellationRequested moved to in-memory index
internal sealed class JobIndexEntry
{
    public bool CancellationRequested { get; set; }  // Memory only
}
```

**Impact**:
- Cancellation state is **lost on application restart**
- Running jobs that were cancelled will resume after restart
- This is an **acceptable trade-off** for "pure domain entity" principle

**Rationale**: Cancellation is a runtime control signal, not domain data.

**Durable Signal**: Job status provides durable cancellation:
```csharp
// Durable cancellation for queued/created jobs
if (job.Status == JobStatus.Cancelled)
    return; // Won't resume

// Ephemeral cancellation for running jobs
if (indexEntry.CancellationRequested)
    return; // Lost on restart
```

---

### 6. Koan.Data.Core Requirement

#### New [Timestamp] Attribute Support (DEPENDENCY)

**Required Change**: `Koan.Data.Core` must implement auto-update for `[Timestamp]` fields.

**Implementation**:
```csharp
// Must be added to Koan.Data.Core
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
        }
    }
}

// Integrate into Entity<T>.Save()
public async Task<T> Save(CancellationToken ct = default)
{
    EntityTimestampHelper.UpdateTimestamps(this);  // Auto-update
    await Data<T, K>.Upsert((T)this, ct);
    return (T)this;
}
```

**Impact**:
- All entities with `[Timestamp]` fields will auto-update on `Save()`
- This is a **new capability** in Koan.Data.Core

---

## Non-Breaking Additions

### New Features (Safe to Adopt)

1. **ICustomRetryPolicy Interface** (Opt-in)
   - Allows custom retry logic
   - Synchronous interface
   - No impact on existing jobs

2. **Jobs.Recipe() System** (Opt-in)
   - Reusable configuration profiles
   - Static API pattern
   - No impact on existing job usage

3. **OpenTelemetry Activity Auto-Capture** (Automatic)
   - Auto-captures `Activity.Current.TraceId`
   - Explicit `correlationId` parameter overrides
   - Graceful null if no Activity

4. **Enterprise Archival** (Enabled by Default for Entity Jobs)
   - 30-day retention for completed/failed/cancelled jobs
   - Only affects entity-persisted jobs
   - InMemory jobs unaffected

5. **REST API** (New Package)
   - `Koan.Jobs.Web` package
   - `JobsController` with progress/cancel/executions endpoints
   - Opt-in via `services.AddKoanJobsWeb()`

6. **Parent-Child Navigation** (Opt-in)
   - `.GetChildren<T>()`, `.GetParent<T>()`, `.StartChild<>()`
   - No impact on existing jobs

7. **FiniteJob<> Pattern** (Opt-in)
   - Base class for item-based processing
   - Automatic progress tracking
   - No impact on existing jobs

---

## Database Migration Guide

Since this is a **greenfield implementation**, no actual migration is needed. However, if databases existed, here's how to migrate:

### PostgreSQL Migration

```sql
-- Drop infrastructure columns
ALTER TABLE "Job" DROP COLUMN IF EXISTS "Source";
ALTER TABLE "Job" DROP COLUMN IF EXISTS "Partition";
ALTER TABLE "Job" DROP COLUMN IF EXISTS "StorageMode";
ALTER TABLE "Job" DROP COLUMN IF EXISTS "AuditTrailEnabled";
ALTER TABLE "Job" DROP COLUMN IF EXISTS "CancellationRequested";
ALTER TABLE "Job" DROP COLUMN IF EXISTS "UpdatedAt";

-- Add new columns
ALTER TABLE "Job" ADD COLUMN "LastModified" TIMESTAMPTZ NOT NULL DEFAULT NOW();

-- Update JobStatus values (if Succeeded exists)
UPDATE "Job" SET "Status" = 100 WHERE "Status" = 90;  -- Succeeded → Completed
```

### MongoDB Migration

```javascript
// Drop infrastructure fields
db.Job.updateMany({}, {
  $unset: {
    Source: "",
    Partition: "",
    StorageMode: "",
    AuditTrailEnabled: "",
    CancellationRequested: "",
    UpdatedAt: ""
  }
});

// Add LastModified field
db.Job.updateMany({}, {
  $set: { LastModified: new Date() }
});

// Update JobStatus (if Succeeded exists)
db.Job.updateMany({ Status: 90 }, { $set: { Status: 100 } });
```

---

## Code Migration Checklist

### For Jobs

- [ ] Remove any code accessing `job.Source`, `job.Partition`, `job.StorageMode`
- [ ] Remove any code accessing `job.CancellationRequested` (cancellation now via coordinator)
- [ ] Replace `job.UpdatedAt` with `job.LastModified` (auto-updated)
- [ ] Replace `JobStatus.Succeeded` with `JobStatus.Completed`

### For Storage Logic

- [ ] Update stores to use `JobIndexCache` for metadata tracking
- [ ] Remove manual `UpdatedAt` assignments (now automatic)

### For Retry Logic

- [ ] Review jobs without `[RetryPolicy]` attribute (now retry 3x by default)
- [ ] Add explicit `[RetryPolicy(MaxAttempts = 1, Strategy = None)]` if single-attempt desired

### For Configuration

- [ ] Update `appsettings.json` InMemory retention if custom values needed
- [ ] Configure archival policies if defaults don't fit

---

## Timeline for Breaking Changes

**Milestone 1 (Week 1)**: All breaking changes implemented
- Entity refactor
- JobStatus.Succeeded removal
- Retry defaults
- TTL changes
- [Timestamp] support

**Milestone 2+ (Weeks 2-8)**: Only additive features
- No further breaking changes
- All additions are opt-in or automatic

---

## Support

For questions or issues with migration:
- Review `IMPLEMENTATION-PLAN.md` for detailed changes
- Check `KOAN-JOBS-PROPOSAL.md` for architectural rationale
- File issues at: https://github.com/anthropics/koan-framework/issues

---

**Breaking Changes Status**: ✅ DOCUMENTED AND APPROVED
