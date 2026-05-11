using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using TimestampAttribute = Koan.Data.Abstractions.Annotations.TimestampAttribute;
using Koan.Data.Core.Model;

namespace Koan.Jobs.Model;

public abstract partial class Job : Entity<Job>
{
    [Index]
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [Index]
    [MaxLength(64)]
    public string? ParentJobId { get; set; }

    [Required]
    public JobStatus Status { get; set; } = JobStatus.Created;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>
    /// Stable type identifier — <see cref="Type.FullName"/> of the concrete <c>Job&lt;,,&gt;</c>
    /// subtype. Set by <see cref="Execution.JobCoordinator"/> at creation time and persisted so
    /// type-based dependency lookups (<see cref="WaitForTypeNames"/>) work consistently across
    /// in-memory and Entity-backed storage modes. See ADR-0017.
    /// </summary>
    [Index]
    [MaxLength(500)]
    public string? TypeName { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }

    [Range(0.0, 1.0)]
    public double Progress { get; set; }

    [MaxLength(500)]
    public string? ProgressMessage { get; set; }

    public int? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }

    public DateTimeOffset? EstimatedCompletion { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ResultJson { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ContextJson { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    /// <summary>
    /// Specific job ids this job won't start until each reaches a terminal state
    /// (<see cref="JobStatus.Completed"/>, <see cref="JobStatus.Failed"/>, or
    /// <see cref="JobStatus.Cancelled"/>). A dependency ending in <see cref="JobStatus.Failed"/>
    /// or <see cref="JobStatus.Cancelled"/> poisons this job — it transitions to
    /// <see cref="JobStatus.Failed"/> with <see cref="LastError"/> set to the dependency
    /// reference. Populated via <see cref="Execution.JobRunBuilder{TJob,TContext,TResult}.WaitFor(string[])"/>.
    /// See ADR-0017.
    /// </summary>
    public List<string> WaitForJobIds { get; set; } = new();

    /// <summary>
    /// Type names (<see cref="Type.FullName"/>) of jobs whose Completed-status existence
    /// gates this one. The check is "at least one Completed job of EACH listed type exists"
    /// — a single successful run satisfies the type-based dependency permanently for this
    /// job. Populated via <see cref="Execution.JobRunBuilder{TJob,TContext,TResult}.WaitFor(System.Type[])"/>.
    /// See ADR-0017.
    /// </summary>
    public List<string> WaitForTypeNames { get; set; } = new();

    [Timestamp(OnSave = true)]
    public DateTimeOffset LastModified { get; set; }
}
