using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class SourceDocument : Entity<SourceDocument>
{
    public string PipelineId { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? MediaType { get; set; }
        = null;
    public long Size { get; set; }
        = 0;

    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Pending;

    public double ExtractionConfidence { get; set; }
        = 0.0;
    public DateTime? ExtractedAt { get; set; }
        = null;

    public string TextHash { get; set; } = string.Empty;
    public int PageCount { get; set; }
        = 0;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum DocumentProcessingStatus
{
    Pending,
    Extracted,
    Indexed,
    Failed
}
