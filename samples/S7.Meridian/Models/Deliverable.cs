using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class Deliverable : Entity<Deliverable>
{
    public string PipelineId { get; set; } = string.Empty;

    public string DeliverableTypeId { get; set; } = string.Empty;
    public int DeliverableTypeVersion { get; set; }
        = 1;

    public string? DataHash { get; set; }
        = null;
    public string? TemplateMdHash { get; set; }
        = null;

    public string DataJson { get; set; } = "{}";

    public string? RenderedMarkdown { get; set; }
        = null;
    public string? RenderedPdfKey { get; set; }
        = null;

    public List<DeliverableMergeDecision> MergeDecisions { get; set; }
        = new();
    public List<string> SourceDocumentIds { get; set; }
        = new();

    public int Version { get; set; }
        = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedAt { get; set; }
        = null;
    public string? FinalizedBy { get; set; }
        = null;
}

public sealed class DeliverableMergeDecision
{
    public string FieldPath { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string? Explanation { get; set; }
        = null;
    public string AcceptedExtractionId { get; set; } = string.Empty;
    public List<string> RejectedExtractionIds { get; set; }
        = new();
    public List<string> SupportingExtractionIds { get; set; }
        = new();
    public Dictionary<string, List<string>> CollectionProvenance { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
