using System.Linq.Expressions;

namespace Koan.AI.Review;

/// <summary>
/// Defines a review queue: which entities to review, which fields to display, and what actions are available.
/// </summary>
public sealed record ReviewQueue<T> where T : IReviewable
{
    public required string Name { get; init; }
    public required Type EntityType { get; init; }
    public required Expression<Func<T, bool>> Filter { get; init; }
    public required Expression<Func<T, object>> DisplayFields { get; init; }
    public required IReadOnlyList<ReviewAction<T>> Actions { get; init; }
}
