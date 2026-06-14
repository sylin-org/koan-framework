# Koan.Jobs Design Documentation

This directory contains all design documentation for the Koan.Jobs pillar.

---

## Quick Start

**Status**: ✅ Approved - Implementation In Progress
**Version**: 3.0 (v1.0 target)
**Timeline**: 8 weeks, 6 milestones

### For Implementers

1. **Read**: `KOAN-JOBS-PROPOSAL.md` - Full architectural vision
2. **Execute**: `IMPLEMENTATION-PLAN.md` - Week-by-week roadmap
3. **Reference**: `ARCHITECTURAL-DECISIONS.md` - Rationale for all decisions
4. **Track**: `BREAKING-CHANGES.md` - All breaking changes documented

### For Reviewers

- `ARCHITECTURAL-DECISIONS.md` - Understand why decisions were made
- `KOAN-JOBS-PROPOSAL.md` (Section: Architectural Decisions) - High-level summary

---

## Document Index

### Primary Documents

| Document | Purpose | Audience |
|----------|---------|----------|
| [`KOAN-JOBS-PROPOSAL.md`](../KOAN-JOBS-PROPOSAL.md) | Complete architectural specification | All stakeholders |
| [`IMPLEMENTATION-PLAN.md`](./IMPLEMENTATION-PLAN.md) | Milestone-by-milestone execution plan | Implementation team |
| [`ARCHITECTURAL-DECISIONS.md`](./ARCHITECTURAL-DECISIONS.md) | ADR for all key decisions | Architects, reviewers |
| [`BREAKING-CHANGES.md`](./BREAKING-CHANGES.md) | All v1.0 breaking changes | Migration teams |

---

## Key Decisions Summary

### Core Principles ✅ APPROVED

1. **Pure Domain Entities**: Jobs contain only domain data, no infrastructure fields
2. **Entity-First Design**: Jobs inherit `Entity<Job>` with GUID v7 auto-generation
3. **Provider Transparency**: Same job code works across SQL, NoSQL, Vector, JSON stores
4. **Separation of Concerns**: Job (domain) vs RetryPolicy (behavior) vs JobExecution (audit)

### Breaking Changes ⚠️ GREENFIELD

- Job entity refactored (infrastructure fields removed)
- `JobStatus.Succeeded` removed (single `Completed` state)
- Retry defaults changed (3 attempts, 5s initial delay)
- `[Timestamp]` attribute support required in Koan.Data.Core

### New Capabilities ✅ ADDITIVE

- `ICustomRetryPolicy` for domain-specific retry logic
- `Jobs.Recipe()` for reusable configuration profiles
- OpenTelemetry Activity auto-capture
- Enterprise archival (enabled by default, 30-day retention)
- REST API via `Koan.Jobs.Web` package
- Parent-child job workflows
- `FiniteJob<>` pattern for item-based processing

---

## Implementation Timeline

| Week | Milestone | Focus |
|------|-----------|-------|
| 1 | Entity Refactor | Clean domain model, [Timestamp] support |
| 2 | Critical Interfaces | ICustomRetryPolicy, Activity integration |
| 3 | Recipe System | Jobs.Recipe() reusable profiles |
| 4-5 | Archival | Production-safe archival with defaults |
| 6 | REST API | JobsController HTTP endpoints |
| 7-8 | Advanced Features | Parent-child, FiniteJob |

**Target**: v1.0 GA after 8 weeks

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                  Koan.Jobs.Core                     │
├─────────────────────────────────────────────────────┤
│  Domain Layer                                       │
│  ├─ Job : Entity<Job> (Pure domain entity)         │
│  ├─ JobExecution : Entity<JobExecution> (Audit)    │
│  └─ Job<TJob, TContext, TResult> (Generic base)    │
├─────────────────────────────────────────────────────┤
│  Execution Layer                                    │
│  ├─ JobExecutor (Retry logic, ICustomRetryPolicy)  │
│  ├─ JobWorkerService (Background processor)        │
│  ├─ JobCoordinator (Orchestration)                 │
│  └─ JobProgressBroker (Real-time updates)          │
├─────────────────────────────────────────────────────┤
│  Storage Layer                                      │
│  ├─ InMemoryJobStore (60/120 min TTL)              │
│  ├─ EntityJobStore (Multi-provider)                │
│  └─ JobIndexCache (Metadata tracking)              │
├─────────────────────────────────────────────────────┤
│  Configuration Layer                                │
│  ├─ Jobs.Recipe() (Reusable profiles)              │
│  ├─ JobRunBuilder (Fluent API)                     │
│  └─ JobArchivalService (30-day retention)          │
└─────────────────────────────────────────────────────┘
```

---

## Usage Patterns

### Basic Job

```csharp
public class EmailJob : Job<EmailJob, EmailContext, EmailResult>
{
    protected override async Task<EmailResult> Execute(
        EmailContext context,
        IJobProgress progress,
        CancellationToken ct)
    {
        progress.Report(0.5, "Sending email...");
        await SendEmail(context.To, context.Body);
        progress.Report(1.0, "Email sent");
        return new EmailResult { Sent = true };
    }
}

// Usage
var job = await EmailJob.Start(new EmailContext { To = "user@example.com" }).Run();
await job.Wait();
```

### With Retry Policy

```csharp
[RetryPolicy(MaxAttempts = 5, Strategy = RetryStrategy.ExponentialBackoff)]
public class BackupJob : Job<BackupJob, BackupContext, BackupResult>
{
    // Retries automatically on failure
}
```

### Custom Retry Logic

```csharp
[RetryPolicy(MaxAttempts = 3)]
public class ApiCallJob : Job<ApiCallJob, ApiContext, ApiResult>, ICustomRetryPolicy
{
    public bool ShouldRetry(JobExecution lastExecution)
    {
        // Don't retry 4xx errors, do retry 5xx
        return lastExecution.ErrorMessage?.Contains("500") == true;
    }
}
```

### Reusable Configuration (Recipe)

```csharp
// Define once
private static readonly JobRecipe<BackupJob, BackupContext, BackupResult> DurableBackups
    = Jobs.Recipe()
        .Persist(source: "jobs", partition: "hot")
        .Audit()
        .WithDefaults(metadata: meta => meta["Module"] = "Backups")
        .Build<BackupJob>();

// Use everywhere
var job = await DurableBackups.Start(context).Run();
```

### Progress Tracking

```csharp
var job = await LongRunningJob.Start(context).Run();

using var subscription = job.OnProgress(async update =>
{
    Console.WriteLine($"Progress: {update.Percentage:P} - {update.Message}");
    Console.WriteLine($"ETA: {update.EstimatedCompletion}");
});

await job.Wait();
```

### Parent-Child Workflows

```csharp
public class ParentJob : Job<ParentJob, ParentContext, ParentResult>
{
    protected override async Task<ParentResult> Execute(...)
    {
        var child1 = await this.StartChild<ChildJob, ChildContext, ChildResult>(context1).Run();
        var child2 = await this.StartChild<ChildJob, ChildContext, ChildResult>(context2).Run();

        await Task.WhenAll(child1.Wait(), child2.Wait());

        var children = await this.GetChildren<ChildJob>();
        return new ParentResult { CompletedChildren = children.Count };
    }
}
```

---

## Testing Strategy

### Unit Tests

- Job entity model validation
- Retry policy logic
- Custom retry policies (4xx vs 5xx)
- Recipe builder and overrides
- Archival policy execution

### Integration Tests

- End-to-end job execution
- Progress tracking across storage modes
- Archival with real entity stores (PostgreSQL, MongoDB)
- HTTP API contracts
- Multi-provider scenarios

### Test Coverage Target

- 80%+ line coverage on new code
- All happy paths covered
- All error paths covered
- Edge cases (cancellation, timeouts, retries)

---

## Future Roadmap

### Post-v1.0 Features

**Distributed Job Execution**:
- Multi-instance coordination
- Leader election for job assignment
- Horizontal scaling support

**Dashboard UI**:
- Real-time job monitoring SPA
- Job history visualization
- Manual job triggering

**S13.DocMind Migration**:
- Migration guide from `DocumentProcessingJob`
- Backward compatibility layer
- Progressive rollout strategy

**SignalR Real-Time Progress**:
- WebSocket-based progress streaming
- Client-side progress updates

**Persistent Queues**:
- Redis queue adapter
- RabbitMQ queue adapter

---

## Contributing

### Review Process

1. Implementation follows `IMPLEMENTATION-PLAN.md` milestones
2. All breaking changes documented in `BREAKING-CHANGES.md`
3. Architectural decisions recorded in `ARCHITECTURAL-DECISIONS.md`
4. Tests written per milestone acceptance criteria
5. XML docs on all public APIs
6. Code review by architect

### Definition of Done

- [ ] Implementation complete
- [ ] Unit + integration tests pass
- [ ] XML docs written
- [ ] Breaking changes documented (if any)
- [ ] Code reviewed and approved
- [ ] Milestone checklist completed

---

## References

### External Standards

- [OpenTelemetry](https://opentelemetry.io/) - Distributed tracing
- [W3C Trace Context](https://www.w3.org/TR/trace-context/) - Correlation standard
- [Hangfire](https://www.hangfire.io/) - Industry job library (comparison)
- [Quartz.NET](https://www.quartz-scheduler.net/) - Industry scheduler (comparison)

### Internal Koan Docs

- Koan Framework Principles
- Entity-First Development Guide
- Multi-Provider Data Architecture
- Auto-Registration Patterns

---

**Documentation Status**: ✅ COMPLETE AND APPROVED

For questions or clarifications, file an issue or contact the architecture team.
