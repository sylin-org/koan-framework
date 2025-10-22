using System;
using System.Collections.Generic;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Externalized configuration for Meridian pipeline processing.
/// Binds from appsettings.json: Meridian:Retrieval, Meridian:Extraction, etc.
/// </summary>
public sealed class MeridianOptions
{
    public RetrievalOptions Retrieval { get; set; } = new();
    public ExtractionOptions Extraction { get; set; } = new();
    public MergeOptions Merge { get; set; } = new();
    public ClassificationOptions Classification { get; set; } = new();
    public ConfidenceOptions Confidence { get; set; } = new();
    public RenderingOptions Rendering { get; set; } = new();
}

public sealed class RetrievalOptions
{
    /// <summary>Number of passages to retrieve from vector search (default: 12).</summary>
    public int TopK { get; set; } = 12;

    /// <summary>Hybrid search balance: 0.0 = pure keyword (BM25), 1.0 = pure semantic (default: 0.5).</summary>
    public double Alpha { get; set; } = 0.5;

    /// <summary>MMR diversity parameter: higher = more diverse passages (default: 0.7).</summary>
    public double MmrLambda { get; set; } = 0.7;

    /// <summary>Maximum tokens to send to LLM per field extraction (default: 2000).</summary>
    public int MaxTokensPerField { get; set; } = 2000;
}

public sealed class ExtractionOptions
{
    /// <summary>Parallel field extraction concurrency (0 = auto-detect cores, default: 0).</summary>
    public int ParallelismDegree { get; set; } = 0;

    /// <summary>AI model to use for extraction (null = use default from Koan.AI config).</summary>
    public string? Model { get; set; }

    /// <summary>Temperature for extraction prompts (default: 0.3 for determinism).</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>Maximum output tokens for extraction response (default: 500).</summary>
    public int MaxOutputTokens { get; set; } = 500;

    /// <summary>OCR configuration for scanned documents.</summary>
    public OcrOptions Ocr { get; set; } = new();
}

public sealed class OcrOptions
{
    /// <summary>Enable OCR fallback when native text extraction is low confidence.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of the Tesseract OCR container.</summary>
    public string BaseUrl { get; set; } = "http://meridian-tesseract:8884/";

    /// <summary>Relative endpoint to invoke for OCR extraction.</summary>
    public string Endpoint { get; set; } = "tesseract";

    /// <summary>Request timeout (seconds) when calling the OCR container.</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Minimum confidence value returned by OCR before accepting the result.</summary>
    public double ConfidenceFloor { get; set; } = 0.55;
}

public sealed class MergeOptions
{
    /// <summary>Enable merge conflict UI explainability (default: true).</summary>
    public bool EnableExplainability { get; set; } = true;

    /// <summary>Enable citation footnotes in rendered markdown (default: true).</summary>
    public bool EnableCitations { get; set; } = true;

    /// <summary>Enable normalized value comparison for approval preservation (default: true).</summary>
    public bool EnableNormalizedComparison { get; set; } = true;

    /// <summary>Default precedence order for source types (e.g., financials over vendor PDFs).</summary>
    public List<string> DefaultSourcePrecedence { get; set; } = new();

    /// <summary>Field-specific merge policies keyed by JSON path (e.g., $.revenue).</summary>
    public Dictionary<string, MergePolicyOptions> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MergePolicyOptions
{
    /// <summary>Name of the merge strategy (highestConfidence, sourcePrecedence, latest, consensus, collection).</summary>
    public string Strategy { get; set; } = "highestConfidence";

    /// <summary>Optional precedence list overriding the default order.</summary>
    public List<string>? SourcePrecedence { get; set; }

    /// <summary>Path to a companion date field for latest-by strategies.</summary>
    public string? LatestByFieldPath { get; set; }

    /// <summary>Minimum unique sources required for consensus selection.</summary>
    public int? ConsensusMinimumSources { get; set; }

    /// <summary>Transform to apply to the accepted value (normalizeToUsd, round2, etc.).</summary>
    public string? Transform { get; set; }

    /// <summary>Strategy for collection merges (union, intersection, concat).</summary>
    public string? CollectionStrategy { get; set; }
}

public sealed class ClassificationOptions
{
    /// <summary>Confidence threshold required to accept heuristic classification.</summary>
    public double HeuristicConfidenceThreshold { get; set; } = 0.9;

    /// <summary>Confidence threshold required to accept vector classification.</summary>
    public double VectorConfidenceThreshold { get; set; } = 0.75;

    /// <summary>Maximum characters of the document text to embed for classification.</summary>
    public int VectorPreviewLength { get; set; } = 1000;

    /// <summary>Model identifier to use for LLM classification fallback (null = default AI model).</summary>
    public string? LlmModel { get; set; }

    /// <summary>Classification types available to the cascade.</summary>
    public List<SourceTypeOptions> Types { get; set; } = new();
}

public sealed class SourceTypeOptions
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public List<string> Tags { get; set; } = new();
    public List<string> DescriptorHints { get; set; } = new();
    public List<string> SignalPhrases { get; set; } = new();
    public bool SupportsManualSelection { get; set; } = true;
    public int? ExpectedPageCountMin { get; set; }
    public int? ExpectedPageCountMax { get; set; }
    public List<string> MimeTypes { get; set; } = new();
    public Dictionary<string, string> FieldQueries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Instructions { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = string.Empty;
}

public sealed class ConfidenceOptions
{
    /// <summary>Low confidence threshold (default: 0.7).</summary>
    public double LowThreshold { get; set; } = 0.7;

    /// <summary>High confidence threshold (default: 0.9).</summary>
    public double HighThreshold { get; set; } = 0.9;

    public string GetBand(double confidence)
    {
        if (confidence >= HighThreshold) return "High";
        if (confidence >= LowThreshold) return "Medium";
        return "Low";
    }

    public string GetBadgeColor(double confidence)
    {
        if (confidence >= HighThreshold) return "green";
        if (confidence >= LowThreshold) return "yellow";
        return "red";
    }
}

public sealed class RenderingOptions
{
    public PandocOptions Pandoc { get; set; } = new();
}

public sealed class PandocOptions
{
    /// <summary>Enable Pandoc rendering. Disable to skip PDF generation.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of the Pandoc rendering container.</summary>
    public string BaseUrl { get; set; } = "http://meridian-pandoc:7070/";

    /// <summary>Endpoint path for rendering requests.</summary>
    public string Endpoint { get; set; } = "render";

    /// <summary>Request timeout (seconds) when calling the Pandoc renderer.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Whether to strip dangerous LaTeX commands before rendering.</summary>
    public bool SanitizeLatex { get; set; } = true;
}
