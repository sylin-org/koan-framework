namespace S5.Recs.Models;

public sealed class Anime
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string[] Genres { get; init; } = [];
    public int? Episodes { get; init; }
    public string? Synopsis { get; init; }
    public double Popularity { get; init; }
}

public sealed class ContentPiece
{
    public required string Id { get; init; }
    public required string AnimeId { get; init; }
    public required string Type { get; init; } // synopsis|vibe
    public required string Text { get; init; }
    public bool HasSpoiler { get; init; }
}

public sealed class Recommendation
{
    public required Anime Anime { get; init; }
    public double Score { get; init; }
    public string[] Reasons { get; init; } = [];
}
