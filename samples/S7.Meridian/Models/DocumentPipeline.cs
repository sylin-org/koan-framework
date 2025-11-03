using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public string SchemaJson { get; set; } = string.Empty;

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

    /// <summary>Optional operator guidance that biases retrieval, not final values.</summary>
    public string? BiasNotes { get; set; }
        = null;

    /// <summary>List of document identifiers attached to this pipeline.</summary>
    public List<string> DocumentIds { get; set; } = new();

    /// <summary>
    /// Authoritative notes containing user-provided data that MUST override any
    /// information extracted from documents. AI will interpret free-text format
    /// and map to field names using fuzzy matching.
    /// </summary>
    public string? AuthoritativeNotes { get; set; }
        = null;

    /// <summary>Current pipeline status for orchestrators and dashboards.</summary>
    public PipelineStatus Status { get; set; }
        = PipelineStatus.Pending;

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

    public void AttachDocument(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return;
        }

        if (!DocumentIds.Any(existing => string.Equals(existing, documentId, StringComparison.Ordinal)))
        {
            DocumentIds.Add(documentId);
        }
    }

    public void AttachDocuments(IEnumerable<string>? documentIds)
    {
        if (documentIds is null)
        {
            return;
        }

        foreach (var id in documentIds)
        {
            AttachDocument(id);
        }
    }

    public void RemoveDocument(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return;
        }

        DocumentIds.RemoveAll(id => string.Equals(id, documentId, StringComparison.Ordinal));
    }

    public async Task<List<SourceDocument>> LoadDocumentsAsync(CancellationToken ct = default)
    {
        if (DocumentIds.Count == 0)
        {
            return new List<SourceDocument>();
        }

        var ids = DocumentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new List<SourceDocument>();
        }

        var loaded = await SourceDocument.Get(ids, ct);
        return loaded
            .Where(doc => doc is not null)
            .Select(doc => doc!)
            .ToList();
    }

    public async Task<List<Passage>> LoadPassagesAsync(CancellationToken ct = default)
    {
        if (DocumentIds.Count == 0)
        {
            return new List<Passage>();
        }

        var ids = DocumentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new List<Passage>();
        }

        var passages = await Passage.Query(p => ids.Contains(p.SourceDocumentId), ct);
        return passages.ToList();
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

    /// <summary>Number of fields sourced from Authoritative Notes (user override).</summary>
    public int NotesSourced { get; set; }
        = 0;

    public TimeSpan ExtractionP95 { get; set; }
        = TimeSpan.Zero;

    public TimeSpan MergeP95 { get; set; }
        = TimeSpan.Zero;
}
