using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// Tracks batch photo processing jobs with progress and errors
/// </summary>
public class ProcessingJob : Entity<ProcessingJob>
{
    public string EventId { get; set; } = "";
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    public int TotalPhotos { get; set; }
    public int ProcessedPhotos { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<string> Errors { get; set; } = new();
    public ProcessingPhase CurrentPhase { get; set; } = ProcessingPhase.Upload;
}

public enum ProcessingPhase
{
    Upload,
    ExifExtraction,
    ImageResize,
    AIAnalysis,
    VectorGeneration,
    Completed
}
