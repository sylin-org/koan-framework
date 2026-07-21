namespace Koan.GoldenJourney.Infrastructure;

public static class GoldenJourneyConstants
{
    public static class Routes
    {
        public const string Reviews = "api/reviews";
    }

    public static class Agent
    {
        public const string PendingTool = "review_pending";
        public const string RecommendTool = "review_recommend";
        public const int DefaultListSize = 20;
        public const int MaximumListSize = 50;
    }

    public static class Outcomes
    {
        public const string Accepted = "review.recommendation-recorded";
        public const string Missing = "review.not-found";
        public const string NotReady = "review.not-ready";
        public const string AlreadyRecommended = "review.already-recommended";
        public const string RationaleRequired = "review.rationale-required";
        public const string InvalidTitle = "review.title-required";
    }
}
