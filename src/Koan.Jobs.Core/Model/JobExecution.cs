using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Jobs.Model;

public class JobExecution : Entity<JobExecution>
{
    [Required]
    [Parent(typeof(Job))]
    [Index]
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

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metrics { get; set; } = new();
}
