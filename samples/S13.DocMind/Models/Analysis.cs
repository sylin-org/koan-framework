using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Analysis entity representing AI-generated results with confidence scoring and structured data
/// Generated after document type assignment through background processing
/// </summary>
public class Analysis : Entity<Analysis>
{
    // Source document references
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DocumentTypeId { get; set; } = "";
    public string DocumentTypeName { get; set; } = "";

    // Analysis metadata
    public string Status { get; set; } = "processing"; // processing, completed, failed
    public DateTime StartedDate { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedDate { get; set; }
    public double? ProcessingTimeMs { get; set; }

    // AI model information
    public string ModelUsed { get; set; } = "";
    public string ModelProvider { get; set; } = ""; // ollama, openai, etc.
    public string ModelVersion { get; set; } = "";

    // Analysis results
    public double OverallConfidence { get; set; } = 0.0;
    public Dictionary<string, object> ExtractedData { get; set; } = new();
    public Dictionary<string, double> FieldConfidences { get; set; } = new(); // per-field confidence scores

    // Token usage tracking
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostEstimate { get; set; }

    // Quality metrics
    public bool IsHighQuality => OverallConfidence >= 0.8;
    public bool RequiresReview => OverallConfidence < 0.6;
    public List<string> ValidationFlags { get; set; } = new();

    // Error handling
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public int RetryCount { get; set; } = 0;

    // Raw AI response (for debugging/reprocessing)
    public string? RawAIResponse { get; set; }
    public Dictionary<string, object> ProcessingMetadata { get; set; } = new();
}