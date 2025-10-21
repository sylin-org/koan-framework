using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class RunLog : Entity<RunLog>
{
    public string PipelineId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string? FieldPath { get; set; }
        = null;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "success";

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
