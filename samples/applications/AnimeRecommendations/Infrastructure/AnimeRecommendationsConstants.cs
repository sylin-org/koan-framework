namespace AnimeRecommendations.Infrastructure;

internal static class AnimeRecommendationsConstants
{
    internal static class Routes
    {
        public const string Catalog = "api/anime/catalog";
        public const string Viewers = "api/anime/viewers";
        public const string Library = "api/anime/library";
        public const string Recommendations = "api/anime/recommendations";
        public const string ViewerRatings = "api/anime/viewers/{viewerId}/ratings";
    }

    internal static class Limits
    {
        public const int DefaultRecommendations = 12;
        public const int MaximumRecommendations = 20;
        public const int MaximumTasteRatings = 32;
        public const int MaximumCandidates = 60;
        public const int MaximumMoodLength = 200;
        public const int MinimumRating = 1;
        public const int MaximumRating = 5;
    }
}
