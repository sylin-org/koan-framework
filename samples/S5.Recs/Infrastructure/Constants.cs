namespace S5.Recs.Infrastructure;

internal static class Constants
{
    internal static class Routes
    {
        public const string Admin = "admin";
        public const string Recs = "api/recs";
    }

    internal static class Paths
    {
        public const string SeedCache = "data/seed-cache";
        public const string OfflineData = "data/offline/anime.json";
        public const string Manifest = "data/seed-cache/manifest.json";
    }

    internal static class Scoring
    {
        public const double VectorWeight = 0.6;
        public const double PopularityWeight = 0.3;
        public const double GenreWeight = 0.2;
        public const double SpoilerPenalty = 0.1; // multiplicative reduction when flagged
        public const double ProfileBlend = 0.7;    // weight for explicit query vs. profile vector
        public const double PopularityHotThreshold = 0.85;
    }

    internal static class Spoilers
    {
        public static readonly string[] Keywords = new[] { "season", "finale", "episode", "death" };
    }
}
