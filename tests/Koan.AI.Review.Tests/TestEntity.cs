using Koan.AI.Review;

namespace Koan.AI.Review.Tests;

public class TestEntity : IReviewable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Rating { get; set; }
    public string? Content { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
