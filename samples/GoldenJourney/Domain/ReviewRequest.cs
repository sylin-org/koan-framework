using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.GoldenJourney.Infrastructure;
using Koan.Jobs;

namespace Koan.GoldenJourney.Domain;

[DataAdapter("sqlite")]
public sealed class ReviewRequest : Entity<ReviewRequest>, IKoanJob<ReviewRequest>
{
    public string Title { get; set; } = "";
    public ReviewImpact Impact { get; set; }
    public bool Urgent { get; set; }
    public ReviewPriority Priority { get; set; } = ReviewPriority.Unassessed;
    public ReviewDisposition? Recommendation { get; set; }
    public string? RecommendationRationale { get; set; }
    public string? RecommendedBy { get; set; }

    public static ReviewRequest Open(string title, ReviewImpact impact, bool urgent)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A review request needs a title.", nameof(title));

        return new ReviewRequest
        {
            Title = title.Trim(),
            Impact = impact,
            Urgent = urgent
        };
    }

    public void Assess()
    {
        if (Priority != ReviewPriority.Unassessed) return;

        Priority = (Impact, Urgent) switch
        {
            (ReviewImpact.High, _) or (_, true) => ReviewPriority.Critical,
            (ReviewImpact.Medium, false) => ReviewPriority.Expedited,
            _ => ReviewPriority.Standard
        };
    }

    public RecommendationOutcome Recommend(
        ReviewDisposition disposition,
        string rationale,
        string recommendedBy)
    {
        if (Priority == ReviewPriority.Unassessed)
            return Rejected(GoldenJourneyConstants.Outcomes.NotReady, "Assess the request before recommending a disposition.");
        if (Recommendation is not null)
            return Rejected(GoldenJourneyConstants.Outcomes.AlreadyRecommended, "This request already has a recommendation.");
        if (string.IsNullOrWhiteSpace(rationale))
            return Rejected(GoldenJourneyConstants.Outcomes.RationaleRequired, "A recommendation needs a rationale.");

        Recommendation = disposition;
        RecommendationRationale = rationale.Trim();
        RecommendedBy = recommendedBy;
        return new RecommendationOutcome(
            true,
            GoldenJourneyConstants.Outcomes.Accepted,
            "Recommendation recorded; a human still owns the final decision.",
            Id);
    }

    public static async Task Execute(ReviewRequest request, JobContext context, CancellationToken ct)
    {
        request.Assess();
        await context.Progress(1, "Ready for recommendation");
    }

    private RecommendationOutcome Rejected(string code, string message) =>
        new(false, code, message, Id);
}
