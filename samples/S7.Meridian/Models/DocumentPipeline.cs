using System.Collections.Generic;
using Koan.Data.Core.Model;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Models;

public sealed class DocumentPipeline : Entity<DocumentPipeline>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
        = "Evidence-backed narrative pipeline";

    /// <summary>Identifier of the deliverable type backing this pipeline.</summary>
    public string DeliverableTypeId { get; set; } = string.Empty;

    /// <summary>Version of the deliverable type snapshot applied to this pipeline.</summary>
    public int DeliverableTypeVersion { get; set; }
        = 1;

    /// <summary>Track source type versions pinned for this pipeline.</summary>
    public Dictionary<string, int> SourceTypeVersions { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>JSON schema describing the target deliverable.</summary>
    public string SchemaJson { get; set; } = "{}";

    /// <summary>Markdown template rendered with the merged field payload.</summary>
    public string TemplateMarkdown { get; set; } = "# Meridian Deliverable\n";

    /// <summary>Identifier of the AnalysisType backing this pipeline.</summary>
    public string AnalysisTypeId { get; set; } = string.Empty;

    /// <summary>Version of the AnalysisType applied to this pipeline.</summary>
    public int AnalysisTypeVersion { get; set; } = 1;

    /// <summary>Analysis-level instructions appended to extraction prompts.</summary>
    public string AnalysisInstructions { get; set; } = string.Empty;

    /// <summary>Tags inherited from the AnalysisType (useful for filtering and telemetry).</summary>
    public List<string> AnalysisTags { get; set; } = new();

    /// <summary>Source types required for this analysis. Empty list means no restriction.</summary>
    public List<string> RequiredSourceTypes { get; set; } = new();

    /// <summary>Optional operator guidance that biases retrieval, not final values.</summary>
    public string? BiasNotes { get; set; }
        = null;

    /// <summary>Current pipeline status for orchestrators and dashboards.</summary>
    public PipelineStatus Status { get; set; }
        = PipelineStatus.Pending;

    public int TotalDocuments { get; set; }
        = 0;
    public int ProcessedDocuments { get; set; }
        = 0;

    public string? DeliverableId { get; set; }
        = null;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
        = null;

    public PipelineQualityMetrics Quality { get; set; } = new();

    public JSchema? TryParseSchema()
    {
        if (string.IsNullOrWhiteSpace(SchemaJson))
        {
            return null;
        }

        try
        {
            return JSchema.Parse(SchemaJson);
        }
        catch (JSchemaException)
        {
            return null;
        }
    }
}

public enum PipelineStatus
{
    Pending,
    Queued,
    Processing,
    ReviewNeeded,
    Completed,
    Failed
}

public sealed class PipelineQualityMetrics
{
    public double CitationCoverage { get; set; }
        = 0.0;

    public int HighConfidence { get; set; }
        = 0;

    public int MediumConfidence { get; set; }
        = 0;

    public int LowConfidence { get; set; }
        = 0;

    public int TotalConflicts { get; set; }
        = 0;

    public int AutoResolved { get; set; }
        = 0;

    public int ManualReviewNeeded { get; set; }
        = 0;

    public TimeSpan ExtractionP95 { get; set; }
        = TimeSpan.Zero;

    public TimeSpan MergeP95 { get; set; }
        = TimeSpan.Zero;
}
