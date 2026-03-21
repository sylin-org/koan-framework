using Koan.AI.Review;

namespace Koan.AI.Integration.Tests.Fixtures;

/// <summary>
/// A concrete IReviewable entity for testing review action handlers.
/// </summary>
internal sealed class TestReviewableEntity : IReviewable
{
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? OriginalContent { get; set; }
    public int Rating { get; set; }
    public List<string> Flags { get; set; } = [];
}
