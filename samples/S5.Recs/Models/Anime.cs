namespace S5.Recs.Models;

public sealed class Anime
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string[] Genres { get; init; } = [];
    public int? Episodes { get; init; }
    public string? Synopsis { get; init; }
    public double Popularity { get; init; }
    // Enriched metadata (optional)
    public string? CoverUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? CoverColorHex { get; init; }
    public string[] Tags { get; init; } = [];
    public string? TitleEnglish { get; init; }
    public string? TitleRomaji { get; init; }
    public string? TitleNative { get; init; }
    public string[] Synonyms { get; init; } = [];
}