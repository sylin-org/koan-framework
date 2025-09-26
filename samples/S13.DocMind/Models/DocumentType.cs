using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// DocumentType entity representing AI analysis templates with vector embeddings for matching
/// Used for semantic document type detection and structured data extraction
/// </summary>
public class DocumentType : Entity<DocumentType>
{
    // Basic metadata
    public string Name { get; set; } = "";
    public string Code { get; set; } = ""; // e.g., MEETING, TECH_SPEC, FEATURE
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";

    // Status and lifecycle
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }

    // AI analysis template configuration
    public string AnalysisPrompt { get; set; } = "";
    public Dictionary<string, object> ExtractionSchema { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public List<string> OptionalFields { get; set; } = new();

    // Vector similarity matching (managed by Weaviate)
    public string? EmbeddingId { get; set; }
    public List<string> KeywordTriggers { get; set; } = new();
    public List<string> SampleTexts { get; set; } = new();

    // Usage statistics
    public int FileCount { get; set; } = 0;
    public double AverageConfidence { get; set; } = 0.0;
    public DateTime? LastUsed { get; set; }

    // Configuration options
    public Dictionary<string, object> ModelSettings { get; set; } = new();
    public double MinConfidenceThreshold { get; set; } = 0.7;
}