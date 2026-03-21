using Koan.AI.Review;
using Koan.Data.Core.Model;

namespace S18.Prism.Models;

public class ResearchFinding : Entity<ResearchFinding>, IReviewable
{
    public string BriefId { get; set; } = "";
    public string SpaceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Url { get; set; }
    public string? SourceName { get; set; }
    public double RelevanceScore { get; set; }
    public string? WhyRelevant { get; set; }
    public DateTime? PublishedAt { get; set; }
    public FindingStatus Status { get; set; } = FindingStatus.PendingReview;

    // IReviewable
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public enum FindingStatus
{
    PendingReview,
    Approved,
    Dismissed,
    AutoIngested
}
