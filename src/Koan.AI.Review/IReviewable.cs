namespace Koan.AI.Review;

/// <summary>
/// Lifecycle status of a reviewable entity.
/// </summary>
public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected,
    Edited,
    Flagged
}

/// <summary>
/// Marks an entity as eligible for human-in-the-loop review.
/// Implement on any entity that should flow through a review queue.
/// </summary>
public interface IReviewable
{
    ReviewStatus ReviewStatus { get; }
    string? ReviewedBy { get; }
    DateTime? ReviewedAt { get; }
    string? RejectionReason { get; }
}
