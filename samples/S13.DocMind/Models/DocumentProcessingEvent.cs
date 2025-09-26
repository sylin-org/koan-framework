using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Timeline entry capturing document pipeline progress.
/// </summary>
public sealed class DocumentProcessingEvent : Entity<DocumentProcessingEvent>
{
    public string DocumentId { get; set; } = string.Empty;
    public DocumentProcessingStage Stage { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DocumentProcessingStatus Status { get; set; }
}

public enum DocumentProcessingStage
{
    Upload = 0,
    Deduplicate = 1,
    Extraction = 2,
    Chunking = 3,
    Insight = 4,
    Suggestion = 5,
    Completion = 6,
    Error = 7
}
