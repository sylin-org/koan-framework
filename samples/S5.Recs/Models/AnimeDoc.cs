using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

// Canonical metadata document stored in Mongo
[DataAdapter("mongo")]
[Sora.Data.Vector.Abstractions.VectorAdapter("weaviate")]
[Storage(Name = "Anime")]
public sealed class AnimeDoc : Entity<AnimeDoc>
{
    public string Title { get; set; } = string.Empty;
    public string[] Genres { get; set; } = Array.Empty<string>();
    public int? Episodes { get; set; }
    public string? Synopsis { get; set; }
    public double Popularity { get; set; }
    // Enriched metadata (optional)
    public string? CoverUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? CoverColorHex { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? TitleEnglish { get; set; }
    public string? TitleRomaji { get; set; }
    public string? TitleNative { get; set; }
    public string[] Synonyms { get; set; } = Array.Empty<string>();
}
