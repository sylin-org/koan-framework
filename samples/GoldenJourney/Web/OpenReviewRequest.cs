using Koan.GoldenJourney.Domain;

namespace Koan.GoldenJourney.Web;

public sealed record OpenReviewRequest(string Title, ReviewImpact Impact, bool Urgent);
