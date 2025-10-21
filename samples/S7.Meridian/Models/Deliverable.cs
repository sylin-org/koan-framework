using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class Deliverable : Entity<Deliverable>
{
    public string PipelineId { get; set; } = string.Empty;
    public string VersionTag { get; set; } = string.Empty;

    public string Markdown { get; set; } = string.Empty;
    public string? PdfStorageKey { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
