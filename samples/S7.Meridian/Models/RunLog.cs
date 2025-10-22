using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class RunLog : Entity<RunLog>
{
    public string PipelineId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
        = null;
    public string? FieldPath { get; set; }
        = null;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
        = null;
    public TimeSpan? Duration =>
        FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;

    public string Status { get; set; } = "success";
    public string? ModelId { get; set; }
        = null;
    public string? PromptHash { get; set; }
        = null;
    public int? TokensUsed { get; set; }
        = null;

    public int? TopK { get; set; }
        = null;
    public double? Alpha { get; set; }
        = null;
    public List<string> PassageIds { get; set; }
        = new();

    public string? ErrorMessage { get; set; }
        = null;

    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
