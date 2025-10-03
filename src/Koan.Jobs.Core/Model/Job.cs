using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
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
    public string Name { get; set; } = string.Empty;

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

    [MaxLength(128)]
    public string? Source { get; set; }

    [MaxLength(128)]
    public string? Partition { get; set; }

    public JobStorageMode StorageMode { get; set; } = JobStorageMode.InMemory;

    public bool AuditTrailEnabled { get; set; }

    public bool CancellationRequested { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
