using Koan.AI.Contracts.Models;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Primary document aggregate tracked throughout the DocMind pipeline.
/// </summary>
[McpEntity(Name = "source-documents", Description = "Uploaded documents pending or completing DocMind analysis.")]
public sealed class SourceDocument : Entity<SourceDocument>
{
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Length { get; set; }
    public StorageLocation Storage { get; set; } = new();
    public DocumentProcessingStatus Status { get; set; } = DocumentProcessingStatus.Uploaded;
    public DocumentSummary Summary { get; set; } = new();
    public string? AssignedProfileId { get; set; }
    public bool AssignedBySystem { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Hash { get; set; }
    public List<string> ChunkIds { get; set; } = new();
    public List<DocumentProfileSuggestion> Suggestions { get; set; } = new();
    public DocumentProcessingError? LastError { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void MarkStatus(DocumentProcessingStatus status, string? reason = null)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
        if (status == DocumentProcessingStatus.Completed)
        {
            CompletedAt = UpdatedAt;
        }

        if (reason is not null)
        {
            LastError = new DocumentProcessingError
            {
                Message = reason,
                Stage = status.ToString(),
                OccurredAt = UpdatedAt.Value
            };
        }
    }
}

public enum DocumentProcessingStatus
{
    Uploaded = 0,
    Deduplicated = 1,
    Extracting = 2,
    Extracted = 3,
    Analyzing = 4,
    Completed = 5,
    Failed = 6
}

public sealed class StorageLocation
{
    public string Provider { get; set; } = "local";
    public string Path { get; set; } = string.Empty;
    public string? Bucket { get; set; }
    public string? Hash { get; set; }
    public long? Size { get; set; }
}

public sealed class DocumentSummary
{
    public bool TextExtracted { get; set; }
    public int WordCount { get; set; }
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public string? PrimaryFindings { get; set; }
    public string? Excerpt { get; set; }
}

public sealed class DocumentProfileSuggestion
{
    public string ProfileId { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool AutoAccepted { get; set; }
    public DateTimeOffset SuggestedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DocumentProcessingError
{
    public string Message { get; set; } = string.Empty;
    public string? Stage { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
