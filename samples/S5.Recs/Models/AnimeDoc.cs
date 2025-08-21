using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Vector.Abstractions;

namespace S5.Recs.Models;

// Canonical metadata document stored in Mongo
[DataAdapter("mongo")]
[Sora.Data.Vector.Abstractions.VectorAdapter("weaviate")]
[Storage(Name = "Anime")]
public sealed class AnimeDoc : IEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string[] Genres { get; set; } = Array.Empty<string>();
    public int? Episodes { get; set; }
    public string? Synopsis { get; set; }
    public double Popularity { get; set; }
}
