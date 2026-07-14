namespace Koan.GoldenJourney.Domain;

public sealed record RecommendationOutcome(
    bool Accepted,
    string Code,
    string Message,
    string RequestId);
