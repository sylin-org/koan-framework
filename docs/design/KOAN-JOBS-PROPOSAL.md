# Koan Jobs: Holistic Long-Running Task Management

**Status**: Proposal
**Date**: 2025-10-02
**Author**: Architecture Team
**Version**: 2.3

---

## Executive Summary

This proposal defines `Koan.Jobs` - a comprehensive, entity-first long-running task management pillar for Koan Framework. Jobs represent trackable, auditable work with progress reporting, correlation support, and provider-transparent storage. The design synthesizes proven patterns from .NET standards, industry libraries (Hangfire/Quartz), and existing Koan implementations (S13.DocMind) into a canonical framework feature.

**Key Tenets:**
- **Entity-First Design**: Jobs are `Entity<T>` with GUID v7 IDs and provider transparency
- **Reference = Intent**: Adding `Koan.Jobs` package auto-enables job infrastructure
- **Semantic Ergonomics**: `await MyJob.Start(context).Run()`, `await job.Wait()`, `job.OnProgress(handler)`
- **Correlation Built-In**: OpenTelemetry Activity integration and extensible metadata support
- **Observable by Default**: Progress tracking, ETA estimation, separate execution history for audit
- **Clean Separation of Concerns**: Jobs define work; policies define behavior; executions record history
- **Adaptive Storage Profiles**: Jobs run in-memory by default; `.Persist(...)` opt-in routes work through EntityContext sources and `.Audit()` keeps execution history when needed
- **Reusable Recipes**: `Jobs.Recipe()` captures persistence, policy, and metadata defaults for consistent reuse across entry points

---

## Architectural Principles

### Separation of Concerns

**Jobs are pure domain entities** describing work to be done:
- ✅ What work needs to be performed (context/payload)
- ✅ Current lifecycle state (created/running/completed)
- ✅ Progress and timing information
- ✅ Result or error outcome

**Policies are declarative behaviors** applied at the class level:
- ✅ Retry strategies (exponential backoff, linear, fixed)
- ✅ Maximum attempt limits
- ✅ Custom retry decision logic

**Executions are audit records** tracking individual attempts:
- ✅ Complete history of every execution attempt
- ✅ Performance metrics per attempt
- ✅ Error details and stack traces
- ✅ Enables analysis: "Jobs of this type usually fail on attempt 2"

This separation ensures:
- Jobs remain lightweight domain entities
- Policies are reusable across job types
- Execution history provides complete auditability
- No forced complexity for simple fire-and-forget jobs

---

## Current State Analysis

### S13.DocMind Pattern (Existing Implementation)

DocMind demonstrates job tracking with `DocumentProcessingJob` entity:

```csharp
public sealed class DocumentProcessingJob : Entity<DocumentProcessingJob>
{
    public Guid SourceDocumentId { get; set; }
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public string CorrelationId { get; set; }
    public int Attempt { get; set; }              // ⚠️ Execution metadata in domain entity
    public int MaxAttempts { get; set; }           // ⚠️ Policy config in domain entity

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastError { get; set; }
    public Dictionary<string, DocumentProcessingStageState> StageTelemetry { get; set; }
}
```

**Strengths:**
- ✅ Entity-first design with auto GUID v7
- ✅ Correlation ID support
- ✅ Stage-aware processing with per-stage telemetry
- ✅ Timing metadata (created/started/completed)
- ✅ Separate event stream (`DocumentProcessingEvent`) for observability

**Limitations:**
- ❌ Retry concerns mixed into domain entity (attempt count, max attempts)
- ❌ Application-specific (document processing)
- ❌ No generic abstraction for reusability
- ❌ No progress percentage/ETA calculation
- ❌ No real-time progress streaming API

**Opportunity**: Generalize this proven pattern with cleaner separation of concerns.

---

## Standards & Industry Alignment

### .NET Standards Leveraged

1. **`IProgress<T>`**: Standard progress reporting interface
   - Thread-safe, asynchronous progress posts
   - `Progress<T>` captures `SynchronizationContext` for UI scenarios

2. **`Activity` (System.Diagnostics)**: Distributed tracing primitive
   - Auto-correlation via `Activity.Current.TraceId`
   - W3C Trace Context standard (OpenTelemetry)
   - Parent/child span relationships

3. **`BackgroundService` / `IHostedService`**: Long-running service hosting
   - Koan has existing abstraction (`Koan.Core.BackgroundServices`)
   - Auto-registration via `[KoanBackgroundService]` attribute

4. **`CancellationToken`**: Cooperative cancellation standard

### Industry Library Patterns

| Feature | Hangfire | Quartz.NET | Koan.Jobs (Proposed) |
|---------|----------|------------|----------------------|
| **Job Storage** | Database-backed | In-memory or persistent | Entity-first (multi-provider) |
| **State Machine** | Enqueued→Processing→Succeeded/Failed | Queued→Executing→Complete | Created→Queued→Running→Completed/Failed/Cancelled |
| **Dashboard** | Built-in web UI | Separate tooling | Leverage Koan.Web entity endpoints + SignalR |
| **Retry Logic** | Automatic with exponential backoff | Manual implementation | Declarative via `[RetryPolicy]` attribute |
| **Execution History** | Limited (single error) | Not built-in | Full audit trail via `JobExecution` entity |
| **Progress Tracking** | Limited (job parameters) | Not built-in | First-class with `IJobProgress` and streaming |
| **Correlation** | Manual | Manual | Automatic via OpenTelemetry Activity |
| **DX Model** | Attribute-based job methods | Programmatic job classes | Entity-first + fluent API |

**Key Differentiation**: Koan.Jobs maintains clean separation between domain (Job), policy (RetryPolicy), and audit (JobExecution) while providing first-class progress tracking and correlation.

---

## Proposed Architecture

### Core Entity Model

```csharp
namespace Koan.Jobs.Core.Model;

/// <summary>
/// Base job entity representing work to be performed.
/// Pure domain entity with no execution infrastructure concerns.
/// Inherits Entity&lt;T&gt; for GUID v7 auto-generation and provider transparency.
/// </summary>
public abstract class Job : Entity<Job>
{
    // ===== IDENTITY & CORRELATION =====
    // Id inherited from Entity<Job> (auto GUID v7)

    [Indexed]
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [Indexed]
    [MaxLength(64)]
    public string? ParentJobId { get; set; }

    // ===== LIFECYCLE STATE =====
    [Required]
    public JobStatus Status { get; set; } = JobStatus.Created;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    // ===== TIMING =====
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }

    // ===== PROGRESS TRACKING =====
    [Range(0.0, 1.0)]
    public double Progress { get; set; } = 0.0;

    [MaxLength(500)]
    public string? ProgressMessage { get; set; }

    public int? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }

    public DateTimeOffset? EstimatedCompletion { get; set; }

    // ===== RESULT / ERROR =====
    [Column(TypeName = "jsonb")]
    public string? ResultJson { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }  // Most recent error only

    // ===== PAYLOAD =====
    [Column(TypeName = "jsonb")]
    public string? ContextJson { get; set; }

    // ===== EXTENSIBLE METADATA =====
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

public enum JobStatus
{
    Created = 0,
    Queued = 10,
    Running = 20,
    Completed = 100,
    Failed = 110,
    Cancelled = 120
}
```

**Key Design Decision**: No `AttemptCount`, `MaxAttempts`, or `NextRetryAt` fields. These are execution infrastructure concerns, not job domain data.

### Job Execution History Entity

```csharp
/// <summary>
/// Audit trail of individual job execution attempts.
/// Separate entity for clean separation of concerns.
/// Enables complete execution history analysis.
/// </summary>
public class JobExecution : Entity<JobExecution>
{
    [Required]
    [Parent(typeof(Job))]
    [Indexed]
    public string JobId { get; set; } = string.Empty;

    [Required]
    public int AttemptNumber { get; set; }

    [Required]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public TimeSpan? Duration { get; set; }

    [Required]
    public JobExecutionStatus Status { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    [MaxLength(4000)]
    public string? StackTrace { get; set; }

    // Execution-specific metadata (performance metrics, resource usage, etc.)
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metrics { get; set; } = new();
}

public enum JobExecutionStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}

// Query execution history
var executions = await JobExecution.Query(
    e => e.JobId == job.Id,
    new DataQueryOptions { OrderBy = "StartedAt DESC" }
);

var attemptCount = executions.Count;
var lastAttempt = executions.FirstOrDefault();
```

**Benefits**:
- ✅ Complete audit trail (every execution recorded)
- ✅ Detailed error history per attempt
- ✅ Performance metrics per attempt
- ✅ Enables analysis: "This job type fails on attempt 2 usually"
- ✅ Jobs remain lightweight

### Retry Policy Configuration

```csharp
/// <summary>
/// Defines retry behavior for job types.
/// Applied at class level, not instance level.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RetryPolicyAttribute : Attribute
{
    public int MaxAttempts { get; set; } = 3;
    public RetryStrategy Strategy { get; set; } = RetryStrategy.ExponentialBackoff;
    public double InitialDelaySeconds { get; set; } = 5;
    public double BackoffMultiplier { get; set; } = 2.0;
    public double MaxDelaySeconds { get; set; } = 300; // 5 minutes
}

public enum RetryStrategy
{
    None,              // No retry (default for jobs without attribute)
    Immediate,         // Retry immediately
    FixedDelay,        // Wait same time between retries
    LinearBackoff,     // Delay increases linearly (5s, 10s, 15s)
    ExponentialBackoff // Delay increases exponentially (5s, 10s, 20s, 40s)
}

/// <summary>
/// Optional interface for jobs needing custom retry logic.
/// Allows inspecting error type, context, etc. to decide if retry should occur.
/// </summary>
public interface ICustomRetryPolicy
{
    /// <summary>
    /// Decide if this specific execution should be retried.
    /// </summary>
    bool ShouldRetry(JobExecution lastExecution);

    /// <summary>
    /// Calculate custom retry delay. Return null to use policy default.
    /// </summary>
    TimeSpan? GetRetryDelay(JobExecution lastExecution) => null;
}

// Usage examples
[RetryPolicy(MaxAttempts = 5, Strategy = RetryStrategy.ExponentialBackoff)]
public class BackupJob : Job<BackupJob, BackupContext, BackupResult>
{
    // Job definition - no retry concerns
}

// No retry for one-time operations
public class DatabaseMigrationJob : Job<DatabaseMigrationJob, MigrationContext, MigrationResult>
{
    // No [RetryPolicy] attribute = no retry
}

// Custom retry logic
[RetryPolicy(MaxAttempts = 3)]
public class ApiCallJob : Job<ApiCallJob, ApiContext, ApiResult>, ICustomRetryPolicy
{
    public bool ShouldRetry(JobExecution lastExecution)
    {
        // Don't retry on 4xx errors, do retry on 5xx
        return lastExecution.ErrorMessage?.Contains("500") == true
            || lastExecution.ErrorMessage?.Contains("503") == true;
    }
}
```

### Extensible Metadata System

```csharp
// Base Job has first-class properties for universal patterns
public abstract class Job : Entity<Job>
{
    // Universal patterns → first-class indexed properties
    [Indexed] public string? CorrelationId { get; set; }
    [Indexed] public string? ParentJobId { get; set; }

    // Context-specific → extensible metadata
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

// Extension methods keep the public surface tidy
public static class JobMetadataExtensions
{
    public static string? GetTenantId(this Job job)
        => job.Metadata.TryGetValue("TenantId", out var v) ? v?.ToString() : null;

    public static Job With(
        this Job job,
        string? tenantId = null,
        string? userId = null,
        string? correlationId = null,
        Action<IDictionary<string, object?>>? metadata = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
            job.Metadata["TenantId"] = tenantId;

        if (!string.IsNullOrWhiteSpace(userId))
            job.Metadata["UserId"] = userId;

        if (!string.IsNullOrWhiteSpace(correlationId))
            job.CorrelationId = correlationId;

        metadata?.Invoke(job.Metadata);
        return job;
    }

    public static Job With(
        this Job job,
        Action<IDictionary<string, object?>> metadata)
        => job.With(metadata: metadata);

    public static Job WithMetadata(this Job job, string key, object? value)
    {
        job.Metadata[key] = value;
        return job;
    }
}

// Usage - fluent and discoverable
var job = await MediaBackupJob.Start(context)
    .With(tenantId: tenant.Id, userId: user.Id, correlationId: correlationId,
        metadata: meta => meta["SourceSystem"] = "WebApp")
    .Run();

// Query by first-class properties (efficient, indexed)
var jobs = await Job.Query(j => j.CorrelationId == correlationId);

// Query by metadata (provider-dependent efficiency)
var tenantJobs = await Job.Query(j =>
    j.Metadata.ContainsKey("TenantId") &&
    j.Metadata["TenantId"] == tenantId);
```

### Typed Job Pattern

```csharp
/// <summary>
/// Generic job entity with typed context and result.
/// Provides type-safe job execution with automatic serialization.
/// </summary>
public abstract class Job<TJob, TContext, TResult> : Job
    where TJob : Job<TJob, TContext, TResult>, new()
{
    // Context property (deserialized from ContextJson)
    [NotMapped]
    public TContext? Context
    {
        get => ContextJson != null
            ? JsonSerializer.Deserialize<TContext>(ContextJson)
            : default;
        set => ContextJson = value != null
            ? JsonSerializer.Serialize(value)
            : null;
    }

    // Result property (deserialized from ResultJson)
    [NotMapped]
    public TResult? Result
    {
        get => ResultJson != null
            ? JsonSerializer.Deserialize<TResult>(ResultJson)
            : default;
        set => ResultJson = value != null
            ? JsonSerializer.Serialize(value)
            : null;
    }

    // Abstract execution method (implemented by concrete jobs)
    protected abstract Task<TResult> Execute(
        TContext context,
        IJobProgress progress,
        CancellationToken cancellationToken);

    // Static job start method now returns a run builder
    public static JobRunBuilder<TJob, TContext, TResult> Start(
        TContext context,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        => new JobRunBuilder<TJob, TContext, TResult>(
            typeof(TJob),
            context,
            correlationId ?? Activity.Current?.TraceId.ToString(),
            cancellationToken);

    // Instance methods
    public async Task<TResult> Wait(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var waitTimeout = timeout ?? TimeSpan.FromMinutes(30);
        var deadline = DateTimeOffset.UtcNow.Add(waitTimeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await Job.Get(Id, cancellationToken);

            if (current?.Status == JobStatus.Completed)
                return current.Result!;

            if (current?.Status == JobStatus.Failed)
                throw new JobFailedException(current.LastError);

            if (current?.Status == JobStatus.Cancelled)
                throw new JobCancelledException();

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException($"Job {Id} did not complete within {waitTimeout}");
    }

    public IDisposable OnProgress(Func<JobProgressUpdate, Task> handler, CancellationToken cancellationToken = default)
    {
        return JobProgressBroker.Subscribe(Id, handler, cancellationToken);
    }

    public async Task<Job> Refresh(CancellationToken cancellationToken = default)
    {
        return await Job.Get(Id, cancellationToken) ?? this;
    }

    public async Task Cancel(CancellationToken cancellationToken = default)
    {
        var current = await Job.Get(Id, cancellationToken);
        if (current != null)
        {
            current.Status = JobStatus.Cancelled;
            current.CompletedAt = DateTimeOffset.UtcNow;
            await current.Save();
        }
    }
}
```

### Job Run Builder

```csharp
public sealed class JobRunBuilder<TJob, TContext, TResult>
    where TJob : Job<TJob, TContext, TResult>, new()
{
    private readonly Type _jobType;
    private readonly TContext _context;
    private readonly string? _correlationId;
    private readonly CancellationToken _cancellationToken;
    private readonly List<Action<TJob>> _mutators = new();
    private JobStorageMode _storageMode = JobStorageMode.InMemory;
    private bool _auditExecutions;
    private string? _source;
    private string? _partition;

    internal JobRunBuilder(Type jobType, TContext context, string? correlationId, CancellationToken cancellationToken)
    {
        _jobType = jobType;
        _context = context;
        _correlationId = correlationId;
        _cancellationToken = cancellationToken;
    }

    public JobRunBuilder<TJob, TContext, TResult> Persist(string? source = null, string? partition = null)
    {
        _storageMode = JobStorageMode.Entity;
        _source = source;
        _partition = partition;
        return this;
    }

    public JobRunBuilder<TJob, TContext, TResult> Audit(bool enabled = true)
    {
        _auditExecutions = enabled;
        return this;
    }

    public JobRunBuilder<TJob, TContext, TResult> With(Action<TJob> configure)
    {
        if (configure != null) _mutators.Add(configure);
        return this;
    }

    public async Task<TJob> Run(CancellationToken cancellationToken = default)
        => await JobRunDispatcher<TJob, TContext, TResult>.Run(this, cancellationToken);

    internal (Type JobType, TContext Context, string? CorrelationId, CancellationToken CancellationToken, JobStorageMode StorageMode, bool Audit, string? Source, string? Partition, IReadOnlyList<Action<TJob>> Mutators) Build()
        => (_jobType, _context, _correlationId, _cancellationToken, _storageMode, _auditExecutions, _source, _partition, _mutators);
}
```

### Progress Reporting Interface

```csharp
public interface IJobProgress
{
    /// <summary>
    /// Report progress as percentage (0.0 to 1.0)
    /// </summary>
    void Report(double percentage, string? message = null);

    /// <summary>
    /// Report progress as step completion
    /// </summary>
    void Report(int current, int total, string? message = null);

    /// <summary>
    /// Estimated completion time based on progress rate
    /// </summary>
    DateTimeOffset? EstimatedCompletion { get; }

    /// <summary>
    /// Check if job cancellation has been requested
    /// </summary>
    bool CancellationRequested { get; }
}

public class JobProgressUpdate
{
    public double Percentage { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? EstimatedCompletion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Jobs publish progress updates through `JobProgressBroker`. Consumers subscribe via `job.OnProgress(handler)`, which returns an `IDisposable` for cleanup. When a synchronous snapshot is needed, `await job.Refresh()` fetches the latest persisted state without waiting for another callback.

```csharp
using var subscription = job.OnProgress(update =>
{
    Logger.LogInformation("{Percentage:P0}: {Message}", update.Percentage, update.Message);
    return Task.CompletedTask;
});

var snapshot = await job.Refresh();
Console.WriteLine($"Status: {snapshot.Status}, Progress: {snapshot.Progress:P}");
```



---

## API Design & Developer Experience

### Example 1: Simple Backup Job

```csharp
// Define job
[RetryPolicy(MaxAttempts = 5, Strategy = RetryStrategy.ExponentialBackoff)]
public class MediaBackupJob : Job<MediaBackupJob, BackupContext, BackupResult>
{
    protected override async Task<BackupResult> Execute(
        BackupContext context,
        IJobProgress progress,
        CancellationToken ct)
    {
        progress.Report(0.1, "Starting backup...");

        var media = await Media.All(ct);
        progress.Report(0.2, $"Found {media.Count} media items");

        var exported = 0;
        foreach (var item in media)
        {
            await ExportMedia(item, context.DestinationPath, ct);
            exported++;
            progress.Report(exported, media.Count, $"Exported {exported}/{media.Count}");
        }

        progress.Report(1.0, "Backup complete");

        return new BackupResult
        {
            ItemsExported = exported,
            BytesWritten = CalculateTotalBytes()
        };
    }
}

// Start job
var job = await MediaBackupJob
    .Start(new BackupContext { DestinationPath = "/backup" })
    .With(correlationId: HttpContext.GetCorrelationId())
    .Persist(source: "jobs", partition: "hot")
    .Audit()
    .Run();

// Monitor progress (callback-based)
using var progress = job.OnProgress(update =>
{
    Console.WriteLine($"{update.Percentage:P}: {update.Message}");
    if (update.EstimatedCompletion.HasValue)
        Console.WriteLine($"  ETA: {update.EstimatedCompletion.Value:g}");
    return Task.CompletedTask;
});

// Ask for a snapshot at any time
var snapshot = await job.Refresh();
Console.WriteLine($"Status: {snapshot.Status}, Progress: {snapshot.Progress:P}");

// Or wait for completion
var result = await job.Wait(timeout: TimeSpan.FromMinutes(10));
Console.WriteLine($"Backed up {result.ItemsExported} items");

// Check execution history
var executions = await JobExecution.Query(e => e.JobId == job.Id);
Console.WriteLine($"Completed in {executions.Count} attempts");
```

### Example 2: Custom Retry Logic

```csharp
[RetryPolicy(MaxAttempts = 3)]
public class ExternalApiJob : Job<ExternalApiJob, ApiContext, ApiResult>, ICustomRetryPolicy
{
    protected override async Task<ApiResult> Execute(
        ApiContext context,
        IJobProgress progress,
        CancellationToken ct)
    {
        var response = await _httpClient.Get(context.Endpoint, ct);
        response.EnsureSuccessStatusCode(); // Throws on 4xx/5xx

        return new ApiResult { StatusCode = (int)response.StatusCode };
    }

    public bool ShouldRetry(JobExecution lastExecution)
    {
        // Don't retry client errors (4xx), do retry server errors (5xx)
        if (lastExecution.ErrorMessage?.Contains("400") == true) return false;
        if (lastExecution.ErrorMessage?.Contains("404") == true) return false;

        // Retry on 5xx errors or network issues
        return lastExecution.ErrorMessage?.Contains("500") == true
            || lastExecution.ErrorMessage?.Contains("503") == true
            || lastExecution.ErrorMessage?.Contains("timeout") == true;
    }

    public TimeSpan? GetRetryDelay(JobExecution lastExecution)
    {
        // Use exponential backoff for 503 (Service Unavailable)
        if (lastExecution.ErrorMessage?.Contains("503") == true)
            return TimeSpan.FromSeconds(Math.Pow(2, lastExecution.AttemptNumber) * 10);

        // Use default policy for other errors
        return null;
    }
}
```

### Example 3: Controller Integration

```csharp
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<JobInfo>> StartBackup(
        [FromBody] BackupRequest request,
        CancellationToken ct)
    {
        var job = await MediaBackupJob
            .Start(new BackupContext { DestinationPath = request.Path })
            .With(correlationId: HttpContext.GetCorrelationId())
            .Persist()
            .Run(ct);

        return Accepted($"/api/jobs/{job.Id}", new JobInfo
        {
            JobId = job.Id,
            Status = job.Status.ToString(),
            CreatedAt = job.CreatedAt
        });
    }

    [HttpGet("status/{jobId}")]
    public async Task<ActionResult<JobStatusResponse>> GetStatus(
        string jobId,
        CancellationToken ct)
    {
        var job = await Job.Get(jobId, ct);
        if (job == null) return NotFound();

        var executions = await JobExecution.Query(e => e.JobId == jobId, ct);

        return Ok(new JobStatusResponse
        {
            Status = job.Status,
            Progress = job.Progress,
            Message = job.ProgressMessage,
            ETA = job.EstimatedCompletion,
            AttemptCount = executions.Count
        });
    }

    [HttpGet("history/{jobId}")]
    public async Task<ActionResult<List<JobExecution>>> GetHistory(
        string jobId,
        CancellationToken ct)
    {
        var executions = await JobExecution.Query(
            e => e.JobId == jobId,
            new DataQueryOptions { OrderBy = "StartedAt DESC" },
            ct
        );

        return Ok(executions);
    }
}
```

### Example 4: Generic Job Controller (Auto-Generated CRUD)

```csharp
// Automatic REST API for all jobs
[Route("api/[controller]")]
public class JobsController : EntityController<Job>
{
    // Inherits:
    // GET /api/jobs (query all jobs)
    // GET /api/jobs/{id} (get specific job)
    // GET /api/jobs?status=Running (filter by status)
    // GET /api/jobs?correlationId=abc123 (find by correlation)

    [HttpGet("{id}/progress")]
    public async Task<ActionResult<JobProgressUpdate>> GetProgress(
        string id,
        CancellationToken ct)
    {
        var job = await Job.Get(id, ct);
        if (job == null) return NotFound();

        return Ok(new JobProgressUpdate
        {
            Percentage = job.Progress,
            Message = job.ProgressMessage,
            EstimatedCompletion = job.EstimatedCompletion,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        var job = await Job.Get(id, ct);
        if (job == null) return NotFound();

        await job.Cancel(ct);
        return Ok();
    }

    [HttpGet("{id}/executions")]
    public async Task<ActionResult<List<JobExecution>>> GetExecutions(
        string id,
        CancellationToken ct)
    {
        var executions = await JobExecution.Query(
            e => e.JobId == id,
            new DataQueryOptions { OrderBy = "StartedAt DESC" },
            ct
        );

        return Ok(executions);
    }
}
```


### Job Recipes (Reusable Profiles)

Lightweight starts are great for one-offs, but many teams want a pre-baked configuration that every call site can share. `Jobs.Recipe()` builds that profile and returns a strongly-typed runner you can stash in DI or static fields.

- `.Persist(...)` and `.Audit()` calls on the recipe become defaults for every run.
- `.WithDefaults(...)` (metadata/action overload) captures tenant, module, or other common tags.
- `.UsePolicy(...)` pins retry/timeout decisions without sprinkling attributes across types.
- Per-run overrides still work — calling `.Persist("jobs", "archive")` on the runner will override the recipe for that invocation only.

```csharp
// Configure once at startup
var durableBackups = Jobs.Recipe()
    .Persist(source: "jobs", partition: "hot")
    .Audit()
    .WithDefaults(metadata: meta => meta["Module"] = "Backups")
    .Build<MediaBackupJob>();

// Somewhere in the app
var job = await durableBackups
    .Start(new BackupContext { DestinationPath = request.Path })
    .With(userId: user.Id, correlationId: HttpContext.GetCorrelationId())
    .Run(ct);
```

Recipes can be registered through `KoanAutoRegistrar` so they are discoverable from DI, or declared as static fields on the job type for ad-hoc scenarios. Because the underlying store choice lives inside the recipe, swapping from in-memory to persisted storage stays a one-line change.

---

## Integration with Existing Koan Infrastructure

### 1. Background Service Execution

Jobs are executed by Koan Background Services with retry logic:

```csharp
[KoanBackgroundService]
public class JobExecutorService<TJob, TContext, TResult> : KoanBackgroundServiceBase
    where TJob : Job<TJob, TContext, TResult>, new()
{
    private readonly IJobQueue<TJob, TContext, TResult> _queue;

    public override async Task Execute(CancellationToken cancellationToken)
    {
        await foreach (var jobId in _queue.Dequeue(cancellationToken))
        {
            await ProcessJob(jobId, cancellationToken);
        }
    }

    private async Task ProcessJob(string jobId, CancellationToken ct)
    {
        var job = await Job.Get(jobId, ct);
        if (job == null) return;

        // Get retry policy from attribute
        var retryPolicy = typeof(TJob).GetCustomAttribute<RetryPolicyAttribute>();

        // Check attempt count from execution history
        var executions = await JobExecution.Query(e => e.JobId == jobId, ct);
        var attemptNumber = executions.Count + 1;

        if (retryPolicy != null && attemptNumber > retryPolicy.MaxAttempts)
        {
            Logger.LogWarning("Job {JobId} exceeded max attempts ({Max})",
                jobId, retryPolicy.MaxAttempts);
            job.Status = JobStatus.Failed;
            await job.Save();
            return;
        }

        // Create execution record
        var execution = new JobExecution
        {
            JobId = jobId,
            AttemptNumber = attemptNumber,
            Status = JobExecutionStatus.Running
        };
        await execution.Save();

        // Update job status
        job.Status = JobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        await job.Save();

        var progress = new JobProgressTracker(job);

        try
        {
            // Execute job
            var result = await job.Execute(job.Context!, progress, ct);

            // Success
            job.Status = JobStatus.Completed;
            job.Result = result;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Duration = job.CompletedAt - job.StartedAt;

            execution.Status = JobExecutionStatus.Succeeded;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Job {JobId} execution failed (attempt {Attempt})",
                jobId, attemptNumber);

            // Record execution failure
            execution.Status = JobExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.StackTrace = ex.StackTrace;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;

            job.LastError = ex.Message;

            // Check if should retry
            var shouldRetry = ShouldRetryJob(job, retryPolicy, execution, attemptNumber);

            if (shouldRetry)
            {
                // Re-queue for retry
                job.Status = JobStatus.Queued;
                var delay = CalculateRetryDelay(job, retryPolicy, attemptNumber, execution);

                Logger.LogInformation("Retrying job {JobId} in {Delay}", jobId, delay);

                await Task.Delay(delay, ct);
                await _queue.Enqueue(jobId, ct);
            }
            else
            {
                // Final failure
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        await execution.Save();
        await job.Save();
    }

    private bool ShouldRetryJob(
        Job job,
        RetryPolicyAttribute? policy,
        JobExecution execution,
        int attemptNumber)
    {
        if (policy == null || policy.Strategy == RetryStrategy.None)
            return false;

        if (attemptNumber >= policy.MaxAttempts)
            return false;

        // Custom retry logic
        if (job is ICustomRetryPolicy customPolicy)
            return customPolicy.ShouldRetry(execution);

        return true;
    }

    private TimeSpan CalculateRetryDelay(
        Job job,
        RetryPolicyAttribute policy,
        int attemptNumber,
        JobExecution execution)
    {
        // Custom delay calculation
        if (job is ICustomRetryPolicy customPolicy)
        {
            var customDelay = customPolicy.GetRetryDelay(execution);
            if (customDelay.HasValue)
                return customDelay.Value;
        }

        // Standard policy delay
        var delay = policy.Strategy switch
        {
            RetryStrategy.Immediate => TimeSpan.Zero,
            RetryStrategy.FixedDelay => TimeSpan.FromSeconds(policy.InitialDelaySeconds),
            RetryStrategy.LinearBackoff =>
                TimeSpan.FromSeconds(policy.InitialDelaySeconds * attemptNumber),
            RetryStrategy.ExponentialBackoff =>
                TimeSpan.FromSeconds(
                    Math.Min(
                        policy.InitialDelaySeconds * Math.Pow(policy.BackoffMultiplier, attemptNumber - 1),
                        policy.MaxDelaySeconds
                    )
                ),
            _ => TimeSpan.FromSeconds(policy.InitialDelaySeconds)
        };

        return delay;
    }
}
```

### 2. OpenTelemetry Integration

```csharp
// Automatic correlation from Activity.Current
var job = await MyJob
    .Start(context)
    .Run();
// job.CorrelationId = Activity.Current?.TraceId.ToString()

// Or explicit from HTTP headers
var correlationId = HttpContext.Request.Headers["x-correlation-id"].FirstOrDefault();
var job = await MyJob
    .Start(context)
    .With(correlationId: correlationId)
    .Run();

// Query by correlation
var relatedJobs = await Job.Query(j => j.CorrelationId == correlationId);
```

### 3. Entity Events Integration

```csharp
// Jobs emit entity lifecycle events
Job.Events
    .OnBeforeUpsert(async (job, ct) =>
    {
        // Validate job before starting
        if (string.IsNullOrEmpty(job.CorrelationId))
            job.CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
    })
    .OnAfterUpsert(async (job, ct) =>
    {
        // Publish job status change events
        await EventBus.Publish(new JobStatusChanged
        {
            JobId = job.Id,
            Status = job.Status,
            Timestamp = DateTimeOffset.UtcNow
        });
    });
```


### 4. Storage Profiles & Persistence

Jobs ship with two built-in stores and switch between them based on fluent calls:

- `InMemoryJobStore` *(default)* — zero configuration, ephemeral, and purged by a background sweeper after a short TTL (default 15 minutes). Data disappears on process restart, which is perfect for demos, API-triggered jobs, and workloads that only need "start → poll → done" semantics.
- `EntityJobStore` — activated once `.Persist(...)` is used. It runs through Koan's `EntityContext`, respects named sources/partitions, and unlocks archival plus execution auditing.

```csharp
// Ephemeral job - best effort, cleared after TTL or process recycle
var scratch = await MediaBackupJob
    .Start(context)
    .Run();

// Persist using default source/partition registered for jobs
var durable = await MediaBackupJob
    .Start(context)
    .Persist()          // flips store to EntityJobStore
    .Audit()            // capture JobExecution rows
    .Run();

// Route to explicit source + partition
var warmPath = await MediaBackupJob
    .Start(context)
    .Persist(source: "jobs", partition: "warm")
    .Run();

// Opt back out of execution history for lightweight persistence
var fireAndForget = await MediaBackupJob
    .Start(context)
    .Persist()
    .Audit(false)
    .Run();
```

`Persist()` without parameters uses `JobsOptions.DefaultSource` and `JobsOptions.DefaultPartition`. Passing `source` and/or `partition` overrides them per-job. `.Audit()` toggles creation of `JobExecution` records; it is ignored while the job remains in the in-memory store.

`JobsOptions.DefaultStore` stays at `InMemory`, so simply referencing `Koan.Jobs.Core` never forces a database dependency. Teams opt in when they value durability.

Once persisted, jobs behave like any other entity and can rely on provider annotations or runtime context routing:

```csharp
[DataAdapter("postgresql")]
public class AnalyticsJob : Job<AnalyticsJob, AnalyticsContext, AnalyticsResult> { }

using (EntityContext.Source("jobs"))
using (EntityContext.Partition("hot"))
{
    var job = await AnalyticsJob.Start(context)
        .Persist()
        .Run();
}
```

### 5. Data Context Configuration (EntityContext)

Jobs leverage Koan's `EntityContext` for flexible data source routing, allowing jobs to be stored in dedicated databases separate from application data.

#### Configuration-Based Source Routing

Define named data sources in `appsettings.json`:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=app.db"
        },
        "Jobs": {
          "Adapter": "postgresql",
          "ConnectionString": "Host=jobs-db;Database=KoanJobs;Username=jobs;Password=***",
          "CommandTimeoutSeconds": "120"
        },
        "Archive": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://archive-cluster/jobs-archive"
        }
      }
    }
  }
}
```

Configure job defaults alongside data sources:

```json
{
  "Koan": {
    "Jobs": {
      "DefaultStore": "InMemory",   // or "Entity"
      "DefaultSource": "Jobs",
      "DefaultPartition": "hot",
      "InMemory": {
        "CompletedRetentionMinutes": 15,
        "FaultedRetentionMinutes": 60
      }
    }
  }
}
```

`DefaultStore` keeps the framework in lightweight mode until persistence is requested. Retention knobs control how long completed/failed jobs live in the in-memory store before the sweeper removes them.

#### Usage Patterns

> Ambient `EntityContext.Source(...)` / `.Partition(...)` scopes are captured when `.Persist()` is invoked. Without persistence calls, jobs stay in the default in-memory store regardless of the current context.

**A. Global Source Configuration (Recommended)**

Configure all jobs to use dedicated source:

```csharp
// Apply to base Job entity via [DataSource] attribute (future)
// Or use EntityContext in auto-registrar

public class KoanJobsAutoRegistrar : IKoanInitializer
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Default routing: all jobs to "Jobs" source
        services.Configure<JobsOptions>(opts => {
            opts.DefaultSource = "Jobs";
        });
    }
}
```

**B. Explicit Source Routing**

Use `EntityContext` for runtime source switching:

```csharp
// Route specific job to dedicated source
using (EntityContext.Source("Jobs"))
{
    var job = await BackupJob
        .Start(context)
        .Persist() // Saved to "Jobs" source (PostgreSQL in config above)
        .Run();
}

// Query jobs from dedicated source
using (EntityContext.Source("Jobs"))
{
    var runningJobs = await Job.Query(j => j.Status == JobStatus.Running);
}

// Archive old jobs to different source
var oldJobs = await Job.Query(j => j.CompletedAt < DateTimeOffset.UtcNow.AddMonths(-6));
using (EntityContext.Source("Archive"))
{
    await Job.UpsertMany(oldJobs); // Archived to MongoDB
}

// Delete from primary source
using (EntityContext.Source("Jobs"))
{
    await Job.Remove(oldJobs.Select(j => j.Id));
}
```

**C. Partition-Based Archiving**

Use partitions for logical separation within same source:

```csharp
// Active jobs in default partition
var job = await BackupJob
    .Start(context)
    .Persist()
    .Run();

// After completion, move to archive partition
if (job.Status == JobStatus.Completed &&
    job.CompletedAt < DateTimeOffset.UtcNow.AddDays(-30))
{
    using (EntityContext.Partition("archive"))
    {
        await job.Save(); // Saved to Job#archive collection/table
    }

    // Remove from default partition
    await job.Remove();
}

// Query archived jobs
using (EntityContext.Partition("archive"))
{
    var archivedJobs = await Job.All();
}
```

**D. Per-Entity Type Adapter Override**

Force specific adapters via `[DataAdapter]` attribute:

```csharp
// Jobs always use PostgreSQL (JSONB for metadata)
[DataAdapter("postgresql")]
public abstract class Job : Entity<Job>
{
    // ...
}

// Execution history in time-series optimized store
[DataAdapter("mongodb")]
public class JobExecution : Entity<JobExecution>
{
    // ...
}

// No EntityContext needed - adapter fixed at type level
var job = await BackupJob
    .Start(context)
    .Persist() // Always PostgreSQL
    .Run();
var executions = await JobExecution.Query(e => e.JobId == jobId); // Always MongoDB
```

#### Configuration Strategy Decision Tree

```
Choose configuration approach based on requirements:

┌─ Need jobs in separate database from app data?
│  └─ YES → Configure "Jobs" source in appsettings.json
│           Use EntityContext.Source("Jobs") globally or in auto-registrar
│
├─ Need different adapters for Job vs JobExecution?
│  └─ YES → Use [DataAdapter] attribute on each entity type
│
├─ Need to archive old jobs to cold storage?
│  └─ YES → Configure "Archive" source (different DB)
│           OR use partitions (same DB, logical separation)
│
├─ Jobs share same DB as application entities?
│  └─ YES → No special configuration needed
│           Jobs use "Default" source automatically
│
└─ Multi-tenant with per-tenant job databases?
   └─ YES → Configure tenant-specific sources
            Use EntityContext.Source(tenantId) at runtime
```

#### Best Practices

1. **Dedicated Source Recommended**: Jobs have different lifecycle and retention than application data
   ```json
   "Jobs": {
     "Adapter": "postgresql",
     "ConnectionString": "Host=jobs-db;..."
   }
   ```

2. **Execution History Separate**: High-write volume suggests NoSQL for executions
   ```csharp
   [DataAdapter("mongodb")]
   public class JobExecution : Entity<JobExecution> { }
   ```

3. **Archive via Source, Not Partition**: Old jobs → different database for cost optimization
   ```csharp
   using (EntityContext.Source("Archive")) // Cheap S3-backed MongoDB
   {
       await Job.UpsertMany(oldJobs);
   }
   ```

4. **Connection Pool Sizing**: Jobs source may need larger pool for concurrent execution
   ```json
   "Jobs": {
     "Adapter": "postgresql",
     "ConnectionString": "Host=jobs-db;Maximum Pool Size=100;..."
   }
   ```

5. **Timeouts**: Long-running job queries need extended timeouts
   ```json
   "Jobs": {
     "CommandTimeoutSeconds": "300"
   }
   ```

#### Example: Full Multi-Tier Configuration

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "postgresql",
          "ConnectionString": "Host=app-db;Database=MyApp;..."
        },
        "Jobs": {
          "Adapter": "postgresql",
          "ConnectionString": "Host=jobs-db;Database=KoanJobs;Maximum Pool Size=100;...",
          "CommandTimeoutSeconds": "300"
        },
        "JobHistory": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://history-cluster/job-executions"
        },
        "Archive": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://s3-archive/jobs?readPreference=secondary"
        }
      }
    }
  }
}
```

```csharp
// Entity configuration
[DataAdapter("postgresql")] // Use Jobs source (configured above)
public abstract class Job : Entity<Job> { }

[DataAdapter("mongodb")] // Use JobHistory source
public class JobExecution : Entity<JobExecution> { }

// Runtime usage
using (EntityContext.Source("Jobs"))
{
    var job = await BackupJob
        .Start(context)
        .Persist()
        .Run();
}

// Executions auto-route to MongoDB via [DataAdapter]
var executions = await JobExecution.Query(e => e.JobId == jobId);

// Archive old jobs (source switch)
var oldJobs = await Job.Query(j => j.CompletedAt < DateTimeOffset.UtcNow.AddMonths(-6));
using (EntityContext.Source("Archive"))
{
    await Job.UpsertMany(oldJobs);
}
```

**Result**:
- Active jobs: PostgreSQL (ACID compliance, queryability)
- Execution history: MongoDB (high write throughput, time-series patterns)
- Archived jobs: MongoDB on S3 (cost-optimized cold storage)

---

## Job Lifecycle Management

### Automatic Archival Policy

> **Note:** Archival runs only for jobs persisted via `.Persist(...)`. Ephemeral jobs in the in-memory store expire via TTL sweeper and never reach archival tiers.

Jobs have natural lifecycle phases: active (running/pending), completed (recent), and historical (archived). Koan.Jobs provides automatic archival policies to move completed jobs through storage tiers based on age and status.

#### Configuration

Define archival policies in `appsettings.json`:

```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Enabled": true,
        "CheckIntervalMinutes": 60,
        "Policies": [
          {
            "Name": "CompletedJobsToWarm",
            "Status": "Completed",
            "MinAgeHours": 24,
            "TargetPartition": "warm",
            "TargetSource": null,
            "DeleteFromSource": true
          },
          {
            "Name": "WarmJobsToCold",
            "Status": "Completed",
            "SourcePartition": "warm",
            "MinAgeHours": 168,
            "TargetSource": "Archive",
            "TargetPartition": null,
            "DeleteFromSource": true
          },
          {
            "Name": "FailedJobsToArchive",
            "Status": "Failed",
            "MinAgeHours": 72,
            "TargetSource": "Archive",
            "MaxRetainCount": 1000,
            "DeleteFromSource": true
          },
          {
            "Name": "DeadLetterRetention",
            "Status": "DeadLettered",
            "MinAgeHours": 720,
            "TargetSource": "Archive",
            "DeleteFromSource": true
          }
        ]
      }
    }
  }
}
```

#### Archival Policy Model

```csharp
public class JobArchivalPolicy
{
    /// <summary>Policy name for logging and diagnostics</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Job status to match (Completed, Failed, Cancelled, DeadLettered)</summary>
    public JobStatus Status { get; set; }

    /// <summary>Source partition to query (null = default partition)</summary>
    public string? SourcePartition { get; set; }

    /// <summary>Minimum age in hours since CompletedAt/LastUpdated</summary>
    public int MinAgeHours { get; set; }

    /// <summary>Target data source (null = same source, different partition)</summary>
    public string? TargetSource { get; set; }

    /// <summary>Target partition (null if TargetSource specified)</summary>
    public string? TargetPartition { get; set; }

    /// <summary>Delete from source after archiving (default: true)</summary>
    public bool DeleteFromSource { get; set; } = true;

    /// <summary>Maximum jobs to retain in source before forcing archival</summary>
    public int? MaxRetainCount { get; set; }

    /// <summary>Job types to apply policy to (null = all jobs)</summary>
    public List<string>? JobTypes { get; set; }

    /// <summary>Batch size for archival operations</summary>
    public int BatchSize { get; set; } = 100;
}
```

#### Implementation: JobArchivalService

```csharp
[KoanBackgroundService]
public class JobArchivalService : KoanPeriodicServiceBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<JobArchivalOptions> _options;
    private readonly ILogger<JobArchivalService> _logger;

    public override TimeSpan Period =>
        TimeSpan.FromMinutes(_options.Value.CheckIntervalMinutes);

    public JobArchivalService(
        IServiceProvider serviceProvider,
        IOptions<JobArchivalOptions> options,
        ILogger<JobArchivalService> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecutePeriodic(CancellationToken ct)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogDebug("Job archival disabled");
            return;
        }

        foreach (var policy in _options.Value.Policies)
        {
            try
            {
                await ExecutePolicy(policy, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Archival policy {PolicyName} failed", policy.Name);
            }
        }
    }

    private async Task ExecutePolicy(
        JobArchivalPolicy policy,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Executing archival policy: {PolicyName}", policy.Name);

        var cutoffDate = DateTimeOffset.UtcNow.AddHours(-policy.MinAgeHours);

        // Query jobs matching policy criteria
        IEnumerable<Job> candidateJobs;

        // Set source context if specified
        using (policy.SourcePartition != null
            ? EntityContext.Partition(policy.SourcePartition)
            : null)
        {
            candidateJobs = await Job.Query(j =>
                j.Status == policy.Status &&
                j.CompletedAt.HasValue &&
                j.CompletedAt.Value <= cutoffDate);

            // Filter by job type if specified
            if (policy.JobTypes?.Any() == true)
            {
                candidateJobs = candidateJobs.Where(j =>
                    policy.JobTypes.Contains(j.GetType().Name));
            }

            // Apply max retain count
            if (policy.MaxRetainCount.HasValue)
            {
                var ordered = candidateJobs
                    .OrderByDescending(j => j.CompletedAt)
                    .ToList();

                if (ordered.Count > policy.MaxRetainCount.Value)
                {
                    candidateJobs = ordered.Skip(policy.MaxRetainCount.Value);
                }
                else
                {
                    candidateJobs = Enumerable.Empty<Job>();
                }
            }
        }

        var jobsToArchive = candidateJobs.ToList();
        if (!jobsToArchive.Any())
        {
            _logger.LogDebug(
                "No jobs match policy {PolicyName}", policy.Name);
            return;
        }

        _logger.LogInformation(
            "Archiving {Count} jobs via policy {PolicyName}",
            jobsToArchive.Count, policy.Name);

        // Archive in batches
        foreach (var batch in jobsToArchive.Chunk(policy.BatchSize))
        {
            // Set target context
            var targetContext = policy.TargetSource != null
                ? EntityContext.Source(policy.TargetSource)
                : policy.TargetPartition != null
                    ? EntityContext.Partition(policy.TargetPartition)
                    : null;

            using (targetContext)
            {
                // Archive jobs to target
                await Job.UpsertMany(batch, ct);
            }

            // Delete from source if requested
            if (policy.DeleteFromSource)
            {
                using (policy.SourcePartition != null
                    ? EntityContext.Partition(policy.SourcePartition)
                    : null)
                {
                    await Job.RemoveMany(batch.Select(j => j.Id), ct);
                }
            }

            _logger.LogDebug(
                "Archived batch of {Count} jobs", batch.Count());
        }

        _logger.LogInformation(
            "Completed archival policy {PolicyName}: {Count} jobs archived",
            policy.Name, jobsToArchive.Count);
    }
}
```

#### Multi-Tier Archival Strategy

**Default Strategy: Two-Tier (Hot → Archive)**

The default configuration provides a simple two-tier approach:

**Hot Tier** (Default Partition)
- Active jobs (Created, Queued, Running)
- Recently completed jobs (<30 days)
- **Query pattern**: High-frequency status checks, progress updates
- **Retention**: 30 days after completion
- **Storage**: Primary job database (PostgreSQL, MongoDB, etc.)

**Archive Tier** (Partition "archive")
- Old completed jobs (>30 days)
- Failed/cancelled/dead-lettered jobs (>30 days)
- **Query pattern**: Infrequent compliance/audit queries
- **Retention**: Indefinite or compliance-driven
- **Storage**: Same database, different partition (logical separation)

**Default Configuration** (automatic):
```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Enabled": true,
        "CheckIntervalMinutes": 60
        // Defaults to 30-day retention, "archive" partition
      }
    }
  }
}
```

**Advanced Strategy: Three-Tier (Hot → Warm → Cold)**

For high-volume deployments, use three-tier strategy with separate storage backends:

**Hot Tier** (Default Partition - PostgreSQL)
- Active + recently completed jobs (<7 days)
- **Query pattern**: Very high frequency
- **Retention**: 7 days
- **Storage**: Fast, expensive storage (SSD-backed PostgreSQL)

**Warm Tier** (Partition "warm" - Same DB)
- Completed jobs (7-30 days old)
- **Query pattern**: Moderate frequency
- **Retention**: 23 days
- **Storage**: Same DB, logical partition

**Cold Tier** (Source "Archive" - MongoDB/S3)
- Old completed jobs (>30 days)
- **Query pattern**: Infrequent compliance queries
- **Retention**: Indefinite
- **Storage**: Cost-optimized (MongoDB on S3, Glacier)

**Three-Tier Configuration Example**:
```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "postgresql",
          "ConnectionString": "Host=jobs-db;Database=KoanJobs;..."
        },
        "Archive": {
          "Adapter": "mongodb",
          "ConnectionString": "mongodb://archive.s3.amazonaws.com/jobs"
        }
      }
    },
    "Jobs": {
      "Archival": {
        "Enabled": true,
        "CheckIntervalMinutes": 60,
        "UseDefaults": false,
        "Policies": [
          {
            "Name": "HotToWarm",
            "Status": "Completed",
            "MinAgeHours": 168,
            "TargetPartition": "warm",
            "DeleteFromSource": true
          },
          {
            "Name": "WarmToCold",
            "Status": "Completed",
            "SourcePartition": "warm",
            "MinAgeHours": 552,
            "TargetSource": "Archive",
            "DeleteFromSource": true,
            "Comment": "168h + 552h = 720h (30 days total)"
          }
        ]
      }
    }
  }
}
```

#### Per-Job-Type Archival Policies

Different job types may have different retention requirements:

```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Policies": [
          {
            "Name": "FastArchiveAnalytics",
            "Status": "Completed",
            "MinAgeHours": 1,
            "JobTypes": ["AnalyticsJob", "MetricsJob"],
            "TargetSource": "Archive",
            "DeleteFromSource": true,
            "Comment": "Analytics jobs archived after 1 hour (high volume, low value)"
          },
          {
            "Name": "RetainComplianceJobs",
            "Status": "Completed",
            "MinAgeHours": 8760,
            "JobTypes": ["AuditJob", "ComplianceReportJob"],
            "TargetSource": "ComplianceArchive",
            "DeleteFromSource": true,
            "Comment": "Compliance jobs retained 1 year in hot storage"
          }
        ]
      }
    }
  }
}
```

#### Querying Archived Jobs

```csharp
// Query across all tiers (expensive, avoid in hot path)
public async Task<List<Job>> FindJobAcrossTiers(string correlationId, CancellationToken ct)
{
    var results = new List<Job>();

    // Check hot tier (default partition)
    results.AddRange(await Job.Query(j => j.CorrelationId == correlationId, ct));

    // Check warm tier
    using (EntityContext.Partition("warm"))
    {
        results.AddRange(await Job.Query(j => j.CorrelationId == correlationId, ct));
    }

    // Check cold tier
    using (EntityContext.Source("Archive"))
    {
        results.AddRange(await Job.Query(j => j.CorrelationId == correlationId, ct));
    }

    return results;
}

// Efficient: Query specific tier based on age
public async Task<Job?> FindRecentJob(string jobId, CancellationToken ct)
{
    // Try hot tier first
    var job = await Job.Get(jobId, ct);
    if (job != null) return job;

    // Try warm tier
    using (EntityContext.Partition("warm"))
    {
        job = await Job.Get(jobId, ct);
    }

    return job; // Don't check cold tier for "recent" queries
}
```

#### Archival Events & Observability

```csharp
// Emit archival events for monitoring
public class JobArchivedEvent
{
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public string SourcePartition { get; set; } = string.Empty;
    public string? TargetSource { get; set; }
    public string? TargetPartition { get; set; }
    public DateTimeOffset ArchivedAt { get; set; }
    public TimeSpan JobAge { get; set; }
}

// Metrics for Prometheus/OpenTelemetry
public class JobArchivalMetrics
{
    private static readonly Counter JobsArchived = Metrics.CreateCounter(
        "koan_jobs_archived_total",
        "Total number of jobs archived",
        new CounterConfiguration { LabelNames = new[] { "policy", "status", "tier" } });

    private static readonly Histogram ArchivalDuration = Metrics.CreateHistogram(
        "koan_jobs_archival_duration_seconds",
        "Duration of archival operations",
        new HistogramConfiguration { LabelNames = new[] { "policy" } });

    public void RecordArchival(JobArchivalPolicy policy, int count, TimeSpan duration)
    {
        JobsArchived.WithLabels(policy.Name, policy.Status.ToString(),
            policy.TargetSource ?? policy.TargetPartition ?? "default")
            .Inc(count);

        ArchivalDuration.WithLabels(policy.Name).Observe(duration.TotalSeconds);
    }
}
```

#### Archival Policy Best Practices

**1. Default Configuration is Production-Ready**
The 30-day default retention is suitable for most applications:
- Long enough for debugging and investigation
- Prevents unbounded storage growth
- Balances operational needs with cost

**2. Gradual Tiering (Advanced)**
```
Hot (7d) → Warm (23d) → Cold (indefinite)
```
Only necessary for high-volume deployments (>10K jobs/day).

**3. Status-Specific Retention**
Default treats all statuses equally (30 days). Customize if needed:
- **Completed**: Standard retention (30 days default)
- **Failed**: Extended retention (60-90 days) for debugging patterns
- **DeadLettered**: Long retention (90+ days) for investigation
- **Cancelled**: Standard or reduced retention (30 days or less)

**4. Batch Size Tuning**
- **Large batches** (1000+): Faster archival, higher memory
- **Small batches** (100): Slower, more transactions, lower memory
- **Default recommendation**: 500 (balanced for most job sizes)

**5. Check Interval**
- **Hourly (default)**: Suitable for most workloads
- **Every 15 minutes**: High-volume job systems (>10K jobs/day)
- **Every 6-12 hours**: Low-volume, cost-sensitive deployments

**6. Delete Strategy**
- **Always delete from source** after successful archive
- **Keep 1 week in warm tier** for common queries
- **Verify archive success** before deletion:
  ```csharp
  if (policy.DeleteFromSource)
  {
      // Verify archived jobs exist in target
      using (targetContext)
      {
          var verifyIds = batch.Select(j => j.Id).ToList();
          var archived = await Job.Query(j => verifyIds.Contains(j.Id));

          if (archived.Count != verifyIds.Count)
          {
              _logger.LogError("Archive verification failed, skipping deletion");
              return;
          }
      }

      // Safe to delete
      await Job.RemoveMany(batch.Select(j => j.Id), ct);
  }
  ```

#### Default Policy Recommendation

**Archival is enabled by default** with a generous 1-month retention window. Koan.Jobs applies these defaults automatically:

```csharp
public static class DefaultArchivalPolicies
{
    public static List<JobArchivalPolicy> GetDefaults() => new()
    {
        new JobArchivalPolicy
        {
            Name = "CompletedJobsArchival",
            Status = JobStatus.Completed,
            MinAgeHours = 720, // 30 days
            TargetPartition = "archive",
            DeleteFromSource = true,
            BatchSize = 500
        },
        new JobArchivalPolicy
        {
            Name = "FailedJobsArchival",
            Status = JobStatus.Failed,
            MinAgeHours = 720, // 30 days
            TargetPartition = "archive",
            DeleteFromSource = true,
            BatchSize = 500
        },
        new JobArchivalPolicy
        {
            Name = "CancelledJobsArchival",
            Status = JobStatus.Cancelled,
            MinAgeHours = 720, // 30 days
            TargetPartition = "archive",
            DeleteFromSource = true,
            BatchSize = 500
        },
        new JobArchivalPolicy
        {
            Name = "DeadLetteredJobsArchival",
            Status = JobStatus.DeadLettered,
            MinAgeHours = 720, // 30 days
            TargetPartition = "archive",
            DeleteFromSource = true,
            BatchSize = 500
        }
    };
}
```

**Rationale for 1-month default**:
- **Conservative enough** for debugging and investigation
- **Prevents unbounded growth** in production deployments
- **Operator-friendly** - clear boundary for hot vs. cold storage
- **Cost-effective** - Balances storage costs with operational needs

**Disable archival** (not recommended):
```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Enabled": false
      }
    }
  }
}
```

**Override defaults** with custom policies:
```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Enabled": true,
        "UseDefaults": false,
        "Policies": [ /* custom policies */ ]
      }
    }
  }
}
```

**Extend retention** while keeping defaults:
```json
{
  "Koan": {
    "Jobs": {
      "Archival": {
        "Enabled": true,
        "Policies": [
          {
            "Name": "CompletedJobsArchival",
            "Status": "Completed",
            "MinAgeHours": 2160,
            "TargetPartition": "archive",
            "DeleteFromSource": true,
            "Comment": "Extended to 90 days for compliance"
          }
        ]
      }
    }
  }
}
```

---

## Specialized Job Types (Future)

### FiniteJob - Item-Based Processing

For jobs that process collections, a `FiniteJob<>` base class eliminates boilerplate:

```csharp
public abstract class FiniteJob<TJob, TContext, TResult, TItem>
    : Job<TJob, TContext, TResult>
    where TJob : FiniteJob<TJob, TContext, TResult, TItem>, new()
{
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }

    // Template method pattern - subclasses provide items and processing logic
    protected abstract IAsyncEnumerable<TItem> GetItems(
        TContext context,
        CancellationToken ct);

    protected abstract Task<ItemResult> ProcessItem(
        TItem item,
        int index,
        IJobProgress progress,
        CancellationToken ct);

    // Framework implements item-based execution
    protected sealed override async Task<TResult> Execute(
        TContext context,
        IJobProgress progress,
        CancellationToken ct)
    {
        var items = new List<TItem>();
        await foreach (var item in GetItems(context, ct))
            items.Add(item);

        TotalItems = items.Count;
        progress.Report(0, TotalItems, $"Starting processing of {TotalItems} items");

        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                var result = await ProcessItem(items[i], i, progress, ct);

                if (result.Success) ProcessedItems++;
                else if (result.Skipped) SkippedItems++;
                else FailedItems++;
            }
            catch
            {
                FailedItems++;
            }

            progress.Report(i + 1, TotalItems,
                $"Processed {i + 1}/{TotalItems} ({FailedItems} failed)");
        }

        return BuildResult();
    }

    protected abstract TResult BuildResult();
}

// Usage example
public class MediaExportJob : FiniteJob<MediaExportJob, ExportContext, ExportResult, Media>
{
    protected override async IAsyncEnumerable<Media> GetItems(
        ExportContext context,
        CancellationToken ct)
    {
        await foreach (var media in Media.AllStream(ct: ct))
            yield return media;
    }

    protected override async Task<ItemResult> ProcessItem(
        Media item,
        int index,
        IJobProgress progress,
        CancellationToken ct)
    {
        await ExportMedia(item, ct);
        return ItemResult.Success();
    }

    protected override ExportResult BuildResult()
        => new() { Exported = ProcessedItems, Failed = FailedItems };
}
```

**Status**: Evaluated for Phase 3. Solves common item-based processing pattern.

---

## Migration Path for S13.DocMind

Existing `DocumentProcessingJob` can migrate to generalized pattern:

```csharp
// BEFORE (S13.DocMind specific)
public sealed class DocumentProcessingJob : Entity<DocumentProcessingJob>
{
    public Guid SourceDocumentId { get; set; }
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public int Attempt { get; set; }           // ❌ Execution metadata
    public int MaxAttempts { get; set; }        // ❌ Policy config
    // ... custom fields
}

// AFTER (using Koan.Jobs)
[RetryPolicy(MaxAttempts = 5, Strategy = RetryStrategy.ExponentialBackoff)]
public class DocumentProcessingJob : Job<DocumentProcessingJob, DocumentContext, DocumentResult>
{
    // Context moved to typed Context property
    // Status migrated to base Job.Status
    // Retry policy now declarative via attribute
    // Stage tracking via job.Metadata or multi-stage job pattern

    protected override async Task<DocumentResult> Execute(
        DocumentContext context,
        IJobProgress progress,
        CancellationToken ct)
    {
        // Existing DocumentProcessingWorker logic moves here
        progress.Report(0.0, "Starting text extraction");
        var text = await ExtractText(context.DocumentId, ct);

        progress.Report(0.3, "Extracting keywords");
        var keywords = await ExtractKeywords(text, ct);

        progress.Report(0.6, "Generating embeddings");
        var embeddings = await GenerateEmbeddings(text, ct);

        progress.Report(1.0, "Processing complete");

        return new DocumentResult { Keywords = keywords, EmbeddingCount = embeddings.Count };
    }
}

// Query execution history
var executions = await JobExecution.Query(e => e.JobId == job.Id);
var attemptCount = executions.Count;
```

**Migration Benefits:**
- ✅ Standardized API (`Start()`, `Wait()`, `OnProgress()`, `Refresh()`)
- ✅ Clean separation: domain (Job) vs policy (RetryPolicy) vs audit (JobExecution)
- ✅ Built-in progress tracking with ETA
- ✅ Correlation ID auto-capture
- ✅ Complete execution history
- ✅ Less boilerplate (no manual worker integration)
- ✅ Reusable across projects

---

## Implementation Roadmap

### Phase 1: Core Infrastructure (Week 1-2)
- [ ] Create `Koan.Jobs.Core` project
- [ ] Implement base `Job` entity (clean, no retry fields)
- [ ] Implement `JobExecution` entity for execution history
- [ ] Implement `RetryPolicyAttribute` and `ICustomRetryPolicy`
- [ ] Implement `Job<TJob, TContext, TResult>` generic base
- [ ] Implement `IJobProgress` and `JobProgressTracker`
- [ ] Create `JobQueue<T>` abstraction (in-memory initially)
- [ ] Implement default `InMemoryJobStore` with TTL eviction
- [ ] Implement `EntityJobStore` and fluent `.Persist(...)`/`.Audit()`
- [ ] Implement `JobRunBuilder` with `.Run()` pipeline
- [ ] Add `Jobs.Recipe()` builder (Persist/Audit/WithDefaults/UsePolicy) and registration hooks
- [ ] Implement `JobProgressBroker` for callbacks and telemetry fan-out
- [ ] Implement `JobExecutorService` with retry logic
- [ ] Auto-registration via `KoanAutoRegistrar`
- [ ] Support `EntityContext` for data source routing
- [ ] Default to "Default" source, detect "Jobs" source if configured

### Phase 2: API Surface (Week 3)
- [ ] Implement `Start()`, `Wait()`, `OnProgress()`/`Refresh()` methods
- [ ] Implement cancellation support
- [ ] Create `JobsController` for REST API (with execution history endpoints)
- [ ] Add OpenTelemetry correlation integration
- [ ] Implement unified metadata helpers (`With(...)`, `GetTenantId`, etc.)
- [ ] Document data source configuration patterns (dedicated DB, archiving, multi-tenant)

### Phase 3: Advanced Features (Week 4-5)
- [ ] Parent-child job relationships
- [ ] `FiniteJob<>` for item-based processing
- [ ] Multi-stage job support (pipeline pattern)
- [ ] Persistent queue implementations (Redis, RabbitMQ)
- [ ] SignalR integration for real-time progress updates
- [ ] Job priority and queueing strategies

### Phase 4: Developer Experience (Week 6)
- [ ] Create `Koan.Jobs.Web` for dashboard UI
- [ ] Code generation for job boilerplate
- [ ] Template project (`dotnet new koan-job`)
- [ ] Comprehensive documentation
- [ ] Migration guide for S13.DocMind

### Phase 5: Enterprise Features (Week 7-8)
- [ ] Implement `JobArchivalService` background service
- [ ] Implement `JobArchivalPolicy` model and configuration binding
- [ ] Add default archival policies (enabled by default, 30-day retention)
- [ ] Implement archive verification before deletion
- [ ] Add archival metrics (Prometheus/OpenTelemetry)
- [ ] Implement `JobArchivalOptions` with `Enabled: true` default
- [ ] Document per-job-type archival policies
- [ ] Document three-tier archival strategy (hot→warm→cold) for high-volume scenarios
- [ ] Job result caching
- [ ] Distributed job execution (multi-instance coordination)
- [ ] Batch job operations

**Note**: Archival is core functionality, not optional. Default 30-day retention prevents unbounded growth while being conservative enough for most use cases.

---

## Decision Points for Review

1. **Naming**: `Koan.Jobs` vs `Koan.Tasks` vs `Koan.Work`?
   - **Recommendation**: `Koan.Jobs` aligns with industry terminology (Hangfire, Kubernetes Jobs)

2. **Progress Persistence**: Should progress updates be persisted to database on every report?
   - **Recommendation**: Throttle persistence (max 1 update per second) to reduce DB load

3. **Queue Implementation**: In-memory or persistent (Redis/RabbitMQ) for initial release?
   - **Recommendation**: In-memory for v1, persistent as opt-in via `Koan.Jobs.Queue.Redis` package

4. **FiniteJob**: Include in Phase 1 or defer to Phase 3?
   - **Recommendation**: Defer to Phase 3 after validating base job pattern

5. **S13.DocMind Migration**: Migrate immediately or keep parallel during transition?
   - **Recommendation**: Keep parallel initially, deprecate old pattern after v1 stabilizes

6. **Dashboard UI**: Built-in minimal dashboard or rely on entity endpoints + custom frontend?
   - **Recommendation**: Leverage existing `EntityController` for v1, dedicated dashboard in v2

7. **Job Result Storage**: Always serialize to JSON or support blob storage for large results?
   - **Recommendation**: JSON for v1 (align with Entity pattern), add `IJobResultStore` abstraction in v2

8. **Default Data Source**: Should jobs use "Default" source or require explicit "Jobs" source configuration?
   - **Recommendation**: Use "Default" source if no "Jobs" source configured (zero-config principle), but document dedicated source as best practice

9. **Archival Policy Defaults**: Should archival be enabled by default with opinionated policies, or opt-in?
   - **Decision**: Enabled by default with generous 1-month retention window. This prevents unbounded job table growth while being conservative enough for most use cases.

10. **Archival Verification**: Should archival service verify jobs exist in target before deleting from source?
   - **Recommendation**: Yes, always verify to prevent data loss. Log verification failures and skip deletion on mismatch.

---

## Appendix: Complete API Reference

### Job Lifecycle Methods

```csharp
// Static/builder methods
await MyJob.Start(context, correlationId?, ct)
    .With(metadata: meta => meta["TenantId"] = tenantId)
    .Persist()
    .Audit()
    .Run();
await MyJob.Get(jobId, ct);
await MyJob.Query(predicate, ct);
await MyJob.Remove(jobId, ct);

// Instance methods
await job.Save();
await job.Wait(timeout?, ct);
using var hook = job.OnProgress(update => Task.CompletedTask);
await job.Refresh(ct);
await job.Cancel(ct);
await job.GetParent<ParentJob>(ct);  // For child jobs
await job.GetChildren<ChildJob>(ct); // For parent jobs

// Metadata fluent API
job.With(tenantId: tenantId, userId: userId)
   .With(metadata: meta => meta[key] = value);
```

### Progress Reporting

```csharp
interface IJobProgress
{
    void Report(double percentage, string? message = null);
    void Report(int current, int total, string? message = null);
    DateTimeOffset? EstimatedCompletion { get; }
    bool CancellationRequested { get; }
}

// Usage in job
progress.Report(0.5, "Halfway done");
progress.Report(5, 10, "Processed 5 of 10 items");

if (progress.EstimatedCompletion.HasValue)
    Logger.LogInformation("ETA: {ETA}", progress.EstimatedCompletion);
```

### Execution History

```csharp
// Query all attempts for a job
var executions = await JobExecution.Query(
    e => e.JobId == jobId,
    new DataQueryOptions { OrderBy = "StartedAt DESC" }
);

// Get attempt count
var attemptCount = executions.Count;

// Analyze failures
var failures = executions.Where(e => e.Status == JobExecutionStatus.Failed);
foreach (var failure in failures)
{
    Console.WriteLine($"Attempt {failure.AttemptNumber}: {failure.ErrorMessage}");
}
```

### Query Patterns

```csharp
// By status
var running = await Job.Query(j => j.Status == JobStatus.Running);

// By correlation
var related = await Job.Query(j => j.CorrelationId == correlationId);

// By date range
var recent = await Job.Query(j =>
    j.CreatedAt >= DateTimeOffset.UtcNow.AddHours(-24)
);

// By parent
var children = await Job.Query(j => j.ParentJobId == parentId);

// By metadata
var tenantJobs = await Job.Query(j =>
    j.Metadata.ContainsKey("TenantId") &&
    j.Metadata["TenantId"] == tenantId
);

// Streaming for large result sets
await foreach (var job in Job.QueryStream("status eq 'Running'", batchSize: 100))
{
    // Process jobs in batches
}
```

---

## Summary

`Koan.Jobs` provides a comprehensive, entity-first solution for long-running task management that:

- ✅ Aligns with Koan Framework's core principles (entity-first, provider transparency, auto-registration)
- ✅ Maintains clean separation of concerns (job = domain, policy = behavior, execution = audit)
- ✅ Leverages .NET standards (`IProgress<T>`, `Activity`, `CancellationToken`)
- ✅ Improves on industry patterns (Hangfire/Quartz) with better DX and observability
- ✅ Generalizes proven patterns from S13.DocMind with cleaner architecture
- ✅ Provides semantic, ergonomic API (`Start()`, `Wait()`, `OnProgress()`, `Refresh()`)
- ✅ Integrates seamlessly with existing Koan infrastructure (BackgroundServices, Entity events, OpenTelemetry)
- ✅ Supports extensible metadata for domain-specific tracking (TenantId, UserId, custom keys)
- ✅ Provides complete execution audit trail via `JobExecution` entity
- ✅ Flexible data source routing via `EntityContext` (dedicated DB, multi-tier archiving, per-tenant isolation)
- ✅ Automatic archival policies with configurable retention and multi-tier storage (hot/warm/cold)

**Key Architectural Wins**:
1. **Clean Separation**: Jobs are lightweight domain entities. Retry is policy. Execution history is separate. This enables jobs without retry concerns, complete audit trails, and clean extensibility.
2. **Flexible Storage**: EntityContext enables dedicated job databases, multi-tier archiving (hot/cold), and per-tenant isolation without changing code.
3. **Lifecycle Management**: Automatic archival policies move jobs through storage tiers based on age, status, and job type - optimizing costs while maintaining queryability.

**Zero-Config Production Safety**:
- Archival enabled by default with 30-day retention
- Prevents unbounded job table growth
- Conservative enough for debugging/investigation
- Can be disabled or customized via configuration

**Next Steps**: Review decision points and approve implementation roadmap for Phase 1 kickoff.

