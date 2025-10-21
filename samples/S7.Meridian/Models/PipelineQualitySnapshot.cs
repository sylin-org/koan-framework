using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class PipelineQualitySnapshot : Entity<PipelineQualitySnapshot>
{
    public string PipelineId { get; set; } = string.Empty;
    public string VersionTag { get; set; } = string.Empty;

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
