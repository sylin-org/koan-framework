namespace S5.Recs.Models;

public sealed class ContentPiece
{
    public required string Id { get; init; }
    public required string AnimeId { get; init; }
    public required string Type { get; init; } // synopsis|vibe
    public required string Text { get; init; }
    public bool HasSpoiler { get; init; }
}