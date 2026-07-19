using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace AnimeRecommendations.Domain;

/// <summary>
/// An anime available for discovery. Saving it also indexes the title, synopsis, genres, and themes because those
/// fields describe what the story feels like.
/// </summary>
[Embedding(
    Properties = new[] { nameof(Title), nameof(Synopsis), nameof(Genres), nameof(Themes) },
    Model = "all-MiniLM-L6-v2")]
public sealed class Anime : Entity<Anime>
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public int Year { get; set; }
    public string Format { get; set; } = "Series";
    public int? Episodes { get; set; }
    public string Synopsis { get; set; } = "";
    public string[] Genres { get; set; } = [];
    public string[] Themes { get; set; } = [];
    public double CommunityScore { get; set; }
    public string Accent { get; set; } = "#6d5dfc";
}
