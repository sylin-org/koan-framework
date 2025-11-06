using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Contracts;

public sealed record PipelineGraphResponse
{
    public PipelineGraph Graph { get; init; } = new();
}

public sealed record PipelineGraph
{
    public PipelineSummary Pipeline { get; init; } = new();
    public IReadOnlyList<DocumentSummary> Documents { get; init; } = Array.Empty<DocumentSummary>();
    public DeliverableSnapshot? Deliverable { get; init; }
        = null;
    public JToken? Canonical { get; init; }
        = null;
    public PipelineNotesSnapshot Notes { get; init; } = new();
    public PipelineQualitySummary? Quality { get; init; }
        = null;
    public IReadOnlyList<JobSnapshot> Jobs { get; init; } = Array.Empty<JobSnapshot>();
    public IReadOnlyList<RunLogSnapshot> Runs { get; init; } = Array.Empty<RunLogSnapshot>();
}

public sealed record PipelineSummary
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
        = null;
    public string DeliverableTypeId { get; init; } = string.Empty;
    public int DeliverableTypeVersion { get; init; }
        = 1;
    public string AnalysisTypeId { get; init; } = string.Empty;
    public int AnalysisTypeVersion { get; init; }
        = 1;
    public string AnalysisTypeName { get; init; } = string.Empty;
    public IReadOnlyList<string> AnalysisTags { get; init; } = Array.Empty<string>();
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> DocumentIds { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; init; }
        = null;
    public int DocumentCount { get; init; }
        = 0;
}

public sealed record DocumentSummary
{
    public string Id { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string? ClassifiedTypeId { get; init; }
        = null;
    public int? ClassifiedTypeVersion { get; init; }
        = null;
    public double ClassificationConfidence { get; init; }
        = 0.0;
    public string Status { get; init; } = string.Empty;
    public bool IsVirtual { get; init; }
        = false;
    public int PageCount { get; init; }
        = 0;
    public long Size { get; init; }
        = 0;
    public DateTime UploadedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record DeliverableSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string DeliverableTypeId { get; init; } = string.Empty;
    public int DeliverableTypeVersion { get; init; }
        = 1;
    public string? DataHash { get; init; }
        = null;
    public string? TemplateMdHash { get; init; }
        = null;
    public int Version { get; init; }
        = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinalizedAt { get; init; }
        = null;
    public string? FinalizedBy { get; init; }
        = null;
    public string? RenderedMarkdown { get; init; }
        = null;
    public string? RenderedPdfKey { get; init; }
        = null;
    public IReadOnlyList<string> SourceDocumentIds { get; init; } = Array.Empty<string>();
}

public sealed record PipelineNotesSnapshot
{
    public string? AuthoritativeNotes { get; init; }
        = null;
    public DateTime? UpdatedAt { get; init; }
        = null;
}

public sealed record PipelineQualitySummary
{
    public double CitationCoverage { get; init; }
        = 0.0;
    public int HighConfidence { get; init; }
        = 0;
    public int MediumConfidence { get; init; }
        = 0;
    public int LowConfidence { get; init; }
        = 0;
    public int TotalConflicts { get; init; }
        = 0;
    public int AutoResolved { get; init; }
        = 0;
    public int ManualReviewNeeded { get; init; }
        = 0;
    public int NotesSourced { get; init; }
        = 0;
}

public sealed record JobSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ProgressPercent { get; init; }
        = 0;
    public int TotalDocuments { get; init; }
        = 0;
    public int ProcessedDocuments { get; init; }
        = 0;
    public string? LastDocumentId { get; init; }
        = null;
    public string? LastError { get; init; }
        = null;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; init; }
        = null;
    public DateTime? CompletedAt { get; init; }
        = null;
}

public sealed record RunLogSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? DocumentId { get; init; }
        = null;
    public string? FieldPath { get; init; }
        = null;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; init; }
        = null;
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
