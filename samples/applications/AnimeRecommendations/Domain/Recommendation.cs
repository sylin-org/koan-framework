namespace AnimeRecommendations.Domain;

public sealed record Recommendation(Anime Anime, double Score, string Reason);

public sealed record RecommendationFeed(
    string ViewerId,
    string Intent,
    string[] TasteAnchors,
    IReadOnlyList<Recommendation> Items);
