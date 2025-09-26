using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// File entity representing uploaded documents with processing state tracking
/// Processing Pipeline: Upload → Extract Text → User Assigns Type → Background AI Analysis → Completed
/// </summary>
public class File : Entity<File>
{
    // Basic file metadata
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string FilePath { get; set; } = "";

    // Processing state tracking
    public string Status { get; set; } = "uploaded"; // uploaded, extracting, extracted, assigned, analyzing, completed, failed
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExtractedDate { get; set; }
    public DateTime? AssignedDate { get; set; }
    public DateTime? CompletedDate { get; set; }

    // Extracted content
    public string? ExtractedText { get; set; }
    public int? PageCount { get; set; }

    // Document type assignment
    public string? DocumentTypeId { get; set; }
    public double? TypeConfidence { get; set; }

    // Analysis results
    public string? AnalysisId { get; set; }

    // Error handling
    public string? ErrorMessage { get; set; }
    public DateTime? LastErrorDate { get; set; }

    // Processing metadata
    public Dictionary<string, object> Metadata { get; set; } = new();
}