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
    public ConfidenceOptions Confidence { get; set; } = new();
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
}

public sealed class MergeOptions
{
    /// <summary>Enable merge conflict UI explainability (default: true).</summary>
    public bool EnableExplainability { get; set; } = true;

    /// <summary>Enable citation footnotes in rendered markdown (default: true).</summary>
    public bool EnableCitations { get; set; } = true;

    /// <summary>Enable normalized value comparison for approval preservation (default: true).</summary>
    public bool EnableNormalizedComparison { get; set; } = true;
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
