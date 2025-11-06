using System.Collections.Generic;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Response from POST /api/pipelines/create endpoint.
/// </summary>
public class CreatePipelineResponse
{
    /// <summary>ID of the created pipeline.</summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>Name of the created pipeline.</summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>Analysis type code (e.g., "EAR", "VDD") or empty if custom.</summary>
    public string? AnalysisType { get; set; }

    /// <summary>Analysis type name (seeded or custom).</summary>
    public string AnalysisTypeName { get; set; } = string.Empty;

    /// <summary>Whether this uses a custom (ephemeral) analysis type.</summary>
    public bool IsCustomAnalysis { get; set; }

    /// <summary>ID of the processing job created for document analysis.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>Current job status (typically "Pending" on creation).</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Document processing results.</summary>
    public List<DocumentCreationResult> Documents { get; set; } = new();

    /// <summary>Summary statistics about the operation.</summary>
    public PipelineCreationStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Result of processing a single document during pipeline creation.
/// </summary>
public class DocumentCreationResult
{
    /// <summary>ID of the created document entity.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Original filename from upload.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Assigned source type code (e.g., "MEET", "INV").</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Source type name for display.</summary>
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>Classification method used (Manual) or the placeholder "Deferred" until background processing completes.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Classification confidence (0.0-1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Whether this document was explicitly listed in the manifest.</summary>
    public bool InManifest { get; set; }
}

/// <summary>
/// Summary statistics about pipeline creation operation.
/// </summary>
public class PipelineCreationStatistics
{
    /// <summary>Total number of documents processed (excluding config file).</summary>
    public int TotalDocuments { get; set; }

    /// <summary>Number of documents explicitly specified in manifest.</summary>
    public int ManifestSpecified { get; set; }

    /// <summary>Number of documents that were auto-classified.</summary>
    public int AutoClassified { get; set; }
}
